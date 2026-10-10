using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `vehicle_target_levels` (migration 28), one row per vehicle and product. Changes need a product operator and are journaled under the vehicle.
public sealed class MariaVehicleTargetRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IVehicleTargetRepository
{
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;

    private static async Task<string?> TextAsync(MySqlConnection connection, string sql, int id, CancellationToken token) =>
        await Command(connection, sql, ("@id", id)).ExecuteScalarAsync(token).ConfigureAwait(false) as string;

    private static async Task<int?> CurrentAsync(MySqlConnection connection, int vehicleId, int productId, CancellationToken token)
    {
        var existing = await Command(connection, "SELECT target_quantity FROM vehicle_target_levels WHERE vehicle_id=@v AND product_id=@p", ("@v", vehicleId), ("@p", productId))
            .ExecuteScalarAsync(token).ConfigureAwait(false);
        return existing is null or DBNull ? null : Convert.ToInt32(existing);
    }

    public async Task<IReadOnlyList<VehicleTarget>> GetForVehicleAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, """
            SELECT t.vehicle_id,t.product_id,p.name,t.target_quantity FROM vehicle_target_levels t INNER JOIN products p ON p.id=t.product_id
            WHERE t.vehicle_id=@vehicle ORDER BY p.name,t.product_id
            """, ("@vehicle", vehicleId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<VehicleTarget>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), Convert.ToInt32(reader.GetValue(3))));
        return result;
    }

    public async Task SetAsync(int vehicleId, int productId, int target, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (target is < 1 or > StockMovementRules.MaxQuantity) throw new VehicleTargetException(VehicleTargetRules.QuantityMessage);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new VehicleTargetException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var actor = (await RepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false)).Username;
        string plate, product;
        int? old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            plate = await TextAsync(connection, "SELECT plate_number FROM vehicles WHERE id=@id", vehicleId, cancellationToken).ConfigureAwait(false)
                ?? throw new VehicleTargetException("Vehiculul nu mai există.");
            product = await TextAsync(connection, "SELECT name FROM products WHERE id=@id", productId, cancellationToken).ConfigureAwait(false)
                ?? throw new VehicleTargetException("Produsul nu mai există.");
            old = await CurrentAsync(connection, vehicleId, productId, cancellationToken).ConfigureAwait(false);
            await using var upsert = Command(connection, """
                INSERT INTO vehicle_target_levels (vehicle_id,product_id,target_quantity,updated_by,updated_utc) VALUES (@v,@p,@t,@by,@now)
                ON DUPLICATE KEY UPDATE target_quantity=@t,updated_by=@by,updated_utc=@now
                """, ("@v", vehicleId), ("@p", productId), ("@t", target), ("@by", actor), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Vehicle, AuditActions.SetVehicleTarget, vehicleId.ToString(), plate,
            AuditDetails.Identification(("Vehicul", plate), ("Produs", product)) + "; "
            + AuditDetails.Changes([new AuditChange("Nivel țintă", old is { } before ? $"{before} buc." : "—", $"{target} buc.")]),
            "", cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(int vehicleId, int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new VehicleTargetException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        string? plate, product;
        int old;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            plate = await TextAsync(connection, "SELECT plate_number FROM vehicles WHERE id=@id", vehicleId, cancellationToken).ConfigureAwait(false);
            product = await TextAsync(connection, "SELECT name FROM products WHERE id=@id", productId, cancellationToken).ConfigureAwait(false);
            if (await CurrentAsync(connection, vehicleId, productId, cancellationToken).ConfigureAwait(false) is not { } current) return;
            old = current;
            await Command(connection, "DELETE FROM vehicle_target_levels WHERE vehicle_id=@v AND product_id=@p", ("@v", vehicleId), ("@p", productId))
                .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var plateText = plate ?? $"#{vehicleId}";
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Vehicle, AuditActions.RemoveVehicleTarget, vehicleId.ToString(), plateText,
            AuditDetails.Identification(("Vehicul", plateText), ("Produs", product ?? $"#{productId}")) + "; "
            + AuditDetails.Changes([new AuditChange("Nivel țintă", $"{old} buc.", "—")]),
            "", cancellationToken).ConfigureAwait(false);
    }
}
