using BlazorStoc.Services;

namespace BlazorStoc.Modules.RiskAnalyses;

// The risk analyses module (pilot of the module contract): the repository (behind the circuit breaker), the reader of the notification and its source.
public sealed class RiskAnalysesModule : IModule
{
    public const string ModuleId = "analize-risc";

    public string Id => ModuleId;
    public string Name => "Analize de risc";
    public Version Version { get; } = new(1, 0, 1);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [new("/analize-risc", "Analize de risc", "⚠", ModuleId)];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRiskAnalysisRepository>(provider => new GuardedRiskAnalysisRepository(
            ActivatorUtilities.CreateInstance<MariaRiskAnalysisRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IRiskAnalysisNotificationReader>(provider => new GuardedRiskAnalysisNotificationReader(
            ActivatorUtilities.CreateInstance<MariaRiskAnalysisNotificationReader>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-in used while the module is out: the file of a beneficiary and the main page still inject the repository.
    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IRiskAnalysisRepository, UnavailableRiskAnalysisRepository>();

    public void RegisterNotificationSources(IServiceCollection services) =>
        services.AddScoped<IExpirySource>(provider => new RiskAnalysisExpirySource(provider.GetRequiredService<IRiskAnalysisNotificationReader>()));
}
