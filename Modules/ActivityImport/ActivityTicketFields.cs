using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The settings of the ticket fields at work: what is kept of a ticket when it is read, the settings as the other parts read them, the record of the source of an intervention and the
// visibility the windows ask for.

public static class ActivityTicketFields
{
    /// <summary>The ticket cut to what the settings read: a piece switched off is not kept, the extra fields are those of the settings, and the names of what was read go with it.</summary>
    public static ActivityTicket Collect(ActivityTicket ticket, ActivityFieldMap map)
    {
        var required = map.Required;
        var extras = ticket.Extras is null ? null : map.Extras.Where(extra => ticket.Extras.ContainsKey(extra.Key)).ToDictionary(extra => extra.Key, extra => ticket.Extras[extra.Key]);
        return ticket with
        {
            Problem = required.Contains(ActivityFieldMap.Problem) ? ticket.Problem : "",
            Report = required.Contains(ActivityFieldMap.Report) ? ticket.Report : "",
            Executor = required.Contains(ActivityFieldMap.Executor) ? ticket.Executor : "",
            Extras = extras is { Count: > 0 } ? extras : null,
            FieldsRead = ActivityFieldMap.Join(required)
        };
    }

    /// <summary>What the settings ask for that the record does not hold (the names missing from <paramref name="have"/>).</summary>
    public static IReadOnlyList<string> Missing(ActivityFieldMap map, string have)
    {
        var held = ActivityFieldMap.Split(have);
        var missing = map.Required.Where(key => !held.Contains(key)).ToList();
        // The end of the event was read from another column and the default is back: the date held is not the one the settings ask for.
        if (held.Contains(ActivityFieldMap.Finished) && !map.Required.Contains(ActivityFieldMap.Finished)) missing.Add(ActivityFieldMap.Finished);
        return [.. missing.Order(StringComparer.Ordinal)];
    }
}

internal static class ActivityFieldSettingsStore
{
    /// <summary>The settings of the ticket fields as saved (the default ones when nothing was saved).</summary>
    public static async Task<ActivityFieldMap> ReadAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT field_map FROM activity_import_settings WHERE id = 1", connection);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string json ? ActivityFieldMap.FromJson(json) : ActivityFieldMap.Empty;
    }
}

/// <summary>The record of what an intervention was taken from (service_intervention_source), written when the ticket is taken and renewed when the settings of the fields ask for more.</summary>
internal static class ActivityTicketSourceStore
{
    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

    public static async Task WriteAsync(IConfiguration configuration, int interventionId, ActivityTicket ticket, ActivityFieldMap map, CancellationToken cancellationToken, bool updateNotesItems = true)
    {
        var source = ActivityTicketPlanner.SourceOf(ticket, map);
        // The pieces that go to the separate record only; those that go to the notes alone are not written there.
        string Separate(string key, string value) => map.IsEnabled(key) && map.InSeparate(key) ? value : "";
        var extras = source.Extras is null ? new Dictionary<string, string>() : map.Extras.Where(extra => extra.Target is ActivityFieldMap.Separate or ActivityFieldMap.Both && source.Extras.ContainsKey(extra.Key)).ToDictionary(extra => extra.Key, extra => source.Extras[extra.Key]);
        var held = ActivityFieldMap.Split(ticket.FieldsRead);
        // A ticket ActivityApp no longer has keeps what it has: the record is marked so that it is not asked for again.
        var fieldsRead = ActivityFieldMap.Join(map.Required.Where(held.Contains).Concat(held.Contains("gone") ? ["gone"] : []));
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO service_intervention_source (intervention_id, source_ticket_id, source_type_id, source_event_id, ticket_number, title, problem, resolution, report, executor, extras, fields_read, notes_items, notes_labels, imported_utc)
            VALUES (@intervention, @ticket, @type, @event, @number, @title, @problem, @resolution, @report, @executor, @extras, @fieldsRead, @notesItems, @notesLabels, @now)
            ON DUPLICATE KEY UPDATE intervention_id=@intervention, source_type_id=@type, source_event_id=@event, ticket_number=@number, title=@title, problem=@problem, resolution=@resolution,
                report=@report, executor=@executor, extras=@extras, fields_read=@fieldsRead, notes_items=IF(@updateItems = 1, @notesItems, notes_items), notes_labels=IF(@updateItems = 1, @notesLabels, notes_labels), imported_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@intervention", interventionId);
        command.Parameters.AddWithValue("@ticket", source.TicketId);
        command.Parameters.AddWithValue("@type", source.TypeId);
        command.Parameters.AddWithValue("@event", source.EventId);
        command.Parameters.AddWithValue("@number", Cut(source.Number, 100));
        command.Parameters.AddWithValue("@title", Cut(source.Title, 500));
        command.Parameters.AddWithValue("@problem", Separate(ActivityFieldMap.Problem, source.Problem));
        command.Parameters.AddWithValue("@resolution", Separate(ActivityFieldMap.Resolution, source.Resolution));
        command.Parameters.AddWithValue("@report", Separate(ActivityFieldMap.Report, source.Report));
        command.Parameters.AddWithValue("@executor", Cut(Separate(ActivityFieldMap.Executor, source.Executor), 200));
        command.Parameters.AddWithValue("@extras", ActivityTicketStore.ExtrasJson(extras) is { Length: > 0 } json ? json : DBNull.Value);
        command.Parameters.AddWithValue("@fieldsRead", Cut(fieldsRead, 1000));
        command.Parameters.AddWithValue("@notesItems", Cut(ActivityTicketPlanner.NotesItems(source, map), 1000));
        command.Parameters.AddWithValue("@updateItems", updateNotesItems ? 1 : 0);
        command.Parameters.AddWithValue("@notesLabels", System.Text.Json.JsonSerializer.Serialize(ActivityTicketPlanner.NoteLines(source, map).ToDictionary(line => line.Key, line => line.Label)));
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Which pieces of the ticket the windows show, from the saved settings. Any signed-in user can ask: it holds no data of a ticket, only what is shown.</summary>
internal sealed class MariaActivityTicketVisibility(IConfiguration configuration) : IActivityTicketVisibility
{
    public async Task<TicketVisibility> GetAsync(CancellationToken cancellationToken = default)
    {
        var map = await ActivityFieldSettingsStore.ReadAsync(configuration, cancellationToken).ConfigureAwait(false);
        return new TicketVisibility(map.IsEnabled(ActivityFieldMap.Problem), map.IsEnabled(ActivityFieldMap.Resolution), map.IsEnabled(ActivityFieldMap.Report), map.IsEnabled(ActivityFieldMap.Executor),
            [.. map.Extras.Select(extra => new TicketExtraLabel(extra.Key, extra.Label))], map.Labels);
    }
}
