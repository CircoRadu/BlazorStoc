using BlazorStoc.Modules;
using BlazorStoc.Modules.Anaf;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The ANAF module: the circuit breaker counts the "unavailable" answers of the lookup (ANAF never throws, it answers), an open circuit refuses at once without calling ANAF,
// the answers about the company (found, not found, invalid CUI) are not faults, the configuration methods are guarded like any module, and the stand-in lets the editors
// of beneficiaries and suppliers keep working while the module is out.
public static class AnafModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class FakeAnaf : IAnafService
    {
        public int Lookups;
        public AnafLookupOutcome Outcome = AnafLookupOutcome.Unavailable;
        public Exception? ConfigFailure = new InvalidOperationException("store down");
        public Task<AnafCompanyResult> LookupCompanyAsync(string cui, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(Outcome == AnafLookupOutcome.Found
                ? new AnafCompanyResult(new AnafCompany("1", "Firma", "Adresa", "J1/1/1", "", "", ""), null, null, AnafLookupOutcome.Found)
                : new AnafCompanyResult(null, Outcome == AnafLookupOutcome.Unavailable ? "timeout" : "nu exista", null, Outcome));
        }
        private Task<T> Config<T>() => ConfigFailure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(ConfigFailure);
        public Task<AnafState> GetStateAsync(CancellationToken cancellationToken = default) => Config<AnafState>();
        public Task<AnafRequest> PreviewAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => Config<AnafRequest>();
        public Task<AnafTestResult> TestAsync(AnafConfig config, string cui, CancellationToken cancellationToken = default) => Config<AnafTestResult>();
        public Task SaveDraftAsync(AnafConfig config, CancellationToken cancellationToken = default) => Config<object>();
        public Task<AnafState> ActivateAsync(AnafConfig config, CancellationToken cancellationToken = default) => Config<AnafState>();
        public Task<AnafState> RollbackAsync(CancellationToken cancellationToken = default) => Config<AnafState>();
        public Task<AnafState> ActivateVersionAsync(int versionId, CancellationToken cancellationToken = default) => Config<AnafState>();
        public Task<AnafState> DeleteVersionAsync(int versionId, string reason, CancellationToken cancellationToken = default) => Config<AnafState>();
        public bool WasTestedSuccessfully(AnafConfig config) => true;
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
        registry.Add(new AnafModule(), new ServiceCollection(), Empty());
        var inner = new FakeAnaf();
        var guarded = new GuardedAnafService(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) await guarded.LookupCompanyAsync("123");
        check(registry.StatusOf(AnafModule.ModuleId) == ModuleStatus.Disabled && inner.Lookups == ModuleRegistry.FailureThreshold,
            "ANAF: repeated \"unavailable\" answers open the circuit of the module although the lookup never throws");
        var paused = await guarded.LookupCompanyAsync("123");
        check(paused.Outcome == AnafLookupOutcome.Unavailable && paused.Error == GuardedAnafService.PausedMessage && inner.Lookups == ModuleRegistry.FailureThreshold,
            "ANAF: with the circuit open a lookup answers \"unavailable\" at once with an explanation and ANAF is not called (nobody waits for another timeout)");
        var refused = 0;
        foreach (var call in new Func<Task>[] { () => guarded.GetStateAsync(), () => guarded.PreviewAsync(null!, "1"), () => guarded.TestAsync(null!, "1"), () => guarded.SaveDraftAsync(null!),
                     () => guarded.ActivateAsync(null!), () => guarded.RollbackAsync(), () => guarded.ActivateVersionAsync(1), () => guarded.DeleteVersionAsync(1, "x") })
        {
            try { await call(); } catch (ModuleUnavailableException) { refused++; }
        }
        check(refused == 8, "ANAF: with the circuit open the eight configuration methods refuse at once");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Outcome = AnafLookupOutcome.Found;
        var found = await guarded.LookupCompanyAsync("123");
        check(found.Outcome == AnafLookupOutcome.Found && registry.StatusOf(AnafModule.ModuleId) == ModuleStatus.Healthy,
            "ANAF: after the cooldown the first answer from ANAF brings the module back");

        foreach (var outcome in new[] { AnafLookupOutcome.NotFound, AnafLookupOutcome.InvalidCui })
        {
            inner.Outcome = outcome;
            for (var i = 0; i < 10; i++) await guarded.LookupCompanyAsync("123");
        }
        inner.ConfigFailure = new AnafException("JSON nevalid");
        for (var i = 0; i < 10; i++) { try { await guarded.SaveDraftAsync(null!); } catch (AnafException) { } }
        inner.ConfigFailure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await guarded.ActivateAsync(null!); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(AnafModule.ModuleId) == ModuleStatus.Healthy,
            "ANAF: a CUI that is not registered or invalid, a refused configuration (AnafException) and access denied are answers for the user, never faults of the module");
        inner.ConfigFailure = new InvalidOperationException("store down");
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) { try { await guarded.GetStateAsync(); } catch (InvalidOperationException) { } }
        check(registry.StatusOf(AnafModule.ModuleId) == ModuleStatus.Disabled, "ANAF: a fault of the configuration store counts for the module too");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new AnafModule(), services, Empty());
        check(services.Any(d => d.ServiceType == typeof(IAnafService)) && services.Any(d => d.ServiceType == typeof(AnafStore)) && services.Any(d => d.ServiceType == typeof(IHttpClientFactory)),
            "ANAF: the module registers the service, its store and the HTTP client of the lookup");
        var module = new AnafModule();
        check(module.Id == "anaf" && module.Nav.Count == 0, "ANAF: the module has no menu entry (its tab is Setari -> ANAF, permission setari-anaf)");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:anaf:Enabled"] = "false" }).Build();
        offRegistry.Add(new AnafModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var anaf = scope.ServiceProvider.GetService<IAnafService>();
            var lookup = anaf is null ? null : await anaf.LookupCompanyAsync("123");
            var refusedState = false;
            try { if (anaf is not null) await anaf.GetStateAsync(); } catch (ModuleUnavailableException) { refusedState = true; }
            check(offRegistry.StatusOf("anaf") == ModuleStatus.Disabled && lookup is { Outcome: AnafLookupOutcome.Unavailable } && refusedState,
                "ANAF: with the module switched off the service still resolves (the editors that inject it are created), a lookup answers \"unavailable\" and the settings say the module is out");
        }
        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("anaf") == ModuleStatus.Healthy, "ANAF: the application registers the ANAF module (Startup/ServiceRegistration)");
    }

    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "ANAF architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Anaf")) && !File.Exists(Path.Combine(root, "Services", "AnafLookup.cs")), "Architecture: the ANAF module lives in Modules/Anaf");
        check(File.Exists(Path.Combine(root, "Services", "AnafApply.cs")) && File.Exists(Path.Combine(root, "Components", "Shared", "AnafDifferencesDialog.razor")),
            "ANAF: the rules and the dialog that apply an answer to a beneficiary or supplier form stay with the editors");
        var settings = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Settings.razor"));
        check(settings.Contains("<ModuleGate ModuleId=\"@BlazorStoc.Modules.Anaf.AnafModule.ModuleId\"><AnafConfigurator /></ModuleGate>"), "ANAF: the settings tab is inside the gate of the module");
    }
}
