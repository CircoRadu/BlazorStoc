using MySqlConnector;

namespace BlazorStoc.Services;

// Two notifications that watch the backups (Setari -> Notificari, like every other source: the administrator creates a template for each):
//  - "Backup lipsa": no backup package was made for BackupMaxAgeDays; one notification, which expires on the date of the last package plus that many days
//    (a fresh backup moves the date, so the engine closes the notification by itself).
//  - "Copie pe NAS lipsa": the copy to the NAS is on, and the last attempt failed or is older than BackupMaxAgeDays. Closed by the next successful copy.

public sealed record BackupAlertState(DateTime? LastBackupUtc, DateTime FirstUseUtc, bool NasCopyEnabled, DateTime? NasLastAttemptUtc, bool? NasLastOk, DateTime? NasConfiguredUtc,
    int MaxAgeDays = BackupAlertRules.BackupMaxAgeDays, DateTime? LastErrorUtc = null, string LastError = "", string NasLastMessage = "",
    DateTime? ClockIssueUtc = null, int ClockSkewMinutes = 0);

public interface IBackupAlertReader
{
    Task<BackupAlertState> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaBackupAlertReader(IConfiguration configuration) : IBackupAlertReader
{
    public async Task<BackupAlertState> GetAsync(CancellationToken cancellationToken = default)
    {
        // The last package is the newest .zip of the backup folder (a temporary file of a running backup starts with ".tmp-").
        var directory = MariaAssetPaths.DatabaseBackups(configuration);
        DateTime? last = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.zip").Where(file => !Path.GetFileName(file).StartsWith(".tmp-", StringComparison.Ordinal))
                .Select(file => (DateTime?)File.GetLastWriteTimeUtc(file)).OrderByDescending(time => time).FirstOrDefault()
            : null;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // Without any backup yet the clock starts at the first recorded event (a stable date, not "today", which would move every day).
        await using var first = new MySqlCommand("SELECT MIN(timestamp_utc) FROM audit_events", connection);
        var firstText = await first.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        var firstUse = firstText is null ? DateTime.UtcNow : MariaTimeText.Parse(firstText);
        int maxAge = BackupAlertRules.BackupMaxAgeDays, skewMinutes = 0; DateTime? errorAt = null, clockIssue = null; var error = "";
        await using (var own = new MySqlCommand("SELECT max_age_days,last_error_utc,last_error,clock_issue_utc,clock_skew_minutes,schedule_enabled,schedule_days FROM backup_settings WHERE id=1", connection))
        await using (var ownReader = await own.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            if (await ownReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                maxAge = ownReader.GetInt32(0);
                errorAt = ownReader.IsDBNull(1) ? null : MariaTimeText.Parse(ownReader.GetString(1));
                error = ownReader.IsDBNull(2) ? "" : ownReader.GetString(2);
                clockIssue = ownReader.IsDBNull(3) ? null : MariaTimeText.Parse(ownReader.GetString(3));
                skewMinutes = ownReader.GetInt32(4);
                maxAge = BackupAlertRules.EffectiveMaxAge(maxAge, ownReader.GetBoolean(5), ownReader.GetInt32(6));
            }
        await using var nas = new MySqlCommand("SELECT copy_enabled,last_attempt_utc,last_ok,updated_utc,last_message FROM backup_nas_settings WHERE id=1", connection);
        await using var reader = await nas.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(last, firstUse, false, null, null, null, maxAge, errorAt, error, "", clockIssue, skewMinutes);
        return new(last, firstUse, reader.GetBoolean(0), reader.IsDBNull(1) ? null : MariaTimeText.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetBoolean(2), MariaTimeText.Parse(reader.GetString(3)), maxAge, errorAt, error, reader.IsDBNull(4) ? "" : reader.GetString(4), clockIssue, skewMinutes);
    }
}

public static class BackupAlertRules
{
    /// <summary>Days after which a backup (or a copy to the NAS) is overdue.</summary>
    public const int BackupMaxAgeDays = 2;

    /// <summary>With a scheduled backup on some days only, a backup is not overdue before the longest stretch between two of them has passed (plus a day).</summary>
    public static int EffectiveMaxAge(int maxAgeDays, bool scheduleEnabled, int scheduleDays)
    {
        if (!scheduleEnabled || !BackupScheduleDays.Valid(scheduleDays)) return maxAgeDays;
        var gap = BackupScheduleDays.LongestGap(scheduleDays);
        return gap <= 1 ? maxAgeDays : Math.Max(maxAgeDays, gap + 1);
    }

    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc.ToLocalTime());

    /// <summary>The date the last backup expires on: its date plus the maximum age; with no backup at all, the first day of use plus it.</summary>
    public static DateOnly BackupExpiry(BackupAlertState state) => LocalDate(state.LastBackupUtc ?? state.FirstUseUtc).AddDays(state.MaxAgeDays);

    /// <summary>The date the NAS copy expires on, or null when the copy is off. A failed last attempt is overdue from the day it failed.</summary>
    public static DateOnly? NasExpiry(BackupAlertState state)
    {
        if (!state.NasCopyEnabled) return null;
        var attempt = state.NasLastAttemptUtc ?? state.NasConfiguredUtc ?? state.FirstUseUtc;
        return state.NasLastOk == false ? LocalDate(attempt) : LocalDate(attempt).AddDays(state.MaxAgeDays);
    }

    /// <summary>"date hour — cause" of the last failed backup, or a plain statement when none is recorded.</summary>
    public static string ErrorText(BackupAlertState state) =>
        state.LastErrorUtc is null ? "nicio eroare înregistrată" : $"{Describe(state.LastErrorUtc)} — {(state.LastError.Length > 0 ? state.LastError : "cauză necunoscută")}";

    public static string Describe(DateTime? utc) => utc is null ? "niciunul" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class BackupMissingSource(IBackupAlertReader reader, string key = ExpirySourceKeys.BackupMissing) : IExpirySource
{
    public const string LastName = "ultimul backup";
    public const string ErrorName = "ultima eroare backup";
    public const string AgeName = "vechime maxima";
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Backup lipsă";
    public string DateLabel => "termenului de backup";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "A fost făcut un backup nou.";
    public string DefaultSubject => "Backup lipsă – ultimul: <ultimul backup>";
    public string DefaultBody =>
        "Nu s-a făcut niciun backup de peste <vechime maxima> zile. Ultimul backup: <ultimul backup>. Ultima eroare: <ultima eroare backup>. " +
        "Verifică Setări → Backup și programarea zilnică.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(LastName, "Data și ora ultimului backup", "08.10.2026 02:00"),
        new(ErrorName, "Data și cauza ultimei erori de backup", "08.10.2026 02:00 — mariadb-dump nu a fost găsit"),
        new(AgeName, "Vechimea maximă acceptată (zile, din Setări → Backup)", "2")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"S-a făcut un backup nou: termenul s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static IReadOnlyDictionary<string, string> Values(BackupAlertState state) => new Dictionary<string, string>
    {
        [LastName] = BackupAlertRules.Describe(state.LastBackupUtc),
        [ErrorName] = BackupAlertRules.ErrorText(state),
        [AgeName] = state.MaxAgeDays.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>The notification as it reads now (the default subject and text with the real values): the read-only model shown in Settings.</summary>
    public static (string Subject, string Body) Preview(BackupAlertState state) =>
        (Fill(new BackupMissingSource(null!).DefaultSubject, Values(state)), Fill(new BackupMissingSource(null!).DefaultBody, Values(state)));

    internal static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        values.Aggregate(text, (current, pair) => current.Replace("<" + pair.Key + ">", pair.Value, StringComparison.Ordinal));

    // The local backup is watched whether or not a NAS is used: a missing or failed backup raises it; the problems of the copy on the NAS have their own notification.
    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var state = await reader.GetAsync(cancellationToken).ConfigureAwait(false);
        return [new(1, "Backup-ul bazei de date", BackupAlertRules.BackupExpiry(state), Values(state), "/setari?tab=backup")];
    }
}

public sealed class NasCopyMissingSource(IBackupAlertReader reader, string key = ExpirySourceKeys.NasCopyMissing) : IExpirySource
{
    public const string AttemptName = "ultima copiere";
    public const string CauseName = "cauza copiere";
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Copie pe NAS lipsă";
    public string DateLabel => "termenului de copiere pe NAS";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Copierea pe NAS a reușit sau a fost oprită.";
    public string DefaultSubject => "Copie pe NAS lipsă";
    public string DefaultBody => "Backup-ul nu a ajuns pe NAS. Ultima încercare: <ultima copiere>. Cauza: <cauza copiere>. Verifică Setări → Backup → Backup NAS: conexiunea, contul și spațiul de pe NAS.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(AttemptName, "Data și ora ultimei încercări de copiere", "08.10.2026 02:00"),
        new(CauseName, "Rezultatul ultimei copieri (cauza eșecului)", "Contul sau parola sunt greșite.")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"S-a făcut o încercare nouă de copiere: termenul s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var state = await reader.GetAsync(cancellationToken).ConfigureAwait(false);
        return BackupAlertRules.NasExpiry(state) is { } expiry
            ? [new(1, "Copia backup-urilor pe NAS", expiry, Values(state), "/setari?tab=backup&subtab=backup-nas")]
            : [];
    }

    public static IReadOnlyDictionary<string, string> Values(BackupAlertState state) => new Dictionary<string, string>
    {
        [AttemptName] = BackupAlertRules.Describe(state.NasLastAttemptUtc),
        [CauseName] = state.NasLastMessage.Length > 0 ? state.NasLastMessage : "necunoscută"
    };

    /// <summary>The notification as it reads now (the default subject and text with the real values): the read-only model shown in Settings.</summary>
    public static (string Subject, string Body) Preview(BackupAlertState state)
    {
        var source = new NasCopyMissingSource(null!);
        return (BackupMissingSource.Fill(source.DefaultSubject, Values(state)), BackupMissingSource.Fill(source.DefaultBody, Values(state)));
    }
}

/// <summary>
/// "Ceas server decalat": the server clock differs from the internet time by more than the tolerance for backups (checked by the daily backup, by every backup and every
/// ten minutes). Open from the day it is found until a check finds the clock right again; meanwhile backups take the internet time.
/// </summary>
public sealed class ClockSkewSource(IBackupAlertReader reader, string key = ExpirySourceKeys.ClockSkew) : IExpirySource
{
    public const string SkewName = "decalaj ceas";
    public const string FoundName = "constatat ceas";
    public string Key => key;
    public string Category => "Sistem";
    public string EventName => "Ceas server decalat";
    public string DateLabel => "constatării decalajului";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Ceasul serverului a fost corectat.";
    public string DefaultSubject => "Ceasul serverului este decalat";
    public string DefaultBody =>
        "Ceasul serverului este <decalaj ceas> față de ora de pe internet (constatat: <constatat ceas>). Backup-urile iau ora de pe internet, iar ștergerea automată a " +
        "pachetelor vechi este oprită până la corectare. Verifică data și ora serverului.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(SkewName, "Cu cât diferă ceasul serverului de ora de pe internet", "cu 30 zile înainte"),
        new(FoundName, "Data și ora constatării", "08.10.2026 02:00")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Decalajul a fost constatat din nou: data s-a mutat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    /// <summary>"cu 30 zile înainte" / "cu 2 ore în urmă" (minutes: server minus internet).</summary>
    public static string Describe(int skewMinutes)
    {
        var size = Math.Abs((long)skewMinutes);
        var amount = size >= 1440 ? $"{size / 1440} {(size / 1440 == 1 ? "zi" : "zile")}" : size >= 60 ? $"{size / 60} {(size / 60 == 1 ? "oră" : "ore")}" : $"{size} min";
        return $"cu {amount} {(skewMinutes >= 0 ? "înainte" : "în urmă")}";
    }

    public static IReadOnlyDictionary<string, string> Values(BackupAlertState state) => new Dictionary<string, string>
    {
        [SkewName] = Describe(state.ClockSkewMinutes),
        [FoundName] = BackupAlertRules.Describe(state.ClockIssueUtc)
    };

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var state = await reader.GetAsync(cancellationToken).ConfigureAwait(false);
        return state.ClockIssueUtc is { } found ? [new(1, "Ceasul serverului", BackupAlertRules.LocalDate(found), Values(state), "/setari?tab=backup&subtab=backup-time")] : [];
    }
}
