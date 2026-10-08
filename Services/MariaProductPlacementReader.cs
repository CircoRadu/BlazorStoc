using MySqlConnector;

namespace BlazorStoc.Services;

// Needs a product operator like the exits. Voided exits and voided returns are left out.
public sealed class MariaProductPlacementReader(IConfiguration configuration, IAccessControl? accessControl = null) : IProductPlacementReader
{
    public async Task<IReadOnlyList<ProductDelivery>> GetDeliveriesAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.beneficiary_id,b.name,e.project_id,p.name,SUM(e.quantity),
                   SUM((SELECT COALESCE(SUM(r.quantity),0) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL))
            FROM stock_movements e LEFT JOIN beneficiaries b ON b.id=e.beneficiary_id LEFT JOIN projects p ON p.id=e.project_id
            WHERE e.product_id=@product AND e.kind=0 AND e.voided_utc IS NULL AND e.destination=@beneficiaryDestination
            GROUP BY e.beneficiary_id,b.name,e.project_id,p.name
            """;
        command.Parameters.AddWithValue("@product", productId);
        command.Parameters.AddWithValue("@beneficiaryDestination", (int)ExitDestination.Beneficiary);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ProductDelivery>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.IsDBNull(0) ? null : checked((int)reader.GetInt64(0)), reader.IsDBNull(1) ? "Beneficiar necunoscut" : reader.GetString(1),
                reader.IsDBNull(2) ? null : checked((int)reader.GetInt64(2)), reader.IsDBNull(3) ? null : reader.GetString(3),
                Convert.ToInt32(reader.GetValue(4)), Convert.ToInt32(reader.GetValue(5))));
        return ProductPlacementRules.StillDelivered(result);
    }
}
