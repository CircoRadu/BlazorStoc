using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaAuditTrail(IConfiguration configuration) : IAuditTrail
{
    public async Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            INSERT INTO audit_events
                (id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id,archive_operation_id)
            VALUES(@id,@timestamp,@actor,@role,@entity,@action,@target,@details,@motif,@entityId,@archiveOperationId)
            """, connection);
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
        // timestamp_utc is VARCHAR(40) text on the real migrated schema, not a native DATETIME (Subtask 2.6);
        // written in the same format the database's own triggers use (MariaTimeText).
        command.Parameters.AddWithValue("@timestamp", MariaTimeText.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("@actor", entry.ActorUsername);
        command.Parameters.AddWithValue("@role", entry.ActorRole);
        command.Parameters.AddWithValue("@entity", entry.EntityType);
        command.Parameters.AddWithValue("@action", AuditActions.Normalize(entry.Action));
        command.Parameters.AddWithValue("@target", entry.Target);
        command.Parameters.AddWithValue("@details", entry.Details);
        command.Parameters.AddWithValue("@motif", entry.Motif ?? string.Empty);
        command.Parameters.AddWithValue("@entityId", entry.EntityId ?? string.Empty);
        command.Parameters.AddWithValue("@archiveOperationId",
            entry.ArchiveOperationId?.ToString("D") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,
                   entity_id,archive_operation_id
            FROM audit_events
            ORDER BY timestamp_utc DESC,id DESC
            LIMIT 2000
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var events = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var timestamp = MariaTimeText.Parse(reader.GetString(1));
            events.Add(new(Guid.Parse(reader.GetString(0)), timestamp, reader.GetString(2), reader.GetString(3),
                reader.GetString(4), AuditActions.Normalize(reader.GetString(5)), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10))));
        }
        return events;
    }
}
