using System.Globalization;
using BlazorStoc.Services;
using BlazorStoc.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Shared;

public partial class InvoiceTemplateWorkbench
{
    private static string SupplierLabel(InvoiceTemplateInfo info) => info.SupplierName.Length > 0 ? info.SupplierName : info.SupplierCui.Length > 0 ? "CUI " + info.SupplierCui : "furnizor nespecificat";

    private async Task LoadRegistryAsync()
    {
        try { registry = await Suppliers.GetSuppliersAsync(lifetime.Token); registryLoaded = true; }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Supplier register loading failed ({ErrorType}).", exception.GetType().Name);
            registry = []; registryLoaded = false;   // without the register the template is not blocked
        }
    }

    private void UseSimilarSupplier()
    {
        if (similarSupplier is not { } suggested) return;
        supplierName = suggested.Name;
        supplierCui = suggested.Cui;
        similarSupplier = null;
        Refresh();
    }

    private void SuggestTemplateName(string previousSupplierName)
    {
        if (existing is not null) return;
        var next = InvoiceTemplateRules.SuggestedName(supplierName, false, DateTime.Today);
        if (templateName.Trim().Length == 0 || templateName == lastSuggestedName || (previousSupplierName.Length > 0 && InvoiceTemplateRules.SameName(templateName, previousSupplierName))) templateName = next;
        lastSuggestedName = next;
    }

    private void AdoptRegistrySupplier()
    {
        similarSupplier = null;
        var documentKey = session is null ? "" : SupplierRules.CompactKey(string.Concat(session.Document.AllWords.Select(word => word.Text)));
        var recognized = SupplierRecognizer.Recognize(registry, supplierCui, supplierName, documentKey);
        if (recognized.NeedsConfirmation) { similarSupplier = recognized.Supplier; return; }
        var found = recognized.Supplier;
        if (found is null) return;
        supplierName = found.Name;
        supplierCui = found.Cui;
        SuggestTemplateName(draft?.SupplierName ?? "");
    }

    private void OpenAddSupplier() { addSupplierKey++; addingSupplier = true; }
    private Task CloseAddSupplier() { addingSupplier = false; return Task.CompletedTask; }

    // The supplier was added in the window (its name is the one ANAF returned, or the one typed there): the template carries it.
    private async Task SupplierAddedAsync(Supplier saved)
    {
        addingSupplier = false;
        supplierName = saved.Name;
        supplierCui = saved.Cui;
        await LoadRegistryAsync();
        // The CUI field read from the invoice (an OCR misreading, like the extra digit) takes the supplier's own, else Refresh() would write the misread one back.
        if (draft is not null)
            foreach (var field in draft.Fields.Where(item => item.Meaning == InvoiceFieldMeanings.SupplierCui)) field.Value = saved.Cui;
        notice = $"Furnizorul „{saved.Name}” a fost adăugat în baza de date; șablonul se leagă de el.";
        Refresh();
    }

    private void SyncSupplierCui()
    {
        var field = draft!.Fields.FirstOrDefault(item => item.Meaning == InvoiceFieldMeanings.SupplierCui && item.Use && item.Value.Trim().Length > 0)
                    ?? draft.Fields.FirstOrDefault(item => item.Meaning == InvoiceFieldMeanings.SupplierCui && item.Value.Trim().Length > 0);
        if (field is not null)
        {
            var cui = InvoiceValues.NormalizeCui(field.Value);
            supplierCui = cui.Length > 0 ? cui : field.Value.Trim();
            cuiFieldSeen = true;
        }
        else if (cuiFieldSeen) { supplierCui = ""; cuiFieldSeen = false; }
    }
}
