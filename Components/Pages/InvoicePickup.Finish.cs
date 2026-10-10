using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

public partial class InvoicePickup
{
    // The description of each entry is generated from the template (and kept as the user edited it afterwards).
    private void PrepareSummary()
    {
        finalizeError = finalizeNotice = null;
        foreach (var line in pick.Where(item => !item.DescriptionEdited))
            line.EntryDescription = session?.IsXml == true ? XmlEntryDescription(line) : overlay is null ? "" : InvoiceProductDescription.RenderRow(overlay, reading!.Extraction.Fields, WithCode(line), NumberHint);
    }

    // Creates the new products (with their images) and records every entry. A row that was saved is never saved twice: after an error the
    // remaining rows can be tried again.
    private async Task FinalizeAsync()
    {
        if (finalizing || finished) return;
        if (!CanFinalize) { showFinalizeBlockers = true; return; }
        showFinalizeBlockers = false;
        finalizeError = finalizeNotice = null;
        var lines = NewLines.Concat(ExistingLines).Where(line => !line.Done && !Skipped(line)).ToList();
        foreach (var line in lines)
            if (QuantityOf(line).Error is { } problem) { finalizeError = problem; return; }
        if (NewLines.Any(line => !line.Done) && newDate is null || ExistingLines.Any(line => !line.Done) && existingDate is null) { finalizeError = "Alege data intrării."; return; }
        var candidatesNow = Candidates;
        foreach (var line in lines)
            if (NegativeStock(line) is not null && !line.Neg.IsResolved && !(candidatesNow.ContainsKey(line) && line.LinkCandidate))
            { finalizeError = $"Stoc negativ la «{catalog.FirstOrDefault(product => product.Id == line.ProductId)?.Name}»: alege «Trec stocul pe 0» sau introdu cantitatea reală din depozit."; return; }
        if (supplierId is null) { finalizeError = SupplierInvoiceRules.SupplierRequiredMessage; return; }
        if (invoiceNumber.Trim().Length == 0) { finalizeError = SupplierInvoiceRules.NumberRequiredMessage; return; }
        if (invoiceDate is null) { finalizeError = SupplierInvoiceRules.DateRequiredMessage; return; }
        finalizing = true;
        StateHasChanged();
        try
        {
            // The invoice is recorded once, before the first entry (a retry after an error reuses it); an invoice already taken is refused here, so
            // nothing is added to the stock twice.
            if (createdInvoice is null && existingInvoice is not null) createdInvoice = existingInvoice;   // a partly taken invoice is continued
            if (createdInvoice is null)
            {
                try
                {
                    createdInvoice = await SupplierInvoices.CreateAsync(new SupplierInvoiceInput { SupplierId = supplierId, Number = invoiceNumber, Date = invoiceDate }, lifetime.Token);
                    await RecordRecognitionAsync(createdInvoice.Id);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception exception) when (exception is SupplierInvoiceOperationException or AccessDeniedException) { finalizeError = exception.Message; return; }
                catch (Exception exception)
                {
                    Logger.LogError("Invoice recording failed ({ErrorType}, database error {ErrorCode}).", exception.GetType().Name, (exception as MySqlConnector.MySqlException)?.ErrorCode);
                    finalizeError = "Factura nu a putut fi înregistrată. Nimic nu a fost salvat; verifică și reîncearcă.";
                    return;
                }
            }
            var candidates = Candidates;
            foreach (var line in lines)
            {
                var isNew = line.Staged is not null;
                try
                {
                    if (!isNew && line.LinkCandidate && candidates.TryGetValue(line, out var match))
                    {
                        var entry = await Movements.GetAsync(match.MovementId, lifetime.Token);
                        if (entry is { InvoiceId: null })
                        {
                            await Movements.AttachToInvoiceAsync([entry], createdInvoice!.Id, "Preluare factură: intrarea liberă existentă a fost legată de factură (potrivire automată)", true, lifetime.Token);
                            line.Done = true;
                            await RememberSupplierCodeAsync(line, line.ProductId);
                            line.Result = "Intrarea liberă existentă a fost legată de factură; stocul nu s-a modificat.";
                            continue;
                        }
                    }
                    var productId = line.CreatedProductId ?? line.ProductId;
                    if (!isNew && NegativeStock(line) is not null)
                    {
                        try { await Movements.RegularizeNegativeStockAsync(productId!.Value, line.Neg.RealQuantity, $"Preluare factură {invoiceNumber}", lifetime.Token); line.Regularized = true; }
                        catch (StockMovementOperationException exception) when (exception.Message == StockMovementRules.StockNotNegativeMessage) { line.Regularized = true; }
                    }
                    if (isNew && line.CreatedProductId is null)
                    {
                        var created = await ProductsRepository.CreateAsync(line.Staged!.Input, lifetime.Token);
                        line.CreatedProductId = created.Id;
                        productId = created.Id;
                        if (line.Staged.Image is { } image) await ImageStore.SaveAsync(created.Id, image, lifetime.Token);
                    }
                    var input = new StockMovementInput { Kind = StockMovementKind.Entry, Date = isNew ? newDate : existingDate, Quantity = QuantityOf(line).Quantity, Description = line.EntryDescription, ProjectComponentId = line.ComponentId, InvoiceId = createdInvoice!.Id,
                        InvoiceQuantity = QuantityOf(line).Quantity, DuplicateReason = line.RetakeAnyway && TakenQuantity(line) is not null ? "Produs preluat din nou la preluarea facturii, confirmat de utilizator" : "" };
                    await Movements.CreateAsync(productId!.Value, input, lifetime.Token);
                    line.Done = true;
                    await RememberSupplierCodeAsync(line, productId);
                    line.Result = isNew ? "Produs adăugat în catalog și intrare înregistrată." : "Intrare înregistrată.";
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception exception) when (exception is ProductOperationException or StockMovementOperationException or AccessDeniedException)
                {
                    line.Result = (line.CreatedProductId is not null ? "Produsul a fost adăugat, dar intrarea nu: " : "") + exception.Message;
                }
                catch (Exception exception)
                {
                    Logger.LogError("Invoice pickup saving failed ({ErrorType}, database error {ErrorCode}).", exception.GetType().Name, (exception as MySqlConnector.MySqlException)?.ErrorCode);
                    line.Result = (line.CreatedProductId is not null ? "Produsul a fost adăugat, dar intrarea nu a putut fi salvată. " : "") + "Salvarea nu a putut fi confirmată; verifică în catalog înainte să reîncerci.";
                }
            }
            var all = NewLines.Concat(ExistingLines).Where(line => line.Done || !Skipped(line)).ToList();
            var skippedCount = NewLines.Concat(ExistingLines).Count(Skipped);
            if (all.All(line => line.Done)) { finished = true; finalizeNotice = $"Preluarea a fost finalizată: {all.Count} {(all.Count == 1 ? "intrare înregistrată" : "intrări înregistrate")}, legate de factura {createdInvoice!.Number} a furnizorului {createdInvoice.SupplierName}." + (skippedCount > 0 ? $" {skippedCount} {(skippedCount == 1 ? "rând deja preluat nu a fost preluat din nou" : "rânduri deja preluate nu au fost preluate din nou")}." : ""); }
            else finalizeError = "Unele rânduri nu au fost salvate; vezi mesajele de sub ele. Cele salvate nu se salvează a doua oară la o nouă încercare.";
        }
        finally { finalizing = false; }
    }

