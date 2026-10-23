using System.Text.Json;

namespace BlazorStoc.Modules.ActivityImport;

// Which column of ActivityApp each text of a ticket is read from. The contract names the usual column of each (Tickete.DescriptionInternal is the problem, ...); the administrator can
// point a field at another text column of the same table (a vendor update that moves the text, or a team that keeps it elsewhere). The map only holds the choices that differ from the
// default; it is applied to the structure report (StructureReport.Physical answers with the chosen column), so the reading of the tickets needs no other change.

/// <summary>A text of a ticket that can be read from another column: the contract column that is its default, and what it is called to the administrator.</summary>
public sealed record ActivityMappableField(string Table, string Column, string Label, string Purpose)
{
    public string Key => ActivityFieldMap.Key(Table, Column);
}

/// <summary>The fields that differ from the default: key "Table.Column" of the contract, value the real column to read instead.</summary>
public sealed class ActivityFieldMap(IReadOnlyDictionary<string, string>? columns = null)
{
    public static ActivityFieldMap Empty { get; } = new();

    public static IReadOnlyList<ActivityMappableField> Fields { get; } =
    [
        new("Tickete", "Subject", "Titlul tichetului", "Devine titlul intervenției și începutul observațiilor."),
        new("Tickete", "DescriptionInternal", "Problema (textul tichetului la deschidere)", "Din ea și din raport se despart „Problemă” și „Rezolvare”."),
        new("EvenimenteAgent", "DescriptionInternal", "Raportul evenimentului de rezolvare", "Același text urmat de ce s-a făcut; din el iese „Rezolvare”."),
        new("Tickete", "ProcesatDe", "Executantul", "Apare în observații ca „Executant”."),
        new("Tickete", "NumeTicket", "Numărul tichetului", "Apare în observații, la „tichet …”.")
    ];

    public IReadOnlyDictionary<string, string> Columns { get; } = columns ?? new Dictionary<string, string>();

    public static string Key(string table, string column) => $"{table}.{column}";

    public string ColumnOf(ActivityMappableField field) => Columns.TryGetValue(field.Key, out var chosen) ? chosen : field.Column;

    public bool IsDefault(ActivityMappableField field) => !Columns.TryGetValue(field.Key, out var chosen) || chosen.Equals(field.Column, StringComparison.OrdinalIgnoreCase);

    /// <summary>The map as typed (a field with its default column is dropped): null when it is not valid, with the reason in <paramref name="error"/>.</summary>
    public static ActivityFieldMap? Create(IReadOnlyDictionary<string, string> chosen, out string? error)
    {
        error = null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in chosen)
        {
            var field = Fields.FirstOrDefault(item => item.Key == key);
            if (field is null) { error = "Câmpul „" + key + "” nu poate fi mapat."; return null; }
            var column = (value ?? "").Trim();
            if (column.Length == 0 || column.Equals(field.Column, StringComparison.OrdinalIgnoreCase)) continue;
            if (column.Length > 128 || column.Any(c => !(char.IsLetterOrDigit(c) || c is '_'))) { error = "Numele coloanei alese pentru „" + field.Label + "” nu este valid."; return null; }
            result[key] = column;
        }
        return new ActivityFieldMap(result);
    }

    public string ToJson() => Columns.Count == 0 ? "" : JsonSerializer.Serialize(Columns.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value));

    public static ActivityFieldMap FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values is null ? Empty : Create(values, out _) ?? Empty;
        }
        catch (JsonException) { return Empty; }
    }

    /// <summary>The text columns of a table of ActivityApp, as the structure read found them (what the administrator can choose from).</summary>
    public static IReadOnlyList<string> TextColumns(IReadOnlyList<ActivityColumn> actual, string table) =>
        [.. actual.Where(column => column.Table.Equals(table, StringComparison.OrdinalIgnoreCase) && ActivityContract.KindOf(column.DataType) == ColumnKind.Text).Select(column => column.Column).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A short text for the journal: what differs from the default, or "—" (all default).</summary>
    public string Describe() => Columns.Count == 0 ? "—" : string.Join(", ", Fields.Where(field => !IsDefault(field)).Select(field => $"{field.Label}: {ColumnOf(field)}"));
}

/// <summary>One line of the screen: the field, the default column, the column in use and the columns that can be chosen.</summary>
public sealed record ActivityFieldChoice(ActivityMappableField Field, string Current, IReadOnlyList<string> Available, bool CurrentMissing);

public sealed record ActivityFieldMapView(IReadOnlyList<ActivityFieldChoice> Fields, string? SourceError);
