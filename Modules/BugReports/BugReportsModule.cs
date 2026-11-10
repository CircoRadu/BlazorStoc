using BlazorStoc.Services;

namespace BlazorStoc.Modules.BugReports;

// The module of the reports of errors: the menu entry above "Ajutor" (MainLayout), the popup (Components/Shared/BugReportDialog.razor), the service (BugReports.cs) and
// Setari -> Rapoarte erori (Components/Shared/BugReportsPanel.razor).
public sealed class BugReportsModule : IModule
{
    public const string ModuleId = "rapoarte-erori";

    public string Id => ModuleId;
    public string Name => "Raportare erori";
    public Version Version { get; } = new(1, 4, 1);
    // The entry above "Ajutor" is written in MainLayout (it opens a popup, it is not a page), so the module has no NavItems.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBugReportService>(provider => new GuardedBugReportService(ActivatorUtilities.CreateInstance<MariaBugReportService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        // The button of the bar above every window: the page script (wwwroot/bug-report.js) photographs the window and opens #bug-dialog.
        services.AddDialogBarAction(new DialogBarAction("bug-report", ModuleId, 10, "Raportează o eroare", "Raportează o eroare în această fereastră", "⚑",
            new Dictionary<string, object> { ["data-bug-open"] = "", ["data-bug-popup"] = "", ["aria-haspopup"] = "dialog" }));
    }

    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IBugReportService, UnavailableBugReportService>();
}
