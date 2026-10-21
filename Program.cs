using BlazorStoc.Components;
using BlazorStoc.Services;
using BlazorStoc.Startup;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using System.Globalization;
using Microsoft.AspNetCore.Localization;

// Description of the release (version, schema version, migrations with their checksums) for tools\release.ps1 and the updater: printed, then the process ends; no web host, no database.
if (args.Length == 1 && args[0] == ReleaseDescription.Argument) return ReleaseDescription.Print();

// Worker mode of the OCR (Modules/Ocr): the application started with --ocr-worker <job folder> reads one file and exits; no web host, no database.
if (args is ["--ocr-worker", var ocrJobFolder]) return await BlazorStoc.Modules.Ocr.OcrWorker.RunAsync(ocrJobFolder);

// Worker mode of the backup (Modules/Backup): the application started with --backup-worker [its own arguments] does one backup, restoration or NAS operation and exits;
// it talks over the standard streams and reads the same configuration (and so the same database and folders) as the application.
if (args is [BlazorStoc.Modules.Backup.BackupWorkerRules.Argument, ..var backupWorkerArguments]) return await BlazorStoc.Modules.Backup.BackupWorker.RunAsync(backupWorkerArguments);

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
// The passwords of the MariaDB accounts come from private files outside the repository (see Startup/PrivateConfiguration).
// What the administrator saved in Setari -> Baza de date (an override file outside the repository) is read last and wins; the worker processes use the same order.
builder.AddEffectiveConfiguration();
var appHosting = AppHosting.Resolve(builder.Configuration, AppHosting.CanBind);
if (appHosting.AppliedUrls is not null) builder.WebHost.UseUrls(appHosting.AppliedUrls);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
builder.AddFileLogging();
if (string.IsNullOrWhiteSpace(builder.Configuration["Database:Password"]) ||
    (builder.Configuration["Authentication:Password"]?.Length ?? 0) < 12)
    throw new InvalidOperationException("Configurați Database__Password și Authentication__Password (minimum 12 caractere) pentru conexiunea MariaDB.");
var modules = new BlazorStoc.Modules.ModuleRegistry();
builder.Services
    .AddWebAndSecurity(builder.Configuration)
    .AddCoreServices()
    .AddModules(builder.Configuration, modules)
    .AddUserServices()
    .AddStockServices()
    .AddProjectServices()
    .AddMaintenanceServices(builder.Configuration)
    .AddNotificationServices(modules)
    .AddBackupServices(builder.Configuration)
    .AddDatabaseSettings(appHosting)
    .AddChangeTracking()
    .AddHostResilience()
    .AddRunTracking(builder.Configuration)
    .AddApplicationUpdate(builder.Configuration);
