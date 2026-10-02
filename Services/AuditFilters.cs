using System.Globalization;

namespace BlazorStoc.Services;

// The text and the selects of the journal filters, shared by the journal (latest events) and the complete journal pages.
public sealed class AuditFilterValues
{
    public string Query { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";

    public bool Any => Query.Trim().Length > 0 || Entity.Length > 0 || Action.Length > 0 || Actor.Length > 0;
    public void Clear() => Query = Entity = Action = Actor = "";
}

public static class AuditFilterOptions
{
    public static readonly IReadOnlyList<(string Value, string Label)> Entities =
    [
        (AuditEntities.Product, "Produse"), (AuditEntities.Beneficiary, "Beneficiari"), (AuditEntities.Vehicle, "Vehicule"),
        (AuditEntities.User, "Utilizatori"), (AuditEntities.Inventory, "Inventar"), (AuditEntities.NotificationTemplate, "Șabloane notificări"),
        (AuditEntities.Notification, "Notificări"), (AuditEntities.NotificationSettings, "Setări notificări"),
        (AuditEntities.WorkPoint, "Puncte de lucru (ștergeri)"), (AuditEntities.ServicePhoto, "Fotografii puncte de lucru (ștergeri)"),
        (AuditEntities.ServiceContract, "Contracte mentenanță (ștergeri)"), (AuditEntities.ServiceIntervention, "Intervenții mentenanță (ștergeri)"),
        (AuditEntities.Journal, "Jurnalul (exporturi)"), (AuditEntities.MapEngine, "Furnizor hartă"), (AuditEntities.MapPinType, "Tipuri de pinuri hartă"),
        (AuditEntities.InvoiceTemplate, "Șabloane facturi")
    ];

    public static readonly IReadOnlyList<(string Value, string Label)> Actions =
    [
        (AuditActions.Create, "Adăugare"), (AuditActions.Edit, "Editare"), (AuditActions.Delete, "Ștergere"), (AuditActions.Login, "Conectare"),
        (AuditActions.Logout, "Deconectare"), (AuditActions.Unlock, "Deblocare"), (AuditActions.Generate, "Generare"),
        (AuditActions.ExportJournal, AuditActions.ExportJournal),
        (AuditActions.EditMapEngine, AuditActions.EditMapEngine), (AuditActions.ResetMapEngine, AuditActions.ResetMapEngine),
        (AuditActions.CreateMapPinType, AuditActions.CreateMapPinType), (AuditActions.EditMapPinType, AuditActions.EditMapPinType), (AuditActions.DeleteMapPinType, AuditActions.DeleteMapPinType),
        (AuditActions.ExpiryItp, AuditActions.ExpiryItp), (AuditActions.ExpiryInsurance, AuditActions.ExpiryInsurance), (AuditActions.ExpiryRovinieta, AuditActions.ExpiryRovinieta),
        (AuditActions.MoveEquipment, AuditActions.MoveEquipment), (AuditActions.ReturnEquipment, AuditActions.ReturnEquipment),
        (AuditActions.CreateNotificationTemplate, AuditActions.CreateNotificationTemplate), (AuditActions.EditNotificationTemplate, AuditActions.EditNotificationTemplate),
        (AuditActions.DeleteNotificationTemplate, AuditActions.DeleteNotificationTemplate), (AuditActions.NotificationCreated, AuditActions.NotificationCreated),
        (AuditActions.AcknowledgeNotification, AuditActions.AcknowledgeNotification), (AuditActions.SnoozeNotification, AuditActions.SnoozeNotification),
        (AuditActions.ResolveNotification, AuditActions.ResolveNotification), (AuditActions.AutoResolveNotification, AuditActions.AutoResolveNotification),
        (AuditActions.ReopenNotification, AuditActions.ReopenNotification), (AuditActions.EditNotificationSettings, AuditActions.EditNotificationSettings),
        (AuditActions.PurgeResolvedNotifications, AuditActions.PurgeResolvedNotifications),
        (AuditActions.CreateWorkPoint, AuditActions.CreateWorkPoint), (AuditActions.EditWorkPoint, AuditActions.EditWorkPoint),
        (AuditActions.EditWorkPointDescription, AuditActions.EditWorkPointDescription), (AuditActions.EditWorkPointCoordinates, AuditActions.EditWorkPointCoordinates),
        (AuditActions.AddWorkPointPhoto, AuditActions.AddWorkPointPhoto),
        (AuditActions.CreateServiceContract, AuditActions.CreateServiceContract), (AuditActions.EditServiceContract, AuditActions.EditServiceContract),
        (AuditActions.EditServiceContractExpiry, AuditActions.EditServiceContractExpiry), (AuditActions.ActivateServiceContract, AuditActions.ActivateServiceContract),
        (AuditActions.DeactivateServiceContract, AuditActions.DeactivateServiceContract), (AuditActions.AddContractPoint, AuditActions.AddContractPoint),
        (AuditActions.RemoveContractPoint, AuditActions.RemoveContractPoint), (AuditActions.EditMaintenanceCycle, AuditActions.EditMaintenanceCycle),
        (AuditActions.RescheduleMaintenance, AuditActions.RescheduleMaintenance), (AuditActions.MoveContractPoint, AuditActions.MoveContractPoint),
        (AuditActions.RecordMaintenance, AuditActions.RecordMaintenance), (AuditActions.EditMaintenanceIntervention, AuditActions.EditMaintenanceIntervention),
        (AuditActions.RecordOnDemand, AuditActions.RecordOnDemand), (AuditActions.EditOnDemandIntervention, AuditActions.EditOnDemandIntervention),
        (AuditActions.AddInterventionPhoto, AuditActions.AddInterventionPhoto),
        (AuditActions.CreateInvoiceTemplate, AuditActions.CreateInvoiceTemplate), (AuditActions.EditInvoiceTemplate, AuditActions.EditInvoiceTemplate),
        (AuditActions.EditInvoiceTemplateDetails, AuditActions.EditInvoiceTemplateDetails), (AuditActions.DeleteInvoiceTemplate, AuditActions.DeleteInvoiceTemplate),
        (AuditActions.ActivateInvoiceTemplate, AuditActions.ActivateInvoiceTemplate), (AuditActions.DeactivateInvoiceTemplate, AuditActions.DeactivateInvoiceTemplate)
    ];
}

// The range of local days a user chose, as UTC instants (computed in the browser, whose time zone is the one the journal shows).
public sealed record AuditUtcRange(string? FromUtc, string? ToUtc)
{
    public DateTime? From => Parse(FromUtc);
    public DateTime? To => Parse(ToUtc);

