using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;

// A maintenance contract needs a reference date and at least one unit to work (otherwise it is frozen); the tests that only need a contract use this complete input.
internal static class ContractTestUnits
{
    public const string ComponentName = "Test componenta contract";
    public static readonly DateOnly Reference = new(2025, 9, 1);

    /// <summary>A discrete component of the nomenclator for the tests (created once, never deleted: the nomenclator only deactivates).</summary>
    public static async Task<ServiceContractUnitInput> DiscreteAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, string name = ComponentName)
    {
        var repository = new MariaMaintenanceComponentRepository(configuration, admin, audit);
        var catalog = await repository.GetCatalogAsync();
        var component = catalog.Components.FirstOrDefault(item => item.Name == name && item.IsDiscrete) ?? await repository.CreateComponentAsync(name, null);
        if (!component.Active) component = await repository.SetComponentActiveAsync(component, true);
        return new ServiceContractUnitInput { ComponentId = component.Id };
    }

    public static ServiceContractInput Complete(this ServiceContractInput input, ServiceContractUnitInput unit, DateOnly? reference = null)
    {
        input.ReferenceDate ??= reference ?? Reference;
        input.Units = [unit];
        return input;
    }
}
