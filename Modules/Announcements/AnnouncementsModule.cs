using BlazorStoc.Services;

namespace BlazorStoc.Modules.Announcements;

// The module of the announcements: the window after the sign-in (AnnouncementPopup.razor, in MainLayout), the service (Announcements.cs, MariaAnnouncementService.cs), the cleaning of the
// formatted text (AnnouncementHtml.cs) and Setari -> Anunturi (AnnouncementsPanel.razor: editor, preview, drafts, publishing, the table, ZIP import/export).
public sealed class AnnouncementsModule : IModule
{
    public const string ModuleId = "anunturi";

    public string Id => ModuleId;
    public string Name => "Anunțuri";
    public Version Version { get; } = new(1, 3, 2);
    // The window opens by itself after the sign-in and the settings tile is written in Settings.razor; no item of its own here.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IAnnouncementService>(provider => new GuardedAnnouncementService(ActivatorUtilities.CreateInstance<MariaAnnouncementService>(provider), provider.GetRequiredService<ModuleRegistry>()));

    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IAnnouncementService, UnavailableAnnouncementService>();
}
