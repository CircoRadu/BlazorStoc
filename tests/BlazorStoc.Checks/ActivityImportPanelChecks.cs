using BlazorStoc.Components.Shared;
using BlazorStoc.Modules.ActivityImport;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The panel Setari -> Preluare ActivityApp, section "Beneficiari si puncte de lucru": simulation, the confirmation before the application, the summary table, the problems and their
// resolution. The services are fakes (the real ones are checked against MariaDB and SQL Server).
public static class ActivityImportPanelChecks
{
    private sealed class FakeSettings : IActivityImportService
    {
        public Task<ActivityImportSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityImportSettings(true, "srv", 1433, "TIPActivity", "citire", true, null, 0, 24, 200, false, null, null, "", StructureStatus.Ok, "", [], null, "", null));
        public ActivityImportInput? LastSaved;
        public Task SaveSettingsAsync(ActivityImportInput input, CancellationToken cancellationToken = default) { LastSaved = input; return Task.CompletedTask; }
        public Task<ActivityTestResult> TestAsync(ActivityImportInput input, CancellationToken cancellationToken = default) => Task.FromResult(new ActivityTestResult(true, "ok", StructureStatus.Ok, false, false));
        public List<ActivityTypeItem> Types = [];
        public Task<ActivityTypesView> GetTypesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ActivityTypesView([.. Types], StructureStatus.Ok, null));
        public List<ActivityTypeItem> Saved = [];
        public Task<ActivityTypesView> GetSavedTypesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ActivityTypesView([.. Saved], StructureStatus.Ok, null));
        public Task SaveTypesAsync(IReadOnlyList<ActivityTypeChoice> choices, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ActivityConnection?> GetConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult<ActivityConnection?>(null);
        public Dictionary<string, string>? SavedMap;
        public Task<ActivityFieldMap> GetFieldMapAsync(CancellationToken cancellationToken = default) => Task.FromResult(ActivityFieldMap.Empty);
        public Task<ActivityFieldMapView> GetFieldChoicesAsync(bool readSource, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityFieldMapView([.. ActivityFieldMap.Fields.Select(field => new ActivityFieldChoice(field, SavedMap?.GetValueOrDefault(field.Key) ?? field.Column,
                field.Table == "Tickete" ? ["Description", "DescriptionInternal", "Detalii", "NumeTicket", "ProcesatDe", "Subject"] : ["DescriptionInternal", "Rezumat"], false))], null));
        public Task SaveFieldMapAsync(IReadOnlyDictionary<string, string> chosen, CancellationToken cancellationToken = default) { SavedMap = new(chosen); return Task.CompletedTask; }
    }

    private sealed class FakePartners : IActivityPartnerImportService
    {
        public List<ImportIssue> Issues = [new(1, 1, "partner", 9004, "CuiProst SRL", "CUI invalid în ActivityApp: «ABC12».", DateTime.UtcNow)];
        public int Applies, Resolved; public readonly List<(long Id, bool Company)> Included = [];
        private static ImportReport Report(bool applied) => new(applied ? "apply" : "simulation", DateTime.UtcNow, "admin", applied, new PlanCounts(5, 2, 1, 0, 2, 0), new PlanCounts(5, 2, 2, 0, 0, 1),
            [new ImportReportItem("partner", 9004, "CuiProst SRL", "Problem", "CUI invalid în ActivityApp: «ABC12»."), new ImportReportItem("workpoint", 9105, "Str. A 1", "Skipped", "Partenerul are o problemă de import."), new ImportReportItem("partner", 9200, "Ion Popescu", "Skipped", ActivityPartnerPlanner.PersonNotTaken)], false);
        public Task<ImportReport> SimulateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Report(false));
        public Task<ImportReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) { Applies++; return Task.FromResult(Report(true)); }
        public Task<ImportReport> ApplyForAsync(IReadOnlySet<long> partnerIds, CancellationToken cancellationToken = default) => Task.FromResult(Report(true));
        public Task<ImportRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default) => Task.FromResult<ImportRunInfo?>(null);
        public Task<IReadOnlyList<ImportIssue>> GetOpenIssuesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImportIssue>>([.. Issues]);
        public Task IncludeAsync(long partnerId, bool asCompany, CancellationToken cancellationToken = default) { Included.Add((partnerId, asCompany)); return Task.CompletedTask; }
        public Task ResolveIssueAsync(long issueId, CancellationToken cancellationToken = default) { Resolved++; Issues.RemoveAll(issue => issue.Id == issueId); return Task.CompletedTask; }
    }

    private sealed class FakeTickets : IActivityTicketImportService
    {
        public int Applies;
        private static TicketReport Report(bool applied) => new(applied ? "tickets-apply" : "tickets-simulation", DateTime.UtcNow, "admin", applied, new DateOnly(2026, 1, 1), ["4 Service"], [], new PlanCounts(6, 3, 0, 1, 1, 1), [new ImportReportItem("ticket", 8005, "Internet", "Problem", "Data efectuării nu poate fi în viitor.")], false);
        public Task<TicketReport> SimulateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Report(false));
        public Task<TicketReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) { Applies++; return Task.FromResult(Report(true)); }
    }

    private sealed class FakeReviews : IActivityReviewService
    {
        public List<ActivityReviewItem> Pending = [new(1, 9001, "T-77", "Revizie centrala", "Alfa SRL", "Sediu", "am facut mentenat la centrala", "mentenanță", "mentenat", new DateOnly(2026, 10, 1), DateTime.UtcNow)];
        public List<(long Id, ReviewDecision Decision)> Decisions = [];
        public List<(long Id, ReviewTakeInput Input)> Takes = [];
        public List<ActivityDiscardedTicket> Discarded = [];
        public List<long> Reactivated = [];
        public Task<IReadOnlyList<ActivityReviewItem>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityReviewItem>>([.. Pending]);
        public Task<ActivityReviewDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default)
        {
            var item = Pending.Single(entry => entry.Id == id);
            var source = new TicketSource(item.TicketId, 4, 1, item.TicketNumber, item.Title, "Nu merge centrala", item.Resolution, "Nu merge centrala\n" + item.Resolution, "Ion Popescu");
            return Task.FromResult(new ActivityReviewDetail(item, 5, 50, source, [new ReviewWorkPoint(50, "Sediu", "Str. 1"), new ReviewWorkPoint(51, "Depozit", "Str. 2")], true, "Note service", "Note mentenanta",
                item.Kind == ActivityTicketPlanner.ServiceKind ? ReviewCategory.Service : ReviewCategory.Maintenance, false));
        }
        public Task TakeAsync(long id, ReviewTakeInput input, CancellationToken cancellationToken = default) { Takes.Add((id, input)); Pending.RemoveAll(item => item.Id == id); return Task.CompletedTask; }
        public Task DecideAsync(long id, ReviewDecision decision, CancellationToken cancellationToken = default)
        {
            Decisions.Add((id, decision));
            if (decision == ReviewDecision.Discard && Pending.FirstOrDefault(item => item.Id == id) is { } gone) Discarded.Add(new(gone.TicketId, gone.TicketNumber, gone.Title, "admin", DateTime.UtcNow, true));
            Pending.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ActivityDiscardedTicket>> GetDiscardedAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDiscardedTicket>>([.. Discarded]);
        public Task ReactivateAsync(long ticketId, CancellationToken cancellationToken = default) { Reactivated.Add(ticketId); Discarded.RemoveAll(item => item.TicketId == ticketId); return Task.CompletedTask; }
    }

    private sealed class FakeSync : IActivitySyncService
    {
        public int Runs, Resets;
        public Task ResetReadingAsync(CancellationToken cancellationToken = default) { Resets++; return Task.CompletedTask; }
        public Task<SyncResult> RunNowAsync(CancellationToken cancellationToken = default) { Runs++; return Task.FromResult(new SyncResult(true, "Beneficiari: 1 noi.", 1, 0, 0, 0, 0)); }
        public Task<SyncResult?> RunScheduledAsync(DateTime nowLocal, CancellationToken cancellationToken = default) => Task.FromResult<SyncResult?>(null);
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        var partners = new FakePartners();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        var settingsService = new FakeSettings();
        context.Services.AddSingleton<IActivityImportService>(settingsService);
        context.Services.AddSingleton<IActivityPartnerImportService>(partners);
        var ticketService = new FakeTickets();
        context.Services.AddSingleton<IActivityTicketImportService>(ticketService);
        var syncService = new FakeSync();
        context.Services.AddSingleton<IActivitySyncService>(syncService);
        var reviewService = new FakeReviews();
        context.Services.AddSingleton<IActivityReviewService>(reviewService);
        // The types saved before are shown at once when the panel opens (the choice is not lost from sight); reading ActivityApp only refreshes them.
        settingsService.Saved = [new(4, "Service", "", null, null, null, null, null, true, ActivityDestination.OnDemand, ActivityImportRules.StateSelected)];
        var cut = context.Render<ActivityImportPanel>();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-issues").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#ai-types tbody tr").Count == 1 && cut.Find("#ai-types").TextContent.Contains("Service", StringComparison.Ordinal) && cut.Find("#ai-types input[role=switch]").HasAttribute("checked") && cut.Find("#ai-types-used").HasAttribute("open"),
            "Activity import panel: the types saved are shown, selected, as soon as the panel opens");
        settingsService.Saved = [];
        check(cut.Find("#ai-issues").TextContent.Contains("CuiProst SRL", StringComparison.Ordinal) && cut.FindAll("#ai-plan-summary").Count == 0,
            "Activity import panel: the open problems are listed when the panel opens, before any simulation");
        var apply = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Aplică importul");
        check(apply.HasAttribute("disabled"), "Activity import panel: the application cannot be started before a simulation");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Simulează importul").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-plan-summary").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        var rows = cut.FindAll("#ai-plan-summary tbody tr").Select(row => string.Join("|", row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()))).ToList();
        check(rows.SequenceEqual(["Beneficiari|5|2|1|0|2|0", "Puncte de lucru|5|2|2|0|0|1"]) && cut.Find("#ai-plan-title").TextContent.Contains("Simulare", StringComparison.Ordinal),
            "Activity import panel: the simulation shows the counts of beneficiaries and work points");
        check(cut.FindAll("#ai-plan-problems tbody tr").Count == 1 && cut.FindAll("#ai-plan-items tbody tr").Count == 0 && cut.FindAll("#ai-plan-ignored tbody tr").Count == 2 && cut.Find("#ai-plan-ignored").TextContent.Contains("Ignorat", StringComparison.Ordinal)
            && !cut.Find("#ai-plan-problems-group").HasAttribute("open") && !cut.Find("#ai-plan-ignored-group").HasAttribute("open") && cut.Find("#ai-plan-items-group").HasAttribute("open"),
            "Activity import panel: the simulation lists the problems (collapsed), the items selected for import (open) and the ignored ones (collapsed) apart");
        check(cut.FindAll("#ai-plan-items-page-size").Count == 1 && cut.FindAll("#ai-plan-ignored-page-size").Count == 1, "Activity import panel: the lists of the simulation are paged, with a choice of rows per page");
        check(cut.FindAll("#ai-plan-ignored button").Count == 2, "Activity import panel: an ignored partner can be included as a person or as a company (a work point cannot)");
        cut.FindAll("#ai-plan-ignored button").Single(button => button.TextContent.Trim() == "Ca persoană fizică").Click();
        // The two choices about the beneficiaries (migration 57): both start On; the automatic import is turned Off and saved with the rest of the settings.
        var toggles = cut.FindAll(".anaf-toggles input[role=switch]");
        check(toggles.Count == 2 && toggles.All(toggle => toggle.HasAttribute("checked")), "Activity import panel: the choices \"preia și beneficiarii fără tichete\" and \"import automat\" are shown, both On by default");
        toggles[1].Change(false);
        cut.Find(".anaf-toggles .editor-actions button.primary").Click();
        cut.WaitForAssertion(() => { if (settingsService.LastSaved is null) throw new InvalidOperationException("not saved yet"); });
        check(settingsService.LastSaved is { TakePartners: true, AutoAcceptPartners: false }, "Activity import panel: the automatic import turned Off is saved with the settings (new beneficiaries then wait for a manual acceptance)");
        cut.WaitForAssertion(() => { if (partners.Included.Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(partners.Included.Single() == (9200, false) && cut.Markup.Contains("neverificat, preluat manual", StringComparison.Ordinal), "Activity import panel: including an ignored partner asks the service, then shows the message in the section");
        check(partners.Applies == 0, "Activity import panel: the simulation applies nothing");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Aplică importul").Click();
        check(cut.Markup.Contains("Se vor crea 2 beneficiari și 2 puncte de lucru", StringComparison.Ordinal) && partners.Applies == 0, "Activity import panel: the application asks for confirmation first, with what it will do");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Renunță").Click();
        check(!cut.Markup.Contains("Se vor crea", StringComparison.Ordinal) && partners.Applies == 0, "Activity import panel: giving up the confirmation applies nothing");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Aplică importul").Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Da, aplică").Click();
        cut.WaitForAssertion(() => { if (!cut.Find("#ai-plan-title").TextContent.Contains("Import aplicat", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(partners.Applies == 1 && cut.FindAll("button").Single(button => button.TextContent.Trim() == "Aplică importul").HasAttribute("disabled"), "Activity import panel: after the confirmation the import is applied once and the button is disabled until a new simulation");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Simulează tichetele").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-tickets-summary").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(string.Join("|", cut.FindAll("#ai-tickets-summary tbody td").Select(cell => cell.TextContent.Trim())) == "6|3|1|1|1" && cut.Find("#ai-tickets-title").TextContent.Contains("Simulare tichete", StringComparison.Ordinal)
            && cut.Find("#ai-tickets-problems").TextContent.Contains("viitor", StringComparison.Ordinal), "Activity import panel: the simulation of the tickets shows the counts and the problems");
        check(!cut.Find("#ai-tickets-problems-group").HasAttribute("open") && !cut.Find("#ai-tickets-waiting-group").HasAttribute("open") && !cut.Find("#ai-tickets-ignored-group").HasAttribute("open") && cut.Find("#ai-tickets-taken-group").HasAttribute("open")
            && cut.FindAll("#ai-tickets-items-page-size").Count == 1, "Activity import panel: the tickets of the simulation are in collapsible, paged groups (problems, waiting, ignored collapsed; selected open)");
        check(ticketService.Applies == 0, "Activity import panel: the simulation of the tickets applies nothing");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Aplică tichetele").Click();
        check(cut.Markup.Contains("Se vor pune 3 tichete în coada de validare", StringComparison.Ordinal) && ticketService.Applies == 0, "Activity import panel: the application of the tickets asks for confirmation first");
        cut.FindAll("button").Where(button => button.TextContent.Trim() == "Da, aplică").Last().Click();
        cut.WaitForAssertion(() => { if (!cut.Find("#ai-tickets-title").TextContent.Contains("Tichete puse la validare", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(ticketService.Applies == 1, "Activity import panel: after the confirmation the tickets are applied once");
        check(cut.Find("#ai-sync-status").TextContent.Contains("Nicio sincronizare încă", StringComparison.Ordinal) && cut.FindAll("#ai-sync-hours").Count == 1, "Activity import panel: the synchronization section shows the interval and that none has run yet");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Importă acum").Click();
        cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Sincronizare terminată. Beneficiari: 1 noi.", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(syncService.Runs == 1, "Activity import panel: \"Importă acum\" runs one synchronization and shows its result");
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 1) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#ai-reviews tbody tr").Count == 1 && cut.Find("#ai-reviews").TextContent.Contains("mentenat", StringComparison.Ordinal) && cut.Find("#ai-reviews").TextContent.Contains("Alfa SRL", StringComparison.Ordinal),
            "Activity import panel: the events to validate are listed with the ticket, the beneficiary, the keyword and what was written");
        // Every operation on a ticket: take as proposed (maintenance, service), view and edit + take (popup), discard (asked to confirm in a popup); a discarded ticket can be reactivated.
        reviewService.Pending.Add(new(2, 9002, "T-78", "Alt tichet", "Beta SRL", "Sediu", "am schimbat routerul", "", "", new DateOnly(2026, 10, 2), DateTime.UtcNow, ActivityTicketPlanner.ServiceKind));
        reviewService.Pending.Add(new(3, 9003, "T-79", "Al treilea", "Beta SRL", "Sediu", "verificat centrala", "revizie", "revizie", new DateOnly(2026, 10, 3), DateTime.UtcNow, "exact"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Actualizează lista").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 3) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.Find("#ai-reviews").TextContent.Contains("Service", StringComparison.Ordinal) && cut.Find("#ai-reviews").TextContent.Contains("Mentenanță", StringComparison.Ordinal),
            "Activity import panel: every ticket in the queue says what it is proposed as (maintenance or service)");
        cut.Find("#ai-reviews button[aria-label^='Preia tichetul T-77 ca mentenanță']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(reviewService.Decisions.SequenceEqual([(1L, ReviewDecision.Maintenance)]) && cut.Markup.Contains("a fost preluat ca mentenanță", StringComparison.Ordinal), "Activity import panel: taking a ticket as proposed sends the decision and the ticket leaves the list");
        cut.Find("#ai-reviews button[aria-label^='Preia tichetul T-78 ca service']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 1) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(reviewService.Decisions.Last() == (2L, ReviewDecision.OnDemand) && cut.Markup.Contains("a fost preluat ca service", StringComparison.Ordinal), "Activity import panel: a service ticket is taken on demand");
        cut.Find("#ai-reviews button[aria-label^='Vizualizează tichetul T-79']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#review-view-title").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.Find("[role=dialog]").TextContent.Contains("Raportul original", StringComparison.Ordinal) && cut.Find("[role=dialog]").TextContent.Contains("Ion Popescu", StringComparison.Ordinal) && cut.Find("[role=dialog]").TextContent.Contains("Nu merge centrala", StringComparison.Ordinal),
            "Activity import panel: a ticket is viewed in a popup with the problem, the resolution, the report and the executor");
        cut.FindAll("[role=dialog] button").Single(button => button.TextContent.Trim() == "Modifică și preia").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#review-edit-title").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        cut.Find("#review-point").Change("51");
        cut.Find("#review-category").Change(nameof(ReviewCategory.Service));
        cut.FindAll("[role=dialog] button").Single(button => button.TextContent.Trim() == "Preia").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(reviewService.Takes is [{ Id: 3, Input: { Category: ReviewCategory.Service, WorkPointId: 51 } }] && reviewService.Takes[0].Input.Notes == "Note service" && cut.FindAll("[role=dialog]").Count == 0,
            "Activity import panel: edit + take sends the category, the point and the notes the operator chose (the notes follow the category) and closes the popup");
        reviewService.Pending.Add(new(4, 9004, "T-80", "Ultimul", "Beta SRL", "Sediu", "nimic", "", "", new DateOnly(2026, 10, 4), DateTime.UtcNow, ActivityTicketPlanner.ServiceKind));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Actualizează lista").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-reviews tbody tr").Count != 1) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        cut.Find("#ai-reviews button[aria-label^='Discard tichet: T-80']").Click();
        check(reviewService.Decisions.Count == 2 && cut.Find("[role=alertdialog]").TextContent.Contains("nu se mai preia", StringComparison.Ordinal), "Activity import panel: discard asks for a confirmation in a popup first");
        cut.FindAll("[role=alertdialog] button").Single(button => button.TextContent.Trim() == "Discard").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-review-empty").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(reviewService.Decisions.Last() == (4L, ReviewDecision.Discard) && cut.Markup.Contains("a fost discardat", StringComparison.Ordinal), "Activity import panel: after the confirmation the ticket is discarded and the list is empty");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Arată tichetele discardate").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-discarded tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        cut.Find("#ai-discarded button[aria-label^='Reactivează tichetul T-80']").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-discarded-empty").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(reviewService.Reactivated.SequenceEqual([9004L]), "Activity import panel: a discarded ticket is listed and can be reactivated");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Recitește tot").Click();
        cut.WaitForAssertion(() => { if (syncService.Resets == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(syncService.Resets == 1 && cut.Markup.Contains("citește din nou toate tichetele", StringComparison.Ordinal), "Activity import panel: \"Recitește tot\" makes the next synchronization read every ticket again");
        check(cut.FindAll("#ai-sync-times").Count == 0 && cut.FindAll("#ai-sync-minutes").Count == 1, "Activity import panel: the regular interval is set in hours and minutes");
        cut.Find("#ai-sync-mode").Change(ActivitySyncSchedule.TimesMode);
        check(cut.FindAll("#ai-sync-times").Count == 1 && cut.FindAll("#ai-sync-hours").Count == 0, "Activity import panel: the fixed hours replace the interval when the schedule is by hours");
        // The ticket types: two collapsible groups, the unused (collapsed) and the used (open by default); a type moves when its switch is changed.
        settingsService.Types = [new(1, "Intervenție la cerere", "Service", true, false, 10, 1, 0, true, ActivityDestination.OnDemand, "ok"), new(2, "Livrare", "Comercial", false, false, 5, 0, 0, false, ActivityDestination.OnDemand, "ok"),
            new(3, "Instalare", "Service", true, false, 2, 0, 0, false, ActivityDestination.OnDemand, "ok")];
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Citește tipurile din ActivityApp").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-types-used").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#ai-types-unused tbody tr").Count == 2 && cut.FindAll("#ai-types tbody tr").Count == 1 && cut.Find("#ai-types-used").HasAttribute("open") && !cut.Find("#ai-types-unused").HasAttribute("open")
            && cut.Markup.IndexOf("ai-types-unused", StringComparison.Ordinal) < cut.Markup.IndexOf("ai-types-used", StringComparison.Ordinal),
            "Activity import panel: the ticket types are in two groups, the unused first and collapsed, the used after it and open by default");
        cut.Find("#ai-types-unused input[role=switch]").Change(true);
        check(cut.FindAll("#ai-types-unused tbody tr").Count == 1 && cut.FindAll("#ai-types tbody tr").Count == 2, "Activity import panel: a type whose switch is turned on moves to the used group");
        // Since 0.5.19 the saved correspondence is shown at once (with the default columns), before the columns are read from ActivityApp.
        check(cut.FindAll("#ai-fields tbody tr").Count == ActivityFieldMap.Fields.Count && !cut.FindAll("button").Single(button => button.TextContent.Trim() == "Salvează corespondența").HasAttribute("disabled"), "Activity import panel: the saved correspondence of the fields is shown at once, before the columns are read");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Citește coloanele din ActivityApp").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-fields tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(cut.FindAll("#ai-fields tbody tr").Count == ActivityFieldMap.Fields.Count && cut.Find("#ai-fields").TextContent.Contains("Detalii", StringComparison.Ordinal) && cut.Find("#ai-fields").TextContent.Contains("DescriptionInternal (implicit)", StringComparison.Ordinal),
            "Activity import panel: every mappable field is listed with its default column and the text columns found in ActivityApp");
        cut.FindAll("#ai-fields select")[1].Change("Detalii");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Salvează corespondența").Click();
        cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Corespondența câmpurilor a fost salvată", StringComparison.Ordinal)) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(settingsService.SavedMap is { } saved && saved["Tickete.DescriptionInternal"] == "Detalii" && saved["Tickete.Subject"] == "Subject", "Activity import panel: saving sends the column chosen for each field");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Revino la coloanele implicite").Click();
        check(cut.FindAll("#ai-fields select").All(select => select.GetAttribute("aria-label") is not null) && cut.FindAll("#ai-fields select")[1].GetAttribute("aria-label")!.Contains("Problema", StringComparison.Ordinal),
            "Activity import panel: the selects of the fields are named for the field");
        cut.Find("#ai-issues button").Click();
        cut.WaitForAssertion(() => { if (cut.FindAll("#ai-issues").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(partners.Resolved == 1, "Activity import panel: marking a problem resolved removes it from the list");
        return Task.CompletedTask;
    }
}
