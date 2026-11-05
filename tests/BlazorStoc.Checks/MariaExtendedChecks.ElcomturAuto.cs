using BlazorStoc.Modules.Elcomtur;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The automatic import of the Elcomtur backup, end to end on a local directory: settings, firms with names, import of the newest file, "nothing new", the last two files kept, a damaged archive,
    // a file still being written, the recovery, the schedule of 04:00, the wrong firm, the journal and the notification state.
    private static async Task ElcomturAutoAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var protection = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("BlazorStoc.Checks");
        var directory = Path.Combine(Path.GetTempPath(), "elcomtur-auto-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(directory, "server");
        var share = Path.Combine(directory, "nas");
        Directory.CreateDirectory(share);
        var own = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MariaElcomturBackupsPath"] = local }).Build();
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var balances = new MariaElcomturBalanceRepository(configuration, admin);
        var service = new ElcomturAutoService(own, protection, beneficiaries, balances, admin, audit, NullLogger<ElcomturAutoService>.Instance);
        var number = Random.Shared.Next(70000000, 79999999);
        var owner = await beneficiaries.CreateAsync(Legal($"Ext ElcomturAuto {Suffix()} SRL", "RO" + number));
        await using (var cleanup = new MySqlCommand("DELETE FROM elcomtur_auto_settings WHERE id=1", probe)) await cleanup.ExecuteNonQueryAsync();
        async Task<int> Events(string action) => (await audit.GetEventsAsync()).Count(item => item.TimestampUtc > started && item.EntityType == AuditEntities.Elcomtur && item.Action == action);
        string Backup(string name, DateTime modifiedUtc, bool damage = false)
        {
            var archive = ElcomturChecks.CreateArchive(new DateOnly(2026, 10, 30), cui: number.ToString());
            var target = Path.Combine(share, name);
            var bytes = File.ReadAllBytes(archive);
            File.Delete(archive);
            if (damage) bytes[30 + "firma1/clienti.dbf".Length + 3] ^= 0xFF;
            File.WriteAllBytes(target, bytes);
            File.SetLastWriteTimeUtc(target, modifiedUtc);
            return target;
        }
        var input = new ElcomturAutoInput(true, share, "", null, "*elcomtur*", "firma1", "ELECTRIC TEST SRL", 3);
        try
        {
            Check(!(await service.GetAsync()).Enabled && (await service.CheckNowAsync(false)) is { Ok: false } notSet && notSet.Message == ElcomturAutoRules.NotConfiguredMessage,
                "Elcomtur automatic: nothing is configured at first, and a check without configuration says so");
            await Rejects<ElcomturAutoException>(() => service.SaveAsync(input with { FirmFolder = "" }), "Elcomtur automatic: an import turned on without a firm is refused");
            await Rejects<AccessDeniedException>(() => new ElcomturAutoService(own, protection, beneficiaries, balances, new TestAccessControl(false, "limited"), audit, NullLogger<ElcomturAutoService>.Instance).GetAsync(),
                "Elcomtur automatic: only the administrator reads the settings");
            await service.SaveAsync(input);
            var saved = await service.GetAsync();
            Check(saved.Enabled && saved.Path == share && saved.Mask == "*elcomtur*" && saved.FirmFolder == "firma1" && saved.FirmLabel == "firma1 — ELECTRIC TEST SRL" && saved.AlertDays == 3 && !saved.HasPassword,
                "Elcomtur automatic: the directory, the mask, the firm with its name and the threshold are saved");
            Check((await service.TestAsync(input)) is { Ok: false } empty && empty.Message.Contains("niciun fișier") && (await service.ReadFirmsAsync(input)) is { Ok: false },
                "Elcomtur automatic: an empty directory is told (the test and the reading of the firms)");

            // The first backup (and a newer file that does not fit the mask).
            var now = DateTime.UtcNow;
            Backup("Backup_Elcomtur_1.elc", now.AddHours(-5));
            Backup("alt.elc", now.AddHours(-1));
            var test = await service.TestAsync(input);
            var firms = await service.ReadFirmsAsync(input);
            Check(test.Ok && test.Message.Contains("Backup_Elcomtur_1.elc") && firms.Ok && firms.Firms.Single().Label == "firma1 — ELECTRIC TEST SRL" && firms.FileName == "Backup_Elcomtur_1.elc",
                "Elcomtur automatic: the test finds the newest file of the mask, and the firms are read from it with their names without copying it");
            Check(!Directory.Exists(local) || Directory.GetFiles(local).Length == 0, "Elcomtur automatic: reading the firms copies nothing to the server");

            var first = await service.CheckNowAsync(false);
            var balance = await balances.GetAsync(owner.Id);
            var afterFirst = await service.GetAsync();
            Check(first.Ok && first.Imported && balance is { Balance: 11950m, Firm: "firma1" } && afterFirst.LastFileName == "Backup_Elcomtur_1.elc" && afterFirst.LastImportUtc is not null && afterFirst.FailingSinceUtc is null
                && Directory.GetFiles(local).Select(Path.GetFileName).SequenceEqual(["Backup_Elcomtur_1.elc"]), "Elcomtur automatic: the newest backup is copied, checked and imported, and kept on the server");
            Check(await Events(AuditActions.ImportElcomturAuto) == 1 && await Events(AuditActions.SetElcomturAuto) >= 1 && await Events(AuditActions.TestElcomturAuto) >= 1, "Elcomtur automatic: the settings, the tests and the import are journaled under their own names");

            var again = await service.CheckNowAsync(false);
            Check(again.Ok && !again.Imported && again.Message.Contains("Nu există un backup mai nou") && await Events(AuditActions.CheckElcomturAuto) == 1 && await Events(AuditActions.ImportElcomturAuto) == 1,
                "Elcomtur automatic: with no newer file nothing is imported, and the check is journaled");

            // Newer files: imported each time, and only the last two stay on the server.
            Backup("Backup_Elcomtur_2.elc", now.AddHours(-4));
            Check((await service.CheckNowAsync(false)).Imported, "Elcomtur automatic: a newer file is imported");
            Backup("Backup_Elcomtur_3.elc", now.AddHours(-3));
            Check((await service.CheckNowAsync(false)).Imported && Directory.GetFiles(local).Select(Path.GetFileName).Order().SequenceEqual(["Backup_Elcomtur_2.elc", "Backup_Elcomtur_3.elc"]),
                "Elcomtur automatic: only the last two imported backups stay on the server");

            // A damaged archive is not imported; the failure is told, kept, journaled and shown as a notification.
            var damagedFile = Backup("Backup_Elcomtur_4.elc", now.AddHours(-2), damage: true);
            var bad = await service.CheckNowAsync(false);
            var failing = await service.GetAsync();
            var alert = await new MariaElcomturAutoAlertReader(configuration, protection).GetAsync();
            Check(!bad.Ok && bad.Message.Contains("corupt") && failing.FailingSinceUtc is not null && failing.LastCheckOk == false && failing.LastFileName == "Backup_Elcomtur_3.elc" && (await balances.GetAsync(owner.Id)) is { Balance: 11950m }
                && Directory.GetFiles(local).All(file => !Path.GetFileName(file).StartsWith(".tmp-")) && await Events(AuditActions.FailElcomturAuto) == 1,
                "Elcomtur automatic: a damaged archive is refused with the cause, the balances stay as they were, no temporary file stays, the failure is journaled");
            Check(ElcomturAutoAlertSource.Instances(alert, DateOnly.FromDateTime(DateTime.Now)).Any(item => item.ObjectId == ElcomturAutoAlertSource.FailedId), "Elcomtur automatic: the failure is a notification");

            // A file the Elcomtur program is still writing.
            File.Delete(damagedFile);
            Backup("Backup_Elcomtur_5.elc", DateTime.UtcNow);
            var tooFresh = await service.CheckNowAsync(false);
            Check(!tooFresh.Ok && tooFresh.Message.Contains("mai puțin de 5 minute"), "Elcomtur automatic: a file modified a moment ago is not taken (still being written)");

            // Recovery: the next check without a problem closes the failure.
            File.Delete(Path.Combine(share, "Backup_Elcomtur_5.elc"));
            var recovered = await service.CheckNowAsync(false);
            Check(recovered.Ok && (await service.GetAsync()).FailingSinceUtc is null && !ElcomturAutoAlertSource.Instances(await new MariaElcomturAutoAlertReader(configuration, protection).GetAsync(), DateOnly.FromDateTime(DateTime.Now)).Any(item => item.ObjectId == ElcomturAutoAlertSource.FailedId),
                "Elcomtur automatic: a good check ends the failure");
            var forced = await service.CheckNowAsync(true);
            Check(forced.Ok && forced.Imported && forced.Message.Contains("Backup_Elcomtur_3.elc"), "Elcomtur automatic: \"Preia ultimul backup acum\" imports the newest file even if it is not newer than the last import");

            // The schedule: nothing before 04:00, one run a day, the next day again.
            var day = new DateTime(2099, 1, 10, 3, 0, 0);
            var before = await service.RunScheduledAsync(day);
            var run = await service.RunScheduledAsync(day.AddHours(1));
            var second = await service.RunScheduledAsync(day.AddHours(2));
            var next = await service.RunScheduledAsync(day.AddDays(1).AddHours(1));
            Check(before is null && run is { Ok: true } && second is null && next is { Ok: true } && (await service.GetAsync()).LastScheduledDate == new DateOnly(2099, 1, 11),
                "Elcomtur automatic: the scheduled check does not run before 04:00, runs once a day and again the next day");

            // The wrong firm and the switch off.
            await service.SaveAsync(input with { FirmFolder = "firma9", FirmName = "" });
            var wrongFirm = await service.CheckNowAsync(true);
            Check(!wrongFirm.Ok && wrongFirm.Message.Contains("nu mai există"), "Elcomtur automatic: a firm that is not in the backup is told");
            await service.SaveAsync(input with { Enabled = false });
            Check(await service.RunScheduledAsync(new DateTime(2099, 1, 12, 5, 0, 0)) is null && !ElcomturAutoAlertSource.Instances(await new MariaElcomturAutoAlertReader(configuration, protection).GetAsync(), DateOnly.FromDateTime(DateTime.Now))
                .Any(item => item.ObjectId is ElcomturAutoAlertSource.MissingId or ElcomturAutoAlertSource.FailedId), "Elcomtur automatic: with the import off nothing runs and the lasting notifications go away");
            var settingsEvents = (await audit.GetEventsAsync()).Where(item => item.TimestampUtc > started && item.Action == AuditActions.SetElcomturAuto).ToList();
            Check(settingsEvents.Count >= 3 && settingsEvents.Any(item => item.Details.Contains("Firmă")) && settingsEvents.All(item => !item.Details.Contains("Parola director: setată")), "Elcomtur automatic: every change of the settings is journaled with old and new values");
        }
        finally
        {
            try { await beneficiaries.DeleteAsync(owner, "Curatare test Elcomtur automat"); } catch (Exception) { }
            await using (var cleanup = new MySqlCommand("DELETE FROM elcomtur_auto_settings WHERE id=1", probe)) await cleanup.ExecuteNonQueryAsync();
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
