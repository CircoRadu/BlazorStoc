namespace BlazorStoc.Services;

/// <summary>
/// What a lookup of a company by its CUI gave: the address and the phone it holds (empty when it holds none), or why it could not answer (Failure; Unavailable when the source itself is
/// out, so that a run does not keep asking; NotFound when the source answered and does not know the CUI).
/// </summary>
public sealed record CompanyLookupResult(string Address, string Phone, string? Failure = null, bool Unavailable = false, bool NotFound = false);

/// <summary>
/// The lookup of the data of a company (today ANAF, provided by the ANAF module). The modules that need it (the import from ActivityApp) ask through this interface and do not know the
/// module behind it; while that module is out the answer is "unavailable".
/// </summary>
public interface ICompanyDataLookup
{
    Task<CompanyLookupResult> LookupAsync(string cui, CancellationToken cancellationToken = default);
}

/// <summary>The lookup acting as a given user: the system itself (the scheduled synchronization of the ActivityApp import), which has no signed-in user.</summary>
public interface ICompanyDataLookupFactory
{
    ICompanyDataLookup For(IAccessControl actor);
}
