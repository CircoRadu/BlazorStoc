using BlazorStoc.Services;

namespace BlazorStoc.Modules.Elcomtur;

// The Elcomtur module: the balances of the beneficiaries read from a backup of the Elcomtur program (Administrare -> Preluare date Elcomtur), shown on the file of the beneficiary and in a tab of
// the Notifications page. The menu entry is under Administrare (not a top-level entry), so the module has no Nav items of its own.
public sealed class ElcomturModule : IModule
{
    public const string ModuleId = "elcomtur";

    public string Id => ModuleId;
    public string Name => "Elcomtur";
    public Version Version { get; } = new(1, 3, 3);
    public IReadOnlyList<ModuleNavItem> Nav { get; } = [];

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IElcomturBalanceRepository>(provider => new GuardedElcomturBalanceRepository(
            ActivatorUtilities.CreateInstance<MariaElcomturBalanceRepository>(provider), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IElcomturImportService>(provider => new GuardedElcomturImportService(
            new ElcomturImportService(provider.GetRequiredService<IBeneficiaryRepository>(), provider.GetRequiredService<IElcomturBalanceRepository>(),
                provider.GetService<IAccessControl>(), provider.GetService<IAuditTrail>()), provider.GetRequiredService<ModuleRegistry>()));
        services.AddScoped<IElcomturAutoService>(provider => new ElcomturAutoService(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
            provider.GetRequiredService<IBeneficiaryRepository>(), provider.GetRequiredService<IElcomturBalanceRepository>(), provider.GetService<IAccessControl>(), provider.GetService<IAuditTrail>(),
            provider.GetRequiredService<ILogger<ElcomturAutoService>>()));
        services.AddScoped<IElcomturAutoAlertReader>(provider => new MariaElcomturAutoAlertReader(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()));
        services.AddHostedService<ElcomturAutoWorker>();
    }

    public void RegisterNotificationSources(IServiceCollection services)
    {
        services.AddScoped<IExpirySource>(provider => new ElcomturAutoAlertSource(provider.GetRequiredService<IElcomturAutoAlertReader>()));
    }

    public void RegisterUnavailable(IServiceCollection services)
    {
        services.AddScoped<IElcomturBalanceRepository, UnavailableElcomturBalanceRepository>();
        services.AddScoped<IElcomturImportService, UnavailableElcomturImportService>();
        services.AddScoped<IElcomturAutoService, UnavailableElcomturAutoService>();
    }
}
