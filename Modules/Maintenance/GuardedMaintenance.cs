using BlazorStoc.Services;

namespace BlazorStoc.Modules.Maintenance;

// The entry points of the maintenance module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call
// fails at once with ModuleUnavailableException. The answers of the module (its operation exceptions, access denied) are not faults.
internal sealed class GuardedServiceContractRepository(IServiceContractRepository inner, ModuleRegistry modules) : IServiceContractRepository
{
    private const string Id = MaintenanceModule.ModuleId;

    public Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForBeneficiaryAsync(beneficiaryId, cancellationToken));

    public Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetDueListAsync(includeOff, cancellationToken));

    public Task<ServiceContractDetails> CreateAsync(int beneficiaryId, ServiceContractInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateAsync(beneficiaryId, input, cancellationToken));

    public Task<ServiceContractDetails> UpdateAsync(ServiceContract original, ServiceContractInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken));

    public Task<ServiceContractActivationPlan> PrepareActivationAsync(int contractId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.PrepareActivationAsync(contractId, cancellationToken));

    public Task<ServiceContractDetails> ActivateAsync(ServiceContract original, IReadOnlyList<ServiceContractReschedule> reschedules, bool removeConflicting, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.ActivateAsync(original, reschedules, removeConflicting, cancellationToken));

    public Task<ServiceContractDetails> DeactivateAsync(ServiceContract original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeactivateAsync(original, cancellationToken));

    public Task DeleteAsync(ServiceContract original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

internal sealed class GuardedServiceInterventionRepository(IServiceInterventionRepository inner, ModuleRegistry modules) : IServiceInterventionRepository
{
    private const string Id = MaintenanceModule.ModuleId;

    public Task<IReadOnlyList<ServiceIntervention>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForBeneficiaryAsync(beneficiaryId, cancellationToken));

    public Task<ServiceInterventionPage> GetPageAsync(ServiceInterventionQuery query, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetPageAsync(query, cancellationToken));

    public Task<ServiceInterventionSaved> RecordAsync(int beneficiaryId, ServiceInterventionInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.RecordAsync(beneficiaryId, input, cancellationToken));

    public Task<ServiceInterventionSaved> UpdateAsync(ServiceIntervention original, ServiceInterventionInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken));

    public Task DeleteAsync(ServiceIntervention original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

// The notification engine treats an exception of a source as "unknown" (it keeps the notifications it has); through the circuit breaker the failures of the
// sources are counted for the module too, and an open circuit spares the database the repeated reads.
internal sealed class GuardedMaintenanceNotificationReader(IMaintenanceNotificationReader inner, ModuleRegistry modules) : IMaintenanceNotificationReader
{
    public Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(MaintenanceModule.ModuleId, () => inner.GetDueAsync(cancellationToken));

    public Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(MaintenanceModule.ModuleId, () => inner.GetContractExpiriesAsync(cancellationToken));
}

internal sealed class GuardedMapConfigurationService(IMapConfigurationService inner, ModuleRegistry modules) : IMapConfigurationService
{
    private const string Id = MaintenanceModule.ModuleId;

    public Task<MapView> GetViewAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetViewAsync(cancellationToken));

    public Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetStateAsync(cancellationToken));

    public Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SaveEngineAsync(input, expectedVersion, cancellationToken));

    public Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.ResetEngineAsync(expectedVersion, cancellationToken));

    public Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SavePinTypeAsync(input, expectedVersion, cancellationToken));

    public Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeletePinTypeAsync(id, reason, expectedVersion, cancellationToken));
}

// The stand-ins of the services while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableServiceContractRepository : IServiceContractRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(MaintenanceModule.ModuleId));

    public Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ServiceContractDetails>>();
    public Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ServiceDueRow>>();
    public Task<ServiceContractDetails> CreateAsync(int beneficiaryId, ServiceContractInput input, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task<ServiceContractDetails> UpdateAsync(ServiceContract original, ServiceContractInput input, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task<ServiceContractActivationPlan> PrepareActivationAsync(int contractId, CancellationToken cancellationToken = default) => Out<ServiceContractActivationPlan>();
    public Task<ServiceContractDetails> ActivateAsync(ServiceContract original, IReadOnlyList<ServiceContractReschedule> reschedules, bool removeConflicting, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task<ServiceContractDetails> DeactivateAsync(ServiceContract original, CancellationToken cancellationToken = default) => Out<ServiceContractDetails>();
    public Task DeleteAsync(ServiceContract original, string reason, CancellationToken cancellationToken = default) => Out<object>();
}

internal sealed class UnavailableServiceInterventionRepository : IServiceInterventionRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(MaintenanceModule.ModuleId));

    public Task<IReadOnlyList<ServiceIntervention>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ServiceIntervention>>();
    public Task<ServiceInterventionPage> GetPageAsync(ServiceInterventionQuery query, CancellationToken cancellationToken = default) => Out<ServiceInterventionPage>();
    public Task<ServiceInterventionSaved> RecordAsync(int beneficiaryId, ServiceInterventionInput input, CancellationToken cancellationToken = default) => Out<ServiceInterventionSaved>();
    public Task<ServiceInterventionSaved> UpdateAsync(ServiceIntervention original, ServiceInterventionInput input, CancellationToken cancellationToken = default) => Out<ServiceInterventionSaved>();
    public Task DeleteAsync(ServiceIntervention original, string reason, CancellationToken cancellationToken = default) => Out<object>();
}

internal sealed class UnavailableMapConfigurationService : IMapConfigurationService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(MaintenanceModule.ModuleId));

    public Task<MapView> GetViewAsync(CancellationToken cancellationToken = default) => Out<MapView>();
    public Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default) => Out<MapSettingsState>();
    public Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default) => Out<object>();
    public Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default) => Out<object>();
    public Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default) => Out<MapPinType>();
    public Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default) => Out<object>();
}
