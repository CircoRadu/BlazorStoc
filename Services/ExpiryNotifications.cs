using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// Expiry notifications. A "source" is any kind of object that carries an expiry date (today: the three vehicle
// documents; later e.g. the risk analysis of an objective, valid three years from its creation). A source is declared
// once, in code, and the rest (templates, the evaluation engine, the pages) works for every registered source.
//
// A template says: for this source, when an object is at most N days before its expiry, raise a notification with this
// subject and text. Placeholders written as <name> in the subject/text are replaced with the values of the object.

public sealed class NotificationOperationException(string message) : Exception(message);

public sealed record ExpiryPlaceholder(string Name, string Description, string Sample);

// Url is the page of the object (for the "open the object" link of a notification); null when the object has none.
public sealed record ExpiryInstance(int ObjectId, string Label, DateOnly Expiry, IReadOnlyDictionary<string, string> Values, string? Url = null);

public interface IExpirySource
{
    // Stable identifier stored in the database, for example "vehicul.rovinieta".
    string Key { get; }
    // Shown in the template form as the category ("Autovehicule") and the event ("Rovinietă").
    string Category { get; }
    string EventName { get; }
    // Completes "Data ... <event> s-a modificat de la ... la ..." in the reason written when the date of an object changes.
    string DateLabel => "expirării";
    // The reason written when the object is no longer among the instances (deleted or taken out of the records).
    string RemovedReason => "Obiectul nu mai este urmărit (a fost șters sau scos din evidență).";
    // The reason written when the date of an object changed (current is the object with its new date).
    string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Data {DateLabel} {EventName} s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";
    // The subject and text the template form proposes for this source.
    // The "days before expiry" the template form proposes for a new template of this source.
    int DefaultThresholdDays => 30;
    string DefaultSubject => ExpiryTemplateRules.DefaultSubject;
    string DefaultBody => ExpiryTemplateRules.DefaultBody;
    // Placeholders specific to this source; the common ones are added by ExpiryTemplateRules.
    IReadOnlyList<ExpiryPlaceholder> Placeholders { get; }
    Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default);
}

public sealed class VehicleExpirySource(IVehicleRepository vehicles, VehicleExpiryKind kind, string key, string eventName) : IExpirySource
{
    public const string PlateName = "numar autovehicul";
    public const string DescriptionName = "descriere autovehicul";
    public string Key => key;
    public string Category => "Autovehicule";
    public string EventName => eventName;
    public string RemovedReason => "Vehiculul a fost șters.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(PlateName, "Numărul de înmatriculare", "TS-01-ABC"),
        new(DescriptionName, "Descrierea autovehiculului", "Dacia Duster")
    ];

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ExpiryInstance>();
        foreach (var vehicle in await vehicles.GetVehiclesAsync(cancellationToken).ConfigureAwait(false))
            if (VehicleRules.GetExpiry(vehicle, kind) is { } expiry)
                result.Add(new(vehicle.Id, $"{vehicle.PlateNumber} · {vehicle.Description}", expiry,
                    new Dictionary<string, string> { [PlateName] = vehicle.PlateNumber, [DescriptionName] = vehicle.Description },
                    $"/vehicule/{vehicle.Id}"));
        return result;
    }
}

public static class ExpirySourceKeys
{
    public const string VehicleItp = "vehicul.itp";
    public const string VehicleInsurance = "vehicul.asigurare";
    public const string VehicleRovinieta = "vehicul.rovinieta";
    public const string MaintenanceDue = "mentenanta.scadenta";
    public const string ContractExpiry = "contract.expirare";
    public const string AwaitedInvoice = "intrare.factura-asteptata";
    public const string OverStock = "stoc.iesiri-peste-stoc";
}

public sealed record NotificationTemplate(int Id, string SourceKey, string Subject, string Body, int ThresholdDays, bool Active, long Version);

public sealed class NotificationTemplateInput
{
    public string SourceKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public int? ThresholdDays { get; set; } = 30;
    public bool Active { get; set; } = true;

    public static NotificationTemplateInput From(NotificationTemplate template) => new()
    {
        SourceKey = template.SourceKey, Subject = template.Subject, Body = template.Body,
        ThresholdDays = template.ThresholdDays, Active = template.Active
    };
}

// A notification stays in the list until it is resolved. It is resolved by a user (button) or by the system when its cause
// disappears (the date of the object changed, the object was removed); the reason and a snapshot of the text are kept, so a
// resolved notification stays readable whatever happens to the object. ObjectLabel/SnapshotValues are stored at creation
// and used when the object cannot be read; the Snapshot* texts are written when the notification is resolved.
public sealed record ExpiryNotification(int Id, int TemplateId, string SourceKey, int ObjectId, DateOnly ExpiryDate,
    DateTime CreatedUtc, string? AcknowledgedBy, DateTime? AcknowledgedUtc, DateOnly? SnoozeUntil, int? SnoozeDays, long Version,
    string? ResolvedBy = null, DateTime? ResolvedUtc = null, string? ResolvedReason = null, bool ResolvedAutomatically = false,
    string? ObjectLabel = null, string? SnapshotValues = null, string? SnapshotSubject = null, string? SnapshotBody = null, string? SnapshotSource = null)
{
    public bool IsResolved => ResolvedUtc is not null;

    // A notification warns while nobody took it over, or when the "remind me" period of the person who did has passed.
    // A resolved notification never warns.
    public bool IsAlert(DateOnly today) => !IsResolved && (AcknowledgedUtc is null || (SnoozeUntil is { } until && until <= today));
}

