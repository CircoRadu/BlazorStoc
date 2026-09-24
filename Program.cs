using BlazorStoc.Components;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
var demo = builder.Configuration.GetValue("App:DemoMode", true);
if (!demo && (string.IsNullOrWhiteSpace(builder.Configuration["Database:Password"]) ||
              (builder.Configuration["Authentication:Password"]?.Length ?? 0) < 12))
    throw new InvalidOperationException("Configurați Database__Password și Authentication__Password (minimum 12 caractere) pentru modul MariaDB.");
builder.Services.AddSingleton(new AppMode(demo));
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
builder.Services.AddScoped<IArchiveService, ArchiveService>();
if (demo)
{
    builder.Services.AddSingleton<SqliteLocalStore>();
    builder.Services.AddSingleton<IAuditTrail, SqliteAuditTrail>();
}
else builder.Services.AddSingleton<IAuditTrail, MariaAuditTrail>();
var keysPath = builder.Configuration["App:DataProtectionPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("BlazorStoc");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<IUserRepository>(services => demo
    ? new SqliteUserRepository(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IArchiveService>())
    : new MariaUserRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<IUserAuthenticator>(services => (IUserAuthenticator)services.GetRequiredService<IUserRepository>());
builder.Services.AddScoped<IProductRepository>(services => demo
    ? new SqliteProductRepository(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IArchiveService>(), services.GetRequiredService<IProductImageStore>())
    : new MariaProductRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>(),
        services.GetRequiredService<IProductImageStore>()));
builder.Services.AddScoped<IStockMovementRepository>(services => demo
    ? new SqliteStockMovementRepository(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IArchiveService>())
    : new MariaStockMovementRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<IBeneficiaryRepository>(services => demo
    ? new SqliteBeneficiaryRepository(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IArchiveService>())
    : new MariaBeneficiaryRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddSingleton<IProductImageStore>(services => demo
    ? new SqliteProductImageStore(services.GetRequiredService<SqliteLocalStore>())
    : new FileProductImageStore(services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IProjectFileStore>(services => demo
    ? new SqliteProjectFileStore(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IArchiveService>())
    : new MariaProjectFileStore(services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<IConfiguration>(),
        services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IArchiveService>()));
builder.Services.AddScoped<IProjectRepository>(services => demo
    ? new SqliteProjectRepository(services.GetRequiredService<SqliteLocalStore>(), services.GetRequiredService<IAccessControl>(),
        services.GetRequiredService<IArchiveService>(), services.GetRequiredService<IProjectFileStore>())
    : new MariaProjectRepository(services.GetRequiredService<IConfiguration>(), services.GetRequiredService<IWebHostEnvironment>(),
        services.GetRequiredService<IAccessControl>(), services.GetRequiredService<IAuditTrail>(),
        services.GetRequiredService<IArchiveService>(), services.GetRequiredService<IProjectFileStore>()));
var app = builder.Build();
if (demo) await app.Services.GetRequiredService<SqliteLocalStore>().InitializeAsync();
else await MariaArchiveSchema.InitializeAsync(app.Configuration);
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets().AllowAnonymous();
app.MapGet("/health/live", () => Results.Text("healthy")).AllowAnonymous();
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
app.MapRazorPages().RequireRateLimiting("login");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }
