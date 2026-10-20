using MySqlConnector;

namespace BlazorStoc.Services;

public sealed class MariaAuditTrail(IConfiguration configuration) : IAuditTrail
{
    public async Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
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
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
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
    private const string Columns = "id,timestamp_utc,actor_username,actor_role,entity_type,action,target,details,motif,entity_id,archive_operation_id";

    private static AuditEvent Read(MySqlDataReader reader) => new(Guid.Parse(reader.GetString(0)), MariaTimeText.Parse(reader.GetString(1)),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), AuditActions.Normalize(reader.GetString(5)), reader.GetString(6),
        reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)));

    // The WHERE clause of a query (the same rules as AuditQueryRules.Matches). LIKE patterns are escaped: the text typed by the user is
    // searched literally.
    private static string Where(AuditQuery query, MySqlCommand command)
    {
        var conditions = new List<string>();
        if (query.Entity.Length > 0) { conditions.Add("entity_type=@entity"); command.Parameters.AddWithValue("@entity", query.Entity); }
        if (query.Action.Length > 0)
        {
            var action = AuditActions.Normalize(query.Action);
            if (action == AuditActions.Edit) conditions.Add("action IN (@action,'Modificare')"); else conditions.Add("action=@action");
            command.Parameters.AddWithValue("@action", action);
        }
        if (query.Actor.Length > 0) { conditions.Add("LOWER(actor_username)=LOWER(@actor)"); command.Parameters.AddWithValue("@actor", query.Actor); }
        if (query.FromUtc is { } from) { conditions.Add("timestamp_utc>=@from"); command.Parameters.AddWithValue("@from", MariaTimeText.Format(from)); }
        if (query.ToUtc is { } to) { conditions.Add("timestamp_utc<@to"); command.Parameters.AddWithValue("@to", MariaTimeText.Format(to)); }
        var text = query.Text.Trim();
        if (text.Length > 0)
        {
            command.Parameters.AddWithValue("@text", "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            conditions.Add("(LOWER(actor_username) LIKE LOWER(@text) OR LOWER(target) LIKE LOWER(@text) OR LOWER(details) LIKE LOWER(@text) OR LOWER(motif) LIKE LOWER(@text))");
        }
        return conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var journalTotal = await ScalarAsync(connection, "SELECT COUNT(*) FROM audit_events", null, cancellationToken).ConfigureAwait(false);
        // "Window": the latest N events of the whole journal are the ones searched (a derived table keeps the newest N first).
        var source = window is { } limit
            ? $"(SELECT {Columns} FROM audit_events ORDER BY timestamp_utc DESC,id DESC LIMIT {Math.Max(1, limit)}) AS w"
            : "audit_events";
        int total;
        await using (var count = new MySqlCommand("", connection))
        {
            count.CommandText = $"SELECT COUNT(*) FROM {source}{Where(query, count)}";
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        var size = pageSize <= 0 ? Math.Min(AuditQueryRules.MaxRows, Math.Max(1, total)) : pageSize;
        var current = Math.Clamp(page, 1, Math.Max(1, (total + size - 1) / size));
        var events = new List<AuditEvent>();
        await using (var command = new MySqlCommand("", connection))
        {
            command.CommandText = $"SELECT {Columns} FROM {source}{Where(query, command)} ORDER BY timestamp_utc DESC,id DESC LIMIT {size} OFFSET {(current - 1) * size}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) events.Add(Read(reader));
        }
        return new(events, total, current, pageSize <= 0 ? 0 : size, journalTotal);
    }

    private static async Task<int> ScalarAsync(MySqlConnection connection, string sql, Action<MySqlCommand>? parameters, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection);
        parameters?.Invoke(command);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT COUNT(*),
                   COALESCE(SUM(timestamp_utc>=@today),0),
                   COUNT(DISTINCT LOWER(actor_username)),
                   COALESCE(SUM(entity_type=@product),0)
            FROM audit_events
            """, connection);
        command.Parameters.AddWithValue("@today", MariaTimeText.Format(todayStartUtc));
        command.Parameters.AddWithValue("@product", AuditEntities.Product);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new(Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default)
    {
        var objects = events.Where(entry => entry.EntityId.Length > 0).Select(entry => (entry.EntityType, entry.EntityId)).Distinct().Take(500).ToList();
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (objects.Count == 0) return result;
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("", connection);
        var conditions = new List<string>();
        for (var index = 0; index < objects.Count; index++)
        {
            conditions.Add($"(entity_type=@t{index} AND entity_id=@i{index})");
            command.Parameters.AddWithValue($"@t{index}", objects[index].EntityType);
            command.Parameters.AddWithValue($"@i{index}", objects[index].EntityId);
        }
        command.Parameters.AddWithValue("@delete", AuditActions.Delete);
        // The deletions that have their own exact action (AuditActions.IsDeletion) count as removals too.
        command.Parameters.AddWithValue("@deleteInvoiceTemplate", AuditActions.DeleteInvoiceTemplate);
        command.CommandText = $"SELECT entity_type,entity_id,MAX(timestamp_utc) FROM audit_events WHERE action IN (@delete, @deleteInvoiceTemplate) AND ({string.Join(" OR ", conditions)}) GROUP BY entity_type,entity_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result[AuditNavigation.ObjectKey(reader.GetString(0), reader.GetString(1))] = MariaTimeText.Parse(reader.GetString(2));
        return result;
    }
}
