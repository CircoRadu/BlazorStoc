using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Backup;

// Runs one backup, restoration or NAS operation in a process of its own and hands the application its answer. The process is what is isolated: a hung share, an
// out-of-memory or a crash while the database is exported, packed, hashed or copied costs that one operation, never the web application.
//
// Limits (Backup:* configuration, seconds): TimeoutSeconds (default 3600) for a backup, RestoreTimeoutSeconds (10800) for a restoration, NasTimeoutSeconds (3600) for the
// operations on the share; CancelGraceSeconds (30) is how long a worker that was asked to stop may take before its whole process tree is killed; MaxParallel (default 2)
// workers at a time; MaxMemoryMb (default 0 = no limit) is the managed heap limit of the worker. Whatever way a worker ends, the lock it took is released at once
// (not left to expire) and the application's view of the lock is refreshed.
public sealed class BackupWorkerRunner
{
    private readonly SemaphoreSlim gate;
    private readonly TimeSpan backupTimeout, restoreTimeout, nasTimeout, cancelGrace;
    private readonly int maxMemoryMb;
    private readonly string lockPath;
    private readonly ILogger? logger;
    private readonly Func<ProcessStartInfo> launch;

    public BackupWorkerRunner(IConfiguration configuration, ILogger? logger = null, Func<ProcessStartInfo>? launch = null)
    {
        gate = new SemaphoreSlim(Math.Max(1, configuration.GetValue("Backup:MaxParallel", 2)));
        backupTimeout = Seconds(configuration, "Backup:TimeoutSeconds", 3600);
        restoreTimeout = Seconds(configuration, "Backup:RestoreTimeoutSeconds", 10800);
        nasTimeout = Seconds(configuration, "Backup:NasTimeoutSeconds", 3600);
        cancelGrace = Seconds(configuration, "Backup:CancelGraceSeconds", 30);
        maxMemoryMb = Math.Max(0, configuration.GetValue("Backup:MaxMemoryMb", 0));
        lockPath = MariaAssetPaths.OperationLock(configuration);
        this.logger = logger;
        this.launch = launch ?? DefaultStartInfo;
    }

