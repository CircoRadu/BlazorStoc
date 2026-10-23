using BlazorStoc.Services;
using MySqlConnector;

namespace BlazorStoc.Modules.ActivityImport;

// What the ticket import keeps apart from the interventions it makes (migration 50): the tickets that cannot be taken yet because their beneficiary still has to be verified (they wait here, with
// everything needed to plan them again without reading ActivityApp), the ones taken, and the ones discarded for good (a discarded ticket is never taken again). Also the watermark of the
// incremental reading.

public static class TicketStates
{
    public const string WaitingBeneficiary = "waiting_beneficiary", Imported = "imported", Discarded = "discarded";
}

public sealed record StoredTicket(ActivityTicket Ticket, string State, int? InterventionId);

internal static class ActivityTicketStore
{
    private const string Columns = "ticket_id, type_id, partner_id, work_point_id, event_id, event_status, ticket_date, event_end, event_modified, ticket_number, title, problem, executor, report, state, intervention_id";

    private static StoredTicket Read(MySqlDataReader reader) => new(new ActivityTicket(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
        reader.IsDBNull(6) ? null : reader.GetDateTime(6), reader.GetString(9), reader.GetString(10), reader.IsDBNull(11) ? "" : reader.GetString(11), reader.GetString(12), reader.GetInt64(4),
        reader.IsDBNull(7) ? null : reader.GetDateTime(7), reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.IsDBNull(13) ? "" : reader.GetString(13), reader.IsDBNull(8) ? null : reader.GetDateTime(8)),
        reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetInt32(15));

    /// <summary>The tickets kept in the given state.</summary>
    public static async Task<IReadOnlyList<StoredTicket>> ReadAsync(IConfiguration configuration, string state, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM activity_import_tickets WHERE state=@state ORDER BY ticket_id", connection);
        command.Parameters.AddWithValue("@state", state);
        var result = new List<StoredTicket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Read(reader));
        return result;
    }

    /// <summary>One ticket kept, in any state, or null.</summary>
    public static async Task<StoredTicket?> FindAsync(IConfiguration configuration, long ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"SELECT {Columns} FROM activity_import_tickets WHERE ticket_id=@id", connection);
        command.Parameters.AddWithValue("@id", ticketId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <summary>The state of every ticket kept (by ticket), to leave out the discarded ones.</summary>
    public static async Task<IReadOnlyDictionary<long, string>> StatesAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("SELECT ticket_id, state FROM activity_import_tickets", connection);
        var result = new Dictionary<long, string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result[reader.GetInt64(0)] = reader.GetString(1);
        return result;
    }

    /// <summary>Keeps the ticket in the given state (a ticket discarded stays discarded unless it is reactivated).</summary>
    public static async Task SaveAsync(IConfiguration configuration, ActivityTicket ticket, string state, int? interventionId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand($"""
            INSERT INTO activity_import_tickets ({Columns}, created_utc, updated_utc)
            VALUES (@id, @type, @partner, @point, @event, @status, @date, @end, @modified, @number, @title, @problem, @executor, @report, @state, @intervention, @now, @now)
            ON DUPLICATE KEY UPDATE type_id=@type, partner_id=@partner, work_point_id=@point, event_id=@event, event_status=@status, ticket_date=@date, event_end=@end, event_modified=@modified,
                ticket_number=@number, title=@title, problem=@problem, executor=@executor, report=@report, updated_utc=@now,
                state=IF(state='discarded', state, @state), intervention_id=IF(state='discarded', intervention_id, COALESCE(@intervention, intervention_id))
            """, connection);
        command.Parameters.AddWithValue("@id", ticket.Id);
        command.Parameters.AddWithValue("@type", ticket.TypeId);
        command.Parameters.AddWithValue("@partner", ticket.PartnerId);
        command.Parameters.AddWithValue("@point", ticket.WorkPointId);
        command.Parameters.AddWithValue("@event", ticket.EventId);
        command.Parameters.AddWithValue("@status", (object?)ticket.EventStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@date", (object?)ticket.TicketDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@end", (object?)ticket.EventEnd ?? DBNull.Value);
        command.Parameters.AddWithValue("@modified", (object?)ticket.EventModified ?? DBNull.Value);
        command.Parameters.AddWithValue("@number", Cut(ticket.Number, 100));
        command.Parameters.AddWithValue("@title", Cut(ticket.Title, 500));
        command.Parameters.AddWithValue("@problem", ticket.Problem);
        command.Parameters.AddWithValue("@executor", Cut(ticket.Executor, 200));
        command.Parameters.AddWithValue("@report", ticket.Report);
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@intervention", (object?)interventionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks the ticket as discarded for good (it is never read again) and drops its intervention link. A ticket not kept yet is kept as discarded (only its id is known).</summary>
    public static async Task DiscardAsync(IConfiguration configuration, long ticketId, string by, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO activity_import_tickets (ticket_id, type_id, partner_id, state, created_utc, updated_utc, decided_by, decided_utc) VALUES (@id, 0, 0, 'discarded', @now, @now, @by, @now)
            ON DUPLICATE KEY UPDATE state='discarded', intervention_id=NULL, updated_utc=@now, decided_by=@by, decided_utc=@now
            """, connection);
        command.Parameters.AddWithValue("@id", ticketId);
        command.Parameters.AddWithValue("@by", by);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A discarded ticket is planned again by the next import: when its data is kept it goes back to the queue (state waiting, so it does not depend on the incremental reading reaching it again);
    /// a ticket kept only by its id is forgotten and read from ActivityApp again.
    /// </summary>
    public static async Task ReactivateAsync(IConfiguration configuration, long ticketId, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using (var back = new MySqlCommand("UPDATE activity_import_tickets SET state='waiting_beneficiary', decided_by=NULL, decided_utc=NULL, intervention_id=NULL, updated_utc=@now WHERE ticket_id=@id AND state='discarded' AND type_id <> 0", connection))
        {
            back.Parameters.AddWithValue("@id", ticketId);
            back.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await back.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var forget = new MySqlCommand("DELETE FROM activity_import_tickets WHERE ticket_id=@id AND state='discarded'", connection);
        forget.Parameters.AddWithValue("@id", ticketId);
        await forget.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets (or clears) the watermark of the incremental reading.</summary>
    public static async Task SetWatermarkAsync(IConfiguration configuration, DateTime? watermark, CancellationToken cancellationToken)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("UPDATE activity_import_settings SET source_watermark=@watermark WHERE id=1", connection);
        command.Parameters.AddWithValue("@watermark", (object?)watermark ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
