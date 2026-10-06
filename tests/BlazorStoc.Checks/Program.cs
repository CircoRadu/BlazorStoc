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

var data = await new DemoProductRepository().GetProductsAsync();
DateOnly? TestExpiry = new DateOnly(2027, 3, 15);

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
// INVOICE_CHECKS_ONLY=1 runs only the invoice template checks (fast loop while working on them).
if (Environment.GetEnvironmentVariable("INVOICE_CHECKS_ONLY") == "1")
{
    await InvoiceChecks.RunAsync(Check);
    Console.WriteLine("Invoice checks finished.");
    return;
}
// COMPONENT_CHECKS_ONLY=1 runs only the Razor component checks (bUnit).
if (Environment.GetEnvironmentVariable("COMPONENT_CHECKS_ONLY") == "1")
{
    await ComponentChecks.RunAsync(Check);
    await PickupWizardChecks.RunAsync(Check);
    await PickupWizardChecks.TemplateFlowAsync(Check);
    await ProductGroupsChecks.RunAsync(Check);
    ReasonSummaryChecks.Run(Check);
    Console.WriteLine("Component checks finished.");
    return;
}
ProductInput ProductEdit(Product product, string reason = "Test automat") { var input = ProductInput.From(product); input.Reason = reason; return input; }
WebUserInput UserEdit(WebUser user, string reason = "Test automat") { var input = WebUserInput.From(user); input.Reason = reason; return input; }
BeneficiaryInput LegalInput(string name, string cui) => new() { Name = name, Cui = cui, Address = "Strada Test 1, Bucuresti", Phone = "0721 000 111" };
BeneficiaryInput BeneficiaryEdit(Beneficiary beneficiary, string reason = "Test automat") { var input = BeneficiaryInput.From(beneficiary); input.Reason = reason; return input; }

var archiveAccess = new TestAccessControl(true, "archive.admin");
var archiveService = new ArchiveService(archiveAccess);
ArchiveOperation? firstArchiveOperation = null;
await archiveService.ExecuteAsync(ArchiveRequests.Product(
    new Product(901, "Test", "Contract", "Produs arhivat", "Date complete", 0, 7), "Motiv test"),
    (operation, _) => { firstArchiveOperation = operation; return Task.CompletedTask; });
ArchiveOperation? secondArchiveOperation = null;
await archiveService.ExecuteAsync(ArchiveRequests.Beneficiary(
    new Beneficiary(902, "Beneficiar arhivat", "RO12345678", 3), "Motiv test"),
    (operation, _) => { secondArchiveOperation = operation; return Task.CompletedTask; });
Check(firstArchiveOperation is not null && firstArchiveOperation.Id != Guid.Empty &&
      firstArchiveOperation.TimestampUtc.Kind == DateTimeKind.Utc &&
      firstArchiveOperation.ActorUsername == "archive.admin" &&
      firstArchiveOperation.ActorRole == AccessRoles.Administrator &&
      firstArchiveOperation.Request.Snapshot.OriginalId == "901" &&
      firstArchiveOperation.Request.Snapshot.Version == 7,
    "Archive contract supplies operation id, UTC timestamp, operator, original id and version");
Check(secondArchiveOperation is not null && secondArchiveOperation.Id != firstArchiveOperation!.Id,
    "Every archive operation receives a unique identifier");
var protectedUserSnapshot = ArchiveRequests.User(
    new WebUser(903, "archive.user", "Archive User", AccessRoles.LimitedUser, false, 2), "Motiv test",
    [new ArchiveProtectedValue("PasswordHash", "identity-password-hash")]);
Check(!protectedUserSnapshot.Snapshot.DataJson.Contains("password", StringComparison.OrdinalIgnoreCase) &&
      protectedUserSnapshot.Snapshot.ProtectedValues.Single().Hash == "identity-password-hash",
    "Password hashes are isolated from the public archive snapshot");
try
{
    ArchiveSnapshot.Create("Test", "1", 0, new { Password = "clear-text" });
    throw new Exception("Clear password accepted in public archive data");
}
catch (ArchiveContractException)
{
    Check(true, "Archive contract rejects clear password fields from public data");
}

Check(DeleteConfirmationRules.ValidateReason(string.Empty, string.Empty) is not null,
    "Delete confirmation requires an explicit reason choice");
Check(DeleteConfirmationRules.ResolveReason("Produsul", DeleteConfirmationRules.DefaultChoice, string.Empty) ==
      "Produsul nu va mai fi folosit",
    "The default deletion choice generates the object-specific audit reason");
Check(DeleteConfirmationRules.DefaultReason("Observația") == "Observația nu va mai fi folosită",
    "The default deletion reason agrees in gender with feminine subjects like Observația");
Check(DeleteConfirmationRules.ResolveReason("Beneficiarul", DeleteConfirmationRules.CustomChoice,
          "  Contract incheiat  ") == "Contract incheiat",
    "The custom deletion reason is trimmed and retained for audit");
Check(DeleteConfirmationRules.IsConfirmationValid("  sterge ") &&
      !DeleteConfirmationRules.IsConfirmationValid("Sterge") &&
      !DeleteConfirmationRules.IsConfirmationValid("șterge") &&
      !DeleteConfirmationRules.IsConfirmationValid("sterge acum"),
    "Final deletion confirmation accepts only the exact case-sensitive word sterge");

// Subtask 3.3 (Task 3): same strict-comparison rule as the deletion word, but its own word.
Check(RestoreConfirmationRules.IsConfirmationValid("  confirma ") &&
      !RestoreConfirmationRules.IsConfirmationValid("Confirma") &&
      !RestoreConfirmationRules.IsConfirmationValid("confirmă") &&
      !RestoreConfirmationRules.IsConfirmationValid("confirma acum"),
    "Restore confirmation accepts only the exact case-sensitive word confirma");

Check(MariaSchemaMigrations.All.Count > 0 && MariaSchemaMigrations.All.Select(m => m.Version).Distinct().Count() == MariaSchemaMigrations.All.Count &&
      MariaSchemaMigrations.All.All(m => m.Statements.All(sql => sql.Split("ADD COLUMN").Length - 1 == sql.Split("ADD COLUMN IF NOT EXISTS").Length - 1 &&
          !System.Text.RegularExpressions.Regex.IsMatch(sql, @"DROP\s+(TABLE|COLUMN|DATABASE|SCHEMA)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) &&
          m.ExpectedColumns.All(c => m.Statements.Any(sql => sql.Contains($"`{c.Column}`") && sql.Contains($"`{c.Table}`")))),
    "MariaDB migrations are idempotent (ADD COLUMN IF NOT EXISTS), never drop a table or column, and every expected column is created by its migration");
Check(MariaSchemaMigrations.All.Single(m => m.Version == 5).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
        new[] { "resolved_by", "resolved_utc", "resolved_reason", "resolved_auto", "object_label", "snapshot_values", "snapshot_subject", "snapshot_body", "snapshot_source", "active_source_key" }.Order()) &&
      MariaSchemaMigrations.All.Single(m => m.Version == 5).Statements.Any(sql => sql.Contains("uq_expiry_notifications_event") && sql.Contains("`source_key`, `object_id`, `expiry_date`")) &&
      MariaSchemaMigrations.All.Single(m => m.Version == 5).Statements.Any(sql => sql.Contains("uq_notification_templates_active")),
    "MariaDB migration 5 adds the resolution columns, the unique key per event and the unique active template per event");
Check(MariaSchemaMigrations.All.Single(m => m.Version == 6).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
        new[] { "id", "purge_enabled", "purge_months", "last_purge_utc", "version" }.Order()) &&
      MariaSchemaMigrations.All.Single(m => m.Version == 6).Statements.All(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `notification_settings`")),
    "MariaDB migration 6 creates the notification settings table (one row, versioned)");
Check(MariaSchemaMigrations.All.Single(m => m.Version == 1).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
        new[] { "kind", "address", "phone", "registry_number", "postal_code", "caen_code", "anaf_verified" }.Order()),
    "MariaDB migration 1 covers exactly the beneficiary columns used by MariaBeneficiaryRepository");
Check(MariaSchemaMigrations.All.Single(m => m.Version == 2).ExpectedColumns.Select(c => c.Column).Order().SequenceEqual(
        new[] { "id", "beneficiary_id", "name", "address", "normalized_address", "phone", "contact_person", "version" }.Order()) &&
      MariaSchemaMigrations.All.Single(m => m.Version == 2).Statements.All(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `beneficiary_work_points`")),
    "MariaDB migration 2 creates the work points table used by MariaWorkPointRepository");
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

// Maintenance map: the separate window and the refresh compare the rows by value.
{
    var mapDay = new DateOnly(2026, 9, 30);
    ServiceDueRow MapRow(int id, DateOnly due) => new(5, "Ben", new ServiceContract(1, 5, "C1", mapDay, 12, null, true, "", 0),
        new ServiceContractPointView(new ServiceContractPoint(id, 1, 7, null, due, 0), "Punct", "Adresa", true), 45.1m, 25.6m, null);
    var shownRows = new[] { MapRow(1, mapDay), MapRow(2, mapDay) };
    Check(MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay)]) == 0 &&
          MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay.AddDays(1))]) == 1 &&
          MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay), MapRow(2, mapDay), MapRow(3, mapDay)]) == 1 &&
          MaintenanceMapRules.ChangedRows(shownRows, [MapRow(1, mapDay)]) == 0,
        "The automatic refresh counts the rows that are new or changed, and none when nothing differs");
    Check(MaintenanceMapRules.WindowUrl == "/mentenanta/harta/fereastra" && MaintenanceMapRules.WindowName == "blazorstoc-harta" && MaintenanceMapRules.AutoRefreshSeconds is >= 15 and <= 120,
        "The map window has a fixed address and name (one reused window) and refreshes at a moderate pace");
}

// Settings → Hartă: the tile provider and the pin types of the overlay.
{
    var engine = new MapEngineSettings();
    Check(MapEngineRules.Validate(engine) is null && MapEngineRules.Validate(new MapEngineSettings { TileUrl = "http://t.example/{z}/{x}/{y}.png" }) is not null &&
          MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}.png" }) is not null && MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://u:p@t.example/{z}/{x}/{y}.png" }) is not null &&
          MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}" }) is { } missingKey && missingKey.Contains("Cheie furnizor") &&
          MapEngineRules.Validate(new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}", ApiKey = "abc" }) is null,
        "The tile address must be https with {z}/{x}/{y}, no credentials, and {key} needs a key");
    Check(MapEngineRules.Validate(new MapEngineSettings { MinZoom = 10, MaxZoom = 5 }) is not null && MapEngineRules.Validate(new MapEngineSettings { StartZoom = 1, MinZoom = 3 }) is not null &&
          MapEngineRules.Validate(new MapEngineSettings { CenterLatitude = 100 }) is not null && MapEngineRules.Validate(new MapEngineSettings { AttributionText = " " }) is not null &&
          MapEngineRules.Validate(new MapEngineSettings { AttributionUrl = "http://x.example" }) is not null, "The zoom range, the centre and the attribution are validated");
    var keyed = new MapEngineSettings { TileUrl = "https://t.example/{z}/{x}/{y}.png?k={key}", ApiKey = "a b&c" }.ToOptions();
    Check(keyed.TileUrl == "https://t.example/{z}/{x}/{y}.png?k=a%20b%26c", "The key is put into the address (escaped) for the map page only");
    var attribution = MapEngineRules.AttributionHtml("<b>x</b> & y", "https://example.org/a?b=1&c=2");
    Check(!attribution.Contains("<b>") && attribution.Contains("&lt;b&gt;x&lt;/b&gt; &amp; y") && attribution.StartsWith("<a href=\"https://example.org/a?b=1&amp;c=2\"") && MapEngineRules.AttributionHtml("x", "javascript:alert(1)") == "x",
        "The attribution is text that becomes safe markup (an https link, everything else encoded)");
    var fromDefaults = MapEngineSettings.FromOptions(new MapOptions());
    Check(fromDefaults.AttributionText == "© OpenStreetMap contributors" && fromDefaults.AttributionUrl == "https://www.openstreetmap.org/copyright" && fromDefaults.ToOptions().TileUrl == MapOptions.DefaultTileUrl,
        "The starting values are those of appsettings.json");
    var keyChanges = MapEngineRules.Changes(new MapEngineSettings(), new MapEngineSettings { ApiKey = "secret-key-1" }).Where(change => change.Before != change.After).ToList();
    Check(keyChanges.Count == 1 && keyChanges[0].Field == "Cheie furnizor" && !keyChanges[0].After.Contains("secret") && keyChanges[0].After == "schimbată",
        "The journal never holds the key itself");

    var defaults = MapPinRules.Defaults();
    Check(defaults.Count == 6 && defaults.Count(type => type.IsFill) == 4 && defaults.All(type => type.BuiltIn) && defaults.Select(type => type.Id).Distinct().Count() == 6 && defaults.All(type => type.Id < MapPinRules.FirstCustomId),
        "Six built-in types: four fills and two badges, with ids below the ones of custom types");
    Check(MapPinRules.Foreground("#ffffff") == "#1d2b2e" && MapPinRules.Foreground("#000000") == "#ffffff" && MapPinRules.Foreground("#d99a1f") == "#1d2b2e" && MapPinRules.Foreground("#c0392b") == "#ffffff" && MapPinRules.Foreground("nope") == "#ffffff",
        "The glyph colour is the one that reads better on the pin colour");
    var edited = defaults.Select(type => type.Clone()).ToList();
    edited[0].Name = "Depășit"; edited[0].Color = "#FF0000"; edited[4].Active = false; edited[3].Active = false;
    var merged = MapPinRules.Effective(edited);
    Check(merged.Count == 6 && merged.Single(type => type.Key == "overdue").Name == "Depășit" && merged.Single(type => type.Key == "overdue").Color == "#ff0000" && !merged.Single(type => type.Kind == "badge" && type.Key == "expired").Active &&
          merged.Single(type => type.Key == "off").Active && MapPinRules.Effective(null).Count == 6, "Stored edits are merged over the built-in types; a fill type can never be switched off");
    var custom = new MapPinType { Id = 1000, Key = "c1000", Kind = "badge", Rule = MapPinType.RuleNoIntervention, Months = 6, Name = "Fără vizită", Color = "#7a4fb5", Glyph = "V", Active = true };
    Check(MapPinRules.Validate(custom, defaults) is null && MapPinRules.Validate(custom.With(type => type.Name = "La zi"), defaults) is { } dup && dup.Contains("Există deja") &&
          MapPinRules.Validate(defaults[0].With(type => type.Color = defaults[2].Color), defaults.Skip(1)) is { } sameColor && sameColor.Contains("folosită deja") &&
          MapPinRules.Validate(custom.With(type => type.Glyph = "<b"), defaults) is not null && MapPinRules.Validate(custom.With(type => type.Color = "red"), defaults) is not null &&
          MapPinRules.Validate(custom.With(type => type.Months = 0), defaults) is not null && MapPinRules.Validate(custom.With(type => { type.Id = 0; type.Name = "D"; }), defaults.Concat([custom.With(t => t.Id = 1001), custom.With(t => { t.Id = 1002; t.Name = "B"; }), custom.With(t => { t.Id = 1003; t.Name = "C"; })])) is { } tooMany && tooMany.Contains("cel mult"),
        "Pin types are validated: name, colour, glyph, months, unique names and fill colours, at most three custom badges");

    var day = new DateOnly(2026, 9, 30);
    ServiceDueRow PinRow(DateOnly? last, bool active = true) => new(5, "Ben", new ServiceContract(1, 5, "C1", day, 12, null, active, "", 0),
        new ServiceContractPointView(new ServiceContractPoint(1, 1, 7, null, day.AddDays(40), 0), "Punct", "Adresa", true), 45.1m, 25.6m, last);
    var customTypes = merged.Concat([custom]).ToList();
    Check(MapPinRules.ExtraBadges(PinRow(null), day, customTypes).SequenceEqual(["c1000"]) && MapPinRules.ExtraBadges(PinRow(day.AddMonths(-6)), day, customTypes).Count == 1 &&
          MapPinRules.ExtraBadges(PinRow(day.AddMonths(-5)), day, customTypes).Count == 0 && MapPinRules.ExtraBadges(PinRow(null, false), day, customTypes).Count == 0 &&
          MapPinRules.ExtraBadges(PinRow(null), day, customTypes.Select(type => type.Id == 1000 ? type.With(t => t.Active = false) : type).ToList()).Count == 0 && MapPinRules.ExtraBadges(PinRow(null), day, null).Count == 0,
        "A custom badge follows its rule (N months or never), only for contracts that are On and only while active");
    Check(MaintenanceMapRules.Marker(PinRow(null), day, 30, 30, customTypes)?.Extra?.Single() == "c1000" && MaintenanceMapRules.Marker(PinRow(null), day, 30, 30)?.Extra is { Count: 0 },
        "The marker carries the keys of its custom badges");
    Check(SettingsNavigation.MapEngineUrl == "/setari?tab=harta&subtab=motor" && SettingsNavigation.MapPinTypeUrl(1000) == "/setari?tab=harta&subtab=overlay&pin=1000" &&
          new[] { AuditActions.EditMapEngine, AuditActions.ResetMapEngine, AuditActions.CreateMapPinType, AuditActions.EditMapPinType }.All(AuditActions.IsCreateOrEdit) && !AuditActions.IsCreateOrEdit(AuditActions.DeleteMapPinType) &&
          new[] { AuditActions.EditMapEngine, AuditActions.ResetMapEngine, AuditActions.CreateMapPinType, AuditActions.EditMapPinType, AuditActions.DeleteMapPinType }.Distinct().Count() == 5 &&
          AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.Administrator, AuditEntities.MapPinType, AuditActions.EditMapPinType, "Tip pin: X", "d", "", "1000")) == "/setari?tab=harta&subtab=overlay&pin=1000" &&
          AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.Administrator, AuditEntities.MapEngine, AuditActions.EditMapEngine, "Furnizor hartă", "d", "", "1")) == "/setari?tab=harta&subtab=motor" &&
          AuditFilterOptions.Actions.Any(option => option.Value == AuditActions.CreateMapPinType) && AuditFilterOptions.Entities.Any(option => option.Value == AuditEntities.MapEngine),
        "Each map settings operation has its own journal action, linked to its sub-tab (the pin type highlighted)");

    var mapDirectory = Path.Combine(Path.GetTempPath(), "blazorstoc-map-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(mapDirectory);
    try
    {
        var mapConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Map:ConfigurationPath"] = Path.Combine(mapDirectory, "map.json") }).Build();
        var mapTrail = new TestAuditTrail();
        MapConfigurationService MapService(bool admin = true) => new(new MapConfigStore(mapConfig), Microsoft.Extensions.Options.Options.Create(new MapOptions()), new TestAccessControl(admin, "ana"), mapTrail);
        var mapService = MapService();
        var viewBefore = await mapService.GetViewAsync();
        Check(viewBefore.Version == 0 && viewBefore.Engine.TileUrl == MapOptions.DefaultTileUrl && viewBefore.PinTypes.Count == 6, "Without saved settings the map uses appsettings.json and the built-in pin types");
        var stateBefore = await mapService.GetStateAsync();
        Check(!stateBefore.EngineSaved && stateBefore.Version == 0, "The settings page starts from the defaults");
        var newEngine = stateBefore.Engine.Clone(); newEngine.TileUrl = "https://tiles.example/{z}/{x}/{y}.png"; newEngine.MaxZoom = 17;
        await mapService.SaveEngineAsync(newEngine, 0);
        var afterEngine = await mapService.GetViewAsync();
        Check(afterEngine.Version == 1 && afterEngine.Engine.TileUrl == "https://tiles.example/{z}/{x}/{y}.png" && afterEngine.Engine.MaxZoom == 17 &&
              mapTrail.Entries.Last() is { Action: var engineAction, EntityType: var engineEntity, Details: var engineDetails } && engineAction == AuditActions.EditMapEngine && engineEntity == AuditEntities.MapEngine &&
              engineDetails.Contains("Adresa dalelor: https://tile.openstreetmap.org/{z}/{x}/{y}.png → https://tiles.example/{z}/{x}/{y}.png") && engineDetails.Contains("Zoom maxim: 19 → 17"),
            "Saving the engine is journaled with the exact operation and the old and the new values");
        await RejectedMap(() => mapService.SaveEngineAsync(newEngine, 0), "A save on an outdated version is refused");
        await RejectedMap(() => mapService.SaveEngineAsync(newEngine, 1), "A save without changes is refused");
        await RejectedMap(() => mapService.SaveEngineAsync(new MapEngineSettings { TileUrl = "http://x/{z}/{x}/{y}" }, 1), "An invalid address is refused");
        await RejectedMap(() => MapService(false).SaveEngineAsync(newEngine, 1), "Only an administrator changes the engine");
        var createdPin = await mapService.SavePinTypeAsync(new MapPinType { Name = "Fără vizită", Color = "#7a4fb5", Glyph = "V", Months = 6, Active = true, Order = 5 }, 1);
        Check(createdPin.Id == MapPinRules.FirstCustomId && createdPin.Key == "c1000" && createdPin.Kind == "badge" && createdPin.Rule == "no-intervention" && !createdPin.BuiltIn &&
              mapTrail.Entries.Last().Action == AuditActions.CreateMapPinType && mapTrail.Entries.Last().EntityId == "1000", "A custom badge is added with its own id, key and journal action");
        var builtIn = (await mapService.GetStateAsync()).PinTypes.Single(type => type.Key == "overdue").Clone();
        builtIn.Name = "Depășită"; builtIn.Color = "#AA0000"; builtIn.Active = false; builtIn.Kind = "badge"; builtIn.Key = "hacked";
        await mapService.SavePinTypeAsync(builtIn, 2);
        var afterBuiltIn = (await mapService.GetViewAsync()).PinTypes;
        Check(afterBuiltIn.Single(type => type.Key == "overdue") is { Name: "Depășită", Color: "#aa0000", Active: true, Kind: "fill" } && afterBuiltIn.All(type => type.Key != "hacked") &&
              mapTrail.Entries.Last().Action == AuditActions.EditMapPinType && mapTrail.Entries.Last().Details.Contains("Culoare: #c0392b → #aa0000"),
            "A built-in type keeps its kind, key and rule (and stays on when it is a fill); the edit is journaled with old and new values");
        await RejectedMap(() => mapService.SavePinTypeAsync(new MapPinType { Name = "fără vizită", Color = "#123456", Months = 3, Active = true }, 3), "Pin type names are unique without regard to letter case");
        await RejectedMap(() => mapService.DeletePinTypeAsync(1, "motiv", 3), "A built-in type cannot be deleted");
        await RejectedMap(() => mapService.DeletePinTypeAsync(MapPinRules.FirstCustomId, "", 3), "A deletion needs a reason");
        await mapService.DeletePinTypeAsync(MapPinRules.FirstCustomId, "nu mai e nevoie", 3);
        Check((await mapService.GetViewAsync()).PinTypes.All(type => type.BuiltIn) && mapTrail.Entries.Last().Action == AuditActions.DeleteMapPinType && mapTrail.Entries.Last().Motif == "nu mai e nevoie", "A custom badge can be deleted with a reason, and the deletion is journaled");
        await mapService.ResetEngineAsync(4);
        var afterReset = await mapService.GetViewAsync();
        Check(afterReset.Engine.TileUrl == MapOptions.DefaultTileUrl && !(await mapService.GetStateAsync()).EngineSaved && mapTrail.Entries.Last().Action == AuditActions.ResetMapEngine && afterReset.PinTypes.Single(type => type.Key == "overdue").Name == "Depășită",
            "Going back to appsettings.json resets the engine only (journaled); the pin types stay");
        File.WriteAllText(Path.Combine(mapDirectory, "map.json"), "{ not json");
        Check((await mapService.GetViewAsync()).PinTypes.Count == 6, "A damaged file falls back to the defaults instead of breaking the map");
    }
    finally { Directory.Delete(mapDirectory, true); }
    async Task RejectedMap(Func<Task> operation, string message)
    {
        try { await operation(); Check(false, message); }
        catch (MapOperationException) { Check(true, message); }
        catch (AccessDeniedException) { Check(true, message); }
    }
}

var anafConfig = new AnafConfig();
Check(AnafRules.Validate(anafConfig) is null, "ANAF default configuration is valid");
Check(AnafRules.NormalizeCui(anafConfig, " ro9178894 ") == "9178894" && AnafRules.NormalizeCui(anafConfig, "28996610") == "28996610" &&
      AnafRules.NormalizeCui(anafConfig, "0123") is null && AnafRules.NormalizeCui(anafConfig, "RO") is null && AnafRules.NormalizeCui(anafConfig, "12345678901") is null,
    "ANAF CUI normalization strips RO and spaces and rejects invalid values");
