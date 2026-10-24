using BlazorStoc.Services;

namespace BlazorStoc.Modules.Invoices;

// The invoice pickup module: taking an invoice over into the stock (the analysis of a PDF or XML invoice, the review and the entry), the session store of the analyses in progress
// and the invoice templates (Setari -> Facturi: the workbench that makes them and the list that manages them). The reading of the pages (OCR) belongs to the Ocr module and the
// reading/analysis building blocks (grid, fields, vocabulary, template model) are shared by both, so they stay in Services/Invoices. The register of supplier invoices (/facturi),
// the entries tied to an invoice and the suppliers stay with the stock and the suppliers: they only ask the module to rename a supplier inside its templates.
public sealed class InvoicesModule : IModule
{
    public const string ModuleId = "preluare-factura";

    public string Id => ModuleId;
    public string Name => "Preluare factură";
    public Version Version { get; } = new(1, 1, 0);
    // The entry sits in the Products menu (MainLayout), which is shown only while the module works; no item of its own here.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // The store of the analyses is an in-memory dictionary: nothing to guard.
        services.AddSingleton<IInvoiceAnalysisStore, InvoiceAnalysisStore>();
        services.AddScoped<IInvoiceAnalysisService>(provider => new GuardedInvoiceAnalysisService(
            ActivatorUtilities.CreateInstance<InvoiceAnalysisService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddSingleton<InvoiceLabSettings>();
        services.AddScoped<IInvoiceTemplateStore, MariaInvoiceTemplateStore>();
        services.AddScoped<IInvoiceTemplateService>(provider => new GuardedInvoiceTemplateService(
            ActivatorUtilities.CreateInstance<InvoiceTemplateService>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: the supplier repository (which renames a supplier inside the templates), the settings page and the media endpoint still inject these.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddSingleton<IInvoiceAnalysisStore, InvoiceAnalysisStore>();
        services.AddScoped<IInvoiceAnalysisService, UnavailableInvoiceAnalysisService>();
        services.AddScoped<IInvoiceTemplateService, UnavailableInvoiceTemplateService>();
    }
}