    private static TimeSpan Seconds(IConfiguration configuration, string key, int fallback) => TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue(key, fallback)));

    /// <summary>The time limit of an operation (for tests and logs).</summary>
    public TimeSpan TimeoutOf(string operation) => operation switch
    {
        BackupOperations.Backup => backupTimeout,
        BackupOperations.Restore => restoreTimeout,
        _ => nasTimeout
    };

    internal async Task<T> RunAsync<T>(BackupJobRequest request, Action<string, string>? onProgress, CancellationToken cancellationToken)
    {
        var payload = await RunAsync(request, onProgress, cancellationToken).ConfigureAwait(false);
        try { return BackupJson.Read<T>(payload); }
        catch (JsonException exception)
        {
            logger?.LogError("Backup worker answer for {Operation} could not be read ({ErrorType}).", request.Operation, exception.GetType().Name);
            throw new BackupWorkerException(BackupWorkerRules.WorkerFailedMessage, exception);
        }
    }

    private async Task<JsonElement> RunAsync(BackupJobRequest request, Action<string, string>? onProgress, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RunProcessAsync(request, onProgress, cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task<JsonElement> RunProcessAsync(BackupJobRequest request, Action<string, string>? onProgress, CancellationToken cancellationToken)
    {
        var info = launch();
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardInputEncoding = new UTF8Encoding(false);
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        if (maxMemoryMb > 0) info.Environment["DOTNET_GCHeapHardLimit"] = ((long)maxMemoryMb * 1024 * 1024).ToString("X", System.Globalization.CultureInfo.InvariantCulture);

        var startedUtc = DateTime.UtcNow;
        Process process;
        try { process = Process.Start(info) ?? throw new BackupWorkerException(BackupWorkerRules.WorkerFailedMessage); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger?.LogError("Backup worker could not be started ({ErrorType}).", exception.GetType().Name);
            throw new BackupWorkerException(BackupWorkerRules.WorkerFailedMessage, exception);
        }
        using (process)
        {
            var processId = process.Id;
            var errors = new StringBuilder();
            var errorReader = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                        lock (errors) { errors.AppendLine(line); if (errors.Length > 4000) errors.Remove(0, errors.Length - 3000); }
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException) { }
            });

            JsonElement? result = null;
            BackupAnswerLine? answer = null;
            var cancelled = false;
            var outputReader = Task.Run(async () =>
            {
                try
                {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        switch (document.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null)
                        {
                            case BackupLines.Progress:
                                // The worker takes the lock itself: the application looks at the lock file again now instead of up to a second later.
                                MaintenanceGate.Invalidate();
                                var step = BackupJson.Read<BackupProgressLine>(document.RootElement);
                                onProgress?.Invoke(step.Stage ?? "", step.Message ?? "");
                                break;
                            case BackupLines.Result:
                                result = document.RootElement.GetProperty("payload").Clone();
                                break;
                            case BackupLines.Answer:
                                answer = BackupJson.Read<BackupAnswerLine>(document.RootElement);
                                break;
                            case BackupLines.Cancelled:
                                cancelled = true;
                                break;
                        }
                    }
                    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                        // A line that is not the protocol (a library wrote to the output): not an answer, ignored.
                    }
                }
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException) { }
            });

            try
            {
                await process.StandardInput.WriteLineAsync(BackupJson.Write(request)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException) { /* the worker is already gone: the missing answer below is the failure */ }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeoutOf(request.Operation));
            try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                // Asked to stop first: the worker finishes the step it is in, removes its temporary files and releases its lock; only a worker that does not
                // answer in time loses its whole process tree.
                await TryAskToStopAsync(process).ConfigureAwait(false);
                if (!await WaitForExitAsync(process, cancelGrace).ConfigureAwait(false)) Kill(process);
                ReleaseLock(processId);
                RemoveLeftovers(request.Operation, startedUtc);
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
                logger?.LogError("Backup worker for {Operation} over the time limit of {Seconds} s was stopped.", request.Operation, (int)TimeoutOf(request.Operation).TotalSeconds);
                throw new BackupWorkerException(BackupWorkerRules.WorkerTimeoutMessage);
            }
            // The end of the output (a program started by the worker may hold the pipe open a little longer, so the wait is bounded).
            await Task.WhenAny(Task.WhenAll(outputReader, errorReader), Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)).ConfigureAwait(false);
            ReleaseLock(processId);

            string stderr;
            lock (errors) stderr = errors.ToString().Trim();
            if (result is { } payload)
            {
                if (stderr.Length > 0) logger?.LogDebug("Backup worker output for {Operation}: {Detail}", request.Operation, Truncate(stderr));
                return payload;
            }
            if (answer is { } refused) throw new BackupOperationException(refused.Kind, refused.Message);
            if (cancelled) throw new OperationCanceledException(cancellationToken);
            RemoveLeftovers(request.Operation, startedUtc);
            logger?.LogError("Backup worker for {Operation} ended without an answer (exit code {ExitCode}): {Detail}", request.Operation, SafeExitCode(process), Truncate(stderr));
            throw new BackupWorkerException(BackupWorkerRules.WorkerFailedMessage);
        }
    }

    // The lock written by this very worker is removed: a worker that died (or was killed) holding it would otherwise keep every session on the waiting page until the lease ends.
    private void ReleaseLock(int processId)
    {
        if (FileOperationLockService.ReleaseIfOwnedBy(lockPath, processId)) logger?.LogWarning("The lock of a backup worker that ended was released.");
        MaintenanceGate.Invalidate();
    }

    // A worker that was killed (or died) in the middle of a backup or restoration cannot clean up after itself: the half-written package, the file with the
    // account password written for mariadb-dump / mariadb, the unpacked restoration folder. What the worker created since it started is removed here, at once
    // (the next worker also sweeps what is old, in case the application itself was the one that went away).
    private void RemoveLeftovers(string operation, DateTime startedUtc)
    {
        if (operation is not (BackupOperations.Backup or BackupOperations.Restore)) return;
        var since = startedUtc.AddSeconds(-1);
        try
        {
            var temp = Path.GetTempPath();
            foreach (var file in Directory.EnumerateFiles(temp, ".blazorstoc-*.cnf"))
                if (File.GetCreationTimeUtc(file) >= since) File.Delete(file);
            foreach (var folder in Directory.EnumerateDirectories(temp, "blazorstoc-restore-*"))
                if (Directory.GetCreationTimeUtc(folder) >= since) Directory.Delete(folder, recursive: true);
            var backups = Path.GetDirectoryName(lockPath);
            if (backups is not null && Directory.Exists(backups))
                foreach (var file in Directory.EnumerateFiles(backups, ".tmp-*"))
                    if (!Path.GetFileName(file).StartsWith(".tmp-fetch-", StringComparison.Ordinal) && File.GetCreationTimeUtc(file) >= since) File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { logger?.LogWarning("Leftovers of a backup worker could not be removed ({ErrorType}).", exception.GetType().Name); }
    }

    private static async Task TryAskToStopAsync(Process process)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(BackupLines.CancelCommand).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan grace)
    {
        using var wait = new CancellationTokenSource(grace);
        try { await process.WaitForExitAsync(wait.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        try { process.WaitForExit(5000); } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch (InvalidOperationException) { return -1; }
    }

    private static string Truncate(string text) => text.Length <= 600 ? text : text[..600] + "...";

    // The application started again with --backup-worker and its own command-line arguments (so the worker reads the same configuration): through its own executable,
    // or through "dotnet BlazorStoc.dll" when the host is not that executable (the dotnet host itself, a test host). The working folder is the application's own,
    // because the configuration files and the private account files are looked up from it.
    internal static ProcessStartInfo DefaultStartInfo()
    {
        var assembly = typeof(BackupWorkerRunner).Assembly.Location;
        var current = Environment.ProcessPath ?? "dotnet";
        var ownExecutable = string.Equals(Path.GetFileNameWithoutExtension(current), typeof(BackupWorkerRunner).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);
        var runsOnDotnet = string.Equals(Path.GetFileNameWithoutExtension(current), "dotnet", StringComparison.OrdinalIgnoreCase);
        var host = runsOnDotnet ? current : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } fromEnvironment ? fromEnvironment : "dotnet";
        var info = ownExecutable ? new ProcessStartInfo(current) { ArgumentList = { BackupWorkerRules.Argument } } : new ProcessStartInfo(host) { ArgumentList = { assembly, BackupWorkerRules.Argument } };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1)) info.ArgumentList.Add(argument);
        info.WorkingDirectory = Environment.CurrentDirectory;
        return info;
    }
}
