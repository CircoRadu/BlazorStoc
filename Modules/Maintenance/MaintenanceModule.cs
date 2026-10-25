using BlazorStoc.Services;

namespace BlazorStoc.Modules.Maintenance;

// The maintenance module: contracts and their coverage, the interventions (maintenance and on demand), the map of the work points with its engine and pin settings
// and the two notification sources (maintenance due, contract expiry). The work points and their photos stay with the beneficiary file (the file, the risk analyses
// and the contracts all use them); the module reads them through the same database.
public sealed class MaintenanceModule : IModule
{
    public const string ModuleId = "mentenanta";

    public string Id => ModuleId;
    public string Name => "Mentenanță";
    public Version Version { get; } = new(1, 0, 1);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [new("/mentenanta", "Mentenanță", "⚒", ModuleId)];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IServiceContractRepository>(provider => new GuardedServiceContractRepository(
            ActivatorUtilities.CreateInstance<MariaServiceContractRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IServiceInterventionRepository>(provider => new GuardedServiceInterventionRepository(
            ActivatorUtilities.CreateInstance<MariaServiceInterventionRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IOnDemandInterventionRecorder>(provider => new OnDemandInterventionRecorder(provider.GetRequiredService<IServiceInterventionRepository>()));
        services.AddScoped<IOnDemandInterventionRecorderFactory, OnDemandInterventionRecorderFactory>();
        services.AddScoped<IMaintenanceNotificationReader>(provider => new GuardedMaintenanceNotificationReader(
            ActivatorUtilities.CreateInstance<MariaMaintenanceNotificationReader>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.Configure<MapOptions>(configuration.GetSection(MapOptions.SectionName));
        services.AddSingleton<MapConfigStore>();
        services.AddScoped<IMapConfigurationService>(provider => new GuardedMapConfigurationService(
            ActivatorUtilities.CreateInstance<MapConfigurationService>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: the beneficiary file and the settings page still inject the contracts and the map configuration.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IServiceContractRepository, UnavailableServiceContractRepository>();
        services.AddScoped<IServiceInterventionRepository, UnavailableServiceInterventionRepository>();
        services.AddScoped<IOnDemandInterventionRecorder, UnavailableOnDemandInterventionRecorder>();
        services.AddScoped<IOnDemandInterventionRecorderFactory, UnavailableOnDemandInterventionRecorderFactory>();
        services.AddScoped<IMapConfigurationService, UnavailableMapConfigurationService>();
    }

    public void RegisterNotificationSources(IServiceCollection services)
    {
        services.AddScoped<IExpirySource>(provider => new MaintenanceDueSource(provider.GetRequiredService<IMaintenanceNotificationReader>()));
        services.AddScoped<IExpirySource>(provider => new ContractExpirySource(provider.GetRequiredService<IMaintenanceNotificationReader>()));
    }
}
