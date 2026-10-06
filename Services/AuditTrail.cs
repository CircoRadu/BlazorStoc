using System.Text;
using System.Text.Json;

namespace BlazorStoc.Services;

public static class AuditEntities
{
    public const string Product = "Produs";
    public const string User = "Utilizator";
    public const string Beneficiary = "Beneficiar";
    public const string Category = "Categorie";
    public const string Subcategory = "Subcategorie";
    public const string Project = "Proiect";
    public const string ProjectObservation = "Observatie";
    public const string ProjectObservationFile = "FisierObservatie";
    public const string StockMovement = "MiscareStoc";
    public const string Vehicle = "Vehicul";
    public const string Inventory = "Inventar";
    public const string DatabaseBackup = "CopieSiguranta";
    public const string NotificationTemplate = "SablonNotificare";
    public const string Notification = "Notificare";
    public const string NotificationSettings = "SetariNotificari";
    // Only the deletions of a work point and of a photo are recorded under these types (they are archived); creations and
    // edits are recorded under the beneficiary the work point belongs to.
    public const string WorkPoint = "PunctLucru";
    public const string ServicePhoto = "FotografiePunctLucru";
    // Only the deletion of a maintenance contract is recorded under this type (it is archived); the other operations on contracts
    // are recorded under the beneficiary the contract belongs to.
    public const string ServiceContract = "ContractMentenanta";
    // Only the deletion of an intervention (either kind) is recorded under this type (it is archived); the other operations on
    // interventions are recorded under the beneficiary they belong to.
    public const string ServiceIntervention = "InterventieMentenanta";
    // The journal itself: only its export is recorded under this type.
    public const string Journal = "Jurnal";
    // Settings → Hartă: the tile provider (one object, id 1) and the pin types of the overlay.
    public const string MapEngine = "FurnizorHarta";
    public const string MapPinType = "TipPinHarta";
    // Settings -> Facturi: the saved invoice templates (layouts used to read supplier invoices).
    public const string InvoiceTemplate = "SablonFactura";
    // Administrare -> Furnizori (the register of suppliers) and the invoices of suppliers that stock entries are taken from.
    public const string Supplier = "Furnizor";
    public const string SupplierInvoice = "FacturaFurnizor";
}

