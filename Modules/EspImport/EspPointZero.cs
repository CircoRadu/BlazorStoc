using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Modules.EspImport;

// The point zero of the stock keeping (docs/PROPUNERE_IMPORT_ESPSTOC.md, section 8): the movements, the taken invoices, the offers and the reservations are deleted, the quantities are set to zero
// and the old journal is emptied, so that the quantities of the import are the beginning. The state of the operation is kept in app_metadata, so an interrupted run can be taken up again.

public sealed class EspImportOperationException(string message) : Exception(message), IExpectedAnswer;

public sealed record EspTableCount(string Table, string Label, long Rows);
public sealed record EspBackupFile(string Name, DateTime CreatedUtc, long SizeBytes);

public sealed record EspPointZeroPreview(IReadOnlyList<EspTableCount> Counts, long PiecesInStock, int ProductsWithStock, long JournalEvents, DateTime? JournalFirstUtc, DateTime? JournalLastUtc,
    IReadOnlyList<EspBackupFile> LocalPackages, IReadOnlyList<EspBackupFile> NasPackages, string? NasNote, EspPointZeroState? State);

public static class EspPointZeroStatus
{
    public const string Started = "started";
    public const string Deleted = "deleted";
    public const string ImportErrors = "import-errors";
    public const string VerifyFailed = "verify-failed";
    public const string Verified = "verified";
    public const string Done = "done";
    public const string DoneWithWarnings = "done-warnings";

    public static bool Finished(string? status) => status is Done or DoneWithWarnings;
}

public sealed class EspPointZeroState
{
    public string Status { get; set; } = EspPointZeroStatus.Started;
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string Operator { get; set; } = "";
    public string Source { get; set; } = "";
    public string? SafetyPackage { get; set; }
    public string? StartPackage { get; set; }
    public int Products { get; set; }
    public int Pieces { get; set; }
    public List<string> LocalToDelete { get; set; } = [];
    public List<string> NasToDelete { get; set; } = [];
    public Dictionary<string, long> Deleted { get; set; } = [];
    public List<string> Log { get; set; } = [];
    public DateTime UpdatedUtc { get; set; }
    /// <summary>The operation runs now in this application (not stored).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Running { get; set; }
}

internal sealed class EspPointZeroStore(IConfiguration configuration)
{
    public const string StateKey = "espstoc.point_zero";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The tables emptied, in the order of their foreign keys (children first).
    internal static readonly (string Table, string Label, string? Where)[] Deletions =
    [
        ("stock_movement_history", "Corecții ale mișcărilor de stoc", null),
        ("stock_movements", "Mișcări de stoc", null),
        ("supplier_recognitions", "Recunoașteri de furnizor din facturi", null),
        ("supplier_invoice_lines", "Linii ale facturilor preluate", null),
        ("supplier_invoices", "Facturi preluate", null),
        ("offer_lines", "Linii de oferte", null),
        ("offers", "Oferte", null),
        ("project_reservations", "Rezervări pe proiecte", null),
        ("archive_stock_movements", "Mișcări arhivate", null),
    ];

