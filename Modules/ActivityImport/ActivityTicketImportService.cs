using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// Stage 3 of the import: the resolved tickets of the types the administrator chose, opened from the chosen date on (by default 1 January of the current year), become on-demand
// interventions in the maintenance register of the beneficiary the ticket's client was tied to (stage 2). A simulation reads ActivityApp and BlazorStoc and reports what an application
// would do, writing nothing but the record of the run; the application records each intervention through the ordinary register (so its rules hold and each one is in the journal under
// the administrator who ran the import), one ticket at a time: one that is refused is a problem and the run goes on. The problem, the resolution, the original report and the executor
// are kept apart in service_intervention_source. Only the administrator (permission "setari-import-activityapp.run").

public sealed record TicketReport(string Kind, DateTime AtUtc, string By, bool Applied, DateOnly Since, IReadOnlyList<string> Types, IReadOnlyList<string> StoppedTypes, PlanCounts Tickets,
    IReadOnlyList<ImportReportItem> Items, bool Truncated, int Maintenance = 0, int MentionedOnly = 0, int ToValidate = 0, int NewBeneficiaries = 0, int NewPoints = 0, int BeneficiariesToVerify = 0);

public interface IActivityTicketImportService
{
    /// <summary>Reads ActivityApp and BlazorStoc and reports what the application would do. Writes nothing but the record of the run.</summary>
    Task<TicketReport> SimulateAsync(CancellationToken cancellationToken = default);
    /// <summary>Records the interventions the simulation shows. Needs the import to be switched on and the maintenance module to work.</summary>
    Task<TicketReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class MariaActivityTicketImportService(IConfiguration configuration, IActivityImportService import, IActivitySource source, IOnDemandInterventionRecorder interventions,
    IWorkPointRepository workPoints, IAccessControl access, IAuditTrail audit, IActivityPartnerImportService? partners = null) : IActivityTicketImportService
{
    private const string Run = "setari-import-activityapp.run";
    // One run at a time in the application, of any kind of import.
    private static SemaphoreSlim Gate => ImportStore.Gate;

    // Tickets: the tickets (read now or kept waiting) by id, to keep them as they are while they wait or after they are taken; NewWatermark: where the next incremental reading starts.
    private sealed record Loaded(DateOnly Since, IReadOnlyList<string> Types, IReadOnlyList<string> Stopped, IReadOnlyList<TicketPlan> Plans, IReadOnlyDictionary<long, ActivityTicket> Tickets, DateTime? NewWatermark, int NewBeneficiaries = 0, int NewPoints = 0, int ToVerify = 0);

    public Task<TicketReport> SimulateAsync(CancellationToken cancellationToken = default) => ExecuteAsync(false, null, cancellationToken);

    public Task<TicketReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => ExecuteAsync(true, progress, cancellationToken);

    private async Task<TicketReport> ExecuteAsync(bool apply, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await access.EnsureAsync(Run, cancellationToken).ConfigureAwait(false);
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration)) throw new ActivityImportException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        if (!await Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new ActivityImportException("O altă rulare a importului este în desfășurare. Așteaptă să se termine.");
        try
        {
            var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
            var started = DateTime.UtcNow;
            var kind = apply ? "tickets-apply" : "tickets-simulation";
            var settings = await import.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (apply && !settings.Enabled) throw new ActivityImportException("Preluarea este inactivă. Activeaz-o în setări înainte de aplicare.");
            var runId = await ImportStore.StartRunAsync(configuration, kind, started, actor, cancellationToken).ConfigureAwait(false);
            TicketReport report;
            try
            {
                progress?.Report("Se citește ActivityApp…");
                var loaded = await LoadAsync(settings, apply, cancellationToken).ConfigureAwait(false);
                report = apply
                    ? await ApplyPlanAsync(loaded, runId, actor, started, progress, cancellationToken).ConfigureAwait(false)
                    : Report(kind, started, actor, false, loaded, loaded.Plans.Select(plan => (plan, plan.Action, Item(plan, plan.Action))));
            }
            catch (Exception exception)
            {
                await ImportStore.FinishRunAsync(configuration, runId, exception is OperationCanceledException ? "interrupted" : "failed", null, exception is OperationCanceledException ? "Rularea a fost întreruptă." : exception is ActivityImportException or ActivitySourceException ? exception.Message : "Eroare internă (" + exception.GetType().Name + ").").ConfigureAwait(false);
                throw;
            }
            await ImportStore.FinishRunAsync(configuration, runId, "completed", report, null).ConfigureAwait(false);
            await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.ActivityImport, apply ? AuditActions.ImportActivityTickets : AuditActions.SimulateActivityImport, runId.ToString(),
                apply ? "Import tichete rezolvate" : "Simulare import tichete rezolvate", Summary(report), "", cancellationToken).ConfigureAwait(false);
            return report;
        }
        finally { Gate.Release(); }
    }

    private static string Summary(TicketReport report) => AuditDetails.Identification(
        ("Tichete rezolvate", $"{report.Tickets.Total}: {report.Tickets.Create} {(report.Applied ? "preluate" : "de preluat")}, {report.Tickets.AlreadyLinked} deja preluate, {report.Tickets.Problems} probleme, {report.Tickets.Skipped} ignorate"),
        ("Mentenanță / de validat", $"{report.Maintenance} înregistrate ca mentenanță, {report.MentionedOnly} la cerere (fără contract), {report.ToValidate} de validat"),
        ("De la data", report.Since.ToString("dd.MM.yyyy")), ("Tipuri", string.Join(", ", report.Types)));

    private static ImportReportItem Item(TicketPlan plan, PlanAction action) => new(ActivityTicketPlanner.TicketEntity, plan.SourceId, plan.Title, action.ToString(), plan.Message);

    // Of the tickets taken: how many are recorded as maintenance (a keyword was found and a contract covers the point), how many mention it but stay on demand (no contract), and how many wait
    // for the administrator (a word that only resembles a keyword).
    private static TicketReport Report(string kind, DateTime at, string by, bool applied, Loaded loaded, IEnumerable<(TicketPlan Plan, PlanAction Action, ImportReportItem Item)> results)
    {
        var list = results.ToList();
        var items = list.Where(entry => entry.Action is PlanAction.Problem or PlanAction.Skipped or PlanAction.Waiting or PlanAction.Create).Select(entry => entry.Item).ToList();
        var taken = list.Where(entry => entry.Action == PlanAction.Create).Select(entry => entry.Plan).ToList();
        return new TicketReport(kind, at, by, applied, loaded.Since, loaded.Types, loaded.Stopped, PlanCounts.Of(list.Select(entry => entry.Action)), [.. items], false,
            taken.Count(plan => plan.Maintenance), taken.Count(plan => plan.Keyword.Length > 0 && !plan.Maintenance && !plan.NeedsValidation), list.Count(entry => entry.Plan.NeedsValidation), loaded.NewBeneficiaries, loaded.NewPoints, loaded.ToVerify);
    }
    private async Task<Loaded> LoadAsync(ActivityImportSettings settings, bool apply, CancellationToken cancellationToken)
    {
        var view = await import.GetTypesAsync(cancellationToken).ConfigureAwait(false);
        if (view.SourceError is not null) throw new ActivityImportException(view.SourceError);
        var selected = view.Items.Where(item => item.Selected).ToList();
        if (selected.Count == 0) throw new ActivityImportException(ActivityImportRules.NoTypesMessage);
        var stopped = view.Items.Where(item => item.State is ActivityImportRules.StateChanged).Select(item => $"{item.Id} {item.Name} ({item.State})").ToList();
        var connection = await import.GetConnectionAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ActivityImportException("Conexiunea la ActivityApp nu este completată (server, cont, parolă). Salveaz-o în setări.");
        var fields = await import.GetFieldMapAsync(cancellationToken).ConfigureAwait(false);
        var structure = ActivityStructureChecker.Check(await source.ReadStructureAsync(connection, cancellationToken).ConfigureAwait(false), null, fields);
        foreach (var entity in new[] { ActivityContract.Tickets, ActivityContract.Events })
            if (structure.IsBlocked(entity)) throw new ActivityImportException($"Structura ActivityApp s-a schimbat: „{entity}” nu poate fi preluat (vezi raportul de structură din setări).");
        // A column the administrator chose that is gone (or no longer text) stops the run: reading the default instead would put another text in the interventions.
        if (structure.Issues.FirstOrDefault(issue => issue.Problem.StartsWith("coloana aleasă", StringComparison.Ordinal)) is { } chosenIssue)
            throw new ActivityImportException($"Corespondența câmpurilor: {chosenIssue.Problem} (tabela {chosenIssue.Table}). Alege altă coloană în setări, secțiunea 6.");
        var since = settings.EffectiveSince(DateOnly.FromDateTime(DateTime.Now));
        // Incremental reading: only the tickets whose closing event was modified since the last successful import (with a margin of one day: a ticket is never taken twice, the map ties it); the
        // first run, and one after "Recitește tot", read everything. Tickets that wait for their beneficiary come from the table of the tickets kept, not from ActivityApp.
        var read = await source.ReadTicketsAsync(connection, structure, [.. selected.Select(item => item.Id)], since.ToDateTime(TimeOnly.MinValue), settings.SourceWatermark?.AddDays(-1), settings.PageSize, cancellationToken).ConfigureAwait(false);
        var states = await ActivityTicketStore.StatesAsync(configuration, cancellationToken).ConfigureAwait(false);
        var tickets = read.ToDictionary(ticket => ticket.Id);
        foreach (var stored in await ActivityTicketStore.ReadAsync(configuration, TicketStates.WaitingBeneficiary, cancellationToken).ConfigureAwait(false)) tickets.TryAdd(stored.Ticket.Id, stored.Ticket);
        var map = await ImportStore.ReadMapAsync(configuration, cancellationToken).ConfigureAwait(false);
        // The clients of these tickets that are not in BlazorStoc yet are taken now (only these: not the whole table of partners), verified in ANAF; one that cannot be taken waits in the queue of the
        // beneficiaries to verify, and its tickets with it.
        int newBeneficiaries = 0, newPoints = 0, toVerify = 0;
        if (apply && partners is not null)
        {
            var known = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PartnerEntity && entry.LocalId != 0).Select(entry => entry.SourceId).ToHashSet();
            var discardedPartners = (await ActivityPartnerCheckStore.ReadAsync(configuration, PartnerCheckStatus.Discarded, cancellationToken).ConfigureAwait(false)).Select(row => row.PartnerId).ToHashSet();
            var needed = tickets.Values.Where(ticket => ticket.PartnerId != 0 && selected.Any(item => item.Id == ticket.TypeId)).Select(ticket => ticket.PartnerId)
                .Where(id => !known.Contains(id) && !discardedPartners.Contains(id)).ToHashSet();
            var partnerReport = await partners.ApplyForAsync(needed, cancellationToken).ConfigureAwait(false);
            newBeneficiaries = partnerReport.Partners.Create;
            newPoints = partnerReport.Points.Create;
            toVerify = partnerReport.AnafUnverified;
            if (needed.Count > 0) map = await ImportStore.ReadMapAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        var partnerMap = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PartnerEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var checks = await ActivityPartnerCheckStore.ReadAsync(configuration, null, cancellationToken).ConfigureAwait(false);
        var ignoredPartners = partnerMap.Where(entry => entry.Value == 0).Select(entry => entry.Key).Concat(checks.Where(row => row.Status == PartnerCheckStatus.Discarded).Select(row => row.PartnerId)).ToHashSet();
        var waitingPartners = checks.Where(row => row.Status == PartnerCheckStatus.Pending).Select(row => row.PartnerId).ToHashSet();
        var discarded = states.Where(entry => entry.Value == TicketStates.Discarded).Select(entry => entry.Key).ToHashSet();
        var locals = new Dictionary<int, IReadOnlyList<WorkPoint>>();
        foreach (var beneficiary in tickets.Values.Select(ticket => partnerMap.TryGetValue(ticket.PartnerId, out var id) ? id : 0).Where(id => id != 0).Distinct())
            locals[beneficiary] = await workPoints.GetAsync(beneficiary, cancellationToken).ConfigureAwait(false);
        var destinations = selected.ToDictionary(item => item.Id, item => item.Destination);
        // The words of the nomenclator apply to every chosen type except those taken as maintenance (which need no word): a ticket that mentions maintenance may be the maintenance of its point
        // (when a contract covers it).
        var words = await ActivityKeywordStore.ActiveAsync(configuration, cancellationToken).ConfigureAwait(false);
        var keywords = new Dictionary<long, IReadOnlyList<string>>();
        if (words.Count > 0) foreach (var item in selected.Where(item => item.Destination != ActivityDestination.Maintenance)) keywords[item.Id] = words;
        var decisions = await ActivityReviewStore.DecisionsAsync(configuration, cancellationToken).ConfigureAwait(false);
        var plans = ActivityTicketPlanner.Plan([.. tickets.Values], destinations, map, locals, DateOnly.FromDateTime(DateTime.Now), keywords, decisions, ignoredPartners, waitingPartners, discarded);
        var wanted = ActivityTicketPlanner.KeywordPoints(plans);
        if (wanted.Count > 0)
        {
            var covered = new HashSet<(int, int)>();
            try
            {
                foreach (var (beneficiary, point) in wanted)
                    if (await interventions.IsCoveredAsync(beneficiary, point, cancellationToken).ConfigureAwait(false)) covered.Add((beneficiary, point));
            }
            catch (ModuleUnavailableException) { throw new ActivityImportException("Modulul Mentenanță este oprit: tichetele se preiau după ce pornește."); }
            plans = ActivityTicketPlanner.WithCoverage(plans, covered);
        }
        // The next reading starts from the latest modification seen, but not after a ticket that could not be taken for a problem (it is read again until it is solved).
        var heldBack = plans.Where(plan => plan.Action == PlanAction.Problem && tickets.TryGetValue(plan.SourceId, out var held) && held.EventModified is not null).Select(plan => tickets[plan.SourceId].EventModified!.Value).DefaultIfEmpty().Min();
        var latest = read.Where(ticket => ticket.EventModified is not null).Select(ticket => ticket.EventModified!.Value).DefaultIfEmpty().Max();
        DateTime? newWatermark = heldBack != default ? heldBack : latest != default ? latest : settings.SourceWatermark;
        if (newWatermark is not null && settings.SourceWatermark is { } previous && newWatermark < previous) newWatermark = previous;
        return new Loaded(since, [.. selected.Select(item => $"{item.Id} {item.Name}")], stopped, plans, tickets, newWatermark, newBeneficiaries, newPoints, toVerify);
    }

    private async Task<TicketReport> ApplyPlanAsync(Loaded loaded, long runId, string actor, DateTime started, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var results = new List<(TicketPlan Plan, PlanAction Action, string Message)>();
        var done = 0;
        // In the order of the dates performed, so that the next maintenance due moves forward visit by visit (an older visit never moves it back; the register decides).
        foreach (var plan in loaded.Plans.OrderBy(item => item.PerformedOn ?? DateOnly.MinValue).ThenBy(item => item.SourceId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++done % 20 == 0) progress?.Report($"Tichete: {done} din {loaded.Plans.Count}…");
            if (plan.Action == PlanAction.Waiting)
            {
                // The beneficiary is still to be verified (or could not be taken): the ticket is kept, with everything needed to plan it again, until it is clarified.
                if (loaded.Tickets.TryGetValue(plan.SourceId, out var waitingTicket)) await ActivityTicketStore.SaveAsync(configuration, waitingTicket, TicketStates.WaitingBeneficiary, null, cancellationToken).ConfigureAwait(false);
                results.Add((plan, plan.Action, plan.Message));
                continue;
            }
            if (plan.Action != PlanAction.Create) { results.Add((plan, plan.Action, plan.Message)); continue; }
            try
            {
                var interventionId = plan.Maintenance
                    ? await interventions.RecordMaintenanceAsync(plan.BeneficiaryId!.Value, plan.WorkPointId!.Value, plan.PerformedOn!.Value, plan.Notes, cancellationToken).ConfigureAwait(false)
                    : await interventions.RecordAsync(plan.BeneficiaryId!.Value, plan.WorkPointId!.Value, plan.PerformedOn!.Value, plan.Notes, cancellationToken).ConfigureAwait(false);
                await ImportStore.WriteMapAsync(configuration, ActivityTicketPlanner.TicketEntity, plan.SourceId, interventionId, "created", plan.Hash, cancellationToken).ConfigureAwait(false);
                await WriteSourceAsync(interventionId, plan.Source!, cancellationToken).ConfigureAwait(false);
                if (loaded.Tickets.TryGetValue(plan.SourceId, out var takenTicket)) await ActivityTicketStore.SaveAsync(configuration, takenTicket, TicketStates.Imported, interventionId, cancellationToken).ConfigureAwait(false);
                // A mention of maintenance at a point under contract: the intervention is already in the register (on demand); the operator says whether the maintenance was really done.
                if (plan.NeedsValidation) await ActivityReviewStore.AddAsync(configuration, plan, interventionId, cancellationToken).ConfigureAwait(false);
                results.Add((plan, PlanAction.Create, ""));
            }
            catch (OnDemandInterventionOperationException exception) { results.Add((plan, PlanAction.Problem, exception.Message)); }
            catch (ModuleUnavailableException) { throw new ActivityImportException("Modulul Mentenanță este oprit: tichetele se preiau după ce pornește. Ce s-a preluat până acum rămâne."); }
        }
        var report = Report("tickets-apply", started, actor, true, loaded, results.Select(result => (result.Plan, result.Action, Item(result.Plan, result.Action) with { Message = result.Message })));
        // The next reading starts here (only a run that came to its end moves it).
        await ActivityTicketStore.SetWatermarkAsync(configuration, loaded.NewWatermark, cancellationToken).ConfigureAwait(false);
        await ImportStore.RecordIssuesAsync(configuration, runId, [ActivityTicketPlanner.TicketEntity],
            results.Where(result => result.Action == PlanAction.Problem).Select(result => (ActivityTicketPlanner.TicketEntity, result.Plan.SourceId, result.Plan.Title, result.Message)), cancellationToken).ConfigureAwait(false);
        return report;
    }

    private async Task WriteSourceAsync(int interventionId, TicketSource source, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO service_intervention_source (intervention_id, source_ticket_id, source_type_id, source_event_id, ticket_number, title, problem, resolution, report, executor, imported_utc)
            VALUES (@intervention, @ticket, @type, @event, @number, @title, @problem, @resolution, @report, @executor, @now)
            ON DUPLICATE KEY UPDATE intervention_id=@intervention, source_type_id=@type, source_event_id=@event, ticket_number=@number, title=@title, problem=@problem, resolution=@resolution,
                report=@report, executor=@executor, imported_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@intervention", interventionId);
        command.Parameters.AddWithValue("@ticket", source.TicketId);
        command.Parameters.AddWithValue("@type", source.TypeId);
        command.Parameters.AddWithValue("@event", source.EventId);
        command.Parameters.AddWithValue("@number", Cut(source.Number, 100));
        command.Parameters.AddWithValue("@title", Cut(source.Title, 500));
        command.Parameters.AddWithValue("@problem", source.Problem);
        command.Parameters.AddWithValue("@resolution", source.Resolution);
        command.Parameters.AddWithValue("@report", source.Report);
        command.Parameters.AddWithValue("@executor", Cut(source.Executor, 200));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}

internal sealed class GuardedActivityTicketImportService(IActivityTicketImportService inner, ModuleRegistry modules) : IActivityTicketImportService
{
    private const string Id = ActivityImportModule.ModuleId;
    public Task<TicketReport> SimulateAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SimulateAsync(cancellationToken));
    public Task<TicketReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ApplyAsync(progress, cancellationToken));
}

internal sealed class UnavailableActivityTicketImportService : IActivityTicketImportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ActivityImportModule.ModuleId));
    public Task<TicketReport> SimulateAsync(CancellationToken cancellationToken = default) => Out<TicketReport>();
    public Task<TicketReport> ApplyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) => Out<TicketReport>();
}
