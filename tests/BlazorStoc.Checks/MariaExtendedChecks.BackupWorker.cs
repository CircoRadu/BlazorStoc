using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using BlazorStoc.Modules;
using BlazorStoc.Modules.Backup;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // The real worker process against the test database: a real backup (package, manifest, checksum, journal entry for the user, lock taken and released by the worker),
    // the lock held by someone else, a cancellation in the middle, the restoration up to the point where it would change the database (the restore account is not given to
    // the worker, so nothing is ever swapped here), and the operations of the NAS copier with the share switched off. Needs mariadb-dump (MARIA_DUMP_PATH or
    // Database:MariaDumpExecutablePath, else the development copy under C:\Dev) and the dump privileges (SHOW VIEW, TRIGGER, LOCK TABLES) of the test account on blazorstoc_test.
    private static async Task BackupWorkerAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var dump = Environment.GetEnvironmentVariable("MARIA_DUMP_PATH") ?? configuration["Database:MariaDumpExecutablePath"]
            ?? @"C:\Dev\BlazorStoc-MariaDB\mariadb-11.4.13-winx64\bin\mariadb-dump.exe";
        var client = Path.Combine(Path.GetDirectoryName(dump)!, "mariadb.exe");
        if (!File.Exists(dump)) { Console.WriteLine($"SKIP: mariadb-dump not found at {dump} (set MARIA_DUMP_PATH); the real backup worker was not exercised."); return; }

        var folder = MariaAssetPaths.DatabaseBackups(configuration);
        var lockPath = MariaAssetPaths.OperationLock(configuration);
        var testConfig = Environment.GetEnvironmentVariable("MARIA_TEST_CONFIG_PATH")!;
        var isolatedDataRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-worker-dataroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDataRoot);
        ProcessStartInfo Launch()
        {
            var info = BackupWorkerRunner.DefaultStartInfo();
            info.Environment["Database__PrivateConfigPath"] = testConfig;
            // The worker reads the override file and the saved accounts of the data folder (AddEffectiveConfiguration): an empty folder of its own keeps the test independent of
            // what the real installation saved there (another port, renamed accounts).
            info.Environment["App__DataRoot"] = isolatedDataRoot;
            // The dedicated accounts are NOT given to the worker: the dump runs as the test account and a restoration stops before it would change anything.
            foreach (var account in new[] { "Backup", "Restore", "Migrator" }) info.Environment[$"Database__{account}PrivateConfigPath"] = Path.Combine(Path.GetTempPath(), "no-such-account.json");
            info.Environment["Database__MariaAssetsRoot"] = configuration["Database:MariaAssetsRoot"];
            info.Environment["Database__MariaDumpExecutablePath"] = dump;
            info.Environment["Database__MariaClientExecutablePath"] = client;
            return info;
        }
        var modules = new ModuleRegistry();
        modules.Add(new BackupModule(), new ServiceCollection(), configuration);
        var runner = new BackupWorkerRunner(configuration, launch: Launch);
        var tester = new TestAccessControl(true, "backup.worker.tester");
        var backups = new OutOfProcessBackupService(runner, modules, tester, configuration, audit);
        var locks = new FileOperationLockService(lockPath);

        // The lock is shared with the worker through its file: held here, the worker is refused (and says so the way the service does).
        BackupResult refused;
        await using (var held = await locks.TryAcquireAsync("test", "someone", AccessRoles.Administrator))
        {
            Check(held is not null, "The test takes the shared lock");
            refused = await backups.CreateBackupAsync(BackupKind.Manual);
        }
        Check(!refused.Success && refused.ErrorMessage == OperationLockRules.HeldMessage && modules.StatusOf("backup") == ModuleStatus.Healthy,
            "Worker: a backup asked while another operation holds the lock is refused with the message of the lock, and the module stays Healthy");

        // A real backup in the worker. Two files of the images and documents are put in place first: the package must carry them.
        var assetStoresNow = BackupAssets.Stores(configuration);
        var markerOne = Path.Combine(assetStoresNow[0].Root, "checks-worker-" + Guid.NewGuid().ToString("N") + ".bin");
        var markerTwoFolder = Path.Combine(assetStoresNow[3].Root, "checks-worker-" + Guid.NewGuid().ToString("N"), "deep");
        Directory.CreateDirectory(assetStoresNow[0].Root);
        Directory.CreateDirectory(markerTwoFolder);
        await File.WriteAllBytesAsync(markerOne, [1, 2, 3, 250, 251]);
        await File.WriteAllTextAsync(Path.Combine(markerTwoFolder, "ținută.txt"), "document");
        var since = DateTime.UtcNow;
        var stages = new List<BackupStage>();
        string? activeDuringRun = null;
        MaintenanceGate.Configure(lockPath);
        BackupResult made;
        try
        {
            made = await backups.CreateBackupAsync(BackupKind.Manual, new SyncProgress<BackupProgress>(step =>
            {
                stages.Add(step.Stage);
                if (step.Stage == BackupStage.Exporting) activeDuringRun = MaintenanceGate.Active?.Operation;
            }));
            Check(MaintenanceGate.Active is null, "Worker: the application sees the lock gone as soon as the worker ends");
        }
        finally { MaintenanceGate.Configure(null); }
        Check(made.Success && made.PackagePath is not null && File.Exists(made.PackagePath) && File.Exists(made.PackagePath + ".sha256"), $"Worker: a real backup leaves a package and its checksum ({made.ErrorMessage})");
        Check(stages.SequenceEqual([BackupStage.Locking, BackupStage.CheckingSpace, BackupStage.Exporting, BackupStage.Verifying, BackupStage.Saving, BackupStage.Done]), "Worker: the page gets the stages of the backup in order");
        Check(activeDuringRun == "backup:Manual", "Worker: while the worker exports, the application sees the lock the worker holds (the sessions go to the waiting page)");
        Check(!File.Exists(lockPath), "Worker: the lock file is gone after the backup");
        using (var archive = ZipFile.OpenRead(made.PackagePath!))
            Check(archive.GetEntry("dump.sql") is { Length: > 0 } && archive.GetEntry("manifest.json") is not null, "Worker: the package holds the dump and the manifest");
        var manifest = await BackupPackageStore.TryReadManifestAsync(made.PackagePath!, CancellationToken.None);
        Check(manifest is { OperatorName: "backup.worker.tester", OperatorRole: AccessRoles.Administrator, Kind: "Manual" } && manifest.TableCount > 20 && manifest.RowCounts.ContainsKey("products"),
            "Worker: the manifest names the user the application passed on, the kind and the tables");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(made.PackagePath!))).ToLowerInvariant();
        Check(hash == (await File.ReadAllTextAsync(made.PackagePath + ".sha256")).Trim(), "Worker: the checksum file is the SHA-256 of the package");
        IReadOnlyList<BackupAssetEntry>? packed;
        bool filesVerified;
        using (var archive = ZipFile.OpenRead(made.PackagePath!))
        {
            packed = await BackupAssets.ReadListAsync(archive, CancellationToken.None);
            filesVerified = packed is not null && await BackupAssets.VerifyAsync(archive, packed, CancellationToken.None);
        }
        var folderFiles = BackupAssets.Scan(assetStoresNow);
        Check(manifest is { FileCount: > 1 } && manifest.FileCount == folderFiles.Count && manifest.FilesBytes == folderFiles.Sum(file => file.Length) && packed is not null && packed.Count == folderFiles.Count
            && manifest.AssetsSha256 == BackupAssets.Signature(packed) && filesVerified && packed.Any(entry => entry.Store == "product-images" && entry.Path == Path.GetFileName(markerOne) && entry.Size == 5)
            && packed.Any(entry => entry.Store == "archive-files" && entry.Path.EndsWith("/deep/ținută.txt", StringComparison.Ordinal)),
            $"Worker: the package carries the images and documents of the folders (a nested file with a non-ASCII name too), listed with sizes and checksums, and the manifest holds only their count and signature ({manifest?.FileCount} / {folderFiles.Count})");
        Check(made.PackageFileName is not null && (await backups.ListPackagesAsync()).FirstOrDefault(package => package.FileName == made.PackageFileName)?.FileCount == manifest?.FileCount,
            "Worker: the list of packages shows how many files each one carries");
        File.Delete(markerOne);
        Directory.Delete(Path.GetDirectoryName(markerTwoFolder)!, recursive: true);
        Check(made.PackageFileName!.Contains("backup worker tester", StringComparison.Ordinal), "Worker: the package name carries the user");
        var events = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc >= since.AddSeconds(-2)).ToList();
        Check(events.Any(item => item.EntityType == AuditEntities.DatabaseBackup && item.Action == AuditActions.Normalize(AuditActions.Generate) && item.Target == made.PackageFileName && item.ActorUsername == "backup.worker.tester"),
            "Worker: the journal records the backup as made by the user the application passed on (the worker writes it)");

        // The same classes in-process give the same kind of package.
        var withDump = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MariaDumpExecutablePath"] = dump }).Build();
        var inProcess = await new MariaDatabaseBackupService(withDump, tester, locks, audit, NullLogger<MariaDatabaseBackupService>.Instance).CreateBackupAsync(BackupKind.Manual);
        var inProcessManifest = inProcess.Success ? await BackupPackageStore.TryReadManifestAsync(inProcess.PackagePath!, CancellationToken.None) : null;
        Check(inProcess.Success, $"The same backup made inside the application works ({inProcess.ErrorMessage})");
        Check(inProcessManifest is not null && inProcessManifest.TableCount == manifest!.TableCount && inProcessManifest.SchemaVersion == manifest.SchemaVersion
            && inProcessManifest.RowCounts.Keys.Order().SequenceEqual(manifest.RowCounts.Keys.Order()), "Worker: a backup made in the application and one made in the worker have the same tables and the same schema version");

        var listed = await backups.ListPackagesAsync();
        Check(listed.Any(item => item.FileName == made.PackageFileName && item.Kind == BackupKind.Manual && item.OperatorName == "backup.worker.tester"), "Worker: the packages it made are listed by the application");

        // Cancelled in the middle of the export: the worker stops cleanly, leaves no temporary file and no lock, and the next backup works.
        using var cancel = new CancellationTokenSource();
        var cancelled = false;
        var watch = Stopwatch.StartNew();
        try { await backups.CreateBackupAsync(BackupKind.Manual, new SyncProgress<BackupProgress>(step => { if (step.Stage == BackupStage.Exporting) cancel.Cancel(); }), cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && watch.Elapsed < TimeSpan.FromSeconds(60) && modules.StatusOf("backup") == ModuleStatus.Healthy, $"Worker: a backup cancelled while exporting ends as cancelled, not as a failure of the module ({watch.Elapsed.TotalSeconds:0.0} s)");
        await Task.Delay(500);
        Check(!File.Exists(lockPath) && !Directory.EnumerateFiles(folder, ".tmp-*").Any(), "Worker: a cancelled backup leaves no lock and no temporary file");
        var again = await backups.CreateBackupAsync(BackupKind.Manual);
        Check(again.Success, "Worker: after a cancelled backup the next one works");

        // The restoration, up to the point where it would change the database.
        var restore = new OutOfProcessRestoreService(runner, modules, tester);
        var restoreStages = new List<RestoreStage>();
        var restoreResult = await restore.RestoreAsync(made.PackageFileName!, new SyncProgress<RestoreProgress>(step => restoreStages.Add(step.Stage)));
        Check(!restoreResult.Success && restoreResult.ErrorMessage == RestoreRules.MigratorNotConfiguredMessage && restoreResult.PreRestorePackageFileName is { Length: > 0 }
            && File.Exists(Path.Combine(folder, restoreResult.PreRestorePackageFileName!)), $"Worker: a restoration takes the snapshot of the current database first and stops before swapping when the restore account is not given ({restoreResult.ErrorMessage})");
        Check(restoreStages.SequenceEqual([RestoreStage.Locking, RestoreStage.SnapshotCurrent, RestoreStage.VerifyingPackage, RestoreStage.ComparingStructure, RestoreStage.ComparingContent, RestoreStage.Importing]),
            "Worker: the page gets the stages of the restoration in order");
        Check(!File.Exists(lockPath), "Worker: the restoration releases the lock");
        var missing = await restore.RestoreAsync("no-such-package.zip");
        Check(!missing.Success && missing.ErrorMessage == RestoreRules.PackageNotFoundMessage, "Worker: a package that is not on the server is refused with the message of the restoration");

        // The NAS copier: the share is off in the test database, so nothing touches the network.
        var copier = new OutOfProcessNasCopier(runner, modules, tester, configuration, NullLogger.Instance);
        var listing = await copier.ListShareAsync();
        var after = await copier.CopyAfterBackupAsync(made.PackageFileName);
        var badPath = await copier.TestAsync(new NasBackupInput("not-a-network-path", "user", "secret", true));
        Check(!listing.Ok && listing.Message.Length > 0 && after.Ok && after.Copied == 0 && !badPath.Ok && badPath.Message == NasBackupRules.PathMessage,
            "Worker: the NAS copier runs in the worker (share off: nothing copied; a path that is not a network path is refused with the message of the form)");
        Check(modules.StatusOf("backup") == ModuleStatus.Healthy, "Worker: after all of these real operations the module is Healthy");
    }
}
