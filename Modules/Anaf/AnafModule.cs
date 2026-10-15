using BlazorStoc.Services;

namespace BlazorStoc.Modules.Anaf;

// The ANAF module: the lookup of a company by CUI in the VAT register of ANAF (an external service) and its configurator (Setari -> ANAF: connection, request, field mapping,
// live test, versions). The editors of beneficiaries and suppliers ask for the lookup through IAnafService; the rules that apply an answer to a form (AnafApplyRules) and the
// dialog that shows the differences stay with those editors. ANAF answers "unavailable" instead of failing, so the circuit breaker counts those answers as faults: while the
// service is down a lookup is refused at once (nobody waits for the timeout of every attempt) and the editors say so.
public sealed class AnafModule : IModule
{
    public const string ModuleId = "anaf";

    public string Id => ModuleId;
    public string Name => "ANAF (date firme)";
    public Version Version { get; } = new(1, 0, 0);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(AnafService.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<AnafStore>();
        services.AddScoped<IAnafService>(provider => new GuardedAnafService(
            ActivatorUtilities.CreateInstance<AnafService>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-in used while the module is out: the editors of beneficiaries and suppliers still inject the service and treat its "unavailable" answer as they always did.
    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IAnafService, UnavailableAnafService>();
}
