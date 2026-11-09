using BlazorStoc.Services;

namespace BlazorStoc.Modules.Proposals;

// The module of the proposals of changes and new features: the menu entry under "Raporteaza o eroare" (MainLayout), the popup (ProposalDialog.razor), the service (Proposals.cs),
// Setari -> Dezvoltare -> Propuneri (ProposalsPanel.razor) and the tab "Propuneri" of Notificari (ProposalAlertsTab.razor).
public sealed class ProposalsModule : IModule
{
    public const string ModuleId = "propuneri";

    public string Id => ModuleId;
    public string Name => "Propuneri";
    public Version Version { get; } = new(1, 0, 0);
    // The entry is written in MainLayout (it opens a popup, it is not a page), so the module has no NavItems.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IProposalService>(provider => new GuardedProposalService(ActivatorUtilities.CreateInstance<MariaProposalService>(provider), provider.GetRequiredService<ModuleRegistry>()));

    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IProposalService, UnavailableProposalService>();
}
