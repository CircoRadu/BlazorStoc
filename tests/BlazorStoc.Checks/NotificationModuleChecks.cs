using System.Text.RegularExpressions;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Notifications;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The notifications module: its guarded entry points behind the circuit breaker, the conveniences of the other pages (threshold, number of warnings, evaluation) that answer a default
// instead of failing, the registration, the stand-in while it is out, the page marked with the module and the places of the core that show its entries only while it works.
public static class NotificationModuleChecks
{
    private static IConfiguration Empty() => new ConfigurationBuilder().Build();

    private sealed class FakeService : IExpiryNotificationService
    {
        public int Calls;
        public Exception? Failure = new InvalidOperationException("notifications down");
        private Task<T> Fail<T>() { Calls++; return Failure is null ? Task.FromResult(default(T)!) : Task.FromException<T>(Failure); }
        public IReadOnlyList<IExpirySource> Sources => [];
        public Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<NotificationTemplate>>();
        public Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default) => Fail<NotificationTemplate>();
        public Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default) => Fail<NotificationTemplate>();
        public Task DeleteTemplateAsync(NotificationTemplate original, string reason, CancellationToken cancellationToken = default) => Fail<object>();
        public Task<int> GetThresholdDaysAsync(string sourceKey, int defaultDays, CancellationToken cancellationToken = default) => Fail<int>();
        public Task<int> CountOpenNotificationsAsync(NotificationTemplate template, CancellationToken cancellationToken = default) => Fail<int>();
        public Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default) => Fail<object>();
        public Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<NotificationView>>();
        public Task<IReadOnlyList<ResolvedNotificationView>> GetResolvedViewsAsync(CancellationToken cancellationToken = default) => Fail<IReadOnlyList<ResolvedNotificationView>>();
        public Task<int> AlertCountAsync(CancellationToken cancellationToken = default) => Fail<int>();
        public Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Fail<NotificationSettings>();
        public Task<NotificationPurgePlan> PreviewPurgeAsync(int months, CancellationToken cancellationToken = default) => Fail<NotificationPurgePlan>();
        public Task<NotificationSettingsSaved> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, CancellationToken cancellationToken = default) => Fail<NotificationSettingsSaved>();
        public Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default) => Fail<ExpiryNotification>();
        public Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default) => Fail<ExpiryNotification>();
        public Task<ExpiryNotification> ResolveAsync(NotificationView view, CancellationToken cancellationToken = default) => Fail<ExpiryNotification>();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await GuardedAsync(check);
        await WiringAsync(check);
        Architecture(check);
    }

    private static async Task GuardedAsync(Action<bool, string> check)
    {
        var clock = new ManualTimeProvider();
        var registry = new ModuleRegistry(clock);
        registry.Add(new NotificationsModule(), new ServiceCollection(), Empty());
        var inner = new FakeService();
        var service = new GuardedExpiryNotificationService(inner, registry);

        // The conveniences of the other pages: a failing engine gives their default and never an exception; each fault still counts for the circuit.
        check(await service.GetThresholdDaysAsync("vehicul.itp", 30) == 30 && await service.AlertCountAsync() == 0, "Notifications: a failing engine gives the default threshold and no warnings instead of failing the page that asks");
        await service.EvaluateAsync(TimeSpan.FromMinutes(5));
        check(registry.StatusOf(NotificationsModule.ModuleId) == ModuleStatus.Disabled && inner.Calls == 3,
            "Notifications: the faults of the three conveniences count for the circuit, which opens");
        var callsBefore = inner.Calls;
        check(await service.GetThresholdDaysAsync("x", 7) == 7 && await service.AlertCountAsync() == 0 && inner.Calls == callsBefore,
            "Notifications: with the circuit open the conveniences answer their default at once and the engine is not called");

        var refused = 0;
        foreach (var call in new Func<Task>[]
        {
            () => service.GetTemplatesAsync(), () => service.CreateTemplateAsync(null!), () => service.UpdateTemplateAsync(null!, null!), () => service.DeleteTemplateAsync(null!, "x"),
            () => service.CountOpenNotificationsAsync(null!), () => service.GetViewsAsync(), () => service.GetResolvedViewsAsync(), () => service.GetSettingsAsync(), () => service.PreviewPurgeAsync(6),
            () => service.SaveSettingsAsync(null!, true, 6), () => service.AcknowledgeAsync(null!), () => service.SnoozeAsync(null!, 1), () => service.ResolveAsync(null!)
        })
        {
            try { await call(); } catch (ModuleUnavailableException) { refused++; }
        }
        check(refused == 13 && inner.Calls == callsBefore, "Notifications: with the circuit open the thirteen other entry points refuse at once and the engine is not called");

        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        inner.Failure = null;
        await service.GetViewsAsync();
        check(registry.StatusOf(NotificationsModule.ModuleId) == ModuleStatus.Healthy, "Notifications: after the cooldown the first successful call brings the module back");
        foreach (Exception answer in new Exception[] { new NotificationOperationException("template already exists"), new AccessDeniedException("no") })
        {
            inner.Failure = answer;
            for (var i = 0; i < 10; i++) { try { await service.CreateTemplateAsync(null!); } catch (Exception ex) when (ex.GetType() == answer.GetType()) { } }
        }
        check(registry.StatusOf(NotificationsModule.ModuleId) == ModuleStatus.Healthy, "Notifications: a refused template (NotificationOperationException) and access denied reach the user unchanged and never disable the module");
    }

    private static async Task WiringAsync(Action<bool, string> check)
    {
        var registry = new ModuleRegistry();
        var services = new ServiceCollection();
        registry.Add(new NotificationsModule(), services, Empty());
        check(services.Any(d => d.ServiceType == typeof(IExpiryNotificationService)) && services.Any(d => d.ServiceType == typeof(IExpiryNotificationRepository)),
            "Notifications: the module registers the engine and its repository");
        var module = new NotificationsModule();
        check(module.Id == "notificari" && module.Nav.Count == 0, "Notifications: the module keeps the permission modules (notificari, setari-notificari); its menu entry (with the warning triangle) stays in MainLayout and is shown only while the module works");
        var page = typeof(ModuleAttribute).Assembly.GetTypes().Single(type => type.Namespace == "BlazorStoc.Components.Pages" && type.Name == "Notifications");
        check(page.GetCustomAttributes(typeof(ModuleAttribute), false).Cast<ModuleAttribute>().SingleOrDefault()?.ModuleId == module.Id, "Notifications: the page is marked with the module, so the router replaces it while it is out");

        var offRegistry = new ModuleRegistry();
        var offServices = new ServiceCollection();
        var offConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:notificari:Enabled"] = "false" }).Build();
        offRegistry.Add(new NotificationsModule(), offServices, offConfiguration);
        using (var provider = offServices.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetService<IExpiryNotificationService>();
            var refusedViews = false;
            try { if (service is not null) await service.GetViewsAsync(); } catch (ModuleUnavailableException) { refusedViews = true; }
            check(offRegistry.StatusOf("notificari") == ModuleStatus.Disabled && service is not null && refusedViews && await service.GetThresholdDaysAsync("x", 14) == 14 && await service.AlertCountAsync() == 0 && service.Sources.Count == 0,
                "Notifications: with the module switched off the service still resolves (the layout, the main page, the threshold readers and the polling endpoint work), answers the defaults and the rest says the module is out");
            await service.EvaluateAsync(TimeSpan.FromMinutes(5));
        }
        var application = new ModuleRegistry();
        BlazorStoc.Startup.ServiceRegistration.AddModules(new ServiceCollection(), Empty(), application);
        check(application.StatusOf("notificari") == ModuleStatus.Healthy, "Notifications: the application registers the notifications module (Startup/ServiceRegistration)");
    }

    private static void Architecture(Action<bool, string> check)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        if (root is null) { check(false, "Notifications architecture: the project folder is found"); return; }
        check(Directory.Exists(Path.Combine(root, "Modules", "Notifications")), "Architecture: the notifications module lives in Modules/Notifications");
        var contract = File.ReadAllText(Path.Combine(root, "Services", "ExpiryNotifications.cs"));
        check(contract.Contains("public interface IExpirySource") && contract.Contains("public interface IExpiryNotificationService") && !contract.Contains("class ExpiryNotificationService"),
            "Notifications: the contract of the sources and of the service stays in the core (Services/ExpiryNotifications.cs); only the engine is in the module");
        var strays = new[] { "MariaExpiryNotificationRepository.cs", "DefaultNotificationTemplates.cs" }.Where(name => File.Exists(Path.Combine(root, "Services", name))).ToList();
        check(strays.Count == 0, "Architecture: the sources of the module are not left in Services (" + string.Join("; ", strays) + ")");
        var settings = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Settings.razor"));
        check(Regex.IsMatch(settings, @"<ModuleGate ModuleId=""@BlazorStoc\.Modules\.Notifications\.NotificationsModule\.ModuleId"">\s*@SubtabBar\(NotificationSubtabs[\s\S]*?</ModuleGate>\s*</div>"),
            "Notifications: the templates and settings tabs of Setari are inside the gate of the module");
        var layout = File.ReadAllText(Path.Combine(root, "Components", "Layout", "MainLayout.razor"));
        check(Regex.IsMatch(layout, @"@if \(Modules\.IsAvailable\(BlazorStoc\.Modules\.Notifications\.NotificationsModule\.ModuleId\)\)\s*\{\s*<AuthorizeView Policy=""perm:notificari\.view"">"), "Notifications: the menu entry is shown only while the module works");
        var dashboard = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Dashboard.razor"));
        check(dashboard.Contains("<ModuleTile ModuleId=\"notificari\" Policy=\"perm:notificari.view\""), "Notifications: the tile of the main page belongs to the module (grey while it is out)");
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        check(program.Contains("IsAvailable(BlazorStoc.Modules.Notifications.NotificationsModule.ModuleId))\ntry") || program.Contains("IsAvailable(BlazorStoc.Modules.Notifications.NotificationsModule.ModuleId))\r\ntry"),
            "Notifications: the starting templates are made only while the module works (the application starts without the module)");
    }
}
