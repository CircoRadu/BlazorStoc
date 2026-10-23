using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Modules.Maintenance;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Preluare ActivityApp: maintenance keywords (the nomenclator), tickets that are also maintenance, events to validate (approximate words), the decisions ------------------
    private static async Task ActivityMaintenanceAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "blazorstoc-ai6-keys-" + Guid.NewGuid().ToString("N"))), options => options.SetApplicationName("BlazorStoc"));
        var fake = new FakeActivitySource
        {
            Columns = ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name,
                column.Kind switch { ColumnKind.Number => "bigint", ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", _ => "bit" }))).ToList()
        };
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var contracts = new MariaServiceContractRepository(configuration, admin, audit);
        var recorder = new OnDemandInterventionRecorder(new MariaServiceInterventionRepository(configuration, admin, audit));
        var settings = new MariaActivityImportService(configuration, protection, fake, admin, audit);
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-2);
        var cui = Random.Shared.Next(70000000, 79999999).ToString();
        // ANAF knows the company (the beneficiary is made verified); the tickets service takes the clients it needs through the partners service.
        var anaf = new FakeAnafLookup(new Dictionary<string, CompanyLookupResult> { [cui] = new("Str. Florilor 5, Timisoara", "0722111222") });
        var partners = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, admin, audit, anaf);
        var tickets = new MariaActivityTicketImportService(configuration, settings, fake, recorder, workPoints, admin, audit, partners);
        var keywords = new MariaActivityKeywordService(configuration, admin, audit);
        var review = new MariaActivityReviewService(configuration, admin, audit, recorder);
        async Task<long> Count(string sql) => await Scalar<long>(probe, sql);
        async Task CleanAsync()
        {
            foreach (var table in new[] { "activity_import_map", "activity_import_issues", "activity_import_runs", "activity_import_types", "activity_import_settings", "activity_import_suggestions", "activity_import_keywords", "activity_import_partner_checks", "activity_import_tickets" })
                await Exec(probe, "DELETE FROM " + table);
        }
        try
        {
            await CleanAsync();

            // The nomenclator: add, no duplicates (case, diacritics and punctuation do not count), rename, deactivate, delete, administrator only, journal by name.
            var word = await keywords.AddAsync("  mentenanță ");
            Check(word is { Keyword: "mentenanță", Active: true } && (await keywords.GetAllAsync()).Count == 1, "Activity keywords: a keyword is added with its spaces tidied and is active");
            var duplicates = 0;
            foreach (var variant in new[] { "MENTENANTA", "mentenanța.", "m-entenanta" })
                try { await keywords.AddAsync(variant); } catch (ActivityImportException) { duplicates++; }
            Check(duplicates == 2 || duplicates == 3, "Activity keywords: the same word in other letters, with punctuation or without diacritics is refused as a duplicate");
            var refusedWords = 0;
            foreach (var bad in new[] { "", "ab", "una, doua", new string('x', 61) })
                try { await keywords.AddAsync(bad); } catch (ActivityImportException) { refusedWords++; }
            Check(refusedWords == 4 && (await keywords.GetAllAsync()).Count <= 2, "Activity keywords: an empty, too short, listed (with comma) or too long keyword is refused");
            await CleanAsync();
            word = await keywords.AddAsync("mentenanță");
            var revision = await keywords.AddAsync("revizie periodica");
            revision = await keywords.RenameAsync(revision, "revizie periodică");
            revision = await keywords.SetActiveAsync(revision, false);
            Check(revision is { Keyword: "revizie periodică", Active: false } && (await ActivityKeywordStore.ActiveAsync(configuration, CancellationToken.None)).SequenceEqual(["mentenanță"]),
                "Activity keywords: a keyword is renamed and deactivated; the import reads only the active ones");
            var stale = 0;
            try { await keywords.SetActiveAsync(word with { Version = word.Version + 5 }, false); } catch (ActivityImportException) { stale++; }
            Check(stale == 1 && (await keywords.GetAllAsync()).Single(item => item.Id == word.Id).Active, "Activity keywords: a change made on an outdated copy is refused");
            var denied = new MariaActivityKeywordService(configuration, new TestAccessControl(false, "operator.ext"), audit);
            var refused = 0;
            foreach (var operation in new Func<Task>[] { () => denied.GetAllAsync(), () => denied.AddAsync("alt cuvant"), () => denied.RenameAsync(word, "altceva"), () => denied.SetActiveAsync(word, false), () => denied.DeleteAsync(word) })
                try { await operation(); } catch (AccessDeniedException) { refused++; }
            Check(refused == 5, "Activity keywords: without the right to the nomenclators the list can be neither read nor changed");

            // The tickets: one partner with a main point under an active maintenance contract and a second point without one.
            fake.Partners = [new(7601, $"Ext Mentenanta {suffix} SRL", "RO" + cui, "Str. Florilor 5", "Timisoara", "Timis", "0722111222", "")];
            fake.Points = [new(7611, 7601, "Str. Florilor 5", "Timisoara", "Timis", "", "Sediu", "", true, null), new(7612, 7601, "Bd. Libertatii 9", "Timisoara", "Timis", "", "Depozit", "", true, null)];
            fake.Types = [new(4, "Service", "S", true, false, 8, 0, 0), new(13, "Mentenanță", "M", true, false, 1, 0, 0)];
            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", "parola-" + suffix, new DateOnly(2026, 1, 1), 0, 24, 200, false));
            await settings.SaveTypesAsync([new(4, "Service", true, ActivityDestination.OnDemand), new(13, "Mentenanță", true, ActivityDestination.Maintenance)]);
            await partners.ApplyAsync();
            var owner = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Mentenanta {suffix} SRL");
            var points = await workPoints.GetAsync(owner.Id);
            var main = points.Single(point => point.IsPrimary);
            var depot = points.Single(point => !point.IsPrimary);
            var contract = await contracts.CreateAsync(owner.Id, new ServiceContractInput
            {
                NumberText = "77 din 01.01.2026", CycleMonths = 6, ValidUntil = new DateOnly(2027, 12, 31),
                Points = [new ServiceContractPointInput { WorkPointId = main.Id, NextDue = new DateOnly(2026, 2, 1) }]
            });
            ActivityTicket Ticket(long id, long point, DateTime end, string done, long type = 4) =>
                new(id, type, 7601, point, new DateTime(2026, 3, 1), "T-" + id, "Lucrare " + id, "Nu merge", "Ion", id + 1000, end, 1, "Nu merge\n" + done);
            fake.TicketRows =
            [
                Ticket(9001, 0, new DateTime(2026, 3, 2, 10, 0, 0), "Am facut MENTENANȚA, curatat filtre."),         // exact (case, diacritics, punctuation): maintenance
                Ticket(9002, 0, new DateTime(2026, 4, 5, 10, 0, 0), "S-a facut mentenata la sistem"),                // cut short: proposed
                Ticket(9003, 0, new DateTime(2026, 4, 6, 10, 0, 0), "verificat si m3ntenan@a completa"),             // digits and a symbol typed for letters: proposed
                Ticket(9004, 0, new DateTime(2026, 4, 7, 10, 0, 0), "Am schimbat routerul"),                         // no word: not taken
                Ticket(9005, 7612, new DateTime(2026, 4, 8, 10, 0, 0), "Mentenanta anuala"),                         // exact, but the point has no contract: on demand
                Ticket(9006, 7612, new DateTime(2026, 4, 9, 10, 0, 0), "mentenata"),                                 // approximate, no contract: nothing to validate, on demand
                Ticket(9007, 0, new DateTime(2026, 4, 10, 10, 0, 0), "mentenanta facuta", type: 13)                  // a type taken as maintenance is skipped as before
            ];

            var interventionsBefore = await Count("SELECT COUNT(*) FROM service_interventions");
            var simulation = await tickets.SimulateAsync();
            Check(simulation.Tickets is { Create: 5, Skipped: 2 } && simulation.Maintenance == 0 && simulation.MentionedOnly == 1 && simulation.ToValidate == 3,
                "Activity maintenance: the simulation takes 5 tickets (3 of them to validate: any word at a point under contract), 1 mentions it without a contract, 1 is skipped");
            Check(await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore && await Count("SELECT COUNT(*) FROM activity_import_suggestions") == 0,
                "Activity maintenance: the simulation records no intervention and no proposal");

            var applied = await tickets.ApplyAsync();
            Check(applied.Tickets.Create == 5 && applied.ToValidate == 3 && await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore + 5
                && await Count("SELECT COUNT(*) FROM service_interventions si JOIN service_intervention_source s ON s.intervention_id = si.id WHERE s.source_ticket_id IN (9001, 9002, 9003, 9005, 9006) AND si.kind = 'C'") == 5,
                "Activity maintenance: every ticket is added at once to the interventions of the beneficiary, on demand (3 of them waiting for the operator)");
            Check(await Count($"SELECT COUNT(*) FROM service_contract_points WHERE contract_id = {contract.Contract.Id} AND next_due = '2026-02-01'") == 1,
                "Activity maintenance: nothing moves the next due date before the operator confirms");
            Check(await Count("SELECT COUNT(*) FROM service_interventions si JOIN service_intervention_source s ON s.intervention_id = si.id WHERE s.source_ticket_id = 9005 AND si.notes LIKE '%dar punctul nu are un contract%'") == 1,
                "Activity maintenance: the notes say that the point has no contract");
            Check(await Count("SELECT COUNT(*) FROM activity_import_suggestions WHERE status = 'pending' AND intervention_id IS NOT NULL AND ticket_id IN (9001, 9002, 9003)") == 3
                && await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity = 'ticket' AND source_id IN (9001, 9002, 9003)") == 3
                && await Count("SELECT COUNT(*) FROM activity_import_tickets WHERE state = 'imported'") == 5, "Activity maintenance: the three proposals point at their interventions and the tickets are kept as taken");

            var pending = await review.GetPendingAsync();
            Check(pending.Count == 3 && pending.Any(item => item is { TicketId: 9001, Kind: "exact", InRegister: true }) && pending.Any(item => item.TicketId == 9002 && item.Found == "mentenata" && item.Kind == "approximate" && item.Keyword == "mentenanță" && item.Beneficiary.Contains("Ext Mentenanta", StringComparison.Ordinal))
                && pending.Any(item => item.TicketId == 9003 && item.Found.Contains("m3ntenan@a", StringComparison.Ordinal)), "Activity maintenance: the tickets to validate carry what was written, the keyword, how it matched and the beneficiary");
            var instances = ActivityReviewSource.Instances(pending);
            var source = new ActivityReviewSource(configuration);
            Check(instances.Count == 3 && (await source.GetInstancesAsync()).Count == 3 && instances.All(item => item.Url == "/setari?tab=preluare-activityapp") && instances.Any(item => item.Values[ActivityReviewSource.DetailsName].Contains("seamănă cu", StringComparison.Ordinal))
                && instances.Any(item => item.Values[ActivityReviewSource.DetailsName].Contains("verifică dacă mentenanța s-a făcut", StringComparison.Ordinal)),
                "Activity maintenance: each ticket waiting is a notification for the administrator, with the link to the settings");
            var again = await tickets.ApplyAsync();
            Check(again.Tickets.Create == 0 && again.ToValidate == 0 && await Count("SELECT COUNT(*) FROM activity_import_suggestions") == 3 && await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore + 5,
                "Activity maintenance: running again neither takes the tickets again nor proposes them twice");

            // The operator: the maintenance was done (9001), it was not (9002), discard (9003).
            await review.DecideAsync(pending.Single(item => item.TicketId == 9001).Id, ReviewDecision.Maintenance);
            await review.DecideAsync(pending.Single(item => item.TicketId == 9002).Id, ReviewDecision.OnDemand);
            await review.DecideAsync(pending.Single(item => item.TicketId == 9003).Id, ReviewDecision.Discard);
            var twice = 0;
            try { await review.DecideAsync(pending.Single(item => item.TicketId == 9002).Id, ReviewDecision.Maintenance); } catch (ActivityImportException) { twice++; }
            Check(twice == 1 && (await review.GetPendingAsync()).Count == 0 && (await source.GetInstancesAsync()).Count == 0, "Activity maintenance: a decision is taken once and the notification goes away");
            Check(await Count("SELECT COUNT(*) FROM service_interventions si JOIN service_intervention_source s ON s.intervention_id = si.id WHERE s.source_ticket_id = 9001 AND si.kind = 'M' AND si.contract_id IS NOT NULL") == 1
                && await Count("SELECT COUNT(*) FROM service_interventions si JOIN service_intervention_source s ON s.intervention_id = si.id WHERE s.source_ticket_id = 9002 AND si.kind = 'C'") == 1
                && await Count("SELECT COUNT(*) FROM service_intervention_source WHERE source_ticket_id = 9003") == 0 && await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore + 4,
                "Activity maintenance: the confirmed ticket became a maintenance intervention (replacing the on-demand one), the rejected stays on demand, the discarded one lost its intervention");
            Check(await Count($"SELECT COUNT(*) FROM service_contract_points WHERE contract_id = {contract.Contract.Id} AND next_due = '2026-09-02'") == 1,
                "Activity maintenance: the confirmed maintenance moves the next due date (the date performed plus the cycle of the contract)");
            Check(await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity = 'ticket' AND source_id = 9003") == 0 && await Count("SELECT COUNT(*) FROM activity_import_tickets WHERE ticket_id = 9003 AND state = 'discarded'") == 1
                && (await review.GetDiscardedAsync()).Any(item => item.TicketId == 9003), "Activity maintenance: the discarded ticket is marked in the database and listed");
            var deniedReview = new MariaActivityReviewService(configuration, new TestAccessControl(false, "operator.ext"), audit);
            var deniedCount = 0;
            try { await deniedReview.DecideAsync(1, ReviewDecision.Maintenance); } catch (AccessDeniedException) { deniedCount++; }
            try { await deniedReview.GetPendingAsync(); } catch (AccessDeniedException) { deniedCount++; }
            try { await deniedReview.ReactivateAsync(9003); } catch (AccessDeniedException) { deniedCount++; }
            Check(deniedCount == 3, "Activity maintenance: only who may run the import decides, discards, reactivates or sees the tickets to validate");

            var decided = await tickets.ApplyAsync();
            Check(decided.Tickets.Create == 0 && decided.ToValidate == 0 && decided.Items.Any(item => item.SourceId == 9003 && item.Message == ActivityTicketPlanner.TicketDiscarded) && await Count("SELECT COUNT(*) FROM service_interventions") == interventionsBefore + 4,
                "Activity maintenance: a discarded ticket is not taken again by a new synchronization");
            await review.ReactivateAsync(9003);
            var reactivated = await tickets.ApplyAsync();
            Check(reactivated.Tickets.Create == 1 && reactivated.ToValidate == 1 && (await review.GetPendingAsync()).Single().TicketId == 9003 && (await review.GetDiscardedAsync()).Count == 0,
                "Activity maintenance: a reactivated ticket is taken at the next synchronization and proposed again");
            var last = await tickets.ApplyAsync();
            Check(last.Tickets.Create == 0 && last.ToValidate == 0, "Activity maintenance: once taken, the tickets are not proposed again");

            await keywords.SetActiveAsync((await keywords.GetAllAsync()).Single(item => item.Id == word.Id), false);
            Check((await keywords.GetAllAsync()).Single(item => item.Id == word.Id).Active == false, "Activity keywords: deactivated again");
            await keywords.DeleteAsync((await keywords.GetAllAsync()).Single(item => item.Id == revision.Id));
            Check((await keywords.GetAllAsync()).Count == 1, "Activity keywords: a keyword is deleted");

            var events = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 400)).Events.Where(entry => entry.TimestampUtc >= started).ToList();
            Check(events.Any(entry => entry.Action == AuditActions.AddActivityKeyword && entry.Details.Contains("mentenanță", StringComparison.Ordinal)) && events.Any(entry => entry.Action == AuditActions.EditActivityKeyword && entry.Details.Contains("revizie", StringComparison.Ordinal))
                && events.Any(entry => entry.Action == AuditActions.DeleteActivityKeyword) && events.Count(entry => entry.Action == AuditActions.DecideActivityMaintenance) == 2
                && events.Any(entry => entry.Action == AuditActions.DecideActivityMaintenance && entry.Details.Contains("este mentenanță", StringComparison.Ordinal))
                && events.Count(entry => entry.Action == AuditActions.DiscardActivityTicket) == 1 && events.Count(entry => entry.Action == AuditActions.ReactivateActivityTicket) == 1,
                "Activity maintenance: keywords, decisions, discard and reactivation are in the journal by their exact action, with the old and the new value");
        }
        finally
        {
            await CleanAsync();
        }
    }
}
