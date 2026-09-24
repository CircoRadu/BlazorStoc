namespace BlazorStoc.Services;

public sealed class SqliteAuditTrail(SqliteLocalStore store) : IAuditTrail
{
    public async Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SqliteLocalStore.InsertAuditAsync(connection, null, entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id,archive_operation_id
            FROM audit_events ORDER BY timestamp_utc DESC LIMIT 2000
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var events = new List<AuditEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) ||
                !DateTime.TryParse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
                continue;
            Guid? archiveOperationId = reader.IsDBNull(10) ? null : Guid.TryParse(reader.GetString(10), out var parsed) ? parsed : null;
            events.Add(new(id, timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime(),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), AuditActions.Normalize(reader.GetString(5)),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), archiveOperationId));
        }
        return events;
    }
}
