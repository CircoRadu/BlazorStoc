using System.Text.Json;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using BlazorStoc.Services.Update;
using BlazorStoc.Updater;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// Stage 4 of the application update (docs/PROPUNERE_ACTUALIZARE.md): Setari -> Actualizare. The requests the application leaves in updates/staged and the updater that answers them
// (RequestProcessor, the REAL class, on a temporary installation with the fake host and fake release tools of the engine checks), the service of the page, the journal of what the
// updater did, the banner and the screen. What needs real processes (the scheduled task, the elevated updater, the real browser upload) is exercised against a real installation, not here.
public static partial class UpdateEngineChecks
{
    private static async Task ApplicationAsync(Action<bool, string> check, string folder, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        Contracts(check, folder);
        await RequestProcessingAsync(check, folder, keys, keysOther, signer);
        await ServiceAsync(check, folder, keys, keysOther, signer);
        await JournalAsync(check, folder, keys, signer);
        await ScreenAsync(check, folder, keys, signer);
    }

    // ---- the files the two programs exchange ----

    private static void Contracts(Action<bool, string> check, string folder)
    {
        var root = Path.Combine(folder, "contracts", "Install Root");
        foreach (var variant in new[] { root, root.ToUpperInvariant(), root + Path.DirectorySeparatorChar })
            if (UpdaterTask.NameFor(variant) != UpdateInstallation.TaskNameFor(variant)) { check(false, "Update page: the scheduled task has the same name for the updater and for the application"); return; }
        check(UpdaterTask.NameFor(root) == UpdaterTask.NameFor(root.ToUpperInvariant() + "\\") && UpdaterTask.NameFor(root) != UpdaterTask.NameFor(root + "2")
            && UpdaterTask.NameFor(root).StartsWith("BlazorStocUpdater-", StringComparison.Ordinal),
            "Update page: the scheduled task has the same name for the updater and for the application, whatever the case or the trailing slash, and a different one for another folder");

        var sent = new UpdateRequestFile(new string('a', 32), "apply", "upload-x.bsu", new string('b', 64), "admin", "motiv", DateTime.UtcNow);
        var received = JsonSerializer.Deserialize<UpdateRequestDocument>(JsonSerializer.Serialize(sent, InstallLayout.Json), UpdateJson.Options);
        check(received is not null && received.Id == sent.Id && received.Kind == sent.Kind && received.Package == sent.Package && received.Sha256 == sent.Sha256 && received.Actor == "admin" && received.Reason == "motiv",
            "Update page: a request written by the application is read by the updater and the other way round");
        var report = new PackageCheckReport("id", "p.bsu", "h", 5, true, ["x"], "1.0.0", "0.9.0", 3, 2, 1, DateTime.UtcNow, "0.9.0", "sig", true, true, null, "d", 4, 6, "notes", DateTime.UtcNow);
        var seen = JsonSerializer.Deserialize<PackageCheckDocument>(JsonSerializer.Serialize(report, InstallLayout.Json), UpdateJson.Options);
        check(seen is not null && seen.Version == "1.0.0" && seen.Running == "0.9.0" && seen.NewMigrations == 1 && seen.Trusted == true && seen.CanInstall && seen.Notes == "notes" && seen.Problems.Count == 1,
            "Update page: the report of the updater is read by the application field by field");
        var entry = new UpdateHistoryEntry(DateTime.UtcNow, "1.0.0", "1.1.0", "Succeeded", "admin", "motiv", "sig", "b.pkg", 4.5, "ok", "hash", UpdateVia.Application, UpdateKind.Install);
        var line = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var parsed = JsonSerializer.Deserialize<UpdateHistoryDocument>(line, UpdateJson.Options);
        check(parsed is not null && parsed.Package == "hash" && parsed.Via == "aplicatie" && parsed.Kind == "install" && parsed.Outcome == "Succeeded" && parsed.Backup == "b.pkg",
            "Update page: a line of the history of the updater is read by the application");
        var status = new UpdateStatus("done", "1.0.0", "1.1.0", "admin", "motiv", DateTime.UtcNow, DateTime.UtcNow, "Succeeded", "ok", null);
        var statusSeen = JsonSerializer.Deserialize<UpdateStatusDocument>(JsonSerializer.Serialize(status, InstallLayout.Json), UpdateJson.Options);
        check(statusSeen is { Phase: "done", Outcome: "Succeeded", To: "1.1.0" }, "Update page: the state of the updater is read by the application");
        var oldLine = JsonSerializer.Deserialize<UpdateHistoryDocument>("{\"atUtc\":\"2026-10-20T10:00:00Z\",\"from\":\"1.0.0\",\"to\":\"1.1.0\",\"outcome\":\"Succeeded\",\"actor\":\"a\",\"reason\":\"r\",\"seconds\":1,\"message\":\"m\"}", UpdateJson.Options);
        check(oldLine is { Via: "installer", Kind: "install", Package: null }, "Update page: a line of the history written before these fields existed is read as an installer update");

        var plain = new UpdateInstallation(Path.Combine(folder, "nowhere"));
        check(!plain.IsManaged && !new UpdateInstallation((string?)null).IsManaged && new UpdateInstallation("  ").Root is null, "Update page: a folder without releases, or no folder, is not an installation made by the updater");
    }

