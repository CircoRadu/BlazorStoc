using System.Globalization;
using System.Text.Json.Serialization;

namespace BlazorStoc.Services;

// Values match the legacy io.io_tip_actiune column: 1 = entry, 0 = exit.
public enum StockMovementKind { Exit = 0, Entry = 1 }

// Where an exit goes (required for every new exit). Exits recorded before this field existed have no destination.
// Vehicle is a transfer into a vehicle: the product is moved, not used, so the total stock does not change.
// WarehouseReturn brings pieces back from a vehicle into the warehouse (made from the vehicle page, never from the exit form).
public enum ExitDestination { Beneficiary = 1, Vehicle = 2, GenericSale = 3, StockCorrection = 4, WarehouseReturn = 5 }

// Quantity of a product held by one vehicle (transfers into it minus what was used from it).
public sealed record VehicleStock(int VehicleId, string PlateNumber, string Description, int Quantity);

// One product held by a vehicle (the "Materiale si echipamente" page).
public sealed record VehicleEquipment(int ProductId, string ProductCode, int Quantity);
// Why an entry has no invoice. Only AwaitedInvoice is tracked (a notification waits for the invoice); the supplier is required for it and
// optional for the other kinds.
public enum FreeEntryType { AwaitedInvoice = 1, Adjustment = 2, FromBeneficiary = 3, Donation = 4, Other = 5 }

// Why an exit took more than the stock held (optional, never blocks): an entry not yet recorded, or a wrong stock in the database.
public enum OverStockCause { UnrecordedEntry = 1, WrongStock = 2 }

public sealed record VehicleTransferLine(int ProductId, int Quantity);
// Moves products out of a vehicle: into another vehicle, or back into the warehouse when TargetVehicleId is null.
// Lines null means every product the vehicle holds at the moment of the operation, with its whole quantity.
public sealed record VehicleTransfer(int SourceVehicleId, int? TargetVehicleId, IReadOnlyList<VehicleTransferLine>? Lines = null);

public sealed record StockMovement(int Id, int ProductId, StockMovementKind Kind, int Quantity, DateOnly Date,
    string Description, int? BeneficiaryId, string? BeneficiaryName, int? ProjectId, string? ProjectName,
    string Operator, long Version, DateTime CreatedUtc, DateTime UpdatedUtc, bool Modified = false,
    ExitDestination? Destination = null, int? VehicleId = null, string? VehiclePlate = null,
    int? SourceVehicleId = null, string? SourceVehiclePlate = null,
    int? InvoiceId = null, string? InvoiceNumber = null, string? SupplierName = null, int? SupplierId = null,
    FreeEntryType? FreeType = null, int? FreeSupplierId = null, string? FreeSupplierName = null, string? Reference = null,
    OverStockCause? OverStockCause = null, int? OperationId = null,
    string? VoidedUtc = null, string? VoidReason = null, string? VoidedBy = null, int? ReturnOfMovementId = null)
{
    // The exit operation it belonged to was voided (storno): the row stays as a trace but no longer counts in any stock.
    [JsonIgnore] public bool IsVoided => VoidedUtc is not null;
    // A free entry that waits for the invoice of its supplier.
    [JsonIgnore] public bool IsAwaitingInvoice => InvoiceId is null && Kind == StockMovementKind.Entry && FreeType == FreeEntryType.AwaitedInvoice;
    // An entry taken from a supplier's invoice (Preluare factura) carries the invoice; an entry without one is a free entry.
    [JsonIgnore] public bool HasInvoice => InvoiceId is not null;
    // Signed effect on the total product stock: entries add, exits subtract, except a transfer into a vehicle (0).
    [JsonIgnore] public int Effect => StockMovementRules.Effect(Kind, Destination, Quantity);
    // A move between vehicles or a return to the warehouse (made from the vehicle page); edited without the destination picker.
    [JsonIgnore] public bool IsVehicleTransfer => Kind == StockMovementKind.Exit && SourceVehicleId is not null &&
        Destination is ExitDestination.Vehicle or ExitDestination.WarehouseReturn;
}

public sealed class StockMovementInput
{
    public StockMovementKind Kind { get; set; } = StockMovementKind.Entry;
    public DateOnly? Date { get; set; }
    public int? Quantity { get; set; }
    public string Description { get; set; } = "";
    public int? BeneficiaryId { get; set; }
    public int? ProjectId { get; set; }
    public ExitDestination? Destination { get; set; }
    // Destination vehicle (only for ExitDestination.Vehicle) and source vehicle (null = the warehouse).
    public int? VehicleId { get; set; }
    public int? SourceVehicleId { get; set; }
    // The invoice an entry is taken from (set only when the entry is created by the invoice pickup; null = a free entry). It is not changed
    // when a movement is edited.
    public int? InvoiceId { get; set; }
    // Free entry (no invoice): the kind, the supplier (required for AwaitedInvoice) and a free reference (a document, a name). Fixed at creation.
    public FreeEntryType? FreeType { get; set; }
    public int? FreeSupplierId { get; set; }
    public string? Reference { get; set; }
    // Entry "Primit de la beneficiar": the exit it returns (the return is tied to the exit and counts against the consumption of its project).
    public int? ReturnOfMovementId { get; set; }
    // Exits only: why the exit went over the stock (optional; kept only when the exit really was over the stock).
    public OverStockCause? OverStockCause { get; set; }
    // Exits to a project: the component of the project the product goes to (chosen), or OutsideOffer for a product that is not in the offer. Both empty = decided
    // automatically at creation (the only component whose offer has the product; "in afara ofertei" when none has it; refused when several have it).
    public int? ProjectComponentId { get; set; }
    public bool OutsideOffer { get; set; }
    // Exits that would take pieces reserved by other projects: ReservationAck = continue without touching the reservations; or lower the reservation of a project
    // (quantity, default what is needed) with a short reason. Neither = the exit is refused with a warning (ReservationWarningException).
    public bool ReservationAck { get; set; }
    public int? ReduceReservationProjectId { get; set; }
    public int? ReduceReservationQuantity { get; set; }
    public string ReduceReservationReason { get; set; } = "";
    // Quantity written on the invoice line for this product (known from the automatic pickup, optional by hand): used to detect an overrun.
    public int? InvoiceQuantity { get; set; }
    // Reason given by the user to add an entry that repeats the product on the same invoice or exceeds its quantity; empty = not confirmed.
    public string DuplicateReason { get; set; } = "";
    public string Reason { get; set; } = "";

