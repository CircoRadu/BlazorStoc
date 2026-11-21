using BlazorStoc.Startup;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Services;

// The authentication of the application with ANAF (OAuth2 of the e-Factura service, tab "Factura" of Setari -> ANAF): the registered application (client id, secret,
// redirect address), the sign-in made by the administrator (ANAF asks for the qualified certificate in the browser of the administrator) and the tokens that result.
// Only the authentication lives here; the use of the tokens (the invoices of e-Factura) is a later step. The secret and the tokens are stored protected (data protection keys).

public sealed class AnafAuthSettings
{
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Environment { get; set; } = AnafAuthRules.Production;
    /// <summary>Protected (never shown): empty when no secret was saved.</summary>
    public string ProtectedSecret { get; set; } = "";
}

public sealed class AnafAuthTokens
{
    public string ProtectedAccess { get; set; } = "";
    public string ProtectedRefresh { get; set; } = "";
    public DateTimeOffset ObtainedAt { get; set; }
    public DateTimeOffset AccessExpiresAt { get; set; }
    public string Admin { get; set; } = "";
}

public sealed class AnafAuthFile
{
    public AnafAuthSettings Settings { get; set; } = new();
    public AnafAuthTokens? Tokens { get; set; }
    /// <summary>The pending sign-in: the random "state" sent to ANAF and its expiry (the answer must come back with the same value).</summary>
    public string PendingState { get; set; } = "";
    public DateTimeOffset PendingUntil { get; set; }
}

// What the page shows: the secret and the tokens are never part of it.
public sealed record AnafAuthView(string ClientId, string RedirectUri, string Environment, bool HasSecret, bool Connected, bool AccessValid,
    DateTimeOffset? ObtainedAt, DateTimeOffset? AccessExpiresAt, string? Admin);

public static class AnafAuthRules
{
    public const string Production = "prod";
    public const string Test = "test";
    public const string AuthorizeUrl = "https://logincert.anaf.ro/anaf-oauth2/v1/authorize";
    public const string TokenUrl = "https://logincert.anaf.ro/anaf-oauth2/v1/token";
    public const string CallbackPath = "/anaf/autentificare/raspuns";
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    public static string? Validate(AnafAuthSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || settings.ClientId.Length > 200 || settings.ClientId.Any(char.IsWhiteSpace)) return "Identificatorul aplicației (client id) este obligatoriu, fără spații.";
        if (settings.Environment is not (Production or Test)) return "Mediul trebuie să fie producție sau test.";
        if (!Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return "Adresa de răspuns (redirect) trebuie să fie o adresă HTTPS completă, fără parametri, exact cea înregistrată la ANAF.";
        return null;
    }

    public static string BuildAuthorizeUrl(AnafAuthSettings settings, string state) =>
        AuthorizeUrl + "?response_type=code&client_id=" + Uri.EscapeDataString(settings.ClientId) + "&redirect_uri=" + Uri.EscapeDataString(settings.RedirectUri)
        + "&token_content_type=jwt&state=" + Uri.EscapeDataString(state);

    public static string NewState() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}

public interface IAnafAuthService
{
    Task<AnafAuthView> GetAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AnafAuthSettings settings, string? newSecret, CancellationToken cancellationToken = default);
    /// <summary>Starts a sign-in: remembers a random state and returns the ANAF address the administrator is sent to.</summary>
    Task<string> BeginAsync(CancellationToken cancellationToken = default);
    /// <summary>Ends a sign-in: checks the state and exchanges the code for the tokens.</summary>
    Task CompleteAsync(string? code, string? state, CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(string reason, CancellationToken cancellationToken = default);
}

public sealed class AnafAuthStore(IConfiguration configuration)
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    private readonly string path = Path.GetFullPath(configuration["Anaf:AuthPath"] ?? AppDataRoot.DataFile(configuration, "anaf-auth.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<AnafAuthFile> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new AnafAuthFile();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AnafAuthFile>(stream, Options, cancellationToken) ?? new AnafAuthFile();
    }

    public async Task WriteAsync(AnafAuthFile file, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(file, Options), new UTF8Encoding(false), cancellationToken);
        File.Move(temp, path, true);
    }
}

