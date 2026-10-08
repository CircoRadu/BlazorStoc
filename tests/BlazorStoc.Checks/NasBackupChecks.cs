using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorStoc.Checks;

// Backup copy to a NAS: the rules of the path and of the typed values, the copy of one package (a temporary name, size and checksum, then a rename,
// nothing deleted), the decorator that copies after a backup, and the stored password (encrypted, never in clear).
public static class NasBackupChecks
{
    private sealed class FakeBackup(bool success) : IDatabaseBackupService
    {
        public int Calls;
        public Task<BackupResult> CreateBackupAsync(BackupKind kind, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default, IOperationLockHandle? existingLock = null)
        { Calls++; return Task.FromResult(success ? BackupResult.Ok("x.zip", "x.zip") : BackupResult.Failed("no")); }
        public Task<IReadOnlyList<BackupPackage>> ListPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BackupPackage>>([]);
        public Task<BackupDeleteResult> DeletePackageAsync(string fileName, string reason, CancellationToken cancellationToken = default) => Task.FromResult(BackupDeleteResult.Ok());
    }

    private sealed class FakeAlertReader(BackupAlertState state) : IBackupAlertReader
    {
        public Task<BackupAlertState> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
    }

    private sealed class FakeCopier : INasBackupCopier
    {
        public int AfterBackup, Manual; public string? LastOnly;
        public Task<NasCopyResult> TestAsync(NasBackupInput input, CancellationToken cancellationToken = default) => Task.FromResult(new NasCopyResult(true, 0, 0, 0, ""));
        public Task<NasCopyResult> CopyMissingAsync(CancellationToken cancellationToken = default) { Manual++; return Task.FromResult(new NasCopyResult(true, 0, 0, 0, "")); }
        public Task<NasShareListing> ListShareAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NasShareListing(true, "", []));
        public Task<NasCopyResult> FetchAsync(string fileName, CancellationToken cancellationToken = default) => Task.FromResult(new NasCopyResult(true, 1, 0, 0, ""));
        public Task RecordBackupFailureAsync(string message, CancellationToken cancellationToken = default) { Failures.Add(message); return Task.CompletedTask; }
        public List<string> Failures = [];
        public Task<NasCopyResult> CopyAfterBackupAsync(string? onlyPackage, CancellationToken cancellationToken = default) { AfterBackup++; LastOnly = onlyPackage; return Task.FromResult(new NasCopyResult(true, 0, 0, 0, "")); }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Backup NAS ===");
        check(NasBackupRules.PathError(@"\\192.168.100.50\BackupStocDepozit") is null && NasBackupRules.PathError(@"\\nas\share\folder\sub\") is null
              && NasBackupRules.PathError(@"C:\backup") is not null && NasBackupRules.PathError(@"\\nas") is not null && NasBackupRules.PathError(@"\\nas\share\..\x") is not null
              && NasBackupRules.PathError(@"\\nas\sh*re") is not null && NasBackupRules.PathError("") is not null,
            "NAS backup: only a network path with a server and a share is accepted, without relative parts or forbidden characters");
        check(NasBackupRules.ShareRoot(@"\\nas\share\folder\sub\") == @"\\nas\share" && NasBackupRules.NormalizePath(@"  \\nas\share\  ") == @"\\nas\share",
            "NAS backup: the connection is made to the root of the share, the path is trimmed");
        check(NasBackupRules.Validate(new(@"\\nas\share", "u", null, true)) is null
              && NasBackupRules.Validate(new("", "", null, false)) is null
              && NasBackupRules.Validate(new(@"\\nas\share", "", null, true)) is not null,
            "NAS backup: path and account are required only when the copy is on");
        check(BackupSettingsRules.Validate(new(false, "xx", 2, false, 1)) is null && BackupSettingsRules.Validate(new(true, "25:99", 2, false, 30)) is not null
              && BackupSettingsRules.Validate(new(true, "03:30", 2, true, 30)) is null && BackupSettingsRules.Validate(new(false, "02:00", 0, false, 30)) is not null
              && BackupSettingsRules.Validate(new(false, "02:00", 31, false, 30)) is not null && BackupSettingsRules.Validate(new(false, "02:00", 2, true, 6)) is not null
              && BackupSettingsRules.Validate(new(false, "02:00", 2, true, 7)) is null,
            "Backup settings: the time is required only when the schedule is on, the age is 1-30 days, the retention is at least 7 days");
        check(NasBackupRules.ConnectionMessage(1326).Contains("parola") && NasBackupRules.ConnectionMessage(53).Contains("găsită") && NasBackupRules.ConnectionMessage(9999).Contains("9999"),
            "NAS backup: network error codes become messages in words");

        var root = Path.Combine(Path.GetTempPath(), "blazorstoc-nas-check-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "local"); var share = Path.Combine(root, "share");
        Directory.CreateDirectory(local); Directory.CreateDirectory(share);
        try
        {
            var package = Path.Combine(local, "Copie siguranta test.zip");
            await File.WriteAllBytesAsync(package, Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray());
            await File.WriteAllTextAsync(package + ".sha256", "abc");
            var first = await NasBackupCopier.CopyOneAsync(package, share, CancellationToken.None);
            var again = await NasBackupCopier.CopyOneAsync(package, share, CancellationToken.None);
            var target = Path.Combine(share, "Copie siguranta test.zip");
            check(first && !again && File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(package)) && File.ReadAllText(target + ".sha256") == "abc"
                  && Directory.GetFiles(share, "*.partial").Length == 0,
                "NAS backup: a package is copied once, byte for byte with its checksum file, no temporary file is left, a second pass skips it");
            await File.WriteAllBytesAsync(Path.Combine(share, "Copie siguranta test2.zip"), [1]);
            var changed = Path.Combine(local, "Copie siguranta test2.zip");
            await File.WriteAllBytesAsync(changed, [1, 2, 3]);
            var threw = false;
            try { await NasBackupCopier.CopyOneAsync(changed, share, CancellationToken.None); } catch (Exception exception) when (exception is IOException or NasBackupException) { threw = true; }
            check(threw && File.ReadAllBytes(Path.Combine(share, "Copie siguranta test2.zip")).Length == 1,
                "NAS backup: a different file with the same name on the share is never overwritten (the failure is reported)");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }

        var inner = new FakeBackup(true); var copier = new FakeCopier();
        var service = new NasCopyingBackupService(inner, copier, NullLogger<NasCopyingBackupService>.Instance);
        await service.CreateBackupAsync(BackupKind.Manual);
        await service.CreateBackupAsync(BackupKind.PreRestore, null, default, new LockHandleStub());
        var failing = new NasCopyingBackupService(new FakeBackup(false), copier, NullLogger<NasCopyingBackupService>.Instance);
        await failing.CreateBackupAsync(BackupKind.Manual);
        check(copier.AfterBackup == 1 && copier.LastOnly == "x.zip" && copier.Manual == 0 && inner.Calls == 2,
            "NAS backup: a successful backup is followed by the copy; a restore's pre-restore backup and a failed backup are not");
        check(BackupRules.FailureMessage(new InvalidOperationException("Utilitarul mariadb-dump nu a fost gasit la calea configurata (/opt/secret/x.exe).")).Contains("mariadb-dump nu a fost gasit")
              && !BackupRules.FailureMessage(new InvalidOperationException("Utilitarul mariadb-dump nu a fost gasit la calea configurata (/opt/secret/x.exe).")).Contains("secret")
              && BackupRules.FailureMessage(new InvalidOperationException("mariadb-dump a esuat (cod 2): Access denied for user x")).Contains("cod 2")
              && !BackupRules.FailureMessage(new InvalidOperationException("mariadb-dump a esuat (cod 2): Access denied for user x")).Contains("Access denied")
              && BackupRules.FailureMessage(new IOException("disk")).Contains("folderul de backup"),
            "Backup: a failed backup names the real cause (tool not found, dump exit code, write failure) without a path, an account or a password");

        // The notifications that watch the backups: the date a backup expires on moves with every new backup, the NAS copy only matters when it is on.
        var today = DateTime.UtcNow;
        var fresh = new BackupAlertState(today.AddHours(-3), today.AddDays(-90), false, null, null, null);
        var stale = fresh with { LastBackupUtc = today.AddDays(-10) };
        var never = fresh with { LastBackupUtc = null };
        check(BackupAlertRules.BackupExpiry(fresh) > BackupAlertRules.LocalDate(today) && BackupAlertRules.BackupExpiry(stale) < BackupAlertRules.LocalDate(today)
              && BackupAlertRules.BackupExpiry(never) == BackupAlertRules.LocalDate(today.AddDays(-90)).AddDays(BackupAlertRules.BackupMaxAgeDays),
            "Backup notifications: the backup expires two days after the last package; with no backup the clock starts at the first use");
        var nasOff = BackupAlertRules.NasExpiry(fresh);
        var nasFailed = BackupAlertRules.NasExpiry(fresh with { NasCopyEnabled = true, NasLastAttemptUtc = today.AddHours(-1), NasLastOk = false });
        var nasGood = BackupAlertRules.NasExpiry(fresh with { NasCopyEnabled = true, NasLastAttemptUtc = today.AddHours(-1), NasLastOk = true });
        check(nasOff is null && nasFailed <= BackupAlertRules.LocalDate(today) && nasGood > BackupAlertRules.LocalDate(today),
            "Backup notifications: the NAS copy is watched only when it is on; a failed last attempt is overdue at once, a good one for two days");
        var staleOn = stale with { NasCopyEnabled = true, MaxAgeDays = 5, LastErrorUtc = today.AddHours(-2), LastError = "mariadb-dump nu a fost gasit" };
        var staleSource = await new BackupMissingSource(new FakeAlertReader(staleOn)).GetInstancesAsync();
        var staleOff = await new BackupMissingSource(new FakeAlertReader(stale)).GetInstancesAsync();
        var nasSource = await new NasCopyMissingSource(new FakeAlertReader(fresh)).GetInstancesAsync();
        check(staleSource.Count == 1 && staleOff.Count == 1 && nasSource.Count == 0,
            "Backup notifications: the missing local backup is watched with or without a NAS; the NAS copy only while it is on");
        var nasModel = NasCopyMissingSource.Preview(fresh with { NasCopyEnabled = true, NasLastAttemptUtc = today, NasLastOk = false, NasLastMessage = "Contul sau parola sunt greșite." });
        check(nasModel.Body.Contains("Contul sau parola sunt greșite.") && !nasModel.Body.Contains("<"), "Backup notifications: the NAS model shows the last attempt and its cause");

        // The table of available backups: server and NAS matched by name; a package only on the NAS is read from its file name; the retention keeps the newest four.
        BackupPackage Pkg(string name, BackupKind kind, int daysAgo, long size = 100) => new(name, kind, size, DateTime.UtcNow.AddDays(-daysAgo), "ion", "Administrator", 5);
        var scheduledName = $"{BackupRules.KindLabel(BackupKind.Scheduled)} 05.10.2026 02-00-01 Administrator sistem.zip";
        var localRows = new[] { Pkg("a.zip", BackupKind.Manual, 1), Pkg("b.zip", BackupKind.InventoryPickup, 2, 50) };
        var shareFiles = new NasShareFile[] { new("a.zip", 100, DateTime.UtcNow), new("b.zip", 49, DateTime.UtcNow), new(scheduledName, 900, DateTime.UtcNow), new("alt-fisier.zip", 1, DateTime.UtcNow) };
        var merged = BackupCatalog.Merge(localRows, shareFiles);
        var onlyNas = merged.Single(row => row.NasOnly);
        check(merged.Count == 3 && merged.Single(row => row.FileName == "a.zip") is { OnLocal: true, OnNas: true, NasSizeDiffers: false } && merged.Single(row => row.FileName == "b.zip").NasSizeDiffers
              && onlyNas.Kind == BackupKind.Scheduled && onlyNas.OperatorName == "sistem" && onlyNas.OperatorRole == "Administrator" && !onlyNas.CanDelete
              && BackupCatalog.Merge(localRows, null).All(row => row.OnLocal && !row.OnNas),
            "Backup table: server and NAS are matched by name, a package only on the NAS is read from its name, an unrelated file is ignored, nothing on the NAS can be deleted");
        var ten = Enumerable.Range(0, 10).Select(index => Pkg($"p{index}.zip", BackupKind.Scheduled, index * 10)).ToList();
        var expired = BackupSettingsRules.Expired(ten, 30, DateTime.UtcNow).Select(item => item.FileName).ToList();
        check(expired.Count == 6 && !expired.Contains("p0.zip") && !expired.Contains("p3.zip") && expired.Contains("p4.zip") && BackupSettingsRules.Expired(ten, 500, DateTime.UtcNow).Count == 0
              && BackupSettingsRules.Expired(ten.Take(4).ToList(), 1, DateTime.UtcNow).Count == 0,
            "Backup retention: only packages older than the threshold go, and the newest four are always kept");
        var mixed = new[] { Pkg("n1.zip", BackupKind.Manual, 1), Pkg("n2.zip", BackupKind.Scheduled, 2), Pkg("n3.zip", BackupKind.InventoryPickup, 3), Pkg("n4.zip", BackupKind.Manual, 4),
            Pkg("old-restore.zip", BackupKind.PreRestore, 100), Pkg("old-pickup.zip", BackupKind.InventoryPickup, 101) };
        check(BackupSettingsRules.Expired(mixed, 30, DateTime.UtcNow).Select(item => item.FileName).Order().SequenceEqual(["old-pickup.zip", "old-restore.zip"]),
            "Backup retention: pre-restore packages are removed like every other kind when old enough");
        var unverifiedSet = new[] { Pkg("n1.zip", BackupKind.Manual, 1), Pkg("n2.zip", BackupKind.Scheduled, 2), Pkg("n3.zip", BackupKind.Manual, 3), Pkg("n4.zip", BackupKind.Manual, 4),
            Pkg("old-ok.zip", BackupKind.Manual, 100), Pkg("old-unverified.zip", BackupKind.Scheduled, 101) with { TimeSource = TrustedClock.SourceUnverified } };
        check(BackupSettingsRules.Expired(unverifiedSet, 30, DateTime.UtcNow).Select(item => item.FileName).SequenceEqual(["old-ok.zip"])
              && Pkg("x.zip", BackupKind.PreRestore, 1) with { TimeSource = TrustedClock.SourceUnverified } is { CanDelete: true }
              && !Pkg("x.zip", BackupKind.Scheduled, 1).CanDelete && Pkg("x.zip", BackupKind.InventoryPickup, 1).CanDelete
              && BackupCatalog.Merge([Pkg("u.zip", BackupKind.Manual, 1) with { TimeSource = TrustedClock.SourceUnverified }], null).Single().CanDelete,
            "Unverified time: such a package is never removed automatically (nor counted among the newest four), and only it can be deleted by hand besides pickup packages");
        var serverNow = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var fiveMinutes = TimeSpan.FromMinutes(5);
        var ntpReply = new byte[48];
        var ntpSeconds = (uint)(serverNow - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        ntpReply[40] = (byte)(ntpSeconds >> 24); ntpReply[41] = (byte)(ntpSeconds >> 16); ntpReply[42] = (byte)(ntpSeconds >> 8); ntpReply[43] = (byte)ntpSeconds;
        check(TrustedClock.Evaluate(serverNow, serverNow.AddMinutes(-2), fiveMinutes).Trusted
              && !TrustedClock.Evaluate(serverNow.AddDays(30), serverNow, fiveMinutes).Trusted && !TrustedClock.Evaluate(serverNow.AddDays(-30), serverNow, fiveMinutes).Trusted
              && !TrustedClock.Evaluate(serverNow, null, fiveMinutes).Trusted && TrustedClock.ParseNtpReply(ntpReply) == serverNow
              && TrustedClock.ParseNtpReply(new byte[10]) is null && TrustedClock.ParseNtpReply(new byte[48]) is null,
            "Trusted clock: a server date moved by more than the tolerance, or a time that cannot be checked, blocks the automatic removal; NTP replies are read");
        var quarter = TimeSpan.FromMinutes(15);
        var skewedCheck = TrustedClock.Decide([(serverNow.AddDays(30), serverNow)], quarter);
        var skewedStamp = TrustedClock.Stamp(serverNow.AddDays(30), skewedCheck);
        var goodStamp = TrustedClock.Stamp(serverNow, TrustedClock.Decide([(serverNow, serverNow.AddMinutes(-3))], quarter));
        var noNet = TrustedClock.Stamp(serverNow, TrustedClock.Decide([], quarter));
        check(skewedStamp == (serverNow, TrustedClock.SourceInternet) && goodStamp == (serverNow, TrustedClock.SourceVerified) && noNet == (serverNow, TrustedClock.SourceUnverified)
              && TrustedClock.Stamp(serverNow, TrustedClock.Decide([(serverNow, serverNow), (serverNow.AddMinutes(10), serverNow)], quarter)).Source == TrustedClock.SourceUnverified
              && TrustedClock.Decide([(serverNow, serverNow), (serverNow.AddSeconds(20), serverNow.AddSeconds(30))], quarter).Trusted,
            "Backup time: a wrong server clock gives the internet time, a right one stays, no answer marks the package unverified, two servers that disagree count as unverified");
        check(BackupTimeRules.Validate("pool.ntp.org") is null && BackupTimeRules.Validate("time.google.com, ntp.firma.local") is null && BackupTimeRules.Validate("") is not null
              && BackupTimeRules.Validate("https://pool.ntp.org") is not null && BackupTimeRules.Validate("a.com, b.com, c.com, d.com") is not null && BackupTimeRules.Validate("a b") is not null
              && BackupTimeRules.Validate("pool.ntp.org, POOL.ntp.org") is not null && BackupTimeRules.Servers("").SequenceEqual(TrustedClock.DefaultServers)
              && BackupSettingsRules.Validate(new(false, "02:00", 2, false, 30, true, "x/y")) is not null && BackupSettingsRules.Validate(new(false, "02:00", 2, false, 30, false, "x/y")) is null,
            "NTP servers: one to three host names, no web addresses, spaces or repeats; empty means the defaults; not checked when the check is off");
        check(BackupScheduleDays.Includes(1, DayOfWeek.Monday) && !BackupScheduleDays.Includes(1, DayOfWeek.Sunday) && BackupScheduleDays.Includes(64, DayOfWeek.Sunday)
              && BackupScheduleDays.LongestGap(127) == 1 && BackupScheduleDays.LongestGap(1) == 7 && BackupScheduleDays.LongestGap(1 | 8) == 4 && BackupScheduleDays.Describe(1 | 16) == "Lu, Vi" && BackupScheduleDays.Describe(127) == "zilnic"
              && BackupSettingsRules.Validate(new(true, "02:00", 2, false, 30, true, "", 0)) == BackupScheduleDays.NoneMessage && BackupSettingsRules.Validate(new(false, "02:00", 2, false, 30, true, "", 0)) is null
              && BackupAlertRules.EffectiveMaxAge(2, true, 127) == 2 && BackupAlertRules.EffectiveMaxAge(2, true, 1) == 8 && BackupAlertRules.EffectiveMaxAge(10, true, 1) == 10 && BackupAlertRules.EffectiveMaxAge(2, false, 1) == 2,
            "Backup days: the mask of weekdays, at least one day when scheduled, and the missing-backup age grows with the longest stretch between two backups");
        var skewReader = new FakeAlertReader(fresh with { ClockIssueUtc = DateTime.UtcNow, ClockSkewMinutes = 43200 });
        var skewInstances = await new ClockSkewSource(skewReader).GetInstancesAsync();
        check(skewInstances.Count == 1 && skewInstances[0].Values[ClockSkewSource.SkewName] == "cu 30 zile înainte" && ClockSkewSource.Describe(-125) == "cu 2 ore în urmă"
              && (await new ClockSkewSource(new FakeAlertReader(fresh)).GetInstancesAsync()).Count == 0,
            "Clock notification: open while a clock problem is recorded, closed when none; the offset reads in days, hours or minutes");
        var pickupCopier = new FakeCopier();
        var pickupResult = await new NasCopyingBackupService(new FakeBackup(true), pickupCopier, NullLogger<NasCopyingBackupService>.Instance).CreateBackupAsync(BackupKind.InventoryPickup);
        await Task.Delay(200);
        check(pickupResult.Success && pickupCopier.AfterBackup == 1 && pickupCopier.LastOnly == "x.zip",
            "Backup NAS: the inventory pickup is not held up by the copy, which still happens (in the background)");
        var model = BackupMissingSource.Preview(staleOn);
        check(model.Body.Contains("mariadb-dump nu a fost gasit") && model.Body.Contains(BackupAlertRules.Describe(staleOn.LastBackupUtc)) && model.Body.Contains(" 5 zile")
              && model.Subject.Contains(BackupAlertRules.Describe(staleOn.LastBackupUtc)) && !model.Body.Contains("<") && BackupAlertRules.BackupExpiry(staleOn) == BackupAlertRules.LocalDate(stale.LastBackupUtc!.Value).AddDays(5),
            "Backup notifications: the model shows the date of the last backup, the date and cause of the last error, and the configured age (5 days moves the due date)");
        var failing2 = new FakeCopier();
        await new NasCopyingBackupService(new FakeBackup(false), failing2, NullLogger<NasCopyingBackupService>.Instance).CreateBackupAsync(BackupKind.Manual);
        check(failing2.Failures.Count == 1 && failing2.Failures[0] == "no", "Backup notifications: a failed backup leaves its cause for the notification");
    }

    private sealed class LockHandleStub : IOperationLockHandle
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
