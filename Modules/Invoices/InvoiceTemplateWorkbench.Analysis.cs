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
    private string ImageUrl(int page) => $"/media/invoice-analysis/{session!.Id}/{page}";
    private Task CloseXmlSample() { xmlSample = null; return Task.CompletedTask; }

    private async Task OpenXmlSampleAsync(IBrowserFile file)
    {
        if (file.Size > InvoiceXmlRules.MaxFileSizeBytes) { error = InvoiceXmlRules.TooLargeMessage; return; }
        try
        {
            using var memory = new MemoryStream();
            await using (var stream = file.OpenReadStream(InvoiceXmlRules.MaxFileSizeBytes, lifetime.Token)) await stream.CopyToAsync(memory, lifetime.Token);
            (xmlSample, xmlSampleName) = InvoiceXmlReader.Unpack(memory.ToArray(), file.Name); xmlSampleKey++;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceAnalysisException exception) { error = exception.Message; }
        catch (IOException) { error = "Fișierul XML nu a putut fi citit."; }
    }

    private async Task XmlTemplateSavedAsync(InvoiceTemplateRecord saved)
    {
        xmlSample = null;
        notice = $"Șablonul XML „{saved.Info.Name}” a fost salvat.";
        await Saved.InvokeAsync();
        await SavedRecord.InvokeAsync(saved);
    }

    private async Task OnFileAsync(InputFileChangeEventArgs args)
    {
        if (busy) return;
        error = notice = null;
        var file = args.File;
        if ((file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) && !IsEditMode) { await OpenXmlSampleAsync(file); return; }
        if (!file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            error = InvoiceAnalysisRules.NotAPdfMessage;
            return;
        }
        if (file.Size > InvoiceAnalysisRules.MaxFileSizeBytes) { error = InvoiceAnalysisRules.TooLargeMessage; return; }
        await RunAnalysisAsync(() => Task.FromResult<Stream>(file.OpenReadStream(InvoiceAnalysisRules.MaxFileSizeBytes, lifetime.Token)), file.Name);
    }

    // Opens a file (uploaded now or the model of the template being edited). A new template is proposed by the analysis of the file; a template
    // being edited is only shown on the file, with the saved positions.
    private async Task RunAnalysisAsync(Func<Task<Stream>> open, string fileName)
    {
        if (busy) return;
        busy = true;
        StateHasChanged();
        try
        {
            await DiscardSessionAsync();
            await using var stream = await open();
            var editing = EditTemplateId is { } editId ? await Templates.GetAsync(editId, lifetime.Token) : null;
            if (editing is not null)
            {
                session = await Analysis.OpenAsync(stream, fileName, lifetime.Token);
                await LoadRegistryAsync();
                decimalHint = InvoiceValues.DecimalStyle(session.Document.AllWords.Select(word => word.Text));
                suggestions = [];
                analyzed = false;
                currentPage = session.Document.Pages[0].Number;
                UseSavedTemplate(editing);
                rebaseAfterRender = true;
            }
            else
            {
                session = await Analysis.AnalyzeAsync(stream, fileName, lifetime.Token);
                await LoadRegistryAsync();
                decimalHint = InvoiceValues.DecimalStyle(session.Document.AllWords.Select(word => word.Text));
                suggestions = InvoiceTemplateSuggestions.Rank(await Templates.GetAllAsync(lifetime.Token), session.Document);
                analyzed = true;
                existing = null;
                currentPage = session.Document.Pages[0].Number;
                UseAutomaticProposal();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceAnalysisException exception) { error = exception.Message; session = null; draft = null; }
        catch (AccessDeniedException exception) { error = exception.Message; session = null; draft = null; }
        catch (Exception exception)
        {
            Logger.LogError("Invoice analysis failed ({ErrorType}).", exception.GetType().Name);
            error = "Fișierul nu a putut fi analizat. Încearcă din nou sau alt fișier.";
            session = null; draft = null;
        }
        finally { busy = false; }
    }

    private void UseAutomaticProposal()
    {
        existing = null;
        draft = InvoiceTemplateDraft.FromAnalysis(session!.Analysis);
        templateName = lastSuggestedName = "";
        supplierName = draft.SupplierName;
        supplierCui = draft.SupplierCui;
        SuggestTemplateName("");
        AdoptRegistrySupplier();
        ResetSelection(); ResetHistory();
        Refresh();
    }

    // A saved template exactly as saved (no alignment, no analysis of the file).
    private void UseSavedTemplate(InvoiceTemplateRecord template)
    {
        existing = template;
        draft = InvoiceTemplateDraft.FromSaved(template.Definition, session!.Document, template.Info.SupplierName, template.Info.SupplierCui);
        templateName = template.Info.Name;
        supplierName = template.Info.SupplierName;
        supplierCui = template.Info.SupplierCui;
        ResetSelection(); ResetHistory();
        Refresh();
    }

    // The user's request to analyse the file: the file is analysed (fields, table, the saved templates that fit it) and the template being
    // edited is placed on it by its anchors. The change is one step of the history, so it can be undone.
    private async Task AnalyzeFileAsync()
    {
        if (busy || saving || session is null) return;
        busy = true;
        error = notice = null;
        StateHasChanged();
        try
        {
            session = Analysis.Analyze(session.Id);
            suggestions = InvoiceTemplateSuggestions.Rank(await Templates.GetAllAsync(lifetime.Token), session.Document);
            analyzed = true;
            if (existing is not null)
            {
                draft = InvoiceTemplateDraft.FromDefinition(existing.Definition, session.Document, supplierName, supplierCui);
                draft.ProductDescription = InvoiceProductDescription.Upgrade(existing.Definition.ProductDescription ?? "", draft);
                ResetSelection();
            }
            if (draft is not null) Refresh();
            notice = "Fișierul a fost analizat" + (existing is not null ? ": șablonul a fost așezat pe fișier (poți anula cu „Anulează”)." : ".");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceAnalysisException exception) { error = exception.Message; }
        catch (Exception exception) when (exception is AccessDeniedException)
        {
            error = exception.Message;
        }
        catch (Exception exception)
        {
            Logger.LogError("Invoice analysis failed ({ErrorType}).", exception.GetType().Name);
            error = "Fișierul nu a putut fi analizat. Încearcă din nou.";
        }
        finally { busy = false; }
    }

    private void UseTemplate(InvoiceTemplateRecord template)
    {
        existing = template;
        draft = InvoiceTemplateDraft.FromDefinition(template.Definition, session!.Document, template.Info.SupplierName, template.Info.SupplierCui);
        templateName = template.Info.Name;
        supplierName = template.Info.SupplierName;
        supplierCui = template.Info.SupplierCui;
        ResetSelection(); ResetHistory();
        Refresh();
    }

    private void ResetSelection() { multiIds.Clear(); popupOpen = false; selectedId = ""; selectedKind = ' '; drawMode = ""; problems = []; }

    private void Refresh()
    {
        if (session is null || draft is null) return;
        RecordHistory();
        SyncSupplierCui();
        try
        {
            // A template opened for editing is only laid over its file: what it reads (the engine finds the table header, aligns the anchors)
            // is computed after the user asks for the analysis of the file.
            preview = IsEditMode && !analyzed ? null : InvoiceTemplateEngine.Apply(draft.ToDefinition(session.Document), session.Document);
            problems = [.. draft.Problems()];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Invoice template preview failed ({ErrorType}).", exception.GetType().Name);
            preview = null;
        }
    }

    private void RefreshPreviewOnly()
    {
        replaying = true;
        try { Refresh(); } finally { replaying = false; }
    }

    private void RefreshIndex() => draft!.HasIndexColumn = draft.Columns.Any(column => column.Use && column.Meaning == InvoiceColumnMeanings.Index);

    private IReadOnlyCollection<InvoiceWord> WordsIn(int page, double x, double y, double width, double height) =>
        session!.Document.Pages.First(item => item.Number == page).Words
            .Where(word => word.CenterX >= x && word.CenterX <= x + width && word.CenterY >= y && word.CenterY <= y + height).ToList().AsReadOnly();
}
