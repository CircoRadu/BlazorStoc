using BlazorStoc.Services;

namespace BlazorStoc.Modules.Vehicles;

// The vehicles module: the register of vehicles (plate, expiries of ITP, insurance and road tax), the equipment of each vehicle with its target levels and the three
// expiry notification sources. The exits and transfers of stock stay in the stock module, which reads the vehicles only through the circuit breaker (TryRunAsync).
public sealed class VehiclesModule : IModule
{
    public const string ModuleId = "vehicule";

    public string Id => ModuleId;
    public string Name => "Vehicule";
    public Version Version { get; } = new(1, 0, 1);
    // The entry sits in the Administration menu (MainLayout), which is shown only while the module works; no item of its own here.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IVehicleRepository>(provider => new GuardedVehicleRepository(
            ActivatorUtilities.CreateInstance<MariaVehicleRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IVehicleTargetRepository>(provider => new GuardedVehicleTargetRepository(
            ActivatorUtilities.CreateInstance<MariaVehicleTargetRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: the stock pages still inject the repositories.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IVehicleRepository, UnavailableVehicleRepository>();
        services.AddScoped<IVehicleTargetRepository, UnavailableVehicleTargetRepository>();
    }

    public void RegisterNotificationSources(IServiceCollection services)
    {
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Insurance, ExpirySourceKeys.VehicleInsurance, "Asigurare"));
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Rovinieta, ExpirySourceKeys.VehicleRovinieta, "Rovinietă"));
    }
}
