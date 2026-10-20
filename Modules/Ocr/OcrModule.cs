using BlazorStoc.Services;

namespace BlazorStoc.Modules.Ocr;

// The OCR as a module: no pages and no menu entry of its own, only the readers other pages use. Ocr:Mode = Worker (default) runs every read in a worker process;
// InProcess runs the engine inside the web application as before (a way back, and what the in-memory checks use).
public sealed class OcrModule : IModule
{
    public string Id => OcrRules.ModuleId;
    public string Name => "Recunoastere documente (OCR)";
    public Version Version { get; } = new(1, 0, 0);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        if (string.Equals(configuration["Ocr:Mode"], "InProcess", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IInvoicePdfReader, InvoicePdfReader>();
            services.AddSingleton<IInventoryPickupOcrService, InventoryPickupOcrService>();
            return;
        }
        services.AddSingleton(provider => new OcrWorkerRunner(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<ILoggerFactory>().CreateLogger("BlazorStoc.Ocr")));
        services.AddSingleton<IInvoicePdfReader>(provider => new OutOfProcessInvoicePdfReader(provider.GetRequiredService<OcrWorkerRunner>(),
            provider.GetRequiredService<ModuleRegistry>(), provider.GetRequiredService<IWebHostEnvironmentTessdataPath>()));
        services.AddSingleton<IInventoryPickupOcrService>(provider => new OutOfProcessInventoryPickupOcrService(provider.GetRequiredService<OcrWorkerRunner>(),
            provider.GetRequiredService<ModuleRegistry>(), provider.GetRequiredService<IWebHostEnvironmentTessdataPath>()));
    }

    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddSingleton<IInvoicePdfReader, UnavailableInvoicePdfReader>();
        services.AddSingleton<IInventoryPickupOcrService, UnavailableInventoryPickupOcrService>();
    }
}
