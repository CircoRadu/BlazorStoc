using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Services;

// Setari -> Baza de date -> Conexiune: host, port, user and password of the application account, and the port the application listens on.
// The values cannot live in the database they describe, so they are kept in a small override file outside the repository (loaded last into the
// configuration, so it wins over the private file and the environment). The password is stored protected (ASP.NET Data Protection, like the NAS
// password) and is never shown, logged or audited. Only the administrator configured in the private file may use any of this (IAccessControl).

public sealed class DatabaseSettingsException(string message) : Exception(message);

public sealed record DatabaseConnectionSettings(string Host, int Port, string Name, string User, string SslMode, bool HasPassword,
    int? AppPort, bool AppBindAll, bool OverrideActive, string? OverrideProblem, string OverridePath);

/// <summary>What the administrator typed. A null <see cref="Password"/> keeps the current one.</summary>
public sealed record DatabaseConnectionInput(string Host, int Port, string User, string? Password, string SslMode, int? AppPort, bool AppBindAll);

public enum DatabaseStepLevel { Ok, Warning, Error }

public sealed record DatabaseTestStep(string Name, DatabaseStepLevel Level, string Detail);

public sealed record DatabaseTestResult(IReadOnlyList<DatabaseTestStep> Steps)
{
    public bool Ok => Steps.All(step => step.Level != DatabaseStepLevel.Error);
}

public sealed record DatabaseSaveResult(bool Success, string Message, bool RolledBack = false, bool RestartNeeded = false);

public static class DatabaseConnectionRules
{
    public const string HostMessage = "Introdu un host valid (nume sau adresă IP, fără spații, cel mult 253 de caractere).";
    public const string PortMessage = "Portul bazei de date trebuie să fie între 1 și 65535.";
    public const string UserMessage = "Introdu utilizatorul bazei de date (cel mult 80 de caractere).";
    public const string PasswordMessage = "Parola poate avea cel mult 200 de caractere.";
    public const string SslMessage = "Pentru un server aflat pe alt calculator, conexiunea trebuie să fie securizată: alege unul dintre primele trei moduri SSL din listă.";
    public const string SslUnknownMessage = "Modul SSL nu este recunoscut.";
    public const string AppPortMessage = "Portul aplicației trebuie să fie între 1024 și 65535.";
    public const string AdminPasswordMessage = "Parola contului de administrator nu este corectă.";
    public const string AdminPasswordRequiredMessage = "Introdu parola administratorului pentru confirmare.";
    public const string TooManyAttemptsMessage = "Prea multe parole greșite. Așteaptă un minut și încearcă din nou.";
    public const string DatabaseNotAllowedMessage = "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.";
    public const string LockHeldMessage = "O altă operație de backup, restaurare sau reconfigurare este deja în curs. Reîncearcă mai târziu.";

    public static readonly IReadOnlyList<string> SslModes = ["Required", "VerifyCA", "VerifyFull", "Preferred", "None"];

