using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Modules.Elcomtur;

// The automatic import of the Elcomtur backup (Setari -> Preluare Elcomtur). Once a day, at the hour chosen in the settings (04:00 by default), the directory (a NAS share, or a local folder) is read: the newest file whose name fits the
// mask is taken, and when it was modified after the last import it is copied to the server, checked (every file inside the archive is read to the end and its CRC compared), and imported
// for the chosen firm. The last two files that were imported stay on the server. Everything is journaled; the outcome (done, failed, no backup for too long) is a notification.

public sealed record ElcomturAutoSettings(bool Enabled, string Path, string UserName, bool HasPassword, string Mask, string FirmFolder, string FirmName, int AlertDays,
    DateTime? LastCheckUtc, bool? LastCheckOk, string LastMessage, DateTime? FailingSinceUtc, string LastFileName, DateTime? LastFileModifiedUtc, DateTime? LastImportUtc,
    DateOnly? LastDataDate, string LastSummary, DateTime UpdatedUtc, DateOnly? LastScheduledDate = null, int ScheduledHour = ElcomturAutoRules.DefaultHour)
{
    public static ElcomturAutoSettings Empty { get; } = new(false, "", "", false, "", "", "", ElcomturAutoRules.DefaultAlertDays, null, null, "", null, "", null, null, null, "", DateTime.UtcNow);

    /// <summary>The firm as it is told to the user: "firma1 — Electric Standard Prest SRL".</summary>
    public string FirmLabel => FirmFolder.Length == 0 ? "" : new ElcomturFirmChoice(FirmFolder, FirmName).Label;
}

/// <summary>What the administrator typed. A null <see cref="Password"/> keeps the stored one.</summary>
public sealed record ElcomturAutoInput(bool Enabled, string Path, string UserName, string? Password, string Mask, string FirmFolder, string FirmName, int AlertDays, int ScheduledHour = ElcomturAutoRules.DefaultHour);

public sealed record ElcomturAutoResult(bool Ok, string Message, bool Imported = false);

/// <summary>The firms of the newest backup found in the directory (or why it could not be read).</summary>
public sealed record ElcomturAutoFirms(bool Ok, string Message, IReadOnlyList<ElcomturFirmChoice> Firms, string FileName = "");

public sealed record ElcomturShareFile(string Name, long SizeBytes, DateTime ModifiedUtc);

public sealed class ElcomturAutoException(string message) : Exception(message);

public static class ElcomturAutoRules
{
    // The hour (0-23, local time) from which the daily check is due; 04:00 unless the administrator chose another one.
    public const int DefaultHour = 4, MinHour = 0, MaxHour = 23;
    public const string HourMessage = "Ora verificării trebuie să fie între 00:00 și 23:00.";

    /// <summary>"04:00": an hour as it is shown.</summary>
    public static string HourText(int hour) => hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
    public const int DefaultAlertDays = 3;
    public const int MinAlertDays = 1, MaxAlertDays = 365;
    // A file modified less than this ago is still being written by the backup of the Elcomtur program.
    public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(5);
    // How many days a "backup taken" notification stays in the list before it closes by itself.
    public const int SuccessNoticeDays = 3;
    public const int KeptBackups = 2;

    public const string PathMessage = "Calea trebuie să fie o cale de rețea (\\\\server\\partajare\\folder) sau un director local (de exemplu D:\\Elcomtur).";
    public const string UserMessage = "Pentru o cale de rețea introdu contul cu care se face conectarea (cel mult 100 de caractere).";
    public const string MaskMessage = "Introdu masca numelui fișierului, de exemplu *elcomtur* (cel mult 200 de caractere, fără / \\ : \" < > |).";
    public const string FirmMessage = "Alege firma din backup (citește firmele din cel mai recent fișier cu butonul „Citește firmele din backup”).";
    public const string AlertMessage = "Pragul de alertă trebuie să fie între 1 și 365 de zile.";
    public const string NotConfiguredMessage = "Preluarea automată nu este configurată (director, mască, firmă și, pentru o cale de rețea, cont și parolă).";

    public static string NormalizePath(string? path) => (path ?? "").Trim().TrimEnd('\\', '/');

    public static bool IsNetwork(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);

