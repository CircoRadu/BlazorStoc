using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Offers;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The offers module: its guarded entry points behind the circuit breaker (user mistakes in an offer or a template are answers, not faults), the registration, the
// stand-ins while it is out, the pages marked with the module, the menu entry from the module and the project situation that says so while the module is out.
public static class OfferModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingOffers : IOfferRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<OfferRecord?> GetLatestAsync(string number, CancellationToken cancellationToken = default) => Fail<OfferRecord?>();
        public Task<IReadOnlyList<OfferLineRecord>> GetLinesAsync(int offerId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<OfferLineRecord>>();
        public Task<IReadOnlyList<OfferRecord>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => Fail<IReadOnlyList<OfferRecord>>();
        public Task<OfferDiff?> DiffAsync(string number, IReadOnlyList<OfferImportLine> lines, CancellationToken cancellationToken = default) => Fail<OfferDiff?>();
        public Task<IReadOnlyDictionary<string, int>> GetRememberedMatchesAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyDictionary<string, int>>();
        public Task<int?> FindBeneficiaryByAliasAsync(string text, CancellationToken cancellationToken = default) => Fail<int?>();
        public Task<OfferImportResult> ImportAsync(OfferImportRequest request, CancellationToken cancellationToken = default) => Fail<OfferImportResult>();
    }

    private sealed class ThrowingTemplates : IOfferTemplateRepository
    {
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("templates"));
        public Task<IReadOnlyList<OfferTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<OfferTemplateRecord>>();
        public Task<OfferTemplateRecord> CreateAsync(string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) => Fail<OfferTemplateRecord>();
        public Task<OfferTemplateRecord> SaveAsync(OfferTemplateRecord original, string name, OfferTemplateDefinition definition, CancellationToken cancellationToken = default) => Fail<OfferTemplateRecord>();
        public Task<OfferTemplateRecord> SetActiveAsync(OfferTemplateRecord original, bool active, CancellationToken cancellationToken = default) => Fail<OfferTemplateRecord>();
        public Task DeleteAsync(OfferTemplateRecord original, CancellationToken cancellationToken = default) => Fail<object>();
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
        registry.Add(new OffersModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingOffers();
        var offers = new GuardedOfferRepository(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await offers.GetForProjectAsync(1); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(OffersModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Offers: repeated faults of the offer repository open the circuit of the module");
        var refused = await RefusedAsync(() => offers.GetLatestAsync("1"), () => offers.GetLinesAsync(1), () => offers.GetForProjectAsync(1), () => offers.DiffAsync("1", []),
            () => offers.GetRememberedMatchesAsync(), () => offers.FindBeneficiaryByAliasAsync("x"), () => offers.ImportAsync(null!));
        check(refused == 7 && inner.Calls == ModuleRegistry.FailureThreshold, "Offers: with the circuit open all seven offer entry points refuse at once and the repository is not called");
        var templates = new GuardedOfferTemplateRepository(new ThrowingTemplates(), registry);
        check(await RefusedAsync(() => templates.GetAllAsync(), () => templates.CreateAsync("x", null!), () => templates.SaveAsync(null!, "x", null!),
                () => templates.SetActiveAsync(null!, true), () => templates.DeleteAsync(null!)) == 5,
            "Offers: the five template entry points share the circuit and refuse at once");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await offers.GetForProjectAsync(1);
        check(registry.StatusOf(OffersModule.ModuleId) == ModuleStatus.Healthy, "Offers: after the cooldown the first successful call brings the module back");

        foreach (Exception answer in new Exception[] { new OfferException("project missing"), new OfferTemplateException("name taken"), new XlsxFormatException("not an xlsx"), new AccessDeniedException("no") })
        {
            inner.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await offers.ImportAsync(null!); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(OffersModule.ModuleId) == ModuleStatus.Healthy,
            "Offers: a rejected offer or template (OfferException, OfferTemplateException), an unreadable file (XlsxFormatException) and access denied reach the user unchanged and never disable the module");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new OffersModule(), services, Empty());
        check(services.Any(d => d.ServiceType == typeof(IOfferRepository)) && services.Any(d => d.ServiceType == typeof(IOfferTemplateRepository)),
            "Offers: the module registers the offer and the template repositories");
        var module = new OffersModule();
        check(module.Id == "oferte" && module.Nav.Count == 1 && module.Nav[0].Href == "/oferte" && module.Nav[0].PermissionModule == "oferte",
            "Offers: the module keeps the address and the permission module of the pages (/oferte, oferte.view)");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes()
            .Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "OfferPickup" or "OfferTemplates").ToList();
        check(pages.Count == 2 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Offers: both pages (taking over an offer, templates) are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:oferte:Enabled"] = "false" }).Build();
        offRegistry.Add(new OffersModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var offers = scope.ServiceProvider.GetService<IOfferRepository>();
            var templates = scope.ServiceProvider.GetService<IOfferTemplateRepository>();
            var refused = 0;
            try { if (offers is not null) await offers.GetForProjectAsync(1); } catch (ModuleUnavailableException) { refused++; }
            try { if (templates is not null) await templates.GetAllAsync(); } catch (ModuleUnavailableException) { refused++; }
            check(offRegistry.StatusOf("oferte") == ModuleStatus.Disabled && refused == 2,
                "Offers: with the module switched off both repositories still resolve (the project situation that injects them is created) and every call says the module is out");
        }

        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("oferte") == ModuleStatus.Healthy, "Offers: the application registers the offers module (Startup/ServiceRegistration)");
        check(application.Snapshot().Select(item => item.Id).Where(id => id is "oferte" or "mentenanta" or "analize-risc").SequenceEqual(["oferte", "mentenanta", "analize-risc"]),
            "Offers: the menu entries keep their order (offers, maintenance, risk analyses)");
    }

    // The sources of the module live in Modules/Offers; the menu entry comes from the module; the project situation answers a module that is out with its own message.
    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Offers architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Offers")) && !Directory.Exists(Path.Combine(root, "Services", "Offers")), "Architecture: the offers module lives in Modules/Offers");
        var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(!layout.Contains("href=\"/oferte\""), "Offers: the menu entry comes from the module's Nav, not from MainLayout");
        var dashboard = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        check(Regex.IsMatch(dashboard, @"<ModuleGate ModuleId=""oferte"" Notice=""false""><AuthorizeView Policy=""perm:oferte\.view"">"), "Offers: the tile of the main page is inside the gate of the module");
        var situation = File.ReadAllText(Path.Combine(root, "Modules", "ProjectReports", "ProjectSituationPage.razor"));
        check(situation.Contains("catch (BlazorStoc.Modules.ModuleUnavailableException)") && situation.Contains("Modulul Oferte este oprit"),
            "Offers: the project situation says the offers module is out instead of a generic error");
    }
}
