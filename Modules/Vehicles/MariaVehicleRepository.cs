using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Real MariaDB schema (Livrare-DDL-MariaDB\schema-mariadb.sql, read in this cycle): the `vehicles` table already
// exists (id, plate_number, normalized_plate UNIQUE VARCHAR(191), description, version) - no DDL is emitted here,
// the runtime account has only SELECT/INSERT/UPDATE/DELETE. `stock_movements` already exists too, with
// `vehicle_id`/`source_vehicle_id` FK columns (ON DELETE RESTRICT) referencing `vehicles(id)`.
public sealed class MariaVehicleRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IVehicleRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id, plate_number, description, version, itp_expiry, insurance_expiry, rovinieta_expiry FROM vehicles ORDER BY plate_number, id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Vehicle>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(ReadVehicle(reader));
        return result;
    }

    public async Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAnyPermissionAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT id, plate_number, description, version, itp_expiry, insurance_expiry, rovinieta_expiry FROM vehicles WHERE id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadVehicle(reader) : null;
    }

    public async Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("vehicule.add", cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var vehicle = await WriteAsync(async (connection, transaction) =>
        {
            await EnsureUniqueAsync(connection, transaction, value.PlateNumber, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO vehicles (plate_number, normalized_plate, description, version, itp_expiry, insurance_expiry, rovinieta_expiry)
                VALUES (@plate, @normalizedPlate, @description, 0, @itp, @insurance, @rovinieta)
                """, ("@plate", value.PlateNumber), ("@normalizedPlate", TextNormalization.UniquenessKey(value.PlateNumber)),
                ("@description", value.Description), ("@itp", SqlDate(value.ItpExpiry)), ("@insurance", SqlDate(value.InsuranceExpiry)),
                ("@rovinieta", SqlDate(value.RovinietaExpiry)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Vehicle(checked((int)command.LastInsertedId), value.PlateNumber, value.Description, 0,
                value.ItpExpiry, value.InsuranceExpiry, value.RovinietaExpiry);
        }, cancellationToken, value.PlateNumber).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Vehicle, vehicle.Id.ToString(),
            VehicleRules.Target(vehicle), VehicleRules.AuditIdentification(vehicle), cancellationToken).ConfigureAwait(false);
        return vehicle;
    }

    public async Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default, string? auditAction = null)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("vehicule.edit", cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var vehicle = await WriteAsync(async (connection, transaction) =>
        {
            VehicleRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value.PlateNumber, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE vehicles SET plate_number=@plate, normalized_plate=@normalizedPlate, description=@description, version=@version,
                    itp_expiry=@itp, insurance_expiry=@insurance, rovinieta_expiry=@rovinieta
                WHERE id=@id AND version=@oldVersion
                """, ("@plate", value.PlateNumber), ("@normalizedPlate", TextNormalization.UniquenessKey(value.PlateNumber)),
                ("@description", value.Description), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version),
                ("@itp", SqlDate(value.ItpExpiry)), ("@insurance", SqlDate(value.InsuranceExpiry)), ("@rovinieta", SqlDate(value.RovinietaExpiry)));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
            return new Vehicle(original.Id, value.PlateNumber, value.Description, version,
                value.ItpExpiry, value.InsuranceExpiry, value.RovinietaExpiry);
        }, cancellationToken, value.PlateNumber, original.Id).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Vehicle, vehicle.Id.ToString(),
            VehicleRules.Target(vehicle),
            VehicleRules.Changes(original, vehicle),
            value.Reason, cancellationToken, auditAction).ConfigureAwait(false);
        return vehicle;
    }

    public async Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null) await accessControl.EnsureAsync("vehicule.delete", cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new VehicleOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Vehicle(original, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                VehicleRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await using (var relations = Command(connection, transaction, """
                    SELECT EXISTS(SELECT 1 FROM stock_movements WHERE vehicle_id=@id OR source_vehicle_id=@id)
                    """, ("@id", original.Id)))
                    VehicleRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM vehicles WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Vehicle ReadVehicle(MySqlDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)), DateOnly.FromDateTime(reader.GetDateTime(5)), DateOnly.FromDateTime(reader.GetDateTime(6)));

    private static object SqlDate(DateOnly? date) => date is { } value ? value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token,
        string? savedPlate = null, int? savedId = null) =>
        MariaDb.WriteAsync(configuration, action, message => new VehicleOperationException(message), async (exception, cancellation) =>
            savedPlate is not null && exception.Number == 1062 ? await ConcurrentDuplicateAsync(savedPlate, savedId, cancellation).ConfigureAwait(false) : null,
            token);
    private static async Task<Vehicle?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id, plate_number, description, version, itp_expiry, insurance_expiry, rovinieta_expiry FROM vehicles WHERE id=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? ReadVehicle(reader) : null;
    }

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction, string plate, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT plate_number, description FROM vehicles
            WHERE normalized_plate=@normalized AND (@id IS NULL OR id<>@id)
            ORDER BY id LIMIT 1 FOR UPDATE
            """, ("@normalized", TextNormalization.UniquenessKey(plate)), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new VehicleOperationException(VehicleRules.DuplicatePlateMessage(reader.GetString(0), reader.GetString(1)));
    }

    // A concurrent save can pass the check above and still hit uq_vehicles_0; report the stored vehicle after rollback.
    private async Task<VehicleOperationException> ConcurrentDuplicateAsync(string plate, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
            await using var command = Command(connection, null, """
                SELECT plate_number, description FROM vehicles
                WHERE normalized_plate=@normalized AND (@id IS NULL OR id<>@id)
                ORDER BY id LIMIT 1
                """, ("@normalized", TextNormalization.UniquenessKey(plate)), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new(VehicleRules.DuplicatePlateMessage(reader.GetString(0), reader.GetString(1)))
                : new(VehicleRules.DuplicatePlateMessage(plate, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(VehicleRules.DuplicatePlateMessage(null, null));
        }
    }

}
