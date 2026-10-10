using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Services;

// Backup copy to a NAS: the backup packages are made locally (MariaDatabaseBackupService) and every new package is copied to a network share
// (\\host\share[\folder]) with an account kept in Settings. The password is stored encrypted (ASP.NET Data Protection, keys in keys/) and is never shown again,
// logged or audited. A daily backup can be scheduled (BackupScheduler). The NAS may refuse to delete files: nothing here deletes on the share.

public sealed record NasBackupSettings(string Path, string UserName, bool HasPassword, bool CopyEnabled, DateTime? LastAttemptUtc, bool? LastOk, string LastMessage)
{
    public static NasBackupSettings Empty { get; } = new("", "", false, false, null, null, "");
}

/// <summary>What the administrator typed. A null <see cref="Password"/> keeps the stored one.</summary>
public sealed record NasBackupInput(string Path, string UserName, string? Password, bool CopyEnabled);

public sealed record NasShareFile(string FileName, long SizeBytes, DateTime ModifiedUtc);

/// <summary>What the share holds (the packages and their checksum files), or why it could not be read.</summary>
public sealed record NasShareListing(bool Ok, string Message, IReadOnlyList<NasShareFile> Files);

public sealed record NasBackupCredentials(string Path, string UserName, string Password);

public sealed record NasCopyResult(bool Ok, int Copied, int AlreadyThere, int Failed, string Message);

public sealed class NasBackupException(string message) : Exception(message);

public static class NasBackupRules
{
    public const string PathMessage = "Calea trebuie să fie o cale de rețea de forma \\\\server\\partajare sau \\\\server\\partajare\\folder.";
    public const string UserMessage = "Introdu contul cu care se face conectarea la partajare (cel mult 100 de caractere).";
    public const string MaxAgeMessage = "Vechimea maximă a unui backup trebuie să fie între 1 și 30 de zile.";
    public const string TimeMessage = "Ora backup-ului programat trebuie să fie de forma HH:mm (de exemplu 02:00).";
    public const string NotConfiguredMessage = "Copierea pe NAS nu este configurată (cale, cont și parolă).";

    public static string NormalizePath(string? path) => (path ?? "").Trim().TrimEnd('\\', '/');

    /// <summary>A UNC path with a server and a share, without relative parts or characters a file name cannot have.</summary>
    public static string? PathError(string? path)
    {
        var value = NormalizePath(path);
        if (!value.StartsWith(@"\\", StringComparison.Ordinal) || value.Length > 500) return PathMessage;
        var parts = value[2..].Split('\\');
        if (parts.Length < 2 || parts.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(['/', ':', '*', '?', '"', '<', '>', '|']) >= 0)) return PathMessage;
        return null;
    }

    /// <summary>The root of the share (\\server\share), the unit a network connection is made to.</summary>
    public static string ShareRoot(string path)
    {
        var parts = NormalizePath(path)[2..].Split('\\');
        return @"\\" + parts[0] + "\\" + parts[1];
    }

    public static string? UserError(string? userName) => string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > 100 ? UserMessage : null;

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);

    public static string? Validate(NasBackupInput input)
    {
        if (input.CopyEnabled && (PathError(input.Path) ?? UserError(input.UserName)) is { } error) return error;
        return null;
    }


    /// <summary>What a failed access to the share means, in words (from the Windows error code of the exception).</summary>
    public static string IoMessage(Exception exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 53 or 67 or 1203 or 1222 or 1231
            ? ConnectionMessage(code)
            : code is 1326 or 86 or 1331 or 1909 || exception is UnauthorizedAccessException
                ? "Contul sau parola sunt greșite, sau contul nu are drept de scriere în folderul ales."
                : "Folderul de pe partajare nu poate fi folosit (verifică calea și drepturile contului).";
    }
    /// <summary>The network error codes a user can act on, in words (never the account or the password).</summary>
    public static string ConnectionMessage(int code) => code switch
    {
        53 or 67 => "Partajarea nu a fost găsită. Verifică adresa serverului și numele partajării.",
        1326 or 86 => "Contul sau parola sunt greșite.",
        5 or 1331 or 1909 => "Contul nu are acces la partajare.",
        1219 => "Există deja o conexiune la acest server cu alt cont. Închide-o (net use) și reîncearcă.",
        1203 or 1222 or 1231 => "Serverul nu poate fi contactat. Verifică rețeaua și adresa.",
        _ => $"Conectarea la partajare a eșuat (cod {code})."
    };
}

