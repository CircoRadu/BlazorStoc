using BlazorStoc.Services;

namespace BlazorStoc.Modules.Notifications;

// The entry points of the notifications module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails at once
// with ModuleUnavailableException. The answers of the module (NotificationOperationException, access denied) are not faults.
// Three members are conveniences of other pages and never fail them: the threshold that colours the due dates, the number of warnings next to the menu entry and the evaluation that
// precedes it. While the module is out or failing they answer the default (the days the page proposes, no warnings, nothing evaluated) through TryRunAsync.
internal sealed class GuardedExpiryNotificationService(IExpiryNotificationService inner, ModuleRegistry modules) : IExpiryNotificationService
{
    private const string Id = NotificationsModule.ModuleId;

    public IReadOnlyList<IExpirySource> Sources => inner.Sources;

    public Task<int> GetThresholdDaysAsync(string sourceKey, int defaultDays, CancellationToken cancellationToken = default) =>
        modules.TryRunAsync(Id, () => inner.GetThresholdDaysAsync(sourceKey, defaultDays, cancellationToken), defaultDays);

    public Task<int> AlertCountAsync(CancellationToken cancellationToken = default) =>
        modules.TryRunAsync(Id, () => inner.AlertCountAsync(cancellationToken), 0);

    public Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default) =>
        modules.TryRunAsync<object?>(Id, async () => { await inner.EvaluateAsync(onlyIfOlderThan, cancellationToken).ConfigureAwait(false); return null; }, null);

    public Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetTemplatesAsync(cancellationToken));
    public Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateTemplateAsync(input, cancellationToken));
    public Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateTemplateAsync(original, input, cancellationToken));
    public Task DeleteTemplateAsync(NotificationTemplate original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteTemplateAsync(original, reason, cancellationToken));
    public Task<int> CountOpenNotificationsAsync(NotificationTemplate template, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CountOpenNotificationsAsync(template, cancellationToken));
    public Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetViewsAsync(cancellationToken));
    public Task<IReadOnlyList<ResolvedNotificationView>> GetResolvedViewsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetResolvedViewsAsync(cancellationToken));
    public Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetSettingsAsync(cancellationToken));
    public Task<NotificationPurgePlan> PreviewPurgeAsync(int months, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.PreviewPurgeAsync(months, cancellationToken));
    public Task<NotificationSettingsSaved> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveSettingsAsync(original, enabled, months, cancellationToken));
    public Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AcknowledgeAsync(view, cancellationToken));
    public Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SnoozeAsync(view, days, cancellationToken));
    public Task<ExpiryNotification> ResolveAsync(NotificationView view, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ResolveAsync(view, cancellationToken));
}

// The stand-in while the module is switched off or did not start: the conveniences answer their defaults, everything else says the module is out.
internal sealed class UnavailableExpiryNotificationService : IExpiryNotificationService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(NotificationsModule.ModuleId));

    public IReadOnlyList<IExpirySource> Sources => [];
    public Task<int> GetThresholdDaysAsync(string sourceKey, int defaultDays, CancellationToken cancellationToken = default) => Task.FromResult(defaultDays);
    public Task<int> AlertCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<NotificationTemplate>>();
    public Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default) => Out<NotificationTemplate>();
    public Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default) => Out<NotificationTemplate>();
    public Task DeleteTemplateAsync(NotificationTemplate original, string reason, CancellationToken cancellationToken = default) => Out<object>();
    public Task<int> CountOpenNotificationsAsync(NotificationTemplate template, CancellationToken cancellationToken = default) => Out<int>();
    public Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<NotificationView>>();
    public Task<IReadOnlyList<ResolvedNotificationView>> GetResolvedViewsAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<ResolvedNotificationView>>();
    public Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Out<NotificationSettings>();
    public Task<NotificationPurgePlan> PreviewPurgeAsync(int months, CancellationToken cancellationToken = default) => Out<NotificationPurgePlan>();
    public Task<NotificationSettingsSaved> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, CancellationToken cancellationToken = default) => Out<NotificationSettingsSaved>();
    public Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default) => Out<ExpiryNotification>();
    public Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default) => Out<ExpiryNotification>();
    public Task<ExpiryNotification> ResolveAsync(NotificationView view, CancellationToken cancellationToken = default) => Out<ExpiryNotification>();
}
