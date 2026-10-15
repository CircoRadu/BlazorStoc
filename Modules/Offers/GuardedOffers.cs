using BlazorStoc.Services;

namespace BlazorStoc.Modules.Offers;

// The entry points of the offers module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails
// at once with ModuleUnavailableException. The answers of the module (OfferException, access denied) are not faults.
internal sealed class GuardedOfferRepository(IOfferRepository inner, ModuleRegistry modules) : IOfferRepository
{
    private const string Id = OffersModule.ModuleId;

    public Task<OfferRecord?> GetLatestAsync(string number, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetLatestAsync(number, cancellationToken));

    public Task<IReadOnlyList<OfferLineRecord>> GetLinesAsync(int offerId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetLinesAsync(offerId, cancellationToken));

    public Task<IReadOnlyList<OfferRecord>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForProjectAsync(projectId, cancellationToken));

    public Task<OfferDiff?> DiffAsync(string number, IReadOnlyList<OfferImportLine> lines, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DiffAsync(number, lines, cancellationToken));

    public Task<IReadOnlyDictionary<string, int>> GetRememberedMatchesAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetRememberedMatchesAsync(cancellationToken));

    public Task<int?> FindBeneficiaryByAliasAsync(string text, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.FindBeneficiaryByAliasAsync(text, cancellationToken));

    public Task<OfferImportResult> ImportAsync(OfferImportRequest request, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.ImportAsync(request, cancellationToken));
}

internal sealed class GuardedOfferTemplateRepository(IOfferTemplateRepository inner, ModuleRegistry modules) : IOfferTemplateRepository
{
    private const string Id = OffersModule.ModuleId;

    public Task<IReadOnlyList<OfferTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAllAsync(cancellationToken));

    public Task<OfferTemplateRecord> CreateAsync(string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateAsync(name, definition, cancellationToken));

    public Task<OfferTemplateRecord> SaveAsync(OfferTemplateRecord original, string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SaveAsync(original, name, definition, cancellationToken));

    public Task<OfferTemplateRecord> SetActiveAsync(OfferTemplateRecord original, bool active, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SetActiveAsync(original, active, cancellationToken));

    public Task DeleteAsync(OfferTemplateRecord original, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, cancellationToken));
}

// The stand-ins of the repositories while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableOfferRepository : IOfferRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(OffersModule.ModuleId));

    public Task<OfferRecord?> GetLatestAsync(string number, CancellationToken cancellationToken = default) => Out<OfferRecord?>();
    public Task<IReadOnlyList<OfferLineRecord>> GetLinesAsync(int offerId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<OfferLineRecord>>();
    public Task<IReadOnlyList<OfferRecord>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<OfferRecord>>();
    public Task<OfferDiff?> DiffAsync(string number, IReadOnlyList<OfferImportLine> lines, CancellationToken cancellationToken = default) => Out<OfferDiff?>();
    public Task<IReadOnlyDictionary<string, int>> GetRememberedMatchesAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyDictionary<string, int>>();
    public Task<int?> FindBeneficiaryByAliasAsync(string text, CancellationToken cancellationToken = default) => Out<int?>();
    public Task<OfferImportResult> ImportAsync(OfferImportRequest request, CancellationToken cancellationToken = default) => Out<OfferImportResult>();
}

internal sealed class UnavailableOfferTemplateRepository : IOfferTemplateRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(OffersModule.ModuleId));

    public Task<IReadOnlyList<OfferTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<OfferTemplateRecord>>();
    public Task<OfferTemplateRecord> CreateAsync(string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) => Out<OfferTemplateRecord>();
    public Task<OfferTemplateRecord> SaveAsync(OfferTemplateRecord original, string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) => Out<OfferTemplateRecord>();
    public Task<OfferTemplateRecord> SetActiveAsync(OfferTemplateRecord original, bool active, CancellationToken cancellationToken = default) => Out<OfferTemplateRecord>();
    public Task DeleteAsync(OfferTemplateRecord original, CancellationToken cancellationToken = default) => Out<object>();
}
