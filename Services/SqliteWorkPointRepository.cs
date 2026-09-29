using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteWorkPointRepository(SqliteLocalStore store, IAccessControl? accessControl = null) : IWorkPointRepository
{
    private const string Columns = "id,beneficiary_id,name,address,phone,contact_person,version";

    public async Task<IReadOnlyList<WorkPoint>> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE beneficiary_id=@id ORDER BY name,id", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<WorkPoint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<WorkPoint> CreateAsync(int beneficiaryId, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            var owner = await GetOwnerAsync(connection, transaction, beneficiaryId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, owner, value, null, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO beneficiary_work_points(beneficiary_id,name,address,normalized_address,phone,contact_person,version)
                VALUES(@beneficiaryId,@name,@address,@normalizedAddress,@phone,@contactPerson,0); SELECT last_insert_rowid();
                """, Fields(beneficiaryId, value));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var workPoint = Build(id, beneficiaryId, value, 0);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Beneficiary, AuditActions.Edit, WorkPointRules.Target(beneficiaryId, owner.Name, workPoint.Name),
                "Punct de lucru adăugat — " + WorkPointRules.Identification(workPoint), string.Empty, beneficiaryId.ToString()),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return workPoint;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<WorkPoint> UpdateAsync(WorkPoint original, WorkPointInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            WorkPointRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var owner = await GetOwnerAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueAsync(connection, transaction, owner, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE beneficiary_work_points SET name=@name,address=@address,normalized_address=@normalizedAddress,
                    phone=@phone,contact_person=@contactPerson,version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(original.BeneficiaryId, value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
            var workPoint = Build(original.Id, original.BeneficiaryId, value, version);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Beneficiary, AuditActions.Edit, WorkPointRules.Target(owner.Id, owner.Name, workPoint.Name),
                "Punct de lucru modificat — " + AuditDetails.Changes(WorkPointRules.Changes(original, workPoint).ToArray()),
                WorkPointRules.GeneratedEditReason, owner.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return workPoint;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(WorkPoint original, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            WorkPointRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            var owner = await GetOwnerAsync(connection, transaction, original.BeneficiaryId, cancellationToken).ConfigureAwait(false);
            await using var delete = SqliteLocalStore.Command(connection, transaction,
                "DELETE FROM beneficiary_work_points WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
            if (await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) throw WorkPointRules.Changed();
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Beneficiary, AuditActions.Edit, WorkPointRules.Target(owner.Id, owner.Name, original.Name),
                "Punct de lucru șters — " + WorkPointRules.Identification(original), WorkPointRules.DeleteReason,
                owner.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<WorkPoint?> GetAsync(SqliteConnection connection, SqliteTransaction transaction, int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            $"SELECT {Columns} FROM beneficiary_work_points WHERE id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static async Task<(int Id, string Name, string Address)> GetOwnerAsync(SqliteConnection connection,
        SqliteTransaction transaction, int beneficiaryId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT name,address FROM beneficiaries WHERE id=@id", ("@id", beneficiaryId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new WorkPointOperationException("Beneficiarul nu mai există. Actualizează lista.");
        return (beneficiaryId, reader.GetString(0), reader.GetString(1));
    }

    // The address must differ from the main work point (the beneficiary's own address) and from every other work point
    // of the same beneficiary; the comparison uses the normalized address, whatever the work point is called.
    private static async Task EnsureUniqueAsync(SqliteConnection connection, SqliteTransaction transaction,
        (int Id, string Name, string Address) owner, WorkPointInput value, int? excludedId, CancellationToken token)
    {
        var key = AddressNormalization.Key(value.Address);
        if (AddressNormalization.Key(owner.Address) == key) throw WorkPointRules.DuplicateAddress(WorkPointRules.PrimaryName);
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT name FROM beneficiary_work_points
            WHERE beneficiary_id=@beneficiaryId AND normalized_address=@normalizedAddress AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@beneficiaryId", owner.Id), ("@normalizedAddress", key), ("@id", excludedId));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw WorkPointRules.DuplicateAddress(existingName);
    }

    private static WorkPoint Read(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetInt64(6));

    private static WorkPoint Build(int id, int beneficiaryId, WorkPointInput value, long version) =>
        new(id, beneficiaryId, value.Name, value.Address, value.Phone, value.ContactPerson, version);

    private static (string, object?)[] Fields(int beneficiaryId, WorkPointInput value) =>
    [
        ("@beneficiaryId", beneficiaryId), ("@name", value.Name), ("@address", value.Address),
        ("@normalizedAddress", AddressNormalization.Key(value.Address)), ("@phone", value.Phone),
        ("@contactPerson", value.ContactPerson)
    ];

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
