using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: `beneficiary_work_points` is created by migration 2 (MariaSchemaMigrations); like the other
// repositories this one never alters the schema.
public sealed class MariaWorkPointRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IWorkPointRepository
{
    private const string Columns = "id, beneficiary_id, name, address, phone, contact_person, version";
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<WorkPoint>> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE beneficiary_id=@id ORDER BY name, id", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkPoint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<WorkPoint> CreateAsync(int beneficiaryId, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var (workPoint, ownerName) = await WriteAsync(async (connection, transaction) =>
        {
            var owner = await GetOwnerAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, owner, value, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiary_work_points (beneficiary_id, name, address, normalized_address, phone, contact_person, version)
                VALUES (@beneficiaryId, @name, @address, @normalizedAddress, @phone, @contactPerson, 0)
                """, Fields(beneficiaryId, value));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Build(checked((int)command.LastInsertedId), beneficiaryId, value, 0), owner.Name);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiaryId.ToString(),
            WorkPointRules.Target(beneficiaryId, ownerName, workPoint.Name),
            [new("Punct de lucru adăugat", string.Empty, WorkPointRules.Identification(workPoint))], string.Empty,
            cancellationToken).ConfigureAwait(false);
        return workPoint;
    }

    public async Task<WorkPoint> UpdateAsync(WorkPoint original, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var (workPoint, ownerName) = await WriteAsync(async (connection, transaction) =>
        {
            WorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var owner = await GetOwnerAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, owner, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE beneficiary_work_points SET name=@name, address=@address, normalized_address=@normalizedAddress,
                    phone=@phone, contact_person=@contactPerson, version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(original.BeneficiaryId, value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
            return (Build(original.Id, original.BeneficiaryId, value, version), owner.Name);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, original.BeneficiaryId.ToString(),
            WorkPointRules.Target(original.BeneficiaryId, ownerName, workPoint.Name),
            WorkPointRules.Changes(original, workPoint), WorkPointRules.GeneratedEditReason, cancellationToken).ConfigureAwait(false);
        return workPoint;
    }

    public async Task DeleteAsync(WorkPoint original, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var ownerName = await WriteAsync(async (connection, transaction) =>
        {
            WorkPointRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var owner = await GetOwnerAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction,
                "DELETE FROM beneficiary_work_points WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
            return owner.Name;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, original.BeneficiaryId.ToString(),
            WorkPointRules.Target(original.BeneficiaryId, ownerName, original.Name),
            [new("Punct de lucru șters", WorkPointRules.Identification(original), string.Empty)], WorkPointRules.DeleteReason,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new WorkPointOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
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
            // A concurrent save passed the check above and hit uq_beneficiary_work_points_0.
            if (exception is MySqlException { Number: 1062 })
                throw new WorkPointOperationException("Există deja un punct de lucru cu această adresă.");
            throw;
        }
    }

    private static async Task<WorkPoint?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<(int Id, string Name, string Address)> GetOwnerAsync(MySqlConnection connection,
        MySqlTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT name, address FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new WorkPointOperationException("Beneficiarul nu mai există. Actualizează lista.");
        return (beneficiaryId, reader.GetString(0), reader.GetString(1));
    }

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction,
        (int Id, string Name, string Address) owner, WorkPointInput value, int? excludedId, CancellationToken token)
    {
        var key = AddressNormalization.Key(value.Address);
        if (AddressNormalization.Key(owner.Address) == key) throw WorkPointRules.DuplicateAddress(WorkPointRules.PrimaryName);
        await using var command = Command(connection, transaction, """
            SELECT name FROM beneficiary_work_points
            WHERE beneficiary_id=@beneficiaryId AND normalized_address=@normalizedAddress AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@beneficiaryId", owner.Id), ("@normalizedAddress", key), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw WorkPointRules.DuplicateAddress(existingName);
    }

    private static WorkPoint Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt64(6));

    private static WorkPoint Build(int id, int beneficiaryId, WorkPointInput value, long version) =>
        new(id, beneficiaryId, value.Name, value.Address, value.Phone, value.ContactPerson, version);

    private static (string, object)[] Fields(int beneficiaryId, WorkPointInput value) =>
    [
        ("@beneficiaryId", beneficiaryId), ("@name", value.Name), ("@address", value.Address),
        ("@normalizedAddress", AddressNormalization.Key(value.Address)), ("@phone", value.Phone),
        ("@contactPerson", value.ContactPerson)
    ];

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
