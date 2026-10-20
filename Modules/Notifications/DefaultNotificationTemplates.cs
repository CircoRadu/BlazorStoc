using MySqlConnector;

namespace BlazorStoc.Services;

// Every event of the registry starts with a template made from its own default subject and text (the ones shown by "Completează un text-exemplu"),
// so notifications work without the administrator writing each template first. The templates that already exist are left as they are, and an event is
// treated once: a template the administrator deletes later is not made again.
public static class DefaultNotificationTemplates
{
    public static async Task<int> SeedAsync(IConfiguration configuration, IExpiryNotificationRepository repository, IEnumerable<IExpirySource> sources,
        IAuditTrail? audit, ILogger logger, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        var done = new HashSet<string>(StringComparer.Ordinal);
        await using (var read = new MySqlCommand("SELECT source_key FROM notification_template_seeds", connection))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) done.Add(reader.GetString(0));

        var created = 0;
        foreach (var source in sources)
        {
            if (done.Contains(source.Key)) continue;
            if (!existing.Any(template => template.SourceKey == source.Key))
            {
                var input = new NotificationTemplateInput
                {
                    SourceKey = source.Key, Subject = source.DefaultSubject, Body = source.DefaultBody,
                    ThresholdDays = Math.Clamp(source.DefaultThresholdDays, 1, ExpiryTemplateRules.MaxThreshold), Active = true
                };
                try
                {
                    var template = await repository.CreateTemplateAsync(ExpiryTemplateRules.Validated(input, source), cancellationToken).ConfigureAwait(false);
                    created++;
                    await AuditRecorder.RecordActionAsync(audit, new SystemAccess(), AuditEntities.NotificationTemplate, AuditActions.CreateNotificationTemplate,
                        template.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ExpiryTemplateRules.Target(source, template.Subject),
                        AuditDetails.Changes(new AuditChange("Subiect", string.Empty, template.Subject)), "Șablon implicit creat de aplicație.", cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is NotificationOperationException or MySqlException)
                {
                    logger.LogWarning("The default template of {Source} could not be created ({ErrorType}).", source.Key, exception.GetType().Name);
                    continue;
                }
            }
            await using var mark = new MySqlCommand("INSERT IGNORE INTO notification_template_seeds (source_key, seeded_utc) VALUES (@key, @utc)", connection);
            mark.Parameters.AddWithValue("@key", source.Key);
            mark.Parameters.AddWithValue("@utc", MariaTimeText.Format(DateTime.UtcNow));
            await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return created;
    }
}
