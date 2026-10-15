using BlazorStoc.Services;

namespace BlazorStoc.Modules.Inventory;

// The inventory module: the report to count against (generation and PDF) and the take-over of the counted sheet (review against the stock, then applying the chosen lines).
// It uses the stock, the safety backup and the OCR module (which reads the scanned sheet) but nothing in the stock calls it, so while it is out only its own pages are lost.
// The layout shared with the OCR reading (InventoryPdfLayout) and the OCR engine itself stay in Services.
public sealed class InventoryModule : IModule
{
    public const string ModuleId = "inventar";

    public string Id => ModuleId;
    public string Name => "Inventar";
    public Version Version { get; } = new(1, 0, 0);
    // The menu section (two entries: report and take-over) stays in MainLayout and is shown only while the module works; the permission modules are inventar and inventar-preluare.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInventoryReportBuilder>(provider => new GuardedInventoryReportBuilder(
            ActivatorUtilities.CreateInstance<InventoryReportBuilder>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddSingleton<IInventoryPdfWriter, InventoryPdfWriter>();
        services.AddScoped<IInventoryPickupBuilder>(provider => new GuardedInventoryPickupBuilder(
            ActivatorUtilities.CreateInstance<InventoryPickupBuilder>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IInventoryPickupApplier>(provider => new GuardedInventoryPickupApplier(
            ActivatorUtilities.CreateInstance<InventoryPickupApplier>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }
}