public static class AuditActions
{
    public const string Create = "Adăugare";
    public const string Edit = "Editare";
    public const string Delete = "Ștergere";
    public const string Login = "Conectare";
    public const string Logout = "Deconectare";
    public const string Unlock = "Deblocare";
    public const string Generate = "Generare";
    public const string Restore = "Restaurare";
    // The complete journal exported to a file (the filters used and the number of events are in the details).
    public const string ExportJournal = "Export jurnal";
    // Map settings: each operation names exactly what happened.
    public const string EditMapEngine = "Modificare furnizor hartă";
    public const string ResetMapEngine = "Resetare furnizor hartă";
    public const string CreateMapPinType = "Adăugare tip pin hartă";
    public const string EditMapPinType = "Modificare tip pin hartă";
    public const string DeleteMapPinType = "Ștergere tip pin hartă";
    // Specific operations of the vehicle module: the action names the exact kind of change in the journal.
    public const string ExpiryItp = "Modificare expirare ITP";
    public const string ExpiryInsurance = "Modificare expirare asigurare";
    public const string ExpiryRovinieta = "Modificare expirare rovinietă";
    public const string MoveEquipment = "Mutare echipament";
    public const string ReturnEquipment = "Returnare echipament în depozit";
    // Expiry notifications: each operation names exactly what happened.
    public const string CreateNotificationTemplate = "Adăugare șablon notificare";
    public const string EditNotificationTemplate = "Modificare șablon notificare";
    public const string DeleteNotificationTemplate = "Ștergere șablon notificare";
    // Written by the system itself when the engine creates a notification (actor "sistem"), so the creation can be followed.
    public const string NotificationCreated = "Notificare creată";
    public const string AcknowledgeNotification = "Preluare notificare";
    public const string SnoozeNotification = "Amânare notificare";
    // A user marks a notification resolved; the system closes one whose cause is gone (date changed, object removed) and
    // reopens an automatically closed one whose date came back.
    public const string ResolveNotification = "Rezolvare notificare";
    public const string AutoResolveNotification = "Rezolvare automată notificare";
    public const string ReopenNotification = "Redeschidere automată notificare";
    // The clean-up of old resolved notifications: the change of its setting (switch or period) and each run that removed rows
    // (by the administrator when the setting is switched on or the period shortened, by the system on the daily run).
    public const string EditNotificationSettings = "Modificare setări curățare notificări";
    public const string PurgeResolvedNotifications = "Curățare notificări rezolvate";
    // Work points: each kind of change is named exactly (an edit that touches only the description or only the coordinates has
    // its own name; a mixed edit is "Modificare punct de lucru"). Deleting a photo or a work point is the archived "Ștergere".
    public const string CreateWorkPoint = "Adăugare punct de lucru";
    public const string EditWorkPoint = "Modificare punct de lucru";
    public const string EditWorkPointDescription = "Modificare descriere punct de lucru";
    public const string EditWorkPointCoordinates = "Modificare coordonate punct de lucru";
    public const string AddWorkPointPhoto = "Adăugare fotografie punct de lucru";
    // Maintenance contracts and their coverage: each kind of change is named exactly. Deleting a contract is the archived "Ștergere".
    public const string CreateServiceContract = "Adăugare contract mentenanță";
    public const string EditServiceContract = "Modificare contract mentenanță";
    public const string EditServiceContractExpiry = "Modificare expirare contract mentenanță";
    public const string ActivateServiceContract = "Activare contract mentenanță";
    public const string DeactivateServiceContract = "Dezactivare contract mentenanță";
    public const string AddContractPoint = "Adăugare punct în contract";
    public const string RemoveContractPoint = "Scoatere punct din contract";
    public const string EditMaintenanceCycle = "Modificare ciclicitate mentenanță";
    public const string RescheduleMaintenance = "Reprogramare intervenție mentenanță";
    public const string MoveContractPoint = "Mutare punct de lucru în alt contract";
    // Interventions (register): each kind of change is named exactly. Deleting one is the archived "Ștergere".
    public const string RecordMaintenance = "Înregistrare intervenție mentenanță";
    public const string EditMaintenanceIntervention = "Modificare intervenție mentenanță";
    public const string RecordOnDemand = "Înregistrare intervenție la cerere";
    public const string EditOnDemandIntervention = "Modificare intervenție la cerere";
    public const string AddInterventionPhoto = "Adăugare fotografie intervenție";
    // Invoice templates: a new version of the layout and a change of only the name/supplier are different operations. A deleted
    // template has no page to link to (see IsDeletion).
    public const string CreateInvoiceTemplate = "Adăugare șablon factură";
    public const string EditInvoiceTemplate = "Modificare șablon factură";
    public const string EditInvoiceTemplateDetails = "Modificare date șablon factură";
    public const string DeleteInvoiceTemplate = "Ștergere șablon factură";
    public const string ActivateInvoiceTemplate = "Activare șablon factură";
    public const string DeactivateInvoiceTemplate = "Dezactivare șablon factură";
    // Suppliers: each kind of change is named exactly (data read again from ANAF is not an ordinary edit). Deleting a supplier is the archived "Ștergere".
    public const string CreateSupplier = "Adăugare furnizor";
    public const string EditSupplier = "Modificare furnizor";
    public const string RecheckSupplier = "Reverificare furnizor ANAF";
    public const string RecordSupplierInvoice = "Înregistrare factură furnizor";

    // Operations that remove their object: earlier events about it stop linking to its page.
    public static bool IsDeletion(string? action) => action is Delete or DeleteInvoiceTemplate;