var anafRequest = AnafRules.Prepare(anafConfig, "RO9178894", new DateOnly(2026, 9, 29));
Check(anafRequest.Cui == "9178894" && anafRequest.Body.Replace(" ", "") == "[{\"cui\":9178894,\"data\":\"2026-09-29\"}]",
    "ANAF request template substitutes CUI and ISO date");
var badTemplate = anafConfig.Clone(); badTemplate.Template = "[{\"cui\": {{cui}}, \"x\": {{altceva}}}]";
var brokenJson = anafConfig.Clone(); brokenJson.Template = "[{\"cui\": {{cui}}";
var otherHost = anafConfig.Clone(); otherHost.Url = "https://example.com/api";
var httpUrl = anafConfig.Clone(); httpUrl.Url = "http://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
var fastInterval = anafConfig.Clone(); fastInterval.Interval = 200;
var badPath = anafConfig.Clone(); badPath.Mappings[1].Path = "$.a[*].b";
var reservedHeader = anafConfig.Clone(); reservedHeader.Headers.Add(new AnafHeader { Name = "Authorization", Value = "x" });
Check(AnafRules.Validate(otherHost) is not null && AnafRules.Validate(httpUrl) is not null && AnafRules.Validate(fastInterval) is not null &&
      AnafRules.Validate(badPath) is not null && AnafRules.Validate(reservedHeader) is not null,
    "ANAF validation rejects foreign hosts, plain HTTP, too-fast intervals, unsupported paths and reserved headers");
foreach (var broken in new[] { badTemplate, brokenJson })
{
    try { AnafRules.Prepare(broken, "9178894", new DateOnly(2026, 9, 29)); Check(false, "ANAF invalid template must be rejected"); }
    catch (AnafException) { Check(true, "ANAF unknown variable and invalid JSON templates are rejected"); }
}
var anafSample = AnafRules.Interpret(anafConfig, anafRequest, 200, 10,
    "{\"found\":[{\"date_generale\":{\"cui\":9178894,\"denumire\":\"FIRMA SRL\",\"statusRO_e_Factura\":true},\"inregistrare_scop_Tva\":{\"scpTVA\":false}}],\"notFound\":[]}");
string? AnafValue(string label) => anafSample.Mapped.Single(m => m.Label == label).Value;
Check(anafSample.Ok && AnafValue("Denumire") == "FIRMA SRL" && AnafValue("Plătitor TVA") == "Nu" && AnafValue("RO e-Factura") == "Da" &&
      AnafValue("Telefon") is null && AnafValue("Inactiv fiscal") is null,
    "ANAF response mapping keeps false distinct from absent fields");
Check(!AnafRules.Interpret(anafConfig, anafRequest, 200, 1, "{\"found\":[],\"notFound\":[9178894]}").Ok &&
      !AnafRules.Interpret(anafConfig, anafRequest, 200, 1, "not json").Ok,
    "ANAF unknown CUI and invalid JSON are reported as failures");
var requiredMissing = anafConfig.Clone(); requiredMissing.Mappings[2].Missing = "error";
Check(!AnafRules.Interpret(requiredMissing, anafRequest, 200, 1,
    "{\"found\":[{\"date_generale\":{\"cui\":9178894,\"denumire\":\"X\"}}],\"notFound\":[]}").Ok,
    "ANAF missing-field policy 'error' blocks a successful test");
Check(AnafRules.Fingerprint(anafConfig) == AnafRules.Fingerprint(anafConfig.Clone()) && AnafRules.Fingerprint(anafConfig) != AnafRules.Fingerprint(fastInterval),
    "ANAF configuration fingerprint follows content");

Check(data.Count == 12, "Demonstration catalogue has 12 fictional products");
Check(ProductSearch.Filter(data, "  POLIZOR  ", "name", "", "").Single().Id == 2, "Search is case-insensitive and trims spaces");
Check(!ProductSearch.Filter(data, "mandrina", "name", "", "").Any(), "Name-only search excludes description matches");
Check(ProductSearch.Filter(data, "mandrina", "description", "", "").Single().Id == 1, "Description-only search works with normalized (diacritic-free) text");
Check(ProductSearch.Filter(data, "", "all", "Masurare", "zero").Single().Id == 5, "Category and stock filters combine");
Check(ProductSearch.Filter(data, "", "all", "", "negative").Single().Id == 8, "Negative stock is distinct from zero stock");
Check(ProductSearch.Filter(data, "", "all", "", "zero").Count() == 2, "Zero stock classification");
Check(!ProductSearch.Filter(data, "' OR 1=1 --", "all", "", "").Any(), "Search text is handled as literal text");
Check(!ProductSearch.Filter(Array.Empty<Product>(), "", "all", "", "").Any(), "Empty database produces an empty list");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await new DemoProductRepository().GetProductsAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Console.WriteLine("PASS: Cancellation is respected"); }

var repository = new DemoProductRepository();
async Task Rejected(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (ProductOperationException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
async Task EnsureProductGroupAsync(IProductRepository productRepository, string category, string subcategory)
{
    var available = await productRepository.GetGroupsAsync();
    if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category)))
        await productRepository.CreateCategoryAsync(category);
    available = await productRepository.GetGroupsAsync();
    if (!available.Any(group => TextNormalization.SameUniqueValue(group.Category, category) &&
                                TextNormalization.SameUniqueValue(group.Subcategory, subcategory)))
        await productRepository.CreateSubcategoryAsync(category, subcategory);
}
async Task<Product> CreateProductAsync(IProductRepository productRepository, ProductInput input)
{
    await EnsureProductGroupAsync(productRepository, input.Category, input.Subcategory);
    return await productRepository.CreateAsync(input);
}
var created = await CreateProductAsync(repository, new ProductInput { Name = "  Șurub   nou  ", Category = "Categorie nouă", Subcategory = "Subcategorie nouă", Description = "Țeavă și șaibă" });
Check(created.Id == 13 && created.Name == "Surub nou" && created.Description == "Teava si saiba" && (await repository.GetProductsAsync()).Contains(created), "Create removes diacritics before persistence");
Check((await repository.GetGroupsAsync()).Contains(new ProductGroup("Categorie noua", "Subcategorie noua")), "New category and subcategory are available without diacritics");
var selectionRulesRepository = new DemoProductRepository();
await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
    { Name = "Produs fara grup", Category = "Categorie inexistenta", Subcategory = "Subcategorie inexistenta" }),
    "Product creation rejects a category that was not created in administration");
await selectionRulesRepository.CreateCategoryAsync("Categorie fara subcategorie");
await Rejected(() => selectionRulesRepository.CreateAsync(new ProductInput
    { Name = "Produs fara subcategorie", Category = "Categorie fara subcategorie", Subcategory = "Subcategorie inexistenta" }),
    "Product creation rejects a subcategory that was not created in administration");
Check(TextNormalization.SameUniqueValue("Categorie nouă", "cATEGORIE NOUA"), "Uniqueness validation ignores case and diacritics");
Check(TextNormalization.SameUniqueValue("Cod   produs", " Cod produs "), "Uniqueness validation ignores repeated and exterior spaces");
try
{
    await repository.CreateAsync(new ProductInput { Name = "șURUB NOU", Category = "Altă categorie", Subcategory = "Altă subcategorie" });
    throw new Exception("Duplicate product accepted");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Categorie noua", StringComparison.Ordinal) && exception.Message.Contains("Subcategorie noua", StringComparison.Ordinal),
        "Duplicate product is rejected globally and reports its category and subcategory");
    Check(exception.Message.Contains("Codul produsului «Surub nou» există deja", StringComparison.Ordinal),
        "Duplicate product code message names the existing code");
}
foreach (var variant in new[] { "  surub   NOU ", "ȘURUB NOU", "Șurub nou" })
    await Rejected(() => repository.CreateAsync(new ProductInput
        { Name = variant, Category = "Categorie nouă", Subcategory = "Subcategorie nouă" }),
        $"Product code «{variant}» is a duplicate despite spacing, case or diacritics");
try
{
    new ProductInput { Name = "   ", Category = "Test", Subcategory = "Test" }.Validated();
    throw new Exception("Empty product code accepted");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Completează codul produsului.", StringComparison.Ordinal),
        "A product cannot be saved without a product code");
}
try
{
    new ProductInput { Name = new string('C', 101), Category = "Test", Subcategory = "Test" }.Validated();
    throw new Exception("Overlong product code accepted");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Codul produsului poate avea cel mult 100 de caractere.", StringComparison.Ordinal),
        "Product code keeps the existing 100-character limit");
}
Check(!ArchiveRequests.Product(created, "Motiv test").Target.Contains('#') &&
      ArchiveRequests.Product(created, "Motiv test").Details.Contains("Cod produs: Surub nou", StringComparison.Ordinal),
    "Product archive target hides the internal identifier and labels the product code");
var duplicateRename = ProductEdit(created);
duplicateRename.Name = "MAȘINĂ DE GĂURIT CU ACUMULATOR";
await Rejected(() => repository.UpdateAsync(created, duplicateRename), "Renaming a product to an existing catalogue name is rejected");
var groupRulesRepository = new DemoProductRepository();
var existingGroupProduct = await groupRulesRepository.CreateAsync(new ProductInput
    { Name = "Produs categorie existenta", Category = "mĂSURARE", Subcategory = "NIVÉLARE" });
Check(existingGroupProduct.Category == "Masurare" && existingGroupProduct.Subcategory == "Nivelare",
    "Existing category and subcategory are reused without case or diacritic differences");
try
{
    await groupRulesRepository.CreateCategoryAsync("Categorie complet noua");
    await groupRulesRepository.CreateAsync(new ProductInput
        { Name = "Produs subcategorie duplicata", Category = "Categorie complet noua", Subcategory = "fÍXARE" });
    throw new Exception("Duplicate subcategory accepted in another category");
}
catch (ProductOperationException exception)
{
    Check(exception.Message.Contains("Consumabile", StringComparison.Ordinal),
        "A globally duplicate subcategory is rejected and reports its existing category");
}
Check((await new DemoProductRepository().GetProductsAsync()).Count == 12, "Demo changes are isolated to a session");
var sharedProductStore = new DemoProductStore();
var sharedWriter = new DemoProductRepository(sharedStore: sharedProductStore);
await CreateProductAsync(sharedWriter, new ProductInput { Name = "Produs meniu", Category = "Categorie meniu", Subcategory = "Subcategorie meniu" });
var sharedReader = new DemoProductRepository(sharedStore: sharedProductStore);
Check((await sharedReader.GetGroupsAsync()).Contains(new ProductGroup("Categorie meniu", "Subcategorie meniu")), "Product menu sees categories created by another repository scope");
var emptyGroupStore = new DemoProductStore();
var emptyGroupRepository = new DemoProductRepository(sharedStore: emptyGroupStore);
await emptyGroupRepository.CreateCategoryAsync("Categorie creata din administrare");
Check((await emptyGroupRepository.GetGroupsAsync()).Contains(new ProductGroup("Categorie creata din administrare", "")),
    "Category administration creates an empty in-memory category");
var administeredSubcategory = await emptyGroupRepository.CreateSubcategoryAsync(
    "Categorie creata din administrare", "Subcategorie creata din administrare");
Check((await emptyGroupRepository.GetGroupsAsync()).Contains(administeredSubcategory),
    "Category administration creates an in-memory subcategory in the selected category");
await Rejected(() => emptyGroupRepository.CreateCategoryAsync("CATEGORIE CREATĂ DIN ADMINISTRARE"),
    "Category creation rejects normalized duplicates");
var productToMove = await CreateProductAsync(emptyGroupRepository, new ProductInput
{
    Name = "Produs pentru grup gol",
    Category = "Categorie pastrata",
    Subcategory = "Subcategorie pastrata"
});
var moveProduct = ProductInput.From(productToMove);
moveProduct.Category = "Categorie destinatie";
moveProduct.Subcategory = "Subcategorie destinatie";
moveProduct.Reason = "Verificare pastrare grup";
await EnsureProductGroupAsync(emptyGroupRepository, moveProduct.Category, moveProduct.Subcategory);
var movedProduct = await emptyGroupRepository.UpdateAsync(productToMove, moveProduct);
var groupsAfterMove = await emptyGroupRepository.GetGroupsAsync();
Check(groupsAfterMove.Contains(new ProductGroup("Categorie pastrata", "Subcategorie pastrata")) &&
      groupsAfterMove.Contains(new ProductGroup("Categorie destinatie", "Subcategorie destinatie")),
    "In-memory catalogue keeps the source group after its last product is moved");
await emptyGroupRepository.DeleteAsync(movedProduct, "Verificare grup gol");
var groupsAfterLastDelete = await emptyGroupRepository.GetGroupsAsync();
Check(groupsAfterLastDelete.Contains(new ProductGroup("Categorie destinatie", "Subcategorie destinatie")),
    "In-memory catalogue keeps a group after its last product is deleted");
var reusedEmptyGroup = await emptyGroupRepository.CreateAsync(new ProductInput
{
    Name = "Produs grup refolosit",
    Category = "CATEGORIE PASTRATA",
    Subcategory = "SUBCATEGORIE PASTRATA"
});
Check(reusedEmptyGroup.Category == "Categorie pastrata" && reusedEmptyGroup.Subcategory == "Subcategorie pastrata" &&
      (await emptyGroupRepository.GetGroupsAsync()).Count(group =>
          TextNormalization.SameUniqueValue(group.Category, "Categorie pastrata") &&
          TextNormalization.SameUniqueValue(group.Subcategory, "Subcategorie pastrata")) == 1,
    "An empty in-memory group is reused without creating case-insensitive duplicates");
await emptyGroupRepository.RenameCategoryAsync("Categorie pastrata", "Categorie redenumita", "Corectie denumire");
Check((await emptyGroupRepository.GetProductsAsync()).Single(product => product.Id == reusedEmptyGroup.Id).Category == "Categorie redenumita" &&
      (await emptyGroupRepository.GetGroupsAsync()).Contains(new ProductGroup("Categorie redenumita", "Subcategorie pastrata")),
    "Renaming an in-memory category updates its products and catalogue group");
var updatedDemoGroup = await emptyGroupRepository.UpdateSubcategoryAsync(
    new ProductGroup("Categorie redenumita", "Subcategorie pastrata"), "Subcategorie redenumita",
    "Categorie destinatie", "Reorganizare catalog");
Check(updatedDemoGroup == new ProductGroup("Categorie destinatie", "Subcategorie redenumita") &&
      (await emptyGroupRepository.GetProductsAsync()).Single(product => product.Id == reusedEmptyGroup.Id) is var movedDemoProduct &&
      movedDemoProduct.Category == updatedDemoGroup.Category && movedDemoProduct.Subcategory == updatedDemoGroup.Subcategory,
    "Renaming and moving an in-memory subcategory updates all associated products");
await Rejected(() => emptyGroupRepository.RenameCategoryAsync("Categorie redenumita", "Categorie destinatie", "Duplicat"),
    "Category administration rejects a case-insensitive duplicate");
var spacingRepository = new DemoProductRepository();
var spacingProduct = await spacingRepository.CreateAsync(new ProductInput
{
    Name = "  Produs   cu   spatii  ", Category = "Măsurare", Subcategory = "Nivelare"
});
Check(spacingProduct.Name == "Produs cu spatii", "Product creation removes exterior and repeated spaces from its name");
var spacingProductEdit = ProductInput.From(spacingProduct);
spacingProductEdit.Name = "  Produs   editat   cu spatii  ";
spacingProductEdit.Reason = "Verificare normalizare";
spacingProduct = await spacingRepository.UpdateAsync(spacingProduct, spacingProductEdit);
Check(spacingProduct.Name == "Produs editat cu spatii", "Product editing removes exterior and repeated spaces from its name");
await spacingRepository.CreateCategoryAsync("  Categorie   cu   spatii  ");
await spacingRepository.RenameCategoryAsync("Categorie cu spatii", "  Categorie   editata  ", "Verificare normalizare");
var spacingSubcategory = await spacingRepository.CreateSubcategoryAsync("Categorie editata", "  Subcategorie   cu   spatii  ");
spacingSubcategory = await spacingRepository.UpdateSubcategoryAsync(spacingSubcategory,
    "  Subcategorie   editata  ", "Categorie editata", "Verificare normalizare");
Check(spacingSubcategory == new ProductGroup("Categorie editata", "Subcategorie editata"),
    "Category and subcategory creation and editing remove exterior and repeated spaces");
await Rejected(() => repository.CreateAsync(new ProductInput()), "Missing fields are rejected");
await Rejected(() => repository.CreateAsync(new ProductInput { Name = "  ", Category = "A", Subcategory = "B" }), "Whitespace name is rejected by repository");
await Rejected(() => repository.CreateAsync(new ProductInput { Name = new string('a', 101), Category = "A", Subcategory = "B" }), "Oversize product name is rejected");
var invalid = ProductEdit(created); invalid.Description = new string('a', 1001);
await Rejected(() => repository.UpdateAsync(created, invalid), "Oversize description is rejected");
Check(created.Quantity == 0, "A new product starts with stock 0");
invalid = ProductInput.From(created); invalid.Description = "Editare nouă";
await Rejected(() => repository.UpdateAsync(created, invalid), "Product edit requires a reason");
var metadataWithoutReason = ProductInput.From(created); metadataWithoutReason.Description = "Editare fără motiv";
await Rejected(() => repository.UpdateAsync(created, metadataWithoutReason), "Every product edit requires a reason");
invalid.Reason = "Inventariere";
var updated = await repository.UpdateAsync(created, invalid);
Check(updated.Quantity == 0 && updated.Version == 1, "Product edit keeps the stock and saves with a new version");
await Rejected(() => repository.UpdateAsync(created, ProductEdit(created)), "Stale edit cannot overwrite changes");
await Rejected(() => repository.DeleteAsync(created, "Test automat"), "Stale delete cannot remove changed product");
var stockedProduct = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
await Rejected(() => repository.DeleteAsync(stockedProduct, "Test automat"), "Nonzero stock prevents deletion");
var empty = updated;
try { ProductRules.CheckDelete(empty, true); throw new Exception("Associated records ignored"); }
catch (ProductOperationException) { Check(true, "Related stock movements or images prevent deletion"); }
await Rejected(() => repository.DeleteAsync(empty, " "), "Product deletion requires a reason");
Check((await repository.GetProductsAsync()).Any(product => product.Id == empty.Id), "Rejected product deletion preserves the object");
await repository.DeleteAsync(empty, "Test automat");
Check(!(await repository.GetProductsAsync()).Any(p => p.Id == empty.Id), "Zero-stock product can be deleted");
await Rejected(() => repository.UpdateAsync(empty, ProductEdit(empty)), "Editing a deleted product is rejected");
await Rejected(() => repository.DeleteAsync(empty, "Test automat"), "Repeated deletion is rejected");
var next = await repository.CreateAsync(ProductInput.From(created));
Check(next.Id > created.Id, "Deleted IDs are not reused");
var legacy = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
var legacyEdit = ProductEdit(legacy); legacyEdit.Description = "Descriere corectată";
Check((await repository.UpdateAsync(legacy, legacyEdit)).Quantity == -2, "Legacy negative stock can be preserved during metadata edits");
var stockSnapshot = (await repository.GetProductsAsync()).Single(p => p.Id == 8);
var stockDifferenceEdit = ProductEdit(stockSnapshot); stockDifferenceEdit.Description = "Altă descriere";
Check((await repository.UpdateAsync(stockSnapshot with { Quantity = 99 }, stockDifferenceEdit)).Quantity == -2,
    "A stock difference in the edit snapshot neither blocks the edit nor overwrites the stored stock");
ProductRules.CheckCurrent(stockSnapshot, stockSnapshot with { Quantity = 7 });
await Rejected(() => Task.Run(() => ProductRules.CheckCurrent(stockSnapshot, stockSnapshot with { Name = "Alt cod" })),
    "Concurrency check still rejects a changed product code");
var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
Check(ProductImageRules.DetectContentType(imageBytes) == "image/png", "Product image format is detected from file content");
try { ProductImageRules.DetectContentType("<svg></svg>"u8); throw new Exception("Unsafe image format accepted"); }
catch (ProductImageException) { Check(true, "Unsupported image formats are rejected"); }
var demoImages = new DemoProductImageStore();
await demoImages.SaveAsync(created.Id, new ProductImageData(imageBytes, "application/octet-stream", "test.png"));
Check((await demoImages.GetAsync(created.Id))?.ContentType == "image/png", "Product image is stored with its detected media type");
await demoImages.DeleteAsync(created.Id);
Check(!await demoImages.ExistsAsync(created.Id), "Product image is removed with the product");
try { await repository.CreateAsync(ProductInput.From(next), cancelled.Token); throw new Exception("Create ignored cancellation"); }
catch (OperationCanceledException) { Check((await repository.GetProductsAsync()).Count == 13, "Cancelled create does not modify catalogue"); }
try { await repository.UpdateAsync(next, ProductEdit(next), cancelled.Token); throw new Exception("Update ignored cancellation"); }
catch (OperationCanceledException) { Check(true, "Cancelled update is respected"); }
try { await repository.DeleteAsync(next, "Test automat", cancelled.Token); throw new Exception("Delete ignored cancellation"); }
catch (OperationCanceledException) { Check((await repository.GetProductsAsync()).Contains(next), "Cancelled delete leaves product unchanged"); }
var stale = next;
var change = ProductEdit(next); change.Name = "Altă denumire";
var changed = await repository.UpdateAsync(next, change);
await repository.UpdateAsync(changed, ProductEdit(next));
await Rejected(() => repository.UpdateAsync(stale, ProductEdit(stale)), "Version detects a change even when values were restored");
var unconfigured = new MariaProductRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
await Rejected(() => unconfigured.CreateAsync(ProductInput.From(next)), "A missing database password blocks writes before opening a SQL connection");
var wrongDatabase = new MariaProductRepository(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Name"] = "stocesp", ["Database:Password"] = "x" }).Build());
await Rejected(() => wrongDatabase.CreateAsync(ProductInput.From(next)), "Writes to the original database are rejected before connecting");

async Task RejectedUser(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (UserOperationException) { Check(true, message); return; }
    throw new Exception("Expected user rejection: " + message);
}
async Task RejectedAccess(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (AccessDeniedException) { Check(true, message); return; }
    throw new Exception("Expected access rejection: " + message);
}
var demoUsers = new DemoUserRepository(new TestAccessControl(true, "administrator-extern"));
Check((await demoUsers.GetUsersAsync()).Count == 2, "Demo user catalogue starts with administrator and limited user");
Check((await demoUsers.AuthenticateAsync("administrator.demo", "admin-demo-123")).User?.Role == AccessRoles.Administrator, "Demo administrator can authenticate");
Check((await demoUsers.AuthenticateAsync("utilizator.demo", "utilizator-demo-123")).User?.Role == AccessRoles.LimitedUser, "Demo product user can authenticate");
Check((await demoUsers.AuthenticateAsync("utilizator.demo", "parola-gresita")).Status == AuthenticationStatus.InvalidCredentials, "Demo authentication rejects a wrong password");
var newUser = await demoUsers.CreateAsync(new WebUserInput { Username = "  Ión.Popescu  ", DisplayName = "  Ión   Popéscu  ", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" });
Check(newUser.Username == "Ion.Popescu" && newUser.DisplayName == "Ion Popescu" && newUser.Role == AccessRoles.LimitedUser && newUser.IsActive, "User values keep letter case and remove diacritics");
Check((await demoUsers.AuthenticateAsync("ion.popescu", "parola-demo-123")).User?.Id == newUser.Id, "New demo user can authenticate");
await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "ION.POPESCU", DisplayName = "Duplicat", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" }), "Usernames are unique without case sensitivity");
await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "ab", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "parola-demo-123" }), "Short username is rejected");
await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "user-valid", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "scurta" }), "Short password is rejected");
await RejectedUser(() => demoUsers.CreateAsync(new WebUserInput { Username = "user-sapte", DisplayName = "Invalid", Role = AccessRoles.LimitedUser, Password = "1234567" }), "A 7-character password is rejected");
var eightCharUser = await demoUsers.CreateAsync(new WebUserInput { Username = "user-opt", DisplayName = "Opt Caractere", Role = AccessRoles.LimitedUser, Password = "12345678" });
Check(WebUserInput.MinimumPasswordLength == 8 && (await demoUsers.AuthenticateAsync("user-opt", "12345678")).User?.Id == eightCharUser.Id, "A password of 8 characters is accepted (the minimum)");
Check(new WebUserInput { Password = "abcdefgh", PasswordConfirmation = "abcdefgh" }.PasswordConfirmationError() is null &&
      new WebUserInput { Password = "abcdefgh", PasswordConfirmation = "abcdefgH" }.PasswordConfirmationError() is { } mismatch && mismatch.Contains("nu coincide") &&
      new WebUserInput { Password = "abcdefgh" }.PasswordConfirmationError() is not null && new WebUserInput().PasswordConfirmationError() is null,
    "The password must be repeated identically in the form; an unchanged (empty) password on edit needs no confirmation");
