using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// The partners new in ActivityApp taken as beneficiaries when a page with a beneficiary choice opens (see IBeneficiarySyncOnOpen). It runs as the system, in its own scope, with its own
// time limit, and only when the import and the taking of the partners are on. A run reads the table of partners once; so that many pages opened in a row do not repeat it, a run that
// finished less than MinimumGap ago is not repeated and a call during a run waits for that run. A failure is logged as one line and never reaches the page.
public sealed class PartnerSyncOnOpen(IServiceScopeFactory scopes, ModuleRegistry modules, ILogger<PartnerSyncOnOpen> logger, TimeProvider? clock = null) : IBeneficiarySyncOnOpen
{
    public static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(2);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object sync = new();
    private Task<bool>? running;
    private DateTimeOffset lastFinished = DateTimeOffset.MinValue;

    public Task<bool> RequestAsync()
    {
        if (!modules.IsAvailable(ActivityImportModule.ModuleId)) return Task.FromResult(false);
        lock (sync)
        {
            if (running is { IsCompleted: false }) return running;
            if (time.GetUtcNow() - lastFinished < MinimumGap) return Task.FromResult(false);
            running = Task.Run(RunAsync);
            return running;
        }
    }

    private async Task<bool> RunAsync()
    {
        try
        {
            using var limit = new CancellationTokenSource(RunLimit);
            using var scope = scopes.CreateScope();
            var (import, partners) = ActivityImportSystem.CreatePartnerServices(scope.ServiceProvider);
            var settings = await import.GetSettingsAsync(limit.Token).ConfigureAwait(false);
            if (!settings.Enabled || !settings.TakePartners) return false;
            var report = await partners.ApplyNewAsync(limit.Token).ConfigureAwait(false);
            return report is not null && report.Partners.Create > 0;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Taking the new ActivityApp partners on page open failed ({ErrorType}).", exception.GetType().Name);
            return false;
        }
        finally { lock (sync) lastFinished = time.GetUtcNow(); }
    }
}
