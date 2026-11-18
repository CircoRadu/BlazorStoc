using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: reads over `stock_movements` (no table of its own). Reading needs a right that sees the stock or the beneficiaries.
public sealed class MariaProjectEquipmentRepository(IConfiguration configuration, IAccessControl? accessControl = null) : IProjectEquipmentRepository
{
    private static readonly string[] ReadKeys = ["stoc.view", "stoc.intrare", "beneficiari.view", "vehicule.view"];

    public async Task<IReadOnlyList<ProjectMovementEntry>> GetHistoryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT m.id,m.movement_date,m.created_utc,m.kind,m.product_id,p.name,COALESCE(sc.name,''),m.quantity,m.operator,m.description,m.voided_utc
            FROM stock_movements m
            INNER JOIN products p ON p.id=m.product_id
            LEFT JOIN subcategories sc ON sc.id=p.subcategory_id
            WHERE (m.kind=0 AND m.destination=@beneficiary AND m.project_id=@project)
               OR (m.kind=1 AND m.voided_utc IS NULL AND m.free_entry_type=@returnType AND m.project_id=@project)
               OR (m.kind=1 AND m.voided_utc IS NULL AND m.return_of_movement_id IS NOT NULL
                   AND EXISTS(SELECT 1 FROM stock_movements e WHERE e.id=m.return_of_movement_id AND e.project_id=@project))
            ORDER BY m.movement_date DESC,m.id DESC
            """, ("@project", projectId), ("@beneficiary", (int)ExitDestination.Beneficiary), ("@returnType", (int)FreeEntryType.ReturnFromProject));
        var result = new List<ProjectMovementEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var stored = reader.GetString(1);
            var date = stored.Length == 0 ? DateOnly.FromDateTime(MariaTimeText.Parse(reader.GetString(2))) : StockMovementRules.ParseStorageDate(stored);
            result.Add(new ProjectMovementEntry(checked((int)reader.GetInt64(0)), date, Convert.ToInt32(reader.GetValue(3)) == (int)StockMovementKind.Entry, checked((int)reader.GetInt64(4)),
                reader.GetString(5), reader.GetString(6), Convert.ToInt32(reader.GetValue(7)), reader.GetString(8), reader.GetString(9), !reader.IsDBNull(10)));
        }
        return result;
    }

    public async Task<IReadOnlyList<ProjectProductNet>> GetProductProjectsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT b.id,b.name,p.id,p.name,x.net
            FROM (SELECT t.project_id,SUM(t.q) AS net FROM (
                    SELECT project_id,quantity AS q FROM stock_movements
                      WHERE kind=0 AND destination=@beneficiary AND voided_utc IS NULL AND product_id=@product AND project_id IS NOT NULL
                    UNION ALL
                    SELECT e.project_id,-r.quantity FROM stock_movements r INNER JOIN stock_movements e ON e.id=r.return_of_movement_id
                      WHERE r.voided_utc IS NULL AND r.product_id=@product AND e.project_id IS NOT NULL
                    UNION ALL
                    SELECT project_id,-quantity FROM stock_movements
                      WHERE kind=1 AND free_entry_type=@returnType AND voided_utc IS NULL AND product_id=@product AND project_id IS NOT NULL
                  ) t GROUP BY t.project_id HAVING SUM(t.q)>0) x
            INNER JOIN projects p ON p.id=x.project_id INNER JOIN beneficiaries b ON b.id=p.beneficiary_id
            ORDER BY b.name,p.name
            """, ("@product", productId), ("@beneficiary", (int)ExitDestination.Beneficiary), ("@returnType", (int)FreeEntryType.ReturnFromProject));
        var result = new List<ProjectProductNet>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ProjectProductNet(checked((int)reader.GetInt64(0)), reader.GetString(1), checked((int)reader.GetInt64(2)), reader.GetString(3), Convert.ToInt32(reader.GetValue(4))));
        return result;
    }

    private Task EnsureAsync(CancellationToken token) => accessControl?.EnsureAnyAsync(ReadKeys, token) ?? Task.CompletedTask;
}