var userEdit = UserEdit(newUser); userEdit.DisplayName = "  Ion   Popescu   Editat  "; userEdit.IsActive = false;
var userWithoutReason = WebUserInput.From(newUser); userWithoutReason.DisplayName = "Editare fără motiv";
await RejectedUser(() => demoUsers.UpdateAsync(newUser, userWithoutReason), "User edits require a reason");
var inactiveUser = await demoUsers.UpdateAsync(newUser, userEdit);
Check(!inactiveUser.IsActive && inactiveUser.Version == 1 && inactiveUser.DisplayName == "Ion Popescu Editat",
    "User can be edited, space-normalized and deactivated without changing password");
Check((await demoUsers.AuthenticateAsync("ion.popescu", "parola-demo-123")).Status == AuthenticationStatus.Inactive, "Inactive demo user receives inactive status");
await RejectedUser(() => demoUsers.UpdateAsync(newUser, UserEdit(newUser)), "Stale user edit is rejected");
await RejectedUser(() => demoUsers.DeleteAsync(inactiveUser, "  "), "User deletion requires a reason");
Check((await demoUsers.GetUsersAsync()).Any(user => user.Id == inactiveUser.Id), "Rejected user deletion preserves the account");
await demoUsers.DeleteAsync(inactiveUser, "Test automat");
Check(!(await demoUsers.GetUsersAsync()).Any(user => user.Id == inactiveUser.Id), "User can be deleted");
var onlyAdmin = (await demoUsers.GetUsersAsync()).Single(user => user.Role == AccessRoles.Administrator);
var demotion = UserEdit(onlyAdmin); demotion.Role = AccessRoles.LimitedUser;
await RejectedUser(() => demoUsers.UpdateAsync(onlyAdmin, demotion), "Last active administrator cannot be demoted");
await RejectedUser(() => demoUsers.DeleteAsync(onlyAdmin, "Test automat"), "Last active administrator cannot be deleted");
var secondAdmin = await demoUsers.CreateAsync(new WebUserInput { Username = "admin.doi", DisplayName = "Administrator Doi", Role = AccessRoles.Administrator, Password = "parola-admin-123" });
var demoted = await demoUsers.UpdateAsync(onlyAdmin, demotion);
Check(demoted.Role == AccessRoles.LimitedUser && secondAdmin.Role == AccessRoles.Administrator, "Administrator can be demoted when another active administrator remains");
var selfUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"));
var self = (await selfUsers.GetUsersAsync()).Single(user => user.Username == "administrator.demo");
var selfDemotion = UserEdit(self); selfDemotion.Role = AccessRoles.LimitedUser;
await RejectedUser(() => selfUsers.UpdateAsync(self, selfDemotion), "Administrator cannot remove own administrator access");
await RejectedUser(() => selfUsers.DeleteAsync(self, "Test automat"), "Administrator cannot delete own account");
var limitedAccess = new TestAccessControl(false, "utilizator.demo");
await RejectedAccess(() => new DemoUserRepository(limitedAccess).GetUsersAsync(), "Limited user cannot list managed user accounts");
var limitedProducts = new DemoProductRepository(limitedAccess);
Check((await CreateProductAsync(limitedProducts, ProductInput.From(next))).Id == 13, "Limited user can operate products");

var auditTrail = new TestAuditTrail();
var auditedProducts = new DemoProductRepository(limitedAccess, auditTrail);
await auditedProducts.CreateCategoryAsync("Test");
await auditedProducts.CreateSubcategoryAsync("Test", "Audit");
var auditedProduct = await auditedProducts.CreateAsync(new ProductInput { Name = "Produs auditat", Category = "Test", Subcategory = "Audit" });
Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Category && entry.Action == AuditActions.Create) == 1 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Subcategory && entry.Action == AuditActions.Create) == 1 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Create && entry.EntityId == auditedProduct.Id.ToString()) == 1,
    "Explicit group creation and product creation each record one audit event");
var auditedProductSameGroup = await auditedProducts.CreateAsync(new ProductInput { Name = "Al doilea produs auditat", Category = "test", Subcategory = "audit" });
Check(auditTrail.Entries.Count(entry => entry.EntityType is AuditEntities.Category or AuditEntities.Subcategory) == 2 &&
      auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Create && entry.EntityId == auditedProductSameGroup.Id.ToString()) == 1,
    "Reusing an existing group does not duplicate category or subcategory events");
await auditedProducts.DeleteAsync(auditedProduct, "Curățare test");
var auditedUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"), new DemoUserStore(), auditTrail);
var auditedUser = await auditedUsers.CreateAsync(new WebUserInput { Username = "audit.user", DisplayName = "Audit User", Role = AccessRoles.LimitedUser, Password = "secret-demo-123" });
Check(auditTrail.Entries.Count == 6 && auditTrail.Entries.All(entry => entry.ActorUsername.Length > 0), "Product, group and user changes are written once to the audit trail");
Check(auditTrail.Entries.Any(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Delete), "Audit trail identifies entity and operation");
Check(auditTrail.Entries.All(entry => !entry.Details.Contains("secret-demo-123", StringComparison.Ordinal)), "Audit trail never records passwords");
var deletedProductEvent = auditTrail.Entries.Single(entry => entry.EntityType == AuditEntities.Product && entry.Action == AuditActions.Delete);
Check(deletedProductEvent.EntityId == auditedProduct.Id.ToString() &&
      deletedProductEvent.Details.Contains(auditedProduct.Name, StringComparison.Ordinal) &&
      deletedProductEvent.Details.Contains(auditedProduct.Category, StringComparison.Ordinal) &&
      deletedProductEvent.Motif == "Curatare test" &&
      deletedProductEvent.ArchiveOperationId is not null,
    "Delete audit details retain object identification and archive operation after removal");

var sessionAuditTrail = new TestAuditTrail();
await AuditRecorder.RecordSessionAsync(sessionAuditTrail, "administrator.demo", AccessRoles.Administrator, true, default);
await AuditRecorder.RecordSessionAsync(sessionAuditTrail, "administrator.demo", AccessRoles.Administrator, false, default);
Check(sessionAuditTrail.Entries.Count == 2 &&
      sessionAuditTrail.Entries.Count(entry => entry.Action == AuditActions.Login) == 1 &&
      sessionAuditTrail.Entries.Count(entry => entry.Action == AuditActions.Logout) == 1,
    "Successful login and confirmed logout are each recorded once");

var productAuditLink = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "admin", AccessRoles.Administrator,
    AuditEntities.Product, AuditActions.Create, "#12 · Produs", "Denumire: Produs", EntityId: "12");
Check(AuditNavigation.DisplayTarget(productAuditLink) == "Produs" &&
      AuditNavigation.DisplayTarget(productAuditLink with { Target = "Cod #7" }) == "Cod #7" &&
      AuditNavigation.DisplayTarget(productAuditLink with { Target = "#AB · Cod" }) == "#AB · Cod" &&
      AuditNavigation.DisplayTarget(productAuditLink with { EntityType = AuditEntities.Beneficiary }) == "#12 · Produs",
    "Legacy product audit targets are displayed without the internal identifier");
var beneficiaryAuditLink = productAuditLink with
    { EntityType = AuditEntities.Beneficiary, Action = AuditActions.Edit, EntityId = "7" };
var userAuditLink = productAuditLink with
    { EntityType = AuditEntities.User, Action = AuditActions.Create, EntityId = "3" };
Check(AuditNavigation.TargetUrl(productAuditLink) == "/produse/12" &&
      AuditNavigation.TargetUrl(beneficiaryAuditLink) == "/beneficiari/7" &&
      AuditNavigation.TargetUrl(userAuditLink) == "/utilizatori/3" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Project, EntityId = "4" }) == "/proiecte/4",
    "Product, beneficiary, user and project audit targets open the read-only object page from entity type and stable identifier");
Check(AuditNavigation.TargetUrl(productAuditLink)!.Contains("edit", StringComparison.Ordinal) == false &&
      AuditNavigation.TargetUrl(userAuditLink)!.Contains('?') == false,
    "Audit target links never carry an edit trigger");
Check(AuditNavigation.TargetUrl(productAuditLink with { Target = "/proiecte/99" }) == "/produse/12" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "12abc", Target = "#99 · Produs" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "-3" }) is null,
    "Audit routes ignore the display text of the target and require a valid stable identifier");
Check(AuditNavigation.TargetUrl(userAuditLink with { Action = AuditActions.Login, EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(userAuditLink with { Action = AuditActions.Logout, EntityId = "" }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Subcategory }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.ProjectObservationFile }) is null,
    "Session events and entity types without a page are shown as text");
var removedAt = productAuditLink.TimestampUtc.AddMinutes(5);
var productDeleteAudit = productAuditLink with { Id = Guid.NewGuid(), TimestampUtc = removedAt, Action = AuditActions.Delete };
var removals = AuditNavigation.RemovalTimes([productAuditLink, productDeleteAudit, beneficiaryAuditLink]);
Check(AuditNavigation.TargetUrl(productAuditLink, removals) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { Action = AuditActions.Edit, TimestampUtc = removedAt.AddMinutes(1) }, removals) == "/produse/12" &&
      AuditNavigation.TargetUrl(beneficiaryAuditLink, removals) == "/beneficiari/7" &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "13" }, removals) == "/produse/13" &&
      AuditNavigation.TargetUrl(userAuditLink with { EntityId = "12" }, removals) == "/utilizatori/12",
    "Objects deleted after the event get no link, while other objects, other types and later events keep theirs");

Check(AuditListState.Default.Url() == "/jurnal" &&
      AuditListState.From("  ", null, null, null, null, null, null) == AuditListState.Default &&
      AuditListState.From("cod x", "Produs", "Editare", "admin", "2026-09-25", 50, 3).Url() ==
        "/jurnal?q=cod%20x&tip=Produs&operatie=Editare&operator=admin&data=2026-09-25&pe-pagina=50&pagina=3",
    "The journal state is written to the address only for values that differ from the defaults");
var restoredJournal = AuditListState.From("a&b=c", "Beneficiar", "Adăugare", "op", "2026-09-25", 20, 2);
Check(restoredJournal.Query == "a&b=c" && restoredJournal.PageSize == 20 && restoredJournal.Page == 2 &&
      AuditListState.DateLabel("2026-09-25") == "25.09.2026" &&
      new Uri("http://localhost" + restoredJournal.Url()).Query.Contains("q=a%26b%3Dc", StringComparison.Ordinal),
    "The journal state round-trips through the address and keeps special characters escaped");
Check(AuditListState.From(null, null, null, null, "25/09/2026", 7, 0) == AuditListState.Default &&
      AuditListState.From(null, null, null, null, "2026-13-40", 0, -4).PageSize == 0 &&
      AuditListState.From(null, null, null, null, "2026-13-40", 0, -4).DateKey is null &&
      AuditListState.From(null, null, null, null, null, null, -4).Page == 1,
    "Malformed journal address values fall back to the defaults");
Check(AuditNavigation.TargetUrl(productAuditLink with { Action = AuditActions.Delete }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityType = AuditEntities.Category }) is null &&
      AuditNavigation.TargetUrl(productAuditLink with { EntityId = "invalid" }) is null,
    "Deleted objects and entities without edit pages do not receive invalid audit links");

var auditRulesTrail = new TestAuditTrail();
var auditRulesProducts = new DemoProductRepository(limitedAccess, auditRulesTrail);
var auditRulesProduct = await CreateProductAsync(auditRulesProducts, new ProductInput
    { Name = "Produs înainte", Category = "Test", Subcategory = "Reguli", Description = "Descriere înainte" });
var auditRulesInput = ProductInput.From(auditRulesProduct);
auditRulesInput.Name = "Produs după";
auditRulesInput.Description = "Descriere după";
auditRulesInput.Reason = "Inventariere";
var auditRulesUpdated = await auditRulesProducts.UpdateAsync(auditRulesProduct, auditRulesInput);
var productEditEvent = auditRulesTrail.Entries.Single(entry => entry.Action == AuditActions.Edit);
Check(productEditEvent.EntityId == auditRulesUpdated.Id.ToString() && productEditEvent.Target.Contains(auditRulesUpdated.Name, StringComparison.Ordinal),
    "Edit audit target uses the saved object and its stable identifier");
Check(productEditEvent.Target == "Produs dupa" && productEditEvent.EntityId == auditRulesProduct.Id.ToString(),
    "Product audit target shows only the product code while the entity id keeps the internal identifier");
Check(productEditEvent.Details.Contains("Cod produs: Produs inainte → Produs dupa", StringComparison.Ordinal) &&
      productEditEvent.Details.Contains("Descriere: Descriere inainte → Descriere dupa", StringComparison.Ordinal) &&
      !productEditEvent.Details.Contains("Cantitate", StringComparison.Ordinal) &&
      !productEditEvent.Details.Contains("Categorie", StringComparison.Ordinal) && productEditEvent.Motif == "Inventariere",
    "Edit audit details contain only changed before and after values, with the reason stored separately");
var successfulAuditCount = auditRulesTrail.Entries.Count;
try
{
    await auditRulesProducts.CreateAsync(new ProductInput
        { Name = "PRODUS DUPA", Category = "Alta", Subcategory = "Alta" });
    throw new Exception("Duplicate audited product accepted");
}
catch (ProductOperationException)
{
    Check(auditRulesTrail.Entries.Count == successfulAuditCount, "Rejected operations do not produce successful audit events");
}

var sensitiveAuditTrail = new TestAuditTrail();
var sensitiveUsers = new DemoUserRepository(new TestAccessControl(true, "administrator.demo"), new DemoUserStore(), sensitiveAuditTrail);
var sensitiveUser = await sensitiveUsers.CreateAsync(new WebUserInput
    { Username = "audit.sensibil", DisplayName = "Audit Sensibil", Role = AccessRoles.LimitedUser, Password = "secret-initial-123" });
var sensitiveInput = UserEdit(sensitiveUser, "Actualizare cont pentru test");
sensitiveInput.DisplayName = "Audit Actualizat";
sensitiveInput.Password = "secret-schimbat-123";
await sensitiveUsers.UpdateAsync(sensitiveUser, sensitiveInput);
var sensitiveEditEvent = sensitiveAuditTrail.Entries.Single(entry => entry.Action == AuditActions.Edit);
Check(sensitiveEditEvent.Details.Contains("Nume afișat: Audit Sensibil → Audit Actualizat", StringComparison.Ordinal) &&
      !sensitiveEditEvent.Details.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
      !sensitiveEditEvent.Details.Contains("parol", StringComparison.OrdinalIgnoreCase) &&
      sensitiveEditEvent.Motif == "Actualizare cont pentru test",
    "User audit changes exclude passwords and password metadata");

var auditContractPath = Path.Combine(Path.GetTempPath(), $"blazorstoc-audit-{Guid.NewGuid():N}.jsonl");
try
{
    var legacyId = Guid.NewGuid();
    var legacyJson = $$"""{"id":"{{legacyId}}","timestampUtc":"2026-09-16T10:49:17.418572Z","actorUsername":"administrator.demo","actorRole":"Administrator","entityType":"Produs","action":"Modificare","target":"#1 · Produs vechi","details":"Denumire actualizată."}""";
    await File.WriteAllTextAsync(auditContractPath, legacyJson + Environment.NewLine);
    var auditConfiguration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["App:AuditPath"] = auditContractPath }).Build();
    var fileAuditTrail = new FileAuditTrail(new TestWebHostEnvironment(Path.GetDirectoryName(auditContractPath)!),
        auditConfiguration, NullLogger<FileAuditTrail>.Instance);

    var legacyEvents = await fileAuditTrail.GetEventsAsync();
    Check(legacyEvents.Count == 1 && legacyEvents[0].Action == AuditActions.Edit &&
          legacyEvents[0].Motif == string.Empty && legacyEvents[0].EntityId == string.Empty,
        "Legacy audit events remain readable and use the current audit contract");
    Check(legacyEvents[0].TimestampUtc.Kind == DateTimeKind.Utc, "Legacy audit timestamps are normalized to UTC");

    await fileAuditTrail.RecordAsync(new AuditWrite("administrator.demo", AccessRoles.Administrator,
        AuditEntities.Product, "Modificare", "#42 · Produs", "Denumire: veche → nouă.", "Corecție", "42"));
    var currentEvents = await fileAuditTrail.GetEventsAsync();
    Check(currentEvents[0].Action == AuditActions.Edit && currentEvents[0].Motif == "Corecție" &&
          currentEvents[0].EntityId == "42" && currentEvents[0].TimestampUtc.Kind == DateTimeKind.Utc,
        "New audit events persist Editare, Motif, entity identifier and UTC timestamp");
}
finally
{
    if (File.Exists(auditContractPath)) File.Delete(auditContractPath);
}

var demoBeneficiaries = new DemoBeneficiaryRepository(limitedAccess, null, auditTrail);
Check((await demoBeneficiaries.GetBeneficiariesAsync()).Count == 3, "Demo beneficiary register is available to limited users");
var beneficiary = await demoBeneficiaries.CreateAsync(LegalInput("  Beneficiár   nou   SRL  ", "  ro12345678  "));
Check(beneficiary.Name == "Beneficiar nou SRL" && beneficiary.Cui == "ro12345678", "Beneficiary values keep letter case and remove diacritics");
Check(BeneficiarySearch.Filter(await demoBeneficiaries.GetBeneficiariesAsync(), "12345678").Single().Id == beneficiary.Id, "Beneficiaries can be searched by CUI");
try { await demoBeneficiaries.CreateAsync(LegalInput("Duplicat", "RO12345678")); throw new Exception("Duplicate CUI accepted"); }
catch (BeneficiaryOperationException exception)
{
    Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage("Beneficiar nou SRL"),
        "Duplicate beneficiary CUI is rejected and names the stored beneficiary, not the typed name");
}
var editedDemoBeneficiary = (await demoBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Cui == "RO10000001");
var duplicateCuiEdit = BeneficiaryEdit(editedDemoBeneficiary); duplicateCuiEdit.Cui = " ro12345678 ";
try { await demoBeneficiaries.UpdateAsync(editedDemoBeneficiary, duplicateCuiEdit); throw new Exception("Duplicate CUI accepted on edit"); }
catch (BeneficiaryOperationException exception)
{
    Check(exception.Message == BeneficiaryRules.DuplicateCuiMessage("Beneficiar nou SRL") && duplicateCuiEdit.Cui == " ro12345678 " &&
          (await demoBeneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == editedDemoBeneficiary.Id) == editedDemoBeneficiary,
        "Editing a beneficiary to an existing CUI names the owner, keeps the form values and changes nothing");
}
var unchangedCuiEdit = BeneficiaryEdit(editedDemoBeneficiary); unchangedCuiEdit.Name = "Construct Demo Actualizat SRL";
var unchangedCuiSaved = await demoBeneficiaries.UpdateAsync(editedDemoBeneficiary, unchangedCuiEdit);
Check(unchangedCuiSaved.Cui == "RO10000001" && unchangedCuiSaved.Name == unchangedCuiEdit.Name,
    "Editing a beneficiary without changing its CUI never reports itself as the duplicate");
Check(BeneficiaryRules.DuplicateCuiMessage(null) == "Există deja un beneficiar cu acest CUI." &&
      BeneficiaryRules.DuplicateCuiMessage("  ") == "Există deja un beneficiar cu acest CUI.",
    "Duplicate CUI message without a known owner falls back to the plain wording");
try { await demoBeneficiaries.CreateAsync(LegalInput("BENEFICIÁR NOU SRL", "RO87654321")); throw new Exception("Duplicate beneficiary name accepted"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("ro12345678", StringComparison.Ordinal), "Duplicate beneficiary name is rejected and reports its CUI"); }
try { await demoBeneficiaries.CreateAsync(LegalInput("CUI invalid", "RO-ABC")); throw new Exception("Invalid CUI accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Invalid beneficiary CUI is rejected"); }
var individual = await demoBeneficiaries.CreateAsync(new BeneficiaryInput
    { Kind = BeneficiaryKinds.Individual, Name = "  Ion   Popescu ", Address = " Strada  Păcii 5 ", Phone = "0744 123.456", Cui = "RO999", RegistryNumber = "J1/2/3" });
Check(individual.Kind == BeneficiaryKinds.Individual && individual.Name == "Ion Popescu" && individual.Address == "Strada Pacii 5" &&
      individual.Phone == "0744123456" && individual.Cui == "" && individual.RegistryNumber == "" && !individual.AnafVerified,
    "Individual beneficiary is normalized (spaces, diacritics, phone digits) and company fields are dropped");
foreach (var (broken, label) in new (BeneficiaryInput, string)[]
         {
             (new() { Kind = BeneficiaryKinds.Individual, Name = "", Address = "A", Phone = "0744123456" }, "full name"),
             (new() { Kind = BeneficiaryKinds.Individual, Name = "Ana Test", Address = "", Phone = "0744123456" }, "address"),
             (new() { Kind = BeneficiaryKinds.Individual, Name = "Ana Test", Address = "A", Phone = "" }, "phone"),
             (new() { Kind = BeneficiaryKinds.Individual, Name = "Ana Test", Address = "A", Phone = "12ab" }, "phone format"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "Firma", Cui = "RO123456", Address = "", Phone = "0744123456" }, "legal address"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "Firma", Cui = "RO123456", Address = "A", Phone = "" }, "legal phone"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "Firma", Cui = "", Address = "A", Phone = "0744123456" }, "legal CUI"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "", Cui = "RO123456", Address = "A", Phone = "0744123456" }, "legal name"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "Firma", Cui = "RO123456", Address = "A", Phone = "0744123456", CaenCode = "12" }, "CAEN"),
             (new() { Kind = BeneficiaryKinds.Legal, Name = "Firma", Cui = "RO123456", Address = "A", Phone = "0744123456", PostalCode = "12" }, "postal code"),
             (new() { Kind = "XX", Name = "Firma", Address = "A", Phone = "0744123456" }, "kind")
         })
{
    try { await demoBeneficiaries.CreateAsync(broken); throw new Exception("Incomplete beneficiary accepted: " + label); }
    catch (BeneficiaryOperationException) { Check(true, "Required/invalid beneficiary field rejected: " + label); }
}
try { await demoBeneficiaries.CreateAsync(new BeneficiaryInput { Kind = BeneficiaryKinds.Individual, Name = "ion POPESCU", Address = "B", Phone = "0755123456" }); throw new Exception("Duplicate individual accepted"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("persoană fizică") && exception.Message.Contains("Ion Popescu"), "A duplicate individual is detected by full name"); }
var legalFull = await demoBeneficiaries.CreateAsync(new BeneficiaryInput
    { Kind = BeneficiaryKinds.Legal, Name = " Firma  Completă SRL ", Cui = " RO 55 44 33 ", Address = "Str. Test 2", Phone = "+40 721 000 222",
      RegistryNumber = " j40/12/2020 ", PostalCode = "012345", CaenCode = "4321", AnafVerified = true });
Check(legalFull.Cui == "RO554433" && legalFull.Phone == "+40721000222" && legalFull.RegistryNumber == "J40/12/2020" && legalFull.PostalCode == "012345" &&
      legalFull.CaenCode == "4321" && legalFull.AnafVerified && legalFull.Name == "Firma Completa SRL",
    "Legal-person beneficiary keeps and normalizes the company fields and the ANAF flag");
Check(BeneficiarySearch.Filter(await demoBeneficiaries.GetBeneficiariesAsync(), "0744 123456").Single().Id == individual.Id,
    "Beneficiaries can be searched by phone number");

Check(ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule&subcategorie=Găurire", "/produse?subcategorie=G%C4%83urire&categorie=scule") &&
      ProductMenuSelection.IsSameSelection("http://localhost:5082/produse", "/produse/") &&
      ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?edit=3", "/produse"),
    "The leave guard treats the same category and subcategory as the same selection");
Check(!ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse", "/produse?categorie=Scule") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse?categorie=Scule&subcategorie=Găurire") &&
      !ProductMenuSelection.IsSameSelection("http://localhost:5082/produse?categorie=Scule", "/produse?categorie=Altele"),
    "The leave guard detects another category, another subcategory or \"Toate produsele\"");
Check(ReturnNavigation.Safe("/produse/3/miscari") == "/produse/3/miscari" && ReturnNavigation.Safe(" /produse/3 ") == "/produse/3" &&
      ReturnNavigation.Safe(null) is null && ReturnNavigation.Safe("") is null && ReturnNavigation.Safe("produse/3") is null &&
      ReturnNavigation.Safe("//evil.example/x") is null && ReturnNavigation.Safe("https://evil.example/x") is null &&
      ReturnNavigation.Safe("/\\evil.example") is null && ReturnNavigation.Safe("/a\nb") is null,
    "The return address accepts only same-site paths");
