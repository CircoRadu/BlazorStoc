using System.Text.RegularExpressions;
using BlazorStoc.Components.Shared;
using BlazorStoc.Modules;
using BlazorStoc.Modules.RiskAnalyses;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The module contract: isolated registration, the circuit breaker, the guarded risk analyses entry points, the module gate and the rule that a module
// does not depend on another one.
public static class ModuleChecks
{
    private sealed class FakeModule(string id, bool failRegister = false, bool failSources = false) : IModule
    {
        public string Id => id;
        public string Name => "Fake " + id;
        public Version Version { get; } = new(2, 1, 0);
        public IReadOnlyList<ModuleNavItem> Nav => [new($"/{id}", id, "x", id)];
        public void Register(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(new Marker(id));
            if (failRegister) throw new InvalidOperationException("register failed");
        }
        public void RegisterNotificationSources(IServiceCollection services)
        {
            services.AddSingleton(new Marker(id + "-sources"));
            if (failSources) throw new InvalidOperationException("sources failed");
        }
    }

    private sealed record Marker(string Name);

    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private static List<string> Markers(IServiceCollection services) =>
        [.. services.Select(descriptor => descriptor.ImplementationInstance).OfType<Marker>().Select(marker => marker.Name)];

    // Every tile of the main page that leads to a page of a module is a ModuleTile of that module: with the module out the tile stays, grey and without a link (only the tiles of the core stay without one).
    private static void DashboardTiles(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Dashboard tiles: the project folder is found"); return; }
        var core = new HashSet<string> { "/produse", "/iesiri/multipla", "/categorii", "/beneficiari", "/setari?tab=utilizatori", "/nomenclator", "/setari" };
        var ungated = File.ReadAllLines(Path.Combine(root, "Components", "Pages", "Dashboard.razor"))
            .Where(line => line.Contains("main-menu-icon") || line.Contains("<ModuleTile "))
            .Select(line => (Href: Regex.Match(line, "(?:<a href|<ModuleTile [^>]*Href)=\"([^\"]+)\"").Groups[1].Value, Gated: line.Contains("<ModuleGate") || line.Contains("<ModuleTile ")))
            .Where(tile => tile.Href.Length > 0 && !core.Contains(tile.Href) && !tile.Gated)
            .Select(tile => tile.Href).ToList();
        check(ungated.Count == 0, "Modules: every module tile of the main page is inside the gate of its module (" + string.Join(", ", ungated) + ")");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Registration(check);
        Breaker(check);
        await ConsumersAsync(check);
        await GuardedRepositoryAsync(check);
        await WiringAsync(check);
        Gate(check);
        Architecture(check);
        DashboardTiles(check);
        await VehicleModuleChecks.RunAsync(check);
        await MaintenanceModuleChecks.RunAsync(check);
        await OfferModuleChecks.RunAsync(check);
        await ProjectReportsModuleChecks.RunAsync(check);
        await JournalModuleChecks.RunAsync(check);
        await ProjectObservationsModuleChecks.RunAsync(check);
        await InventoryModuleChecks.RunAsync(check);
        await AnafModuleChecks.RunAsync(check);
        await InvoiceModuleChecks.RunAsync(check);
        await NotificationModuleChecks.RunAsync(check);
        await SupplierModuleChecks.RunAsync(check);
    }