    // Events whose object still has a live page (a plain edit or one of the specific edit operations).
    public static bool IsCreateOrEdit(string? action) =>
        action is Create or Edit or ExpiryItp or ExpiryInsurance or ExpiryRovinieta or MoveEquipment or ReturnEquipment
            or CreateNotificationTemplate or EditNotificationTemplate or NotificationCreated or AcknowledgeNotification or SnoozeNotification
            or ResolveNotification or AutoResolveNotification or ReopenNotification or EditNotificationSettings
            or CreateWorkPoint or EditWorkPoint or EditWorkPointDescription or EditWorkPointCoordinates or AddWorkPointPhoto
            or CreateServiceContract or EditServiceContract or EditServiceContractExpiry or ActivateServiceContract or DeactivateServiceContract
            or AddContractPoint or RemoveContractPoint or EditMaintenanceCycle or RescheduleMaintenance or MoveContractPoint
            or RecordMaintenance or EditMaintenanceIntervention or RecordOnDemand or EditOnDemandIntervention or AddInterventionPhoto
            or EditMapEngine or ResetMapEngine or CreateMapPinType or EditMapPinType
            or CreateInvoiceTemplate or EditInvoiceTemplate or EditInvoiceTemplateDetails or ActivateInvoiceTemplate or DeactivateInvoiceTemplate
            or CreateSupplier or EditSupplier or RecheckSupplier or RecordSupplierInvoice;

    public static string Normalize(string? action) =>
        string.Equals(action, "Modificare", StringComparison.OrdinalIgnoreCase) ? Edit : action ?? string.Empty;
}

public sealed record AuditChange(string Field, string Before, string After);

public static class AuditDetails
{
    public static string Changes(params AuditChange[] changes) => string.Join("; ", changes
        .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))
        .Select(change => $"{change.Field}: {change.Before} → {change.After}"));

    public static string Identification(params (string Field, string Value)[] values) =>
        string.Join("; ", values.Select(value => $"{value.Field}: {value.Value}"));
}

public sealed record AuditEvent(Guid Id, DateTime TimestampUtc, string ActorUsername, string ActorRole,
    string EntityType, string Action, string Target, string Details, string Motif = "", string EntityId = "",
    Guid? ArchiveOperationId = null);

public sealed record AuditWrite(string ActorUsername, string ActorRole, string EntityType, string Action,
    string Target, string Details, string Motif = "", string EntityId = "", Guid? ArchiveOperationId = null);

public static class AuditNavigation
{
    // Older product events stored "#<id> · <code>"; the internal identifier is hidden when they are displayed.
    public static string DisplayTarget(AuditEvent entry)
    {
        if (entry.EntityType != AuditEntities.Product || !entry.Target.StartsWith('#')) return entry.Target;
        var separator = entry.Target.IndexOf(" · ", StringComparison.Ordinal);
        return separator > 1 && entry.Target[1..separator].All(char.IsAsciiDigit)
            ? entry.Target[(separator + 3)..]
            : entry.Target;
    }

    // Registry of the read-only pages that the "Țintă" column links to, keyed by entity type. A new entity with its
    // own page is added here and nowhere else; entity types without an entry are shown as plain text. Every route
    // is built from the entity type and the stable identifier only, never from the display text of the target.
    private static readonly IReadOnlyDictionary<string, Func<int, string>> Routes = new Dictionary<string, Func<int, string>>
    {
        [AuditEntities.Product] = ProductNavigation.ProductUrl,
        [AuditEntities.Beneficiary] = BeneficiaryProjectListState.BeneficiaryUrl,
        [AuditEntities.User] = UserNavigation.UserUrl,
        [AuditEntities.Project] = ProjectNavigation.ProjectUrl,
        // The event stores the observation id; /observatii/{id} resolves it to the page under its project.
        [AuditEntities.ProjectObservation] = ProjectNavigation.ObservationUrl,
        // The event stores the movement id; /miscari/{id} resolves it to the product's movements page.
        [AuditEntities.StockMovement] = StockMovementNavigation.MovementUrl,
        // The vehicle page (its equipment, edit and delete actions).
        [AuditEntities.Vehicle] = VehicleNavigation.PageUrl,
        // Templates and the clean-up setting live in the Notifications tab of Settings (the template row is highlighted);
        // notifications have their own page.
        [AuditEntities.NotificationTemplate] = SettingsNavigation.TemplateUrl,
        [AuditEntities.Notification] = _ => "/notificari",
        [AuditEntities.NotificationSettings] = _ => SettingsNavigation.NotificationSettingsUrl,
        // Map settings: the engine sub-tab, or the overlay sub-tab with the pin type highlighted.
        [AuditEntities.MapEngine] = _ => SettingsNavigation.MapEngineUrl,
        [AuditEntities.MapPinType] = SettingsNavigation.MapPinTypeUrl,
        // The saved templates sub-tab of Settings -> Facturi, the template highlighted.
        [AuditEntities.InvoiceTemplate] = SettingsNavigation.InvoiceTemplateUrl,
        // The supplier page (its data and invoices). An invoice event stores the invoice id, which has no page of its own: plain text.
        [AuditEntities.Supplier] = SupplierNavigation.SupplierUrl
    };