    // ---- the updater answers the requests ----

    private static string Stage(World world, string package, string? name = null)
    {
        Directory.CreateDirectory(world.Layout.StagedFolder);
        name ??= $"upload-{Guid.NewGuid():N}.bsu";
        File.Copy(package, Path.Combine(world.Layout.StagedFolder, name), overwrite: true);
        return name;
    }

    private static string Request(World world, string kind, string package, string? sha = null, string reason = "motiv", string actor = "admin.test", string? id = null)
    {
        id ??= Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(world.Layout.StagedFolder);
        File.WriteAllText(Path.Combine(world.Layout.StagedFolder, RequestProcessor.RequestFileName(id)),
            JsonSerializer.Serialize(new UpdateRequestFile(id, kind, package, sha, actor, reason, DateTime.UtcNow), InstallLayout.Json));
        return id;
    }

    private static string Sha(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    private static PackageCheckReport? ReportOf(World world, string id) =>
        File.Exists(Path.Combine(world.Layout.StateFolder, RequestProcessor.CheckFileName(id)))
            ? JsonSerializer.Deserialize<PackageCheckReport>(File.ReadAllText(Path.Combine(world.Layout.StateFolder, RequestProcessor.CheckFileName(id))), InstallLayout.Json) : null;

    private static async Task RequestProcessingAsync(Action<bool, string> check, string folder, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        var good = new UpdateRequestFile(new string('c', 32), "apply", "upload-1.bsu", new string('d', 64), "admin", "motiv", DateTime.UtcNow);
        check(RequestProcessor.Validate(good) is null && RequestProcessor.Validate(good with { Kind = "check", Sha256 = null, Reason = "" }) is null,
            "Update requests: a well formed request passes; a check needs neither hash nor reason");
        var bad = new[]
        {
            good with { Id = "../x" }, good with { Id = new string('C', 32) }, good with { Kind = "install" }, good with { Package = "..\\evil.bsu" }, good with { Package = "a/b.bsu" },
            good with { Package = "C:\\x.bsu" }, good with { Package = "x.zip" }, good with { Package = ".bsu" }, good with { Package = new string('a', 120) + ".bsu" }, good with { Sha256 = null },
            good with { Sha256 = "xyz" }, good with { Reason = " " }, good with { Reason = new string('r', 501) }, good with { Reason = "a\nb" }, good with { Actor = "" }, good with { Actor = "a\u0001" }
        };
        check(bad.All(request => RequestProcessor.Validate(request) is not null), "Update requests: an id, kind, package name, hash, reason or actor that could reach a path or a line of a file is refused");

        var world = await World.InstalledAsync(Path.Combine(folder, "w-req"), keys, signer, "2.0.0");
        var processor = () => new RequestProcessor(world.Layout, world.Engine(), world.Log);

        // check: a package of a trusted signer
        var newer = await world.PackAsync(keys, "2.1.0", notes: "Corectii si o functie noua");
        var name = Stage(world, newer);
        var id = Request(world, "check", name, reason: "");
        var taken = await processor().ProcessAsync();
        var report = ReportOf(world, id);
        check(taken == 1 && report is { Ok: true, CanInstall: true, Version: "2.1.0", Running: "2.0.0", Trusted: true, Blocking: null } && report.Sha256 == Sha(newer) && report.Notes == "Corectii si o functie noua" && report.FileCount == 2,
            "Update requests: a check reports the version, the running one, the signer, the notes and that the package can be installed");
        check(report is { NewMigrations: 0, SchemaVersion: 1, RunningSchemaVersion: 1 }, "Update requests: the check counts the migrations the package adds over the schema of the running version");
        check(world.Host.Calls.Count == 0 && world.Layout.Current == "2.0.0" && File.Exists(Path.Combine(world.Layout.StagedFolder, name)) && !Directory.EnumerateFiles(world.Layout.StagedFolder, "request-*").Any(),
            "Update requests: a check changes nothing (nothing stopped, the package stays staged) and the request is removed");

        // check: signer not known, damaged, older, another platform
        var strangerName = Stage(world, await world.PackAsync(keysOther, "2.2.0"));
        var strangerId = Request(world, "check", strangerName);
        var damagedPackage = await world.PackAsync(keys, "2.3.0");
        using (var zip = System.IO.Compression.ZipFile.Open(damagedPackage, System.IO.Compression.ZipArchiveMode.Update))
        {
            zip.GetEntry("app/BlazorStoc.dll")!.Delete();
            using var stream = zip.CreateEntry("app/BlazorStoc.dll").Open();
            stream.Write("not the real one"u8);
        }
        var damagedId = Request(world, "check", Stage(world, damagedPackage));
        var olderId = Request(world, "check", Stage(world, await world.PackAsync(keys, "1.0.0")));
        var otherPlatformId = Request(world, "check", Stage(world, await world.PackAsync(keys, "2.4.0", platform: "linux-x64")));
        await processor().ProcessAsync();
        check(ReportOf(world, strangerId) is { Ok: true, CanInstall: false, Trusted: false, Blocking: "untrusted" } stranger && stranger.Detail!.Contains("does not know", StringComparison.Ordinal),
            "Update requests: a package signed with a key the server does not know cannot be installed from the application");
        check(ReportOf(world, damagedId) is { Ok: false, CanInstall: false, Blocking: "invalid" } damaged && damaged.Problems.Count > 0, "Update requests: a damaged package is reported invalid with the problems found");
        check(ReportOf(world, olderId) is { Ok: true, CanInstall: false, Blocking: "refused" } older && older.Detail!.Contains("older", StringComparison.Ordinal), "Update requests: an older version is reported as refused");
        check(ReportOf(world, otherPlatformId) is { CanInstall: false, Blocking: "refused" } platform && platform.Detail!.Contains("linux-x64", StringComparison.Ordinal), "Update requests: a package for another platform is reported as refused");

        // garbage and strangers
        File.WriteAllText(Path.Combine(world.Layout.StagedFolder, "request-garbage.json"), "{ not json");
        File.WriteAllText(Path.Combine(world.Layout.StagedFolder, "request-big.json"), new string(' ', 20000));
        var missingId = Request(world, "check", "upload-missing.bsu");
        var badId = Request(world, "apply", "..\\x.bsu", sha: new string('0', 64));
        world.Host.Calls.Clear();
        await processor().ProcessAsync();
        check(!Directory.EnumerateFiles(world.Layout.StagedFolder, "request-*").Any() && ReportOf(world, missingId) is null && ReportOf(world, badId) is null && world.Host.Calls.Count == 0 && world.Layout.Current == "2.0.0",
            "Update requests: a request that is not JSON, is too big, names a missing package or a path is removed and does nothing");

        // apply: the wrong file, an unknown signer, a downgrade
        var hashOfCheck = Sha(newer);
        File.AppendAllText(Path.Combine(world.Layout.StagedFolder, name), "tampered");
        Request(world, "apply", name, sha: hashOfCheck);
        world.Host.Calls.Clear();
        await processor().ProcessAsync();
        var lastLine = JsonSerializer.Deserialize<UpdateHistoryEntry>(File.ReadAllLines(world.Layout.HistoryFile)[^1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        check(world.Layout.Current == "2.0.0" && world.Host.Calls.Count == 0 && lastLine is { Outcome: "Refused", Via: "aplicatie" } && lastLine.Message.Contains("changed", StringComparison.Ordinal),
            "Update requests: a package that is not the file the administrator was shown is refused before anything is touched, and the refusal is in the history");
        Request(world, "apply", strangerName, sha: Sha(Path.Combine(world.Layout.StagedFolder, strangerName)));
        await processor().ProcessAsync();
        check(world.Layout.Current == "2.0.0" && world.Host.Calls.Count == 0 && new TrustedKeys(world.Layout.TrustedKeysFile).Fingerprints().Count == 1,
            "Update requests: the application cannot make the server trust a new key (an unknown signer is refused and the list of keys is unchanged)");
        Request(world, "apply", Stage(world, await world.PackAsync(keys, "1.5.0"), "upload-down.bsu"), sha: Sha(Path.Combine(world.Layout.StagedFolder, "upload-down.bsu")));
        await processor().ProcessAsync();
        check(world.Layout.Current == "2.0.0" && world.Host.Calls.Count == 0, "Update requests: a downgrade cannot be asked for from the application");

        // apply: the file that was checked
        var fresh = Stage(world, newer, "upload-fresh.bsu");
        Request(world, "apply", fresh, sha: hashOfCheck, reason: "corectii", actor: "ion.popescu");
        await processor().ProcessAsync();
        var applied = JsonSerializer.Deserialize<UpdateHistoryEntry>(File.ReadAllLines(world.Layout.HistoryFile)[^1], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        check(world.Layout.Current == "2.1.0" && world.Layout.Previous == "2.0.0" && applied is { Outcome: "Succeeded", Via: "aplicatie", Kind: "install", Actor: "ion.popescu", Reason: "corectii" } && applied.Package == hashOfCheck,
            "Update requests: the package that was checked and confirmed is installed, and the history keeps who asked, why, from where and the hash of the package");
        check(!File.Exists(Path.Combine(world.Layout.StagedFolder, fresh)) && Directory.GetFiles(world.Layout.AppliedFolder, $"BlazorStoc-2.1.0-{hashOfCheck[..12]}.bsu").Length == 1,
            "Update requests: the package of an update that worked is moved to updates/applied with its hash");

        // housekeeping
        var stale = Path.Combine(world.Layout.StagedFolder, "upload-stale.bsu");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));
        var staleReport = Path.Combine(world.Layout.StateFolder, "check-" + new string('e', 32) + ".json");
        File.WriteAllText(staleReport, "{}");
        File.SetLastWriteTimeUtc(staleReport, DateTime.UtcNow.AddDays(-3));
        var recent = Path.Combine(world.Layout.StagedFolder, "upload-recent.bsu");
        File.WriteAllText(recent, "x");
        await processor().ProcessAsync();
        check(!File.Exists(stale) && !File.Exists(staleReport) && File.Exists(recent), "Update requests: uploads and reports nobody came back for are removed after two days; recent ones stay");
    }

    // ---- the service of the page, with the real updater code answering through a fake launcher ----

    private sealed class ProcessorLauncher(World world) : IUpdateLauncher
    {
        public string? Error { get; set; }
        public int Starts { get; private set; }
        public async Task<string?> StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            if (Error is not null) return Error;
            await new RequestProcessor(world.Layout, world.Engine(), world.Log).ProcessAsync(cancellationToken);
            return null;
        }
    }

    private sealed class Page
    {
        public World World { get; }
        public UpdateInstallation Installation { get; }
        public ProcessorLauncher Launcher { get; }
        public TestAuditTrail Audit { get; } = new();
        public UpdateJournal Journal { get; }
        public UpdateCoordinator Coordinator { get; }
        public ApplicationUpdateService Service { get; }
        public string StateFile { get; }

        public Page(World world, bool administrator = true, TimeSpan? notice = null, UpdateInstallation? installation = null)
        {
            World = world;
            Installation = installation ?? new UpdateInstallation(world.Layout.Root);
            Launcher = new ProcessorLauncher(world);
            StateFile = Path.Combine(world.Layout.Root, "..", "update-journal.json");
            Journal = new UpdateJournal(Installation, Audit, StateFile, NullLogger<UpdateJournal>.Instance) { RetryDelay = TimeSpan.FromMilliseconds(5), Attempts = 3 };
            Coordinator = new UpdateCoordinator(Installation, Launcher, Journal, TimeProvider.System, NullLogger<UpdateCoordinator>.Instance) { PollInterval = TimeSpan.FromMilliseconds(20) };
            Service = new ApplicationUpdateService(Installation, Launcher, Coordinator, Journal, new TestAccessControl(administrator, "admin.test"), Audit, TimeProvider.System)
            { Notice = notice ?? TimeSpan.Zero, CheckPoll = TimeSpan.FromMilliseconds(10), CheckTimeout = TimeSpan.FromSeconds(5) };
        }

        public async Task<PackageCheckDocument> UploadAsync(string package) { await using var stream = File.OpenRead(package); return await Service.UploadAndCheckAsync(stream, Path.GetFileName(package)); }

        public async Task<bool> WaitAsync(Func<bool> done, int milliseconds = 8000)
        {
            for (var waited = 0; waited < milliseconds; waited += 25) { if (done()) return true; await Task.Delay(25); }
            return done();
        }
    }

    private static async Task ServiceAsync(Action<bool, string> check, string folder, SigningKeyStore keys, SigningKeyStore keysOther, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w-svc"), keys, signer, "3.0.0");
        var page = new Page(world);
        var overview = page.Service.Overview();
        check(overview.Managed && overview.Current == "3.0.0" && overview.Installed.SequenceEqual(["3.0.0"]) && overview.Scheduled is null && overview.UnacknowledgedFailure is null && overview.History.Count == 1,
            "Update page: the overview says the installation, the version on disk and the history");

        // refusals before anything is stored
        var denied = new Page(world, administrator: false);
        var newer = await world.PackAsync(keys, "3.1.0", notes: "Note");
        var deniedStopped = await ThrowsAsync(() => denied.UploadAsync(newer));
        check(deniedStopped && !Directory.EnumerateFiles(world.Layout.StagedFolder).Any() && denied.Launcher.Starts == 0, "Update page: without the right nothing is stored and the updater is not asked");
        var notBsu = Path.Combine(folder, "notes.txt");
        File.WriteAllText(notBsu, "text");
        check(await ThrowsAsync(() => page.UploadAsync(notBsu)) && !Directory.EnumerateFiles(world.Layout.StagedFolder).Any(), "Update page: a file that is not a .bsu is refused and nothing is stored");
        var fakeBsu = Path.Combine(folder, "fake.bsu");
        File.WriteAllText(fakeBsu, "this is not a zip");
        check(await ThrowsAsync(() => page.UploadAsync(fakeBsu)) && !Directory.EnumerateFiles(world.Layout.StagedFolder).Any(), "Update page: a .bsu that is not an archive is refused and no partial file is left");
        var emptyBsu = Path.Combine(folder, "empty.bsu");
        File.WriteAllBytes(emptyBsu, []);
        check(await ThrowsAsync(() => page.UploadAsync(emptyBsu)) && !Directory.EnumerateFiles(world.Layout.StagedFolder).Any(), "Update page: an empty file is refused");
        var unmanaged = new Page(world, installation: new UpdateInstallation(Path.Combine(folder, "none")));
        check(!unmanaged.Service.Overview().Managed && await ThrowsAsync(() => unmanaged.UploadAsync(newer)), "Update page: an application that does not run from an installation cannot take a package");

        // the updater cannot be started
        page.Launcher.Error = "task missing";
        var launcherFailed = await ThrowsAsync(() => page.UploadAsync(newer));
        page.Launcher.Error = null;
        check(launcherFailed && !Directory.EnumerateFiles(world.Layout.StagedFolder).Any(), "Update page: when the updater cannot be started the package and the request are removed and the administrator is told");

        // upload, check, refusals at the confirmation
        var report = await page.UploadAsync(newer);
        check(report is { CanInstall: true, Version: "3.1.0", Running: "3.0.0" } && File.Exists(Path.Combine(world.Layout.StagedFolder, report.Package)) && report.Sha256 == Sha(newer),
            "Update page: the package is stored, the updater checks it and the page gets the report of the file it stored");
        var noReason = await ThrowsAsync(() => page.Service.ConfirmAsync(report.Id, "  "));
        check(noReason && page.Audit.Entries.Count == 0 && page.Coordinator.Current is null, "Update page: without a reason nothing is journaled and nothing is scheduled");
        var strangerReport = await page.UploadAsync(await world.PackAsync(keysOther, "3.2.0"));
        check(strangerReport is { CanInstall: false, Blocking: "untrusted" } && await ThrowsAsync(() => page.Service.ConfirmAsync(strangerReport.Id, "motiv")) && page.Audit.Entries.Count == 0,
            "Update page: a package that cannot be installed cannot be confirmed");
        var again = await page.UploadAsync(newer);
        check(!Directory.EnumerateFiles(world.Layout.StagedFolder, "upload-*").Skip(1).Any(), "Update page: only the last uploaded package is kept");

        // cancelling during the notice
        var slow = new Page(world, notice: TimeSpan.FromMilliseconds(600));
        var slowReport = await slow.UploadAsync(newer);
        var announced = await slow.Service.ConfirmAsync(slowReport.Id, "de probat");
        check(slow.Coordinator.Current is { Launched: false, To: "3.1.0" } && slow.Audit.Entries.Single().Action == AuditActions.ApplicationUpdate && slow.Audit.Entries.Single().Motif == "de probat"
            && announced.RequestedBy == "admin.test", "Update page: confirming journals \"Actualizare aplicație\" with the reason under the administrator's name and tells the users");
        check(await ThrowsAsync(() => slow.Service.ConfirmAsync(slowReport.Id, "inca o data")), "Update page: a second update cannot be scheduled while one waits");
        check(await slow.Service.CancelAsync() && slow.Coordinator.Current is null && slow.Audit.Entries.Last().Action == AuditActions.ApplicationUpdateCancelled, "Update page: the administrator can cancel during the notice, and the cancellation is journaled");
        await Task.Delay(900);
        check(world.Host.Calls.Count(call => call == "stop") == 0 && world.Layout.Current == "3.0.0" && !Directory.EnumerateFiles(world.Layout.StagedFolder, "request-*").Any(),
            "Update page: after a cancellation nothing is asked of the updater (the application is not stopped)");

        // the update runs
        world.Host.Calls.Clear();
        var changes = 0;
        page.Coordinator.Changed += () => changes++;
        var toRun = await page.UploadAsync(newer);
        var confirmed = await page.Service.ConfirmAsync(toRun.Id, "functie noua");
        check(confirmed.To == "3.1.0" && page.Audit.Entries.Any(entry => entry.Action == AuditActions.ApplicationUpdate && entry.Details.Contains("3.0.0") && entry.Details.Contains("3.1.0") && entry.Details.Contains(toRun.Sha256)),
            "Update page: the journal entry of the confirmation has the versions and the hash of the package");
        check(await page.WaitAsync(() => page.Coordinator.Current is null) && world.Layout.Current == "3.1.0" && world.Host.Running == "3.1.0" && page.Launcher.Starts >= 2 && changes >= 2,
            "Update page: after the notice the updater is started, installs the package, and the announcement goes away");
        await page.WaitAsync(() => page.Audit.Entries.Any(entry => entry.Action == AuditActions.ApplicationUpdateSucceeded));
        check(page.Audit.Entries.Any(entry => entry.Action == AuditActions.ApplicationUpdateSucceeded && entry.ActorUsername == "sistem" && entry.Details.Contains("3.0.0") && entry.Details.Contains("din aplicație")),
            "Update page: the outcome is written to the journal by the system, from the history of the updater");

        // an update refused after the confirmation (the file is changed behind the page)
        var refusedPage = new Page(world);
        var checkedPackage = await refusedPage.UploadAsync(await world.PackAsync(keys, "3.2.0"));
        File.AppendAllText(Path.Combine(world.Layout.StagedFolder, checkedPackage.Package), "x");
        await refusedPage.Service.ConfirmAsync(checkedPackage.Id, "motiv");
        await refusedPage.WaitAsync(() => refusedPage.Coordinator.Current is null);
        check(await refusedPage.WaitAsync(() => refusedPage.Audit.Entries.Any(entry => entry.Action == AuditActions.ApplicationUpdateRefused)) && world.Layout.Current == "3.1.0" && refusedPage.Audit.Entries.Any(entry => entry.Action == AuditActions.ApplicationUpdateRefused),
            "Update page: an update the updater refuses after the confirmation is journaled as refused and the application keeps running");
    }

    // ---- what the updater did, in the journal ----

    private static async Task JournalAsync(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w-jr"), keys, signer, "4.0.0");
        var page = new Page(world);
        check(await page.Journal.WriteNewAsync() == 1 && page.Audit.Entries.Count(entry => entry.Action == AuditActions.ApplicationUpdateInstaller) == 1,
            "Update journal: the history written before the application looked (the installer) is journaled once, as an update made by the installer");
        check(await page.Journal.WriteNewAsync() == 0, "Update journal: an entry already journaled is not journaled again");
        var restarted = new UpdateJournal(page.Installation, page.Audit, page.StateFile, NullLogger<UpdateJournal>.Instance);
        check(await restarted.WriteNewAsync() == 0, "Update journal: the marker survives a restart of the application");

        world.Host.Broken.Add("4.1.0");
        var broken = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "4.1.0")));
        check(broken.Outcome == UpdateOutcome.RolledBack && await page.Journal.WriteNewAsync() == 1, "Update journal: an update that was rolled back is journaled");
        var failedEntry = page.Audit.Entries.Last();
        check(failedEntry.Action == AuditActions.ApplicationUpdateFailed && failedEntry.EntityType == AuditEntities.Application && failedEntry.ActorUsername == "sistem" && failedEntry.Details.Contains("4.0.0") && failedEntry.Details.Contains("4.1.0")
            && failedEntry.Details.Contains("did not answer"), "Update journal: the entry says \"Actualizare eșuată\" with the versions and the reason");
        check(page.Journal.UnacknowledgedFailure() is { To: "4.1.0" }, "Update journal: a failed update stays to be acknowledged");
        await page.Service.AcknowledgeAsync();
        check(page.Journal.UnacknowledgedFailure() is null && page.Audit.Entries.Last().Action == AuditActions.ApplicationUpdateAcknowledged && page.Audit.Entries.Last().ActorUsername == "admin.test",
            "Update journal: taking note of the failure is journaled under the administrator's name and the notice goes away");
        var refusedOne = await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "4.0.0")));
        await page.Journal.WriteNewAsync();
        check(refusedOne.Outcome == UpdateOutcome.Refused && page.Audit.Entries.Last().Action == AuditActions.ApplicationUpdateRefused && page.Journal.UnacknowledgedFailure() is null,
            "Update journal: a refused update is journaled as refused and needs no acknowledgement (nothing was changed)");
        await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "4.2.0")));
        var back = await world.Engine().RollbackAsync("ion", "prea lent");
        await page.Journal.WriteNewAsync();
        check(back.Outcome == UpdateOutcome.RolledBack && page.Audit.Entries.Last().Action == AuditActions.ApplicationRollback && page.Audit.Entries.Last().Motif == "prea lent",
            "Update journal: a return to the earlier version by hand is journaled as such, with the reason");

        // the update that brings this very version is still running when the application starts: its line of the history comes after the start
        File.WriteAllText(world.Layout.StatusFile, JsonSerializer.Serialize(new UpdateStatus("health", "4.0.0", "4.3.0", "admin", "motiv", DateTime.UtcNow, DateTime.UtcNow, null, "waiting", null), InstallLayout.Json));
        var before = page.Audit.Entries.Count;
        var service = new UpdateJournalService(page.Journal, page.Installation, NullLogger<UpdateJournalService>.Instance) { PollInterval = TimeSpan.FromMilliseconds(20) };
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        check(page.Audit.Entries.Count == before, "Update journal: at the start of the application an update that has not finished writes nothing yet");
        await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "4.3.0"), allowDowngrade: true) with { Via = UpdateVia.Application, PackageSha256 = new string('a', 64) });
        check(await page.WaitAsync(() => page.Audit.Entries.Skip(before).Any(entry => entry.Action == AuditActions.ApplicationUpdateSucceeded && entry.Details.Contains("4.3.0"))),
            "Update journal: the application waits for the update that brought it to finish and then journals its outcome");
        await service.StopAsync(CancellationToken.None);

        check(new[] { AuditActions.ApplicationUpdate, AuditActions.ApplicationUpdateCancelled, AuditActions.ApplicationUpdateSucceeded, AuditActions.ApplicationUpdateInstaller, AuditActions.ApplicationUpdateFailed,
                AuditActions.ApplicationUpdateRefused, AuditActions.ApplicationRollback, AuditActions.ApplicationUpdateAcknowledged }.All(action => AuditFilterOptions.Actions.Any(item => item.Value == action)),
            "Update journal: every action of the update is in the filter of the journal page");
        check(Permissions.IsKnown("setari-actualizare.apply") && Permissions.IsKnown("setari-actualizare.view") && Permissions.IsAdminOnly("setari-actualizare.apply") && !Permissions.GrantableKeys.Contains("setari-actualizare.apply"),
            "Update page: the rights to see and to apply an update exist and are reserved to the administrator");
    }

    // ---- the screen and the banner ----

    private static async Task ScreenAsync(Action<bool, string> check, string folder, SigningKeyStore keys, string signer)
    {
        var world = await World.InstalledAsync(Path.Combine(folder, "w-ui"), keys, signer, "5.0.0");
        var page = new Page(world, notice: TimeSpan.FromSeconds(30));
        using var context = new BunitContext();
        context.Services.AddSingleton(page.Installation);
        context.Services.AddSingleton<IUpdateLauncher>(page.Launcher);
        context.Services.AddSingleton(page.Journal);
        context.Services.AddSingleton(page.Coordinator);
        context.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin.test"));
        context.Services.AddSingleton<IAuditTrail>(page.Audit);
        context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        context.Services.AddScoped(_ => page.Service);

        var panel = context.Render<UpdatePanel>();
        check(panel.Markup.Contains("5.0.0") && panel.FindAll("#update-package").Count == 1 && panel.FindAll("#update-history tbody tr").Count == 1 && panel.FindAll("#update-not-managed").Count == 0,
            "Update screen: the page shows the version, the field for the package and the history");
        check(panel.FindAll("#update-history-page-size option").Select(option => option.TextContent).SequenceEqual(["10", "25", "50", "Toate"]) && panel.Markup.Contains("1–1 din 1"),
            "Update screen: the history has a selector of the number of entries per page and the range shown");

        var package = await world.PackAsync(keys, "5.1.0", notes: "Linia unu\nLinia doi");
        panel.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(await File.ReadAllBytesAsync(package), "BlazorStoc-5.1.0.bsu"));
        panel.WaitForState(() => panel.FindAll("#update-report").Count == 1, TimeSpan.FromSeconds(10));
        check(panel.Find("#update-verdict").TextContent.Contains("Poate fi instalat") && panel.Markup.Contains("5.0.0 → 5.1.0") && panel.Markup.Contains("nicio migrare nouă") && panel.Markup.Contains("Linia unu")
            && panel.FindAll("#update-confirm-title").Count == 1, "Update screen: after the upload the page shows the report: versions, signer, schema, notes and the confirmation");
        panel.Find("#update-confirm-title").ParentElement!.QuerySelector("button.danger")!.Click();
        check(panel.Markup.Contains("validation-message") && page.Coordinator.Current is null, "Update screen: without a reason the update is not scheduled and the field says why");
        panel.Find("#update-reason").Input("din test");
        panel.Find("#update-confirm-title").ParentElement!.QuerySelector("button.danger")!.Click();
        panel.WaitForState(() => panel.FindAll("#update-scheduled").Count == 1, TimeSpan.FromSeconds(5));
        check(page.Coordinator.Current is { To: "5.1.0", Launched: false } && panel.Find("#update-scheduled").TextContent.Contains("5.0.0 → 5.1.0") && panel.FindAll("#update-package").Count == 0,
            "Update screen: after the confirmation the page shows the countdown and no field for another package");

        var banner = context.Render<UpdateBanner>();
        check(banner.FindAll("#update-notice").Count == 1 && banner.Find("#update-notice").TextContent.Contains("5.1.0") && banner.Find("#update-notice").TextContent.Contains("salvează ce lucrezi"),
            "Update banner: every user is told that the application is about to stop for the update");
        panel.Find("#update-scheduled button.act-close").Click();
        panel.WaitForState(() => panel.FindAll("#update-scheduled").Count == 0, TimeSpan.FromSeconds(5));
        banner.WaitForState(() => banner.FindAll("#update-notice").Count == 0, TimeSpan.FromSeconds(5));
        check(page.Coordinator.Current is null && panel.Markup.Contains("a fost anulată") && banner.FindAll("#update-notice").Count == 0 && panel.FindAll("#update-package").Count == 1,
            "Update screen: cancelling during the notice brings back the field for the package and removes the banner");

        // a failure waits for the administrator
        world.Host.Broken.Add("5.2.0");
        await world.Engine().InstallAsync(world.Request(await world.PackAsync(keys, "5.2.0")));
        await page.Journal.WriteNewAsync();
        var failed = context.Render<UpdatePanel>();
        var failedBanner = context.Render<UpdateBanner>();
        check(failed.FindAll("#update-failure").Count == 1 && failed.Find("#update-failure").TextContent.Contains("5.2.0") && failedBanner.FindAll("#update-failure-notice").Count == 1,
            "Update screen: a failed update shows on the page and in the banner of the administrator until he takes note");
        failed.Find("#update-failure button").Click();
        failed.WaitForState(() => failed.FindAll("#update-failure").Count == 0, TimeSpan.FromSeconds(5));
        failedBanner.WaitForState(() => failedBanner.FindAll("#update-failure-notice").Count == 0, TimeSpan.FromSeconds(5));
        check(failedBanner.FindAll("#update-failure-notice").Count == 0, "Update screen: acknowledging the failure removes the banner above the pages too");
        check(failed.FindAll("#update-failure").Count == 0 && page.Audit.Entries.Last().Action == AuditActions.ApplicationUpdateAcknowledged, "Update screen: \"Am luat la cunoștință\" removes the notice and is journaled");

        // not an installation
        using var plain = new BunitContext();
        var bare = new Page(world, installation: new UpdateInstallation(Path.Combine(folder, "none-ui")));
        plain.Services.AddSingleton(bare.Installation);
        plain.Services.AddSingleton<IUpdateLauncher>(bare.Launcher);
        plain.Services.AddSingleton(bare.Journal);
        plain.Services.AddSingleton(bare.Coordinator);
        plain.Services.AddSingleton<IAccessControl>(new TestAccessControl(true, "admin.test"));
        plain.Services.AddSingleton<IAuditTrail>(bare.Audit);
        plain.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        plain.Services.AddScoped(_ => bare.Service);
        var unmanaged = plain.Render<UpdatePanel>();
        check(unmanaged.FindAll("#update-not-managed").Count == 1 && unmanaged.FindAll("#update-package").Count == 0 && unmanaged.FindAll("#update-history").Count == 0,
            "Update screen: an application that does not run from an installation says so and offers no upload");
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception exception) when (exception is UpdateOperationException or AccessDeniedException) { return true; }
    }
}
