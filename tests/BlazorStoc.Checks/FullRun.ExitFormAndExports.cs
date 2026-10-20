#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 153-300 of the former Program.cs.
    internal static async Task ExitFormAndExportsAsync(string[] args)
    {
        // Backup/restore scope: every table a schema migration creates must be in MariaArchiveSchema.RequiredTables, otherwise
        // mariadb-dump exports it but the manifest and the canonical row hash silently leave it out (found 30.09.2026 on the
        // real instance: dump had 28 tables, manifest 27, because beneficiary_work_points was missing).
        {
            var requiredTables = ((string[])typeof(MariaArchiveSchema).GetField("RequiredTables",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var migrationTables = MariaSchemaMigrations.All.SelectMany(m => m.Statements)
                .SelectMany(sql => System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE TABLE IF NOT EXISTS `(\w+)`").Select(match => match.Groups[1].Value))
                .Distinct().ToList();
            Check(migrationTables.Count > 0 && migrationTables.All(requiredTables.Contains),
                $"Every table created by a MariaDB migration is part of the backup/restore table list ({string.Join(", ", migrationTables.Where(t => !requiredTables.Contains(t)))} missing)");
        }

        var dumpApp = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:User"] = "app", ["Database:Password"] = "p1" }).Build();
        var dumpBackup = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:User"] = "app", ["Database:Password"] = "p1", ["Database:BackupUser"] = "bk", ["Database:BackupPassword"] = "p2" }).Build();
        var dumpHalf = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:User"] = "app", ["Database:Password"] = "p1", ["Database:BackupUser"] = "bk" }).Build();
        Check(MariaDatabaseBackupService.DumpAccount(dumpApp) == ("app", "p1") && MariaDatabaseBackupService.DumpAccount(dumpBackup) == ("bk", "p2") &&
              MariaDatabaseBackupService.DumpAccount(dumpHalf) == ("app", "p1"),
            "mariadb-dump uses the dedicated backup account only when both its user and password are configured");
        var splitRoot = Path.Combine(Path.GetTempPath(), "blazorstoc-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(splitRoot);
        try
        {
            var dumpText = string.Join(Environment.NewLine,
                "CREATE TABLE `t` (id int);", "INSERT INTO `t` VALUES (1);", "UNLOCK TABLES;",
                "/*!50003 SET @saved_cs_client      = @@character_set_client */ ;", "/*!50003 SET character_set_client  = utf8mb4 */ ;",
                "/*!50003 SET collation_connection  = utf8mb4_general_ci */ ;", "DELIMITER ;;",
                "/*!50003 CREATE*/ /*!50017 DEFINER=`root`@`127.0.0.1`*/ /*!50003 TRIGGER trg_a AFTER INSERT ON t FOR EACH ROW BEGIN", "INSERT INTO log VALUES (1); END */;;", "DELIMITER ;",
                "/*!50003 SET collation_connection  = @saved_col_connection */ ;", "CREATE TABLE `u` (id int);", "-- done") + Environment.NewLine;
            var dumpFile = Path.Combine(splitRoot, "d.sql");
            await File.WriteAllTextAsync(dumpFile, dumpText);
            var splitCount = await RestoreDumpSplitter.SplitAsync(dumpFile, Path.Combine(splitRoot, "m.sql"), Path.Combine(splitRoot, "t.sql"), CancellationToken.None);
            var splitMain = await File.ReadAllTextAsync(Path.Combine(splitRoot, "m.sql"));
            var splitTriggers = await File.ReadAllTextAsync(Path.Combine(splitRoot, "t.sql"));
            Check(splitCount == 1 && splitMain.Contains("CREATE TABLE `t`") && splitMain.Contains("CREATE TABLE `u`") && !splitMain.Contains("TRIGGER") &&
                  splitTriggers.Contains("TRIGGER trg_a") && splitTriggers.Contains("DELIMITER ;;") && splitTriggers.Contains("INSERT INTO log VALUES (1); END */;;") &&
                  !splitMain.Contains("DEFINER") && !splitTriggers.Contains("DEFINER"),
                "The restore dump splitter moves trigger blocks to a separate file and removes every DEFINER clause");
            Check(RestoreDumpSplitter.StripPlainDefiner("CREATE DEFINER=`root`@`127.0.0.1` TRIGGER x AFTER INSERT ON t FOR EACH ROW SET @a=1") == "CREATE TRIGGER x AFTER INSERT ON t FOR EACH ROW SET @a=1",
                "A plain DEFINER clause is removed from a trigger definition");
            Check(!MariaRestoreAccount.IsConfigured(dumpApp) && MariaRestoreAccount.IsConfigured(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:RestoreUser"] = "r", ["Database:RestorePassword"] = "p" }).Build()),
                "The real restoration needs the dedicated restore account (not the application or migrator account)");
        }
        finally { try { Directory.Delete(splitRoot, true); } catch (IOException) { } }
        var comboOptions = new List<SelectOption>
        {
            new(1, "Construct Demo SRL", "RO10000001 0721000001"), new(2, "Șantier Întâi SRL", "RO10000002"),
            new(3, "Ion Păun", "0744123456"), new(4, "Atelier Tehnic SRL")
        };
        Check(SearchableSelectRules.Filter(comboOptions, "").Count == 4 && SearchableSelectRules.Filter(comboOptions, null).Count == 4 &&
              SearchableSelectRules.Filter(comboOptions, "   ").Count == 4,
            "Combobox: an empty query keeps every option");
        Check(SearchableSelectRules.Filter(comboOptions, "construct").Single().Id == 1 && SearchableSelectRules.Filter(comboOptions, "CONSTRUCT dem").Single().Id == 1,
            "Combobox: filtering ignores letter case");
        Check(SearchableSelectRules.Filter(comboOptions, "santier intai").Single().Id == 2 && SearchableSelectRules.Filter(comboOptions, "ŞANTIER").Single().Id == 2 &&
              SearchableSelectRules.Filter(comboOptions, "paun").Single().Id == 3,
            "Combobox: filtering ignores Romanian diacritics in the query and in the options");
        Check(SearchableSelectRules.Filter(comboOptions, "ro10000002").Single().Id == 2 && SearchableSelectRules.Filter(comboOptions, "0744").Single().Id == 3,
            "Combobox: the extra search text (CUI, phone) also matches");
        Check(SearchableSelectRules.Filter(comboOptions, "srl").Select(o => o.Id).SequenceEqual([1, 2, 4]) && SearchableSelectRules.Filter(comboOptions, "inexistent").Count == 0,
            "Combobox: options narrow as more text is typed and an unknown text leaves none");

        var draftClock = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        var exitDraft = new ExitFormDraft { UtcNow = () => draftClock };
        var draftForm = new StockMovementInput
        {
            Kind = StockMovementKind.Exit, Date = new DateOnly(2026, 9, 28), Quantity = 7, Description = "Livrare parțială client",
            Destination = ExitDestination.Beneficiary, BeneficiaryId = 3, ProjectId = 9, SourceVehicleId = 2
        };
        exitDraft.Store(42, draftForm, true, "sugestie");
        draftForm.Quantity = 999; draftForm.Description = "modificat dupa salvare";
        Check(exitDraft.HasDraftFor(42) && !exitDraft.HasDraftFor(43), "Exit form draft: stored for one product only");
        var restoredAfterCancel = exitDraft.Take(42);
        Check(restoredAfterCancel is not null && restoredAfterCancel.Form.Quantity == 7 && restoredAfterCancel.Form.Description == "Livrare parțială client" && restoredAfterCancel.Form.Date == new DateOnly(2026, 9, 28) &&
              restoredAfterCancel.Form.Kind == StockMovementKind.Exit && restoredAfterCancel.Form.Destination == ExitDestination.Beneficiary && restoredAfterCancel.Form.BeneficiaryId == 3 &&
              restoredAfterCancel.Form.ProjectId == 9 && restoredAfterCancel.Form.SourceVehicleId == 2 && restoredAfterCancel.UseProject && restoredAfterCancel.Suggestion == "sugestie" &&
              restoredAfterCancel.NewBeneficiaryId is null && restoredAfterCancel.NewProjectId is null,
            "Exit form draft: a cancelled add returns exactly the values typed before (quantity, date, description, destination, source, project)");
        Check(exitDraft.Take(42) is null && !exitDraft.HasDraftFor(42), "Exit form draft: taken only once");
        exitDraft.Store(42, draftForm, false, null);
        exitDraft.SetNewBeneficiary(77);
        var withBeneficiary = exitDraft.Take(42);
        Check(withBeneficiary is { NewBeneficiaryId: 77, NewProjectId: null } && withBeneficiary.Form.Quantity == 999, "Exit form draft: the newly created beneficiary is handed back with the form");
        exitDraft.Store(42, draftForm, true, null);
        exitDraft.SetNewProject(55);
        Check(exitDraft.Take(42) is { NewProjectId: 55, NewBeneficiaryId: null }, "Exit form draft: the newly created project is handed back with the form");
        exitDraft.Store(42, draftForm, true, null);
        Check(exitDraft.Take(43) is null && !exitDraft.HasDraftFor(42), "Exit form draft: another product never receives it");
        exitDraft.Store(42, draftForm, true, null);
        draftClock = draftClock.AddMinutes(31);
        Check(exitDraft.Take(42) is null, "Exit form draft: expires after 30 minutes");
        exitDraft.SetNewBeneficiary(1); exitDraft.SetNewProject(1);
        Check(exitDraft.Take(42) is null, "Exit form draft: nothing is invented when none was stored");

        Check(AnafRules.HttpErrorMessage(404).Contains("HTTP 404") && AnafRules.HttpErrorMessage(404).Contains("Nu este vorba despre CUI") && AnafRules.HttpErrorMessage(404).Contains("administrator") &&
              AnafRules.HttpErrorMessage(500).Contains("HTTP 500") && AnafRules.HttpErrorMessage(429).Contains("HTTP 429") && AnafRules.HttpErrorMessage(418).Contains("HTTP 418"),
            "ANAF HTTP errors explain the cause and what to do (a 404 points at the configured address, not at the CUI)");

        // Journal on the server: windows, filters, paging, exact operation names, the CSV, the address state of the complete journal.
        {
            var journalBase = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
            var journal = Enumerable.Range(1, 60).Select(index => new AuditEvent(Guid.NewGuid(), journalBase.AddHours(index), index % 2 == 0 ? "Ana" : "bob",
                AccessRoles.Administrator, index % 3 == 0 ? AuditEntities.Beneficiary : AuditEntities.Product,
                index % 5 == 0 ? AuditActions.Delete : index == 7 ? "Modificare" : AuditActions.Edit, $"Obiect {index}", index == 12 ? "100% sigur" : $"detaliu {index}", "motiv", index.ToString())).ToList();
            var journalPage = AuditQueryRules.Page(journal, new(), 1, 10, null);
            Check(journalPage.Total == 60 && journalPage.Events.Count == 10 && journalPage.JournalTotal == 60 && journalPage.Events[0].Target == "Obiect 60" &&
                  AuditQueryRules.Page(journal, new(), 99, 10, null).Page == 6, "The journal page is newest first, counts all matches and clamps the page");
            var recentJournal = AuditQueryRules.Page(journal, new(Actor: "ANA"), 1, 100, 20);
            Check(recentJournal.Total == 10 && recentJournal.JournalTotal == 60 && recentJournal.Events.All(entry => entry.ActorUsername == "Ana" && entry.TimestampUtc > journalBase.AddHours(40)),
                "A window looks only at the latest events of the journal, and the operator filter ignores letter case");
            Check(AuditQueryRules.Page(journal, new(Action: AuditActions.Edit), 1, 100, null).Events.Any(entry => entry.Target == "Obiect 7") &&
                  AuditQueryRules.Page(journal, new(Action: AuditActions.Delete), 1, 100, null).Total == 12, "A legacy \"Modificare\" counts as an edit");
            Check(AuditQueryRules.Page(journal, new(Text: "100%"), 1, 10, null).Total == 1 && AuditQueryRules.Page(journal, new(Text: "OBIECT 3"), 1, 10, null).Total == 11,
                "Search ignores letter case and looks in target, details and reason");
            Check(AuditQueryRules.Page(journal, new(FromUtc: journalBase.AddHours(10), ToUtc: journalBase.AddHours(20)), 1, 100, null).Total == 10 &&
                  AuditQueryRules.Page(journal, new(), 1, 0, null).Events.Count == 60, "The range includes its start and excludes its end; page size 0 returns everything");
            Check(AuditQueryRules.Summary(journal, journalBase.AddHours(50)) == new AuditSummary(60, 11, 2, journal.Count(entry => entry.EntityType == AuditEntities.Product)), "The summary counts events, today, operators and products");
            var deletedObject = journal.Single(entry => entry.Target == "Obiect 10");
            var removalMap = AuditQueryRules.Removals(journal, [deletedObject, journal[0]]);
            Check(removalMap.Count == 1 && removalMap.ContainsKey(AuditNavigation.ObjectKey(deletedObject.EntityType, deletedObject.EntityId)),
                "Removal times are limited to the objects of the shown events");
            Check(AuditQueryRules.RecentWindow == 500 && AuditQueryRules.MaxRows >= 10_000 && AuditQueryRules.CompletePageSizes.SequenceEqual([25, 50, 100]) &&
                  !AuditActions.IsCreateOrEdit(AuditActions.ExportJournal) && AuditActions.ExportJournal == "Export jurnal" && new AuditQuery().IsEmpty && !new AuditQuery(Text: "x").IsEmpty,
                "The latest-events window is 500; the export has its own exact operation name and no link to an object");
            var csv = AuditQueryRules.Csv([journal[0] with { Details = "=SUM(1;2)", Motif = "a \"b\"\nc" }]);
            Check(csv.StartsWith("Data și ora (UTC);Operator;") && csv.Contains("\"'=SUM(1;2)\"") && csv.Contains("\"a \"\"b\"\" c\"") && csv.Contains("01.09.2026 09:00:00") && csv.EndsWith("\r\n"),
                "The CSV quotes values, neutralizes formulas and writes UTC times as dd.MM.yyyy");
            Check(AuditQueryRules.Describe(new(Actor: "ana", FromUtc: journalBase), d => d.ToString("dd.MM.yyyy")) == "operator: ana; de la 01.09.2026" && AuditQueryRules.Describe(new(), d => "") == "fără filtre",
                "The export is recorded with the filters that were used");
            var browserRange = new AuditUtcRange("2026-08-31T21:00:00.000Z", "2026-09-30T21:00:00.000Z");
            Check(browserRange.From == new DateTime(2026, 8, 31, 21, 0, 0, DateTimeKind.Utc) && browserRange.To?.Kind == DateTimeKind.Utc && new AuditUtcRange(null, null).From is null && new AuditUtcRange("nu", "").To is null,
                "The day range sent by the browser is read as UTC instants (and nothing when absent)");
            var journalToday = new DateOnly(2026, 9, 30);
            Check(AuditCompleteState.Parse(journalToday, null, null, null, null, null, null, null, null, null).Url() == "/jurnal/complet?de-la=2026-08-30" &&
                  AuditCompleteState.Parse(journalToday, null, null, null, null, "2026-09-05", "2026-09-01", null, 100, 3).Url() == "/jurnal/complet?de-la=2026-09-01&pana-la=2026-09-05&pe-pagina=100&pagina=3" &&
                  AuditCompleteState.Parse(journalToday, "a b", "Produs", null, "ana", null, null, "1", 7, 0).Url() == "/jurnal/complet?q=a%20b&tip=Produs&operator=ana&tot=1",
                "The complete journal shows the last month by default, keeps a swapped range in order, accepts only its page sizes and asks for the whole journal with tot=1");
            var journalFile = Path.Combine(Path.GetTempPath(), "blazorstoc-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(journalFile);
            try
            {
                var fileTrail = new FileAuditTrail(new TestWebHostEnvironment(journalFile), new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["App:AuditPath"] = Path.Combine(journalFile, "audit.jsonl") }).Build(), NullLogger<FileAuditTrail>.Instance);
                for (var index = 1; index <= 5; index++) await fileTrail.RecordAsync(new("ana", AccessRoles.Administrator, AuditEntities.Product, index == 4 ? AuditActions.Delete : AuditActions.Edit, $"Fisier {index}", "d", "", index.ToString()));
                var filePage = await fileTrail.QueryAsync(new(Text: "fisier"), 2, 2);
                var fileRemovals = await fileTrail.RemovalTimesAsync(filePage.Events.Concat((await fileTrail.QueryAsync(new(), 1, 10)).Events));
                Check(filePage.Total == 5 && filePage.Events.Count == 2 && filePage.Page == 2 && (await fileTrail.SummaryAsync(DateTime.UtcNow.Date)).Total == 5 && fileRemovals.Count == 1,
                    "The file journal answers the same queries, summary and removals");
            }
            finally { Directory.Delete(journalFile, true); }
        }
    }
}