// An event is (source, object, expiry date): at most one unresolved notification exists for it, created by the single active
// template of the source. Label and ValuesJson are the object's display name and placeholder values, kept for later.
public sealed record SyncEntry(int TemplateId, string SourceKey, int ObjectId, DateOnly ExpiryDate, string Label, string ValuesJson);

public sealed record CreatedNotification(int NotificationId, SyncEntry Entry);

public sealed record NotificationResolution(string ResolvedBy, string Reason, bool Automatic, string ObjectLabel, string Subject, string Body, string SourceText);

// A notification with its text produced for the current day. Overdue ones (DaysLeft < 0) are listed first.
public sealed record NotificationView(ExpiryNotification Notification, NotificationTemplate Template, string Category, string EventName,
    string ObjectLabel, string Subject, string Body, int DaysLeft, bool IsAlert, int MaxSnoozeDays, string? Url = null)
{
    public bool IsOverdue => DaysLeft < 0;
}

// A resolved notification as it was when it was closed (texts from the snapshot, not from the object).
public sealed record ResolvedNotificationView(ExpiryNotification Notification, string SourceText, string ObjectLabel, string Subject, string Body);
public static class ExpiryTemplateRules
{
    public const int MaxSubject = 200;
    public const int MaxBody = 2000;
    public const int MaxThreshold = 730;
    public const string EventName = "eveniment";
    public const string ExpiryDateName = "data expirare";
    public const string DaysLeftName = "zile ramase";
    public const string DaysOverdueName = "zile depasire";
    // A reminder for an overdue notification has no "days left" to be bound by: 1..30 days, 7 proposed.
    public const int MaxOverdueSnoozeDays = 30;
    public const int SuggestedOverdueSnoozeDays = 7;

    public const string DefaultSubject = "Expirare <eveniment> – <numar autovehicul>";
    public const string DefaultBody =
        "Atenție: <eveniment> pentru autovehiculul <numar autovehicul> (<descriere autovehicul>) are data de expirare <data expirare> " +
        "(zile rămase: <zile ramase>; zile de depășire: <zile depasire>). Vă rugăm să luați măsurile necesare.";

