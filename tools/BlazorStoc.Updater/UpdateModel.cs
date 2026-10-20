namespace BlazorStoc.Updater;

/// <summary>What the updater needs from the running application and its supervisor. The real one drives tools/supervisor.ps1 (<see cref="SupervisorHost"/>); the checks use a fake.</summary>
public interface IApplicationHost
{
    /// <summary>Stops the supervisor and the application and returns when nothing answers any more (the files of the version are free).</summary>
    Task StopAsync(CancellationToken cancellationToken);
    /// <summary>Starts the supervisor, that starts the version in current.txt.</summary>
    Task StartAsync(CancellationToken cancellationToken);
    /// <summary>What /health answers now (not reachable = no answer).</summary>
    Task<HealthAnswer> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>The answer of /health: Reachable (something answered), StatusCode (200 = process and database are well), Version (what the application says it is).</summary>
public sealed record HealthAnswer(bool Reachable, int StatusCode, string? Version)
{
    public static readonly HealthAnswer None = new(false, 0, null);
    public bool IsWell(string version) => Reachable && StatusCode == 200 && Version == version;
}

/// <summary>
/// What the updater asks of an installed version, through that version's own BlazorStoc.dll (so the code that does it is the code that goes with that schema): the state of the
/// schema, a backup, the migrations, a restoration. The real one runs the processes (<see cref="ProcessReleaseTools"/>).
/// </summary>
public interface IReleaseTools
{
    /// <summary>"--schema-status" of that version; null when it could not be read (no database, an old version without the option).</summary>
    Task<SchemaStatus?> SchemaStatusAsync(string appFolder, CancellationToken cancellationToken);
    /// <summary>A backup of the database before the update; returns the file name of the package. Throws <see cref="UpdateStepException"/> when it could not be made.</summary>
    Task<string> BackupAsync(string appFolder, string actor, CancellationToken cancellationToken);
    /// <summary>"--migrate-schema" of that version with the migrator account; throws <see cref="UpdateStepException"/> when the schema is not current afterwards.</summary>
    Task MigrateAsync(string appFolder, CancellationToken cancellationToken);
    /// <summary>Restores the database from the package of a backup (the restoration of the application, which first makes its own safety copy).</summary>
    Task RestoreAsync(string appFolder, string packageFileName, string actor, CancellationToken cancellationToken);
}

/// <summary>What "--schema-status" printed: the highest migration in the ledger of the database, the highest of the code, and how many migrations a run would apply now.</summary>
public sealed record SchemaStatus(int LedgerVersion, int CodeVersion, bool LedgerPresent, int Pending, IReadOnlyList<string> MissingColumns, IReadOnlyList<string> Drifted);

public interface IUpdateLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>A step of the update failed (the message is for the administrator). Before the switch it ends the update with nothing changed; after it, it starts the rollback.</summary>
public sealed class UpdateStepException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The package or the situation is refused before anything is changed (an untrusted signer, an older version, no room on the disk...).</summary>
public sealed class UpdateRefusedException(string message) : Exception(message);

public enum UpdateOutcome
{
    /// <summary>The new version runs and answers.</summary>
    Succeeded,
    /// <summary>Refused before anything was changed; the old version was never stopped.</summary>
    Refused,
    /// <summary>The update failed after it began; the old version was put back and answers.</summary>
    RolledBack,
    /// <summary>The update failed and the old version could not be put back or does not answer: the application may be down and needs an administrator.</summary>
    Failed
}

public sealed record UpdateResult(UpdateOutcome Outcome, string? From, string To, string? Backup, string Message);

/// <summary>What an administrator asked for. Actor and Reason go to the log and to the history (the application writes them to its journal at the next start).</summary>
public sealed record InstallRequest(string PackagePath, string Actor, string Reason, bool Reinstall = false, bool AllowDowngrade = false,
    string? TrustFingerprint = null, Func<string, bool>? ConfirmUnknownSigner = null, string Via = UpdateVia.Installer, string Kind = UpdateKind.Install, string? PackageSha256 = null);

/// <summary>The written state of an update (state/update-status.json), for the application to show and for an administrator to read after a crash. Phase "done" = finished.</summary>
public sealed record UpdateStatus(string Phase, string? From, string To, string Actor, string Reason, DateTime StartedUtc, DateTime UpdatedUtc, string? Outcome, string Message, string? Backup);

/// <summary>One line of state/update-history.jsonl.</summary>
public sealed record UpdateHistoryEntry(DateTime AtUtc, string? From, string To, string Outcome, string Actor, string Reason, string? Signer, string? Backup, double Seconds, string Message,
    string? Package = null, string Via = UpdateVia.Installer, string Kind = UpdateKind.Install);

/// <summary>Where an update was asked from: the page Setari -> Actualizare of the application (through the request files) or the installer an administrator runs on the server.</summary>
public static class UpdateVia
{
    public const string Application = "aplicatie";
    public const string Installer = "installer";
}

/// <summary>What an entry of the history is: the installation of a package or the return to the earlier version (by hand).</summary>
public static class UpdateKind
{
    public const string Install = "install";
    public const string Rollback = "rollback";
}
