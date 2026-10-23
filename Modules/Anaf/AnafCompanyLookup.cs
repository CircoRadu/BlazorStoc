using BlazorStoc.Services;

namespace BlazorStoc.Modules.Anaf;

// The company lookup of the core (ICompanyDataLookup) answered by the ANAF service, behind its circuit breaker: the same lookup as the form of a beneficiary.
internal sealed class AnafCompanyLookup(IAnafService anaf) : ICompanyDataLookup
{
    public async Task<CompanyLookupResult> LookupAsync(string cui, CancellationToken cancellationToken = default)
    {
        var answer = await anaf.LookupCompanyAsync(cui, cancellationToken).ConfigureAwait(false);
        if (answer.Company is { } company) return new(company.Address, company.Phone);
        return new("", "", answer.Error ?? "fără răspuns", answer.Outcome == AnafLookupOutcome.Unavailable, answer.Outcome is AnafLookupOutcome.NotFound or AnafLookupOutcome.InvalidCui);
    }
}

// The lookup as another actor (the scheduled synchronization acts as the system): a new ANAF service with that access, behind the same circuit breaker.
internal sealed class AnafCompanyLookupFactory(AnafStore store, IHttpClientFactory httpClientFactory, IAuditTrail audit, ILoggerFactory loggers, ModuleRegistry modules) : ICompanyDataLookupFactory
{
    public ICompanyDataLookup For(IAccessControl actor) =>
        new AnafCompanyLookup(new GuardedAnafService(new AnafService(store, httpClientFactory, actor, audit, loggers.CreateLogger<AnafService>()), modules));
}

internal sealed class UnavailableCompanyLookupFactory : ICompanyDataLookupFactory
{
    public ICompanyDataLookup For(IAccessControl actor) => new UnavailableCompanyLookup();
}

// While the ANAF module is switched off or did not start.
internal sealed class UnavailableCompanyLookup : ICompanyDataLookup
{
    public Task<CompanyLookupResult> LookupAsync(string cui, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CompanyLookupResult("", "", "Modulul ANAF este oprit momentan.", true));
}