    private static void Registration(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        check(registry.Add(new FakeModule("good"), services, Empty()) && registry.StatusOf("good") == ModuleStatus.Healthy, "Modules: a module that registers is Healthy");
        check(!registry.Add(new FakeModule("broken", failRegister: true), services, Empty()) && registry.StatusOf("broken") == ModuleStatus.Disabled,
            "Modules: a module whose registration throws is Disabled and does not stop the application from starting");
        check(registry.Add(new FakeModule("after"), services, Empty()) && registry.StatusOf("after") == ModuleStatus.Healthy,
            "Modules: the module registered after a broken one is not affected");
        var markers = Markers(services);
        check(markers.SequenceEqual(["good", "after"]), "Modules: a module that fails halfway leaves none of its services behind (" + string.Join(",", markers) + ")");
        check(registry.NavItems().Select(item => item.Href).SequenceEqual(["/good", "/after"]), "Modules: the menu has the entries of the available modules only");
        check(registry.StatusOf("unknown") == ModuleStatus.Disabled && !registry.IsAvailable("unknown"), "Modules: an unknown module is reported as not available");
        var snapshot = registry.Snapshot();
        check(snapshot.Select(item => (item.Id, item.Status)).SequenceEqual([("good", ModuleStatus.Healthy), ("broken", ModuleStatus.Disabled), ("after", ModuleStatus.Healthy)])
            && snapshot[0].Version == "2.1.0", "Modules: the snapshot lists every module with its version and status in registration order");

        var sourceServices = new ServiceCollection();
        var withSources = new ModuleRegistry();
        withSources.Add(new FakeModule("a"), sourceServices, Empty());
        withSources.Add(new FakeModule("b", failSources: true), sourceServices, Empty());
        withSources.Add(new FakeModule("c"), sourceServices, Empty());
        withSources.AddNotificationSources(sourceServices);
        var names = Markers(sourceServices);
        check(withSources.StatusOf("b") == ModuleStatus.Disabled && withSources.StatusOf("a") == ModuleStatus.Healthy && withSources.StatusOf("c") == ModuleStatus.Healthy,
            "Modules: a module whose notification sources fail to register is Disabled, the others are not");
        check(names.Contains("a-sources") && names.Contains("c-sources") && !names.Contains("b-sources"),
            "Modules: the notification sources of a failed module are not registered (" + string.Join(",", names) + ")");

        var off = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:off:Enabled"] = "false", ["Modules:on:Enabled"] = "true" }).Build();
        off.Add(new FakeModule("off"), offServices, configuration);
        off.Add(new FakeModule("on"), offServices, configuration);
        off.Add(new FakeModule("unset"), offServices, configuration);
        check(off.StatusOf("off") == ModuleStatus.Disabled && off.ReasonOf("off") == "disabled-by-configuration" && off.StatusOf("on") == ModuleStatus.Healthy
            && off.StatusOf("unset") == ModuleStatus.Healthy && !Markers(offServices).Contains("off"),
            "Modules: Modules:<id>:Enabled=false switches a module off without registering anything");
        off.AddNotificationSources(offServices);
        check(!Markers(offServices).Contains("off-sources") && Markers(offServices).Contains("on-sources"), "Modules: a module switched off registers no notification sources either");
    }

    private static void Breaker(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new FakeModule("m"), new ServiceCollection(), Empty());
        registry.Add(new FakeModule("other"), new ServiceCollection(), Empty());
        var fault = new InvalidOperationException("boom");

