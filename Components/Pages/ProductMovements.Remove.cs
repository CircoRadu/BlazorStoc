using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    private void RequestVoid(StockMovement movement) { notice = null; voidError = ""; voidReason = ""; voiding = movement; }
    private void CancelVoid() { if (!voidBusy) voiding = null; }

    private async Task VoidAsync()
    {
        if (voidBusy || voiding?.OperationId is not { } operationId) return;
        if (ChangeReasonRules.ValidationError(voidReason) is { } problem) { voidError = problem; return; }
        voidBusy = true; voidError = "";
        try
        {
            await Movements.VoidExitOperationAsync(operationId, voidReason, lifetime.Token);
            voiding = null; notice = $"Operația #{operationId} a fost stornată.";
            await LoadPageAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is StockMovementOperationException or AccessDeniedException) { voidError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Operation storno failed ({ErrorType}).", ex.GetType().Name); voidError = "Stornarea nu a putut fi salvată. Actualizează pagina și reîncearcă."; }
        finally { voidBusy = false; }
    }

    private void RequestDelete(StockMovement movement) { notice = null; deleteError = null; deleting = movement; }
    private Task CancelDelete() { deleting = null; deleteError = null; return Task.CompletedTask; }

    private async Task DeleteAsync(string reason)
    {
        if (deleteBusy || deleting is null) return;
        deleteBusy = true; deleteError = null;
        try
        {
            stock = await Movements.DeleteAsync(deleting, reason, lifetime.Token);
            deleting = null; notice = $"Mișcarea a fost mutată în arhivă. Stoc curent: {stock} buc.";
            await LoadPageAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is StockMovementOperationException or AccessDeniedException or ArchiveContractException) { deleteError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Movement deletion failed ({ErrorType}).", ex.GetType().Name); deleteError = "Ștergerea nu a putut fi confirmată. Actualizează pagina înainte să reîncerci."; }
        finally { deleteBusy = false; }
    }

    private async Task OpenHistoryAsync(StockMovement movement)
    {
        if (!movement.Modified) return;
        historyFor = movement; history = Array.Empty<StockMovementHistoryEntry>(); historyError = null;
        try { history = await Movements.GetHistoryAsync(movement.Id, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogError("Movement history loading failed ({ErrorType}).", ex.GetType().Name); historyError = "Istoricul nu a putut fi încărcat."; }
    }

    private void CloseHistory() => historyFor = null;
    private void HistoryKeyAsync(KeyboardEventArgs args) { if (args.Key == "Escape") CloseHistory(); }
}
