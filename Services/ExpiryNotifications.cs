using System.Globalization;
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

public sealed record ExpiryInstance(int ObjectId, string Label, DateOnly Expiry, IReadOnlyDictionary<string, string> Values);

public interface IExpirySource
{
    // Stable identifier stored in the database, for example "vehicul.rovinieta".
    string Key { get; }
    // Shown in the template form as the category ("Autovehicule") and the event ("Rovinietă").
    string Category { get; }
    string EventName { get; }
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
                    new Dictionary<string, string> { [PlateName] = vehicle.PlateNumber, [DescriptionName] = vehicle.Description }));
        return result;
    }
}

public static class ExpirySourceKeys
{
    public const string VehicleItp = "vehicul.itp";
    public const string VehicleInsurance = "vehicul.asigurare";
    public const string VehicleRovinieta = "vehicul.rovinieta";
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

public sealed record ExpiryNotification(int Id, int TemplateId, string SourceKey, int ObjectId, DateOnly ExpiryDate,
    DateTime CreatedUtc, string? AcknowledgedBy, DateTime? AcknowledgedUtc, DateOnly? SnoozeUntil, int? SnoozeDays, long Version)
{
    // A notification warns while nobody took it over, or when the "remind me" period of the person who did has passed.
    public bool IsAlert(DateOnly today) => AcknowledgedUtc is null || (SnoozeUntil is { } until && until <= today);
}

// CreateIfMissing is false for the templates that are switched off: their existing notifications (with their state) are
// kept, hidden, so switching the template on again brings them back as they were; no new ones are created meanwhile.
public sealed record SyncEntry(int TemplateId, string SourceKey, int ObjectId, DateOnly ExpiryDate, bool CreateIfMissing = true);

public sealed record CreatedNotification(int NotificationId, SyncEntry Entry);

// A notification with its text produced for the current day.
public sealed record NotificationView(ExpiryNotification Notification, NotificationTemplate Template, string Category, string EventName,
    string ObjectLabel, string Subject, string Body, int DaysLeft, bool IsAlert, int MaxSnoozeDays);

public static class ExpiryTemplateRules
{
    public const int MaxSubject = 200;
    public const int MaxBody = 2000;
    public const int MaxThreshold = 730;
    public const string EventName = "eveniment";
    public const string ExpiryDateName = "data expirare";
    public const string DaysLeftName = "zile ramase";

    public const string DefaultSubject = "Expirare <eveniment> – <numar autovehicul>";
    public const string DefaultBody =
        "Atenție: <eveniment> pentru autovehiculul <numar autovehicul> (<descriere autovehicul>) expiră la data de <data expirare>, " +
        "peste <zile ramase> zile. Vă rugăm să luați măsurile necesare înainte de această dată.";

