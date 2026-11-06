using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorStoc.Modules.ActivityImport;

// The scheduled synchronization: once a minute the worker asks whether a synchronization is due (the import and the synchronization are on, the hour is in the window, the interval -
// longer after failures - has passed) and runs it as the system (no user). It only waits while the module is out. Nothing here can take the application down: a failure is remembered by
// the synchronization itself, anything unexpected is written to the log and the next minute tries again.
public sealed class ActivitySyncWorker(IServiceScopeFactory scopes, ModuleRegistry modules, ILogger<ActivitySyncWorker> logger, TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public TimeSpan Tick { get; init; } = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let the application finish starting (the migrations, the first requests) before the first check.
            await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Tick, time);
            do { await CheckAsync(stoppingToken).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One check; returns the result of the synchronization it ran, or null (nothing due, module out, or an unexpected failure that was logged).</summary>
    public async Task<SyncResult?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!modules.IsAvailable(ActivityImportModule.ModuleId)) return null;
        try
        {
            using var scope = scopes.CreateScope();
            var services = ActivityImportSystem.Create(scope.ServiceProvider);
            return await services.Sync.RunScheduledAsync(time.GetLocalNow().DateTime, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // The database may be busy (a backup), the module may be switched off between the check and the call: not worth more than a line, and the next minute.
            logger.LogWarning("ActivityApp synchronization check failed ({ErrorType}).", exception.GetType().Name);
            return null;
        }
    }
}

/// <summary>The services of the import built to act as the system (the scheduled synchronization has no signed-in user: its work is journaled under "sistem").</summary>
public sealed record ActivityImportSystem(IActivitySyncService Sync)
{
    public static ActivityImportSystem Create(IServiceProvider provider)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var audit = provider.GetRequiredService<IAuditTrail>();
        var source = provider.GetRequiredService<IActivitySource>();
        var system = new SystemAccess();
        var import = new MariaActivityImportService(configuration, provider.GetRequiredService<IDataProtectionProvider>(), source, system, audit);
        // The company lookup (ANAF) as the system too; without the module that provides it (not started) the companies are taken as not verified.
        var lookup = provider.GetService<ICompanyDataLookupFactory>()?.For(system);
        var partners = new MariaActivityPartnerImportService(configuration, import, source, new MariaBeneficiaryRepository(configuration, system, audit), new MariaWorkPointRepository(configuration, system, audit), system, audit, lookup);
        var tickets = new MariaActivityTicketImportService(configuration, import, source, new MariaWorkPointRepository(configuration, system, audit), system, audit, partners);
        return new ActivityImportSystem(new MariaActivitySyncService(configuration, import, source, partners, tickets, system, audit));
    }
}