    public static string? TargetUrl(AuditEvent entry, IReadOnlyDictionary<string, DateTime>? removals = null)
    {
        if (!AuditActions.IsCreateOrEdit(entry.Action) ||
            !int.TryParse(entry.EntityId, out var entityId) || entityId <= 0 ||
            !Routes.TryGetValue(entry.EntityType, out var route))
            return null;
        // A deletion recorded at or after this event means the live page no longer exists (the object is archived).
        if (removals is not null && removals.TryGetValue(ObjectKey(entry.EntityType, entry.EntityId), out var removedAt) &&
            removedAt >= entry.TimestampUtc)
            return null;
        return route(entityId);
    }

    // Latest recorded deletion per object. Events after the deletion (for example an identifier reused later) keep
    // their links, older ones are shown as text.
    public static IReadOnlyDictionary<string, DateTime> RemovalTimes(IEnumerable<AuditEvent> events)
    {
        var removals = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var entry in events)
        {
            if (!AuditActions.IsDeletion(entry.Action) || entry.EntityId.Length == 0) continue;
            var key = ObjectKey(entry.EntityType, entry.EntityId);
            if (!removals.TryGetValue(key, out var known) || entry.TimestampUtc > known) removals[key] = entry.TimestampUtc;
        }
        return removals;
    }

    public static string ObjectKey(string entityType, string entityId) => $"{entityType}:{entityId}";
}

public static class ProductNavigation
{
    // Stable read-only page of a product (its data and stock movements); editing needs the explicit "Editează" action.
    public static string ProductUrl(int productId) => $"/produse/{productId}";
}

public static class UserNavigation
{
    public static string UserUrl(int userId) => $"/utilizatori/{userId}";
}

public static class SettingsNavigation
{
    public const string TabParameter = "tab";
    public const string SubtabParameter = "subtab";
    public const string TemplateParameter = "sablon";
    public const string NotificationsTab = "notificari";
    public const string TemplatesSubtab = "templates";
    public const string SettingsSubtab = "settings";

    public static string TemplateUrl(int templateId) =>
        $"/setari?{TabParameter}={NotificationsTab}&{SubtabParameter}={TemplatesSubtab}&{TemplateParameter}={templateId}";

    public const string MapTab = "harta";
    public const string MapEngineSubtab = "motor";
    public const string MapOverlaySubtab = "overlay";
    public const string PinParameter = "pin";

    public static string MapEngineUrl => $"/setari?{TabParameter}={MapTab}&{SubtabParameter}={MapEngineSubtab}";
    public static string MapPinTypeUrl(int pinTypeId) => $"/setari?{TabParameter}={MapTab}&{SubtabParameter}={MapOverlaySubtab}&{PinParameter}={pinTypeId}";

    public const string InvoicesTab = "facturi";
    public const string InvoiceAnalysisSubtab = "analiza";
    public const string InvoiceEditSubtab = "editare";
    public const string InvoiceTemplatesSubtab = "sabloane";
    public const string InvoiceTemplateParameter = "sablonfactura";
    public const string InvoiceEditParameter = "editeaza";