        registry.ReportFailure("m", fault);
        check(registry.StatusOf("m") == ModuleStatus.Degraded && registry.IsAvailable("m"), "Breaker: the first fault makes the module Degraded but it is still tried");
        registry.ReportSuccess("m");
        check(registry.StatusOf("m") == ModuleStatus.Healthy, "Breaker: a success after a fault brings the module back to Healthy");
        registry.ReportFailure("m", fault); registry.ReportFailure("m", fault); registry.ReportSuccess("m"); registry.ReportFailure("m", fault); registry.ReportFailure("m", fault);
        check(registry.IsAvailable("m"), "Breaker: faults separated by a success do not add up (the threshold is consecutive faults)");
        registry.ReportFailure("m", fault);
        check(registry.StatusOf("m") == ModuleStatus.Disabled, $"Breaker: {ModuleRegistry.FailureThreshold} consecutive faults open the circuit");
        check(registry.StatusOf("other") == ModuleStatus.Healthy, "Breaker: the other module is not affected by the open circuit");
        var refused = false;
        try { registry.EnsureAvailable("m"); } catch (ModuleUnavailableException exception) { refused = exception.ModuleId == "m"; }
        check(refused, "Breaker: a call to a module with an open circuit is refused with ModuleUnavailableException");
        registry.ReportSuccess("m");
        check(registry.StatusOf("m") == ModuleStatus.Disabled, "Breaker: a call that was already running when the circuit opened does not close it");
        clock.Advance(ModuleRegistry.Cooldown - TimeSpan.FromSeconds(1));
        check(registry.StatusOf("m") == ModuleStatus.Disabled, "Breaker: the circuit stays open during the cooldown");
        clock.Advance(TimeSpan.FromSeconds(2));
        check(registry.StatusOf("m") == ModuleStatus.Degraded && registry.IsAvailable("m"), "Breaker: after the cooldown the module is Degraded and the next call is let through");
        registry.ReportFailure("m", fault);
        check(registry.StatusOf("m") == ModuleStatus.Disabled, "Breaker: one fault after the cooldown opens the circuit again at once");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        _ = registry.StatusOf("m");
        registry.ReportSuccess("m");
        check(registry.StatusOf("m") == ModuleStatus.Healthy, "Breaker: a success after the cooldown closes the circuit");
        registry.ReportFailure("m", fault);
        registry.ReportSuccess("m");
        registry.ReportFailure("m", fault); registry.ReportFailure("m", fault);
        check(registry.IsAvailable("m"), "Breaker: after a recovery the count of faults starts from zero");

