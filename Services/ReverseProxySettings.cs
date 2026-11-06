using System.Net;
using System.Text.Json;
using BlazorStoc.Services;

namespace BlazorStoc.Services;

/// <summary>Setari -> Baza de date -> Aplicatia: the application is served through a reverse proxy that ends TLS (nginx). Off = plain HTTP exactly as before.</summary>
public sealed record ReverseProxyOptions(bool Enabled, IReadOnlyList<string> TrustedProxies)
{
    public static ReverseProxyOptions Off { get; } = new(false, []);
}

public static class ReverseProxyRules
{
    public const string FileName = "reverse-proxy.json";
    public const string NeedProxyMessage = "Cu HTTPS pornit scrie cel puțin un IP (sau rețea CIDR) al proxy-ului, altfel aplicația nu are în cine să aibă încredere.";

    public static string PathFor(IConfiguration configuration) =>
        configuration["Hosting:ProxyPath"] is { Length: > 0 } configured ? configured : BlazorStoc.Startup.AppDataRoot.Combine(configuration, FileName);

    /// <summary>One address or one network per entry (10.0.0.5, fd00::1, 10.0.0.0/24). Null = valid.</summary>
    public static string? ValidateEntry(string entry)
    {
        var text = entry.Trim();
        if (text.Length == 0) return "Un IP gol nu este valid.";
        var slash = text.IndexOf('/');
        var address = slash < 0 ? text : text[..slash];
        if (!IPAddress.TryParse(address, out var ip)) return $"'{text}' nu este o adresă IP validă.";
        if (slash < 0) return null;
        var max = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return int.TryParse(text[(slash + 1)..], out var prefix) && prefix >= 0 && prefix <= max ? null : $"'{text}': prefixul rețelei trebuie să fie între 0 și {max}.";
    }

    /// <summary>Splits what the administrator typed (comma, semicolon, space or new line), trims, removes duplicates. Invalid entries are returned in <paramref name="problem"/>.</summary>
    public static IReadOnlyList<string> Parse(string? typed, out string? problem)
    {
        problem = null;
        var list = new List<string>();
        foreach (var raw in (typed ?? "").Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (ValidateEntry(raw) is { } bad) { problem = bad; return []; }
            var entry = raw.Trim();
            if (!list.Contains(entry, StringComparer.OrdinalIgnoreCase)) list.Add(entry);
        }
        return list;
    }

    public static string? Validate(ReverseProxyOptions options)
    {
        if (options.Enabled && options.TrustedProxies.Count == 0) return NeedProxyMessage;
        foreach (var entry in options.TrustedProxies) if (ValidateEntry(entry) is { } problem) return problem;
        return null;
    }

    /// <summary>The settings in effect, read from the configuration (the file is a configuration source, see PrivateConfiguration).</summary>
    public static ReverseProxyOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Hosting:Https");
        var proxies = section.GetSection("TrustedProxies").GetChildren().Select(item => item.Value).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToArray();
        return new(section.GetValue("Enabled", false), proxies);
    }

    /// <summary>Entries that parse become addresses and networks for the forwarded-headers middleware; the rest was already refused on saving.</summary>
    public static (IReadOnlyList<IPAddress> Addresses, IReadOnlyList<(IPAddress Prefix, int Length)> Networks) Split(IEnumerable<string> entries)
    {
        var addresses = new List<IPAddress>();
        var networks = new List<(IPAddress, int)>();
        foreach (var entry in entries)
        {
            var slash = entry.IndexOf('/');
            if (slash < 0) { if (IPAddress.TryParse(entry, out var ip)) addresses.Add(ip); }
            else if (IPAddress.TryParse(entry[..slash], out var prefix) && int.TryParse(entry[(slash + 1)..], out var length)) networks.Add((prefix, length));
        }
        return (addresses, networks);
    }

    public static void Write(string path, ReverseProxyOptions options)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { Hosting = new { Https = new { options.Enabled, options.TrustedProxies } } }, new JsonSerializerOptions { WriteIndented = true });
        DatabaseOverrideFile.WriteRaw(path, json);
    }
}

public sealed record ReverseProxySaveResult(bool Success, string Message, bool RestartNeeded = false);

public interface IReverseProxySettingsService
{
    Task<ReverseProxyOptions> GetAsync(CancellationToken cancellationToken = default);
    Task<ReverseProxySaveResult> SaveAsync(ReverseProxyOptions options, string administratorPassword, CancellationToken cancellationToken = default);
}

public sealed class ReverseProxySettingsService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, ILogger<ReverseProxySettingsService> logger,
    IUserAuthenticator? authenticator = null) : IReverseProxySettingsService
{
    public async Task<ReverseProxyOptions> GetAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return ReverseProxyRules.Read(configuration);
    }

    public async Task<ReverseProxySaveResult> SaveAsync(ReverseProxyOptions options, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var normalized = new ReverseProxyOptions(options.Enabled, options.TrustedProxies.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if (ReverseProxyRules.Validate(normalized) is { } problem) throw new DatabaseSettingsException(problem);
        var current = ReverseProxyRules.Read(configuration);
        if (current.Enabled == normalized.Enabled && current.TrustedProxies.SequenceEqual(normalized.TrustedProxies, StringComparer.OrdinalIgnoreCase))
            return new(true, "Nu există modificări de salvat.");
        var path = ReverseProxyRules.PathFor(configuration);
        try
        {
            ReverseProxyRules.Write(path, normalized);
            (configuration as IConfigurationRoot)?.Reload();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError("The reverse proxy settings could not be saved ({ErrorType}).", exception.GetType().Name);
            throw new DatabaseSettingsException("Setările nu au putut fi salvate. Verifică drepturile asupra folderului de configurare.");
        }
        try
        {
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Database, AuditActions.SetReverseProxy, "", "HTTPS prin proxy", AuditDetails.Changes(
                new AuditChange("HTTPS prin proxy", current.Enabled ? "pornit" : "oprit", normalized.Enabled ? "pornit" : "oprit"),
                new AuditChange("IP proxy", Show(current.TrustedProxies), Show(normalized.TrustedProxies))), "", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}).", AuditActions.SetReverseProxy, exception.GetType().Name);
        }
        return new(true, "Setările au fost salvate; se aplică la următoarea pornire a aplicației.", RestartNeeded: true);
    }

    private static string Show(IReadOnlyList<string> list) => list.Count == 0 ? "niciunul" : string.Join(", ", list);
}
