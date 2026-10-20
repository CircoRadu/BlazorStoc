using Microsoft.Extensions.Hosting;

namespace BlazorStoc.Startup;

// A background service of the application (the backup scheduler, the relay of the change events, the run tracking) that fails must not take the whole application with it:
// by default .NET stops the host when the work of a BackgroundService throws, so one faulty service would end every page and every module with it. The failure is still
// logged by the host (as a critical error with the exception) and the supervisor and the journal see the rest; only that service stops.
public static class HostResilience
{
    public static IServiceCollection AddHostResilience(this IServiceCollection services)
    {
        services.Configure<HostOptions>(options => options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        return services;
    }
}