    public StockMovementInput Clone() => (StockMovementInput)MemberwiseClone();

    // An exit recorded before destinations existed and holding a beneficiary is shown as an exit to a beneficiary;
    // one without any relation has no destination, so editing it requires choosing one.
    public static StockMovementInput From(StockMovement movement) => new()
    {
        Kind = movement.Kind, Date = movement.Date, Quantity = movement.Quantity, Description = movement.Description,
        BeneficiaryId = movement.BeneficiaryId, ProjectId = movement.ProjectId,
        Destination = movement.Kind == StockMovementKind.Exit
            ? movement.Destination ?? (movement.BeneficiaryId is not null ? ExitDestination.Beneficiary : null)
            : null,
        VehicleId = movement.VehicleId, SourceVehicleId = movement.SourceVehicleId, InvoiceId = movement.InvoiceId,
        FreeType = movement.FreeType, FreeSupplierId = movement.FreeSupplierId, Reference = movement.Reference,
        OverStockCause = movement.OverStockCause, ReturnOfMovementId = movement.ReturnOfMovementId
    };
}

// Entries only: Source narrows them to those with or without an invoice; SupplierId to those of invoices of that supplier.
public enum EntrySource { All, WithInvoice, Free }
public sealed record StockMovementQuery(StockMovementKind? Kind = null, bool Descending = false, int Page = 1, int PageSize = 10,
    EntrySource Source = EntrySource.All, int? SupplierId = null, bool OverStockOnly = false,
    ExitDestination? Destination = null, int? BeneficiaryId = null, int? VehicleId = null, string? Text = null, DateOnly? Date = null);
public sealed record MovementSupplier(int Id, string Name);

// What the user chose for a product whose stock is negative: stock set to zero, or the real quantity found in the warehouse.
public sealed class NegativeStockResolution
{
    public bool Zero { get; set; }
    public int? Real { get; set; }
    public bool IsResolved => Zero || Real is >= 0 and <= StockMovementRules.MaxQuantity;
    // Real quantity in the warehouse (0 when the user chose "set to zero").
    public int RealQuantity => Zero ? 0 : Real ?? 0;
}
// Stock is the total; InVehicles is the part of it held by vehicles (the warehouse holds Stock - InVehicles).
public sealed record StockMovementPage(IReadOnlyList<StockMovement> Items, int TotalCount, int Stock, bool AnyModified, int InVehicles = 0,
    IReadOnlyList<MovementSupplier>? Suppliers = null, IReadOnlyCollection<int>? OverStockIds = null,
    IReadOnlyList<MovementSupplier>? ExitBeneficiaries = null, IReadOnlyList<MovementSupplier>? ExitVehicles = null);
public sealed record StockMovementResult(StockMovement Movement, int Stock);

// An exit operation: one or more exits of different products with a common date, destination (beneficiary / project / vehicle / generic sale /
// stock correction) and reference, saved all or nothing. Each line is a complete exit input (its own quantity, source and description).
public sealed record ExitOperationLine(int ProductId, StockMovementInput Input);
// What saving a line would do, shown in the summary before the operation is saved: the part over the stock, a repeated exit, the stock afterwards.
public sealed record ExitLinePreview(int Index, int ProductId, string ProductName, int OverStock, bool Duplicate, int StockAfter, int ReservationExcess = 0,
    IReadOnlyList<ReservationHolder>? Holders = null);
public sealed record ExitOperationResult(int OperationId, IReadOnlyList<StockMovementResult> Movements);
// The operation as printed on the consumption note (bon de consum / aviz de predare) and shown for the storno.
public sealed record ExitOperationLineInfo(int MovementId, int ProductId, string ProductName, int Quantity, string Source, string Description);
public sealed record ExitOperationDetails(int OperationId, DateOnly Date, ExitDestination? Destination, string? BeneficiaryName, string? BeneficiaryCui,
    string? ProjectName, string? VehiclePlate, string? Reference, string Operator, string? VoidedUtc, string? VoidReason,
    IReadOnlyList<ExitOperationLineInfo> Lines);
// An exit to a beneficiary that a return can be tied to: Returned is what earlier returns already took back.
public sealed record ReturnableExit(int MovementId, int OperationId, DateOnly Date, string? Reference, string? BeneficiaryName, string? ProjectName, int Quantity, int Returned)
{
    public int Remaining => Quantity - Returned;
}
// Consumption of a project or beneficiary per product: what left in exits to it minus what came back in returns.
public sealed record NetConsumption(int ProductId, string ProductName, int Exited, int Returned)
{
    public int Net => Exited - Returned;
}

// A product that is in the "De regularizat" state: the stock of the warehouse (or of a vehicle) is negative after exits over the stock.
// Since = date of the oldest exit over the stock that has not been covered yet; Cause = the cause of the latest such exit that has one.
public sealed record RegularizationItem(int ProductId, string ProductName, int Stock, int WarehouseStock, bool NegativeVehicle,
    int OverStockExits, DateOnly Since, OverStockCause? Cause)
{
    public int Days(DateOnly today) => Math.Max(0, today.DayNumber - Since.DayNumber);
    // The regularization operation exists for a negative total stock; a negative vehicle (or warehouse with stock in vehicles) is fixed by editing the exits.
    public bool CanRegularize => Stock < 0;
}
public sealed record RegularizationQuery(OverStockCause? Cause = null, bool NoCause = false, int? OlderThanDays = null);
public sealed record StockMovementHistoryEntry(int Id, int MovementId, string Actor, DateTime TimestampUtc, string Changes,
    int StockCorrection, string Reason);
