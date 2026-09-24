using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaUserRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null)
    : IUserRepository, IUserAuthenticator
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private static readonly object PasswordSubject = new();
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private static readonly string DummyPasswordHash = PasswordHasher.HashPassword(PasswordSubject, Guid.NewGuid().ToString("N"));

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        username = TextNormalization.ForStorage(username);
        password ??= "";
        if (username.Length == 0 || password.Length == 0) return new(AuthenticationStatus.InvalidCredentials);

        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT u.id_web_user,u.username,u.display_name,r.role_code,u.password_hash,u.is_active
            FROM web_user u INNER JOIN web_role r ON r.id_web_role=u.id_web_role
            WHERE UPPER(u.username)=UPPER(@username)
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("@username", username);
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
            await using var rehash = new MySqlCommand("UPDATE web_user SET password_hash=@hash,updated_utc=UTC_TIMESTAMP() WHERE id_web_user=@id AND password_hash=@oldHash", connection);
            rehash.Parameters.AddWithValue("@hash", PasswordHasher.HashPassword(PasswordSubject, password));
            rehash.Parameters.AddWithValue("@id", user.Id);
            rehash.Parameters.AddWithValue("@oldHash", storedHash);
            await rehash.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new(AuthenticationStatus.Success, user);
    }

    public async Task<IReadOnlyList<WebUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT u.id_web_user,u.username,u.display_name,r.role_code,u.is_active,u.user_version
            FROM web_user u INNER JOIN web_role r ON r.id_web_role=u.id_web_role
            ORDER BY u.username,u.id_web_user
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var users = new List<WebUser>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            users.Add(ReadUser(reader));
        return users;
    }

    public async Task<WebUser> CreateAsync(WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        var value = input.Validated(true);
        cancellationToken.ThrowIfCancellationRequested();
        var passwordHash = PasswordHasher.HashPassword(PasswordSubject, value.Password);
        try
        {
            var user = await WriteAsync(async (connection, transaction, actor) =>
            {
                await EnsureUniqueUsernameAsync(connection, transaction, value.Username, null, cancellationToken).ConfigureAwait(false);
                await using var command = Command(connection, transaction, """
                    INSERT INTO web_user(username,display_name,password_hash,id_web_role,is_active,user_version,created_utc,updated_utc)
                    SELECT @username,@displayName,@passwordHash,id_web_role,1,0,UTC_TIMESTAMP(),UTC_TIMESTAMP()
                    FROM web_role WHERE role_code=@role
                    """, ("@username", value.Username), ("@displayName", value.DisplayName), ("@passwordHash", passwordHash), ("@role", value.Role));
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Nivelul de acces selectat nu există în baza de date.");
                var user = new WebUser(checked((int)command.LastInsertedId), value.Username, value.DisplayName, value.Role, true, 0);
                await AuditAsync(connection, transaction, actor, "create", null, user, cancellationToken).ConfigureAwait(false);
                return user;
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.User, user.Id.ToString(),
                $"#{user.Id} · {user.Username}", AuditDetails.Identification(
                    ("Nume utilizator", user.Username), ("Nume afișat", user.DisplayName),
                    ("Rol", user.Role), ("Stare", "activ")), cancellationToken).ConfigureAwait(false);
            return user;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
        }
    }

    public async Task<WebUser> UpdateAsync(WebUser original, WebUserInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
        var actor = await GetCurrentUsernameAsync(cancellationToken);
        var value = input.Validated(false);
        if (string.Equals(original.Username, actor, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(original.Username, value.Username, StringComparison.Ordinal))
            throw new UserOperationException("Nu îți poți redenumi propriul cont în timpul sesiunii curente.");
        if (string.Equals(original.Username, actor, StringComparison.OrdinalIgnoreCase) &&
            (!value.IsActive || value.Role != AccessRoles.Administrator))
            throw new UserOperationException("Nu îți poți dezactiva propriul cont și nu îți poți elimina drepturile de administrator.");

        cancellationToken.ThrowIfCancellationRequested();
        var passwordHash = value.Password.Length == 0 ? null : PasswordHasher.HashPassword(PasswordSubject, value.Password);
        try
        {
            var updated = await WriteAsync(async (connection, transaction, auditActor) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                WebUserRules.CheckCurrent(current, original);
                await EnsureUniqueUsernameAsync(connection, transaction, value.Username, original.Id, cancellationToken).ConfigureAwait(false);
                if (original.Role == AccessRoles.Administrator && original.IsActive &&
                    (value.Role != AccessRoles.Administrator || !value.IsActive) &&
                    await CountOtherActiveAdministratorsAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false) == 0)
                    throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");

                var version = checked(original.Version + 1);
                var passwordClause = passwordHash is null ? "" : ",password_hash=@passwordHash";
                await using var command = Command(connection, transaction, $"""
                    UPDATE web_user u
                    INNER JOIN web_role r ON r.role_code=@role
                    SET u.username=@username,u.display_name=@displayName,u.id_web_role=r.id_web_role,
                        u.is_active=@active,u.user_version=@version,u.updated_utc=UTC_TIMESTAMP(){passwordClause}
                    WHERE u.id_web_user=@id AND u.user_version=@oldVersion
                    """, ("@role", value.Role), ("@username", value.Username), ("@displayName", value.DisplayName),
                    ("@active", value.IsActive), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version));
                if (passwordHash is not null) command.Parameters.AddWithValue("@passwordHash", passwordHash);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Utilizatorul s-a schimbat între timp sau nivelul de acces nu mai există. Actualizează lista.");
                var updated = new WebUser(original.Id, value.Username, value.DisplayName, value.Role, value.IsActive, version);
                await AuditAsync(connection, transaction, auditActor, "update", current, updated, cancellationToken).ConfigureAwait(false);
                return updated;
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.User, updated.Id.ToString(),
                $"#{updated.Id} · {updated.Username}", UserAuditChanges(original, updated), value.Reason, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
        }
    }

    public async Task DeleteAsync(WebUser original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken);
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
            await WriteAsync(async (connection, transaction, auditActor) =>
            {
                var current = await GetLockedAsync(connection, transaction, original.Id, token).ConfigureAwait(false);
                WebUserRules.CheckCurrent(current, original);
                if (original.Role == AccessRoles.Administrator && original.IsActive &&
                    await CountOtherActiveAdministratorsAsync(connection, transaction, original.Id, token).ConfigureAwait(false) == 0)
                    throw new UserOperationException("Sistemul trebuie să păstreze cel puțin un administrator activ.");
                await MariaArchivePersistence.InsertAsync(connection, transaction, operation, null, token)
                    .ConfigureAwait(false);
                await AuditAsync(connection, transaction, auditActor, "delete", current, null, token).ConfigureAwait(false);
                await using var command = Command(connection, transaction,
                    "DELETE FROM web_user WHERE id_web_user=@id AND user_version=@version", ("@id", original.Id), ("@version", original.Version));
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
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "SELECT password_hash FROM web_user WHERE id_web_user=@id LIMIT 1", connection);
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string
            ?? throw new UserOperationException("Utilizatorul nu mai există. Actualizează lista.");
    }

    private static AuditChange[] UserAuditChanges(WebUser before, WebUser after) =>
    [
        new("Nume utilizator", before.Username, after.Username),
        new("Nume afișat", before.DisplayName, after.DisplayName),
        new("Rol", before.Role, after.Role),
        new("Stare", before.IsActive ? "activ" : "inactiv", after.IsActive ? "activ" : "inactiv")
    ];

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, string, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
            throw new UserOperationException("Administrarea utilizatorilor este permisă numai în baza BlazorStoc.");
        var actor = await GetCurrentUsernameAsync(token) ?? "administrator-necunoscut";
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction, actor).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureUniqueUsernameAsync(MySqlConnection connection, MySqlTransaction transaction,
        string username, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction,
            "SELECT EXISTS(SELECT 1 FROM web_user WHERE UPPER(username)=UPPER(@username) AND (@id IS NULL OR id_web_user<>@id))",
            ("@username", username), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(token).ConfigureAwait(false)))
            throw new UserOperationException("Există deja un utilizator cu acest nume.");
    }

    private static async Task<WebUser?> GetLockedAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT u.id_web_user,u.username,u.display_name,r.role_code,u.is_active,u.user_version
            FROM web_user u INNER JOIN web_role r ON r.id_web_role=u.id_web_role
            WHERE u.id_web_user=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadUser(reader) : null;
    }

    private static async Task<int> CountOtherActiveAdministratorsAsync(MySqlConnection connection, MySqlTransaction transaction, int excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT u.id_web_user FROM web_user u INNER JOIN web_role r ON r.id_web_role=u.id_web_role
            WHERE r.role_code='Administrator' AND u.is_active=1 AND u.id_web_user<>@id FOR UPDATE
            """, ("@id", excludedId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var count = 0;
        while (await reader.ReadAsync(token).ConfigureAwait(false)) count++;
        return count;
    }

    private static async Task AuditAsync(MySqlConnection connection, MySqlTransaction transaction, string actor, string action,
        WebUser? before, WebUser? after, CancellationToken token)
    {
        var details = JsonSerializer.Serialize(new { Before = before, After = after });
        await using var command = Command(connection, transaction, """
            INSERT INTO web_user_audit(actor_username,target_username,audit_action,audit_details,created_utc)
            VALUES(@actor,@target,@action,@details,UTC_TIMESTAMP())
            """, ("@actor", actor), ("@target", after?.Username ?? before?.Username ?? ""), ("@action", action), ("@details", details));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
    private Task<string?> GetCurrentUsernameAsync(CancellationToken token) => accessControl?.GetUsernameAsync(token) ?? Task.FromResult<string?>(null);
    private static WebUser ReadUser(MySqlDataReader reader) => new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5));
    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
