using Microsoft.AspNetCore.Components;
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
        Labels(check);
        SampleRules(check);
        StructureChange(check);
        SampleWindow(check);
    }

    private static TicketSample Sample(long id = 9, string problem = "Nu merge aerul", string report = "Nu merge aerul\nAm schimbat filtrul", string executor = "Ion Pop", bool older = true) =>
        new(id, "T-" + id, "Aer conditionat", new DateTime(2026, 10, 1, 8, 30, 0), [
            new("Tickete", [new("ID", "bigint", id.ToString(), false), new("NumeTicket", "nvarchar", "T-" + id, true), new("Subject", "nvarchar", "Aer conditionat", true), new("DescriptionInternal", "nvarchar", problem, true),
                new("Detalii", "nvarchar", "Alt text", true), new("ProcesatDe", "nvarchar", "Birou", true), new("Observatii", "nvarchar", "Poarta inchisa", true)]),
            new("EvenimenteAgent", [new("DescriptionInternal", "nvarchar", report, true)])], executor, older);

    private static void Labels(Action<bool, string> check)
    {
        ActivityFieldMap? Make(Dictionary<string, string> labels, out string? error, IEnumerable<ActivityExtraField>? extras = null) => ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, extras, labels, out error);
        var map = Make(new() { [ActivityFieldMap.Problem] = "Defect", [ActivityFieldMap.Executor] = "Tehnician", [ActivityFieldMap.Title] = "Altceva", [ActivityFieldMap.Report] = "Raportul original" }, out var error)!;
        check(error is null && map.Labels.Count == 2 && map.LabelOf(ActivityFieldMap.Problem) == "Defect" && map.LabelOf(ActivityFieldMap.Report) == "Raportul original" && map.LabelOf(ActivityFieldMap.Title) == "Titlul tichetului"
              && map.NoteLabelOf(ActivityFieldMap.Executor) == "Tehnician" && map.NoteLabelOf(ActivityFieldMap.Resolution) == "Rezolvare" && ActivityFieldMap.Empty.NoteLabelOf(ActivityFieldMap.Problem) == "Problemă",
            "Ticket fields: the name of an optional piece can be chosen (the title and the number keep theirs, a name equal to the usual one is not kept) and the notes keep their usual names otherwise");
        var again = ActivityFieldMap.FromJson(map.ToJson());
        check(again.ToJson() == map.ToJson() && again.LabelOf(ActivityFieldMap.Problem) == "Defect" && ActivityFieldMap.FromJson("{\"V\":2,\"Columns\":{},\"Off\":[],\"Targets\":{},\"Extras\":[]}").Labels.Count == 0,
            "Ticket fields: the names chosen survive being written and read back, and settings saved before names could be chosen are still read");
        Make(new() { [ActivityFieldMap.Problem] = "Rezolvarea" }, out var clash);
        Make(new() { [ActivityFieldMap.Problem] = "Nume", [ActivityFieldMap.Report] = "nume" }, out var twice);
        Make(new() { [ActivityFieldMap.Problem] = new string('a', 61) }, out var longName);
        Make(new() { [ActivityFieldMap.Problem] = "Observații" }, out var withExtra, [new ActivityExtraField("Tickete", "Observatii", "observații", "both")]);
        check(new[] { clash, twice, longName }.All(text => text is not null) && withExtra is null, "Ticket fields: a name used by another piece (capitals do not matter) or longer than 60 characters is refused; an extra field may repeat a name");
        var ticket = Ticket();
        var text = ActivityTicketPlanner.Notes(ActivityTicketPlanner.SourceOf(ticket, map), map);
        check(text.Contains("Defect: Nu merge aerul", StringComparison.Ordinal) && text.Contains("Tehnician: Ion Pop", StringComparison.Ordinal) && text.Contains("Rezolvare: Am schimbat filtrul", StringComparison.Ordinal) && !text.Contains("Problema:", StringComparison.Ordinal),
            "Ticket fields: the notes carry the names chosen");
        check(map.Describe().Contains("numit „Defect”", StringComparison.Ordinal), "Ticket fields: the journal names the renamed pieces");
    }

    private static void SampleRules(Action<bool, string> check)
    {
        var sample = Sample();
        var preview = ActivityTicketSampleRules.Preview(sample, ActivityFieldMap.Empty);
        string Value(string key) => preview.Items.Single(item => item.Key == key).Value;
        check(preview.Items.Count == 6 && Value("problem") == "Nu merge aerul" && Value("resolution") == "Am schimbat filtrul" && Value("executor") == "Ion Pop" && Value("title") == "Aer conditionat" && Value("number") == "T-9"
              && preview.Notes.Contains("Executant: Ion Pop", StringComparison.Ordinal) && preview.Notes.StartsWith("Preluat din ActivityApp, tichet T-9: Aer conditionat", StringComparison.Ordinal),
            "Example ticket: with the default settings each piece gets its usual column, the executor comes from the agents of the event, and the notes are the ones the import would write");
        var moved = ActivityFieldMap.Create(new Dictionary<string, string> { ["Tickete.DescriptionInternal"] = "Detalii" }, [ActivityFieldMap.Executor], null, [new ActivityExtraField("Tickete", "Observatii", "Observații client", "both")], out _)!;
        var other = ActivityTicketSampleRules.Preview(sample, moved);
        check(other.Items.Single(item => item.Key == "problem").Value == "Alt text" && other.Items.Single(item => item.Key == "executor").Target == "oprit" && other.Items.Single(item => item.Key == "executor").Value == ""
              && other.Items.Single(item => item.Key == "x:Tickete.Observatii").Value == "Poarta inchisa" && !other.Notes.Contains("Executant", StringComparison.Ordinal) && other.Notes.Contains("Observatii client: Poarta inchisa", StringComparison.Ordinal),
            "Example ticket: the preview follows the settings on the screen (another column, a piece switched off, an extra field)");
        check(ActivityTicketSampleRules.Format(null) is null && ActivityTicketSampleRules.Format(new DateTime(2026, 10, 1, 8, 30, 0)) == "01.10.2026 08:30:00" && ActivityTicketSampleRules.Format(true) == "da" && ActivityTicketSampleRules.Format(new byte[3]) == "(conținut binar, 3 octeți)"
              && ActivityTicketSampleRules.Format(12.5m) == "12.5" && ActivityTicketSampleRules.Format(new string('x', 5000))!.EndsWith("(text tăiat)", StringComparison.Ordinal),
            "Example ticket: dates are written the way the application writes them, binary content is named and long text is cut");
    }

    private static void StructureChange(Action<bool, string> check)
    {
        IReadOnlyList<ActivityColumn> Columns(params (string Table, string Column, string Type)[] items) => [.. items.Select(item => new ActivityColumn(item.Table, item.Column, item.Type))];
        var before = ActivityTicketStructure.Snapshot(Columns(("Tickete", "ID", "bigint"), ("Tickete", "Subject", "nvarchar"), ("Tickete", "NrMasina", "int"), ("EvenimenteAgent", "ID", "int"), ("Alta", "X", "int")));
        var now = ActivityTicketStructure.Snapshot(Columns(("Tickete", "ID", "int"), ("Tickete", "Subject", "nvarchar"), ("Tickete", "NrMasina", "varchar"), ("Tickete", "Observatii", "nvarchar"), ("EvenimenteAgent", "ID", "int"), ("Alta", "Y", "int")));
        check(before.Count == 4 && before.All(line => !line.StartsWith("Alta", StringComparison.Ordinal)), "Ticket structure: only the columns of the tickets and of their events are compared");
        var diff = ActivityTicketStructure.Compare(before, now);
        check(diff.Added.SequenceEqual(["Tickete.Observatii"]) && diff.Removed.Count == 0 && diff.Changed.Count == 1 && diff.Changed[0].StartsWith("Tickete.NrMasina", StringComparison.Ordinal) && diff.Any
              && diff.Describe().Contains("coloane noi: Tickete.Observatii", StringComparison.Ordinal) && diff.Describe().Contains("coloane cu alt tip", StringComparison.Ordinal),
            "Ticket structure: a column that is new or of another kind is noticed (bigint to int is not a change), and said in words");
        var gone = ActivityTicketStructure.Compare(now, before);
        check(gone.Removed.SequenceEqual(["Tickete.Observatii"]) && gone.Describe().Contains("coloane dispărute: Tickete.Observatii", StringComparison.Ordinal) && !ActivityTicketStructure.Compare(now, now).Any, "Ticket structure: a column that is gone is noticed, and the same structure is no change");
        var state = new ActivityImportAlertState(null, "", 0, null, 0, null, 0, null, new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc), diff.Describe());
        var instance = ActivityImportAlertSource.Instances(state).Single();
        check(instance.ObjectId == ActivityImportAlertSource.TicketStructureId && instance.Values[ActivityImportAlertSource.ProblemName].Contains("structura tichetelor", StringComparison.Ordinal)
              && instance.Values[ActivityImportAlertSource.DetailsName].Contains("Tickete.Observatii", StringComparison.Ordinal) && instance.Values[ActivityImportAlertSource.DetailsName].Contains("Corespondența verificată", StringComparison.Ordinal),
            "Ticket structure: the notification names what changed and asks to check the correspondence of the fields");
        check(ActivityImportAlertSource.Instances(state with { TicketStructureSince = null }).Count == 0, "Ticket structure: no change, no notification");
    }

    private static void SampleWindow(Action<bool, string> check)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IActivityTicketVisibility>(new Visibility(TicketVisibility.All));
        TicketSampleDialog.SampleToggle? toggled = null;
        TicketSampleDialog.SampleEdit? edited = null;
        int older = 0, newer = 0;
        var map = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new ActivityExtraField("Tickete", "Observatii", "Observații client", "both")], new Dictionary<string, string> { [ActivityFieldMap.Problem] = "Defect" }, out _)!;
        var targets = ActivityFieldMap.Info.Where(item => item.FieldKey is not null).Select(item => new TicketSampleDialog.SampleTarget(item.Key, map.LabelOf(item.Key), item.FieldKey!.Split('.')[0])).ToList();
        var sample = Sample();
        sample = sample with { Tables = [.. sample.Tables, new SampleTable("Client", [new SampleColumn("CodFiscal", "nvarchar", "RO123", true)])] };
        var used = new List<TicketSampleDialog.SampleUsed>
        {
            new(ActivityFieldMap.Title, "Tickete", "Subject", "Tichet – Subject", "Titlul tichetului", "notes", ActivityFieldMap.Title, true, "Se preia mereu"),
            new(ActivityFieldMap.Problem, "Tickete", "DescriptionInternal", "Tichet – DescriptionInternal", "Defect", "both", ActivityFieldMap.Problem),
            new("x:Tickete.Observatii", "Tickete", "Observatii", "Tichet – Observatii", "Observații client", "both", "extra"),
            new("client-cui", "Client", "CodFiscal", "Client – CodFiscal", "CUI client", "notes", "client", true, "Se preia automat pentru persoane juridice")
        };
        var cut = context.Render<TicketSampleDialog>(parameters => parameters.Add(p => p.Sample, sample).Add(p => p.Map, map).Add(p => p.Targets, targets).Add(p => p.Used, used).Add(p => p.HasNewer, false)
            .Add(p => p.Older, EventCallback.Factory.Create(new object(), () => older++)).Add(p => p.Newer, EventCallback.Factory.Create(new object(), () => newer++))
            .Add(p => p.Toggle, EventCallback.Factory.Create<TicketSampleDialog.SampleToggle>(new object(), value => toggled = value))
            .Add(p => p.Edit, EventCallback.Factory.Create<TicketSampleDialog.SampleEdit>(new object(), value => edited = value)));
        check(cut.Find("#ticket-sample-info").TextContent.Contains("Tichet 9", StringComparison.Ordinal) && cut.FindAll("#ticket-sample-Tickete tbody tr").Count == 7 && cut.FindAll("#ticket-sample-EvenimenteAgent tbody tr").Count == 1
              && cut.Find("#ticket-sample-Tickete").TextContent.Contains("Poarta inchisa", StringComparison.Ordinal),
            "Example ticket window: every column of the ticket and of its event is listed with its value");
        check(cut.FindAll("#ticket-sample-group-Tickete-content").Count == 1 && cut.FindAll("#ticket-sample-group-EvenimenteAgent-content").Count == 1 && cut.FindAll("#ticket-sample-group-Client-content").Count == 1 && cut.Find("#ticket-sample-Client").TextContent.Contains("RO123", StringComparison.Ordinal),
            "Example ticket window: the columns are in collapsible lists, one for the ticket, one for the event and one for the client (its CUI)");
        var table = cut.Find("#ticket-sample-preview").TextContent;
        check(table.Contains("Defect", StringComparison.Ordinal) && table.Contains("Nu merge aerul", StringComparison.Ordinal) && table.Contains("Observații client", StringComparison.Ordinal) && cut.Find("#ticket-sample-notes").TextContent.Contains("Defect: N", StringComparison.Ordinal),
            "Example ticket window: it shows what each piece gets (under the names chosen) and the notes that would be written as description: value");
        check(cut.Find("#ticket-sample-Tickete").TextContent.Contains("Defect", StringComparison.Ordinal) && cut.Find("#ticket-sample-Tickete").TextContent.Contains("Observații client", StringComparison.Ordinal),
            "Example ticket window: the column in use shows which piece (or free field) reads it");
        check(!cut.Find("#ticket-sample-older").HasAttribute("disabled") && cut.Find("#ticket-sample-newer").HasAttribute("disabled"), "Example ticket window: the previous ticket can be asked for, the newer one only after going back");
        cut.Find("#ticket-sample-older").Click();
        check(older == 1 && cut.Find("#ticket-sample-used-count").TextContent == "4" && cut.FindAll("#ticket-sample-used tbody tr").Count == 4, "Example ticket window: the used columns are listed, with the title and the client CUI always there");
        // The list of the used ones: the description, the destination and the role are edited there, a column is detached from there, and the locked ones have nothing to press.
        cut.Find("input[aria-label='Descrierea pentru Tichet – Observatii']").Change("Cerere client");
        check(edited is { Key: "x:Tickete.Observatii", Label: "Cerere client" }, "Example ticket window: the description of a used column is edited in the list and passed on");
        cut.Find("select[aria-label='Se atașează ca: Observații client']").Change("separate");
        check(edited is { Key: "x:Tickete.Observatii", Target: "separate" }, "Example ticket window: the destination of a used column is changed in the list");
        cut.Find("select[aria-label='Rolul coloanei Tichet – Observatii']").Change(ActivityFieldMap.Executor);
        check(edited is { Key: "x:Tickete.Observatii", Role: "executor" }, "Example ticket window: a free field can take the role of a piece of information");
        cut.Find("button[aria-label='Detașează Observații client']").Click();
        check(edited is { Key: "x:Tickete.Observatii", Detach: true } && cut.FindAll("button[aria-label='Detașează Titlul tichetului']").Count == 0 && cut.FindAll("button[aria-label='Detașează CUI client']").Count == 0,
            "Example ticket window: a used column is detached from the list, and the title and the client CUI cannot be");
        // The lists: a column is switched on with its toggle (all are off by default); the title and the client CUI cannot be switched off.
        cut.Find("#ticket-sample-Tickete [aria-label='Folosește coloana Detalii']").Change(true);
        check(toggled is { Table: "Tickete", Column: "Detalii", On: true }, "Example ticket window: switching a column on is passed on");
        check(cut.Find("#ticket-sample-Tickete [aria-label='Folosește coloana Subject']").HasAttribute("disabled") && cut.Find("#ticket-sample-Client [aria-label='Folosește coloana CodFiscal']").HasAttribute("disabled"),
            "Example ticket window: the title and the client CUI cannot be switched off");
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
        check(new[] { duplicate, table, column, target }.All(text => text is not null) && clash is null && many is null,
            "Ticket fields: a duplicate column, another table, a bad column name or another destination are refused; extra field names may repeat and their number is not limited");
        var repeated = Make(Enumerable.Range(0, 30).Select(index => new ActivityExtraField(index % 2 == 0 ? "Tickete" : "EvenimenteAgent", "C" + index, "Observații", "both")), out var repeatedError);
        check(repeatedError is null && repeated!.Extras.Count == 30 && ActivityFieldMap.FromJson(repeated.ToJson()).Extras.Count == 30 && repeated.SourceOf("x:EvenimenteAgent.C1") == "Eveniment – C1" && repeated.SourceOf(ActivityFieldMap.Title) == "Tichet – Subject",
            "Ticket fields: thirty extra fields with the same name are kept, written and read back, and each tells its column (Tichet – / Eveniment –)");
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
        var number = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "NumarMasina", "Număr", "both")], out _)!;
        check(ActivityStructureChecker.Check(actual, null, number).Extras.Select(extra => extra.Key).SequenceEqual(["x:Tickete.NumarMasina"]) && ActivityStructureChecker.Check(actual, null, number).Issues.All(issue => !issue.Problem.Contains("Număr", StringComparison.Ordinal)),
            "Ticket fields: an extra field may read a column of any readable kind (a number here), not only text");
        IReadOnlyList<ActivityColumn> withBinary = [.. actual, new("Tickete", "Poza", "varbinary")];
        var binary = ActivityFieldMap.Create(new Dictionary<string, string>(), null, null, [new("Tickete", "Poza", "Poza", "both")], out _)!;
        check(ActivityStructureChecker.Check(withBinary, null, binary).Issues.Any(issue => issue.Problem.Contains("nu poate fi citit", StringComparison.Ordinal)) && ActivityStructureChecker.Check(withBinary, null, binary).Extras.Count == 0,
            "Ticket fields: an extra field whose column is binary is not read and is reported");
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
