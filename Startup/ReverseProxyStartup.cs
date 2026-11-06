using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

namespace BlazorStoc.Startup;

/// <summary>
/// HTTPS through a reverse proxy that ends TLS (Setari -> Baza de date -> Aplicatia). Off (the default) = the application stays on plain HTTP exactly as before: no forwarded
/// headers are trusted, the cookie works over HTTP and no HSTS is sent. On = the headers X-Forwarded-For/Proto of the listed proxies are trusted (so the scheme is https),
/// the cookies are Secure (a direct visit over HTTP cannot log in, which is the point) and HSTS is sent on https responses. The security headers below are sent in both cases.
/// </summary>
public static class ReverseProxyStartup
{
    public static IServiceCollection AddReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        var options = ReverseProxyRules.Read(configuration);
        if (!options.Enabled || ReverseProxyRules.Validate(options) is not null) return services;
        var (addresses, networks) = ReverseProxyRules.Split(options.TrustedProxies);
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Only the proxies written on the page are trusted: the default (loopback) is replaced, so nobody else can claim another address or https.
            forwarded.KnownProxies.Clear();
            forwarded.KnownNetworks.Clear();
            foreach (var address in addresses) forwarded.KnownProxies.Add(address);
            foreach (var (prefix, length) in networks) forwarded.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
        });
        services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, cookie => cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always);
        // The antiforgery cookie keeps SameAsRequest: with Always it throws on every plain-HTTP request; those are answered by the page below instead.
        return services;
    }

    public const string DirectAccessMessage = "Aplicația este disponibilă doar prin adresa https a serverului. Deschide adresa primită de la administrator.";

    public static bool ShouldBlock(bool enabled, bool isHttps, bool isLocal, PathString path) =>
        enabled && !isHttps && !isLocal && !path.StartsWithSegments("/health");

    private static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address);

    public static WebApplication UseReverseProxy(this WebApplication app)
    {
        var options = ReverseProxyRules.Read(app.Configuration);
        var enabled = options.Enabled && ReverseProxyRules.Validate(options) is null;
        if (options.Enabled && !enabled) app.Logger.LogWarning("HTTPS through a proxy is switched on but no valid proxy address is saved; it stays off until one is written in Setari.");
        if (enabled) app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            // A visit that did not come through the proxy (plain http from another computer) gets a clear page instead of a login that cannot keep its session. The monitoring
            // addresses (/health...) and the computer itself (loopback: the way back when the proxy is broken) are not blocked.
            if (ShouldBlock(enabled, context.Request.IsHttps, IsLocal(context), context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(DirectAccessMessage);
                return;
            }
            var headers = context.Response.Headers;
            context.Response.OnStarting(() =>
            {
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "SAMEORIGIN";
                headers["Referrer-Policy"] = "same-origin";
                headers["Content-Security-Policy"] = "frame-ancestors 'self'";
                if (enabled && context.Request.IsHttps) headers["Strict-Transport-Security"] = "max-age=31536000";
                return Task.CompletedTask;
            });
            await next();
        });
        return app;
    }
}