// Contract consumed by the project "Echipamente" page (TODO Task 2 / Subtask 2.4).
public sealed record ProjectStockMovement(int Id, int ProductId, string ProductCode, int Quantity, DateOnly Date, string Operator);

public class StockMovementOperationException(string message) : Exception(message);

// The exit repeats one recorded the same day (same product, quantity, destination and relations); it is added only with a reason.
public sealed class DuplicateExitWarningException(string message) : StockMovementOperationException(message);

// The entry repeats a product already taken from the invoice, or exceeds the quantity written on it; it is added only with a reason.
public sealed class InvoiceEntryWarningException(string message, bool exceeds, int existingQuantity, DateOnly? existingDate)
    : StockMovementOperationException(message)
{
    public bool Exceeds { get; } = exceeds;
    public int ExistingQuantity { get; } = existingQuantity;
    public DateOnly? ExistingDate { get; } = existingDate;
}

// A free entry listed for association with an invoice (the "Intrari fara factura" tab).
public sealed record FreeEntry(int MovementId, int ProductId, string ProductName, int Quantity, DateOnly Date, FreeEntryType? Type,
    int? SupplierId, string? SupplierName, string? Reference, string Description, string Operator);
// Type null = every entry with a recorded reason; Unspecified = the older entries recorded without one.
public sealed record FreeEntryQuery(FreeEntryType? Type = null, int? SupplierId = null, int? OlderThanDays = null, bool Unspecified = false);

public interface IStockMovementRepository
{
    Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default);
    Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);
    // Vehicles holding the product (quantity greater than zero), ordered by registration number.
    Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default);
    // Products held by one vehicle (quantity greater than zero), ordered by product code.
    Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default);
    // Returns pieces to the warehouse or moves them to another vehicle, one stock movement per product, all or nothing.
    Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default);
    // Product id -> quantity held by vehicles, only for products with a quantity greater than zero in vehicles.
    Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default);
    // Vehicle id -> number of live movements that use the vehicle as source or destination.
    Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default);
    Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default);
    Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default);
    // Free entries (no invoice), oldest first, for the "Intrari fara factura" tab and the notification.
    // The stock of the product is negative (an operating error): a correction entry brings it to the real quantity found in the warehouse (0 = set to
    // zero). Computed under the product lock, so it stays right if the stock changed meanwhile; refused when the stock is no longer negative.
    Task<StockMovementResult> RegularizeNegativeStockAsync(int productId, int realWarehouseQuantity, string context, CancellationToken cancellationToken = default);
    // Products to regularize (negative stock after exits over the stock), oldest first; the list of the "De regularizat" tab and the dashboard counter.
    // Exit operation (the exit basket): all lines or none; every line follows the rules of a single exit (over the stock allowed and marked,
    // repeated exit needs a reason). The preview runs the same checks without saving and reports the warnings of each line.
    // The operation (all its exits, voided or not), for the consumption note and the storno dialog; null when it does not exist.
    Task<ExitOperationDetails?> GetOperationAsync(int operationId, CancellationToken cancellationToken = default);
    // Storno: cancels every exit of the operation with a reason, keeping them as a trace (stock and vehicle quantities are restored); refused when
    // a return is tied to one of them or when a vehicle would be left with a negative quantity.
    Task VoidExitOperationAsync(int operationId, string reason, CancellationToken cancellationToken = default);
    // Exits to a beneficiary of the product that still have something to return, newest first.
    Task<IReadOnlyList<ReturnableExit>> GetReturnableExitsAsync(int productId, CancellationToken cancellationToken = default);
    // Net consumption of a project, or of a beneficiary (all its projects and exits without a project).
    Task<IReadOnlyList<NetConsumption>> GetNetConsumptionAsync(int? projectId, int? beneficiaryId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExitLinePreview>> PreviewExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default);
    Task<ExitOperationResult> CreateExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RegularizationItem>> GetToRegularizeAsync(RegularizationQuery? query = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FreeEntry>> GetFreeEntriesAsync(FreeEntryQuery query, CancellationToken cancellationToken = default);
    // Administrator only. Ties free entries to an invoice (stock unchanged), all or nothing; each entry is its own event in the journal.
    // The reason is required; when the quantities differ from the invoice line it says why.
    // viaPickup: the operator doing the invoice pickup ties an entry the pickup matched (instead of the administrator).
    Task<IReadOnlyList<StockMovement>> AttachToInvoiceAsync(IReadOnlyList<StockMovement> entries, int invoiceId, string reason, bool viaPickup = false, CancellationToken cancellationToken = default);
    // Administrator only. Frees an entry from its invoice (stock unchanged).
    Task<StockMovement> DetachFromInvoiceAsync(StockMovement entry, string reason, CancellationToken cancellationToken = default);
}

public static class StockMovementRules
{
    public const int MaxQuantity = 100_000;
    public const int MaxDescriptionLength = 500;
    public const string DescriptionRequiredMessage = "Completează câmpul «Descriere».";
    public const string StaleMessage = "Mișcarea a fost modificată sau ștearsă între timp. Actualizează pagina și reia operația.";
    public const string FutureDateMessage = "Data mișcării nu poate fi în viitor. Alege azi sau o zi anterioară.";
    public static readonly DateOnly EarliestDate = new(1990, 1, 1);

