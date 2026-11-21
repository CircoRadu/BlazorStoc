using Microsoft.AspNetCore.Components.Web;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

// The "view" window of a movement (the eye icon on a row): every parameter of the movement, the edit button of the window, and the history of its changes
// in a collapsible section (closed by default, with the number of changes as a badge).
public partial class ProductMovements
{
    private StockMovement? viewing;
    private IReadOnlyList<StockMovementHistoryEntry> viewHistory = Array.Empty<StockMovementHistoryEntry>();
    private string? viewHistoryError;
    private bool viewHistoryOpen, viewHistoryLoading;

    private bool CanEditViewing => viewing is { IsVoided: false } && stockModify && !Blocked;

    private async Task OpenViewAsync(StockMovement movement)
    {
        viewing = movement; viewHistory = Array.Empty<StockMovementHistoryEntry>(); viewHistoryError = null; viewHistoryOpen = false; viewHistoryLoading = true;
        try { viewHistory = await Movements.GetHistoryAsync(movement.Id, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogError("Movement history loading failed ({ErrorType}).", ex.GetType().Name); viewHistoryError = "Istoricul nu a putut fi încărcat."; }
        finally { viewHistoryLoading = false; }
    }

    private void CloseView() => viewing = null;
    private void ViewKey(KeyboardEventArgs args) { if (args.Key == "Escape") CloseView(); }

    private async Task EditFromViewAsync()
    {
        if (viewing is not { } movement || !CanEditViewing) return;
        viewing = null;
        await OpenEditAsync(movement);
    }

    // The parameters of a movement as label/value pairs, in the order they are read; only those the movement has.
    private IEnumerable<(string Label, string Value)> ViewFacts(StockMovement m)
    {
        yield return ("Data", StockMovementRules.DisplayDate(m.Date));
        yield return ("Tip", StockMovementRules.KindLabel(m.Kind));
        yield return ("Număr bucăți", m.Quantity.ToString());
        yield return ("Descriere", m.Description);
        if (m.InvoiceNumber is not null) yield return ("Factură", m.InvoiceNumber);
        if (m.SupplierName is not null) yield return ("Furnizor", m.SupplierName);
        if (m.Kind == StockMovementKind.Entry && m.InvoiceNumber is null) yield return ("Proveniență", StockMovementRules.FreeEntryLabel(m) + (m.FreeSupplierName is not null ? $" · {m.FreeSupplierName}" : ""));
        if (m.Destination is { } destination) yield return ("Destinație", StockMovementRules.DestinationLabel(destination));
        if (m.BeneficiaryName is not null) yield return ("Beneficiar", m.BeneficiaryName);
        if (m.ProjectName is not null) yield return ("Proiect", m.ProjectName);
        if (m.VehiclePlate is not null) yield return ("Vehicul (în)", m.VehiclePlate);
        if (m.SourceVehiclePlate is not null) yield return ("Vehicul (din)", m.SourceVehiclePlate);
        if (m.Reference is not null) yield return ("Referință", m.Reference);
        if (m.OverStockCause is { } cause) yield return ("Cauza ieșirii peste stoc", StockMovementRules.CauseLabel(cause));
        if (pageData.OverStockIds?.Contains(m.Id) == true) yield return ("Stoc", "Ieșirea a depășit stocul din depozit (sau din mașină) la data ei");
        if (m.OperationId is { } operation) yield return ("Operațiune", $"#{operation}");
        if (m.ReturnOfMovementId is { } returnOf) yield return ("Retur al ieșirii", $"#{returnOf}");
        yield return ("Creat de", m.Operator);
        yield return ("Creat la", m.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        if (m.Modified)
        {
            yield return ("Modificat de", m.LastOperator is { Length: > 0 } ? m.LastOperator : m.Operator);
            yield return ("Modificat la", m.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
        }
        if (m.IsVoided)
        {
            yield return ("Stornat de", m.VoidedBy ?? "—");
            yield return ("Motiv storno", m.VoidReason ?? "—");
        }
    }
}
