using BlazorStoc.Services;
using BlazorStoc.Services.Update;

namespace BlazorStoc.Startup;

public static class UpdateRegistration
{
    /// <summary>
    /// Setari -> Actualizare (docs/PROPUNERE_ACTUALIZARE.md): the installation the application runs from, the coordinator of the one update that may wait or run (singleton, shared by every
    /// session), the journal of what the updater did, and the service of the page. Not a module: an update must stay possible when a module is out.
    /// </summary>
    public static IServiceCollection AddApplicationUpdate(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new UpdateInstallation(configuration));
        services.AddSingleton<IUpdateLauncher, ScheduledTaskLauncher>();
        services.AddSingleton(provider => new UpdateJournal(provider.GetRequiredService<UpdateInstallation>(), provider.GetRequiredService<IAuditTrail>(),
            UpdateJournal.DefaultStateFile(configuration), provider.GetRequiredService<ILogger<UpdateJournal>>()));
        services.AddSingleton<UpdateCoordinator>();
        services.AddHostedService<UpdateJournalService>();
        services.AddScoped(provider => new ApplicationUpdateService(provider.GetRequiredService<UpdateInstallation>(), provider.GetRequiredService<IUpdateLauncher>(),
            provider.GetRequiredService<UpdateCoordinator>(), provider.GetRequiredService<UpdateJournal>(), provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(),
            provider.GetRequiredService<TimeProvider>())
        {
            Notice = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Update:NoticeSeconds", 60), 0, 600))
        });
        return services;
    }
}
