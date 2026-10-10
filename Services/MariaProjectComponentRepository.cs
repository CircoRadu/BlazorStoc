using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB mode: `project_components` (migration 23). Changes need a beneficiary operator (like the projects); each has its own journal action,
// recorded under the project, and is checked against the version read.
public sealed class MariaProjectComponentRepository(
    IConfiguration configuration,
    IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null,
    IStockMovementRepository? movements = null) : IProjectComponentRepository
{

    private const string Select = """
        SELECT c.id,c.project_id,c.system_type_id,t.name,c.state,c.archived_utc,c.archive_reason,c.version
        FROM project_components c INNER JOIN system_types t ON t.id=c.system_type_id
        """;

    public async Task<IReadOnlyList<ProjectComponent>> GetForProjectAsync(int projectId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(connection, null, $"{Select} WHERE c.project_id=@project {(includeArchived ? "" : "AND c.archived_utc IS NULL")} ORDER BY t.sort_order,t.id",
            cancellationToken, ("@project", projectId)).ConfigureAwait(false);
    }

    private static async Task<List<ProjectComponent>> ReadAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql, CancellationToken token,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new List<ProjectComponent>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new ProjectComponent(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), reader.GetString(3),
                (ComponentState)Convert.ToInt32(reader.GetValue(4)), !reader.IsDBNull(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(7)));
        return result;
    }

    public async Task<IReadOnlyList<ProjectComponent>> AddAsync(int projectId, IReadOnlyCollection<int> systemTypeIds, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var ids = systemTypeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var (added, projectName) = await WriteAsync(async (connection, transaction) =>
        {
            string? name;
            await using (var project = Command(connection, transaction, "SELECT name FROM projects WHERE id=@id FOR UPDATE", ("@id", projectId)))
                name = await project.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (name is null) throw new ProjectComponentException("Proiectul nu mai există.");
            var existing = await ReadAsync(connection, transaction, $"{Select} WHERE c.project_id=@project", cancellationToken, ("@project", projectId)).ConfigureAwait(false);
            var result = new List<int>();
            foreach (var typeId in ids)
            {
                var current = existing.FirstOrDefault(item => item.SystemTypeId == typeId);
                if (current is not null) throw new ProjectComponentException(current.Archived ? ProjectComponentRules.ArchivedExistsMessage : ProjectComponentRules.AlreadyExistsMessage);
                await using (var active = Command(connection, transaction, "SELECT active FROM system_types WHERE id=@id", ("@id", typeId)))
                    if (await active.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not { } flag || Convert.ToInt32(flag) != 1)
                        throw new ProjectComponentException(ProjectComponentRules.UnknownTypeMessage);
                var now = MariaTimeText.Format(DateTime.UtcNow);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO project_components(project_id,system_type_id,state,version,created_utc,updated_utc) VALUES(@project,@type,@state,0,@now,@now)
                    """, ("@project", projectId), ("@type", typeId), ("@state", (int)ComponentState.Offered), ("@now", now));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                result.Add(checked((int)insert.LastInsertedId));
            }
            return (result, name);
        }, cancellationToken).ConfigureAwait(false);
        var components = new List<ProjectComponent>();
        foreach (var id in added)
        {
            var component = await GetAsync(id, cancellationToken).ConfigureAwait(false);
            components.Add(component);
            await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, AuditActions.AddProjectComponent, projectId.ToString(), projectName,
                AuditDetails.Identification(("Proiect", projectName), ("Componentă", component.SystemTypeName), ("Stare", ProjectComponentRules.StateLabel(component.State))),
                string.Empty, cancellationToken).ConfigureAwait(false);
        }
        return components;
    }

    public async Task<ProjectComponent> SetStateAsync(ProjectComponent component, ComponentState state, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (!Enum.IsDefined(state)) throw new ProjectComponentException("Starea aleasă nu există.");
        var (current, changed) = await ChangeAsync(component, found =>
        {
            if (found.Archived) throw new ProjectComponentException(ProjectComponentRules.ArchivedStateMessage);
            return found.State == state ? null : ("state=@state", [("@state", (object)(int)state)]);
        }, cancellationToken).ConfigureAwait(false);
        if (changed)
            await RecordAsync(current, AuditActions.ChangeProjectComponentState, [new AuditChange("Stare", ProjectComponentRules.StateLabel(current.State), ProjectComponentRules.StateLabel(state))],
                string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectComponent> ArchiveAsync(ProjectComponent component, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (ChangeReasonRules.ValidationError(reason) is { } problem) throw new ProjectComponentException(problem);
        reason = TextNormalization.ForStorage(reason);
        if (await GetPendingExitsAsync(component.Id, cancellationToken).ConfigureAwait(false) is { Count: > 0 } pending) throw new ProjectComponentHasExitsException(pending);
        var (current, _) = await ChangeAsync(component, found => found.Archived
            ? throw new ProjectComponentException(ProjectComponentRules.AlreadyArchivedMessage)
            : ("archived_utc=@when,archive_reason=@reason", [("@when", (object)MariaTimeText.Format(DateTime.UtcNow)), ("@reason", reason)]), cancellationToken).ConfigureAwait(false);
        await ReleaseReservationsAsync(current, cancellationToken).ConfigureAwait(false);
        await RecordAsync(current, AuditActions.ArchiveProjectComponent, [new AuditChange("Componentă", "Activă", "Arhivată")], reason, cancellationToken).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    private const string RemainingSql = "e.quantity-COALESCE((SELECT SUM(r.quantity) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL),0)";

    public async Task<IReadOnlyList<ComponentChoice>> GetChoicesAsync(int projectId, int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var withProduct = new HashSet<int>();
        await using (var command = Command(connection, null, ComponentExitSql.ComponentsWithProduct, ("@product", productId), ("@project", projectId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) withProduct.Add(checked((int)reader.GetInt64(0)));
        var all = await ReadAsync(connection, null, $"{Select} WHERE c.project_id=@project AND c.archived_utc IS NULL ORDER BY t.sort_order,t.id", cancellationToken, ("@project", projectId)).ConfigureAwait(false);
        return [.. all.Select(item => new ComponentChoice(item.Id, item.SystemTypeId, item.SystemTypeName, withProduct.Contains(item.Id))).OrderByDescending(item => item.InOffer)];
    }

    public async Task<IReadOnlyList<ComponentExit>> GetPendingExitsAsync(int componentId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"""
            SELECT e.id,e.product_id,p.name,e.movement_date,e.reference,e.quantity,{RemainingSql} AS remaining FROM stock_movements e INNER JOIN products p ON p.id=e.product_id
            WHERE e.kind=0 AND e.voided_utc IS NULL AND e.component_settled IS NULL AND e.destination=@destination AND e.project_component_id=@component
            HAVING remaining>0 ORDER BY p.name,e.id
            """, ("@component", componentId), ("@destination", (int)ExitDestination.Beneficiary));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ComponentExit>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ComponentExit(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), StockMovementRules.ParseStorageDate(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4), Convert.ToInt32(reader.GetValue(5)), Convert.ToInt32(reader.GetValue(6))));
        return result;
    }

    public async Task ResolveExitsAsync(ProjectComponent component, IReadOnlyList<ComponentExitResolution> resolutions, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        if (resolutions.Count == 0) return;
        var pending = await GetPendingExitsAsync(component.Id, cancellationToken).ConfigureAwait(false);
        var choices = (await GetChoicesAsync(component.ProjectId, 0, cancellationToken).ConfigureAwait(false)).ToDictionary(item => item.ComponentId);
        // Everything is checked before the first change.
        foreach (var resolution in resolutions)
        {
            if (pending.All(item => item.MovementId != resolution.MovementId) || !Enum.IsDefined(resolution.Action)) throw new ProjectComponentException(ProjectComponentRules.ExitNotPendingMessage);
            if (resolution.Action == ComponentExitAction.MoveToComponent && (resolution.TargetComponentId is not { } target || target == component.Id || !choices.ContainsKey(target)))
                throw new ProjectComponentException(ProjectComponentRules.TargetInvalidMessage);
        }
        foreach (var resolution in resolutions)
        {
            var exit = pending.Single(item => item.MovementId == resolution.MovementId);
            string action, after;
            switch (resolution.Action)
            {
                case ComponentExitAction.ReturnToWarehouse:
                    if (movements is null) throw new ProjectComponentException("Returul în depozit nu este disponibil.");
                    await movements.CreateAsync(exit.ProductId, new StockMovementInput
                    {
                        Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Now), Quantity = exit.Remaining, FreeType = FreeEntryType.FromBeneficiary,
                        Description = $"Retur la scoaterea componentei {component.SystemTypeName}", ReturnOfMovementId = exit.MovementId
                    }, cancellationToken).ConfigureAwait(false);
                    action = AuditActions.ComponentExitReturned; after = $"Retur în depozit ({exit.Remaining} buc.)";
                    break;
                case ComponentExitAction.LeftAtBeneficiary:
                    await ExecuteAsync("UPDATE stock_movements SET component_settled=1 WHERE id=@id", [("@id", exit.MovementId)], cancellationToken).ConfigureAwait(false);
                    action = AuditActions.ComponentExitLeft; after = $"Rămas la beneficiar ({exit.Remaining} buc.)";
                    break;
                case ComponentExitAction.Consumed:
                    await ExecuteAsync("UPDATE stock_movements SET component_settled=2 WHERE id=@id", [("@id", exit.MovementId)], cancellationToken).ConfigureAwait(false);
                    action = AuditActions.ComponentExitConsumed; after = $"Consumat ({exit.Remaining} buc.)";
                    break;
                default:
                    var targetId = resolution.TargetComponentId!.Value;
                    await ExecuteAsync("UPDATE stock_movements SET project_component_id=@target,outside_offer=0 WHERE id=@id", [("@target", targetId), ("@id", exit.MovementId)], cancellationToken).ConfigureAwait(false);
                    action = AuditActions.ComponentExitMoved; after = $"Mutată pe componenta {choices[targetId].Name}";
                    break;
            }
            await RecordAsync(component, action, [new AuditChange($"{exit.ProductName} (ieșirea #{exit.MovementId})", $"Pe componenta {component.SystemTypeName}", after)], string.Empty, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(string sql, (string Name, object? Value)[] parameters, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        await using var command = Command(connection, null, sql, parameters);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OfferComponent>> GetComponentsWithProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT DISTINCT pc.id,pc.project_id,p.name,t.name FROM project_components pc
            INNER JOIN projects p ON p.id=pc.project_id INNER JOIN system_types t ON t.id=pc.system_type_id
            INNER JOIN offers o ON o.project_id=pc.project_id AND o.system_type_id=pc.system_type_id
            INNER JOIN offer_lines l ON l.offer_id=o.id AND l.product_id=@product AND l.in_stock=1
            WHERE pc.archived_utc IS NULL AND o.revision=(SELECT MAX(o2.revision) FROM offers o2 WHERE o2.number_key=o.number_key)
            ORDER BY p.name,t.name
            """, ("@product", productId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<OfferComponent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new OfferComponent(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3)));
        return result;
    }

    public async Task<IReadOnlyDictionary<(int SystemTypeId, int ProductId), int>> GetReceivedAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT pc.system_type_id,m.product_id,SUM(m.quantity) FROM stock_movements m INNER JOIN project_components pc ON pc.id=m.project_component_id
            WHERE m.kind=1 AND m.voided_utc IS NULL AND pc.project_id=@project AND pc.archived_utc IS NULL GROUP BY pc.system_type_id,m.product_id
            """, ("@project", projectId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<(int, int), int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)))] = Convert.ToInt32(reader.GetValue(2));
        return result;
    }

    public async Task<IReadOnlyList<ComponentNet>> GetNetByComponentAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT e.project_component_id,pc.system_type_id,e.outside_offer,e.product_id,p.name,
                   SUM(e.quantity-COALESCE((SELECT SUM(r.quantity) FROM stock_movements r WHERE r.return_of_movement_id=e.id AND r.voided_utc IS NULL),0))
            FROM stock_movements e INNER JOIN products p ON p.id=e.product_id LEFT JOIN project_components pc ON pc.id=e.project_component_id
            WHERE e.kind=0 AND e.voided_utc IS NULL AND e.destination=@destination AND e.project_id=@project
            GROUP BY e.project_component_id,pc.system_type_id,e.outside_offer,e.product_id,p.name ORDER BY p.name,e.product_id
            """, ("@project", projectId), ("@destination", (int)ExitDestination.Beneficiary));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ComponentNet>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ComponentNet(reader.IsDBNull(0) ? null : checked((int)reader.GetInt64(0)), reader.IsDBNull(1) ? null : checked((int)reader.GetInt64(1)), Convert.ToInt32(reader.GetValue(2)) != 0,
                checked((int)reader.GetInt64(3)), reader.GetString(4), Convert.ToInt32(reader.GetValue(5))));
        return result;
    }

    // Taking a component out releases the reservations held for it (one journal event each).
    private async Task ReleaseReservationsAsync(ProjectComponent component, CancellationToken token)
    {
        var released = new List<(int Id, string Product, int Quantity)>();
        await using (var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false))
        {
            await using (var select = Command(connection, null, "SELECT r.id,p.name,r.quantity FROM project_reservations r INNER JOIN products p ON p.id=r.product_id WHERE r.project_component_id=@id", ("@id", component.Id)))
            await using (var reader = await select.ExecuteReaderAsync(token).ConfigureAwait(false))
                while (await reader.ReadAsync(token).ConfigureAwait(false)) released.Add((checked((int)reader.GetInt64(0)), reader.GetString(1), Convert.ToInt32(reader.GetValue(2))));
            if (released.Count == 0) return;
            await using var delete = Command(connection, null, "DELETE FROM project_reservations WHERE project_component_id=@id", ("@id", component.Id));
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        foreach (var item in released)
            await RecordAsync(component, AuditActions.ReservationReleasedByComponent, [new AuditChange(item.Product, $"{item.Quantity} buc. rezervate", "Eliberat")], string.Empty, token).ConfigureAwait(false);
    }

    public async Task<ProjectComponent> ReactivateAsync(ProjectComponent component, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var (current, _) = await ChangeAsync(component, found => found.Archived
            ? ("archived_utc=NULL,archive_reason=NULL", [])
            : throw new ProjectComponentException(ProjectComponentRules.NotArchivedMessage), cancellationToken).ConfigureAwait(false);
        await RecordAsync(current, AuditActions.ReactivateProjectComponent, [new AuditChange("Componentă", "Arhivată", "Activă")], string.Empty, cancellationToken).ConfigureAwait(false);
        return await GetAsync(current.Id, cancellationToken).ConfigureAwait(false);
    }

    // Applies one change under the version read; the callback returns the assignments (null = nothing to change) or refuses.
    private async Task<(ProjectComponent Current, bool Changed)> ChangeAsync(ProjectComponent original,
        Func<ProjectComponent, (string Assignments, (string Name, object? Value)[] Parameters)?> decide, CancellationToken token) =>
        await WriteAsync(async (connection, transaction) =>
        {
            var found = (await ReadAsync(connection, transaction, $"{Select} WHERE c.id=@id FOR UPDATE", token, ("@id", original.Id)).ConfigureAwait(false)).FirstOrDefault();
            if (found is null || found.Version != original.Version) throw new ProjectComponentException(ProjectComponentRules.StaleMessage);
            if (decide(found) is not { } change) return (found, false);
            var parameters = change.Parameters.Concat([("@id", (object)found.Id), ("@now", (object)MariaTimeText.Format(DateTime.UtcNow))]).ToArray();
            await using var update = Command(connection, transaction, $"UPDATE project_components SET {change.Assignments},version=version+1,updated_utc=@now WHERE id=@id", parameters);
            await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return (found, true);
        }, token).ConfigureAwait(false);

    private async Task RecordAsync(ProjectComponent component, string action, AuditChange[] changes, string reason, CancellationToken token)
    {
        string name;
        await using (var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false))
        {
            await using var command = Command(connection, null, "SELECT name FROM projects WHERE id=@id", ("@id", component.ProjectId));
            name = await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string ?? $"#{component.ProjectId}";
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Project, action, component.ProjectId.ToString(), name,
            AuditDetails.Identification(("Proiect", name), ("Componentă", component.SystemTypeName)) + "; " + AuditDetails.Changes(changes), reason, token).ConfigureAwait(false);
    }

    private async Task<ProjectComponent> GetAsync(int id, CancellationToken token)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        return (await ReadAsync(connection, null, $"{Select} WHERE c.id=@id", token, ("@id", id)).ConfigureAwait(false)).FirstOrDefault()
               ?? throw new ProjectComponentException(ProjectComponentRules.StaleMessage);
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ProjectComponentException(message),
            exception => exception.Number == 1062 ? new ProjectComponentException(ProjectComponentRules.AlreadyExistsMessage) : null, token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");

    private Task EnsureOperatorAsync(CancellationToken token) => accessControl?.EnsureBeneficiaryOperatorAsync(token) ?? Task.CompletedTask;
}
