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
    public const string ActivityImport = "PreluareActivityApp";
    public const string JournalSettings = "SetariJurnal";
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
    // Only the deletion of a risk analysis is recorded under this type (it is archived); the other operations on analyses
    // are recorded under the beneficiary the analysis belongs to.
    public const string RiskAnalysis = "AnalizaRisc";
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
    // Nomenclator -> Tipuri de sisteme.
    public const string SystemType = "TipSistem";
    // Oferte -> Sabloane: the templates of offer sheets (.xlsx).
    public const string OfferTemplate = "SablonOferta";
    // Setari -> Baza de date: the connection of the application to MariaDB and the port it listens on (administrator configured in the private file only).
    public const string Database = "BazaDeDate";
    // Setari -> Tipuri de utilizatori: the user types (named sets of permissions) the administrator defines.
    public const string UserType = "TipUtilizator";
    // Modules (Modules/) and the application itself: written by the system (a module taken out or back, an unexpected stop of the application).
    public const string Module = "Modul";
    public const string Application = "Aplicatie";
}

public static class AuditActions
{
    public const string Create = "Adăugare";
    public const string Edit = "Editare";
    public const string Delete = "Ștergere";
    public const string Login = "Conectare";
    public const string Logout = "Deconectare";
    // A sign-in that was refused (wrong password, unknown or inactive account): the user name typed and the address are in the details, never the password.
    public const string LoginFailed = "Conectare eșuată";
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
    // Risk analyses of work points: each kind of change is named exactly. Deleting one is the archived "Ștergere".
    public const string CreateRiskAnalysis = "Adăugare analiză de risc";
    public const string EditRiskAnalysis = "Modificare analiză de risc";
    public const string EditRiskAnalysisValidity = "Modificare valabilitate analiză de risc";
    public const string RenewRiskAnalysis = "Reînnoire analiză de risc";
    public const string UndoRiskAnalysisRenewal = "Anulare reînnoire analiză de risc";
    public const string ActivateRiskAnalysis = "Activare analiză de risc";
    public const string DeactivateRiskAnalysis = "Dezactivare analiză de risc";
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
    // The supplier was renamed in the register: the name in its templates (and in their entry description) follows.
    public const string LinkInvoiceTemplateSupplier = "Legare șablon factură de furnizor";
    public const string RenameInvoiceTemplateSupplier = "Redenumire furnizor în șablon factură";
    // Suppliers: each kind of change is named exactly (data read again from ANAF is not an ordinary edit). Deleting a supplier is the archived "Ștergere".
    public const string CreateSupplier = "Adăugare furnizor";
    public const string EditSupplier = "Modificare furnizor";
    public const string RecheckSupplier = "Reverificare furnizor ANAF";
    public const string RecordSupplierInvoice = "Înregistrare factură furnizor";
    public const string EditSupplierInvoiceNumber = "Modificare număr factură";
    public const string EditSupplierInvoiceDate = "Modificare dată factură";
    public const string MoveSupplierInvoice = "Mutare factură la alt furnizor";
    public const string AddSupplierAlias = "Adăugare denumire alternativă furnizor";
    public const string RemoveSupplierAlias = "Ștergere denumire alternativă furnizor";
    public const string LinkSupplierProductCode = "Legare cod furnizor de produs";
    public const string DeleteCategory = "Ștergere categorie goală";
    public const string ReorderCategories = "Reordonare categorii";
    public const string DeleteSubcategory = "Ștergere subcategorie goală";
    // Required parameters of a subcategory (model + values in the product code): each kind of change has its own action.
    public const string AddSubcategoryParameter = "Adăugare parametru obligatoriu subcategorie";
    public const string EditSubcategoryParameter = "Modificare parametru obligatoriu subcategorie";
    public const string DeleteSubcategoryParameter = "Ștergere parametru obligatoriu subcategorie";
    public const string AddParameterValue = "Adăugare valoare parametru";
    public const string DeleteParameterValue = "Ștergere valoare parametru";
    public const string EditParameterValue = "Modificare valoare parametru";
    public const string RenameProductByParameter = "Redenumire produs (valoare parametru)";
    public const string ChangeSupplierProductCode = "Schimbare legătură cod furnizor";
    // Stock entries and invoices: a free entry (no invoice), its tie to an invoice and the entry the user confirmed against a warning.
    public const string RecordFreeEntry = "Intrare liberă înregistrată";
    public const string AttachEntryToInvoice = "Atașare intrare la factură";
    public const string DetachEntryFromInvoice = "Detașare intrare de la factură";
    public const string DuplicateEntryOnInvoice = "Intrare dublată pe factură (confirmat)";
    // Negative stock is an operating error: before the next entry the user states the real quantity (or zero) and a correction entry brings the stock there.
    public const string RegularizeNegativeStock = "Regularizare stoc negativ";
    public const string ExitOverStock = "Ieșire peste stocul din depozit";
    // Exits by destination (the "peste stoc" part, when there is one, is in the details) and the exit confirmed against the duplicate warning.
    public const string ExitToBeneficiary = "Ieșire spre beneficiar";
    public const string ExitToVehicle = "Ieșire spre vehicul";
    public const string GenericSale = "Vânzare generică";
    public const string StockCorrection = "Corecție stoc";
    public const string DuplicateExit = "Ieșire dublată (confirmat)";
    public const string ImportOffer = "Preluare ofertă";
    public const string ReviseOffer = "Revizie ofertă";
    public const string LinkOfferLine = "Legare linie ofertă de produs";
    public const string AddBeneficiaryAlias = "Adăugare denumire alternativă beneficiar";
    public const string CreateOfferTemplate = "Creare șablon ofertă";
    public const string EditOfferTemplate = "Modificare șablon ofertă";
    public const string ActivateOfferTemplate = "Activare șablon ofertă";
    public const string DeactivateOfferTemplate = "Dezactivare șablon ofertă";
    public const string DeleteOfferTemplate = "Ștergere șablon ofertă";
    public const string AddProjectComponent = "Adăugare componentă proiect";
    public const string ChangeProjectComponentState = "Modificare stare componentă proiect";
    public const string ArchiveProjectComponent = "Arhivare componentă proiect";
    public const string ReactivateProjectComponent = "Reactivare componentă proiect";
    public const string SetVehicleTarget = "Setare nivel țintă vehicul";
    public const string RemoveVehicleTarget = "Eliminare nivel țintă vehicul";
    public const string SetMinStock = "Setare stoc minim";
    public const string RemoveMinStock = "Eliminare stoc minim";
    public const string SetNasBackup = "Modificare configurare backup NAS";
    public const string SetBackupSettings = "Modificare setări backup";
    public const string RetentionDelete = "Ștergere automată pachet backup vechi";
    public const string FetchBackupFromNas = "Aducere pachet backup de pe NAS";
    public const string TestNasBackup = "Testare conexiune NAS";
    public const string SetActivityImport = "Modificare setări preluare ActivityApp";
    public const string SetJournalDisplay = "Modificare evenimente afișate în jurnal";
    public const string TestActivityImport = "Testare conexiune ActivityApp";
    public const string SetActivityImportTypes = "Modificare tipuri tichete preluate ActivityApp";
    public const string SetActivityFieldMap = "Modificare corespondență câmpuri ActivityApp";
    public const string DecideActivityMaintenance = "Validare mentenanță ActivityApp";
    public const string AddActivityKeyword = "Adăugare cuvânt cheie mentenanță ActivityApp";
    public const string EditActivityKeyword = "Modificare cuvânt cheie mentenanță ActivityApp";
    public const string DeleteActivityKeyword = "Ștergere cuvânt cheie mentenanță ActivityApp";
    public const string RetryActivityPartnerCheck = "Reverificare ANAF beneficiar ActivityApp";
    public const string AcceptActivityPartnerCheck = "Acceptare manuală beneficiar ActivityApp";
    public const string DiscardActivityPartner = "Discard partener ActivityApp";
    public const string DiscardActivityTicket = "Discard tichet ActivityApp";
    public const string ReactivateActivityTicket = "Reactivare tichet ActivityApp discardat";
    public const string ResetActivityWatermark = "Recitire completă tichete ActivityApp";
    public const string SimulateActivityImport = "Simulare import ActivityApp";
    public const string ImportActivityPartners = "Import beneficiari ActivityApp";
    public const string ImportActivityTickets = "Import tichete ActivityApp";
    public const string SyncActivityImport = "Sincronizare ActivityApp";
    public const string ResolveActivityImportIssue = "Rezolvare problemă import ActivityApp";
    public const string IncludeActivityPartner = "Includere manuală partener ActivityApp";
    public const string SetDatabaseConnection = "Modificare conexiune bază de date";
    public const string TestDatabaseConnection = "Testare conexiune bază de date";
    public const string RevertDatabaseConnection = "Revenire la configurația anterioară a conexiunii bazei de date";
    public const string ResetDatabaseConnection = "Resetare conexiune bază de date la configurația de bază";
    public const string SetAppPort = "Modificare port aplicație";
    // Setari -> Baza de date -> Conexiune: the server of the database (Windows service) and the application itself.
    public const string StopDatabaseServer = "Oprire server bază de date";
    public const string StartDatabaseServer = "Pornire server bază de date";
    public const string RestartDatabaseServer = "Repornire server bază de date";
    public const string ChangeDatabaseServerPort = "Modificare port server bază de date";
    public const string RestartApplication = "Repornire aplicație";
    // Setari -> Baza de date -> Conexiune -> Conturile bazei: the passwords of the predefined accounts (never written in a journal entry, only that and when they changed).
    public const string ChangeDatabaseAccountPassword = "Schimbare parolă cont bază de date";
    public const string VerifyDatabaseAccounts = "Verificare conturi bază de date";
    public const string ResetDatabaseAccounts = "Resetare conturi bază de date";
    public const string CloseOpenSession = "Închidere sesiune deschisă";
    public const string ExportDatabaseConfiguration = "Export configurare bază de date";
    public const string ImportDatabaseConfiguration = "Import configurare bază de date";
    public const string CopyBackupToNas = "Copiere backup pe NAS";
    public const string SetProjectDeadline = "Setare termen proiect";
    public const string RemoveProjectDeadline = "Eliminare termen proiect";
    public const string ReserveStock = "Rezervare stoc";
    public const string ReduceReservation = "Scădere rezervare";
    public const string ReleaseReservation = "Eliberare rezervare";
    public const string ReservationConsumed = "Rezervare consumată prin ieșire";
    public const string ReservationReducedByExit = "Scădere rezervare la ieșire";
    public const string ReservationReleasedByComponent = "Rezervare eliberată la scoaterea componentei";
    public const string ComponentExitReturned = "Retur în depozit la scoaterea componentei";
    public const string ComponentExitLeft = "Rămas la beneficiar la scoaterea componentei";
    public const string ComponentExitMoved = "Mutare ieșire pe altă componentă";
    public const string ComponentExitConsumed = "Consumat la scoaterea componentei";
    // User types (Setari -> Tipuri de utilizatori): each operation names exactly what happened; a permission granted or revoked is one event per key.
    public const string CreateUserType = "Adăugare tip utilizator";
    public const string RenameUserType = "Redenumire tip utilizator";
    public const string EditUserTypeDescription = "Modificare descriere tip utilizator";
    public const string GrantPermission = "Acordare permisiune";
    public const string RevokePermission = "Revocare permisiune";
    public const string DeleteUserType = "Ștergere tip utilizator";
    public const string ChangeUserType = "Schimbare tip utilizator";
    // The first administrator account, made by the installer (BlazorStoc.dll --create-administrator), recorded as made by the system.
    public const string CreateInstallerAdministrator = "Creare cont administrator la instalare";
    public const string AccessDenied = "Acces refuzat";
    public const string ReorderSubcategories = "Reordonare subcategorii";
    public const string AddSystemType = "Adăugare tip de sistem";
    public const string RenameSystemType = "Modificare denumire tip de sistem";
    public const string ActivateSystemType = "Activare tip de sistem";
    public const string DeactivateSystemType = "Dezactivare tip de sistem";
    public const string MoveSystemType = "Schimbare ordine tip de sistem";
    public const string AddSystemTypeAlias = "Adăugare denumire alternativă tip de sistem";
    public const string RemoveSystemTypeAlias = "Ștergere denumire alternativă tip de sistem";
    public const string DeleteAnafVersion = "Ștergere versiune configurație ANAF";
    public const string ActivateAnafVersion = "Activare versiune configurație ANAF";
    public const string VoidExitOperation = "Stornare operație de ieșire";
    public const string ReturnFromBeneficiary = "Retur de la beneficiar";
    // The system records when a module is taken out of service after repeated failures and when it comes back; the administrator's "try again" is recorded under his name.
    public const string ModuleDisabled = "Oprire modul";
    public const string ModuleRecovered = "Revenire modul";
    public const string ModuleRetry = "Reîncercare modul";
    // The administrator stops / starts a module by hand (Setari -> Module); recorded under his name, the stop with the reason he gave.
    public const string ModuleManualStop = "Oprire manuală modul";
    public const string ModuleManualStart = "Pornire manuală modul";
    // Found at the next start: the previous run of the application did not end normally (killed, crashed, the machine went down).
    public const string ApplicationUnexpectedStop = "Oprire neașteptată aplicație";
    // Updating the application (Setari -> Actualizare, docs/PROPUNERE_ACTUALIZARE.md): the administrator's confirmation and his giving up, then what the updater did (written by the system from
    // the history of the updater at the next start, also for an update made by the installer on the server) and the administrator taking note of a failed one.
    public const string ApplicationUpdate = "Actualizare aplicație";
    public const string ApplicationUpdateCancelled = "Anulare actualizare aplicație";
    public const string ApplicationUpdateSucceeded = "Actualizare reușită";
    public const string ApplicationUpdateInstaller = "Actualizare aplicație (installer)";
    public const string ApplicationUpdateFailed = "Actualizare eșuată";
    public const string ApplicationUpdateRefused = "Actualizare refuzată";
    public const string ApplicationRollback = "Revenire la versiunea anterioară";
    public const string ApplicationUpdateAcknowledged = "Confirmare eșec actualizare";

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
            or CreateRiskAnalysis or EditRiskAnalysis or EditRiskAnalysisValidity or RenewRiskAnalysis or UndoRiskAnalysisRenewal or ActivateRiskAnalysis or DeactivateRiskAnalysis
            or RecordMaintenance or EditMaintenanceIntervention or RecordOnDemand or EditOnDemandIntervention or AddInterventionPhoto
            or EditMapEngine or ResetMapEngine or CreateMapPinType or EditMapPinType
            or CreateInvoiceTemplate or EditInvoiceTemplate or EditInvoiceTemplateDetails or ActivateInvoiceTemplate or DeactivateInvoiceTemplate or RenameInvoiceTemplateSupplier or LinkInvoiceTemplateSupplier
            or CreateSupplier or EditSupplier or RecheckSupplier or RecordSupplierInvoice or EditSupplierInvoiceNumber or EditSupplierInvoiceDate or MoveSupplierInvoice or AddSupplierAlias or RemoveSupplierAlias or LinkSupplierProductCode or ChangeSupplierProductCode
            or RecordFreeEntry or AttachEntryToInvoice or DetachEntryFromInvoice or DuplicateEntryOnInvoice or RegularizeNegativeStock or ExitOverStock
            or ExitToBeneficiary or ExitToVehicle or GenericSale or StockCorrection or DuplicateExit or VoidExitOperation or ReturnFromBeneficiary
            or ImportOffer or ReviseOffer or LinkOfferLine or AddBeneficiaryAlias
            or CreateOfferTemplate or EditOfferTemplate or ActivateOfferTemplate or DeactivateOfferTemplate
            or AddProjectComponent or ChangeProjectComponentState or ArchiveProjectComponent or ReactivateProjectComponent or SetVehicleTarget or RemoveVehicleTarget or SetMinStock or RemoveMinStock or SetProjectDeadline or RemoveProjectDeadline or ReserveStock or ReduceReservation or ReleaseReservation or ReservationConsumed or ReservationReducedByExit or ReservationReleasedByComponent or ComponentExitReturned or ComponentExitLeft or ComponentExitMoved or ComponentExitConsumed
            or CreateInstallerAdministrator or AddSystemType or RenameSystemType or ActivateSystemType or DeactivateSystemType or MoveSystemType or AddSystemTypeAlias or RemoveSystemTypeAlias or DeleteAnafVersion or ActivateAnafVersion;

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
        [AuditEntities.Supplier] = SupplierNavigation.SupplierUrl,
        [AuditEntities.SystemType] = SystemTypeNavigation.PageUrl,
        [AuditEntities.OfferTemplate] = OfferNavigation.TemplateUrl
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
    // The list of accounts is the "Utilizatori" tab of Settings (the old /utilizatori address redirects there).
    public const string ListUrl = "/setari?tab=utilizatori&subtab=conturi";
    public static string EditUrl(int userId) => $"{ListUrl}&edit={userId}";
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

