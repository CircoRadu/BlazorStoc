using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Suppliers;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The suppliers module: the guarded repositories behind the circuit breaker, the registration, the stand-ins while it is out, the pages marked with the module and the
// consumers outside it (the menu, the dashboard, the origin of a stock entry, the pickup of an invoice) that keep working without it.
public static class SupplierModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class ThrowingSuppliers : ISupplierRepository
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("db down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<Supplier>>();
        public Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default) => Fail<Supplier?>();
        public Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default) => Fail<Supplier>();
        public Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default) => Fail<Supplier>();
        public Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
        public Task<Supplier> AddAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => Fail<Supplier>();
        public Task<Supplier> RemoveAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => Fail<Supplier>();
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
        registry.Add(new SuppliersModule(), new ServiceCollection(), Empty());
        var inner = new ThrowingSuppliers();
        var guarded = new GuardedSupplierRepository(inner, registry);
        for (var i = 0; i < ModuleRegistry.FailureThreshold; i++)
        {
            try { await guarded.GetSuppliersAsync(); } catch (InvalidOperationException) { }
        }
        check(registry.StatusOf(SuppliersModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == ModuleRegistry.FailureThreshold,
            "Suppliers: repeated faults of the repository open the circuit of the module");
        var unavailable = 0;
        foreach (var call in new Func<Task>[]
        {
            () => guarded.GetSuppliersAsync(), () => guarded.GetAsync(1), () => guarded.CreateAsync(new()), () => guarded.UpdateAsync(null!, new()),
            () => guarded.DeleteAsync(null!, "x"), () => guarded.AddAliasAsync(null!, "x"), () => guarded.RemoveAliasAsync(null!, "x")
        })
        {
            try { await call(); } catch (ModuleUnavailableException) { unavailable++; }
        }
        check(unavailable == 7 && inner.Calls == ModuleRegistry.FailureThreshold, "Suppliers: with the circuit open all seven entry points refuse at once and the repository is not called");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await guarded.GetSuppliersAsync();
        check(registry.StatusOf(SuppliersModule.ModuleId) == ModuleStatus.Healthy, "Suppliers: after the cooldown the first successful call brings the module back");
        inner.Failure = new SupplierOperationException("CUI already used");
        for (var i = 0; i < 10; i++) { try { await guarded.CreateAsync(new()); } catch (SupplierOperationException) { } }
        inner.Failure = new SupplierInvoiceOperationException("number already used");
        for (var i = 0; i < 10; i++) { try { await guarded.CreateAsync(new()); } catch (SupplierInvoiceOperationException) { } }
        inner.Failure = new AccessDeniedException("no");
        for (var i = 0; i < 10; i++) { try { await guarded.DeleteAsync(null!, "x"); } catch (AccessDeniedException) { } }
        check(registry.StatusOf(SuppliersModule.ModuleId) == ModuleStatus.Healthy, "Suppliers: answers of the module (operation exceptions, access denied) are not faults");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        ServiceProvider Build(params (string Key, string Value)[] values)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value)).Build();
            var services = new ServiceCollection();
            var registry = new ModuleRegistry();
            services.AddSingleton(registry);
            registry.Add(new SuppliersModule(), services, configuration);
            return services.BuildServiceProvider();
        }
        // Switched off from the configuration: the stand-ins resolve (the pages are created) and every call says the module is out.
        using var off = Build(("Modules:furnizori:Enabled", "false"));
        using var scope = off.CreateScope();
        var told = 0;
        try { await scope.ServiceProvider.GetRequiredService<ISupplierRepository>().GetSuppliersAsync(); } catch (ModuleUnavailableException) { told++; }
        try { await scope.ServiceProvider.GetRequiredService<ISupplierInvoiceRepository>().GetAllAsync(); } catch (ModuleUnavailableException) { told++; }
        try { await scope.ServiceProvider.GetRequiredService<ISupplierProductCodes>().GetAsync(1); } catch (ModuleUnavailableException) { told++; }
        try { await scope.ServiceProvider.GetRequiredService<ISupplierRecognitionLog>().SummaryAsync(); } catch (ModuleUnavailableException) { told++; }
        check(told == 4, "Suppliers: with the module switched off the four services still resolve and say that the module is out");

        var module = new SuppliersModule();
        check(module.Id == "furnizori" && module.Nav.Count == 0, "Suppliers: the module has no menu item of its own (the entries sit in Administrare and follow the module)");
        check(!ModuleRegistry.IsFault(new SupplierOperationException("x")) && !ModuleRegistry.IsFault(new SupplierInvoiceOperationException("x")),
            "Suppliers: the operation exceptions of the module are answers, not faults");

        // The registrations of the application: the module is the only one that registers the four services.
        var registrations = new ServiceCollection();
        var applicationRegistry = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddCoreServices(registrations);
        BlazorStoc.Startup.ServiceRegistration.AddModules(registrations, Empty(), applicationRegistry);
        check(new[] { typeof(ISupplierRepository), typeof(ISupplierInvoiceRepository), typeof(ISupplierProductCodes), typeof(ISupplierRecognitionLog) }
            .All(type => registrations.Count(descriptor => descriptor.ServiceType == type) == 1 && registrations.Last(descriptor => descriptor.ServiceType == type).ImplementationFactory is not null),
            "Suppliers: in the registrations of the application the four services are registered once, by the module");
        check(applicationRegistry.StatusOf(SuppliersModule.ModuleId) == ModuleStatus.Healthy, "Suppliers: the module is registered and healthy in the application");
    }

    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Suppliers: the project folder is found"); return; }
        var pages = typeof(ModuleAttribute).Assembly.GetTypes().Where(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name is "Suppliers" or "SupplierDetail" or "Invoices").ToList();
        check(pages.Count == 3 && pages.All(page => page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == "furnizori"),
            "Suppliers: the three pages (suppliers, the file of a supplier, the register of invoices) are marked with the module, so the router replaces them while it is out");
        var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(Regex.IsMatch(layout, @"Modules\.IsAvailable\(BlazorStoc\.Modules\.Suppliers\.SuppliersModule\.ModuleId\)[\s\S]{0,200}href=""/furnizori"""), "Suppliers: the menu entries Furnizori and Facturi appear only while the module works");
        var dashboard = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        check(Regex.Matches(dashboard, @"<ModuleGate ModuleId=""@BlazorStoc\.Modules\.Suppliers\.SuppliersModule\.ModuleId"" Notice=""false"">").Count == 2, "Suppliers: the two tiles of the main page are in a gate of the module");
        var strays = Directory.GetFiles(Path.Combine(root, "Services"), "*.cs").Select(Path.GetFileName).Where(name => name is not null && (name.StartsWith("MariaSupplier", StringComparison.Ordinal) || name.StartsWith("Supplier", StringComparison.Ordinal) || name == "CachedSupplierRepository.cs")).ToList();
        check(strays.Count == 0, "Suppliers: the sources of the module are not left in Services (" + string.Join(", ", strays) + ")");
        var picker = File.ReadAllText(Path.Combine(root, "Components", "Shared", "EntryOriginPicker.razor"));
        check(picker.Contains("SuppliersUp") && picker.Contains("ModuleUnavailableException"), "Suppliers: the origin of a stock entry offers only the free entry and refuses an invoice cleanly while the module is out");
        var pickup = File.ReadAllText(Path.Combine(root, "Modules", "Invoices", "InvoicePickup.razor"));
        check(pickup.Contains("IsAvailable(\"furnizori\")") && !pickup.Contains("BlazorStoc.Modules.Suppliers"), "Suppliers: the pickup of an invoice says that suppliers are out (by id, without referring to the module)");
    }
}
