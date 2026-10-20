using BlazorStoc.Services;

namespace BlazorStoc.Modules.Offers;

// The offers module: taking over the Excel offers (.xlsx reading, matching of the lines to products, revisions) and the templates that describe the layout of an offer.
// The components of a project, the reservations and the exits stay in the stock flow; the project situation reads the offers through this module's repository, so while
// the module is out the situation says so instead of showing numbers without the offers.
public sealed class OffersModule : IModule
{
    public const string ModuleId = "oferte";

    public string Id => ModuleId;
    public string Name => "Oferte";
    public Version Version { get; } = new(1, 0, 0);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [new("/oferte", "Oferte", "▤", ModuleId)];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOfferRepository>(provider => new GuardedOfferRepository(
            ActivatorUtilities.CreateInstance<MariaOfferRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IOfferTemplateRepository>(provider => new GuardedOfferTemplateRepository(
            ActivatorUtilities.CreateInstance<MariaOfferTemplateRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-ins used while the module is out: the project situation still injects the offers.
    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IOfferRepository, UnavailableOfferRepository>();
        services.AddScoped<IOfferTemplateRepository, UnavailableOfferTemplateRepository>();
    }
}
