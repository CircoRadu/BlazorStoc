using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Nomenclator: componente de mentenanta (familii, componente, denumiri alternative) ------------------------------------------
    private static async Task MaintenanceComponentsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var suffix = Suffix();
        var repository = new MariaMaintenanceComponentRepository(configuration, admin, audit);
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.EntityType == AuditEntities.MaintenanceComponent && item.Action == action);
        try
        {
            var security = await repository.CreateFamilyAsync($"Securitate fizica {suffix}");
            var burglar = await repository.CreateComponentAsync($"Efractie {suffix}", security.Id);
            var cctv = await repository.CreateComponentAsync($"TVCI {suffix}", security.Id);
            var fire = await repository.CreateComponentAsync($"Incendiu {suffix}", null);
            var catalog = await repository.GetCatalogAsync();
            Check(catalog.Families.Any(family => family.Id == security.Id) && catalog.MembersOf(security.Id).Select(item => item.Name).SequenceEqual([burglar.Name, cctv.Name]) && catalog.Discrete.Any(item => item.Id == fire.Id),
                "Maintenance components: a family with its components in order and a discrete component");
            Check(await Events(AuditActions.AddMaintenanceFamily) == 1 && await Events(AuditActions.AddMaintenanceComponent) == 3, "Maintenance components: additions are journaled with their own actions");

            // Names are one namespace: families, components and alternative names; case, diacritics and separators do not count.
            await Rejects<MaintenanceComponentOperationException>(() => repository.CreateComponentAsync($"SECURITATE-FIZICA {suffix}", null), "A component cannot take the name of a family");
            await Rejects<MaintenanceComponentOperationException>(() => repository.CreateFamilyAsync($"tvci {suffix}"), "A family cannot take the name of a component");
            await Rejects<MaintenanceComponentOperationException>(() => repository.CreateComponentAsync(" ", null), "An empty name is refused");
            cctv = await repository.AddAliasAsync(cctv, $"CCTV {suffix}");
            await Rejects<MaintenanceComponentOperationException>(() => repository.CreateComponentAsync($"cctv-{suffix}", null), "An alternative name cannot be reused as a name");
            await Rejects<MaintenanceComponentOperationException>(() => repository.AddAliasAsync(burglar, $"CCTV {suffix}"), "An alternative name cannot be given twice");
            Check(MaintenanceComponentRules.Find(await repository.GetCatalogAsync(), $"cctv {suffix}")?.Id == cctv.Id && await Events(AuditActions.AddMaintenanceComponentAlias) == 1, "Maintenance components: a component is found by its alternative name (ticket text) and the alias is journaled");

            // Rename, active, order.
            var renamed = await repository.RenameComponentAsync(burglar, $"Antiefractie {suffix}");
            await Rejects<MaintenanceComponentOperationException>(() => repository.RenameComponentAsync(burglar, $"Altceva {suffix}"), "A concurrent change of a component (outdated version) is refused");
            var inactive = await repository.SetComponentActiveAsync(renamed, false);
            Check(renamed.Name.StartsWith("Antiefractie", StringComparison.Ordinal) && !inactive.Active && await Events(AuditActions.RenameMaintenanceComponent) == 1 && await Events(AuditActions.DeactivateMaintenanceComponent) == 1,
                "Maintenance components: rename and activation are journaled with their own actions");
            await repository.MoveComponentAsync(inactive, 1);
            Check((await repository.GetCatalogAsync()).MembersOf(security.Id).Select(item => item.Id).SequenceEqual([cctv.Id, burglar.Id]), "Maintenance components: a component moves down within its family");

            // Family change: to another family (end of the list) and to discrete; an inactive family is refused.
            var other = await repository.CreateFamilyAsync($"Aparare {suffix}");
            var moved = await repository.ChangeFamilyAsync((await repository.GetCatalogAsync()).Components.First(item => item.Id == fire.Id), other.Id);
            Check(moved.FamilyId == other.Id && await Events(AuditActions.ChangeMaintenanceComponentFamily) == 1, "Maintenance components: a component goes into a family and the change is journaled");
            var backToDiscrete = await repository.ChangeFamilyAsync(moved, null);
            Check(backToDiscrete.IsDiscrete, "Maintenance components: a component becomes discrete again");
            var inactiveFamily = await repository.SetFamilyActiveAsync(other, false);
            await Rejects<MaintenanceComponentOperationException>(() => repository.ChangeFamilyAsync(backToDiscrete, inactiveFamily.Id), "A component cannot be put in an inactive family");
            await Rejects<MaintenanceComponentOperationException>(() => repository.CreateComponentAsync($"Nou {suffix}", inactiveFamily.Id), "A new component cannot be created in an inactive family");
            await repository.MoveFamilyAsync((await repository.GetCatalogAsync()).Families.First(item => item.Id == other.Id), -1);
            Check(await Events(AuditActions.DeactivateMaintenanceFamily) == 1 && await Events(AuditActions.MoveMaintenanceFamily) == 1, "Maintenance components: family activation and order are journaled with their own actions");

            // Only the administrator changes the lists; an operator reads them.
            var operatorRepository = new MariaMaintenanceComponentRepository(configuration, new TestAccessControl(false, "operator"), audit);
            Check((await operatorRepository.GetCatalogAsync()).Components.Any(item => item.Id == cctv.Id), "Maintenance components: an operator reads the lists");
            await Rejects<AccessDeniedException>(() => operatorRepository.CreateFamilyAsync($"Interzis {suffix}"), "An operator cannot add a family");
            await Rejects<AccessDeniedException>(() => operatorRepository.AddAliasAsync(cctv, $"Interzis {suffix}"), "An operator cannot add an alternative name");
        }
        finally
        {
            await using var cleanup = new MySqlCommand("DELETE FROM maintenance_component_aliases WHERE alias LIKE @like; DELETE FROM maintenance_components WHERE name LIKE @like; DELETE FROM maintenance_families WHERE name LIKE @like", probe);
            cleanup.Parameters.AddWithValue("@like", $"% {suffix}");
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
