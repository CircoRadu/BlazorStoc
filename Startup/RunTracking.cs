using System.Globalization;
using System.Text.Json;
using BlazorStoc.Modules;
using BlazorStoc.Services;

namespace BlazorStoc.Startup;

// What the journal of activity learns about the application itself and its modules, written by the system:
//  - a module taken out of service after repeated failures, and its return (ModuleJournal, fed by ModuleRegistry.Subscribe);
//  - an unexpected stop of the application (RunMarker + RunTrackingService): a marker file is written at start, its time is refreshed every 30 s and it is deleted on a
//    normal stop; a marker found at the next start means the previous run was killed, crashed or lost with the machine. The supervisor (tools\supervisor.ps1) adds
//    why, when it knows (exit code, "not answering"), in a file beside the marker, and removes the marker itself when the stop was asked for.
// The journal is the database: while it does not answer (or a backup holds it) the entry is retried in the background, for a while, instead of being lost.

public static class JournalWithRetry
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(10);
    public const int DefaultAttempts = 30;

    /// <summary>Writes the entry; a failure is retried after <paramref name="delay"/> up to <paramref name="attempts"/> times, then logged and dropped. Never throws (except cancellation).</summary>
    public static async Task<bool> WriteAsync(IAuditTrail audit, AuditWrite entry, ILogger? logger, TimeSpan? delay = null, int attempts = DefaultAttempts, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await audit.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt == attempts)
                {
                    logger?.LogWarning("The journal entry \"{Action}\" could not be written after {Attempts} attempts ({ErrorType}).", entry.Action, attempts, exception.GetType().Name);
                    return false;
                }
                await Task.Delay(delay ?? DefaultDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        return false;
    }
}

/// <summary>Writes the changes of state of the modules to the journal of activity (user "sistem").</summary>
public sealed class ModuleJournal(ModuleRegistry registry, IAuditTrail audit, ILogger<ModuleJournal> logger)
{
    public const string SystemUser = "sistem";
    public const string SystemRole = "Sistem";
    public TimeSpan RetryDelay { get; init; } = JournalWithRetry.DefaultDelay;

    public void Start() => registry.Subscribe(change => _ = Task.Run(() => WriteAsync(change)));

    public static AuditWrite Entry(ModuleChange change)
    {
        var disabled = change.Kind == ModuleChangeKind.Disabled;
        var details = disabled
            ? AuditDetails.Identification(("Cauză", change.Origin), ("Tip eroare", string.IsNullOrEmpty(change.Reason) ? "-" : change.Reason))
            : AuditDetails.Identification(("Stare", "funcționează din nou"));
        return new AuditWrite(SystemUser, SystemRole, AuditEntities.Module, disabled ? AuditActions.ModuleDisabled : AuditActions.ModuleRecovered, change.Name, details, string.Empty, change.Id);
    }

    public Task<bool> WriteAsync(ModuleChange change) => JournalWithRetry.WriteAsync(audit, Entry(change), logger, RetryDelay);
}

public sealed record PreviousRun(DateTime? StartedUtc, DateTime? LastActivityUtc, int? ProcessId, string? ExitInfo);

