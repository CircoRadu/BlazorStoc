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

public static class ExpirySourceKeys
{
    public const string VehicleItp = "vehicul.itp";
    public const string VehicleInsurance = "vehicul.asigurare";
    public const string VehicleRovinieta = "vehicul.rovinieta";
    public const string MaintenanceDue = "mentenanta.scadenta";
    public const string ContractExpiry = "contract.expirare";
    public const string RiskAnalysisExpiry = "analiza-risc.expirare";
    public const string AwaitedInvoice = "intrare.factura-asteptata";
    public const string OverStock = "stoc.iesiri-peste-stoc";
    public const string MinStock = "stoc.sub-minim";
    public const string StaleReservation = "stoc.rezervare-fara-miscare";
    public const string ProjectDeficit = "proiect.deficit-termen";
    public const string BackupMissing = "sistem.backup-lipsa";
    public const string NasCopyMissing = "sistem.copie-nas-lipsa";
    public const string ClockSkew = "sistem.ceas-server-decalat";
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
