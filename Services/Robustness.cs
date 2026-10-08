using System.Collections.Concurrent;
using System.Text;

namespace BlazorStoc.Services;

// Robustness against errors: a daily rolling log file with retention, the last-resort handlers for exceptions nobody caught (they stop the
// process, so the error is written first) and one helper for tasks started without awaiting them.

/// <summary>Writes the application log to <c>{directory}/blazorstoc-yyyyMMdd.log</c>; files older than the retention are deleted.</summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string directory;
    private readonly int retentionDays;
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, RollingFileLogger> loggers = new();
    private DateOnly cleanedOn;

    public RollingFileLoggerProvider(string directory, int retentionDays)
    {
        this.directory = directory;
        this.retentionDays = Math.Max(1, retentionDays);
    }

    public ILogger CreateLogger(string categoryName) => loggers.GetOrAdd(categoryName, name => new RollingFileLogger(this, name));

    public void Dispose() { }

    internal void Write(string line)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                var today = DateOnly.FromDateTime(DateTime.Now);
                File.AppendAllText(Path.Combine(directory, $"blazorstoc-{today:yyyyMMdd}.log"), line + Environment.NewLine, Encoding.UTF8);
                if (cleanedOn != today) { cleanedOn = today; DeleteOld(today); }
            }
        }
        catch (Exception) { /* a log that cannot be written must never break the application */ }
    }

    private void DeleteOld(DateOnly today)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "blazorstoc-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["blazorstoc-".Length..];
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", out var day) && day < today.AddDays(-retentionDays))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    private sealed class RollingFileLogger(RollingFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {formatter(state, exception)}";
            // The stack trace goes to the file only (never to users).
            if (exception is not null) text += Environment.NewLine + exception;
            owner.Write(text);
        }
    }
}

internal static class Robustness
{
    /// <summary>Adds the rolling file log (Logging:File:Directory, default "logs"; Logging:File:RetentionDays, default 14) and the process-wide handlers.</summary>
    public static void AddFileLogging(this WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Logging:File:Directory"];
        var directory = Path.IsPathRooted(configured ?? "") ? configured! : Path.Combine(builder.Environment.ContentRootPath, string.IsNullOrWhiteSpace(configured) ? "logs" : configured);
        var retention = int.TryParse(builder.Configuration["Logging:File:RetentionDays"], out var days) ? days : 14;
        var provider = new RollingFileLoggerProvider(directory, retention);
        builder.Logging.AddProvider(provider);
        // An exception outside every try (a task nobody awaits, a timer, async void) ends the process: the error is written before it stops.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            provider.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Critical] UnhandledException (terminating: {args.IsTerminating}): {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            provider.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Error] UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };
    }

    /// <summary>For a task started without awaiting it: an exception is logged (the cancellation of the owner is not an error) instead of being lost.</summary>
    public static void FireAndForget(this Task task, ILogger? logger, string what)
    {
        _ = Observe(task, logger, what);

        static async Task Observe(Task task, ILogger? logger, string what)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { logger?.LogError(exception, "Background task failed: {What}", what); }
        }
    }
}
