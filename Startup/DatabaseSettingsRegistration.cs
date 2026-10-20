using BlazorStoc.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;

namespace BlazorStoc.Startup;

public static class DatabaseSettingsRegistration
{
    // Setari -> Baza de date: the settings of the connection, the state of the database and of the application; the open sessions are counted by a CircuitHandler.
    public static IServiceCollection AddDatabaseSettings(this IServiceCollection services, AppHostingInfo appHosting)
    {
        services.AddSingleton(appHosting);
        services.AddSingleton<CircuitCounter>();
        services.AddSingleton<OpenSessionRegistry>();
        services.AddScoped<CircuitHolder>();
        services.AddScoped<CircuitHandler, CircuitCountHandler>();
        services.AddScoped<IDatabaseSettingsService>(provider => new DatabaseSettingsService(provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(),
            provider.GetRequiredService<IOperationLockService>(), provider.GetRequiredService<AppHostingInfo>(), provider.GetRequiredService<ILogger<DatabaseSettingsService>>(),
            authenticator: provider.GetService<IUserAuthenticator>()));
        services.AddScoped<IDatabaseStatusService>(provider => new DatabaseStatusService(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAccessControl>(),
            provider.GetRequiredService<MariaSchemaMigrator>(), provider.GetRequiredService<AppHostingInfo>(), provider.GetRequiredService<CircuitCounter>(),
            provider.GetRequiredService<IServer>(), provider.GetRequiredService<IHostEnvironment>(), provider.GetRequiredService<OpenSessionRegistry>(), provider.GetRequiredService<IAuditTrail>()));
        services.AddScoped<IDatabaseAccountService>(provider => new DatabaseAccountService(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IDataProtectionProvider>(),
            provider.GetRequiredService<IAccessControl>(), provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<IOperationLockService>(),
            provider.GetRequiredService<ILogger<DatabaseAccountService>>(), provider.GetService<IUserAuthenticator>()));
        services.AddSingleton<IPortInspector, NetstatPortInspector>();
        services.AddSingleton<IApplicationRestart, ApplicationRestart>();
        if (OperatingSystem.IsWindows()) services.AddSingleton<IServerServices, WindowsServerServices>(); else services.AddSingleton<IServerServices, NoServerServices>();
        services.AddScoped<IDatabaseServerService>(provider => new DatabaseServerService(provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IAccessControl>(),
            provider.GetRequiredService<IAuditTrail>(), provider.GetRequiredService<IOperationLockService>(), provider.GetRequiredService<IDatabaseSettingsService>(),
            provider.GetRequiredService<IServerServices>(), provider.GetRequiredService<IPortInspector>(), provider.GetRequiredService<IApplicationRestart>(),
            provider.GetRequiredService<ILogger<DatabaseServerService>>(), provider.GetService<IUserAuthenticator>()));
        services.AddHostedService<DatabaseAccountRecovery>();
        return services;
    }
}

/// <summary>At start: a password change that was interrupted between the two phases is completed or dropped.</summary>
internal sealed class DatabaseAccountRecovery(IServiceScopeFactory scopes, ILogger<DatabaseAccountRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDatabaseAccountService>().RecoverPendingAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("The check of an interrupted password change could not be done ({ErrorType}).", exception.GetType().Name); }
    }
}
