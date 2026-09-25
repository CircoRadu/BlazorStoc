using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: the `vehicul` table belongs to BlazorStoc (there is no legacy equivalent) and is created on first use.
public sealed class MariaVehicleRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IVehicleRepository
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static volatile bool schemaReady;
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id_vehicul, vehicul_numar, vehicul_descriere, vehicul_versiune FROM vehicul ORDER BY vehicul_numar, id_vehicul
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Vehicle>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        return result;
    }

    public async Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var vehicle = await WriteAsync(async (connection, transaction) =>
        {
            await EnsureUniqueAsync(connection, transaction, value.PlateNumber, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO vehicul (vehicul_numar, vehicul_descriere, vehicul_versiune) VALUES (@plate, @description, 0)
                """, ("@plate", value.PlateNumber), ("@description", value.Description));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Vehicle(checked((int)command.LastInsertedId), value.PlateNumber, value.Description);
        }, cancellationToken, value.PlateNumber).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Vehicle, vehicle.Id.ToString(),
            SqliteVehicleRepository.Target(vehicle), AuditDetails.Identification(
                ("Număr de înmatriculare", vehicle.PlateNumber), ("Descriere", vehicle.Description)), cancellationToken).ConfigureAwait(false);
        return vehicle;
    }

    public async Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var vehicle = await WriteAsync(async (connection, transaction) =>
        {
            VehicleRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value.PlateNumber, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE vehicul SET vehicul_numar=@plate, vehicul_descriere=@description, vehicul_versiune=@version
                WHERE id_vehicul=@id AND vehicul_versiune=@oldVersion
                """, ("@plate", value.PlateNumber), ("@description", value.Description), ("@version", version),
                ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
            return new Vehicle(original.Id, value.PlateNumber, value.Description, version);
        }, cancellationToken, value.PlateNumber, original.Id).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Vehicle, vehicle.Id.ToString(),
            SqliteVehicleRepository.Target(vehicle),
            [new("Număr de înmatriculare", original.PlateNumber, vehicle.PlateNumber), new("Descriere", original.Description, vehicle.Description)],
            value.Reason, cancellationToken).ConfigureAwait(false);
        return vehicle;
    }

    public async Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new VehicleOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Vehicle(original, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                VehicleRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM vehicul WHERE id_vehicul=@id AND vehicul_versiune=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new VehicleOperationException(VehicleRules.ConcurrentMessage);
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token,
        string? savedPlate = null, int? savedId = null)
    {
        if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
            throw new VehicleOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            if (savedPlate is not null && exception is MySqlException { Number: 1062 })
                throw await ConcurrentDuplicateAsync(savedPlate, savedId, token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken token)
    {
        if (schemaReady) return;
        await SchemaGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (schemaReady) return;
            if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
                throw new VehicleOperationException("Secțiunea Vehicule poate modifica schema numai în baza BlazorStoc.");
            await using var create = new MySqlCommand("""
                CREATE TABLE IF NOT EXISTS vehicul (
                    id_vehicul INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    vehicul_numar VARCHAR(12) NOT NULL,
                    vehicul_descriere VARCHAR(100) NOT NULL,
                    vehicul_versiune BIGINT NOT NULL DEFAULT 0,
                    UNIQUE KEY UX_vehicul_numar (vehicul_numar)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
                """, connection);
            await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            schemaReady = true;
        }
        finally { SchemaGate.Release(); }
    }

    private static async Task<Vehicle?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id_vehicul, vehicul_numar, vehicul_descriere, vehicul_versiune FROM vehicul WHERE id_vehicul=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)) : null;
    }

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction, string plate, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT vehicul_numar, vehicul_descriere FROM vehicul
            WHERE UPPER(vehicul_numar)=UPPER(@plate) AND (@id IS NULL OR id_vehicul<>@id)
            ORDER BY id_vehicul LIMIT 1 FOR UPDATE
            """, ("@plate", plate), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new VehicleOperationException(VehicleRules.DuplicatePlateMessage(reader.GetString(0), reader.GetString(1)));
    }

    // A concurrent save can pass the check above and still hit UX_vehicul_numar; report the stored vehicle after rollback.
    private async Task<VehicleOperationException> ConcurrentDuplicateAsync(string plate, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var command = Command(connection, null, """
                SELECT vehicul_numar, vehicul_descriere FROM vehicul
                WHERE UPPER(vehicul_numar)=UPPER(@plate) AND (@id IS NULL OR id_vehicul<>@id)
                ORDER BY id_vehicul LIMIT 1
                """, ("@plate", plate), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
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

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
