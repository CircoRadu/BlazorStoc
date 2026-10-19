using BlazorStoc.Modules;
using BlazorStoc.Modules.Journal;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The activity journal module (the two pages that read the journal): the reader behind the circuit breaker (access denied is an answer, not a fault), the registration over
// the application's journal, the stand-in while the module is out, the pages marked with the module and the links that go away with it. Writing to the journal stays in the core.
public static class JournalModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingReader : IAuditReader
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<AuditEvent>>();
        public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => Fail<AuditPage>();
        public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => Fail<AuditSummary>();
        public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => Fail<IReadOnlyDictionary<string, DateTime>>();
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
        registry.Add(new JournalModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingReader();
        var reader = new GuardedAuditReader(inner, registry);
        var query = new AuditQuery();
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await reader.QueryAsync(query, 1, 20); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(JournalModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Journal: repeated faults of the journal reader open the circuit of the module");
        check(await RefusedAsync(() => reader.GetEventsAsync(), () => reader.QueryAsync(query, 1, 20), () => reader.SummaryAsync(DateTime.UtcNow), () => reader.RemovalTimesAsync([])) == 4
              && inner.Calls == ModuleRegistry.FailureThreshold,
            "Journal: with the circuit open all four entry points refuse at once and the journal is not read");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await reader.GetEventsAsync();
        check(registry.StatusOf(JournalModule.ModuleId) == ModuleStatus.Healthy, "Journal: after the cooldown the first successful call brings the module back");

        inner.Failure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await reader.QueryAsync(query, 1, 20); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(JournalModule.ModuleId) == ModuleStatus.Healthy, "Journal: access denied reaches the page unchanged and never disables the module");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        services.AddSingleton<ModuleRegistry>(registry);
        services.AddSingleton<IAuditTrail, TestAuditTrail>();
        registry.Add(new JournalModule(), services, Empty());
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var reader = scope.ServiceProvider.GetService<IAuditReader>();
            check(reader is not null && reader.GetType().Name == "GuardedAuditReader", "Journal: the module registers the reader over the application's journal (IAuditTrail)");
            var events = reader is null ? null : await reader.GetEventsAsync();
            check(events is not null, "Journal: the guarded reader answers from the journal underneath");
        }
        var module = new JournalModule();
        check(module.Id == "jurnal" && module.Nav.Count == 0, "Journal: the module id is jurnal and it adds no menu entry of its own (the link is gated by it)");
        check(typeof(IAuditReader).IsAssignableFrom(typeof(IAuditTrail)), "Journal: the whole journal (IAuditTrail) is the reader plus the writing, so every writer is unchanged");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes().Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "Audit" or "AuditFull").ToList();
        check(pages.Count == 2 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Journal: both pages (latest events, complete journal) are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:jurnal:Enabled"] = "false" }).Build();
        offRegistry.Add(new JournalModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var reader = scope.ServiceProvider.GetService<IAuditReader>();
            var refused = 0;
            try { if (reader is not null) await reader.QueryAsync(new AuditQuery(), 1, 20); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("jurnal") == ModuleStatus.Disabled && refused == 1, "Journal: with the module switched off the reader still resolves and every call says the module is out");
        }

        var application = new ModuleRegistry();
        var applicationServices = new ServiceCollection();
        BlazorStoc.Startup.ServiceRegistration.AddModules(applicationServices, Empty(), application);
        check(application.StatusOf("jurnal") == ModuleStatus.Healthy && applicationServices.Count(d => d.ServiceType == typeof(IAuditReader)) == 1,
            "Journal: the application registers the module, once");
    }

    // The sources of the module live in Modules/Journal; the link to its pages goes away with it; the writing of the journal stays out of it.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Journal architecture: the project folder is found"); return; }
        var folder = Path.Combine(root, "Modules", "Journal");
        check(new[] { "Audit.razor", "AuditFull.razor", "AuditFilterBar.razor", "AuditFilters.cs" }.All(name => File.Exists(Path.Combine(folder, name))
                  && !File.Exists(Path.Combine(root, "Services", name)) && !File.Exists(Path.Combine(root, "Components", "Pages", name)) && !File.Exists(Path.Combine(root, "Components", "Shared", name))),
            "Architecture: the journal module lives in Modules/Journal (nothing of it is left in Services or Components)");
        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        check(Read("Components", "Layout", "MainLayout.razor").Contains("JournalModule.ModuleId"), "Journal: the menu link is shown only while the module is available");
        check(Read("Components", "Pages", "Dashboard.razor").Contains("ModuleGate ModuleId=\"jurnal\""), "Journal: the tile of the main page is in a gate of the module");
        check(!Read("Modules", "Journal", "Audit.razor").Contains("IAuditTrail") && Read("Modules", "Journal", "AuditFull.razor").Contains("IAuditReader Reader"),
            "Journal: the latest-events page only reads (IAuditReader); the complete journal reads through the reader and records its export through the journal");
    }
}
