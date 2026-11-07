using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `maintenance_families`, `maintenance_components` and `maintenance_component_aliases` (migration 75). Reading needs a product operator, changing needs an administrator; every
// change is checked against the version read (a concurrent change is refused) and has its own journal action. The names of the families, the components and the alternative names
// are checked against each other under one transaction (the keys are also unique per table in the database).
public sealed class MariaMaintenanceComponentRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null) : IMaintenanceComponentRepository
{
    public async Task<MaintenanceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MaintenanceCatalog> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken token)
    {
        var aliases = new Dictionary<int, List<string>>();
        await using (var command = Command(connection, transaction, "SELECT component_id,alias FROM maintenance_component_aliases ORDER BY alias,id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                if (!aliases.TryGetValue(id, out var list)) aliases[id] = list = [];
                list.Add(reader.GetString(1));
            }
        var families = new List<MaintenanceFamily>();
        await using (var command = Command(connection, transaction, "SELECT id,name,active,sort_order,version FROM maintenance_families ORDER BY sort_order,id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                families.Add(new MaintenanceFamily(checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToBoolean(reader.GetValue(2)), Convert.ToInt32(reader.GetValue(3)), reader.GetInt64(4)));
        var components = new List<MaintenanceComponent>();
        await using (var command = Command(connection, transaction, "SELECT id,family_id,name,active,sort_order,version FROM maintenance_components ORDER BY sort_order,id"))
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var id = checked((int)reader.GetInt64(0));
                components.Add(new MaintenanceComponent(id, reader.IsDBNull(1) ? null : checked((int)reader.GetInt64(1)), reader.GetString(2), Convert.ToBoolean(reader.GetValue(3)),
                    Convert.ToInt32(reader.GetValue(4)), aliases.TryGetValue(id, out var list) ? list : [], reader.GetInt64(5)));
            }
        return new MaintenanceCatalog(families, components);
    }

    // ---- Families ----------------------------------------------------------------------------------------------------------------

    public async Task<MaintenanceFamily> CreateFamilyAsync(string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(name);
        var id = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (MaintenanceComponentRules.NameProblem(text, catalog) is { } problem) throw new MaintenanceComponentOperationException(problem);
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO maintenance_families(name,name_key,active,sort_order,version,created_utc,updated_utc) VALUES(@name,@key,1,@order,0,@now,@now)
                """, ("@name", text), ("@key", MaintenanceComponentRules.Key(text)), ("@order", catalog.Families.Count == 0 ? 1 : catalog.Families.Max(family => family.SortOrder) + 1), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)insert.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, AuditActions.AddMaintenanceFamily, id.ToString(), text,
            AuditDetails.Identification(("Familie", text)), string.Empty, cancellationToken).ConfigureAwait(false);
        return (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).Families.First(family => family.Id == id);
    }

    public async Task<MaintenanceFamily> RenameFamilyAsync(MaintenanceFamily original, string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(name);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var family = CheckFamily(catalog, original);
            if (text == family.Name) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.NoChangeMessage);
            if (MaintenanceComponentRules.NameProblem(text, catalog, ignoreFamilyId: family.Id) is { } problem) throw new MaintenanceComponentOperationException(problem);
            await UpdateAsync(connection, transaction, "maintenance_families", family.Id, family.Version, "name=@name,name_key=@key", cancellationToken,
                ("@name", text), ("@key", MaintenanceComponentRules.Key(text))).ConfigureAwait(false);
            return family;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), text,
            [new AuditChange("Denumire familie", current.Name, text)], string.Empty, cancellationToken, AuditActions.RenameMaintenanceFamily).ConfigureAwait(false);
        return (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).Families.First(family => family.Id == current.Id);
    }

    public async Task<MaintenanceFamily> SetFamilyActiveAsync(MaintenanceFamily original, bool active, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var family = CheckFamily(await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false), original);
            if (family.Active == active) return family;
            await UpdateAsync(connection, transaction, "maintenance_families", family.Id, family.Version, "active=@active", cancellationToken, ("@active", active ? 1 : 0)).ConfigureAwait(false);
            return family;
        }, cancellationToken).ConfigureAwait(false);
        if (current.Active != active)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), current.Name,
                [new AuditChange("Stare familie", current.Active ? "Activ" : "Inactiv", active ? "Activ" : "Inactiv")], string.Empty, cancellationToken,
                active ? AuditActions.ActivateMaintenanceFamily : AuditActions.DeactivateMaintenanceFamily).ConfigureAwait(false);
        return (await GetCatalogAsync(cancellationToken).ConfigureAwait(false)).Families.First(family => family.Id == current.Id);
    }

    public async Task MoveFamilyAsync(MaintenanceFamily original, int direction, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var moved = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var family = CheckFamily(catalog, original);
            var all = catalog.Families.ToList();
            var index = all.FindIndex(item => item.Id == family.Id);
            var neighbour = index + Math.Sign(direction);
            if (neighbour < 0 || neighbour >= all.Count) return (Family: family, Changed: false, From: 0, To: 0);
            var order = all.Select(item => item.Id).ToList();
            (order[index], order[neighbour]) = (order[neighbour], order[index]);
            await RenumberAsync(connection, transaction, "maintenance_families", order, cancellationToken).ConfigureAwait(false);
            return (Family: family, Changed: true, From: index + 1, To: neighbour + 1);
        }, cancellationToken).ConfigureAwait(false);
        if (moved.Changed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, moved.Family.Id.ToString(), moved.Family.Name,
                [new AuditChange("Poziție familie", moved.From.ToString(CultureInfo.InvariantCulture), moved.To.ToString(CultureInfo.InvariantCulture))], string.Empty, cancellationToken,
                AuditActions.MoveMaintenanceFamily).ConfigureAwait(false);
    }

    // ---- Components --------------------------------------------------------------------------------------------------------------

    public async Task<MaintenanceComponent> CreateComponentAsync(string name, int? familyId, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(name);
        var (id, familyName) = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var family = familyId is { } wanted ? catalog.Family(wanted) ?? throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage) : null;
            if (family is { Active: false }) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.InactiveFamilyMessage);
            if (MaintenanceComponentRules.NameProblem(text, catalog) is { } problem) throw new MaintenanceComponentOperationException(problem);
            var group = catalog.Components.Where(component => component.FamilyId == familyId).ToList();
            var now = MariaTimeText.Format(DateTime.UtcNow);
            await using var insert = Command(connection, transaction, """
                INSERT INTO maintenance_components(family_id,name,name_key,active,sort_order,version,created_utc,updated_utc) VALUES(@family,@name,@key,1,@order,0,@now,@now)
                """, ("@family", familyId), ("@name", text), ("@key", MaintenanceComponentRules.Key(text)), ("@order", group.Count == 0 ? 1 : group.Max(component => component.SortOrder) + 1), ("@now", now));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (checked((int)insert.LastInsertedId), family?.Name);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, AuditActions.AddMaintenanceComponent, id.ToString(), text,
            AuditDetails.Identification(("Componentă", text), ("Familie", familyName ?? "componentă discretă")), string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetComponentAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaintenanceComponent> RenameComponentAsync(MaintenanceComponent original, string name, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(name);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var component = CheckComponent(catalog, original);
            if (text == component.Name) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.NoChangeMessage);
            if (MaintenanceComponentRules.NameProblem(text, catalog, ignoreComponentId: component.Id) is { } problem) throw new MaintenanceComponentOperationException(problem);
            await UpdateAsync(connection, transaction, "maintenance_components", component.Id, component.Version, "name=@name,name_key=@key", cancellationToken,
                ("@name", text), ("@key", MaintenanceComponentRules.Key(text))).ConfigureAwait(false);
            return component;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), text,
            [new AuditChange("Denumire componentă", current.Name, text)], string.Empty, cancellationToken, AuditActions.RenameMaintenanceComponent).ConfigureAwait(false);
        return await GetComponentAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaintenanceComponent> SetComponentActiveAsync(MaintenanceComponent original, bool active, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var component = CheckComponent(await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false), original);
            if (component.Active == active) return component;
            await UpdateAsync(connection, transaction, "maintenance_components", component.Id, component.Version, "active=@active", cancellationToken, ("@active", active ? 1 : 0)).ConfigureAwait(false);
            return component;
        }, cancellationToken).ConfigureAwait(false);
        if (current.Active != active)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), current.Name,
                [new AuditChange("Stare componentă", current.Active ? "Activ" : "Inactiv", active ? "Activ" : "Inactiv")], string.Empty, cancellationToken,
                active ? AuditActions.ActivateMaintenanceComponent : AuditActions.DeactivateMaintenanceComponent).ConfigureAwait(false);
        return await GetComponentAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveComponentAsync(MaintenanceComponent original, int direction, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var moved = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var component = CheckComponent(catalog, original);
            var group = catalog.Components.Where(item => item.FamilyId == component.FamilyId).ToList();
            var index = group.FindIndex(item => item.Id == component.Id);
            var neighbour = index + Math.Sign(direction);
            if (neighbour < 0 || neighbour >= group.Count) return (Component: component, Changed: false, From: 0, To: 0);
            var order = group.Select(item => item.Id).ToList();
            (order[index], order[neighbour]) = (order[neighbour], order[index]);
            await RenumberAsync(connection, transaction, "maintenance_components", order, cancellationToken).ConfigureAwait(false);
            return (Component: component, Changed: true, From: index + 1, To: neighbour + 1);
        }, cancellationToken).ConfigureAwait(false);
        if (moved.Changed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, moved.Component.Id.ToString(), moved.Component.Name,
                [new AuditChange("Poziție componentă", moved.From.ToString(CultureInfo.InvariantCulture), moved.To.ToString(CultureInfo.InvariantCulture))], string.Empty, cancellationToken,
                AuditActions.MoveMaintenanceComponent).ConfigureAwait(false);
    }

    public async Task<MaintenanceComponent> ChangeFamilyAsync(MaintenanceComponent original, int? familyId, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var (current, before, after) = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var component = CheckComponent(catalog, original);
            if (component.FamilyId == familyId) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.SameFamilyMessage);
            var target = familyId is { } wanted ? catalog.Family(wanted) ?? throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage) : null;
            if (target is { Active: false }) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.InactiveFamilyMessage);
            var group = catalog.Components.Where(item => item.FamilyId == familyId).ToList();
            await UpdateAsync(connection, transaction, "maintenance_components", component.Id, component.Version, "family_id=@family,sort_order=@order", cancellationToken,
                ("@family", familyId), ("@order", group.Count == 0 ? 1 : group.Max(item => item.SortOrder) + 1)).ConfigureAwait(false);
            return (component, MaintenanceComponentRules.FamilyText(catalog, component), target?.Name ?? "Componentă discretă");
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), current.Name,
            [new AuditChange("Familie", before, after)], string.Empty, cancellationToken, AuditActions.ChangeMaintenanceComponentFamily).ConfigureAwait(false);
        return await GetComponentAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaintenanceComponent> AddAliasAsync(MaintenanceComponent component, string alias, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(alias);
        var current = await WriteAsync(async (connection, transaction) =>
        {
            var catalog = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var found = CheckComponent(catalog, component);
            if (MaintenanceComponentRules.AliasProblem(text, found, catalog) is { } problem) throw new MaintenanceComponentOperationException(problem);
            await using var insert = Command(connection, transaction,
                "INSERT INTO maintenance_component_aliases(component_id,alias,alias_key,created_utc) VALUES(@component,@alias,@key,@now)",
                ("@component", found.Id), ("@alias", text), ("@key", MaintenanceComponentRules.Key(text)), ("@now", MariaTimeText.Format(DateTime.UtcNow)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await UpdateAsync(connection, transaction, "maintenance_components", found.Id, found.Version, "", cancellationToken).ConfigureAwait(false);
            return found;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), current.Name,
            [new AuditChange("Denumire alternativă", "—", text)], string.Empty, cancellationToken, AuditActions.AddMaintenanceComponentAlias).ConfigureAwait(false);
        return await GetComponentAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MaintenanceComponent> RemoveAliasAsync(MaintenanceComponent component, string alias, CancellationToken cancellationToken = default)
    {
        await EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var text = MaintenanceComponentRules.Clean(alias);
        var (current, removed) = await WriteAsync(async (connection, transaction) =>
        {
            var found = CheckComponent(await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false), component);
            await using var delete = Command(connection, transaction, "DELETE FROM maintenance_component_aliases WHERE component_id=@component AND alias_key=@key",
                ("@component", found.Id), ("@key", MaintenanceComponentRules.Key(text)));
            var count = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (count > 0) await UpdateAsync(connection, transaction, "maintenance_components", found.Id, found.Version, "", cancellationToken).ConfigureAwait(false);
            return (found, count > 0);
        }, cancellationToken).ConfigureAwait(false);
        if (removed)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.MaintenanceComponent, current.Id.ToString(), current.Name,
                [new AuditChange("Denumire alternativă", text, "—")], string.Empty, cancellationToken, AuditActions.RemoveMaintenanceComponentAlias).ConfigureAwait(false);
        return await GetComponentAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------------------

    private async Task<MaintenanceComponent> GetComponentAsync(int id, CancellationToken token) =>
        (await GetCatalogAsync(token).ConfigureAwait(false)).Components.FirstOrDefault(component => component.Id == id) ?? throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage);

    private static MaintenanceFamily CheckFamily(MaintenanceCatalog catalog, MaintenanceFamily original)
    {
        var current = catalog.Families.FirstOrDefault(family => family.Id == original.Id);
        if (current is null || current.Version != original.Version) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage);
        return current;
    }

    private static MaintenanceComponent CheckComponent(MaintenanceCatalog catalog, MaintenanceComponent original)
    {
        var current = catalog.Components.FirstOrDefault(component => component.Id == original.Id);
        if (current is null || current.Version != original.Version) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage);
        return current;
    }

    // Orders are renumbered 1..n so equal or missing numbers never matter.
    private static async Task RenumberAsync(MySqlConnection connection, MySqlTransaction transaction, string table, IReadOnlyList<int> order, CancellationToken token)
    {
        for (var position = 0; position < order.Count; position++)
            await using (var update = Command(connection, transaction, $"UPDATE {table} SET sort_order=@order,version=version+1,updated_utc=@now WHERE id=@id",
                ("@order", position + 1), ("@id", order[position]), ("@now", MariaTimeText.Format(DateTime.UtcNow))))
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task UpdateAsync(MySqlConnection connection, MySqlTransaction transaction, string table, int id, long version, string assignments, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        var all = parameters.Concat([("@id", (object)id), ("@version", (object)version), ("@now", (object)MariaTimeText.Format(DateTime.UtcNow))]).ToArray();
        await using var update = Command(connection, transaction,
            $"UPDATE {table} SET {(assignments.Length > 0 ? assignments + "," : "")}version=version+1,updated_utc=@now WHERE id=@id AND version=@version", all);
        if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new MaintenanceComponentOperationException(MaintenanceComponentRules.StaleMessage);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new MaintenanceComponentOperationException(message),
            exception => exception.Number == 1062 ? new MaintenanceComponentOperationException(MaintenanceComponentRules.DuplicateMessage) : null, token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
    private Task EnsureAdministratorAsync(CancellationToken token) => accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask;
}
