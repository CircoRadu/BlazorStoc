using BlazorStoc.Components.Shared;
using BlazorStoc.Modules.ActivityImport;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Setari -> Preluare ActivityApp, sectiunea 6: what is taken from a ticket (pieces switched on or off, their destination, extra fields), what the tickets kept hold (fields_read), the notes
// made from it and rebuilt when the settings ask for more, and what the windows show. The paths that need the database run in MariaExtendedChecks.ActivityTickets.
public static class ActivityFieldSettingsChecks
{
    private static ActivityTicket Ticket(string problem = "Nu merge aerul", string report = "Nu merge aerul\nAm schimbat filtrul", string executor = "Ion Pop", IReadOnlyDictionary<string, string>? extras = null, string read = ActivityFieldMap.LegacyRead) =>
        new(7, 4, 100, 200, new DateTime(2026, 10, 1), "T-77", "Aer conditionat", problem, executor, 9, new DateTime(2026, 10, 2), 3, report, null, "", 21, extras, read);

    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("=== Informațiile din tichet (setări) ===");
        Map(check);
        Required(check);
        Notes(check);
        Rebuild(check);
        Structure(check);
        Window(check);
    }

    private static void Map(Action<bool, string> check)
    {
        var empty = ActivityFieldMap.Empty;
        check(ActivityFieldMap.Info.Where(item => !item.Mandatory).All(item => empty.IsEnabled(item.Key)) && empty.IsEnabled(ActivityFieldMap.Title) && empty.IsEnabled(ActivityFieldMap.Number)
              && empty.TargetOf(ActivityFieldMap.Problem) == ActivityFieldMap.Both && empty.TargetOf(ActivityFieldMap.Executor) == ActivityFieldMap.Both && empty.TargetOf(ActivityFieldMap.Report) == ActivityFieldMap.Separate
              && empty.ToJson() == "", "Ticket fields: by default everything is taken, the report goes to the separate record only, and nothing is saved");
        var map = ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii", ["Tickete.Subject"] = "Subject" }, [ActivityFieldMap.Resolution, ActivityFieldMap.Title],
            new Dictionary<string, string> { [ActivityFieldMap.Executor] = ActivityFieldMap.Notes, [ActivityFieldMap.Problem] = ActivityFieldMap.Both },
            [new ActivityExtraField("Tickete", "Observatii", "Observații client", ActivityFieldMap.Separate)], out var error);
        check(error is null && map is not null && map.Columns.Count == 1 && map.IsEnabled(ActivityFieldMap.Title) && !map.IsEnabled(ActivityFieldMap.Resolution) && map.TargetOf(ActivityFieldMap.Executor) == ActivityFieldMap.Notes
              && !map.Targets.ContainsKey(ActivityFieldMap.Problem) && map.Extras.Count == 1,
            "Ticket fields: the title cannot be switched off, the default column and the default destination are not kept, the rest is");
        var again = ActivityFieldMap.FromJson(map!.ToJson());
        check(again.ToJson() == map.ToJson() && !again.IsEnabled(ActivityFieldMap.Resolution) && again.Extras[0].Label == "Observații client" && again.Extras[0].Key == "x:Tickete.Observatii" && again.ColumnOf(ActivityFieldMap.Fields[1]) == "Detalii",
            "Ticket fields: the settings survive being written and read back");
        check(ActivityFieldMap.FromJson("{\"Tickete.DescriptionInternal\":\"Detalii\"}").Columns["Tickete.DescriptionInternal"] == "Detalii" && ActivityFieldMap.FromJson("{\"Tickete.DescriptionInternal\":\"Detalii\"}").Off.Count == 0
              && ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii" }, out _)!.ToJson() == "{\"Tickete.DescriptionInternal\":\"Detalii\"}",
            "Ticket fields: the format saved before these settings is still read, and written the same when only columns differ");
        ActivityFieldMap? Make(IEnumerable<ActivityExtraField> extras, out string? problem) => ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, extras, out problem);
        Make([new("Tickete", "A", "Nume", "both"), new("tickete", "a", "Alt nume", "both")], out var duplicate);
        Make([new("Tickete", "A", "Problema", "both")], out var clash);
        Make([new("Clienti", "A", "Nume", "both")], out var table);
        Make([new("Tickete", "A;B", "Nume", "both")], out var column);
        Make([new("Tickete", "A", "Nume", "altundeva")], out var target);
        Make(Enumerable.Range(0, 11).Select(index => new ActivityExtraField("Tickete", "C" + index, "Nume " + index, "both")), out var many);
        check(new[] { duplicate, clash, table, column, target, many }.All(text => text is not null), "Ticket fields: a duplicate column, a name already used, another table, a bad column name, another destination or too many extra fields are refused");
        check(ActivityFieldMap.Create(new Dictionary<string, string>(), ["altceva"], null, null, out var unknown) is null && unknown is not null, "Ticket fields: a piece of information that does not exist is refused");
    }

    private static void Required(Action<bool, string> check)
    {
        var all = ActivityFieldMap.Empty.Required;
        check(all.SetEquals(["title", "number", "executor", "problem", "report"]), "Ticket fields: by default the title, number, executor, problem and report are read");
        ActivityFieldMap Off(params string[] keys) => ActivityFieldMap.Create(new Dictionary<string, string>(), keys, null, null, out _)!;
        var noResolution = Off(ActivityFieldMap.Resolution, ActivityFieldMap.Problem).Required;
        var onlyResolution = Off(ActivityFieldMap.Problem, ActivityFieldMap.Report).Required;
        var none = Off(ActivityFieldMap.Problem, ActivityFieldMap.Resolution, ActivityFieldMap.Report, ActivityFieldMap.Executor).Required;
        check(!noResolution.Contains("problem") && noResolution.Contains("report") && onlyResolution.Contains("problem") && onlyResolution.Contains("report") && none.SetEquals(["title", "number"]),
            "Ticket fields: the problem and the report are read only when they or the resolution (made from both) are taken");
        var withExtra = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "Observatii", "Observații", "both")], out _)!;
        check(withExtra.Required.Contains("x:Tickete.Observatii"), "Ticket fields: an extra field is read");
        var collected = ActivityTicketFields.Collect(Ticket(extras: new Dictionary<string, string> { ["x:Tickete.Observatii"] = "Urgent", ["x:Tickete.Alta"] = "scoasa" }), Off(ActivityFieldMap.Executor, ActivityFieldMap.Problem, ActivityFieldMap.Resolution));
        check(collected.Executor == "" && collected.Problem == "" && collected.Report.Length > 0 && collected.Extras is null && collected.FieldsRead == "number,report,title",
            "Ticket fields: what is switched off is not kept with the ticket, and what was read is recorded with it");
        var kept = ActivityTicketFields.Collect(Ticket(extras: new Dictionary<string, string> { ["x:Tickete.Observatii"] = "Urgent", ["x:Tickete.Alta"] = "scoasa" }), withExtra);
        check(kept.Extras is { Count: 1 } && kept.Extras["x:Tickete.Observatii"] == "Urgent" && kept.FieldsRead == "executor,number,problem,report,title,x:Tickete.Observatii",
            "Ticket fields: only the extra fields of the settings are kept");
        check(ActivityTicketFields.Missing(withExtra, ActivityFieldMap.LegacyRead).SequenceEqual(["x:Tickete.Observatii"]) && ActivityTicketFields.Missing(ActivityFieldMap.Empty, ActivityFieldMap.LegacyRead).Count == 0
              && ActivityTicketFields.Missing(ActivityFieldMap.Empty, "number,title").Count == 3,
            "Ticket fields: a ticket kept before the settings existed holds everything the default asks for, and lacks only what was added since");
    }

    private static void Notes(Action<bool, string> check)
    {
        var ticket = Ticket();
        var source = ActivityTicketPlanner.SourceOf(ticket);
        var legacy = ActivityTicketPlanner.Notes(source.Number, source.Title, source.Problem, source.Resolution, source.Executor);
        check(ActivityTicketPlanner.Notes(source, ActivityFieldMap.Empty) == legacy && legacy.Contains("Executant: Ion Pop", StringComparison.Ordinal) && !legacy.Contains("Raport original", StringComparison.Ordinal),
            "Ticket notes: with the default settings the notes are exactly what they were before the settings existed");
        check(ActivityTicketPlanner.HashOf(ticket) == ActivityTicketPlanner.HashOf(ticket, ActivityFieldMap.Empty), "Ticket notes: the default settings do not change the value that detects a change of the ticket (no ticket in the queue is renewed for nothing)");
        ActivityFieldMap Make(string[]? off = null, Dictionary<string, string>? targets = null, ActivityExtraField[]? extras = null) => ActivityFieldMap.Create(new Dictionary<string, string>(), off, targets, extras, out _)!;
        var separate = Make(targets: new() { [ActivityFieldMap.Executor] = ActivityFieldMap.Separate });
        var textSeparate = ActivityTicketPlanner.Notes(ActivityTicketPlanner.SourceOf(ticket, separate), separate);
        check(!textSeparate.Contains("Executant", StringComparison.Ordinal) && textSeparate.Contains("Problema: Nu merge aerul", StringComparison.Ordinal) && textSeparate.Contains("Rezolvare: Am schimbat filtrul", StringComparison.Ordinal),
            "Ticket notes: a piece that goes to the separate record only is not in the notes");
        var off = Make(off: [ActivityFieldMap.Problem, ActivityFieldMap.Executor]);
        var offSource = ActivityTicketPlanner.SourceOf(ticket, off);
        var textOff = ActivityTicketPlanner.Notes(offSource, off);
        check(offSource.Problem == "" && offSource.Executor == "" && offSource.Resolution == "Am schimbat filtrul" && !textOff.Contains("Problema", StringComparison.Ordinal) && !textOff.Contains("Executant", StringComparison.Ordinal) && textOff.Contains("Rezolvare: Am schimbat filtrul", StringComparison.Ordinal),
            "Ticket notes: a piece that is switched off is not in the source nor in the notes, and the resolution is still told apart from the problem");
        var withExtra = Make(extras: [new("Tickete", "Observatii", "Observații client", ActivityFieldMap.Notes)], targets: new() { [ActivityFieldMap.Report] = ActivityFieldMap.Both });
        var extraTicket = Ticket(extras: new Dictionary<string, string> { ["x:Tickete.Observatii"] = "Poarta e închisă" });
        var textExtra = ActivityTicketPlanner.Notes(ActivityTicketPlanner.SourceOf(extraTicket, withExtra), withExtra, "mentenanță", true);
        check(textExtra.Contains("Observatii client: Poarta e inchisa", StringComparison.Ordinal) && textExtra.Contains("Raport original:", StringComparison.Ordinal) && textExtra.Contains("Mentenanta: recunoscuta dupa „mentenanta”.", StringComparison.Ordinal),
            "Ticket notes: an extra field and the report go to the notes when so chosen, followed by the remark about maintenance");
        check(ActivityTicketPlanner.HashOf(extraTicket, withExtra) != ActivityTicketPlanner.HashOf(extraTicket, ActivityFieldMap.Empty), "Ticket notes: an extra field changes the value that detects a change of the ticket");
        check(ActivityTicketPlanner.NotesItems(ActivityTicketPlanner.SourceOf(extraTicket, withExtra), withExtra) == "problem,resolution,report,executor,x:Tickete.Observatii", "Ticket notes: the pieces that went to the notes are listed in order, to know later what the generated text held");
    }

    private static void Rebuild(Action<bool, string> check)
    {
        var ticket = Ticket();
        var before = ActivityTicketPlanner.SourceOf(ticket);
        var oldMap = ActivityFieldMap.Empty;
        var oldPrefix = ActivityTicketPlanner.NotesPrefix(before.Number, before.Title, ActivityTicketPlanner.NoteLines(before, oldMap).Select(line => (line.Label, line.Value)));
        var withExtra = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "Observatii", "Observații client", ActivityFieldMap.Notes)], out _)!;
        var newTicket = Ticket(extras: new Dictionary<string, string> { ["x:Tickete.Observatii"] = "Poarta e închisă" });
        var newSource = ActivityTicketPlanner.SourceOf(newTicket, withExtra);
        var newPrefix = ActivityTicketPlanner.NotesPrefix(newSource.Number, newSource.Title, ActivityTicketPlanner.NoteLines(newSource, withExtra).Select(line => (line.Label, line.Value)));
        var generated = ActivityTicketPlanner.Notes(source: before, map: oldMap);
        var rebuilt = ActivityTicketPlanner.RebuildNotes(generated, oldPrefix, newPrefix);
        check(rebuilt is not null && rebuilt.Contains("Observatii client: Poarta e inchisa", StringComparison.Ordinal) && rebuilt.StartsWith("Preluat din ActivityApp, tichet T-77: Aer conditionat", StringComparison.Ordinal),
            "Ticket notes refresh: notes that are still what the import wrote get the new lines");
        var withRemark = ActivityTicketPlanner.Notes(before, oldMap, "mentenanță", true, uncovered: true);
        var keptRemark = ActivityTicketPlanner.RebuildNotes(withRemark, oldPrefix, newPrefix);
        check(keptRemark is not null && keptRemark.EndsWith("punctul nu are un contract de mentenanta activ (scadenta nu s-a mutat).", StringComparison.Ordinal) && keptRemark.Contains("Observatii client", StringComparison.Ordinal),
            "Ticket notes refresh: the remark about maintenance after the lines of the ticket is kept");
        check(ActivityTicketPlanner.RebuildNotes(generated + "\nAm sunat clientul", oldPrefix, newPrefix) is null && ActivityTicketPlanner.RebuildNotes("Text scris de operator", oldPrefix, newPrefix) is null
              && ActivityTicketPlanner.RebuildNotes(generated.Replace("Ion Pop", "Alt Executant"), oldPrefix, newPrefix) is null,
            "Ticket notes refresh: notes an operator edited (added to, rewritten or changed inside) are left as they are");
    }

    private static void Structure(Action<bool, string> check)
    {
        var map = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "Observatii", "Observații client", "both"), new("EvenimenteAgent", "Rezumat", "Rezumat", "both"), new("Tickete", "Lipsa", "Altceva", "both")], out _)!;
        string TypeOf(ColumnKind kind) => kind switch { ColumnKind.Text => "nvarchar", ColumnKind.Date => "datetime", ColumnKind.Flag => "bit", _ => "bigint" };
        IReadOnlyList<ActivityColumn> actual = [.. ActivityContract.Tables.SelectMany(table => table.Columns.Select(column => new ActivityColumn(table.Name, column.Name, TypeOf(column.Kind)))),
            new("Tickete", "Observatii", "nvarchar"), new("EvenimenteAgent", "Rezumat", "nvarchar"), new("Tickete", "NumarMasina", "int")];
        var report = ActivityStructureChecker.Check(actual, null, map);
        check(report.Extras.Select(extra => extra.Key).SequenceEqual(["x:Tickete.Observatii", "x:EvenimenteAgent.Rezumat"]) && report.Issues.Any(issue => issue.Problem.StartsWith("coloana aleasă pentru „Altceva” lipsește", StringComparison.Ordinal)),
            "Ticket fields: the extra fields whose column exists are read, one that is gone is reported like a chosen column that is gone");
        var wrong = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "NumarMasina", "Număr", "both")], out _)!;
        check(ActivityStructureChecker.Check(actual, null, wrong).Issues.Any(issue => issue.Problem.Contains("nu are text", StringComparison.Ordinal)) && ActivityStructureChecker.Check(actual, null, wrong).Extras.Count == 0,
            "Ticket fields: an extra field whose column holds no text is not read and is reported");
    }

    private sealed class Visibility(TicketVisibility value) : IActivityTicketVisibility
    {
        public Task<TicketVisibility> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private static void Window(Action<bool, string> check)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IActivityTicketVisibility>(new Visibility(new TicketVisibility(true, false, false, true, [new("x:Tickete.Observatii", "Observații client")])));
        var cut = context.Render<TicketInfoRows>(parameters => parameters.Add(p => p.Problem, "Nu merge").Add(p => p.Resolution, "Reparat").Add(p => p.Report, "Raport complet").Add(p => p.Executor, "Ion Pop")
            .Add(p => p.Extras, new Dictionary<string, string> { ["x:Tickete.Observatii"] = "Poarta e închisă", ["x:Tickete.Scoasa"] = "nu apare" }).Add(p => p.ShowEmpty, true));
        var text = cut.Markup;
        check(text.Contains("Nu merge", StringComparison.Ordinal) && !text.Contains("Reparat", StringComparison.Ordinal) && !text.Contains("Raport complet", StringComparison.Ordinal) && text.Contains("Ion Pop", StringComparison.Ordinal)
              && text.Contains("Observații client", StringComparison.Ordinal) && text.Contains("Poarta e închisă", StringComparison.Ordinal) && !text.Contains("nu apare", StringComparison.Ordinal),
            "Ticket window: only the pieces switched on are shown, with the extra fields of the settings under their names");
        using var plain = new BunitContext();
        var all = plain.Render<TicketInfoRows>(parameters => parameters.Add(p => p.Problem, "Nu merge").Add(p => p.Resolution, "Reparat").Add(p => p.Report, "Raport complet").Add(p => p.Executor, ""));
        check(all.Markup.Contains("Reparat", StringComparison.Ordinal) && all.Markup.Contains("Raport complet", StringComparison.Ordinal) && !all.Markup.Contains("Executant", StringComparison.Ordinal),
            "Ticket window: without the import module everything is shown, and an empty piece is left out when empty pieces are not asked for");
    }
}
