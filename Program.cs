using BlazorStoc.Components;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
// Subtask 2.2 (Task 2): parola contului MariaDB local (blazorstoc_dev) nu se pune niciodata in appsettings.json
// sau in Git. Se citeste, optional, dintr-un fisier JSON local, in afara repository-ului, cu forma
// { "Database": { "Host": "...", "Port": ..., "User": "...", "Password": "...", "SslMode": "..." } } —
// aceleasi chei ca sectiunea "Database" din appsettings.json, suprascriindu-le. Calea implicita este cea din
// docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md (application-connection.private.json); poate fi schimbata prin
// Database:PrivateConfigPath (variabila de mediu Database__PrivateConfigPath) fara sa schimbe mecanismul.
// Fisierul e optional daca parolele sunt date prin variabile de mediu.
// Follow-up Task 2 (28.09.2026, dupa comutarea 2.13): acelasi fisier poate contine si o sectiune
// "Authentication": { "Username": "...", "Password": "..." }, incarcata generic de acelasi AddJsonFile
// (nu e nevoie de cod separat) — asta permite pornirea reala (fara nicio parola in
// appsettings.json sau in argumente de proces, in loc de variabile de mediu setate manual.
var mariaPrivateConfigPath = builder.Configuration["Database:PrivateConfigPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "application-connection.private.json");
builder.Configuration.AddJsonFile(mariaPrivateConfigPath, optional: true, reloadOnChange: false);
// Schema-migration account (blazorstoc_migrator). Its private file has the same shape as the application one
// ({ "Database": { "User", "Password" } }) but must NOT override the application account, so it is mapped explicitly to
// Database:MigratorUser / Database:MigratorPassword. Default: next to the application file; the project's
// local-secrets folder is the fallback. Optional: without it the schema is only checked, never changed.
var migratorPrivateConfigPath = builder.Configuration["Database:MigratorPrivateConfigPath"] is { Length: > 0 } configuredMigratorPath
    ? configuredMigratorPath
    : new[]
        {
            Path.Combine(Path.GetDirectoryName(mariaPrivateConfigPath) ?? "", "migration-account.private.json"),
            Path.Combine(builder.Environment.ContentRootPath, "local-secrets", "migration-account.private.json")
        }.FirstOrDefault(File.Exists);
if (migratorPrivateConfigPath is not null && File.Exists(migratorPrivateConfigPath))
{
    using var migratorFile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(migratorPrivateConfigPath));
    if (migratorFile.RootElement.TryGetProperty("Database", out var migratorSection))
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MigratorUser"] = migratorSection.TryGetProperty("User", out var migratorUser) ? migratorUser.GetString() : null,
            ["Database:MigratorPassword"] = migratorSection.TryGetProperty("Password", out var migratorPassword) ? migratorPassword.GetString() : null
        });
}
// Backup account (blazorstoc_backup: SELECT, SHOW VIEW, TRIGGER, LOCK TABLES on the BlazorStoc schema, used only by
// mariadb-dump). Same shape and lookup as the migrator file; mapped to Database:BackupUser / Database:BackupPassword
// so it never replaces the application account. Optional: without it the dump runs as the application account.
var backupPrivateConfigPath = builder.Configuration["Database:BackupPrivateConfigPath"] is { Length: > 0 } configuredBackupPath
    ? configuredBackupPath
    : new[]
        {
            Path.Combine(Path.GetDirectoryName(mariaPrivateConfigPath) ?? "", "backup-account.private.json"),
            Path.Combine(builder.Environment.ContentRootPath, "local-secrets", "backup-account.private.json")
        }.FirstOrDefault(File.Exists);