    public static string? PathError(string? path)
    {
        var value = NormalizePath(path);
        if (value.Length == 0 || value.Length > 500) return PathMessage;
        if (IsNetwork(value)) return NasBackupRules.PathError(value) is null ? null : PathMessage;
        return Path.IsPathRooted(value) && value.IndexOfAny(['*', '?', '"', '<', '>', '|']) < 0 && !value.Split('\\', '/').Any(part => part == "..") ? null : PathMessage;
    }

    public static string? MaskError(string? mask)
    {
        var value = (mask ?? "").Trim();
        return value.Length == 0 || value.Length > 200 || value.IndexOfAny(['/', '\\', ':', '"', '<', '>', '|']) >= 0 || value.All(character => character is '*' or '?') ? MaskMessage : null;
    }

    public static string? Validate(ElcomturAutoInput input)
    {
        if (input.AlertDays is < MinAlertDays or > MaxAlertDays) return AlertMessage;
        if (input.ScheduledHour is < MinHour or > MaxHour) return HourMessage;
        if (!input.Enabled) return null;
        if (PathError(input.Path) is { } path) return path;
        if (IsNetwork(NormalizePath(input.Path)) && (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Trim().Length > 100)) return UserMessage;
        if (MaskError(input.Mask) is { } mask) return mask;
        if (string.IsNullOrWhiteSpace(input.FirmFolder)) return FirmMessage;
        return null;
    }

    /// <summary>The mask as a file name pattern: * is any text, ? one character; a mask without them is the whole name. Capital letters do not matter.</summary>
    public static Regex MaskPattern(string mask) =>
        new("^" + Regex.Escape(mask.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The most recently modified file that fits the mask (temporary and hidden files are not backups), or null.</summary>
    public static ElcomturShareFile? Newest(IEnumerable<ElcomturShareFile> files, string mask)
    {
        var pattern = MaskPattern(mask);
        return files.Where(file => !file.Name.StartsWith('.') && !file.Name.StartsWith('~') && pattern.IsMatch(file.Name))
            .OrderByDescending(file => file.ModifiedUtc).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    public static DateTime TruncateToSeconds(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    /// <summary>A file is new when it was modified after the reference (the file of the last import, or the time of the last import itself).</summary>
    public static bool IsNewer(DateTime fileModifiedUtc, DateTime referenceUtc) => TruncateToSeconds(fileModifiedUtc) > TruncateToSeconds(referenceUtc);

    /// <summary>The scheduled check is due from the chosen hour on (04:00 by default), once a day (the day of the last scheduled attempt is not repeated).</summary>
    public static bool IsDue(DateTime localNow, DateOnly? lastScheduledDate, int hour = DefaultHour) => localNow.Hour >= hour && lastScheduledDate != DateOnly.FromDateTime(localNow);

    public static string Describe(DateTime? utc) => utc is null ? "niciodată" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>What a failed access to the directory means, in words.</summary>
    public static string IoMessage(Exception exception)
    {
        var code = exception.HResult & 0xFFFF;
        if (code is 53 or 67 or 1203 or 1222 or 1231) return NasBackupRules.ConnectionMessage(code);
        if (exception is DirectoryNotFoundException) return "Directorul nu există. Verifică calea.";
        if (code is 1326 or 86 or 1331 or 1909 || exception is UnauthorizedAccessException) return "Contul sau parola sunt greșite, sau contul nu are drept de citire în director.";
        return "Directorul sau fișierul nu poate fi citit (verifică calea, rețeaua și drepturile contului).";
    }
}

public interface IElcomturAutoService
{
    /// <summary>Administrator only.</summary>
    Task<ElcomturAutoSettings> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator only; journaled (the password only as "schimbata", never its value).</summary>
    Task SaveAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default);
    /// <summary>Connects with the typed values (an empty password uses the stored one) and reports what the directory holds for the mask. Journaled.</summary>
    Task<ElcomturAutoResult> TestAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default);
    /// <summary>The firms (with their names) of the newest backup that fits the mask, read in place on the share without copying the file.</summary>
    Task<ElcomturAutoFirms> ReadFirmsAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default);
    /// <summary>The check of the scheduled run, on request: imports the newest file when it is newer than the last import (or whatever the newest is, when force is true).</summary>
    Task<ElcomturAutoResult> CheckNowAsync(bool force, CancellationToken cancellationToken = default);
    /// <summary>The scheduled run for the system: null when it is not due (off, before the chosen hour, or already tried today).</summary>
    Task<ElcomturAutoResult?> RunScheduledAsync(DateTime localNow, CancellationToken cancellationToken = default);
}

/// <summary>The settings row (one row, id 1). Without an access control (the system) nothing is checked.</summary>
public sealed class MariaElcomturAutoStore(IConfiguration configuration, IDataProtectionProvider protection, IAccessControl? access = null, IAuditTrail? audit = null)
{
    private const string Purpose = "BlazorStoc.ElcomturAuto.Password";
    private readonly IDataProtector protector = protection.CreateProtector(Purpose);

