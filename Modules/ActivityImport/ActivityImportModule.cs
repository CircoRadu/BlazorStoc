using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// The ticket import module: takes the service tickets of ActivityApp (a separate application with its own SQL Server database) into the maintenance registers of BlazorStoc, linked to
// the beneficiaries. ActivityApp is only ever read, with a read-only account, in small and short queries (see ActivitySource). Setari -> Preluare ActivityApp is for the administrator
// alone. Stage 1 (this one): connection, structure check against the contract, the list of ticket types and the choice of the types; nothing is imported yet.
// The module has no menu entry and no notification source of its own.
public sealed class ActivityImportModule : IModule
{
    public const string ModuleId = "import-activityapp";

    public string Id => ModuleId;
    public string Name => "Preluare tichete ActivityApp";
    public Version Version { get; } = new(0, 3, 0);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IActivitySource, SqlActivitySource>();
        services.AddScoped<IActivityImportService>(provider => new GuardedActivityImportService(
            ActivatorUtilities.CreateInstance<MariaActivityImportService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IActivityPartnerImportService>(provider => new GuardedActivityPartnerImportService(
            ActivatorUtilities.CreateInstance<MariaActivityPartnerImportService>(provider, provider.GetRequiredService<IActivityImportService>()), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IActivityTicketImportService>(provider => new GuardedActivityTicketImportService(
            ActivatorUtilities.CreateInstance<MariaActivityTicketImportService>(provider, provider.GetRequiredService<IActivityImportService>()), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IActivitySyncService>(provider => new GuardedActivitySyncService(ActivatorUtilities.CreateInstance<MariaActivitySyncService>(provider,
            provider.GetRequiredService<IActivityImportService>(), provider.GetRequiredService<IActivityPartnerImportService>(), provider.GetRequiredService<IActivityTicketImportService>()), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IActivityImportAlertReader, MariaActivityImportAlertReader>();
        services.AddScoped<IActivityKeywordService>(provider => new GuardedActivityKeywordService(ActivatorUtilities.CreateInstance<MariaActivityKeywordService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IActivityReviewService>(provider => new GuardedActivityReviewService(ActivatorUtilities.CreateInstance<MariaActivityReviewService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IBeneficiaryCheckService>(provider => new GuardedBeneficiaryCheckService(ActivatorUtilities.CreateInstance<MariaBeneficiaryCheckService>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddHostedService<ActivitySyncWorker>();
    }

    public void RegisterNotificationSources(IServiceCollection services)
    {
        services.AddScoped<IExpirySource>(provider => new ActivityImportAlertSource(provider.GetRequiredService<IActivityImportAlertReader>()));
        services.AddScoped<IExpirySource>(provider => new ActivityReviewSource(provider.GetRequiredService<IConfiguration>()));
        services.AddScoped<IExpirySource>(provider => new ActivityPartnerCheckSource(provider.GetRequiredService<IConfiguration>()));
    }

    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IActivityImportService, UnavailableActivityImportService>();
        services.AddScoped<IActivityPartnerImportService, UnavailableActivityPartnerImportService>();
        services.AddScoped<IActivityTicketImportService, UnavailableActivityTicketImportService>();
        services.AddScoped<IActivitySyncService, UnavailableActivitySyncService>();
        services.AddScoped<IActivityReviewService, UnavailableActivityReviewService>();
        services.AddScoped<IActivityKeywordService, UnavailableActivityKeywordService>();
        services.AddScoped<IBeneficiaryCheckService, UnavailableBeneficiaryCheckService>();
    }
}