    private static readonly Regex Token = new("<([^<>\r\n]{1,60})>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ExpiryPlaceholder> CommonPlaceholders { get; } =
    [
        new(EventName, "Denumirea evenimentului (ITP, Asigurare, Rovinietă)", "Rovinietă"),
        new(ExpiryDateName, "Data expirării, dd.mm.yyyy", "15.03.2027"),
        new(DaysLeftName, "Numărul de zile rămase până la expirare (0 după expirare)", "30"),
        new(DaysOverdueName, "Numărul de zile de depășire (0 înainte de expirare)", "0")
    ];

    public static IReadOnlyList<ExpiryPlaceholder> AllPlaceholders(IExpirySource source) => [.. CommonPlaceholders, .. source.Placeholders];

    public static IReadOnlyList<string> UnknownPlaceholders(string text, IExpirySource source)
    {
        var known = AllPlaceholders(source).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        return Token.Matches(text).Select(match => match.Groups[1].Value).Where(name => !known.Contains(name)).Distinct().ToArray();
    }

    // Returns the normalized input or throws with every problem found.
    public static NotificationTemplateInput Validated(NotificationTemplateInput input, IExpirySource? source)
    {
        var errors = new List<string>();
        var subject = (input.Subject ?? "").Trim();
        var body = (input.Body ?? "").Replace("\r\n", "\n").Trim();
        if (source is null) errors.Add("Alege categoria și evenimentul pentru care se creează șablonul.");
        if (subject.Length == 0) errors.Add("Completează subiectul.");
        if (subject.Length > MaxSubject) errors.Add($"Subiectul poate avea cel mult {MaxSubject} de caractere.");
        if (body.Length == 0) errors.Add("Completează textul notificării.");
        if (body.Length > MaxBody) errors.Add($"Textul poate avea cel mult {MaxBody} de caractere.");
        if (input.ThresholdDays is not (>= 1 and <= MaxThreshold))
            errors.Add($"Numărul de zile înainte de expirare trebuie să fie între 1 și {MaxThreshold}.");
        if (source is not null)
        {
            var unknown = UnknownPlaceholders(subject, source).Concat(UnknownPlaceholders(body, source)).Distinct().ToArray();
            if (unknown.Length > 0)
                errors.Add("Marcaje necunoscute: " + string.Join(", ", unknown.Select(name => $"<{name}>")) + ". Folosește doar marcajele din listă.");
        }
        if (errors.Count > 0) throw new NotificationOperationException(string.Join(" ", errors));
        return new NotificationTemplateInput { SourceKey = source!.Key, Subject = subject, Body = body, ThresholdDays = input.ThresholdDays, Active = input.Active };
    }

    public static NotificationOperationException DuplicateActive(string existingSubject) =>
        new($"Există deja un șablon activ pentru acest eveniment («{existingSubject}»). Dezactivează-l mai întâi.");

    public static int DaysLeft(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    // The reminder period may not reach the last two days before the expiry: days left minus two. Once the date has passed
    // that rule would be negative, so an overdue notification may be postponed by up to MaxOverdueSnoozeDays.
    public static int MaxSnoozeDays(DateOnly expiry, DateOnly today) =>
        DaysLeft(expiry, today) < 0 ? MaxOverdueSnoozeDays : Math.Max(0, DaysLeft(expiry, today) - 2);

    // The number of days the reminder field starts with.
    public static int SuggestedSnoozeDays(DateOnly expiry, DateOnly today) =>
        DaysLeft(expiry, today) < 0 ? SuggestedOverdueSnoozeDays : 1;

    public static string Render(string text, string eventName, ExpiryInstance instance, DateOnly today)
    {
        var values = new Dictionary<string, string>(instance.Values, StringComparer.Ordinal)
        {
            [EventName] = eventName,
            [ExpiryDateName] = StockMovementRules.DisplayDate(instance.Expiry),
            [DaysLeftName] = Math.Max(0, DaysLeft(instance.Expiry, today)).ToString(CultureInfo.InvariantCulture),
            [DaysOverdueName] = Math.Max(0, -DaysLeft(instance.Expiry, today)).ToString(CultureInfo.InvariantCulture)
        };
        return Token.Replace(text, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    public static string RenderSample(string text, IExpirySource source) =>
        Token.Replace(text, match => match.Groups[1].Value == EventName ? source.EventName
            : AllPlaceholders(source).FirstOrDefault(item => item.Name == match.Groups[1].Value)?.Sample ?? match.Value);

    public static string Target(IExpirySource? source, string subject) => source is null ? subject : $"{source.Category} · {source.EventName}: {subject}";

    public static IEnumerable<AuditChange> Changes(NotificationTemplate before, NotificationTemplate after) =>
        new AuditChange[]
        {
            new("Subiect", before.Subject, after.Subject), new("Text", before.Body, after.Body),
            new("Zile înainte de expirare", before.ThresholdDays.ToString(CultureInfo.InvariantCulture), after.ThresholdDays.ToString(CultureInfo.InvariantCulture)),
            new("Activ", before.Active ? "da" : "nu", after.Active ? "da" : "nu")
        }.Where(change => change.Before != change.After);
}

public interface IExpiryNotificationRepository
{
    Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default);
    Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default);
    Task DeleteTemplateAsync(NotificationTemplate original, CancellationToken cancellationToken = default);
    // Every notification, resolved ones included.
    Task<IReadOnlyList<ExpiryNotification>> GetNotificationsAsync(CancellationToken cancellationToken = default);
    // Null when the event already has a notification (a concurrent evaluation created it).
    Task<CreatedNotification?> CreateNotificationAsync(SyncEntry entry, CancellationToken cancellationToken = default);
    // Marks the notification resolved. Null when it was changed meanwhile and the resolution is automatic (the next evaluation
    // decides again); a manual resolution of a notification changed meanwhile throws.
    Task<ExpiryNotification?> ResolveAsync(ExpiryNotification original, NotificationResolution resolution, CancellationToken cancellationToken = default);
    // Brings an automatically resolved notification back (unread), when the date of its object returned to the notified one.
    Task<bool> ReopenAsync(ExpiryNotification original, CancellationToken cancellationToken = default);
    Task<int> CountOpenAsync(int templateId, CancellationToken cancellationToken = default);
    // The clean-up setting (defaults when nothing is stored yet). Saving throws when the settings were changed meanwhile.
    Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<NotificationSettings> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, DateTime? lastPurgeUtc, CancellationToken cancellationToken = default);
    // Deletes the given resolved notifications that are still resolved and unchanged; returns how many rows were deleted.
    Task<int> DeleteResolvedAsync(IReadOnlyList<ExpiryNotification> notifications, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> AcknowledgeAsync(ExpiryNotification original, string username, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> SnoozeAsync(ExpiryNotification original, string username, int days, DateOnly today, CancellationToken cancellationToken = default);
}

public interface IExpiryNotificationService
{
    IReadOnlyList<IExpirySource> Sources { get; }
    Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default);
    Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default);
    Task DeleteTemplateAsync(NotificationTemplate original, string reason, CancellationToken cancellationToken = default);
    // The number of days before the date within which the active template of the source raises its notification (the default when the source
    // has none). Any signed-in user may read it: the pages colour the "soon" state of the due dates with it.
    Task<int> GetThresholdDaysAsync(string sourceKey, int defaultDays, CancellationToken cancellationToken = default);
    // How many unresolved notifications a template's deletion would take away with it.
    Task<int> CountOpenNotificationsAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
    // Evaluates every source now (at most once per interval when a minimum age is given).
    Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default);
    // The unresolved notifications: the overdue ones first (oldest date first), then the others by date.
    Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default);
    // The most recently resolved notifications.
    Task<IReadOnlyList<ResolvedNotificationView>> GetResolvedViewsAsync(CancellationToken cancellationToken = default);
    Task<int> AlertCountAsync(CancellationToken cancellationToken = default);
    // Administrator only. The clean-up of old resolved notifications: the setting, what a period would remove, and the save
    // (which also runs the clean-up at once when it is switched on or the period shortened).
    Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<NotificationPurgePlan> PreviewPurgeAsync(int months, CancellationToken cancellationToken = default);
    Task<NotificationSettingsSaved> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> ResolveAsync(NotificationView view, CancellationToken cancellationToken = default);
}