public interface INasBackupSettingsRepository
{
    /// <summary>Administrator only.</summary>
    Task<NasBackupSettings> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator only; journaled (the password only as "schimbata", never its value).</summary>
    Task SaveAsync(NasBackupInput input, CancellationToken cancellationToken = default);
}

public interface INasBackupCopier
{
    /// <summary>Connects with the typed values (an empty password uses the stored one), writes and renames a small file and reports it. Administrator only.</summary>
    Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default);
    /// <summary>Copies every local package (and its checksum file) that is not yet on the share. Administrator only, or the system (backup just made).</summary>
    Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default);
    /// <summary>The copy that follows a backup: only the package just made (onlyPackage), or, when null, every package missing from the share (the scheduled backup also catches up what an earlier copy missed).</summary>
    Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default);
    /// <summary>The packages on the share (administrator only, or the system for the retention). A share that cannot be read gives Ok=false with the cause.</summary>
    Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default);
    /// <summary>Copies a package that is only on the share into the local backup folder (checked by size and checksum, never over an existing file). Administrator only.</summary>
    Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default);
    /// <summary>Remembers why a backup failed (shown in the backup notification). Never throws.</summary>
    Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>The settings row (one row, id 1). Without an access control (the system: scheduler, copier) nothing is checked or journaled here.</summary>
public sealed class MariaNasBackupStore(IConfiguration configuration, IDataProtectionProvider protection, IAccessControl? access = null, IAuditTrail? audit = null)
    : INasBackupSettingsRepository
{
    private const string Purpose = "BlazorStoc.NasBackup.Password";
    private readonly IDataProtector protector = protection.CreateProtector(Purpose);

    public async Task<NasBackupSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
    }

    internal async Task<(NasBackupSettings Settings, string? Password)> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT unc_path,username,password_protected,copy_enabled,last_attempt_utc,last_ok,last_message FROM backup_nas_settings WHERE id=1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (NasBackupSettings.Empty, null);
        var protectedPassword = reader.IsDBNull(2) ? null : reader.GetString(2);
        string? password = null;
        if (protectedPassword is not null)
        {
            // Keys lost or changed (another machine, a restored database): the password has to be typed again.
            try { password = protector.Unprotect(protectedPassword); } catch (CryptographicException) { password = null; }
        }
        var settings = new NasBackupSettings(reader.GetString(0), reader.GetString(1), password is not null, reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : MariaTimeText.Parse(reader.GetString(4)), reader.IsDBNull(5) ? null : reader.GetBoolean(5), reader.IsDBNull(6) ? "" : reader.GetString(6));
        return (settings, password);
    }

    internal async Task<NasBackupCredentials?> CredentialsAsync(CancellationToken cancellationToken)
    {
        var (settings, password) = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return password is null || NasBackupRules.PathError(settings.Path) is not null || string.IsNullOrWhiteSpace(settings.UserName)
            ? null : new NasBackupCredentials(NasBackupRules.NormalizePath(settings.Path), settings.UserName.Trim(), password);
    }

    public async Task SaveAsync(NasBackupInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        if (NasBackupRules.Validate(input) is { } error) throw new NasBackupException(error);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new NasBackupException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var old = (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var path = NasBackupRules.NormalizePath(input.Path);
        var passwordChanged = !string.IsNullOrEmpty(input.Password);
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("""
                INSERT INTO backup_nas_settings (id,unc_path,username,password_protected,copy_enabled,updated_by,updated_utc)
                VALUES (1,@path,@user,@pass,@copy,@by,@now)
                ON DUPLICATE KEY UPDATE unc_path=@path,username=@user,password_protected=IF(@changed=1,@pass,password_protected),
                    copy_enabled=@copy,updated_by=@by,updated_utc=@now
                """, connection);
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@user", input.UserName.Trim());
            command.Parameters.AddWithValue("@pass", passwordChanged ? protector.Protect(input.Password!) : DBNull.Value);
            command.Parameters.AddWithValue("@changed", passwordChanged ? 1 : 0);
            command.Parameters.AddWithValue("@copy", input.CopyEnabled);
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var changes = new[]
        {
            new AuditChange("Cale NAS", Show(old.Path), Show(path)),
            new AuditChange("Cont NAS", Show(old.UserName), Show(input.UserName.Trim())),
            new AuditChange("Parola NAS", old.HasPassword ? "setată" : "—", passwordChanged ? "schimbată" : old.HasPassword ? "setată" : "—"),
            new AuditChange("Copiere pe NAS", old.CopyEnabled ? "activă" : "inactivă", input.CopyEnabled ? "activă" : "inactivă")
        };
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.SetNasBackup, "", "Configurare backup NAS",
            AuditDetails.Changes(changes), "", cancellationToken).ConfigureAwait(false);
    }

    internal async Task RecordResultAsync(bool ok, string message, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE backup_nas_settings SET last_attempt_utc=@now,last_ok=@ok,last_message=@message WHERE id=1", connection);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@ok", ok);
        command.Parameters.AddWithValue("@message", message.Length > 480 ? message[..480] : message);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Show(string value) => value.Length == 0 ? "—" : value;
}

