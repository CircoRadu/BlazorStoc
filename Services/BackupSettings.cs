using MySqlConnector;

namespace BlazorStoc.Services;

// Settings -> Backup: the daily backup (time), the age after which a missing backup is notified, and the automatic removal of old local packages.
// They concern the local backup, with or without a NAS. The NAS settings (path, account, password) stay in Settings -> Backup NAS.

public sealed record BackupSettings(bool ScheduleEnabled, string ScheduleTime, int MaxAgeDays, bool RetentionEnabled, int RetentionDays,
    DateTime? LastErrorUtc = null, string LastError = "", bool TimeCheckEnabled = true, string NtpServers = "", int ScheduleDays = BackupScheduleDays.All)
{
    public static BackupSettings Empty { get; } = new(false, "02:00", BackupAlertRules.BackupMaxAgeDays, false, BackupSettingsRules.DefaultRetentionDays);
}

/// <summary><c>NtpServers</c>: up to three host names separated by commas; empty = the defaults (<see cref="TrustedClock.DefaultServers"/>).</summary>
public sealed record BackupSettingsInput(bool ScheduleEnabled, string ScheduleTime, int MaxAgeDays, bool RetentionEnabled, int RetentionDays,
    bool TimeCheckEnabled = true, string NtpServers = "", int ScheduleDays = BackupScheduleDays.All);

/// <summary>The days of the week of the scheduled backup, as a mask: Monday = 1, Tuesday = 2 ... Sunday = 64 (127 = every day).</summary>
public static class BackupScheduleDays
{
    public const int All = 127;
    public const string NoneMessage = "Alege cel puțin o zi a săptămânii pentru backup-ul programat.";
    public static readonly string[] Names = ["Luni", "Marți", "Miercuri", "Joi", "Vineri", "Sâmbătă", "Duminică"];
    private static readonly string[] Short = ["Lu", "Ma", "Mi", "Jo", "Vi", "Sâ", "Du"];

    /// <summary>0 = Monday ... 6 = Sunday.</summary>
    public static int Index(DayOfWeek day) => ((int)day + 6) % 7;
    public static bool Includes(int mask, DayOfWeek day) => (mask & (1 << Index(day))) != 0;
    public static bool Valid(int mask) => mask is >= 1 and <= All;

    /// <summary>The longest stretch, in days, between two scheduled backups (a single day: a week). The notification of a missing backup waits at least that long.</summary>
    public static int LongestGap(int mask)
    {
        var days = Enumerable.Range(0, 7).Where(index => (mask & (1 << index)) != 0).ToList();
        if (days.Count == 0) return 1;
        if (days.Count == 1) return 7;
        return Enumerable.Range(0, days.Count).Max(index => (days[(index + 1) % days.Count] - days[index] + 7) % 7);
    }

    public static string Describe(int mask) =>
        mask == All ? "zilnic" : string.Join(", ", Enumerable.Range(0, 7).Where(index => (mask & (1 << index)) != 0).Select(index => Short[index]));
}


