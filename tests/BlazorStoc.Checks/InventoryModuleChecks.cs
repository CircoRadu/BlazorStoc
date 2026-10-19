using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Inventory;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The inventory module: its guarded entry points behind the circuit breaker, the registration, the pages marked with the module, the menu section and the tiles that
// are shown only while it works, and the shared PDF font class that lets the core PDF writers stand without the inventory writer.
public static class InventoryModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class Throwing : IInventoryReportBuilder, IInventoryPickupBuilder, IInventoryPickupApplier
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        Task<InventoryReport> IInventoryReportBuilder.BuildAsync(InventoryRequest request, CancellationToken cancellationToken) => Fail<InventoryReport>();
        Task<InventoryPickupReview> IInventoryPickupBuilder.BuildAsync(IReadOnlyList<InventoryPickupScanRow> rows, CancellationToken cancellationToken) => Fail<InventoryPickupReview>();
        Task<InventoryPickupApplyResult> IInventoryPickupApplier.ApplyAsync(IReadOnlyList<InventoryPickupLine> selectedLines, CancellationToken cancellationToken) => Fail<InventoryPickupApplyResult>();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await GuardedAsync(check);
        Wiring(check);
        Architecture(check);
    }

    private static async Task GuardedAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new InventoryModule(), new ServiceCollection(), Empty());
        var inner = new Throwing();
        var report = new GuardedInventoryReportBuilder(inner, registry);
        var pickup = new GuardedInventoryPickupBuilder(inner, registry);
        var applier = new GuardedInventoryPickupApplier(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await report.BuildAsync(null!); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(InventoryModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Inventory: repeated faults of the report builder open the circuit of the module");
        var refused = 0;
        foreach (var call in new Func<Task>[] { () => report.BuildAsync(null!), () => pickup.BuildAsync([]), () => applier.ApplyAsync([]) })
        {
            try { await call(); } catch (ModuleUnavailableException) { refused++; }
        }
        check(refused == 3 && inner.Calls == ModuleRegistry.FailureThreshold, "Inventory: with the circuit open the three entry points (report, review, applying) refuse at once and nothing is called");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await pickup.BuildAsync([]);
        check(registry.StatusOf(InventoryModule.ModuleId) == ModuleStatus.Healthy, "Inventory: after the cooldown the first successful call brings the module back");
        foreach (Exception answer in new Exception[] { new InventoryOperationException("nothing selected"), new AccessDeniedException("no") })
        {
            inner.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await applier.ApplyAsync([]); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(InventoryModule.ModuleId) == ModuleStatus.Healthy, "Inventory: a refused operation (InventoryOperationException) and access denied reach the user unchanged and never disable the module");
    }

    private static void Wiring(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new InventoryModule(), services, Empty());
        check(new[] { typeof(IInventoryReportBuilder), typeof(IInventoryPdfWriter), typeof(IInventoryPickupBuilder), typeof(IInventoryPickupApplier) }.All(type => services.Any(d => d.ServiceType == type)),
            "Inventory: the module registers the report builder, the PDF writer, the review builder and the applier");
        var module = new InventoryModule();
        check(module.Id == "inventar" && module.Nav.Count == 0, "Inventory: the module keeps the permission modules (inventar, inventar-preluare); its menu section stays in MainLayout and is shown only while the module works");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes()
            .Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "Inventory" or "InventoryPickup").ToList();
        check(pages.Count == 2 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Inventory: both pages (report, take-over) are marked with the module, so the router replaces them while it is out");

        var offRegistry = new ModuleRegistry();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:inventar:Enabled"] = "false" }).Build();
        var offServices = new ServiceCollection();
        offRegistry.Add(new InventoryModule(), offServices, offConfiguration);
        check(offRegistry.StatusOf("inventar") == ModuleStatus.Disabled && !offServices.Any(d => d.ServiceType == typeof(IInventoryPickupApplier)),
            "Inventory: switched off in the configuration, the module registers nothing (only its own pages used the services)");

        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("inventar") == ModuleStatus.Healthy, "Inventory: the application registers the inventory module (Startup/ServiceRegistration)");
    }

    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Inventory architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Inventory")), "Architecture: the inventory module lives in Modules/Inventory");
        var strays = new[] { "Inventory.cs", "InventoryPdfWriter.cs", "InventoryPickup.cs", "InventorySelectionState.cs" }.Where(name => File.Exists(Path.Combine(root, "Services", name))).ToList();
        check(strays.Count == 0, "Architecture: the sources of the inventory are not left in Services (" + string.Join("; ", strays) + ")");
        check(File.Exists(Path.Combine(root, "Services", "InventoryPickupOcr.cs")) && File.Exists(Path.Combine(root, "Services", "InventoryPdfLayout.cs")),
            "Inventory: the OCR engine and the layout it shares with the PDF stay in Services (the Ocr module and the invoice reading use them)");
        var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(Regex.IsMatch(layout, @"@if \(Modules\.IsAvailable\(BlazorStoc\.Modules\.Inventory\.InventoryModule\.ModuleId\)\)\s*\{\s*<AuthorizeView Policy=""perm:inventar\.view\|inventar-preluare\.view"">"),
            "Inventory: the menu section is shown only while the module works");
        var dashboard = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        check(Regex.Matches(dashboard, @"<ModuleTile ModuleId=""inventar"" Policy=""perm:inventar(-preluare)?\.view""").Count == 2, "Inventory: both tiles of the main page belong to the module (grey while it is out)");
        var corePdf = new[] { "ConsumptionNotePdfWriter.cs", "ProjectSituationPdfWriter.cs", "InventoryPdfLayout.cs" }.Where(name => File.ReadAllLines(name == "ProjectSituationPdfWriter.cs" ? Path.Combine(root, "Modules", "ProjectReports", name) : Path.Combine(root, "Services", name)).Any(line => !line.TrimStart().StartsWith("//") && line.Contains("InventoryPdfWriter"))).ToList();
        check(corePdf.Count == 0, "Inventory: the PDF writers and the layout of the core use the shared PdfFonts, not the writer of the module (" + string.Join("; ", corePdf) + ")");
    }
}