/// <summary>
/// Network access to the share with the account typed in Settings. On Windows a separate logon with "new credentials" (the same as runas /netonly) is made, and the
/// work on the share runs impersonated with it: the account is used only for the connection to the network, never as a local user, and it does not collide with
/// another connection of the same Windows session to the same server (error 1219 of a mapped connection). Elsewhere the path is a mounted folder.
/// </summary>
internal sealed class NetworkShareConnection : IDisposable
{
    private const int LogonNewCredentials = 9, ProviderWinNT50 = 3;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Microsoft.Win32.SafeHandles.SafeAccessTokenHandle? token;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string user, string? domain, string password, int logonType, int provider, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);

    private NetworkShareConnection(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle? token) => this.token = token;

    public static async Task<NetworkShareConnection> OpenAsync(string path, string userName, string password, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!OperatingSystem.IsWindows()) return new NetworkShareConnection(null);
            // "DOMAIN\user" is kept as typed; otherwise the NAS name is the domain, as in net use \nas\share /user:nas\user.
            var slash = userName.IndexOf('\\');
            var domain = slash > 0 ? userName[..slash] : NasBackupRules.ShareRoot(path)[2..].Split('\\')[0];
            var user = slash > 0 ? userName[(slash + 1)..] : userName;
            if (!LogonUser(user, domain, password, LogonNewCredentials, ProviderWinNT50, out var handle))
                throw new NasBackupException(NasBackupRules.ConnectionMessage(Marshal.GetLastWin32Error()));
            return new NetworkShareConnection(handle);
        }
        catch { Gate.Release(); throw; }
    }

    /// <summary>Runs the work on the share with the account's network credentials.</summary>
    public Task RunAsync(Func<Task> work) =>
        token is null || !OperatingSystem.IsWindows() ? work() : System.Security.Principal.WindowsIdentity.RunImpersonatedAsync(token, work);

    public void Dispose()
    {
        try { token?.Dispose(); }
        finally { Gate.Release(); }
    }
}

