using BlazorStoc.Services;
using BlazorStoc.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Checks;

// Setari -> Baza de date -> HTTPS prin proxy: the rules of the addresses, the file as a configuration source, and the real behaviour of the pipeline
// (a small host on a free local port): off = plain HTTP as before, on = only the listed proxy is believed, HSTS and Secure cookies.
public static class ReverseProxyChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== HTTPS prin proxy ===");
        Rules(check);
        Blocking(check);
        File(check);
        await PipelineAsync(check);
    }

    private static void Rules(Action<bool, string> check)
    {
        check(ReverseProxyRules.ValidateEntry("10.0.0.5") is null && ReverseProxyRules.ValidateEntry("fd00::1") is null && ReverseProxyRules.ValidateEntry("10.0.0.0/24") is null,
            "HTTPS proxy: an address, an IPv6 address and a network are accepted");
        check(ReverseProxyRules.ValidateEntry("nginx") is not null && ReverseProxyRules.ValidateEntry("10.0.0.0/33") is not null && ReverseProxyRules.ValidateEntry("10.0.0.300") is not null,
            "HTTPS proxy: a name, a prefix above 32 or an impossible address are refused");
        var list = ReverseProxyRules.Parse("10.0.0.1, 10.0.0.2;10.0.0.1\r\n10.1.0.0/16", out var problem);
        check(problem is null && list.SequenceEqual(["10.0.0.1", "10.0.0.2", "10.1.0.0/16"]), "HTTPS proxy: the typed list is split on commas and lines and without duplicates");
        ReverseProxyRules.Parse("10.0.0.1, abc", out var bad);
        check(bad is not null, "HTTPS proxy: one invalid entry refuses the whole list");
        check(ReverseProxyRules.Validate(new(true, [])) == ReverseProxyRules.NeedProxyMessage && ReverseProxyRules.Validate(new(false, [])) is null && ReverseProxyRules.Validate(new(true, ["10.0.0.1"])) is null,
            "HTTPS proxy: on needs at least one address, off needs none");
    }

    private static void Blocking(Action<bool, string> check)
    {
        check(ReverseProxyStartup.ShouldBlock(true, false, false, "/") && ReverseProxyStartup.ShouldBlock(true, false, false, "/Account/Login"),
            "HTTPS proxy on: a plain-http visit from another computer is refused with a clear page");
        check(!ReverseProxyStartup.ShouldBlock(true, true, false, "/") && !ReverseProxyStartup.ShouldBlock(true, false, true, "/setari")
              && !ReverseProxyStartup.ShouldBlock(true, false, false, "/health/live") && !ReverseProxyStartup.ShouldBlock(true, false, false, "/health"),
            "HTTPS proxy on: https, the computer itself and the monitoring addresses are not refused");
        check(!ReverseProxyStartup.ShouldBlock(false, false, false, "/"), "HTTPS proxy off: nothing is refused (plain HTTP as before)");
    }

    private static void File(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-proxy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, ReverseProxyRules.FileName);
            var empty = ReverseProxyRules.Read(new ConfigurationBuilder().AddJsonFile(path, optional: true).Build());
            check(!empty.Enabled && empty.TrustedProxies.Count == 0, "HTTPS proxy: without the file it is off");
            ReverseProxyRules.Write(path, new(true, ["10.0.0.1", "10.2.0.0/16"]));
            var read = ReverseProxyRules.Read(new ConfigurationBuilder().AddJsonFile(path, optional: true).Build());
            check(read.Enabled && read.TrustedProxies.SequenceEqual(["10.0.0.1", "10.2.0.0/16"]), "HTTPS proxy: what is saved is read back from the file as configuration");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static async Task<WebApplication> StartAsync(params (string, string)[] values)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(values.ToDictionary(pair => pair.Item1, pair => (string?)pair.Item2));
        builder.Services.AddReverseProxy(builder.Configuration);
        var app = builder.Build();
        app.UseReverseProxy();
        app.MapGet("/probe", (HttpContext context) => Results.Text(context.Request.Scheme + "|" + context.Connection.RemoteIpAddress));
        await app.StartAsync();
        return app;
    }

    private static async Task PipelineAsync(Action<bool, string> check)
    {
        async Task<(HttpResponseMessage Response, string Body)> Get(WebApplication app, bool forwardedHttps)
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
            if (forwardedHttps) { request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-For", "10.9.9.9"); }
            var response = await client.SendAsync(request);
            return (response, await response.Content.ReadAsStringAsync());
        }

        await using (var off = await StartAsync())
        {
            var (response, body) = await Get(off, forwardedHttps: true);
            check(body.StartsWith("http|", StringComparison.Ordinal) && !body.Contains("10.9.9.9", StringComparison.Ordinal) && !response.Headers.Contains("Strict-Transport-Security"),
                "HTTPS proxy off: forwarded headers are ignored and no HSTS is sent (plain HTTP as before)");
            check(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) && nosniff.Contains("nosniff") && response.Headers.Contains("X-Frame-Options")
                  && response.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Any(value => value.Contains("frame-ancestors 'self'", StringComparison.Ordinal)),
                "Security headers: nosniff, X-Frame-Options and frame-ancestors are sent even with the proxy off");
        }
        await using (var trusted = await StartAsync(("Hosting:Https:Enabled", "true"), ("Hosting:Https:TrustedProxies:0", "127.0.0.1")))
        {
            var (response, body) = await Get(trusted, forwardedHttps: true);
            check(body.StartsWith("https|10.9.9.9", StringComparison.Ordinal) && response.Headers.Contains("Strict-Transport-Security"),
                "HTTPS proxy on: the headers of the listed proxy are believed (scheme https, client address) and HSTS is sent");
            var (plain, plainBody) = await Get(trusted, forwardedHttps: false);
            check(plainBody.StartsWith("http|", StringComparison.Ordinal) && !plain.Headers.Contains("Strict-Transport-Security"), "HTTPS proxy on: a request without forwarded headers stays http and gets no HSTS");
            var cookie = trusted.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            check(cookie.Cookie.SecurePolicy == CookieSecurePolicy.Always, "HTTPS proxy on: the session cookie is Secure");
        }
        await using (var foreign = await StartAsync(("Hosting:Https:Enabled", "true"), ("Hosting:Https:TrustedProxies:0", "10.77.77.77")))
        {
            var (response, body) = await Get(foreign, forwardedHttps: true);
            check(body.StartsWith("http|", StringComparison.Ordinal) && !body.Contains("10.9.9.9", StringComparison.Ordinal) && !response.Headers.Contains("Strict-Transport-Security"),
                "HTTPS proxy on: headers from an address that is not in the list are ignored");
        }
        await using (var empty = await StartAsync(("Hosting:Https:Enabled", "true")))
        {
            var (_, body) = await Get(empty, forwardedHttps: true);
            check(body.StartsWith("http|", StringComparison.Ordinal), "HTTPS proxy on without any address stays off instead of trusting everyone");
        }
    }
}