public sealed class ExpiryNotificationService(IExpiryNotificationRepository repository, IEnumerable<IExpirySource> sources,
    IAccessControl access, IAuditTrail? auditTrail = null, TimeProvider? timeProvider = null) : IExpiryNotificationService
{
    public const int MaxResolvedListed = 500;
    public const string SystemRole = "Sistem";
    public const string ManualResolutionReason = "Marcată manual ca rezolvată.";

    // One evaluation per process is enough: the result is stored, and every session reads it.
    private static readonly SemaphoreSlim EvaluationGate = new(1, 1);
    private static DateTime lastEvaluationUtc = DateTime.MinValue;

    private readonly IReadOnlyList<IExpirySource> registered = sources.ToArray();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public IReadOnlyList<IExpirySource> Sources => registered;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private IExpirySource? Find(string key) => registered.FirstOrDefault(source => source.Key == key);

    public static void ResetEvaluationClock() => lastEvaluationUtc = DateTime.MinValue;

    public async Task<IReadOnlyList<NotificationTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<NotificationTemplate> CreateTemplateAsync(NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = ExpiryTemplateRules.Validated(input, Find(input.SourceKey));
        await EnsureSingleActiveAsync(value, null, cancellationToken).ConfigureAwait(false);
        var created = await repository.CreateTemplateAsync(value, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.NotificationTemplate, AuditActions.CreateNotificationTemplate,
            created.Id.ToString(CultureInfo.InvariantCulture), ExpiryTemplateRules.Target(Find(created.SourceKey), created.Subject),
            AuditDetails.Changes(new AuditChange("Subiect", string.Empty, created.Subject), new AuditChange("Zile înainte de expirare", string.Empty,
                created.ThresholdDays.ToString(CultureInfo.InvariantCulture))), string.Empty, cancellationToken).ConfigureAwait(false);
        ExpireEvaluation();
        return created;
    }

    public async Task<NotificationTemplate> UpdateTemplateAsync(NotificationTemplate original, NotificationTemplateInput input, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var value = ExpiryTemplateRules.Validated(input, Find(input.SourceKey));
        await EnsureSingleActiveAsync(value, original.Id, cancellationToken).ConfigureAwait(false);
        var updated = await repository.UpdateTemplateAsync(original, value, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, access, AuditEntities.NotificationTemplate, updated.Id.ToString(CultureInfo.InvariantCulture),
            ExpiryTemplateRules.Target(Find(updated.SourceKey), updated.Subject), ExpiryTemplateRules.Changes(original, updated),
            "Modificare șablon notificare", cancellationToken, AuditActions.EditNotificationTemplate).ConfigureAwait(false);
        ExpireEvaluation();
        return updated;
    }

    // Two active templates for the same event would raise two notifications for it: only one may be active at a time.
    private async Task EnsureSingleActiveAsync(NotificationTemplateInput value, int? ownId, CancellationToken cancellationToken)
    {
        if (!value.Active) return;
        var other = (await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(template => template.Active && template.SourceKey == value.SourceKey && template.Id != ownId);
        if (other is not null) throw ExpiryTemplateRules.DuplicateActive(other.Subject);
    }

    public async Task DeleteTemplateAsync(NotificationTemplate original, string reason, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reason)) throw new NotificationOperationException("Motivul ștergerii este obligatoriu.");
        await repository.DeleteTemplateAsync(original, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.NotificationTemplate, AuditActions.DeleteNotificationTemplate,
            original.Id.ToString(CultureInfo.InvariantCulture), ExpiryTemplateRules.Target(Find(original.SourceKey), original.Subject),
            AuditDetails.Changes(new AuditChange("Șablon șters", original.Subject, string.Empty)), reason.Trim(), cancellationToken).ConfigureAwait(false);
        ExpireEvaluation();
    }

    public async Task<int> GetThresholdDaysAsync(string sourceKey, int defaultDays, CancellationToken cancellationToken = default) =>
        (await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false)).Where(template => template.Active && template.SourceKey == sourceKey)
            .Select(template => (int?)template.ThresholdDays).FirstOrDefault() ?? defaultDays;

    public async Task<int> CountOpenNotificationsAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return await repository.CountOpenAsync(template.Id, cancellationToken).ConfigureAwait(false);
    }

    private static void ExpireEvaluation() => lastEvaluationUtc = DateTime.MinValue;

    // The three things the evaluation does, in this order: (1) closes the unresolved notifications whose cause is gone (the date of
    // the object changed, or the object is no longer there), writing the reason; (2) brings back an automatically closed
    // notification whose date returned; (3) creates the notifications of the objects that came within the period of the single
    // active template of their source (also after the date: there is no lower limit), unless the event already has one.
    // It never deletes: a notification stays in the list until it is resolved.
    public async Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default)
    {
        await EvaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (onlyIfOlderThan is { } age && DateTime.UtcNow - lastEvaluationUtc < age) return;
            var today = Today;
            var templates = await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
            var notifications = await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
            var templateById = templates.ToDictionary(template => template.Id);
            var instances = new Dictionary<string, IReadOnlyDictionary<int, ExpiryInstance>>(StringComparer.Ordinal);
            var sourceKeys = templates.Select(template => template.SourceKey).Concat(notifications.Where(item => !item.IsResolved).Select(item => item.SourceKey))
                .Distinct(StringComparer.Ordinal);
            foreach (var key in sourceKeys)
                if (await ReadInstancesAsync(Find(key), cancellationToken).ConfigureAwait(false) is { } byId) instances[key] = byId;

            var journal = new List<(string Action, ExpiryNotification Notification, string Label, string Details)>();
            foreach (var notification in notifications.Where(item => !item.IsResolved))
            {
                if (!instances.TryGetValue(notification.SourceKey, out var byId)) continue; // unreadable source: keep everything
                var source = Find(notification.SourceKey)!;
                byId.TryGetValue(notification.ObjectId, out var current);
                if (current is not null && current.Expiry == notification.ExpiryDate) continue;
                var reason = current is null ? source.RemovedReason : source.DateChangedReason(notification.ExpiryDate, current.Expiry, current);
                var resolution = BuildResolution(SystemUser, reason, true, notification, templateById.GetValueOrDefault(notification.TemplateId), source, current, today);
                if (await repository.ResolveAsync(notification, resolution, cancellationToken).ConfigureAwait(false) is not null)
                    journal.Add((AuditActions.AutoResolveNotification, notification, resolution.ObjectLabel, reason));
            }

            var created = new List<CreatedNotification>();
            foreach (var template in templates.Where(template => template.Active).OrderBy(template => template.Id).GroupBy(template => template.SourceKey).Select(group => group.First()))
            {
                if (!instances.TryGetValue(template.SourceKey, out var byId)) continue;
                foreach (var instance in byId.Values)
                {
                    if (ExpiryTemplateRules.DaysLeft(instance.Expiry, today) > template.ThresholdDays) continue;
                    var existing = notifications.FirstOrDefault(item => item.SourceKey == template.SourceKey && item.ObjectId == instance.ObjectId && item.ExpiryDate == instance.Expiry);
                    if (existing is null)
                    {
                        var entry = new SyncEntry(template.Id, template.SourceKey, instance.ObjectId, instance.Expiry, instance.Label, JsonSerializer.Serialize(instance.Values));
                        if (await repository.CreateNotificationAsync(entry, cancellationToken).ConfigureAwait(false) is { } added) created.Add(added);
                    }
                    else if (existing.IsResolved && existing.ResolvedAutomatically &&
                             await repository.ReopenAsync(existing, cancellationToken).ConfigureAwait(false))
                        journal.Add((AuditActions.ReopenNotification, existing, existing.ObjectLabel ?? instance.Label,
                            $"Data {Find(template.SourceKey)?.DateLabel ?? "expirării"} a revenit la {StockMovementRules.DisplayDate(instance.Expiry)}."));
                }
            }
            await RecordCreatedAsync(created, templates, instances, today, cancellationToken).ConfigureAwait(false);
            await RecordSystemAsync(journal, cancellationToken).ConfigureAwait(false);
            lastEvaluationUtc = DateTime.UtcNow;
            await RunScheduledPurgeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { EvaluationGate.Release(); }
    }

    public async Task<NotificationSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        return await repository.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<NotificationPurgePlan> PreviewPurgeAsync(int months, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        EnsureValidMonths(months);
        return await PlanPurgeAsync(months, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureValidMonths(int months)
    {
        if (!NotificationPurgeRules.IsValidMonths(months))
            throw new NotificationOperationException($"Vechimea trebuie să fie între {NotificationPurgeRules.MinMonths} și {NotificationPurgeRules.MaxMonths} de luni.");
    }

    // Stores the switch and the period. With the switch on, the clean-up runs at once, so what the confirmation announced
    // (switch turned on, or period shortened) is what happens; the daily run then continues from today.
    public async Task<NotificationSettingsSaved> SaveSettingsAsync(NotificationSettings original, bool enabled, int months, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        EnsureValidMonths(months);
        var changes = NotificationPurgeRules.Changes(original, enabled, months).ToArray();
        if (changes.Length == 0) return new(original, 0, null);
        var saved = await repository.SaveSettingsAsync(original, enabled, months, original.LastPurgeUtc, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.NotificationSettings, AuditActions.EditNotificationSettings,
            "1", "Ștergerea notificărilor rezolvate", AuditDetails.Changes(changes), string.Empty, cancellationToken).ConfigureAwait(false);
        if (!enabled) return new(saved, 0, null);

        var plan = await PlanPurgeAsync(months, cancellationToken).ConfigureAwait(false);
        var removed = await repository.DeleteResolvedAsync(plan.Removable, cancellationToken).ConfigureAwait(false);
        try { saved = await repository.SaveSettingsAsync(saved, true, months, clock.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false); }
        catch (NotificationOperationException) { saved = await repository.GetSettingsAsync(cancellationToken).ConfigureAwait(false); }
        if (removed > 0)
            await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.Notification, AuditActions.PurgeResolvedNotifications,
                string.Empty, "Notificări rezolvate", NotificationPurgeRules.RemovedText(removed, plan.Cutoff), string.Empty, cancellationToken).ConfigureAwait(false);
        return new(saved, removed, plan.Cutoff);
    }

    // The resolved notifications older than the limit that a clean-up would delete. One resolved by a user stays while its event
    // is still current (same object, same date): deleting it would make the next evaluation create the notification again,
    // unresolved. One resolved by the system goes (its cause is gone, or it is reopened by the evaluation when it comes back).
    // When the source of a manual one cannot be read, it stays: nothing is deleted on a guess.
    private async Task<NotificationPurgePlan> PlanPurgeAsync(int months, CancellationToken cancellationToken)
    {
        var zone = clock.LocalTimeZone;
        var cutoff = NotificationPurgeRules.Cutoff(Today, months);
        var old = (await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.IsResolved && NotificationPurgeRules.ResolvedDay(item, zone) < cutoff).ToList();
        var removable = old.Where(item => item.ResolvedAutomatically).ToList();
        foreach (var group in old.Where(item => !item.ResolvedAutomatically).GroupBy(item => item.SourceKey))
        {
            var source = Find(group.Key);
            var byId = source is null ? null : await ReadInstancesAsync(source, cancellationToken).ConfigureAwait(false);
            if (source is not null && byId is null) continue;
            removable.AddRange(group.Where(item => byId is null || !byId.TryGetValue(item.ObjectId, out var current) || current.Expiry != item.ExpiryDate));
        }
        return new(cutoff, removable);
    }

    // Once a day, with the evaluation, when the switch is on. The day is claimed first (a version-checked write of the last-run
    // time), so two instances never clean up twice; a failed clean-up gives the claim back so the next evaluation retries.
    // Nothing here may fail the evaluation. Journal: written by the system, only when something was removed.
    private async Task RunScheduledPurgeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await repository.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (!NotificationPurgeRules.IsDue(settings, Today, clock.LocalTimeZone)) return;
            NotificationSettings claimed;
            try { claimed = await repository.SaveSettingsAsync(settings, true, settings.PurgeMonths, clock.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false); }
            catch (NotificationOperationException) { return; }
            int removed; NotificationPurgePlan plan;
            try
            {
                plan = await PlanPurgeAsync(settings.PurgeMonths, cancellationToken).ConfigureAwait(false);
                removed = await repository.DeleteResolvedAsync(plan.Removable, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                try { await repository.SaveSettingsAsync(claimed, true, settings.PurgeMonths, settings.LastPurgeUtc, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception inner) when (inner is not OperationCanceledException) { }
                return;
            }
            if (removed > 0 && auditTrail is not null)
                await auditTrail.RecordAsync(new AuditWrite(SystemUser, SystemRole, AuditEntities.Notification, AuditActions.PurgeResolvedNotifications,
                    "Notificări rezolvate", NotificationPurgeRules.RemovedText(removed, plan.Cutoff), string.Empty, string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }

    public const string SystemUser = "sistem";

    private static async Task<IReadOnlyDictionary<int, ExpiryInstance>?> ReadInstancesAsync(IExpirySource? source, CancellationToken cancellationToken)
    {
        if (source is null) return null;
        try
        {
            var byId = new Dictionary<int, ExpiryInstance>();
            foreach (var instance in await source.GetInstancesAsync(cancellationToken).ConfigureAwait(false)) byId[instance.ObjectId] = instance;
            return byId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return null; }
    }

    // The instance as it was when the notification was created (used when the object cannot be read or is gone).
    private static ExpiryInstance Stored(ExpiryNotification notification)
    {
        IReadOnlyDictionary<string, string> values = new Dictionary<string, string>();
        try { if (!string.IsNullOrEmpty(notification.SnapshotValues)) values = JsonSerializer.Deserialize<Dictionary<string, string>>(notification.SnapshotValues) ?? values; }
        catch (JsonException) { }
        return new(notification.ObjectId, notification.ObjectLabel ?? $"#{notification.ObjectId}", notification.ExpiryDate, values);
    }

    private static string SourceText(IExpirySource? source, string fallbackKey) =>
        source is null ? fallbackKey : source.Category.Length == 0 ? source.EventName : $"{source.Category} · {source.EventName}";

    // The texts of the notification as they read on the day it is closed, for the resolved list.
    private static NotificationResolution BuildResolution(string by, string reason, bool automatic, ExpiryNotification notification,
        NotificationTemplate? template, IExpirySource source, ExpiryInstance? current, DateOnly today)
    {
        var known = current ?? Stored(notification);
        var instance = new ExpiryInstance(notification.ObjectId, known.Label, notification.ExpiryDate, known.Values);
        return new(by, reason, automatic, known.Label,
            template is null ? source.EventName : ExpiryTemplateRules.Render(template.Subject, source.EventName, instance, today),
            template is null ? string.Empty : ExpiryTemplateRules.Render(template.Body, source.EventName, instance, today), SourceText(source, notification.SourceKey));
    }

    // The system journals what it does by itself ("Notificare creată", "Rezolvare automată notificare", "Redeschidere
    // automată notificare"; actor "sistem"), so it can be followed. A journal failure never hides a stored change.
    private async Task RecordCreatedAsync(IReadOnlyList<CreatedNotification> created, IReadOnlyList<NotificationTemplate> templates,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, ExpiryInstance>> instances, DateOnly today, CancellationToken cancellationToken)
    {
        if (auditTrail is null) return;
        foreach (var item in created)
        {
            try
            {
                var source = Find(item.Entry.SourceKey);
                var template = templates.FirstOrDefault(candidate => candidate.Id == item.Entry.TemplateId);
                await auditTrail.RecordAsync(new AuditWrite(SystemUser, SystemRole, AuditEntities.Notification, AuditActions.NotificationCreated,
                    $"{source?.EventName ?? item.Entry.SourceKey} · {item.Entry.Label}",
                    AuditDetails.Changes(
                        new AuditChange("Expiră la", string.Empty, StockMovementRules.DisplayDate(item.Entry.ExpiryDate)),
                        new AuditChange("Zile rămase", string.Empty, ExpiryTemplateRules.DaysLeft(item.Entry.ExpiryDate, today).ToString(CultureInfo.InvariantCulture)),
                        new AuditChange("Șablon", string.Empty, template?.Subject ?? $"#{item.Entry.TemplateId}")),
                    string.Empty, item.NotificationId.ToString(CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
    }

    private async Task RecordSystemAsync(IReadOnlyList<(string Action, ExpiryNotification Notification, string Label, string Details)> journal, CancellationToken cancellationToken)
    {
        if (auditTrail is null) return;
        foreach (var (action, notification, label, details) in journal)
        {
            try
            {
                var source = Find(notification.SourceKey);
                await auditTrail.RecordAsync(new AuditWrite(SystemUser, SystemRole, AuditEntities.Notification, action,
                    $"{source?.EventName ?? notification.SourceKey} · {label}",
                    AuditDetails.Changes(new AuditChange("Expiră la", StockMovementRules.DisplayDate(notification.ExpiryDate), string.Empty),
                        new AuditChange("Motiv", string.Empty, details)),
                    string.Empty, notification.Id.ToString(CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
    }

    public async Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var open = (await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false)).Where(item => !item.IsResolved).ToList();
        if (open.Count == 0) return [];
        var templates = (await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(template => template.Id);
        var live = new Dictionary<string, IReadOnlyDictionary<int, ExpiryInstance>?>(StringComparer.Ordinal);
        var result = new List<NotificationView>();
        foreach (var notification in open)
        {
            if (!templates.TryGetValue(notification.TemplateId, out var template)) continue;
            var source = Find(notification.SourceKey);
            if (!live.TryGetValue(notification.SourceKey, out var byId))
                live[notification.SourceKey] = byId = await ReadInstancesAsync(source, cancellationToken).ConfigureAwait(false);
            // The live object when it still has the notified date; otherwise (not yet closed by the next evaluation, or the
            // source cannot be read) the object as it was stored, so the notification never disappears from the list.
            ExpiryInstance? current = null;
            if (byId is not null && byId.TryGetValue(notification.ObjectId, out var found) && found.Expiry == notification.ExpiryDate) current = found;
            var instance = current ?? Stored(notification);
            var eventName = source?.EventName ?? notification.SourceKey;
            result.Add(new(notification, template, source?.Category ?? string.Empty, eventName, instance.Label,
                ExpiryTemplateRules.Render(template.Subject, eventName, instance, today), ExpiryTemplateRules.Render(template.Body, eventName, instance, today),
                ExpiryTemplateRules.DaysLeft(notification.ExpiryDate, today), notification.IsAlert(today),
                ExpiryTemplateRules.MaxSnoozeDays(notification.ExpiryDate, today), current?.Url));
        }
        return result.OrderByDescending(view => view.IsOverdue).ThenBy(view => view.Notification.ExpiryDate)
            .ThenBy(view => view.ObjectLabel, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<ResolvedNotificationView>> GetResolvedViewsAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false)).Where(item => item.IsResolved)
            .OrderByDescending(item => item.ResolvedUtc).ThenByDescending(item => item.Id).Take(MaxResolvedListed)
            .Select(item => new ResolvedNotificationView(item, item.SnapshotSource ?? SourceText(Find(item.SourceKey), item.SourceKey),
                item.ObjectLabel ?? $"#{item.ObjectId}", item.SnapshotSubject ?? string.Empty, item.SnapshotBody ?? string.Empty)).ToList();

    public async Task<int> AlertCountAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        return (await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false)).Count(item => item.IsAlert(today));
    }

    public async Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default)
    {
        var user = await OperatorAsync(cancellationToken).ConfigureAwait(false);
        var updated = await repository.AcknowledgeAsync(view.Notification, user, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.Notification, AuditActions.AcknowledgeNotification,
            updated.Id.ToString(CultureInfo.InvariantCulture), Target(view),
            AuditDetails.Changes(new AuditChange("Stare", StateLabel(view.Notification, Today), "preluată de " + user)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default)
    {
        var user = await OperatorAsync(cancellationToken).ConfigureAwait(false);
        var today = Today;
        var max = ExpiryTemplateRules.MaxSnoozeDays(view.Notification.ExpiryDate, today);
        if (max < 1) throw new NotificationOperationException("Amânarea nu mai este posibilă: expirarea este prea aproape (limita este numărul de zile rămase minus 2).");
        if (days < 1 || days > max)
            throw new NotificationOperationException(view.Notification.ExpiryDate < today
                ? $"Alege un număr de zile între 1 și {max}."
                : $"Alege un număr de zile între 1 și {max} (zilele rămase până la expirare minus 2).");
        var updated = await repository.SnoozeAsync(view.Notification, user, days, today, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.Notification, AuditActions.SnoozeNotification,
            updated.Id.ToString(CultureInfo.InvariantCulture), Target(view),
            AuditDetails.Changes(new AuditChange("Amânare", "fără", $"{days} zile, până la {StockMovementRules.DisplayDate(today.AddDays(days))}"),
                new AuditChange("Stare", StateLabel(view.Notification, today), "preluată de " + user)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<ExpiryNotification> ResolveAsync(NotificationView view, CancellationToken cancellationToken = default)
    {
        var user = await OperatorAsync(cancellationToken).ConfigureAwait(false);
        var resolution = new NotificationResolution(user, ManualResolutionReason, false, view.ObjectLabel, view.Subject, view.Body,
            view.Category.Length == 0 ? view.EventName : $"{view.Category} · {view.EventName}");
        var updated = await repository.ResolveAsync(view.Notification, resolution, cancellationToken).ConfigureAwait(false)
            ?? throw new NotificationOperationException("Notificarea a fost modificată între timp. Actualizează lista.");
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.Notification, AuditActions.ResolveNotification,
            updated.Id.ToString(CultureInfo.InvariantCulture), Target(view),
            AuditDetails.Changes(new AuditChange("Stare", StateLabel(view.Notification, Today), "rezolvată de " + user)),
            string.Empty, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<string> OperatorAsync(CancellationToken cancellationToken) =>
        await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } user
            ? user : throw new NotificationOperationException("Autentifică-te pentru a prelua notificările.");

    // The state before the operation, as written in the journal.
    private static string StateLabel(ExpiryNotification notification, DateOnly today) =>
        notification.AcknowledgedBy is null ? "nepreluată"
        : notification.IsAlert(today) ? $"amânarea cerută de {notification.AcknowledgedBy} încheiată"
        : notification.SnoozeUntil is { } until ? $"amânată de {notification.AcknowledgedBy} până la {StockMovementRules.DisplayDate(until)}"
        : $"preluată de {notification.AcknowledgedBy}";

    private static string Target(NotificationView view) => $"{view.EventName} · {view.ObjectLabel}";
}