/// <summary>Copies the local packages to the share. Nothing on the share is ever deleted (the NAS may forbid it); a copy is written under a temporary name, checked by size and checksum, then renamed.</summary>
public sealed class NasBackupCopier(IConfiguration configuration, IDataProtectionProvider protection, IAccessControl? access, IAuditTrail? audit, ILogger<NasBackupCopier> logger)
    : INasBackupCopier
{
    private readonly MariaNasBackupStore store = new(configuration, protection);

    public async Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        if ((NasBackupRules.PathError(input.Path) ?? NasBackupRules.UserError(input.UserName)) is { } error) return new(false, 0, 0, 0, error);
        var password = string.IsNullOrEmpty(input.Password) ? (await store.ReadAsync(cancellationToken).ConfigureAwait(false)).Password : input.Password;
        if (string.IsNullOrEmpty(password)) return new(false, 0, 0, 0, "Introdu parola contului (nu este salvată nici una).");
        var path = NasBackupRules.NormalizePath(input.Path);
        NasCopyResult result = default!;
        try
        {
            using var connection = await NetworkShareConnection.OpenAsync(path, input.UserName.Trim(), password, cancellationToken).ConfigureAwait(false);
            await connection.RunAsync(async () =>
            {
            Directory.CreateDirectory(path);
            var temporary = Path.Combine(path, $".blazorstoc-test-{Guid.NewGuid():N}.tmp");
            var renamed = temporary + ".ok";
            await File.WriteAllTextAsync(temporary, "BlazorStoc", cancellationToken).ConfigureAwait(false);
            File.Move(temporary, renamed);
            // A NAS that forbids deleting keeps these 2 KB test files; it is said so, never hidden.
            var deleted = true;
            try { File.Delete(renamed); } catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { deleted = false; }
            result = new(true, 0, 0, 0, deleted ? "Conexiunea funcționează: se poate crea, redenumi și șterge."
                : "Conexiunea funcționează: se poate crea și redenumi. Ștergerea nu este permisă pe partajare (nu este necesară); fișierul de test rămas poate fi șters din NAS.");
            });
        }
        catch (NasBackupException exception) { result = new(false, 0, 0, 0, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("NAS connection test failed ({ErrorType}).", exception.GetType().Name);
            result = new(false, 0, 0, 0, NasBackupRules.IoMessage(exception));
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.TestNasBackup, "", "Testare conexiune NAS",
            AuditDetails.Identification(("Cale", NasBackupRules.NormalizePath(input.Path)), ("Cont", input.UserName.Trim()), ("Rezultat", result.Ok ? "reușit" : result.Message)), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        return await CopyCoreAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default) => CopyCoreAsync(onlyPackage, cancellationToken);

    public async Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        var (settings, _) = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.CopyEnabled) return new(false, "Copierea pe NAS este oprită.", []);
        var credentials = await store.CredentialsAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null) return new(false, NasBackupRules.NotConfiguredMessage, []);
        var files = new List<NasShareFile>();
        try
        {
            using var connection = await NetworkShareConnection.OpenAsync(credentials.Path, credentials.UserName, credentials.Password, cancellationToken).ConfigureAwait(false);
            await connection.RunAsync(() =>
            {
                if (Directory.Exists(credentials.Path))
                    foreach (var file in Directory.EnumerateFiles(credentials.Path, "*.zip").Where(item => item.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        var info = new FileInfo(file);
                        files.Add(new NasShareFile(info.Name, info.Length, info.LastWriteTimeUtc));
                    }
                return Task.CompletedTask;
            });
        }
        catch (NasBackupException exception) { return new(false, exception.Message, []); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("NAS listing failed ({ErrorType}).", exception.GetType().Name);
            return new(false, NasBackupRules.IoMessage(exception), []);
        }
        return new(true, "", files);
    }

    public async Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("."))
            return new(false, 0, 0, 1, "Pachet invalid.");
        var credentials = await store.CredentialsAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null) return new(false, 0, 0, 1, NasBackupRules.NotConfiguredMessage);
        var local = MariaAssetPaths.DatabaseBackups(configuration);
        Directory.CreateDirectory(local);
        var target = Path.Combine(local, fileName);
        if (File.Exists(target)) return new(true, 0, 1, 0, "Pachetul este deja pe server.");
        NasCopyResult result = default!;
        var temporary = Path.Combine(local, $".tmp-fetch-{Guid.NewGuid():N}.zip");
        try
        {
            using var connection = await NetworkShareConnection.OpenAsync(credentials.Path, credentials.UserName, credentials.Password, cancellationToken).ConfigureAwait(false);
            await connection.RunAsync(async () =>
            {
                var source = Path.Combine(credentials.Path, fileName);
                if (!File.Exists(source)) { result = new(false, 0, 0, 1, "Pachetul nu mai este pe NAS."); return; }
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                var hash = await HashAsync(temporary, cancellationToken).ConfigureAwait(false);
                // The checksum file written with the package (when the share has it) must match, and the package must be a readable one.
                if (File.Exists(source + ".sha256") && !string.Equals((await File.ReadAllTextAsync(source + ".sha256", cancellationToken).ConfigureAwait(false)).Trim(), hash, StringComparison.OrdinalIgnoreCase))
                { result = new(false, 0, 0, 1, "Pachetul de pe NAS este deteriorat (suma de control nu coincide)."); return; }
                if (await BackupPackageStore.TryReadManifestAsync(temporary, cancellationToken).ConfigureAwait(false) is null)
                { result = new(false, 0, 0, 1, "Fișierul de pe NAS nu este un pachet de backup valid."); return; }
                File.Move(temporary, target, overwrite: false);
                await File.WriteAllTextAsync(target + ".sha256", hash.ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
                result = new(true, 1, 0, 0, "Pachetul a fost adus de pe NAS pe server.");
            });
        }
        catch (NasBackupException exception) { result = new(false, 0, 0, 1, exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Fetching a package from the NAS failed ({ErrorType}).", exception.GetType().Name);
            result = new(false, 0, 0, 1, NasBackupRules.IoMessage(exception));
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.FetchBackupFromNas, fileName, $"Pachet: {fileName}",
            AuditDetails.Identification(("Pachet", fileName), ("Rezultat", result.Message)), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default)
    {
        try { await new MariaBackupSettingsStore(configuration).RecordBackupErrorAsync(message, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is MySqlException) { logger.LogWarning("Backup failure could not be saved ({ErrorType}).", exception.GetType().Name); }
    }

    private async Task<NasCopyResult> CopyCoreAsync(string? onlyPackage, CancellationToken cancellationToken)
    {
        var (settings, _) = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.CopyEnabled) return new(true, 0, 0, 0, "Copierea pe NAS este oprită.");
        var credentials = await store.CredentialsAsync(cancellationToken).ConfigureAwait(false);
        if (credentials is null) return new(false, 0, 0, 0, NasBackupRules.NotConfiguredMessage);
        var local = MariaAssetPaths.DatabaseBackups(configuration);
        int copied = 0, already = 0, failed = 0;
        var names = new List<string>();
        string message = "";
        try
        {
            using var connection = await NetworkShareConnection.OpenAsync(credentials.Path, credentials.UserName, credentials.Password, cancellationToken).ConfigureAwait(false);
            await connection.RunAsync(async () =>
            {
            Directory.CreateDirectory(credentials.Path);
            var packages = Directory.Exists(local)
                ? Directory.EnumerateFiles(local, "*.zip").Where(file => !Path.GetFileName(file).StartsWith(".tmp-", StringComparison.Ordinal) && (onlyPackage is null || Path.GetFileName(file) == onlyPackage)).OrderBy(file => file, StringComparer.Ordinal).ToList()
                : [];
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(package);
                try
                {
                    if (await CopyOneAsync(package, credentials.Path, cancellationToken).ConfigureAwait(false)) { copied++; names.Add(name); } else already++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NasBackupException)
                {
                    failed++;
                    logger.LogWarning("Copy of a backup package to the NAS failed ({ErrorType}).", exception.GetType().Name);
                }
            }
            message = onlyPackage is not null && failed == 0 ? (copied == 1 ? $"Pachetul nou a fost copiat pe NAS: {onlyPackage}." : $"Pachetul nou era deja pe NAS: {onlyPackage}.")
                : failed == 0 ? $"Copiate {copied}, deja pe NAS {already}." : $"Copiate {copied}, deja pe NAS {already}, eșuate {failed} (vezi jurnalul aplicației).";
            });
        }
        catch (NasBackupException exception) { return await FinishAsync(new(false, 0, 0, 0, exception.Message), names, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("NAS copy failed ({ErrorType}).", exception.GetType().Name);
            return await FinishAsync(new(false, 0, 0, 0, NasBackupRules.IoMessage(exception)), names, cancellationToken).ConfigureAwait(false);
        }
        return await FinishAsync(new(failed == 0, copied, already, failed, message), names, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NasCopyResult> FinishAsync(NasCopyResult result, List<string> names, CancellationToken cancellationToken)
    {
        try { await store.RecordResultAsync(result.Ok, result.Message, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is MySqlException) { logger.LogWarning("NAS copy status could not be saved ({ErrorType}).", exception.GetType().Name); }
        if (result.Copied > 0 || !result.Ok)
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.CopyBackupToNas, "", "Copiere backup pe NAS",
                AuditDetails.Identification(("Rezultat", result.Message), ("Pachete copiate", names.Count == 0 ? "—" : string.Join(", ", names))), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>True when the package was copied now; false when it was already on the share (same size).</summary>
    internal static async Task<bool> CopyOneAsync(string package, string destination, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(package);
        var target = Path.Combine(destination, name);
        var length = new FileInfo(package).Length;
        if (File.Exists(target) && new FileInfo(target).Length == length) return false;
        var partial = target + ".partial";
        await using (var source = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        if (new FileInfo(partial).Length != length) throw new NasBackupException("Copia are altă mărime decât originalul.");
        // The copy is read back and compared with the original, so a silent corruption on the way is found.
        if (await HashAsync(partial, cancellationToken).ConfigureAwait(false) != await HashAsync(package, cancellationToken).ConfigureAwait(false))
            throw new NasBackupException("Copia nu coincide cu originalul.");
        File.Move(partial, target, overwrite: false);
        if (File.Exists(package + ".sha256") && !File.Exists(target + ".sha256"))
            await File.WriteAllTextAsync(target + ".sha256", await File.ReadAllTextAsync(package + ".sha256", cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<string> HashAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>After every backup made by the application, the new package goes to the NAS (when enabled). The backup itself never fails because of the copy.</summary>
public sealed class NasCopyingBackupService(IDatabaseBackupService inner, INasBackupCopier copier, ILogger<NasCopyingBackupService> logger) : IDatabaseBackupService
{
    public async Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default,
        IOperationLockHandle? existingLock = null)
    {
        var result = await inner.CreateBackupAsync(kind, progress, cancellationToken, existingLock).ConfigureAwait(false);
        // A restore holds the application frozen: its pre-restore package is copied by the next copy, not now.
        if (!result.Success)
        {
            await copier.RecordBackupFailureAsync(result.ErrorMessage ?? "Backup esuat.", cancellationToken).ConfigureAwait(false);
            return result;
        }
        if (existingLock is not null) return result;
        // The inventory pickup does not wait for the NAS (the local package is already made and checked): the copy goes on in the background and a
        // failure shows as the "Copie pe NAS lipsa" notification. A scheduled backup also catches up the packages an earlier copy missed; any other
        // backup copies only the package it has just made.
        if (kind == BackupKind.InventoryPickup) { copier.CopyAfterBackupAsync(result.PackageFileName, CancellationToken.None).FireAndForget(logger, "Copy of the pickup backup to the NAS"); return result; }
        try { await copier.CopyAfterBackupAsync(kind == BackupKind.Scheduled ? null : result.PackageFileName, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning("Copy of the new backup to the NAS failed ({ErrorType}).", exception.GetType().Name); }
        return result;
    }

    public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) => inner.ListPackagesAsync(cancellationToken);

    public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) => inner.DeletePackageAsync(fileName, reason, cancellationToken);
}

/// <summary>The "user" of work the application does by itself (the scheduled backup): an administrator named "sistem".</summary>
public sealed class SystemAccess : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("sistem");
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>The daily backup: once a day, at the time chosen in Settings, a backup is made (and copied to the NAS). A busy lock or a failure is retried after 15 minutes.</summary>
public sealed class BackupScheduler(IConfiguration configuration, IDataProtectionProvider protection, IOperationLockService locks, IAuditTrail audit,
    ILoggerFactory loggers, TimeProvider? clock = null, Func<CancellationToken, Task<ClockCheck>>? clockCheck = null) : BackgroundService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private ClockCheck? lastClock;
    private DateTimeOffset lastClockAt = DateTimeOffset.MinValue;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private string retentionDate = "";
    private DateTimeOffset retentionRetryAfter = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var logger = loggers.CreateLogger<BackupScheduler>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try { await TickAsync(logger, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception) { logger.LogWarning("Scheduled backup check failed ({ErrorType}).", exception.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(ILogger logger, CancellationToken token)
    {
        var serverNow = time.GetLocalNow();
        var settingsStore = new MariaBackupSettingsStore(configuration);
        // The server date can be changed on purpose (for another application): every ten minutes the clock is compared with the internet time, and while it is
        // off (more than the tolerance for backups) the schedule follows the internet time, so the daily backup stays on the real day and hour.
        var sinceCheck = serverNow - lastClockAt;
        if (lastClock is null || sinceCheck < TimeSpan.Zero || sinceCheck >= TimeSpan.FromMinutes(10))
        {
            lastClock = await (clockCheck?.Invoke(token) ?? BackupTimeCheck.RunAsync(configuration, token, forBackup: true, overall: TimeSpan.FromSeconds(6))).ConfigureAwait(false);
            lastClockAt = serverNow;
            try { await settingsStore.RecordClockAsync(lastClock, token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning("The clock check could not be recorded ({ErrorType}).", exception.GetType().Name); }
        }
        var now = lastClock is { Trusted: false, Skew: { } skew, ReferenceUtc: not null } ? (serverNow - skew).ToLocalTime() : serverNow;
        var today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var system = new SystemAccess();
        var copier = new NasBackupCopier(configuration, protection, system, audit, loggers.CreateLogger<NasBackupCopier>());
        // The old packages are removed once a day, independently of the daily backup (a backup that failed must not stop the clean-up).
        if (retentionDate != today && now >= retentionRetryAfter)
        {
            retentionRetryAfter = now.AddHours(1); // an error or a blocked run is retried in an hour, not every tick
            var (current, _) = await settingsStore.ReadAsync(token).ConfigureAwait(false);
            var nasOn = (await new MariaNasBackupStore(configuration, protection).ReadAsync(token).ConfigureAwait(false)).Settings.CopyEnabled;
            var retention = await BackupRetention.RunAsync(configuration, current, nasOn, copier, audit, logger, token).ConfigureAwait(false);
            // Blocked (untrusted clock, NAS unreadable): tried again in an hour; otherwise once a day.
            if (retention.Blocked is null) retentionDate = today;
            var removed = retention.Removed;
            if (removed > 0) logger.LogInformation("{Count} old backup packages were removed.", removed);
        }
        if (now < retryAfter) return;
        var (settings, last) = await settingsStore.ReadAsync(token).ConfigureAwait(false);
        if (!settings.ScheduleEnabled || last == today || !BackupScheduleDays.Includes(settings.ScheduleDays, now.DayOfWeek) || !NasBackupRules.TryParseTime(settings.ScheduleTime, out var at) || TimeOnly.FromDateTime(now.DateTime) < at) return;
        IDatabaseBackupService service = new NasCopyingBackupService(
            new MariaDatabaseBackupService(configuration, system, locks, audit, loggers.CreateLogger<MariaDatabaseBackupService>()), copier, loggers.CreateLogger<NasCopyingBackupService>());
        var result = await service.CreateBackupAsync(BackupKind.Scheduled, null, token).ConfigureAwait(false);
        if (result.Success) { await settingsStore.MarkScheduledAsync(today, token).ConfigureAwait(false); logger.LogInformation("Scheduled backup made: {File}", result.PackageFileName); }
        else
        {
            retryAfter = now.AddMinutes(15);
            logger.LogWarning("Scheduled backup failed: {Message}", result.ErrorMessage);
        }
    }
}
