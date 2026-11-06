using BlazorStoc.Modules;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Preluare ActivityApp, stage 4: synchronization (by hand and scheduled), backoff, alerts, what was deleted in ActivityApp (only signalled), the worker ----------------
    private static async Task ActivitySyncAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "blazorstoc-ai4-keys-" + Guid.NewGuid().ToString("N"))), options => options.SetApplicationName("BlazorStoc"));
        var fake = new FakeActivitySource
        {
            Columns = ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name,
                column.Kind switch { ColumnKind.Number => "bigint", ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", _ => "bit" }))).ToList()
        };
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var recorder = new OnDemandInterventionRecorder(new MariaServiceInterventionRepository(configuration, admin, audit));
        var settings = new MariaActivityImportService(configuration, protection, fake, admin, audit);
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-2);
        var cui = Random.Shared.Next(90000000, 99999999).ToString();
        var anaf = new FakeAnafLookup(new Dictionary<string, CompanyLookupResult> { [cui] = new("Str. Florilor 5, Timisoara", "0722111222") });
        var partners = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, admin, audit, anaf);
        var tickets = new MariaActivityTicketImportService(configuration, settings, fake, workPoints, admin, audit, partners);
        var sync = new MariaActivitySyncService(configuration, settings, fake, partners, tickets, admin, audit);
        async Task<long> Count(string sql) => await Scalar<long>(probe, sql);
        async Task<long> Events(string action) => (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 300)).Events.Count(entry => entry.TimestampUtc >= started && entry.Action == action);
        async Task MakeDue() => await Exec(probe, "UPDATE activity_import_settings SET last_sync_utc = UTC_TIMESTAMP() - INTERVAL 10 HOUR WHERE id = 1");
        async Task Save(bool sync2 = true, bool enabled = true) =>
            await settings.SaveSettingsAsync(new ActivityImportInput(enabled, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, new DateOnly(2026, 1, 1), 0, 24, 200, false, sync2, 60));
        try
        {
            foreach (var table in new[] { "activity_import_map", "activity_import_issues", "activity_import_runs", "activity_import_types", "activity_import_settings", "activity_import_partner_checks", "activity_import_tickets", "activity_import_suggestions", "activity_import_keywords" }) await Exec(probe, "DELETE FROM " + table);
            fake.Partners = [new(7301, $"Ext Sync {suffix} SRL", "RO" + cui, "Str. Florilor 5", "Timisoara", "Timis", "0722111222", "")];
            fake.Points = [new(7401, 7301, "Str. Florilor 5", "Timisoara", "Timis", "", "Sediu", "", true, null)];
            fake.Types = [new(4, "Service", "S", true, false, 1, 0, 0)];
            fake.TicketRows = [new(7501, 4, 7301, 0, new DateTime(2026, 3, 1), "T-7501", "Internet", "Nu merge", "Ion", 8501, new DateTime(2026, 3, 2), 1, "Nu merge\nRouter nou")];
            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", "parola-" + suffix, new DateOnly(2026, 1, 1), 0, 24, 200, false, true, 60));
            await settings.SaveTypesAsync([new(4, "Service", true, ActivityDestination.OnDemand)]);
            await new MariaActivityKeywordService(configuration, admin, audit).AddAsync("router");
            var saved = await settings.GetSettingsAsync();
            Check(saved.SyncEnabled && saved.SyncIntervalMinutes == 60 && saved.LastSyncUtc is null, "Activity sync: the synchronization setting and its interval are saved");
            var badInterval = 0;
            try { await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, null, 0, 24, 200, false, true, 2)); } catch (ActivityImportException) { badInterval++; }
            Check(badInterval == 1, "Activity sync: an interval under 5 minutes is refused");

            var result = await sync.RunNowAsync();
            var after = await settings.GetSettingsAsync();
            Check(result is { Ok: true, PartnersCreated: 1, PointsCreated: 0, TicketsCreated: 1, NewlyDeleted: 0 } && result.Message.Contains("Beneficiari: 1 noi", StringComparison.Ordinal) && result.Message.Contains("tichete: 1 puse la validare", StringComparison.Ordinal),
                "Activity sync: a synchronization applies the partners and the tickets (the main point is the one created with the beneficiary)");
            Check(after.LastSyncOk == true && after.SyncFailures == 0 && after.LastSyncUtc is not null && after.LastSyncMessage == result.Message && await Events(AuditActions.SyncActivityImport) == 1, "Activity sync: the result is remembered and the run is in the journal");
            var beneficiary = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Sync {suffix} SRL");

            Check(await sync.RunScheduledAsync(DateTime.Now) is null, "Activity sync: right after a synchronization none is due");
            await MakeDue();
            var quiet = await sync.RunScheduledAsync(DateTime.Now);
            Check(quiet is { Ok: true, PartnersCreated: 0, TicketsCreated: 0, NewlyDeleted: 0 } && await Events(AuditActions.SyncActivityImport) == 1, "Activity sync: a scheduled run with nothing new is made but leaves no line in the journal");
            await Save(sync2: false);
            await MakeDue();
            Check(await sync.RunScheduledAsync(DateTime.Now) is null, "Activity sync: with the synchronization off nothing runs on schedule");
            await Save();
            await Exec(probe, "UPDATE activity_import_settings SET window_from_hour = 3, window_to_hour = 4 WHERE id = 1");
            await MakeDue();
            Check(await sync.RunScheduledAsync(new DateTime(2026, 10, 23, 12, 0, 0)) is null && await sync.RunScheduledAsync(new DateTime(2026, 10, 23, 3, 30, 0)) is { Ok: true }, "Activity sync: outside the time window nothing runs; inside it the due run is made");
            await Exec(probe, "UPDATE activity_import_settings SET window_from_hour = 0, window_to_hour = 24 WHERE id = 1");

            // Deleted in ActivityApp: only signalled.
            fake.Live[LiveKinds.Partners] = [];
            await MakeDue();
            var deleted = await sync.RunScheduledAsync(DateTime.Now);
            Check(deleted is { NewlyDeleted: 1 } && await Count($"SELECT COUNT(*) FROM activity_import_map WHERE entity='partner' AND source_id=7301 AND source_deleted=1") == 1
                && await Count("SELECT COUNT(*) FROM activity_import_issues WHERE entity='partner-deleted' AND resolved=0") == 1 && await Events(AuditActions.SyncActivityImport) == 2,
                "Activity sync: a partner deleted in ActivityApp is marked and listed as an open problem, and the run is journaled");
            Check((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == beneficiary.Id) == beneficiary && (await workPoints.GetAsync(beneficiary.Id)).Count == 1, "Activity sync: what was deleted in ActivityApp stays unchanged in BlazorStoc (only signalled)");
            await MakeDue();
            var again = await sync.RunScheduledAsync(DateTime.Now);
            Check(again is { NewlyDeleted: 0 } && await Count("SELECT COUNT(*) FROM activity_import_issues WHERE entity='partner-deleted' AND resolved=0") == 1, "Activity sync: the next run does not list the same deletion twice");
            var issue = (await partners.GetOpenIssuesAsync()).Single(item => item.Entity == "partner-deleted");
            await partners.ResolveIssueAsync(issue.Id);
            await MakeDue();
            await sync.RunScheduledAsync(DateTime.Now);
            Check(await Count("SELECT COUNT(*) FROM activity_import_issues WHERE entity='partner-deleted' AND resolved=0") == 0 && await Count("SELECT COUNT(*) FROM activity_import_issues WHERE entity='partner-deleted' AND resolved=1") == 1,
                "Activity sync: a deletion the administrator marked resolved is not listed again");
            // The ticket waits in the queue until an operator takes it (nothing is recorded by the synchronization); a taken ticket is the one whose deletion is signalled.
            var queueReview = new MariaActivityReviewService(configuration, admin, audit, recorder);
            foreach (var waiting in (await queueReview.GetPendingAsync()).Where(item => item.TicketId == 7501)) await queueReview.DecideAsync(waiting.Id, ReviewDecision.OnDemand);
            fake.Live.Remove(LiveKinds.Partners);
            fake.Live[LiveKinds.Tickets] = [];
            await MakeDue();
            var ticketGone = await sync.RunScheduledAsync(DateTime.Now);
            Check(await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity='partner' AND source_deleted=1") == 0 && ticketGone is { NewlyDeleted: 1 } && await Count("SELECT COUNT(*) FROM activity_import_issues WHERE entity='ticket-deleted' AND resolved=0") == 1
                && (await new MariaServiceInterventionRepository(configuration, admin, audit).GetForBeneficiaryAsync(beneficiary.Id)).Count == 1, "Activity sync: a partner that exists again is unmarked; a deleted ticket is signalled and its intervention stays");
            fake.Live.Remove(LiveKinds.Tickets);

            // Doubt about the source: almost everything "deleted" at once is not marked.
            for (var index = 0; index < 25; index++) await Exec(probe, $"INSERT INTO activity_import_map (entity, source_id, local_id, origin, content_hash, imported_utc) VALUES ('workpoint', {90000 + index}, 1, 'linked', '', UTC_TIMESTAMP())");
            fake.Live[LiveKinds.WorkPoints] = [];
            await MakeDue();
            var doubt = await sync.RunScheduledAsync(DateTime.Now);
            Check(doubt is { Ok: true, NewlyDeleted: 0 } && doubt.Message.Contains("verificare omisă", StringComparison.Ordinal) && await Count("SELECT COUNT(*) FROM activity_import_map WHERE source_deleted=1") == 0,
                "Activity sync: when ActivityApp seems to have lost all the imported rows of a kind (25 at once) they are not marked, and the run says so");
            fake.Live.Remove(LiveKinds.WorkPoints);
            await Exec(probe, "DELETE FROM activity_import_map WHERE source_id >= 90000");

            // Failing source: the failures count, the wait grows, the alert comes after three and goes away with the recovery.
            fake.Failure = new ActivitySourceException("ActivityApp nu a răspuns la timp.");
            var failedManually = 0;
            try { await sync.RunNowAsync(); } catch (ActivityImportException) { failedManually++; }
            var first = await settings.GetSettingsAsync();
            Check(failedManually == 1 && first.LastSyncOk == false && first.SyncFailures == 1 && first.LastSyncMessage.Contains("nu a răspuns", StringComparison.Ordinal), "Activity sync: a failing source ends the manual run with its message and the failure is counted");
            Check(!ActivitySyncSchedule.Due(first, DateTime.Now, DateTime.UtcNow), "Activity sync: after a failure the next scheduled run waits");
            Check((await new MariaActivityImportAlertReader(configuration).GetAsync()).FailingSince is null, "Activity sync: one failure is not yet a notification");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await MakeDue();
                var failed = await sync.RunScheduledAsync(DateTime.Now);
                Check(failed is { Ok: false }, "Activity sync: a scheduled run against a failing source does not throw, it reports the failure");
            }
            var third = await settings.GetSettingsAsync();
            var alertState = await new MariaActivityImportAlertReader(configuration).GetAsync();
            Check(third.SyncFailures == 3 && alertState.FailingSince is not null && ActivityImportAlertSource.Instances(alertState).Any(item => item.ObjectId == ActivityImportAlertSource.FailingId && item.Values[ActivityImportAlertSource.DetailsName].Contains("nu a răspuns", StringComparison.Ordinal)),
                "Activity sync: after three failures in a row the administrator's notification appears, with the cause");
            var failureLines = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 300)).Events.Count(entry => entry.TimestampUtc >= started && entry.Action == AuditActions.SyncActivityImport && entry.Details.Contains("eșuată", StringComparison.Ordinal));
            Check(failureLines == 1, "Activity sync: the journal gets the failure when it starts (here the manual run), not every repeated failure after it");
            fake.Failure = null;
            await MakeDue();
            var recovered = await sync.RunScheduledAsync(DateTime.Now);
            var healthy = await settings.GetSettingsAsync();
            Check(recovered is { Ok: true } && healthy.SyncFailures == 0 && (await new MariaActivityImportAlertReader(configuration).GetAsync()).FailingSince is null, "Activity sync: when the source answers again the failures and the notification go away");

            // Structure changed: the synchronization stops, the notification says why.
            var goodColumns = fake.Columns;
            fake.Columns = [.. goodColumns.Where(column => column is not { Table: "NomenclatorParteneri", Column: "CodFiscal" })];
            await MakeDue();
            var stopped = await sync.RunScheduledAsync(DateTime.Now);
            var structureAlert = await new MariaActivityImportAlertReader(configuration).GetAsync();
            Check(stopped is { Ok: false } && structureAlert.StructureSince is not null && structureAlert.StructureIssues == 1 && ActivityImportAlertSource.Instances(structureAlert).Any(item => item.ObjectId == ActivityImportAlertSource.StructureId),
                "Activity sync: a change of the ActivityApp structure stops the synchronization and raises the notification with the number of missing columns");
            fake.Columns = goodColumns;
            await MakeDue();
            await sync.RunScheduledAsync(DateTime.Now);
            Check((await new MariaActivityImportAlertReader(configuration).GetAsync()).StructureSince is null, "Activity sync: the structure notification goes away when the structure is as expected again");

            // The open problems and the new types raise theirs.
            fake.Types = [.. fake.Types, new(13, "Mentenanță", "M", true, false, 0, 0, 0)];
            await Exec(probe, "INSERT INTO activity_import_issues (run_id, entity, source_id, source_name, message, created_utc) VALUES (0, 'probe', 1, 'x', 'x', UTC_TIMESTAMP())");
            await MakeDue();
            await sync.RunScheduledAsync(DateTime.Now);
            var open = await new MariaActivityImportAlertReader(configuration).GetAsync();
            Check(open.IssuesSince is not null && open.OpenIssues >= 1 && open.TypesSince is not null, "Activity sync: open problems and a new ticket type each raise their notification");
            await Exec(probe, "DELETE FROM activity_import_issues WHERE source_name = 'x'");

            // The worker: the real graph of services acting as the system.
            fake.Partners = [.. fake.Partners, new(7302, $"Ext Sync Worker {suffix} SRL", "RO" + (long.Parse(cui) + 1), "Str. Noua 2", "Arad", "Arad", "0733111222", "")];
            fake.TicketRows = [.. fake.TicketRows, new(7502, 4, 7302, 0, new DateTime(2026, 5, 1), "T-7502", "Internet", "Nu merge", "Ion", 8502, new DateTime(2026, 5, 2), 1, "Nu merge\nRouter nou")];
            var registry = new ModuleRegistry();
            registry.Add(new ActivityImportModule(), new ServiceCollection(), new ConfigurationBuilder().Build());
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton(audit);
            services.AddSingleton<IActivitySource>(fake);
            services.AddSingleton(protection);
            services.AddSingleton(registry);
            services.AddScoped<IOnDemandInterventionRecorderFactory>(provider => new OnDemandInterventionRecorderFactory(configuration, audit, registry));
            await using var provider = services.BuildServiceProvider();
            var worker = new ActivitySyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), registry, NullLogger<ActivitySyncWorker>.Instance);
            await MakeDue();
            var worked = await worker.CheckAsync(CancellationToken.None);
            Check(worked is { Ok: true, PartnersCreated: 1 } && (await beneficiaries.GetBeneficiariesAsync()).Any(item => item.Name == $"Ext Sync Worker {suffix} SRL"), "Activity sync: the worker runs a due synchronization with the real services");
            var systemEvents = (await audit.QueryAsync(new AuditQuery(Text: $"Ext Sync Worker {suffix}", Entity: AuditEntities.Beneficiary), 1, 20)).Events;
            Check(systemEvents.Any(entry => entry.ActorUsername == "sistem") && await Events(AuditActions.SyncActivityImport) >= 5, "Activity sync: what the worker creates is in the journal under \"sistem\", and so is its synchronization");
            Check(await worker.CheckAsync(CancellationToken.None) is null, "Activity sync: the worker does nothing when no synchronization is due");
            var outRegistry = new ModuleRegistry();
            outRegistry.Add(new ActivityImportModule(), new ServiceCollection(), new ConfigurationBuilder().Build());
            for (var index = 0; index < ModuleRegistry.FailureThreshold; index++) outRegistry.ReportFailure(ActivityImportModule.ModuleId, new InvalidOperationException("x"));
            await MakeDue();
            var pausedWorker = new ActivitySyncWorker(provider.GetRequiredService<IServiceScopeFactory>(), outRegistry, NullLogger<ActivitySyncWorker>.Instance);
            var lastBefore = (await settings.GetSettingsAsync()).LastSyncUtc;
            Check(await pausedWorker.CheckAsync(CancellationToken.None) is null && (await settings.GetSettingsAsync()).LastSyncUtc == lastBefore, "Activity sync: while the module is out the worker waits and runs nothing");
            fake.Failure = new InvalidOperationException("unexpected");
            var unexpected = await worker.CheckAsync(CancellationToken.None);
            fake.Failure = null;
            Check(unexpected is { Ok: false } && (await settings.GetSettingsAsync()).LastSyncOk == false, "Activity sync: an unexpected error in a scheduled run does not escape the worker; it is remembered as a failure");
            fake.Failure = null;
        }
        finally
        {
            foreach (var table in new[] { "activity_import_map", "activity_import_issues", "activity_import_runs", "activity_import_types", "activity_import_settings", "activity_import_partner_checks", "activity_import_tickets", "activity_import_suggestions", "activity_import_keywords" }) await Exec(probe, "DELETE FROM " + table);
        }
    }
}
