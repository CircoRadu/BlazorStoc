using System.Text.Json;
using BlazorStoc.Services;
using BlazorStoc.Startup;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;

namespace BlazorStoc.Modules.Backup;

// The worker side: the application started again with --backup-worker. It builds its configuration the way the application does (the same files and the same
// command-line arguments, so the same database and folders), reads one request from the standard input, runs the very same service classes the application would
// run in-process (backup, restoration, NAS copier), and reports on the standard output. It takes the shared operation lock itself, so the application is frozen for
// exactly as long as the worker works, and a worker that dies leaves only a lock that expires.
public static class BackupWorker
{
    public static async Task<int> RunAsync(string[] configurationArguments)
    {
        // The protocol goes to the real standard output; whatever a library writes to the console goes to the error stream, where the application reads it as log.
        var protocol = Console.Out;
        Console.SetOut(Console.Error);
        var writer = new ProtocolWriter(protocol);

        BackupJobRequest request;
        try
        {
            var line = await Console.In.ReadLineAsync().ConfigureAwait(false);
            // (a caller that writes through a UTF-8 stream with a preamble puts a byte order mark before the first line)
            request = BackupJson.Read<BackupJobRequest>(line?.TrimStart('﻿') ?? throw new JsonException("no request"));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Console.Error.WriteLine($"Backup worker: the request could not be read ({exception.GetType().Name}).");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        // "cancel" from the application stops the work cleanly; the application going away (end of input) stops a backup or a copy the same way, but not a
        // restoration: a restoration that is already swapping the schemas is finished, never abandoned halfway.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
                    if (line == BackupLines.CancelCommand) { cancellation.Cancel(); return; }
                if (request.Operation != BackupOperations.Restore) cancellation.Cancel();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException) { }
        });

        try
        {
            var configuration = BuildConfiguration(configurationArguments);
            using var loggers = LoggerFactory.Create(logging =>
            {
                logging.AddSimpleConsole(options => options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ");
                logging.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            });
            await RunOperationAsync(request, configuration, loggers, writer, cancellation.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            writer.Write(new { type = BackupLines.Cancelled });
            return 0;
        }
        catch (NasBackupException exception) { return Answer(writer, BackupAnswerLine.Nas, exception.Message); }
        catch (MaintenanceInProgressException exception) { return Answer(writer, BackupAnswerLine.Maintenance, exception.Message); }
        catch (MySqlException exception)
        {
            Console.Error.WriteLine($"Backup worker: the database refused ({exception.GetType().Name}, error {exception.ErrorCode}).");
            return Answer(writer, BackupAnswerLine.Database, "Baza de date nu a raspuns.");
        }
        catch (Exception exception)
        {
            // Anything else is not an answer: the process ends with a failing code and no closing line.
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static int Answer(ProtocolWriter writer, string kind, string message)
    {
        writer.Write(new BackupAnswerLine(BackupLines.Answer, kind, message));
        return 0;
    }

    // The configuration of the web application without the web host: appsettings, environment, the command line of the application, the private account files and the override file of Setari -> Baza de date (what the administrator saved there, so a worker never works on the old connection).
    // An argument list the configuration cannot read is not a reason to stop: the files and the environment alone are what a supervised application runs with.
    internal static IConfiguration BuildConfiguration(string[] arguments)
    {
        try { return Configure(arguments); }
        catch (FormatException) { return Configure([]); }

        static IConfiguration Configure(string[] arguments)
        {
            var builder = WebApplication.CreateBuilder(arguments);
            builder.AddEffectiveConfiguration();
            return builder.Configuration;
        }
    }

    private static async Task RunOperationAsync(BackupJobRequest request, IConfiguration configuration, ILoggerFactory loggers, ProtocolWriter writer, CancellationToken token)
    {
        var access = new DelegatedAccess(request.OperatorName, request.OperatorIsAdministrator);
        var audit = new MariaAuditTrail(configuration);
        var lockPath = Path.Combine(MariaAssetPaths.DatabaseBackups(configuration), "operation.lock.json");
        var locks = new FileOperationLockService(lockPath, audit, loggers.CreateLogger<FileOperationLockService>());
        var backups = new MariaDatabaseBackupService(configuration, access, locks, audit, loggers.CreateLogger<MariaDatabaseBackupService>());
        if (request.Operation is BackupOperations.Backup or BackupOperations.Restore && FileOperationLockService.ReadActive(lockPath) is null) SweepStaleTemporaryFiles(configuration);
        object result;
        switch (request.Operation)
        {
            case BackupOperations.Backup:
            {
                var kind = Enum.Parse<BackupKind>(request.Kind ?? throw new JsonException("no kind"));
                result = await backups.CreateBackupAsync(kind, new SynchronousProgress<BackupProgress>(step => writer.Progress(step.Stage.ToString(), step.Message)), token).ConfigureAwait(false);
                break;
            }
            case BackupOperations.Restore:
            {
                var restore = new MariaDatabaseRestoreService(configuration, access, locks, backups, audit, loggers.CreateLogger<MariaDatabaseRestoreService>());
                result = await restore.RestoreAsync(request.Package ?? throw new JsonException("no package"),
                    new SynchronousProgress<RestoreProgress>(step => writer.Progress(step.Stage.ToString(), step.Message)), token).ConfigureAwait(false);
                break;
            }
            default:
            {
                var protection = DataProtectionProvider.Create(new DirectoryInfo(DataProtectionKeys.PathFor(configuration)), options => options.SetApplicationName("BlazorStoc"));
                var copier = new NasBackupCopier(configuration, protection, access, audit, loggers.CreateLogger<NasBackupCopier>());
                result = request.Operation switch
                {
                    BackupOperations.NasTest => await copier.TestAsync(request.Nas ?? throw new JsonException("no settings"), token).ConfigureAwait(false),
                    BackupOperations.NasCopyMissing => await copier.CopyMissingAsync(token).ConfigureAwait(false),
                    BackupOperations.NasCopyAfterBackup => await copier.CopyAfterBackupAsync(request.Package, token).ConfigureAwait(false),
                    BackupOperations.NasList => await copier.ListShareAsync(token).ConfigureAwait(false),
                    BackupOperations.NasFetch => await copier.FetchAsync(request.Package ?? throw new JsonException("no package"), token).ConfigureAwait(false),
                    _ => throw new JsonException($"unknown operation {request.Operation}")
                };
                break;
            }
        }
        writer.Write(new { type = BackupLines.Result, payload = result });
    }

    // A worker that was killed (time limit, the machine) cannot clean up after itself. The credentials file it wrote for mariadb-dump / mariadb holds the password of
    // a database account, so a stale one is removed, together with the unpacked restore folders and the half-written packages. Done only by a worker that is about
    // to take the exclusive lock, and only when nobody holds it: a long restoration in progress still needs its own files.
    private static void SweepStaleTemporaryFiles(IConfiguration configuration)
    {
        try
        {
            var temp = Path.GetTempPath();
            var limit = DateTime.UtcNow.AddMinutes(-15);
            foreach (var file in Directory.EnumerateFiles(temp, ".blazorstoc-*.cnf"))
                if (File.GetLastWriteTimeUtc(file) < limit) File.Delete(file);
            foreach (var folder in Directory.EnumerateDirectories(temp, "blazorstoc-restore-*"))
                if (Directory.GetLastWriteTimeUtc(folder) < limit) Directory.Delete(folder, recursive: true);
            var backups = MariaAssetPaths.DatabaseBackups(configuration);
            if (Directory.Exists(backups))
                foreach (var file in Directory.EnumerateFiles(backups, ".tmp-*"))
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1)) File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ProtocolWriter(TextWriter output)
    {
        private readonly object gate = new();

        public void Progress(string stage, string message) => Write(new BackupProgressLine(BackupLines.Progress, stage, message));

        public void Write<T>(T line)
        {
            var text = BackupJson.Write(line);
            lock (gate)
            {
                output.WriteLine(text);
                output.Flush();
            }
        }
    }
}