Check(ReturnNavigation.EditUrl(3, "/produse/3/miscari") == "/produse?edit=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
      ReturnNavigation.DeleteUrl(3, "/produse/3/miscari") == "/produse?sterge=3&inapoi=%2Fproduse%2F3%2Fmiscari" &&
      ReturnNavigation.EditUrl(3, "https://evil.example") == "/produse?edit=3",
    "Edit and delete links from the product page carry the page to return to");
var saveSummary = SaveSummary.Changed(new("Nume", "Alfa", "Alfa"), new("Descriere", "", "Text nou"), new("Cod", " A1 ", "B2"), new("Lung", "x", new string('y', 500)));
Check(saveSummary.Select(change => change.Field).SequenceEqual(["Descriere", "Cod", "Lung"]) &&
      saveSummary[0].Before == SaveSummary.Empty && saveSummary[1].Before == "A1" && saveSummary[2].After.Length == SaveSummary.MaximumValueLength + 1,
    "Save summary lists only changed fields, shows empty values and shortens very long ones");
Check(SaveSummary.Changed(new AuditChange("Nume", "Alfa", "Alfa")).Count == 0, "Save summary is empty when no field changed");
var beneficiaryInput = BeneficiaryEdit(beneficiary); beneficiaryInput.Name = "  Beneficiar   actualizat   SRL  ";
var beneficiaryWithoutReason = BeneficiaryInput.From(beneficiary); beneficiaryWithoutReason.Name = "Fără motiv";
try { await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryWithoutReason); throw new Exception("Beneficiary edit without reason accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Beneficiary edits require a reason"); }
var updatedBeneficiary = await demoBeneficiaries.UpdateAsync(beneficiary, beneficiaryInput);
Check(updatedBeneficiary.Version == 1 && updatedBeneficiary.Name == "Beneficiar actualizat SRL", "Beneficiary can be edited with optimistic concurrency");
try { await demoBeneficiaries.UpdateAsync(beneficiary, BeneficiaryEdit(beneficiary)); throw new Exception("Stale beneficiary accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Stale beneficiary edit is rejected"); }
try { await demoBeneficiaries.DeleteAsync(updatedBeneficiary, " "); throw new Exception("Beneficiary deletion without reason accepted"); }
catch (BeneficiaryOperationException) { Check(true, "Beneficiary deletion requires a reason"); }
Check((await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Rejected beneficiary deletion preserves the object");
await demoBeneficiaries.DeleteAsync(updatedBeneficiary, "Test automat");
Check(!(await demoBeneficiaries.GetBeneficiariesAsync()).Any(item => item.Id == updatedBeneficiary.Id), "Beneficiary can be deleted");
Check(auditTrail.Entries.Count(entry => entry.EntityType == AuditEntities.Beneficiary) == 6, "Beneficiary changes are written to the audit trail");


void ProjectRejected(Action operation, string message)
{
    try { operation(); }
    catch (ProjectOperationException) { Check(true, message); return; }
    throw new Exception("Expected project rejection: " + message);
}
var projectNow = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
var projectInput = new ProjectInput { BeneficiaryId = 1, Name = "  Hală   producție  ", Observations = "  Montaj în două etape  " }.Validated();
Check(projectInput.Name == "Hala productie" && projectInput.Observations == "Montaj in doua etape",
    "Project name and general observations use the existing trimming, space and diacritic rules");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = "   " }.Validated(), "Project name is required");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 0, Name = "Proiect" }.Validated(), "Project requires a beneficiary");
ProjectRejected(() => new ProjectInput { BeneficiaryId = 1, Name = new string('P', 201) }.Validated(), "Project name is limited to 200 characters");
var project = ProjectRules.Create(10, projectInput, projectNow);
Check(project.Version == 0 && project.CreatedAtUtc == projectNow && project.UpdatedAtUtc == projectNow &&
      project.CreatedAtUtc.Kind == DateTimeKind.Utc, "New projects start at version 0 with UTC creation and update timestamps");
try { ProjectRules.Create(11, projectInput, DateTime.SpecifyKind(projectNow, DateTimeKind.Local)); throw new Exception("Local project timestamp accepted"); }
catch (ArgumentException) { Check(true, "Project timestamps must be UTC"); }
var existingProjects = new[] { project, ProjectRules.Create(12, new ProjectInput { BeneficiaryId = 2, Name = "Alt proiect" }.Validated(), projectNow) };
ProjectRejected(() => ProjectRules.EnsureUniqueName(existingProjects, 1, "  HALĂ  PRODUCȚIE ", null, "Construct Demo SRL"),
    "Project names are unique per beneficiary regardless of case, diacritics and spacing");
ProjectRules.EnsureUniqueName(existingProjects, 2, "Hala productie", null, "Atelier Tehnic SRL");
Check(true, "The same project name is allowed for a different beneficiary");
ProjectRules.EnsureUniqueName(existingProjects, 1, "Hala productie", project.Id, "Construct Demo SRL");
Check(true, "A project keeps its own name when edited");
try { ProjectRules.EnsureUniqueName(existingProjects, 1, "hala productie", null, "Construct Demo SRL"); throw new Exception("Duplicate project name accepted"); }
catch (ProjectOperationException exception)
{
    Check(exception.Message.Contains("Construct Demo SRL", StringComparison.Ordinal) && exception.Message.Contains("Hala productie", StringComparison.Ordinal),
        "Duplicate project message names the beneficiary and the existing project");
}
Check(ProjectRules.NormalizedName("  hală   PRODUCȚIE ") == ProjectRules.NormalizedName("Hala productie"),
    "Normalized project name key is stable for the per-beneficiary unique index");
var projectEdit = ProjectInput.From(project); projectEdit.Name = "Hala noua";
ProjectRejected(() => projectEdit.Validated(true), "Project edits require a reason");
projectEdit.Reason = "Corectie denumire";
var editedProject = ProjectRules.Edited(project, projectEdit.Validated(true), projectNow.AddMinutes(5));
Check(editedProject.Version == 1 && editedProject.BeneficiaryId == project.BeneficiaryId && editedProject.CreatedAtUtc == projectNow &&
      editedProject.UpdatedAtUtc == projectNow.AddMinutes(5), "Project edits increment the version and keep the creation timestamp");
var projectMoveAttempt = ProjectInput.From(project); projectMoveAttempt.BeneficiaryId = project.BeneficiaryId + 1; projectMoveAttempt.Reason = "Mutare";
try { ProjectRules.Edited(project, projectMoveAttempt.Validated(true), projectNow.AddMinutes(5)); throw new Exception("Project moved to another beneficiary"); }
catch (ProjectOperationException exception) { Check(exception.Message == ProjectRules.BeneficiaryLockedMessage, "A project edit cannot change the beneficiary chosen at creation"); }
ProjectRejected(() => ProjectRules.CheckCurrent(editedProject, project), "Stale project version is rejected");
ProjectRejected(() => ProjectRules.CheckCurrent(null, project), "Deleted project is rejected on edit");
ProjectRejected(() => ProjectRules.CheckBeneficiaryExists(null), "Project save is rejected when the beneficiary no longer exists");
var observationInput = new ProjectObservationInput { Name = "  Verificare   șantier ", Content = " Fundația este turnată " }.Validated();
var observation = ProjectRules.CreateObservation(1, project.Id, observationInput, "operator", projectNow);
Check(observation.Name == "Verificare santier" && observation.Content == "Fundatia este turnata" &&
      observation.Author == "operator" && observation.Version == 0 && observation.CreatedAtUtc.Kind == DateTimeKind.Utc,
    "Project observations normalize text and keep author, version and UTC timestamps");
ProjectRejected(() => new ProjectObservationInput { Name = " " }.Validated(), "Observation name is required");
ProjectRejected(() => ProjectRules.CreateObservation(2, project.Id, observationInput, " ", projectNow), "Observation author is required");
var observationEdit = ProjectObservationInput.From(observation); observationEdit.Content = "Actualizat";
ProjectRejected(() => observationEdit.Validated(true), "Observation edits require a reason");
observationEdit.Reason = "Completare";
var editedObservation = ProjectRules.EditedObservation(observation, observationEdit.Validated(true), projectNow.AddMinutes(1));
Check(editedObservation.Version == 1 && editedObservation.CreatedAtUtc == projectNow, "Observation edits increment the version");
ProjectRejected(() => ProjectRules.CheckCurrent(editedObservation, observation), "Stale observation version is rejected");
var projectFile = ProjectFileRules.Create(1, observation.Id, @"..\..\C:\secret\Plan  fațadă.PDF", "application/pdf", 1024,
    new string('A', 64), "operator", projectNow);
Check(projectFile.OriginalName == "Plan  fațadă.PDF" && projectFile.StoredName.EndsWith(".pdf", StringComparison.Ordinal) &&
      projectFile.StoredName.Length == 36 && !projectFile.StoredName.Contains("Plan", StringComparison.Ordinal) &&
      projectFile.Sha256 == new string('a', 64) && projectFile.UploadedAtUtc.Kind == DateTimeKind.Utc,
    "Observation file metadata keeps a safe original name, a generated internal name and a normalized hash");
Check(ProjectFileRules.NewStoredName(".exe/../x") is { Length: 32 } && ProjectFileRules.NewStoredName(".pdf") != ProjectFileRules.NewStoredName(".pdf"),
    "Internal file names are unique and reject unsafe extensions");
ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "gol.txt", "text/plain", 0, new string('a', 64), "operator", projectNow),
    "Empty observation files are rejected");
ProjectRejected(() => ProjectFileRules.Create(2, observation.Id, "a.txt", "text/plain", 1, "nu-este-hash", "operator", projectNow),
    "Observation file metadata requires a SHA-256 hash");
ProjectRejected(() => ProjectFileRules.SafeOriginalName("../.."), "Path-only file names are rejected");
try { BeneficiaryRules.CheckNoLiveProjects(2); throw new Exception("Beneficiary with live projects accepted for deletion"); }
catch (BeneficiaryOperationException exception) { Check(exception.Message.Contains("2 proiecte", StringComparison.Ordinal), "Beneficiary deletion is blocked while live projects exist"); }
BeneficiaryRules.CheckNoLiveProjects(0);
Check(true, "Beneficiary without live projects passes the project deletion rule");

// Run only against an explicitly started local test instance with the documented test credentials.
if (args.Length == 2 && args[0] == "--http")
{
    var baseUri = new Uri(args[1]);
    if (!baseUri.IsLoopback) throw new Exception("HTTP checks are restricted to localhost");
    using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new System.Net.CookieContainer() };
    using var http = new HttpClient(handler) { BaseAddress = baseUri };
    var response = await http.GetAsync("/");
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("/Account/Login"), "Unauthenticated catalogue access redirects to login");
    response = await http.GetAsync("/app.css");
    Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/css" && (await response.Content.ReadAsStringAsync()).Length > 1000, "Styles are served before authentication");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.BadRequest, "Login rejects requests without antiforgery token");
    async Task<string> Token(string path)
    {
        var html = await http.GetStringAsync(path);
        var match = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!match.Success) throw new Exception("Missing antiforgery token");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    var token = await Token("/Account/Login");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","wrong"},{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("incorect"), "Invalid credentials do not authenticate");
    token = await Token("/Account/Login");
    response = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>{{"Username","admin"},{"Password","local-test-password-123"},{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Valid credentials create a session");
    response = await http.GetAsync("/");
    Check(response.IsSuccessStatusCode, "Authenticated catalogue is accessible");
    token = await Token("/Account/Logout");
    response = await http.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout accepts authenticated antiforgery token");
    response = await http.GetAsync("/");
    Check(response.StatusCode == System.Net.HttpStatusCode.Redirect, "Logout revokes the browser session");
}

// ---- Stock movements (Task 1) ----
async Task RejectedMovement(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (StockMovementOperationException) { Check(true, message); return; }
    throw new Exception("Expected rejection: " + message);
}
// Exits need a destination: unless given, an exit with a beneficiary goes to the beneficiary, any other one is a generic sale.
StockMovementInput MovementInput(StockMovementKind kind, int quantity, string description = "Test", DateOnly? date = null,
    int? beneficiaryId = null, int? projectId = null, string reason = "", ExitDestination? destination = null,
    int? vehicleId = null, int? sourceVehicleId = null, bool noDestination = false) => new()
{
    Kind = kind, Quantity = quantity, Description = description, Date = date ?? new DateOnly(2026, 9, 24),
    BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason,
    Destination = noDestination || kind == StockMovementKind.Entry ? null
        : destination ?? (beneficiaryId is not null ? ExitDestination.Beneficiary : ExitDestination.GenericSale),
    VehicleId = vehicleId, SourceVehicleId = sourceVehicleId
};

var entryRule = StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 3, "  Factură nouă  "), StockMovementKind.Entry, false);
Check(entryRule.Description == "Factura noua" && entryRule.Quantity == 3, "Movement description is normalized like other stored text");
Check(StockMovementRules.Effect(StockMovementKind.Entry, 5) == 5 && StockMovementRules.Effect(StockMovementKind.Exit, 5) == -5, "Entries add and exits subtract stock");
Check(StockMovementRules.DisplayDate(new DateOnly(2022, 8, 22)) == "22.08.2022" && StockMovementRules.LegacyDate(new DateOnly(2022, 8, 22)) == "22-08-2022" && StockMovementRules.ParseLegacyDate("01-03-2024") == new DateOnly(2024, 3, 1),
    "Movement dates are displayed as dd.MM.yyyy while the legacy column keeps dd-MM-yyyy");
Check(StockMovementRules.NormalizeDisplayDates("Data: 22-08-2022 → 01-03-2024; cod 99-99-2020; ref 122-08-2022") == "Data: 22.08.2022 → 01.03.2024; cod 99-99-2020; ref 122-08-2022"
      && StockMovementRules.NormalizeDisplayDates(null) == "", "Older journal texts show real dates as dd.MM.yyyy and leave other text untouched");
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, "  "), StockMovementKind.Exit, false); throw new Exception("Blank description accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement description is mandatory"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 0), StockMovementKind.Exit, false); throw new Exception("Zero quantity accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement quantity must be at least 1"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, StockMovementRules.MaxQuantity + 1), StockMovementKind.Exit, false); throw new Exception("Oversize quantity accepted"); }
catch (StockMovementOperationException) { Check(true, "Movement quantity has an upper limit"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, beneficiaryId: 1), StockMovementKind.Entry, false); throw new Exception("Entry with beneficiary accepted"); }
catch (StockMovementOperationException) { Check(true, "Beneficiary and project are refused for entries"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, projectId: 1), StockMovementKind.Exit, false); throw new Exception("Project without beneficiary accepted"); }
catch (StockMovementOperationException) { Check(true, "A project requires a beneficiary"); }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1), StockMovementKind.Exit, true); throw new Exception("Edit without reason accepted"); }
catch (StockMovementOperationException) { Check(true, "Editing a movement requires a reason"); }
var movementToday = new DateOnly(2026, 9, 25);
Check(StockMovementRules.Validated(MovementInput(StockMovementKind.Exit, 1, date: movementToday), StockMovementKind.Exit, false, movementToday).Date == movementToday &&
      StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: movementToday.AddDays(-1)), StockMovementKind.Entry, false, movementToday).Date == movementToday.AddDays(-1),
    "Movement date may be today or in the past");
foreach (var futureKind in new[] { StockMovementKind.Entry, StockMovementKind.Exit })
    foreach (var futureIsEdit in new[] { false, true })
    {
        try { StockMovementRules.Validated(MovementInput(futureKind, 1, date: movementToday.AddDays(1), reason: "Corectie"), futureKind, futureIsEdit, movementToday); throw new Exception("Future movement date accepted"); }
        catch (StockMovementOperationException exception)
        {
            Check(exception.Message == StockMovementRules.FutureDateMessage,
                $"A future movement date is rejected ({futureKind}, {(futureIsEdit ? "edit" : "create")})");
        }
    }
try { StockMovementRules.Validated(MovementInput(StockMovementKind.Entry, 1, date: new DateOnly(2099, 1, 1)), StockMovementKind.Entry, false); throw new Exception("Distant future date accepted"); }
catch (StockMovementOperationException) { Check(true, "A date far in the future is rejected against the real current day"); }
Check(AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Create, "t", "d", "", "5")) == "/miscari/5" &&
      AuditNavigation.TargetUrl(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "a", "r", AuditEntities.StockMovement, AuditActions.Delete, "t", "d", "", "5")) is null,
    "Journal links movement events to their product page and not after deletion");

Check(AddressNormalization.Key("Str. Florilor, nr. 5, Bl. A2") == AddressNormalization.Key("strada FLORILOR 5 bloc a2") &&
      AddressNormalization.Key("Șoseaua Nordului 10") == AddressNormalization.Key("Soseaua  nordului, numarul 10") &&
      AddressNormalization.Key("Strada Florilor 5") != AddressNormalization.Key("Strada Florilor 6"),
    "Work point address normalization ignores case, diacritics, punctuation, spacing and common abbreviations");








// Task 8: the 18 triggers already installed on the real MariaDB database (6 watched tables x 3 operations).
Check(ChangeEventTriggers.Maria.Count == 6 && ChangeEventTriggers.Suffixes.Count() == 3,
    "6 watched tables x 3 operations = the 18 triggers already installed on the real database");

// Relay: cursor, grace period, ledger, failures (with an in-memory source and a manual clock).
{
    var clock = new ManualTimeProvider();
    var source = new FakeChangeSource();
    var relayFeed = new InProcessChangeFeed(null, clock);
    var seen = new List<ChangeEvent>();
    using var relaySubscription = relayFeed.Subscribe(change => { lock (seen) seen.Add(change); return Task.CompletedTask; });
    source.Add("Produs", AuditActions.Edit, "1");
    var relay = new ChangeEventRelay(source, relayFeed, NullLogger<ChangeEventRelay>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sync:GraceMilliseconds"] = "1000" }).Build(), clock);
    Check(await relay.PollOnceAsync(default) == 0 && relay.Cursor == 1, "The relay starts after the newest stored event instead of replaying history");
    source.Add("Produs", AuditActions.Edit, "2");
    source.Add("Utilizator", AuditActions.Create, "7");
    Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 0, "A new event is held back during the grace period");
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    Check(await relay.PollOnceAsync(default) == 2 && seen.Count == 2 && seen[0].EntityId == "2" && seen[1].EntityType == "Utilizator" &&
          seen.All(change => change.Origin == Guid.Empty) && relay.Cursor == 3,
        "After the grace period each event is published once, in order, without an origin");
    clock.Advance(TimeSpan.FromSeconds(5));
    Check(await relay.PollOnceAsync(default) == 0 && seen.Count == 2, "Polling again never publishes an event twice");

    var ownOrigin = Guid.NewGuid();
    relayFeed.Publish(new("Proiect", AuditActions.Edit, "5", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 5));
    source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
    source.Add("Proiect", AuditActions.Edit, "5", projectId: 5);
    seen.Clear();
    await relay.PollOnceAsync(default);
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    Check(seen.Count == 1 && seen[0].Origin == Guid.Empty, "The trigger's copy of a change already published by a session is dropped once; a second change is still announced");

    relayFeed.Publish(new("Proiect", AuditActions.Delete, "9", ownOrigin, clock.GetUtcNow().UtcDateTime, ProjectId: 9));
    clock.Advance(TimeSpan.FromSeconds(40));
    Check(!relayFeed.TryConsumeLocal("Proiect", AuditActions.Delete, "9"), "A locally published change is forgotten after the ledger lifetime");

    source.FailNextRead = true;
    var cursorBeforeFailure = relay.Cursor;
    source.Add("Produs", AuditActions.Edit, "11");
    try { await relay.PollOnceAsync(default); throw new Exception("Failing source did not throw"); }
    catch (InvalidOperationException) { }
    seen.Clear();
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    clock.Advance(TimeSpan.FromMilliseconds(1100));
    await relay.PollOnceAsync(default);
    Check(relay.Cursor > cursorBeforeFailure && seen.Any(change => change.EntityId == "11"), "After a failed read the relay resumes from its cursor and loses nothing");
}

