using BlazorStoc.Services;

namespace BlazorStoc.Modules.RiskAnalyses;

// The entry points of the risk analyses module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is
// open a call fails at once with ModuleUnavailableException. The answers of the module (its operation exception, access denied) are not faults.
internal sealed class GuardedRiskAnalysisRepository(IRiskAnalysisRepository inner, ModuleRegistry modules) : IRiskAnalysisRepository
{
    private const string Id = RiskAnalysesModule.ModuleId;

    public Task<IReadOnlyList<RiskAnalysisView>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForBeneficiaryAsync(beneficiaryId, cancellationToken));

    public Task<IReadOnlyList<RiskAnalysisView>> GetAllAsync(bool includeOff, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAllAsync(includeOff, cancellationToken));

    public Task<RiskAnalysisView> CreateAsync(int beneficiaryId, RiskAnalysisInput input, bool deactivateExisting, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateAsync(beneficiaryId, input, deactivateExisting, cancellationToken));

    public Task<RiskAnalysisView> UpdateAsync(RiskAnalysis original, RiskAnalysisInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken));

    public Task<RiskAnalysisView> RenewAsync(RiskAnalysis original, RiskAnalysisRenewalInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.RenewAsync(original, input, cancellationToken));

    public Task<RiskAnalysisView> UndoLastRenewalAsync(RiskAnalysis original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.UndoLastRenewalAsync(original, cancellationToken));

    public Task<RiskAnalysisView> ActivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.ActivateAsync(original, cancellationToken));

    public Task<RiskAnalysisView> DeactivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeactivateAsync(original, cancellationToken));

    public Task DeleteAsync(RiskAnalysis original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

// The notification engine already treats an exception of a source as "unknown" (it keeps the notifications it has); through the circuit breaker
// the failures of the source are counted for the module too, and an open circuit spares the database the repeated reads.
internal sealed class GuardedRiskAnalysisNotificationReader(IRiskAnalysisNotificationReader inner, ModuleRegistry modules) : IRiskAnalysisNotificationReader
{
    public Task<IReadOnlyList<RiskAnalysisExpiryItem>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(RiskAnalysesModule.ModuleId, () => inner.GetActiveAsync(cancellationToken));
}

// The stand-in of the repository while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableRiskAnalysisRepository : IRiskAnalysisRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(RiskAnalysesModule.ModuleId));

    public Task<IReadOnlyList<RiskAnalysisView>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<RiskAnalysisView>>();
    public Task<IReadOnlyList<RiskAnalysisView>> GetAllAsync(bool includeOff, CancellationToken cancellationToken = default) => Out<IReadOnlyList<RiskAnalysisView>>();
    public Task<RiskAnalysisView> CreateAsync(int beneficiaryId, RiskAnalysisInput input, bool deactivateExisting, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task<RiskAnalysisView> UpdateAsync(RiskAnalysis original, RiskAnalysisInput input, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task<RiskAnalysisView> RenewAsync(RiskAnalysis original, RiskAnalysisRenewalInput input, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task<RiskAnalysisView> UndoLastRenewalAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task<RiskAnalysisView> ActivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task<RiskAnalysisView> DeactivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Out<RiskAnalysisView>();
    public Task DeleteAsync(RiskAnalysis original, string reason, CancellationToken cancellationToken = default) => Out<object>();
}