    private static readonly Regex Token = new("<([^<>\r\n]{1,60})>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ExpiryPlaceholder> CommonPlaceholders { get; } =
    [
        new(EventName, "Denumirea evenimentului (ITP, Asigurare, Rovinietă)", "Rovinietă"),
        new(ExpiryDateName, "Data expirării, dd.mm.yyyy", "15.03.2027"),
        new(DaysLeftName, "Numărul de zile rămase până la expirare", "30")
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

    public static int DaysLeft(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    // The reminder period may not reach the last two days before the expiry: days left minus two.
    public static int MaxSnoozeDays(DateOnly expiry, DateOnly today) => Math.Max(0, DaysLeft(expiry, today) - 2);

    public static string Render(string text, string eventName, ExpiryInstance instance, DateOnly today)
    {
        var values = new Dictionary<string, string>(instance.Values, StringComparer.Ordinal)
        {
            [EventName] = eventName,
            [ExpiryDateName] = StockMovementRules.DisplayDate(instance.Expiry),
            [DaysLeftName] = DaysLeft(instance.Expiry, today).ToString(CultureInfo.InvariantCulture)
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
    Task<IReadOnlyList<ExpiryNotification>> GetNotificationsAsync(CancellationToken cancellationToken = default);
    // Makes the stored notifications match the wanted ones (creates the missing ones that may be created, removes the ones
    // no longer wanted, except those of the sources that could not be read). Returns the notifications it created.
    Task<IReadOnlyList<CreatedNotification>> SynchronizeAsync(IReadOnlyCollection<SyncEntry> wanted, IReadOnlySet<string> unreadableSources, CancellationToken cancellationToken = default);
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
    // Evaluates every active template now (at most once per interval when a minimum age is given).
    Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default);
    Task<int> AlertCountAsync(CancellationToken cancellationToken = default);
    Task<ExpiryNotification> AcknowledgeAsync(NotificationView view, CancellationToken cancellationToken = default);
    Task<ExpiryNotification> SnoozeAsync(NotificationView view, int days, CancellationToken cancellationToken = default);
}

public sealed class ExpiryNotificationService(IExpiryNotificationRepository repository, IEnumerable<IExpirySource> sources,
    IAccessControl access, IAuditTrail? auditTrail = null, TimeProvider? timeProvider = null) : IExpiryNotificationService
{
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
        var updated = await repository.UpdateTemplateAsync(original, value, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, access, AuditEntities.NotificationTemplate, updated.Id.ToString(CultureInfo.InvariantCulture),
            ExpiryTemplateRules.Target(Find(updated.SourceKey), updated.Subject), ExpiryTemplateRules.Changes(original, updated),
            "Modificare șablon notificare", cancellationToken, AuditActions.EditNotificationTemplate).ConfigureAwait(false);
        ExpireEvaluation();
        return updated;
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

    private static void ExpireEvaluation() => lastEvaluationUtc = DateTime.MinValue;

    public async Task EvaluateAsync(TimeSpan? onlyIfOlderThan = null, CancellationToken cancellationToken = default)
    {
        await EvaluationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (onlyIfOlderThan is { } age && DateTime.UtcNow - lastEvaluationUtc < age) return;
            var today = Today;
            var wanted = new List<SyncEntry>();
            var labels = new Dictionary<(string Source, int Object), string>();
            var unreadable = new HashSet<string>(StringComparer.Ordinal);
            var templates = await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var group in templates.GroupBy(template => template.SourceKey))
            {
                var source = Find(group.Key);
                if (source is null) { unreadable.Add(group.Key); continue; }
                IReadOnlyList<ExpiryInstance> instances;
                try { instances = await source.GetInstancesAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    unreadable.Add(group.Key);
                    continue;
                }
                foreach (var instance in instances) labels[(group.Key, instance.ObjectId)] = instance.Label;
                foreach (var template in group)
                    foreach (var instance in instances)
                        if (instance.Expiry >= today && ExpiryTemplateRules.DaysLeft(instance.Expiry, today) <= template.ThresholdDays)
                            wanted.Add(new(template.Id, group.Key, instance.ObjectId, instance.Expiry, template.Active));
            }
            var created = await repository.SynchronizeAsync(wanted, unreadable, cancellationToken).ConfigureAwait(false);
            await RecordCreatedAsync(created, templates, labels, today, cancellationToken).ConfigureAwait(false);
            lastEvaluationUtc = DateTime.UtcNow;
        }
        finally { EvaluationGate.Release(); }
    }

    // The system journals every notification it creates ("Notificare creată", actor "sistem"), so it can be followed whether
    // the notifications were created. A journal failure never hides the notification that was already stored.
    private async Task RecordCreatedAsync(IReadOnlyList<CreatedNotification> created, IReadOnlyList<NotificationTemplate> templates,
        IReadOnlyDictionary<(string Source, int Object), string> labels, DateOnly today, CancellationToken cancellationToken)
    {
        if (auditTrail is null) return;
        foreach (var item in created)
        {
            try
            {
                var source = Find(item.Entry.SourceKey);
                var template = templates.FirstOrDefault(candidate => candidate.Id == item.Entry.TemplateId);
                var label = labels.TryGetValue((item.Entry.SourceKey, item.Entry.ObjectId), out var known) ? known : $"#{item.Entry.ObjectId}";
                await auditTrail.RecordAsync(new AuditWrite("sistem", SystemRole, AuditEntities.Notification, AuditActions.NotificationCreated,
                    $"{source?.EventName ?? item.Entry.SourceKey} · {label}",
                    AuditDetails.Changes(
                        new AuditChange("Expiră la", string.Empty, StockMovementRules.DisplayDate(item.Entry.ExpiryDate)),
                        new AuditChange("Zile rămase", string.Empty, ExpiryTemplateRules.DaysLeft(item.Entry.ExpiryDate, today).ToString(CultureInfo.InvariantCulture)),
                        new AuditChange("Șablon", string.Empty, template?.Subject ?? $"#{item.Entry.TemplateId}")),
                    string.Empty, item.NotificationId.ToString(CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
        }
    }

    public const string SystemRole = "Sistem";

    public async Task<IReadOnlyList<NotificationView>> GetViewsAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var notifications = await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
        if (notifications.Count == 0) return [];
        var templates = (await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(template => template.Id);
        var instancesBySource = new Dictionary<string, Dictionary<int, ExpiryInstance>>(StringComparer.Ordinal);
        var result = new List<NotificationView>();
        foreach (var notification in notifications)
        {
            var source = Find(notification.SourceKey);
            if (source is null || !templates.TryGetValue(notification.TemplateId, out var template) || !template.Active) continue;
            if (!instancesBySource.TryGetValue(source.Key, out var byId))
            {
                byId = (await source.GetInstancesAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(item => item.ObjectId);
                instancesBySource[source.Key] = byId;
            }
            // Removed or re-dated objects are cleaned up by the next evaluation; they are not shown meanwhile.
            if (!byId.TryGetValue(notification.ObjectId, out var instance) || instance.Expiry != notification.ExpiryDate) continue;
            result.Add(new(notification, template, source.Category, source.EventName, instance.Label,
                ExpiryTemplateRules.Render(template.Subject, source.EventName, instance, today),
                ExpiryTemplateRules.Render(template.Body, source.EventName, instance, today),
                ExpiryTemplateRules.DaysLeft(instance.Expiry, today), notification.IsAlert(today),
                ExpiryTemplateRules.MaxSnoozeDays(instance.Expiry, today)));
        }
        return result.OrderByDescending(view => view.IsAlert).ThenBy(view => view.Notification.ExpiryDate).ThenBy(view => view.ObjectLabel, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<int> AlertCountAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var active = (await repository.GetTemplatesAsync(cancellationToken).ConfigureAwait(false)).Where(template => template.Active).Select(template => template.Id).ToHashSet();
        return (await repository.GetNotificationsAsync(cancellationToken).ConfigureAwait(false)).Count(item => active.Contains(item.TemplateId) && item.IsAlert(today));
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
        if (days < 1 || days > max) throw new NotificationOperationException($"Alege un număr de zile între 1 și {max} (zilele rămase până la expirare minus 2).");
        var updated = await repository.SnoozeAsync(view.Notification, user, days, today, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.Notification, AuditActions.SnoozeNotification,
            updated.Id.ToString(CultureInfo.InvariantCulture), Target(view),
            AuditDetails.Changes(new AuditChange("Amânare", "fără", $"{days} zile, până la {StockMovementRules.DisplayDate(today.AddDays(days))}"),
                new AuditChange("Stare", StateLabel(view.Notification, today), "preluată de " + user)),
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
