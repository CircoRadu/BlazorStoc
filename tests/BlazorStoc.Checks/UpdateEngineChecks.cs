using System.IO.Compression;
using System.Text.Json;
using BlazorStoc.Updater;

namespace BlazorStoc.Checks;

// Stage 3 of the application update (docs/PROPUNERE_ACTUALIZARE.md): the installation of a package with a fake application host and fake release tools, on a temporary
// installation folder. What is checked is the order of the steps, the pointer current.txt at every moment that matters, the refusals that cost no downtime, the rollbacks
// and the state kept for the application. The real processes (supervisor, dotnet, backup worker) are exercised by the run against a real application, not here.
public static partial class UpdateEngineChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-eng-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var keys = new SigningKeyStore(Path.Combine(folder, "key"));
            var keysOther = new SigningKeyStore(Path.Combine(folder, "key-other"));
            var signer = keys.Generate();
            keysOther.Generate();
            LayoutAndKeys(check, folder);
            await FirstInstallAndUpdate(check, folder, keys, signer);
            await Refusals(check, folder, keys, keysOther, signer);
            await Failures(check, folder, keys, signer);
            await Rollback(check, folder, keys, signer);
            await Housekeeping(check, folder, keys, signer);
            await ApplicationAsync(check, folder, keys, keysOther, signer);
            await InstallerAsync(check, folder, keys, keysOther, signer);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // ---- layout and trusted keys ----

    private static void LayoutAndKeys(Action<bool, string> check, string folder)
    {
        var layout = new InstallLayout(Path.Combine(folder, "layout"));
        layout.EnsureFolders();
        check(layout.Current is null && layout.InstalledVersions().Count == 0, "Update engine: an installation with nothing in it has no current version");
        File.WriteAllText(layout.CurrentFile, "..\\..\\evil");
        check(layout.Current is null && ThrowsArgument(() => layout.ReleaseFolder("..\\x")) && ThrowsArgument(() => layout.AppFolder("1.0")) && ThrowsArgument(() => InstallLayout.Checked("1.0.0/../x")),
            "Update engine: only major.minor.patch becomes a folder name, and a garbage current.txt is read as nothing");
        layout.SetCurrent("1.2.3");
        check(layout.Current == "1.2.3" && !File.Exists(layout.CurrentFile + ".tmp"), "Update engine: the pointer is switched through a temporary file that is not left behind");

        var trusted = new TrustedKeys(Path.Combine(folder, "layout", "updater", "trusted-keys.json"));
        var raw = new string('a', 64);
        var spaced = string.Join(' ', Enumerable.Range(0, 8).Select(index => raw.Substring(index * 8, 8))).ToUpperInvariant();
        trusted.Add(spaced, "admin", "test", DateTime.UtcNow);
        trusted.Add(raw, "other", "again", DateTime.UtcNow);
        check(trusted.Load().Count == 1 && trusted.Contains(spaced.ToLowerInvariant()) && trusted.Load()[0].AddedBy == "admin",
            "Update engine: a fingerprint is stored in one normal form (no spaces, lower case) and adding it twice changes nothing");
        check(ThrowsArgument(() => trusted.Add("abc", "x", "x", DateTime.UtcNow)) && ThrowsArgument(() => trusted.Add(new string('z', 64), "x", "x", DateTime.UtcNow)),
            "Update engine: something that is not a 64-hex fingerprint is not accepted");
        check(trusted.Remove(raw) && trusted.Load().Count == 0 && !trusted.Remove(raw), "Update engine: a fingerprint can be removed from the list");
    }

    // ---- the ordinary installation ----

    private static async Task FirstInstallAndUpdate(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = new World(Path.Combine(folder, "w1"));
        var first = await world.PackAsync(keys, "1.0.0");
        var refused = await world.Engine().InstallAsync(world.Request(first));
        check(refused.Outcome == UpdateOutcome.Refused && refused.Message.Contains("does not know", StringComparison.Ordinal) && world.Layout.Current is null && world.Host.Calls.Count == 0
            && !Directory.EnumerateDirectories(world.Layout.ReleasesFolder).Any(), "Update engine: a package of an unknown signer is refused when nobody confirms it, with nothing unpacked or started");

        var installed = await world.Engine().InstallAsync(world.Request(first, trust: signer));
        check(installed.Outcome == UpdateOutcome.Succeeded && world.Layout.Current == "1.0.0" && world.Layout.Previous is null && world.Layout.HasRelease("1.0.0")
            && File.Exists(world.Layout.ManifestFile("1.0.0")) && world.Host.Running == "1.0.0" && new TrustedKeys(world.Layout.TrustedKeysFile).Contains(signer),
            "Update engine: the first installation unpacks, points current.txt at the version, starts it and remembers the signer the administrator confirmed");
        check(world.Host.Calls.SequenceEqual(["stop", "start:1.0.0"]) && world.Tools.Calls.All(call => !call.StartsWith("backup", StringComparison.Ordinal)),
            "Update engine: the first installation has no running version to back up with, so it only stops, switches and starts");
        var status = world.Engine().ReadStatus();
        check(status is { Phase: "done", Outcome: "Succeeded", To: "1.0.0" } && File.ReadAllLines(world.Layout.HistoryFile).Length == 2, "Update engine: the state and the history say how the installation ended (the refusal too)");

        world.Host.Calls.Clear(); world.Tools.Calls.Clear();
        world.Tools.Pending = 2;
        var second = await world.PackAsync(keys, "1.1.0");
        var update = await world.Engine().InstallAsync(world.Request(second));
        check(update.Outcome == UpdateOutcome.Succeeded && world.Layout.Current == "1.1.0" && world.Layout.Previous == "1.0.0" && world.Host.Running == "1.1.0" && update.Backup == "backup-1.pkg",
            "Update engine: an update moves current.txt, keeps the old version as previous and starts the new one");
        check(world.Host.Calls.SequenceEqual(["stop", "start:1.1.0"]) && world.Tools.Calls.SequenceEqual(["schema:1.1.0.partial", "backup:1.0.0", "migrate:1.1.0"]),
            "Update engine: the new version is asked about the schema before the stop, the backup is made by the RUNNING version after the stop, the migrations by the NEW one");
        check(Directory.Exists(world.Layout.AppFolder("1.0.0")) && !Directory.EnumerateDirectories(world.Layout.ReleasesFolder).Any(path => path.EndsWith(".partial", StringComparison.Ordinal)),
            "Update engine: the old version stays on disk for a rollback and no unpacking folder is left");

        world.Host.Calls.Clear(); world.Tools.Calls.Clear();
        world.Tools.Pending = 0;
        var third = await world.PackAsync(keys, "1.2.0");
        await world.Engine().InstallAsync(world.Request(third));
        check(world.Tools.Calls.All(call => !call.StartsWith("migrate", StringComparison.Ordinal)) && world.Layout.Current == "1.2.0", "Update engine: an update that changes no schema runs no migration");
    }

    // ---- refusals cost no downtime ----

    private static async Task Refusals(Action<bool, string> check, string folder, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w2"), keys, signer, "2.0.0");
        async Task<bool> Refused(string package, InstallRequest? request = null, string? because = null, UpdateOptions? options = null)
        {
            world.Host.Calls.Clear(); world.Tools.Calls.Clear();
            var result = await world.Engine(options).InstallAsync(request ?? world.Request(package));
            return result.Outcome == UpdateOutcome.Refused && (because is null || result.Message.Contains(because, StringComparison.OrdinalIgnoreCase))
                && world.Host.Calls.Count == 0 && world.Tools.Calls.All(call => call.StartsWith("schema", StringComparison.Ordinal)) && world.Layout.Current == "2.0.0" && world.Host.Running == "2.0.0"
                && !Directory.EnumerateDirectories(world.Layout.ReleasesFolder).Any(path => path.EndsWith(".partial", StringComparison.Ordinal));
        }
        check(await Refused(await world.PackAsync(keys, "2.0.0"), because: "already installed"), "Update engine: the version that is already installed is refused (nothing is stopped)");
        check(await Refused(await world.PackAsync(keys, "1.9.0"), because: "older"), "Update engine: an older version is refused without --allow-downgrade");
        check(await Refused(await world.PackAsync(keys, "3.0.0", minFrom: "2.5.0"), because: "intermediate"), "Update engine: a package that needs a newer starting version than the installed one is refused");
        check(await Refused(await world.PackAsync(keys, "2.1.0", platform: "linux-x64"), because: "linux-x64"), "Update engine: a package for another platform is refused");
        check(await Refused(await world.PackAsync(keysOther, "2.1.0"), world.Request(await world.PackAsync(keysOther, "2.1.0"), confirm: false), because: "does not know"),
            "Update engine: a package of another signer that nobody confirms is refused");
        check(await Refused(await world.PackAsync(keys, "2.1.0"), because: "room", options: world.Options(freeSpace: 1000)), "Update engine: not enough room on the disk is refused before anything is unpacked");
        var tampered = await world.PackAsync(keys, "2.1.0");
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update)) { zip.GetEntry("app/BlazorStoc.dll")!.Delete(); using var stream = zip.CreateEntry("app/BlazorStoc.dll").Open(); stream.Write("not the real one"u8); }
        check(await Refused(tampered, because: "not valid"), "Update engine: a package changed after signing is refused");
        world.Tools.Status = new SchemaStatus(60, 50, true, 0, [], []);
        check(await Refused(await world.PackAsync(keys, "2.1.0"), because: "newer than"), "Update engine: a database whose schema is newer than the package's is refused");
        world.Tools.Status = null;
        check(await Refused(await world.PackAsync(keys, "2.1.0"), because: "could not read"), "Update engine: a new version that cannot read the state of the database is refused before the stop");
        world.Tools.Status = SchemaStatusOf(0);

        await using (var held = new FileStream(world.Layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            check(await Refused(await world.PackAsync(keys, "2.1.0"), because: "Another update"), "Update engine: two updates at the same time are refused");

        var again = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "2.0.0"), reinstall: true));
        check(again.Outcome == UpdateOutcome.Succeeded && world.Layout.Current == "2.0.0" && world.Layout.Previous is null, "Update engine: --reinstall puts the same version again (repair) and leaves previous alone");
        var down = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "1.5.0"), allowDowngrade: true));
        check(down.Outcome == UpdateOutcome.Succeeded && world.Layout.Current == "1.5.0" && world.Layout.Previous == "2.0.0", "Update engine: --allow-downgrade accepts an older version");
    }

    // ---- whatever fails after the switch goes back ----

    private static async Task Failures(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w3"), keys, signer, "3.0.0");
        world.Tools.Pending = 1;
        world.Host.Broken.Add("3.1.0");
        var broken = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "3.1.0")));
        check(broken.Outcome == UpdateOutcome.RolledBack && world.Layout.Current == "3.0.0" && world.Layout.Previous is null && world.Host.Running == "3.0.0" && world.Host.Up,
            "Update engine: a new version that does not answer is replaced by the old one, which is started again");
        check(world.Tools.Calls.Contains("restore:3.0.0:backup-1.pkg") && broken.Message.Contains("3.0.0", StringComparison.Ordinal),
            "Update engine: after migrations had been started the database is restored from the backup made a moment before (nothing was written in between)");
        check(world.Engine().ReadStatus() is { Phase: "done", Outcome: "RolledBack" }, "Update engine: the state says the update was rolled back");

        world.Tools.Calls.Clear(); world.Host.Broken.Clear();
        world.Tools.FailMigrate = true;
        var migration = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "3.2.0")));
        check(migration.Outcome == UpdateOutcome.RolledBack && world.Layout.Current == "3.0.0" && world.Host.Running == "3.0.0" && world.Tools.Calls.Any(call => call.StartsWith("restore", StringComparison.Ordinal)),
            "Update engine: migrations that fail put the old version back and restore the database");
        world.Tools.FailMigrate = false;

        world.Tools.Calls.Clear();
        world.Tools.FailBackup = true;
        var noBackup = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "3.3.0")));
        check(noBackup.Outcome == UpdateOutcome.RolledBack && world.Layout.Current == "3.0.0" && world.Host.Running == "3.0.0" && world.Tools.Calls.All(call => !call.StartsWith("restore", StringComparison.Ordinal))
            && !world.Tools.Calls.Any(call => call.StartsWith("migrate", StringComparison.Ordinal)), "Update engine: no backup, no update: the old version is started again and nothing is migrated or restored");
        world.Tools.FailBackup = false;

        world.Host.Broken.Add("3.4.0"); world.Host.Broken.Add("3.0.0");
        var worst = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "3.4.0")));
        check(worst.Outcome == UpdateOutcome.Failed && worst.Message.Contains("down", StringComparison.OrdinalIgnoreCase) && world.Layout.Current == "3.0.0", "Update engine: when the old version does not come back either the result says the application is down");
        world.Host.Broken.Clear();

        var fresh = new World(Path.Combine(folder, "w3b"));
        fresh.Host.Broken.Add("1.0.0");
        var first = await fresh.Engine().InstallAsync(fresh.Request(await fresh.PackAsync(keys, "1.0.0"), trust: signer));
        check(first.Outcome == UpdateOutcome.Failed && fresh.Layout.Current is null, "Update engine: a first installation that does not start leaves no current.txt behind");
    }

    // ---- going back by hand ----

    private static async Task Rollback(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w4"), keys, signer, "4.0.0");
        var none = await world.Engine().RollbackAsync("admin", "test");
        check(none.Outcome == UpdateOutcome.Refused && world.Host.Calls.Count == 0, "Update engine: with no earlier version there is nothing to go back to");
        await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "4.1.0")));
        world.Host.Calls.Clear(); world.Tools.Calls.Clear();
        var back = await world.Engine().RollbackAsync("admin", "the new one is wrong", restoreFromBackup: "backup-1.pkg");
        check(back.Outcome == UpdateOutcome.RolledBack && world.Layout.Current == "4.0.0" && world.Layout.Previous == "4.1.0" && world.Host.Running == "4.0.0"
            && world.Host.Calls.SequenceEqual(["stop", "start:4.0.0"]) && world.Tools.Calls.SequenceEqual(["restore:4.0.0:backup-1.pkg"]),
            "Update engine: going back by hand stops, restores the database when asked, swaps the pointers and starts the earlier version");
        var forward = await world.Engine().RollbackAsync("admin", "and forward again");
        check(forward.Outcome == UpdateOutcome.RolledBack && world.Layout.Current == "4.1.0" && world.Layout.Previous == "4.0.0", "Update engine: going back twice returns to where it started");
    }

    // ---- housekeeping ----

    private static async Task Housekeeping(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = new World(Path.Combine(folder, "w5"));
        await world.Engine(keep: 2).InstallAsync(world.Request(await world.PackAsync(keys, "5.0.0"), trust: signer));
        foreach (var version in new[] { "5.1.0", "5.2.0", "5.3.0", "5.4.0" }) await world.Engine(keep: 2).InstallAsync(world.Request(await world.PackAsync(keys, version)));
        var kept = world.Layout.InstalledVersions();
        check(world.Layout.Current == "5.4.0" && world.Layout.Previous == "5.3.0" && kept.SequenceEqual(["5.4.0", "5.3.0"]) && !File.Exists(world.Layout.CurrentFile + ".tmp"),
            "Update engine: only the newest versions are kept (the running one, the previous one and the number asked for), older ones are removed");

        var interrupted = new World(Path.Combine(folder, "w6"));
        interrupted.Layout.EnsureFolders();
        File.WriteAllText(interrupted.Layout.StatusFile, JsonSerializer.Serialize(new UpdateStatus("migrating", "1.0.0", "1.1.0", "admin", "x", DateTime.UtcNow, DateTime.UtcNow, null, "x", null), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await interrupted.Engine().InstallAsync(interrupted.Request(await interrupted.PackAsync(keys, "1.1.0"), trust: signer));
        check(interrupted.Log.Lines.Any(line => line.Contains("did not finish", StringComparison.Ordinal) && line.Contains("migrating", StringComparison.Ordinal)),
            "Update engine: an update that was interrupted earlier is reported in the log and does not stop the next one");
    }

    // ---- helpers ----

    private static SchemaStatus SchemaStatusOf(int pending) => new(10, 10, true, pending, [], []);
    private static bool ThrowsArgument(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }

    private sealed class World
    {
        private int counter;
        public InstallLayout Layout { get; }
        public FakeHost Host { get; }
        public FakeTools Tools { get; } = new();
        public ListLog Log { get; } = new();
        private readonly string folder;

        public World(string folder)
        {
            this.folder = folder;
            Directory.CreateDirectory(folder);
            Layout = new InstallLayout(Path.Combine(folder, "install"));
            Host = new FakeHost(Layout);
        }

        public static async Task<World> InstalledAsync(string folder, SigningKeyStore keys, string signer, string version)
        {
            var world = new World(folder);
            var result = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, version), trust: signer));
            if (result.Outcome != UpdateOutcome.Succeeded) throw new InvalidOperationException("Fixture: " + result.Message);
            world.Host.Calls.Clear(); world.Tools.Calls.Clear();
            return world;
        }

        public UpdateOptions Options(long? freeSpace = null, int keep = 3) =>
            new(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(5), keep, () => freeSpace ?? long.MaxValue / 2);

        public UpdateEngine Engine(UpdateOptions? options = null, int keep = 3) => new(Layout, Host, Tools, Log, options ?? Options(keep: keep));

        public InstallRequest Request(string package, string? trust = null, bool reinstall = false, bool allowDowngrade = false, bool confirm = false) =>
            new(package, "admin.test", "test", reinstall, allowDowngrade, trust, _ => confirm);

        public async Task<string> PackAsync(SigningKeyStore keys, string version, string? minFrom = null, string platform = "win-x64", string? notes = null)
        {
            var app = Path.Combine(folder, "build-" + (++counter));
            Directory.CreateDirectory(Path.Combine(app, "wwwroot"));
            File.WriteAllText(Path.Combine(app, "BlazorStoc.dll"), "dll " + version);
            File.WriteAllText(Path.Combine(app, "wwwroot", "app.css"), "css " + version);
            var describe = JsonSerializer.Serialize(new { version, schemaVersion = 1, migrations = new[] { new { version = 1, description = "one", checksum = new string('1', 64) } } });
            var package = Path.Combine(folder, $"BlazorStoc-{version}-{counter}.bsu");
            await PackageBuilder.PackAsync(app, describe, package, keys, platform, "net9.0", minFrom, DateTime.UtcNow, notes);
            return package;
        }
    }

    private sealed class ListLog : IUpdateLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warn(string message) => Lines.Add(message);
        public void Error(string message) => Lines.Add(message);
    }

    private sealed class FakeHost(InstallLayout layout) : IApplicationHost
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> Broken { get; } = [];
        public string? Running { get; private set; }
        public bool Up { get; private set; }

        public Task StopAsync(CancellationToken cancellationToken) { Calls.Add("stop"); Up = false; Running = null; return Task.CompletedTask; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var version = layout.Current;
            Calls.Add("start:" + version);
            Up = version is not null && !Broken.Contains(version);
            Running = Up ? version : null;
            return Task.CompletedTask;
        }

        public Task<HealthAnswer> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Up ? new HealthAnswer(true, 200, Running) : HealthAnswer.None);
    }

    private sealed class FakeTools : IReleaseTools
    {
        private int backups;
        public List<string> Calls { get; } = [];
        public int Pending { get; set; }
        public SchemaStatus? Status { get; set; } = SchemaStatusOf(0);
        public bool FailBackup { get; set; }
        public bool FailMigrate { get; set; }
        public bool FailRestore { get; set; }

        public Task<SchemaStatus?> SchemaStatusAsync(string appFolder, CancellationToken cancellationToken)
        {
            Calls.Add("schema:" + VersionOf(appFolder));
            return Task.FromResult(Status is null ? null : Status with { Pending = Status.Pending + Pending });
        }

        public Task<string> BackupAsync(string appFolder, string actor, CancellationToken cancellationToken)
        {
            Calls.Add("backup:" + VersionOf(appFolder));
            if (FailBackup) throw new UpdateStepException("The backup of the database was not made.");
            return Task.FromResult($"backup-{++backups}.pkg");
        }

        public Task MigrateAsync(string appFolder, CancellationToken cancellationToken)
        {
            Calls.Add("migrate:" + VersionOf(appFolder));
            return FailMigrate ? throw new UpdateStepException("The migrations did not complete.") : Task.CompletedTask;
        }

        public Task RestoreAsync(string appFolder, string packageFileName, string actor, CancellationToken cancellationToken)
        {
            Calls.Add($"restore:{VersionOf(appFolder)}:{packageFileName}");
            return FailRestore ? throw new UpdateStepException("The restoration of the database failed.") : Task.CompletedTask;
        }

        // ".../releases/1.1.0/app" or ".../releases/1.1.0.partial/app" -> "1.1.0" / "1.1.0.partial"
        private static string VersionOf(string appFolder) => Path.GetFileName(Path.GetDirectoryName(appFolder)!);
    }
}