if (backupPrivateConfigPath is not null && File.Exists(backupPrivateConfigPath))
{
    using var backupFile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(backupPrivateConfigPath));
    if (backupFile.RootElement.TryGetProperty("Database", out var backupSection))
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:BackupUser"] = backupSection.TryGetProperty("User", out var backupUser) ? backupUser.GetString() : null,
            ["Database:BackupPassword"] = backupSection.TryGetProperty("Password", out var backupPassword) ? backupPassword.GetString() : null
        });
}
// Restore account (blazorstoc_restore: schema swap for the database restoration). Same lookup, mapped to
// Database:RestoreUser / Database:RestorePassword; without it the real restoration stops before changing anything.
var restorePrivateConfigPath = builder.Configuration["Database:RestorePrivateConfigPath"] is { Length: > 0 } configuredRestorePath
    ? configuredRestorePath
    : new[]
        {
            Path.Combine(Path.GetDirectoryName(mariaPrivateConfigPath) ?? "", "restore-account.private.json"),
            Path.Combine(builder.Environment.ContentRootPath, "local-secrets", "restore-account.private.json")
        }.FirstOrDefault(File.Exists);
if (restorePrivateConfigPath is not null && File.Exists(restorePrivateConfigPath))
{
    using var restoreFile = System.Text.Json.JsonDocument.Parse(File.ReadAllText(restorePrivateConfigPath));
    if (restoreFile.RootElement.TryGetProperty("Database", out var restoreSection))
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:RestoreUser"] = restoreSection.TryGetProperty("User", out var restoreUser) ? restoreUser.GetString() : null,
            ["Database:RestorePassword"] = restoreSection.TryGetProperty("Password", out var restorePassword) ? restorePassword.GetString() : null
        });
}
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
if (string.IsNullOrWhiteSpace(builder.Configuration["Database:Password"]) ||
    (builder.Configuration["Authentication:Password"]?.Length ?? 0) < 12)
    throw new InvalidOperationException("Configurați Database__Password și Authentication__Password (minimum 12 caractere) pentru conexiunea MariaDB.");
