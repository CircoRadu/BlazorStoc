using BlazorStoc.Services;

namespace BlazorStoc.Modules.Notifications;

// The notifications module: the engine that evaluates every expiry source against the templates and keeps the notifications (taking over, snoozing, resolving, the clean-up of the old
// ones), the starting templates, the Notificari page and the templates and settings tabs of Setari. The contract of the sources (IExpirySource and its records) and the sources of the
// domains (vehicles, maintenance, risk analyses, stock, invoices, backup) are not part of it: a source keeps working for its own page whatever the engine does.
// Reading the notifications is a convenience of the other pages (the thresholds that colour the due dates, the number of warnings next to the menu entry), so those members
// answer a harmless default while the module is out or failing instead of failing the page.
public sealed class NotificationsModule : IModule
{
    public const string ModuleId = "notificari";

    public string Id => ModuleId;
    public string Name => "Notificări";
    public Version Version { get; } = new(1, 0, 0);
    // The menu entry has a warning triangle driven by the count; it stays in MainLayout and is shown only while the module works.
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IExpiryNotificationRepository, MariaExpiryNotificationRepository>();
        services.AddScoped<IExpiryNotificationService>(provider => new GuardedExpiryNotificationService(
            ActivatorUtilities.CreateInstance<ExpiryNotificationService>(provider), provider.GetRequiredService<ModuleRegistry>()));
    }

    // Stand-in used while the module is out: the layout, the main page, the pages that read a threshold and the polling endpoint still inject the service.
    public void RegisterUnavailable(IServiceCollection services) => services.AddScoped<IExpiryNotificationService, UnavailableExpiryNotificationService>();
}
