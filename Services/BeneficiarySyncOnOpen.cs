namespace BlazorStoc.Services;

/// <summary>
/// Asks, in the background, for the partners new in the source application (ActivityApp) to be taken as beneficiaries now, so that a page with a beneficiary choice shows
/// a beneficiary entered there a moment ago without waiting for the scheduled synchronization. The call never blocks the page: it is made after the first render, runs at
/// most every couple of minutes (a call during a run joins it) and the page reloads its list only when something new was taken. The service exists only while the import module is registered.
/// </summary>
public interface IBeneficiarySyncOnOpen
{
    /// <summary>True when new beneficiaries were taken by this run; false when nothing is new, the run was skipped (too soon), or the import is off or failed.</summary>
    Task<bool> RequestAsync();
}

/// <summary>A stage of the taking of the partners made on request; Total is 0 while the amount of work is not known yet.</summary>
public sealed record PartnerTakeProgress(string Stage, int Done, int Total)
{
    public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Done * 100.0 / Total), 0, 100);
}

/// <summary>The outcome of the taking made on request: what was created, tied or left waiting, and the names of the new beneficiaries. Message explains a run that did not complete or had nothing to do.</summary>
public sealed record PartnerTakeResult(bool Completed, int Created, int Linked, int Waiting, int Problems, IReadOnlyList<string> NewNames, string? Message);

/// <summary>
/// The button "Preia din ActivityApp" of the pages with a beneficiary choice: takes the partners new in ActivityApp as beneficiaries now, with progress. It needs the right to add beneficiaries
/// (beneficiari.add); the work is done as the system, and the journal records the action under the user. It exists only while the import module is registered.
/// </summary>
public interface IBeneficiaryTakeService
{
    /// <summary>Whether the button is shown: the module runs, the import and the taking of the partners are on, and the user may add beneficiaries.</summary>
    Task<bool> CanTakeAsync(CancellationToken cancellationToken = default);
    Task<PartnerTakeResult> TakeAsync(IProgress<PartnerTakeProgress>? progress, CancellationToken cancellationToken = default);
}
