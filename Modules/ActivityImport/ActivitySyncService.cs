using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// Stage 4: the periodic synchronization. One synchronization is what the administrator does by hand, in order: apply the partners and their work points, apply the resolved tickets, then
// look for what was deleted in ActivityApp since it was imported. Deletions are only signalled (an open problem "sters in sursa"): nothing is ever deleted or changed in BlazorStoc because
// of them. The scheduled run is made by the system (no signed-in user) inside the time window of the settings, every N minutes; after a failure it waits longer each time (up to 4 hours).
// A source that does not answer is an expected thing: the failure is remembered and shown, and the notification for the administrator appears after three in a row.

public sealed record SyncResult(bool Ok, string Message, int PartnersCreated, int PointsCreated, int TicketsCreated, int Problems, int NewlyDeleted);

public static class ActivitySyncSchedule
{
    public const int FailuresBeforeAlert = 3;
    public const int MaximumBackoffMinutes = 240;

    /// <summary>The hour is in the window; a window that crosses midnight (22 to 6) is accepted; 0 to 24 is always.</summary>
    public static bool InWindow(int fromHour, int toHour, int hour) => fromHour < toHour ? hour >= fromHour && hour < toHour : hour >= fromHour || hour < toHour;

    /// <summary>How long after the last attempt the next one is made: the interval, doubled for each failure in a row, at most 4 hours (never less than the interval).</summary>
    public static TimeSpan Wait(int intervalMinutes, int failures) =>
        TimeSpan.FromMinutes(failures <= 0 ? intervalMinutes : Math.Max(intervalMinutes, Math.Min(intervalMinutes * Math.Pow(2, Math.Min(failures, 8)), MaximumBackoffMinutes)));

    public const string IntervalMode = "interval";
    public const string TimesMode = "times";
    public const int MaximumTimes = 24;

    public static string ModeLabel(string mode) => mode == TimesMode ? "la ore fixe" : "la interval";

    /// <summary>The fixed hours typed as "HH:mm" (also "H:mm", "H.mm" or just the hour), separated by comma, semicolon, space or new line; sorted, without repeats. The reason when one is not a time of day.</summary>
    public static IReadOnlyList<TimeOnly> ParseTimes(string? text, out string? error)
    {
        error = null;
        var times = new SortedSet<TimeOnly>();
        foreach (var part in (text ?? "").Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split([':', '.']);
            var minute = 0;
            if (pieces.Length is < 1 or > 2 || !int.TryParse(pieces[0], out var hour) || (pieces.Length == 2 && !int.TryParse(pieces[1], out minute)) || hour is < 0 or > 23 || minute is < 0 or > 59)
            {
                error = $"„{part}” nu este o oră validă (se scrie 08:30, între 00:00 și 23:59).";
                return [];
            }
            times.Add(new TimeOnly(hour, minute));
        }
        if (times.Count > MaximumTimes) { error = $"Cel mult {MaximumTimes} de ore fixe."; return []; }
        return [.. times];
    }

