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
    private void OpenSaveAsNew()
    {
        if (existing is null || saving) return;
        Refresh();
        if (problems.Count > 0) return;
        saveAsName = templateName;
        saveAsError = "";
        showSaveAs = true;
    }

    private void CloseSaveAs() => showSaveAs = false;
    private void SaveAsKeyDown(KeyboardEventArgs args) { if (args.Key == "Escape") CloseSaveAs(); }

    private async Task ConfirmSaveAsNewAsync()
    {
        var name = saveAsName.Trim();
        if (name.Length == 0) { saveAsError = InvoiceTemplateRules.NameRequiredMessage; return; }
        if (existing is not null && InvoiceTemplateRules.SameName(name, existing.Info.Name)) { saveAsError = "Alege o denumire diferită de cea a șablonului editat."; return; }
        showSaveAs = false;
        await SaveAsync(asNew: true, name);
    }

    // "Salvează" replaces the template being edited (no earlier version is kept); a new template, or "Salvează ca șablon nou" (asNew, with the new name
    // asked for), creates another template and leaves the edited one as it was.
    private async Task SaveAsync(bool asNew, string? newName = null)
    {
        if (saving || session is null || draft is null) return;
        Refresh();
        if (problems.Count > 0) return;
        if (registryLoaded && RegistrySupplier is null) { error = "Furnizorul nu este în baza de date: adaugă-l cu „+ Adaugă furnizorul” (secțiunea Salvare) înainte de a salva șablonul."; return; }
        saving = true; error = null; notice = null;
        try
        {
            // The template carries the supplier's name as it is in the database (the single source: a rename there reaches the template).
            if (RegistrySupplier is { } linked) { supplierName = linked.Name; supplierCui = linked.Cui; }
            draft.SupplierName = supplierName;
            draft.SupplierCui = supplierCui;
            // Where the rows of the table ended in this file is kept with the template, so that it can be shown later without reading the file.
            if (preview is not null && draft.HasTable && preview.Rows.Where(row => row.Page == draft.HeaderPage).ToList() is { Count: > 0 } headerRows) draft.BodyBottom = headerRows.Max(row => row.Bottom);
            var input = new InvoiceTemplateInput
            {
                Name = newName ?? templateName, SupplierName = supplierName, SupplierCui = supplierCui, SupplierId = RegistrySupplier?.Id, Definition = draft.ToDefinition(session.Document),
                ModelFileName = session.FileName, ModelContent = session.SourcePdf
            };
            var saved = !asNew && existing is not null
                ? await Templates.SaveAsync(existing.Info, input, lifetime.Token)
                : await Templates.CreateAsync(input, lifetime.Token);
            // The file is kept with the template (as its model) and dropped from memory.
            await DiscardSessionAsync();
            session = null; draft = null; preview = null; existing = null; suggestions = [];
            notice = $"Șablonul „{saved.Info.Name}” a fost salvat. Factura model a fost salvată împreună cu șablonul.";
            await SavedRecord.InvokeAsync(saved);
            await Saved.InvokeAsync();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceTemplateOperationException exception) { error = exception.Message; }
        catch (AccessDeniedException exception) { error = exception.Message; }
        catch (Exception exception)
        {
            Logger.LogError("Invoice template saving failed ({ErrorType}).", exception.GetType().Name);
            error = "Șablonul nu a putut fi salvat. Reîncearcă.";
        }
        finally { saving = false; }
    }
}
