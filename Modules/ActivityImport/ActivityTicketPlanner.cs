using System.Text;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.ActivityImport;

// What the import of resolved tickets will do, decided without touching anything. A resolved ticket (the ticket and the event that closed it) of a chosen type is NEVER recorded by itself:
// it goes to the queue of the events to validate (Create), for the beneficiary the ticket's client was tied to (stage 2) and the work point of the ticket (or the main point when the ticket has
// none that was tied); an operator decides there whether it is maintenance, a service on demand, or to be discarded, and only then the intervention is recorded. The queue shows what the
// ticket looks like: a type taken as maintenance, or a keyword in what was done (exact, or one that only resembles it), mean "maintenance"; the rest mean "service". The problem and the
// resolution are told apart from the report of the event (see SplitReport). Tickets whose client was not imported are not errors: they wait, and are taken after the partner is.

public sealed record TicketSource(long TicketId, long TypeId, long EventId, string Number, string Title, string Problem, string Resolution, string Report, string Executor, IReadOnlyDictionary<string, string>? Extras = null, DateTime? Finished = null);

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
    /// <summary>The colour (Label) of an event in the state "Finalizat" in ActivityApp (light blue; Alocat, In lucru and Incident have other colours).</summary>
    public const long FinalizedLabel = 2;
    public const string NotFinished = "Evenimentul nu este în starea Finalizat în ActivityApp: tichetul se preia după ce este finalizat.";
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

    /// <summary>
    /// The ticket as the intervention keeps it apart: the problem and the resolution told apart from the report, the executor, the number and the extra fields. With the settings of the fields
    /// (<paramref name="map"/>) a piece of information that is switched off is empty and only the extra fields of the settings are kept; without them everything the ticket holds is kept.
    /// </summary>
    public static TicketSource SourceOf(ActivityTicket ticket, ActivityFieldMap? map = null)
    {
        var title = TextNormalization.ForObjectNameOrCode(ticket.Title);
        var (problem, resolution) = SplitReport(ticket.Problem, ticket.Report);
        var report = ticket.Report;
        var executor = TextNormalization.ForObjectNameOrCode(ticket.Executor);
        IReadOnlyDictionary<string, string>? extras = ticket.Extras;
        if (map is not null)
        {
            if (!map.IsEnabled(ActivityFieldMap.Problem)) problem = "";
            if (!map.IsEnabled(ActivityFieldMap.Resolution)) resolution = "";
            if (!map.IsEnabled(ActivityFieldMap.Report)) report = "";
            if (!map.IsEnabled(ActivityFieldMap.Executor)) executor = "";
            extras = ticket.Extras is null ? null : map.Extras.Where(extra => ticket.Extras.ContainsKey(extra.Key)).ToDictionary(extra => extra.Key, extra => ticket.Extras[extra.Key]);
        }
        return new TicketSource(ticket.Id, ticket.TypeId, ticket.EventId, TextNormalization.ForObjectNameOrCode(ticket.Number), title, problem, resolution, report, executor, extras is { Count: > 0 } ? extras : null, ticket.EventEnd);
    }

    public static string HashOf(ActivityTicket ticket, ActivityFieldMap? map = null)
    {
        var source = SourceOf(ticket, map);
        var values = new List<string> { ticket.Id.ToString(), ticket.EventId.ToString(), source.Title, source.Problem, source.Resolution, source.Executor, (ticket.EventEnd ?? ticket.TicketDate)?.ToString("O") ?? "", ticket.Gps };
        // Only what the settings add changes the value: with the default settings it is the same as before, so no ticket waiting in the queue is renewed for nothing.
        if (map is not null && map.IsEnabled(ActivityFieldMap.Report) && map.TargetOf(ActivityFieldMap.Report) != ActivityFieldMap.Separate) values.Add(source.Report);
        if (source.Extras is not null) foreach (var pair in source.Extras.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { values.Add(pair.Key); values.Add(pair.Value); }
        return ActivityPartnerPlanner.Hash([.. values]);
    }

    /// <summary>The lines of the notes that come from the ticket, in order: problem, resolution, report, executor (the pieces that are on and go to the notes) and the extra fields that go to the notes.</summary>
    public static IReadOnlyList<(string Key, string Label, string Value)> NoteLines(TicketSource source, ActivityFieldMap map)
    {
        var lines = new List<(string Key, string Label, string Value)>();
        void Add(string key, string label, string value) { if (map.IsEnabled(key) && map.InNotes(key) && value.Length > 0) lines.Add((key, label, value)); }
        Add(ActivityFieldMap.Problem, map.NoteLabelOf(ActivityFieldMap.Problem), source.Problem);
        Add(ActivityFieldMap.Resolution, map.NoteLabelOf(ActivityFieldMap.Resolution), source.Resolution);
        Add(ActivityFieldMap.Report, map.NoteLabelOf(ActivityFieldMap.Report), source.Report);
        Add(ActivityFieldMap.Executor, map.NoteLabelOf(ActivityFieldMap.Executor), source.Executor);
        foreach (var extra in map.Extras)
            if (extra.Target is ActivityFieldMap.Notes or ActivityFieldMap.Both && source.Extras is not null && source.Extras.TryGetValue(extra.Key, out var value) && value.Length > 0) lines.Add((extra.Key, extra.Label, value));
        // The order chosen by dragging (a stable sort: lines with the same place keep the order above).
        return [.. lines.OrderBy(line => map.PlaceOf(line.Key))];
    }

    /// <summary>The label of the end of the event in the notes, and the format of its date.</summary>
    public const string FinishedFormat = "dd.MM.yyyy HH:mm";

    /// <summary>The required lines of the notes, always first and in this order: title, number, id of the ticket, id of the event, end of the event (what the ticket does not hold is left out).</summary>
    public static IReadOnlyList<(string Label, string Value)> RequiredLines(TicketSource source)
    {
        var lines = new List<(string, string)>();
        if (source.Title.Length > 0) lines.Add(("Titlul tichetului", source.Title));
        if (source.Number.Length > 0) lines.Add(("Numărul tichetului", source.Number));
        if (source.TicketId > 0) lines.Add(("ID ticket", source.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (source.EventId > 0) lines.Add(("ID eveniment", source.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (source.Finished is { } finished) lines.Add(("Data finalizării evenimentului", finished.ToString(FinishedFormat, System.Globalization.CultureInfo.InvariantCulture)));
        return lines;
    }

    /// <summary>The keys of the pieces of information that went to the notes (kept with the source record, to know later what the generated text held).</summary>
    public static string NotesItems(TicketSource source, ActivityFieldMap map) => string.Join(',', NoteLines(source, map).Select(line => line.Key));

    /// <summary>The generated start of the notes: the header and the lines of the ticket, without the remarks about maintenance and without any cut.</summary>
    public static string NotesPrefix(string number, string title, IEnumerable<(string Label, string Value)> lines) =>
        NotesPrefix(new TicketSource(0, 0, 0, number, title, "", "", "", "", null), lines);

    /// <summary>
    /// The generated start of the notes (BUG-0023): "Preluat din ActivityApp", then each line as "**Label**: value" with a blank line between lines: first the required ones (title, number, ids, end of the
    /// event), then the ones of the settings in the order chosen. The labels are written between ** so that the windows that show the notes draw them bold (<see cref="NotesMarkup"/>).
    /// </summary>
    public static string NotesPrefix(TicketSource source, IEnumerable<(string Label, string Value)> lines)
    {
        var text = new StringBuilder();
        text.Append("Preluat din ActivityApp");
        foreach (var (label, value) in RequiredLines(source).Concat(lines)) text.Append("\n\n").Append(NotesMarkup.Bold(label)).Append(": ").Append(Compact(value));
        return text.ToString();
    }

    /// <summary>A value without the empty lines and the spaces around lines that the source keeps (BUG-0026): only the blank line between two fields stays.</summary>
    public static string Compact(string value) =>
        string.Join("\n", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).Where(line => line.Length > 0));

    /// <summary>The notes of an intervention taken from the ticket with the settings of the fields.</summary>
    public static string Notes(TicketSource source, ActivityFieldMap map, string keyword = "", bool maintenance = false, bool uncovered = false) =>
        Finish(NotesPrefix(source, NoteLines(source, map).Select(line => (line.Label, line.Value))), keyword, maintenance, uncovered);

    public static string Notes(string number, string title, string problem, string resolution, string executor, string keyword = "", bool maintenance = false, bool uncovered = false) =>
        Finish(NotesPrefix(number, title, new[] { ("Problemă", problem), ("Rezolvare", resolution), ("Executant", executor) }.Where(line => line.Item2.Length > 0)), keyword, maintenance, uncovered);

    // The remarks about maintenance after the lines of the ticket, then the text as it is stored (with the cut at the maximum length).
    private static string Finish(string prefix, string keyword, bool maintenance, bool uncovered)
    {
        var text = new StringBuilder(prefix);
        if (maintenance && uncovered) text.Append("\nMentenanță validată de operator; punctul nu are un contract de mentenanță activ (scadența nu s-a mutat).");
        else if (keyword == TypeKeyword) text.Append(maintenance ? "\nMentenanță: tichet de un tip de mentenanță." : "\nTichet de un tip de mentenanță, validat ca service la cerere.");
        else if (keyword.Length > 0) text.Append(maintenance ? $"\nMentenanță: recunoscută după „{keyword}”." : $"\nMenționează „{keyword}”, validat ca service la cerere.");
        var value = TextNormalization.ForStorage(text.ToString().Replace("\r\n", "\n"));
        return value.Length <= OnDemandInterventionRules.NotesMaximumLength ? value : value[..(OnDemandInterventionRules.NotesMaximumLength - 1)] + "…";
    }

    /// <summary>
    /// The notes with the generated start replaced by <paramref name="newPrefix"/>, when <paramref name="current"/> is still what the import wrote: the old generated start, then nothing or the one
    /// remark about maintenance the import adds. Null when the text is anything else (an operator edited it, or it was cut): it is then left as it is.
    /// </summary>
    public static string? RebuildNotes(string current, string oldPrefix, string newPrefix)
    {
        var old = TextNormalization.ForStorage(oldPrefix.Replace("\r\n", "\n"));
        if (!current.StartsWith(old, StringComparison.Ordinal)) return null;
        var tail = current[old.Length..];
        if (tail.Length > 0 && !IsGeneratedRemark(tail)) return null;
        var value = TextNormalization.ForStorage((newPrefix + tail).Replace("\r\n", "\n"));
        return value.Length <= OnDemandInterventionRules.NotesMaximumLength ? value : value[..(OnDemandInterventionRules.NotesMaximumLength - 1)] + "…";
    }

    // The notes are stored without diacritics (TextNormalization.ForStorage), so the remarks are compared the way they are stored.
    private static readonly string[] RemarkStarts = [.. new[] { "Mentenanță validată de operator;", "Mentenanță: ", "Tichet de un tip de mentenanță", "Menționează „" }.Select(text => "\n" + TextNormalization.ForStorage(text))];

    private static bool IsGeneratedRemark(string tail) =>
        tail.StartsWith('\n') && !tail.AsSpan(1).Contains('\n') && RemarkStarts.Any(start => tail.StartsWith(start, StringComparison.Ordinal));

    /// <param name="tickets">The resolved tickets of the chosen types.</param>
    /// <param name="destinations">The chosen types and the destination of each (see <see cref="ActivityDestination"/>).</param>
    /// <param name="map">The ties already made: "partner", "workpoint" and "ticket".</param>
    /// <param name="localPoints">The work points of the beneficiaries tied to partners (key: beneficiary id); a beneficiary missing from it no longer exists.</param>
    /// <param name="today">For the rule that the date performed is not in the future.</param>
    public static IReadOnlyList<TicketPlan> Plan(IReadOnlyList<ActivityTicket> tickets, IReadOnlyDictionary<long, string> destinations, IReadOnlyList<ImportMapEntry> map,
        IReadOnlyDictionary<int, IReadOnlyList<WorkPoint>> localPoints, DateOnly today, IReadOnlyDictionary<long, IReadOnlyList<string>>? keywords = null,
        IReadOnlySet<long>? ignoredPartners = null, IReadOnlySet<long>? waitingPartners = null, IReadOnlySet<long>? discarded = null, IReadOnlySet<long>? queued = null, ActivityFieldMap? fields = null)
    {
        var partners = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PartnerEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var points = map.Where(entry => entry.Entity == ActivityPartnerPlanner.PointEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var tied = map.Where(entry => entry.Entity == TicketEntity).ToDictionary(entry => entry.SourceId, entry => entry.LocalId);
        var plans = new List<TicketPlan>();
        foreach (var ticket in tickets.OrderBy(item => item.Id))
        {
            var source = SourceOf(ticket, fields);
            var (title, problem, resolution, executor) = (source.Title, source.Problem, source.Resolution, source.Executor);
            var performed = ticket.EventEnd ?? ticket.TicketDate;
            var hash = HashOf(ticket, fields);
            TicketPlan Make(PlanAction action, string message, int? beneficiary = null, int? point = null, DateOnly? date = null, string notes = "") =>
                new(ticket.Id, title.Length > 0 ? title : $"Tichet {ticket.Id}", action, beneficiary, point, date, notes, source, message, hash);
            if (tied.ContainsKey(ticket.Id)) { plans.Add(Make(PlanAction.AlreadyLinked, "")); continue; }
            if (queued is not null && queued.Contains(ticket.Id)) { plans.Add(Make(PlanAction.AlreadyLinked, AwaitingApproval)); continue; }
            if (discarded is not null && discarded.Contains(ticket.Id)) { plans.Add(Make(PlanAction.Skipped, TicketDiscarded)); continue; }
            // Only a finished event is taken: in ActivityApp the state of the event (Alocat, In lucru, Finalizat, Incident) is its colour (Label). Without the column the state is not known and the ticket is not held back.
            if (ticket.EventLabel is { } label && label != FinalizedLabel) { plans.Add(Make(PlanAction.Skipped, NotFinished)); continue; }
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
            if (OnDemandInterventionRules.DateError(date, today) is { } dateError) { plans.Add(Make(PlanAction.Problem, date > today ? $"Evenimentul din ActivityApp este programat pentru {StockMovementRules.DisplayDate(date)} (în viitor), deci lucrarea nu a fost încă efectuată. Nu trebuie făcut nimic aici: tichetul se preia singur la un import de după această dată; dacă lucrarea s-a făcut mai devreme, corectează data evenimentului în ActivityApp." : dateError)); continue; }
            // What was done (not the problem, which may only name the maintenance as a wish) is searched for the words of the type. A type taken as maintenance needs no word. The result only
            // says what to propose in the queue (maintenance or service); the operator decides.
            var byType = destination == ActivityDestination.Maintenance;
            var match = byType ? new KeywordMatch(KeywordMatchKind.Exact, TypeKeyword, "tichet de mentenanță")
                : keywords is not null && keywords.TryGetValue(ticket.TypeId, out var words) ? ActivityKeywords.Match(resolution, words) : KeywordMatch.None;
            // Only a maintenance type, or a ticket whose text has a keyword of the nomenclator, reaches the queue: any other ticket is not taken.
            if (match.Kind == KeywordMatchKind.None) { plans.Add(Make(PlanAction.Skipped, NoKeyword)); continue; }
            var plan = Make(PlanAction.Create, AwaitingApproval, beneficiary, point, date, fields is null ? Notes(source.Number, title, problem, resolution, executor) : Notes(source, fields)) with { NeedsValidation = true };
            plans.Add(match.Kind switch
            {
                KeywordMatchKind.Exact => plan with { Keyword = match.Word, Found = match.Found, MatchKind = byType ? "type" : "exact" },
                _ => plan with { Approximate = match.Word, Found = match.Found, MatchKind = "approximate" }
            });
        }
        return plans;
    }
}
