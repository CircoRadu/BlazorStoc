using System.Data;
using Microsoft.AspNetCore.Identity;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `web_users` mirrors the SQLite schema exactly (single table, no role lookup table), so this
// repository is the direct MySQL translation of SqliteUserRepository rather than the legacy web_user/web_role shape.
public sealed class MariaUserRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null, IUserPermissions? permissions = null)
    : IUserRepository, IUserAuthenticator
{
    // normalized_username VARCHAR(191); the source username is capped at 100 (WebUserInput), so the uppercase
    // key can never legitimately need truncation, but a defensive check keeps that silent-truncation impossible.
    private const int MaxNormalizedUsernameLength = 191;
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private static readonly object PasswordSubject = new();
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private static readonly string DummyPasswordHash = PasswordHasher.HashPassword(PasswordSubject, Guid.NewGuid().ToString("N"));

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        username = TextNormalization.ForStorage(username);
        password ??= "";
        if (username.Length == 0 || password.Length == 0) return new(AuthenticationStatus.InvalidCredentials);

        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
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
        var storedHash = reader.GetString(4);
        var isActive = reader.GetBoolean(5);
        var verification = PasswordHasher.VerifyHashedPassword(PasswordSubject, storedHash, password);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (verification == PasswordVerificationResult.Failed) return new(AuthenticationStatus.InvalidCredentials);
        if (!isActive) return new(AuthenticationStatus.Inactive);
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var rehash = Command(connection, null,
                "UPDATE web_users SET password_hash=@hash WHERE id=@id AND password_hash=@oldHash",
                ("@hash", PasswordHasher.HashPassword(PasswordSubject, password)), ("@id", user.Id), ("@oldHash", storedHash));
            await rehash.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new(AuthenticationStatus.Success, user);
    }

    public async Task<IReadOnlyList<WebUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAsync("utilizatori.view", cancellationToken);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            """
            SELECT u.id,u.username,u.display_name,u.role,u.is_active,u.version,u.user_type_id,COALESCE(t.name,'')
            FROM web_users u LEFT JOIN user_types t ON t.id=u.user_type_id ORDER BY u.username,u.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var users = new List<WebUser>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            users.Add(ReadUser(reader));
        return users;
    }

    public async Task<WebUser> CreateAsync(WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("utilizatori.add", cancellationToken);
        var value = input.Validated(true);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizedUsername(value.Username);
        var passwordHash = PasswordHasher.HashPassword(PasswordSubject, value.Password);
        try
        {
            var user = await WriteAsync(async (connection, transaction) =>
            {
                await EnsureUniqueUsernameAsync(connection, transaction, normalized, null, cancellationToken).ConfigureAwait(false);
                var type = await ResolveTypeAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
                await using var command = Command(connection, transaction, """
                    INSERT INTO web_users(username,normalized_username,display_name,password_hash,role,user_type_id,is_active,version)
                    VALUES(@username,@normalized,@displayName,@passwordHash,@role,@typeId,1,0)
                    """, ("@username", value.Username), ("@normalized", normalized), ("@displayName", value.DisplayName),
                    ("@passwordHash", passwordHash), ("@role", type.Role), ("@typeId", type.Id is null ? DBNull.Value : type.Id.Value));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return new WebUser(checked((int)command.LastInsertedId), value.Username, value.DisplayName, type.Role, true, 0, type.Id, type.Name);
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.User, user.Id.ToString(),
                $"#{user.Id} · {user.Username}", AuditDetails.Identification(
                    ("Nume utilizator", user.Username), ("Nume afișat", user.DisplayName),
                    ("Rol", user.Role), ("Tip utilizator", user.TypeName), ("Stare", "activ")), cancellationToken).ConfigureAwait(false);
            permissions?.Invalidate();
            return user;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
        }
    }

    public async Task<WebUser> UpdateAsync(WebUser original, WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("utilizatori.edit", cancellationToken);
        var actor = await GetCurrentUsernameAsync(cancellationToken);
        var value = input.Validated(false);
        if (string.Equals(original.Username, actor, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(original.Username, value.Username, StringComparison.Ordinal))
            throw new UserOperationException("Nu îți poți redenumi propriul cont în timpul sesiunii curente.");
        var ownAccount = string.Equals(original.Username, actor, StringComparison.OrdinalIgnoreCase);
        if (ownAccount && !value.IsActive)
            throw new UserOperationException("Nu îți poți dezactiva propriul cont și nu îți poți elimina drepturile de administrator.");

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizedUsername(value.Username);
        var passwordHash = value.Password.Length == 0 ? null : PasswordHasher.HashPassword(PasswordSubject, value.Password);
        try
        {
            var updated = await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                WebUserRules.CheckCurrent(current, original);
                await EnsureUniqueUsernameAsync(connection, transaction, normalized, original.Id, cancellationToken).ConfigureAwait(false);
                var type = await ResolveTypeAsync(connection, transaction, value, cancellationToken).ConfigureAwait(false);
                if (ownAccount && type.Role != AccessRoles.Administrator)
                    throw new UserOperationException("Nu îți poți dezactiva propriul cont și nu îți poți elimina drepturile de administrator.");
                if (original.Role == AccessRoles.Administrator && original.IsActive &&
                    (type.Role != AccessRoles.Administrator || !value.IsActive) &&
                    await CountOtherActiveAdministratorsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false) == 0)
                    throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");

                var version = checked(original.Version + 1);
                var passwordClause = passwordHash is null ? "" : ",password_hash=@passwordHash";
                await using var command = Command(connection, transaction, $"""
                    UPDATE web_users SET username=@username,normalized_username=@normalized,display_name=@displayName,
                        role=@role,user_type_id=@typeId,is_active=@active,version=@version{passwordClause}
                    WHERE id=@id AND version=@oldVersion
                    """, ("@username", value.Username), ("@normalized", normalized), ("@displayName", value.DisplayName),
                    ("@role", type.Role), ("@typeId", type.Id is null ? DBNull.Value : type.Id.Value), ("@active", value.IsActive), ("@version", version),
                    ("@id", original.Id), ("@oldVersion", original.Version));
                if (passwordHash is not null) command.Parameters.AddWithValue("@passwordHash", passwordHash);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Utilizatorul s-a schimbat între timp. Actualizează lista.");
                return new WebUser(original.Id, value.Username, value.DisplayName, type.Role, value.IsActive, version, type.Id, type.Name);
            }, cancellationToken).ConfigureAwait(false);
            var target = $"#{updated.Id} · {updated.Username}";
            var typeChanged = original.TypeId != updated.TypeId || original.Role != updated.Role;
            var changes = UserAuditChanges(original, updated);
            // A change of type has its own journal action; the plain edit keeps the other fields (it is skipped when nothing else changed).
            if (changes.Any(change => change.Field is not ("Rol" or "Tip utilizator") && !Equals(change.Before, change.After)) || !typeChanged)
                await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.User, updated.Id.ToString(), target,
                    changes.Where(change => change.Field is not ("Rol" or "Tip utilizator")), value.Reason, cancellationToken).ConfigureAwait(false);
            if (typeChanged)
                await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.User, updated.Id.ToString(), target,
                    changes.Where(change => change.Field is "Rol" or "Tip utilizator"), value.Reason, cancellationToken, AuditActions.ChangeUserType).ConfigureAwait(false);
            permissions?.Invalidate();
            return updated;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
        }
    }

    public async Task DeleteAsync(WebUser original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAsync("utilizatori.delete", cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new UserOperationException(reasonError);
        var actor = await GetCurrentUsernameAsync(cancellationToken);
        if (string.Equals(original.Username, actor, StringComparison.OrdinalIgnoreCase))
            throw new UserOperationException("Nu îți poți șterge propriul cont.");
        var passwordHash = await GetPasswordHashAsync(original.Id, cancellationToken).ConfigureAwait(false);
        await archiver.ExecuteAsync(ArchiveRequests.User(original, motif,
            [new ArchiveProtectedValue("PasswordHash", passwordHash)]), async (operation, token) =>
        {
            await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                WebUserRules.CheckCurrent(current, original);
                if (original.Role == AccessRoles.Administrator && original.IsActive &&
                    await CountOtherActiveAdministratorsAsync(connection, transaction, original.Id, token).ConfigureAwait(false) == 0)
                    throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, [], token)
                    .ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM web_users WHERE id=@id AND version=@version", ("@id", original.Id), ("@version", original.Version));
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Utilizatorul s-a schimbat între timp. Actualizează lista.");
                await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, token)
                    .ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetPasswordHashAsync(int id, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, null,
            "SELECT password_hash FROM web_users WHERE id=@id LIMIT 1", ("@id", id));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
            ?? throw new UserOperationException("Utilizatorul nu mai există. Actualizează lista.");
    }

    private static string NormalizedUsername(string username)
    {
        var normalized = TextNormalization.UniquenessKey(username);
        if (normalized.Length > MaxNormalizedUsernameLength)
            throw new UserOperationException("Numele de utilizator este prea lung pentru a fi stocat.");
        return normalized;
    }

    private static AuditChange[] UserAuditChanges(WebUser before, WebUser after) =>
    [
        new("Nume utilizator", before.Username, after.Username),
        new("Nume afișat", before.DisplayName, after.DisplayName),
        new("Rol", before.Role, after.Role),
        new("Tip utilizator", before.TypeName, after.TypeName),
        new("Stare", before.IsActive ? "activ" : "inactiv", after.IsActive ? "activ" : "inactiv")
    ];

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new UserOperationException(message), token,
            "Administrarea utilizatorilor este permisă numai în baza BlazorStoc.");

    private static async Task EnsureUniqueUsernameAsync(MySqlConnection connection, MySqlTransaction transaction,
        string normalizedUsername, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM web_users WHERE normalized_username=@normalized AND (@id IS NULL OR id<>@id))",
            ("@normalized", normalizedUsername), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false)))
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
    }

    private static async Task<WebUser?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT u.id,u.username,u.display_name,u.role,u.is_active,u.version,u.user_type_id,COALESCE(t.name,'')
            FROM web_users u LEFT JOIN user_types t ON t.id=u.user_type_id WHERE u.id=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadUser(reader) : null;
    }

    private static async Task<int> CountOtherActiveAdministratorsAsync(MySqlConnection connection, MySqlTransaction transaction, int excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT id FROM web_users WHERE role=@role AND is_active=1 AND id<>@id FOR UPDATE
            """, ("@role", AccessRoles.Administrator), ("@id", excludedId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false)) count++;
        return count;
    }

    // The type of a user and the role it implies: "Administrator" exactly for the system type of that name, "Utilizator" for every other type.
    // Without a TypeId the system type of the given role is used (callers that only know the role).
    private static async Task<(long? Id, string Name, string Role)> ResolveTypeAsync(MySqlConnection connection, MySqlTransaction transaction,
        WebUserInput value, CancellationToken token)
    {
        await using var command = value.TypeId is null
            ? Command(connection, transaction, "SELECT id,name,is_system FROM user_types WHERE name=@name COLLATE utf8mb4_nopad_bin AND is_system=1", ("@name", value.Role))
            : Command(connection, transaction, "SELECT id,name,is_system FROM user_types WHERE id=@id", ("@id", value.TypeId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (value.TypeId is not null) throw new UserOperationException("Tipul de utilizator selectat nu mai există.");
            return (null, value.Role, value.Role);   // the types are not seeded yet
        }
        var name = reader.GetString(1);
        var administrator = reader.GetBoolean(2) && string.Equals(name, AccessRoles.Administrator, StringComparison.Ordinal);
        return (reader.GetInt64(0), name, administrator ? AccessRoles.Administrator : AccessRoles.LimitedUser);
    }

    private Task EnsureAsync(string permission, CancellationToken token) => accessControl?.EnsureAsync(permission, token) ?? Task.CompletedTask;
    private Task<string?> GetCurrentUsernameAsync(CancellationToken token) => accessControl?.GetUsernameAsync(token) ?? Task.FromResult<string?>(null);
    private static WebUser ReadUser(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5), reader.IsDBNull(6) ? null : reader.GetInt64(6), reader.GetString(7));
}
