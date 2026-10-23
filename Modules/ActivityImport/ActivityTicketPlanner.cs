using System.Text;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// What the import of resolved tickets will do, decided without touching anything. A resolved ticket (the ticket and the event that closed it) becomes an on-demand intervention
// of the beneficiary the ticket's client was tied to (stage 2), at the work point of the ticket, or the main point when the ticket has none that was tied. The problem and the
// resolution are told apart from the report of the event (see SplitReport). Tickets whose client was not imported (orphans, or partners with a problem) are not errors: they are
// skipped, and taken after the partner is. A type whose destination is "de mentenanta" is not imported yet (a maintenance intervention moves due dates; it needs a way to record one
// without moving them), and is said so.

public sealed record TicketSource(long TicketId, long TypeId, long EventId, string Number, string Title, string Problem, string Resolution, string Report, string Executor);

public sealed record TicketPlan(long SourceId, string Title, PlanAction Action, int? BeneficiaryId, int? WorkPointId, DateOnly? PerformedOn, string Notes, TicketSource? Source, string Message, string Hash,
    string Keyword = "", bool Maintenance = false, string Approximate = "", string Found = "", bool NeedsValidation = false, bool Decided = false, string MatchKind = "")
{
    /// <summary>How the text of the ticket wrote the word that only resembles a keyword.</summary>
    public string Written => Found;
}

/// <summary>What the administrator decided about a ticket whose text only resembled a keyword.</summary>
public enum KeywordDecision { Confirmed, Rejected }

public static class ActivityTicketPlanner
{
    public const string TicketEntity = "ticket";
    public const string TicketDiscarded = "Tichet discardat: nu se mai preia.";
    public const string ClientNotTaken = "Clientul tichetului nu se preia (persoană fizică sau discardat): tichetul nu se preia.";
    public const string ClientToVerify = "Clientul tichetului este de verificat: tichetul așteaptă clarificarea beneficiarului.";
    public const string ClientNotImported = "Clientul tichetului nu a putut fi preluat încă (vezi problemele): tichetul așteaptă.";
    public const string MaintenanceNotAvailable = "Destinația „de mentenanță” nu este disponibilă încă (ar muta scadențele); tipul se preia ca „la cerere” după ce o alegi.";

