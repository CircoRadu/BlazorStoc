using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Preluare ActivityApp, stage 3: resolved tickets -> on-demand interventions; types, date limit, ties, problems, source detail, journal -------------------
    private static async Task ActivityTicketsAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "blazorstoc-ai3-keys-" + Guid.NewGuid().ToString("N"))), options => options.SetApplicationName("BlazorStoc"));
        var fake = new FakeActivitySource
        {
            Columns = ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name,
                column.Kind switch { ColumnKind.Number => "bigint", ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", _ => "bit" }))).ToList()
        };
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var interventions = new MariaServiceInterventionRepository(configuration, admin, audit);
        var recorder = new OnDemandInterventionRecorder(interventions);
        var settings = new MariaActivityImportService(configuration, protection, fake, admin, audit);
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-2);
        var cui = Random.Shared.Next(80000000, 89999999).ToString();
        // ANAF knows both companies; the tickets service takes the clients it needs through the partners service.
        var anaf = new FakeAnafLookup(new Dictionary<string, CompanyLookupResult>
        {
            [cui] = new("Str. Florilor 5, Timisoara", "0722111222"), [(long.Parse(cui) + 1).ToString()] = new("Str. Noua 2, Arad", "0733111222")
        });
        var partners = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, admin, audit, anaf);
        var tickets = new MariaActivityTicketImportService(configuration, settings, fake, recorder, workPoints, admin, audit, partners);
        async Task<long> Count(string sql) => await Scalar<long>(probe, sql);
        try
        {
            foreach (var table in new[] { "activity_import_map", "activity_import_issues", "activity_import_runs", "activity_import_types", "activity_import_settings", "activity_import_partner_checks", "activity_import_tickets", "activity_import_suggestions" }) await Exec(probe, "DELETE FROM " + table);
            fake.Partners = [new(7001, $"Ext Tichete {suffix} SRL", "RO" + cui, "Str. Florilor 5", "Timisoara", "Timis", "0722111222", "")];
            fake.Points = [new(7101, 7001, "Str. Florilor 5", "Timisoara", "Timis", "", "Sediu", "", true, null), new(7102, 7001, "Bd. Libertatii 9", "Timisoara", "Timis", "", "Depozit", "", true, null)];
            fake.Types = [new(4, "Service", "S", true, false, 3, 0, 0), new(13, "Mentenanță", "M", true, false, 1, 0, 0), new(25, "Intervenție la cerere", "I", false, false, 0, 0, 0)];
            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", "parola-" + suffix, new DateOnly(2026, 1, 1), 0, 24, 200, false));

            var noTypes = 0;
            try { await tickets.SimulateAsync(); } catch (ActivityImportException) { noTypes++; }
            Check(noTypes == 1, "Activity tickets: without a chosen type the simulation says so");
            await settings.SaveTypesAsync([new(4, "Service", true, ActivityDestination.OnDemand), new(13, "Mentenanță", true, ActivityDestination.Maintenance), new(25, "Intervenție la cerere", false, ActivityDestination.OnDemand)]);

            await partners.ApplyAsync();
            var beneficiary = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Tichete {suffix} SRL");
            var points = await workPoints.GetAsync(beneficiary.Id);
            var depot = points.Single(point => !point.IsPrimary);
            var main = points.Single(point => point.IsPrimary);

            ActivityTicket Ticket(long id, long type, long partner, long point, DateTime? end, string report, DateTime? opened = null) =>
                new(id, type, partner, point, opened ?? new DateTime(2026, 3, 1), "T-" + id, "Internet " + id, "Nu merge internetul", "Ion Popescu", id + 1000, end, 1, report);
            fake.TicketRows =
            [
                Ticket(8001, 4, 7001, 7102, new DateTime(2026, 3, 2, 10, 0, 0), "Nu merge internetul\nS-a schimbat routerul"),
                Ticket(8002, 4, 7001, 0, new DateTime(2026, 4, 5, 12, 0, 0), "Am reinstalat tot"),
                Ticket(8003, 13, 7001, 0, new DateTime(2026, 4, 6), "Nu merge internetul"),
                Ticket(8004, 4, 9999, 0, new DateTime(2026, 4, 7), "Nu merge internetul\nceva"),
                Ticket(8005, 4, 7001, 0, DateTime.Today.AddDays(30), "Nu merge internetul\nceva"),
                Ticket(8006, 4, 7001, 0, new DateTime(2025, 12, 31), "Nu merge internetul\nceva", opened: new DateTime(2025, 12, 30))
            ];

            var interventionsBefore = await Count("SELECT COUNT(*) FROM service_interventions");
            var simulation = await tickets.SimulateAsync();
            Check(!simulation.Applied && simulation.Since == new DateOnly(2026, 1, 1) && simulation.Tickets is { Total: 5, Create: 2, Skipped: 1, Waiting: 1, Problems: 1 } && simulation.Types.SequenceEqual(["4 Service", "13 Mentenanță"]),
                "Activity tickets: the simulation counts the tickets of the chosen types from the chosen date (2 to take, 1 skipped, 1 waiting for its client, 1 problem; the ticket before the date is not read)");
            Check(simulation.Items.Any(item => item.SourceId == 8003 && item.Message.Contains("mentenanță", StringComparison.Ordinal)) && simulation.Items.Any(item => item.SourceId == 8005 && item.Action == "Problem"),
                "Activity tickets: the skipped and the problem tickets are listed with the reason");
            Check(await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore && await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity='ticket'") == 0
                && await Count("SELECT COUNT(*) FROM activity_import_runs WHERE kind='tickets-simulation' AND status='completed'") == 1, "Activity tickets: the simulation writes nothing but its run record");

            await settings.SaveSettingsAsync(new ActivityImportInput(false, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, new DateOnly(2026, 1, 1), 0, 24, 200, false));
            var off = 0;
            try { await tickets.ApplyAsync(); } catch (ActivityImportException) { off++; }
            Check(off == 1 && await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore, "Activity tickets: the application is refused while the import is switched off");
            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, new DateOnly(2026, 1, 1), 0, 24, 200, false));

            var applied = await tickets.ApplyAsync();
            Check(applied.Applied && applied.Tickets is { Create: 2, Skipped: 1, Waiting: 1, Problems: 1 }, "Activity tickets: the application does what the simulation showed");
            var recorded = (await interventions.GetForBeneficiaryAsync(beneficiary.Id)).OrderBy(item => item.PerformedOn).ToList();
            Check(recorded.Count == 2 && recorded.All(item => item.Kind == ServiceInterventionKind.OnDemand && !item.MovesDue) && recorded[0].PerformedOn == new DateOnly(2026, 3, 2) && recorded[0].WorkPointId == depot.Id && recorded[1].WorkPointId == main.Id,
                "Activity tickets: each resolved ticket is an on-demand intervention (moves no due date), dated by its event, at the ticket's point or the main one");
            Check(recorded[0].Notes.Contains("Problema: Nu merge internetul", StringComparison.Ordinal) && recorded[0].Notes.Contains("Rezolvare: S-a schimbat routerul", StringComparison.Ordinal) && recorded[0].Notes.Contains("Executant: Ion Popescu", StringComparison.Ordinal)
                && recorded[1].Notes.Contains("Rezolvare: Am reinstalat tot", StringComparison.Ordinal), "Activity tickets: the notes tell the problem, the resolution (what was added, or the whole rewritten report) and the executor");
            Check(await Count("SELECT COUNT(*) FROM service_intervention_source WHERE source_ticket_id IN (8001, 8002)") == 2
                && await Count("SELECT COUNT(*) FROM service_intervention_source WHERE source_ticket_id=8001 AND problem='Nu merge internetul' AND resolution='S-a schimbat routerul' AND report='Nu merge internetul\nS-a schimbat routerul' AND executor='Ion Popescu' AND ticket_number='T-8001'") == 1
                && await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity='ticket'") == 2, "Activity tickets: the problem, the resolution, the original report and the executor are kept apart, and the ties are remembered");
            var issues = await partners.GetOpenIssuesAsync();
            Check(issues.Count(issue => issue.Entity == "ticket") == 1 && issues.Single(issue => issue.Entity == "ticket").SourceId == 8005, "Activity tickets: the ticket with a date in the future is an open problem");

            var again = await tickets.ApplyAsync();
            Check(again.Tickets is { Create: 0, AlreadyLinked: 2 } && (await interventions.GetForBeneficiaryAsync(beneficiary.Id)).Count == 2 && (await partners.GetOpenIssuesAsync()).Count(issue => issue.Entity == "ticket") == 1,
                "Activity tickets: a second application takes nothing again and does not duplicate the problem");

            // The client of a skipped ticket is imported later: the ticket is taken at the next application.
            fake.Partners = [.. fake.Partners, new(7002, $"Ext Tichete Doi {suffix} SRL", "RO" + (long.Parse(cui) + 1), "Str. Noua 2", "Arad", "Arad", "0733111222", "")];
            fake.Points = [.. fake.Points, new(7103, 7002, "Str. Noua 2", "Arad", "Arad", "", "", "", true, null)];
            fake.TicketRows = [.. fake.TicketRows, Ticket(8007, 4, 7002, 7103, new DateTime(2026, 5, 1), "Nu merge internetul\nrezolvat")];
            var skipped = await tickets.SimulateAsync();
            Check(skipped.Tickets is { Skipped: 1, Waiting: 2 } && skipped.Items.Any(item => item.SourceId == 8007 && item.Action == "Waiting" && item.Message.Contains("așteaptă", StringComparison.Ordinal)), "Activity tickets: a ticket whose client is not imported yet waits, not an error");
            Check(await Count("SELECT COUNT(*) FROM activity_import_tickets WHERE ticket_id=8004 AND state='waiting_beneficiary'") == 1, "Activity tickets: a ticket that waits is kept with its data until its client can be taken");
            // The application takes the client of the ticket by itself (no pass over the whole table of partners), verified in ANAF.
            var later = await tickets.ApplyAsync();
            Check(later.Tickets.Create == 1 && later.NewBeneficiaries == 1 && later.BeneficiariesToVerify == 0 && await Count("SELECT COUNT(*) FROM service_intervention_source WHERE source_ticket_id=8007") == 1
                && (await beneficiaries.GetBeneficiariesAsync()).Any(item => item.Name == $"Ext Tichete Doi {suffix} SRL" && item.AnafVerified), "Activity tickets: the application takes the client of the ticket on demand and the ticket is taken");

            fake.Failure = new ActivitySourceException("ActivityApp nu a răspuns la timp.");
            var down = 0;
            try { await tickets.ApplyAsync(); } catch (Exception exception) when (exception is ActivityImportException or ActivitySourceException) { down++; }
            fake.Failure = null;
            Check(down == 1 && await Count("SELECT COUNT(*) FROM activity_import_runs WHERE status='running'") == 0 && (await partners.GetLastRunAsync()) is { Status: "failed" }, "Activity tickets: a source that does not answer ends the run as failed and recorded");

            var events = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 200)).Events.Where(entry => entry.TimestampUtc >= started).ToList();
            Check(events.Any(entry => entry.Action == AuditActions.SimulateActivityImport && entry.Details.Contains("Tichete rezolvate", StringComparison.Ordinal)) && events.Count(entry => entry.Action == AuditActions.ImportActivityTickets) == 3
                && events.Any(entry => entry.Action == AuditActions.ImportActivityTickets && entry.Details.Contains("2 preluate", StringComparison.Ordinal)), "Activity tickets: the simulation and each application are in the journal by their exact action, with the counts");
            var register = (await audit.QueryAsync(new AuditQuery(Text: $"Ext Tichete {suffix}"), 1, 100)).Events;
            Check(register.Any(entry => entry.Action == AuditActions.RecordOnDemand && entry.ActorUsername == "integration.tester"), "Activity tickets: each intervention is in the journal as a recorded on-demand intervention under the administrator who ran the import");

            // Incremental reading: only the tickets whose event was modified since the last run (with a margin of one day) are read; "Recitește tot" starts over.
            fake.Failure = null;
            ActivityTicket Modified(long id, DateTime modified) => Ticket(id, 4, 7001, 0, new DateTime(2026, 6, 1, 10, 0, 0), "Nu merge internetul\nrezolvat") with { EventModified = modified };
            fake.TicketRows = [Modified(8100, new DateTime(2026, 6, 1)), Modified(8101, new DateTime(2026, 6, 5))];
            var first = await tickets.ApplyAsync();
            var mark = (await settings.GetSettingsAsync()).SourceWatermark;
            Check(first.Tickets.Create == 2 && mark == new DateTime(2026, 6, 5), "Activity tickets: a run that came to its end remembers the latest modification it saw");
            fake.TicketRows = [.. fake.TicketRows, Modified(8102, new DateTime(2026, 6, 10))];
            var incremental = await tickets.ApplyAsync();
            Check(incremental.Tickets is { Create: 1, AlreadyLinked: 1 } && (await settings.GetSettingsAsync()).SourceWatermark == new DateTime(2026, 6, 10),
                "Activity tickets: the next run reads only the tickets modified since (one day before the mark), takes the new one and moves the mark; the older one is not read at all");
            await ActivityTicketStore.SetWatermarkAsync(configuration, null, CancellationToken.None);
            var everything = await tickets.ApplyAsync();
            Check(everything.Tickets is { Create: 0, AlreadyLinked: 3 } && (await Count("SELECT COUNT(*) FROM service_intervention_source WHERE source_ticket_id IN (8100, 8101, 8102)")) == 3, "Activity tickets: after the mark is cleared everything is read again and nothing is taken twice");

            var denied = new MariaActivityTicketImportService(configuration, settings, fake, recorder, workPoints, new TestAccessControl(false, "operator.ext"), audit);
            var refusals = 0;
            foreach (var operation in new Func<Task>[] { () => denied.SimulateAsync(), () => denied.ApplyAsync() })
                try { await operation(); } catch (AccessDeniedException) { refusals++; }
            Check(refusals == 2, "Activity tickets: a user who is not the administrator can neither simulate nor apply");

            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, new DateOnly(2026, 1, 1), 0, 24, 200, false));
            // Stage 5: the correspondence of the ticket texts to columns of ActivityApp.
            var defaultColumns = fake.Columns;
            fake.Columns = [.. defaultColumns, new ActivityColumn("Tickete", "Detalii", "nvarchar")];
            var choices = await settings.GetFieldChoicesAsync();
            Check(choices.Fields.Single(item => item.Field.Key == "Tickete.DescriptionInternal").Available.Contains("Detalii") && choices.SourceError is null && !choices.Fields.Any(item => item.CurrentMissing),
                "Activity field map: the columns of the table with text are offered, the default is in use");
            await settings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii", ["Tickete.Subject"] = "Subject" });
            await tickets.SimulateAsync();
            Check(fake.LastTicketStructure?.Physical("Tickete", "DescriptionInternal") == "Detalii" && fake.LastTicketStructure.Physical("Tickete", "Subject") == "Subject"
                && (await settings.GetFieldMapAsync()).Columns.Single().Value == "Detalii" && await Scalar<string>(probe, "SELECT field_map FROM activity_import_settings WHERE id = 1") == "{\"Tickete.DescriptionInternal\":\"Detalii\"}",
                "Activity field map: the saved choice is stored (only what differs from the default) and the reading of the tickets uses the chosen column");
            var mapEvents = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 200)).Events.Where(entry => entry.TimestampUtc >= started && entry.Action == AuditActions.SetActivityFieldMap).ToList();
            Check(mapEvents.Count == 1 && mapEvents[0].Details.Contains("Detalii", StringComparison.Ordinal), "Activity field map: the change is journaled by name with the old and the new value");
            await settings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii" });
            Check((await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 200)).Events.Count(entry => entry.TimestampUtc >= started && entry.Action == AuditActions.SetActivityFieldMap) == 1, "Activity field map: saving the same choice again journals nothing");
            var refused = 0;
            try { await settings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.Subject"] = "x; DROP" }); } catch (ActivityImportException) { refused++; }
            try { await settings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.Nimic"] = "Subject" }); } catch (ActivityImportException) { refused++; }
            Check(refused == 2 && (await settings.GetFieldMapAsync()).Columns.Count == 1, "Activity field map: a field that cannot be mapped or an invalid column name is refused and the stored choice stays");
            fake.Columns = defaultColumns;
            var stopped = "";
            try { await tickets.SimulateAsync(); } catch (ActivityImportException exception) { stopped = exception.Message; }
            Check(stopped.Contains("Corespondența câmpurilor", StringComparison.Ordinal) && stopped.Contains("secțiunea 6", StringComparison.Ordinal),
                "Activity field map: when the chosen column is gone from ActivityApp the run stops with that reason instead of reading another text");
            await settings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "DescriptionInternal" });
            await tickets.SimulateAsync();
            Check(await Count("SELECT COUNT(*) FROM activity_import_settings WHERE id = 1 AND field_map IS NULL") == 1 && (await settings.GetFieldMapAsync()).Columns.Count == 0 && fake.LastTicketStructure?.Physical("Tickete", "DescriptionInternal") == "DescriptionInternal",
                "Activity field map: choosing the default column back clears the choice and the run works again");
            var deniedSettings = new MariaActivityImportService(configuration, protection, fake, new TestAccessControl(false, "operator.ext"), audit);
            var deniedMap = 0;
            try { await deniedSettings.SaveFieldMapAsync(new Dictionary<string, string> { ["Tickete.Subject"] = "Subject" }); } catch (AccessDeniedException) { deniedMap++; }
            try { await deniedSettings.GetFieldChoicesAsync(); } catch (AccessDeniedException) { deniedMap++; }
            Check(deniedMap == 2, "Activity field map: a user who is not the administrator can neither read nor change the correspondence");
        }
        finally
        {
            foreach (var table in new[] { "activity_import_map", "activity_import_issues", "activity_import_runs", "activity_import_types", "activity_import_settings", "activity_import_partner_checks", "activity_import_tickets", "activity_import_suggestions" }) await Exec(probe, "DELETE FROM " + table);
        }
    }
}
