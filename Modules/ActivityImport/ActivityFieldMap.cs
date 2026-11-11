using System.Text.Json;

namespace BlazorStoc.Modules.ActivityImport;

// What is taken from a ticket of ActivityApp and where it goes. The contract names the usual column of each text (Tickete.DescriptionInternal is the problem, ...); the administrator can
// point a field at another text column of the same table (a vendor update that moves the text, or a team that keeps it elsewhere), switch a piece of information off (it is then not read,
// not written and not shown), choose where it goes (in the notes of the intervention, in the separate record of the source, or both) and add extra fields: any text column of the tickets or
// of their events, with a name of the administrator's. Title and number are always taken. The map only holds the choices that differ from the default; the columns are applied to the
// structure report (StructureReport.Physical answers with the chosen column), so the reading of the tickets needs no other change.

/// <summary>A text of a ticket that can be read from another column: the contract column that is its default, and what it is called to the administrator.</summary>
public sealed record ActivityMappableField(string Table, string Column, string Label, string Purpose)
{
    public string Key => ActivityFieldMap.Key(Table, Column);
}

/// <summary>A piece of information of a ticket: the one that is always taken (title, number) or the one that can be switched off. FieldKey: the mappable field it is read from (null = derived).</summary>
public sealed record ActivityInfoItem(string Key, string Label, string? FieldKey, bool Mandatory, string Purpose, string DefaultTarget);

/// <summary>An extra text of the ticket, read from a column of the tickets or of their events and shown under the name the administrator gave it.</summary>
public sealed record ActivityExtraField(string Table, string Column, string Label, string Target)
{
    public string Key => ActivityFieldMap.ExtraKey(Table, Column);
}

/// <summary>The settings of the ticket fields: columns that differ from the default, the pieces of information switched off, their destinations and the extra fields.</summary>
public sealed class ActivityFieldMap
{
    public const string Notes = "notes", Separate = "separate", Both = "both";
    public const string Title = "title", Number = "number", Problem = "problem", Resolution = "resolution", Report = "report", Executor = "executor";
    public const int MaximumLabel = 60;
    public static readonly string[] ExtraTables = ["Tickete", "EvenimenteAgent"];

    public ActivityFieldMap(IReadOnlyDictionary<string, string>? columns = null, IEnumerable<string>? off = null, IReadOnlyDictionary<string, string>? targets = null, IEnumerable<ActivityExtraField>? extras = null,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        Columns = columns ?? new Dictionary<string, string>();
        Off = new HashSet<string>(off ?? [], StringComparer.Ordinal);
        Targets = targets ?? new Dictionary<string, string>();
        Extras = [.. extras ?? []];
        Labels = labels ?? new Dictionary<string, string>();
    }

    public static ActivityFieldMap Empty { get; } = new();

    public static IReadOnlyList<ActivityMappableField> Fields { get; } =
    [
        new("Tickete", "Subject", "Titlul tichetului", "Devine titlul intervenției și începutul observațiilor."),
        new("Tickete", "DescriptionInternal", "Problema (textul tichetului la deschidere)", "Din ea și din raport se despart „Problemă” și „Rezolvare”."),
        new("EvenimenteAgent", "DescriptionInternal", "Raportul evenimentului de rezolvare", "Același text urmat de ce s-a făcut; din el iese „Rezolvare”."),
        new("Tickete", "ProcesatDe", "Executantul", "Apare în observații ca „Executant”."),
        new("Tickete", "NumeTicket", "Numărul tichetului", "Apare în observații, la „tichet …”.")
    ];

    /// <summary>The pieces of information of a ticket, in the order they are shown.</summary>
    public static IReadOnlyList<ActivityInfoItem> Info { get; } =
    [
        new(Title, "Titlul tichetului", Key("Tickete", "Subject"), true, "Devine titlul intervenției și începutul observațiilor. Se preia întotdeauna.", Notes),
        new(Number, "Numărul tichetului", Key("Tickete", "NumeTicket"), true, "Apare în observații, la „tichet …”. Se preia întotdeauna.", Notes),
        new(Problem, "Problema", Key("Tickete", "DescriptionInternal"), false, "Textul tichetului la deschidere.", Both),
        new(Resolution, "Rezolvarea", null, false, "Ce s-a făcut: se obține din raportul evenimentului, fără textul problemei.", Both),
        new(Report, "Raportul original", Key("EvenimenteAgent", "DescriptionInternal"), false, "Raportul complet al evenimentului de rezolvare, așa cum este în ActivityApp.", Separate),
        new(Executor, "Executantul", Key("Tickete", "ProcesatDe"), false, "Agenții atașați evenimentului (cei care au făcut lucrarea).", Both)
    ];

