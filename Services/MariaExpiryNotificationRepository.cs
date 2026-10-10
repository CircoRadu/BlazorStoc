using System.Data;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// MariaDB tables `notification_templates` and `expiry_notifications`, created by migration 4 and extended by migration 5, and
// `notification_settings` (migration 6, MariaSchemaMigrations); this repository never alters the schema. The state of a
// notification (taken over / reminder / resolved) is global, shared by all users. Nothing here deletes a notification except
// the deletion of its template and the clean-up of old resolved notifications (DeleteResolvedAsync).
public sealed class MariaExpiryNotificationRepository(IConfiguration configuration) : IExpiryNotificationRepository
{
    private const string TemplateColumns = "id, source_key, subject, body, threshold_days, is_active, version";
    private const string NotificationColumns =
        "id, template_id, source_key, object_id, expiry_date, created_utc, acknowledged_by, acknowledged_utc, snooze_until, snooze_days, version, " +
        "resolved_by, resolved_utc, resolved_reason, resolved_auto, object_label, snapshot_values, snapshot_subject, snapshot_body, snapshot_source";


    public async Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"SELECT {TemplateColumns} FROM notification_templates ORDER BY source_key, threshold_days, id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<NotificationTemplate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTemplate(reader));
        return result;
    }

    public async Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            INSERT INTO notification_templates (source_key, subject, body, threshold_days, is_active, version)
            VALUES (@key, @subject, @body, @days, @active, 0)
            """, Fields(input));
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw ExpiryTemplateRules.DuplicateActive("șablonul existent"); }
        return Build(checked((int)command.LastInsertedId), input, 0);
    }

    public async Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var version = checked(original.Version + 1);
        await using var command = Command(connection, null, """
            UPDATE notification_templates SET source_key=@key, subject=@subject, body=@body, threshold_days=@days, is_active=@active, version=@version
            WHERE id=@id AND version=@oldVersion
            """, [.. Fields(input), ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version)]);
        int changed;
        try { changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { throw ExpiryTemplateRules.DuplicateActive("șablonul existent"); }
        if (changed != 1)
            throw new NotificationOperationException("Șablonul a fost modificat sau șters între timp. Actualizează lista și reîncearcă.");
        return Build(original.Id, input, version);
    }

    public async Task DeleteTemplateAsync(NotificationTemplate original, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        // The notifications of the template are removed with it (foreign key ON DELETE CASCADE).
        await using var command = Command(connection, null, "DELETE FROM notification_templates WHERE id=@id AND version=@version",
            ("@id", original.Id), ("@version", original.Version));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new NotificationOperationException("Șablonul a fost modificat sau șters între timp. Actualizează lista și reîncearcă.");
    }

    public async Task<IReadOnlyList<ExpiryNotification>> GetNotificationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, $"SELECT {NotificationColumns} FROM expiry_notifications ORDER BY expiry_date, id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ExpiryNotification>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadNotification(reader));
        return result;
    }

    // INSERT IGNORE: a concurrent evaluation may have created the notification of the same event (unique key on the event).
    // Only the session whose insert really added the row reports the creation.
    public async Task<CreatedNotification?> CreateNotificationAsync(SyncEntry entry, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var insert = Command(connection, null, """
            INSERT IGNORE INTO expiry_notifications (template_id, source_key, object_id, expiry_date, created_utc, object_label, snapshot_values, version)
            VALUES (@template, @source, @object, @expiry, @created, @label, @values, 0)
            """, ("@template", entry.TemplateId), ("@source", entry.SourceKey), ("@object", entry.ObjectId),
            ("@expiry", SqlDate(entry.ExpiryDate)), ("@created", MariaTimeText.Format(DateTime.UtcNow)),
            ("@label", Truncate(entry.Label, 300)), ("@values", entry.ValuesJson));
        return await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1
            ? new CreatedNotification(checked((int)insert.LastInsertedId), entry) : null;
    }

    public async Task<ExpiryNotification?> ResolveAsync(ExpiryNotification original, NotificationResolution resolution, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        await using var update = Command(connection, null, """
            UPDATE expiry_notifications SET resolved_by=@by, resolved_utc=@now, resolved_reason=@reason, resolved_auto=@auto, object_label=@label,
                snapshot_subject=@subject, snapshot_body=@body, snapshot_source=@source, version=version+1
            WHERE id=@id AND version=@version AND resolved_utc IS NULL
            """, ("@by", Truncate(resolution.ResolvedBy, 100)), ("@now", MariaTimeText.Format(now)), ("@reason", Truncate(resolution.Reason, 500)),
            ("@auto", resolution.Automatic ? 1 : 0), ("@label", Truncate(resolution.ObjectLabel, 300)), ("@subject", Truncate(resolution.Subject, 500)),
            ("@body", resolution.Body), ("@source", Truncate(resolution.SourceText, 200)), ("@id", original.Id), ("@version", original.Version));
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            return original with
            {
                ResolvedBy = resolution.ResolvedBy, ResolvedUtc = MariaTimeText.Parse(MariaTimeText.Format(now)), ResolvedReason = resolution.Reason,
                ResolvedAutomatically = resolution.Automatic, ObjectLabel = resolution.ObjectLabel, SnapshotSubject = resolution.Subject,
                SnapshotBody = resolution.Body, SnapshotSource = resolution.SourceText, Version = original.Version + 1
            };
        if (resolution.Automatic) return null;
        await using var read = Command(connection, null, $"SELECT {NotificationColumns} FROM expiry_notifications WHERE id=@id", ("@id", original.Id));
        await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var current = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadNotification(reader) : null;
        throw new NotificationOperationException(current is null
            ? "Notificarea nu mai există. Actualizează lista."
            : current.IsResolved ? $"Notificarea a fost deja rezolvată de {current.ResolvedBy}. Actualizează lista."
            : "Notificarea a fost modificată între timp. Actualizează lista.");
    }

    public async Task<bool> ReopenAsync(ExpiryNotification original, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var update = Command(connection, null, """
            UPDATE expiry_notifications SET resolved_by=NULL, resolved_utc=NULL, resolved_reason=NULL, resolved_auto=0,
                snapshot_subject=NULL, snapshot_body=NULL, snapshot_source=NULL,
                acknowledged_by=NULL, acknowledged_utc=NULL, snooze_until=NULL, snooze_days=NULL, version=version+1
            WHERE id=@id AND version=@version AND resolved_auto=1 AND resolved_utc IS NOT NULL
            """, ("@id", original.Id), ("@version", original.Version));
        return await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<int> CountOpenAsync(int templateId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null,
            "SELECT COUNT(*) FROM expiry_notifications WHERE template_id=@id AND resolved_utc IS NULL", ("@id", templateId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT purge_enabled, purge_months, last_purge_utc, version FROM notification_settings WHERE id=1");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new NotificationSettings(reader.GetInt32(0) != 0, reader.GetInt32(1), MariaTimeText.ParseOrNull(Text(reader, 2)), reader.GetInt64(3))
            : NotificationSettings.Default;
    }

    // The single row is created by the first save (the migrator account has no data rights); a concurrent first save loses.
    public async Task<NotificationSettings> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, DateTime? lastPurgeUtc,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        object last = lastPurgeUtc is { } value ? MariaTimeText.Format(value) : DBNull.Value;
        var version = original.Version < 0 ? 0 : checked(original.Version + 1);
        int changed;
        try
        {
            await using var command = original.Version < 0
                ? Command(connection, null, "INSERT INTO notification_settings (id, purge_enabled, purge_months, last_purge_utc, version) VALUES (1, @enabled, @months, @last, 0)",
                    ("@enabled", enabled ? 1 : 0), ("@months", months), ("@last", last))
                : Command(connection, null, """
                    UPDATE notification_settings SET purge_enabled=@enabled, purge_months=@months, last_purge_utc=@last, version=@version
                    WHERE id=1 AND version=@oldVersion
                    """, ("@enabled", enabled ? 1 : 0), ("@months", months), ("@last", last), ("@version", version), ("@oldVersion", original.Version));
            changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry) { changed = 0; }
        if (changed != 1) throw new NotificationOperationException("Setările au fost modificate între timp. Actualizează pagina și reîncearcă.");
        return new NotificationSettings(enabled, months, lastPurgeUtc is { } stored ? MariaTimeText.Parse(MariaTimeText.Format(stored)) : null, version);
    }

    // Only rows still resolved and unchanged since they were read are deleted (version check), so a notification reopened
    // in the meantime is never removed. Returns how many rows were deleted.
    public async Task<int> DeleteResolvedAsync(IReadOnlyList<ExpiryNotification> notifications, CancellationToken cancellationToken = default)
    {
        if (notifications.Count == 0) return 0;
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var deleted = 0;
            foreach (var notification in notifications)
            {
                await using var command = Command(connection, transaction, "DELETE FROM expiry_notifications WHERE id=@id AND version=@version AND resolved_utc IS NOT NULL",
                    ("@id", notification.Id), ("@version", notification.Version));
                deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public Task<ExpiryNotification> AcknowledgeAsync(ExpiryNotification original, string username, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(original, username, null, DateOnly.MinValue, cancellationToken);

    public Task<ExpiryNotification> SnoozeAsync(ExpiryNotification original, string username, int days, DateOnly today, CancellationToken cancellationToken = default) =>
        ChangeStateAsync(original, username, days, today, cancellationToken);

    private async Task<ExpiryNotification> ChangeStateAsync(ExpiryNotification original, string username, int? days, DateOnly today, CancellationToken token)
    {
        EnsureWritable();
        await using var connection = await MariaDb.OpenAsync(configuration, token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        try
        {
            ExpiryNotification? current;
            await using (var read = Command(connection, transaction, $"SELECT {NotificationColumns} FROM expiry_notifications WHERE id=@id FOR UPDATE", ("@id", original.Id)))
            await using (var reader = await read.ExecuteReaderAsync(token).ConfigureAwait(false))
                current = await reader.ReadAsync(token).ConfigureAwait(false) ? ReadNotification(reader) : null;
            if (current is null) throw new NotificationOperationException("Notificarea nu mai există (șablonul a fost șters). Actualizează lista.");
            if (current.IsResolved) throw new NotificationOperationException($"Notificarea a fost deja rezolvată de {current.ResolvedBy}. Actualizează lista.");
            if (current.Version != original.Version)
                throw new NotificationOperationException(current.AcknowledgedBy is { } by && current.AcknowledgedBy != original.AcknowledgedBy
                    ? $"Notificarea a fost deja preluată de {by}. Actualizează lista."
                    : "Notificarea a fost modificată între timp. Actualizează lista.");
            var now = DateTime.UtcNow;
            await using var update = Command(connection, transaction, """
                UPDATE expiry_notifications SET acknowledged_by=@user, acknowledged_utc=@now, snooze_until=@until, snooze_days=@days, version=version+1
                WHERE id=@id AND version=@version AND resolved_utc IS NULL
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
            reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)), reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.GetInt64(10),
            Text(reader, 11), reader.IsDBNull(12) ? null : MariaTimeText.Parse(reader.GetString(12)), Text(reader, 13), reader.GetInt32(14) != 0,
            Text(reader, 15), Text(reader, 16), Text(reader, 17), Text(reader, 18), Text(reader, 19));

    private static string? Text(MySqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    // The snapshot columns have fixed lengths; a longer text is cut rather than failing the whole evaluation.
    private static string Truncate(string? value, int length) => value is null ? string.Empty : value.Length <= length ? value : value[..length];

    private static NotificationTemplate Build(int id, NotificationTemplateInput input, long version) =>
        new(id, input.SourceKey, input.Subject, input.Body, input.ThresholdDays ?? 0, input.Active, version);

    private static (string, object?)[] Fields(NotificationTemplateInput input) =>
    [
        ("@key", input.SourceKey), ("@subject", input.Subject), ("@body", input.Body),
        ("@days", input.ThresholdDays ?? 0), ("@active", input.Active ? 1 : 0)
    ];

    private static object SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}
