using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: `beneficiaries` is the real, already-populated table (migration\schema-mariadb.sql), created and
// owned outside the app - the runtime account has no DDL rights, so this repository never creates or alters it.
public sealed class MariaBeneficiaryRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IBeneficiaryRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id, name, cui, version FROM beneficiaries ORDER BY name, id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Beneficiary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    public async Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var beneficiary = await WriteAsync(async (connection, transaction) =>
        {
            await EnsureUniqueAsync(connection, transaction, value, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiaries (name, normalized_name, cui, normalized_cui, version)
                VALUES (@name, @normalizedName, @cui, @normalizedCui, 0)
                """, ("@name", value.Name), ("@normalizedName", TextNormalization.UniquenessKey(value.Name)),
                ("@cui", value.Cui), ("@normalizedCui", TextNormalization.UniquenessKey(value.Cui)));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Beneficiary(checked((int)command.LastInsertedId), value.Name, value.Cui);
        }, cancellationToken, value).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}", AuditDetails.Identification(
                ("Denumire", beneficiary.Name), ("CUI", beneficiary.Cui)), cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var beneficiary = await WriteAsync(async (connection, transaction) =>
        {
            BeneficiaryRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueAsync(connection, transaction, value, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE beneficiaries SET name=@name, normalized_name=@normalizedName, cui=@cui,
                    normalized_cui=@normalizedCui, version=@version WHERE id=@id AND version=@oldVersion
                """, ("@name", value.Name), ("@normalizedName", TextNormalization.UniquenessKey(value.Name)),
                ("@cui", value.Cui), ("@normalizedCui", TextNormalization.UniquenessKey(value.Cui)),
                ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
            return new Beneficiary(original.Id, value.Name, value.Cui, version);
        }, cancellationToken, value, original.Id).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}",
            [new("Denumire", original.Name, beneficiary.Name), new("CUI", original.Cui, beneficiary.Cui)],
            value.Reason, cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new BeneficiaryOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Beneficiary(original, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                BeneficiaryRules.CheckCurrent(await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                await using (var relations = Command(connection, transaction,
                    "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE beneficiary_id=@id)", ("@id", original.Id)))
                    BeneficiaryRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await using (var projects = Command(connection, transaction,
                    "SELECT COUNT(*) FROM projects WHERE beneficiary_id=@id", ("@id", original.Id)))
                    BeneficiaryRules.CheckNoLiveProjects(Convert.ToInt32(await projects.ExecuteScalarAsync(token).ConfigureAwait(false)));
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM beneficiaries WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token,
        BeneficiaryInput? savedValue = null, int? savedId = null)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new BeneficiaryOperationException("Modificările sunt permise numai în baza BlazorStoc.");
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
            if (savedValue is not null && exception is MySqlException { Number: 1062 })
                throw await ConcurrentDuplicateAsync(savedValue, savedId, token).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Beneficiary?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT id, name, cui, version FROM beneficiaries WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static Beneficiary Read(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3));

    private static async Task EnsureUniqueAsync(MySqlConnection connection, MySqlTransaction transaction,
        BeneficiaryInput value, int? excludedId, CancellationToken token)
    {
        await using (var cui = Command(connection, transaction, """
            SELECT name FROM beneficiaries
            WHERE normalized_cui=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(value.Cui)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value)))
        {
            if (await cui.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
                throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateCuiMessage(existingName));
        }
        await using var name = Command(connection, transaction, """
            SELECT name, cui FROM beneficiaries
            WHERE normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(value.Name)),
            ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await name.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new BeneficiaryOperationException($"Beneficiarul «{reader.GetString(0)}» există deja și are CUI «{reader.GetString(1)}».");
    }

    // A concurrent save can pass the checks above and still hit uq_beneficiaries_0/uq_beneficiaries_1; report the
    // stored row after rollback, checking CUI first (same precedence as EnsureUniqueAsync).
    private async Task<BeneficiaryOperationException> ConcurrentDuplicateAsync(BeneficiaryInput value, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using (var cui = Command(connection, null, """
                SELECT name FROM beneficiaries
                WHERE normalized_cui=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
                """, ("@normalized", TextNormalization.UniquenessKey(value.Cui)),
                ("@id", excludedId is null ? DBNull.Value : excludedId.Value)))
            {
                if (await cui.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
                    return new(BeneficiaryRules.DuplicateCuiMessage(existingName));
            }
            await using var name = Command(connection, null, """
                SELECT name, cui FROM beneficiaries
                WHERE normalized_name=@normalized AND (@id IS NULL OR id<>@id) LIMIT 1
                """, ("@normalized", TextNormalization.UniquenessKey(value.Name)),
                ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
            await using var reader = await name.ExecuteReaderAsync(token).ConfigureAwait(false);
            return await reader.ReadAsync(token).ConfigureAwait(false)
                ? new($"Beneficiarul «{reader.GetString(0)}» există deja și are CUI «{reader.GetString(1)}».")
                : new(BeneficiaryRules.DuplicateCuiMessage(null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(BeneficiaryRules.DuplicateCuiMessage(null));
        }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
