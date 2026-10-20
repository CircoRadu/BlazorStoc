using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    [Parameter] public int Id { get; set; }

    // Set by the add-beneficiary / add-project pages when they send the user back to a half-filled exit form.
    [SupplyParameterFromQuery(Name = "restaurare")] public string? RestoreFlag { get; set; }

    private string? restoreMessage;
    private bool restoreHandled, stateApplied;
    private static readonly int[] PageSizes = [10, 20, 50, 0];
    private Product? product;
    private StockMovementPage pageData = new([], 0, 0, false);
    private int stock, inVehicles;
    private bool loading = true, canManage, stockIn, stockOut, stockModify, stockVoid, permEdit, permDelete, saving, editSaving, deleteBusy, imagePreview;
    private string? error, notice, formError, editError, deleteError, historyError;
    private StockMovementKind? filter;
    private EntrySource source = EntrySource.All;
    private int? supplierFilter;
    private bool descending;
    private int pageNumber = 1, pageSize = 10;
    private StockMovementInput form = NewForm();
    private EntryOriginPicker? originPicker;
    private readonly NegativeStockResolution negResolution = new();
    private bool overStockOnly;
    private ExitDestination? destinationFilter;
    private int? beneficiaryFilter, vehicleFilter;

    private static readonly ExitDestination[] FilterDestinations =
        [ExitDestination.Beneficiary, ExitDestination.Vehicle, ExitDestination.GenericSale, ExitDestination.StockCorrection, ExitDestination.WarehouseReturn];

    private DuplicateExitWarningException? exitWarning;
    private ReservationWarningException? reservationWarning;
    private int reservationVersion, suggestionTrigger, entryChoiceKey;
    private readonly ReservationChoice reservationChoice = new();

    // The exit being added goes over the warehouse stock, or over what the source vehicle holds (a transfer or return cannot, it is refused).
    private bool FormOverStock => form.Kind == StockMovementKind.Exit && form.Quantity is { } quantity
        && (form.SourceVehicleId is { } vehicle
            ? form.Destination is not (ExitDestination.Vehicle or ExitDestination.WarehouseReturn) && quantity > (vehicleStocks.FirstOrDefault(item => item.VehicleId == vehicle)?.Quantity ?? 0)
            : quantity > Math.Max(0, WarehouseStock));

    private int WarehouseStock => StockMovementRules.WarehouseStock(stock, inVehicles);
    private InvoiceEntryWarningException? invoiceWarning;
    private string duplicateReason = "";
    private IReadOnlyList<StockMovement>? linking;
    private bool linkDetach;
    private bool useProject;
    private IReadOnlyList<Beneficiary> beneficiaries = Array.Empty<Beneficiary>();
    private IReadOnlyList<Vehicle> vehicles = Array.Empty<Vehicle>();
    private IReadOnlyList<VehicleStock> vehicleStocks = Array.Empty<VehicleStock>();
    private bool vehiclesLoaded;
    private ExitDestinationPicker? addPicker, editPicker;

    // The description proposed by the form (destination vehicle, stock correction); replaced only while untouched.
    private string? addSuggestion, editSuggestion;

    private IReadOnlyList<AuditChange>? pendingEditChanges;
    private IReadOnlyList<Project> projects = Array.Empty<Project>(), editProjects = Array.Empty<Project>();
    private StockMovement? editing, deleting, historyFor, voiding;
    private string voidReason = "", voidError = "";
    private bool voidBusy;
    private IReadOnlyList<ReturnableExit> returnables = [];
    private StockMovementInput editForm = new();
    private bool editUseProject;

    // The reason of an edit: the summary of the changes made in the form (one on each line, following the form) or a text the user writes.
    private bool editReasonAuto = true;

    private string editCustomReason = "";

    private string EditAutoSummary
    {
        get
        {
            if (editing is null) return "";
            string? BeneficiaryName(int? id) => editForm.Destination == ExitDestination.Beneficiary ? beneficiaries.FirstOrDefault(item => item.Id == id)?.Name : null;
            string? ProjectName(int? id) => editUseProject ? editProjects.FirstOrDefault(item => item.Id == id)?.Name : null;
            string? VehiclePlate(int? id) => vehicles.FirstOrDefault(item => item.Id == id)?.PlateNumber;
            return ChangeReasonSummary.Build(
            [
                new("Data", StockMovementRules.DisplayDate(editing.Date), editForm.Date is { } date ? StockMovementRules.DisplayDate(date) : "—"),
                new("Cantitate", editing.Quantity.ToString(), editForm.Quantity?.ToString() ?? "—"),
                new("Descriere", editing.Description, editForm.Description),
                new("Destinație", editing.Destination is { } oldDestination ? StockMovementRules.DestinationLabel(oldDestination) : "—",
                    editForm.Destination is { } newDestination ? StockMovementRules.DestinationLabel(newDestination) : "—"),
                new("Beneficiar", editing.BeneficiaryName ?? "—", BeneficiaryName(editForm.BeneficiaryId) ?? "—"),
                new("Proiect", editing.ProjectName ?? "—", ProjectName(editForm.ProjectId) ?? "—"),
                new("Vehicul", editing.VehiclePlate ?? "—", VehiclePlate(editForm.VehicleId) ?? "—"),
                new("Sursă", StockMovementRules.SourceLabel(editing), editForm.SourceVehicleId is null ? "Depozit" : $"Mașina {VehiclePlate(editForm.SourceVehicleId)}")
            ]);
        }
    }

    private IReadOnlyList<StockMovementHistoryEntry> history = Array.Empty<StockMovementHistoryEntry>();
    private readonly CancellationTokenSource lifetime = new();
    private string ImageUrl => $"/media/products/{Id}?v={product?.Version}";

    // Back to the catalog scoped to the product's category and subcategory, as the catalog's own links do.
    // The page the edit/delete actions start from; closing them brings the user back here.
    private string CurrentPath => new Uri(Navigation.Uri).AbsolutePath;

    private string CatalogUrl => product is null ? "/produse"
        : $"/produse?categorie={Uri.EscapeDataString(product.Category)}&subcategorie={Uri.EscapeDataString(product.Subcategory)}";

    private int TotalPages => pageSize <= 0 ? 1 : Math.Max(1, (pageData.TotalCount + pageSize - 1) / pageSize);
    private string StockClass => stock < 0 ? "negative" : stock == 0 ? "zero" : "positive";

    private string CorrectionText
    {
        get
        {
            if (editing is null || editForm.Quantity is not { } quantity) return "—";
            var correction = StockMovementRules.Effect(editing.Kind, editForm.Destination, quantity) - editing.Effect;
            return correction > 0 ? $"+{correction}" : correction.ToString();
        }
    }

    private string DeleteDescription => deleting is null ? string.Empty
        : deleting.Effect == 0
            ? "Stocul total al produsului nu se schimbă (produsul mutat în vehicul nu mai apare acolo). Mișcarea și istoricul ei vor fi mutate în arhivă."
            : $"Stocul produsului va fi corectat cu {(deleting.Effect <= 0 ? "+" : "-")}{Math.Abs(deleting.Effect)} buc. Mișcarea și istoricul ei vor fi mutate în arhivă.";

    // Another session or application changed this product or its movements: refresh in place (Task 8).
    private LiveRefresh? live, liveLocks;

    // Edit lock held by another session (Task 9): shown read-only with who and since when; refreshed on the lock's
    // events and every 10 s, because a lease also ends silently when it expires.
    private ProductLock? holder;

    private bool isAdministrator, holderReleased, unlocking, unlockBusy;
    private string? unlockError;
    private string ReturnPath => $"/produse/{Id}/miscari?restaurare=1";

    // The filters of the movement list: a text field, and the column labels (kind, invoice / supplier, beneficiary, vehicle) that open a panel of choices.
    private string textFilter = "";

    private CancellationTokenSource? textDebounce;

    private IReadOnlyList<FilterChip> ActiveFilters
    {
        get
        {
            var chips = new List<FilterChip>();
            FilterChip Chip(string text, Func<Task> remove) => new(text, EventCallback.Factory.Create(this, async () => { pageNumber = 1; await remove(); }));
            if (!string.IsNullOrWhiteSpace(textFilter)) chips.Add(Chip($"Text: {textFilter.Trim()}", () => { textFilter = ""; return LoadPageAsync(); }));
            if (dateFilter is { } day) chips.Add(Chip($"data: {StockMovementRules.DisplayDate(day)}", () => { dateFilter = null; return LoadPageAsync(); }));
            if (filter is not null) chips.Add(Chip(filter == StockMovementKind.Entry ? "Intrări" : "Ieșiri", () => SetFilterAsync(null)));
            if (overStockOnly) chips.Add(Chip("Peste stoc", () => { overStockOnly = false; return LoadPageAsync(); }));
            if (source != EntrySource.All) chips.Add(Chip(source == EntrySource.Free ? "Intrări libere" : "Cu factură", () => { source = EntrySource.All; supplierFilter = null; return LoadPageAsync(); }));
            if (supplierFilter is { } supplierId) chips.Add(Chip($"Furnizor: {pageData.Suppliers?.FirstOrDefault(item => item.Id == supplierId)?.Name ?? supplierId.ToString()}", () => { supplierFilter = null; return LoadPageAsync(); }));
            if (destinationFilter is { } destination) chips.Add(Chip($"Destinație: {StockMovementRules.DestinationLabel(destination)}", () => { destinationFilter = null; return LoadPageAsync(); }));
            if (beneficiaryFilter is { } beneficiaryId) chips.Add(Chip($"Beneficiar: {pageData.ExitBeneficiaries?.FirstOrDefault(item => item.Id == beneficiaryId)?.Name ?? beneficiaryId.ToString()}", () => { beneficiaryFilter = null; return LoadPageAsync(); }));
            if (vehicleFilter is { } vehicleId) chips.Add(Chip($"Vehicul: {pageData.ExitVehicles?.FirstOrDefault(item => item.Id == vehicleId)?.Name ?? vehicleId.ToString()}", () => { vehicleFilter = null; return LoadPageAsync(); }));
            return chips;
        }
    }

    // Filters as in the activity journal: a search box and selects, the active filters as chips with "Șterge filtrele", the values of the table as filters
    // (date, type, supplier, beneficiary, destination, vehicle), and the state in the address (so Back restores it).
    private DateOnly? dateFilter;

    private string? appliedAddress;
    [SupplyParameterFromQuery(Name = "q")] public string? QueryText { get; set; }
    [SupplyParameterFromQuery(Name = "tip")] public string? KindParameter { get; set; }
    [SupplyParameterFromQuery(Name = "sursa")] public string? SourceParameter { get; set; }
    [SupplyParameterFromQuery(Name = "furnizor")] public int? SupplierParameter { get; set; }
    [SupplyParameterFromQuery(Name = "destinatie")] public int? DestinationParameter { get; set; }
    [SupplyParameterFromQuery(Name = "beneficiar")] public int? BeneficiaryParameter { get; set; }
    [SupplyParameterFromQuery(Name = "vehicul")] public int? VehicleParameter { get; set; }
    [SupplyParameterFromQuery(Name = "data")] public string? DateParameter { get; set; }
    [SupplyParameterFromQuery(Name = "peste-stoc")] public string? OverStockParameter { get; set; }
    [SupplyParameterFromQuery(Name = "pe-pagina")] public int? PageSizeParameter { get; set; }
    [SupplyParameterFromQuery(Name = "pagina")] public int? PageParameter { get; set; }

    // Task 1: leaving the add form or the edit dialog with modified values asks for confirmation first.
    private UnsavedChangesTracker? addTracker, editTracker;
}
