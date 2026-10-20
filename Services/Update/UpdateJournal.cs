using System.Globalization;
using System.Text.Json;
using BlazorStoc.Startup;

namespace BlazorStoc.Services.Update;

/// <summary>
/// What the updater did, written to the journal of activity. The updater cannot write to the database (it has no account for it) and the application is the one that stops while an
/// update runs, so the outcome is read back from state\update-history.jsonl: at every start of the application (<see cref="UpdateJournalService"/>) and right after an update that
/// was refused or ended while the application stayed up. A small marker in the data root remembers the last entry written, so each entry is journaled exactly once, also when it was made
/// by the installer on the server (then nobody was signed in to the application). The moment an administrator CONFIRMS an update from the page is journaled by the page under his name.
/// </summary>
public sealed class UpdateJournal(UpdateInstallation installation, IAuditTrail audit, string stateFile, ILogger<UpdateJournal> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public TimeSpan RetryDelay { get; init; } = JournalWithRetry.DefaultDelay;
    public int Attempts { get; init; } = JournalWithRetry.DefaultAttempts;

    private sealed record Marker(DateTime? LastJournaledUtc, DateTime? AcknowledgedUtc);

    public static string DefaultStateFile(IConfiguration configuration) => AppDataRoot.Combine(configuration, "update-journal.json");

    /// <summary>The entry of the journal for one line of the history of updates.</summary>
    public static AuditWrite Entry(UpdateHistoryDocument entry)
    {
        var rollback = entry.Kind == "rollback";
        var action = entry.Outcome switch
        {
            "Succeeded" => entry.Via == "installer" ? AuditActions.ApplicationUpdateInstaller : AuditActions.ApplicationUpdateSucceeded,
            "RolledBack" => rollback ? AuditActions.ApplicationRollback : AuditActions.ApplicationUpdateFailed,
            "Refused" => AuditActions.ApplicationUpdateRefused,
            _ => AuditActions.ApplicationUpdateFailed
        };
        var parts = new List<(string, string)>
        {
            ("De la", entry.From ?? "-"), ("La", entry.To == "?" ? "-" : entry.To), ("Cum", entry.Via == "installer" ? "installer pe server" : "din aplicație"),
            ("Efectuat de", entry.Actor), ("La data", Local(entry.AtUtc)), ("Durată", $"{entry.Seconds.ToString("0", CultureInfo.InvariantCulture)} s"), ("Rezultat", entry.Message)
        };
        if (!string.IsNullOrEmpty(entry.Package)) parts.Add(("Pachet SHA-256", entry.Package));
        if (!string.IsNullOrEmpty(entry.Signer)) parts.Add(("Semnat de", entry.Signer.Length > 16 ? entry.Signer[..16] : entry.Signer));
        if (!string.IsNullOrEmpty(entry.Backup)) parts.Add(("Backup înainte de actualizare", entry.Backup));
        var reason = entry.Reason.Length > ChangeReasonRules.MaximumLength ? entry.Reason[..ChangeReasonRules.MaximumLength] : entry.Reason;
        return new AuditWrite(ModuleJournal.SystemUser, ModuleJournal.SystemRole, AuditEntities.Application, action, "Aplicația BlazorStoc", AuditDetails.Identification([.. parts]), reason);
    }

    /// <summary>Writes the entries of the history that are not in the journal yet. Returns how many were written; stops at the first one the journal does not accept (the next call goes on from there).</summary>
    public async Task<int> WriteNewAsync(CancellationToken cancellationToken = default)
    {
        if (!installation.IsManaged) return 0;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var marker = ReadMarker();
            var written = 0;
            foreach (var entry in installation.AllHistory().Where(entry => marker.LastJournaledUtc is not { } last || entry.AtUtc > last))
            {
                if (!await JournalWithRetry.WriteAsync(audit, Entry(entry), logger, RetryDelay, Attempts, cancellationToken).ConfigureAwait(false)) break;
                marker = marker with { LastJournaledUtc = entry.AtUtc };
                WriteMarker(marker);
                written++;
            }
            return written;
        }
        finally { gate.Release(); }
    }

    /// <summary>The updates that ended badly and that no administrator has taken note of yet: the last entry of the history when it is a failure newer than the acknowledgement.</summary>
    public UpdateHistoryDocument? UnacknowledgedFailure()
    {
        if (!installation.IsManaged) return null;
        var last = installation.History(1).FirstOrDefault();
        if (last is null || !IsFailure(last)) return null;
        return ReadMarker().AcknowledgedUtc is { } acknowledged && last.AtUtc <= acknowledged ? null : last;
    }

    /// <summary>The administrator took note of the failure (journaled by the page); the banner goes away.</summary>
    public void Acknowledge(DateTime atUtc)
    {
        var marker = ReadMarker();
        WriteMarker(marker with { AcknowledgedUtc = atUtc });
    }

    /// <summary>A rolled-back update, a failed one, or a manual rollback that failed: the cases that leave the application on another version than the administrator asked, or down.</summary>
    public static bool IsFailure(UpdateHistoryDocument entry) =>
        entry.Kind == "rollback" ? entry.Outcome == "Failed" : entry.Outcome is "RolledBack" or "Failed";

    public static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private Marker ReadMarker()
    {
        try { return File.Exists(stateFile) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(stateFile), UpdateJson.Options) ?? new Marker(null, null) : new Marker(null, null); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new Marker(null, null); }
    }

    private void WriteMarker(Marker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        var temporary = stateFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(marker, UpdateJson.Options));
        File.Move(temporary, stateFile, overwrite: true);
    }
}

/// <summary>
/// At every start of the application: the outcome of an update that happened while it was stopped (or by the installer) goes to the journal. The update that brought THIS version is
/// still running when the application starts (the updater writes its history line only after this version answered /health), so while the state says it is not done the history is
/// looked at again every few seconds, up to a quarter of an hour.
/// </summary>
public sealed class UpdateJournalService(UpdateJournal journal, UpdateInstallation installation, ILogger<UpdateJournalService> logger) : BackgroundService
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan WaitLimit { get; init; } = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await journal.WriteNewAsync(stoppingToken).ConfigureAwait(false);
            if (!installation.IsManaged) return;
            var waited = false;
            var deadline = DateTime.UtcNow + WaitLimit;
            while (installation.Status() is { Phase: not "done" } && DateTime.UtcNow < deadline)
            {
                waited = true;
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            if (waited) await journal.WriteNewAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("The updates could not be written to the journal: {Message}", exception.Message);
        }
    }
}