var app = builder.Build();
modules.AttachLogger(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BlazorStoc.Modules"));
// The changes of state of the modules go to the journal of activity (the modules out since startup are announced now, nobody listened before).
app.Services.GetRequiredService<BlazorStoc.Startup.ModuleJournal>().Start();
if (BlazorStoc.Startup.AppDataRoot.UsesLegacyFolder(app.Configuration))
    app.Logger.LogWarning("The data are still in the per-user folder {Legacy}; run tools\\migrate-data-root.ps1 to move them to {Machine} (the same folder for every user and for the Windows service).",
        BlazorStoc.Startup.AppDataRoot.LegacyUserFolder, BlazorStoc.Startup.AppDataRoot.MachineFolder);
// A database that does not answer at startup does not stop the application: /health reports 503 and the pages show their own error until it is back.
// A database that answers but lacks the expected tables is still fatal (InvalidOperationException).
try { await MariaArchiveSchema.InitializeAsync(app.Configuration); }
// A backup/restore in progress (also the lock a killed process left behind, until its lease of two minutes runs out) is the same: the application starts and the
// pages show the waiting page; stopping here would put a supervisor in a restart loop.
catch (Exception exception) when (exception is MySqlConnector.MySqlException or MaintenanceInProgressException)
{ app.Logger.LogError("MariaDB did not answer at startup or a backup/restore holds it ({ErrorType}); the application starts without it.", exception.GetType().Name); }
// Each notification event without a template gets its starting template once (see DefaultNotificationTemplates), while the notifications module works.
if (app.Services.GetRequiredService<BlazorStoc.Modules.ModuleRegistry>().IsAvailable(BlazorStoc.Modules.Notifications.NotificationsModule.ModuleId))
try
{
    using var seedScope = app.Services.CreateScope();
    var made = await DefaultNotificationTemplates.SeedAsync(app.Configuration, seedScope.ServiceProvider.GetRequiredService<IExpiryNotificationRepository>(),
        seedScope.ServiceProvider.GetServices<IExpirySource>(), seedScope.ServiceProvider.GetService<IAuditTrail>(), app.Logger);
    if (made > 0) app.Logger.LogInformation("{Count} default notification templates were created.", made);
}
catch (Exception exception) when (exception is MySqlConnector.MySqlException or InvalidOperationException or MaintenanceInProgressException)
{ app.Logger.LogWarning("The default notification templates were not created ({ErrorType}).", exception.GetType().Name); }
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
// A page navigation made while a backup/restore is running goes to the waiting page instead of a half-loaded page. It must come BEFORE the
// authorization: the permission policies read the user's permissions from the database, which throws while the operation lock is held (500 instead of the page).
app.Use(async (context, next) =>
{
    if (MaintenanceSupport.ShouldRedirect(context.Request, MaintenanceGate.Active))
    {
        context.Response.Redirect(MaintenanceSupport.WaitingUrl(context.Request));
        return;
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultPasswordGate();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapHelp();
app.MapSessionPing();
app.MapGet("/api/furnizori/recunoastere.csv", async (ISupplierRecognitionLog log, CancellationToken token) =>
{
    // While the suppliers module is out the log cannot be read: a clear 503, not an error page.
    try { return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(await log.ExportCsvAsync(token))).ToArray(), "text/csv; charset=utf-8", "recunoastere-furnizori.csv"); }
    catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
}).RequireAuthorization(PermissionPolicy.For("furnizori.view"));
// The state of every module (no details: the reasons are in the log). For logged-in users and the supervisor of the modules.
app.MapGet("/health/modules", (BlazorStoc.Modules.ModuleRegistry registry) =>
{
    var all = registry.Snapshot();
    return Results.Json(new
    {
        status = all.All(item => item.Status == BlazorStoc.Modules.ModuleStatus.Healthy) ? "healthy" : "degraded",
        app = new { version = AppVersion.Current },
        modules = all.Select(item => new { id = item.Id, name = item.Name, version = item.Version, status = item.Status.ToString().ToLowerInvariant() })
    });
}).RequireAuthorization();
app.MapGet("/health/live", () => Results.Text("healthy")).AllowAnonymous();
// For monitoring and the supervisor: 200 when the process and the database answer, 503 when the database does not (no details are given).
app.MapGet("/health", async (IConfiguration configuration, CancellationToken token) =>
{
    try
    {
        await using var connection = await MariaDb.OpenAsync(configuration, token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(token);
        return Results.Json(new { status = "healthy", database = "ok", version = AppVersion.Current });
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { return Results.StatusCode(499); }
    catch (Exception) { return Results.Json(new { status = "unhealthy", database = "unavailable" }, statusCode: 503); }
}).AllowAnonymous();
// Polled by every open page (wwwroot/maintenance-watch.js): tells an already-open tab that a backup/restore has
// started or ended, so it can be moved to the waiting page and brought back. Carries no names or details.
// Bon de consum / aviz de predare of an exit operation (opens in the browser's PDF viewer).
app.MapGet("/api/iesiri/{operationId:int}/bon.pdf", async (int operationId, IStockMovementRepository movements, IConsumptionNotePdfWriter writer, CancellationToken token) =>
{
    try
    {
        var operation = await movements.GetOperationAsync(operationId, token);
        return operation is null ? Results.NotFound() : Results.File(writer.Write(operation, DateTime.Now), "application/pdf");
    }
    catch (AccessDeniedException) { return Results.Forbid(); }
}).RequireAuthorization(PermissionPolicy.For("stoc.view"));
// Situatia proiectului: PDF and CSV exports (the same calculation as the page).
app.MapGet("/api/consum.csv", async (int? beneficiar, int? proiect, string? de, string? pana, IConsumptionReader reader, CancellationToken token) =>
{
    try
    {
        var query = new ConsumptionQuery(beneficiar, proiect, ConsumptionExportRules.ParseDate(de), ConsumptionExportRules.ParseDate(pana));
        var csv = ConsumptionExportRules.ToCsv(query, await reader.GetAsync(query, token));
        return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "consum.csv");
    }
    catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch (AccessDeniedException) { return Results.Forbid(); }
}).RequireAuthorization(PermissionPolicy.For("export-consum.export"));
app.MapGet("/api/proiecte/{projectId:int}/situatie.pdf", async (int projectId, IProjectSituationReader reader, IProjectSituationPdfWriter writer, CancellationToken token) =>
{
    try { return Results.File(writer.Write(await reader.GetAsync(projectId, token), DateTime.Now), "application/pdf"); }
    catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch (OfferException) { return Results.NotFound(); }
    catch (AccessDeniedException) { return Results.Forbid(); }
}).RequireAuthorization(PermissionPolicy.For("beneficiari.export"));
app.MapGet("/api/proiecte/{projectId:int}/situatie.csv", async (int projectId, IProjectSituationReader reader, CancellationToken token) =>
{
    try
    {
        var situation = await reader.GetAsync(projectId, token);
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(ProjectSituationRules.ToCsv(situation))).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", $"situatie-proiect-{projectId}.csv");
    }
    catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch (OfferException) { return Results.NotFound(); }
    catch (AccessDeniedException) { return Results.Forbid(); }
}).RequireAuthorization(PermissionPolicy.For("beneficiari.export"));
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
}).RequireAuthorization(PermissionPolicy.For("stoc.view"));
app.MapGet("/media/project-files/{fileId:int}", async (int fileId, IObservationFiles files, CancellationToken token) =>
{
    try
    {
        var file = await files.GetContentAsync(fileId, token);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Content, file.ContentType, file.OriginalName, enableRangeProcessing: true);
    }
    catch (BlazorStoc.Modules.ModuleUnavailableException) { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
}).RequireAuthorization(PermissionPolicy.For("beneficiari.view"));
// A page picture of the invoice under analysis (Settings -> Facturi): kept in memory, served only to the administrator who uploaded the file.
app.MapGet("/media/invoice-analysis/{id:guid}/{page:int}", (Guid id, int page, HttpContext context, IInvoiceAnalysisStore store) =>
{
    var session = store.Get(id, context.User.Identity?.Name ?? "necunoscut");
    if (session is null || page < 1 || page > session.Previews.Count) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(session.Previews[page - 1], "image/png");
}).RequireAuthorization(PermissionPolicy.For("preluare-factura.view", "setari-facturi.view"));
app.MapGet("/media/service-photos/{photoId:int}", async (int photoId, IServicePhotoStore photos, CancellationToken token) =>
{
    var photo = await photos.GetContentAsync(photoId, token);
    return photo is null ? Results.NotFound() : Results.File(photo.Content, photo.ContentType, photo.OriginalName, enableRangeProcessing: true);
}).RequireAuthorization(PermissionPolicy.For("mentenanta.view"));
app.MapHub<ChangesHub>(ChangesHub.Path);
app.MapRazorPages().RequireRateLimiting("login");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
// MariaDB mode: bring the schema up to date with the migrator account before serving requests (idempotent). Without
// that account only a warning is logged. "--migrate-schema" applies the migrations and exits (0 = schema is current).
var migrateOnly = args.Contains("--migrate-schema");
// "--schema-status": prints one line "SCHEMA-STATUS {json}" (ledger version, version of the code, pending migrations, missing columns, drift) and exits; changes nothing.
var statusOnly = args.Contains("--schema-status");
{
    var migrator = app.Services.GetRequiredService<MariaSchemaMigrator>();
    if (statusOnly)
    {
        try
        {
            Console.Out.WriteLine("SCHEMA-STATUS " + System.Text.Json.JsonSerializer.Serialize(await migrator.StatusAsync(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            return 0;
        }
        catch (Exception exception)
        {
            app.Logger.LogError("The schema status could not be read ({ErrorType}): {Message}", exception.GetType().Name, exception.Message);
            return 1;
        }
    }
    try
    {
        var report = await migrator.MigrateAsync(force: migrateOnly);
        if (report.MissingColumns.Count > 0)
            app.Logger.LogError("MariaDB schema is behind the application: missing {Columns}. {Hint}", string.Join(", ", report.MissingColumns),
                report.MigratorConfigured ? "The migration ran but the columns are still missing." : "Configure Database:MigratorUser/MigratorPassword (migration-account.private.json) and restart, or run --migrate-schema.");
        else app.Logger.LogInformation("BlazorStoc {Version}: MariaDB schema is current, version {Schema} ({Applied} migration(s) applied now).", AppVersion.Current, report.SchemaVersion, report.Applied.Count);
        if (report.MissingColumns.Count == 0)
        {
            await MariaBackupSettingsStore.CopyFromNasSettingsAsync(app.Configuration);
            // The main work point of the beneficiaries that existed before migration 7 (idempotent: creates only what is missing).
            await UserTypeSeeder.EnsureAsync(app.Configuration);
            var backfill = await WorkPointBackfill.EnsurePrimariesAsync(app.Configuration);
            if (backfill.Created + backfill.Promoted > 0 || backfill.EmptyAddress > 0)
                app.Logger.LogInformation("Main work points: {Created} created, {Promoted} promoted from an additional point, {Empty} beneficiary(ies) without an address.", backfill.Created, backfill.Promoted, backfill.EmptyAddress);
        }
        // Exit codes of --migrate-schema (also used by the updater): 0 = schema current, 1 = columns still missing, 3 = an applied migration was edited (drift).
        if (migrateOnly) return report.MissingColumns.Count > 0 ? 1 : report.Drifted.Count > 0 ? 3 : 0;
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
// "--create-administrator": the installer makes the first administrator account (details on the standard input) after the schema is current, then exits.
if (args.Contains("--create-administrator"))
{
    try { return await BlazorStoc.Services.AdministratorBootstrap.RunAsync(app.Services, app.Configuration, Console.In, Console.Out); }
    catch (Exception exception)
    {
        app.Logger.LogError("The administrator account was not made ({ErrorType}): {Message}", exception.GetType().Name, exception.Message);
        return BlazorStoc.Services.AdministratorBootstrap.Failed;
    }
}
app.Run();
return 0;
public partial class Program { }
