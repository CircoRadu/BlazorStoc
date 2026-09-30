using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteVehicleRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null)
    : IVehicleRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT id,plate_number,description,version,itp_expiry,insurance_expiry,rovinieta_expiry FROM vehicles ORDER BY plate_number,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var vehicles = new List<Vehicle>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) vehicles.Add(Read(reader));
        return vehicles;
    }

    public async Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(connection, null, id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureUniqueAsync(connection, transaction, value, null, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO vehicles(plate_number,normalized_plate,description,version,itp_expiry,insurance_expiry,rovinieta_expiry)
                VALUES(@plate,@normalizedPlate,@description,0,@itp,@insurance,@rovinieta); SELECT last_insert_rowid();
                """, ("@plate", value.PlateNumber), ("@normalizedPlate", TextNormalization.UniquenessKey(value.PlateNumber)),
                ("@description", value.Description), ("@itp", VehicleRules.StorageExpiry(value.ItpExpiry)),
                ("@insurance", VehicleRules.StorageExpiry(value.InsuranceExpiry)), ("@rovinieta", VehicleRules.StorageExpiry(value.RovinietaExpiry)));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var vehicle = new Vehicle(id, value.PlateNumber, value.Description, 0, value.ItpExpiry, value.InsuranceExpiry, value.RovinietaExpiry);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Vehicle, AuditActions.Create, Target(vehicle),
                VehicleRules.AuditIdentification(vehicle),
                string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return vehicle;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            VehicleRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE vehicles SET plate_number=@plate,normalized_plate=@normalizedPlate,description=@description,version=@version,
                    itp_expiry=@itp,insurance_expiry=@insurance,rovinieta_expiry=@rovinieta
                WHERE id=@id AND version=@oldVersion
                """, ("@plate", value.PlateNumber), ("@normalizedPlate", TextNormalization.UniquenessKey(value.PlateNumber)),
                ("@description", value.Description), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version),
                ("@itp", VehicleRules.StorageExpiry(value.ItpExpiry)), ("@insurance", VehicleRules.StorageExpiry(value.InsuranceExpiry)),
                ("@rovinieta", VehicleRules.StorageExpiry(value.RovinietaExpiry)));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
            var vehicle = new Vehicle(original.Id, value.PlateNumber, value.Description, version, value.ItpExpiry, value.InsuranceExpiry, value.RovinietaExpiry);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Vehicle, AuditActions.Edit, Target(vehicle),
                AuditDetails.Changes(VehicleRules.Changes(original, vehicle)), value.Reason, vehicle.Id.ToString()),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return vehicle;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new VehicleOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Vehicle(original, motif), async (operation, token) =>
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            try
            {
                VehicleRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await using (var relations = SqliteLocalStore.Command(connection, transaction, """
                    SELECT EXISTS(SELECT 1 FROM stock_movements WHERE vehicle_id=@id OR source_vehicle_id=@id)
                    """, ("@id", original.Id)))
                    VehicleRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var delete = SqliteLocalStore.Command(connection, transaction,
                    "DELETE FROM vehicles WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
                await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                    AuditEntities.Vehicle, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
                    operation.Request.Motif, original.Id.ToString(), operation.Id), token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal static string Target(Vehicle vehicle) => $"#{vehicle.Id} · {vehicle.PlateNumber}";

    private static async Task<Vehicle?> GetAsync(SqliteConnection connection, SqliteTransaction? transaction,
        int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT id,plate_number,description,version,itp_expiry,insurance_expiry,rovinieta_expiry FROM vehicles WHERE id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Vehicle Read(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
            VehicleRules.ParseStoredExpiry(reader.GetString(4)), VehicleRules.ParseStoredExpiry(reader.GetString(5)),
            VehicleRules.ParseStoredExpiry(reader.GetString(6)));

    private static async Task EnsureUniqueAsync(SqliteConnection connection, SqliteTransaction transaction,
        VehicleInput value, int? excludedId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT plate_number,description FROM vehicles
            WHERE normalized_plate=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(value.PlateNumber)), ("@id", excludedId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new VehicleOperationException(VehicleRules.DuplicatePlateMessage(reader.GetString(0), reader.GetString(1)));
    }

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