    public IReadOnlyDictionary<string, string> Columns { get; }
    /// <summary>The optional pieces of information that are switched off (a piece not named here is on).</summary>
    public IReadOnlySet<string> Off { get; }
    /// <summary>The destination chosen for a piece of information that differs from its default.</summary>
    public IReadOnlyDictionary<string, string> Targets { get; }
    public IReadOnlyList<ActivityExtraField> Extras { get; }
    /// <summary>The names the administrator gave to the optional pieces of information (key -> name); a piece not named here keeps its usual name.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>The name of a piece of information (or of an extra field) as the windows show it.</summary>
    public string LabelOf(string key) => ExtraOf(key)?.Label ?? (Labels.TryGetValue(key, out var chosen) ? chosen : ItemOf(key)?.Label ?? key);

    /// <summary>The name of a piece of information in front of its text in the notes: the one chosen, else the one the notes always had.</summary>
    public string NoteLabelOf(string key) => ExtraOf(key)?.Label ?? (Labels.TryGetValue(key, out var chosen) ? chosen : DefaultNoteLabel(key));

    /// <summary>The name in front of a piece of information in the notes of an intervention before names could be chosen.</summary>
    public static string DefaultNoteLabel(string key) => key switch { Problem => "Problemă", Resolution => "Rezolvare", Report => "Raport original", Executor => "Executant", _ => ItemOf(key)?.Label ?? key };

    /// <summary>How the windows name the table of a column: "Tichet" (the ticket as opened) or "Eveniment" (the event that closed it).</summary>
    public static string TablePrefix(string table) => table.Equals("EvenimenteAgent", StringComparison.OrdinalIgnoreCase) ? "Eveniment" : "Tichet";

    /// <summary>"Tichet – Subject": a column as the windows name it.</summary>
    public static string ColumnName(string table, string column) => $"{TablePrefix(table)} – {column}";

    /// <summary>The column a piece of information is read from, "Tichet – Subject" (null: derived, or not read from a column).</summary>
    public string? SourceOf(string key)
    {
        if (ExtraOf(key) is { } extra) return ColumnName(extra.Table, extra.Column);
        var field = Fields.FirstOrDefault(item => item.Key == ItemOf(key)?.FieldKey);
        return field is null ? null : ColumnName(field.Table, ColumnOf(field));
    }

    public static string Key(string table, string column) => $"{table}.{column}";
    public static string ExtraKey(string table, string column) => $"x:{table}.{column}";

    public static ActivityInfoItem? ItemOf(string key) => Info.FirstOrDefault(item => item.Key == key);

    public string ColumnOf(ActivityMappableField field) => Columns.TryGetValue(field.Key, out var chosen) ? chosen : field.Column;