    public static string FormatTimes(IEnumerable<TimeOnly> times) => string.Join(", ", times.Select(time => time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>The latest fixed hour at or before now (today, else the last one of yesterday); null when there is none.</summary>
    public static DateTime? LatestTime(IReadOnlyList<TimeOnly> times, DateTime nowLocal)
    {
        if (times.Count == 0) return null;
        var today = times.Select(time => nowLocal.Date + time.ToTimeSpan()).Where(moment => moment <= nowLocal).ToList();
        return today.Count > 0 ? today.Max() : nowLocal.Date.AddDays(-1) + times[^1].ToTimeSpan();
    }

    /// <summary>The first fixed hour after now (today, else the first one of tomorrow); null when there is none.</summary>
    public static DateTime? NextTime(IReadOnlyList<TimeOnly> times, DateTime nowLocal)
    {
        if (times.Count == 0) return null;
        var later = times.Select(time => nowLocal.Date + time.ToTimeSpan()).Where(moment => moment > nowLocal).ToList();
        return later.Count > 0 ? later.Min() : nowLocal.Date.AddDays(1) + times[0].ToTimeSpan();
    }

    /// <summary>
    /// By interval: the time window is respected and the next run waits for the interval after the last attempt (longer after failures). At fixed hours: due when a fixed hour has passed since the last
    /// attempt (so one missed because the application was off is made when it is on again, once), whatever the time window: the hours chosen are the window.
    /// </summary>
    public static bool Due(ActivityImportSettings settings, DateTime nowLocal, DateTime nowUtc)
    {
        if (!settings.Enabled || !settings.SyncEnabled) return false;
        if (settings.SyncMode == TimesMode)
        {
            if (LatestTime(ParseTimes(settings.SyncTimes, out _), nowLocal) is not { } latest) return false;
            return settings.LastSyncUtc is null || nowLocal - (nowUtc - settings.LastSyncUtc.Value) < latest;
        }
        return InWindow(settings.WindowFromHour, settings.WindowToHour, nowLocal.Hour)
            && (settings.LastSyncUtc is null || nowUtc >= settings.LastSyncUtc.Value + Wait(settings.SyncIntervalMinutes, settings.SyncFailures));
    }

    /// <summary>When the next scheduled run is expected, for the panel (null when none is scheduled or it depends on a first run).</summary>
    public static DateTime? NextRun(ActivityImportSettings settings, DateTime nowLocal)
    {
        if (!settings.Enabled || !settings.SyncEnabled) return null;
        if (settings.SyncMode == TimesMode) return NextTime(ParseTimes(settings.SyncTimes, out _), nowLocal);
        return settings.LastSyncUtc is { } last ? last.ToLocalTime() + Wait(settings.SyncIntervalMinutes, settings.SyncFailures) : null;
    }
}

public interface IActivitySyncService
{
    /// <summary>"Rulează acum": one synchronization made now by the administrator (needs the import to be switched on; ignores the interval and the window).</summary>
    Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default);
    /// <summary>The scheduled check: runs a synchronization when one is due (see <see cref="ActivitySyncSchedule.Due"/>) and returns its result; null when none is due. Never throws for a source that fails.</summary>
    Task<SyncResult?> RunScheduledAsync(DateTime nowLocal, CancellationToken cancellationToken = default);
    /// <summary>"Recitește tot": the next synchronization reads every resolved ticket again (the incremental reading starts over). Journaled.</summary>
    Task ResetReadingAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaActivitySyncService(IConfiguration configuration, IActivityImportService import, IActivitySource source, IActivityPartnerImportService partners,
    IActivityTicketImportService tickets, IAccessControl access, IAuditTrail audit) : IActivitySyncService
{
    private const string Run = "setari-import-activityapp.run";
    private const int SuspiciousMinimum = 20;
    private static readonly TimeSpan PartnerPassInterval = TimeSpan.FromHours(6);
    private static DateTime lastPartnerPass = DateTime.MinValue;
    public static readonly string[] DeletedEntities = ["partner-deleted", "workpoint-deleted", "ticket-deleted"];

    public async Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled) throw new ActivityImportException("Preluarea este inactivă. Activeaz-o în setări înainte de sincronizare.");
        return await RunCoreAsync(settings, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncResult?> RunScheduledAsync(DateTime nowLocal, CancellationToken cancellationToken = default)
    {
        var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!ActivitySyncSchedule.Due(settings, nowLocal, DateTime.UtcNow)) return null;
        return await RunCoreAsync(settings, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResetReadingAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        await ActivityTicketStore.SetWatermarkAsync(configuration, null, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.ResetActivityWatermark, "1", "Recitire completă tichete ActivityApp",
            AuditDetails.Identification(("Citire până acum", settings.SourceWatermark is { } mark ? $"de la {mark:dd.MM.yyyy HH:mm}" : "completă"), ("De acum", "următoarea sincronizare citește toate tichetele")), "", cancellationToken).ConfigureAwait(false);
    }

    private async Task<SyncResult> RunCoreAsync(ActivityImportSettings before, bool manual, CancellationToken cancellationToken)
    {
        SyncResult result;
        try
        {
            // The tickets lead: the clients of the tickets read (and of those that wait) are taken as needed, verified in ANAF; there is no longer a pass over the whole table of partners.
            // Every new partner is taken first (companies and persons, with or without tickets; the clean ones are created, the others wait in the queue of the beneficiaries to verify and the
            // administrators are notified); the pass reads the whole table of partners, so a scheduled run makes it at most every few hours, a run by hand always.
            ImportReport? partnerReport = null;
            if (before.TakePartners && (manual || DateTime.UtcNow - lastPartnerPass >= PartnerPassInterval))
            {
                partnerReport = await partners.ApplyNewAsync(cancellationToken).ConfigureAwait(false);
                if (partnerReport is not null) lastPartnerPass = DateTime.UtcNow;
            }
            TicketReport? ticketReport = null;
            try { ticketReport = await tickets.ApplyAsync(null, cancellationToken).ConfigureAwait(false); }
            catch (ActivityImportException exception) when (exception.Message == ActivityImportRules.NoTypesMessage) { }
            var deleted = await DetectDeletedAsync(cancellationToken).ConfigureAwait(false);
            var problems = ticketReport?.Tickets.Problems ?? 0;
            var newBeneficiaries = (ticketReport?.NewBeneficiaries ?? 0) + (partnerReport?.Partners.Create ?? 0);
            var newPoints = (ticketReport?.NewPoints ?? 0) + (partnerReport?.Points.Create ?? 0);
            var toVerify = (await ActivityPartnerCheckStore.ReadAsync(configuration, PartnerCheckStatus.Pending, cancellationToken).ConfigureAwait(false)).Count;
            var message = $"Beneficiari: {newBeneficiaries} noi" + (toVerify > 0 ? $" ({toVerify} de verificat)" : "") + $"; puncte de lucru: {newPoints} noi; tichete: " +
                (ticketReport is null ? "niciun tip ales" : $"{ticketReport.Tickets.Create} preluate" + (ticketReport.Tickets.Waiting > 0 ? $", {ticketReport.Tickets.Waiting} în așteptare" : "")) + $"; probleme: {problems}; șterse în ActivityApp: {deleted.Total}" + (deleted.Note.Length > 0 ? $" ({deleted.Note})" : "") + ".";
            result = new SyncResult(true, message, newBeneficiaries, newPoints, ticketReport?.Tickets.Create ?? 0, problems, deleted.Newly);
        }
        catch (ActivityImportException exception) when (exception.Message.StartsWith("O altă rulare", StringComparison.Ordinal))
        {
            // Another run (by hand, or the scheduled one) is going on: not a failure of the source; the next check tries again.
            if (manual) throw;
            return new SyncResult(true, "O altă rulare era în desfășurare.", 0, 0, 0, 0, 0);
        }
        catch (Exception exception) when (exception is ActivityImportException or ActivitySourceException)
        {
            result = new SyncResult(false, exception.Message, 0, 0, 0, 0, 0);
        }
        catch (Exception exception) when (!manual && exception is not OperationCanceledException)
        {
            // A scheduled run never takes the application down: whatever went wrong is remembered like any failure (the details are in the application log of the caller).
            result = new SyncResult(false, "Sincronizarea a eșuat din cauza unei erori interne (" + exception.GetType().Name + ").", 0, 0, 0, 0, 0);
        }
        var failures = result.Ok ? 0 : before.SyncFailures + 1;
        await RecordAsync(result, failures).ConfigureAwait(false);
        await UpdateAlertsAsync(result.Ok, failures).ConfigureAwait(false);
        var changed = result.PartnersCreated + result.PointsCreated + result.TicketsCreated + result.NewlyDeleted > 0;
        // The journal gets a line when the synchronization did something, when it was asked for by hand, or when it started to fail (not for every quiet hour).
        if (manual || changed || (!result.Ok && before.LastSyncOk != false))
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, AuditActions.SyncActivityImport, "", manual ? "Sincronizare la cerere" : "Sincronizare periodică",
                AuditDetails.Identification(("Rezultat", result.Ok ? "reușită" : "eșuată"), ("Detalii", result.Message)), "", CancellationToken.None).ConfigureAwait(false);
        if (!result.Ok && manual) throw new ActivityImportException(result.Message);
        return result;
    }

    private async Task RecordAsync(SyncResult result, int failures)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, CancellationToken.None).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_settings SET last_sync_utc=@now, last_sync_ok=@ok, last_sync_message=@message, sync_failures=@failures WHERE id = 1", connection);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@ok", result.Ok);
        command.Parameters.AddWithValue("@message", result.Message.Length > 480 ? result.Message[..480] : result.Message);
        command.Parameters.AddWithValue("@failures", failures);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // The conditions the administrator is told about, each with the moment it began (kept while it lasts, cleared when it is gone).
    private async Task UpdateAlertsAsync(bool ok, int failures)
    {
        // Reading the types also checks (and stores) the structure of ActivityApp, so a change of it is noticed even when the synchronization itself stopped because of it.
        int? types = null;
        try
        {
            var view = await import.GetTypesAsync(CancellationToken.None).ConfigureAwait(false);
            if (view.SourceError is null) types = view.Items.Any(item => item.State is ActivityImportRules.StateNew or ActivityImportRules.StateChanged or ActivityImportRules.StateMissing) ? 1 : 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { types = null; }
        var settings = await import.GetSettingsAsync(CancellationToken.None).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, CancellationToken.None).ConfigureAwait(false);
        long openIssues;
        await using (var count = new MySqlCommand("SELECT COUNT(*) FROM activity_import_issues WHERE resolved = 0", connection)) openIssues = Convert.ToInt64(await count.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false));
        await using var command = new MySqlCommand("""
            UPDATE activity_import_settings SET
                alert_failing_since = CASE WHEN @failing = 1 THEN COALESCE(alert_failing_since, @now) ELSE NULL END,
                alert_structure_since = CASE WHEN @structure = 1 THEN COALESCE(alert_structure_since, @now) ELSE NULL END,
                alert_issues_since = CASE WHEN @issues = 1 THEN COALESCE(alert_issues_since, @now) ELSE NULL END,
                alert_types_since = CASE WHEN @types = 1 THEN COALESCE(alert_types_since, @now) WHEN @types = 0 THEN NULL ELSE alert_types_since END
            WHERE id = 1
            """, connection);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@failing", !ok && failures >= ActivitySyncSchedule.FailuresBeforeAlert ? 1 : 0);
        command.Parameters.AddWithValue("@structure", settings.StructureStatus == StructureStatus.Changed ? 1 : 0);
        command.Parameters.AddWithValue("@issues", openIssues > 0 ? 1 : 0);
        command.Parameters.AddWithValue("@types", types is null ? DBNull.Value : types.Value);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private sealed record Deleted(int Total, int Newly, string Note);

    /// <summary>
    /// What was imported and no longer exists (or is deleted) in ActivityApp is marked and listed as an open problem; nothing is changed or deleted in BlazorStoc. What the administrator
    /// marked resolved is not listed again. When ActivityApp answers that almost nothing of what was imported exists (an account without rights to the table, a wrong database) the kind is
    /// not marked: that is a doubt about the source, not thousands of deletions.
    /// </summary>
    private async Task<Deleted> DetectDeletedAsync(CancellationToken cancellationToken)
    {
        if (!await ImportStore.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return new Deleted(0, 0, "sincronizarea s-a oprit înaintea verificării");
        try
        {
            var connection = await import.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (connection is null) return new Deleted(0, 0, "");
            var structure = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false));
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            var notes = new List<string>();
            var total = 0;
            var newly = 0;
            var issues = new List<(string Entity, long SourceId, string Name, string Message)>();
            foreach (var (kind, entity, issueEntity) in new[] { (LiveKinds.Partners, ActivityPartnerPlanner.PartnerEntity, "partner-deleted"), (LiveKinds.WorkPoints, ActivityPartnerPlanner.PointEntity, "workpoint-deleted"), (LiveKinds.Tickets, ActivityTicketPlanner.TicketEntity, "ticket-deleted") })
            {
                var mine = entries.Where(entry => entry.Entity == entity).ToList();
                if (mine.Count == 0) continue;
                var block = kind switch { LiveKinds.Partners => ActivityContract.Partners, LiveKinds.WorkPoints => ActivityContract.WorkPoints, _ => ActivityContract.Tickets };
                if (structure.IsBlocked(block)) { notes.Add($"{entity}: structură schimbată"); continue; }
                var live = await source.ReadLiveIdsAsync(connection, structure, kind, [.. mine.Select(entry => entry.SourceId)], cancellationToken).ConfigureAwait(false);
                if (live.Count == 0 && mine.Count >= SuspiciousMinimum) { notes.Add($"{entity}: toate par șterse, verificare omisă"); continue; }
                foreach (var entry in mine)
                {
                    var gone = !live.Contains(entry.SourceId);
                    if (gone != entry.Deleted) { await SetDeletedAsync(entity, entry.SourceId, gone, cancellationToken).ConfigureAwait(false); if (gone) newly++; }
                    if (gone) { total++; issues.Add((issueEntity, entry.SourceId, $"id local {entry.LocalId}", $"Șters în ActivityApp după preluare. La noi rămâne neschimbat (id local {entry.LocalId}); ștergerea la noi este o acțiune manuală.")); }
                }
            }
            await ReplaceDeletedIssuesAsync(issues, cancellationToken).ConfigureAwait(false);
            return new Deleted(total, newly, string.Join("; ", notes));
        }
        finally { ImportStore.Gate.Release(); }
    }

    private sealed record Entry(string Entity, long SourceId, int LocalId, bool Deleted);

    private async Task<List<Entry>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT entity, source_id, local_id, source_deleted FROM activity_import_map", connection);
        var result = new List<Entry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(new Entry(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetBoolean(3)));
        return result;
    }

    private async Task SetDeletedAsync(string entity, long sourceId, bool deleted, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_map SET source_deleted=@deleted, source_deleted_utc=@at WHERE entity=@entity AND source_id=@source", connection);
        command.Parameters.AddWithValue("@deleted", deleted);
        command.Parameters.AddWithValue("@at", deleted ? DateTime.UtcNow : DBNull.Value);
        command.Parameters.AddWithValue("@entity", entity);
        command.Parameters.AddWithValue("@source", sourceId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceDeletedIssuesAsync(IReadOnlyList<(string Entity, long SourceId, string Name, string Message)> issues, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var resolved = new HashSet<(string, long)>();
        await using (var read = new MySqlCommand("SELECT entity, source_id FROM activity_import_issues WHERE resolved = 1 AND entity IN ('partner-deleted', 'workpoint-deleted', 'ticket-deleted')", connection))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) resolved.Add((reader.GetString(0), reader.GetInt64(1)));
        var fresh = issues.Where(issue => !resolved.Contains((issue.Entity, issue.SourceId))).ToList();
        await ImportStore.RecordIssuesAsync(configuration, 0, DeletedEntities, fresh, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class GuardedActivitySyncService(IActivitySyncService inner, ModuleRegistry modules) : IActivitySyncService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RunNowAsync(cancellationToken));
    public Task<SyncResult?> RunScheduledAsync(DateTime nowLocal, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RunScheduledAsync(nowLocal, cancellationToken));
    public Task ResetReadingAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ResetReadingAsync(cancellationToken));
}

internal sealed class UnavailableActivitySyncService : IActivitySyncService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default) => Out<SyncResult>();
    public Task<SyncResult?> RunScheduledAsync(DateTime nowLocal, CancellationToken cancellationToken = default) => Out<SyncResult?>();
    public Task ResetReadingAsync(CancellationToken cancellationToken = default) => Out<object>();
}
