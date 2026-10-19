using BlazorStoc.Modules;
using BlazorStoc.Modules.ProjectReports;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The project reports module (situation of a project, equipment, consumption export): its guarded readers behind the circuit breaker (an unknown project, access denied and
// the offers module being out are answers, not faults), the registration, the stand-ins while it is out, the pages marked with the module and the links that go away with it.
public static class ProjectReportsModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private static ConsumptionQuery AnyQuery() => new(null, null, null, null);

    private sealed class ThrowingSituation : IProjectSituationReader
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<ProjectSituation> GetAsync(int projectId, CancellationToken cancellationToken = default) => Fail<ProjectSituation>();
        public Task<IReadOnlyList<ReserveSuggestion>> GetReserveSuggestionsAsync(int productId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ReserveSuggestion>>();
    }

    private sealed class ThrowingConsumption : IConsumptionReader
    {
        public Task<IReadOnlyList<ConsumptionRow>> GetAsync(ConsumptionQuery query, CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<ConsumptionRow>>(new InvalidOperationException("consumption"));
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
        registry.Add(new ProjectReportsModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingSituation();
        var situation = new GuardedProjectSituationReader(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await situation.GetAsync(1); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(ProjectReportsModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Project reports: repeated faults of the situation reader open the circuit of the module");
        check(await RefusedAsync(() => situation.GetAsync(1), () => situation.GetReserveSuggestionsAsync(1)) == 2 && inner.Calls == ModuleRegistry.FailureThreshold,
            "Project reports: with the circuit open both entry points of the situation refuse at once and the reader is not called");
        var consumption = new GuardedConsumptionReader(new ThrowingConsumption(), registry);
        check(await RefusedAsync(() => consumption.GetAsync(AnyQuery())) == 1, "Project reports: the consumption export shares the circuit and refuses at once");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await situation.GetReserveSuggestionsAsync(1);
        check(registry.StatusOf(ProjectReportsModule.ModuleId) == ModuleStatus.Healthy, "Project reports: after the cooldown the first successful call brings the module back");

        foreach (Exception answer in new Exception[] { new OfferException("project missing"), new AccessDeniedException("no"), new ModuleUnavailableException("oferte") })
        {
            inner.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await situation.GetAsync(1); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(ProjectReportsModule.ModuleId) == ModuleStatus.Healthy,
            "Project reports: an unknown project (OfferException), access denied and the offers module being out reach the caller unchanged and never disable this module");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new ProjectReportsModule(), services, Empty());
        check(services.Any(d => d.ServiceType == typeof(IProjectSituationReader)) && services.Any(d => d.ServiceType == typeof(IConsumptionReader)) && services.Any(d => d.ServiceType == typeof(IProjectSituationPdfWriter)),
            "Project reports: the module registers the situation reader, the consumption reader and the PDF writer");
        var module = new ProjectReportsModule();
        check(module.Id == "situatie-proiect" && module.Nav.Count == 0, "Project reports: the module id is situatie-proiect and it adds no menu entry of its own (the links are gated by it)");

        var pageNames = new[] { "ProjectSituationPage", "ConsumptionExportPage", "ProjectEquipment" };
        var pages = typeof(ModuleAttribute).Assembly.GetTypes().Where(type => type.Namespace == "BlazorStoc.Components.Pages" && pageNames.Contains(type.Name)).ToList();
        check(pages.Count == 3 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Project reports: the three pages (situation, consumption export, equipment) are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:situatie-proiect:Enabled"] = "false" }).Build();
        offRegistry.Add(new ProjectReportsModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var reader = scope.ServiceProvider.GetService<IProjectSituationReader>();
            var export = scope.ServiceProvider.GetService<IConsumptionReader>();
            var writer = scope.ServiceProvider.GetService<IProjectSituationPdfWriter>();
            var refused = 0;
            try { if (reader is not null) await reader.GetAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (reader is not null) await reader.GetReserveSuggestionsAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (export is not null) await export.GetAsync(AnyQuery()); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("situatie-proiect") == ModuleStatus.Disabled && refused == 3 && writer is not null,
                "Project reports: with the module switched off the readers and the writer still resolve (the components of a project, the suggestions and the export endpoints are created) and every call says the module is out");
        }

        var application = new ModuleRegistry();
        var applicationServices = new ServiceCollection();
        BlazorStoc.Startup.ServiceRegistration.AddModules(applicationServices, Empty(), application);
        check(application.StatusOf("situatie-proiect") == ModuleStatus.Healthy && applicationServices.Count(d => d.ServiceType == typeof(IProjectSituationReader)) == 1
              && applicationServices.Count(d => d.ServiceType == typeof(IConsumptionReader)) == 1,
            "Project reports: the application registers the module, and the readers are registered once (by the module, not again by the core)");
    }

    // The sources of the module live in Modules/ProjectReports; the links to its pages go away with it.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Project reports architecture: the project folder is found"); return; }
        var folder = Path.Combine(root, "Modules", "ProjectReports");
        check(new[] { "ProjectSituation.cs", "MariaProjectSituationReader.cs", "ProjectSituationPdfWriter.cs", "ConsumptionExport.cs", "ProjectSituationPage.razor", "ConsumptionExportPage.razor", "ProjectEquipment.razor" }
                  .All(name => File.Exists(Path.Combine(folder, name)) && !File.Exists(Path.Combine(root, "Services", name)) && !File.Exists(Path.Combine(root, "Components", "Pages", name))),
            "Architecture: the project reports module lives in Modules/ProjectReports (nothing of it is left in Services or Components/Pages)");

        string Read(params string[] path) => File.ReadAllText(Path.Combine([root, .. path]));
        check(Read("Components", "Layout", "MainLayout.razor").Contains("ProjectReportsModule.ModuleId"), "Project reports: the export menu link is shown only while the module is available");
        check(Read("Components", "Pages", "Dashboard.razor").Contains("ModuleGate ModuleId=\"situatie-proiect\""), "Project reports: the export tile of the main page is in a gate of the module");
        var project = Read("Components", "Pages", "ProjectPage.razor");
        check(project.Split("ModuleGate ModuleId=\"situatie-proiect\"").Length - 1 == 2, "Project reports: the situation and equipment rows of the project page are in gates of the module");
        check(Read("Components", "Shared", "ProjectComponentsSection.razor").Contains("ModuleGate ModuleId=\"situatie-proiect\""), "Project reports: the situation link of the components section is in a gate of the module");
        var program = Read("Program.cs");
        check(program.Split("catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }").Length - 1 >= 4,
            "Project reports: the three export endpoints (consumption CSV, situation PDF and CSV) answer 503 while the module is out");
    }
}