    private static DateTime? Parse(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : null;
}

// Filters, page size and page of the complete journal (/jurnal/complet). Like AuditListState they live in the address. Without a date
// range in the address the last month is shown; "tot=1" asks for the whole journal.
public sealed record AuditCompleteState(string Query, string Entity, string Action, string Actor, string? From, string? To, bool All, int PageSize, int Page)
{
    public const string Path = "/jurnal/complet";

    public static AuditCompleteState Default(DateOnly today) =>
        new("", "", "", "", today.AddMonths(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, false, AuditQueryRules.DefaultCompletePageSize, 1);

    public static AuditCompleteState Parse(DateOnly today, string? query, string? entity, string? action, string? actor, string? from, string? to,
        string? all, int? pageSize, int? page)
    {
        var fromKey = AuditListState.ValidDateKey(from);
        var toKey = AuditListState.ValidDateKey(to);
        var whole = all == "1";
        if (fromKey is not null && toKey is not null && string.CompareOrdinal(fromKey, toKey) > 0) (fromKey, toKey) = (toKey, fromKey);
        if (!whole && fromKey is null && toKey is null) fromKey = Default(today).From;
        if (whole) fromKey = toKey = null;
        return new((query ?? "").Trim(), (entity ?? "").Trim(), (action ?? "").Trim(), (actor ?? "").Trim(), fromKey, toKey, whole,
            pageSize is { } size && AuditQueryRules.CompletePageSizes.Contains(size) ? size : AuditQueryRules.DefaultCompletePageSize, Math.Max(1, page ?? 1));
    }

    public string Url()
    {
        var parts = new List<string>();
        void Add(string name, string? value) { if (!string.IsNullOrEmpty(value)) parts.Add($"{name}={Uri.EscapeDataString(value)}"); }
        Add("q", Query);
        Add("tip", Entity);
        Add("operatie", Action);
        Add("operator", Actor);
        Add("de-la", From);
        Add("pana-la", To);
        if (All) parts.Add("tot=1");
        if (PageSize != AuditQueryRules.DefaultCompletePageSize) parts.Add($"pe-pagina={PageSize}");
        if (Page > 1) parts.Add($"pagina={Page}");
        return Path + (parts.Count == 0 ? "" : "?" + string.Join('&', parts));
    }
}
