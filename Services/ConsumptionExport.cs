using System.Globalization;
using System.Text;
using MySqlConnector;

namespace BlazorStoc.Services;

// Consumption export: what was handed over to beneficiaries / projects in a period (the exits to a beneficiary minus the returns tied to them), per
// product, beneficiary and project. Voided exits and voided returns are left out. Read only; needs a product operator like the exits.

public sealed record ConsumptionQuery(int? BeneficiaryId, int? ProjectId, DateOnly? From, DateOnly? To);
public sealed record ConsumptionRow(string ProductName, string BeneficiaryName, string? ProjectName, int Exited, int Returned)
{
    public int Net => Exited - Returned;
}

public interface IConsumptionReader
{
    Task<IReadOnlyList<ConsumptionRow>> GetAsync(ConsumptionQuery query, CancellationToken cancellationToken = default);
}

public static class ConsumptionExportRules
{
    public static string ToCsv(ConsumptionQuery query, IEnumerable<ConsumptionRow> rows)
    {
        var text = new StringBuilder();
        static string Cell(string value) => value.Contains(';') || value.Contains('"') || value.Contains('\n') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        void Row(params string[] cells) => text.Append(string.Join(';', cells.Select(Cell))).Append("\r\n");
        Row("Perioada", query.From is { } from ? StockMovementRules.DisplayDate(from) : "inceput", query.To is { } to ? StockMovementRules.DisplayDate(to) : "azi");
        Row();
        Row("Produs", "Beneficiar", "Proiect", "Iesit", "Returnat", "Consum net");
        foreach (var row in rows)
            Row(row.ProductName, row.BeneficiaryName, row.ProjectName ?? "", Number(row.Exited), Number(row.Returned), Number(row.Net));
        return text.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}

public sealed class MariaConsumptionReader(IConfiguration configuration, IAccessControl? accessControl = null) : IConsumptionReader
{
    public async Task<IReadOnlyList<ConsumptionRow>> GetAsync(ConsumptionQuery query, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT p.name,b.name,pr.name,SUM(e.quantity),
                   SUM((SELECT COALESCE(SUM(r.quantity),0) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL))
            FROM stock_movements e INNER JOIN products p ON p.id=e.product_id
            LEFT JOIN beneficiaries b ON b.id=e.beneficiary_id LEFT JOIN projects pr ON pr.id=e.project_id
            WHERE e.kind=0 AND e.voided_utc IS NULL AND e.destination=@destination
              AND (@beneficiary IS NULL OR e.beneficiary_id=@beneficiary) AND (@project IS NULL OR e.project_id=@project)
              AND (@from IS NULL OR e.movement_date>=@from) AND (@to IS NULL OR e.movement_date<=@to)
            GROUP BY e.product_id,p.name,e.beneficiary_id,b.name,e.project_id,pr.name
            ORDER BY b.name,pr.name,p.name,e.product_id
            """, connection);
        command.Parameters.AddWithValue("@destination", (int)ExitDestination.Beneficiary);
        command.Parameters.AddWithValue("@beneficiary", (object?)query.BeneficiaryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@project", (object?)query.ProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("@from", query.From is { } from ? StockMovementRules.StorageDate(from) : DBNull.Value);
        command.Parameters.AddWithValue("@to", query.To is { } to ? StockMovementRules.StorageDate(to) : DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ConsumptionRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.IsDBNull(1) ? "—" : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3)), Convert.ToInt32(reader.GetValue(4))));
        return result;
    }
}
