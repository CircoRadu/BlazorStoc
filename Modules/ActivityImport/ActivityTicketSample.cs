namespace BlazorStoc.Modules.ActivityImport;

// A real ticket of ActivityApp shown to the administrator (Setari -> Preluare ActivityApp, section 6): every column of the ticket and of the event that closed it with its value, so that it is
// clear which column holds what, and what each piece of information (and the notes) would get from it with the settings on the screen. Nothing of it is kept: it is read when asked for and
// shown. The most recent finished ticket first; "the previous one" is the next older ticket.

/// <summary>One column of a table of ActivityApp with its value for the ticket shown (null = empty). IsText: a column that can be chosen for a piece of information or an extra field.</summary>
public sealed record SampleColumn(string Column, string DataType, string? Value, bool IsText)
{
    /// <summary>A column of another readable kind (number, date, flag): it can be a free field with a description, not one of the texts (problem, report ...).</summary>
    public bool OtherKind { get; init; }

    /// <summary>The column can be used: as a text or as a free field.</summary>
    public bool Readable => IsText || OtherKind;
}

public sealed record SampleTable(string Table, IReadOnlyList<SampleColumn> Columns);

/// <summary>The ticket shown. Executor: the agents attached to its event (what the import takes as the executor). HasOlder: there is an older finished ticket to go to.</summary>
public sealed record TicketSample(long TicketId, string Number, string Title, DateTime? TicketDate, IReadOnlyList<SampleTable> Tables, string Executor, bool HasOlder);

/// <summary>The answer of the service: the ticket, or why there is none to show.</summary>
public sealed record ActivityTicketSampleView(TicketSample? Sample, string? Error);

/// <summary>What one piece of information gets from the ticket shown, with the settings given.</summary>
public sealed record SamplePreviewItem(string Key, string Label, string Value, string Target, string Source = "");

public sealed record SamplePreview(IReadOnlyList<SamplePreviewItem> Items, string Notes);

public static class ActivityTicketSampleRules
{
    public const int MaximumValueLength = 4000;

    /// <summary>The destination shown for the end of the event: it is the date of the intervention, not a text of the notes.</summary>
    public const string Performed = "performed";

    private static string? ValueOf(TicketSample sample, string table, string column) =>
        sample.Tables.FirstOrDefault(item => item.Table.Equals(table, StringComparison.OrdinalIgnoreCase))?.Columns.FirstOrDefault(item => item.Column.Equals(column, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>The end of the event as the import would read it: the value of the column chosen for it, written the way the window shows dates.</summary>
    public static DateTime? FinishedOf(TicketSample sample, ActivityFieldMap map) =>
        DateTime.TryParseExact(ValueOf(sample, "EvenimenteAgent", map.ColumnOf(ActivityFieldMap.FinishedField)), "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    /// <summary>The ticket as the import would read it with these settings: each text from the column the settings name for it, the extra fields from their columns and the executor from the agents of the event.</summary>
    public static ActivityTicket ToTicket(TicketSample sample, ActivityFieldMap map)
    {
        string From(string table, string column)
        {
            var field = ActivityFieldMap.Fields.First(item => item.Table == table && item.Column == column);
            return ValueOf(sample, table, map.ColumnOf(field)) ?? "";
        }
        var extras = map.Extras.ToDictionary(extra => extra.Key, extra => ValueOf(sample, extra.Table, extra.Column) ?? "");
        var ticket = new ActivityTicket(sample.TicketId, 0, 0, 0, sample.TicketDate, map.NumberChosen ? From("Tickete", "NumeTicket") : "", From("Tickete", "Subject"), From("Tickete", "DescriptionInternal"),
            sample.Executor.Length > 0 ? sample.Executor : From("Tickete", "ProcesatDe"), long.TryParse(ValueOf(sample, "EvenimenteAgent", "ID"), out var eventId) ? eventId : 0, FinishedOf(sample, map), null, From("EvenimenteAgent", "DescriptionInternal"), null, "", null, extras.Count == 0 ? null : extras);
        return ActivityTicketFields.Collect(ticket, map);
    }

    /// <summary>What each piece of information gets, and the notes as they would be written (without the remark about maintenance), with the settings given. Pieces switched off are listed as such.</summary>
    public static SamplePreview Preview(TicketSample sample, ActivityFieldMap map)
    {
        var source = ActivityTicketPlanner.SourceOf(ToTicket(sample, map), map);
        var items = new List<SamplePreviewItem>
        {
            new(ActivityFieldMap.Title, map.LabelOf(ActivityFieldMap.Title), source.Title, ActivityFieldMap.Notes, map.SourceOf(ActivityFieldMap.Title) ?? ""),
            new(ActivityFieldMap.Number, map.LabelOf(ActivityFieldMap.Number), source.Number, ActivityFieldMap.Notes, map.NumberChosen ? map.SourceOf(ActivityFieldMap.Number) ?? "" : "coloana nu este aleasă"),
            new(ActivityFieldMap.Finished, map.LabelOf(ActivityFieldMap.Finished), FinishedOf(sample, map) is { } finished ? finished.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "", Performed, map.SourceOf(ActivityFieldMap.Finished) ?? "")
        };
        string From(string key) => key == ActivityFieldMap.Resolution
            ? ActivityFieldMap.ColumnName("EvenimenteAgent", map.ColumnOf(ActivityFieldMap.Fields.First(field => field.Table == "EvenimenteAgent" && field.Column == "DescriptionInternal"))) + " fără textul problemei"
            : map.SourceOf(key) ?? "";
        string Value(string key) => key switch
        {
            ActivityFieldMap.Problem => source.Problem, ActivityFieldMap.Resolution => source.Resolution, ActivityFieldMap.Report => source.Report, ActivityFieldMap.Executor => source.Executor,
            _ => source.Extras is not null && source.Extras.TryGetValue(key, out var value) ? value : ""
        };
        foreach (var item in ActivityFieldMap.Info.Where(item => !item.Mandatory))
            items.Add(new(item.Key, map.LabelOf(item.Key), map.IsEnabled(item.Key) ? Value(item.Key) : "", map.IsEnabled(item.Key) ? map.TargetOf(item.Key) : "oprit", map.IsEnabled(item.Key) ? From(item.Key) : ""));
        foreach (var extra in map.Extras) items.Add(new(extra.Key, extra.Label, Value(extra.Key), extra.Target, From(extra.Key)));
        return new SamplePreview(items, ActivityTicketPlanner.Notes(source, map));
    }

    /// <summary>A value as the window shows it: dates written the way the application writes them, long text cut, binary content named.</summary>
    public static string? Format(object? value)
    {
        if (value is null or DBNull) return null;
        var text = value switch
        {
            DateTime date => date.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset date => date.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            bool flag => flag ? "da" : "nu",
            byte[] bytes => $"(conținut binar, {bytes.Length} octeți)",
            IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        return text.Length > MaximumValueLength ? text[..MaximumValueLength] + "… (text tăiat)" : text;
    }
}
