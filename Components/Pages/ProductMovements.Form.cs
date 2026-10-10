using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using BlazorStoc.Components.Shared;
using BlazorStoc.Services;

namespace BlazorStoc.Components.Pages;

public partial class ProductMovements
{
    private static StockMovementInput NewForm() => new() { Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Today) };

    // Both buttons keep the typed form, then open the add form of the beneficiary / project with a way back.
    private Task AddBeneficiaryAsync()
    {
        ExitDraft.Store(Id, form, useProject, addSuggestion);
        addTracker?.Rebase();
        Navigation.NavigateTo($"/beneficiari?adauga=1&{ReturnNavigation.QueryName}={Uri.EscapeDataString(ReturnPath)}");
        return Task.CompletedTask;
    }

    private Task AddProjectAsync()
    {
        if (form.BeneficiaryId is not { } beneficiaryId) return Task.CompletedTask;
        ExitDraft.Store(Id, form, useProject, addSuggestion);
        addTracker?.Rebase();
        Navigation.NavigateTo($"/beneficiari/{beneficiaryId}?adauga-proiect=1&{ReturnNavigation.QueryName}={Uri.EscapeDataString(ReturnPath)}");
        return Task.CompletedTask;
    }

    private async Task EnsureBeneficiariesAsync()
    {
        if (beneficiaries.Count > 0) return;
        try { beneficiaries = (await Beneficiaries.GetBeneficiariesAsync(lifetime.Token)).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogError("Beneficiary list loading failed ({ErrorType}).", ex.GetType().Name); formError = "Lista beneficiarilor nu a putut fi încărcată."; }
    }

    // Entry and exit are two tabs: passing from one to the other starts a clean form (nothing typed for one is carried into the other).
    private async Task SetKindAsync(StockMovementKind kind)
    {
        if (form.Kind == kind) return;
        form = NewForm(); useProject = false; addSuggestion = null; addTracker?.Rebase();
        form.Kind = kind; formError = null;
        if (kind == StockMovementKind.Entry)
        {
            useProject = false; form.BeneficiaryId = form.ProjectId = form.VehicleId = form.SourceVehicleId = null; form.Destination = null;
            form.Description = StockMovementRules.ApplySuggestion(form.Description, addSuggestion, null); addSuggestion = null;
        }
        else { await EnsureBeneficiariesAsync(); await EnsureVehiclesAsync(); }
    }

    private async Task EnsureVehiclesAsync()
    {
        if (vehiclesLoaded) return;
        try { vehicles = await VehicleRepository.GetVehiclesAsync(lifetime.Token); vehiclesLoaded = true; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogError("Vehicle list loading failed ({ErrorType}).", ex.GetType().Name); formError = "Lista vehiculelor nu a putut fi încărcată."; }
    }

    private async Task DestinationChangedAsync(bool isEdit)
    {
        var target = isEdit ? editForm : form;
        if (isEdit) editError = null; else formError = null;
        if (target.Destination == ExitDestination.Beneficiary) await EnsureBeneficiariesAsync();
        if (!isEdit && target.Destination != ExitDestination.Beneficiary) { projects = Array.Empty<Project>(); }
        if (isEdit && target.Destination != ExitDestination.Beneficiary) { editProjects = Array.Empty<Project>(); }
        ApplySuggestion(isEdit);
    }

    // Proposes the description for a vehicle transfer or a stock correction; text typed by the user is never overwritten.
    private void ApplySuggestion(bool isEdit)
    {
        var target = isEdit ? editForm : form;
        var plate = target.VehicleId is { } vehicleId ? vehicles.FirstOrDefault(vehicle => vehicle.Id == vehicleId)?.PlateNumber : null;
        var suggestion = StockMovementRules.SuggestedDescription(target.Destination, plate, StockMovementRules.Today);
        target.Description = StockMovementRules.ApplySuggestion(target.Description, isEdit ? editSuggestion : addSuggestion, suggestion);
        if (isEdit) editSuggestion = suggestion; else addSuggestion = suggestion;
    }

    private static void SetProject(StockMovementInput target, int? projectId) => target.ProjectId = projectId;

    private void ToggleProject(StockMovementInput target, bool value, bool isEdit)
    {
        if (isEdit) editUseProject = value; else useProject = value;
        if (!value) target.ProjectId = null;
    }

    private async Task SelectBeneficiaryAsync(StockMovementInput target, int? beneficiaryId, bool isEdit)
    {
        target.BeneficiaryId = beneficiaryId; target.ProjectId = null;
        var list = Array.Empty<Project>() as IReadOnlyList<Project>;
        if (beneficiaryId is { } id)
        {
            try { list = await Projects.GetForBeneficiaryAsync(id, lifetime.Token); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
            catch (Exception ex) { Logger.LogError("Project list loading failed ({ErrorType}).", ex.GetType().Name); }
        }
        if (isEdit) editProjects = list; else projects = list;
    }

    private async Task AddAsync()
    {
        if (saving || product is null) return;
        formError = form.Kind == StockMovementKind.Exit ? addPicker?.ValidationError() : originPicker?.ValidationError();
        if (formError is not null) return;
        saving = true; notice = null;
        try
        {
            if (form.Kind == StockMovementKind.Entry && stock < 0 && !await ApplyRegularizationAsync("Intrare înregistrată din pagina produsului")) return;
            if (form.Kind == StockMovementKind.Entry && originPicker is not null && await originPicker.PrepareAsync(lifetime.Token) is { } originError) { formError = originError; return; }
            var result = await Movements.CreateAsync(product.Id, form, lifetime.Token);
            stock = result.Stock; product = product with { Quantity = stock };
            form.Description = ""; form.Quantity = null; addSuggestion = null; ApplySuggestion(false); addTracker?.Rebase();
            notice = $"Mișcarea a fost adăugată. Stoc curent: {stock} buc.";
            invoiceWarning = null; exitWarning = null; reservationWarning = null; duplicateReason = ""; form.DuplicateReason = ""; form.InvoiceQuantity = null; form.Reference = null;
            new ReservationChoice().ApplyTo(form);
            reservationVersion++;
            if (form.Kind == StockMovementKind.Entry) { suggestionTrigger++; form.ProjectComponentId = null; entryChoiceKey++; }
            if (originPicker is not null) await originPicker.RefreshAsync();
            pageNumber = 1; await LoadPageAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceEntryWarningException warning) { invoiceWarning = warning; form.DuplicateReason = ""; }
        catch (DuplicateExitWarningException warning) { exitWarning = warning; form.DuplicateReason = ""; }
        catch (ReservationWarningException warning) { reservationWarning = warning; reservationChoice.Mode = ReservationChoiceMode.None; reservationChoice.ProjectId = null; reservationChoice.Quantity = null; reservationChoice.Reason = ""; }
        catch (Exception ex) when (ex is StockMovementOperationException or AccessDeniedException) { formError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Movement creation failed ({ErrorType}).", ex.GetType().Name); formError = "Mișcarea nu a putut fi salvată. Verifică datele introduse și reîncearcă."; }
        finally { saving = false; }
    }

    private void OriginChanged() { invoiceWarning = null; formError = null; }

    private async Task ConfirmDuplicateExitAsync()
    {
        if (ChangeReasonRules.ValidationError(duplicateReason) is { } problem) { formError = problem; return; }
        form.DuplicateReason = duplicateReason; exitWarning = null; formError = null;
        await AddAsync();
    }

    private async Task ConfirmDuplicateAsync()
    {
        if (ChangeReasonRules.ValidationError(duplicateReason) is { } problem) { formError = problem; return; }
        form.DuplicateReason = duplicateReason; invoiceWarning = null; formError = null;
        await AddAsync();
    }

    private async Task ConfirmReservationAsync()
    {
        if (reservationChoice.Validate() is { } problem) { formError = problem; return; }
        reservationChoice.ApplyTo(form); reservationWarning = null; formError = null;
        await AddAsync();
    }

    private void CancelWarning() { invoiceWarning = null; exitWarning = null; reservationWarning = null; duplicateReason = ""; form.DuplicateReason = ""; new ReservationChoice().ApplyTo(form); }

    // Shows the entries of the invoice so the existing one can be edited instead of adding another.
    private async Task ModifyExistingAsync()
    {
        CancelWarning();
        filter = StockMovementKind.Entry; source = EntrySource.WithInvoice; supplierFilter = null; pageNumber = 1;
        await LoadPageAsync();
    }

    private void OpenLink(StockMovement entry) { linking = [entry]; linkDetach = entry.InvoiceId is not null; }
    private void CancelLink() => linking = null;

    private async Task LinkDoneAsync(string message)
    {
        linking = null; editing = null; notice = message;
        await LoadPageAsync();
    }

    // The stock is negative (an operating error): the correction entry brings the warehouse to the real quantity the user stated.
    private async Task<bool> ApplyRegularizationAsync(string context)
    {
        if (product is null || stock >= 0) return true;
        if (!negResolution.IsResolved) { formError = StockMovementRules.RealQuantityMessage; return false; }
        var result = await Movements.RegularizeNegativeStockAsync(product.Id, negResolution.RealQuantity, context, lifetime.Token);
        stock = result.Stock; product = product with { Quantity = stock };
        negResolution.Zero = false; negResolution.Real = null;
        return true;
    }

    private async Task RegularizeAsync()
    {
        if (saving || product is null) return;
        saving = true; formError = null; notice = null;
        try
        {
            if (await ApplyRegularizationAsync("Regularizare din pagina produsului"))
            {
                notice = $"Stocul a fost regularizat. Stoc curent: {stock} buc.";
                pageNumber = 1; await LoadPageAsync();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is StockMovementOperationException or AccessDeniedException) { formError = ex.Message; }
        catch (Exception ex) { Logger.LogError("Stock regularization failed ({ErrorType}).", ex.GetType().Name); formError = "Regularizarea nu a putut fi salvată. Actualizează pagina și reîncearcă."; }
        finally { saving = false; }
    }

    private string AddSnapshot() => FormSnapshot.Of(form, useProject);

    // Both are called by the leave dialog (another component), so the page asks for its own re-render to show the form or dialog closed.
    private async Task DiscardAddAsync() { form = NewForm(); useProject = false; addSuggestion = null; formError = null; addTracker?.Rebase(); await InvokeAsync(StateHasChanged); }
}