// Live refresh: bursts, own origin, busy forms, fallback.
{
    var liveFeed = new InProcessChangeFeed();
    var own = Guid.NewGuid();
    var refreshes = 0; var renders = 0; var busy = false;
    using var live = new LiveRefresh(liveFeed, own, change => change.EntityType == "Produs" && change.EntityId == "1",
        () => { Interlocked.Increment(ref refreshes); return Task.CompletedTask; }, () => busy,
        () => { Interlocked.Increment(ref renders); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(60));
    for (var i = 0; i < 3; i++) liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
    liveFeed.Publish(new("Produs", AuditActions.Edit, "2", Guid.Empty, DateTime.UtcNow));
    liveFeed.Publish(new("Produs", AuditActions.Edit, "1", own, DateTime.UtcNow));
    await Task.Delay(400);
    Check(refreshes == 1 && !live.HasPendingChange, "A burst of relevant events refreshes once; other entities and the session's own changes are ignored");

    busy = true;
    liveFeed.Publish(new("Produs", AuditActions.Edit, "1", Guid.Empty, DateTime.UtcNow));
    await Task.Delay(300);
    Check(refreshes == 1 && live.HasPendingChange && renders >= 1, "While a form is open nothing is refreshed and the page is asked to show a notice");
    busy = false;
    await live.ReloadPendingAsync();
    Check(refreshes == 2 && !live.HasPendingChange, "The user's reload applies the pending change and clears the notice");
}
{
    var fallbackRefreshes = 0; var fallbackBusy = true;
    using var fallback = new LiveRefresh(new InProcessChangeFeed(), Guid.NewGuid(), _ => true,
        () => { Interlocked.Increment(ref fallbackRefreshes); return Task.CompletedTask; }, () => fallbackBusy,
        () => Task.CompletedTask, fallbackInterval: TimeSpan.FromMilliseconds(80));
    await Task.Delay(300);
    Check(fallbackRefreshes == 0, "The periodic fallback does not refresh while a form is open");
    fallbackBusy = false;
    await Task.Delay(400);
    Check(fallbackRefreshes >= 1, "The periodic fallback refreshes when the page is idle even if no notification arrived");
}



Check(ProductLockRules.LeaseSeconds >= 60 && ProductLockRules.LeaseSeconds <= 120 && ProductLockRules.HeartbeatSeconds * 2 < ProductLockRules.LeaseSeconds,
    "The lease expires within 1–2 minutes and the heartbeat renews it well before that");
{
    var sample = new ProductLock(3, "ana", "s", new DateTime(2026, 9, 25, 10, 5, 0, DateTimeKind.Utc), DateTime.UtcNow, DateTime.UtcNow.AddSeconds(90), 90);
    var message = ProductLockRules.HeldMessage(sample);
    Check(message.Contains("ana") && message.Contains(sample.AcquiredUtc.ToLocalTime().ToString("HH:mm")) && message.Contains("consulta"),
        "The read-only message says who edits the product, since when, and that it can still be consulted");
}

// Task 1: unsaved-changes tracking and the leave warning.
{
    var guard = new UnsavedChanges();
    var name = "Ciocan";
    var closedOne = 0; var closedTwo = 0; var discardingSeen = false;
    var first = guard.Track(() => FormSnapshot.Values(name), () => { closedOne++; discardingSeen = guard.IsDiscarding; return Task.CompletedTask; });
    var otherText = "Nemodificat";
    var second = guard.Track(() => FormSnapshot.Values(otherText), () => { closedTwo++; return Task.CompletedTask; });
    Check(guard.TrackerCount == 2 && !guard.HasUnsavedChanges && !first.IsModified, "A form that was just opened is not modified");
    name = "Ciocan 2 kg";
    Check(first.IsModified && guard.HasUnsavedChanges, "A changed value marks the form as modified");
    name = "Ciocan";
    Check(!first.IsModified && !guard.HasUnsavedChanges, "Returning to the initial value makes the form unmodified again");

    var ran = 0;
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    Check(ran == 1 && guard.Pending is null, "An unmodified form is closed without asking");
    name = "Ciocan nou";
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    Check(ran == 1 && guard.Pending is not null, "A modified form asks first and does not run the action yet");
    guard.Cancel();
    Check(ran == 1 && guard.Pending is null && first.IsModified, "\"Înapoi la editare\" keeps the values, the form and the warning");
    await first.RunAfterConfirmAsync(() => { ran++; return Task.CompletedTask; });
    await guard.ConfirmAsync();
    Check(ran == 2 && closedOne == 1 && discardingSeen && !guard.IsDiscarding && !first.IsModified && closedTwo == 0,
        "\"Părăsește editarea\" discards only that form and then runs the interrupted action");

    otherText = "Modificat";
    var navigated = "";
    guard.Request(() => { navigated = "/beneficiari"; return Task.CompletedTask; });
    await guard.ConfirmAsync();
    Check(navigated == "/beneficiari" && closedTwo == 1 && closedOne == 1 && !guard.HasUnsavedChanges,
        "Leaving the page discards every modified form (and only those) and then performs the navigation");

    otherText = "Salvat";
    Check(second.IsModified == false, "A discarded form no longer counts as modified");
    var saved = guard.Track(() => FormSnapshot.Values(otherText), () => Task.CompletedTask);
    otherText = "Salvat cu succes";
    Check(saved.IsModified, "A new tracker starts from the values at that moment");
    saved.Rebase();
    Check(!saved.IsModified && !guard.HasUnsavedChanges, "A successful save makes the saved values the unmodified state");
    otherText = "Altă valoare";
    Check(saved.IsModified, "Changes after a save are detected again");

    var flips = 0;
    guard.Changed += () => flips++;
    saved.NotifyRendered(); saved.NotifyRendered();
    otherText = "Salvat cu succes"; // back to the saved value
    saved.Rebase();
    Check(flips >= 1, "The host is notified when the unsaved state flips");

    var addingValue = "";
    var addTracker = guard.Track(() => FormSnapshot.Values(addingValue), () => Task.CompletedTask, adding: true);
    var editValue = "a";
    var editTracker = guard.Track(() => FormSnapshot.Values(editValue), () => Task.CompletedTask);
    addingValue = "nou";
    guard.Request(() => Task.CompletedTask);
    Check(guard.Pending is { Adding: true }, "The question says \"adăugare\" when only forms that add a new object are affected");
    guard.Cancel();
    editValue = "b";
    guard.Request(() => Task.CompletedTask);
    Check(guard.Pending is { Adding: false }, "The question says \"editare\" as soon as an edited (existing) object is affected");
    guard.Cancel();
    guard.Request(() => Task.CompletedTask, addTracker);
    Check(guard.Pending is { Adding: true }, "Closing a single add form asks about the adding");
    guard.Cancel();
    addTracker.Dispose(); editTracker.Dispose();

    saved.Dispose(); first.Dispose(); second.Dispose();
    Check(guard.TrackerCount == 0 && !guard.HasUnsavedChanges, "Closing an editor removes its tracker");

    var product = ProductInput.From(data[0]);
    var before = FormSnapshot.Of(product);
    product.Reason = "Doar un motiv";
    Check(FormSnapshot.Of(product) == before, "Typing only the change reason is not an unsaved value change");
    product.Name += " x";
    Check(FormSnapshot.Of(product) != before, "Changing a product field changes the snapshot");
    Check(FormSnapshot.Of(product, "imagine.png") != FormSnapshot.Of(product), "A selected image counts as a change");
    var user = new WebUserInput { Username = "ana", Password = "abc" };
    var userBefore = FormSnapshot.Of(user);
    user.Password = "abcd";
    Check(FormSnapshot.Of(user) != userBefore, "A typed password counts as a change (compared only through a hash)");
}

// Task 1: inventory report (selection logic, the report builder, PDF generation and its journal event).
{
    // Selection logic (InventorySelectionState): category <-> subcategories <-> "select all", independent of the UI.
    var electric = ("Scule electrice", (IReadOnlyList<string>)new List<string> { "Găurire", "Tăiere" });
    var consumables = ("Consumabile", (IReadOnlyList<string>)new List<string> { "Fixare" });
    var emptyCategory = ("Categorie fără subcategorii", (IReadOnlyList<string>)new List<string>());
    var allCategories = new List<(string, IReadOnlyList<string>)> { electric, consumables, emptyCategory };
    var selection = new InventorySelectionState();
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: false } &&
          selection.AllState(allCategories) is { Checked: false, Indeterminate: false },
        "Nothing selected at the start is neither checked nor indeterminate");
    selection.SetSubcategory("Scule electrice", "Găurire", true);
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: true },
        "Selecting only one of two subcategories makes the category checkbox indeterminate");
    selection.SetSubcategory("Scule electrice", "Tăiere", true);
    Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: true, Indeterminate: false },
        "Selecting every subcategory checks the category checkbox");
    selection.SetCategory("Scule electrice", electric.Item2, false);
    Check(!selection.IsSelected("Scule electrice", "Găurire") && !selection.IsSelected("Scule electrice", "Tăiere"),
        "Deselecting a category propagates to all of its subcategories");
    Check(selection.CategoryState("Categorie fără subcategorii", emptyCategory.Item2) is { Checked: false, Indeterminate: false },
        "A category without subcategories is never checked or indeterminate");
    selection.SetAll(allCategories, true);
    Check(selection.AllState(allCategories) is { Checked: true, Indeterminate: false } &&
          selection.Count(allCategories) == 3 && selection.IsSelected("Consumabile", "Fixare"),
        "\"Select all categories\" selects every subcategory, ignoring categories without subcategories");
    selection.SetSubcategory("Consumabile", "Fixare", false);
    Check(selection.AllState(allCategories) is { Checked: false, Indeterminate: true },
        "\"Select all\" becomes indeterminate once one subcategory is cleared");
    Check(selection.ToItems(allCategories).Count == 2 && selection.ToItems(allCategories)
        .All(item => item.Category == "Scule electrice"), "ToItems returns exactly the selected pairs");

    // Report builder: a small fake catalogue with a negative, a zero and a vehicle-held product.
    var products = new List<Product>
    {
        new(1, "Scule electrice", "Găurire", "Mașină de găurit", "desc", 5, 1),
        new(2, "Scule electrice", "Tăiere", "Polizor", "desc", 0, 1),
        new(3, "Consumabile", "Fixare", "Șurub", "desc", -3, 1),
        new(4, "Consumabile", "Fixare", "Diblu", "desc", 10, 1),
        new(5, "Consumabile", "Ambalare", "Folie", "desc", 2, 1)
    };
    var groups = new List<ProductGroup>
    {
        new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere"),
        new("Consumabile", "Fixare"), new("Consumabile", "Ambalare")
    };
    var inVehicles = new Dictionary<int, int> { [4] = 4 }; // Diblu: 10 total, 4 in a vehicle -> 6 in the warehouse.
    var builder = new InventoryReportBuilder(new FakeInventoryProductRepository(products, groups), new FakeInventoryStockMovementRepository(inVehicles));

    var report = await builder.BuildAsync(new InventoryRequest(
        [new("Scule electrice", "Găurire"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
    Check(report.Categories.Count == 2 && report.SelectedCategoryCount == 2 && report.SelectedSubcategoryCount == 2,
        "The report contains only the selected categories and subcategories");
    var electricSection = report.Categories.Single(c => c.Category == "Scule electrice");
    Check(electricSection.Subcategories.Single().Subcategory == "Găurire" &&
          electricSection.Subcategories.Single().Lines.Single() == new InventoryLine("Mașină de găurit", 5),
        "An unselected subcategory (Tăiere) of a partially selected category is left out of the report");
    var consumablesSection = report.Categories.Single(c => c.Category == "Consumabile");
    var fixareLines = consumablesSection.Subcategories.Single().Lines;
    Check(fixareLines.Select(line => line.Code).SequenceEqual(["Diblu", "Șurub"]), "Products are ordered by code, case-insensitively");
    Check(fixareLines.Single(line => line.Code == "Diblu").Quantity == 6, "The warehouse value excludes the quantity held by vehicles");
    Check(fixareLines.Single(line => line.Code == "Șurub") is { Quantity: -3, IsNegative: true }, "Negative stock is kept and flagged");
    Check(report.ProductCount == 3 && report.NegativeCount == 1, "The report totals count every line and the negative ones separately");
    Check(!report.Categories.Any(c => c.Category == "Consumabile" && c.Subcategories.Any(s => s.Subcategory == "Ambalare")),
        "An unselected subcategory does not appear even when its category is otherwise selected");

    var zeroFiltered = await builder.BuildAsync(new InventoryRequest(
        [new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
    Check(zeroFiltered.Categories.Single().Subcategories.Single().Subcategory == "Găurire",
        "Excluding zero stock omits an emptied subcategory (Tăiere's only product has quantity 0) but keeps a non-empty one");

    try
    {
        await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
        throw new Exception("Empty result after filtering accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoProductsMessage, "A selection left empty by the zero-stock filter is reported, not silently generated"); }

    try
    {
        await builder.BuildAsync(new InventoryRequest([], ExcludeZeroStock: false));
        throw new Exception("Empty selection accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoSelectionMessage, "An empty selection is rejected before any catalogue access"); }

    try
    {
        await builder.BuildAsync(new InventoryRequest([new("Categorie inexistentă", "Subcategorie inexistentă")], ExcludeZeroStock: false));
        throw new Exception("Stale selection accepted");
    }
    catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.InvalidSelectionMessage, "A selection that no longer matches the catalogue is reported instead of generating a partial file"); }

    var duplicateSelection = await builder.BuildAsync(new InventoryRequest(
        [new("Consumabile", "Fixare"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
    Check(duplicateSelection.SelectedSubcategoryCount == 1, "A repeated selection entry is counted once");

    Check(InventoryRules.FileName(new DateTime(2026, 9, 25, 14, 8, 0)) == "Inventar_2026-09-25_1408.pdf",
        "The proposed file name uses the local moment of generation");

    // PDF generation: the file starts with %PDF, has the expected texts (including a diacritic code, extracted
    // through the embedded font's own ToUnicode map), the negative row is red, and headings use the bold 14pt font.
    var pdfWriter = new InventoryPdfWriter();
    var generatedLocal = new DateTime(2026, 9, 25, 14, 8, 0);
    var pdfBytes = pdfWriter.Write(report, generatedLocal);
    Check(pdfBytes.Length > 4 && pdfBytes[0] == (byte)'%' && pdfBytes[1] == (byte)'P' && pdfBytes[2] == (byte)'D' && pdfBytes[3] == (byte)'F',
        "The generated file starts with the PDF signature");
    using (var pdfStream = new MemoryStream(pdfBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(pdfDocument.PageCount >= 1, "The generated PDF has at least one page");
        var pageText = PdfTextExtractor.ExtractText(pdfDocument.Pages[0]);
        Check(pageText.Contains("Inventar") && pageText.Contains("Generat la: 25.09.2026 14:08"),
            "The first page has the title and the local generation moment in the dd.MM.yyyy HH:mm form");
        Check(pageText.Contains("Scule electrice") && pageText.Contains("Găurire") && pageText.Contains("Consumabile") && pageText.Contains("Fixare"),
            "The selected category and subcategory names appear on the page");
        Check(pageText.Contains("Nr. crt.") && pageText.Contains("Cod produs") && pageText.Contains("Valoare stoc") && pageText.Contains("Valoare reală"),
            "The table header has the four required columns (Nr. crt., Cod produs, Valoare stoc, Valoare reală)");
        Check(pageText.Contains("Șurub"), "A product code with Romanian diacritics is extracted correctly from the embedded font");
        var fontNames = PdfTextExtractor.FontBaseNames(pdfDocument.Pages[0]);
        var fontUsage = PdfTextExtractor.FontUsage(pdfDocument.Pages[0]);
        Check(fontUsage.Any(usage => usage.Size == 14 && fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
            "Category and subcategory names use a bold 14pt font");
        Check(fontUsage.Any(usage => usage.Size == 12 && !fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
            "The rest of the text uses a normal 12pt font");
        Check(PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
            "The page sets the red fill colour for the negative-stock row");
    }

    var noNegativeReport = await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Găurire")], ExcludeZeroStock: false));
    var noNegativeBytes = pdfWriter.Write(noNegativeReport, generatedLocal);
    using (var pdfStream = new MemoryStream(noNegativeBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(!PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
            "A report with no negative stock never sets the red fill colour");
    }

    // A large catalogue produces more than one page, with the table header repeated on the following pages.
    var manyLines = Enumerable.Range(1, 300).Select(index => new InventoryLine($"Produs {index:000}", 1)).ToArray();
    var largeReport = new InventoryReport(DateTime.UtcNow,
        [new InventoryCategorySection("Categorie mare", [new InventorySubcategorySection("Subcategorie mare", manyLines)])],
        1, 1, manyLines.Length, 0);
    var largeBytes = pdfWriter.Write(largeReport, generatedLocal);
    using (var pdfStream = new MemoryStream(largeBytes))
    {
        var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
        Check(pdfDocument.PageCount > 1, "A catalogue of 300 products spans more than one page");
        Check(Enumerable.Range(0, pdfDocument.PageCount).Count(index => PdfTextExtractor.ExtractText(pdfDocument.Pages[index]).Contains("Cod produs")) > 1,
            "The table header is repeated on the pages that continue the table");
        // Data rows are InventoryPdfWriter.RowHeight tall (room for handwriting), so a page holds far fewer rows than at
        // the old single-line height (16pt: about 45 rows on an A4 page); the rows are still all there.
        var rowsPerPage = Enumerable.Range(0, pdfDocument.PageCount).Select(index => System.Text.RegularExpressions.Regex.Matches(PdfTextExtractor.ExtractText(pdfDocument.Pages[index]), "Produs [0-9]{3}").Count).ToArray();
        var pageCapacity = (int)((842 - 2 * 40 - 30) / InventoryPdfWriter.RowHeight);
        Check(InventoryPdfWriter.RowHeight >= 30 && rowsPerPage.Sum() == 300 && rowsPerPage.Max() <= pageCapacity && pdfDocument.PageCount >= 300 / pageCapacity,
            "Table rows are tall enough for handwriting: a page holds at most " + pageCapacity + " of them and none is lost across pages");
        Check($"Pagina {pdfDocument.PageCount} din {pdfDocument.PageCount}" is { } lastPageLabel &&
              PdfTextExtractor.ExtractText(pdfDocument.Pages[pdfDocument.PageCount - 1]).Contains(lastPageLabel),
            "Each page has a \"Pagina x din y\" footer");
    }

    // The journal event: written only after a successful generation, with a summary but no product data.
    var inventoryAudit = new TestAuditTrail();
    var inventoryAccess = new TestAccessControl(false, "gestionar.stoc");
    var auditDetails = AuditDetails.Identification(
        ("Categorii selectate", report.SelectedCategoryCount.ToString()), ("Subcategorii selectate", report.SelectedSubcategoryCount.ToString()),
        ("Stoc 0 exclus", "nu"), ("Produse în situație", report.ProductCount.ToString()),
        ("Din care cu stoc negativ", report.NegativeCount.ToString()), ("Fișier", "Inventar_2026-09-25_1408.pdf"));
    await AuditRecorder.RecordGenerateAsync(inventoryAudit, inventoryAccess, AuditEntities.Inventory, "Situație de inventar", auditDetails, CancellationToken.None);
    var inventoryEvent = inventoryAudit.Entries.Single();
    Check(inventoryEvent.ActorUsername == "gestionar.stoc" && inventoryEvent.EntityType == AuditEntities.Inventory &&
          inventoryEvent.EntityId.Length == 0 && !inventoryEvent.Details.Contains("Mașină de găurit") && !inventoryEvent.Details.Contains("Șurub") &&
          inventoryEvent.Details.Contains("Produse în situație: 3") && inventoryEvent.Details.Contains("Din care cu stoc negativ: 1"),
        "A successful generation writes exactly one event with the user, a summary and no product data or link");
    Check(AuditActions.Normalize(AuditActions.Generate) == "Generare", "The generation action is named \"Generare\" in the journal");
}

// Task 1: every message shown to the user is in Romanian. The literals that become user-visible messages (validation
// attributes, operation exceptions, error/notice fields, ...Message constants) must not contain common English words.
{
    var projectRoot = AppContext.BaseDirectory;
    while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
    Check(projectRoot is not null, "The project folder was found for the message scan");
    var files = new[] { "Services", "Components", "Pages" }
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(projectRoot!, folder), "*.*", SearchOption.AllDirectories))
        .Where(file => file.EndsWith(".cs") || file.EndsWith(".razor") || file.EndsWith(".cshtml"))
        .Append(Path.Combine(projectRoot!, "Program.cs")).ToArray();
    var patterns = new[]
    {
        @"(?:ErrorMessage|ParsingErrorMessage)\s*=\s*""([^""]*)""",
        @"\w+Exception\(\s*\$?""([^""]*)""",
        @"\b(?:error|Error|formError|editError|deleteError|historyError|notice|lockMessage|unlockError|reasonError|imageError|filesError|message|Message)\s*=\s*\$?""([^""]*)""",
        @"\b\w*Message\s*(?:=|=>)\s*\$?""([^""]*)""",
        @"\b[eE]rrors\.Add\(\s*\$?""([^""]*)""",
    };
    var english = new HashSet<string>(["the", "is", "must", "cannot", "failed", "invalid", "required", "please", "error", "already",
        "exists", "found", "unable", "could", "should", "will", "your", "been", "denied", "missing", "expected", "value", "field", "not"],
        StringComparer.OrdinalIgnoreCase);
    var scanned = 0; var offenders = new List<string>();
    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        foreach (var pattern in patterns)
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, pattern))
            {
                var literal = match.Groups[1].Value;
                scanned++;
                var words = System.Text.RegularExpressions.Regex.Matches(literal, @"[A-Za-z']+").Select(word => word.Value);
                var found = words.Where(english.Contains).ToArray();
                if (found.Length > 0) offenders.Add($"{Path.GetFileName(file)}: \"{literal}\" ({string.Join(", ", found)})");
            }
    }
    Check(scanned > 100, $"The message scan reads the message literals ({scanned} found)");
    Check(offenders.Count == 0, "Message literals shown to users contain no English words" + (offenders.Count == 0 ? "" : ": " + string.Join(" | ", offenders.Take(5))));
}

// Task 1 (preluare inventar OCR): the pipeline is exercised against a real scan the user provided of the
// situatia de inventar PDF, printed, filled in by hand and scanned back (tests/BlazorStoc.Checks/Fixtures).
InventoryPickupScanResult pickupScan;
{
    var projectRoot = AppContext.BaseDirectory;
    while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
    Check(projectRoot is not null, "The project folder was found for the inventory pickup fixture");
    var tessdataEnv = new TestWebHostEnvironment(projectRoot!);
    using var ocrService = new InventoryPickupOcrService(new TessdataPath(tessdataEnv));
    await using var fixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba.pdf"));
    pickupScan = await ocrService.ScanAsync(fixtureStream);

    (string Code, int Value) Row(string code) => pickupScan.Rows
        .Where(row => TextNormalization.SameUniqueValue(row.RawCode, code))
        .Select(row => (row.RawCode, row.RecognizedValue ?? -1)).First();
    Check(pickupScan.PageCount == 1, "The sample scan has a single page");
    Check(Row("Surub autoforant 4,8 x 25 test").Value == 105, "OCR reads the handwritten value for a fully separated 3-digit number");
    Check(Row("Casca de protectie alba XXL").Value == 24, "OCR reads a 2-digit handwritten value from a table further down the page");
    Check(Row("Manusi de lucru").Value == 54, "OCR reads a 2-digit handwritten value after recalibrating the column geometry per page");
    Check(Row("Nivela cu bula 60 cm").Value == 6, "OCR reads a single handwritten digit");
    Check(Row("Ciocan rotopercutor SDS Plus").Value == 0, "OCR reads a handwritten zero");
    Check(pickupScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Set chei combinate")) is false ||
          pickupScan.Rows.First(row => TextNormalization.SameUniqueValue(row.RawCode, "Set chei combinate")).RecognizedValue is null,
        "A row left blank on the form never gets a fabricated value");
    var diblu = pickupScan.Rows.First(row => TextNormalization.SameUniqueValue(row.RawCode, "Diblu nylon 8 x 40 test 22"));
    Check(diblu.RecognizedValue == 123, "OCR reads a 3-digit value written with tighter spacing than the other rows");
    // Known residual limitation (documented in docs/TESTE_RAMASE.md): the embedded generic MNIST classifier
    // sometimes confidently misreads a stylized handwritten digit (this scan's cursive "8") as a different one.
    // The row must still surface for the user to see and correct, which is what this checks.
    Check(pickupScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Polizor unghiular")),
        "A row is never silently dropped just because the digit classifier read it with (mistaken) confidence");
}

// Task 1 (preluare inventar OCR), regression: a real user scan of the same form (same products, printed and
// scanned again) came back with zero recognized rows - the "Nu a fost gasit niciun tabel..." error - even though
// every table's rule lines are clearly visible to the eye. The scan carried well under half a degree of paper
// skew, enough to spread each line's ink over roughly ten image rows so no single row reached the fill-ratio
// threshold used to detect a rule line (confirmed by instrumenting FindHorizontalLines, not guessed). Fixed with a
// small vertical dilation before that check (InventoryPickupOcrService.FindHorizontalLines,
// LineDetectionDilationHeight); this fixture locks the fix in.
{
    var projectRoot = AppContext.BaseDirectory;
    while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
    var tessdataEnv = new TestWebHostEnvironment(projectRoot!);
    using var ocrService = new InventoryPickupOcrService(new TessdataPath(tessdataEnv));
    await using var fixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-inclinata.pdf"));
    var skewedScan = await ocrService.ScanAsync(fixtureStream);
    Check(skewedScan.Rows.Count == 10, $"A slightly skewed real scan is still read as a table (10 rows expected, got {skewedScan.Rows.Count})");
    Check(skewedScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Ciocan rotopercutor SDS Plus") && row.RecognizedValue == 1),
        "A row from the skewed scan still reads its handwritten value correctly");

    // A second real scan of the same form, rotated by a few degrees (clearly visible to the eye, not just a
    // fraction of a degree) - the small dilation above cannot bridge a skew this large; only actively deskewing
    // the whole page (InventoryPickupOcrService.FindSkewDegrees/Rotate) does.
    await using var rotatedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-rotita.pdf"));
    var rotatedScan = await ocrService.ScanAsync(rotatedFixtureStream);
    Check(rotatedScan.Rows.Count == 10, $"A visibly rotated real scan is still read as a table after deskewing (10 rows expected, got {rotatedScan.Rows.Count})");
    Check(rotatedScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Masina de gaurit cu acumulator") && row.RecognizedValue == 11),
        "A row from the rotated scan still reads its handwritten value correctly after deskewing");

    // A real Konica Minolta scan (30.09.2026): the table's left border sat ~88 px right of the computed position,
    // outside the former 3% calibration radius, so the dividers were derived from the wrong edge and the single
    // row was rejected ("Nu a fost gasit niciun tabel"). CalibrateColumns now searches 8% of the page width.
    await using var shiftedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-decalata.pdf"));
    var shiftedScan = await ocrService.ScanAsync(shiftedFixtureStream);
    Check(shiftedScan.Rows.Count == 1 && shiftedScan.Rows[0].RecognizedValue == 50 && !shiftedScan.Rows[0].Uncertain,
        $"A scan whose table is shifted several millimetres from the computed position is still read (1 row = 50 expected, got {shiftedScan.Rows.Count})");
    Check(shiftedScan.Rows[0].Number is null, "A form printed before the \"Nr. crt.\" column existed is still read, without a running number");

    // The first real scan of the form WITH the "Nr. crt." column (30.09.2026, Konica Minolta, pencil-like faint ink,
    // about -1.5 degrees of skew): two tables, running numbers 1,2 then 1 again, and a handwritten "10" whose thin "1"
    // the plain read loses (it reads 1) - only the contrast-stretched re-read recovers it.
    await using var numberedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-nr-crt.pdf"));
    var numberedScan = await ocrService.ScanAsync(numberedFixtureStream);
    Check(numberedScan.Rows.Select(row => row.Number).SequenceEqual(new int?[] { 1, 2, 1 }),
        $"A real scan of the numbered form restarts the running number for the second subcategory (got {string.Join(",", numberedScan.Rows.Select(row => row.Number?.ToString() ?? "?"))})");
    Check(numberedScan.Rows.Select(row => row.RecognizedValue).SequenceEqual(new int?[] { 2, 10, 7 }) && numberedScan.Rows.All(row => !row.Uncertain),
        $"A real faint, slightly rotated scan is read correctly, including a thin handwritten \"10\" (got {string.Join(",", numberedScan.Rows.Select(row => row.RecognizedValue?.ToString() ?? "?"))})");

    // Forms with the "Nr. crt." column, end to end: the writer's PDF is rasterised, "handwriting" is painted into every
    // row's "Valoare reala" cell (black pen, graphite pencil, and a washed-out scan), put back into a PDF and read by
    // the OCR. The running number restarts at 1 for every subcategory and is only display data (never stored).
    {
        var numberedReport = new InventoryReport(DateTime.UtcNow,
            [new InventoryCategorySection("Categorie test", [
                new InventorySubcategorySection("Prima subcategorie", [new("Produs alfa", 3), new("Produs beta", 4), new("Produs gama", 5)]),
                new InventorySubcategorySection("A doua subcategorie", [new("Produs delta", 6), new("Produs epsilon", 7)])])],
            1, 2, 5, 0);
        var formPdf = new InventoryPdfWriter().Write(numberedReport, new DateTime(2026, 9, 30, 9, 0, 0));

        byte[] HandwriteAndRescan(byte[] pdf, int inkGray, int thickness, double washOut)
        {
            using var source = new MemoryStream(pdf);
            var bitmaps = PDFtoImage.Conversion.ToImages(source, options: new PDFtoImage.RenderOptions(Dpi: 300, Grayscale: true)).ToList();
            using var bitmap = bitmaps[0];
            using var gray8 = bitmap.ColorType == SkiaSharp.SKColorType.Gray8 ? null : bitmap.Copy(SkiaSharp.SKColorType.Gray8);
            var pixels = gray8 ?? bitmap;
            using var raw = OpenCvSharp.Mat.FromPixelData(pixels.Height, pixels.Width, OpenCvSharp.MatType.CV_8UC1, pixels.GetPixels(), (long)pixels.RowBytes);
            using var image = raw.Clone();
            var scale = 300 / 72.0;
            var columns = InventoryPdfLayout.ComputeColumns(image.Cols / scale);
            int left = (int)(columns.NumberX * scale), right = (int)(columns.RightEdge * scale);

            // Horizontal rules of the rendered form (dark rows across the table width), then the data rows between them.
            var lineRows = new List<int>();
            for (var y = 0; y < image.Rows; y++)
            {
                using var strip = image.SubMat(y, y + 1, left, right);
                using var dark = strip.LessThan(128).ToMat();
                if (OpenCvSharp.Cv2.CountNonZero(dark) > (right - left) * 0.6) lineRows.Add(y);
            }
            var lines = new List<int>();
            foreach (var y in lineRows) if (lines.Count == 0 || y - lines[^1] > 3) lines.Add(y);
            var dataRows = 0;
            for (var i = 0; i + 1 < lines.Count; i++)
            {
                var top = lines[i];
                var height = lines[i + 1] - top;
                if (height < 120 || height > 170) continue; // data rows are 34 pt (about 142 px); header rows and gaps are not
                var text = (10 + dataRows).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var size = OpenCvSharp.Cv2.GetTextSize(text, OpenCvSharp.HersheyFonts.HersheySimplex, 2.6, thickness, out var baseline);
                var cellLeft = (int)(columns.RealX * scale);
                var origin = new OpenCvSharp.Point(cellLeft + ((right - cellLeft) - size.Width) / 2, top + (height + size.Height) / 2);
                OpenCvSharp.Cv2.PutText(image, text, origin, OpenCvSharp.HersheyFonts.HersheySimplex, 2.6, new OpenCvSharp.Scalar(inkGray), thickness, OpenCvSharp.LineTypes.AntiAlias);
                dataRows++;
            }
            // Washed-out scan: every ink level pulled towards white (out = 255 - (255 - in) * washOut).
            if (washOut < 1.0) image.ConvertTo(image, OpenCvSharp.MatType.CV_8UC1, washOut, 255 * (1 - washOut));

            using var document = new PdfSharp.Pdf.PdfDocument();
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using (var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
            using (var png = new MemoryStream(image.ImEncode(".png")))
            using (var xImage = PdfSharp.Drawing.XImage.FromStream(png))
                graphics.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }

        foreach (var (label, inkGray, thickness, washOut) in new[] { ("black pen", 0, 6, 1.0), ("graphite pencil", 150, 2, 1.0), ("washed-out scan", 0, 6, 0.4) })
        {
            using var formStream = new MemoryStream(HandwriteAndRescan(formPdf, inkGray, thickness, washOut));
            InventoryPickupScanResult scanned;
            try { scanned = await ocrService.ScanAsync(formStream); }
            catch (InventoryPickupOcrException exception) { Check(false, $"A form with the \"Nr. crt.\" column ({label}) is read ({exception.Message})"); continue; }
            Check(scanned.Rows.Count == 5 && TextNormalization.SameUniqueValue(scanned.Rows[0].RawCode, "Produs alfa"),
                $"A form with the \"Nr. crt.\" column ({label}) is read: 5 rows expected, got {scanned.Rows.Count}");
            Check(scanned.Rows.Select(row => row.Number).SequenceEqual(new int?[] { 1, 2, 3, 1, 2 }),
                $"The running number restarts at 1 for each subcategory ({label}): got {string.Join(",", scanned.Rows.Select(row => row.Number?.ToString() ?? "?"))}");
            Check(scanned.Rows.Select(row => row.RecognizedValue).SequenceEqual(new int?[] { 10, 11, 12, 13, 14 }),
                $"The handwritten values are read ({label}): got {string.Join(",", scanned.Rows.Select(row => row.RecognizedValue?.ToString() ?? "?"))}");
        }
    }

    // A single PDF can have a different skew on every page (each page was fed through the scanner separately, or
    // a multi-page situatia de inventar was assembled from several individual scans). FindSkewDegrees/Rotate must
    // run per page, not once for the whole document - locked in here with a 2-page fixture built from the two
    // fixtures above (page 1 keeps its ~0.3 degree skew, page 2 its ~2.7 degrees), rather than assuming the code
    // already does this correctly from reading it.
    await using var multiPageFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-multipagina.pdf"));
    var multiPageScan = await ocrService.ScanAsync(multiPageFixtureStream);
    Check(multiPageScan.PageCount == 2, "The multi-page fixture has two pages");
    Check(multiPageScan.Rows.Count(row => row.Page == 1) == 10, "Page 1 (mild skew) is fully read on its own");
    Check(multiPageScan.Rows.Count(row => row.Page == 2) == 10, "Page 2 (visible rotation) is fully read on its own, independently of page 1's skew");
}

// Domain logic (matching, diffing, applying) is tested against fake repositories, independent of the real OCR
// pipeline above, so these checks stay meaningful even if the sample scan or the embedded model ever change.
{
    IReadOnlyList<Product> catalog =
    [
        new(1, "Consumabile", "Fixare", "Surub autoforant 4,8 x 25 test", "", 96),
        new(2, "Scule de mana", "Strangere", "Set chei combinate", "", 7),
        new(3, "Masurare", "Nivelare", "Nivela cu bula 60 cm", "", 5),
    ];
    IReadOnlyDictionary<int, int> noVehicleStock = new Dictionary<int, int>();
    var builder = new InventoryPickupBuilder(new FakeInventoryProductRepository(catalog, []), new FakeInventoryPickupMovementRepository(noVehicleStock));

    var exactMatch = await builder.BuildAsync([new InventoryPickupScanRow(1, "Surub autoforant 4,8 x 25 test", 105, false)]);
    Check(exactMatch.Lines.Single().ProductId == 1 && exactMatch.Lines.Single().Difference == 9,
        "An exact name match computes the difference against the current warehouse stock");

    var noDifference = await builder.BuildAsync([new InventoryPickupScanRow(1, "Nivela cu bula 60 cm", 5, false)]);
    Check(noDifference.Lines.Count == 0, "A recognized value equal to the current stock produces no line to act on");

    var uncertainNoDifference = await builder.BuildAsync([new InventoryPickupScanRow(1, "Nivela cu bula 60 cm", 5, true)]);
    Check(uncertainNoDifference.Lines.Count == 1, "An uncertain row is shown for review even when its (possibly wrong) value matches the current stock");

    var fuzzyMatch = await builder.BuildAsync([new InventoryPickupScanRow(1, "Suruh autoforant 4,8 x 25 test", 100, false)]);
    Check(fuzzyMatch.Lines.SingleOrDefault()?.ProductId == 1, "A one-character OCR misread still matches its product by a close, unambiguous name");

    var notFound = await builder.BuildAsync([new InventoryPickupScanRow(1, "Produs care nu exista", 3, false)]);
    Check(notFound.Lines.Count == 0 && notFound.NotFound.Single().RawCode == "Produs care nu exista",
        "A code with no close match in the catalogue is reported separately, not silently matched to something else");

    var pickupRepository = new FakeInventoryPickupMovementRepository(noVehicleStock);
    var applier = new InventoryPickupApplier(pickupRepository, NullLogger<InventoryPickupApplier>.Instance);
    var toApply = new[]
    {
        new InventoryPickupLine(1, "Surub autoforant 4,8 x 25 test", "Surub autoforant 4,8 x 25 test", "Consumabile", "Fixare", 96, 105, false),
        new InventoryPickupLine(2, "Set chei combinate", "Set chei combinate", "Scule de mana", "Strangere", 7, 2, false),
    };
    var applyResult = await applier.ApplyAsync(toApply);
    Check(applyResult.Applied.Count == 2 && applyResult.Failed.Count == 0, "Every selected line with a difference is applied");
    Check(pickupRepository.Created[0].Input.Kind == StockMovementKind.Entry && pickupRepository.Created[0].Input.Quantity == 9,
        "A surplus (real value above current stock) is recorded as an Entry");
    Check(pickupRepository.Created[1].Input.Kind == StockMovementKind.Exit && pickupRepository.Created[1].Input.Destination == ExitDestination.StockCorrection
          && pickupRepository.Created[1].Input.Quantity == 5, "A shortfall is recorded as an Exit with the existing StockCorrection destination, not a new movement type");

    var failingRepository = new FakeInventoryPickupMovementRepository(noVehicleStock, failProductIds: new HashSet<int> { 2 });
    var partialApplier = new InventoryPickupApplier(failingRepository, NullLogger<InventoryPickupApplier>.Instance);
    var partialResult = await partialApplier.ApplyAsync(toApply);
    Check(partialResult.Applied.Count == 1 && partialResult.Failed.Count == 1,
        "A failure applying one line does not prevent the others from being applied, and is reported back explicitly");
}

// Subtask 1.4 (Task 2): pure-logic checks for the database backup mechanism that need no MariaDB connection -
// package naming, the canonical row-hash building blocks, and the file-based operation lock's heartbeat/expiry.
Check(BackupNaming.BuildFileName(BackupKind.InventoryPickup, new DateTime(2026, 9, 29, 14, 5, 30), AccessRoles.Administrator, "Ionescu Ștefan")
        == "Copie siguranta preluare inventar 29.09.2026 14-05-30 Administrator Ionescu Stefan.zip",
    "Backup file names follow the required model, in dd.MM.yyyy HH-mm-ss local time, without diacritics");
Check(BackupNaming.SanitizeToken("a/b:c*d") == "a b c d", "Filesystem-invalid characters in operator name/role become spaces, never break the file name");
Check(BackupNaming.SanitizeToken("  ") == "necunoscut", "An empty operator token still produces a valid, non-empty file name segment");

Check(CanonicalRowHasher.CanonicalizeValue(null, 'I') == "NULL", "A null value canonicalizes the same regardless of column type");
Check(CanonicalRowHasher.CanonicalizeValue(42L, 'I') == "I:42" && CanonicalRowHasher.CanonicalizeValue(42, 'I') == "I:42",
    "Integer values canonicalize identically whether read back as int or long");
Check(CanonicalRowHasher.CanonicalizeValue(3.5m, 'R') == "R:3.5", "Decimal values canonicalize with an invariant-culture textual form");
Check(CanonicalRowHasher.CanonicalizeValue("abc", 'T') == "T:abc", "Text values keep a distinct prefix from numeric ones");
Check(CanonicalRowHasher.PrefixForDataType("int") == 'I' && CanonicalRowHasher.PrefixForDataType("bigint") == 'I' &&
      CanonicalRowHasher.PrefixForDataType("decimal") == 'R' && CanonicalRowHasher.PrefixForDataType("varchar") == 'T' &&
      CanonicalRowHasher.PrefixForDataType("longtext") == 'T' && CanonicalRowHasher.PrefixForDataType("blob") == 'B',
    "Every MariaDB column type used by the migrated schema maps to the expected canonical prefix");
var sameRowColumns = new[] { new CanonicalColumn("id", 'I'), new CanonicalColumn("name", 'T') };
var hashA = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Test"]);
var hashB = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Test"]);
var hashC = CanonicalRowHasher.HashRow(sameRowColumns, [1, "Different"]);
Check(hashA == hashB, "The same row always canonicalizes to the same hash");
Check(hashA != hashC, "A single changed column value changes the row's hash");

await RunOperationLockChecksAsync();
async Task RunOperationLockChecksAsync()
{
    var lockPath = Path.Combine(Path.GetTempPath(), $"blazorstoc-lock-check-{Guid.NewGuid():N}", "operation.lock.json");
    var service = new FileOperationLockService(lockPath);
    try
    {
        var firstHandle = await service.TryAcquireAsync("backup:test", "ionescu", AccessRoles.Administrator);
        Check(firstHandle is not null, "Acquiring a free lock succeeds");
        var secondHandle = await service.TryAcquireAsync("backup:test", "popescu", AccessRoles.LimitedUser);
        Check(secondHandle is null, "A second concurrent request is refused while the lock is held");
        var active = await service.GetActiveAsync();
        Check(active is { OperatorName: "ionescu" }, "The active lock reports the operator that holds it");
        await firstHandle!.DisposeAsync();
        Check(await service.GetActiveAsync() is null, "Releasing the lock (disposing the handle) clears it immediately");
        var thirdHandle = await service.TryAcquireAsync("backup:test", "vasilescu", AccessRoles.Administrator);
        Check(thirdHandle is not null, "The lock can be acquired again once released");
        await thirdHandle!.DisposeAsync();
    }
    finally { try { Directory.Delete(Path.GetDirectoryName(lockPath)!, true); } catch (IOException) { } }
}

// Write freeze of a backup/restore (MaintenanceGate): while the shared operation lock is held, every session but the
// operation itself is refused at the data-access layer; a page navigation is sent to the waiting page.
await RunMaintenanceGateChecksAsync();
async Task RunMaintenanceGateChecksAsync()
{
    // The MariaDB chokepoint (DatabaseConnections.Create, internal): refused for a non-owner while the lock is held, allowed
    // otherwise and for the owner. Creating the connection object opens nothing, so no database is needed.
    {
        var mariaGateRoot = Path.Combine(Path.GetTempPath(), $"blazorstoc-gate-maria-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mariaGateRoot);
        var mariaGateLockPath = Path.Combine(mariaGateRoot, "operation.lock.json");
        var mariaGateLocks = new FileOperationLockService(mariaGateLockPath);
        var createConnection = typeof(FileOperationLockService).Assembly.GetType("BlazorStoc.Services.DatabaseConnections")!
            .GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var mariaGateConfiguration = new ConfigurationBuilder().Build();
        string TryCreate()
        {
            try { using var connection = (IDisposable)createConnection.Invoke(null, [mariaGateConfiguration])!; return "created"; }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is MaintenanceInProgressException) { return "blocked"; }
        }
        try
        {
            MaintenanceGate.Configure(mariaGateLockPath);
            Check(TryCreate() == "created", "MariaDB connections are created normally while no operation runs");
            var mariaHeld = await mariaGateLocks.TryAcquireAsync("backup:test", "administrator", AccessRoles.Administrator);
            Check(TryCreate() == "blocked", "A MariaDB connection is refused for a non-owner while an operation holds the lock");
            using (MaintenanceGate.EnterOwnerScope())
                Check(TryCreate() == "created", "The operation that holds the lock still gets its own MariaDB connections");
            await mariaHeld!.DisposeAsync();
            Check(TryCreate() == "created", "MariaDB connections are created again once the operation ends");
        }
        finally
        {
            MaintenanceGate.Configure(null);
            try { Directory.Delete(mariaGateRoot, true); } catch (IOException) { }
        }
    }

    // Page navigations while an operation runs go to the waiting page; the running page's own traffic does not.
    var activeOperation = new OperationLockInfo("backup:test", "administrator", AccessRoles.Administrator, DateTime.UtcNow, DateTime.UtcNow);
    static Microsoft.AspNetCore.Http.HttpRequest PageRequest(string path, string method = "GET", string accept = "text/html,application/xhtml+xml")
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?categorie=Scule");
        context.Request.Headers.Accept = accept;
        return context.Request;
    }
    Check(MaintenanceSupport.ShouldRedirect(PageRequest("/produse"), activeOperation), "A page navigation during an operation is redirected");
    Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse"), null), "Nothing is redirected when no operation runs");
    Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse", method: "POST"), activeOperation), "Only GET navigations are redirected");
    Check(!MaintenanceSupport.ShouldRedirect(PageRequest("/produse", accept: "application/json"), activeOperation), "A request that does not ask for HTML is not redirected");
    foreach (var passThrough in new[] { "/intretinere", "/api/maintenance", "/Account/Login", "/_blazor", "/_framework/blazor.web.js", "/hubs/changes", "/app.css", "/media/products/3" })
        Check(!MaintenanceSupport.ShouldRedirect(PageRequest(passThrough), activeOperation), $"{passThrough} is never redirected to the waiting page");
    Check(MaintenanceSupport.WaitingUrl(PageRequest("/produse")) == "/intretinere?returnUrl=%2Fproduse%3Fcategorie%3DScule",
        "The waiting page URL remembers where to come back to");
    Check(MaintenanceSupport.SafeReturnUrl("/produse?categorie=Scule") == "/produse?categorie=Scule" &&
          MaintenanceSupport.SafeReturnUrl("//evil.example") == "/" && MaintenanceSupport.SafeReturnUrl("https://evil.example") == "/" &&
          MaintenanceSupport.SafeReturnUrl("/intretinere?returnUrl=/x") == "/" && MaintenanceSupport.SafeReturnUrl(null) == "/",
        "The return address is always a local path, never another site or the waiting page itself");
}

// ---- Expiry notifications: rules that need no database ----
{
    var pureSource = new TestExpirySource("test.pure", []);
    var okTemplate = ExpiryTemplateRules.Validated(new NotificationTemplateInput { SourceKey = "test.pure", Subject = "  Expirare <eveniment> <obiect> ", Body = "La <data expirare> (<zile ramase> zile) pentru <obiect>.", ThresholdDays = 30 }, pureSource);
    Check(okTemplate.Subject == "Expirare <eveniment> <obiect>" && okTemplate.SourceKey == "test.pure", "A valid notification template is accepted and trimmed");
    void TemplateRejected(NotificationTemplateInput candidate, IExpirySource? source, string contains, string message)
    {
        try { ExpiryTemplateRules.Validated(candidate, source); throw new Exception(message + " (accepted)"); }
        catch (NotificationOperationException exception) { Check(exception.Message.Contains(contains, StringComparison.Ordinal), message); }
    }
    TemplateRejected(new() { SourceKey = "test.pure", Subject = "S <nu exista>", Body = "T", ThresholdDays = 5 }, pureSource, "<nu exista>", "An unknown placeholder in the subject is rejected and named");
    TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T <alt marcaj>", ThresholdDays = 5 }, pureSource, "<alt marcaj>", "An unknown placeholder in the text is rejected and named");
    TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T", ThresholdDays = 0 }, pureSource, "între 1 și", "A threshold of zero days is rejected");
    TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = "T", ThresholdDays = ExpiryTemplateRules.MaxThreshold + 1 }, pureSource, "între 1 și", "A threshold above the limit is rejected");
    TemplateRejected(new() { SourceKey = "test.pure", Subject = " ", Body = "T", ThresholdDays = 5 }, pureSource, "subiectul", "An empty subject is rejected");
    TemplateRejected(new() { SourceKey = "test.pure", Subject = "S", Body = new string('x', ExpiryTemplateRules.MaxBody + 1), ThresholdDays = 5 }, pureSource, "cel mult", "An oversize text is rejected");
    TemplateRejected(new() { SourceKey = "x", Subject = "S", Body = "T", ThresholdDays = 5 }, null, "categoria", "A template without a known source is rejected");
    var vehicleSource = new VehicleExpirySource(null!, VehicleExpiryKind.Rovinieta, ExpirySourceKeys.VehicleRovinieta, "Rovinietă");
    ExpiryTemplateRules.Validated(new NotificationTemplateInput { SourceKey = vehicleSource.Key, Subject = ExpiryTemplateRules.DefaultSubject, Body = ExpiryTemplateRules.DefaultBody, ThresholdDays = 30 }, vehicleSource);
    Check(true, "The default example subject and text only use placeholders of the vehicle sources");

    var renderToday = new DateOnly(2026, 9, 25);
    var instance = new ExpiryInstance(7, "TS-01-ABC · Dacia", new DateOnly(2026, 10, 15), new Dictionary<string, string> { ["obiect"] = "TS-01-ABC" });
    Check(ExpiryTemplateRules.Render("<eveniment> <obiect> <data expirare> <zile ramase> <necunoscut>", "ITP", instance, renderToday) == "ITP TS-01-ABC 15.10.2026 20 <necunoscut>",
        "Placeholders are replaced with the object values, the date as dd.mm.yyyy and the days left");
    Check(ExpiryTemplateRules.RenderSample("<obiect> <zile ramase>", pureSource) == "Obiect test 30", "The preview uses the sample value of each placeholder");
    Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 10, 15), renderToday) == 18, "The reminder limit is the days left minus two (20 - 2 = 18)");
    Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 27), renderToday) == 0 && ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 26), renderToday) == 0,
        "With two days or less left there is no room for a reminder");

    var open = new ExpiryNotification(1, 1, "test.pure", 7, new DateOnly(2026, 10, 15), DateTime.UtcNow, null, null, null, null, 0);
    Check(open.IsAlert(renderToday), "A notification nobody took over warns");
    var taken = open with { AcknowledgedBy = "ana", AcknowledgedUtc = DateTime.UtcNow };
    Check(!taken.IsAlert(renderToday), "A notification that was taken over stops warning");
    var snoozed = taken with { SnoozeUntil = renderToday.AddDays(3), SnoozeDays = 3 };
    Check(!snoozed.IsAlert(renderToday) && !snoozed.IsAlert(renderToday.AddDays(2)) && snoozed.IsAlert(renderToday.AddDays(3)) && snoozed.IsAlert(renderToday.AddDays(4)),
        "A reminder warns again exactly when its period ends");
    Check(ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 24), renderToday) == 30 && ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 1), renderToday) == 30 &&
          ExpiryTemplateRules.MaxSnoozeDays(new DateOnly(2026, 9, 25), renderToday) == 0,
        "An overdue notification may be postponed by up to 30 days (the days-left rule would be negative); on the due date itself there is no room");
    Check(ExpiryTemplateRules.SuggestedSnoozeDays(new DateOnly(2026, 9, 24), renderToday) == 7 && ExpiryTemplateRules.SuggestedSnoozeDays(new DateOnly(2026, 10, 15), renderToday) == 1,
        "The reminder field starts with 7 days for an overdue notification and with 1 otherwise");
    var overdueInstance = new ExpiryInstance(7, "TS-01-ABC", new DateOnly(2026, 9, 22), new Dictionary<string, string>());
    Check(ExpiryTemplateRules.Render("<zile ramase>|<zile depasire>", "ITP", overdueInstance, renderToday) == "0|3" &&
          ExpiryTemplateRules.Render("<zile ramase>|<zile depasire>", "ITP", instance, renderToday) == "20|0",
        "The days left never print below zero; the days overdue print the overdue days (0 before the date)");
    Check(ExpiryTemplateRules.AllPlaceholders(pureSource).Any(item => item.Name == ExpiryTemplateRules.DaysOverdueName), "The days-overdue placeholder is offered to every source");
    var resolvedRow = open with { ResolvedBy = "ana", ResolvedUtc = DateTime.UtcNow, ResolvedReason = "test" };
    Check(resolvedRow.IsResolved && !resolvedRow.IsAlert(renderToday) && !open.IsResolved, "A resolved notification never warns");
    Check(AuditActions.IsCreateOrEdit(AuditActions.ResolveNotification) && AuditActions.IsCreateOrEdit(AuditActions.AutoResolveNotification) && AuditActions.IsCreateOrEdit(AuditActions.ReopenNotification) &&
          AuditActions.ResolveNotification != AuditActions.AutoResolveNotification, "The resolution operations are named exactly and keep their link to the notifications page")
    ;
    Check(ExpiryTemplateRules.DuplicateActive("Sablon").Message.Contains("șablon activ") && ExpiryTemplateRules.DuplicateActive("Sablon").Message.Contains("Sablon"),
        "The refusal of a second active template names the existing one");

    Check(AuditActions.IsCreateOrEdit(AuditActions.CreateNotificationTemplate) && AuditActions.IsCreateOrEdit(AuditActions.EditNotificationTemplate) &&
          AuditActions.IsCreateOrEdit(AuditActions.AcknowledgeNotification) && AuditActions.IsCreateOrEdit(AuditActions.SnoozeNotification) && AuditActions.IsCreateOrEdit(AuditActions.NotificationCreated) &&
          !AuditActions.IsCreateOrEdit(AuditActions.DeleteNotificationTemplate), "The notification operations keep their link to the object, except the deletion");
    var snoozeEvent = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, "ana", AccessRoles.LimitedUser, AuditEntities.Notification, AuditActions.SnoozeNotification, "ITP · TS-01-ABC", "d", "", "5");
    var templateEvent = snoozeEvent with { EntityType = AuditEntities.NotificationTemplate, Action = AuditActions.CreateNotificationTemplate };
    Check(AuditNavigation.TargetUrl(snoozeEvent) == "/notificari" && AuditNavigation.TargetUrl(templateEvent) == "/setari?tab=notificari&subtab=templates&sablon=5", "Journal entries of notifications link to the notifications page and of templates to their Settings row");
    Check(AuditNavigation.TargetUrl(templateEvent with { EntityId = "7" }) == "/setari?tab=notificari&subtab=templates&sablon=7" &&
          AuditNavigation.TargetUrl(templateEvent with { EntityId = "7", Action = AuditActions.EditNotificationTemplate }) == "/setari?tab=notificari&subtab=templates&sablon=7",
        "A journal entry of a template links to that template in Settings, not to the ANAF tab");

    // The clean-up of old resolved notifications: period limits, limit date, texts, and when the daily run is due.
    Check(NotificationPurgeRules.IsValidMonths(1) && NotificationPurgeRules.IsValidMonths(60) && !NotificationPurgeRules.IsValidMonths(0) && !NotificationPurgeRules.IsValidMonths(61) &&
          NotificationPurgeRules.DefaultMonths == 12 && !NotificationSettings.Default.PurgeEnabled && NotificationSettings.Default.PurgeMonths == 12,
        "The clean-up period is 1 to 60 months, 12 by default, and the switch starts off");
    Check(NotificationPurgeRules.Cutoff(new DateOnly(2026, 9, 30), 12) == new DateOnly(2025, 9, 30) && NotificationPurgeRules.Cutoff(new DateOnly(2026, 3, 31), 1) == new DateOnly(2026, 2, 28),
        "The limit date is the given number of months before today (the last day of a shorter month is used)");
    Check(NotificationPurgeRules.RemovedText(3, new DateOnly(2025, 9, 30)) == "au fost eliminate din baza de date 3 notificări rezolvate mai vechi de 30.09.2025" &&
          NotificationPurgeRules.RemovedText(1, new DateOnly(2025, 9, 30)) == "a fost eliminată din baza de date 1 notificare rezolvată mai veche de 30.09.2025" &&
          NotificationPurgeRules.WillRemoveText(1, new DateOnly(2025, 9, 30)).StartsWith("va fi eliminată") &&
          NotificationPurgeRules.WillRemoveText(2, new DateOnly(2025, 9, 30)) == "vor fi eliminate din baza de date 2 notificări rezolvate mai vechi de 30.09.2025",
        "The journal and the confirmation say how many notifications go and the limit date, as dd.mm.yyyy");
    var purgeZone = TimeZoneInfo.Utc;
    var purgeNow = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
    var purgeOn = new NotificationSettings(true, 12, null, 0);
    Check(NotificationPurgeRules.IsDue(purgeOn, new DateOnly(2026, 9, 30), purgeZone) &&
          !NotificationPurgeRules.IsDue(purgeOn with { LastPurgeUtc = purgeNow }, new DateOnly(2026, 9, 30), purgeZone) &&
          NotificationPurgeRules.IsDue(purgeOn with { LastPurgeUtc = purgeNow }, new DateOnly(2026, 10, 1), purgeZone) &&
          !NotificationPurgeRules.IsDue(purgeOn with { PurgeEnabled = false }, new DateOnly(2026, 9, 30), purgeZone),
        "The daily clean-up is due when the switch is on and it has not run today");
    Check(NotificationPurgeRules.Changes(NotificationSettings.Default, true, 12).Single().Field == "Ștergerea notificărilor rezolvate" &&
          NotificationPurgeRules.Changes(NotificationSettings.Default, false, 12).Count() == 0 &&
          NotificationPurgeRules.Changes(purgeOn, true, 3).Single() is { Field: "Vechime (luni)", Before: "12", After: "3" },
        "The journal records the switch and the period with the old and new value, and nothing when nothing changed");
    Check(AuditActions.IsCreateOrEdit(AuditActions.EditNotificationSettings) && !AuditActions.IsCreateOrEdit(AuditActions.PurgeResolvedNotifications) &&
          AuditActions.EditNotificationSettings != AuditActions.PurgeResolvedNotifications,
        "The setting change and the clean-up are named exactly; the clean-up has no object to link to");
    Check(AuditNavigation.TargetUrl(snoozeEvent with { EntityType = AuditEntities.NotificationSettings, Action = AuditActions.EditNotificationSettings, EntityId = "1" }) == "/setari?tab=notificari&subtab=settings",
        "A journal entry of the clean-up setting links to the notification settings sub-tab");
}