public static class BackupTimeRules
{
    public const int MaxServers = 3;
    /// <summary>The servers offered in Settings (reliable public NTP servers); a host of one's own network can be typed instead.</summary>
    public static readonly IReadOnlyList<string> Suggested = ["time.cloudflare.com", "time.google.com", "time.windows.com", "pool.ntp.org", "time.nist.gov", "time.apple.com"];
    public const string ServersMessage = "Serverele NTP: între 1 și 3 nume de gazdă (de ex. pool.ntp.org), fără adrese web, fără spații și fără repetări.";
    private static readonly char[] Separators = [',', ';', '\n', '\r'];
    private static readonly System.Text.RegularExpressions.Regex Host = new(@"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Parse(string? text) =>
        [.. (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The servers to ask: the saved ones, or the defaults when none is saved.</summary>
    public static IReadOnlyList<string> Servers(string? text) => Parse(text) is { Count: > 0 } saved ? saved : TrustedClock.DefaultServers;

    public static string? Validate(string? text)
    {
        var raw = (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var servers = Parse(text);
        return servers.Count is < 1 or > MaxServers || servers.Count != raw.Length || servers.Any(server => !Host.IsMatch(server)) ? ServersMessage : null;
    }
}


public static class BackupSettingsRules
{
    /// <summary>The newest packages are never removed by the retention, whatever their age.</summary>
    public const int KeepNewest = 4;
    public const int DefaultRetentionDays = 30, MinRetentionDays = 7, MaxRetentionDays = 3650;
    public const string RetentionMessage = "Pachetele se șterg după cel puțin 7 zile (cel mult 3650); ultimele 4 pachete se păstrează oricum.";

    public static string? Validate(BackupSettingsInput input)
    {
        if (input.ScheduleEnabled && !NasBackupRules.TryParseTime(input.ScheduleTime, out _)) return NasBackupRules.TimeMessage;
        if (input.ScheduleEnabled && !BackupScheduleDays.Valid(input.ScheduleDays)) return BackupScheduleDays.NoneMessage;
        if (input.MaxAgeDays is < 1 or > 30) return NasBackupRules.MaxAgeMessage;
        if (input.RetentionEnabled && input.RetentionDays is < MinRetentionDays or > MaxRetentionDays) return RetentionMessage;
        if (input.TimeCheckEnabled && !string.IsNullOrWhiteSpace(input.NtpServers) && BackupTimeRules.Validate(input.NtpServers) is { } servers) return servers;
        return null;
    }

    /// <summary>
    /// The packages the retention removes: older than the threshold and not among the newest <see cref="KeepNewest"/>. A package made while the time could not be
    /// verified (its date may be wrong) is never removed automatically, nor counted: only an administrator deletes it by hand.
    /// </summary>
    public static IReadOnlyList<BackupPackage> Expired(IReadOnlyList<BackupPackage> packages, int days, DateTime nowUtc) =>
        [.. packages.Where(package => !package.TimeUnverified).OrderByDescending(package => package.CreatedAtUtc).Skip(KeepNewest).Where(package => package.CreatedAtUtc < nowUtc.AddDays(-days))];
}

public interface IBackupSettingsRepository
{
    /// <summary>Administrator only.</summary>
    Task<BackupSettings> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Administrator only; journaled.</summary>
    Task SaveAsync(BackupSettingsInput input, CancellationToken cancellationToken = default);
    /// <summary>Administrator only: asks the given NTP servers now ("Verifică acum"); nothing is saved.</summary>
    Task<IReadOnlyList<TimeProbeEntry>> ProbeTimeAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default);
}

/// <summary>The settings row (one row, id 1). Without an access control (the system: scheduler, backup flow) nothing is checked or journaled here.</summary>
public sealed class MariaBackupSettingsStore(IConfiguration configuration, IAccessControl? access = null, IAuditTrail? audit = null) : IBackupSettingsRepository
{
    public async Task<BackupSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
    }

    internal async Task<(BackupSettings Settings, string? LastScheduledDate)> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT schedule_enabled,schedule_time,last_scheduled_date,max_age_days,retention_enabled,retention_days,last_error_utc,last_error,ntp_check_enabled,ntp_servers,schedule_days FROM backup_settings WHERE id=1
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (BackupSettings.Empty, null);
        return (new BackupSettings(reader.GetBoolean(0), reader.GetString(1), reader.GetInt32(3), reader.GetBoolean(4), reader.GetInt32(5),
            reader.IsDBNull(6) ? null : MariaTimeText.Parse(reader.GetString(6)), reader.IsDBNull(7) ? "" : reader.GetString(7),
            reader.GetBoolean(8), reader.IsDBNull(9) ? "" : reader.GetString(9), reader.GetInt32(10)), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    /// <summary>
    /// Once, after the table exists: the schedule and age that were kept with the NAS settings (before the backup settings had their own table) are copied here.
    /// Done by the application account (the migration account can only change the structure); a row that exists is never touched.
    /// </summary>
    public static async Task CopyFromNasSettingsAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT IGNORE INTO backup_settings (id,schedule_enabled,schedule_time,last_scheduled_date,max_age_days,last_error_utc,last_error,updated_by,updated_utc)
            SELECT id,schedule_enabled,schedule_time,last_scheduled_date,max_age_days,last_error_utc,last_error,updated_by,updated_utc FROM backup_nas_settings WHERE id=1
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(BackupSettingsInput input, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (BackupSettingsRules.Validate(input) is { } error) throw new NasBackupException(error);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new NasBackupException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var old = (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        var time = NasBackupRules.TryParseTime(input.ScheduleTime, out var parsed) ? parsed.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : old.ScheduleTime;
        await using (var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            await using var command = new MySqlCommand("""
                INSERT INTO backup_settings (id,schedule_enabled,schedule_time,last_scheduled_date,max_age_days,retention_enabled,retention_days,ntp_check_enabled,ntp_servers,schedule_days,updated_by,updated_utc)
                VALUES (1,@schedule,@time,IF(@resetSchedule=1,@scheduleDate,NULL),@maxAge,@retention,@days,@ntpOn,@ntp,@weekdays,@by,@now)
                ON DUPLICATE KEY UPDATE schedule_enabled=@schedule,schedule_time=@time,max_age_days=@maxAge,retention_enabled=@retention,retention_days=@days,ntp_check_enabled=@ntpOn,ntp_servers=@ntp,schedule_days=@weekdays,
                    last_scheduled_date=IF(@resetSchedule=1,@scheduleDate,last_scheduled_date),updated_by=@by,updated_utc=@now
                """, connection);
            command.Parameters.AddWithValue("@schedule", input.ScheduleEnabled);
            command.Parameters.AddWithValue("@time", time);
            command.Parameters.AddWithValue("@maxAge", input.MaxAgeDays);
            command.Parameters.AddWithValue("@retention", input.RetentionEnabled);
            command.Parameters.AddWithValue("@days", input.RetentionDays);
            command.Parameters.AddWithValue("@weekdays", BackupScheduleDays.Valid(input.ScheduleDays) ? input.ScheduleDays : BackupScheduleDays.All);
            command.Parameters.AddWithValue("@ntpOn", input.TimeCheckEnabled);
            command.Parameters.AddWithValue("@ntp", string.Join(", ", BackupTimeRules.Parse(input.NtpServers)));
            // A new schedule (switched on, or another time) starts from now: a time still ahead today runs today, a time already past waits for tomorrow (no surprise backup at save).
            var newSchedule = input.ScheduleEnabled && (!old.ScheduleEnabled || old.ScheduleTime != time);
            var now = DateTime.Now;
            command.Parameters.AddWithValue("@resetSchedule", newSchedule ? 1 : 0);
            command.Parameters.AddWithValue("@scheduleDate", NasBackupRules.TryParseTime(time, out var at) && TimeOnly.FromDateTime(now) < at ? DBNull.Value : now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@by", actor);
            command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var changes = new[]
        {
            new AuditChange("Backup programat", old.ScheduleEnabled ? $"{BackupScheduleDays.Describe(old.ScheduleDays)} la {old.ScheduleTime}" : "oprit",
                input.ScheduleEnabled ? $"{BackupScheduleDays.Describe(BackupScheduleDays.Valid(input.ScheduleDays) ? input.ScheduleDays : BackupScheduleDays.All)} la {time}" : "oprit"),
            new AuditChange("Vechime maximă backup", $"{old.MaxAgeDays} zile", $"{input.MaxAgeDays} zile"),
            new AuditChange("Ștergere pachete vechi", old.RetentionEnabled ? $"după {old.RetentionDays} zile" : "oprită", input.RetentionEnabled ? $"după {input.RetentionDays} zile" : "oprită"),
            new AuditChange("Verificare oră pe internet", old.TimeCheckEnabled ? string.Join(", ", BackupTimeRules.Servers(old.NtpServers)) : "oprită",
                input.TimeCheckEnabled ? string.Join(", ", BackupTimeRules.Servers(input.NtpServers)) : "oprită")
        };
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.SetBackupSettings, "", "Setări backup",
            AuditDetails.Changes(changes), "", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimeProbeEntry>> ProbeTimeAsync(IReadOnlyList<string> servers, CancellationToken cancellationToken = default)
    {
        if (access is not null) await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (servers.Count is < 1 or > BackupTimeRules.MaxServers || servers.Any(server => BackupTimeRules.Validate(server) is not null)) throw new NasBackupException(BackupTimeRules.ServersMessage);
        return await TrustedClock.ProbeAsync(servers, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The outcome of a check of the server clock, for the notification "Ceas server decalat": a clock that is off is recorded (when and by how much), a clock
    /// that is right clears it; a check that could not be made changes nothing. Written by the system, never by a user.
    /// </summary>
    internal async Task RecordClockAsync(ClockCheck check, CancellationToken cancellationToken)
    {
        if (!check.Trusted && check.Skew is null) return;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (check.Trusted)
        {
            await using var clear = new MySqlCommand("UPDATE backup_settings SET clock_issue_utc=NULL,clock_skew_minutes=0 WHERE id=1 AND clock_issue_utc IS NOT NULL", connection);
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        await using var command = new MySqlCommand("""
            INSERT INTO backup_settings (id,clock_issue_utc,clock_skew_minutes) VALUES (1,@now,@minutes)
            ON DUPLICATE KEY UPDATE clock_issue_utc=IF(clock_issue_utc IS NULL OR clock_skew_minutes<>@minutes,@now,clock_issue_utc),clock_skew_minutes=@minutes
            """, connection);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@minutes", (int)Math.Clamp(Math.Round(check.Skew!.Value.TotalMinutes), int.MinValue + 1, int.MaxValue));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The last failed backup (cause and time) for the notification text; written by the backup flow, never by a user.</summary>
    internal async Task RecordBackupErrorAsync(string message, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO backup_settings (id,last_error_utc,last_error) VALUES (1,@now,@message)
            ON DUPLICATE KEY UPDATE last_error_utc=@now,last_error=@message
            """, connection);
        command.Parameters.AddWithValue("@now", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@message", message.Length > 480 ? message[..480] : message);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task MarkScheduledAsync(string date, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE backup_settings SET last_scheduled_date=@date WHERE id=1", connection);
        command.Parameters.AddWithValue("@date", date);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The automatic removal of old LOCAL packages (the NAS is never touched: packages there are removed by hand). With the NAS copy on, a package is removed only
/// when the share holds a copy of the same size, so the last copy of a package is never lost; when the share cannot be read nothing is removed.
/// </summary>
/// <summary>The clock check with the servers saved in Settings (the defaults when the settings cannot be read).</summary>
public static class BackupTimeCheck
{
    public static async Task<ClockCheck> RunAsync(IConfiguration configuration, CancellationToken cancellationToken, bool forBackup, TimeSpan? overall = null)
    {
        TimeCheckSettings settings;
        try
        {
            var (saved, _) = await new MariaBackupSettingsStore(configuration).ReadAsync(cancellationToken).ConfigureAwait(false);
            settings = new(saved.TimeCheckEnabled, BackupTimeRules.Servers(saved.NtpServers));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { settings = new(true, TrustedClock.DefaultServers); }
        return await TrustedClock.CheckAsync(configuration, settings, cancellationToken, forBackup, overall).ConfigureAwait(false);
    }
}

public static class BackupRetention
{
    /// <summary>
    /// The local packages the retention would remove now for this threshold (nothing is removed here). <c>Blocked</c> says why nothing is selected:
    /// the server clock cannot be trusted (checked against the internet time) or the NAS copy is on but the share cannot be read.
    /// </summary>
    public static async Task<RetentionSelection> SelectAsync(IConfiguration configuration, int retentionDays, bool nasCopyEnabled, INasBackupCopier copier,
        CancellationToken cancellationToken, Func<CancellationToken, Task<ClockCheck>>? clockCheck = null)
    {
        var directory = MariaAssetPaths.DatabaseBackups(configuration);
        var expired = BackupSettingsRules.Expired(await BackupPackageStore.ListAsync(directory, cancellationToken).ConfigureAwait(false), retentionDays, DateTime.UtcNow);
        if (expired.Count == 0) return new([], null);
        var clock = await (clockCheck?.Invoke(cancellationToken) ?? BackupTimeCheck.RunAsync(configuration, cancellationToken, forBackup: false)).ConfigureAwait(false);
        if (!clock.Trusted) return new([], clock.Message);
        if (!nasCopyEnabled) return new(expired, null);
        var share = await copier.ListShareAsync(cancellationToken).ConfigureAwait(false);
        if (!share.Ok) return new([], "NAS-ul nu a putut fi citit, deci ștergerea pachetelor vechi este amânată.");
        return new([.. expired.Where(package => share.Files.Any(file => file.FileName == package.FileName && file.SizeBytes == package.SizeBytes))], null);
    }

    public static async Task<RetentionResult> RunAsync(IConfiguration configuration, BackupSettings settings, bool nasCopyEnabled, INasBackupCopier copier, IAuditTrail? audit,
        ILogger logger, CancellationToken cancellationToken, IAccessControl? actor = null, Func<CancellationToken, Task<ClockCheck>>? clockCheck = null)
    {
        if (!settings.RetentionEnabled) return new(0, null);
        var directory = MariaAssetPaths.DatabaseBackups(configuration);
        var selection = await SelectAsync(configuration, settings.RetentionDays, nasCopyEnabled, copier, cancellationToken, clockCheck).ConfigureAwait(false);
        if (selection.Blocked is not null) { logger.LogWarning("Old backup packages were not removed: {Reason}", selection.Blocked); return new(0, selection.Blocked); }
        var expired = selection.Packages;
        var removed = 0;
        foreach (var package in expired)
        {
            try
            {
                File.Delete(Path.Combine(directory, package.FileName));
                try { File.Delete(Path.Combine(directory, package.FileName + ".sha256")); } catch (IOException) { }
                removed++;
                await AuditRecorder.RecordActionAsync(audit, actor ?? new SystemAccess(), AuditEntities.DatabaseBackup, AuditActions.RetentionDelete, package.FileName, $"Pachet: {package.FileName}",
                    AuditDetails.Identification(("Pachet", package.FileName), ("Creat", package.CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
                        ("Motiv", $"mai vechi de {settings.RetentionDays} zile (se păstrează ultimele {BackupSettingsRules.KeepNewest})"), ("Pe NAS", nasCopyEnabled ? "da, copia rămâne" : "NAS oprit")),
                    "", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { logger.LogWarning("An old backup package could not be removed ({ErrorType}).", exception.GetType().Name); }
        }
        return new(removed, null);
    }
}

public sealed record RetentionSelection(IReadOnlyList<BackupPackage> Packages, string? Blocked);

public sealed record RetentionResult(int Removed, string? Blocked);

public sealed record BackupRetentionPreview(IReadOnlyList<BackupPackage> Packages, string? Note);

/// <summary>The retention on demand, for the Backup settings tab: what a new threshold would remove, and the removal itself right after the settings are saved. Administrator only.</summary>
public interface IBackupRetentionService
{
    Task<BackupRetentionPreview> PreviewAsync(int retentionDays, CancellationToken cancellationToken = default);
    Task<RetentionResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class BackupRetentionService(IConfiguration configuration, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection, IAccessControl access,
    IAuditTrail? audit, INasBackupCopier copier, ILogger<BackupRetentionService> logger) : IBackupRetentionService
{
    public async Task<BackupRetentionPreview> PreviewAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var nasOn = (await new MariaNasBackupStore(configuration, protection).ReadAsync(cancellationToken).ConfigureAwait(false)).Settings.CopyEnabled;
        var packages = await BackupRetention.SelectAsync(configuration, retentionDays, nasOn, copier, cancellationToken).ConfigureAwait(false);
        return new(packages.Packages, packages.Blocked is null ? null : packages.Blocked + " Salvarea nu șterge nimic acum; ștergerea automată reîncearcă.");
    }

    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var (settings, _) = await new MariaBackupSettingsStore(configuration).ReadAsync(cancellationToken).ConfigureAwait(false);
        var nasOn = (await new MariaNasBackupStore(configuration, protection).ReadAsync(cancellationToken).ConfigureAwait(false)).Settings.CopyEnabled;
        return await BackupRetention.RunAsync(configuration, settings, nasOn, copier, audit, logger, cancellationToken, access).ConfigureAwait(false);
    }
}
