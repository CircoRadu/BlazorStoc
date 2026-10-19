using System.Diagnostics;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.Ocr;

// Runs one OCR job in a process of its own and hands the job folder to the caller to read. The process is what is isolated: a native crash, an
// out-of-memory or a hang of the OCR libraries (Tesseract, OpenCV, ONNX, PDFium) costs that one job, never the web application.
//
// Limits (Ocr:* configuration): TimeoutSeconds (default 300), MaxMemoryMb (managed heap of the worker, default 2048), MaxProcessMemoryMb (everything the
// worker process commits, native libraries included, through a Windows Job Object; default 4096, 0 = none), MaxParallel (default 2). A job over the time
// limit, or cancelled by the caller, has its whole process tree killed; the worker also dies with the application (kill on close). The job folder is always removed.
public sealed class OcrWorkerRunner
{
    private readonly SemaphoreSlim gate;
    private readonly TimeSpan timeout;
    private readonly int maxMemoryMb;
    private readonly int maxProcessMemoryMb;
    private long peakProcessMemoryBytes;
    private readonly ILogger? logger;
    private readonly Func<string, ProcessStartInfo> launch;

    public OcrWorkerRunner(IConfiguration configuration, ILogger? logger = null, Func<string, ProcessStartInfo>? launch = null)
    {
        gate = new SemaphoreSlim(Math.Max(1, configuration.GetValue("Ocr:MaxParallel", 2)));
        timeout = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Ocr:TimeoutSeconds", 300)));
        maxMemoryMb = Math.Max(256, configuration.GetValue("Ocr:MaxMemoryMb", 2048));
        maxProcessMemoryMb = Math.Max(0, configuration.GetValue("Ocr:MaxProcessMemoryMb", 4096));
        this.logger = logger;
        this.launch = launch ?? DefaultLaunch;
    }

    /// <summary>The time limit of a job (for tests and logs).</summary>
    public TimeSpan Timeout => timeout;

    /// <summary>The most memory one worker has committed so far, native libraries included (0 where Job Objects are not available).</summary>
    public long PeakProcessMemoryBytes => Interlocked.Read(ref peakProcessMemoryBytes);

    internal async Task<T> RunAsync<T>(string operation, string tessdataPath, byte[] pdf, string? documentJson, Func<string, T> read, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-ocr", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, OcrFiles.Request), OcrJson.Write(new OcrJobRequest(operation, tessdataPath)), cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(folder, OcrFiles.Input), pdf, cancellationToken).ConfigureAwait(false);
            if (documentJson is not null) await File.WriteAllTextAsync(Path.Combine(folder, OcrFiles.Document), documentJson, cancellationToken).ConfigureAwait(false);

            var (exitCode, stderr) = await RunProcessAsync(folder, cancellationToken).ConfigureAwait(false);

            var errorPath = Path.Combine(folder, OcrFiles.Refusal);
            if (File.Exists(errorPath))
            {
                var error = OcrJson.Read<OcrJobError>(await File.ReadAllTextAsync(errorPath, cancellationToken).ConfigureAwait(false));
                throw error.Kind == OcrJobError.Inventory ? new InventoryPickupOcrException(error.Message) : new InvoiceAnalysisException(error.Message);
            }
            if (exitCode != 0 || !File.Exists(Path.Combine(folder, OcrFiles.Result)))
            {
                logger?.LogError("OCR worker for {Operation} ended without a result (exit code {ExitCode}): {Detail}", operation, exitCode, Truncate(stderr));
                throw new OcrWorkerException(OcrRules.CrashedMessage);
            }
            try { return read(folder); }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
            {
                logger?.LogError("OCR worker result for {Operation} could not be read ({ErrorType}).", operation, exception.GetType().Name);
                throw new OcrWorkerException(OcrRules.CrashedMessage, exception);
            }
        }
        finally
        {
            gate.Release();
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { logger?.LogWarning("OCR job folder could not be removed ({ErrorType}).", exception.GetType().Name); }
        }
    }

    private async Task<(int ExitCode, string Stderr)> RunProcessAsync(string folder, CancellationToken cancellationToken)
    {
        var info = launch(folder);
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        // The heap limit stops a runaway worker (a hostile or damaged file) before it takes the memory of the machine; the native libraries are not covered by it.
        info.Environment["DOTNET_GCHeapHardLimit"] = ((long)maxMemoryMb * 1024 * 1024).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        // The job object limits what the process commits (the native libraries too) and ends it with the application; where it is not available the worker runs without it.
        using var job = ProcessJob.Create((long)maxProcessMemoryMb * 1024 * 1024);
        Process process;
        try { process = Process.Start(info) ?? throw new OcrWorkerException(OcrRules.CrashedMessage); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger?.LogError("OCR worker could not be started ({ErrorType}).", exception.GetType().Name);
            throw new OcrWorkerException(OcrRules.CrashedMessage, exception);
        }
        using (process)
        {
            job?.Attach(process);
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                Kill(process);
                if (cancellationToken.IsCancellationRequested) throw;
                logger?.LogError("OCR worker over the time limit of {Seconds} s was stopped.", (int)timeout.TotalSeconds);
                throw new OcrWorkerException(OcrRules.TimeoutMessage);
            }
            _ = await Task.WhenAny(output, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
            var stderr = errors.IsCompletedSuccessfully ? errors.Result : "";
            RememberPeak(job);
            return (process.ExitCode, stderr);
        }
    }

    private void RememberPeak(ProcessJob? job)
    {
        var peak = job?.PeakProcessMemoryBytes() ?? 0;
        long seen;
        while (peak > (seen = Interlocked.Read(ref peakProcessMemoryBytes)) && Interlocked.CompareExchange(ref peakProcessMemoryBytes, peak, seen) != seen) { }
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim() + "...";

    // The application started again with --ocr-worker: through its own executable, or through "dotnet BlazorStoc.dll" when the host is not that executable
    // (the dotnet host itself, a test host).
    private static ProcessStartInfo DefaultLaunch(string folder)
    {
        var assembly = typeof(OcrWorkerRunner).Assembly.Location;
        var current = Environment.ProcessPath ?? "dotnet";
        var ownExecutable = string.Equals(Path.GetFileNameWithoutExtension(current), typeof(OcrWorkerRunner).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase);
        var runsOnDotnet = string.Equals(Path.GetFileNameWithoutExtension(current), "dotnet", StringComparison.OrdinalIgnoreCase);
        var host = runsOnDotnet ? current : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } fromEnvironment ? fromEnvironment : "dotnet";
        var info = ownExecutable
            ? new ProcessStartInfo(current) { ArgumentList = { "--ocr-worker", folder } }
            : new ProcessStartInfo(host) { ArgumentList = { assembly, "--ocr-worker", folder } };
        info.WorkingDirectory = AppContext.BaseDirectory;
        return info;
    }
}
