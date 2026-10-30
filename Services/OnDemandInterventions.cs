namespace BlazorStoc.Services;

// What other parts of the application (the ActivityApp ticket import) may ask of the maintenance register without depending on the maintenance module: to record an intervention
// "la cerere" (on demand). It takes any work point of the beneficiary and moves no due date. The register itself (rules, journal, photos) stays in the maintenance module, which
// provides the implementation; while that module is out the call throws ModuleUnavailableException.

public interface IOnDemandInterventionRecorder
{
    /// <summary>Records the intervention and returns its id. A refusal by the register (work point of another beneficiary, invalid date) is an <see cref="OnDemandInterventionOperationException"/>.</summary>
    Task<int> RecordAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default);
    /// <summary>Whether an active maintenance contract of the beneficiary covers the work point.</summary>
    Task<bool> IsCoveredAsync(int beneficiaryId, int workPointId, CancellationToken cancellationToken = default);
    /// <summary>Records a maintenance intervention (the next due date counted from the date performed, unless a more recent maintenance of the point exists) and returns its id. A point that no active contract covers is an <see cref="OnDemandInterventionOperationException"/>.</summary>
    Task<int> RecordMaintenanceAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default);
    /// <summary>Records a maintenance intervention even when no active contract covers the point (a contract that was not recorded yet): then it has no contract and moves no due date; with a contract it is the same as <see cref="RecordMaintenanceAsync"/>.</summary>
    Task<int> RecordMaintenanceUncoveredAsync(int beneficiaryId, int workPointId, DateOnly performedOn, string notes, CancellationToken cancellationToken = default);
    /// <summary>Deletes an intervention of the beneficiary (archived, with the reason). An intervention that does not exist any more is not an error.</summary>
    Task DeleteAsync(int beneficiaryId, int interventionId, string reason, CancellationToken cancellationToken = default);
}

public sealed class OnDemandInterventionOperationException(string message) : Exception(message);

public static class OnDemandInterventionRules
{
    /// <summary>The notes of an intervention have at most this many characters.</summary>
    public const int NotesMaximumLength = 2000;
    public const int FirstYear = 2000;

    public static string? DateError(DateOnly performedOn, DateOnly today) =>
        performedOn > today ? "Data efectuării nu poate fi în viitor." : performedOn.Year < FirstYear ? "Data efectuării nu este validă." : null;
}

/// <summary>The recorder acting as a given user: the system itself (the scheduled synchronization of the ActivityApp import), which has no signed-in user.</summary>
public interface IOnDemandInterventionRecorderFactory
{
    IOnDemandInterventionRecorder For(IAccessControl actor);
}