public sealed class AnafAuthService(AnafAuthStore store, IHttpClientFactory httpClientFactory, IDataProtectionProvider protection, IAccessControl access, IAuditTrail auditTrail,
    ILogger<AnafAuthService> logger) : IAnafAuthService
{
    private const string Key = "setari-anaf.edit";
    private IDataProtector Protector => protection.CreateProtector("BlazorStoc.Anaf.Auth");

    public async Task<AnafAuthView> GetAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        var file = await ReadAsync(cancellationToken);
        var tokens = file.Tokens;
        return new(file.Settings.ClientId, file.Settings.RedirectUri, file.Settings.Environment, file.Settings.ProtectedSecret.Length > 0, tokens is not null,
            tokens is not null && tokens.AccessExpiresAt > DateTimeOffset.Now, tokens?.ObtainedAt, tokens?.AccessExpiresAt, tokens?.Admin);
    }

    private async Task<AnafAuthFile> ReadAsync(CancellationToken cancellationToken)
    {
        await store.Gate.WaitAsync(cancellationToken);
        try { return await store.ReadAsync(cancellationToken); }
        finally { store.Gate.Release(); }
    }

    public async Task SaveSettingsAsync(AnafAuthSettings settings, string? newSecret, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        settings.ClientId = (settings.ClientId ?? "").Trim();
        settings.RedirectUri = (settings.RedirectUri ?? "").Trim();
        if (AnafAuthRules.Validate(settings) is { } problem) throw new AnafException(problem);
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            var file = await store.ReadAsync(cancellationToken);
            var old = file.Settings;
            var secretChanged = !string.IsNullOrEmpty(newSecret);
            if (old.ProtectedSecret.Length == 0 && !secretChanged) throw new AnafException("Secretul aplicației (client secret) este obligatoriu la prima salvare.");
            var changes = new List<AuditChange>();
            if (old.ClientId != settings.ClientId) changes.Add(new("Client id", old.ClientId.Length == 0 ? "—" : old.ClientId, settings.ClientId));
            if (old.RedirectUri != settings.RedirectUri) changes.Add(new("Adresă de răspuns", old.RedirectUri.Length == 0 ? "—" : old.RedirectUri, settings.RedirectUri));
            if (old.Environment != settings.Environment) changes.Add(new("Mediu", old.Environment, settings.Environment));
            if (secretChanged) changes.Add(new("Client secret", old.ProtectedSecret.Length == 0 ? "nesetat" : "setat", "schimbat (valoarea nu se jurnalizează)"));
            file.Settings = new AnafAuthSettings
            {
                ClientId = settings.ClientId, RedirectUri = settings.RedirectUri, Environment = settings.Environment,
                ProtectedSecret = secretChanged ? Protector.Protect(newSecret!) : old.ProtectedSecret
            };
            // Tokens given to another application (or environment) are of no use to the new settings.
            if (file.Tokens is not null && (old.ClientId != settings.ClientId || old.Environment != settings.Environment || secretChanged)) { file.Tokens = null; changes.Add(new("Token", "obținut", "șters (setările s-au schimbat)")); }
            file.PendingState = "";
            await store.WriteAsync(file, cancellationToken);
            if (changes.Count > 0)
                await AuditRecorder.RecordEditAsync(auditTrail, access, "IntegrareANAF", "autentificare", "Autentificare ANAF", changes, "Modificare setări autentificare ANAF", cancellationToken, AuditActions.EditAnafAuthSettings);
        }
        finally { store.Gate.Release(); }
    }

    public async Task<string> BeginAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            var file = await store.ReadAsync(cancellationToken);
            if (file.Settings.ProtectedSecret.Length == 0) throw new AnafException("Salvează mai întâi setările aplicației (client id, secret, adresă de răspuns).");
            if (AnafAuthRules.Validate(file.Settings) is { } problem) throw new AnafException(problem);
            file.PendingState = AnafAuthRules.NewState();
            file.PendingUntil = DateTimeOffset.Now + AnafAuthRules.PendingLifetime;
            await store.WriteAsync(file, cancellationToken);
            return AnafAuthRules.BuildAuthorizeUrl(file.Settings, file.PendingState);
        }
        finally { store.Gate.Release(); }
    }

    public async Task CompleteAsync(string? code, string? state, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(code)) throw new AnafException("ANAF nu a trimis codul de autorizare.");
        AnafAuthFile file;
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            file = await store.ReadAsync(cancellationToken);
            var valid = file.PendingState.Length > 0 && file.PendingUntil > DateTimeOffset.Now && !string.IsNullOrEmpty(state)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(file.PendingState), Encoding.UTF8.GetBytes(state));
            if (!valid) throw new AnafException("Autentificarea nu a fost pornită din această aplicație sau a expirat (10 minute). Pornește-o din nou.");
            file.PendingState = "";
            await store.WriteAsync(file, cancellationToken);
        }
        finally { store.Gate.Release(); }
        var tokens = await RequestTokensAsync(file.Settings, new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = file.Settings.RedirectUri }, cancellationToken);
        tokens.Admin = await access.GetUsernameAsync(cancellationToken) ?? "Administrator";
        await SaveTokensAsync(tokens, "Autentificare în ANAF", AuditActions.ConnectAnafAuth, cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        var file = await ReadAsync(cancellationToken);
        if (file.Tokens is null || file.Tokens.ProtectedRefresh.Length == 0) throw new AnafException("Nu există un token de reînnoit. Autentifică-te în ANAF.");
        string refresh;
        try { refresh = Protector.Unprotect(file.Tokens.ProtectedRefresh); }
        catch (CryptographicException) { throw new AnafException("Tokenul salvat nu mai poate fi citit. Autentifică-te din nou în ANAF."); }
        var tokens = await RequestTokensAsync(file.Settings, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, cancellationToken);
        tokens.Admin = await access.GetUsernameAsync(cancellationToken) ?? "Administrator";
        await SaveTokensAsync(tokens, "Reînnoire token ANAF", AuditActions.RefreshAnafAuth, cancellationToken);
    }

    public async Task DisconnectAsync(string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Key, cancellationToken);
        if (string.IsNullOrWhiteSpace(reason)) throw new AnafException("Motivul deconectării este obligatoriu.");
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            var file = await store.ReadAsync(cancellationToken);
            if (file.Tokens is null) throw new AnafException("Aplicația nu este autentificată în ANAF.");
            var before = file.Tokens;
            file.Tokens = null;
            await store.WriteAsync(file, cancellationToken);
            await AuditRecorder.RecordEditAsync(auditTrail, access, "IntegrareANAF", "autentificare", "Autentificare ANAF",
                [new AuditChange("Token", $"obținut {before.ObtainedAt:dd.MM.yyyy HH:mm} de {before.Admin}", "șters")], reason.Trim(), cancellationToken, AuditActions.DisconnectAnafAuth);
        }
        finally { store.Gate.Release(); }
    }

    private async Task SaveTokensAsync(AnafAuthTokens tokens, string motif, string action, CancellationToken cancellationToken)
    {
        await store.Gate.WaitAsync(cancellationToken);
        try
        {
            var file = await store.ReadAsync(cancellationToken);
            var before = file.Tokens;
            file.Tokens = tokens;
            await store.WriteAsync(file, cancellationToken);
            await AuditRecorder.RecordEditAsync(auditTrail, access, "IntegrareANAF", "autentificare", "Autentificare ANAF",
                [new AuditChange("Token", before is null ? "neobținut" : $"valabil până la {before.AccessExpiresAt:dd.MM.yyyy HH:mm}", $"valabil până la {tokens.AccessExpiresAt:dd.MM.yyyy HH:mm}")], motif, cancellationToken, action);
        }
        finally { store.Gate.Release(); }
    }

    private async Task<AnafAuthTokens> RequestTokensAsync(AnafAuthSettings settings, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        string secret;
        try { secret = Protector.Unprotect(settings.ProtectedSecret); }
        catch (CryptographicException) { throw new AnafException("Secretul salvat nu mai poate fi citit. Introdu-l din nou în setări."); }
        form["client_id"] = settings.ClientId;
        form["client_secret"] = secret;
        form["token_content_type"] = "jwt";
        try
        {
            using var client = httpClientFactory.CreateClient(AnafService.ClientName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await client.PostAsync(AnafAuthRules.TokenUrl, new FormUrlEncodedContent(form), timeout.Token);
            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("ANAF token request refused with HTTP {Status}", (int)response.StatusCode);
                throw new AnafException($"ANAF a refuzat cererea de token (HTTP {(int)response.StatusCode}). Verifică client id, secretul și adresa de răspuns înregistrate la ANAF.");
            }
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(access)) throw new AnafException("ANAF nu a returnat un token.");
            var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var parsed) && parsed > 0 ? parsed : ExpiryFromJwt(access);
            var now = DateTimeOffset.Now;
            return new AnafAuthTokens
            {
                ProtectedAccess = Protector.Protect(access), ProtectedRefresh = string.IsNullOrEmpty(refresh) ? "" : Protector.Protect(refresh),
                ObtainedAt = now, AccessExpiresAt = now.AddSeconds(seconds)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AnafException("ANAF nu a răspuns în 30 de secunde."); }
        catch (HttpRequestException ex) { logger.LogWarning(ex, "ANAF token request failed"); throw new AnafException("Serviciul de autentificare ANAF nu este disponibil."); }
        catch (JsonException) { throw new AnafException("ANAF a returnat un răspuns necitibil."); }
    }

    // ANAF puts the expiry in the JWT ("exp"); used when the answer has no "expires_in".
    private static int ExpiryFromJwt(string jwt)
    {
        try
        {
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
                return (int)Math.Max(60, seconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception) { }
        return 3600;
    }
}

