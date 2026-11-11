using BlazorStoc.Services;

namespace BlazorStoc.Modules.EspImport;

// The stock import module: takes the products with a positive quantity (and their images) from an export of the old ESPStoc database into the BlazorStoc catalog, as the point zero of the stock
// keeping (docs/PROPUNERE_IMPORT_ESPSTOC.md). Setari -> Preluare stoc ESPStoc, administrator only. The module has no menu entry and no notification source of its own.
public sealed class EspImportModule : IModule
{
    public const string ModuleId = "import-espstoc";

    public string Id => ModuleId;
    public string Name => "Preluare stoc ESPStoc";
    public Version Version { get; } = new(0, 2, 1);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<EspAnalysisStore>();
        services.AddScoped<IEspImportService>(provider => new GuardedEspImportService(
            ActivatorUtilities.CreateInstance<MariaEspImportService>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IEspImportService, UnavailableEspImportService>();
}

internal sealed class GuardedEspImportService(IEspImportService inner, ModuleRegistry modules) : IEspImportService
{
    private const string Id = EspImportModule.ModuleId;
    public Task<string> SaveUploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveUploadAsync(body, fileName, cancellationToken));
    public Task<EspAnalysis> AnalyzeAsync(string path, string sourceName, bool deleteAfter, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AnalyzeAsync(path, sourceName, deleteAfter, cancellationToken));
    public Task<EspAnalysis?> GetCurrentAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetCurrentAsync(cancellationToken));
    public Task DiscardAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DiscardAsync(cancellationToken));
    public Task RefreshAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RefreshAsync(cancellationToken));
    public Task SetOmitAsync(int oldId, bool omit, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SetOmitAsync(oldId, omit, cancellationToken));
    public Task ChooseAsync(int oldId, string baseModel, IReadOnlyList<EspPick> picks, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ChooseAsync(oldId, baseModel, picks, cancellationToken));
    public Task SetFrozenAsync(int oldId, bool frozen, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SetFrozenAsync(oldId, frozen, cancellationToken));
    public Task<int> FreezeUnresolvedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.FreezeUnresolvedAsync(cancellationToken));
    public Task<EspPointZeroPreview> PreviewAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.PreviewAsync(cancellationToken));
    public Task<EspPointZeroState?> GetStateAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetStateAsync(cancellationToken));
    public Task<EspApplyResult> ApplyAsync(string confirmation, IProgress<string>? progress, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ApplyAsync(confirmation, progress, cancellationToken));
}

internal sealed class UnavailableEspImportService : IEspImportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(EspImportModule.ModuleId));
    public Task<string> SaveUploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default) => Out<string>();
    public Task<EspAnalysis> AnalyzeAsync(string path, string sourceName, bool deleteAfter, CancellationToken cancellationToken = default) => Out<EspAnalysis>();
    public Task<EspAnalysis?> GetCurrentAsync(CancellationToken cancellationToken = default) => Out<EspAnalysis?>();
    public Task DiscardAsync(CancellationToken cancellationToken = default) => Out<object>();
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Out<object>();
    public Task SetOmitAsync(int oldId, bool omit, CancellationToken cancellationToken = default) => Out<object>();
    public Task ChooseAsync(int oldId, string baseModel, IReadOnlyList<EspPick> picks, CancellationToken cancellationToken = default) => Out<object>();
    public Task SetFrozenAsync(int oldId, bool frozen, CancellationToken cancellationToken = default) => Out<object>();
    public Task<int> FreezeUnresolvedAsync(CancellationToken cancellationToken = default) => Out<int>();
    public Task<EspPointZeroPreview> PreviewAsync(CancellationToken cancellationToken = default) => Out<EspPointZeroPreview>();
    public Task<EspPointZeroState?> GetStateAsync(CancellationToken cancellationToken = default) => Out<EspPointZeroState?>();
    public Task<EspApplyResult> ApplyAsync(string confirmation, IProgress<string>? progress, CancellationToken cancellationToken = default) => Out<EspApplyResult>();
}