    public async Task<EspPointZeroState?> ReadStateAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, "SELECT `value` FROM app_metadata WHERE `key`=@key", ("@key", StateKey));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string text ? JsonSerializer.Deserialize<EspPointZeroState>(text, Json) : null;
    }

    public async Task WriteStateAsync(EspPointZeroState state, CancellationToken token)
    {
        state.UpdatedUtc = DateTime.UtcNow;
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, "INSERT INTO app_metadata(`key`,`value`) VALUES(@key,@value) ON DUPLICATE KEY UPDATE `value`=@value",
            ("@key", StateKey), ("@value", JsonSerializer.Serialize(state, Json)));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<EspTableCount> Counts, long Pieces, int ProductsWithStock, long JournalEvents, DateTime? First, DateTime? Last)> CountAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        var counts = new List<EspTableCount>();
        foreach (var (table, label, _) in Deletions.Where(item => item.Table is not "archive_stock_movements"))
            counts.Add(new(table, label, await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}`", token).ConfigureAwait(false)));
        counts.Add(new("archive_stock_movements", "Mișcări arhivate", await ScalarAsync(connection, "SELECT COUNT(*) FROM archive_stock_movements", token).ConfigureAwait(false)));
        var pieces = await ScalarAsync(connection, "SELECT COALESCE(SUM(quantity),0) FROM products", token).ConfigureAwait(false);
        var withStock = await ScalarAsync(connection, "SELECT COUNT(*) FROM products WHERE quantity<>0", token).ConfigureAwait(false);
        var events = await ScalarAsync(connection, "SELECT COUNT(*) FROM audit_events", token).ConfigureAwait(false);
        DateTime? first = null, last = null;
        await using (var range = Command(connection, "SELECT MIN(timestamp_utc),MAX(timestamp_utc) FROM audit_events"))
        await using (var reader = await range.ExecuteReaderAsync(token).ConfigureAwait(false))
            if (await reader.ReadAsync(token).ConfigureAwait(false) && !reader.IsDBNull(0))
            {
                first = ParseUtc(reader.GetString(0));
                last = ParseUtc(reader.GetString(1));
            }
        return (counts, pieces, (int)withStock, events, first, last);
    }

    private static DateTime? ParseUtc(string text) => DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private static async Task<long> ScalarAsync(MySqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = Command(connection, sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Deletes the movements, the taken invoices, the offers, the reservations and the archived movements, sets every quantity to zero and closes the stock notifications. One transaction: all or nothing.</summary>
    public async Task<Dictionary<string, long>> DeleteAsync(CancellationToken token)
    {
        var deleted = new Dictionary<string, long>();
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        try
        {
            foreach (var (table, _, where) in Deletions)
            {
                if (table == "archive_stock_movements")
                {
                    // The archived movements are removed with the archive operations of that kind (and the links they hold).
                    deleted["archive_relations"] = await ExecuteAsync(connection, transaction, "DELETE FROM archive_relations WHERE archive_id IN (SELECT id FROM archive_operations WHERE entity_type='MiscareStoc')", token).ConfigureAwait(false);
                }
                deleted[table] = await ExecuteAsync(connection, transaction, $"DELETE FROM `{table}`{(where is null ? "" : " WHERE " + where)}", token).ConfigureAwait(false);
                if (table == "archive_stock_movements")
                    deleted["archive_operations"] = await ExecuteAsync(connection, transaction, "DELETE FROM archive_operations WHERE entity_type='MiscareStoc'", token).ConfigureAwait(false);
            }
            deleted["archive_products"] = await ExecuteAsync(connection, transaction, "UPDATE archive_products SET quantity=0 WHERE quantity<>0", token).ConfigureAwait(false);
            deleted["products"] = await ExecuteAsync(connection, transaction, "UPDATE products SET quantity=0, version=version+1", token).ConfigureAwait(false);
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            deleted["notifications"] = await ExecuteAsync(connection, transaction, """
                UPDATE expiry_notifications SET resolved_by='sistem', resolved_utc=@now, resolved_reason='Punct zero gestiune', resolved_auto=1
                WHERE resolved_utc IS NULL AND (source_key LIKE 'stoc.%' OR source_key LIKE '%factur%')
                """, token, ("@now", now)).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        return deleted;
    }

    private static async Task<long> ExecuteAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql, CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<(long Positive, long Pieces, long Movements)> VerifyAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        return (await ScalarAsync(connection, "SELECT COUNT(*) FROM products WHERE quantity>0", token).ConfigureAwait(false),
            await ScalarAsync(connection, "SELECT COALESCE(SUM(quantity),0) FROM products", token).ConfigureAwait(false),
            await ScalarAsync(connection, "SELECT COUNT(*) FROM stock_movements", token).ConfigureAwait(false));
    }

    /// <summary>Empties the journal of actions (the only code that ever deletes from it).</summary>
    public async Task<long> PurgeJournalAsync(CancellationToken token)
    {
        await using var connection = await OpenAsync(configuration, token).ConfigureAwait(false);
        return await ExecuteAsync(connection, null, "DELETE FROM audit_events", token).ConfigureAwait(false);
    }
}