    public bool IsDefault(ActivityMappableField field) => !Columns.TryGetValue(field.Key, out var chosen) || chosen.Equals(field.Column, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a piece of information is taken: title and number always, the others unless switched off.</summary>
    public bool IsEnabled(string key) => ItemOf(key) is { } item && (item.Mandatory || !Off.Contains(key));

    /// <summary>Where a piece of information goes (Notes, Separate or Both); the extra fields have their own destination.</summary>
    public string TargetOf(string key)
    {
        if (ExtraOf(key) is { } extra) return extra.Target;
        return Targets.TryGetValue(key, out var chosen) && IsTarget(chosen) ? chosen : ItemOf(key)?.DefaultTarget ?? Both;
    }

    public bool InNotes(string key) => TargetOf(key) is Notes or Both;
    public bool InSeparate(string key) => TargetOf(key) is Separate or Both;

    public ActivityExtraField? ExtraOf(string key) => Extras.FirstOrDefault(extra => extra.Key == key);

    public static bool IsTarget(string? value) => value is Notes or Separate or Both;

    /// <summary>
    /// What has to be read from ActivityApp for the settings: the title, the number, the executor when taken, the problem when it or the resolution is taken, the report when it or the resolution
    /// is taken (the resolution is the report without the text of the problem), and every extra field. Kept with each ticket (fields_read) to know which ones lack something.
    /// </summary>
    public IReadOnlySet<string> Required
    {
        get
        {
            var set = new HashSet<string>(StringComparer.Ordinal) { Title, Number };
            if (IsEnabled(Executor)) set.Add(Executor);
            if (IsEnabled(Problem) || IsEnabled(Resolution)) set.Add(Problem);
            if (IsEnabled(Report) || IsEnabled(Resolution)) set.Add(Report);
            foreach (var extra in Extras) set.Add(extra.Key);
            return set;
        }
    }

    /// <summary>The names of what was read, to keep with the ticket: "number,title,problem,...".</summary>
    public static string Join(IEnumerable<string> keys) => string.Join(',', keys.Order(StringComparer.Ordinal));

    public static IReadOnlySet<string> Split(string? text) => new HashSet<string>((text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);

    /// <summary>What a ticket read before these settings existed holds: everything the notes and the source record used.</summary>
    public const string LegacyRead = "executor,number,problem,report,title";

    /// <summary>The map as typed (a field with its default column is dropped, the defaults are not kept): null when it is not valid, with the reason in <paramref name="error"/>.</summary>
    public static ActivityFieldMap? Create(IReadOnlyDictionary<string, string> chosen, out string? error) => Create(chosen, null, null, null, out error);

    public static ActivityFieldMap? Create(IReadOnlyDictionary<string, string> chosen, IEnumerable<string>? off, IReadOnlyDictionary<string, string>? targets, IEnumerable<ActivityExtraField>? extras, out string? error) =>
        Create(chosen, off, targets, extras, null, out error);

    public static ActivityFieldMap? Create(IReadOnlyDictionary<string, string> chosen, IEnumerable<string>? off, IReadOnlyDictionary<string, string>? targets, IEnumerable<ActivityExtraField>? extras,
        IReadOnlyDictionary<string, string>? labels, out string? error)
    {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in chosen)
        {
            var field = Fields.FirstOrDefault(item => item.Key == key);
            if (field is null) { error = "Câmpul „" + key + "” nu poate fi mapat."; return null; }
            var column = (value ?? "").Trim();
            if (column.Length == 0 || column.Equals(field.Column, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ValidColumn(column)) { error = "Numele coloanei alese pentru „" + field.Label + "” nu este valid."; return null; }
            result[key] = column;
        }
        var disabled = new List<string>();
        foreach (var key in off ?? [])
        {
            var item = ItemOf(key);
            if (item is null) { error = "Informația „" + key + "” nu există."; return null; }
            if (item.Mandatory) continue;
            if (!disabled.Contains(key)) disabled.Add(key);
        }
        var chosenTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in targets ?? new Dictionary<string, string>())
        {
            var item = ItemOf(key);
            if (item is null || item.Mandatory) continue;
            if (!IsTarget(value)) { error = "Destinația aleasă pentru „" + item.Label + "” nu este validă."; return null; }
            if (value != item.DefaultTarget) chosenTargets[key] = value;
        }
        var list = new List<ActivityExtraField>();
        foreach (var extra in extras ?? [])
        {
            var table = ExtraTables.FirstOrDefault(name => name.Equals(extra.Table, StringComparison.OrdinalIgnoreCase));
            var label = (extra.Label ?? "").Trim();
            var column = (extra.Column ?? "").Trim();
            if (table is null) { error = "Câmpul suplimentar „" + label + "” trebuie să fie dintr-un tabel permis (Tickete sau EvenimenteAgent)."; return null; }
            if (!ValidColumn(column)) { error = "Numele coloanei pentru câmpul suplimentar „" + label + "” nu este valid."; return null; }
            if (label.Length == 0 || label.Length > MaximumLabel || label.Any(char.IsControl)) { error = $"Numele unui câmp suplimentar are între 1 și {MaximumLabel} de caractere."; return null; }
            if (!IsTarget(extra.Target)) { error = "Destinația aleasă pentru „" + label + "” nu este validă."; return null; }
            if (list.Any(item => item.Table == table && item.Column.Equals(column, StringComparison.OrdinalIgnoreCase))) { error = "Coloana „" + column + "” a fost adăugată de două ori."; return null; }
            // The name of an extra field is only a label: the field is told apart by its table and column, so a name may repeat (also a name of a piece of information).
            list.Add(new ActivityExtraField(table, column, label, extra.Target));
        }
        // The names chosen for the optional pieces: 1 to 60 characters, none used twice (also not by the pieces that keep their usual name, nor by an extra field).
        var chosenLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in labels ?? new Dictionary<string, string>())
        {
            var item = ItemOf(key);
            if (item is null || item.Mandatory) continue;
            var label = (value ?? "").Trim();
            if (label.Length == 0 || label.Equals(item.Label, StringComparison.Ordinal)) continue;
            if (label.Length > MaximumLabel || label.Any(char.IsControl)) { error = $"Numele unei informații are între 1 și {MaximumLabel} de caractere."; return null; }
            chosenLabels[key] = label;
        }
        var names = Info.Select(item => chosenLabels.TryGetValue(item.Key, out var custom) ? custom : item.Label).ToList();
        if (names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } twice) { error = "Numele „" + twice.Key + "” este folosit de două ori."; return null; }
        return new ActivityFieldMap(result, disabled, chosenTargets, list, chosenLabels);
    }

    private static bool ValidColumn(string column) => column.Length is > 0 and <= 128 && column.All(c => char.IsLetterOrDigit(c) || c is '_');

    // The old format is the dictionary of the columns alone; the new one (version 2) is an object, written only when something besides the columns differs from the default.
    public string ToJson()
    {
        if (Columns.Count == 0 && Off.Count == 0 && Targets.Count == 0 && Extras.Count == 0 && Labels.Count == 0) return "";
        var columns = Columns.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (Off.Count == 0 && Targets.Count == 0 && Extras.Count == 0 && Labels.Count == 0) return JsonSerializer.Serialize(columns);
        return JsonSerializer.Serialize(new Stored(2, columns, [.. Off.Order(StringComparer.Ordinal)], Targets.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value),
            [.. Extras.Select(extra => new StoredExtra(extra.Table, extra.Column, extra.Label, extra.Target))], Labels.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    private sealed record Stored(int V, Dictionary<string, string> Columns, List<string> Off, Dictionary<string, string> Targets, List<StoredExtra> Extras, Dictionary<string, string>? Labels = null);
    private sealed record StoredExtra(string Table, string Column, string Label, string Target);

    public static ActivityFieldMap FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Empty;
            if (document.RootElement.TryGetProperty("v", out _) || document.RootElement.TryGetProperty("V", out _))
            {
                var stored = JsonSerializer.Deserialize<Stored>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (stored is null) return Empty;
                return Create(stored.Columns ?? [], stored.Off ?? [], stored.Targets ?? [], (stored.Extras ?? []).Select(extra => new ActivityExtraField(extra.Table, extra.Column, extra.Label, extra.Target)), stored.Labels ?? [], out _) ?? Empty;
            }
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values is null ? Empty : Create(values, out _) ?? Empty;
        }
        catch (JsonException) { return Empty; }
    }

    /// <summary>The text columns of a table of ActivityApp, as the structure read found them (what the administrator can choose from).</summary>
    public static IReadOnlyList<string> TextColumns(IReadOnlyList<ActivityColumn> actual, string table) =>
        [.. actual.Where(column => column.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && ActivityContract.KindOf(column.DataType) == ColumnKind.Text).Select(column => column.Column).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The columns of a table that can be a free field (text, number, date or flag), as the structure read found them.</summary>
    public static IReadOnlyList<string> ReadableColumns(IReadOnlyList<ActivityColumn> actual, string table) =>
        [.. actual.Where(column => column.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && ActivityContract.KindOf(column.DataType) is not null).Select(column => column.Column).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A short text for the journal: what differs from the default, or "—" (all default).</summary>
    public string Describe()
    {
        var parts = new List<string>();
        parts.AddRange(Fields.Where(field => !IsDefault(field)).Select(field => $"{field.Label}: {ColumnOf(field)}"));
        parts.AddRange(Info.Where(item => !item.Mandatory && Off.Contains(item.Key)).Select(item => $"{item.Label}: oprit"));
        parts.AddRange(Info.Where(item => !item.Mandatory && Labels.ContainsKey(item.Key)).Select(item => $"{item.Label}: numit „{Labels[item.Key]}”"));
        parts.AddRange(Info.Where(item => !item.Mandatory && !Off.Contains(item.Key) && Targets.ContainsKey(item.Key)).Select(item => $"{LabelOf(item.Key)}: {TargetText(TargetOf(item.Key))}"));
        parts.AddRange(Extras.Select(extra => $"{extra.Label} ({extra.Table}.{extra.Column}): {TargetText(extra.Target)}"));
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public static string TargetText(string target) => target switch { Notes => "în observații", Separate => "câmp separat", _ => "observații și câmp separat" };
}

/// <summary>One line of the screen: the field, the default column, the column in use and the columns that can be chosen.</summary>
public sealed record ActivityFieldChoice(ActivityMappableField Field, string Current, IReadOnlyList<string> Available, bool CurrentMissing);

/// <summary>What the screen shows: a line per column that can be pointed elsewhere, the settings in force and, when the source was read, the text columns of the tables extra fields can come from.</summary>
public sealed record ActivityFieldMapView(IReadOnlyList<ActivityFieldChoice> Fields, string? SourceError, ActivityFieldMap? Map = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? TextColumns = null, TicketStructureDiff? StructureChange = null);
