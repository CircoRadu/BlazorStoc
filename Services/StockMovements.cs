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
public sealed record VehicleTransferLine(int ProductId, int Quantity);
// Moves products out of a vehicle: into another vehicle, or back into the warehouse when TargetVehicleId is null.
// Lines null means every product the vehicle holds at the moment of the operation, with its whole quantity.
public sealed record VehicleTransfer(int SourceVehicleId, int? TargetVehicleId, IReadOnlyList<VehicleTransferLine>? Lines = null);

public sealed record StockMovement(int Id, int ProductId, StockMovementKind Kind, int Quantity, DateOnly Date,
    string Description, int? BeneficiaryId, string? BeneficiaryName, int? ProjectId, string? ProjectName,
    string Operator, long Version, DateTime CreatedUtc, DateTime UpdatedUtc, bool Modified = false,
    ExitDestination? Destination = null, int? VehicleId = null, string? VehiclePlate = null,
    int? SourceVehicleId = null, string? SourceVehiclePlate = null)
{
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
    public string Reason { get; set; } = "";

    // An exit recorded before destinations existed and holding a beneficiary is shown as an exit to a beneficiary;
    // one without any relation has no destination, so editing it requires choosing one.
    public static StockMovementInput From(StockMovement movement) => new()
    {
        Kind = movement.Kind, Date = movement.Date, Quantity = movement.Quantity, Description = movement.Description,
        BeneficiaryId = movement.BeneficiaryId, ProjectId = movement.ProjectId,
        Destination = movement.Kind == StockMovementKind.Exit
            ? movement.Destination ?? (movement.BeneficiaryId is not null ? ExitDestination.Beneficiary : null)
            : null,
        VehicleId = movement.VehicleId, SourceVehicleId = movement.SourceVehicleId
    };
}

public sealed record StockMovementQuery(StockMovementKind? Kind = null, bool Descending = false, int Page = 1, int PageSize = 10);
// Stock is the total; InVehicles is the part of it held by vehicles (the warehouse holds Stock - InVehicles).
public sealed record StockMovementPage(IReadOnlyList<StockMovement> Items, int TotalCount, int Stock, bool AnyModified, int InVehicles = 0);
public sealed record StockMovementResult(StockMovement Movement, int Stock);
public sealed record StockMovementHistoryEntry(int Id, int MovementId, string Actor, DateTime TimestampUtc, string Changes,
    int StockCorrection, string Reason);
// Contract consumed by the project "Echipamente" page (TODO Task 2 / Subtask 2.4).
public sealed record ProjectStockMovement(int Id, int ProductId, string ProductCode, int Quantity, DateOnly Date, string Operator);

public sealed class StockMovementOperationException(string message) : Exception(message);

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

    public const string DestinationRequiredMessage = "Alege destinația ieșirii.";
    public const string TransferBetweenVehiclesMessage =
        "Transferul dintr-o mașină în alta nu se face din formularul de ieșire; se face din pagina vehiculului.";

    public static int Effect(StockMovementKind kind, ExitDestination? destination, int quantity) =>
        kind == StockMovementKind.Entry ? quantity : destination is ExitDestination.Vehicle or ExitDestination.WarehouseReturn ? 0 : -quantity;
    public static int Effect(StockMovementKind kind, int quantity) => Effect(kind, null, quantity);
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
        if (input.Date is not { } date || date < EarliestDate) errors.Add("Alege o dată validă pentru mișcare.");
        else if (date > (today ?? Today)) errors.Add(FutureDateMessage);
        if (input.Quantity is not { } quantity || quantity < 1) errors.Add("Introdu o cantitate întreagă mai mare decât zero.");
        else if (quantity > MaxQuantity) errors.Add($"Cantitatea poate fi cel mult {MaxQuantity:N0}.");
        if (description.Length == 0) errors.Add(DescriptionRequiredMessage);
        else if (description.Length > MaxDescriptionLength) errors.Add($"Descrierea poate avea cel mult {MaxDescriptionLength} de caractere.");
        var beneficiaryId = input.BeneficiaryId;
        var projectId = input.ProjectId;
        var destination = input.Destination;
        var vehicleId = input.VehicleId;
        var sourceVehicleId = input.SourceVehicleId;
        if (kind == StockMovementKind.Entry)
        {
            if (beneficiaryId is not null || projectId is not null)
                errors.Add("Beneficiarul și proiectul se pot alege numai la ieșire.");
            if (destination is not null || vehicleId is not null || sourceVehicleId is not null)
                errors.Add("Destinația și sursa se pot alege numai la ieșire.");
        }
        else
        {
            if (destination is not { } chosen || !Enum.IsDefined(chosen)) errors.Add(DestinationRequiredMessage);
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
        var reason = ChangeReasonRules.Normalize(input.Reason);
        if (isEdit && ChangeReasonRules.ValidationError(reason) is { } reasonError) errors.Add(reasonError);
        if (errors.Count > 0) throw new StockMovementOperationException(string.Join(" ", errors));
        return new StockMovementInput
        {
            Kind = kind, Date = input.Date, Quantity = input.Quantity, Description = description,
            BeneficiaryId = beneficiaryId, ProjectId = projectId, Reason = reason,
            Destination = kind == StockMovementKind.Exit ? destination : null,
            VehicleId = vehicleId, SourceVehicleId = sourceVehicleId
        };
    }

    public static void CheckCurrent(StockMovement? current, StockMovement original)
    {
        if (current is null || current.Version != original.Version) throw new StockMovementOperationException(StaleMessage);
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

public static class StockMovementNavigation
{
    public static string ProductUrl(int productId) => $"/produse/{productId}/miscari";
    public static string MovementUrl(int movementId) => $"/miscari/{movementId}";
}
