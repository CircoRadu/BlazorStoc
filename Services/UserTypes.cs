using System.ComponentModel.DataAnnotations;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// User types (Setari -> Tipuri de utilizatori): named sets of permission keys from the catalog (Services/Permissions.cs). The two system types
// exist from the start: "Administrator" always has every key and cannot be changed or deleted; "Utilizator" can have its keys edited but keeps its
// name and cannot be deleted. Every other type is created, edited and deleted by the administrator.
public sealed record UserTypeSummary(long Id, string Name, string Description, bool IsSystem, int UserCount, int PermissionCount, long Version);

public sealed record UserTypeDetail(UserTypeSummary Summary, IReadOnlySet<string> Keys)
{
    public long Id => Summary.Id;
    public string Name => Summary.Name;
}

public sealed class UserTypeInput
{
    [Required(ErrorMessage = "Completează numele tipului.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Numele tipului trebuie să aibă între 2 și 60 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(300, ErrorMessage = "Descrierea poate avea cel mult 300 de caractere.")]
    public string Description { get; set; } = "";

    public HashSet<string> Keys { get; set; } = [];

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public UserTypeInput Validated()
    {
        var normalized = new UserTypeInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name), Description = TextNormalization.ForStorage(Description ?? ""),
            Keys = [.. Permissions.Normalize(Keys ?? [])], Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new UserOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        return normalized;
    }

    public static UserTypeInput From(UserTypeDetail detail) => new() { Name = detail.Name, Description = detail.Summary.Description, Keys = [.. detail.Keys] };
}

public interface IUserTypeRepository
{
    Task<IReadOnlyList<UserTypeSummary>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<UserTypeDetail?> GetAsync(long id, CancellationToken cancellationToken = default);
    Task<UserTypeDetail> CreateAsync(UserTypeInput input, CancellationToken cancellationToken = default);
    Task<UserTypeDetail> UpdateAsync(UserTypeDetail original, UserTypeInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(UserTypeDetail original, string reason, CancellationToken cancellationToken = default);
}

public sealed class MariaUserTypeRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null,
    IUserPermissions? permissions = null) : IUserTypeRepository
{
    private const string SelectSummary = """
        SELECT t.id,t.name,t.description,t.is_system,
               (SELECT COUNT(*) FROM web_users u WHERE u.user_type_id=t.id),
               (SELECT COUNT(*) FROM user_type_permissions p WHERE p.user_type_id=t.id),t.version
        FROM user_types t
        """;

    public async Task<IReadOnlyList<UserTypeSummary>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        // The list also feeds the user form, so a user who may add or edit users can read it.
        if (accessControl is not null)
            await accessControl.EnsureAnyAsync(["tipuri-utilizatori.view", "utilizatori.add", "utilizatori.edit"], cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, SelectSummary + " ORDER BY t.is_system DESC, t.name");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<UserTypeSummary>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) list.Add(ReadSummary(reader));
        return list;
    }

    public async Task<UserTypeDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(Permissions.Key("tipuri-utilizatori", Permissions.View), cancellationToken);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadDetailAsync(connection, null, id, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserTypeDetail> CreateAsync(UserTypeInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(Permissions.Key("tipuri-utilizatori", Permissions.Add), cancellationToken);
        var value = input.Validated();
        var key = TextNormalization.UniquenessKey(value.Name);
        try
        {
            var created = await WriteAsync(async (connection, transaction) =>
            {
                await using var insert = Command(connection, transaction,
                    "INSERT INTO user_types(name,normalized_name,description,is_system,version) VALUES(@name,@key,@description,0,0)",
                    ("@name", value.Name), ("@key", key), ("@description", value.Description));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var id = insert.LastInsertedId;
                foreach (var permission in value.Keys) await AddKeyAsync(connection, transaction, id, permission, cancellationToken).ConfigureAwait(false);
                return (await ReadDetailAsync(connection, transaction, id, false, cancellationToken).ConfigureAwait(false))!;
            }, cancellationToken).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserType, AuditActions.CreateUserType, created.Id.ToString(),
                Target(created), AuditDetails.Identification(("Nume", created.Name), ("Descriere", created.Summary.Description),
                    ("Număr permisiuni", created.Keys.Count.ToString())), string.Empty, cancellationToken).ConfigureAwait(false);
            foreach (var permission in created.Keys)
                await RecordKeyAsync(AuditActions.GrantPermission, created, permission, string.Empty, cancellationToken).ConfigureAwait(false);
            permissions?.Invalidate();
            return created;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new UserOperationException("Există deja un tip de utilizator cu acest nume.");
        }
    }

    public async Task<UserTypeDetail> UpdateAsync(UserTypeDetail original, UserTypeInput input, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(Permissions.Key("tipuri-utilizatori", Permissions.Edit), cancellationToken);
        var value = input.Validated();
        if (original.Summary.IsSystem && original.Name == AccessRoles.Administrator)
            throw new UserOperationException("Tipul Administrator are întotdeauna toate drepturile și nu poate fi modificat.");
        if (original.Summary.IsSystem && !string.Equals(value.Name, original.Name, StringComparison.Ordinal))
            throw new UserOperationException("Un tip de sistem nu poate fi redenumit.");
        try
        {
            var updated = await WriteAsync(async (connection, transaction) =>
            {
                var current = await ReadDetailAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
                if (current is null || current.Summary.Version != original.Summary.Version)
                    throw new UserOperationException("Tipul a fost modificat sau șters între timp. Actualizează lista și reia operația.");
                await using var update = Command(connection, transaction,
                    "UPDATE user_types SET name=@name,normalized_name=@key,description=@description,version=version+1 WHERE id=@id AND version=@version",
                    ("@name", value.Name), ("@key", TextNormalization.UniquenessKey(value.Name)), ("@description", value.Description),
                    ("@id", original.Id), ("@version", original.Summary.Version));
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new UserOperationException("Tipul s-a schimbat între timp. Actualizează lista.");
                foreach (var permission in current.Keys.Except(value.Keys))
                    await RemoveKeyAsync(connection, transaction, original.Id, permission, cancellationToken).ConfigureAwait(false);
                foreach (var permission in value.Keys.Except(current.Keys))
                    await AddKeyAsync(connection, transaction, original.Id, permission, cancellationToken).ConfigureAwait(false);
                return (await ReadDetailAsync(connection, transaction, original.Id, false, cancellationToken).ConfigureAwait(false))!;
            }, cancellationToken).ConfigureAwait(false);
            var reason = value.Reason.Length > 0 ? value.Reason : "Modificare tip de utilizator";
            if (!string.Equals(original.Name, updated.Name, StringComparison.Ordinal))
                await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.UserType, updated.Id.ToString(), Target(updated),
                    [new("Nume", original.Name, updated.Name)], reason, cancellationToken, AuditActions.RenameUserType).ConfigureAwait(false);
            if (!string.Equals(original.Summary.Description, updated.Summary.Description, StringComparison.Ordinal))
                await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.UserType, updated.Id.ToString(), Target(updated),
                    [new("Descriere", original.Summary.Description, updated.Summary.Description)], reason, cancellationToken, AuditActions.EditUserTypeDescription).ConfigureAwait(false);
            foreach (var permission in original.Keys.Except(updated.Keys))
                await RecordKeyAsync(AuditActions.RevokePermission, updated, permission, reason, cancellationToken).ConfigureAwait(false);
            foreach (var permission in updated.Keys.Except(original.Keys))
                await RecordKeyAsync(AuditActions.GrantPermission, updated, permission, reason, cancellationToken).ConfigureAwait(false);
            permissions?.Invalidate();
            return updated;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new UserOperationException("Există deja un tip de utilizator cu acest nume.");
        }
    }

    public async Task DeleteAsync(UserTypeDetail original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(Permissions.Key("tipuri-utilizatori", Permissions.Delete), cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new UserOperationException(reasonError);
        if (original.Summary.IsSystem) throw new UserOperationException("Un tip de sistem nu poate fi șters.");
        await WriteAsync<bool>(async (connection, transaction) =>
        {
            var current = await ReadDetailAsync(connection, transaction, original.Id, true, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Summary.Version != original.Summary.Version)
                throw new UserOperationException("Tipul a fost modificat sau șters între timp. Actualizează lista și reia operația.");
            if (current.Summary.UserCount > 0)
                throw new UserOperationException($"Tipul este atribuit la {current.Summary.UserCount} utilizator(i). Mută-i întâi pe alt tip.");
            await using var delete = Command(connection, transaction, "DELETE FROM user_types WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserType, AuditActions.DeleteUserType, original.Id.ToString(), Target(original),
            AuditDetails.Identification(("Nume", original.Name), ("Descriere", original.Summary.Description), ("Număr permisiuni", original.Keys.Count.ToString())),
            motif, cancellationToken).ConfigureAwait(false);
        permissions?.Invalidate();
    }

    private static string Target(UserTypeDetail type) => $"#{type.Id} · {type.Name}";

    private Task RecordKeyAsync(string action, UserTypeDetail type, string permission, string reason, CancellationToken cancellationToken) =>
        AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.UserType, action, type.Id.ToString(), Target(type),
            AuditDetails.Identification(("Permisiune", PermissionLabel(permission))), reason, cancellationToken);

    // "Furnizori: Stergere" for the key furnizori.delete; the key itself when the catalog does not know it.
    public static string PermissionLabel(string key)
    {
        foreach (var module in Permissions.Modules)
            foreach (var action in module.Actions)
                if (module.Key(action.Id) == key) return $"{module.Title}: {action.Label} ({key})";
        return key;
    }

    private static UserTypeSummary ReadSummary(MySqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
        checked((int)reader.GetInt64(4)), checked((int)reader.GetInt64(5)), reader.GetInt64(6));

    private static async Task<UserTypeDetail?> ReadDetailAsync(MySqlConnection connection, MySqlTransaction? transaction, long id, bool forUpdate, CancellationToken token)
    {
        UserTypeSummary summary;
        await using (var command = Command(connection, transaction, SelectSummary + " WHERE t.id=@id" + (forUpdate ? " FOR UPDATE" : ""), ("@id", id)))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
            summary = ReadSummary(reader);
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        await using var select = Command(connection, transaction, "SELECT permission_key FROM user_type_permissions WHERE user_type_id=@id", ("@id", id));
        await using var keyReader = await select.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await keyReader.ReadAsync(token).ConfigureAwait(false)) keys.Add(keyReader.GetString(0));
        // The administrator type is every key of the catalog, also those added after it was seeded.
        return new(summary, summary.IsSystem && summary.Name == AccessRoles.Administrator ? new HashSet<string>(Permissions.AllKeys, StringComparer.Ordinal) : keys);
    }

    private static async Task AddKeyAsync(MySqlConnection connection, MySqlTransaction transaction, long id, string key, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "INSERT IGNORE INTO user_type_permissions(user_type_id,permission_key) VALUES(@id,@key)", ("@id", id), ("@key", key));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task RemoveKeyAsync(MySqlConnection connection, MySqlTransaction transaction, long id, string key, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "DELETE FROM user_type_permissions WHERE user_type_id=@id AND permission_key=@key", ("@id", id), ("@key", key));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new UserOperationException(message), token,
            "Administrarea tipurilor de utilizatori este permisă numai în baza BlazorStoc.");

    private Task EnsureAsync(string permission, CancellationToken token) => accessControl?.EnsureAsync(permission, token) ?? Task.CompletedTask;
}
