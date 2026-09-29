using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteBeneficiaryRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null)
    : IBeneficiaryRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    public async Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT id,name,cui,version,kind,address,phone,registry_number,postal_code,caen_code,anaf_verified FROM beneficiaries ORDER BY name,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var beneficiaries = new List<Beneficiary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) beneficiaries.Add(Read(reader));
        return beneficiaries;
    }

    public async Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default)
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
                INSERT INTO beneficiaries(name,normalized_name,cui,normalized_cui,kind,address,phone,registry_number,
                    postal_code,caen_code,anaf_verified,version)
                VALUES(@name,@normalizedName,@cui,@normalizedCui,@kind,@address,@phone,@registryNumber,
                    @postalCode,@caenCode,@anafVerified,0); SELECT last_insert_rowid();
                """, Fields(value));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var beneficiary = Build(id, value, 0);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Beneficiary, AuditActions.Create, $"#{id} · {beneficiary.Name}",
                BeneficiaryRules.Identification(beneficiary), string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return beneficiary;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            BeneficiaryRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE beneficiaries SET name=@name,normalized_name=@normalizedName,cui=@cui,
                    normalized_cui=@normalizedCui,kind=@kind,address=@address,phone=@phone,registry_number=@registryNumber,
                    postal_code=@postalCode,caen_code=@caenCode,anaf_verified=@anafVerified,
                    version=@version WHERE id=@id AND version=@oldVersion
                """, [.. Fields(value), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
            var beneficiary = Build(original.Id, value, version);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Beneficiary, AuditActions.Edit, $"#{beneficiary.Id} · {beneficiary.Name}",
                AuditDetails.Changes(BeneficiaryRules.Changes(original, beneficiary).ToArray()), value.Reason, beneficiary.Id.ToString()),
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return beneficiary;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new BeneficiaryOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Beneficiary(original, motif), async (operation, token) =>
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            try
            {
                BeneficiaryRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await using (var relations = SqliteLocalStore.Command(connection, transaction,
                    "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE beneficiary_id=@id)", ("@id", original.Id)))
                    BeneficiaryRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await using (var projects = SqliteLocalStore.Command(connection, transaction,
                    "SELECT COUNT(*) FROM projects WHERE beneficiary_id=@id", ("@id", original.Id)))
                    BeneficiaryRules.CheckNoLiveProjects(Convert.ToInt32(await projects.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, [], token)
                    .ConfigureAwait(false);
                await using var delete = SqliteLocalStore.Command(connection, transaction,
                    "DELETE FROM beneficiaries WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
                await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                    AuditEntities.Beneficiary, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
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

    private static async Task<Beneficiary?> GetAsync(SqliteConnection connection, SqliteTransaction transaction,
        int id, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT id,name,cui,version,kind,address,phone,registry_number,postal_code,caen_code,anaf_verified FROM beneficiaries WHERE id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Beneficiary Read(SqliteDataReader reader) =>
        new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
            reader.GetInt64(10) != 0);

    private static Beneficiary Build(int id, BeneficiaryInput value, long version) => new(id, value.Name, value.Cui, version,
        value.Kind, value.Address, value.Phone, value.RegistryNumber, value.PostalCode, value.CaenCode, value.AnafVerified);

    private static (string, object?)[] Fields(BeneficiaryInput value) =>
    [
        ("@name", value.Name), ("@normalizedName", TextNormalization.UniquenessKey(value.Name)), ("@cui", value.Cui),
        ("@normalizedCui", BeneficiaryRules.IdentityKey(value)), ("@kind", value.Kind), ("@address", value.Address),
        ("@phone", value.Phone), ("@registryNumber", value.RegistryNumber), ("@postalCode", value.PostalCode),
        ("@caenCode", value.CaenCode), ("@anafVerified", value.AnafVerified ? 1 : 0)
    ];

    private static async Task EnsureUniqueAsync(SqliteConnection connection, SqliteTransaction transaction,
        BeneficiaryInput value, int? excludedId, CancellationToken token)
    {
        await using (var identity = SqliteLocalStore.Command(connection, transaction, """
            SELECT name FROM beneficiaries
            WHERE normalized_cui=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", BeneficiaryRules.IdentityKey(value)), ("@id", excludedId)))
        {
            if (await identity.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
                throw BeneficiaryRules.DuplicateIdentity(value, existingName);
        }
        await using var name = SqliteLocalStore.Command(connection, transaction, """
            SELECT name,cui FROM beneficiaries
            WHERE normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(value.Name)), ("@id", excludedId));
        await using var reader = await name.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateNameMessage(reader.GetString(0), reader.GetString(1)));
    }

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
