using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Vehicles;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The vehicles module: its guarded entry points behind the circuit breaker, the registration, the stand-ins while it is out, the pages marked with the module
// and the stock pages that read the vehicles without failing when the module is out.
public static class VehicleModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingVehicles : IVehicleRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<Vehicle>>();
        public Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default) => Fail<Vehicle?>();
        public Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default) => Fail<Vehicle>();
        public Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default, string? auditAction = null) => Fail<Vehicle>();
        public Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
    }

    private sealed class ThrowingTargets : IVehicleTargetRepository
    {
        public Task<IReadOnlyList<VehicleTarget>> GetForVehicleAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("targets");
        public Task SetAsync(int vehicleId, int productId, int target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("targets");
        public Task RemoveAsync(int vehicleId, int productId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("targets");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await GuardedAsync(check);
        await WiringAsync(check);
        Architecture(check);
    }

    private static async Task GuardedAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new VehiclesModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingVehicles();
        var guarded = new GuardedVehicleRepository(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await guarded.GetVehiclesAsync(); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(VehiclesModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Vehicles: repeated faults of the repository open the circuit of the module");
        var unavailable = 0;
        foreach (var call in new Func<Task>[]
        {
            () => guarded.GetVehiclesAsync(), () => guarded.GetAsync(1), () => guarded.CreateAsync(new()), () => guarded.UpdateAsync(null!, new()), () => guarded.DeleteAsync(null!, "x")
        })
        {
            try { await call(); } catch (ModuleUnavailableException) { unavailable++; }
        }
        check(unavailable == 5 && inner.Calls == ModuleRegistry.FailureThreshold, "Vehicles: with the circuit open all five entry points refuse at once and the repository is not called");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await guarded.GetVehiclesAsync();
        check(registry.StatusOf(VehiclesModule.ModuleId) == ModuleStatus.Healthy, "Vehicles: after the cooldown the first successful call brings the module back");
        inner.Failure = new VehicleOperationException("plate already used");
        for (var i = 0; i < 10; i++) { try { await guarded.CreateAsync(new()); } catch (VehicleOperationException) { } }
        inner.Failure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await guarded.DeleteAsync(null!, "x"); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(VehiclesModule.ModuleId) == ModuleStatus.Healthy, "Vehicles: validation conflicts and access denied reach the user unchanged and never disable the module");

        var targets = new GuardedVehicleTargetRepository(new ThrowingTargets(), registry);
        var targetFaults = 0;
        foreach (var call in new Func<Task>[] { () => targets.GetForVehicleAsync(1), () => targets.SetAsync(1, 1, 1), () => targets.RemoveAsync(1, 1) })
        {
            try { await call(); } catch (InvalidOperationException) { targetFaults++; }
        }
        check(targetFaults == 3 && registry.StatusOf(VehiclesModule.ModuleId) == ModuleStatus.Disabled, "Vehicles: the faults of the target levels count for the module too");

        var sourceRefused = false;
        try { await new VehicleExpirySource(guarded, VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP").GetInstancesAsync(); } catch (ModuleUnavailableException) { sourceRefused = true; }
        check(sourceRefused, "Vehicles: an expiry source of a module with an open circuit fails at once (the engine keeps the notifications it has)");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new VehiclesModule(), services, Empty());
        registry.AddNotificationSources(services);
        check(services.Any(d => d.ServiceType == typeof(IVehicleRepository)) && services.Any(d => d.ServiceType == typeof(IVehicleTargetRepository))
            && services.Count(d => d.ServiceType == typeof(IExpirySource)) == 3, "Vehicles: the module registers the repository, the target levels and its three expiry sources (ITP, insurance, road tax)");
        var module = new VehiclesModule();
        check(module.Id == "vehicule" && module.Nav.Count == 0, "Vehicles: the module keeps the permission module (vehicule.view); its menu entry stays in Administration and is shown only while the module works");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes()
            .Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "Vehicles" or "VehiclePage" or "VehicleEquipmentPage").ToList();
        check(pages.Count == 3 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Vehicles: the three pages (list, vehicle, equipment) are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:vehicule:Enabled"] = "false" }).Build();
        offRegistry.Add(new VehiclesModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetService<IVehicleRepository>();
            var targets = scope.ServiceProvider.GetService<IVehicleTargetRepository>();
            var refused = 0;
            try { if (repository is not null) await repository.GetVehiclesAsync(); } catch (ModuleUnavailableException) { refused++; }
            try { if (targets is not null) await targets.GetForVehicleAsync(1); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("vehicule") == ModuleStatus.Disabled && refused == 2,
                "Vehicles: with the module switched off both repositories still resolve (the stock pages that inject them are created) and every call says the module is out");
            check(await offRegistry.TryRunAsync("vehicule", () => repository!.GetVehiclesAsync(), (IReadOnlyList<Vehicle>)[]) is { Count: 0 },
                "Vehicles: a stock page reading the vehicles through the registry gets the empty list while the module is switched off");
        }
        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("vehicule") == ModuleStatus.Healthy, "Vehicles: the application registers the vehicles module (Startup/ServiceRegistration)");
    }

    // The sources of the module live in Modules/Vehicles, and the stock pages never read the vehicles without the circuit breaker (TryRunAsync).
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Vehicles architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Vehicles")), "Architecture: the vehicles module lives in Modules/Vehicles");
        var strays = Directory.GetFiles(Path.Combine(root, "Services"), "*.cs").Select(Path.GetFileName)
            .Where(name => name is not null && Regex.IsMatch(name, "^(Maria)?Vehicle")).ToList();
        check(strays.Count == 0, "Architecture: the sources of the vehicles are not left in Services (" + string.Join("; ", strays) + ")");
        var unguarded = new[] { "Components/Pages/ExitOperation.razor", "Components/Pages/ProductMovements.Form.cs" }
            .Where(file => File.ReadAllText(Path.Combine(root, file)) is var text && (!text.Contains("TryRunAsync") || Regex.IsMatch(text, @"await\s+Vehicles?Repository\.GetVehiclesAsync"))).ToList();
        check(unguarded.Count == 0, "Vehicles: the stock pages read the vehicles through ModuleRegistry.TryRunAsync (" + string.Join(", ", unguarded) + ")");
    }
}
