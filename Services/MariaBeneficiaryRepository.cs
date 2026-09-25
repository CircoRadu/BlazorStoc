using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaBeneficiaryRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IArchiveService? archiveService = null) : IBeneficiaryRepository
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id_beneficiar, COALESCE(beneficiar_denumire, ''), COALESCE(beneficiar_cui, ''), beneficiar_versiune
            FROM beneficiar ORDER BY beneficiar_denumire, id_beneficiar
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Beneficiary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        return result;
    }

    public async Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated();
        var beneficiary = await WriteAsync(async (connection, transaction, userId) =>
        {
            await EnsureUniqueCuiAsync(connection, transaction, value.Cui, null, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueNameAsync(connection, transaction, value.Name, null, cancellationToken).ConfigureAwait(false);
            await using var command = Command(connection, transaction, """
                INSERT INTO beneficiar (id_user, beneficiar_denumire, beneficiar_cui, beneficiar_versiune)
                VALUES (@user, @name, @cui, 0)
                """, ("@user", userId), ("@name", value.Name), ("@cui", value.Cui));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new Beneficiary(checked((int)command.LastInsertedId), value.Name, value.Cui);
        }, cancellationToken, value.Cui).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}", AuditDetails.Identification(
                ("Denumire", beneficiary.Name), ("CUI", beneficiary.Cui)), cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var value = input.Validated(true);
        var beneficiary = await WriteAsync(async (connection, transaction, userId) =>
        {
            var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            BeneficiaryRules.CheckCurrent(current, original);
            await EnsureUniqueCuiAsync(connection, transaction, value.Cui, original.Id, cancellationToken).ConfigureAwait(false);
            await EnsureUniqueNameAsync(connection, transaction, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE beneficiar SET id_user=@user, beneficiar_denumire=@name, beneficiar_cui=@cui, beneficiar_versiune=@version
                WHERE id_beneficiar=@id AND beneficiar_versiune=@oldVersion
                """, ("@user", userId), ("@name", value.Name), ("@cui", value.Cui), ("@version", version),
                ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
            return new Beneficiary(original.Id, value.Name, value.Cui, version);
        }, cancellationToken, value.Cui, original.Id).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Beneficiary, beneficiary.Id.ToString(),
            $"#{beneficiary.Id} · {beneficiary.Name}",
            [new("Denumire", original.Name, beneficiary.Name), new("CUI", original.Cui, beneficiary.Cui)],
            value.Reason, cancellationToken).ConfigureAwait(false);
        return beneficiary;
    }

    public async Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new BeneficiaryOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Beneficiary(original, motif), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction, _) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                BeneficiaryRules.CheckCurrent(current, original);
                await using (var relations = Command(connection, transaction, "SELECT EXISTS(SELECT 1 FROM io WHERE id_beneficiar=@id)", ("@id", original.Id)))
                    BeneficiaryRules.CheckDelete(Convert.ToBoolean(await relations.ExecuteScalarAsync(token).ConfigureAwait(false)));
                // The `project` table is created lazily by the project module; a beneficiary can be deleted safely
                // before it exists, since no project could reference it yet.
                try
                {
                    await using var projects = Command(connection, transaction,
                        "SELECT COUNT(*) FROM project WHERE id_beneficiar=@id", ("@id", original.Id));
                    BeneficiaryRules.CheckNoLiveProjects(Convert.ToInt32(await projects.ExecuteScalarAsync(token).ConfigureAwait(false)));
                }
                catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.NoSuchTable) { }
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token)
                    .ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM beneficiar WHERE id_beneficiar=@id AND beneficiar_versiune=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new BeneficiaryOperationException("Beneficiarul s-a schimbat între timp. Actualizează lista.");
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token)
                    .ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, int, Task<T>> action, CancellationToken token,
        string? savedCui = null, int? savedId = null)
    {
        if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
            throw new BeneficiaryOperationException("Modificările sunt permise numai în baza BlazorStoc.");
        var userId = configuration.GetValue<int>("Database:ApplicationUserId");
        if (userId <= 0)
            throw new BeneficiaryOperationException("Salvarea nu este configurată. Asociază aplicația cu un utilizator al bazei de date.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            await using (var actor = Command(connection, transaction, "SELECT id_user FROM `user` WHERE id_user=@id FOR UPDATE", ("@id", userId)))
                if (await actor.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
                    throw new BeneficiaryOperationException("Utilizatorul asociat aplicației nu există în baza de date.");
            var result = await action(connection, transaction, userId).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            if (savedCui is not null && exception is MySqlException { Number: 1062 })
                throw await ConcurrentDuplicateCuiAsync(savedCui, savedId, token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureSchemaAsync(MySqlConnection connection, CancellationToken token)
    {
        await SchemaGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var database = configuration["Database:Name"] ?? "BlazorStoc";
            if (!string.Equals(database, "BlazorStoc", StringComparison.Ordinal))
                throw new BeneficiaryOperationException("Secțiunea Beneficiari poate modifica schema numai în baza BlazorStoc.");
            if (!await HasColumnAsync(connection, database, "beneficiar_cui", token).ConfigureAwait(false))
            {
                await using var addCui = new MySqlCommand("ALTER TABLE beneficiar ADD COLUMN beneficiar_cui VARCHAR(12) NULL", connection);
                await addCui.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                await using var index = new MySqlCommand("CREATE UNIQUE INDEX UX_beneficiar_cui ON beneficiar (beneficiar_cui)", connection);
                await index.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            if (!await HasColumnAsync(connection, database, "beneficiar_versiune", token).ConfigureAwait(false))
            {
                await using var addVersion = new MySqlCommand("ALTER TABLE beneficiar ADD COLUMN beneficiar_versiune BIGINT NOT NULL DEFAULT 0", connection);
                await addVersion.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        finally { SchemaGate.Release(); }
    }

    private static async Task<bool> HasColumnAsync(MySqlConnection connection, string database, string column, CancellationToken token)
    {
        await using var command = new MySqlCommand("""
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=@database AND TABLE_NAME='beneficiar' AND COLUMN_NAME=@column
            """, connection);
        command.Parameters.AddWithValue("@database", database);
        command.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) == 1;
    }

    private static async Task<Beneficiary?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id_beneficiar, COALESCE(beneficiar_denumire, ''), COALESCE(beneficiar_cui, ''), beneficiar_versiune
            FROM beneficiar WHERE id_beneficiar=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)) : null;
    }

    private static async Task EnsureUniqueCuiAsync(MySqlConnection connection, MySqlTransaction transaction, string cui, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT COALESCE(beneficiar_denumire,'') FROM beneficiar
            WHERE UPPER(beneficiar_cui)=UPPER(@cui) AND (@id IS NULL OR id_beneficiar<>@id)
            ORDER BY id_beneficiar LIMIT 1
            """, ("@cui", cui), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is string existingName)
            throw new BeneficiaryOperationException(BeneficiaryRules.DuplicateCuiMessage(existingName));
    }

    // A concurrent save can pass the check above and still hit UX_beneficiar_cui; report the stored name after rollback.
    private async Task<BeneficiaryOperationException> ConcurrentDuplicateCuiAsync(string cui, int? excludedId, CancellationToken token)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var command = Command(connection, null,"""
                SELECT COALESCE(beneficiar_denumire,'') FROM beneficiar
                WHERE UPPER(beneficiar_cui)=UPPER(@cui) AND (@id IS NULL OR id_beneficiar<>@id)
                ORDER BY id_beneficiar LIMIT 1
                """, ("@cui", cui), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
            return new(BeneficiaryRules.DuplicateCuiMessage(await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(BeneficiaryRules.DuplicateCuiMessage(null));
        }
    }

    private static async Task EnsureUniqueNameAsync(MySqlConnection connection, MySqlTransaction transaction,
        string name, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id_beneficiar,COALESCE(beneficiar_denumire,''),COALESCE(beneficiar_cui,'')
            FROM beneficiar
            WHERE (@id IS NULL OR id_beneficiar<>@id)
            ORDER BY id_beneficiar
            FOR UPDATE
            """, ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var existingName = reader.GetString(1);
            if (!TextNormalization.SameUniqueValue(existingName, name)) continue;
            throw new BeneficiaryOperationException($"Beneficiarul «{existingName}» există deja și are CUI «{reader.GetString(2)}».");
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