        var answers = new ModuleRegistry(clock);
        answers.Add(new FakeModule("a"), new ServiceCollection(), Empty());
        for (var i = 0; i < 10; i++)
        {
            answers.ReportFailure("a", new RiskAnalysisOperationException("validation"));
            answers.ReportFailure("a", new AccessDeniedException("denied"));
            answers.ReportFailure("a", new OperationCanceledException());
            answers.ReportFailure("a", new TimeoutException());
            answers.ReportFailure("a", new ModuleUnavailableException("a"));
            answers.ReportFailure("a", new MaintenanceInProgressException(MaintenanceGate.BlockedMessage));
        }
        check(answers.StatusOf("a") == ModuleStatus.Healthy, "Breaker: answers of the module (operation exception, access denied), cancellation and timeouts are not faults");
        check(!ModuleRegistry.IsFault(new MaintenanceInProgressException(MaintenanceGate.BlockedMessage)),
            "Breaker: a call that meets the write freeze of a backup or restoration is not a fault (pages loading during a backup must not take modules out)");
        check(ModuleRegistry.IsFault(new NullReferenceException()) && ModuleRegistry.IsFault(new InvalidOperationException()) && ModuleRegistry.IsFault(new InvalidCastException()),
            "Breaker: programming errors count as faults");
        answers.ReportFailure("unknown", fault);
        answers.ReportSuccess("unknown");
        check(answers.StatusOf("unknown") == ModuleStatus.Disabled, "Breaker: reports about an unknown module are ignored");
    }

    private static async Task ConsumersAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry(new ManualTimeProvider());
        registry.Add(new FakeModule("m"), new ServiceCollection(), Empty());
        var calls = 0;
        Task<int> Faulty() { calls++; throw new InvalidOperationException("db"); }
        check(await registry.TryRunAsync("m", () => Task.FromResult(5), -1) == 5, "Consumers: a working module gives its result");
        check(await registry.TryRunAsync("m", Faulty, -1) == -1, "Consumers: a failing module gives the fallback instead of an exception, so the page loads");
        await registry.TryRunAsync("m", Faulty, -1);
        await registry.TryRunAsync("m", Faulty, -1);
        var before = calls;
        check(await registry.TryRunAsync("m", Faulty, -1) == -1 && calls == before, "Consumers: with the circuit open the module is not even called");
        var denied = false;
        var other = new ModuleRegistry();
        other.Add(new FakeModule("x"), new ServiceCollection(), Empty());
        try { await other.TryRunAsync<int>("x", () => throw new AccessDeniedException("no"), 0); } catch (AccessDeniedException) { denied = true; }
        check(denied && other.StatusOf("x") == ModuleStatus.Healthy, "Consumers: access denied is not swallowed and does not count as a failure");
        var cancelled = false;
        try { await other.TryRunAsync<int>("x", () => throw new OperationCanceledException(), 0); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Consumers: cancellation is not swallowed");
        var rethrown = false;
        try { await other.RunAsync("x", () => Task.FromException<int>(new InvalidOperationException("own"))); } catch (InvalidOperationException) { rethrown = true; }
        check(rethrown && other.StatusOf("x") == ModuleStatus.Degraded, "Consumers: the guarded call of the module itself rethrows the exception and reports the fault");
        var ran = false;
        await other.RunAsync("x", () => { ran = true; return Task.CompletedTask; });
        check(ran && other.StatusOf("x") == ModuleStatus.Healthy, "Consumers: the guarded call without a result reports the success");
    }

    private sealed class ThrowingRepository : IRiskAnalysisRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<RiskAnalysisView>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<RiskAnalysisView>>();
        public Task<IReadOnlyList<RiskAnalysisView>> GetAllAsync(bool includeOff, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<RiskAnalysisView>>();
        public Task<RiskAnalysisView> CreateAsync(int beneficiaryId, RiskAnalysisInput input, bool deactivateExisting, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task<RiskAnalysisView> UpdateAsync(RiskAnalysis original, RiskAnalysisInput input, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task<RiskAnalysisView> RenewAsync(RiskAnalysis original, RiskAnalysisRenewalInput input, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task<RiskAnalysisView> UndoLastRenewalAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task<RiskAnalysisView> ActivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task<RiskAnalysisView> DeactivateAsync(RiskAnalysis original, CancellationToken cancellationToken = default) => Fail<RiskAnalysisView>();
        public Task DeleteAsync(RiskAnalysis original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
    }

    private sealed class FailingReader : IRiskAnalysisNotificationReader
    {
        public Task<IReadOnlyList<RiskAnalysisExpiryItem>> GetActiveAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("reader");
    }

    private static async Task GuardedRepositoryAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new RiskAnalysesModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingRepository();
        var guarded = new GuardedRiskAnalysisRepository(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await guarded.GetAllAsync(true); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(RiskAnalysesModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Risk analyses: repeated faults of the repository open the circuit of the module");
        var unavailable = 0;
        foreach (var call in new Func<Task>[]
        {
            () => guarded.GetAllAsync(true), () => guarded.GetForBeneficiaryAsync(1), () => guarded.CreateAsync(1, new(), false), () => guarded.UpdateAsync(null!, new()),
            () => guarded.RenewAsync(null!, new()), () => guarded.UndoLastRenewalAsync(null!), () => guarded.ActivateAsync(null!), () => guarded.DeactivateAsync(null!),
            () => guarded.DeleteAsync(null!, "x")
        })
        {
            try { await call(); } catch (ModuleUnavailableException) { unavailable++; }
        }
        check(unavailable == 9 && inner.Calls == ModuleRegistry.FailureThreshold, "Risk analyses: with the circuit open all nine entry points refuse at once and the repository is not called");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await guarded.GetAllAsync(true);
        check(registry.StatusOf(RiskAnalysesModule.ModuleId) == ModuleStatus.Healthy, "Risk analyses: after the cooldown the first successful call brings the module back");
        inner.Failure = new RiskAnalysisOperationException("conflict");
        for (var i = 0; i < 10; i++) { try { await guarded.RenewAsync(null!, new()); } catch (RiskAnalysisOperationException) { } }
        inner.Failure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await guarded.DeleteAsync(null!, "x"); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(RiskAnalysesModule.ModuleId) == ModuleStatus.Healthy, "Risk analyses: validation conflicts and access denied reach the user unchanged and never disable the module");
        var reader = new GuardedRiskAnalysisNotificationReader(new FailingReader(), registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) { try { await reader.GetActiveAsync(); } catch (InvalidOperationException) { } }
        check(registry.StatusOf(RiskAnalysesModule.ModuleId) == ModuleStatus.Disabled, "Risk analyses: the failures of the notification reader count for the module too");
        var sourceRefused = false;
        try { await new RiskAnalysisExpirySource(reader).GetInstancesAsync(); } catch (ModuleUnavailableException) { sourceRefused = true; }
        check(sourceRefused, "Risk analyses: the notification source of a module with an open circuit fails at once (the engine keeps the notifications it has)");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new RiskAnalysesModule(), services, Empty());
        registry.AddNotificationSources(services);
        check(services.Any(d => d.ServiceType == typeof(IRiskAnalysisRepository)) && services.Any(d => d.ServiceType == typeof(IRiskAnalysisNotificationReader))
            && services.Count(d => d.ServiceType == typeof(IExpirySource)) == 1, "Risk analyses: the module registers the repository, the notification reader and its one notification source");
        var module = new RiskAnalysesModule();
        check(module.Id == "analize-risc" && module.Nav.Count == 1 && module.Nav[0].Href == "/analize-risc" && module.Nav[0].PermissionModule == "analize-risc",
            "Risk analyses: the module keeps the address and the permission module of the page (/analize-risc, analize-risc.view)");
        var page = typeof(ModuleAttribute).Assembly.GetTypes().Single(type => type.Name == "RiskAnalyses" && type.Namespace == "BlazorStoc.Components.Pages");
        var attribute = page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault();
        check(attribute?.ModuleId == module.Id, "Risk analyses: the page is marked with its module, so the router can replace it while the module is out");

        // A module that is out leaves stand-ins for what other pages inject, so those pages are still created (the circuit of the page must not fail).
        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:analize-risc:Enabled"] = "false" }).Build();
        offRegistry.Add(new RiskAnalysesModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var standIn = scope.ServiceProvider.GetService<IRiskAnalysisRepository>();
            var refusedCall = false;
            try { if (standIn is not null) await standIn.GetAllAsync(false); } catch (ModuleUnavailableException) { refusedCall = true; }
            check(offRegistry.StatusOf("analize-risc") == ModuleStatus.Disabled && standIn is not null && refusedCall,
                "Risk analyses: with the module switched off the repository still resolves (pages that inject it are created) and every call says the module is out");
            check(await offRegistry.TryRunAsync("analize-risc", () => standIn!.GetAllAsync(false), Array.Empty<RiskAnalysisView>()) is { Count: 0 },
                "Risk analyses: a page reading through the registry gets the empty fallback while the module is switched off");
        }
        var failing = new ModuleRegistry();
        var failingServices = new ServiceCollection();
        failing.Add(new FakeModule("f", failRegister: true), failingServices, Empty());
        check(!failingServices.Any(), "Modules: a module that failed to start adds nothing unless it declares stand-ins");

        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.Snapshot().Select(item => item.Id).Contains("analize-risc") && application.StatusOf("analize-risc") == ModuleStatus.Healthy,
            "Modules: the application registers the risk analyses module (Startup/ServiceRegistration)");
    }

    private static void Gate(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new FakeModule("g"), new ServiceCollection(), Empty());
        using var context = new BunitContext();
        context.Services.AddSingleton(registry);
        const string content = "<p id=\"inside\">content</p>";
        var healthy = context.Render<ModuleGate>(p => p.Add(g => g.ModuleId, "g").AddChildContent(content));
        check(healthy.FindAll("#inside").Count == 1 && healthy.FindAll(".error-banner").Count == 0, "Gate: the content of an available module is shown");
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) registry.ReportFailure("g", new InvalidOperationException());
        var gone = context.Render<ModuleGate>(p => p.Add(g => g.ModuleId, "g").AddChildContent(content));
        check(gone.FindAll("#inside").Count == 0 && gone.Markup.Contains("modul indisponibil"), "Gate: a module that is out shows a notice instead of its content");
        var quiet = context.Render<ModuleGate>(p => p.Add(g => g.ModuleId, "g").Add(g => g.Notice, false).AddChildContent(content));
        check(quiet.FindAll("#inside").Count == 0 && quiet.FindAll(".error-banner").Count == 0, "Gate: without Notice a module that is out leaves nothing behind (menus, tiles)");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        var back = context.Render<ModuleGate>(p => p.Add(g => g.ModuleId, "g").AddChildContent(content));
        check(back.FindAll("#inside").Count == 1, "Gate: after the cooldown the content is shown again");
        var unknown = context.Render<ModuleGate>(p => p.Add(g => g.ModuleId, "nope").AddChildContent(content));
        check(unknown.FindAll("#inside").Count == 0, "Gate: an unknown module is treated as out");
        var unavailable = context.Render<ModuleUnavailable>();
        check(unavailable.Markup.Contains("Modul indisponibil") && unavailable.Markup.Contains("href=\"/\""), "Gate: the unavailable notice explains and links back to the main page");
    }

    // A module does not depend on another module: its sources (a folder Modules/<Name>) mention no namespace of another module folder.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        check(root is not null, "Architecture: the project folder is found for the module scan");
        if (root is null) return;
        var modulesRoot = Path.Combine(root, "Modules");
        var folders = Directory.GetDirectories(modulesRoot).Select(Path.GetFileName).OfType<string>().ToList();
        check(folders.Contains("RiskAnalyses"), "Architecture: the risk analyses module lives in Modules/RiskAnalyses");
        var violations = new List<string>();
        foreach (var folder in folders)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(modulesRoot, folder), "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs") || f.EndsWith(".razor")))
            {
                var text = File.ReadAllText(file);
                foreach (var other in folders.Where(name => name != folder))
                    if (Regex.IsMatch(text, $@"BlazorStoc\.Modules\.{other}\b")) violations.Add($"{Path.GetFileName(file)} -> {other}");
            }
        }
        check(violations.Count == 0, "Architecture: no module refers to another module (" + string.Join("; ", violations) + ")");
        // The layout is rendered statically (Routes has no render mode): a banner inside it updates only if it is an interactive island of its own.
        var mainLayout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(Regex.IsMatch(mainLayout, @"<ModuleAlertBanner @rendermode=""@\(new InteractiveServerRenderMode\(prerender: false\)\)"" />"),
            "Architecture: the banner of the modules that are out is an interactive island (the static layout would show the state of the page load forever)");
        var unmarked = Directory.GetFiles(modulesRoot, "*.razor", SearchOption.AllDirectories)
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"^﻿?@page\s", RegexOptions.Multiline) && !File.ReadAllText(file).Contains("[BlazorStoc.Modules.Module("))
            .Select(Path.GetFileName).ToList();
        check(unmarked.Count == 0, "Architecture: every routable page of a module is marked with [Module] (" + string.Join(", ", unmarked) + ")");
        // A component written with a namespace prefix (<Shared.Name />) is not resolved by Razor: it ends up in the page as an unknown HTML element and shows nothing.
        var qualified = new[] { "Components", "Modules", "Pages" }.Where(folder => Directory.Exists(Path.Combine(root, folder)))
            .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.razor", SearchOption.AllDirectories))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"<(Shared|Layout|Pages)\.[A-Z]")).Select(Path.GetFileName).ToList();
        check(qualified.Count == 0, "Architecture: no component is written with a namespace prefix in its tag (" + string.Join(", ", qualified) + ")");
        var strays = Directory.GetFiles(Path.Combine(root, "Services"), "*.cs").Select(Path.GetFileName)
            .Where(name => name is not null && name.Contains("RiskAnalys", StringComparison.Ordinal)).ToList();
        check(strays.Count == 0, "Architecture: the sources of the risk analyses are not left in Services (" + string.Join(", ", strays) + ")");
    }
}
