using System.Threading.RateLimiting;
using BlazorStoc.Components;
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
        // An explicit App:DataProtectionPath always wins; otherwise the dedicated local asset directory from the handoff document.
        var keysPath = configuration["App:DataProtectionPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "assets", "data-protection-keys");
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
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
        services.AddHttpClient(AnafService.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<AnafStore>();
        services.AddScoped<IAnafService, AnafService>();
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

    // Products, stock movements, reservations, stock alerts, consumption, inventory and its pickup.
    public static IServiceCollection AddStockServices(this IServiceCollection services)
    {
        services.AddScoped<IProductRepository, MariaProductRepository>();
        services.AddScoped<IProductParameterRepository, MariaProductParameterRepository>();
        services.AddScoped<IStockMovementRepository, MariaStockMovementRepository>();
        services.AddScoped<IReservationRepository, MariaReservationRepository>();
        services.AddScoped<IProductPlacementReader, MariaProductPlacementReader>();
        services.AddScoped<IVehicleRepository, MariaVehicleRepository>();
        services.AddScoped<IVehicleTargetRepository, MariaVehicleTargetRepository>();
        services.AddScoped<ISystemTypeRepository, MariaSystemTypeRepository>();
        services.AddScoped<IProductMinStockRepository, MariaProductMinStockRepository>();
        services.AddScoped<IProjectDeadlineRepository, MariaProjectDeadlineRepository>();
        services.AddScoped<IConsumptionReader, MariaConsumptionReader>();
        services.AddSingleton<IConsumptionNotePdfWriter, ConsumptionNotePdfWriter>();
        services.AddSingleton<IProductImageStore, FileProductImageStore>();
        services.AddScoped<IInventoryReportBuilder, InventoryReportBuilder>();
        services.AddSingleton<IInventoryPdfWriter, InventoryPdfWriter>();
        services.AddSingleton<IWebHostEnvironmentTessdataPath, TessdataPath>();
        services.AddSingleton<IInventoryPickupOcrService, InventoryPickupOcrService>();
        services.AddScoped<IInventoryPickupBuilder, InventoryPickupBuilder>();
        services.AddScoped<IInventoryPickupApplier, InventoryPickupApplier>();
        return services;
    }

    // Beneficiaries, components, offers and the project situation. The project and its files are in AddChangeTracking (they notify the other sessions).
    public static IServiceCollection AddProjectServices(this IServiceCollection services)
    {
        services.AddScoped<IBeneficiaryRepository, MariaBeneficiaryRepository>();
        services.AddScoped<IProjectComponentRepository, MariaProjectComponentRepository>();
        services.AddScoped<IOfferRepository, MariaOfferRepository>();
        services.AddScoped<IOfferTemplateRepository, MariaOfferTemplateRepository>();
        services.AddScoped<IProjectSituationReader, MariaProjectSituationReader>();
        services.AddSingleton<IProjectSituationPdfWriter, ProjectSituationPdfWriter>();
        return services;
    }

    // Suppliers, their invoices and the invoice templates (Settings -> Facturi).
    public static IServiceCollection AddSupplierAndInvoiceServices(this IServiceCollection services)
    {
        services.AddScoped<ISupplierRepository>(provider => new CachedSupplierRepository(new MariaSupplierRepository(provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<IArchiveService>(),
            provider.GetRequiredService<IInvoiceTemplateService>())));
        services.AddScoped<ISupplierProductCodes, MariaSupplierProductCodes>();
        services.AddScoped<ISupplierRecognitionLog, MariaSupplierRecognitionLog>();
        services.AddScoped<ISupplierInvoiceRepository, MariaSupplierInvoiceRepository>();
        services.AddSingleton<IInvoicePdfReader, InvoicePdfReader>();
        services.AddSingleton<IInvoiceAnalysisStore, InvoiceAnalysisStore>();
        services.AddScoped<IInvoiceAnalysisService, InvoiceAnalysisService>();
        services.AddSingleton<InvoiceLabSettings>();
        services.AddScoped<IInvoiceTemplateStore, MariaInvoiceTemplateStore>();
        services.AddScoped<IInvoiceTemplateService, InvoiceTemplateService>();
        return services;
    }

    // Work points, contracts, interventions, photos and the map.
    public static IServiceCollection AddMaintenanceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkPointRepository, MariaWorkPointRepository>();
        services.AddScoped<IServicePhotoStore, MariaServicePhotoStore>();
        services.AddScoped<IServiceContractRepository, MariaServiceContractRepository>();
        services.AddScoped<IRiskAnalysisRepository, MariaRiskAnalysisRepository>();
        services.AddScoped<IServiceInterventionRepository, MariaServiceInterventionRepository>();
        services.Configure<MapOptions>(configuration.GetSection(MapOptions.SectionName));
        services.AddSingleton<MapConfigStore>();
        services.AddScoped<IMapConfigurationService, MapConfigurationService>();
        return services;
    }

    // The notification templates and the sources that raise them. The registration order of the IExpirySource entries is the order the sources are evaluated in.
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddScoped<IExpiryNotificationRepository, MariaExpiryNotificationRepository>();
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Insurance, ExpirySourceKeys.VehicleInsurance, "Asigurare"));
        services.AddScoped<IExpirySource>(provider => new VehicleExpirySource(provider.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Rovinieta, ExpirySourceKeys.VehicleRovinieta, "Rovinietă"));
        services.AddScoped<IMaintenanceNotificationReader, MariaMaintenanceNotificationReader>();
        services.AddScoped<IExpirySource, MaintenanceDueSource>();
        services.AddScoped<IExpirySource, ContractExpirySource>();
        services.AddScoped<IRiskAnalysisNotificationReader, MariaRiskAnalysisNotificationReader>();
        services.AddScoped<IExpirySource, RiskAnalysisExpirySource>();
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
        services.AddScoped<IExpiryNotificationService, ExpiryNotificationService>();
        return services;
    }

    // Backup, NAS copy, retention, the daily scheduler and the restoration, all behind one shared operation lock.
    public static IServiceCollection AddBackupServices(this IServiceCollection services, IConfiguration configuration)
    {
        // One shared lock file in the MariaDB backup directory; singleton because the lock and its heartbeat must be the same instance across the whole app, not
        // re-created per request.
        var operationLockPath = Path.Combine(MariaAssetPaths.DatabaseBackups(configuration), "operation.lock.json");
        services.AddSingleton<IOperationLockService>(provider => new FileOperationLockService(operationLockPath,
            provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<ILogger<FileOperationLockService>>()));
        // While that lock is held, every session but the one holding it is refused at the data-access layer and redirected to
        // the waiting page (MaintenanceGate, MaintenanceSupport).
        MaintenanceGate.Configure(operationLockPath);
        services.AddScoped<IDatabaseBackupService>(provider => new NasCopyingBackupService(new MariaDatabaseBackupService(provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IOperationLockService>(), provider.GetRequiredService<IAuditTrail>(),
                provider.GetRequiredService<ILogger<MariaDatabaseBackupService>>()),
            provider.GetRequiredService<INasBackupCopier>(), provider.GetRequiredService<ILogger<NasCopyingBackupService>>()));
        // Backup NAS: a package made by the application is copied to the share afterwards; settings and copier for the Settings tab; the daily backup.
        services.AddScoped<INasBackupSettingsRepository, MariaNasBackupStore>();
        services.AddScoped<IBackupSettingsRepository, MariaBackupSettingsStore>();
        services.AddScoped<INasBackupCopier, NasBackupCopier>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();
        services.AddHostedService<BackupScheduler>();
        // Shares the same lock and backup service as above (the pre-restore snapshot in Pas 0 goes through
        // IDatabaseBackupService.CreateBackupAsync, passing the restore's own already-held lock handle).
        services.AddScoped<IDatabaseRestoreService, MariaDatabaseRestoreService>();
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