internal sealed class GuardedAnafAuthService(IAnafAuthService inner, BlazorStoc.Modules.ModuleRegistry modules) : IAnafAuthService
{
    private const string Id = BlazorStoc.Modules.Anaf.AnafModule.ModuleId;
    public Task<AnafAuthView> GetAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(cancellationToken));
    public Task SaveSettingsAsync(AnafAuthSettings settings, string? newSecret, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveSettingsAsync(settings, newSecret, cancellationToken));
    public Task<string> BeginAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.BeginAsync(cancellationToken));
    public Task CompleteAsync(string? code, string? state, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CompleteAsync(code, state, cancellationToken));
    public Task RefreshAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RefreshAsync(cancellationToken));
    public Task DisconnectAsync(string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DisconnectAsync(reason, cancellationToken));
}

internal sealed class UnavailableAnafAuthService : IAnafAuthService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new BlazorStoc.Modules.ModuleUnavailableException(BlazorStoc.Modules.Anaf.AnafModule.ModuleId));
    public Task<AnafAuthView> GetAsync(CancellationToken cancellationToken = default) => Out<AnafAuthView>();
    public Task SaveSettingsAsync(AnafAuthSettings settings, string? newSecret, CancellationToken cancellationToken = default) => Out<object>();
    public Task<string> BeginAsync(CancellationToken cancellationToken = default) => Out<string>();
    public Task CompleteAsync(string? code, string? state, CancellationToken cancellationToken = default) => Out<object>();
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Out<object>();
    public Task DisconnectAsync(string reason, CancellationToken cancellationToken = default) => Out<object>();
}