    public static string InvoiceTemplateUrl(int templateId) =>
        $"/setari?{TabParameter}={InvoicesTab}&{SubtabParameter}={InvoiceTemplatesSubtab}&{InvoiceTemplateParameter}={templateId}";

    public static string NotificationSettingsUrl => $"/setari?{TabParameter}={NotificationsTab}&{SubtabParameter}={SettingsSubtab}";
}

// Filters of the journal, applied on the server. Dates are UTC instants: From included, To excluded (the caller converts the local
// days the user chose). Text searches the operator, target, details and reason, without regard to letter case.
public sealed record AuditQuery(string Text = "", string Entity = "", string Action = "", string Actor = "",
    DateTime? FromUtc = null, DateTime? ToUtc = null)
{
    public bool IsEmpty => Text.Trim().Length == 0 && Entity.Length == 0 && Action.Length == 0 && Actor.Length == 0 && FromUtc is null && ToUtc is null;
}

// One page of results. Total is the number of events that match (inside the window, when there is one); JournalTotal is the size of
// the whole journal, so a page limited to the latest events can say that there are more.
public sealed record AuditPage(IReadOnlyList<AuditEvent> Events, int Total, int Page, int PageSize, int JournalTotal);

public sealed record AuditSummary(int Total, int Today, int Actors, int Products);

public interface IAuditTrail
{
    Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default);

    // Filtering, search and paging on the server. window: only the latest N events of the journal are considered (null = all of it).
    // pageSize 0 returns every match of the window (never more than AuditQueryRules.MaxRows).
    Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default);
    Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default);

    // The latest recorded deletion of each object that the given events are about (the events after it keep their links, see
    // AuditNavigation.TargetUrl); the deletions may be older than the page or outside the window.
    Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default);
}

public static class AuditQueryRules
{
    // The default view of the journal page: the latest events only; the complete journal has its own page.
    public const int RecentWindow = 500;
    // The most rows one query (or one export) returns.
    public const int MaxRows = 50_000;
    public static readonly IReadOnlyList<int> CompletePageSizes = [25, 50, 100];
    public const int DefaultCompletePageSize = 50;