// Extended work points: coordinates, journal names, photo rules, schema and archive registry.
{
    static string Coordinates(string text) => WorkPointCoordinates.TryParse(text, out var latitude, out var longitude, out _) ? $"{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "error";
    Check(Coordinates("45.7489, 21.2087") == "45.7489|21.2087" && Coordinates("45.7489 21.2087") == "45.7489|21.2087" && Coordinates("45,7489 21,2087") == "45.7489|21.2087" &&
          Coordinates("45,7489; 21,2087") == "45.7489|21.2087" && Coordinates("-33.8688,151.2093") == "-33.8688|151.2093" && Coordinates("45.12345678, 21.1") == "45.123457|21.1",
        "Coordinates are read as pasted from a map (dot or comma decimals, comma, space or semicolon between) and rounded to 6 decimals");
    Check(Coordinates("") == "error" && Coordinates("abc") == "error" && Coordinates("45.7") == "error" && Coordinates("45.7, 21.2, 3") == "error" && Coordinates("90.1, 10") == "error" &&
          Coordinates("-91, 10") == "error" && Coordinates("10, 180.5") == "error" && Coordinates("90, 180") == "90|180" && Coordinates("-90, -180") == "-90|-180",
        "Unreadable coordinates and values out of range (latitude -90..90, longitude -180..180) are refused");
    Check(WorkPointCoordinates.Format(45.7489m, 21.2087m) == "45.7489, 21.2087" && WorkPointCoordinates.Format(null, null) == "" && WorkPointCoordinates.Format(45.75m, null) == "",
        "Coordinates are shown as \"latitude, longitude\", empty when the point has none");
    var withCoordinates = new WorkPointInput { Name = "A", Address = "Str. X 1", UseCoordinates = true, CoordinatesText = "45,5 21,5" }.Validated();
    var withoutCoordinates = new WorkPointInput { Name = "A", Address = "Str. X 1", UseCoordinates = false, CoordinatesText = "45,5 21,5" }.Validated();
    Check(withCoordinates.Latitude == 45.5m && withCoordinates.Longitude == 21.5m && withoutCoordinates.Latitude is null && withoutCoordinates.Longitude is null && withoutCoordinates.CoordinatesText == "",
        "With the switch off the coordinates are dropped; with it on both values are kept");
    var pointBefore = new WorkPoint(5, 1, "Depozit", "Str. X 1", "0722333444", "Ion", 0, false, "text", 45.5m, 21.5m);
    Check(WorkPointRules.EditAction(pointBefore, pointBefore with { Description = "nou" }) == AuditActions.EditWorkPointDescription &&
          WorkPointRules.EditAction(pointBefore, pointBefore with { Latitude = 46m }) == AuditActions.EditWorkPointCoordinates &&
          WorkPointRules.EditAction(pointBefore, pointBefore with { Latitude = null, Longitude = null }) == AuditActions.EditWorkPointCoordinates &&
          WorkPointRules.EditAction(pointBefore, pointBefore with { Description = "nou", Latitude = 46m }) == AuditActions.EditWorkPoint &&
          WorkPointRules.EditAction(pointBefore, pointBefore with { Name = "Alt nume" }) == AuditActions.EditWorkPoint &&
          WorkPointRules.EditAction(pointBefore, pointBefore with { Name = "Alt nume", Description = "nou" }) == AuditActions.EditWorkPoint,
        "The journal names an edit of only the description, of only the coordinates, or any other edit exactly");
    Check(new[] { AuditActions.CreateWorkPoint, AuditActions.EditWorkPoint, AuditActions.EditWorkPointDescription, AuditActions.EditWorkPointCoordinates, AuditActions.AddWorkPointPhoto }.All(AuditActions.IsCreateOrEdit) &&
          new[] { AuditActions.CreateWorkPoint, AuditActions.EditWorkPoint, AuditActions.EditWorkPointDescription, AuditActions.EditWorkPointCoordinates, AuditActions.AddWorkPointPhoto }.Distinct().Count() == 5,
        "The work point operations are named apart and keep their link to the beneficiary page");
    byte[] Image(params byte[] head) => [.. head, .. new byte[40]];
    static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    Check(ServicePhotoRules.DetectContentType(Image(137, 80, 78, 71, 13, 10, 26, 10)) == "image/png" && ServicePhotoRules.DetectContentType(Image(255, 216, 255, 224)) == "image/jpeg" &&
          ServicePhotoRules.DetectContentType(Image(71, 73, 70, 56, 57, 97)) == "image/gif" &&
          ServicePhotoRules.DetectContentType([.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WEBP"u8.ToArray(), 0, 0]) == "image/webp",
        "Photos are recognised as PNG, JPEG, GIF or WebP by their bytes");
    Check(Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType("text"u8)) && Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType(ReadOnlySpan<byte>.Empty)) &&
          Throws<WorkPointOperationException>(() => ServicePhotoRules.DetectContentType(new byte[ServicePhotoRules.MaximumBytes + 1])),
        "A file that is not an image, an empty file and one above 10 MB are refused");
    Check(ServicePhotoRules.SafeOriginalName(@"C:\poze\Intrare.PNG") == "Intrare.PNG" && ServicePhotoRules.NormalizeCaption("  Intrare   principala ") == "Intrare principala" &&
          Throws<WorkPointOperationException>(() => ServicePhotoRules.NormalizeCaption(new string('x', 201))) &&
          ServicePhotoRules.NewStoredName("image/png").EndsWith(".png") && ServicePhotoRules.NewStoredName("image/png") != ServicePhotoRules.NewStoredName("image/png") &&
          ServicePhotoRules.Hash([1, 2, 3]).Length == 64,
        "Photo names are cleaned of path segments, captions are normalised and limited, stored names are generated");
    var migration7 = MariaSchemaMigrations.All.Single(m => m.Version == 7);
    Check(new[] { "description", "is_primary", "latitude", "longitude", "primary_beneficiary_id" }.All(column => migration7.ExpectedColumns.Contains(("beneficiary_work_points", column))) &&
          migration7.Statements.Any(sql => sql.Contains("uq_work_points_primary")) && migration7.Statements.Any(sql => sql.Contains("ck_work_points_coordinates")) &&
          migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `service_photos`") && sql.Contains("ck_service_photos_owner")) &&
          migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_work_points`")) && migration7.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_photos`")),
        "MariaDB migration 7 adds the work point columns, one main point per beneficiary, the coordinate check, the photos table and the two archive tables");
    Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.WorkPoint && schema.TableName == "archive_work_points" && schema.SupportsFiles) &&
          ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServicePhoto && schema.TableName == "archive_service_photos" && schema.SupportsFiles),
        "The work points and their photos are registered for archiving");
    var archivedPoint = ArchiveRequests.WorkPoint(pointBefore, "Beneficiar SRL", [new ServicePhoto(9, 5, null, "a.png", "x.png", "image/png", 10, new string('a', 64), "", "ana", DateTime.UtcNow)], "Motiv");
    Check(archivedPoint.Snapshot.EntityType == AuditEntities.WorkPoint && archivedPoint.Snapshot.Relations.Count == 1 && archivedPoint.Snapshot.Relations[0].RelationType == AuditEntities.ServicePhoto,
        "The archive request of a work point carries its photos as relations");
}