    // The latest selectable day: movements record what already happened, so the calendar stops at today.
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public const int MaxReferenceLength = 200;
    public const int MaxOperationLines = 50;
    public const string MovementVoidedMessage = "Mișcarea aparține unei operații stornate și nu se mai poate modifica sau șterge.";
    public const string OperationMissingMessage = "Operația nu există sau a fost deja stornată.";
    public const string VoidHasReturnsMessage = "Operația are retururi legate de ea: nu se poate storna. Anulează mai întâi returul.";
    public static string VoidVehicleMessage(string plate) => $"Stornarea ar lăsa mașina {plate} cu stoc negativ (produsul a fost deja folosit din ea).";
    public static string ReturnTooMuchMessage(int remaining) => $"Returul depășește cantitatea ieșită și nereturnată ({remaining} buc.).";
    public const string ComponentChoiceMessage = "Produsul apare în mai multe componente ale proiectului: alege componenta sau „în afara ofertei”.";
    public const string ComponentInvalidMessage = "Componenta aleasă nu aparține proiectului sau este scoasă din proiect.";
    public const string ReturnMissingMessage = "Ieșirea aleasă pentru retur nu mai există, este stornată sau nu este spre un beneficiar a acestui produs.";
    public const string EmptyOperationMessage = "Adaugă cel puțin un produs în operație.";
    public static string OperationLinesMessage => $"O operație poate avea cel mult {MaxOperationLines} de produse.";
    public const string OperationMismatchMessage = "Toate liniile unei operații au aceeași dată, destinație și referință.";
    public static string LineMessage(int index, string message) => $"Linia {index + 1}: {message}";
    public const string RegularizationReference = "Regularizare stoc negativ";
    public const string StockNotNegativeMessage = "Stocul produsului nu mai este negativ. Actualizează pagina și reia operația.";
    public const string RealQuantityMessage = "Introdu cantitatea reală din depozit (număr întreg, cel puțin 0).";
    public static string NegativeStockWarning(int stock) =>
        $"Stoc curent {stock}. Stoc negativ nu poate exista fizic: ieșiri sau intrări au fost operate greșit. Stabilește cantitatea reală din depozit înainte de intrare.";
    public static string RegularizationDescription(int stock, int real, string context) =>
        $"Regularizare stoc negativ ({stock}): cantitate reală în depozit {real}" + (string.IsNullOrWhiteSpace(context) ? "" : $" · {context}");
    // Effect of a movement on the quantity held by the warehouse itself (the vehicles hold the rest of the stock).
    public static int WarehouseEffect(StockMovementKind kind, ExitDestination? destination, int? sourceVehicleId, int quantity) =>
        kind == StockMovementKind.Entry ? quantity
        : sourceVehicleId is not null ? (destination == ExitDestination.WarehouseReturn ? quantity : 0)
        : -quantity;
    // Exits that took more than the warehouse held at that moment (movements in date order; the stock before the first one is what is left
    // after removing their total effect from the current warehouse stock, so quantities set when a product was created are counted).
    // An exit taken from a vehicle is over the stock when the vehicle held less than that at the time (what it holds comes only from transfers into it).
    public static IReadOnlySet<int> OverStockExits(IEnumerable<(int Id, DateOnly Date, StockMovementKind Kind, ExitDestination? Destination, int? SourceVehicleId, int Quantity, int? VehicleId)> movements, int warehouseStock)
    {
        var ordered = movements.OrderBy(item => item.Date).ThenBy(item => item.Id).ToList();
        var balance = warehouseStock - ordered.Sum(item => WarehouseEffect(item.Kind, item.Destination, item.SourceVehicleId, item.Quantity));
        var held = new Dictionary<int, int>();
        var over = new HashSet<int>();
        foreach (var item in ordered)
        {
            balance += WarehouseEffect(item.Kind, item.Destination, item.SourceVehicleId, item.Quantity);
            if (item.Kind != StockMovementKind.Exit) continue;
            if (item.SourceVehicleId is { } source)
            {
                held[source] = held.GetValueOrDefault(source) - item.Quantity;
                if (held[source] < 0) over.Add(item.Id);
            }
            else if (balance < 0) over.Add(item.Id);
            if (item.Destination == ExitDestination.Vehicle && item.VehicleId is { } target) held[target] = held.GetValueOrDefault(target) + item.Quantity;
        }
        return over;
    }
    public static string CauseLabel(OverStockCause cause) => cause switch
    {
        OverStockCause.UnrecordedEntry => "Intrare neoperată",
        OverStockCause.WrongStock => "Stoc greșit în bază",
        _ => string.Empty
    };

    // The over-stock exits that still leave the product negative: those since the balance (warehouse, or each vehicle) was last not negative.
    // Empty when nothing is negative now (an entry or a regularization after them closed the case).
    public static IReadOnlyList<(int Id, DateOnly Date, OverStockCause? Cause)> UnresolvedOverStock(
        IEnumerable<(int Id, DateOnly Date, StockMovementKind Kind, ExitDestination? Destination, int? SourceVehicleId, int Quantity, int? VehicleId, OverStockCause? Cause)> movements,
        int warehouseStock)
    {
        var ordered = movements.OrderBy(item => item.Date).ThenBy(item => item.Id).ToList();
        var balance = warehouseStock - ordered.Sum(item => WarehouseEffect(item.Kind, item.Destination, item.SourceVehicleId, item.Quantity));
        var held = new Dictionary<int, int>();
        var warehouse = new List<(int, DateOnly, OverStockCause?)>();
        var inVehicles = new Dictionary<int, List<(int, DateOnly, OverStockCause?)>>();
        foreach (var item in ordered)
        {
            balance += WarehouseEffect(item.Kind, item.Destination, item.SourceVehicleId, item.Quantity);
            if (item.Kind == StockMovementKind.Exit)
            {
                if (item.SourceVehicleId is { } source)
                {
                    held[source] = held.GetValueOrDefault(source) - item.Quantity;
                    if (held[source] < 0)
                    {
                        if (!inVehicles.TryGetValue(source, out var list)) inVehicles[source] = list = [];
                        list.Add((item.Id, item.Date, item.Cause));
                    }
                }
                else if (balance < 0) warehouse.Add((item.Id, item.Date, item.Cause));
                if (item.Destination == ExitDestination.Vehicle && item.VehicleId is { } target) held[target] = held.GetValueOrDefault(target) + item.Quantity;
            }
            if (balance >= 0) warehouse.Clear();
            foreach (var (vehicle, list) in inVehicles) if (held.GetValueOrDefault(vehicle) >= 0) list.Clear();
        }
        var result = new List<(int, DateOnly, OverStockCause?)>();
        if (balance < 0) result.AddRange(warehouse);
        foreach (var (vehicle, list) in inVehicles) if (held.GetValueOrDefault(vehicle) < 0) result.AddRange(list);
        return result.OrderBy(item => item.Item2).ThenBy(item => item.Item1).ToList();
    }

