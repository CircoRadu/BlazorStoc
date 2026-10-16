using System.Diagnostics;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Backup;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The backup worker: the services the pages and the schedule use are served by a process of their own; a worker that dies, hangs, is cancelled or prints garbage
// costs only that one operation (circuit breaker of the "backup" module, lock released at once, limits applied), and the answers of the services (a refused copy,
// a full disk, the lock held by someone else) stay the answers the pages already show. The worker is played by small PowerShell scripts here; the real one is
// exercised against the test database by the MariaDB section "Backup worker" (MariaExtendedChecks.BackupWorker.cs).
public static class BackupWorkerChecks
{
    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value)).Build();

    private static ProcessStartInfo Script(string script) => new("powershell.exe") { ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script } };

    // The first line the worker gets is the request.
    private const string ReadRequest = "$request = [Console]::In.ReadLine() | ConvertFrom-Json; ";

    private const string Done = """
        '{"type":"result","payload":{"success":true,"packagePath":"p.zip","packageFileName":"p.zip","errorMessage":null}}'
        """;

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "blazorstoc-bw-" + Guid.NewGuid().ToString("N"));

    // Every rig gets a backup folder of its own (the lock file lives in it) unless the check names one.
    private static (string Key, string Value)[] WithFolder((string Key, string Value)[] values) =>
        values.Any(item => item.Key == "Database:MariaBackupFilesPath") ? values : [.. values, ("Database:MariaBackupFilesPath", NewFolder())];

    private static (ModuleRegistry Modules, BackupWorkerRunner Runner, IConfiguration Configuration) Rig(Func<ProcessStartInfo> launch, params (string Key, string Value)[] values)
    {
        var configuration = Settings(WithFolder(values));
        var modules = new ModuleRegistry(new ManualTimeProvider());
        modules.Add(new BackupModule(), new ServiceCollection(), configuration);
        return (modules, new BackupWorkerRunner(configuration, launch: launch), configuration);
    }

    private static OutOfProcessBackupService Backups(BackupWorkerRunner runner, ModuleRegistry modules, IConfiguration configuration) =>
        new(runner, modules, new TestAccessControl(true, "claude.test"), configuration, null);

    public static async Task RunAsync(Action<bool, string> check)
    {
        await ProtocolAsync(check);
        await AnswersAsync(check);
        await FailuresAsync(check);
        await StopsAsync(check);
        await LimitsAsync(check);
        await RealWorkerAsync(check);
        Wiring(check);
    }

    // ---- what the application sends and what it gets back ----

    private static async Task ProtocolAsync(Action<bool, string> check)
    {
        var script = ReadRequest + """
            '{"type":"progress","stage":"Exporting","message":"se exporta"}'
            'a line that is not the protocol'
            '{"type":"progress","stage":"Done","message":"gata"}'
            $name = $request.operatorName + '|' + $request.kind + '|' + $request.operatorIsAdministrator
            ('{"type":"result","payload":{"success":true,"packagePath":"p.zip","packageFileName":"' + $name + '","errorMessage":null}}')
            """;
        var (modules, runner, configuration) = Rig(() => Script(script));
        var steps = new List<string>();
        var result = await Backups(runner, modules, configuration).CreateBackupAsync(BackupKind.Manual, new SyncProgress<BackupProgress>(step => steps.Add($"{step.Stage}:{step.Message}")));
        check(result.Success && result.PackageFileName == "claude.test|Manual|True", "Backup worker: the request reaches the worker with the user, the role and the kind of backup, and its result comes back as the service's own record");
        check(steps.SequenceEqual(["Exporting:se exporta", "Done:gata"]), "Backup worker: the progress lines reach the page's progress in order, a line that is not the protocol is ignored");
        check(modules.StatusOf("backup") == ModuleStatus.Healthy, "Backup worker: a good operation leaves the module Healthy");

        var limited = Rig(() => Script(ReadRequest + "if ($request.operatorIsAdministrator) { exit 4 }; " + Done));
        var asUser = await new OutOfProcessBackupService(limited.Runner, limited.Modules, new TestAccessControl(false, "operator"), limited.Configuration, null).CreateBackupAsync(BackupKind.InventoryPickup);
        check(asUser.Success, "Backup worker: a user who is not an administrator is passed on as such");

        var failedResult = Rig(() => Script("""
            '{"type":"result","payload":{"success":false,"packagePath":null,"packageFileName":null,"errorMessage":"Spatiu insuficient"}}'
            """));
        for (var i = 0; i < ModuleRegistry.FailureThreshold + 2; i++) await Backups(failedResult.Runner, failedResult.Modules, failedResult.Configuration).CreateBackupAsync(BackupKind.Manual);
        var failed = await Backups(failedResult.Runner, failedResult.Modules, failedResult.Configuration).CreateBackupAsync(BackupKind.Manual);
        check(!failed.Success && failed.ErrorMessage == "Spatiu insuficient" && failedResult.Modules.StatusOf("backup") == ModuleStatus.Healthy,
            "Backup worker: a backup that the service refuses (full disk...) is an answer, repeated as often as it happens, never a failure of the module");

        var restore = Rig(() => Script("""
            '{"type":"progress","stage":"Importing","message":"se importa"}'
            '{"type":"result","payload":{"success":true,"errorMessage":null,"preRestorePackageFileName":"pre.zip"}}'
            """));
        var stages = new List<RestoreStage>();
        var restored = await new OutOfProcessRestoreService(restore.Runner, restore.Modules, new TestAccessControl(true, "claude.test"))
            .RestoreAsync("pachet.zip", new SyncProgress<RestoreProgress>(step => stages.Add(step.Stage)));
        check(restored.Success && restored.PreRestorePackageFileName == "pre.zip" && stages.SequenceEqual([RestoreStage.Importing]), "Backup worker: a restoration returns its result and its stages the same way");
        var invalid = await new OutOfProcessRestoreService(restore.Runner, restore.Modules, new TestAccessControl(true, "claude.test")).RestoreAsync(@"..\x.zip");
        check(!invalid.Success && invalid.ErrorMessage == RestoreRules.PackageNotFoundMessage, "Backup worker: a package name that is a path is refused without starting a worker");

        var nas = Rig(() => Script(ReadRequest + """
            if ($request.operation -eq 'nas-list') { '{"type":"result","payload":{"ok":true,"message":"","files":[{"fileName":"a.zip","sizeBytes":12,"modifiedUtc":"2026-10-16T08:00:00Z"}]}}' }
            elseif ($request.operation -eq 'nas-test') { $text = ($request.nas.path + '|' + $request.nas.userName + '|' + $request.nas.password + '|' + $request.nas.copyEnabled).Replace('\', '\\'); '{"type":"result","payload":{"ok":true,"copied":0,"alreadyThere":0,"failed":0,"message":"' + $text + '"}}' }
            else { '{"type":"result","payload":{"ok":true,"copied":1,"alreadyThere":0,"failed":0,"message":"' + $request.operation + '|' + $request.package + '"}}' }
            """));
        var copier = new OutOfProcessNasCopier(nas.Runner, nas.Modules, new TestAccessControl(true, "claude.test"), nas.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var listing = await copier.ListShareAsync();
        check(listing.Ok && listing.Files.Single().FileName == "a.zip" && listing.Files.Single().SizeBytes == 12 && listing.Files.Single().ModifiedUtc == new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc),
            "Backup worker: the listing of the share comes back with names, sizes and dates");
        var tested = await copier.TestAsync(new NasBackupInput(@"\\nas\share", "user", "secret", true));
        check(tested.Message == @"\\nas\share|user|secret|True", "Backup worker: the settings typed for the connection test (the password included) reach the worker over the pipe");
        check((await copier.CopyAfterBackupAsync("x.zip")).Message == "nas-copy-after-backup|x.zip" && (await copier.CopyAfterBackupAsync(null)).Message == "nas-copy-after-backup|"
            && (await copier.FetchAsync("y.zip")).Message == "nas-fetch|y.zip" && (await copier.CopyMissingAsync()).Message == "nas-copy-missing|",
            "Backup worker: each operation of the copier is its own request (copy of one package, of all, fetch)");

        var denied = Rig(() => Script(Done));
        var refused = false;
        try { await new OutOfProcessNasCopier(denied.Runner, denied.Modules, new TestAccessControl(false, "operator", productOperator: false), denied.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ListShareAsync(); }
        catch (AccessDeniedException) { refused = true; }
        check(refused, "Backup worker: the right to read the share is checked by the application before any worker is started");
    }

    // ---- what the services say stays what they say ----

    private static async Task AnswersAsync(Action<bool, string> check)
    {
        var shareRefused = Rig(() => Script("""
            '{"type":"answer","kind":"nas","message":"Contul sau parola sunt gresite."}'
            """));
        var copier = new OutOfProcessNasCopier(shareRefused.Runner, shareRefused.Modules, new TestAccessControl(true, "claude.test"), shareRefused.Configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var message = "";
        try { await copier.CopyAfterBackupAsync(null); } catch (NasBackupException exception) { message = exception.Message; }
        check(message == "Contul sau parola sunt gresite.", "Backup worker: a refusal of the share reaches the page as the NasBackupException it already handles");
        for (var i = 0; i < ModuleRegistry.FailureThreshold + 2; i++) { try { await copier.CopyAfterBackupAsync(null); } catch (NasBackupException) { } }
        check(shareRefused.Modules.StatusOf("backup") == ModuleStatus.Healthy, "Backup worker: refusals of the share never count as failures of the module");

        var databaseDown = Rig(() => Script("""
            '{"type":"answer","kind":"database","message":"Baza de date nu a raspuns."}'
            """));
        var backup = await Backups(databaseDown.Runner, databaseDown.Modules, databaseDown.Configuration).CreateBackupAsync(BackupKind.Scheduled);
        var restore = await new OutOfProcessRestoreService(databaseDown.Runner, databaseDown.Modules, new TestAccessControl(true, "claude.test")).RestoreAsync("pachet.zip");
        check(!backup.Success && backup.ErrorMessage!.StartsWith(BackupRules.GenericFailedMessage, StringComparison.Ordinal) && !restore.Success && restore.ErrorMessage == RestoreRules.GenericFailedMessage
            && databaseDown.Modules.StatusOf("backup") == ModuleStatus.Healthy, "Backup worker: a database that does not answer gives the backup and the restoration their own failed messages, not a failure of the module");
    }

    // ---- a worker that fails costs one operation ----

    private static async Task FailuresAsync(Action<bool, string> check)
    {
        var starts = 0;
        var clock = new ManualTimeProvider();
        var configuration = Settings(("Database:MariaBackupFilesPath", NewFolder()));
        var modules = new ModuleRegistry(clock);
        modules.Add(new BackupModule(), new ServiceCollection(), configuration);
        var crashing = new BackupWorkerRunner(configuration, launch: () => { starts++; return Script("exit 3"); });
        var good = new BackupWorkerRunner(configuration, launch: () => Script(Done));

        var first = await Backups(crashing, modules, configuration).CreateBackupAsync(BackupKind.Manual);
        check(!first.Success && first.ErrorMessage == BackupWorkerRules.WorkerFailedMessage && starts == 1 && modules.StatusOf("backup") == ModuleStatus.Degraded,
            "Backup worker: a worker that dies without an answer gives the page a failed result and makes the module Degraded");
        for (var i = 0; i < ModuleRegistry.FailureThreshold - 1; i++) await Backups(crashing, modules, configuration).CreateBackupAsync(BackupKind.Manual);
        check(modules.StatusOf("backup") == ModuleStatus.Disabled && starts == ModuleRegistry.FailureThreshold, "Backup worker: repeated crashes take the backup module out");
        var unavailable = await Backups(crashing, modules, configuration).CreateBackupAsync(BackupKind.Manual);
        var copierTold = await new OutOfProcessNasCopier(crashing, modules, new TestAccessControl(true, "claude.test"), configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance).ListShareAsync();
        var restoreTold = await new OutOfProcessRestoreService(crashing, modules, new TestAccessControl(true, "claude.test")).RestoreAsync("pachet.zip");
        check(unavailable.ErrorMessage == BackupWorkerRules.UnavailableMessage && !copierTold.Ok && copierTold.Message == BackupWorkerRules.UnavailableMessage && restoreTold.ErrorMessage == BackupWorkerRules.UnavailableMessage
            && starts == ModuleRegistry.FailureThreshold, "Backup worker: with the circuit open the backup, the share and the restoration are told so and no process is started");
        clock.Advance(ModuleRegistry.Cooldown + TimeSpan.FromSeconds(1));
        var recovered = await Backups(good, modules, configuration).CreateBackupAsync(BackupKind.Manual);
        check(recovered.Success && modules.StatusOf("backup") == ModuleStatus.Healthy, "Backup worker: after the cooldown a good operation brings the module back with no restart of the application");

        var garbage = Rig(() => Script("'not json at all'; 'nor this'"));
        var garbageResult = await Backups(garbage.Runner, garbage.Modules, garbage.Configuration).CreateBackupAsync(BackupKind.Manual);
        check(garbageResult.ErrorMessage == BackupWorkerRules.WorkerFailedMessage, "Backup worker: output that is not the protocol is a failure of the operation, not a crash of the application");
        var unreadable = Rig(() => Script("""'{"type":"result","payload":{"success":"maybe"}}'"""));
        check((await Backups(unreadable.Runner, unreadable.Modules, unreadable.Configuration).CreateBackupAsync(BackupKind.Manual)).ErrorMessage == BackupWorkerRules.WorkerFailedMessage,
            "Backup worker: a result the record cannot be read from is a failure of the operation");
        var missing = Rig(() => new ProcessStartInfo("this-program-does-not-exist.exe"));
        check((await Backups(missing.Runner, missing.Modules, missing.Configuration).CreateBackupAsync(BackupKind.Manual)).ErrorMessage == BackupWorkerRules.WorkerFailedMessage,
            "Backup worker: a worker that cannot even be started is a failure of the operation");
    }

    // ---- time limit, cancellation, the lock of a worker that is stopped ----

    private static async Task StopsAsync(Action<bool, string> check)
    {
        var lockScript = ReadRequest + """
            $folder = $env:BW_LOCKDIR
            New-Item -ItemType Directory -Force $folder | Out-Null
            $now = [DateTime]::UtcNow.ToString('o')
            '{"operation":"backup:Manual","operatorName":"x","operatorRole":"Administrator","processId":' + $PID + ',"acquiredUtc":"' + $now + '","heartbeatUtc":"' + $now + '"}' | Set-Content -Path (Join-Path $folder 'operation.lock.json')
            New-Item -ItemType File -Path (Join-Path $folder '.tmp-half.sql') | Out-Null
            New-Item -ItemType File -Path (Join-Path $env:TEMP ('.blazorstoc-dump-bwtest' + $env:BW_MARK + '.cnf')) | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $env:TEMP ('blazorstoc-restore-bwtest' + $env:BW_MARK)) | Out-Null
            '{"type":"progress","stage":"Exporting","message":"lucreaza"}'
            Start-Sleep -Seconds 60
            """;
        var folder = NewFolder();
        var mark = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        // What was in the folder before the worker started is not the worker's: an old half package and the temporary file of a fetch from the share stay.
        var oldHalf = Path.Combine(folder, ".tmp-old.sql");
        var fetching = Path.Combine(folder, ".tmp-fetch-1.zip");
        File.WriteAllText(oldHalf, "x"); File.SetCreationTimeUtc(oldHalf, DateTime.UtcNow.AddHours(-3));
        File.WriteAllText(fetching, "x"); File.SetCreationTimeUtc(fetching, DateTime.UtcNow.AddHours(-3));
        ProcessStartInfo Hanging() { var info = Script(lockScript); info.Environment["BW_LOCKDIR"] = folder; info.Environment["BW_MARK"] = mark; return info; }
        var hanging = Rig(Hanging, ("Backup:TimeoutSeconds", "3"), ("Backup:CancelGraceSeconds", "1"), ("Database:MariaBackupFilesPath", folder));
        var lockPath = MariaAssetPaths.OperationLock(hanging.Configuration);
        var watch = Stopwatch.StartNew();
        var timedOut = await Backups(hanging.Runner, hanging.Modules, hanging.Configuration).CreateBackupAsync(BackupKind.Manual);
        check(timedOut.ErrorMessage == BackupWorkerRules.WorkerTimeoutMessage && watch.Elapsed < TimeSpan.FromSeconds(30) && hanging.Modules.StatusOf("backup") == ModuleStatus.Degraded,
            $"Backup worker: a worker over the time limit is stopped, the page gets the timeout message and the module counts it ({watch.Elapsed.TotalSeconds:0.0} s)");
        check(!File.Exists(lockPath) && MaintenanceGate.Active is null, "Backup worker: the lock of a worker that was stopped is released at once, not left to expire");
        check(!File.Exists(Path.Combine(folder, ".tmp-half.sql")) && !File.Exists(Path.Combine(Path.GetTempPath(), $".blazorstoc-dump-bwtest{mark}.cnf")) && !Directory.Exists(Path.Combine(Path.GetTempPath(), $"blazorstoc-restore-bwtest{mark}")),
            "Backup worker: what a stopped backup left behind (half package, file with the account password, unpacked folder) is removed at once");
        check(File.Exists(oldHalf) && File.Exists(fetching), "Backup worker: the clean-up leaves what was there before the worker started and the temporary file of a fetch");

        var foreign = Path.Combine(NewFolder(), "operation.lock.json");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        File.WriteAllText(foreign, $$"""{"operation":"restore","operatorName":"x","operatorRole":"Administrator","processId":{{Environment.ProcessId}},"acquiredUtc":"{{DateTime.UtcNow:o}}","heartbeatUtc":"{{DateTime.UtcNow:o}}"}""");
        check(!FileOperationLockService.ReleaseIfOwnedBy(foreign, Environment.ProcessId + 1) && File.Exists(foreign) && FileOperationLockService.ReleaseIfOwnedBy(foreign, Environment.ProcessId) && !File.Exists(foreign),
            "Backup worker: only the lock written by the stopped worker itself is removed, never another operation's");

        // A worker that answers "cancel": the operation ends cleanly and quickly.
        var polite = Rig(() => Script(ReadRequest + """
            '{"type":"progress","stage":"Exporting","message":"lucreaza"}'
            while ($true) { $line = [Console]::In.ReadLine(); if ($line -eq $null -or $line -eq 'cancel') { break } }
            '{"type":"cancelled"}'
            """), ("Backup:CancelGraceSeconds", "20"));
        using var cancel = new CancellationTokenSource();
        var cancelled = false;
        watch.Restart();
        try
        {
            await Backups(polite.Runner, polite.Modules, polite.Configuration).CreateBackupAsync(BackupKind.Manual,
                new SyncProgress<BackupProgress>(_ => cancel.CancelAfter(TimeSpan.FromMilliseconds(200))), cancel.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && watch.Elapsed < TimeSpan.FromSeconds(15) && polite.Modules.StatusOf("backup") == ModuleStatus.Healthy,
            $"Backup worker: cancelling the request (the user leaves the page) asks the worker to stop and waits for it, and is not a failure of the module ({watch.Elapsed.TotalSeconds:0.0} s)");

        // A worker that does not answer "cancel" loses its process tree after the grace.
        var stubborn = Rig(() => Script(ReadRequest + "'{\"type\":\"progress\",\"stage\":\"Exporting\",\"message\":\"lucreaza\"}'; Start-Sleep -Seconds 60"), ("Backup:CancelGraceSeconds", "1"));
        using var impatient = new CancellationTokenSource();
        var stopped = false;
        watch.Restart();
        try
        {
            await Backups(stubborn.Runner, stubborn.Modules, stubborn.Configuration).CreateBackupAsync(BackupKind.Manual,
                new SyncProgress<BackupProgress>(_ => impatient.CancelAfter(TimeSpan.FromMilliseconds(200))), impatient.Token);
        }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped && watch.Elapsed < TimeSpan.FromSeconds(30), $"Backup worker: a worker that ignores the request to stop is killed after the grace period ({watch.Elapsed.TotalSeconds:0.0} s)");

        var one = Rig(() => { Thread.Sleep(3000); return Script(Done); }, ("Backup:MaxParallel", "1"));
        var holder = Task.Run(() => Backups(one.Runner, one.Modules, one.Configuration).CreateBackupAsync(BackupKind.Manual));
        await Task.Delay(500);
        using var waitingCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var queuedCancelled = false;
        try { await Backups(one.Runner, one.Modules, one.Configuration).CreateBackupAsync(BackupKind.Manual, null, waitingCancel.Token); } catch (OperationCanceledException) { queuedCancelled = true; }
        await holder;
        check(queuedCancelled, "Backup worker: a request waiting for a free worker can be cancelled");
    }

    // ---- limits ----

    private static async Task LimitsAsync(Action<bool, string> check)
    {
        ProcessStartInfo? seen = null;
        var capped = Rig(() => { seen = Script(ReadRequest + Done); return seen; }, ("Backup:MaxMemoryMb", "1024"));
        await Backups(capped.Runner, capped.Modules, capped.Configuration).CreateBackupAsync(BackupKind.Manual);
        check(seen?.Environment["DOTNET_GCHeapHardLimit"] == "40000000", "Backup worker: the worker gets a heap limit from Backup:MaxMemoryMb (1024 MB)");
        check(seen is { RedirectStandardInput: true, RedirectStandardOutput: true, RedirectStandardError: true, CreateNoWindow: true, UseShellExecute: false },
            "Backup worker: the worker runs without a window, talking over the standard streams");

        ProcessStartInfo? plain = null;
        var uncapped = Rig(() => { plain = Script(Done); return plain; });
        await Backups(uncapped.Runner, uncapped.Modules, uncapped.Configuration).CreateBackupAsync(BackupKind.Manual);
        check(plain is not null && !plain.Environment.ContainsKey("DOTNET_GCHeapHardLimit"), "Backup worker: without a configured limit the worker's heap is not capped");

        var runner = new BackupWorkerRunner(Settings(("Backup:TimeoutSeconds", "100"), ("Backup:RestoreTimeoutSeconds", "200"), ("Backup:NasTimeoutSeconds", "300")));
        check(runner.TimeoutOf(BackupOperations.Backup) == TimeSpan.FromSeconds(100) && runner.TimeoutOf(BackupOperations.Restore) == TimeSpan.FromSeconds(200)
            && runner.TimeoutOf(BackupOperations.NasList) == TimeSpan.FromSeconds(300) && new BackupWorkerRunner(Settings()).TimeoutOf(BackupOperations.Restore) > new BackupWorkerRunner(Settings()).TimeoutOf(BackupOperations.Backup),
            "Backup worker: the time limits follow Backup:* and a restoration is given longer than a backup by default");

        var start = BackupWorkerRunner.DefaultStartInfo();
        check(start.ArgumentList.Contains(BackupWorkerRules.Argument) && start.WorkingDirectory == Environment.CurrentDirectory, "Backup worker: the worker starts again from the application's own folder (it reads the same files) with --backup-worker");
    }

    // ---- the real worker, without a database ----

    private static async Task RealWorkerAsync(Action<bool, string> check)
    {
        var configuration = Settings(("Database:MariaBackupFilesPath", NewFolder()));
        var modules = new ModuleRegistry(new ManualTimeProvider());
        modules.Add(new BackupModule(), new ServiceCollection(), configuration);
        var runner = new BackupWorkerRunner(configuration);
        var failed = false;
        var watch = Stopwatch.StartNew();
        try { await runner.RunAsync<BackupResult>(new BackupJobRequest("no-such-operation", "claude.test", true), null, CancellationToken.None); }
        catch (BackupWorkerException) { failed = true; }
        check(failed, $"Backup worker: the real worker started from the application reads its request, does not know the operation and ends without an answer ({watch.Elapsed.TotalSeconds:0.0} s)");

        var arguments = BackupWorker.BuildConfiguration(["--Database:Name=from-the-command-line"]);
        check(arguments["Database:Name"] == "from-the-command-line", "Backup worker: the worker reads the command-line arguments of the application as configuration (the same database and folders)");
        var odd = BackupWorker.BuildConfiguration(["-x", "value"]);
        check(odd is not null, "Backup worker: a command line the configuration cannot read does not stop the worker");
    }

    // ---- registration ----

    private static void Wiring(Action<bool, string> check)
    {
        ServiceProvider Build(params (string Key, string Value)[] values)
        {
            var configuration = Settings(WithFolder(values));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configuration);
            services.AddSingleton<IAccessControl>(new TestAccessControl(true, "claude.test"));
            services.AddSingleton<IAuditTrail>(new MariaAuditTrail(configuration));
            services.AddSingleton<IOperationLockService>(new FileOperationLockService(MariaAssetPaths.OperationLock(configuration)));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            var registry = new ModuleRegistry();
            services.AddSingleton(registry);
            registry.Add(new BackupModule(), services, configuration);
            return services.BuildServiceProvider();
        }
        using (var worker = Build())
        {
            using var scope = worker.CreateScope();
            var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            check(worker.GetRequiredService<IBackupOperations>().GetType().Name == "OutOfProcessBackupOperations" && backups is NasCopyingBackupService
                && scope.ServiceProvider.GetRequiredService<INasBackupCopier>().GetType().Name == "OutOfProcessNasCopier" && scope.ServiceProvider.GetRequiredService<IDatabaseRestoreService>().GetType().Name == "OutOfProcessRestoreService",
                "Backup worker: by default the services the pages use run in a worker process (the copy to the NAS after the backup is kept)");
        }
        using (var inProcess = Build(("Backup:Mode", "InProcess")))
        {
            using var scope = inProcess.CreateScope();
            check(inProcess.GetRequiredService<IBackupOperations>() is InProcessBackupOperations && scope.ServiceProvider.GetRequiredService<INasBackupCopier>() is NasBackupCopier
                && scope.ServiceProvider.GetRequiredService<IDatabaseRestoreService>() is MariaDatabaseRestoreService, "Backup worker: Backup:Mode=InProcess keeps the classes inside the application (the way back)");
        }
        using (var off = Build(("Modules:backup:Enabled", "false")))
        {
            using var scope = off.CreateScope();
            var made = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().CreateBackupAsync(BackupKind.Manual).GetAwaiter().GetResult();
            var listing = scope.ServiceProvider.GetRequiredService<INasBackupCopier>().ListShareAsync().GetAwaiter().GetResult();
            var restored = scope.ServiceProvider.GetRequiredService<IDatabaseRestoreService>().RestoreAsync("pachet.zip").GetAwaiter().GetResult();
            check(!made.Success && made.ErrorMessage == BackupWorkerRules.UnavailableMessage && !listing.Ok && !restored.Success && restored.ErrorMessage == BackupWorkerRules.UnavailableMessage,
                "Backup worker: with the module switched off the services still resolve (the pages are created) and say that backups are unavailable");
        }
        // The registrations of the real application, in the order of Program.cs: a registration made later by another method would silently replace the module's.
        {
            var configuration = Settings(("Database:MariaBackupFilesPath", NewFolder()));
            var registry = new ModuleRegistry();
            var services = new ServiceCollection();
            BlazorStoc.Startup.ServiceRegistration.AddCoreServices(services);
            BlazorStoc.Startup.ServiceRegistration.AddModules(services, configuration, registry);
            BlazorStoc.Startup.ServiceRegistration.AddBackupServices(services, configuration);
            MaintenanceGate.Configure(null);
            check(new[] { typeof(IDatabaseBackupService), typeof(INasBackupCopier), typeof(IDatabaseRestoreService), typeof(IBackupOperations) }.All(type => services.Count(descriptor => descriptor.ServiceType == type) == 1)
                && services.Last(descriptor => descriptor.ServiceType == typeof(IBackupOperations)).ImplementationFactory is not null,
                "Backup worker: in the registrations of the application the backup, the copier, the restoration and the operations are registered once, by the module");
            check(registry.StatusOf("backup") == ModuleStatus.Healthy, "Backup worker: the module is registered and healthy in the application");
        }
        var module = new BackupModule();
        check(module.Nav.Count == 0 && module.Id == "backup", "Backup worker: the module has no page and no menu entry of its own");
        check(ModuleRegistry.IsFault(new BackupWorkerException("x")) && !ModuleRegistry.IsFault(new BackupOperationException(BackupAnswerLine.Nas, "x")),
            "Backup worker: a dead worker is a failure of the module, a refusal of the share or the database is not");
        var access = new DelegatedAccess("claude.test", false);
        check(access.GetUsernameAsync().Result == "claude.test" && !access.IsAdministratorAsync().Result && access.HasAsync("setari-backup.edit").Result,
            "Backup worker: the worker acts as the user the application already checked (name and role for the journal, the permissions were checked before)");
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Program.cs"));
        check(program.Contains("BackupWorkerRules.Argument") && program.Contains("BackupWorker.RunAsync"), "Backup worker: Program.cs starts the worker mode on --backup-worker");
    }

    private static string ProjectRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "BlazorStoc.csproj"))) root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        return root ?? throw new InvalidOperationException("project folder not found");
    }
}
