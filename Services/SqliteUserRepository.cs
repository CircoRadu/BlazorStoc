using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteUserRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null)
    : IUserRepository, IUserAuthenticator
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private static readonly object PasswordSubject = new();
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private static readonly string DummyPasswordHash = PasswordHasher.HashPassword(PasswordSubject, Guid.NewGuid().ToString("N"));

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        username = TextNormalization.ForStorage(username);
        password ??= string.Empty;
        if (username.Length == 0 || password.Length == 0) return new(AuthenticationStatus.InvalidCredentials);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT id,username,display_name,role,password_hash,is_active
            FROM web_users WHERE normalized_username=@username LIMIT 1
            """, ("@username", TextNormalization.UniquenessKey(username)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            PasswordHasher.VerifyHashedPassword(PasswordSubject, DummyPasswordHash, password);
            return new(AuthenticationStatus.InvalidCredentials);
        }
        var user = new AuthenticatedWebUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        var passwordHash = reader.GetString(4);
        var active = reader.GetBoolean(5);
        var verification = PasswordHasher.VerifyHashedPassword(PasswordSubject, passwordHash, password);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (verification == PasswordVerificationResult.Failed) return new(AuthenticationStatus.InvalidCredentials);
        if (!active) return new(AuthenticationStatus.Inactive);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var rehash = SqliteLocalStore.Command(connection, null, """
                UPDATE web_users SET password_hash=@newHash WHERE id=@id AND password_hash=@oldHash
                """, ("@newHash", PasswordHasher.HashPassword(PasswordSubject, password)),
                ("@id", user.Id), ("@oldHash", passwordHash));
            await rehash.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new(AuthenticationStatus.Success, user);
    }

    public async Task<IReadOnlyList<WebUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT id,username,display_name,role,is_active,version FROM web_users ORDER BY username,id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var users = new List<WebUser>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) users.Add(Read(reader));
        return users;
    }

    public async Task<WebUser> CreateAsync(WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(true);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var hash = PasswordHasher.HashPassword(PasswordSubject, value.Password);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureUniqueUsernameAsync(connection, transaction, value.Username, null, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO web_users(username,normalized_username,display_name,password_hash,role,is_active,version)
                VALUES(@username,@normalized,@displayName,@passwordHash,@role,1,0); SELECT last_insert_rowid();
                """, ("@username", value.Username), ("@normalized", TextNormalization.UniquenessKey(value.Username)),
                ("@displayName", value.DisplayName), ("@passwordHash", hash), ("@role", value.Role));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var user = new WebUser(id, value.Username, value.DisplayName, value.Role, true, 0);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.User, AuditActions.Create, $"#{id} · {user.Username}", AuditDetails.Identification(
                    ("Nume utilizator", user.Username), ("Nume afișat", user.DisplayName),
                    ("Rol", user.Role), ("Stare", "activ")), string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return user;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<WebUser> UpdateAsync(WebUser original, WebUserInput input,
        CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(false);
        var currentUsername = await GetCurrentUsernameAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(original.Username, currentUsername, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(original.Username, value.Username, StringComparison.Ordinal))
            throw new UserOperationException("Nu îți poți redenumi propriul cont în timpul sesiunii curente.");
        if (string.Equals(original.Username, currentUsername, StringComparison.OrdinalIgnoreCase) &&
            (!value.IsActive || value.Role != AccessRoles.Administrator))
            throw new UserOperationException("Nu îți poți dezactiva propriul cont și nu îți poți elimina drepturile de administrator.");
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        var passwordHash = value.Password.Length == 0 ? null : PasswordHasher.HashPassword(PasswordSubject, value.Password);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            WebUserRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueUsernameAsync(connection, transaction, value.Username, original.Id, cancellationToken).ConfigureAwait(false);
            if (original.Role == AccessRoles.Administrator && original.IsActive &&
                (value.Role != AccessRoles.Administrator || !value.IsActive) &&
                await CountOtherAdministratorsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false) == 0)
                throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");
            var version = checked(original.Version + 1);
            var passwordClause = passwordHash is null ? string.Empty : ",password_hash=@passwordHash";
            await using var update = SqliteLocalStore.Command(connection, transaction, $"""
                UPDATE web_users SET username=@username,normalized_username=@normalized,display_name=@displayName,
                    role=@role,is_active=@active,version=@version{passwordClause}
                WHERE id=@id AND version=@oldVersion
                """, ("@username", value.Username), ("@normalized", TextNormalization.UniquenessKey(value.Username)),
                ("@displayName", value.DisplayName), ("@role", value.Role), ("@active", value.IsActive ? 1 : 0),
                ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version));
            if (passwordHash is not null) update.Parameters.AddWithValue("@passwordHash", passwordHash);
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new UserOperationException("Utilizatorul s-a schimbat între timp. Actualizează lista.");
            var user = new WebUser(original.Id, value.Username, value.DisplayName, value.Role, value.IsActive, version);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.User, AuditActions.Edit, $"#{user.Id} · {user.Username}",
                AuditDetails.Changes(UserAuditChanges(original, user)), value.Reason, user.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return user;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(WebUser original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new UserOperationException(reasonError);
        var currentUsername = await GetCurrentUsernameAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(original.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            throw new UserOperationException("Nu îți poți șterge propriul cont.");
        var passwordHash = await GetPasswordHashAsync(original.Id, cancellationToken).ConfigureAwait(false);
        await archiver.ExecuteAsync(ArchiveRequests.User(original, motif,
            [new ArchiveProtectedValue("PasswordHash", passwordHash)]), async (operation, token) =>
        {
            await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
            try
            {
                WebUserRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, token).ConfigureAwait(false), original);
                if (original.Role == AccessRoles.Administrator && original.IsActive &&
                    await CountOtherAdministratorsAsync(connection, transaction, original.Id, token).ConfigureAwait(false) == 0)
                    throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");
                await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, null, token)
                    .ConfigureAwait(false);
                await using var delete = SqliteLocalStore.Command(connection, transaction,
                    "DELETE FROM web_users WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Utilizatorul s-a schimbat între timp. Actualizează lista.");
                await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(operation.ActorUsername, operation.ActorRole,
                    AuditEntities.User, AuditActions.Delete, operation.Request.Target, operation.Request.Details,
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

    private async Task<string> GetPasswordHashAsync(int id, CancellationToken token)
    {
        await using var connection = await store.OpenConnectionAsync(token).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT password_hash FROM web_users WHERE id=@id", ("@id", id));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
            ?? throw new UserOperationException("Utilizatorul nu mai există. Actualizează lista.");
    }

    private static async Task<WebUser?> GetAsync(SqliteConnection connection, SqliteTransaction transaction, int id,
        CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction,
            "SELECT id,username,display_name,role,is_active,version FROM web_users WHERE id=@id", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static WebUser Read(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5));

    private static async Task EnsureUniqueUsernameAsync(SqliteConnection connection, SqliteTransaction transaction,
        string username, int? excludedId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM web_users
                WHERE normalized_username=@normalized AND (@id IS NULL OR id<>@id))
            """, ("@normalized", TextNormalization.UniquenessKey(username)), ("@id", excludedId));
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false)))
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
    }

    private static async Task<int> CountOtherAdministratorsAsync(SqliteConnection connection,
        SqliteTransaction transaction, int excludedId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT COUNT(*) FROM web_users WHERE role=@role AND is_active=1 AND id<>@id
            """, ("@role", AccessRoles.Administrator), ("@id", excludedId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
    }

    private static AuditChange[] UserAuditChanges(WebUser before, WebUser after) =>
    [
        new("Nume utilizator", before.Username, after.Username),
        new("Nume afișat", before.DisplayName, after.DisplayName),
        new("Rol", before.Role, after.Role),
        new("Stare", before.IsActive ? "activ" : "inactiv", after.IsActive ? "activ" : "inactiv")
    ];

    private Task EnsureAdministratorAsync(CancellationToken token) =>
        accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
    private Task<string?> GetCurrentUsernameAsync(CancellationToken token) =>
        accessControl?.GetUsernameAsync(token) ?? Task.FromResult<string?>(null);
}
