using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The maintenance module: its guarded entry points (contracts, interventions, map configuration, notification reader) behind the circuit breaker, the registration, the
// stand-ins while it is out, the pages marked with the module, the order of the modules and the beneficiary file that shows its sections only while the module works.
public static class MaintenanceModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingContracts : IServiceContractRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<ServiceContractDetails>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ServiceContractDetails>>();
        public Task<IReadOnlyList<ServiceDueRow>> GetDueListAsync(bool includeOff, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ServiceDueRow>>();
        public Task<ServiceContractDetails> CreateAsync(int beneficiaryId, ServiceContractInput input, CancellationToken cancellationToken = default) => Fail<ServiceContractDetails>();
        public Task<ServiceContractDetails> UpdateAsync(ServiceContract original, ServiceContractInput input, CancellationToken cancellationToken = default) => Fail<ServiceContractDetails>();
        public Task<ServiceContractActivationPlan> PrepareActivationAsync(int contractId, CancellationToken cancellationToken = default) => Fail<ServiceContractActivationPlan>();
        public Task<ServiceContractDetails> ActivateAsync(ServiceContract original, IReadOnlyList<ServiceContractReschedule> reschedules, bool removeConflicting, CancellationToken cancellationToken = default) => Fail<ServiceContractDetails>();
        public Task<ServiceContractDetails> DeactivateAsync(ServiceContract original, CancellationToken cancellationToken = default) => Fail<ServiceContractDetails>();
        public Task DeleteAsync(ServiceContract original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
    }

    private sealed class ThrowingInterventions : IServiceInterventionRepository
    {
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("interventions"));
        public Task<IReadOnlyList<ServiceIntervention>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ServiceIntervention>>();
        public Task<ServiceInterventionPage> GetPageAsync(ServiceInterventionQuery query, CancellationToken cancellationToken = default) => Fail<ServiceInterventionPage>();
        public Task<ServiceInterventionSaved> RecordAsync(int beneficiaryId, ServiceInterventionInput input, CancellationToken cancellationToken = default) => Fail<ServiceInterventionSaved>();
        public Task<ServiceInterventionSaved> UpdateAsync(ServiceIntervention original, ServiceInterventionInput input, CancellationToken cancellationToken = default) => Fail<ServiceInterventionSaved>();
        public Task DeleteAsync(ServiceIntervention original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
    }

    private sealed class ThrowingMapConfiguration : IMapConfigurationService
    {
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("map"));
        public Task<MapView> GetViewAsync(CancellationToken cancellationToken = default) => Fail<MapView>();
        public Task<MapSettingsState> GetStateAsync(CancellationToken cancellationToken = default) => Fail<MapSettingsState>();
        public Task SaveEngineAsync(MapEngineSettings input, long expectedVersion, CancellationToken cancellationToken = default) => Fail<object>();
        public Task ResetEngineAsync(long expectedVersion, CancellationToken cancellationToken = default) => Fail<object>();
        public Task<MapPinType> SavePinTypeAsync(MapPinType input, long expectedVersion, CancellationToken cancellationToken = default) => Fail<MapPinType>();
        public Task DeletePinTypeAsync(int id, string reason, long expectedVersion, CancellationToken cancellationToken = default) => Fail<object>();
    }

    private sealed class ThrowingReader : IMaintenanceNotificationReader
    {
        public Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("reader");
        public Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("reader");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await GuardedAsync(check);
        await WiringAsync(check);
        Architecture(check);
    }

    private static async Task<int> RefusedAsync(params Func<Task>[] calls)
    {
        var refused = 0;
        foreach (var call in calls)
        {
            try { await call(); } catch (ModuleUnavailableException) { refused++; }
        }
        return refused;
    }

    private static async Task GuardedAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new MaintenanceModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingContracts();
        var contracts = new GuardedServiceContractRepository(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await contracts.GetForBeneficiaryAsync(1); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(MaintenanceModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Maintenance: repeated faults of the contract repository open the circuit of the module");
        var refused = await RefusedAsync(() => contracts.GetForBeneficiaryAsync(1), () => contracts.GetDueListAsync(true), () => contracts.CreateAsync(1, null!), () => contracts.UpdateAsync(null!, null!),
            () => contracts.PrepareActivationAsync(1), () => contracts.ActivateAsync(null!, [], false), () => contracts.DeactivateAsync(null!), () => contracts.DeleteAsync(null!, "x"));
        check(refused == 8 && inner.Calls == ModuleRegistry.FailureThreshold, "Maintenance: with the circuit open all eight contract entry points refuse at once and the repository is not called");
        var interventions = new GuardedServiceInterventionRepository(new ThrowingInterventions(), registry);
        check(await RefusedAsync(() => interventions.GetForBeneficiaryAsync(1), () => interventions.GetPageAsync(null!), () => interventions.RecordAsync(1, null!),
                () => interventions.UpdateAsync(null!, null!), () => interventions.DeleteAsync(null!, "x")) == 5,
            "Maintenance: the five intervention entry points share the circuit and refuse at once");
        var map = new GuardedMapConfigurationService(new ThrowingMapConfiguration(), registry);
        check(await RefusedAsync(() => map.GetViewAsync(), () => map.GetStateAsync(), () => map.SaveEngineAsync(null!, 0), () => map.ResetEngineAsync(0),
                () => map.SavePinTypeAsync(null!, 0), () => map.DeletePinTypeAsync(1, "x", 0)) == 6,
            "Maintenance: the six map configuration entry points share the circuit and refuse at once");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await contracts.GetForBeneficiaryAsync(1);
        check(registry.StatusOf(MaintenanceModule.ModuleId) == ModuleStatus.Healthy, "Maintenance: after the cooldown the first successful call brings the module back");
        inner.Failure = new ServiceContractOperationException("changed by someone else");
        for (var i = 0; i < 10; i++) { try { await contracts.UpdateAsync(null!, null!); } catch (ServiceContractOperationException) { } }
        inner.Failure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await contracts.DeleteAsync(null!, "x"); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(MaintenanceModule.ModuleId) == ModuleStatus.Healthy, "Maintenance: validation conflicts and access denied reach the user unchanged and never disable the module");

        var reader = new GuardedMaintenanceNotificationReader(new ThrowingReader(), registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) { try { await reader.GetDueAsync(); } catch (InvalidOperationException) { } }
        check(registry.StatusOf(MaintenanceModule.ModuleId) == ModuleStatus.Disabled, "Maintenance: the failures of the notification reader count for the module too");
        var sourcesRefused = 0;
        try { await new MaintenanceDueSource(reader).GetInstancesAsync(); } catch (ModuleUnavailableException) { sourcesRefused++; }
        try { await new ContractExpirySource(reader).GetInstancesAsync(); } catch (ModuleUnavailableException) { sourcesRefused++; }
        check(sourcesRefused == 2, "Maintenance: the two notification sources of a module with an open circuit fail at once (the engine keeps the notifications it has)");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new MaintenanceModule(), services, Empty());
        registry.AddNotificationSources(services);
        check(new[] { typeof(IServiceContractRepository), typeof(IServiceInterventionRepository), typeof(IMaintenanceNotificationReader), typeof(IMapConfigurationService) }
                .All(type => services.Any(d => d.ServiceType == type)) && services.Count(d => d.ServiceType == typeof(IExpirySource)) == 2,
            "Maintenance: the module registers contracts, interventions, the notification reader, the map configuration and its two notification sources");
        var module = new MaintenanceModule();
        check(module.Id == "mentenanta" && module.Nav.Count == 1 && module.Nav[0].Href == "/mentenanta" && module.Nav[0].PermissionModule == "mentenanta",
            "Maintenance: the module keeps the address and the permission module of the page (/mentenanta, mentenanta.view)");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes()
            .Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "ServiceMaintenance" or "MaintenanceMap" or "MaintenanceMapWindow").ToList();
        check(pages.Count == 3 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Maintenance: the three pages (register, map, map window) are marked with the module, so the router replaces them while it is out");
        var backupWaiting = typeof(ModuleAttribute).Assembly.GetTypes().Single(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name == "Maintenance");
        check(backupWaiting.GetCustomAttributes(typeof(ModuleAttribute), false).Length == 0, "Maintenance: the waiting page of a backup (/intretinere) is not part of the module");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:mentenanta:Enabled"] = "false" }).Build();
        offRegistry.Add(new MaintenanceModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var contracts = scope.ServiceProvider.GetService<IServiceContractRepository>();
            var interventions = scope.ServiceProvider.GetService<IServiceInterventionRepository>();
            var map = scope.ServiceProvider.GetService<IMapConfigurationService>();
            var refused = 0;
            try { if (contracts is not null) await contracts.GetForBeneficiaryAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (interventions is not null) await interventions.GetForBeneficiaryAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (map is not null) await map.GetViewAsync(); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("mentenanta") == ModuleStatus.Disabled && refused == 3,
                "Maintenance: with the module switched off the three services still resolve (the beneficiary file and the settings page that inject them are created) and every call says the module is out");
            check(await offRegistry.TryRunAsync("mentenanta", () => contracts!.GetForBeneficiaryAsync(1), (IReadOnlyList<ServiceContractDetails>)[]) is { Count: 0 },
                "Maintenance: the beneficiary file reading the contracts through the registry gets the empty list while the module is switched off");
        }

        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("mentenanta") == ModuleStatus.Healthy, "Maintenance: the application registers the maintenance module (Startup/ServiceRegistration)");
        check(application.Snapshot().Select(item => item.Id).Where(id => id is "vehicule" or "mentenanta" or "analize-risc").SequenceEqual(["vehicule", "mentenanta", "analize-risc"]),
            "Maintenance: the modules keep the order of the notification sources and of the menu entries (vehicles, maintenance, risk analyses)");
    }

    // The sources of the module live in Modules/Maintenance; the beneficiary file shows the contract and intervention sections only inside the gate of the module.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Maintenance architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Maintenance")), "Architecture: the maintenance module lives in Modules/Maintenance");
        var strays = Directory.GetFiles(Path.Combine(root, "Services"), "*.cs").Select(Path.GetFileName)
            .Where(name => name is not null && Regex.IsMatch(name, "^(Maria)?(ServiceContract|ServiceIntervention|MaintenanceMap|MaintenanceNotification|MapConfiguration|MapOptions)")).ToList();
        check(strays.Count == 0, "Architecture: the sources of the maintenance are not left in Services (" + string.Join("; ", strays) + ")");
        var beneficiary = File.ReadAllText(Path.Combine(root, "Components", "Pages", "BeneficiaryDetail.razor"));
        check(Regex.IsMatch(beneficiary, @"<ModuleGate ModuleId=""@BlazorStoc\.Modules\.Maintenance\.MaintenanceModule\.ModuleId""><ServiceContractsSection[^\n]*\n\s*\n\s*<ServiceInterventionsSection[^\n]*</ModuleGate>")
                && beneficiary.Contains("TryRunAsync(BlazorStoc.Modules.Maintenance.MaintenanceModule.ModuleId"),
            "Maintenance: the beneficiary file shows the contract and intervention sections only inside the module gate and reads the contracts through TryRunAsync");
        var settings = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Settings.razor"));
        check(Regex.IsMatch(settings, @"<ModuleGate[^>]*MaintenanceModule[^>]*><MapEngineEditor />") && Regex.IsMatch(settings, @"<ModuleGate[^>]*MaintenanceModule[^>]*><MapPinTypesEditor"),
            "Maintenance: the map tabs of the settings page are inside the gate of the module");
    }
}