    public const string ModulesTab = "module";
    public const string UpdateTab = "actualizare";
    public const string RefreshParameter = "reaplica";

    // The banner's link: the changing value makes Settings apply it even when the address is the one it already applied.
    public static string ModulesUrl() => $"/setari?{TabParameter}={ModulesTab}&{RefreshParameter}={DateTime.UtcNow.Ticks}";
    public static string UpdateUrl() => $"/setari?{TabParameter}={UpdateTab}&{RefreshParameter}={DateTime.UtcNow.Ticks}";

    public static string NotificationSettingsUrl =>$"/setari?{TabParameter}={NotificationsTab}&{SubtabParameter}={SettingsSubtab}";
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

// What the journal pages read (the activity journal module). IAuditTrail is the whole journal: the writing every part of the application does, plus this reading.
public interface IAuditReader
{
    Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default);

    // Filtering, search and paging on the server. window: only the latest N events of the journal are considered (null = all of it).
    // pageSize 0 returns every match of the window (never more than AuditQueryRules.MaxRows).
    Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default);
    Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default);

    // The latest recorded deletion of each object that the given events are about (the events after it keep their links, see
    // AuditNavigation.TargetUrl); the deletions may be older than the page or outside the window.
    Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default);
}

public interface IAuditTrail : IAuditReader
{
    Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default);
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

    // The address the browser connected from (IPv4-mapped IPv6 shown as IPv4) is part of the details of both events.
    public static string? FormatAddress(System.Net.IPAddress? address)
    {
        if (address is null) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }

    public static Task RecordSessionAsync(IAuditTrail? trail, string actorUsername, string actorRole,
        bool connected, CancellationToken cancellationToken, string? address = null, int? rememberedDays = null) => trail is null
        ? Task.CompletedTask
        : trail.RecordAsync(new(actorUsername, actorRole, AuditEntities.User,
            connected ? AuditActions.Login : AuditActions.Logout, actorUsername,
            (connected ? "Sesiune autentificată" : "Sesiune încheiată") + (string.IsNullOrEmpty(address) ? "" : " · IP " + address) +
            (rememberedDays is > 0 ? $" · păstrat autentificat {rememberedDays} zile" : ""),
            string.Empty, string.Empty), cancellationToken);

    // A refused sign-in: the actor and the target are the user name typed (cleaned and cut), the reason and the address are in the details. The password is never passed here.
    public static Task RecordLoginFailedAsync(IAuditTrail? trail, string typedUsername, string reason,
        CancellationToken cancellationToken, string? address = null)
    {
        if (trail is null) return Task.CompletedTask;
        var name = new string((typedUsername ?? "").Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length > 100) name = name[..100];
        if (name.Length == 0) name = "(necompletat)";
        return trail.RecordAsync(new(name, AccessRoles.LimitedUser, AuditEntities.User, AuditActions.LoginFailed, name,
            "Autentificare refuzată: " + reason + (string.IsNullOrEmpty(address) ? "" : " · IP " + address), string.Empty, string.Empty), cancellationToken);
    }

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