    public async Task<ElcomturAutoSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-import-elcomtur.view", cancellationToken).ConfigureAwait(false);
        return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
    }

    internal async Task<(ElcomturAutoSettings Settings, string? Password)> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT enabled,directory_path,username,password_protected,file_mask,firm_folder,firm_name,alert_days,last_check_utc,last_check_ok,last_message,failing_since_utc,
                   last_file_name,last_file_modified_utc,last_import_utc,last_data_date,last_summary,updated_utc,last_scheduled_date,scheduled_hour FROM elcomtur_auto_settings WHERE id=1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (ElcomturAutoSettings.Empty, null);
        string? protectedPassword = reader.IsDBNull(3) ? null : reader.GetString(3), password = null;
        // Keys lost or changed (another machine, a restored database): the password has to be typed again.
        if (protectedPassword is not null) { try { password = protector.Unprotect(protectedPassword); } catch (CryptographicException) { password = null; } }
        DateTime? At(int ordinal) => reader.IsDBNull(ordinal) ? null : MariaTimeText.Parse(reader.GetString(ordinal));
        string Text(int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        var settings = new ElcomturAutoSettings(reader.GetBoolean(0), reader.GetString(1), reader.GetString(2), password is not null, reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7),
            At(8), reader.IsDBNull(9) ? null : reader.GetBoolean(9), Text(10), At(11), Text(12), At(13), At(14),
            reader.IsDBNull(15) ? null : DateOnly.ParseExact(reader.GetString(15), "yyyy-MM-dd", CultureInfo.InvariantCulture), Text(16), MariaTimeText.Parse(reader.GetString(17)),
            reader.IsDBNull(18) ? null : DateOnly.ParseExact(reader.GetString(18), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.IsDBNull(19) ? ElcomturAutoRules.DefaultHour : reader.GetInt32(19));
        return (settings, password);
    }

