using System.Threading.RateLimiting;
using BlazorStoc.Components;
using BlazorStoc.Modules;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Modules.Anaf;
using BlazorStoc.Modules.Backup;
using BlazorStoc.Modules.Inventory;
using BlazorStoc.Modules.Invoices;
using BlazorStoc.Modules.Journal;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Modules.Notifications;
using BlazorStoc.Modules.Ocr;
using BlazorStoc.Modules.Offers;
using BlazorStoc.Modules.ProjectObservations;
using BlazorStoc.Modules.ProjectReports;
using BlazorStoc.Modules.RiskAnalyses;
using BlazorStoc.Modules.Suppliers;
using BlazorStoc.Modules.Vehicles;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorStoc.Startup;

// The service registrations of the application, one method per module (the same split as docs/HARTA_COD.md). A repository whose constructor
// takes only registered services (configuration, access control, journal, archive...) is registered by type; a factory remains only where the
// constructor needs a value of its own (a path, a kind, a wrapped repository).
internal static class ServiceRegistration
{
    public static IServiceCollection AddWebAndSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddRazorPages();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = "BlazorStoc.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });
        services.AddCascadingAuthenticationState();
        var keysPath = DataProtectionKeys.PathFor(configuration);
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("BlazorStoc");
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("Prea multe încercări. Așteaptă un minut și încearcă din nou.", token);
            };
            options.AddPolicy("login", LoginRateLimit.Partition);
        });
        return services;
    }

    // Access, journal, archive, clock, schema and the other services every module relies on.
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<MariaSchemaMigrator>();
        services.AddHttpContextAccessor();
        services.AddSingleton<IUserPermissions, MariaUserPermissions>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAccessControl, CurrentUserAccess>();
        services.AddScoped<IArchiveService, ArchiveService>();
        services.AddSingleton<IAuditTrail, MariaAuditTrail>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddUserServices(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, MariaUserRepository>();
        services.AddScoped<IUserTypeRepository, MariaUserTypeRepository>();
        services.AddScoped<IUserAuthenticator>(provider => (IUserAuthenticator)provider.GetRequiredService<IUserRepository>());
        return services;
    }

    // Products, stock movements, reservations, stock alerts and the consumption note.
    public static IServiceCollection AddStockServices(this IServiceCollection services)
    {
        services.AddScoped<IProductRepository, MariaProductRepository>();
        services.AddScoped<ISubcategoryTree>(provider => (MariaProductRepository)provider.GetRequiredService<IProductRepository>());
        services.AddScoped<ICatalogSettings, MariaCatalogSettings>();
        services.AddScoped<IProductParameterRepository, MariaProductParameterRepository>();
        services.AddScoped<IProductTagRepository, MariaProductTagRepository>();
        services.AddScoped<IDefinitionRelocation, MariaDefinitionRelocation>();
        services.AddScoped<IStockMovementRepository, MariaStockMovementRepository>();
        services.AddScoped<IReservationRepository, MariaReservationRepository>();
        services.AddScoped<IProductPlacementReader, MariaProductPlacementReader>();
        services.AddScoped<ISystemTypeRepository, MariaSystemTypeRepository>();
        services.AddScoped<IProductMinStockRepository, MariaProductMinStockRepository>();
        services.AddScoped<IProjectDeadlineRepository, MariaProjectDeadlineRepository>();
        services.AddSingleton<IConsumptionNotePdfWriter, ConsumptionNotePdfWriter>();
        services.AddSingleton<IProductImageStore, FileProductImageStore>();
        services.AddSingleton<IWebHostEnvironmentTessdataPath, TessdataPath>();
        // IInventoryPickupOcrService and IInvoicePdfReader are registered by the Ocr module (Modules/Ocr): in a worker process or in-process (Ocr:Mode).
        return services;
    }

    // Beneficiaries and components (the offers and the project situation are modules). The project and its files are in AddChangeTracking (they notify the other sessions).
    public static IServiceCollection AddProjectServices(this IServiceCollection services)
    {
        services.AddScoped<IBeneficiaryRepository, MariaBeneficiaryRepository>();
        services.AddScoped<IProjectComponentRepository, MariaProjectComponentRepository>();
        return services;
    }

    // The modules (see Modules/): each registers itself through IModule; one that fails to start is disabled and the others go on.
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration, ModuleRegistry registry)
    {
        services.AddSingleton(registry);
        // The modules the administrator stopped by hand (module_state) stay stopped after a restart; the switch itself is ModuleControl (Setari -> Module).
        services.AddSingleton<IModuleStateStore, MariaModuleStateStore>();
        services.AddHostedService<ModuleStateRestorer>();
        services.AddScoped<ModuleControl>();
        registry.Add(new OcrModule(), services, configuration);
        registry.Add(new BackupModule(), services, configuration);
        // The order is the order of the menu entries and of the notification sources: vehicles, offers, maintenance, risk analyses (as before the modules).
        registry.Add(new VehiclesModule(), services, configuration);
        registry.Add(new OffersModule(), services, configuration);
        registry.Add(new ProjectReportsModule(), services, configuration);
        registry.Add(new InventoryModule(), services, configuration);
        registry.Add(new AnafModule(), services, configuration);
        registry.Add(new ActivityImportModule(), services, configuration);
        registry.Add(new InvoicesModule(), services, configuration);
        registry.Add(new SuppliersModule(), services, configuration);
        registry.Add(new NotificationsModule(), services, configuration);
        registry.Add(new MaintenanceModule(), services, configuration);
        registry.Add(new RiskAnalysesModule(), services, configuration);
        registry.Add(new JournalModule(), services, configuration);
        registry.Add(new ProjectObservationsModule(), services, configuration);
        return services;
    }

    // Work points and their photos (the contracts, the interventions and the map belong to the maintenance module).
    public static IServiceCollection AddMaintenanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkPointRepository, MariaWorkPointRepository>();
        services.AddScoped<IServicePhotoStore, MariaServicePhotoStore>();
        return services;
    }

    // The notification templates and the sources that raise them. The registration order of the IExpirySource entries is the order the sources are evaluated in.
    public static IServiceCollection AddNotificationServices(this IServiceCollection services, ModuleRegistry modules)
    {
        // The sources of the modules (vehicles, maintenance, risk analyses) are registered here, before the awaited invoices.
        modules.AddNotificationSources(services);
        services.AddScoped<IAwaitedEntryReader, MariaAwaitedEntryReader>();
        services.AddScoped<IExpirySource, AwaitedInvoiceSource>();
        services.AddScoped<IOverStockReader, MariaOverStockReader>();
        services.AddScoped<IExpirySource, OverStockSource>();
        services.AddScoped<IStockAlertReader, MariaStockAlertReader>();
        services.AddScoped<IExpirySource, MinStockSource>();
        services.AddScoped<IExpirySource, StaleReservationSource>();
        services.AddScoped<IExpirySource, ProjectDeficitSource>();
        services.AddScoped<IBackupAlertReader, MariaBackupAlertReader>();
        services.AddScoped<IExpirySource, BackupMissingSource>();
        services.AddScoped<IExpirySource, NasCopyMissingSource>();
        services.AddScoped<IExpirySource, ClockSkewSource>();
        return services;
    }

    // Backup, NAS copy, retention, the daily scheduler and the restoration, all behind one shared operation lock.
    public static IServiceCollection AddBackupServices(this IServiceCollection services, IConfiguration configuration)
    {
        // One shared lock file in the MariaDB backup directory; singleton because the lock and its heartbeat must be the same instance across the whole app, not
        // re-created per request.
        var operationLockPath = MariaAssetPaths.OperationLock(configuration);
        services.AddSingleton<IOperationLockService>(provider => new FileOperationLockService(operationLockPath,
            provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<ILogger<FileOperationLockService>>()));
        // While that lock is held, every session but the one holding it is refused at the data-access layer and redirected to
        // the waiting page (MaintenanceGate, MaintenanceSupport).
        MaintenanceGate.Configure(operationLockPath);
        // The backup service, the NAS copier and the restoration are registered by the backup module (Modules/Backup): they run in a worker process, or in the
        // application when Backup:Mode is InProcess. Here stay the lock, the settings of the schedule and of the share, the retention and the daily schedule.
        services.AddScoped<INasBackupSettingsRepository, MariaNasBackupStore>();
        services.AddScoped<IBackupSettingsRepository, MariaBackupSettingsStore>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();
        services.AddHostedService<BackupScheduler>();
        return services;
    }

    // What lets one session learn about another's changes (feed, SignalR, relay of the database events), the per-circuit state used by the pages
    // and the repositories that announce their changes to the other sessions.
    public static IServiceCollection AddChangeTracking(this IServiceCollection services)
    {
        services.AddSingleton<InProcessChangeFeed>();
        services.AddSingleton<IChangeFeed>(provider => provider.GetRequiredService<InProcessChangeFeed>());
        services.AddSingleton<IChangeEventSource, MariaChangeEventSource>();
        services.AddHostedService<ChangeEventRelay>();
        services.AddSignalR();
        services.AddHostedService<SignalRChangeBroadcaster>();
        services.AddScoped<ChangeOrigin>();
        services.AddScoped<UnsavedChanges>();
        services.AddScoped<ExitFormDraft>();
        services.AddScoped<ListNavigationContext>();
        services.AddScoped<IProductLockRepository>(provider => new ChangeNotifyingProductLockRepository(new MariaProductLockRepository(provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>()),
            provider.GetRequiredService<IChangeFeed>(), provider.GetRequiredService<ChangeOrigin>()));
        services.AddScoped<IProjectFileStore>(provider => new ChangeNotifyingProjectFileStore(new MariaProjectFileStore(provider.GetRequiredService<IWebHostEnvironment>(),
                provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IArchiveService>(),
                provider.GetRequiredService<IAuditTrail>()),
            provider.GetRequiredService<IChangeFeed>(), provider.GetRequiredService<ChangeOrigin>()));
        services.AddScoped<IProjectRepository>(provider => new ChangeNotifyingProjectRepository(new MariaProjectRepository(provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IWebHostEnvironment>(), provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(),
                provider.GetRequiredService<IArchiveService>(), provider.GetRequiredService<IProjectFileStore>()),
            provider.GetRequiredService<IChangeFeed>(), provider.GetRequiredService<ChangeOrigin>()));
        return services;
    }
}
