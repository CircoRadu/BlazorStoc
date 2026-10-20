using System.Diagnostics;
using System.Text.Json;

namespace BlazorStoc.Services.Update;

/// <summary>Starts the updater: the application cannot change its own installation, so it starts the scheduled task the updater registered (with the rights of an administrator) after it left a request in updates\staged.</summary>
public interface IUpdateLauncher
{
    /// <summary>Returns null when the task was started, else the reason it could not be (the administrator is told to use the installer).</summary>
    Task<string?> StartAsync(CancellationToken cancellationToken);
}

/// <summary>"schtasks /Run /TN &lt;task&gt;": the task is run by the scheduler, with its own (elevated) token, not by this process.</summary>
public sealed class ScheduledTaskLauncher(UpdateInstallation installation) : IUpdateLauncher
{
    public async Task<string?> StartAsync(CancellationToken cancellationToken)
    {
        if (!installation.IsManaged) return "Aplicația nu rulează dintr-o instalare făcută de Updater.";
        var start = new ProcessStartInfo("schtasks.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/Run", "/TN", installation.TaskName }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("schtasks.exe nu a putut fi pornit.");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var text = (await output.ConfigureAwait(false) + await error.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 ? null : $"Sarcina programată {installation.TaskName} nu a putut fi pornită ({text}). Rulează „BlazorStoc.Updater init” ca administrator, cu contul sub care rulează aplicația.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Sarcina programată a Updater-ului nu a putut fi pornită: " + exception.Message;
        }
    }
}

/// <summary>An update that was confirmed and is waiting for its hour (Launched = false) or is running (the application is about to stop, or already did).</summary>
public sealed record UpdateAnnouncement(string Id, string From, string To, DateTime StartsUtc, string RequestedBy, bool Launched);

/// <summary>
/// The one update of the application that may be waiting or running (a singleton shared by every session). Confirming an update does not stop the application at once: every user is told
/// (the banner reads <see cref="Current"/>) and the updater is asked only after the notice time. Until then the administrator may still cancel. After it was started, this object watches
/// state\update-status.json: when the updater is done and the application is still here (the update was refused, or it was rolled back), the announcement goes away and the outcome is
/// written to the journal; when the update worked, the application is replaced and the new one journals the outcome at its start.
/// </summary>
public sealed class UpdateCoordinator(UpdateInstallation installation, IUpdateLauncher launcher, UpdateJournal journal, TimeProvider clock, ILogger<UpdateCoordinator> logger)
{
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;

    public static readonly TimeSpan DefaultNotice = TimeSpan.FromSeconds(60);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan WatchLimit { get; init; } = TimeSpan.FromMinutes(45);

    public UpdateAnnouncement? Current { get; private set; }
    /// <summary>Why the updater could not be started the last time (shown on the page).</summary>
    public string? LastError { get; private set; }
    public event Action? Changed;

    /// <summary>Takes the update if none is waiting or running. The request file is written only when the notice time has passed.</summary>
    public bool TrySchedule(UpdateRequestDocument request, string from, string to, TimeSpan notice)
    {
        lock (sync)
        {
            if (Current is not null) return false;
            var startsUtc = clock.GetUtcNow().UtcDateTime + notice;
            var announcement = new UpdateAnnouncement(request.Id, from, to, startsUtc, request.Actor, false);
            Current = announcement;
            LastError = null;
            var source = new CancellationTokenSource();
            cancellation = source;
            _ = Task.Run(() => RunAsync(announcement, request, source.Token));
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Tells every open session to look again (a failure was acknowledged: the banner of the other administrators goes away too).</summary>
    public void Notify() => Changed?.Invoke();

    /// <summary>Gives up while the notice time runs. False when there was nothing to cancel or the updater was already asked.</summary>
    public bool Cancel()
    {
        lock (sync)
        {
            if (Current is not { Launched: false }) return false;
            cancellation?.Cancel();
            Current = null;
        }
        Changed?.Invoke();
        return true;
    }

    private async Task RunAsync(UpdateAnnouncement announcement, UpdateRequestDocument request, CancellationToken cancellationToken)
    {
        try
        {
            var wait = announcement.StartsUtc - clock.GetUtcNow().UtcDateTime;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, cancellationToken).ConfigureAwait(false);

            var launchedAt = clock.GetUtcNow().UtcDateTime;
            Directory.CreateDirectory(installation.StagedFolder);
            var path = installation.RequestPath(request.Id);
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(request with { RequestedUtc = launchedAt }, UpdateJson.Options), CancellationToken.None).ConfigureAwait(false);
            File.Move(path + ".tmp", path, overwrite: true);
            lock (sync) { if (Current?.Id == announcement.Id) Current = announcement with { Launched = true }; }
            Changed?.Invoke();

            var error = await launcher.StartAsync(CancellationToken.None).ConfigureAwait(false);
            if (error is not null)
            {
                logger.LogWarning("The updater could not be started: {Error}", error);
                try { File.Delete(path); } catch (IOException) { }
                LastError = error;
                Finish(announcement.Id);
                return;
            }

            // The updater works: when it is done and this application is still alive, the update was refused or rolled back.
            var limit = launchedAt + WatchLimit;
            while (clock.GetUtcNow().UtcDateTime < limit)
            {
                await Task.Delay(PollInterval, clock, CancellationToken.None).ConfigureAwait(false);
                if (installation.Status() is { Phase: "done" } status && status.UpdatedUtc >= launchedAt - TimeSpan.FromSeconds(5)) break;
            }
            Finish(announcement.Id);
            await journal.WriteNewAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(exception, "The update could not be handed to the updater.");
            LastError = "Cererea de actualizare nu a putut fi trimisă către Updater: " + exception.Message;
            Finish(announcement.Id);
        }
    }

    private void Finish(string id)
    {
        lock (sync) { if (Current?.Id == id) Current = null; }
        Changed?.Invoke();
    }
}
