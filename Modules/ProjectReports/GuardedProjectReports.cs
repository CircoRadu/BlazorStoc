using BlazorStoc.Services;

namespace BlazorStoc.Modules.ProjectReports;

// The entry points of the project situation module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open
// a call fails at once with ModuleUnavailableException. The answers of the module (OfferException for an unknown project, access denied) are not faults, and neither
// is the offers module being out (its own ModuleUnavailableException).
internal sealed class GuardedProjectSituationReader(IProjectSituationReader inner, ModuleRegistry modules) : IProjectSituationReader
{
    private const string Id = ProjectReportsModule.ModuleId;

    public Task<ProjectSituation> GetAsync(int projectId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAsync(projectId, cancellationToken));

    public Task<IReadOnlyList<ReserveSuggestion>> GetReserveSuggestionsAsync(int productId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetReserveSuggestionsAsync(productId, cancellationToken));
}

internal sealed class GuardedConsumptionReader(IConsumptionReader inner, ModuleRegistry modules) : IConsumptionReader
{
    public Task<IReadOnlyList<ConsumptionRow>> GetAsync(ConsumptionQuery query, CancellationToken cancellationToken = default) =>
        modules.RunAsync(ProjectReportsModule.ModuleId, () => inner.GetAsync(query, cancellationToken));
}

// The stand-ins of the readers while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableProjectSituationReader : IProjectSituationReader
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ProjectReportsModule.ModuleId));

    public Task<ProjectSituation> GetAsync(int projectId, CancellationToken cancellationToken = default) => Out<ProjectSituation>();
    public Task<IReadOnlyList<ReserveSuggestion>> GetReserveSuggestionsAsync(int productId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<ReserveSuggestion>>();
}

internal sealed class UnavailableConsumptionReader : IConsumptionReader
{
    public Task<IReadOnlyList<ConsumptionRow>> GetAsync(ConsumptionQuery query, CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<ConsumptionRow>>(new ModuleUnavailableException(ProjectReportsModule.ModuleId));
}
