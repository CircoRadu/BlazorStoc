using BlazorStoc.Services;

namespace BlazorStoc.Modules.Maintenance;

// The maintenance register as seen by the rest of the application (IOnDemandInterventionRecorder): an on-demand intervention, or a maintenance one, recorded through the register itself,
// behind the circuit breaker of the module. The refusals of the register become the neutral exception of the contract.
internal sealed class OnDemandInterventionRecorder(IServiceInterventionRepository interventions) : IOnDemandInterventionRecorder
{
    public Task<int> RecordAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default) =>
        RecordAsync(ServiceInterventionKind.OnDemand, beneficiaryId, workPointId, performedOn, notes, cancellationToken);

    // The next due date is counted from the date performed; a visit older than the latest maintenance of the point moves nothing (the register decides).
    public Task<int> RecordMaintenanceAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default) =>
        RecordAsync(ServiceInterventionKind.Maintenance, beneficiaryId, workPointId, performedOn, notes, cancellationToken);

    public Task<bool> IsCoveredAsync(int beneficiaryId, int workPointId, CancellationToken cancellationToken = default) =>
        interventions.IsCoveredAsync(beneficiaryId, workPointId, cancellationToken);

    public async Task DeleteAsync(int beneficiaryId, int interventionId, string reason, CancellationToken cancellationToken = default)
    {
        var original = (await interventions.GetForBeneficiaryAsync(beneficiaryId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == interventionId);
        if (original is null) return;
        try { await interventions.DeleteAsync(original, reason, cancellationToken).ConfigureAwait(false); }
        catch (ServiceInterventionOperationException exception) { throw new OnDemandInterventionOperationException(exception.Message); }
    }

    private async Task<int> RecordAsync(ServiceInterventionKind kind, int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await interventions.RecordAsync(beneficiaryId, new ServiceInterventionInput
            {
                Kind = kind, WorkPointId = workPointId, PerformedOn = performedOn, Notes = notes, Basis = ServiceNextDueBasis.FromPerformed
            }, cancellationToken).ConfigureAwait(false);
            return saved.Intervention.Id;
        }
        catch (ServiceInterventionOperationException exception) { throw new OnDemandInterventionOperationException(exception.Message); }
    }
}

internal sealed class UnavailableOnDemandInterventionRecorder : IOnDemandInterventionRecorder
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(MaintenanceModule.ModuleId));
    public Task<int> RecordAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default) => Out<int>();
    public Task<int> RecordMaintenanceAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default) => Out<int>();
    public Task<bool> IsCoveredAsync(int beneficiaryId, int workPointId, CancellationToken cancellationToken = default) => Out<bool>();
    public Task DeleteAsync(int beneficiaryId, int interventionId, string reason, CancellationToken cancellationToken = default) => Task.FromException(new ModuleUnavailableException(MaintenanceModule.ModuleId));
}

internal sealed class OnDemandInterventionRecorderFactory(IConfiguration configuration, IAuditTrail audit, ModuleRegistry modules) : IOnDemandInterventionRecorderFactory
{
    public IOnDemandInterventionRecorder For(IAccessControl actor) =>
        new OnDemandInterventionRecorder(new GuardedServiceInterventionRepository(new MariaServiceInterventionRepository(configuration, actor, audit), modules));
}

internal sealed class UnavailableOnDemandInterventionRecorderFactory : IOnDemandInterventionRecorderFactory
{
    public IOnDemandInterventionRecorder For(IAccessControl actor) => new UnavailableOnDemandInterventionRecorder();
}