    public static bool IsLoopback(string? host)
    {
        var value = (host ?? "").Trim().Trim('[', ']');
        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(value, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool IsHostCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':';

    /// <summary>The first problem of the typed values, or null when they can be tested.</summary>
    public static string? Validate(DatabaseConnectionInput input)
    {
        var host = (input.Host ?? "").Trim();
        if (host.Length == 0 || host.Length > 253 || !host.All(IsHostCharacter)) return HostMessage;
        if (input.Port is < 1 or > 65535) return PortMessage;
        var user = (input.User ?? "").Trim();
        if (user.Length == 0 || user.Length > 80 || user.Any(char.IsControl)) return UserMessage;
        if ((input.Password?.Length ?? 0) > 200) return PasswordMessage;
        if (!SslModes.Contains(input.SslMode, StringComparer.OrdinalIgnoreCase)) return SslUnknownMessage;
        if (!IsLoopback(host) && input.SslMode is not ("Required" or "VerifyCA" or "VerifyFull")) return SslMessage;
        if (input.AppPort is { } appPort && (appPort < 1024 || appPort > 65535)) return AppPortMessage;
        return null;
    }

    /// <summary>A connection string for one test: no pool, short timeouts, the same safety options as the application's.</summary>
    public static MySqlConnectionStringBuilder TestBuilder(string host, int port, string database, string user, string? password, string sslMode, string charSet = "utf8mb4") => new()
    {
        Server = host.Trim(),
        Port = (uint)port,
        Database = database,
        UserID = user.Trim(),
        Password = password,
        SslMode = Enum.Parse<MySqlSslMode>(sslMode, true),
        CharacterSet = charSet,
        ConnectionTimeout = 10,
        DefaultCommandTimeout = 20,
        Pooling = false,
        AllowLoadLocalInfile = false
    };

    /// <summary>What a failed connection means, in words. Never contains the password.</summary>
    public static string Describe(Exception exception, string host, int port, string database) => exception switch
    {
        MySqlException { Number: 1045 } => "Utilizatorul sau parola sunt greșite.",
        MySqlException { Number: 1044 or 1142 } => "Contul nu are drepturi pe această bază de date.",
        MySqlException { Number: 1049 } => $"Baza „{database}” nu există pe server.",
        MySqlException { Number: 1130 } => "Serverul nu acceptă conexiuni de la acest calculator pentru acest cont.",
        MySqlException ex when ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            => "Conexiunea securizată (SSL) nu a putut fi stabilită. Verifică modul SSL și certificatul serverului.",
        // 1042: no server answered at that address (MySqlConnector reports a refused connection or an unknown host this way).
        MySqlException { Number: 1042 } or MySqlException { InnerException: SocketException } or SocketException => $"Serverul nu răspunde la {host}:{port}. Verifică hostul, portul și dacă serverul rulează.",
        MySqlException ex when ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => $"Serverul de la {host}:{port} nu a răspuns în 10 secunde.",
        TimeoutException => $"Serverul de la {host}:{port} nu a răspuns în 10 secunde.",
        MySqlException ex => $"Conexiunea a eșuat (cod {ex.Number}).",
        _ => $"Conexiunea a eșuat ({exception.GetType().Name})."
    };

    public static bool PasswordMatches(IConfiguration configuration, string? typed)
    {
        var expected = configuration["Authentication:Password"];
        return !string.IsNullOrEmpty(expected) && !string.IsNullOrEmpty(typed) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(typed)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}

/// <summary>Wrong administrator passwords typed to confirm a change: after five in a row the confirmation is refused for a minute.</summary>
internal static class AdminPasswordGate
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, (int Failures, DateTime BlockedUntilUtc)> State = new(StringComparer.Ordinal);

    public static bool IsBlocked(string user) => State.TryGetValue(user, out var state) && state.BlockedUntilUtc > DateTime.UtcNow;

    public static void Failed(string user) => State.AddOrUpdate(user, _ => (1, DateTime.MinValue), (_, old) =>
    {
        var failures = old.BlockedUntilUtc != DateTime.MinValue && old.BlockedUntilUtc <= DateTime.UtcNow ? 1 : old.Failures + 1;
        return failures >= MaxFailures ? (0, DateTime.UtcNow + Lockout) : (failures, DateTime.MinValue);
    });

    public static void Succeeded(string user) => State.TryRemove(user, out _);
}

/// <summary>The override file: what the administrator changed, on top of the private configuration.</summary>
public sealed record DatabaseOverrideData(string? Host, int? Port, string? User, string? SslMode, string? PasswordProtected, int? AppPort, bool? AppBindAll,
    IReadOnlyDictionary<string, AccountOverride>? Accounts = null);

/// <summary>What was saved for one predefined account (Services/DatabaseAccounts.cs): its name when it is not the catalog one, its password (protected), a new password
/// written before the server is changed (so a stop in the middle loses nothing) and when the password was last changed.</summary>
public sealed record AccountOverride(string? User = null, string? PasswordProtected = null, string? PendingPasswordProtected = null, DateTime? ChangedUtc = null);

public static class DatabaseOverrideFile
{
    public const string ProtectorPurpose = "BlazorStoc.DatabaseConnection.Password";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // Outside the repository on purpose: the private files in local-secrets are committed, this one must never be.
    public static string PathFor(IConfiguration configuration) =>
        configuration["Database:OverridePath"] is { Length: > 0 } configured
            ? configured
            : BlazorStoc.Startup.AppDataRoot.Combine(configuration, "database-connection.override.json");

    public static string KeysPath(IConfiguration configuration) => BlazorStoc.Startup.DataProtectionKeys.PathFor(configuration);

    public static byte[]? ReadRaw(string path)
    {
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static DatabaseOverrideData? Read(string path)
    {
        var raw = ReadRaw(path);
        if (raw is null) return null;
        try { return JsonSerializer.Deserialize<DatabaseOverrideData>(raw, JsonOptions); }
        catch (JsonException) { return null; }
    }

    public static void Write(string path, DatabaseOverrideData data) => WriteRaw(path, JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions));

    /// <summary>Atomic: a temporary file next to the target, then a replacement, so a crash never leaves half a file.</summary>
    public static void WriteRaw(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Puts the file back as it was (null = it did not exist).</summary>
    public static void Restore(string path, byte[]? content)
    {
        if (content is null) { if (File.Exists(path)) File.Delete(path); }
        else WriteRaw(path, content);
    }
}

/// <summary>Feeds the override file into the configuration. A password that cannot be decrypted (keys lost) disables the database part of the override.</summary>
public sealed class DatabaseOverrideConfigurationSource(string path, IDataProtector protector) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(path, protector);

    private sealed class Provider(string path, IDataProtector protector) : ConfigurationProvider
    {
        public override void Load()
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var file = DatabaseOverrideFile.Read(path);
            if (file is not null)
            {
                var password = (string?)null;
                var databaseUsable = true;
                if (file.PasswordProtected is not null)
                {
                    try { password = protector.Unprotect(file.PasswordProtected); }
                    catch (CryptographicException)
                    {
                        databaseUsable = false;
                        data["Database:OverrideProblem"] = "Parola salvată nu mai poate fi citită (cheile de protecție s-au schimbat). Introdu din nou parola.";
                    }
                }
                if (databaseUsable)
                {
                    if (file.Host is not null) data["Database:Host"] = file.Host;
                    if (file.Port is not null) data["Database:Port"] = file.Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (file.User is not null) data["Database:User"] = file.User;
                    if (file.SslMode is not null) data["Database:SslMode"] = file.SslMode;
                    if (password is not null) data["Database:Password"] = password;
                    data["Database:OverrideActive"] = "true";
                }
                foreach (var (key, account) in file.Accounts ?? new Dictionary<string, AccountOverride>())
                {
                    var definition = DatabaseAccounts.Find(key);
                    if (definition is null) continue;
                    try
                    {
                        var accountPassword = account.PasswordProtected is null ? null : protector.Unprotect(account.PasswordProtected);
                        var pending = account.PendingPasswordProtected is null ? null : protector.Unprotect(account.PendingPasswordProtected);
                        if (account.User is not null && DatabaseAccounts.IsSafeUserName(account.User)) data[definition.UserKey] = account.User;
                        if (accountPassword is not null) data[definition.PasswordKey] = accountPassword;
                        if (pending is not null) data[$"Database:Accounts:{definition.Key}:PendingPassword"] = pending;
                        if (account.ChangedUtc is not null) data[$"Database:Accounts:{definition.Key}:ChangedUtc"] = account.ChangedUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (CryptographicException)
                    {
                        data["Database:OverrideProblem"] = "Parola salvată nu mai poate fi citită (cheile de protecție s-au schimbat). Introdu din nou parola.";
                    }
                }
                if (file.AppPort is not null) data["Hosting:AppPort"] = file.AppPort.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (file.AppBindAll is not null) data["Hosting:AppBindAll"] = file.AppBindAll.Value ? "true" : "false";
            }
            Data = data;
        }
    }
}

public static class DatabaseOverrideConfiguration
{
    /// <summary>Adds the override file as the last configuration source (it wins over the private file and the environment).</summary>
    public static IConfigurationBuilder AddDatabaseOverride(this IConfigurationBuilder builder, string path, string keysPath)
    {
        Directory.CreateDirectory(keysPath);
        var provider = DataProtectionProvider.Create(new DirectoryInfo(keysPath), options => options.SetApplicationName("BlazorStoc"));
        return builder.Add(new DatabaseOverrideConfigurationSource(path, provider.CreateProtector(DatabaseOverrideFile.ProtectorPurpose)));
    }
}

/// <summary>The address the application listens on. Decided once, before the host is built.</summary>
public sealed record AppHostingInfo(int? ConfiguredPort, bool BindAll, string? AppliedUrls, string? Note)
{
    public static AppHostingInfo None { get; } = new(null, false, null, null);
}

public static class AppHosting
{
    /// <summary>
    /// The urls to listen on from the override file, or null to keep what the host already has. Addresses imposed from outside (--urls,
    /// ASPNETCORE_URLS, launchSettings) win, so a start script still decides; the note says so, and the page shows it.
    /// </summary>
    public static AppHostingInfo Resolve(IConfiguration configuration, Func<int, bool> canBind)
    {
        var port = configuration.GetValue<int?>("Hosting:AppPort");
        var bindAll = configuration.GetValue("Hosting:AppBindAll", false);
        // The port a start script proposes (Hosting:DefaultPort, set by tools\supervisor.ps1 and tools\start-preview.ps1 instead of imposing the address): used when no port was saved from
        // the page, or when the saved one is taken. A script that does impose the address (ASPNETCORE_URLS, --urls) still wins, as before.
        var defaultPort = configuration.GetValue<int?>("Hosting:DefaultPort");
        if (!string.IsNullOrWhiteSpace(configuration["urls"]))
            return port is null ? new(null, bindAll, null, null)
                : new(port, bindAll, null, "Portul salvat nu se aplică: adresele sunt impuse la pornire (variabila ASPNETCORE_URLS, --urls sau scriptul de pornire).");
        var fallback = defaultPort is { } proposed ? $"http://127.0.0.1:{proposed}" : null;
        if (port is null) return new(null, bindAll, fallback, null);
        if (!canBind(port.Value))
            return new(port, bindAll, fallback, fallback is null ? $"Portul salvat ({port}) este ocupat; aplicația folosește adresa implicită."
                : $"Portul salvat ({port}) este ocupat; aplicația folosește portul {defaultPort}.");
        return new(port, bindAll, $"http://{(bindAll ? "0.0.0.0" : "127.0.0.1")}:{port}", null);
    }

    public static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }
}

public interface IDatabaseSettingsService
{
    Task<DatabaseConnectionSettings> GetAsync(CancellationToken cancellationToken = default);
    Task<DatabaseTestResult> TestAsync(DatabaseConnectionInput input, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> SaveAsync(DatabaseConnectionInput input, string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> ResetToBaseAsync(string administratorPassword, CancellationToken cancellationToken = default);
    /// <summary>After the server answers on a new port (Services/DatabaseServer.cs): the connection of the application follows it. The confirmation and the journal entry are those of the caller.</summary>
    Task ApplyServerPortAsync(int port, CancellationToken cancellationToken = default);
}

public sealed class DatabaseSettingsService(IConfiguration configuration, IDataProtectionProvider protection, IAccessControl access, IAuditTrail audit,
    IOperationLockService locks, AppHostingInfo hosting, ILogger<DatabaseSettingsService> logger, Func<int, bool>? canBind = null, IUserAuthenticator? authenticator = null) : IDatabaseSettingsService
{
    // The tables whose absence means the connection points at something that is not this application's database.
    private static readonly string[] KeyTables = ["products", "stock_movements", "web_users", "audit_events"];
    private readonly IDataProtector protector = protection.CreateProtector(DatabaseOverrideFile.ProtectorPurpose);
    private readonly Func<int, bool> canBindPort = canBind ?? AppHosting.CanBind;
    private string OverridePath => DatabaseOverrideFile.PathFor(configuration);

    public async Task<DatabaseConnectionSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return Current();
    }

    private DatabaseConnectionSettings Current() => new(
        configuration["Database:Host"] ?? "127.0.0.1", configuration.GetValue("Database:Port", 3307), configuration["Database:Name"] ?? "BlazorStoc",
        configuration["Database:User"] ?? DatabaseAccounts.Find(DatabaseAccounts.ApplicationKey)!.User, configuration["Database:SslMode"] ?? "Required",
        !string.IsNullOrEmpty(configuration["Database:Password"]),
        configuration.GetValue<int?>("Hosting:AppPort"), configuration.GetValue("Hosting:AppBindAll", false),
        configuration.GetValue("Database:OverrideActive", false), configuration["Database:OverrideProblem"], OverridePath);

    public async Task<DatabaseTestResult> TestAsync(DatabaseConnectionInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (DatabaseConnectionRules.Validate(input) is { } problem) throw new DatabaseSettingsException(problem);
        var result = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
        var target = $"{input.Host.Trim()}:{input.Port}";
        await RecordAsync(AuditActions.TestDatabaseConnection, "Test conexiune bază de date",
            $"{target}, utilizator {input.User.Trim()}: {(result.Ok ? "reușit" : "eșuat")}", cancellationToken).ConfigureAwait(false);
        return result;
    }

    // The checks, in order. An error in the first stops the rest (nothing else can be asked); the others are independent.
    private async Task<DatabaseTestResult> ProbeAsync(DatabaseConnectionInput input, CancellationToken cancellationToken)
    {
        var steps = new List<DatabaseTestStep>();
        var name = configuration["Database:Name"] ?? "BlazorStoc";
        var password = input.Password is { Length: > 0 } typed ? typed : configuration["Database:Password"];
        var builder = DatabaseConnectionRules.TestBuilder(input.Host, input.Port, name, input.User, password, input.SslMode, configuration["Database:CharSet"] ?? "utf8mb4");
        await using var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            steps.Add(new("Conectare", DatabaseStepLevel.Ok, $"{input.Host.Trim()}:{input.Port}, utilizator {input.User.Trim()}"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            steps.Add(new("Conectare", DatabaseStepLevel.Error, DatabaseConnectionRules.Describe(exception, input.Host.Trim(), input.Port, name)));
            return new(steps);
        }
        try
        {
            await using (var command = new MySqlCommand("SELECT VERSION()", connection))
                steps.Add(new("Server", DatabaseStepLevel.Ok, "MariaDB/MySQL " + Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))));
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = new MySqlCommand("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) tables.Add(reader.GetString(0));
            var missing = KeyTables.Where(table => !tables.Contains(table)).ToArray();
            steps.Add(missing.Length == 0
                ? new("Schema aplicației", DatabaseStepLevel.Ok, $"{tables.Count} tabele în baza {name}")
                : new("Schema aplicației", DatabaseStepLevel.Error, $"În baza {name} lipsesc tabele ale aplicației ({string.Join(", ", missing)}). Hostul sau portul indică probabil alt server."));
            var privileges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = new MySqlCommand("""
                SELECT PRIVILEGE_TYPE FROM information_schema.SCHEMA_PRIVILEGES WHERE REPLACE(TABLE_SCHEMA, '\\', '') = @db
                UNION SELECT PRIVILEGE_TYPE FROM information_schema.USER_PRIVILEGES
                """, connection))
            {
                command.Parameters.AddWithValue("@db", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) privileges.Add(reader.GetString(0));
            }
            var lacking = new[] { "SELECT", "INSERT", "UPDATE", "DELETE" }.Where(privilege => !privileges.Contains(privilege)).ToArray();
            steps.Add(lacking.Length == 0
                ? new("Drepturile contului", DatabaseStepLevel.Ok, "SELECT, INSERT, UPDATE, DELETE")
                : new("Drepturile contului", DatabaseStepLevel.Warning, $"Nu am putut confirma drepturile {string.Join(", ", lacking)} pe baza {name}; dacă sunt acordate pe tabele, e în regulă."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            steps.Add(new("Interogare", DatabaseStepLevel.Error, DatabaseConnectionRules.Describe(exception, input.Host.Trim(), input.Port, name)));
        }
        return new(steps);
    }

    public async Task<DatabaseSaveResult> SaveAsync(DatabaseConnectionInput input, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await ConfirmAdministratorAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (DatabaseConnectionRules.Validate(input) is { } problem) throw new DatabaseSettingsException(problem);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new DatabaseSettingsException(DatabaseConnectionRules.DatabaseNotAllowedMessage);

        var current = Current();
        var host = input.Host.Trim();
        var user = input.User.Trim();
        var passwordTyped = !string.IsNullOrEmpty(input.Password);
        var databaseChanged = passwordTyped || !string.Equals(host, current.Host, StringComparison.OrdinalIgnoreCase) || input.Port != current.Port ||
            !string.Equals(user, current.User, StringComparison.Ordinal) || !string.Equals(input.SslMode, current.SslMode, StringComparison.OrdinalIgnoreCase);
        var hostingChanged = input.AppPort != current.AppPort || (input.AppPort is not null && input.AppBindAll != current.AppBindAll);
        if (!databaseChanged && !hostingChanged) return new(true, "Nu există modificări de salvat.");

        if (hostingChanged && input.AppPort is { } appPort && appPort != hosting.ConfiguredPort && !canBindPort(appPort))
            throw new DatabaseSettingsException($"Portul {appPort} este ocupat pe acest calculator. Alege alt port.");

        if (databaseChanged)
        {
            var test = await ProbeAsync(input, cancellationToken).ConfigureAwait(false);
            if (!test.Ok)
                return new(false, "Conexiunea nu a putut fi verificată, deci nimic nu a fost salvat: " + test.Steps.First(step => step.Level == DatabaseStepLevel.Error).Detail);
        }

        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var path = OverridePath;
        var previousFile = DatabaseOverrideFile.ReadRaw(path);
        var previous = DatabaseOverrideFile.Read(path);
        var next = new DatabaseOverrideData(
            databaseChanged ? host : previous?.Host, databaseChanged ? input.Port : previous?.Port, databaseChanged ? user : previous?.User,
            databaseChanged ? input.SslMode : previous?.SslMode,
            passwordTyped ? protector.Protect(input.Password!) : previous?.PasswordProtected,
            input.AppPort, input.AppPort is null ? null : input.AppBindAll,
            // What the page of the accounts saved stays; a password or a name typed here for the application account replaces what was saved for it there.
            databaseChanged ? WithoutApplicationAccount(previous?.Accounts, passwordTyped, !string.Equals(user, current.User, StringComparison.Ordinal)) : previous?.Accounts);

        IOperationLockHandle? handle = null;
        IDisposable? owner = null;
        if (databaseChanged)
        {
            // Every other session is sent to the waiting page while the connection is changed and checked again.
            handle = await locks.TryAcquireAsync("Reconfigurare conexiune baza de date", actor, AccessRoles.Administrator, cancellationToken).ConfigureAwait(false);
            if (handle is null) return new(false, DatabaseConnectionRules.LockHeldMessage);
            owner = MaintenanceGate.EnterOwnerScope();
        }
        try
        {
            DatabaseOverrideFile.Write(path, next);
            ReloadConfiguration();
            if (databaseChanged)
            {
                var verification = await VerifyCurrentAsync(cancellationToken).ConfigureAwait(false);
                if (verification is not null)
                {
                    DatabaseOverrideFile.Restore(path, previousFile);
                    ReloadConfiguration();
                    var restored = await VerifyCurrentAsync(cancellationToken).ConfigureAwait(false);
                    owner?.Dispose(); owner = null;
                    if (handle is not null) { await handle.DisposeAsync().ConfigureAwait(false); handle = null; }
                    await RecordAsync(AuditActions.RevertDatabaseConnection, "Revenire automată la configurația anterioară",
                        $"Conexiunea nouă ({host}:{input.Port}, utilizator {user}) nu a trecut verificarea după aplicare: {verification}", cancellationToken).ConfigureAwait(false);
                    return new(false, "Verificarea după aplicare a eșuat (" + verification + "). S-a revenit automat la configurația anterioară" +
                        (restored is null ? "." : $", dar nici aceasta nu răspunde acum ({restored})."), RolledBack: true);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The file could not be written or the configuration reloaded: put everything back and say so.
            logger.LogError("Database connection settings could not be applied ({ErrorType}).", exception.GetType().Name);
            try { DatabaseOverrideFile.Restore(path, previousFile); ReloadConfiguration(); }
            catch (Exception restoreFailure) { logger.LogError("Database connection settings could not be restored ({ErrorType}).", restoreFailure.GetType().Name); }
            throw new DatabaseSettingsException("Setările nu au putut fi salvate. Verifică drepturile asupra fișierului de configurare; configurația anterioară a rămas în vigoare.");
        }
        finally
        {
            owner?.Dispose();
            if (handle is not null) await handle.DisposeAsync().ConfigureAwait(false);
        }

        var after = Current();
        if (databaseChanged)
        {
            await RecordAsync(AuditActions.SetDatabaseConnection, "Conexiune bază de date", AuditDetails.Changes(
                new AuditChange("Host", current.Host, after.Host), new AuditChange("Port bază de date", current.Port.ToString(), after.Port.ToString()),
                new AuditChange("Utilizator", current.User, after.User), new AuditChange("Mod SSL", current.SslMode, after.SslMode),
                new AuditChange("Parola", "setată", passwordTyped ? "schimbată" : "setată")), cancellationToken).ConfigureAwait(false);
        }
        if (hostingChanged)
        {
            await RecordAsync(AuditActions.SetAppPort, "Port aplicație", AuditDetails.Changes(
                new AuditChange("Port aplicație", current.AppPort?.ToString() ?? "implicit", after.AppPort?.ToString() ?? "implicit"),
                new AuditChange("Interfață", Interface(current.AppBindAll), Interface(after.AppBindAll))), cancellationToken).ConfigureAwait(false);
        }
        var message = databaseChanged ? "Conexiunea a fost salvată și aplicată; verificarea după aplicare a reușit." : "Setările au fost salvate.";
        if (hostingChanged) message += " Portul aplicației se aplică la următoarea pornire a aplicației.";
        return new(true, message, RestartNeeded: hostingChanged);
    }

    public async Task ApplyServerPortAsync(int port, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var path = OverridePath;
        var previous = DatabaseOverrideFile.Read(path) ?? new DatabaseOverrideData(null, null, null, null, null, null, null);
        DatabaseOverrideFile.Write(path, previous with { Port = port });
        ReloadConfiguration();
    }

    public async Task<DatabaseSaveResult> ResetToBaseAsync(string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await ConfirmAdministratorAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var path = OverridePath;
        var previousFile = DatabaseOverrideFile.ReadRaw(path);
        if (previousFile is null) return new(true, "Nu există nicio suprascriere: se folosește deja configurația de bază.");
        var current = Current();
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var handle = await locks.TryAcquireAsync("Reconfigurare conexiune baza de date", actor, AccessRoles.Administrator, cancellationToken).ConfigureAwait(false);
        if (handle is null) return new(false, DatabaseConnectionRules.LockHeldMessage);
        string? failure;
        using (MaintenanceGate.EnterOwnerScope())
        {
            try
            {
                // The passwords saved for the accounts are not connection settings: they stay (the server has them), only the rest goes back to the base file.
                var kept = DatabaseOverrideFile.Read(path)?.Accounts;
                if (kept is { Count: > 0 }) DatabaseOverrideFile.Write(path, new DatabaseOverrideData(null, null, null, null, null, null, null, kept));
                else DatabaseOverrideFile.Restore(path, null);
                ReloadConfiguration();
                failure = await VerifyCurrentAsync(cancellationToken).ConfigureAwait(false);
                if (failure is not null) { DatabaseOverrideFile.Restore(path, previousFile); ReloadConfiguration(); }
            }
            finally { await handle.DisposeAsync().ConfigureAwait(false); }
        }
        if (failure is not null)
            return new(false, $"Configurația de bază nu răspunde ({failure}); s-a păstrat suprascrierea.", RolledBack: true);
        var after = Current();
        await RecordAsync(AuditActions.ResetDatabaseConnection, "Conexiune bază de date", AuditDetails.Changes(
            new AuditChange("Host", current.Host, after.Host), new AuditChange("Port bază de date", current.Port.ToString(), after.Port.ToString()),
            new AuditChange("Utilizator", current.User, after.User), new AuditChange("Mod SSL", current.SslMode, after.SslMode),
            new AuditChange("Port aplicație", current.AppPort?.ToString() ?? "implicit", after.AppPort?.ToString() ?? "implicit")), cancellationToken).ConfigureAwait(false);
        return new(true, "S-a revenit la configurația din fișierul de bază. Portul aplicației se aplică la următoarea pornire.", RestartNeeded: current.AppPort != after.AppPort);
    }

    private static IReadOnlyDictionary<string, AccountOverride>? WithoutApplicationAccount(IReadOnlyDictionary<string, AccountOverride>? accounts, bool passwordTyped, bool userChanged)
    {
        if (accounts is null || (!passwordTyped && !userChanged) || !accounts.TryGetValue(DatabaseAccounts.ApplicationKey, out var application)) return accounts;
        var copy = new Dictionary<string, AccountOverride>(accounts, StringComparer.OrdinalIgnoreCase);
        copy[DatabaseAccounts.ApplicationKey] = application with { User = userChanged ? null : application.User, PasswordProtected = passwordTyped ? null : application.PasswordProtected, PendingPasswordProtected = null };
        return copy;
    }

    private static string Interface(bool all) => all ? "toate interfețele" : "doar acest calculator";

    private Task ConfirmAdministratorAsync(string typed, CancellationToken cancellationToken) =>
        new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(typed, cancellationToken);

    // The configuration is re-read (the new file) and the pools of the old connection string are closed, so the next query uses the new one.
    private void ReloadConfiguration()
    {
        (configuration as IConfigurationRoot)?.Reload();
        MySqlConnection.ClearAllPools();
    }

    // null = the connection now in use works; otherwise why it does not. Runs under the owner scope of the lock, because it goes through the same gate as every query.
    private async Task<string?> VerifyCurrentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = DatabaseConnections.Create(configuration);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('products','stock_movements','web_users','audit_events')", connection);
            var found = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            return found == KeyTables.Length ? null : "baza nu conține tabelele aplicației";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return DatabaseConnectionRules.Describe(exception, configuration["Database:Host"] ?? "", configuration.GetValue("Database:Port", 3307), configuration["Database:Name"] ?? "BlazorStoc");
        }
    }

    // The journal is in the database being reconfigured: if it cannot be written, the entry goes to the application log instead of being lost.
    private async Task RecordAsync(string action, string target, string details, CancellationToken cancellationToken)
    {
        try { await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Database, action, "", target, details, "", cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}): {Details}", action, exception.GetType().Name, details);
        }
    }
}