    // The text with every run of white space as one space: what is compared when the report is checked to begin with the problem.
    private static string Collapse(string text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The problem is the text of the ticket at its opening. The report of the event is that text followed by what was done: when it begins with it (ignoring differences of spaces) the
    /// resolution is what follows; when the operator rewrote it, the resolution is the whole report; when nothing was added the resolution is empty (none is made up).
    /// </summary>
    public static (string Problem, string Resolution) SplitReport(string? problem, string? report)
    {
        var opening = (problem ?? "").Trim();
        var written = (report ?? "").Trim();
        if (written.Length == 0) return (opening, "");
        if (opening.Length == 0) return ("", written);
        // Walk both texts together, skipping white space: the report begins with the opening when all of the opening is matched.
        var index = 0;
        foreach (var expected in opening)
        {
            if (char.IsWhiteSpace(expected)) continue;
            while (index < written.Length && char.IsWhiteSpace(written[index])) index++;
            if (index >= written.Length || written[index] != expected) return (opening, written);
            index++;
        }
        var rest = written[index..].TrimStart(' ', '\t', '\r', '\n', '-', ':', ';', '.', ',').TrimEnd();
        return (opening, Collapse(rest).Length == 0 ? "" : rest);
    }

    public static string Notes(string number, string title, string problem, string resolution, string executor, string keyword = "", bool maintenance = false)
    {
        var text = new StringBuilder();
        text.Append("Preluat din ActivityApp");
        if (number.Length > 0) text.Append($", tichet {number}");
        if (title.Length > 0) text.Append($": {title}");
        if (problem.Length > 0) text.Append("\nProblemă: ").Append(problem);
        if (resolution.Length > 0) text.Append("\nRezolvare: ").Append(resolution);
        if (executor.Length > 0) text.Append("\nExecutant: ").Append(executor);
        if (keyword.Length > 0) text.Append(maintenance ? $"\nMentenanță: recunoscută după „{keyword}”." : $"\nMenționează „{keyword}”, dar punctul nu are un contract de mentenanță activ: înregistrată la cerere.");
        var value = TextNormalization.ForStorage(text.ToString().Replace("\r\n", "\n"));
        return value.Length <= OnDemandInterventionRules.NotesMaximumLength ? value : value[..(OnDemandInterventionRules.NotesMaximumLength - 1)] + "…";
    }

    /// <param name="tickets">The resolved tickets of the chosen types.</param>
    /// <param name="destinations">The chosen types and the destination of each (see <see cref="ActivityDestination"/>).</param>
    /// <param name="map">The ties already made: "partner", "workpoint" and "ticket".</param>
    /// <param name="localPoints">The work points of the beneficiaries tied to partners (key: beneficiary id); a beneficiary missing from it no longer exists.</param>
    /// <param name="today">For the rule that the date performed is not in the future.</param>
    public static IReadOnlyList<TicketPlan> Plan(IReadOnlyList<ActivityTicket> tickets, IReadOnlyDictionary<long, string> destinations, IReadOnlyList<ImportMapEntry> map,
        IReadOnlyDictionary<int, IReadOnlyList<WorkPoint>> localPoints, DateOnly today, IReadOnlyDictionary<long, IReadOnlyList<string>>? keywords = null,
        IReadOnlyDictionary<long, KeywordDecision>? decisions = null, IReadOnlySet<long>? ignoredPartners = null, IReadOnlySet<long>? waitingPartners = null, IReadOnlySet<long>? discarded = null)
    {
        var partners = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PartnerEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var points = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PointEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var tied = map.Where(entry => entry.Entity == TicketEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var plans = new List<TicketPlan>();
        foreach (var ticket in tickets.OrderBy(item => item.Id))
        {
            var title = TextNormalization.ForObjectNameOrCode(ticket.Title);
            var (problem, resolution) = SplitReport(ticket.Problem, ticket.Report);
            var executor = TextNormalization.ForObjectNameOrCode(ticket.Executor);
            var performed = ticket.EventEnd ?? ticket.TicketDate;
            var hash = ActivityPartnerPlanner.Hash(ticket.Id.ToString(), ticket.EventId.ToString(), title, problem, resolution, executor, performed?.ToString("O") ?? "");
            var source = new TicketSource(ticket.Id, ticket.TypeId, ticket.EventId, TextNormalization.ForObjectNameOrCode(ticket.Number), title, problem, resolution, ticket.Report, executor);
            TicketPlan Make(PlanAction action, string message, int? beneficiary = null, int? point = null, DateOnly? date = null, string notes = "") =>
                new(ticket.Id, title.Length > 0 ? title : $"Tichet {ticket.Id}", action, beneficiary, point, date, notes, source, message, hash);
            if (tied.ContainsKey(ticket.Id)) { plans.Add(Make(PlanAction.AlreadyLinked, "")); continue; }
            if (discarded is not null && discarded.Contains(ticket.Id)) { plans.Add(Make(PlanAction.Skipped, TicketDiscarded)); continue; }
            if (!destinations.TryGetValue(ticket.TypeId, out var destination)) { plans.Add(Make(PlanAction.Skipped, "Tipul tichetului nu este între cele alese.")); continue; }
            if (destination == ActivityDestination.Maintenance) { plans.Add(Make(PlanAction.Skipped, MaintenanceNotAvailable)); continue; }
            // A client that is a person (or one the operator discarded) is never taken: neither are its tickets. A client still to be verified, or that could not be taken yet, makes the ticket wait.
            if (ignoredPartners is not null && ignoredPartners.Contains(ticket.PartnerId)) { plans.Add(Make(PlanAction.Skipped, ClientNotTaken)); continue; }
            if (waitingPartners is not null && waitingPartners.Contains(ticket.PartnerId)) { plans.Add(Make(PlanAction.Waiting, ClientToVerify)); continue; }
            if (!partners.TryGetValue(ticket.PartnerId, out var beneficiary) || beneficiary == 0) { plans.Add(Make(PlanAction.Waiting, ClientNotImported)); continue; }
            if (!localPoints.TryGetValue(beneficiary, out var own) || own.Count == 0) { plans.Add(Make(PlanAction.Problem, "Beneficiarul legat de client nu mai există în BlazorStoc.")); continue; }
            var point = ticket.WorkPointId != 0 && points.TryGetValue(ticket.WorkPointId, out var local) && own.Any(item => item.Id == local) ? local : own.FirstOrDefault(item => item.IsPrimary)?.Id;
            if (point is null) { plans.Add(Make(PlanAction.Problem, "Beneficiarul nu are punct de lucru principal.")); continue; }
            if (performed is null) { plans.Add(Make(PlanAction.Problem, "Tichetul nu are nicio dată (nici a evenimentului, nici a tichetului).")); continue; }
            var date = DateOnly.FromDateTime(performed.Value);
            if (OnDemandInterventionRules.DateError(date, today) is { } dateError) { plans.Add(Make(PlanAction.Problem, dateError)); continue; }
            // What was done (not the problem, which may only name the maintenance as a wish) is searched for the words of the type. An exact word counts; a word that only resembles one (typing
            // mistake, cut short) is proposed to the administrator, unless he already decided about this ticket. Whether it counts at all is decided later, by the coverage of the point.
            var match = keywords is not null && keywords.TryGetValue(ticket.TypeId, out var words) ? ActivityKeywords.Match(resolution, words) : KeywordMatch.None;
            var decision = decisions is not null && decisions.TryGetValue(ticket.Id, out var decided) ? decided : (KeywordDecision?)null;
            var plan = Make(PlanAction.Create, "", beneficiary, point, date, Notes(source.Number, title, problem, resolution, executor));
            // A decision already taken by the operator (Confirmed: maintenance; Rejected: not) is final and asked no more; otherwise any match, exact or not, is proposed for validation when a
            // maintenance contract covers the point (see WithCoverage): a text with the word may say that the maintenance could NOT be done.
            plans.Add(match.Kind switch
            {
                KeywordMatchKind.None => plan,
                _ when decision == KeywordDecision.Confirmed => plan with { Keyword = match.Word, Decided = true },
                _ when decision == KeywordDecision.Rejected => plan,
                KeywordMatchKind.Exact => plan with { Keyword = match.Word, Found = match.Found, MatchKind = "exact" },
                _ => plan with { Approximate = match.Word, Found = match.Found, MatchKind = "approximate" }
            });
        }
        return plans;
    }

    /// <summary>The (beneficiary, work point) pairs of the tickets to be taken that mention maintenance, exactly or approximately: the ones whose coverage by a maintenance contract has to be asked.</summary>
    public static IReadOnlyList<(int BeneficiaryId, int WorkPointId)> KeywordPoints(IEnumerable<TicketPlan> plans) =>
        [.. plans.Where(plan => plan.Action == PlanAction.Create && (plan.Keyword.Length > 0 || plan.Approximate.Length > 0)).Select(plan => (plan.BeneficiaryId!.Value, plan.WorkPointId!.Value)).Distinct()];

    /// <summary>
    /// A ticket that mentions maintenance (a word found exactly or one that only resembles it) at a point that an active maintenance contract of the beneficiary covers
    /// (<paramref name="covered"/>) is added at once to the interventions of the beneficiary as an on-demand one and proposed to the operator, who says whether the maintenance was really done
    /// (a text with the word may say that it could not be), keeps it on demand or discards it. A decision already taken is applied at once (Confirmed: a maintenance intervention). Without a
    /// contract the answer would change nothing: the ticket is taken on demand, with a line in the notes when it mentions the word. The notes of the others are untouched.
    /// </summary>
    public static IReadOnlyList<TicketPlan> WithCoverage(IReadOnlyList<TicketPlan> plans, IReadOnlySet<(int BeneficiaryId, int WorkPointId)> covered) =>
        [.. plans.Select(plan =>
        {
            if (plan.Action != PlanAction.Create || (plan.Keyword.Length == 0 && plan.Approximate.Length == 0)) return plan;
            var isCovered = covered.Contains((plan.BeneficiaryId!.Value, plan.WorkPointId!.Value));
            var source = plan.Source!;
            if (plan.Decided) return plan with { Maintenance = isCovered, Notes = Notes(source.Number, source.Title, source.Problem, source.Resolution, source.Executor, plan.Keyword, isCovered) };
            var word = plan.Keyword.Length > 0 ? plan.Keyword : plan.Approximate;
            if (isCovered) return plan with { NeedsValidation = true, Message = $"Verificare mentenanță: «{plan.Written}» în tichet (cuvânt cheie «{word}»). Confirmă în „Evenimente de validat”." };
            return plan.Keyword.Length > 0
                ? plan with { Notes = Notes(source.Number, source.Title, source.Problem, source.Resolution, source.Executor, plan.Keyword, false) }
                : plan with { Approximate = "", Found = "", MatchKind = "" };
        })];
}