    private async Task NewPickupAsync()
    {
        await DiscardAsync();
        pick = []; finished = false; finalizeNotice = null; finalizeError = null; step = 1;
        ResetInvoiceHeader();
        newDate = existingDate = StockMovementRules.Today;
    }

    // The rows kept in step 1 (with the columns the template reads), looked up in the catalog.
    private async Task BuildPickAsync()
    {
        step2Loading = true; step2Error = null;
        StateHasChanged();
        try
        {
            catalog = await ProductsRepository.GetProductsAsync(lifetime.Token);
            groups = await ProductsRepository.GetGroupsAsync(lifetime.Token);
            try { freeEntries = await Movements.GetFreeEntriesAsync(new FreeEntryQuery(), lifetime.Token); } catch (Exception exception) when (exception is not OperationCanceledException) { freeEntries = []; }
            var previous = pick.ToList();
            pick = [.. rows.Select((row, rowIndex) =>
            {
                var key = SourceKey(row);
                var again = previous.FindIndex(item => item.SourceKey == key);
                if (again < 0) return new PickRow { Cells = new Dictionary<string, string>(row.Cells), SourceKey = key, Page = row.Page, Top = row.Top, Bottom = row.Bottom, RowIndex = rowIndex };
                var kept = previous[again];
                previous.RemoveAt(again);
                kept.RowIndex = rowIndex;
                return kept;
            })];
            await LoadSupplierCodesAsync();
            await LoadVariantsAsync();
            foreach (var item in pick) Rematch(item);   // also against the catalog as it is now
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.LogError("Invoice pickup product lookup failed ({ErrorType}).", exception.GetType().Name);
            step2Error = "Catalogul de produse nu a putut fi citit. Reîncearcă prin „Pasul precedent” și „Pasul următor”.";
        }
        finally { step2Loading = false; }
    }
}