// Maintenance contracts (pure rules): the number/date field, the input validation, the derived due state, the exact journal
// operation of an edit, the registry entries and migration 8.
{
    static string Parsed(string text) => ServiceContractNumber.TryParse(text, out var number, out var date, out _) ? $"{number}|{date:yyyy-MM-dd}" : "error";
    static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    Check(Parsed("26/23.09.2025") == "26|2025-09-23" && Parsed("26 / 23.09.2025") == "26|2025-09-23" && Parsed("26 din 23.09.2025") == "26|2025-09-23" &&
          Parsed(" 26   DIN   3.9.2025 ") == "26|2025-09-03" && Parsed("ab-7/01.02.2026") == "AB-7|2026-02-01" && Parsed("12/A/23.09.2025") == "12/A|2025-09-23",
        "The contract number field accepts 26/23.09.2025, 26 / 23.09.2025 and 26 din 23.09.2025 (number in capitals, date read as zz.ll.aaaa)");
    Check(Parsed("26") == "error" && Parsed("26/32.13.2025") == "error" && Parsed("/23.09.2025") == "error" && Parsed("26/23-09-2025") == "error" &&
          Parsed("26/23.09.1999") == "error" && Parsed(new string('9', 31) + "/23.09.2025") == "error" && Parsed("") == "error" && Parsed("26din23.09.2025") == "error",
        "A number without a date, an impossible date, an empty number, a date before 2000 or a number over 30 characters is refused");
    Check(ServiceContractNumber.Format("26", new DateOnly(2025, 9, 23)) == "26/23.09.2025" &&
          new ServiceContract(1, 1, "26", new DateOnly(2025, 9, 23), 3, null, true, "", 0).Label == "26/23.09.2025",
        "The contract is shown as number/dd.MM.yyyy");

    static ServiceContractInput Contract(Action<ServiceContractInput>? change = null)
    {
        var value = new ServiceContractInput { NumberText = "26 din 23.09.2025", CycleMonths = 3, Points = [new() { WorkPointId = 5, NextDue = new DateOnly(2025, 10, 15) }] };
        change?.Invoke(value);
        return value;
    }
    var valid = Contract().Validated();
    Check(valid.Number == "26" && valid.Date == new DateOnly(2025, 9, 23) && valid.CycleMonths == 3 && valid.Points.Count == 1,
        "A valid contract input is normalized (number and date read from the field)");
    Check(Throws<ServiceContractOperationException>(() => Contract(value => value.CycleMonths = 0).Validated()) && Throws<ServiceContractOperationException>(() => Contract(value => value.CycleMonths = 13).Validated()) &&
          Contract(value => value.CycleMonths = 12).Validated().CycleMonths == 12 && Contract(value => value.CycleMonths = 1).Validated().CycleMonths == 1,
        "The cycle is 1 to 12 months");
    Check(Throws<ServiceContractOperationException>(() => Contract(value => value.ValidUntil = new DateOnly(2025, 9, 22)).Validated()) &&
          Contract(value => value.ValidUntil = new DateOnly(2025, 9, 23)).Validated().ValidUntil == new DateOnly(2025, 9, 23) &&
          Contract(value => value.ValidUntil = new DateOnly(2027, 1, 1)).Validated().ValidUntil == new DateOnly(2027, 1, 1) && Contract().Validated().ValidUntil is null,
        "The expiry date cannot precede the contract date; without it the contract has no term");
    Check(Throws<ServiceContractOperationException>(() => Contract(value => value.Points.Add(new() { WorkPointId = 5, NextDue = new DateOnly(2025, 11, 1) })).Validated()) &&
          Throws<ServiceContractOperationException>(() => Contract(value => value.Points[0].NextDue = null).Validated()) &&
          Throws<ServiceContractOperationException>(() => Contract(value => value.Points[0].CycleMonths = 13).Validated()) &&
          Throws<ServiceContractOperationException>(() => Contract(value => value.Notes = new string('x', 1001)).Validated()) &&
          Contract(value => value.Points[0] = new() { WorkPointId = 5, MoveFromOtherContract = true }).Validated().Points[0].MoveFromOtherContract &&
          Contract(value => value.Points.Clear()).Validated().Points.Count == 0,
        "A point appears once, needs its first due date (unless it is moved in) and a cycle of 1 to 12; notes are limited; a contract may have no point");

    var today = new DateOnly(2026, 9, 30);
    Check(ServiceDueRules.State(today.AddDays(-1), today) == ServiceDueState.Overdue && ServiceDueRules.State(today, today) == ServiceDueState.DueSoon &&
          ServiceDueRules.State(today.AddDays(30), today) == ServiceDueState.DueSoon && ServiceDueRules.State(today.AddDays(31), today) == ServiceDueState.OnTime &&
          ServiceDueRules.State(today.AddDays(10), today, 7) == ServiceDueState.OnTime && ServiceDueRules.State(today.AddDays(7), today, 7) == ServiceDueState.DueSoon,
        "The displayed state is overdue before today, soon within the threshold (30 days by default) and on time otherwise");
    var contractRecord = new ServiceContract(1, 1, "26", new DateOnly(2025, 9, 23), 3, new DateOnly(2026, 9, 29), true, "", 0);
    Check(ServiceDueRules.EffectiveCycle(null, 3) == 3 && ServiceDueRules.EffectiveCycle(6, 3) == 6 && ServiceDueRules.IsExpired(contractRecord, today) &&
          !ServiceDueRules.IsExpired(contractRecord with { ValidUntil = today }, today) && !ServiceDueRules.IsExpired(contractRecord with { ValidUntil = null }, today),
        "A point inherits the cycle of the contract unless it has its own; a contract is expired the day after its expiry date");
    var twoPoints = new ServiceContractDetails(contractRecord, [
        new(new(1, 1, 5, null, new DateOnly(2026, 1, 20), 0), "Sediu", "Str. A 1", true), new(new(2, 1, 6, 6, new DateOnly(2026, 1, 10), 0), "Depozit", "Str. B 2", false)]);
    Check(twoPoints.NextDue == new DateOnly(2026, 1, 10) && new ServiceContractDetails(contractRecord, []).NextDue is null &&
          twoPoints.EffectiveCycle(twoPoints.Points[0].Point) == 3 && twoPoints.EffectiveCycle(twoPoints.Points[1].Point) == 6,
        "The next due date of a contract is the earliest among its points");

    Check(ServiceContractRules.EditAction(contractRecord, contractRecord with { ValidUntil = new DateOnly(2027, 9, 29) }) == AuditActions.EditServiceContractExpiry &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { ValidUntil = null }) == AuditActions.EditServiceContractExpiry &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { CycleMonths = 6 }) == AuditActions.EditMaintenanceCycle &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { CycleMonths = 6, ValidUntil = null }) == AuditActions.EditServiceContract &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { Notes = "x" }) == AuditActions.EditServiceContract &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { Number = "27" }) == AuditActions.EditServiceContract &&
          ServiceContractRules.EditAction(contractRecord, contractRecord with { Number = "27", ValidUntil = null }) == AuditActions.EditServiceContract,
        "The journal names the exact edit: only the expiry date, only the cycle, or the contract in general");
    var contractActions = new[] { AuditActions.CreateServiceContract, AuditActions.EditServiceContract, AuditActions.EditServiceContractExpiry, AuditActions.ActivateServiceContract,
        AuditActions.DeactivateServiceContract, AuditActions.AddContractPoint, AuditActions.RemoveContractPoint, AuditActions.EditMaintenanceCycle, AuditActions.RescheduleMaintenance,
        AuditActions.MoveContractPoint };
    Check(contractActions.Distinct().Count() == 10 && contractActions.All(AuditActions.IsCreateOrEdit) && contractActions.All(action => action != AuditActions.Create && action != AuditActions.Edit),
        "Each maintenance contract operation has its own journal action, linked to the beneficiary page");
    Check(ServiceContractRules.Changes(contractRecord, contractRecord with { ValidUntil = new DateOnly(2027, 9, 29) }).Any(change => change.Field == "Expiră" && change.Before == "29.09.2026" && change.After == "29.09.2027") &&
          AuditDetails.Changes([.. ServiceContractRules.Changes(contractRecord, contractRecord with { ValidUntil = null })]) == "Expiră: 29.09.2026 → fără termen",
        "The journal details hold the old and the new value in dd.MM.yyyy");

    Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServiceContract && schema.TableName == "archive_service_contracts" && schema.SupportsRelations),
        "The maintenance contracts are registered for archiving");
    var archivedContract = ArchiveRequests.ServiceContract(twoPoints, "Beneficiar SRL", "Motiv");
    Check(archivedContract.Snapshot.EntityType == AuditEntities.ServiceContract && archivedContract.Snapshot.Relations.Count == 2 &&
          archivedContract.Snapshot.Relations.All(relation => relation.RelationType == ArchiveRequests.ServiceContractPointRelation) && archivedContract.Target.Contains("26/23.09.2025"),
        "The archive request of a contract carries its coverage as relations");

    var migration8 = MariaSchemaMigrations.All.Single(m => m.Version == 8);
    Check(new[] { "contract_number", "contract_date", "cycle_months", "valid_until", "is_active", "notes" }.All(column => migration8.ExpectedColumns.Contains(("service_contracts", column))) &&
          new[] { "contract_id", "work_point_id", "active_work_point_id", "cycle_months", "next_due" }.All(column => migration8.ExpectedColumns.Contains(("service_contract_points", column))) &&
          migration8.Statements.Any(sql => sql.Contains("uq_service_contracts_number") && sql.Contains("ck_service_contracts_cycle") && sql.Contains("ck_service_contracts_valid_until")) &&
          migration8.Statements.Any(sql => sql.Contains("uq_service_contract_points_active") && sql.Contains("uq_service_contract_points_pair") && sql.Contains("ON DELETE RESTRICT")) &&
          migration8.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_contracts`")),
        "MariaDB migration 8 adds the contracts, their coverage with the one-active-contract key and the archive table");
}

// Register of interventions (pure rules): the three ways to choose the next due date, the rule of the latest intervention, the input
// validation, the exact journal operations, the registry entry and migration 9.
{
    static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    var performed = new DateOnly(2026, 1, 18);
    var planned = new DateOnly(2026, 1, 15);
    var options = ServiceInterventionRules.Options(performed, planned, 3);
    Check(options.Count == 3 && options[0].Basis == ServiceNextDueBasis.FromPerformed && options[0].Date == new DateOnly(2026, 4, 18) && options[0].Enabled &&
          options[1].Basis == ServiceNextDueBasis.FromPlanned && options[1].Date == new DateOnly(2026, 4, 15) && options[1].Enabled &&
          options[2].Basis == ServiceNextDueBasis.Chosen && options[2].Date is null && options[2].Enabled,
        "The three variants: from the date performed, from the planned date, chosen by the operator");
    var late = ServiceInterventionRules.Options(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 15), 3);
    Check(late[0].Enabled && !late[1].Enabled && late[1].Reason is not null && late[2].Enabled &&
          !ServiceInterventionRules.Options(new DateOnly(2026, 4, 15), planned, 3)[1].Enabled,
        "The planned-date variant is disabled (with its reason) when planned date plus cycle is not after the date performed");
    Check(ServiceInterventionRules.Options(new DateOnly(2026, 11, 30), planned, 3)[0].Date == new DateOnly(2027, 2, 28) &&
          ServiceInterventionRules.Options(new DateOnly(2026, 8, 31), planned, 6)[0].Date == new DateOnly(2027, 2, 28),
        "A month-end date moves to the last day of a shorter month");
    Check(ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPerformed, performed, planned, 3, null, out var error) == new DateOnly(2026, 4, 18) && error is null &&
          ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPlanned, performed, planned, 3, null, out error) == new DateOnly(2026, 4, 15) &&
          ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, new DateOnly(2026, 6, 2), out error) == new DateOnly(2026, 6, 2) &&
          ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, null, out error) is null && error is not null &&
          ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.Chosen, performed, planned, 3, performed, out error) is null && error is not null &&
          ServiceInterventionRules.ResolveDue(ServiceNextDueBasis.FromPlanned, new DateOnly(2026, 6, 1), planned, 3, null, out error) is null && error is not null,
        "The chosen date must exist and be after the date performed; a planned-date result not after it is refused");
    Check(ServiceInterventionRules.PerformedOnError(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30)) is null &&
          ServiceInterventionRules.PerformedOnError(new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30)) is not null,
        "The date performed cannot be in the future");

    ServiceIntervention Row(int id, ServiceInterventionKind kind, int point, DateOnly on, ServiceNextDueBasis? basis) => new(id, kind, 1, point, kind == ServiceInterventionKind.Maintenance ? 7 : null,
        "Sediu", "Str. A 1", kind == ServiceInterventionKind.Maintenance ? "26/23.09.2025" : null, on, basis is null ? null : new DateOnly(2025, 12, 1), basis, basis is null ? null : on.AddMonths(3), "", "ana", DateTime.UtcNow, 0);
    var register = new[]
    {
        Row(1, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 1, 10), ServiceNextDueBasis.FromPerformed),
        Row(2, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 4, 12), ServiceNextDueBasis.FromPlanned),
        Row(3, ServiceInterventionKind.Maintenance, 5, new DateOnly(2026, 2, 1), null),
        Row(4, ServiceInterventionKind.OnDemand, 5, new DateOnly(2026, 8, 1), null),
        Row(5, ServiceInterventionKind.Maintenance, 6, new DateOnly(2026, 3, 1), ServiceNextDueBasis.Chosen)
    };
    Check(ServiceInterventionRules.LatestMoving(register, 5)?.Id == 2 && ServiceInterventionRules.LatestMoving(register, 6)?.Id == 5 && ServiceInterventionRules.LatestMoving(register, 9) is null &&
          !register[2].MovesDue && !register[3].MovesDue && register[1].MovesDue,
        "The latest due-moving maintenance intervention of a point is the one with the latest date performed (on-demand and non-moving ones do not count)");
    Check(ServiceInterventionRules.Moves(new DateOnly(2026, 4, 12), new DateOnly(2026, 4, 12)) && ServiceInterventionRules.Moves(new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 12)) &&
          !ServiceInterventionRules.Moves(new DateOnly(2026, 4, 11), new DateOnly(2026, 4, 12)) && ServiceInterventionRules.Moves(new DateOnly(2020, 1, 1), null),
        "A new maintenance intervention moves the due date unless a later one already exists");

    Check(ServiceInterventionRules.KindCode(ServiceInterventionKind.Maintenance) == 'M' && ServiceInterventionRules.KindCode(ServiceInterventionKind.OnDemand) == 'C' &&
          ServiceInterventionRules.ParseKind("M") == ServiceInterventionKind.Maintenance && ServiceInterventionRules.ParseKind("C") == ServiceInterventionKind.OnDemand &&
          ServiceInterventionRules.ParseBasis("E") == ServiceNextDueBasis.FromPerformed && ServiceInterventionRules.ParseBasis("P") == ServiceNextDueBasis.FromPlanned &&
          ServiceInterventionRules.ParseBasis("O") == ServiceNextDueBasis.Chosen && ServiceInterventionRules.ParseBasis(null) is null &&
          new[] { ServiceNextDueBasis.FromPerformed, ServiceNextDueBasis.FromPlanned, ServiceNextDueBasis.Chosen }.Select(ServiceInterventionRules.BasisCode).Distinct().Count() == 3,
        "The kind and the choice are stored as one-letter codes and read back");

    static ServiceInterventionInput Input(Action<ServiceInterventionInput>? change = null)
    {
        var value = new ServiceInterventionInput { WorkPointId = 5, PerformedOn = new DateOnly(2026, 1, 18), Notes = "  Filtre schimbate  " };
        change?.Invoke(value);
        return value;
    }
    Check(Input().Validated().Notes == "Filtre schimbate" && Input().Validated().Kind == ServiceInterventionKind.Maintenance &&
          Throws<ServiceInterventionOperationException>(() => Input(value => value.WorkPointId = 0).Validated()) &&
          Throws<ServiceInterventionOperationException>(() => Input(value => value.PerformedOn = null).Validated()) &&
          Throws<ServiceInterventionOperationException>(() => Input(value => value.PerformedOn = new DateOnly(1999, 1, 1)).Validated()) &&
          Throws<ServiceInterventionOperationException>(() => Input(value => value.Notes = new string('x', 2001)).Validated()) &&
          Input(value => value.Notes = new string('x', 2000)).Validated().Notes.Length == 2000,
        "A valid intervention input is normalized; the point and the date are required and the notes are limited to 2000 characters");
    var fromRow = ServiceInterventionInput.From(register[4]);
    Check(fromRow.Basis == ServiceNextDueBasis.Chosen && fromRow.ChosenDue == register[4].NextDueSet && ServiceInterventionInput.From(register[3]).Basis == ServiceNextDueBasis.FromPerformed &&
          ServiceInterventionInput.From(register[3]).ChosenDue is null,
        "The correction form starts from the recorded choice");

    var interventionActions = new[] { AuditActions.RecordMaintenance, AuditActions.EditMaintenanceIntervention, AuditActions.RecordOnDemand, AuditActions.EditOnDemandIntervention, AuditActions.AddInterventionPhoto };
    Check(interventionActions.Distinct().Count() == 5 && interventionActions.All(AuditActions.IsCreateOrEdit) && interventionActions.All(action => action != AuditActions.Create && action != AuditActions.Edit),
        "Each intervention operation has its own journal action, linked to the beneficiary page");
    Check(ServiceInterventionRules.Changes(register[0], register[0] with { PerformedOn = new DateOnly(2026, 1, 12) }).Any(change => change.Field == "Efectuată la" && change.Before == "10.01.2026" && change.After == "12.01.2026") &&
          ServiceInterventionRules.Target(register[3], "Beneficiar SRL").Contains("la cerere") && ServiceInterventionRules.Target(register[0], "Beneficiar SRL").Contains("de mentenanță") &&
          ServiceInterventionRules.Identification(register[0]).Contains("10.01.2026"),
        "The journal details hold the old and the new value in dd.MM.yyyy and the kind of the intervention");

    Check(ArchiveSchemaRegistry.All.Any(schema => schema.EntityType == AuditEntities.ServiceIntervention && schema.TableName == "archive_service_interventions" && schema.SupportsRelations && schema.SupportsFiles),
        "The interventions are registered for archiving (with their photos and files)");
    var archivedIntervention = ArchiveRequests.ServiceIntervention(register[0], "Beneficiar SRL",
        [new ServicePhoto(9, null, 1, "a.png", "x.png", "image/png", 10, new string('a', 64), "", "ana", DateTime.UtcNow)], "Motiv");
    Check(archivedIntervention.Snapshot.EntityType == AuditEntities.ServiceIntervention && archivedIntervention.Snapshot.Relations.Count == 1 &&
          archivedIntervention.Snapshot.Relations[0].RelationType == AuditEntities.ServicePhoto && archivedIntervention.Target.Contains("de mentenanță"),
        "The archive request of an intervention carries its photos as relations");

    var migration9 = MariaSchemaMigrations.All.Single(m => m.Version == 9);
    Check(new[] { "kind", "beneficiary_id", "work_point_id", "contract_id", "work_point_name", "contract_label", "performed_on", "planned_due", "next_due_basis", "next_due_set", "notes", "version" }
              .All(column => migration9.ExpectedColumns.Contains(("service_interventions", column))) &&
          migration9.ExpectedColumns.Contains(("archive_service_interventions", "next_due_set")) &&
          migration9.Statements.Any(sql => sql.Contains("ck_service_interventions_kind") && sql.Contains("ck_service_interventions_due") && sql.Contains("ck_service_interventions_next_due") && sql.Contains("ON DELETE RESTRICT")) &&
          migration9.Statements.Any(sql => sql.Contains("fk_service_photos_intervention")) &&
          migration9.Statements.Any(sql => sql.Contains("CREATE TABLE IF NOT EXISTS `archive_service_interventions`")),
        "MariaDB migration 9 adds the register with its constraints, the photo link and the archive table");
}

// Maintenance notification sources (pure): keys, category and event, placeholders, the texts rendered for an instance, the reasons
// written when a date changes, and the proposed default texts.
{
    var reader = new FakeMaintenanceReader(
        [new MaintenanceDueItem(7, 3, "Demo Puncte SRL", "Sediu central", "Strada Demo 10, Timișoara", "26/23.09.2025", new DateOnly(2026, 11, 5), new DateOnly(2026, 8, 1)),
         new MaintenanceDueItem(8, 3, "Demo Puncte SRL", "Depozit", "Str. Depozitului 5", "26/23.09.2025", new DateOnly(2026, 10, 20), null)],
        [new ContractExpiryItem(11, 3, "Demo Puncte SRL", "26/23.09.2025", new DateOnly(2025, 9, 23), new DateOnly(2027, 9, 22))]);
    var dueSource = new MaintenanceDueSource(reader);
    var expirySource = new ContractExpirySource(reader);
    Check(dueSource.Key == "mentenanta.scadenta" && expirySource.Key == "contract.expirare" && ExpirySourceKeys.MaintenanceDue == dueSource.Key && ExpirySourceKeys.ContractExpiry == expirySource.Key &&
          dueSource.Category == "Mentenanță" && expirySource.Category == "Mentenanță" && dueSource.EventName != expirySource.EventName,
        "The two maintenance sources have their stable keys and share the category \"Mentenanță\"");
    var dueInstances = await dueSource.GetInstancesAsync();
    Check(dueInstances.Count == 2 && dueInstances[0].ObjectId == 7 && dueInstances[0].Expiry == new DateOnly(2026, 11, 5) && dueInstances[0].Label == "Demo Puncte SRL · Sediu central" &&
          dueInstances[0].Url == "/beneficiari/3" && dueInstances[0].Values["data ultima interventie"] == "01.08.2026" && dueInstances[1].Values["data ultima interventie"] == MaintenanceDueSource.NoIntervention,
        "A due instance is the covered point, dated at its next due date, with the link of the beneficiary and the last maintenance intervention (or none)");
    var today = new DateOnly(2026, 10, 30);
    var subject = ExpiryTemplateRules.Render(dueSource.DefaultSubject, dueSource.EventName, dueInstances[0], today);
    var body = ExpiryTemplateRules.Render(dueSource.DefaultBody, dueSource.EventName, dueInstances[0], today);
    Check(subject == "Scadență mentenanță – Demo Puncte SRL, Sediu central" && body.Contains("Sediu central (Strada Demo 10, Timișoara)") && body.Contains("contract 26/23.09.2025") && body.Contains("05.11.2026") &&
          body.Contains("zile rămase: 6") && body.Contains("Ultima intervenție de mentenanță: 01.08.2026") && !body.Contains('<'),
        "The proposed text of the due source renders every placeholder");
    var expiryInstances = await expirySource.GetInstancesAsync();
    Check(expiryInstances.Count == 1 && expiryInstances[0].ObjectId == 11 && expiryInstances[0].Expiry == new DateOnly(2027, 9, 22) && expiryInstances[0].Label == "Demo Puncte SRL · Contract 26/23.09.2025" &&
          ExpiryTemplateRules.Render(expirySource.DefaultBody, expirySource.EventName, expiryInstances[0], new DateOnly(2027, 9, 12)) ==
              "Contractul de mentenanță 26/23.09.2025 al beneficiarului Demo Puncte SRL expiră la data de 22.09.2027 (zile rămase: 10; zile de depășire: 0).",
        "An expiry instance is the contract dated at its expiry date; the proposed text names the contract, the beneficiary and the date");
    Check(ExpiryTemplateRules.UnknownPlaceholders("<beneficiar> <punct de lucru> <adresa punct de lucru> <numar contract> <data ultima interventie> <data expirare> <zile ramase> <zile depasire>", dueSource).Count == 0 &&
          ExpiryTemplateRules.UnknownPlaceholders("<beneficiar> <numar contract> <data contract> <data expirare>", expirySource).Count == 0 &&
          ExpiryTemplateRules.UnknownPlaceholders("<punct de lucru>", expirySource).Count == 1 && ExpiryTemplateRules.UnknownPlaceholders("<numar autovehicul>", dueSource).Count == 1,
        "Each source accepts its own placeholders and refuses the ones of other sources");
    Check(dueSource.DateChangedReason(new DateOnly(2026, 1, 18), new DateOnly(2026, 5, 5), dueInstances[0]) == "Scadența intervenției de mentenanță s-a modificat de la 18.01.2026 la 05.05.2026 (ultima intervenție de mentenanță: 01.08.2026)." &&
          dueSource.DateChangedReason(new DateOnly(2026, 1, 18), new DateOnly(2026, 5, 5), dueInstances[1]) == "Scadența intervenției de mentenanță s-a modificat de la 18.01.2026 la 05.05.2026." &&
          expirySource.DateChangedReason(new DateOnly(2027, 9, 22), new DateOnly(2028, 9, 22), expiryInstances[0]) == "Data expirării contractului s-a modificat de la 22.09.2027 la 22.09.2028." &&
          ((IExpirySource)new TestExpirySource("t", [])).DateChangedReason(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), dueInstances[0]) == "Data expirării Eveniment test s-a modificat de la 01.01.2026 la 01.02.2026." &&
          dueSource.RemovedReason.Contains("contractul a fost dezactivat") && expirySource.RemovedReason.Contains("dezactivat"),
        "The reasons written when a date changes or the object leaves are specific to each source (the other sources keep the generic text)");
    Check(((IExpirySource)new TestExpirySource("t", [])).DefaultSubject == ExpiryTemplateRules.DefaultSubject && dueSource.DefaultSubject != ExpiryTemplateRules.DefaultSubject,
        "A source without its own proposed text keeps the vehicle one");
}

// Maintenance map (pure): the state of the contract term, the fill (due state) and the badge (contract state) that never share a colour,
// the markers and the rows without coordinates, the provider configuration and its fallbacks.
{
    var today = new DateOnly(2026, 9, 30);
    ServiceContract Contract(DateOnly? validUntil, bool active = true) => new(1, 1, "26", new DateOnly(2025, 9, 23), 3, validUntil, active, "", 0);
    Check(ServiceDueRules.ExpiryState(Contract(null), today) == ServiceExpiryState.NoTerm && ServiceDueRules.ExpiryState(Contract(today.AddDays(-1)), today) == ServiceExpiryState.Expired &&
          ServiceDueRules.ExpiryState(Contract(today), today) == ServiceExpiryState.ExpiresSoon && ServiceDueRules.ExpiryState(Contract(today.AddDays(30)), today) == ServiceExpiryState.ExpiresSoon &&
          ServiceDueRules.ExpiryState(Contract(today.AddDays(31)), today) == ServiceExpiryState.Valid && ServiceDueRules.ExpiryState(Contract(today.AddDays(31)), today, 45) == ServiceExpiryState.ExpiresSoon,
        "The contract term is expired before today, expiring within the threshold (30 days by default), valid after it, or without a term");
    Check(ServiceDueRules.ExpiryText(Contract(null), today) == "Fără termen" && ServiceDueRules.ExpiryText(Contract(today.AddDays(-1)), today) == "Expirat de o zi" &&
          ServiceDueRules.ExpiryText(Contract(today.AddDays(-12)), today) == "Expirat de 12 zile" && ServiceDueRules.ExpiryText(Contract(today), today) == "Expiră astăzi" &&
          ServiceDueRules.ExpiryText(Contract(today.AddDays(1)), today) == "Expiră mâine" && ServiceDueRules.ExpiryText(Contract(today.AddDays(9)), today) == "Expiră în 9 zile" &&
          ServiceDueRules.ExpiryText(Contract(new DateOnly(2027, 5, 1)), today) == "Valabil până la 01.05.2027",
        "The contract term is written out (\"Expirat de N zile\", \"Expiră în N zile\", \"Fără termen\")");

    ServiceDueRow Row(int id, DateOnly nextDue, ServiceContract contract, decimal? latitude = 45.75m, decimal? longitude = 21.22m) =>
        new(3, "Demo SRL", contract, new(new(id, 1, 5, null, nextDue, 0), "Sediu", "Str. A 1", false), latitude, longitude, new DateOnly(2026, 8, 1));
    var overdueExpired = Row(1, today.AddDays(-5), Contract(today.AddDays(-2)));
    var soonExpiring = Row(2, today.AddDays(10), Contract(today.AddDays(12)));
    var okNoTerm = Row(3, today.AddDays(90), Contract(null));
    var off = Row(4, today.AddDays(-50), Contract(today.AddDays(-9), active: false));
    Check(MaintenanceMapRules.Fill(overdueExpired, today, 30) == "overdue" && MaintenanceMapRules.Fill(soonExpiring, today, 30) == "soon" && MaintenanceMapRules.Fill(okNoTerm, today, 30) == "ok" &&
          MaintenanceMapRules.Fill(off, today, 30) == "off" && MaintenanceMapRules.Fill(soonExpiring, today, 5) == "ok",
        "The fill of a marker shows the due state only (grey for a contract switched Off, whatever its dates); the threshold is the one given");
    Check(MaintenanceMapRules.Badge(overdueExpired, today, 30) == "expired" && MaintenanceMapRules.Badge(soonExpiring, today, 30) == "soon" && MaintenanceMapRules.Badge(okNoTerm, today, 30) == "" &&
          MaintenanceMapRules.Badge(off, today, 30) == "" && MaintenanceMapRules.Badge(soonExpiring, today, 5) == "",
        "The badge shows the contract term only: expired, expiring soon, or nothing (also nothing for a contract Off)");
    Check(MaintenanceMapRules.Fill(overdueExpired, today, 30) != MaintenanceMapRules.Badge(overdueExpired, today, 30) && MaintenanceMapRules.Fill(overdueExpired, today, 30) == "overdue" && MaintenanceMapRules.Badge(overdueExpired, today, 30) == "expired",
        "An overdue point of an expired contract shows both states, each in its own place");
    var marker = MaintenanceMapRules.Marker(overdueExpired, today, 30, 30);
    Check(marker is { Id: 1, Lat: 45.75, Lng: 21.22, Fill: "overdue", Contract: "expired", Title: "Demo SRL · Sediu" } && MaintenanceMapRules.Marker(Row(5, today, Contract(null), null, null), today, 30, 30) is null &&
          MaintenanceMapRules.Marker(Row(6, today, Contract(null), 45.75m, null), today, 30, 30) is null && !Row(5, today, Contract(null), null, null).HasCoordinates && overdueExpired.HasCoordinates,
        "A row with both coordinates becomes a marker with plain fields; one without them (or with only one) gets none and is listed apart");
    Check(MaintenanceMapRules.CoordinatesText(overdueExpired) == "45.75, 21.22" && MaintenanceMapRules.CoordinatesText(Row(5, today, Contract(null), null, null)) == "" &&
          MaintenanceMapRules.AddInterventionUrl(3, 17) == "/beneficiari/3?adauga-interventie=17",
        "The panel shows the coordinates and links to the form of a new intervention on the point");
    var sameBuilding = new[] { overdueExpired, Row(7, today.AddDays(3), Contract(null)), Row(8, today.AddDays(3), Contract(null), 46m, 22m), Row(9, today.AddDays(3), Contract(null), null, null) };
    Check(MaintenanceMapRules.SameLocation(sameBuilding, overdueExpired).Select(row => row.Point.Point.Id).SequenceEqual([7]) && MaintenanceMapRules.SameLocation(sameBuilding, sameBuilding[3]).Count == 0,
        "The other points at the same coordinates are listed with the selected one");

    var defaults = new MapOptions().Normalized();
    Check(defaults.TileUrl == MapOptions.DefaultTileUrl && defaults.MinZoom == 3 && defaults.MaxZoom == 19 && defaults.Attribution.Contains("OpenStreetMap") &&
          new MapOptions { TileUrl = "http://tiles.example/{z}/{x}/{y}.png" }.Normalized().TileUrl == MapOptions.DefaultTileUrl &&
          new MapOptions { TileUrl = "https://tiles.example/{z}/{x}.png" }.Normalized().TileUrl == MapOptions.DefaultTileUrl &&
          new MapOptions { TileUrl = "https://tiles.example/{z}/{x}/{y}.png" }.Normalized().TileUrl == "https://tiles.example/{z}/{x}/{y}.png" &&
          new MapOptions { TileUrl = "https://{s}.tiles.example/{z}/{x}/{y}{r}.png" }.Normalized().TileUrl == "https://{s}.tiles.example/{z}/{x}/{y}{r}.png" &&
          new MapOptions { Attribution = " " }.Normalized().Attribution == MapOptions.DefaultAttribution &&
          new MapOptions { MinZoom = 25, MaxZoom = 2 }.Normalized() is { MinZoom: 18, MaxZoom: 18 } && new MapOptions { CenterLatitude = 200 }.Normalized().CenterLatitude == 90,
        "The tile provider comes from the configuration (only https with {z}/{x}/{y}); anything unusable falls back to the default, the zoom range is kept valid");
}

await ComponentChecks.RunAsync(Check);
await PickupWizardChecks.RunAsync(Check);
await PickupWizardChecks.TemplateFlowAsync(Check);
await ProductGroupsChecks.RunAsync(Check);
ReasonSummaryChecks.Run(Check);
await InvoiceChecks.RunAsync(Check);

// Subtask 2.11: opt-in real integration checks against the isolated blazorstoc_test MariaDB database. Skipped
// entirely (no-op, prints nothing extra) unless RUN_MARIA_INTEGRATION_CHECKS=1, so the default dotnet run/CI
// experience (the checks above, no network, no MariaDB needed) is unchanged.
if (Environment.GetEnvironmentVariable("RUN_MARIA_INTEGRATION_CHECKS") == "1")
{
    var mariaConfigPath = Environment.GetEnvironmentVariable("MARIA_TEST_CONFIG_PATH")
        ?? throw new InvalidOperationException("Set MARIA_TEST_CONFIG_PATH to the test database's private config JSON.");
    var mariaConfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddJsonFile(mariaConfigPath).Build();
    await BlazorStoc.Checks.MariaIntegrationChecks.RunAsync(mariaConfiguration);
    await BlazorStoc.Checks.MariaExtendedChecks.RunAsync(mariaConfiguration);
}

// Reports synchronously in the reporter's own execution context (Progress<T> would post to the thread pool).
sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

static class Assert
{
    // Runs the action and returns the expected exception (null when none, or a different one, was raised).
    public static async Task<TException?> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); return null; }
        catch (TException exception) { return exception; }
        catch (Exception) { return null; }
    }
}

sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null) Console.WriteLine(exception);
    }
}

sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan span) => now += span;
}

sealed class FakeChangeSource : IChangeEventSource
{
    private readonly List<StoredChange> events = [];
    public bool FailNextRead { get; set; }
    public void Add(string type, string action, string id, int? projectId = null) =>
        events.Add(new(events.Count + 1, type, action, id, projectId, null, null, DateTime.UtcNow));
    public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<long> LatestIdAsync(CancellationToken cancellationToken) => Task.FromResult<long>(events.Count);
    public Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        if (FailNextRead) { FailNextRead = false; throw new InvalidOperationException("Source unavailable"); }
        return Task.FromResult<IReadOnlyList<StoredChange>>(events.Where(change => change.Id > afterId).Take(limit).ToList());
    }
    public Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken) => Task.CompletedTask;
}

sealed class TestAccessControl(bool administrator, string username, bool productOperator = true) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(administrator);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(productOperator);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => administrator
        ? Task.CompletedTask
        : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => productOperator ? Task.CompletedTask : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class FakeInventoryProductRepository(IReadOnlyList<Product> products, IReadOnlyList<ProductGroup> groups) : IProductRepository
{
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(products);
    public Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(products.FirstOrDefault(product => product.Id == id));
    public Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult(groups);
    public Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameCategoryAsync(string originalCategory, string newCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory, string targetCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

sealed class FakeInventoryStockMovementRepository(IReadOnlyDictionary<int, int> inVehicles) : IStockMovementRepository
{
    public Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default) => Task.FromResult(inVehicles);
    public Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// Records every movement CreateAsync receives (Task 1's inventory pickup applier checks), and can be told to fail
// for specific products to exercise partial-failure reporting without touching a real database.
sealed class FakeInventoryPickupMovementRepository(IReadOnlyDictionary<int, int> inVehicles, ISet<int>? failProductIds = null) : IStockMovementRepository
{
    public List<(int ProductId, StockMovementInput Input)> Created { get; } = [];
    public Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default) => Task.FromResult(inVehicles);
    public Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        if (failProductIds?.Contains(productId) == true) throw new StockMovementOperationException("Esec de test");
        Created.Add((productId, input));
        var movement = new StockMovement(Created.Count, productId, input.Kind, input.Quantity ?? 0,
            input.Date ?? DateOnly.FromDateTime(DateTime.Now), input.Description, null, null, null, null,
            "test", 1, DateTime.UtcNow, DateTime.UtcNow, false, input.Destination);
        return Task.FromResult(new StockMovementResult(movement, 0));
    }
    public Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

sealed class TestAuditTrail : IAuditTrail
{
    public List<AuditWrite> Entries { get; } = [];
    public Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
    public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => Task.FromResult(AuditQueryRules.Page([], query, page, pageSize, window));
    public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => Task.FromResult(AuditQueryRules.Summary([], todayStartUtc));
    public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, DateTime>>(new Dictionary<string, DateTime>());
}

sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BlazorStoc.Checks";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class TestExpirySource(string key, List<ExpiryInstance> instances) : IExpirySource
{
    public bool Fail { get; set; }
    public List<ExpiryInstance> Instances { get; } = instances;
    public string Key => key;
    public string Category => "Categorie test";
    public string EventName => "Eveniment test";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } = [new("obiect", "Denumirea obiectului", "Obiect test")];
    public Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Fail ? Task.FromException<IReadOnlyList<ExpiryInstance>>(new InvalidOperationException("source unavailable")) : Task.FromResult<IReadOnlyList<ExpiryInstance>>(Instances.ToArray());
}

sealed class FakeMaintenanceReader(IReadOnlyList<MaintenanceDueItem> due, IReadOnlyList<ContractExpiryItem> expiries) : IMaintenanceNotificationReader
{
    public Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default) => Task.FromResult(due);
    public Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(expiries);
}

static class MapPinTestExtensions
{
    public static MapPinType With(this MapPinType type, Action<MapPinType> change) { var copy = type.Clone(); change(copy); return copy; }
}
