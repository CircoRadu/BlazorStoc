using BlazorStoc.Services;

namespace BlazorStoc.Modules.Maintenance;

// Records in the maintenance register the interventions of the tickets the operators decided (activity_ticket_outcomes, written by the ticket import): once a minute while the module runs, and at
// the first tick after it was switched on again, so the due dates follow the latest tickets taken while it was out. It acts as the system (no user). Nothing here can take the application
// down: anything unexpected is written to the log and the next minute tries again. The ticket import does not depend on this worker: it only leaves the outcomes here.
public sealed class TicketOutcomeWorker(IServiceScopeFactory scopes, ModuleRegistry modules, ILogger<TicketOutcomeWorker> logger, TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public TimeSpan Tick { get; init; } = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Tick, time);
            do { await CheckAsync(stoppingToken).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One pass; returns how many interventions were recorded (0 while the module is out or when nothing waits).</summary>
    public async Task<int> CheckAsync(CancellationToken cancellationToken)
    {
        if (!modules.IsAvailable(MaintenanceModule.ModuleId)) return 0;
        try
        {
            using var scope = scopes.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IOnDemandInterventionRecorderFactory>().For(new SystemAccess());
            return await scope.ServiceProvider.GetRequiredService<ITicketOutcomeProcessor>().ApplyPendingAsync(recorder, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (ModuleUnavailableException) { return 0; }
        catch (Exception exception)
        {
            logger.LogWarning("Recording the decided ActivityApp tickets in the maintenance register failed ({ErrorType}).", exception.GetType().Name);
            return 0;
        }
    }
}
