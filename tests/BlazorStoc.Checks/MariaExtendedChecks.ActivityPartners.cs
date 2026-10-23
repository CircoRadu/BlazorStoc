using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // ---- Preluare ActivityApp, stage 2: partners -> beneficiaries, work points -> work points; simulation, application, ties, problems, journal ----------------
    private sealed class FakeAnafLookup(Dictionary<string, CompanyLookupResult> answers) : ICompanyDataLookup
    {
        public Dictionary<string, CompanyLookupResult> Answers { get; } = answers;
        public List<string> Calls { get; } = [];
        public string? Outage { get; set; }
        public Task<CompanyLookupResult> LookupAsync(string cui, CancellationToken cancellationToken = default)
        {
            Calls.Add(cui);
            if (Outage is not null) return Task.FromResult(new CompanyLookupResult("", "", Outage, true));
            return Task.FromResult(Answers.TryGetValue(cui, out var answer) ? answer : new CompanyLookupResult("", "", "necunoscut", false, true));
        }
    }

    private static async Task ActivityPartnersAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "blazorstoc-ai2-keys-" + Guid.NewGuid().ToString("N"))), options => options.SetApplicationName("BlazorStoc"));
        var fake = new FakeActivitySource
        {
            Columns = ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name,
                column.Kind switch { ColumnKind.Number => "bigint", ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", _ => "bit" }))).ToList()
        };
        var beneficiaries = new MariaBeneficiaryRepository(configuration, admin, audit);
        var workPoints = new MariaWorkPointRepository(configuration, admin, audit);
        var settings = new MariaActivityImportService(configuration, protection, fake, admin, audit);
        var partners = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, admin, audit);
        var suffix = Suffix();
        var started = DateTime.UtcNow.AddSeconds(-2);
        var cuiNew = Random.Shared.Next(50000000, 59999999).ToString();
        var cuiOld = Random.Shared.Next(60000000, 69999999).ToString();
        var cuiFive = Random.Shared.Next(70000000, 79999999).ToString();
        async Task<long> Count(string sql) => await Scalar<long>(probe, sql);
        ActivityTicket Modified(long id, long partner) => new(id, 4, partner, 0, new DateTime(2026, 3, 1), "T-" + id, "Lucrare " + id, "Nu merge", "Ion", id + 1000, new DateTime(2026, 3, 2), 1, "Nu merge\nrezolvat");
        async Task<long> Named(string prefix) => await Count($"SELECT COUNT(*) FROM beneficiaries WHERE name LIKE '{prefix}%'");
        try
        {
            await Exec(probe, "DELETE FROM activity_import_map");
            await Exec(probe, "DELETE FROM activity_import_issues");
            await Exec(probe, "DELETE FROM activity_import_runs");
            await Exec(probe, "DELETE FROM activity_import_partner_checks");
            await Exec(probe, "DELETE FROM activity_import_tickets");
            await Exec(probe, "DELETE FROM activity_import_types");
            await Exec(probe, "DELETE FROM activity_import_settings");
            var existing = await beneficiaries.CreateAsync(new BeneficiaryInput { Name = $"Ext Import Existent {suffix} SRL", Cui = "RO" + cuiOld, Address = "Str. Veche 1, Timisoara", Phone = "0744111222" });
            fake.Partners =
            [
                new(9001, $"Ext Import Nou {suffix} SRL", "RO " + cuiNew, "Str. Florilor 5", "Timisoara", "Timis", "0722 111 222", ""),
                new(9002, $"Ext Import Existent {suffix} SRL", "RO" + cuiOld, "Alta adresa", "Arad", "Arad", "0755000111", ""),
                new(9003, $"Ext Import Persoana {suffix}", "-", "Str. Pietrei 2", "Lugoj", "Timis", "0766000222", ""),
                new(9004, $"Ext Import CuiProst {suffix} SRL", "ABC12", "Str. A 1", "Oras", "J", "0722000333", ""),
                new(9005, $"Ext Import FaraAdresa {suffix} SRL", cuiFive, "", "", "", "0788000111", "")
            ];
            fake.Points =
            [
                new(9101, 9001, "Str. Florilor 5", "Timisoara", "Timis", "", "Sediu", "", true, null),
                new(9102, 9001, "Bd. Libertatii 9", "Timisoara", "Timis", "0722111222", "Depozit", "45.7489, 21.2087", true, true),
                new(9103, 9002, "Str. Veche 1, Timisoara", "", "", "", "", "", true, null),
                new(9104, 9002, "Str. Noua 3", "Arad", "Arad", "", "Magazin", "", false, null),
                new(9105, 9004, "Str. A 1", "Oras", "J", "", "", "", true, null)
            ];

            var noSettings = 0;
            try { await partners.SimulateAsync(); } catch (ActivityImportException) { noSettings++; }
            Check(noSettings == 1, "Activity partners: without a saved connection the simulation says so");
            await settings.SaveSettingsAsync(new ActivityImportInput(false, "SERVER-" + suffix, 1433, "TIPActivity", "citire", "parola-" + suffix, null, 0, 24, 200, false));

            var beneficiariesBefore = await Count("SELECT COUNT(*) FROM beneficiaries");
            var pointsBefore = await Count("SELECT COUNT(*) FROM beneficiary_work_points");
            var simulation = await partners.SimulateAsync();
            Check(!simulation.Applied && simulation.Partners is { Total: 5, Create: 1, Link: 1, Problems: 1, Skipped: 2 } && simulation.Points is { Total: 5, Link: 2, Create: 2, Skipped: 1 },
                "Activity partners: the simulation counts what an application would do (1 beneficiary to create, 1 to link, 1 problem, 2 persons not taken; 2 points to link, 2 to create, 1 skipped)");
            Check(await Count("SELECT COUNT(*) FROM beneficiaries") == beneficiariesBefore && await Count("SELECT COUNT(*) FROM beneficiary_work_points") == pointsBefore && await Count("SELECT COUNT(*) FROM activity_import_map") == 0,
                "Activity partners: the simulation writes nothing (no beneficiary, no work point, no tie)");
            Check(simulation.Items.Count(item => item.Action == "Problem") == 1 && simulation.Items.Any(item => item.SourceId == 9005 && item.Message.Contains("adres", StringComparison.Ordinal))
                && simulation.Items.Any(item => item.SourceId == 9004 && item.Action == "Skipped" && item.Message == ActivityPartnerPlanner.PersonNotTaken) && simulation.AnafUnverified == 1
                && simulation.Items[0].Action == "Problem" && simulation.Items.Single(item => item.SourceId == 9001).Action == "Create" && simulation.Items.Single(item => item.SourceId == 9001).Message.Contains("De verificat", StringComparison.Ordinal) && simulation.Items[^1].Action == "Skipped",
                "Activity partners: the report lists first the problem, then the company to create (to verify), and last what is not taken (the persons and their points)");
            Check(await Count("SELECT COUNT(*) FROM activity_import_runs WHERE kind='simulation' AND status='completed'") == 1, "Activity partners: the simulation is recorded as a run");

            var notEnabled = 0;
            try { await partners.ApplyAsync(); } catch (ActivityImportException) { notEnabled++; }
            Check(notEnabled == 1 && await Named("Ext Import Nou " + suffix) == 0, "Activity partners: the application is refused while the import is switched off");
            await settings.SaveSettingsAsync(new ActivityImportInput(true, "SERVER-" + suffix, 1433, "TIPActivity", "citire", null, null, 0, 24, 200, false));

            var applied = await partners.ApplyAsync();
            Check(applied.Applied && applied.Partners is { Create: 1, Link: 1, Problems: 1, Skipped: 2 } && applied.Points is { Create: 2, Link: 2, Skipped: 1 }, "Activity partners: the application does what the simulation showed");
            var created = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Cui == cuiNew);
            Check(created.Name == $"Ext Import Nou {suffix} SRL" && created.Address == "Str. Florilor 5, Timisoara, Timis" && created.Phone == "0722111222" && !created.IsIndividual, "Activity partners: a company is created with the CUI without RO, the address put together and the phone normalized");
            Check(await Named("Ext Import Persoana " + suffix) == 0 && applied.Items.Any(item => item.SourceId == 9003 && item.Action == "Skipped"), "Activity partners: a partner without tax id is a person: not imported, listed as ignored");
            var createdPoints = await workPoints.GetAsync(created.Id);
            var depot = createdPoints.Single(point => point.Name == "Depozit");
            Check(createdPoints.Count == 2 && createdPoints.Count(point => point.IsPrimary) == 1 && depot.Latitude == 45.7489m && depot.Longitude == 21.2087m && depot.Description.Contains("sistat", StringComparison.Ordinal),
                "Activity partners: the point with the partner's address is the main one, the other is an additional point with coordinates and the note that it was closed");
            var existingPoints = await workPoints.GetAsync(existing.Id);
            Check((await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Id == existing.Id) == existing && existingPoints.Count == 2 && existingPoints.Single(point => !point.IsPrimary) is { Name: "Magazin" } shop && shop.Description.Contains("inactiv", StringComparison.Ordinal),
                "Activity partners: an existing beneficiary is not changed; its main point is tied to the point with the same address and the other becomes an additional point");
            Check(await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity='partner'") == 4 && await Count("SELECT COUNT(*) FROM activity_import_map WHERE entity='workpoint'") == 4
                && await Count($"SELECT COUNT(*) FROM activity_import_map WHERE entity='partner' AND source_id=9002 AND local_id={existing.Id} AND origin='linked'") == 1 && await Count("SELECT COUNT(*) FROM activity_import_map WHERE origin='created'") == 3 && await Count("SELECT COUNT(*) FROM activity_import_map WHERE origin='ignored' AND local_id=0") == 2,
                "Activity partners: the ties are remembered (the beneficiary created, the existing one linked, the 2 persons marked as ignored, 4 points)");
            Check(await Count("SELECT COUNT(*) FROM activity_import_partner_checks WHERE status='pending' AND reason='anaf_unavailable' AND beneficiary_id IS NOT NULL") == 1 && await Count("SELECT COUNT(*) FROM activity_import_partner_checks WHERE partner_id=9005 AND status='pending' AND beneficiary_id IS NULL") == 1
                && (await new MariaBeneficiaryCheckService(configuration, admin, audit, beneficiaries).GetPendingAsync()).Count == 2,
                "Activity partners: the company that ANAF could not verify and the one without address are in the queue of the beneficiaries to verify"); 
            var openIssues = await partners.GetOpenIssuesAsync();
            Check(openIssues.Count == 1 && openIssues.Any(issue => issue.SourceId == 9005 && issue.Message.Contains("adres", StringComparison.Ordinal)), "Activity partners: what could not be imported is kept as open problems");

            var beneficiariesAfter = await Count("SELECT COUNT(*) FROM beneficiaries");
            var pointsAfter = await Count("SELECT COUNT(*) FROM beneficiary_work_points");
            var again = await partners.ApplyAsync();
            Check(again.Partners is { Create: 0, Link: 0, AlreadyLinked: 2, Problems: 1, Skipped: 2 } && again.Points.Create == 0 && await Count("SELECT COUNT(*) FROM beneficiaries") == beneficiariesAfter && await Count("SELECT COUNT(*) FROM beneficiary_work_points") == pointsAfter,
                "Activity partners: a second application creates nothing again (the ties make it idempotent)");
            Check((await partners.GetOpenIssuesAsync()).Count == 1, "Activity partners: the problems of the new application replace the open ones (no duplicates)");

            // The partner whose address was missing is completed in ActivityApp: the next application takes it (and its problem goes away).
            fake.Partners = [.. fake.Partners.Select(partner => partner.Id == 9005 ? partner with { Address = "Str. B 2", City = "Oras", County = "J" } : partner)];
            var fixedUp = await partners.ApplyAsync();
            Check(fixedUp.Partners.Create == 1 && fixedUp.Partners.Problems == 0 && (await partners.GetOpenIssuesAsync()).Count == 0 && await Count("SELECT COUNT(*) FROM activity_import_partner_checks WHERE partner_id=9005 AND reason='anaf_unavailable' AND beneficiary_id IS NOT NULL") == 1,
                "Activity partners: a partner completed in ActivityApp is taken at the next application, its problem is gone, and it is still to verify (ANAF could not answer)");
            // A new problem (a name too long for a beneficiary), to mark as resolved.
            fake.Partners = [.. fake.Partners, new(9006, $"Ext Import Lung {suffix} " + new string('x', 210), "RO" + cuiNew + "9", "Str. Lunga 1", "Oras", "J", "0722000444", "")];
            await partners.ApplyAsync();
            await partners.ResolveIssueAsync((await partners.GetOpenIssuesAsync()).Single().Id);
            Check((await partners.GetOpenIssuesAsync()).Count == 0 && await Count("SELECT COUNT(*) FROM activity_import_issues WHERE resolved=1") == 1, "Activity partners: a problem marked resolved leaves the open list and is kept");

            fake.Failure = new ActivitySourceException("ActivityApp nu a răspuns la timp.");
            var down = 0;
            try { await partners.ApplyAsync(); } catch (ActivitySourceException) { down++; }
            fake.Failure = null;
            var last = await partners.GetLastRunAsync();
            Check(down == 1 && last is { Status: "failed" } && await Count("SELECT COUNT(*) FROM activity_import_runs WHERE status='running'") == 0, "Activity partners: a source that does not answer ends the run as failed, recorded, nothing left running");

            var events = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 100)).Events.Where(entry => entry.TimestampUtc >= started).ToList();
            Check(events.Any(entry => entry.Action == AuditActions.SimulateActivityImport) && events.Count(entry => entry.Action == AuditActions.ImportActivityPartners) == 4 && events.Any(entry => entry.Action == AuditActions.ResolveActivityImportIssue)
                && events.Any(entry => entry.Action == AuditActions.ImportActivityPartners && entry.Details.Contains("1 creați", StringComparison.Ordinal)), "Activity partners: the simulation, each application and the resolution of a problem are journaled by their exact action, with the counts");
            var creations = (await audit.QueryAsync(new AuditQuery(Text: $"Ext Import Nou {suffix}", Entity: AuditEntities.Beneficiary), 1, 20)).Events;
            Check(creations.Any(entry => entry.ActorUsername == "integration.tester"), "Activity partners: the beneficiaries created by the import are in the journal under the administrator who ran it");

            // An ignored person included by hand is taken as an individual, not verified in ANAF, and the inclusion is journaled.
            await partners.IncludeAsync(9003, false);
            await partners.ApplyAsync();
            var manual = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == "Ext Import Persoana " + suffix);
            Check(manual is { Kind: BeneficiaryKinds.Individual, AnafVerified: false } && await Count($"SELECT COUNT(*) FROM activity_import_map WHERE entity='partner' AND source_id=9003 AND local_id={manual.Id} AND origin='created'") == 1
                && (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 100)).Events.Any(entry => entry.TimestampUtc >= started && entry.Action == AuditActions.IncludeActivityPartner && entry.Details.Contains("neverificat, preluat manual", StringComparison.Ordinal)),
                "Activity partners: an ignored person included by hand is created as an individual, not verified, and the inclusion is journaled");
            var includeTwice = 0;
            try { await partners.IncludeAsync(9003, false); } catch (ActivityImportException) { includeTwice++; }
            Check(includeTwice == 1, "Activity partners: a partner already taken cannot be included again");

            // A company without address (and one without phone) is completed from ANAF; an answer that cannot be had is the reason of the problem, and with ANAF out the rest are not asked.
            var cuiA = Random.Shared.Next(80000000, 84999999).ToString();
            var cuiB = Random.Shared.Next(85000000, 89999999).ToString();
            var cuiC = Random.Shared.Next(90000000, 94999999).ToString();
            var anafFake = new FakeAnafLookup(new Dictionary<string, CompanyLookupResult>
            {
                [cuiA] = new("Str. ANAF 7, Cluj", "0745 999 888"),
                [cuiB] = new("", "", "CUI-ul nu este înregistrat în ANAF.", false, true),
                [cuiC] = new("Str. ANAF 8", "260681")
            });
            fake.Partners =
            [
                new(9201, $"Ext Import Anaf A {suffix} SRL", cuiA, "", "", "", "", ""),
                new(9202, $"Ext Import Anaf B {suffix} SRL", cuiB, "", "", "", "", ""),
                new(9203, $"Ext Import Anaf C {suffix} SRL", cuiC, "Str. C 3", "Oras", "J", "-", "")
            ];
            fake.Points = [];
            var withAnaf = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, admin, audit, anafFake);
            var anafSimulation = await withAnaf.SimulateAsync();
            Check(anafSimulation.Partners is { Total: 3, Create: 2, Problems: 1 } && anafSimulation.AnafFilled == 1 && anafSimulation.AnafUnverified == 0 && anafSimulation.Items.Single(item => item.SourceId == 9202).Message.Contains("ANAF nu cunoaște CUI-ul", StringComparison.Ordinal),
                "Activity partners: a company without address is completed from ANAF (simulation too); a CUI ANAF does not know, with no address either, stays out with that reason");
            var anafApplied = await withAnaf.ApplyAsync();
            var viaAnaf = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Import Anaf A {suffix} SRL");
            var noPhone = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Import Anaf C {suffix} SRL");
            Check(anafApplied.AnafFilled == 1 && viaAnaf.Address == "Str. ANAF 7, Cluj" && viaAnaf.Phone == "0745999888" && noPhone.Phone == "" && noPhone.Address == "Str. C 3, Oras, J" && viaAnaf.AnafVerified && noPhone.AnafVerified,
                "Activity partners: the beneficiary is created, verified in ANAF, with the address and phone of ANAF; a phone that is not a full number is not taken and the beneficiary is created without one (the phone is optional)");
            Check(new[] { cuiA, cuiB, cuiC }.All(asked => anafFake.Calls.Count(call => call == asked) == 2), "Activity partners: each company is asked of ANAF once per run (the simulation and the application)");
            fake.Partners = [new(9301, $"Ext Import Anaf D {suffix} SRL", cuiA + "1", "", "", "", "", ""), new(9302, $"Ext Import Anaf E {suffix} SRL", cuiA + "2", "", "", "", "", ""),
                new(9303, $"Ext Import Anaf F {suffix} SRL", cuiA + "3", "Str. F 6", "Oras", "J", "0722333444", "")];
            anafFake.Calls.Clear();
            anafFake.Outage = "ANAF nu răspunde.";
            var outage = await withAnaf.SimulateAsync();
            Check(outage.Partners is { Problems: 2, Create: 1 } && outage.AnafFilled == 0 && outage.AnafUnverified == 1 && anafFake.Calls.Count == 1 && outage.Items.Where(item => item.Action == "Problem").All(item => item.Message.Contains("(ANAF nu a putut răspunde)", StringComparison.Ordinal)) && outage.Items.Any(item => item.Action == "Create" && item.Message.Contains("De verificat", StringComparison.Ordinal)),
                "Activity partners: with ANAF out the first lookup is the only one; a company without address keeps the reason as a problem, and one with a valid CUI is still imported, as not verified");
            await withAnaf.ApplyAsync();
            var unverified = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Import Anaf F {suffix} SRL");
            Check(!unverified.AnafVerified && unverified.Address == "Str. F 6, Oras, J" && await Count($"SELECT COUNT(*) FROM activity_import_partner_checks WHERE partner_id=9303 AND status='pending' AND reason='anaf_unavailable'") == 1,
                "Activity partners: the company imported while ANAF was out is marked as not verified in ANAF and put in the queue");
            // ANAF answers again: the next run asks again about the beneficiaries taken without verification; the confirmed one is marked as verified and leaves the queue.
            anafFake.Outage = null;
            anafFake.Answers[cuiA + "3"] = new("Str. F 6, Oras", "");
            await withAnaf.ApplyAsync();
            var verifiedLater = (await beneficiaries.GetBeneficiariesAsync()).Single(item => item.Name == $"Ext Import Anaf F {suffix} SRL");
            Check(verifiedLater.AnafVerified && await Count("SELECT COUNT(*) FROM activity_import_partner_checks WHERE partner_id=9303 AND status='resolved'") == 1,
                "Activity partners: when ANAF answers again the beneficiary taken without verification is marked as verified and leaves the queue");

            // The queue on the Beneficiari page: the list, retry, accept by hand, discard (with the tickets that waited); only who may edit beneficiaries.
            var checkService = new MariaBeneficiaryCheckService(configuration, admin, audit, beneficiaries, anafFake);
            await ActivityTicketStore.SaveAsync(configuration, Modified(7777, 9301), TicketStates.WaitingBeneficiary, null, CancellationToken.None);
            var queue = await checkService.GetPendingAsync();
            var withBeneficiary = queue.First(item => item.BeneficiaryId is not null);
            var withoutBeneficiary = queue.First(item => item.PartnerId == 9301);
            Check(queue.Count >= 2 && withoutBeneficiary.BeneficiaryId is null && withoutBeneficiary.WaitingTickets == 1 && withBeneficiary.ReasonText.Length > 0, "Activity partners: the queue lists the partners to verify with the reason and the tickets that wait for each");
            var retryMessage = await checkService.RetryAsync(withBeneficiary.Id);
            Check(retryMessage.Contains("nu cunoaște", StringComparison.Ordinal) && (await checkService.GetPendingAsync()).Any(item => item.Id == withBeneficiary.Id), "Activity partners: retry says what ANAF answered and leaves the partner in the queue");
            anafFake.Answers[SupplierRules.CuiDigits(withBeneficiary.Cui)] = new("Str. Veche 1", "");
            var retryOk = await checkService.RetryAsync(withBeneficiary.Id);
            Check(retryOk.Contains("verificat", StringComparison.Ordinal) && !(await checkService.GetPendingAsync()).Any(item => item.Id == withBeneficiary.Id), "Activity partners: when ANAF confirms the CUI on retry the beneficiary is verified and leaves the queue");
            var cannotAccept = 0;
            try { await checkService.AcceptAsync(withoutBeneficiary.Id); } catch (ActivityImportException) { cannotAccept++; }
            Check(cannotAccept == 1, "Activity partners: a partner with no beneficiary yet cannot be accepted by hand");
            await checkService.DiscardAsync(withoutBeneficiary.Id);
            Check(await Count("SELECT COUNT(*) FROM activity_import_partner_checks WHERE partner_id=9301 AND status='discarded'") == 1 && await Count("SELECT COUNT(*) FROM activity_import_tickets WHERE ticket_id=7777 AND state='discarded'") == 1,
                "Activity partners: a discarded partner leaves the queue and the tickets that waited for it are discarded for good");
            var another = (await checkService.GetPendingAsync()).FirstOrDefault(item => item.BeneficiaryId is not null);
            if (another is not null) await checkService.AcceptAsync(another.Id);
            var checkEvents = (await audit.QueryAsync(new AuditQuery(Entity: AuditEntities.ActivityImport), 1, 300)).Events.Where(entry => entry.TimestampUtc >= started).ToList();
            Check(checkEvents.Any(entry => entry.Action == AuditActions.RetryActivityPartnerCheck) && checkEvents.Any(entry => entry.Action == AuditActions.DiscardActivityPartner && entry.Details.Contains("Motiv", StringComparison.Ordinal)),
                "Activity partners: a retry that verifies and a discard are in the journal by their exact action");

            var operatorAccess = new TestAccessControl(false, "operator.ext");
            var denied = new MariaActivityPartnerImportService(configuration, settings, fake, beneficiaries, workPoints, operatorAccess, audit);
            var refusals = 0;
            foreach (var operation in new Func<Task>[] { () => denied.SimulateAsync(), () => denied.ApplyAsync(), () => denied.GetLastRunAsync(), () => denied.GetOpenIssuesAsync(), () => denied.ResolveIssueAsync(1), () => denied.IncludeAsync(9003, false) })
                try { await operation(); } catch (AccessDeniedException) { refusals++; }
            Check(refusals == 6, "Activity partners: a user who is not the administrator can neither simulate, apply, read the runs and problems, resolve a problem nor include a partner");
        }
        finally
        {
            await Exec(probe, "DELETE FROM activity_import_map");
            await Exec(probe, "DELETE FROM activity_import_issues");
            await Exec(probe, "DELETE FROM activity_import_runs");
            await Exec(probe, "DELETE FROM activity_import_partner_checks");
            await Exec(probe, "DELETE FROM activity_import_tickets");
            await Exec(probe, "DELETE FROM activity_import_settings");
        }
    }
}