    public static IReadOnlyList<RegularizationItem> FilterRegularization(IEnumerable<RegularizationItem> items, RegularizationQuery? query, DateOnly today)
    {
        var filtered = items.Where(item =>
            (query?.Cause is not { } cause || item.Cause == cause)
            && (query?.NoCause != true || item.Cause is null)
            && (query?.OlderThanDays is not { } days || item.Days(today) >= days));
        return filtered.OrderBy(item => item.Since).ThenBy(item => item.ProductName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string DuplicateExitMessage(string product, int quantity, string destination, DateOnly date) =>
        $"Aceeași ieșire a fost deja înregistrată pentru {product}: {quantity} buc., {destination}, în ziua {DisplayDate(date)}. Adaug-o totuși doar dacă nu e o dublură, cu motiv.";
    // Journal action of an exit by its destination (what the exit form records; moves between vehicles have their own).
    public static string ExitAuditAction(ExitDestination? destination) => destination switch
    {
        ExitDestination.Beneficiary => AuditActions.ExitToBeneficiary,
        ExitDestination.Vehicle => AuditActions.ExitToVehicle,
        ExitDestination.GenericSale => AuditActions.GenericSale,
        ExitDestination.StockCorrection => AuditActions.StockCorrection,
        ExitDestination.WarehouseReturn => AuditActions.ReturnEquipment,
        _ => AuditActions.Create
    };
    public const string AwaitedSupplierRequiredMessage = "Pentru o factură așteptată alege furnizorul.";
    public const string NothingToAttachMessage = "Alege cel puțin o intrare liberă.";
    public const string AlreadyOnInvoiceMessage = "Intrarea este deja legată de o factură.";
    public const string NotOnInvoiceMessage = "Intrarea nu este legată de o factură.";
    public const string InvoiceMissingMessage = "Factura aleasă nu mai există. Actualizează pagina și reia operația.";
    public const string DestinationRequiredMessage = "Alege destinația ieșirii.";
    public const string TransferBetweenVehiclesMessage =
        "Transferul dintr-o mașină în alta nu se face din formularul de ieșire; se face din pagina vehiculului.";

    public static int Effect(StockMovementKind kind, ExitDestination? destination, int quantity) =>
        kind == StockMovementKind.Entry ? quantity : destination is ExitDestination.Vehicle or ExitDestination.WarehouseReturn ? 0 : -quantity;
    public static int Effect(StockMovementKind kind, int quantity) => Effect(kind, null, quantity);
    public static string FreeTypeLabel(FreeEntryType type) => type switch
    {
        FreeEntryType.AwaitedInvoice => "Achiziție fără factură (factură așteptată)",
        FreeEntryType.Adjustment => "Inventar / corecție în plus",
        FreeEntryType.FromBeneficiary => "Primit de la beneficiar / vehicul",
        FreeEntryType.Donation => "Donație",
        _ => "Altul"
    };
    // What the table shows for an entry without invoice: "Intrare liberă" with its kind when known.
    public static string FreeEntryLabel(StockMovement movement) => movement.FreeType is { } type
        ? $"Intrare liberă · {FreeTypeLabel(type)}" : "Intrare liberă";
    // Wording of the duplicate / overrun warnings (the service refuses the entry until the user gives a reason).
    public static string RepeatedOnInvoiceMessage(string product, int existing, DateOnly date) =>
        $"Produsul {product} a fost deja preluat de pe această factură: {existing} buc. (intrare din {DisplayDate(date)}). Modifică intrarea existentă sau adaugă totuși o intrare nouă, cu motiv.";
    public static string ExceedsInvoiceMessage(string product, int invoiceQuantity, int total) =>
        $"Produsul {product} are pe factură {invoiceQuantity} buc., iar cu această intrare totalul preluat ar fi {total} buc. Adaugă totuși intrarea numai cu motiv.";
    public static string KindLabel(StockMovementKind kind) => kind == StockMovementKind.Entry ? "Intrare" : "Ieșire";
    public static string DestinationLabel(ExitDestination destination) => destination switch
    {
        ExitDestination.Beneficiary => "Beneficiar",
        ExitDestination.Vehicle => "Autovehicul",
        ExitDestination.GenericSale => "Vânzare generică",
        ExitDestination.WarehouseReturn => "Restituire în depozit",
        _ => "Corecție stoc"
    };

    // Description proposed by the exit form; the user can edit it. Null when the destination proposes nothing.
    public static string? SuggestedDescription(ExitDestination? destination, string? vehiclePlate, DateOnly today) => destination switch
    {
        ExitDestination.Vehicle when !string.IsNullOrWhiteSpace(vehiclePlate) => $"Completare stoc mașină {vehiclePlate} {DisplayDate(today)}",
        ExitDestination.StockCorrection => $"Corecție stoc {DisplayDate(today)}",
        _ => null
    };

    // A suggestion never overwrites text typed by the user: it replaces only an empty description or the previous,
    // unchanged suggestion (which is removed when the new destination suggests nothing).
    public static string ApplySuggestion(string? description, string? previousSuggestion, string? newSuggestion)
    {
        var current = description ?? string.Empty;
        return current.Trim().Length == 0 || (previousSuggestion is not null && current == previousSuggestion)
            ? newSuggestion ?? string.Empty
            : current;
    }

    public static string NotEnoughInVehicleMessage(string plate, int held) => held <= 0
        ? $"În mașina {plate} nu există acest produs."
        : $"În mașina {plate} există numai {held} {(held == 1 ? "bucată" : "bucăți")} din acest produs.";
    public static string NegativeVehicleStockMessage(string plate) =>
        $"Operația ar lăsa în mașina {plate} o cantitate negativă din acest produs: bucăți din ea au fost deja folosite sau mutate.";
    public const string NothingToTransferMessage = "Mașina nu conține materiale sau echipamente.";
    public const string SameVehicleMessage = "Alege o altă mașină decât cea din care se scot produsele.";
    public static string TransferDescription(string sourcePlate, string? targetPlate, DateOnly day) => targetPlate is null
        ? $"Restituire în depozit din mașina {sourcePlate} {DisplayDate(day)}"
        : $"Mutare din mașina {sourcePlate} în mașina {targetPlate} {DisplayDate(day)}";

    // Checks a transfer request before it reaches the database (identifiers, quantities, no repeated product).
    public static void ValidateTransfer(VehicleTransfer transfer)
    {
        if (transfer.SourceVehicleId <= 0) throw new StockMovementOperationException(VehicleMissingMessage);
        if (transfer.TargetVehicleId is { } target && (target <= 0 || target == transfer.SourceVehicleId))
            throw new StockMovementOperationException(target == transfer.SourceVehicleId ? SameVehicleMessage : VehicleMissingMessage);
        if (transfer.Lines is null) return;
        if (transfer.Lines.Count == 0) throw new StockMovementOperationException(NothingToTransferMessage);
        if (transfer.Lines.Select(line => line.ProductId).Distinct().Count() != transfer.Lines.Count)
            throw new StockMovementOperationException("Același produs apare de două ori în operație.");
        foreach (var line in transfer.Lines)
        {
            if (line.ProductId <= 0) throw new StockMovementOperationException("Produsul selectat nu este valid.");
            if (line.Quantity < 1) throw new StockMovementOperationException("Introdu o cantitate întreagă mai mare decât zero.");
            if (line.Quantity > MaxQuantity) throw new StockMovementOperationException($"Cantitatea poate fi cel mult {MaxQuantity:N0}.");
        }
    }

    public const string VehicleMissingMessage = "Vehiculul selectat nu mai există. Actualizează lista și reia operația.";

    public static string ProductsLabel(int quantity) => $"{quantity} {(quantity == 1 ? "produs" : "produse")}";

    // "N produse: X în depozit, Y în vehicule", shown only when some of the stock is held by vehicles.
    public static string? StockBreakdown(int total, int inVehicles) => inVehicles > 0
        ? $"{ProductsLabel(total)}: {StockSplit(total, inVehicles)}"
        : null;
    // "X în depozit, Y în vehicule" (for places that already show the total).
    public static string? StockSplit(int total, int inVehicles) => inVehicles > 0
        ? $"{total - inVehicles} în depozit, {inVehicles} în vehicule"
        : null;
    // The warehouse holds the total minus the part in vehicles (used by the physical inventory).
    public static int WarehouseStock(int total, int inVehicles) => total - Math.Max(0, inVehicles);
    // Every date shown to the user is dd.MM.yyyy (development rule). The legacy database column io_data keeps its own
    // dd-MM-yyyy text (LegacyDate); it is never shown.
    public static string DisplayDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public static string LegacyDate(DateOnly date) => date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    // Journal and history texts written before the rule hold movement dates as dd-MM-yyyy; they are shown as dd.MM.yyyy.
    // Only real calendar dates are rewritten (a code such as 99-99-2020 stays as it is).
    public static string NormalizeDisplayDates(string? text) => string.IsNullOrEmpty(text) ? text ?? string.Empty
        : System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\d)(\d{2})-(\d{2})-(\d{4})(?!\d)", match =>
            DateOnly.TryParseExact(match.Value, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? DisplayDate(date) : match.Value);
    public static string StorageDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseStorageDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateOnly ParseLegacyDate(string value) => DateOnly.ParseExact(value.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture);

    // allowVehicleTransfer: the movement being edited is an existing move between vehicles (made from the vehicle page).
    public static StockMovementInput Validated(StockMovementInput input, StockMovementKind kind, bool isEdit, DateOnly? today = null,
        bool allowVehicleTransfer = false)
    {
        var errors = new List<string>();
        var description = TextNormalization.ForStorage(input.Description ?? string.Empty);
        CheckDateQuantityAndDescription(input, description, today, errors);
        if (kind == StockMovementKind.Entry) CheckEntry(input, errors);
        else CheckExit(input, allowVehicleTransfer, errors);
        var reference = TextNormalization.ForStorage(input.Reference ?? string.Empty);
        if (reference.Length > MaxReferenceLength) errors.Add($"Referința poate avea cel mult {MaxReferenceLength} de caractere.");
        var reason = ChangeReasonRules.Normalize(input.Reason);
        var duplicateReason = ChangeReasonRules.Normalize(input.DuplicateReason);
        if (duplicateReason.Length > 0 && ChangeReasonRules.ValidationError(duplicateReason) is { } duplicateError) errors.Add(duplicateError);
        if (isEdit && ChangeReasonRules.ValidationError(reason) is { } reasonError) errors.Add(reasonError);
        if (errors.Count > 0) throw new StockMovementOperationException(string.Join(" ", errors));
        return Cleaned(input, kind, description, reference, reason, duplicateReason);
    }

    private static void CheckDateQuantityAndDescription(StockMovementInput input, string description, DateOnly? today, List<string> errors)
    {
        if (input.Date is not { } date || date < EarliestDate) errors.Add("Alege o dată validă pentru mișcare.");
        else if (date > (today ?? Today)) errors.Add(FutureDateMessage);
        if (input.Quantity is not { } quantity || quantity < 1) errors.Add("Introdu o cantitate întreagă mai mare decât zero.");
        else if (quantity > MaxQuantity) errors.Add($"Cantitatea poate fi cel mult {MaxQuantity:N0}.");
        if (description.Length == 0) errors.Add(DescriptionRequiredMessage);
        else if (description.Length > MaxDescriptionLength) errors.Add($"Descrierea poate avea cel mult {MaxDescriptionLength} de caractere.");
    }

    // An entry has no beneficiary, project, destination or vehicle; its invoice or its free-entry reason and supplier are checked together.
    private static void CheckEntry(StockMovementInput input, List<string> errors)
    {
        if (input.BeneficiaryId is not null || input.ProjectId is not null)
            errors.Add("Beneficiarul și proiectul se pot alege numai la ieșire.");
        if (input.Destination is not null || input.VehicleId is not null || input.SourceVehicleId is not null)
            errors.Add("Destinația și sursa se pot alege numai la ieșire.");
        if (input.InvoiceId is <= 0) errors.Add("Factura aleasă nu este validă.");
        if (input.InvoiceId is not null && (input.FreeType is not null || input.FreeSupplierId is not null))
            errors.Add("O intrare cu factură nu are tip de intrare liberă.");
        if (input.FreeType is { } freeType && !Enum.IsDefined(freeType)) errors.Add("Alege motivul intrării libere.");
        else if (input.FreeType == FreeEntryType.AwaitedInvoice && input.FreeSupplierId is null) errors.Add(AwaitedSupplierRequiredMessage);
        if (input.FreeSupplierId is <= 0) errors.Add("Furnizorul ales nu este valid.");
        if (input.FreeSupplierId is not null && input.FreeType is null && input.InvoiceId is null) errors.Add("Alege motivul intrării libere.");
        if (input.InvoiceQuantity is < 1 or > MaxQuantity) errors.Add("Cantitatea de pe factură trebuie să fie între 1 și " + MaxQuantity.ToString("N0", CultureInfo.InvariantCulture) + ".");
    }

    // An exit has no invoice or free-entry reason; what else it needs depends on where it goes.
    private static void CheckExit(StockMovementInput input, bool allowVehicleTransfer, List<string> errors)
    {
        var beneficiaryId = input.BeneficiaryId;
        var projectId = input.ProjectId;
        var vehicleId = input.VehicleId;
        var sourceVehicleId = input.SourceVehicleId;
        if (input.InvoiceId is not null) errors.Add("Factura se poate alege numai la intrare.");
        if (input.FreeType is not null || input.FreeSupplierId is not null)
            errors.Add("Motivul intrării libere se poate alege numai la intrare.");
        if (input.Destination is not { } chosen || !Enum.IsDefined(chosen)) errors.Add(DestinationRequiredMessage);
        else if (chosen == ExitDestination.Beneficiary)
        {
            if (beneficiaryId is null or <= 0) errors.Add("Alege un beneficiar din listă.");
            if (projectId is not null && beneficiaryId is null) errors.Add("Alege beneficiarul înainte de proiect.");
            if (projectId is <= 0) errors.Add("Alege un proiect din listă sau debifează opțiunea proiect.");
            if (vehicleId is not null) errors.Add("Vehiculul se poate alege numai pentru ieșirile spre autovehicul.");
        }
        else if (chosen == ExitDestination.Vehicle)
        {
            if (vehicleId is null or <= 0) errors.Add("Alege vehiculul spre care se face ieșirea.");
            if (beneficiaryId is not null || projectId is not null)
                errors.Add("Beneficiarul și proiectul se pot alege numai pentru ieșirile spre beneficiar.");
            if (sourceVehicleId is not null && !allowVehicleTransfer) errors.Add(TransferBetweenVehiclesMessage);
        }
        else if (chosen == ExitDestination.WarehouseReturn)
        {
            if (sourceVehicleId is null or <= 0) errors.Add("Restituirea în depozit cere mașina din care se scot produsele.");
            if (beneficiaryId is not null || projectId is not null || vehicleId is not null)
                errors.Add("Restituirea în depozit nu are beneficiar, proiect sau vehicul destinație.");
        }
        else if (beneficiaryId is not null || projectId is not null || vehicleId is not null)
            errors.Add("Vânzarea generică și corecția de stoc nu au beneficiar, proiect sau vehicul.");
        if (sourceVehicleId is <= 0) errors.Add("Alege mașina din care se scoate produsul sau alege depozitul.");
    }

    // The input as it is kept once it passed: what does not belong to the kind of movement is dropped.
    private static StockMovementInput Cleaned(StockMovementInput input, StockMovementKind kind, string description, string reference, string reason, string duplicateReason)
    {
        var projectId = input.ProjectId;
        return new StockMovementInput
        {
            Kind = kind, Date = input.Date, Quantity = input.Quantity, Description = description,
            BeneficiaryId = input.BeneficiaryId, ProjectId = projectId, Reason = reason,
            Destination = kind == StockMovementKind.Exit ? input.Destination : null,
            VehicleId = input.VehicleId, SourceVehicleId = input.SourceVehicleId, InvoiceId = kind == StockMovementKind.Entry ? input.InvoiceId : null,
            FreeType = kind == StockMovementKind.Entry && input.InvoiceId is null ? input.FreeType : null,
            FreeSupplierId = kind == StockMovementKind.Entry && input.InvoiceId is null ? input.FreeSupplierId : null,
            Reference = (kind == StockMovementKind.Exit || input.InvoiceId is null) && reference.Length > 0 ? reference : null,
            InvoiceQuantity = kind == StockMovementKind.Entry && input.InvoiceId is not null ? input.InvoiceQuantity : null,
            ReturnOfMovementId = kind == StockMovementKind.Entry && input.InvoiceId is null && input.FreeType == FreeEntryType.FromBeneficiary ? input.ReturnOfMovementId : null,
            OverStockCause = kind == StockMovementKind.Exit && input.OverStockCause is { } cause && Enum.IsDefined(cause) ? cause : null,
            // An exit is tied to a component only with a project; an entry may be tied to a component of any project (the offer it supplies).
            ProjectComponentId = kind == StockMovementKind.Exit ? (projectId is not null ? input.ProjectComponentId : null) : input.ProjectComponentId,
            OutsideOffer = kind == StockMovementKind.Exit && projectId is not null && input.OutsideOffer,
            ReservationAck = kind == StockMovementKind.Exit && input.ReservationAck,
            ReduceReservationProjectId = kind == StockMovementKind.Exit ? input.ReduceReservationProjectId : null,
            ReduceReservationQuantity = kind == StockMovementKind.Exit ? input.ReduceReservationQuantity : null,
            ReduceReservationReason = kind == StockMovementKind.Exit ? (input.ReduceReservationReason ?? "").Trim() : "",
            DuplicateReason = duplicateReason
        };
    }
    public static void CheckCurrent(StockMovement? current, StockMovement original)
    {
        if (current is null || current.Version != original.Version) throw new StockMovementOperationException(StaleMessage);
        if (current.IsVoided) throw new StockMovementOperationException(MovementVoidedMessage);
    }

    public static string Target(string productCode) => productCode;

    public static string AuditIdentification(StockMovement movement, string productCode)
    {
        var values = new List<(string Field, string Value)>
        {
            (ProductCode.Label, productCode), ("Tip", KindLabel(movement.Kind)), ("Cantitate", movement.Quantity.ToString(CultureInfo.InvariantCulture)),
            ("Data", DisplayDate(movement.Date)), ("Descriere", movement.Description)
        };
        if (movement.Destination is { } destination) values.Add(("Destinație", DestinationLabel(destination)));
        if (movement.BeneficiaryName is not null) values.Add(("Beneficiar", movement.BeneficiaryName));
        if (movement.ProjectName is not null) values.Add(("Proiect", movement.ProjectName));
        if (movement.VehiclePlate is not null) values.Add(("Vehicul", movement.VehiclePlate));
        if (movement.FreeType is { } freeType) values.Add(("Tip intrare", FreeTypeLabel(freeType)));
        if (movement.FreeSupplierName is not null) values.Add(("Furnizor", movement.FreeSupplierName));
        if (movement.Reference is not null) values.Add(("Referință", movement.Reference));
        if (movement.OverStockCause is { } overCause) values.Add(("Cauza peste stoc", CauseLabel(overCause)));
        if (movement.OperationId is { } operation) values.Add(("Operație", $"#{operation}"));
        if (movement.ReturnOfMovementId is { } returned) values.Add(("Retur al ieșirii", $"#{returned}"));
        if (movement.InvoiceNumber is not null) values.Add(("Factură", movement.SupplierName is null ? movement.InvoiceNumber : $"{movement.InvoiceNumber} · {movement.SupplierName}"));
        if (movement.Kind == StockMovementKind.Exit) values.Add(("Sursă", SourceLabel(movement)));
        return AuditDetails.Identification(values.ToArray());
    }

    public static AuditChange[] AuditChanges(StockMovement before, StockMovement after) =>
    [
        new("Data", DisplayDate(before.Date), DisplayDate(after.Date)),
        new("Cantitate", before.Quantity.ToString(CultureInfo.InvariantCulture), after.Quantity.ToString(CultureInfo.InvariantCulture)),
        new("Descriere", before.Description, after.Description),
        new("Destinație", before.Destination is { } b ? DestinationLabel(b) : "—", after.Destination is { } a ? DestinationLabel(a) : "—"),
        new("Beneficiar", before.BeneficiaryName ?? "—", after.BeneficiaryName ?? "—"),
        new("Proiect", before.ProjectName ?? "—", after.ProjectName ?? "—"),
        new("Vehicul", before.VehiclePlate ?? "—", after.VehiclePlate ?? "—"),
        new("Referință", before.Reference ?? "—", after.Reference ?? "—"),
        new("Cauza peste stoc", before.OverStockCause is { } bc ? CauseLabel(bc) : "—", after.OverStockCause is { } ac ? CauseLabel(ac) : "—"),
        new("Sursă", before.Kind == StockMovementKind.Exit ? SourceLabel(before) : "—", after.Kind == StockMovementKind.Exit ? SourceLabel(after) : "—")
    ];

    public static string SourceLabel(StockMovement movement) =>
        movement.SourceVehiclePlate is { } plate ? $"Mașina {plate}" : "Depozit";

    public static bool HasChanges(StockMovement before, StockMovement after) =>
        AuditChanges(before, after).Any(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal));

