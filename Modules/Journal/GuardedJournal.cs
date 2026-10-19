using BlazorStoc.Services;

namespace BlazorStoc.Modules.Journal;

// The reading of the journal behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails at once
// with ModuleUnavailableException. Access denied is an answer, not a fault.
internal sealed class GuardedAuditReader(IAuditReader inner, ModuleRegistry modules) : IAuditReader
{
    private const string Id = JournalModule.ModuleId;

    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetEventsAsync(cancellationToken));

    public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.QueryAsync(query, page, pageSize, window, cancellationToken));

    public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SummaryAsync(todayStartUtc, cancellationToken));

    public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.RemovalTimesAsync(events, cancellationToken));
}

// The stand-in of the reader while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableAuditReader : IAuditReader
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(JournalModule.ModuleId));

    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<AuditEvent>>();
    public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => Out<AuditPage>();
    public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => Out<AuditSummary>();
    public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => Out<IReadOnlyDictionary<string, DateTime>>();
}
