using System.Data;
using System.Globalization;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB mode: `system_types` and `system_type_aliases` (migration 22). Reading needs a product operator, changing needs an administrator;
// every change is checked against the version read (a concurrent change is refused) and has its own journal action.
public sealed class MariaSystemTypeRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : ISystemTypeRepository
{
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<SystemType>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAllAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<SystemType>> ReadAllAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        var aliases = new Dictionary<int, List<string>>();
        await using (var command = Command(connection, transaction, "SELECT system_type_id,alias FROM system_type_aliases ORDER BY alias,id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                if (!aliases.TryGetValue(id, out var list)) aliases[id] = list = [];
                list.Add(reader.GetString(1));
            }
        var result = new List<SystemType>();
        await using (var command = Command(connection, transaction, "SELECT id,name,active,sort_order,version FROM system_types ORDER BY sort_order,id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                result.Add(new SystemType(id, reader.GetString(1), Convert.ToBoolean(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)),
                    aliases.TryGetValue(id, out var list) ? list : [], reader.GetInt64(4)));
            }
        return result;
    }

    public async Task<SystemType?> FindAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = SystemTypeRules.Key(text);
        if (key.Length == 0) return null;
        return (await GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(type => SystemTypeRules.Key(type.Name) == key || type.Aliases.Any(alias => SystemTypeRules.Key(alias) == key));
    }

    public async Task<SystemType> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(name);
        var id = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (SystemTypeRules.NameProblem(text, all) is { } problem) throw new SystemTypeOperationException(problem);
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO system_types(name,name_key,active,sort_order,version,created_utc,updated_utc) VALUES(@name,@key,1,@order,0,@now,@now)
                """, ("@name", text), ("@key", SystemTypeRules.Key(text)), ("@order", all.Count == 0 ? 1 : all.Max(type => type.SortOrder) + 1), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)insert.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.SystemType, AuditActions.AddSystemType, id.ToString(), text,
            AuditDetails.Identification(("Denumire", text)), string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemType> RenameAsync(SystemType original, string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(name);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var type = CheckCurrent(all, original);
            if (text == type.Name) throw new SystemTypeOperationException(SystemTypeRules.NoChangeMessage);
            if (SystemTypeRules.NameProblem(text, all, type.Id) is { } problem) throw new SystemTypeOperationException(problem);
            await UpdateAsync(connection, transaction, type, "name=@name,name_key=@key", cancellationToken, ("@name", text), ("@key", SystemTypeRules.Key(text))).ConfigureAwait(false);
            return type;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SystemType, current.Id.ToString(), text,
            [new AuditChange("Denumire", current.Name, text)], string.Empty, cancellationToken, AuditActions.RenameSystemType).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemType> SetActiveAsync(SystemType original, bool active, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var type = CheckCurrent(await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false), original);
            if (type.Active == active) return type;
            await UpdateAsync(connection, transaction, type, "active=@active", cancellationToken, ("@active", active ? 1 : 0)).ConfigureAwait(false);
            return type;
        }, cancellationToken).ConfigureAwait(false);
        if (current.Active != active)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SystemType, current.Id.ToString(), current.Name,
                [new AuditChange("Stare", current.Active ? "Activ" : "Inactiv", active ? "Activ" : "Inactiv")], string.Empty, cancellationToken,
                active ? AuditActions.ActivateSystemType : AuditActions.DeactivateSystemType).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemType> MoveAsync(SystemType original, int direction, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var moved = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var type = CheckCurrent(all, original);
            var index = all.FindIndex(item => item.Id == type.Id);
            var neighbourIndex = index + Math.Sign(direction);
            if (neighbourIndex < 0 || neighbourIndex >= all.Count) return (Type: type, Changed: false, From: 0, To: 0);
            // The two types swap places: orders are renumbered 1..n so equal or missing numbers never matter.
            var order = all.Select(item => item.Id).ToList();
            (order[index], order[neighbourIndex]) = (order[neighbourIndex], order[index]);
            for (var position = 0; position < order.Count; position++)
                await using (var update = Command(connection, transaction, "UPDATE system_types SET sort_order=@order,version=version+1,updated_utc=@now WHERE id=@id",
                    ("@order", position + 1), ("@id", order[position]), ("@now", MariaTimeText.Format(DateTime.UtcNow))))
                    await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Type: type, Changed: true, From: index + 1, To: neighbourIndex + 1);
        }, cancellationToken).ConfigureAwait(false);
        if (moved.Changed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SystemType, moved.Type.Id.ToString(), moved.Type.Name,
                [new AuditChange("Poziție", moved.From.ToString(CultureInfo.InvariantCulture), moved.To.ToString(CultureInfo.InvariantCulture))], string.Empty, cancellationToken,
                AuditActions.MoveSystemType).ConfigureAwait(false);
        return await GetAsync(moved.Type.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemType> AddAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(alias);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var all = await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var found = CheckCurrent(all, type);
            if (SystemTypeRules.AliasProblem(text, found, all) is { } problem) throw new SystemTypeOperationException(problem);
            await using var insert = Command(connection, transaction,
                "INSERT INTO system_type_aliases(system_type_id,alias,alias_key,created_utc) VALUES(@type,@alias,@key,@now)",
                ("@type", found.Id), ("@alias", text), ("@key", SystemTypeRules.Key(text)), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await UpdateAsync(connection, transaction, found, "", cancellationToken).ConfigureAwait(false);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SystemType, current.Id.ToString(), current.Name,
            [new AuditChange("Denumire alternativă", "—", text)], string.Empty, cancellationToken, AuditActions.AddSystemTypeAlias).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemType> RemoveAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = SystemTypeRules.Clean(alias);
        var (current, removed) = await WriteAsync(async (connection, transaction) =>
        {
            var found = CheckCurrent(await ReadAllAsync(connection, transaction, cancellationToken).ConfigureAwait(false), type);
            await using var delete = Command(connection, transaction, "DELETE FROM system_type_aliases WHERE system_type_id=@type AND alias_key=@key",
                ("@type", found.Id), ("@key", SystemTypeRules.Key(text)));
            var count = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (count > 0) await UpdateAsync(connection, transaction, found, "", cancellationToken).ConfigureAwait(false);
            return (found, count > 0);
        }, cancellationToken).ConfigureAwait(false);
        if (removed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.SystemType, current.Id.ToString(), current.Name,
                [new AuditChange("Denumire alternativă", text, "—")], string.Empty, cancellationToken, AuditActions.RemoveSystemTypeAlias).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SystemType> GetAsync(int id, CancellationToken token)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        return (await ReadAllAsync(connection, null, token).ConfigureAwait(false)).FirstOrDefault(type => type.Id == id)
               ?? throw new SystemTypeOperationException(SystemTypeRules.StaleMessage);
    }

    // The version read by the page must still be the stored one.
    private static SystemType CheckCurrent(IEnumerable<SystemType> all, SystemType original)
    {
        var current = all.FirstOrDefault(type => type.Id == original.Id);
        if (current is null || current.Version != original.Version) throw new SystemTypeOperationException(SystemTypeRules.StaleMessage);
        return current;
    }

    private static async Task UpdateAsync(MySqlConnection connection, MySqlTransaction transaction, SystemType type, string assignments, CancellationToken token,
        params (string Name, object Value)[] parameters)
    {
        var all = parameters.Concat([("@id", (object)type.Id), ("@version", (object)type.Version), ("@now", (object)MariaTimeText.Format(DateTime.UtcNow))]).ToArray();
        await using var update = Command(connection, transaction,
            $"UPDATE system_types SET {(assignments.Length > 0 ? assignments + "," : "")}version=version+1,updated_utc=@now WHERE id=@id AND version=@version", all);
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new SystemTypeOperationException(SystemTypeRules.StaleMessage);
    }

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new SystemTypeOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new SystemTypeOperationException(SystemTypeRules.DuplicateMessage);
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