    // Text kept in the history: changed values, followed by the stock correction.
    public static string HistorySummary(StockMovement before, StockMovement after, int correction)
    {
        var changes = AuditDetails.Changes(AuditChanges(before, after));
        var sign = correction > 0 ? "+" : string.Empty;
        return $"{changes}; Corecție stoc: {sign}{correction}";
    }
}

// The invoice pickup looks for a free entry the invoice line may already be in stock as: same product, same quantity, entered within
// 30 days of the invoice, the entries of the same supplier first. An entry is proposed once.
public static class FreeEntryMatching
{
    public const int MaxDaysApart = 30;

    public static FreeEntry? Find(IEnumerable<FreeEntry> candidates, int productId, int quantity, DateOnly invoiceDate, int? supplierId, ISet<int>? used = null) =>
        candidates.Where(entry => entry.ProductId == productId && entry.Quantity == quantity && used?.Contains(entry.MovementId) != true
                                  && Math.Abs(entry.Date.DayNumber - invoiceDate.DayNumber) <= MaxDaysApart)
            .OrderByDescending(entry => supplierId is not null && entry.SupplierId == supplierId)
            .ThenBy(entry => Math.Abs(entry.Date.DayNumber - invoiceDate.DayNumber)).ThenBy(entry => entry.MovementId)
            .FirstOrDefault();
}

public static class StockMovementNavigation
{
    public static string ProductUrl(int productId) => $"/produse/{productId}/miscari";
    public static string MovementUrl(int movementId) => $"/miscari/{movementId}";
}
