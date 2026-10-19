using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Invoices;
using BlazorStoc.Modules.Ocr;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The invoice pickup module: its guarded entry points (analysis sessions, templates) behind the circuit breaker, the answers of the module that are not faults, the failures
// of the OCR module that are counted for that module only (IBelongsToModule), the registration, the stand-ins while it is out, the pages marked with the module and the places of
// the core that show its entries only while it works.
public static class InvoiceModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class FakeAnalysis : IInvoiceAnalysisService
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("analysis down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        private T FailSync<T>() { Calls++; return Failure is null ? default! : throw Failure; }
        public Task<InvoiceAnalysisSession> AnalyzeAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) => Fail<InvoiceAnalysisSession>();
        public Task<InvoiceAnalysisSession> OpenAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) => Fail<InvoiceAnalysisSession>();
        public InvoiceAnalysisSession Analyze(Guid id) => FailSync<InvoiceAnalysisSession>();
        public InvoiceAnalysisSession? Get(Guid id) => FailSync<InvoiceAnalysisSession?>();
        public void Discard(Guid id) { }
        public Task<InvoiceAnalysisSession> OpenXmlAsync(byte[] content, string fileName, CancellationToken cancellationToken = default) => Fail<InvoiceAnalysisSession>();
    }

    private sealed class FakeTemplates : IInvoiceTemplateService
    {
        public Exception? Failure = new InvalidOperationException("templates down");
        private Task<T> Fail<T>() => Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure);
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<InvoiceTemplateInfo>>();
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateRecord?>();
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<InvoiceTemplateRecord>>();
        public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateModel?>();
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateRecord>();
        public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateRecord>();
        public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateInfo>();
        public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateInfo>();
        public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
        public Task<int> RenameSupplierAsync(string supplierCui, string oldName, string newName, CancellationToken cancellationToken = default) => Fail<int>();
        public Task<InvoiceTemplateInfo> LinkSupplierAsync(InvoiceTemplateInfo original, Supplier supplier, CancellationToken cancellationToken = default) => Fail<InvoiceTemplateInfo>();
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
        registry.Add(new InvoicesModule(), new ServiceCollection(), Empty());
        var inner = new FakeAnalysis();
        var analysis = new GuardedInvoiceAnalysisService(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await analysis.AnalyzeAsync(Stream.Null, "x.pdf"); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(InvoicesModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Invoices: repeated faults of the analysis open the circuit of the module");
        var refused = await RefusedAsync(() => analysis.AnalyzeAsync(Stream.Null, "x"), () => analysis.OpenAsync(Stream.Null, "x"), () => analysis.OpenXmlAsync([], "x"),
            () => { analysis.Analyze(Guid.Empty); return Task.CompletedTask; }, () => { analysis.Get(Guid.Empty); return Task.CompletedTask; });
        check(refused == 5 && inner.Calls == ModuleRegistry.FailureThreshold, "Invoices: with the circuit open the five analysis entry points (asynchronous and in memory) refuse at once and the analysis is not called");
        var templates = new GuardedInvoiceTemplateService(new FakeTemplates(), registry);
        check(await RefusedAsync(() => templates.ListAsync(), () => templates.GetAsync(1), () => templates.GetAllAsync(), () => templates.GetModelAsync(1), () => templates.CreateAsync(null!),
                () => templates.SaveAsync(null!, null!), () => templates.SetActiveAsync(null!, true), () => templates.UpdateDetailsAsync(null!, null!), () => templates.DeleteAsync(null!, "x"),
                () => templates.RenameSupplierAsync("1", "a", "b"), () => templates.LinkSupplierAsync(null!, null!)) == 11,
            "Invoices: the eleven template entry points share the circuit and refuse at once");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await analysis.AnalyzeAsync(Stream.Null, "x.pdf");
        check(registry.StatusOf(InvoicesModule.ModuleId) == ModuleStatus.Healthy, "Invoices: after the cooldown the first successful call brings the module back");

        var fakeTemplates = new FakeTemplates();
        var guardedTemplates = new GuardedInvoiceTemplateService(fakeTemplates, registry);
        foreach (Exception answer in new Exception[] { new InvoiceAnalysisException("not an invoice"), new InvoiceTemplateOperationException("name taken"), new AccessDeniedException("no") })
        {
            fakeTemplates.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await guardedTemplates.CreateAsync(null!); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(InvoicesModule.ModuleId) == ModuleStatus.Healthy,
            "Invoices: a refused file (InvoiceAnalysisException), a refused template (InvoiceTemplateOperationException) and access denied reach the user unchanged and never disable the module");

        // The reading of the pages is the Ocr module's: its worker failing passes through the analysis but is counted for the Ocr module only.
        var shared = new ModuleRegistry(clock);
        shared.Add(new InvoicesModule(), new ServiceCollection(), Empty());
        shared.Add(new OcrModule(), new ServiceCollection(), Empty());
        var ocrFailing = new FakeAnalysis { Failure = new OcrWorkerException("the worker died") };
        var viaInvoices = new GuardedInvoiceAnalysisService(ocrFailing, shared);
        for (var i = 0; i < 10; i++) { try { await viaInvoices.AnalyzeAsync(Stream.Null, "x.pdf"); } catch (OcrWorkerException) { } }
        check(shared.StatusOf(InvoicesModule.ModuleId) == ModuleStatus.Healthy, "Invoices: the failures of the OCR worker that pass through the analysis do not count for the invoice module");
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++) shared.ReportFailure(OcrRules.ModuleId, new OcrWorkerException("the worker died"));
        check(shared.StatusOf(OcrRules.ModuleId) == ModuleStatus.Disabled && shared.StatusOf(InvoicesModule.ModuleId) == ModuleStatus.Healthy,
            "Invoices: the same exception still counts for the Ocr module, which goes out alone");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new InvoicesModule(), services, Empty());
        check(new[] { typeof(IInvoiceAnalysisStore), typeof(IInvoiceAnalysisService), typeof(IInvoiceTemplateStore), typeof(IInvoiceTemplateService), typeof(InvoiceLabSettings) }
                .All(type => services.Any(d => d.ServiceType == type)),
            "Invoices: the module registers the analysis store and service, the template store and service and the lab settings");
        var module = new InvoicesModule();
        check(module.Id == "preluare-factura" && module.Nav.Count == 0, "Invoices: the module keeps the permission modules (preluare-factura, setari-facturi); its menu entry stays in the Products menu and is shown only while the module works");

        var pages = typeof(ModuleAttribute).Assembly.GetTypes()
            .Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "InvoicePickup" or "InvoiceViewer").ToList();
        check(pages.Count == 2 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id),
            "Invoices: both pages (the pickup and the viewer of the invoice) are marked with the module, so the router replaces them while it is out");
        var register = typeof(ModuleAttribute).Assembly.GetTypes().Single(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name == "Invoices");
        check(register.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == "furnizori", "Invoices: the register of supplier invoices (/facturi) belongs to the suppliers module, not to the pickup module");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:preluare-factura:Enabled"] = "false" }).Build();
        offRegistry.Add(new InvoicesModule(), offServices, offConfiguration);
        offServices.AddSingleton(TimeProvider.System);   // registered by the application core; the in-memory store of the analyses needs the clock
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var templates = scope.ServiceProvider.GetService<IInvoiceTemplateService>();
            var analysis = scope.ServiceProvider.GetService<IInvoiceAnalysisService>();
            var store = scope.ServiceProvider.GetService<IInvoiceAnalysisStore>();
            var refusedList = false;
            try { if (templates is not null) await templates.ListAsync(); } catch (ModuleUnavailableException) { refusedList = true; }
            var renamed = templates is null ? -1 : await templates.RenameSupplierAsync("1", "Vechi", "Nou");
            check(offRegistry.StatusOf("preluare-factura") == ModuleStatus.Disabled && refusedList && renamed == 0 && analysis is not null && analysis.Get(Guid.NewGuid()) is null && store is not null && store.Count == 0,
                "Invoices: with the module switched off the services still resolve (the supplier register, the settings page and the media endpoint that use them work), the rename of a supplier inside the templates changes nothing and the rest says the module is out");
        }
        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("preluare-factura") == ModuleStatus.Healthy, "Invoices: the application registers the invoice module (Startup/ServiceRegistration)");
    }

    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Invoices architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Invoices")), "Architecture: the invoice module lives in Modules/Invoices");
        var strays = new[] { "InvoiceAnalysisService.cs", "InvoiceTemplateStore.cs", "FileInvoiceTemplateStore.cs", "InvoiceLab.cs", "InvoiceXmlTree.cs", "InvoiceVariants.cs" }
            .Where(name => File.Exists(Path.Combine(root, "Services", "Invoices", name))).ToList();
        check(strays.Count == 0, "Architecture: the sources of the module are not left in Services/Invoices (" + string.Join("; ", strays) + ")");
        check(File.Exists(Path.Combine(root, "Services", "Invoices", "InvoicePdfReader.cs")) && File.Exists(Path.Combine(root, "Services", "Invoices", "InvoiceTableReader.cs")) && File.Exists(Path.Combine(root, "Services", "Invoices", "InvoiceTemplates.cs")),
            "Invoices: the reading and analysis building blocks shared with the Ocr module stay in Services/Invoices");

        var settings = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Settings.razor"));
        check(Regex.IsMatch(settings, @"<ModuleGate ModuleId=""@BlazorStoc\.Modules\.Invoices\.InvoicesModule\.ModuleId"">\s*@SubtabBar\(InvoiceSubtabs[\s\S]*?</ModuleGate>\s*</div>\s*@if \(activeTab == AnafTab"),
            "Invoices: the Setari -> Facturi tab (workbench and list of templates) is inside the gate of the module");
        var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(Regex.IsMatch(layout, @"@if \(Modules\.IsAvailable\(BlazorStoc\.Modules\.Invoices\.InvoicesModule\.ModuleId\)\) \{\s*<AuthorizeView Policy=""perm:preluare-factura\.view"">"), "Invoices: the menu entry is shown only while the module works");
        var dashboard = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        check(dashboard.Contains("<ModuleTile ModuleId=\"preluare-factura\" Policy=\"perm:preluare-factura.view\""), "Invoices: the tile of the main page belongs to the module (grey while it is out)");
        var picker = File.ReadAllText(Path.Combine(root, "Components", "Shared", "EntryOriginPicker.razor"));
        check(Regex.IsMatch(picker, @"@if \(Modules\.IsAvailable\(BlazorStoc\.Modules\.Invoices\.InvoicesModule\.ModuleId\)\) \{ <a class=""refresh act-confirm origin-pdf"""), "Invoices: the \"Preia din PDF\" link of the entry form is shown only while the module works");
        var suppliers = File.ReadAllText(Path.Combine(root, "Modules", "Suppliers", "MariaSupplierRepository.cs"));
        check(suppliers.Contains("BlazorStoc.Modules.ModuleUnavailableException"), "Invoices: renaming a supplier does not fail because the invoice module is out (the register ignores a module that is out)");
    }
}
