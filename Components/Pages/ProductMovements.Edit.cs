using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    private async Task OpenEditAsync(StockMovement movement)
    {
        notice = null; editError = null; editing = movement; editForm = StockMovementInput.From(movement); editReasonAuto = true; editCustomReason = "";
        editUseProject = movement.ProjectId is not null; editProjects = Array.Empty<Project>(); editSuggestion = null;
        if (movement.Kind == StockMovementKind.Exit)
        {
            await EnsureBeneficiariesAsync();
            await EnsureVehiclesAsync();
            if (movement.BeneficiaryId is { } beneficiaryId)
            {
                try { editProjects = await Projects.GetForBeneficiaryAsync(beneficiaryId, lifetime.Token); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception ex) { Logger.LogError("Project list loading failed ({ErrorType}).", ex.GetType().Name); }
            }
        }
    }

    // Editing an existing movement needs an explicit confirmation of the changed fields before anything is saved.
    private Task SaveEditAsync()
    {
        if (editSaving || editing is null) return Task.CompletedTask;
        editError = editing.Kind == StockMovementKind.Exit && !editing.IsVehicleTransfer ? editPicker?.ValidationError() : null;
        if (editError is not null) return Task.CompletedTask;
        if (editReasonAuto && EditAutoSummary.Length == 0) { editError = ChangeReasonSummary.NoChangesMessage; return Task.CompletedTask; }
        editForm.Reason = ChangeReasonSummary.Chosen(editReasonAuto, EditAutoSummary, editCustomReason);
        try
        {
            var value = StockMovementRules.Validated(editForm, editing.Kind, true, allowVehicleTransfer: editing.IsVehicleTransfer);
            string? BeneficiaryName(int? id) => value.Destination == ExitDestination.Beneficiary ? beneficiaries.FirstOrDefault(item => item.Id == id)?.Name : null;
            string? ProjectName(int? id) => editUseProject ? editProjects.FirstOrDefault(item => item.Id == id)?.Name : null;
            string? VehiclePlate(int? id) => vehicles.FirstOrDefault(item => item.Id == id)?.PlateNumber;
            string SourceText(int? id) => id is null ? "Depozit" : $"Mașina {VehiclePlate(id)}";
            pendingEditChanges = SaveSummary.Changed(
                new("Data", StockMovementRules.DisplayDate(editing.Date), StockMovementRules.DisplayDate(value.Date!.Value)),
                new("Cantitate", editing.Quantity.ToString(), value.Quantity!.Value.ToString()),
                new("Descriere", editing.Description, value.Description),
                new("Destinație", editing.Destination is { } oldDestination ? StockMovementRules.DestinationLabel(oldDestination) : "—",
                    value.Destination is { } newDestination ? StockMovementRules.DestinationLabel(newDestination) : "—"),
                new("Beneficiar", editing.BeneficiaryName ?? "—", BeneficiaryName(value.BeneficiaryId) ?? "—"),
                new("Proiect", editing.ProjectName ?? "—", ProjectName(value.ProjectId) ?? "—"),
                new("Vehicul", editing.VehiclePlate ?? "—", VehiclePlate(value.VehicleId) ?? "—"),
                new("Sursă", StockMovementRules.SourceLabel(editing), SourceText(value.SourceVehicleId)));
        }
        catch (StockMovementOperationException exception) { editError = exception.Message; }
        return Task.CompletedTask;
    }

    private Task ConfirmEditAsync() { pendingEditChanges = null; return PersistEditAsync(); }
    private Task CancelEditConfirmation() { pendingEditChanges = null; return Task.CompletedTask; }

    private async Task PersistEditAsync()
    {
        if (editSaving || editing is null) return;
        editSaving = true;
        try
        {
            var result = await Movements.UpdateAsync(editing, editForm, lifetime.Token);
            editing = null; stock = result.Stock; notice = $"Modificarea a fost salvată. Stoc curent: {stock} buc.";
            await LoadPageAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is StockMovementOperationException or AccessDeniedException) { editError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Movement update failed ({ErrorType}).", ex.GetType().Name); editError = "Modificarea nu a putut fi salvată. Actualizează pagina și reîncearcă."; }
        finally { editSaving = false; }
    }

    private void CloseEdit() { if (!editSaving) { editing = null; pendingEditChanges = null; } }
    private void EditKeyAsync(KeyboardEventArgs args) { if (args.Key == "Escape" && pendingEditChanges is null) RequestCloseEditAsync().FireAndForget(Logger, "close edit"); }
    private string EditSnapshot() => FormSnapshot.Of(editForm, editUseProject);
    private async Task DiscardEditAsync() { CloseEdit(); await InvokeAsync(StateHasChanged); }
    private Task RequestCloseEditAsync() => editTracker is null ? DiscardEditAsync() : editTracker.RunAfterConfirmAsync(DiscardEditAsync);
}