    public async Task SaveAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-import-elcomtur.edit", cancellationToken).ConfigureAwait(false);
        if (ElcomturAutoRules.Validate(input) is { } error) throw new ElcomturAutoException(error);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ElcomturAutoException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var old = (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var path = ElcomturAutoRules.NormalizePath(input.Path);
        var passwordChanged = !string.IsNullOrEmpty(input.Password);
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("""
                INSERT INTO elcomtur_auto_settings (id,enabled,directory_path,username,password_protected,file_mask,firm_folder,firm_name,alert_days,scheduled_hour,updated_by,updated_utc)
                VALUES (1,@enabled,@path,@user,@pass,@mask,@firm,@firmName,@days,@hour,@by,@now)
                ON DUPLICATE KEY UPDATE enabled=@enabled,directory_path=@path,username=@user,password_protected=IF(@changed=1,@pass,password_protected),file_mask=@mask,
                    firm_folder=@firm,firm_name=@firmName,alert_days=@days,scheduled_hour=@hour,updated_by=@by,updated_utc=@now
                """, connection);
            command.Parameters.AddWithValue("@enabled", input.Enabled);
            command.Parameters.AddWithValue("@path", path);
            command.Parameters.AddWithValue("@user", input.UserName.Trim());
            command.Parameters.AddWithValue("@pass", passwordChanged ? protector.Protect(input.Password!) : DBNull.Value);
            command.Parameters.AddWithValue("@changed", passwordChanged ? 1 : 0);
            command.Parameters.AddWithValue("@mask", input.Mask.Trim());
            command.Parameters.AddWithValue("@firm", input.FirmFolder.Trim());
            command.Parameters.AddWithValue("@firmName", input.FirmName.Trim());
            command.Parameters.AddWithValue("@days", input.AlertDays);
            command.Parameters.AddWithValue("@hour", input.ScheduledHour);
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var changes = new[]
        {
            new AuditChange("Preluare automată", old.Enabled ? "activă" : "inactivă", input.Enabled ? "activă" : "inactivă"),
            new AuditChange("Director backup Elcomtur", Show(old.Path), Show(path)),
            new AuditChange("Cont director", Show(old.UserName), Show(input.UserName.Trim())),
            new AuditChange("Parola director", old.HasPassword ? "setată" : "—", passwordChanged ? "schimbată" : old.HasPassword ? "setată" : "—"),
            new AuditChange("Mască fișier", Show(old.Mask), Show(input.Mask.Trim())),
            new AuditChange("Firmă", Show(old.FirmLabel), Show(new ElcomturFirmChoice(input.FirmFolder.Trim(), input.FirmName.Trim()).Label)),
            new AuditChange("Prag alertă lipsă backup (zile)", old.AlertDays.ToString(CultureInfo.InvariantCulture), input.AlertDays.ToString(CultureInfo.InvariantCulture)),
            new AuditChange("Ora verificării automate", ElcomturAutoRules.HourText(old.ScheduledHour), ElcomturAutoRules.HourText(input.ScheduledHour))
        };
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Elcomtur, AuditActions.SetElcomturAuto, "", "Configurare preluare automată Elcomtur",
            AuditDetails.Changes(changes), "", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The outcome of one check. The import columns are only set by a successful import; a success clears the failure, a failure keeps the day it began.</summary>
    internal async Task RecordAsync(bool ok, string message, DateOnly? scheduledDate, string? fileName, DateTime? fileModifiedUtc, DateOnly? dataDate, string? summary, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            UPDATE elcomtur_auto_settings SET last_check_utc=@now,last_check_ok=@ok,last_message=@message,
                failing_since_utc=IF(@ok=1,NULL,COALESCE(failing_since_utc,@now)),
                last_scheduled_date=COALESCE(@scheduled,last_scheduled_date),
                last_file_name=IF(@file IS NULL,last_file_name,@file),last_file_modified_utc=IF(@file IS NULL,last_file_modified_utc,@modified),
                last_import_utc=IF(@file IS NULL,last_import_utc,@now),last_data_date=IF(@file IS NULL,last_data_date,@data),last_summary=IF(@file IS NULL,last_summary,@summary)
            WHERE id=1
            """, connection);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@ok", ok);
        command.Parameters.AddWithValue("@message", message.Length > 480 ? message[..480] : message);
        command.Parameters.AddWithValue("@scheduled", scheduledDate is null ? DBNull.Value : scheduledDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@file", fileName is null ? DBNull.Value : fileName);
        command.Parameters.AddWithValue("@modified", fileModifiedUtc is null ? DBNull.Value : MariaTimeText.Format(fileModifiedUtc.Value));
        command.Parameters.AddWithValue("@data", dataDate is null ? DBNull.Value : dataDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@summary", summary is null ? DBNull.Value : summary.Length > 480 ? summary[..480] : summary);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Show(string value) => value.Length == 0 ? "—" : value;
}

public sealed class ElcomturAutoService(IConfiguration configuration, IDataProtectionProvider protection, IBeneficiaryRepository beneficiaries, IElcomturBalanceRepository balances,
    IAccessControl? access, IAuditTrail? audit, ILogger<ElcomturAutoService> logger, TimeProvider? clock = null) : IElcomturAutoService
{
    // Only one check at a time in the whole application (a manual one while the scheduled one runs would copy the same 340 MB twice).
    private static readonly SemaphoreSlim Running = new(1, 1);
    private readonly MariaElcomturAutoStore store = new(configuration, protection, access, audit);
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public Task<ElcomturAutoSettings> GetAsync(CancellationToken cancellationToken = default) => store.GetAsync(cancellationToken);

    public Task SaveAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default) => store.SaveAsync(input, cancellationToken);

    // ---- the directory -------------------------------------------------------------------------------------------------------------------------------------------------

    private static async Task<T> OnShareAsync<T>(string path, string userName, string password, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (!ElcomturAutoRules.IsNetwork(path)) return await work().ConfigureAwait(false);
        using var connection = await NetworkShareConnection.OpenAsync(path, userName, password, cancellationToken).ConfigureAwait(false);
        T result = default!;
        await connection.RunAsync(async () => result = await work().ConfigureAwait(false)).ConfigureAwait(false);
        return result;
    }

    private static List<ElcomturShareFile> ListFiles(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        return [.. new DirectoryInfo(directory).EnumerateFiles().Select(info => new ElcomturShareFile(info.Name, info.Length, info.LastWriteTimeUtc))];
    }

    /// <summary>The password to use: the typed one, else the stored one (null when neither exists).</summary>
    private async Task<(string Path, string User, string? Password)> ConnectionOfAsync(ElcomturAutoInput input, CancellationToken cancellationToken)
    {
        var password = string.IsNullOrEmpty(input.Password) ? (await store.ReadAsync(cancellationToken).ConfigureAwait(false)).Password : input.Password;
        return (ElcomturAutoRules.NormalizePath(input.Path), input.UserName.Trim(), password);
    }

    private static string? InputError(ElcomturAutoInput input, string? password)
    {
        if (ElcomturAutoRules.PathError(input.Path) is { } path) return path;
        if (ElcomturAutoRules.IsNetwork(ElcomturAutoRules.NormalizePath(input.Path)))
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Trim().Length > 100) return ElcomturAutoRules.UserMessage;
            if (string.IsNullOrEmpty(password)) return "Introdu parola contului (nu este salvată nici una).";
        }
        return ElcomturAutoRules.MaskError(input.Mask);
    }

    private string Failure(Exception exception)
    {
        switch (exception)
        {
            case ElcomturAutoException or ElcomturOperationException or NasBackupException: return exception.Message;
            case MySqlException: logger.LogWarning("Elcomtur automatic import: database error ({ErrorType}).", exception.GetType().Name); return "Datele nu au putut fi salvate în baza de date. Reîncearcă; detaliile sunt în jurnalul aplicației.";
            default: logger.LogWarning("Elcomtur automatic import: access failed ({ErrorType}).", exception.GetType().Name); return ElcomturAutoRules.IoMessage(exception);
        }
    }

    private static bool Expected(Exception exception) =>
        exception is ElcomturAutoException or ElcomturOperationException or NasBackupException or IOException or UnauthorizedAccessException or MySqlException or InvalidDataException;

    public async Task<ElcomturAutoResult> TestAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-import-elcomtur.edit", cancellationToken).ConfigureAwait(false);
        var (path, user, password) = await ConnectionOfAsync(input, cancellationToken).ConfigureAwait(false);
        ElcomturAutoResult result;
        if (InputError(input, password) is { } invalid) result = new(false, invalid);
        else
        {
            try
            {
                var files = await OnShareAsync(path, user, password ?? "", () => Task.FromResult(ListFiles(path)), cancellationToken).ConfigureAwait(false);
                var matching = files.Where(file => ElcomturAutoRules.MaskPattern(input.Mask).IsMatch(file.Name) && !file.Name.StartsWith('.')).ToList();
                var newest = ElcomturAutoRules.Newest(files, input.Mask);
                result = newest is null
                    ? new(false, $"Conexiunea funcționează, dar în director nu există niciun fișier care să corespundă măștii „{input.Mask.Trim()}” ({files.Count} fișiere în total).")
                    : new(true, $"Conexiunea funcționează. {matching.Count} fișiere corespund măștii; cel mai recent: {newest.Name} ({FormatSize(newest.SizeBytes)}, modificat {ElcomturAutoRules.Describe(newest.ModifiedUtc)}).");
            }
            catch (Exception exception) when (Expected(exception) || exception is DirectoryNotFoundException) { result = new(false, Failure(exception)); }
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Elcomtur, AuditActions.TestElcomturAuto, "", "Testare director backup Elcomtur",
            AuditDetails.Identification(("Director", path), ("Mască", input.Mask.Trim()), ("Rezultat", result.Ok ? "reușit" : result.Message)), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<ElcomturAutoFirms> ReadFirmsAsync(ElcomturAutoInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-import-elcomtur.edit", cancellationToken).ConfigureAwait(false);
        var (path, user, password) = await ConnectionOfAsync(input, cancellationToken).ConfigureAwait(false);
        if (InputError(input, password) is { } invalid) return new(false, invalid, []);
        try
        {
            return await OnShareAsync(path, user, password ?? "", () =>
            {
                var newest = ElcomturAutoRules.Newest(ListFiles(path), input.Mask);
                if (newest is null) return Task.FromResult(new ElcomturAutoFirms(false, $"În director nu există niciun fișier care să corespundă măștii „{input.Mask.Trim()}”.", []));
                // The archive is read in place (only its table of contents and the small FIRMA table of each firm), not copied.
                var firms = ElcomturArchive.FirmChoices(Path.Combine(path, newest.Name));
                return Task.FromResult(firms.Count == 0
                    ? new ElcomturAutoFirms(false, $"Fișierul {newest.Name} nu conține nicio firmă cu tabelele Elcomtur necesare (clienți, facturi emise, casă).", [], newest.Name)
                    : new ElcomturAutoFirms(true, $"Firmele din {newest.Name} (modificat {ElcomturAutoRules.Describe(newest.ModifiedUtc)}).", firms, newest.Name));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (Expected(exception) || exception is DirectoryNotFoundException) { return new(false, Failure(exception), []); }
    }

    // ---- the check -----------------------------------------------------------------------------------------------------------------------------------------------------

    public async Task<ElcomturAutoResult> CheckNowAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAsync("setari-import-elcomtur.run", cancellationToken).ConfigureAwait(false);
        return await CheckAsync(force, null, cancellationToken).ConfigureAwait(false) ?? new(false, "O verificare sau o preluare este deja în curs.");
    }

    public async Task<ElcomturAutoResult?> RunScheduledAsync(DateTime localNow, CancellationToken cancellationToken = default)
    {
        var (settings, _) = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled || !ElcomturAutoRules.IsDue(localNow, settings.LastScheduledDate, settings.ScheduledHour)) return null;
        return await CheckAsync(false, DateOnly.FromDateTime(localNow), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ElcomturAutoResult?> CheckAsync(bool force, DateOnly? scheduledDate, CancellationToken cancellationToken)
    {
        if (!await Running.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return null;
        try { return await CheckCoreAsync(force, scheduledDate, cancellationToken).ConfigureAwait(false); }
        finally { Running.Release(); }
    }

    private async Task<ElcomturAutoResult> CheckCoreAsync(bool force, DateOnly? scheduledDate, CancellationToken cancellationToken)
    {
        var (settings, password) = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var input = new ElcomturAutoInput(settings.Enabled, settings.Path, settings.UserName, password, settings.Mask, settings.FirmFolder, settings.FirmName, settings.AlertDays, settings.ScheduledHour);
        // A manual check also works while the automatic import is off, but never without a complete configuration (nothing is recorded then).
        if (settings.Path.Length == 0 || ElcomturAutoRules.PathError(settings.Path) is not null || ElcomturAutoRules.MaskError(settings.Mask) is not null || settings.FirmFolder.Length == 0
            || (ElcomturAutoRules.IsNetwork(ElcomturAutoRules.NormalizePath(settings.Path)) && (password is null || settings.UserName.Length == 0)))
            return new(false, ElcomturAutoRules.NotConfiguredMessage);
        var path = ElcomturAutoRules.NormalizePath(settings.Path);
        var local = MariaAssetPaths.ElcomturBackups(configuration);
        var temporary = Path.Combine(local, $".tmp-{Guid.NewGuid():N}.elc");
        ElcomturShareFile? chosen = null;
        try
        {
            // 1. The newest file that fits the mask.
            var files = await OnShareAsync(path, settings.UserName, password ?? "", () => Task.FromResult(ListFiles(path)), cancellationToken).ConfigureAwait(false);
            chosen = ElcomturAutoRules.Newest(files, settings.Mask)
                ?? throw new ElcomturAutoException($"În directorul {path} nu există niciun fișier care să corespundă măștii „{settings.Mask}”. Verifică masca și dacă backup-ul Elcomtur se salvează în acel director.");
            // 2. Newer than the last import? The reference is the file of the last automatic import; before any, the time of the last import (also a manual one).
            var reference = settings.LastFileModifiedUtc ?? (await balances.GetInfoAsync(cancellationToken).ConfigureAwait(false))?.ImportedUtc;
            if (!force && reference is { } known && !ElcomturAutoRules.IsNewer(chosen.ModifiedUtc, known))
            {
                var message = $"Nu există un backup mai nou decât ultima preluare (cel mai recent fișier: {chosen.Name}, modificat {ElcomturAutoRules.Describe(chosen.ModifiedUtc)}).";
                await store.RecordAsync(true, message, scheduledDate, null, null, null, null, cancellationToken).ConfigureAwait(false);
                await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Elcomtur, AuditActions.CheckElcomturAuto, "", "Verificare backup Elcomtur",
                    AuditDetails.Identification(("Director", path), ("Mască", settings.Mask), ("Rezultat", "niciun backup nou"), ("Cel mai recent fișier", chosen.Name), ("Modificat", ElcomturAutoRules.Describe(chosen.ModifiedUtc))), "", cancellationToken).ConfigureAwait(false);
                return new(true, message);
            }
            // 3. Still being written by the Elcomtur program?
            if (time.GetUtcNow().UtcDateTime - chosen.ModifiedUtc < ElcomturAutoRules.SettleTime)
                throw new ElcomturAutoException($"Fișierul {chosen.Name} a fost modificat acum mai puțin de {(int)ElcomturAutoRules.SettleTime.TotalMinutes} minute (programul Elcomtur încă îl scrie). Reîncearcă mai târziu.");
            // 4. Copied to the server.
            Directory.CreateDirectory(local);
            await OnShareAsync(path, settings.UserName, password ?? "", async () =>
            {
                await using (var source = new FileStream(Path.Combine(path, chosen.Name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true))
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    if (source.Length > ElcomturImportService.MaximumBytes) throw new ElcomturAutoException($"Fișierul {chosen.Name} este prea mare pentru preluare.");
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                    if (target.Length != source.Length) throw new ElcomturAutoException($"Copierea fișierului {chosen.Name} pe server nu s-a terminat (dimensiune diferită). Reîncearcă.");
                }
                return 0;
            }, cancellationToken).ConfigureAwait(false);
            // 5. Integrity of every file inside, then the chosen firm.
            var entries = await Task.Run(() => ElcomturArchive.Verify(temporary, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (!ElcomturArchive.Firms(temporary).Contains(settings.FirmFolder, StringComparer.OrdinalIgnoreCase))
                throw new ElcomturAutoException($"Firma {settings.FirmLabel} nu mai există în backup-ul {chosen.Name}. Alege altă firmă în Setări → Preluare Elcomtur.");
            // 6. Imported (journaled by the import itself under its own action).
            var import = new ElcomturImportService(beneficiaries, balances, access, audit);
            var report = await import.RunFileAsync(temporary, settings.FirmFolder, true, AuditActions.ImportElcomturAuto, chosen.Name, cancellationToken).ConfigureAwait(false);
            // 7. Kept on the server; only the last two imported files stay.
            var kept = Path.Combine(local, chosen.Name);
            File.Move(temporary, kept, overwrite: true);
            File.SetLastWriteTimeUtc(kept, chosen.ModifiedUtc);
            Retention(local);
            var summary = $"{report.Matched} beneficiari cu balanță, {report.MatchedOwed} cu plăți restante (total {ElcomturFormat.Money(report.OwedTotal)})";
            var done = $"Backup-ul {chosen.Name} (modificat {ElcomturAutoRules.Describe(chosen.ModifiedUtc)}) a fost verificat ({entries} fișiere citite fără erori) și preluat pentru {settings.FirmLabel}, date din {StockMovementRules.DisplayDate(report.DataDate)}: {summary}.";
            await store.RecordAsync(true, done, scheduledDate, chosen.Name, ElcomturAutoRules.TruncateToSeconds(chosen.ModifiedUtc), report.DataDate, summary, cancellationToken).ConfigureAwait(false);
            return new(true, done, true);
        }
        catch (Exception exception) when (Expected(exception) || exception is DirectoryNotFoundException)
        {
            var message = Failure(exception);
            try
            {
                await store.RecordAsync(false, message, scheduledDate, null, null, null, null, cancellationToken).ConfigureAwait(false);
                await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Elcomtur, AuditActions.FailElcomturAuto, "", "Preluare automată Elcomtur eșuată",
                    AuditDetails.Identification(("Director", path), ("Fișier", chosen?.Name ?? "—"), ("Cauza", message)), "", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception recordFailure) when (recordFailure is MySqlException or IOException)
            { logger.LogWarning("Elcomtur automatic import: the failure could not be recorded ({ErrorType}).", recordFailure.GetType().Name); }
            return new(false, message);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }

    /// <summary>Only the last two imported backups stay on the server (the temporary files of a run in progress are not touched).</summary>
    internal static void Retention(string folder)
    {
        foreach (var old in new DirectoryInfo(folder).EnumerateFiles().Where(file => !file.Name.StartsWith(".tmp-", StringComparison.Ordinal))
                     .OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name, StringComparer.Ordinal).Skip(ElcomturAutoRules.KeptBackups))
            try { old.Delete(); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string FormatSize(long bytes) => bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):N0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
