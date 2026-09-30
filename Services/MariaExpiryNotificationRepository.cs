using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB tables `notification_templates` and `expiry_notifications`, created by migration 4 (MariaSchemaMigrations); this
// repository never alters the schema. The state of a notification (taken over / reminder) is global, shared by all users.
public sealed class MariaExpiryNotificationRepository(IConfiguration configuration) : IExpiryNotificationRepository
{
    private const string TemplateColumns = "id, source_key, subject, body, threshold_days, is_active, version";
    private const string NotificationColumns =
        "id, template_id, source_key, object_id, expiry_date, created_utc, acknowledged_by, acknowledged_utc, snooze_until, snooze_days, version";

    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);

    public async Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"SELECT {TemplateColumns} FROM notification_templates ORDER BY source_key, threshold_days, id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<NotificationTemplate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTemplate(reader));
        return result;
    }

    public async Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            INSERT INTO notification_templates (source_key, subject, body, threshold_days, is_active, version)
            VALUES (@key, @subject, @body, @days, @active, 0)
            """, Fields(input));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return Build(checked((int)command.LastInsertedId), input, 0);
    }

    public async Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var version = checked(original.Version + 1);
        await using var command = Command(connection, null, """
            UPDATE notification_templates SET source_key=@key, subject=@subject, body=@body, threshold_days=@days, is_active=@active, version=@version
            WHERE id=@id AND version=@oldVersion
            """, [.. Fields(input), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new NotificationOperationException("Șablonul a fost modificat sau șters între timp. Actualizează lista și reîncearcă.");
        return Build(original.Id, input, version);
    }

    public async Task DeleteTemplateAsync(NotificationTemplate original, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // The notifications of the template are removed with it (foreign key ON DELETE CASCADE).
        await using var command = Command(connection, null, "DELETE FROM notification_templates WHERE id=@id AND version=@version",
            ("@id", original.Id), ("@version", original.Version));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new NotificationOperationException("Șablonul a fost modificat sau șters între timp. Actualizează lista și reîncearcă.");
    }

    public async Task<IReadOnlyList<ExpiryNotification>> GetNotificationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"SELECT {NotificationColumns} FROM expiry_notifications ORDER BY expiry_date, id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ExpiryNotification>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadNotification(reader));
        return result;
    }

    public async Task<IReadOnlyList<CreatedNotification>> SynchronizeAsync(IReadOnlyCollection<SyncEntry> wanted, IReadOnlySet<string> unreadableSources, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var existing = new List<(int Id, string Source, (int Template, int Object, DateOnly Expiry) Key)>();
        await using (var read = Command(connection, null, "SELECT id, source_key, template_id, object_id, expiry_date FROM expiry_notifications"))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                existing.Add((checked((int)reader.GetInt64(0)), reader.GetString(1),
                    (checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)), DateOnly.FromDateTime(reader.GetDateTime(4)))));
        var wantedKeys = wanted.Select(entry => (entry.TemplateId, entry.ObjectId, entry.ExpiryDate)).ToHashSet();
        var existingKeys = existing.Select(item => item.Key).ToHashSet();

        var created = new List<CreatedNotification>();
        foreach (var entry in wanted.Where(entry => entry.CreateIfMissing && !existingKeys.Contains((entry.TemplateId, entry.ObjectId, entry.ExpiryDate))))
        {
            // INSERT IGNORE: a concurrent evaluation may have created the same notification (unique key).
            await using var insert = Command(connection, null, """
                INSERT IGNORE INTO expiry_notifications (template_id, source_key, object_id, expiry_date, created_utc, version)
                VALUES (@template, @source, @object, @expiry, @created, 0)
                """, ("@template", entry.TemplateId), ("@source", entry.SourceKey), ("@object", entry.ObjectId),
                ("@expiry", SqlDate(entry.ExpiryDate)), ("@created", MariaTimeText.Format(DateTime.UtcNow)));
            // Only the session whose insert really added the row reports the creation (the others got 0 rows).
            if (await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
                created.Add(new(checked((int)insert.LastInsertedId), entry));
        }
        foreach (var item in existing.Where(item => !wantedKeys.Contains(item.Key) && !unreadableSources.Contains(item.Source)))
        {
            await using var delete = Command(connection, null, "DELETE FROM expiry_notifications WHERE id=@id", ("@id", item.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return created;
    }

    public Task<ExpiryNotification> AcknowledgeAsync(ExpiryNotification original, string username, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(original, username, null, DateOnly.MinValue, cancellationToken);

    public Task<ExpiryNotification> SnoozeAsync(ExpiryNotification original, string username, int days, DateOnly today, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(original, username, days, today, cancellationToken);

    private async Task<ExpiryNotification> ChangeStateAsync(ExpiryNotification original, string username, int? days, DateOnly today, CancellationToken token)
    {
        EnsureWritable();
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            ExpiryNotification? current;
            await using (var read = Command(connection, transaction, $"SELECT {NotificationColumns} FROM expiry_notifications WHERE id=@id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false))
                current = await reader.ReadAsync(token).ConfigureAwait(false) ? ReadNotification(reader) : null;
            if (current is null) throw new NotificationOperationException("Notificarea nu mai există (data de expirare a fost modificată sau șablonul șters). Actualizează lista.");
            if (current.Version != original.Version)
                throw new NotificationOperationException(current.AcknowledgedBy is { } by && current.AcknowledgedBy != original.AcknowledgedBy
                    ? $"Notificarea a fost deja preluată de {by}. Actualizează lista."
                    : "Notificarea a fost modificată între timp. Actualizează lista.");
            var now = DateTime.UtcNow;
            await using var update = Command(connection, transaction, """
                UPDATE expiry_notifications SET acknowledged_by=@user, acknowledged_utc=@now, snooze_until=@until, snooze_days=@days, version=version+1
                WHERE id=@id AND version=@version
                """, ("@user", username), ("@now", MariaTimeText.Format(now)),
                ("@until", days is null ? DBNull.Value : SqlDate(today.AddDays(days.Value))),
                ("@days", days is null ? DBNull.Value : days.Value), ("@id", original.Id), ("@version", original.Version));
            if (await update.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1)
                throw new NotificationOperationException("Notificarea a fost modificată între timp. Actualizează lista.");
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return current with
            {
                AcknowledgedBy = username, AcknowledgedUtc = MariaTimeText.Parse(MariaTimeText.Format(now)),
                SnoozeUntil = days is null ? null : today.AddDays(days.Value), SnoozeDays = days, Version = original.Version + 1
            };
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private void EnsureWritable()
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new NotificationOperationException("Modificările sunt permise numai în baza BlazorStoc.");
    }

    private static NotificationTemplate ReadTemplate(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5) != 0, reader.GetInt64(6));

    private static ExpiryNotification ReadNotification(MySqlDataReader reader) =>
        new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2), checked((int)reader.GetInt64(3)),
            DateOnly.FromDateTime(reader.GetDateTime(4)), MariaTimeText.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : MariaTimeText.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.GetInt64(10));

    private static NotificationTemplate Build(int id, NotificationTemplateInput input, long version) =>
        new(id, input.SourceKey, input.Subject, input.Body, input.ThresholdDays ?? 0, input.Active, version);

    private static (string, object)[] Fields(NotificationTemplateInput input) =>
    [
        ("@key", input.SourceKey), ("@subject", input.Subject), ("@body", input.Body),
        ("@days", input.ThresholdDays ?? 0), ("@active", input.Active ? 1 : 0)
    ];

    private static object SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
