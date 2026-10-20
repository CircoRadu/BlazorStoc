using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    private static string TransferText(StockMovement movement) => movement.Destination == ExitDestination.WarehouseReturn
        ? $"Restituire în depozit din mașina {movement.SourceVehiclePlate}."
        : $"Mutare din mașina {movement.SourceVehiclePlate} în mașina {movement.VehiclePlate}.";

    private async Task SetFilterAsync(StockMovementKind? kind, bool load = true)
    {
        filter = kind; pageNumber = 1;
        if (kind != StockMovementKind.Entry) { source = EntrySource.All; supplierFilter = null; }
        if (kind != StockMovementKind.Exit) { overStockOnly = false; destinationFilter = null; beneficiaryFilter = vehicleFilter = null; }
        if (load) await LoadPageAsync();
    }

    private async Task DestinationFilterChangedAsync(ChangeEventArgs e)
    {
        destinationFilter = int.TryParse(e.Value?.ToString(), out var id) && Enum.IsDefined((ExitDestination)id) ? (ExitDestination)id : null;
        if (destinationFilter is not null) filter = StockMovementKind.Exit;
        pageNumber = 1; await LoadPageAsync();
    }

    private async Task BeneficiaryFilterChangedAsync(int? id)
    {
        beneficiaryFilter = id;
        if (beneficiaryFilter is not null) filter = StockMovementKind.Exit;
        pageNumber = 1; await LoadPageAsync();
    }

    private async Task VehicleFilterChangedAsync(ChangeEventArgs e)
    {
        vehicleFilter = int.TryParse(e.Value?.ToString(), out var id) ? id : null;
        if (vehicleFilter is not null) filter = StockMovementKind.Exit;
        pageNumber = 1; await LoadPageAsync();
    }

    private async Task SetOverStockAsync(bool value) { overStockOnly = value; pageNumber = 1; await LoadPageAsync(); }

    private async Task SetSourceAsync(EntrySource value)
    {
        source = value; pageNumber = 1;
        if (value == EntrySource.Free) supplierFilter = null;
        await LoadPageAsync();
    }

    private async Task SupplierFilterChangedAsync(int? id)
    {
        supplierFilter = id;
        if (supplierFilter is not null) { filter = StockMovementKind.Entry; if (source != EntrySource.WithInvoice) source = EntrySource.WithInvoice; }
        pageNumber = 1; await LoadPageAsync();
    }

    private async Task TextChangedAsync()
    {
        textDebounce?.Cancel(); textDebounce?.Dispose();
        var debounce = textDebounce = new CancellationTokenSource();
        try { await Task.Delay(350, debounce.Token); }
        catch (TaskCanceledException) { return; }
        pageNumber = 1; await LoadPageAsync();
    }

    private sealed record FilterChip(string Text, EventCallback Remove);

    private async Task ClearFiltersAsync()
    {
        textFilter = ""; filter = null; source = EntrySource.All; supplierFilter = null; overStockOnly = false; destinationFilter = null; beneficiaryFilter = vehicleFilter = null;
        dateFilter = null; pageNumber = 1; await LoadPageAsync();
    }

    private async Task KindChosenAsync(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? "";
        pageNumber = 1;
        if (value == "over") { await SetFilterAsync(StockMovementKind.Exit, load: false); overStockOnly = true; await LoadPageAsync(); }
        else await SetFilterAsync(value == "in" ? StockMovementKind.Entry : value == "out" ? StockMovementKind.Exit : null);
    }

    private async Task SourceChosenAsync(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? "";
        source = value == "invoice" ? EntrySource.WithInvoice : value == "free" ? EntrySource.Free : EntrySource.All;
        if (source != EntrySource.WithInvoice) supplierFilter = null;
        if (source != EntrySource.All) filter = StockMovementKind.Entry;
        pageNumber = 1; await LoadPageAsync();
    }

    private async Task FilterByDateAsync(DateOnly date) { dateFilter = date; pageNumber = 1; await LoadPageAsync(); }
    private async Task FilterByKindAsync(StockMovementKind kind) => await SetFilterAsync(kind);
    private async Task FilterBySupplierAsync(int id) { filter = StockMovementKind.Entry; source = EntrySource.WithInvoice; supplierFilter = id; pageNumber = 1; await LoadPageAsync(); }
    private async Task FilterByBeneficiaryAsync(int id) { filter = StockMovementKind.Exit; beneficiaryFilter = id; pageNumber = 1; await LoadPageAsync(); }
    private async Task FilterByDestinationAsync(ExitDestination destination) { filter = StockMovementKind.Exit; destinationFilter = destination; pageNumber = 1; await LoadPageAsync(); }
    private async Task FilterByVehicleAsync(int id) { filter = StockMovementKind.Exit; vehicleFilter = id; pageNumber = 1; await LoadPageAsync(); }
    private async Task ToggleSortAsync() { descending = !descending; pageNumber = 1; await LoadPageAsync(); }
    private async Task PreviousPageAsync() { if (pageNumber > 1) { pageNumber--; await LoadPageAsync(); } }
    private async Task NextPageAsync() { if (pageNumber < TotalPages) { pageNumber++; await LoadPageAsync(); } }
    private async Task PageSizeChangedAsync(ChangeEventArgs args) { if (int.TryParse(args.Value?.ToString(), out var size) && PageSizes.Contains(size)) { pageSize = size; pageNumber = 1; await LoadPageAsync(); } }
}
