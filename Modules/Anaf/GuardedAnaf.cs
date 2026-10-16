using BlazorStoc.Services;

namespace BlazorStoc.Modules.Anaf;

// ANAF did not answer (timeout, network, an error status, an invalid answer): counted as a fault of the module by the circuit breaker.
internal sealed class AnafUnavailableException(string? reason) : Exception(reason ?? "ANAF nu a putut răspunde.");

// The entry points of the ANAF module behind the circuit breaker of the registry. The lookup of a company never throws (it answers "unavailable"), so its outcome is reported:
// "unavailable" is a fault, anything else (found, not found, invalid CUI) is a success. While the circuit is open the lookup answers "unavailable" at once, without calling ANAF.
// The configuration methods (the settings tab) use the plain guarded call: their own answers (AnafException, access denied) are not faults.
internal sealed class GuardedAnafService(IAnafService inner, ModuleRegistry modules) : IAnafService
{
    private const string Id = AnafModule.ModuleId;
    internal const string PausedMessage = "ANAF nu răspunde și interogările sunt oprite pentru scurt timp (modulul ANAF este oprit după erori repetate).";

    public async Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default)
    {
        if (!modules.IsAvailable(Id)) return new(null, PausedMessage, null, AnafLookupOutcome.Unavailable);
        var result = await inner.LookupCompanyAsync(cui, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == AnafLookupOutcome.Unavailable) modules.ReportFailure(Id, new AnafUnavailableException(result.Error));
        else modules.ReportSuccess(Id);
        return result;
    }

    public Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetStateAsync(cancellationToken));
    public Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.PreviewAsync(config, cui, cancellationToken));
    public Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.TestAsync(config, cui, cancellationToken));
    public Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveDraftAsync(config, cancellationToken));
    public Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ActivateAsync(config, cancellationToken));
    public Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RollbackAsync(cancellationToken));
    public Task<AnafState> ActivateVersionAsync(int versionId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ActivateVersionAsync(versionId, cancellationToken));
    public Task<AnafState> DeleteVersionAsync(int versionId, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteVersionAsync(versionId, reason, cancellationToken));
    public bool WasTestedSuccessfully(AnafConfig config) => inner.WasTestedSuccessfully(config);
}

// The stand-in of the service while the module is switched off or did not start: the lookup answers "unavailable" (what the editors already handle), the settings say the module is out.
internal sealed class UnavailableAnafService : IAnafService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(AnafModule.ModuleId));

    public Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AnafCompanyResult(null, "Modulul ANAF este oprit momentan.", null, AnafLookupOutcome.Unavailable));
    public Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default) => Out<AnafState>();
    public Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => Out<AnafRequest>();
    public Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => Out<AnafTestResult>();
    public Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default) => Out<object>();
    public Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default) => Out<AnafState>();
    public Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default) => Out<AnafState>();
    public Task<AnafState> ActivateVersionAsync(int versionId, CancellationToken cancellationToken = default) => Out<AnafState>();
    public Task<AnafState> DeleteVersionAsync(int versionId, string reason, CancellationToken cancellationToken = default) => Out<AnafState>();
    public bool WasTestedSuccessfully(AnafConfig config) => false;
}