    // The events that answer the question "does it match?" in memory (the file journal and the tests). The database applies the same
    // rules in SQL (MariaAuditTrail): a legacy "Modificare" counts as an edit, the operator is compared without letter case.
    public static bool Matches(AuditEvent entry, AuditQuery query)
    {
        var text = query.Text.Trim();
        return (query.Entity.Length == 0 || entry.EntityType == query.Entity) &&
               (query.Action.Length == 0 || AuditActions.Normalize(entry.Action) == AuditActions.Normalize(query.Action)) &&
               (query.Actor.Length == 0 || string.Equals(entry.ActorUsername, query.Actor, StringComparison.OrdinalIgnoreCase)) &&
               (query.FromUtc is null || entry.TimestampUtc >= query.FromUtc) &&
               (query.ToUtc is null || entry.TimestampUtc < query.ToUtc) &&
               (text.Length == 0 || entry.ActorUsername.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                entry.Target.Contains(text, StringComparison.OrdinalIgnoreCase) || entry.Details.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                entry.Motif.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    public static AuditPage Page(IEnumerable<AuditEvent> all, AuditQuery query, int page, int pageSize, int? window)
    {
        var ordered = all.OrderByDescending(entry => entry.TimestampUtc).ToList();
        var inWindow = window is { } limit ? ordered.Take(limit).ToList() : ordered;
        var matching = inWindow.Where(entry => Matches(entry, query)).ToList();
        var size = pageSize <= 0 ? Math.Min(MaxRows, Math.Max(1, matching.Count)) : pageSize;
        var current = Math.Clamp(page, 1, Math.Max(1, (matching.Count + size - 1) / size));
        return new(matching.Skip((current - 1) * size).Take(size).ToList(), matching.Count, current, pageSize <= 0 ? 0 : size, ordered.Count);
    }

    public static AuditSummary Summary(IEnumerable<AuditEvent> all, DateTime todayStartUtc)
    {
        var list = all as IReadOnlyCollection<AuditEvent> ?? all.ToList();
        return new(list.Count, list.Count(entry => entry.TimestampUtc >= todayStartUtc),
            list.Select(entry => entry.ActorUsername).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            list.Count(entry => entry.EntityType == AuditEntities.Product));
    }

    public static IReadOnlyDictionary<string, DateTime> Removals(IEnumerable<AuditEvent> all, IEnumerable<AuditEvent> shown)
    {
        var wanted = shown.Select(entry => AuditNavigation.ObjectKey(entry.EntityType, entry.EntityId)).ToHashSet(StringComparer.Ordinal);
        return AuditNavigation.RemovalTimes(all).Where(item => wanted.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value);
    }

    // A short description of the filters, for the journal of an export ("operator: ana; tip: Produs; de la 01.09.2026 ...").
    public static string Describe(AuditQuery query, Func<DateTime, string> date)
    {
        var parts = new List<string>();
        if (query.Text.Trim().Length > 0) parts.Add($"căutare: „{query.Text.Trim()}”");
        if (query.Entity.Length > 0) parts.Add($"tip: {query.Entity}");
        if (query.Action.Length > 0) parts.Add($"operație: {query.Action}");
        if (query.Actor.Length > 0) parts.Add($"operator: {query.Actor}");
        if (query.FromUtc is { } from) parts.Add($"de la {date(from)}");
        if (query.ToUtc is { } to) parts.Add($"înainte de {date(to)}");
        return parts.Count == 0 ? "fără filtre" : string.Join("; ", parts);
    }

    // CSV (semicolon-separated, as Excel in Romanian expects; quoted values; a value that a spreadsheet could read as a formula is
    // prefixed with an apostrophe). Times are UTC, written dd.MM.yyyy HH:mm:ss.
    public static string Csv(IEnumerable<AuditEvent> events)
    {
        static string Cell(string value)
        {
            var text = value.Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 0 && "=+-@\t".Contains(text[0])) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var builder = new StringBuilder();
        builder.Append("Data și ora (UTC);Operator;Rol;Tip;Operație;Țintă;Detalii;Motiv\r\n");
        foreach (var entry in events)
            builder.Append(string.Join(';', Cell(entry.TimestampUtc.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)),
                Cell(entry.ActorUsername), Cell(entry.ActorRole), Cell(entry.EntityType), Cell(entry.Action),
                Cell(AuditNavigation.DisplayTarget(entry)), Cell(entry.Details), Cell(entry.Motif))).Append("\r\n");
        return builder.ToString();
    }
}

public sealed class FileAuditTrail(IWebHostEnvironment environment, IConfiguration configuration, ILogger<FileAuditTrail> logger) : IAuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path = Path.GetFullPath(configuration["App:AuditPath"] ??
        Path.Combine(environment.ContentRootPath, "data", "audit-events.jsonl"));

