using MySqlConnector;

namespace BlazorStoc.Services;

/// <summary>One row of schema_migrations (migration 43): a migration that is part of the database, with the checksum of its file at that time.</summary>
public sealed record SchemaLedgerRow(int Version, string Description, string Checksum, string Origin, DateTime AppliedUtc, string AppliedByVersion);

/// <summary>
/// The ledger of the applied migrations (table schema_migrations). The migrator account has DDL rights only, so the table is created by migration 43 with that account and
/// the rows are read and written here with the application account. The ledger does not decide what a migration does (every statement stays idempotent); it tells which
/// migrations are still to be applied, which version the schema has, and whether a migration that was already applied has been edited since (drift).
/// </summary>
public static class MariaSchemaLedger
{
    /// <summary>The migration was already part of the database when the ledger was started (an existing installation, or a first start on an empty ledger).</summary>
    public const string BaselineOrigin = "baseline";
    /// <summary>The migration was applied by the migrator after the ledger existed.</summary>
    public const string AppliedOrigin = "applied";

    private const int NoSuchTable = 1146;

    /// <summary>The rows by version, or null while the table does not exist (the database is older than migration 43).</summary>
    public static async Task<IReadOnlyDictionary<int, SchemaLedgerRow>?> ReadAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = new MySqlCommand(
                "SELECT version, description, checksum, origin, applied_utc, applied_by_version FROM schema_migrations ORDER BY version", connection);
            var rows = new Dictionary<int, SchemaLedgerRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                rows[reader.GetInt32(0)] = new SchemaLedgerRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc), reader.GetString(5));
            return rows;
        }
        catch (MySqlException exception) when (exception.Number == NoSuchTable) { return null; }
    }

    /// <summary>The highest version in the ledger (0 when the ledger is empty or missing).</summary>
    public static async Task<int> CurrentVersionAsync(IConfiguration configuration, CancellationToken cancellationToken = default) =>
        (await ReadAsync(configuration, cancellationToken).ConfigureAwait(false))?.Keys.DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>Writes the rows of the migrations that are not in the ledger yet (a version already there is left as it is).</summary>
    public static async Task RecordAsync(IConfiguration configuration, IEnumerable<MariaMigration> migrations, string origin, DateTime appliedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        foreach (var migration in migrations)
        {
            await using var command = new MySqlCommand("""
                INSERT IGNORE INTO schema_migrations (version, description, checksum, origin, applied_utc, applied_by_version)
                VALUES (@version, @description, @checksum, @origin, @at, @appVersion)
                """, connection);
            command.Parameters.AddWithValue("@version", migration.Version);
            command.Parameters.AddWithValue("@description", migration.Description.Length > 300 ? migration.Description[..300] : migration.Description);
            command.Parameters.AddWithValue("@checksum", migration.Checksum);
            command.Parameters.AddWithValue("@origin", origin);
            command.Parameters.AddWithValue("@at", appliedUtc);
            command.Parameters.AddWithValue("@appVersion", AppVersion.Current);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The migrations of the ledger whose file now has another checksum: "version: description". An applied migration is never edited; a change is a new migration.</summary>
    public static IReadOnlyList<string> Drift(IReadOnlyDictionary<int, SchemaLedgerRow> ledger, IEnumerable<MariaMigration> migrations) =>
        migrations.Where(migration => ledger.TryGetValue(migration.Version, out var row) && !string.Equals(row.Checksum, migration.Checksum, StringComparison.Ordinal))
            .Select(migration => $"{migration.Version}: {migration.Description}").ToArray();
}
