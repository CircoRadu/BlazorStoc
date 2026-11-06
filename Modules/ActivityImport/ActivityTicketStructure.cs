using System.Text.Json;
using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// The structure of the tickets of ActivityApp (the columns of Tickete and of EvenimenteAgent) as the correspondence of the fields was last checked against it. At every application of the ticket
// import the structure now found is compared with that one: a column that is new, gone or of another kind means ActivityApp changed and the correspondence (Setari -> Preluare date ActivityApp,
// section 6) may have to follow it. Then a notification asks for it; it goes away when the administrator confirms the correspondence for the new structure (or when the structure is the same again).
// The first time nothing is compared: the structure found becomes the one the correspondence is checked against.

/// <summary>What differs between the structure the correspondence was checked against and the one ActivityApp has now. Each entry is "Tabel.Coloana" (and the kinds for a change).</summary>
public sealed record TicketStructureDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public static TicketStructureDiff None { get; } = new([], [], []);

    public bool Any => Added.Count + Removed.Count + Changed.Count > 0;

    private static string List(IReadOnlyList<string> items) => items.Count <= 8 ? string.Join(", ", items) : string.Join(", ", items.Take(8)) + $" și încă {items.Count - 8}";

    /// <summary>The change in words: "coloane noi: ...; coloane dispărute: ...; coloane cu alt tip: ...".</summary>
    public string Describe() => string.Join("; ", new[]
    {
        Added.Count > 0 ? "coloane noi: " + List(Added) : "", Removed.Count > 0 ? "coloane dispărute: " + List(Removed) : "", Changed.Count > 0 ? "coloane cu alt tip: " + List(Changed) : ""
    }.Where(text => text.Length > 0));
}

public static class ActivityTicketStructure
{
    private static readonly string[] Tables = ["Tickete", "EvenimenteAgent"];

    /// <summary>The columns of the two tables, "Tabel.Coloana:fel" (the kind of the data: text, number, date, flag), in a fixed order.</summary>
    public static IReadOnlyList<string> Snapshot(IReadOnlyList<ActivityColumn> actual) =>
        [.. actual.Where(column => Tables.Contains(column.Table, StringComparer.OrdinalIgnoreCase))
            .Select(column => $"{Tables.First(table => table.Equals(column.Table, StringComparison.OrdinalIgnoreCase))}.{column.Column}:{KindName(column.DataType)}").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    private static string KindName(string dataType) => ActivityContract.KindOf(dataType) switch
    {
        ColumnKind.Text => "text", ColumnKind.Number => "număr", ColumnKind.Date => "dată", ColumnKind.Flag => "da/nu", _ => dataType.Trim().ToLowerInvariant()
    };

    public static TicketStructureDiff Compare(IReadOnlyList<string> before, IReadOnlyList<string> now)
    {
        static (string Name, string Kind) Split(string line) { var colon = line.LastIndexOf(':'); return colon < 0 ? (line, "") : (line[..colon], line[(colon + 1)..]); }
        var old = before.Select(Split).ToDictionary(item => item.Name, item => item.Kind, StringComparer.OrdinalIgnoreCase);
        var current = now.Select(Split).ToDictionary(item => item.Name, item => item.Kind, StringComparer.OrdinalIgnoreCase);
        return new TicketStructureDiff([.. current.Keys.Where(name => !old.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase)], [.. old.Keys.Where(name => !current.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase)],
            [.. current.Where(pair => old.TryGetValue(pair.Key, out var kind) && !kind.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)).Select(pair => $"{pair.Key} ({old[pair.Key]} → {pair.Value})").Order(StringComparer.OrdinalIgnoreCase)]);
    }
}

internal static class ActivityTicketStructureStore
{
    /// <summary>
    /// Compares the structure found now with the one the correspondence was checked against and keeps the notification state: a difference starts it (on the day it was noticed, with the list of what
    /// changed), no difference ends it. With nothing to compare against yet the structure found becomes the one to compare with. Returns the difference.
    /// </summary>
    public static async Task<TicketStructureDiff> CheckAsync(IConfiguration configuration, IReadOnlyList<ActivityColumn> actual, CancellationToken cancellationToken)
    {
        var now = ActivityTicketStructure.Snapshot(actual);
        if (now.Count == 0) return TicketStructureDiff.None;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        string? stored;
        await using (var read = new MySqlCommand("SELECT ticket_structure_snapshot FROM activity_import_settings WHERE id = 1", connection))
            stored = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrEmpty(stored))
        {
            await WriteBaselineAsync(connection, now, cancellationToken).ConfigureAwait(false);
            return TicketStructureDiff.None;
        }
        var diff = ActivityTicketStructureRules.Parse(stored) is { } before ? ActivityTicketStructure.Compare(before, now) : TicketStructureDiff.None;
        await using var update = new MySqlCommand("""
            UPDATE activity_import_settings SET alert_ticket_structure_since = IF(@any = 1, COALESCE(alert_ticket_structure_since, @now), NULL), ticket_structure_diff = IF(@any = 1, @diff, NULL) WHERE id = 1
            """, connection);
        update.Parameters.AddWithValue("@any", diff.Any ? 1 : 0);
        update.Parameters.AddWithValue("@now", DateTime.UtcNow);
        update.Parameters.AddWithValue("@diff", JsonSerializer.Serialize(diff));
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return diff;
    }

    /// <summary>The administrator confirmed the correspondence for the structure ActivityApp has now: it becomes the one to compare with and the notification ends.</summary>
    public static async Task AcknowledgeAsync(IConfiguration configuration, IReadOnlyList<ActivityColumn> actual, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await WriteBaselineAsync(connection, ActivityTicketStructure.Snapshot(actual), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBaselineAsync(MySqlConnection connection, IReadOnlyList<string> snapshot, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("UPDATE activity_import_settings SET ticket_structure_snapshot = @snapshot, alert_ticket_structure_since = NULL, ticket_structure_diff = NULL WHERE id = 1", connection);
        command.Parameters.AddWithValue("@snapshot", JsonSerializer.Serialize(snapshot));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The difference noticed at the last application and not confirmed yet (None when there is none).</summary>
    public static async Task<TicketStructureDiff> PendingAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ticket_structure_diff FROM activity_import_settings WHERE id = 1", connection);
        return ActivityTicketStructureRules.ParseDiff(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string);
    }
}

public static class ActivityTicketStructureRules
{
    public static IReadOnlyList<string>? Parse(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<string>>(json); }
        catch (JsonException) { return null; }
    }

    public static TicketStructureDiff ParseDiff(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? TicketStructureDiff.None : JsonSerializer.Deserialize<TicketStructureDiff>(json) ?? TicketStructureDiff.None; }
        catch (JsonException) { return TicketStructureDiff.None; }
    }
}
