namespace BlazorStoc.Services;

// What the maintenance module asks of the ticket import without depending on it: to record, through the recorder it gives, the interventions of the tickets the operators decided while the
// maintenance module was out (or since its last run). The import keeps the decisions (activity_ticket_outcomes); the maintenance module only runs this at its own pace. The processor does not
// need the import module to be running: it is plain database code, so the pending outcomes are recorded as soon as the maintenance module runs again.
public interface ITicketOutcomeProcessor
{
    /// <summary>Records the pending outcomes in the order of the dates performed; returns how many were recorded. Stops (leaving the rest pending) while the maintenance module is out.</summary>
    Task<int> ApplyPendingAsync(IOnDemandInterventionRecorder recorder, CancellationToken cancellationToken = default);
}
