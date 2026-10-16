using BlazorStoc.Services;

namespace BlazorStoc.Modules.Suppliers;

// The suppliers module: the register of suppliers (CUI, aliases, ANAF data), the register of the invoices stock entries are taken from (/facturi), the links between
// the codes suppliers give to products and the catalog, and the log of how suppliers were recognised on invoices. The stock entries, the pickup of invoices and the
// notification "factura asteptata" keep their own code; they read the suppliers through the repositories below (stand-ins that say "module out" while it is out) and
// the tables of the module stay in the database, so the entries of stock still show their invoice and supplier when the module is switched off.
public sealed class SuppliersModule : IModule
{
    public const string ModuleId = "furnizori";

    public string Id => ModuleId;
    public string Name => "Furnizori și facturi de furnizor";
    public Version Version { get; } = new(1, 0, 0);
    // The entries sit in the Administration menu (MainLayout), which shows them only while the module works; no item of its own here.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISupplierRepository>(provider => new CachedSupplierRepository(new GuardedSupplierRepository(new MariaSupplierRepository(provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<IArchiveService>(),
            provider.GetRequiredService<IInvoiceTemplateService>()), provider.GetRequiredService<ModuleRegistry>())));
        services.AddScoped<ISupplierInvoiceRepository>(provider => new GuardedSupplierInvoiceRepository(
            ActivatorUtilities.CreateInstance<MariaSupplierInvoiceRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<ISupplierProductCodes>(provider => new GuardedSupplierProductCodes(
            ActivatorUtilities.CreateInstance<MariaSupplierProductCodes>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<ISupplierRecognitionLog>(provider => new GuardedSupplierRecognitionLog(
            ActivatorUtilities.CreateInstance<MariaSupplierRecognitionLog>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<ISupplierRepository, UnavailableSupplierRepository>();
        services.AddScoped<ISupplierInvoiceRepository, UnavailableSupplierInvoiceRepository>();
        services.AddScoped<ISupplierProductCodes, UnavailableSupplierProductCodes>();
        services.AddScoped<ISupplierRecognitionLog, UnavailableSupplierRecognitionLog>();
    }
}
