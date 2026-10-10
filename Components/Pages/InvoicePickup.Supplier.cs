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
    // "Yes, it is the same invoice": the number of the stored invoice replaces the one read (and is to be confirmed again).
    private async Task UseSimilarAsync(SupplierInvoice invoice)
    {
        invoiceNumber = invoice.Number;
        numberVerified = false;
        await RefreshExistingInvoiceAsync();
    }

    // The supplier of the CUI as typed: a Romanian CUI by its digits, a VAT identifier of another state by its letters and digits.
    private void ResolveSupplier()
    {
        var digits = SupplierRules.CuiDigits(supplierCuiText);
        var compact = SupplierRules.CompactKey(supplierCuiText);
        supplierId = supplierList.FirstOrDefault(supplier => supplier.IsExternal ? compact.Length > 0 && SupplierRules.CompactKey(supplier.Cui) == compact
            : digits.Length > 0 && SupplierRules.CuiDigits(supplier.Cui) == digits)?.Id;
        // A supplier found in the register (by its CUI, its name, or just added) has its CUI verified by that very fact; the number and the date stay to be checked.
        cuiVerified = supplierId is not null;
    }

    private async Task NumberEdited(ChangeEventArgs e)
    {
        invoiceNumber = e.Value?.ToString() ?? "";
        numberVerified = false;
        await RefreshExistingInvoiceAsync();
    }

    private async Task CuiEdited(ChangeEventArgs e)
    {
        supplierCuiText = e.Value?.ToString() ?? "";
        cuiVerified = false;
        recognition = null;
        ResolveSupplier();
        await RefreshExistingInvoiceAsync();
    }

    private Task DateEdited(DateOnly? picked)
    {
        invoiceDate = picked;
        dateVerified = false;
        return Task.CompletedTask;
    }

    private async Task LoadSuppliersAsync()
    {
        supplierError = null;
        try { supplierList = await Suppliers.GetSuppliersAsync(lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            Logger.LogError("Supplier list loading failed ({ErrorType}).", exception.GetType().Name);
            supplierError = "Registrul de furnizori nu a putut fi citit. Reia „Pasul precedent” și „Pasul următor”.";
            supplierList = [];
        }
        if (headerPrefilledFor != session?.Id) { PrefillInvoiceHeader(); headerPrefilledFor = session?.Id; }
        else ResolveSupplier();
        await RefreshExistingInvoiceAsync();
    }

    // The number, the date and the supplier as the template read them from the invoice (the user checks and corrects them).
    private void PrefillInvoiceHeader()
    {
        string FieldValue(string meaning) => reading?.Extraction.Fields.FirstOrDefault(field => field.Meaning == meaning && field.Value.Trim().Length > 0)?.Value.Trim() ?? "";
        invoiceNumber = TextNormalization.ForObjectNameOrCode(FieldValue(InvoiceFieldMeanings.InvoiceNumber));
        invoiceDate = InvoiceValues.ParseDate(FieldValue(InvoiceFieldMeanings.InvoiceDate)) is { } date && date >= StockMovementRules.EarliestDate && date <= StockMovementRules.Today ? date : null;
        invoiceSupplierCui = InvoiceValues.NormalizeCui(FieldValue(InvoiceFieldMeanings.SupplierCui)) is { Length: > 0 } fromField ? fromField
            : session?.Analysis.SupplierCui is { Length: > 0 } fromAnalysis ? fromAnalysis
            : chosen?.Info.SupplierCui ?? "";
        invoiceSupplierName = FieldValue(InvoiceFieldMeanings.SupplierName) is { Length: > 0 } nameField ? nameField
            : session?.Analysis.SupplierName is { Length: > 0 } fromAnalysisName ? fromAnalysisName
            : chosen?.Info.SupplierName ?? "";
        supplierCuiText = invoiceSupplierCui;
        numberVerified = cuiVerified = dateVerified = false;   // read, not yet checked by the user
        ResolveSupplier();
        // The CUI read from the invoice found no supplier (misread, or not read): the name read from the invoice is looked up in the register,
        // legal form ignored (SC / S.C. / SRL / S.R.L.), and the supplier's own CUI is taken, to be checked against the text of the invoice.
        // One recognition for CUI, name, alias and near miss (SupplierRecognizer), with its method and confidence.
        BuildCuiSearchText();
        recognition = SupplierRecognizer.Recognize(supplierList, invoiceSupplierCui, invoiceSupplierName, cuiSearchText);
        initialRecognition = recognition;
        supplierByName = false;
        similarSupplier = null;
        if (recognition.NeedsConfirmation) similarSupplier = recognition.Supplier;
        else if (supplierId is null && recognition.Supplier is { } found)
        {
            supplierCuiText = found.Cui;
            supplierId = found.Id;
            supplierByName = true;
        }
        cuiVerified = ResolvedSupplier is not null;
    }

    // The proposal and the final choice go to the recognition log (to see where the reading goes wrong); never blocks recording the invoice.
    private async Task RecordRecognitionAsync(int invoiceId)
    {
        if (Services.GetService<ISupplierRecognitionLog>() is not { } log) return;
        var proposed = initialRecognition ?? new SupplierRecognition(null, SupplierMatchMethod.None, "low");
        try
        {
            await log.RecordAsync(new SupplierRecognitionEntry(invoiceId, proposed.Method, proposed.Confidence, invoiceSupplierName, invoiceSupplierCui, proposed.Supplier?.Id, supplierId, templateChanged), lifetime.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Logger.LogWarning("Recording the supplier recognition failed ({ErrorType}).", exception.GetType().Name); }
    }

    // The near-miss supplier is only proposed (the OCR may have misread a letter): the user confirms, and the CUI is then checked as usual.
    private void UseSimilarSupplier()
    {
        if (similarSupplier is not { } suggested) return;
        supplierCuiText = suggested.Cui;
        cuiVerified = false;
        similarSupplier = null;
        ResolveSupplier();
    }

    private async Task RememberAliasAsync()
    {
        if (AliasOffer is not { } read || ResolvedSupplier is not { } supplier) return;
        try
        {
            await Suppliers.AddAliasAsync(supplier, read, lifetime.Token);
            aliasNotice = $"Denumirea „{read}” a fost reținută pentru {supplier.Name}.";
            supplierList = await Suppliers.GetSuppliersAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is SupplierOperationException or AccessDeniedException) { supplierError = exception.Message; }
        catch (NotSupportedException) { }
    }

    private void BuildCuiSearchText()
    {
        if (cuiSearchFor == session?.Id) return;
        cuiSearchFor = session?.Id;
        cuiSearchText = session is null ? "" : SupplierRules.CompactKey(string.Concat(session.Document.AllWords.Select(word => word.Text)));
    }

    // The invoice of this supplier and number may have been taken before, in part: what was taken is read, so the pickup continues on the same
    // invoice and the products already taken are marked.
    private async Task RefreshExistingInvoiceAsync()
    {
        if (createdInvoice is not null) return;
        existingInvoice = null; takenEntries = []; similarInvoices = [];
        if (supplierId is null || invoiceNumber.Trim().Length == 0) return;
        try
        {
            existingInvoice = await SupplierInvoices.FindAsync(supplierId.Value, invoiceNumber, lifetime.Token);
            if (existingInvoice is not null)
            {
                takenEntries = await SupplierInvoices.GetEntriesAsync(existingInvoice.Id, lifetime.Token);
                if (invoiceDate != existingInvoice.Date) { invoiceDate = existingInvoice.Date; dateVerified = false; }   // the stored date; to be confirmed
            }
            else
            {
                // OCR can misread the number: invoices of this supplier with a look-alike number (and the same date, when it is known) are offered.
                similarInvoices = [.. (await SupplierInvoices.GetForSupplierAsync(supplierId.Value, lifetime.Token))
                    .Where(item => SupplierInvoiceRules.LooksLike(invoiceNumber, item.Number) && (invoiceDate is null || item.Date == invoiceDate))];
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.LogError("Reading the invoice taken earlier failed ({ErrorType}).", exception.GetType().Name);
            existingInvoice = null; takenEntries = [];
            supplierError = "Nu s-a putut verifica dacă factura a mai fost preluată. Reia „Pasul precedent” și „Pasul următor”.";
        }
    }

    private void OpenAddSupplier(bool fromInvoice) { addFromInvoice = fromInvoice; addSupplierKey++; addingSupplier = true; }
    private Task CloseAddSupplier() { addingSupplier = false; return Task.CompletedTask; }

    // The supplier was saved in the window: it is offered and chosen at once.
    private async Task SupplierAddedAsync(Supplier saved)
    {
        addingSupplier = false;
        supplierCuiText = saved.Cui;   // the supplier's own tax id is now in the field: to be confirmed
        cuiVerified = false;
        await LoadSuppliersAsync();
    }

    private void ResetInvoiceHeader()
    {
        createdInvoice = existingInvoice = null; takenEntries = []; similarInvoices = []; supplierId = null; invoiceNumber = ""; invoiceDate = null; headerPrefilledFor = null;
        invoiceSupplierCui = invoiceSupplierName = supplierCuiText = "";
        numberVerified = cuiVerified = dateVerified = false;
    }

    private static string SupplierLabel(InvoiceTemplateInfo info) => InvoiceTemplateText.SupplierLabel(info);
}
