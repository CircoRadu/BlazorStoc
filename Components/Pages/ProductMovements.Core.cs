using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    private async Task RefreshHolderAsync()
    {
        try
        {
            var held = await Locks.GetAsync(Id, lifetime.Token);
            var other = held is not null && held.SessionId != Origin.Id.ToString() ? held : null;
            if (holder is not null && other is null) holderReleased = true;
            else if (other is not null) holderReleased = false;
            holder = other;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning("Product lock check failed ({ErrorType}).", ex.GetType().Name); }
    }

    private void OpenUnlock() { unlockError = null; unlocking = true; }
    private void CancelUnlock() { if (!unlockBusy) unlocking = false; }

    private async Task ForceUnlockAsync(string reason)
    {
        if (unlockBusy) return;
        unlockBusy = true; unlockError = null;
        try
        {
            await Locks.ForceReleaseAsync(Id, reason, lifetime.Token);
            unlocking = false;
            notice = "Blocarea a fost eliberată și înregistrată în jurnal.";
            await RefreshHolderAsync();
            holderReleased = false;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ProductLockException or AccessDeniedException) { unlockError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Forced unlock failed ({ErrorType}).", ex.GetType().Name); unlockError = "Deblocarea nu a putut fi efectuată. Reîncearcă."; }
        finally { unlockBusy = false; }
    }

    protected override void OnInitialized()
    {
        liveLocks = new LiveRefresh(ChangeFeed, Origin.Id, change => change.EntityType == ChangeEntities.ProductLock && change.EntityId == Id.ToString(),
            () => InvokeAsync(async () => { await RefreshHolderAsync(); StateHasChanged(); }), () => false,
            () => InvokeAsync(StateHasChanged), fallbackInterval: TimeSpan.FromSeconds(10));
        InitializeLive();
    }

    private void InitializeLive() =>
        live = new LiveRefresh(ChangeFeed, Origin.Id, change => change.EntityType == AuditEntities.Product && change.EntityId == Id.ToString(),
            () => InvokeAsync(RefreshSilentlyAsync),
            () => editing is not null || deleting is not null || historyFor is not null,
            () => InvokeAsync(StateHasChanged), fallbackInterval: TimeSpan.FromSeconds(60));

    private Task ReloadLiveAsync() => live?.ReloadPendingAsync() ?? Task.CompletedTask;

    // Automatic refresh: no spinner (the table stays on screen) and the page is redrawn only when something changed.
    private async Task RefreshSilentlyAsync()
    {
        if (loading || product is null || saving || editSaving || deleteBusy) return;
        try
        {
            var current = await Products.GetProductAsync(Id, lifetime.Token);
            if (current is null) { await LoadAsync(); StateHasChanged(); return; }
            var data = await Movements.GetPageAsync(Id, CurrentQuery(), lifetime.Token);
            var vehicles = canManage ? await Movements.GetVehicleStocksAsync(Id, lifetime.Token) : vehicleStocks;
            current = current with { Quantity = data.Stock };
            if (current == product && SamePage(data, pageData) && vehicles.SequenceEqual(vehicleStocks)) return;
            product = current; pageData = data; stock = data.Stock; inVehicles = data.InVehicles; vehicleStocks = vehicles;
            if (pageNumber > TotalPages) { pageNumber = TotalPages; await LoadPageAsync(); }
            StateHasChanged();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning("Movement page refresh failed ({ErrorType}).", ex.GetType().Name); }
    }

    private static bool SamePage(StockMovementPage a, StockMovementPage b) =>
        a.TotalCount == b.TotalCount && a.Stock == b.Stock && a.AnyModified == b.AnyModified && a.InVehicles == b.InVehicles
        && a.Items.SequenceEqual(b.Items)
        && SameSet(a.OverStockIds, b.OverStockIds)
        && (a.Suppliers ?? []).SequenceEqual(b.Suppliers ?? [])
        && (a.ExitBeneficiaries ?? []).SequenceEqual(b.ExitBeneficiaries ?? [])
        && (a.ExitVehicles ?? []).SequenceEqual(b.ExitVehicles ?? []);

    private static bool SameSet(IReadOnlyCollection<int>? a, IReadOnlyCollection<int>? b) =>
        (a ?? []).Count == (b ?? []).Count && (a ?? []).All(id => (b ?? []).Contains(id));

    protected override async Task OnParametersSetAsync()
    {
        if (product is not null && product.Id != Id) { stateApplied = false; dateFilter = null; textFilter = ""; product = null; filter = null; source = EntrySource.All; supplierFilter = null; destinationFilter = null; beneficiaryFilter = vehicleFilter = null; overStockOnly = false; pageNumber = 1; form = NewForm(); useProject = false; addSuggestion = null; }
        if (product is null) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        loading = true; error = null;
        try
        {
            var productTask = Products.GetProductAsync(Id, lifetime.Token);
            var accessTask = Access.HasModuleWriteAsync("stoc", lifetime.Token);
            await Task.WhenAll(productTask, accessTask);
            product = await productTask; canManage = await accessTask;
            stockIn = await Access.HasAsync("stoc.intrare", lifetime.Token); stockOut = await Access.HasAsync("stoc.iesire", lifetime.Token);
            stockModify = await Access.HasAsync("stoc.modificare", lifetime.Token); stockVoid = await Access.HasAsync("stoc.stornare", lifetime.Token);
            if (!stockIn && !stockVoid && stockOut) form.Kind = StockMovementKind.Exit;
            permEdit = await Access.HasAsync("produse.edit", lifetime.Token); permDelete = await Access.HasAsync("produse.delete", lifetime.Token);
            if (!stateApplied) { ApplyStateFromAddress(); stateApplied = true; }
            isAdministrator = await Access.IsAdministratorAsync(lifetime.Token);
            if (product is not null) { await LoadPageAsync(); await RefreshHolderAsync(); }
            await RestoreExitFormAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogError("Movement page loading failed ({ErrorType}).", ex.GetType().Name); error = "Pagina nu a putut fi încărcată. Verifică serverul și conexiunea la baza de date."; }
        finally { loading = false; }
    }

    // Coming back from "Adaugă beneficiar" / "Adaugă proiect" (?restaurare=1): puts the stored exit form back and selects
    // the beneficiary / project the user just created. Any other visit forgets a stored form.
    private async Task RestoreExitFormAsync()
    {
        if (restoreHandled) return;
        if (RestoreFlag != "1") { ExitDraft.Clear(); return; }
        restoreHandled = true;
        var snapshot = ExitDraft.Take(Id);
        if (product is null)
        {
            restoreMessage = "Formularul de ieșire nu a mai putut fi restaurat.";
            return;
        }
        if (snapshot is null || !canManage)
        {
            restoreMessage = "Formularul de ieșire nu a mai putut fi restaurat (sesiunea a fost reîncărcată sau au trecut mai mult de 30 de minute). Introdu din nou datele mișcării.";
            NavigateWithoutRestoreFlag();
            return;
        }
        form = snapshot.Form; useProject = snapshot.UseProject; addSuggestion = snapshot.Suggestion;
        await EnsureBeneficiariesAsync(); await EnsureVehiclesAsync();
        if (snapshot.NewBeneficiaryId is { } newBeneficiary)
        {
            // The list may have been cached before the beneficiary was created.
            beneficiaries = (await Beneficiaries.GetBeneficiariesAsync(lifetime.Token)).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            await SelectBeneficiaryAsync(form, newBeneficiary, false);
            useProject = false;
            restoreMessage = "Formularul de ieșire a fost restaurat; beneficiarul nou este selectat.";
        }
        else if (form.BeneficiaryId is { } chosen)
        {
            if (beneficiaries.All(b => b.Id != chosen)) form.BeneficiaryId = form.ProjectId = null;
            else
            {
                var keepProject = form.ProjectId;
                await SelectBeneficiaryAsync(form, chosen, false);
                if (snapshot.NewProjectId is { } newProject && projects.Any(p => p.Id == newProject)) { form.ProjectId = newProject; useProject = true; }
                else if (keepProject is { } previous && projects.Any(p => p.Id == previous)) form.ProjectId = previous;
            }
            restoreMessage = snapshot.NewProjectId is not null
                ? "Formularul de ieșire a fost restaurat; proiectul nou este selectat."
                : "Formularul de ieșire a fost restaurat.";
        }
        else restoreMessage = "Formularul de ieșire a fost restaurat.";
        addTracker?.Rebase();
        NavigateWithoutRestoreFlag();
    }

    // Drops ?restaurare=1 from the address (replace, so a refresh or Back does not restore again).
    private void NavigateWithoutRestoreFlag() => Navigation.NavigateTo($"/produse/{Id}/miscari", replace: true);

    private StockMovementQuery CurrentQuery() => new(filter, descending, pageNumber, pageSize, source, supplierFilter, overStockOnly,
        destinationFilter, beneficiaryFilter, vehicleFilter, textFilter, dateFilter);

    private async Task LoadPageAsync()
    {
        loading = true; error = null;
        try
        {
            pageData = await Movements.GetPageAsync(Id, CurrentQuery(), lifetime.Token);
            SyncState();
            stock = pageData.Stock; inVehicles = pageData.InVehicles;
            if (canManage) { vehicleStocks = await Movements.GetVehicleStocksAsync(Id, lifetime.Token); returnables = await Movements.GetReturnableExitsAsync(Id, lifetime.Token); }
            var pages = TotalPages;
            if (pageNumber > pages) { pageNumber = pages; pageData = await Movements.GetPageAsync(Id, CurrentQuery(), lifetime.Token); }
            if (product is not null) product = product with { Quantity = stock };
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (StockMovementOperationException ex) { error = ex.Message; }
        catch (Exception ex) { Logger.LogError("Movement list loading failed ({ErrorType}).", ex.GetType().Name); error = "Mișcările nu au putut fi încărcate. Verifică serverul și conexiunea la baza de date."; }
        finally { loading = false; }
    }

    // Read from the address once, when the page is opened for a product (later the page itself is the source of the state).
    private void ApplyStateFromAddress()
    {
        textFilter = (QueryText ?? "").Trim();
        filter = KindParameter == "intrari" ? StockMovementKind.Entry : KindParameter == "iesiri" ? StockMovementKind.Exit : null;
        source = SourceParameter == "factura" ? EntrySource.WithInvoice : SourceParameter == "libere" ? EntrySource.Free : EntrySource.All;
        supplierFilter = SupplierParameter; beneficiaryFilter = BeneficiaryParameter; vehicleFilter = VehicleParameter;
        destinationFilter = DestinationParameter is { } destination && Enum.IsDefined((ExitDestination)destination) ? (ExitDestination)destination : null;
        dateFilter = DateOnly.TryParseExact(DateParameter, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) ? day : null;
        overStockOnly = OverStockParameter == "1";
        if (supplierFilter is not null) { filter = StockMovementKind.Entry; if (source == EntrySource.All) source = EntrySource.WithInvoice; }
        if (source != EntrySource.All) filter = StockMovementKind.Entry;
        if (destinationFilter is not null || beneficiaryFilter is not null || vehicleFilter is not null || overStockOnly) filter = StockMovementKind.Exit;
        pageSize = PageSizeParameter is { } size && PageSizes.Contains(size) ? size : 10;
        pageNumber = Math.Max(1, PageParameter ?? 1);
    }

    private void SyncState()
    {
        var values = new Dictionary<string, object?>
        {
            ["q"] = string.IsNullOrWhiteSpace(textFilter) ? null : textFilter.Trim(),
            ["tip"] = filter == StockMovementKind.Entry ? "intrari" : filter == StockMovementKind.Exit ? "iesiri" : null,
            ["sursa"] = source == EntrySource.WithInvoice ? "factura" : source == EntrySource.Free ? "libere" : null,
            ["furnizor"] = supplierFilter, ["destinatie"] = destinationFilter is { } destination ? (int)destination : null,
            ["beneficiar"] = beneficiaryFilter, ["vehicul"] = vehicleFilter,
            ["data"] = dateFilter is { } day ? StockMovementRules.StorageDate(day) : null,
            ["peste-stoc"] = overStockOnly ? "1" : null,
            ["pe-pagina"] = pageSize == 10 ? null : pageSize, ["pagina"] = pageNumber <= 1 ? null : pageNumber
        };
        var address = Navigation.GetUriWithQueryParameters(values);
        if (address == Navigation.Uri || address == appliedAddress) return;
        appliedAddress = address;
        Navigation.NavigateTo(address, replace: true);
    }

    private void ImageKeyAsync(KeyboardEventArgs args) { if (args.Key == "Escape") imagePreview = false; }
    public void Dispose() { live?.Dispose(); liveLocks?.Dispose(); lifetime.Cancel(); lifetime.Dispose(); }
}
