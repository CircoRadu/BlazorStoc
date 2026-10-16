using BlazorStoc.Services;

namespace BlazorStoc.Modules.Backup;

// The backup as a module: no pages and no menu entry of its own (its screens are the Backup tab of Settings), only the services the pages and the daily schedule
// use. Backup:Mode = Worker (default) runs every backup, restoration and NAS operation in a worker process; InProcess runs the same classes inside the web application
// as before (a way back, and what the in-memory checks use). The lock, the settings of the schedule and of the share, the retention and the schedule itself stay in
// the application; the schedule asks this module for the services it uses, so it too works through the worker.
public sealed class BackupModule : IModule
{
    public string Id => BackupWorkerRules.ModuleId;
    public string Name => "Copii de siguranta si restaurare";
    public Version Version { get; } = new(1, 0, 0);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (string.Equals(configuration["Backup:Mode"], "InProcess", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<IBackupOperations, InProcessBackupOperations>();
        else
        {
            services.AddSingleton(provider => new BackupWorkerRunner(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<ILoggerFactory>().CreateLogger("BlazorStoc.Backup")));
            services.AddSingleton<IBackupOperations>(provider => new OutOfProcessBackupOperations(provider.GetRequiredService<BackupWorkerRunner>(), provider.GetRequiredService<ModuleRegistry>(),
                provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<ILoggerFactory>()));
        }
        AddScopedServices(services);
    }

    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddSingleton<IBackupOperations, UnavailableBackupOperations>();
        AddScopedServices(services);
    }

    // What the pages inject: the services of the signed-in user.
    private static void AddScopedServices(IServiceCollection services)
    {
        services.AddScoped(provider => provider.GetRequiredService<IBackupOperations>().Backups(provider.GetRequiredService<IAccessControl>()));
        services.AddScoped(provider => provider.GetRequiredService<IBackupOperations>().Copier(provider.GetRequiredService<IAccessControl>()));
        services.AddScoped(provider => provider.GetRequiredService<IBackupOperations>().Restore(provider.GetRequiredService<IAccessControl>()));
    }
}
