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
    private string? PageRegionPicture(string key, int pageNumber, InvoiceBox area, IEnumerable<InvoiceBox> outlines, double marginX = InvoiceRegionPicture.Margin, double marginY = InvoiceRegionPicture.Margin)
    {
        if (session is null) return null;
        if (regionPictures.TryGetValue(key, out var cached)) return cached;
        var page = session.Document.Pages.FirstOrDefault(item => item.Number == pageNumber);
        var picture = page is not null && pageNumber >= 1 && pageNumber <= session.Previews.Count
            ? InvoiceRegionPicture.DataUri(session.Previews[pageNumber - 1], page, area, outlines, marginX, marginY) : null;
        return regionPictures[key] = picture;
    }

    // The label and the value of a header field (number, date) as the template read them on this file, the value outlined.
    private string? HeaderFieldPicture(string meaning)
    {
        if (overlay?.Fields.FirstOrDefault(item => item.Meaning == meaning && item.Use && item.Width > 0 && item.Height > 0) is not { } field) return null;
        var left = field.LabelX > 0 && field.LabelX < field.X ? field.LabelX : field.X;
        var top = field.LabelY > 0 ? Math.Min(field.LabelY, field.Y) : field.Y;
        var area = new InvoiceBox(field.Page, left, top, Math.Max(1, field.X + field.Width - left), Math.Max(1, field.Y + field.Height - top));
        return PageRegionPicture($"field|{meaning}|{field.Page}|{field.X:F1}|{field.Y:F1}", field.Page, area, [], 12, 1);
    }

    // The table row as it stands on the page, the cells that are not read as numbers outlined.
    private string? RowPicture(PickRow item, IReadOnlyList<ShownColumn> bad)
    {
        if (reading is null || item.Page < 1 || item.Bottom <= item.Top || bad.Count == 0) return null;
        var columns = reading.Extraction.Columns.Where(column => ShownColumns.Any(shown => shown.Id == column.Id)).ToList();
        if (columns.Count == 0) return null;
        var left = columns.Min(column => column.Left);
        var right = columns.Max(column => column.Right);
        var outlines = columns.Where(column => bad.Any(shown => shown.Id == column.Id))
            .Select(column => new InvoiceBox(item.Page, column.Left, item.Top, Math.Max(1, column.Right - column.Left), item.Bottom - item.Top)).ToList();
        var area = new InvoiceBox(item.Page, left, item.Top, Math.Max(1, right - left), item.Bottom - item.Top);
        return PageRegionPicture($"row|{item.Page}|{item.Top:F1}|{item.Bottom:F1}|{string.Join(",", bad.Select(shown => shown.Id))}", item.Page, area, outlines, 6);
    }

    private string ImageUrl(int page) => $"/media/invoice-analysis/{session!.Id}/{page}";

    private async Task FileSelectedAsync(InputFileChangeEventArgs e)
    {
        if (busy) return;
        error = notice = null;
        var file = e.File;
        if (file.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { await XmlFileSelectedAsync(file); return; }
        if (!file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)) { error = InvoiceAnalysisRules.NotAPdfMessage; return; }
        if (file.Size > InvoiceAnalysisRules.MaxFileSizeBytes) { error = InvoiceAnalysisRules.TooLargeMessage; return; }

        busy = true;
        StateHasChanged();
        try
        {
            DiscardSession();
            pick = []; finished = false; finalizeNotice = finalizeError = null; ResetInvoiceHeader();   // another file: nothing of the previous pickup applies
            await using var stream = file.OpenReadStream(InvoiceAnalysisRules.MaxFileSizeBytes, lifetime.Token);
            session = await Analysis.AnalyzeAsync(stream, file.Name, lifetime.Token);
            creatingTemplate = false;
            allTemplates = await Templates.GetAllAsync(lifetime.Token);
            suggestions = InvoiceTemplateSuggestions.Rank(allTemplates, session.Document);
            isEFacturaPdf = InvoiceEFacturaPdf.IsExport(session.Document.AllWords.Select(word => word.Text));
            // The supplier recognised with high confidence (CUI, name, alias) brings its own active templates to the front, even below the layout threshold.
            try
            {
                var registered = await Suppliers.GetSuppliersAsync(lifetime.Token);
                var recognized = SupplierRecognizer.Recognize(registered, session.Analysis.SupplierCui, session.Analysis.SupplierName,
                    SupplierRules.CompactKey(string.Concat(session.Document.AllWords.Select(word => word.Text))));
                if (recognized is { Supplier: { } known, Confidence: "high", NeedsConfirmation: false })
                    suggestions = InvoiceTemplateSuggestions.PreferSupplier(suggestions, allTemplates, session.Document, known);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { Logger.LogWarning("Choosing the template by supplier failed ({ErrorType}).", exception.GetType().Name); }
            currentPage = session.Document.Pages[0].Number;
            chosen = Lab.Enabled ? null : suggestions.FirstOrDefault()?.Template;
            autoTemplateId = chosen?.Info.Id; templateChanged = false;   // the laboratory starts from the automatic reading, the one that is measured against the reference
            LoadReading();
            RefreshLab();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceAnalysisException exception) { error = exception.Message; ClearReading(); }
        catch (AccessDeniedException exception) { error = exception.Message; ClearReading(); }
        catch (Exception exception)
        {
            Logger.LogError("Invoice pickup analysis failed ({ErrorType}).", exception.GetType().Name);
            error = "Fișierul nu a putut fi citit. Încearcă din nou sau alt fișier.";
            ClearReading();
        }
        finally { busy = false; }
    }

    // A template that the file was not matched with (made now, or chosen by hand from the whole list) is added to the proposals and used.
    private void ApplyTemplate(InvoiceTemplateRecord template)
    {
        if (suggestions.All(item => item.Template.Info.Id != template.Info.Id))
            suggestions = [new InvoiceTemplateSuggestion(template, InvoiceTemplateEngine.Match(template.Definition, template.Info.SupplierCui, session!.Document, template.Info.SupplierName)), .. suggestions];
        chosen = template;
        LoadReading();
    }

    private void ChooseAnyTemplate(ChangeEventArgs e)
    {
        if (session is null || !int.TryParse(e.Value?.ToString(), out var id)) return;
        if (allTemplates.FirstOrDefault(item => item.Info.Id == id && item.Info.Active && !item.Definition.IsXml) is { } template) ApplyTemplate(template);
    }

    private void OpenTemplateCreation() { if (session is not null) creatingTemplate = true; }
    private void CloseTemplateCreation() => creatingTemplate = false;

    // The template made in the window was saved: the window closes and the template reads the file at once.
    private async Task OnTemplateCreatedAsync(InvoiceTemplateRecord saved)
    {
        creatingTemplate = false;
        if (session is null) return;
        try
        {
            allTemplates = await Templates.GetAllAsync(lifetime.Token);
            ApplyTemplate(allTemplates.FirstOrDefault(item => item.Info.Id == saved.Info.Id) ?? saved);
            notice = $"Șablonul „{saved.Info.Name}” a fost creat și aplicat facturii.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Logger.LogError("Applying the created invoice template failed ({ErrorType}).", exception.GetType().Name);
            error = "Șablonul a fost salvat, dar nu a putut fi aplicat facturii. Alege-l din listă.";
        }
    }

    private void ChooseTemplate(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var id)) return;
        chosen = suggestions.FirstOrDefault(item => item.Template.Info.Id == id)?.Template ?? chosen;
        if (autoTemplateId is not null) templateChanged = chosen?.Info.Id != autoTemplateId;   // the user chose another template than the one proposed
        LoadReading();
    }

    // Reads the file with the detected template (the best saved one for the file); without one, with what the automatic analysis proposes.
    private void LoadReading()
    {
        InvoiceTemplateDefinition? definition = null;
        if (chosen is not null)
        {
            definition = chosen.Definition;
            var match = suggestions.First(item => item.Template.Info.Id == chosen.Info.Id).Match;
            templateInfo = $"Șablon detectat: {chosen.Info.Name} · {SupplierLabel(chosen.Info)} · potrivire {(int)Math.Round(match.Score * 100)}%";
        }
        else if (session!.Analysis.Table is not null)
        {
            definition = InvoiceTemplateDraft.FromAnalysis(session!.Analysis).ToDefinition(session!.Document);
            templateInfo = Lab.Enabled ? "Laborator OCR: citirea automată a fișierului."
                : "Niciun șablon salvat nu se potrivește cu factura: s-a folosit citirea automată. Verifică rândurile și coloanele.";
        }
        else templateInfo = null;

        reading = definition is null ? null : InvoicePickupReader.Read(definition, session!.Document);
        if (reading is not null && reading.Extraction.Columns.Count == 0) reading = null;
        // The template's own overlay (fields with their read values, header, columns) placed on this file the way the engine reads it.
        overlay = reading is null || definition is null ? null
            : chosen is not null ? InvoiceTemplateDraft.FromDefinition(definition, session!.Document, chosen.Info.SupplierName, chosen.Info.SupplierCui)
            : InvoiceTemplateDraft.FromAnalysis(session!.Analysis);
        excluded.Clear(); takenAutoDone.Clear();
        regionPictures.Clear();
        if (createdInvoice is null) headerPrefilledFor = null;   // another reading of the file: the number, date and supplier are read again
        ResetSeparators();
    }

    private void ResetSeparators()
    {
        separators = reading is null ? [] : [.. reading.Separators];
        separatorCounter = separators.Count;
        selectedId = "";
        addMode = false;
        RereadRows();
    }

    private void RereadRows()
    {
        var before = rows.Count;
        rows = reading is null ? [] : InvoicePickupReader.Reread(reading, session!.Document, separators);
        if (rows.Count != before) { excluded.Clear(); takenAutoDone.Clear(); }   // the rows changed: the choices made on the old ones no longer apply
        QueueTakenPreview();
    }

    // The color of a column on the overlay: the one the template editor gives it (by its place among the template's columns).
    private string ColumnColor(string id)
    {
        var index = overlay?.Columns.FindIndex(item => item.Id == id) ?? -1;
        return index < 0 ? "#6b7a80" : $"hsl({(int)(index * 137.5 % 360)} 70% 42%)";
    }

    private string ColumnColor(InvoiceColumn column) => ColumnColor(column.Id);

    // The columns of the table as this page draws them (a following page may be shifted against the first one).
    private IEnumerable<InvoiceColumn> ColumnsOn(InvoicePageData page)
    {
        var columns = reading!.Extraction.Columns;
        var firstPage = separators.Count == 0 ? page.Number : separators.Min(item => item.Page);
        if (page.Number != firstPage && InvoiceTableReader.RepeatedHeader(page, columns) is { } repeated) columns = [.. InvoiceTableReader.ColumnsOnPage(columns, repeated)];
        return columns.Where(column => ShowUnnamedColumns || column.Meaning != InvoiceColumnMeanings.Ignore);
    }

    private void ClearReading() { pick = []; isEFacturaPdf = false; session = null; reading = null; overlay = null; excluded.Clear(); takenAutoDone.Clear(); separators = []; rows = []; templateInfo = null; suggestions = []; allTemplates = []; xmlTemplates = []; creatingTemplate = false; chosen = null; selectedId = ""; ResetInvoiceHeader(); }

    // Called by invoice-separators.js when a line was dragged (move, shorten, lengthen): the rows are read again between the new lines.
    [JSInvokable]
    public Task OnSeparatorChanged(string id, double y, double left, double right)
    {
        var index = separators.FindIndex(item => item.Id == id);
        if (index < 0) return Task.CompletedTask;
        var page = CurrentPageData;
        left = Math.Clamp(left, 0, page.Width);
        right = Math.Clamp(right, left + 1, page.Width);
        separators[index] = separators[index] with { Y = Math.Clamp(y, 0, page.Height), Left = left, Right = right };
        selectedId = id;
        RereadRows();
        return InvokeAsync(StateHasChanged);
    }

    // Called by invoice-separators.js when the page was pressed in "add line" mode.
    [JSInvokable]
    public Task OnSeparatorAdded(int pageNumber, double y)
    {
        if (reading is null) return Task.CompletedTask;
        var page = session!.Document.Pages.FirstOrDefault(item => item.Number == pageNumber) ?? CurrentPageData;
        var columns = reading.Extraction.Columns;
        separatorCounter++;
        var separator = new InvoiceSeparator("s" + separatorCounter.ToString(CultureInfo.InvariantCulture), page.Number, Math.Clamp(y, 0, page.Height), columns.Min(column => column.Left), columns.Max(column => column.Right));
        separators.Add(separator);
        selectedId = separator.Id;
        addMode = false;
        RereadRows();
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnSeparatorDeleteKey()
    {
        DeleteSelected();
        return InvokeAsync(StateHasChanged);
    }
}