/// <summary>The marker file of a run of the application (see the comment at the top of this file).</summary>
public sealed class RunMarker(string path, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private sealed record Data(DateTime StartedUtc, DateTime HeartbeatUtc, int ProcessId);
    private sealed record ExitData(int? ExitCode, string? Reason, DateTime? AtUtc);
    // The supervisor (PowerShell) writes its note in camelCase.
    private static readonly JsonSerializerOptions NoteOptions = new(JsonSerializerDefaults.Web);

    public string Path { get; } = path;
    public string ExitPath => Path + ".exit";

    /// <summary>The run that left its marker behind (null when the previous run ended normally, or there was none). Reading also takes the supervisor's note about it.</summary>
    public PreviousRun? ReadPrevious()
    {
        if (!File.Exists(Path)) return null;
        DateTime? started = null, last = null;
        int? processId = null;
        try
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path));
            if (data is not null) { started = data.StartedUtc; last = data.HeartbeatUtc; processId = data.ProcessId; }
        }
        catch (Exception exception) when (exception is JsonException or IOException) { }
        string? exitInfo = null;
        try
        {
            if (File.Exists(ExitPath))
            {
                var exit = JsonSerializer.Deserialize<ExitData>(File.ReadAllText(ExitPath), NoteOptions);
                if (exit is not null)
                    {
                    // The reason the supervisor gives usually already says the code ("exited with code -1"); it is added only when the reason does not.
                    var codeText = exit.ExitCode is { } code && !(exit.Reason?.Contains("code " + code.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ?? false) ? $"cod de ieșire {code}" : null;
                    exitInfo = string.Join(", ", new[] { exit.Reason, codeText }.Where(part => !string.IsNullOrEmpty(part)));
                }
                File.Delete(ExitPath);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { }
        return new PreviousRun(started, last, processId, string.IsNullOrEmpty(exitInfo) ? null : exitInfo);
    }

    public void Start() => Write(time.GetUtcNow().UtcDateTime, time.GetUtcNow().UtcDateTime);

    public void Heartbeat(DateTime startedUtc) => Write(startedUtc, time.GetUtcNow().UtcDateTime);

    public void Stop()
    {
        try { File.Delete(Path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    private void Write(DateTime started, DateTime heartbeat)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Data(started, heartbeat, Environment.ProcessId)));
            File.Move(temporary, Path, overwrite: true);
        }
        // Whatever is wrong with the place (no rights, a bad path, a full disk), the application keeps running; only the report of an unexpected stop is lost.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
    }

    public static string DefaultPath(IConfiguration configuration)
    {
        if (configuration["App:RunMarkerPath"] is { Length: > 0 } configured) return configured;
        var urls = configuration["ASPNETCORE_URLS"] ?? configuration["urls"] ?? "default";
        var name = new string([.. urls.Select(c => char.IsLetterOrDigit(c) ? c : '-')]).Trim('-');
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-MariaDB", "assets", "run-state", $"run-{name}.json");
    }
}

/// <summary>Keeps the marker alive while the application runs and reports an unexpected stop of the previous run to the journal.</summary>
public sealed class RunTrackingService(RunMarker marker, IAuditTrail audit, ILogger<RunTrackingService> logger, TimeProvider? clock = null) : BackgroundService
{
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public TimeSpan RetryDelay { get; init; } = JournalWithRetry.DefaultDelay;

    public static string Local(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static AuditWrite Entry(PreviousRun previous)
    {
        var parts = new List<(string, string)>();
        if (previous.LastActivityUtc is { } last) parts.Add(("Ultima activitate", Local(last)));
        if (previous.StartedUtc is { } started) parts.Add(("Pornită la", Local(started)));
        if (previous.ProcessId is { } processId) parts.Add(("Proces", processId.ToString(CultureInfo.InvariantCulture)));
        if (previous.ExitInfo is { } info) parts.Add(("Cauză", info));
        if (parts.Count == 0) parts.Add(("Detalii", "necunoscute (marcajul de rulare nu a putut fi citit)"));
        return new AuditWrite(ModuleJournal.SystemUser, ModuleJournal.SystemRole, AuditEntities.Application, AuditActions.ApplicationUnexpectedStop,
            "Aplicația BlazorStoc", AuditDetails.Identification([.. parts]), string.Empty, string.Empty);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var previous = marker.ReadPrevious();
        marker.Start();
        var started = time.GetUtcNow().UtcDateTime;
        if (previous is not null)
        {
            logger.LogWarning("The previous run of the application did not end normally (last activity {Last}).", previous.LastActivityUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "unknown");
            _ = Task.Run(() => JournalWithRetry.WriteAsync(audit, Entry(previous), logger, RetryDelay, cancellationToken: stoppingToken), stoppingToken);
        }
        using var timer = new PeriodicTimer(HeartbeatEvery);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) marker.Heartbeat(started); }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        marker.Stop();
    }
}

public static class RunTrackingRegistration
{
    public static IServiceCollection AddRunTracking(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new RunMarker(RunMarker.DefaultPath(configuration)));
        services.AddSingleton<ModuleJournal>();
        services.AddHostedService<RunTrackingService>();
        return services;
    }
}