builder.Services.AddSingleton<MariaSchemaMigrator>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.Cookie.Name = "BlazorStoc.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IAccessControl, CurrentUserAccess>();
builder.Services.AddHttpClient(AnafService.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<AnafStore>();
builder.Services.AddScoped<IAnafService, AnafService>();
builder.Services.AddScoped<IArchiveService, ArchiveService>();
builder.Services.AddSingleton<IAuditTrail, MariaAuditTrail>();
// An explicit App:DataProtectionPath always wins; otherwise the dedicated local asset directory from the handoff document.
var keysPath = builder.Configuration["App:DataProtectionPath"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "assets", "data-protection-keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("BlazorStoc");
builder.Services.AddRateLimiter(options =>
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
builder.Services.AddScoped<IUserRepository>(services => new MariaUserRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<IUserAuthenticator>(services => (IUserAuthenticator)services.GetRequiredService<IUserRepository>());
builder.Services.AddScoped<IProductRepository>(services => new MariaProductRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>(),
        services.GetRequiredService<IProductImageStore>()));
builder.Services.AddScoped<IStockMovementRepository>(services => new MariaStockMovementRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<IWorkPointRepository>(services => new MariaWorkPointRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>()));
builder.Services.AddScoped<IServicePhotoStore>(services => new MariaServicePhotoStore(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>()));
builder.Services.AddScoped<IServiceContractRepository>(services => new MariaServiceContractRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>()));
builder.Services.AddScoped<IServiceInterventionRepository>(services => new MariaServiceInterventionRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), timeProvider: services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<IBeneficiaryRepository>(services => new MariaBeneficiaryRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<ISupplierRepository>(services => new MariaSupplierRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<ISupplierInvoiceRepository>(services => new MariaSupplierInvoiceRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IExpiryNotificationRepository>(services => new MariaExpiryNotificationRepository(services.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IExpirySource>(services => new VehicleExpirySource(services.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Itp, ExpirySourceKeys.VehicleItp, "ITP"));
builder.Services.AddScoped<IExpirySource>(services => new VehicleExpirySource(services.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Insurance, ExpirySourceKeys.VehicleInsurance, "Asigurare"));
builder.Services.AddScoped<IExpirySource>(services => new VehicleExpirySource(services.GetRequiredService<IVehicleRepository>(), VehicleExpiryKind.Rovinieta, ExpirySourceKeys.VehicleRovinieta, "Rovinietă"));
builder.Services.Configure<MapOptions>(builder.Configuration.GetSection(MapOptions.SectionName));
builder.Services.AddSingleton<MapConfigStore>();
builder.Services.AddScoped<IMapConfigurationService, MapConfigurationService>();
builder.Services.AddScoped<IMaintenanceNotificationReader, MariaMaintenanceNotificationReader>();
builder.Services.AddScoped<IExpirySource>(services => new MaintenanceDueSource(services.GetRequiredService<IMaintenanceNotificationReader>()));
builder.Services.AddScoped<IExpirySource>(services => new ContractExpirySource(services.GetRequiredService<IMaintenanceNotificationReader>()));
builder.Services.AddScoped<IExpiryNotificationService, ExpiryNotificationService>();
builder.Services.AddScoped<IInventoryReportBuilder, InventoryReportBuilder>();
builder.Services.AddSingleton<IInventoryPdfWriter, InventoryPdfWriter>();
builder.Services.AddSingleton<IWebHostEnvironmentTessdataPath, TessdataPath>();
builder.Services.AddSingleton<IInventoryPickupOcrService, InventoryPickupOcrService>();
builder.Services.AddSingleton<IInvoicePdfReader, InvoicePdfReader>();
builder.Services.AddSingleton<IInvoiceAnalysisStore, InvoiceAnalysisStore>();
builder.Services.AddScoped<IInvoiceAnalysisService, InvoiceAnalysisService>();
builder.Services.AddSingleton<InvoiceLabSettings>();
builder.Services.AddScoped<IInvoiceTemplateStore>(services => new MariaInvoiceTemplateStore(services.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IInvoiceTemplateService, InvoiceTemplateService>();
builder.Services.AddScoped<IInventoryPickupBuilder, InventoryPickupBuilder>();
builder.Services.AddScoped<IInventoryPickupApplier, InventoryPickupApplier>();
// One shared lock file in the MariaDB backup directory; singleton because the lock and its heartbeat must be the same instance across the whole app, not
// re-created per request.
var operationLockPath = Path.Combine( MariaAssetPaths.DatabaseBackups(builder.Configuration), "operation.lock.json");
builder.Services.AddSingleton<IOperationLockService>(services => new FileOperationLockService(operationLockPath,
    services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<ILogger<FileOperationLockService>>()));
// While that lock is held, every session but the one holding it is refused at the data-access layer and redirected to
// the waiting page (MaintenanceGate, MaintenanceSupport).
MaintenanceGate.Configure(operationLockPath);
builder.Services.AddScoped<IDatabaseBackupService>(services => new MariaDatabaseBackupService(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IOperationLockService>(), services.GetRequiredService<IAuditTrail>(),
        services.GetRequiredService<ILogger<MariaDatabaseBackupService>>()));
// Subtask 3.3/3.4 (Task 3): shares the same lock and backup service as above (the pre-restore snapshot in Pas 0
// goes through IDatabaseBackupService.CreateBackupAsync, passing the restore's own already-held lock handle).
builder.Services.AddScoped<IDatabaseRestoreService>(services => new MariaDatabaseRestoreService(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IOperationLockService>(), services.GetRequiredService<IDatabaseBackupService>(),
        services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<ILogger<MariaDatabaseRestoreService>>()));
builder.Services.AddScoped<IVehicleRepository>(services => new MariaVehicleRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddSingleton<IProductImageStore>(services => new FileProductImageStore(services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<InProcessChangeFeed>();
builder.Services.AddSingleton<IChangeFeed>(services => services.GetRequiredService<InProcessChangeFeed>());
builder.Services.AddSingleton<IChangeEventSource>(services => new MariaChangeEventSource(services.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService(services => new ChangeEventRelay(services.GetRequiredService<IChangeEventSource>(),
    services.GetRequiredService<IChangeFeed>(), services.GetRequiredService<ILogger<ChangeEventRelay>>(),
    services.GetRequiredService<IConfiguration>()));
builder.Services.AddSignalR();
builder.Services.AddHostedService<SignalRChangeBroadcaster>();
builder.Services.AddScoped<ChangeOrigin>();
builder.Services.AddScoped<UnsavedChanges>();
builder.Services.AddScoped<ExitFormDraft>();
builder.Services.AddScoped<ListNavigationContext>();
builder.Services.AddScoped<IProductLockRepository>(services => new ChangeNotifyingProductLockRepository( new MariaProductLockRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>()),
    services.GetRequiredService<IChangeFeed>(), services.GetRequiredService<ChangeOrigin>()));
builder.Services.AddScoped<IProjectFileStore>(services => new ChangeNotifyingProjectFileStore( new MariaProjectFileStore(services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<IConfiguration>(),
        services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IArchiveService>(), services.GetRequiredService<IAuditTrail>()),
    services.GetRequiredService<IChangeFeed>(), services.GetRequiredService<ChangeOrigin>()));
builder.Services.AddScoped<IProjectRepository>(services => new ChangeNotifyingProjectRepository( new MariaProjectRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IWebHostEnvironment>(),
        services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(),
        services.GetRequiredService<IArchiveService>(), services.GetRequiredService<IProjectFileStore>()),
    services.GetRequiredService<IChangeFeed>(), services.GetRequiredService<ChangeOrigin>()));
var app = builder.Build();
await MariaArchiveSchema.InitializeAsync(app.Configuration);
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
// Romanian culture for every request and circuit: framework texts and number formats follow it (Task 1).
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(new CultureInfo("ro-RO")),
    SupportedCultures = [new CultureInfo("ro-RO")],
    SupportedUICultures = [new CultureInfo("ro-RO")],
    RequestCultureProviders = []
});
// Error status codes without a page of their own get a Romanian page instead of an empty response (Task 1).
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    var (title, text) = response.StatusCode switch
    {
        404 => ("Pagina nu a fost găsită.", "Adresa deschisă nu există sau a fost mutată."),
        403 => ("Acces interzis.", "Nu ai dreptul să deschizi această pagină."),
        401 => ("Autentificare necesară.", "Autentifică-te pentru a continua."),
        _ => ("Cererea nu a putut fi procesată.", "Încearcă din nou. Dacă problema persistă, contactează administratorul.")
    };
    response.ContentType = "text/html; charset=utf-8";
    await response.WriteAsync($"<!DOCTYPE html><html lang=\"ro\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /><title>{title} · BlazorStoc</title><link rel=\"stylesheet\" href=\"/app.css\" /></head><body class=\"login-body\"><main class=\"login-card\"><h1>{title}</h1><p class=\"muted\">{text}</p><a class=\"refresh\" href=\"/\">Înapoi la pagina principală</a></main></body></html>");
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
// A page navigation made while a backup/restore is running goes to the waiting page instead of a half-loaded page.
app.Use(async (context, next) =>
{
    if (MaintenanceSupport.ShouldRedirect(context.Request, MaintenanceGate.Active))
    {
        context.Response.Redirect(MaintenanceSupport.WaitingUrl(context.Request));
        return;
    }
    await next();
});
app.MapStaticAssets().AllowAnonymous();
app.MapGet("/health/live", () => Results.Text("healthy")).AllowAnonymous();
// Polled by every open page (wwwroot/maintenance-watch.js): tells an already-open tab that a backup/restore has
// started or ended, so it can be moved to the waiting page and brought back. Carries no names or details.
app.MapGet("/api/maintenance", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { active = MaintenanceGate.Active is not null });
}).RequireAuthorization();
// Polled by wwwroot/notification-watch.js: the number of notifications that warn now (also re-evaluates the templates
// at most every few minutes, so a session left open still learns about new notifications).
app.MapGet("/api/notifications/alerts", async (HttpContext context, IExpiryNotificationService notifications, CancellationToken token) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        await notifications.EvaluateAsync(TimeSpan.FromMinutes(5), token);
        return Results.Json(new { count = await notifications.AlertCountAsync(token) });
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { return Results.StatusCode(499); }
    catch (Exception) { return Results.Json(new { count = -1 }); }
}).RequireAuthorization();
app.MapGet("/media/products/{productId:int}", async (int productId, IProductImageStore images, CancellationToken token) =>
{
    var image = await images.GetAsync(productId, token);
    return image is null
        ? Results.Redirect(ProductImageRules.PlaceholderUrl)
        : Results.File(image.Content, image.ContentType, enableRangeProcessing: true);
}).RequireAuthorization();
app.MapGet("/media/project-files/{fileId:int}", async (int fileId, IProjectFileStore files, CancellationToken token) =>
{
    var file = await files.GetContentAsync(fileId, token);
    return file is null
        ? Results.NotFound()
        : Results.File(file.Content, file.ContentType, file.OriginalName, enableRangeProcessing: true);
}).RequireAuthorization();
// A page picture of the invoice under analysis (Settings -> Facturi): kept in memory, served only to the administrator who uploaded the file.
app.MapGet("/media/invoice-analysis/{id:guid}/{page:int}", (Guid id, int page, HttpContext context, IInvoiceAnalysisStore store) =>
{
    if (!context.User.IsInRole(AccessRoles.Administrator) && !context.User.IsInRole(AccessRoles.LimitedUser)) return Results.Forbid();
    var session = store.Get(id, context.User.Identity?.Name ?? "necunoscut");
    if (session is null || page < 1 || page > session.Previews.Count) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(session.Previews[page - 1], "image/png");
}).RequireAuthorization();
app.MapGet("/media/service-photos/{photoId:int}", async (int photoId, IServicePhotoStore photos, CancellationToken token) =>
{
    var photo = await photos.GetContentAsync(photoId, token);
    return photo is null ? Results.NotFound() : Results.File(photo.Content, photo.ContentType, photo.OriginalName, enableRangeProcessing: true);
}).RequireAuthorization();
app.MapHub<ChangesHub>(ChangesHub.Path);
app.MapRazorPages().RequireRateLimiting("login");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
// MariaDB mode: bring the schema up to date with the migrator account before serving requests (idempotent). Without
// that account only a warning is logged. "--migrate-schema" applies the migrations and exits (0 = schema is current).
var migrateOnly = args.Contains("--migrate-schema");
{
    var migrator = app.Services.GetRequiredService<MariaSchemaMigrator>();
    try
    {
        var report = await migrator.MigrateAsync();
        if (report.MissingColumns.Count > 0)
            app.Logger.LogError("MariaDB schema is behind the application: missing {Columns}. {Hint}", string.Join(", ", report.MissingColumns),
                report.MigratorConfigured ? "The migration ran but the columns are still missing." : "Configure Database:MigratorUser/MigratorPassword (migration-account.private.json) and restart, or run --migrate-schema.");
        else app.Logger.LogInformation("MariaDB schema is current ({Applied} migration(s) applied now).", report.Applied.Count);
        if (report.MissingColumns.Count == 0)
        {
            // The main work point of the beneficiaries that existed before migration 7 (idempotent: creates only what is missing).
            var backfill = await WorkPointBackfill.EnsurePrimariesAsync(app.Configuration);
            if (backfill.Created + backfill.Promoted > 0 || backfill.EmptyAddress > 0)
                app.Logger.LogInformation("Main work points: {Created} created, {Promoted} promoted from an additional point, {Empty} beneficiary(ies) without an address.", backfill.Created, backfill.Promoted, backfill.EmptyAddress);
        }
        if (migrateOnly) return report.MissingColumns.Count == 0 ? 0 : 1;
    }
    catch (Exception exception) when (!migrateOnly)
    {
        app.Logger.LogError("MariaDB schema check/migration failed ({ErrorType}); the application starts without it.", exception.GetType().Name);
    }
    catch (Exception exception)
    {
        app.Logger.LogError("MariaDB schema migration failed ({ErrorType}): {Message}", exception.GetType().Name, exception.Message);
        return 1;
    }
}
app.Run();
return 0;
public partial class Program { }
