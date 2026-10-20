using System.Diagnostics;
using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>
/// The real <see cref="IApplicationHost"/>: tools/supervisor.ps1 (copied to scripts/ by "init") stops and starts the application, and /health is asked over HTTP. A stop also keeps
/// the scheduled task of the supervisor from bringing the application back in the middle of the update (the stop window); the start ends that window. Without a scheduled task
/// the supervisor is started directly, detached, so it outlives the updater.
/// </summary>
public sealed class SupervisorHost(InstallLayout layout, UpdaterSettings settings, IUpdateLog log, HttpClient? http = null) : IApplicationHost
{
    private readonly HttpClient client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    private string Script => settings.SupervisorScript ?? Path.Combine(layout.ScriptsFolder, "supervisor.ps1");

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Script)) throw new UpdateStepException($"The supervisor script {Script} is missing (run \"init\" first).");
        log.Info("Stopping the supervisor and the application.");
        var output = await RunPowerShellAsync(["-Action", "stop", "-Port", settings.Port.ToString(), "-InstallRoot", layout.Root, "-StopWindowMinutes", "60"], TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false);
        log.Info(output.Trim());
        // Nothing may answer any more: the files of the version are then free to be replaced and the port is free for the next one.
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (!(await ProbeAsync(cancellationToken).ConfigureAwait(false)).Reachable) return;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        throw new UpdateStepException("The application still answers after the stop.");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        log.Info("Starting the supervisor (it starts the version in current.txt).");
        var output = await RunPowerShellAsync(["-Action", "start", "-Port", settings.Port.ToString(), "-InstallRoot", layout.Root], TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
        if (!output.Contains("No scheduled task", StringComparison.OrdinalIgnoreCase)) { log.Info(output.Trim()); return; }
        log.Info("No scheduled task of the supervisor: starting the supervisor directly.");
        var arguments = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", Script, "-Action", "run", "-Port", settings.Port.ToString(), "-InstallRoot", layout.Root };
        if (settings.MariaRoot is not null) { arguments.Add("-MariaRoot"); arguments.Add(settings.MariaRoot); }
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = layout.Root };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process.Start(start);
    }

    public async Task<HealthAnswer> ProbeAsync(CancellationToken cancellationToken)
    {
        foreach (var port in settings.Ports())
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/health", cancellationToken).ConfigureAwait(false);
                string? version = null;
                try
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("version", out var value)) version = value.GetString();
                }
                catch (JsonException) { }
                return new HealthAnswer(true, (int)response.StatusCode, version);
            }
            catch (Exception exception) when ((exception is HttpRequestException or TaskCanceledException or IOException) && !cancellationToken.IsCancellationRequested) { }
        }
        return HealthAnswer.None;
    }

    private async Task<string> RunPowerShellAsync(IReadOnlyList<string> scriptArguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = layout.Root };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script }) start.ArgumentList.Add(argument);
        foreach (var argument in scriptArguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new UpdateStepException("powershell.exe could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new UpdateStepException("The supervisor script did not finish in time.");
        }
        var text = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new UpdateStepException($"The supervisor script ended with code {process.ExitCode}: {text.Trim()}");
        return text;
    }
}