    public async Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, entry.ActorUsername, entry.ActorRole,
            entry.EntityType, AuditActions.Normalize(entry.Action), entry.Target, entry.Details,
            entry.Motif ?? string.Empty, entry.EntityId ?? string.Empty, entry.ArchiveOperationId);
        var line = JsonSerializer.Serialize(auditEvent, JsonOptions) + Environment.NewLine;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError("Audit event could not be persisted ({ErrorType}).", exception.GetType().Name);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Array.Empty<AuditEvent>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            return lines.Reverse().Select(Parse).Where(entry => entry is not null).Cast<AuditEvent>().Take(2000).ToArray();
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<AuditEvent>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Array.Empty<AuditEvent>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            return lines.Select(Parse).Where(entry => entry is not null).Cast<AuditEvent>().ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) =>
        AuditQueryRules.Page(await ReadAllAsync(cancellationToken).ConfigureAwait(false), query, page, pageSize, window);

    public async Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) =>
        AuditQueryRules.Summary(await ReadAllAsync(cancellationToken).ConfigureAwait(false), todayStartUtc);

    public async Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) =>
        AuditQueryRules.Removals(await ReadAllAsync(cancellationToken).ConfigureAwait(false), events);

    private static AuditEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<AuditEvent>(line, JsonOptions);
            return entry is null ? null : entry with
            {
                TimestampUtc = ToUtc(entry.TimestampUtc),
                Action = AuditActions.Normalize(entry.Action),
                Motif = entry.Motif ?? string.Empty,
                EntityId = entry.EntityId ?? string.Empty
            };
        }
        catch (JsonException) { return null; }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public static class AuditRecorder
{
    public static Task RecordCreateAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, string details, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Create, entityId, target, details, string.Empty, cancellationToken);

    public static Task RecordEditAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, IEnumerable<AuditChange> changes, string motif, CancellationToken cancellationToken,
        string? action = null) =>
        RecordEntityAsync(trail, access, entityType, action ?? AuditActions.Edit, entityId, target,
            AuditDetails.Changes(changes.ToArray()), motif, cancellationToken);

    public static Task RecordActionAsync(IAuditTrail? trail, IAccessControl? access, string entityType, string action,
        string entityId, string target, string details, string motif, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, action, entityId, target, details, motif, cancellationToken);

    public static Task RecordDeleteAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string entityId, string target, string details, string motif, CancellationToken cancellationToken,
        Guid? archiveOperationId = null) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Delete, entityId, target, details, motif,
            cancellationToken, archiveOperationId);

    public static Task RecordDeleteAsync(IAuditTrail? trail, ArchiveOperation operation,
        CancellationToken cancellationToken) => trail is null
        ? Task.CompletedTask
        : trail.RecordAsync(new(operation.ActorUsername, operation.ActorRole,
            operation.Request.Snapshot.EntityType, AuditActions.Delete, operation.Request.Target,
            operation.Request.Details, operation.Request.Motif, operation.Request.Snapshot.OriginalId,
            operation.Id), cancellationToken);

    // An administrator released another user's product edit lock; the reason is mandatory (Task 9).
    public static Task RecordUnlockAsync(IAuditTrail? trail, IAccessControl? access, string entityId, string target,
        string details, string motif, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, AuditEntities.Product, AuditActions.Unlock, entityId, target, details, motif, cancellationToken);

    // A read-only report generated on demand (the inventory PDF): no object identifier, so no link in "Țintă".
    public static Task RecordGenerateAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string target, string details, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Generate, string.Empty, target, details, string.Empty, cancellationToken);

    // Subtask 3.4 (Task 3): the restoration itself (distinct from the automatic pre-restore snapshot, which is
    // logged separately through RecordGenerateAsync like every other backup).
    public static Task RecordRestoreAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string target, string details, CancellationToken cancellationToken) =>
        RecordEntityAsync(trail, access, entityType, AuditActions.Restore, string.Empty, target, details, string.Empty, cancellationToken);

    public static Task RecordSessionAsync(IAuditTrail? trail, string actorUsername, string actorRole,
        bool connected, CancellationToken cancellationToken) => trail is null
        ? Task.CompletedTask
        : trail.RecordAsync(new(actorUsername, actorRole, AuditEntities.User,
            connected ? AuditActions.Login : AuditActions.Logout, actorUsername,
            connected ? "Sesiune autentificată" : "Sesiune încheiată", string.Empty, string.Empty), cancellationToken);

    private static async Task RecordEntityAsync(IAuditTrail? trail, IAccessControl? access, string entityType,
        string action, string entityId, string target, string details, string motif, CancellationToken cancellationToken,
        Guid? archiveOperationId = null)
    {
        if (trail is null) return;
        var actor = access is null ? "sistem" : await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "necunoscut";
        var role = access is not null && await access.IsAdministratorAsync(cancellationToken).ConfigureAwait(false)
            ? AccessRoles.Administrator : AccessRoles.LimitedUser;
        await trail.RecordAsync(new(actor, role, entityType, action, target, details, motif, entityId, archiveOperationId), cancellationToken).ConfigureAwait(false);
    }
}
