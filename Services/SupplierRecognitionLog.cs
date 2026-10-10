using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// What the recognition of the supplier proposed for an invoice taken into stock and what the user finally chose: kept (table
// `supplier_recognitions`, migration 16) to see where the reading goes wrong. Only the name and the tax id as read are kept, never the text of
// the invoice; the entries go with their invoice.
public sealed record SupplierRecognitionEntry(long InvoiceId, SupplierMatchMethod Method, string Confidence, string ReadName, string ReadCui, int? RecognizedSupplierId, int? ChosenSupplierId, bool TemplateChanged = false)
{
    public bool Corrected => RecognizedSupplierId != ChosenSupplierId;
}

public sealed record SupplierRecognitionCount(string Method, string Confidence, int Total, int Corrected, int TemplateChanged = 0);
public sealed record SupplierRecognitionCorrection(DateTime Utc, string InvoiceNumber, string ReadName, string ReadCui, string RecognizedName, string ChosenName, int? ChosenSupplierId = null);
public sealed record SupplierRecognitionSummary(int Total, int Corrected, IReadOnlyList<SupplierRecognitionCount> ByMethod, IReadOnlyList<SupplierRecognitionCorrection> Recent);

public interface ISupplierRecognitionLog
{
    Task RecordAsync(SupplierRecognitionEntry entry, CancellationToken cancellationToken = default);
    Task<SupplierRecognitionSummary> SummaryAsync(CancellationToken cancellationToken = default);
    // The whole log as CSV (one row per invoice), for analysis outside the application.
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public static class SupplierRecognitionText
{
    public static string MethodName(string method) => method switch
    {
        "cui" => "CUI", "name" => "nume", "alias" => "denumire alternativă", "similar" => "nume apropiat", _ => "nerecunoscut"
    };

    public static string Key(SupplierMatchMethod method) => method.ToString().ToLowerInvariant();
    public static string ConfidenceName(string confidence) => confidence switch { "high" => "mare", "medium" => "medie", _ => "scăzută" };
}

public sealed class MariaSupplierRecognitionLog(IConfiguration configuration, IAccessControl? accessControl = null) : ISupplierRecognitionLog
{

    public async Task RecordAsync(SupplierRecognitionEntry entry, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyAsync(["furnizori.edit", "preluare-factura.add"], cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) return;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT IGNORE INTO supplier_recognitions (invoice_id, method, confidence, read_name, read_cui, recognized_supplier_id, chosen_supplier_id, corrected, template_changed, created_utc)
            VALUES (@invoice, @method, @confidence, @name, @cui, @recognized, @chosen, @corrected, @templateChanged, @now)
            """, connection);
        command.Parameters.AddWithValue("@invoice", entry.InvoiceId);
        command.Parameters.AddWithValue("@method", SupplierRecognitionText.Key(entry.Method));
        command.Parameters.AddWithValue("@confidence", entry.Confidence);
        command.Parameters.AddWithValue("@name", Cut(entry.ReadName, 200));
        command.Parameters.AddWithValue("@cui", Cut(entry.ReadCui, 30));
        command.Parameters.AddWithValue("@recognized", entry.RecognizedSupplierId is { } recognized ? recognized : DBNull.Value);
        command.Parameters.AddWithValue("@chosen", entry.ChosenSupplierId is { } chosen ? chosen : DBNull.Value);
        command.Parameters.AddWithValue("@corrected", entry.Corrected ? 1 : 0);
        command.Parameters.AddWithValue("@templateChanged", entry.TemplateChanged ? 1 : 0);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(MariaTimeText.Now()));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string? value, int length) => (value ?? "").Trim() is { } text && text.Length > length ? text[..length] : (value ?? "").Trim();

    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT r.created_utc, i.number, r.method, r.confidence, r.read_name, r.read_cui, COALESCE(rs.name, ''), COALESCE(cs.name, ''), r.corrected, r.template_changed
            FROM supplier_recognitions r
            INNER JOIN supplier_invoices i ON i.id = r.invoice_id
            LEFT JOIN suppliers rs ON rs.id = r.recognized_supplier_id
            LEFT JOIN suppliers cs ON cs.id = r.chosen_supplier_id
            ORDER BY r.id
            """, connection);
        var builder = new System.Text.StringBuilder("Data;Factura;Metoda;Incredere;Nume citit;CUI citit;Furnizor propus;Furnizor ales;Corectat;Sablon schimbat\r\n");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            builder.AppendJoin(';', new[]
            {
                Csv(StockMovementRules.DisplayDate(DateOnly.FromDateTime(MariaTimeText.Parse(reader.GetString(0)).ToLocalTime()))), Csv(reader.GetString(1)), Csv(reader.GetString(2)), Csv(reader.GetString(3)),
                Csv(reader.GetString(4)), Csv(reader.GetString(5)), Csv(reader.GetString(6)), Csv(reader.GetString(7)), reader.GetInt32(8) == 1 ? "da" : "nu", reader.GetInt32(9) == 1 ? "da" : "nu"
            }).Append("\r\n");
        return builder.ToString();
    }

    // A value in quotes; a leading =, +, - or @ is neutralised so a spreadsheet does not run a name read from an invoice as a formula.
    public static string Csv(string value)
    {
        var text = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    public async Task<SupplierRecognitionSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var counts = new List<SupplierRecognitionCount>();
        await using (var command = new MySqlCommand("SELECT method, confidence, COUNT(*), COALESCE(SUM(corrected),0), COALESCE(SUM(template_changed),0) FROM supplier_recognitions GROUP BY method, confidence ORDER BY method, confidence", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                counts.Add(new(reader.GetString(0), reader.GetString(1), Convert.ToInt32(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), Convert.ToInt32(reader.GetValue(4))));
        var recent = new List<SupplierRecognitionCorrection>();
        await using (var command = new MySqlCommand("""
            SELECT r.created_utc, i.number, r.read_name, r.read_cui, COALESCE(rs.name, ''), COALESCE(cs.name, ''), r.chosen_supplier_id
            FROM supplier_recognitions r
            INNER JOIN supplier_invoices i ON i.id = r.invoice_id
            LEFT JOIN suppliers rs ON rs.id = r.recognized_supplier_id
            LEFT JOIN suppliers cs ON cs.id = r.chosen_supplier_id
            WHERE r.corrected = 1 ORDER BY r.id DESC LIMIT 50
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                recent.Add(new(MariaTimeText.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : checked((int)reader.GetInt64(6))));
        return new(counts.Sum(item => item.Total), counts.Sum(item => item.Corrected), counts, recent);
    }
}
