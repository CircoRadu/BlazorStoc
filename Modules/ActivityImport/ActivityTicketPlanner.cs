using System.Text;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// What the import of resolved tickets will do, decided without touching anything. A resolved ticket (the ticket and the event that closed it) of a chosen type is NEVER recorded by itself:
// it goes to the queue of the events to validate (Create), for the beneficiary the ticket's client was tied to (stage 2) and the work point of the ticket (or the main point when the ticket has
// none that was tied); an operator decides there whether it is maintenance, a service on demand, or to be discarded, and only then the intervention is recorded. The queue shows what the
// ticket looks like: a type taken as maintenance, or a keyword in what was done (exact, or one that only resembles it), mean "maintenance"; the rest mean "service". The problem and the
// resolution are told apart from the report of the event (see SplitReport). Tickets whose client was not imported are not errors: they wait, and are taken after the partner is.

public sealed record TicketSource(long TicketId, long TypeId, long EventId, string Number, string Title, string Problem, string Resolution, string Report, string Executor);

public sealed record TicketPlan(long SourceId, string Title, PlanAction Action, int? BeneficiaryId, int? WorkPointId, DateOnly? PerformedOn, string Notes, TicketSource? Source, string Message, string Hash,
    string Keyword = "", string Approximate = "", string Found = "", bool NeedsValidation = false, string MatchKind = "")
{
    /// <summary>How the text of the ticket wrote the word that only resembles a keyword.</summary>
    public string Written => Found;
}

public static class ActivityTicketPlanner
{
    public const string TicketEntity = "ticket";
    public const string TicketDiscarded = "Tichet discardat: nu se mai preia.";
    public const string ClientNotTaken = "Clientul tichetului nu se preia (persoană fizică sau discardat): tichetul nu se preia.";
    public const string ClientToVerify = "Clientul tichetului este de verificat: tichetul așteaptă clarificarea beneficiarului.";
    public const string ClientNotImported = "Clientul tichetului nu a putut fi preluat încă (vezi problemele): tichetul așteaptă.";
    public const string AwaitingApproval = "Tichetul așteaptă validarea în „Evenimente de validat”.";
    public const string NoKeyword = "Tichet fără cuvânt cheie din nomenclator (și de un tip care nu e de mentenanță): nu se preia.";
    public const string ServiceKind = "service";
    /// <summary>The keyword shown for a ticket taken because its TYPE is a maintenance one (no word is searched in the text).</summary>
    public const string TypeKeyword = "tip de mentenanță";

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

    /// <summary>The ticket as the intervention keeps it apart: the problem and the resolution told apart from the report, the executor, the number.</summary>
    public static TicketSource SourceOf(ActivityTicket ticket)
    {
        var title = TextNormalization.ForObjectNameOrCode(ticket.Title);
        var (problem, resolution) = SplitReport(ticket.Problem, ticket.Report);
        return new TicketSource(ticket.Id, ticket.TypeId, ticket.EventId, TextNormalization.ForObjectNameOrCode(ticket.Number), title, problem, resolution, ticket.Report, TextNormalization.ForObjectNameOrCode(ticket.Executor));
    }

    public static string HashOf(ActivityTicket ticket)
    {
        var source = SourceOf(ticket);
        return ActivityPartnerPlanner.Hash(ticket.Id.ToString(), ticket.EventId.ToString(), source.Title, source.Problem, source.Resolution, source.Executor, (ticket.EventEnd ?? ticket.TicketDate)?.ToString("O") ?? "");
    }

    public static string Notes(string number, string title, string problem, string resolution, string executor, string keyword = "", bool maintenance = false, bool uncovered = false)
    {
        var text = new StringBuilder();
        text.Append("Preluat din ActivityApp");
        if (number.Length > 0) text.Append($", tichet {number}");
        if (title.Length > 0) text.Append($": {title}");
        if (problem.Length > 0) text.Append("\nProblemă: ").Append(problem);
        if (resolution.Length > 0) text.Append("\nRezolvare: ").Append(resolution);
        if (executor.Length > 0) text.Append("\nExecutant: ").Append(executor);
        if (maintenance && uncovered) text.Append("\nMentenanță validată de operator; punctul nu are un contract de mentenanță activ (scadența nu s-a mutat).");
        else if (keyword == TypeKeyword) text.Append(maintenance ? "\nMentenanță: tichet de un tip de mentenanță." : "\nTichet de un tip de mentenanță, validat ca service la cerere.");
        else if (keyword.Length > 0) text.Append(maintenance ? $"\nMentenanță: recunoscută după „{keyword}”." : $"\nMenționează „{keyword}”, validat ca service la cerere.");
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
        IReadOnlySet<long>? ignoredPartners = null, IReadOnlySet<long>? waitingPartners = null, IReadOnlySet<long>? discarded = null, IReadOnlySet<long>? queued = null)
    {
        var partners = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PartnerEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var points = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PointEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var tied = map.Where(entry => entry.Entity == TicketEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var plans = new List<TicketPlan>();
        foreach (var ticket in tickets.OrderBy(item => item.Id))
        {
            var source = SourceOf(ticket);
            var (title, problem, resolution, executor) = (source.Title, source.Problem, source.Resolution, source.Executor);
            var performed = ticket.EventEnd ?? ticket.TicketDate;
            var hash = HashOf(ticket);
            TicketPlan Make(PlanAction action, string message, int? beneficiary = null, int? point = null, DateOnly? date = null, string notes = "") =>
                new(ticket.Id, title.Length > 0 ? title : $"Tichet {ticket.Id}", action, beneficiary, point, date, notes, source, message, hash);
            if (tied.ContainsKey(ticket.Id)) { plans.Add(Make(PlanAction.AlreadyLinked, "")); continue; }
            if (queued is not null && queued.Contains(ticket.Id)) { plans.Add(Make(PlanAction.AlreadyLinked, AwaitingApproval)); continue; }
            if (discarded is not null && discarded.Contains(ticket.Id)) { plans.Add(Make(PlanAction.Skipped, TicketDiscarded)); continue; }
            if (!destinations.TryGetValue(ticket.TypeId, out var destination)) { plans.Add(Make(PlanAction.Skipped, "Tipul tichetului nu este între cele alese.")); continue; }
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
            // What was done (not the problem, which may only name the maintenance as a wish) is searched for the words of the type. A type taken as maintenance needs no word. The result only
            // says what to propose in the queue (maintenance or service); the operator decides.
            var byType = destination == ActivityDestination.Maintenance;
            var match = byType ? new KeywordMatch(KeywordMatchKind.Exact, TypeKeyword, "tichet de mentenanță")
                : keywords is not null && keywords.TryGetValue(ticket.TypeId, out var words) ? ActivityKeywords.Match(resolution, words) : KeywordMatch.None;
            // Only a maintenance type, or a ticket whose text has a keyword of the nomenclator, reaches the queue: any other ticket is not taken.
            if (match.Kind == KeywordMatchKind.None) { plans.Add(Make(PlanAction.Skipped, NoKeyword)); continue; }
            var plan = Make(PlanAction.Create, AwaitingApproval, beneficiary, point, date, Notes(source.Number, title, problem, resolution, executor)) with { NeedsValidation = true };
            plans.Add(match.Kind switch
            {
                KeywordMatchKind.Exact => plan with { Keyword = match.Word, Found = match.Found, MatchKind = byType ? "type" : "exact" },
                _ => plan with { Approximate = match.Word, Found = match.Found, MatchKind = "approximate" }
            });
        }
        return plans;
    }
}
