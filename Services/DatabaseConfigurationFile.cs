using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorStoc.Startup;

namespace BlazorStoc.Services;

/// <summary>The account of the server that administers it (root): read from admin.private.cnf in the folder of the data, protected there by the permissions of the
/// folder. It is used only by the operations of the page of the accounts (reset, export, import); the application never connects with it otherwise.</summary>
public sealed record DatabaseAdministrationAccount(string Host, int Port, string User, string Password)
{
    public const string FileName = "admin.private.cnf";

    public static string PathFor(IConfiguration configuration) =>
        configuration["Database:AdminConfigPath"] is { Length: > 0 } configured ? configured : AppDataRoot.Combine(configuration, FileName);

    public static DatabaseAdministrationAccount? Read(IConfiguration configuration)
    {
        var path = PathFor(configuration);
        string[] lines;
        try { lines = File.Exists(path) ? File.ReadAllLines(path) : []; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || separator <= 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        if (!values.TryGetValue("user", out var user) || !values.TryGetValue("password", out var password) || user.Length == 0 || password.Length == 0) return null;
        var port = values.TryGetValue("port", out var portText) && int.TryParse(portText, out var parsed) ? parsed : configuration.GetValue("Database:Port", 3307);
        return new(values.GetValueOrDefault("host") ?? configuration["Database:Host"] ?? "127.0.0.1", port, user, password);
    }

    public static void Write(IConfiguration configuration, DatabaseAdministrationAccount account)
    {
        var path = PathFor(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, $"[client]\nhost={account.Host}\nport={account.Port}\nprotocol=tcp\nuser={account.User}\npassword={account.Password}\ndefault-character-set=utf8mb4\n", new UTF8Encoding(false));
    }
}

/// <summary>The file the administrator keeps after the passwords were changed: the server, the predefined accounts with their passwords and, when asked, the account that
/// administers the server. It holds secrets, so it is always encrypted with a pass phrase (PBKDF2 and AES-GCM); nothing of it stays on the server.</summary>
public static class DatabaseConfigurationFile
{
    public const string Format = "BlazorStoc.DatabaseConfiguration";
    public const int Iterations = 300_000;
    public const int MinimumPassphraseLength = 12;
    public const string PassphraseTooShortMessage = "Fraza-parolă trebuie să aibă cel puțin 12 caractere.";
    public const string WrongPassphraseMessage = "Fraza-parolă nu este corectă sau fișierul este deteriorat.";
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes(Format + ".v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public sealed record ServerPart(string Host, int Port, string Name, string SslMode);
    public sealed record AccountPart(string Key, string User, string Purpose, string Password);
    public sealed record AdministrationPart(string Host, int Port, string User, string Password);
    public sealed record Content(DateTime CreatedUtc, string CreatedBy, ServerPart Server, IReadOnlyList<AccountPart> Accounts, AdministrationPart? Administration);

    private sealed record Envelope(string Format, int Version, string Kdf, int Iterations, string Salt, string Nonce, string Tag, string Data);

    public static string? ValidatePassphrase(string? passphrase) =>
        string.IsNullOrEmpty(passphrase) || passphrase.Length < MinimumPassphraseLength ? PassphraseTooShortMessage : null;

    public static byte[] Encrypt(Content content, string passphrase)
    {
        if (ValidatePassphrase(passphrase) is { } problem) throw new DatabaseSettingsException(problem);
        var plain = JsonSerializer.SerializeToUtf8Bytes(content, JsonOptions);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length)) aes.Encrypt(nonce, plain, cipher, tag, AssociatedData);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plain);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(Format, 1, "PBKDF2-SHA256", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cipher)), JsonOptions);
    }

    public static Content Decrypt(byte[] file, string passphrase)
    {
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(file, JsonOptions); }
        catch (JsonException) { throw new DatabaseSettingsException("Fișierul nu este un fișier de configurare al bazei de date."); }
        if (envelope is null || envelope.Format != Format || envelope.Version != 1 || envelope.Iterations is < 100_000 or > 5_000_000)
            throw new DatabaseSettingsException("Fișierul nu este un fișier de configurare al bazei de date.");
        try
        {
            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var tag = Convert.FromBase64String(envelope.Tag);
            var cipher = Convert.FromBase64String(envelope.Data);
            var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase ?? ""), salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            try { using var aes = new AesGcm(key, tag.Length); aes.Decrypt(nonce, cipher, tag, plain, AssociatedData); }
            finally { CryptographicOperations.ZeroMemory(key); }
            var content = JsonSerializer.Deserialize<Content>(plain, JsonOptions);
            CryptographicOperations.ZeroMemory(plain);
            return content ?? throw new DatabaseSettingsException(WrongPassphraseMessage);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            throw new DatabaseSettingsException(WrongPassphraseMessage);
        }
    }

    public static string FileNameFor(DateTime utcNow) => $"ESPGestiuneStoc-baza-de-date-{utcNow.ToLocalTime():yyyyMMdd-HHmm}.bsconfig";
}
