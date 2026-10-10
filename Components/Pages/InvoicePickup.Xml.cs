using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

// Invoice pickup, the XML invoice (UBL / e-Factura, also from a ZIP): read directly, with the XML template of the supplier or the standard UBL paths. No OCR.
public partial class InvoicePickup
{
    // ---- XML invoice: read directly with an XML template (the one of the supplier, else the standard UBL paths) ----

    private IReadOnlyList<InvoiceTemplateRecord> xmlTemplates = [];

    private async Task XmlFileSelectedAsync(IBrowserFile file)
    {
        if (file.Size > InvoiceXmlRules.MaxFileSizeBytes) { error = InvoiceXmlRules.TooLargeMessage; return; }
        busy = true;
        StateHasChanged();
        try
        {
            DiscardSession();
            pick = []; finished = false; finalizeNotice = finalizeError = null; ResetInvoiceHeader();
            byte[] content;
            await using (var stream = file.OpenReadStream(InvoiceXmlRules.MaxFileSizeBytes, lifetime.Token))
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory, lifetime.Token);
                content = memory.ToArray();
            }
            var (xmlContent, xmlName) = InvoiceXmlReader.Unpack(content, file.Name);
            session = await Analysis.OpenXmlAsync(xmlContent, xmlName, lifetime.Token);
            creatingTemplate = false;
            allTemplates = await Templates.GetAllAsync(lifetime.Token);
            xmlTemplates = [.. allTemplates.Where(template => template.Info.Active && template.Definition.IsXml)];
            // The template of the supplier named by the file (its tax id) is used; without one the standard UBL paths read the file.
            var digits = SupplierRules.CuiDigits(session.SupplierCuiFromXml);
            chosen = digits.Length == 0 ? null : xmlTemplates.FirstOrDefault(template => SupplierRules.CuiDigits(template.Info.SupplierCui) == digits);
            suggestions = [];
            autoTemplateId = chosen?.Info.Id; templateChanged = false;
            currentPage = 1;
            LoadXmlReading();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (InvoiceAnalysisException exception) { error = exception.Message; ClearReading(); }
        catch (AccessDeniedException exception) { error = exception.Message; ClearReading(); }
        catch (Exception exception)
        {
            Logger.LogError("XML invoice pickup failed ({ErrorType}).", exception.GetType().Name);
            error = "Fișierul XML nu a putut fi citit. Încearcă din nou sau alt fișier.";
            ClearReading();
        }
        finally { busy = false; }
    }

    private void LoadXmlReading()
    {
        var mapping = chosen?.Definition.Xml ?? InvoiceXmlMapping.Ubl;
        var extraction = InvoiceXmlReader.Read(session!.Xml!, mapping);
        reading = new InvoicePickupReading(extraction, [], "");
        overlay = null;
        rows = extraction.Rows;
        session.Analysis = session.Analysis with { Warnings = extraction.Warnings };
        templateInfo = chosen is not null ? $"Șablon XML detectat: {chosen.Info.Name} · {SupplierLabel(chosen.Info)}"
            : "Nu există un șablon XML pentru acest furnizor: factura este citită cu căile standard UBL. Verifică rândurile; un șablon propriu se creează în Setări → Facturi → Șabloane salvate.";
        excluded.Clear(); takenAutoDone.Clear();
        regionPictures.Clear();
        if (createdInvoice is null) headerPrefilledFor = null;
        separators = [];
        QueueTakenPreview();
    }

    private void ChooseXmlTemplate(ChangeEventArgs e)
    {
        if (session is not { IsXml: true } || !int.TryParse(e.Value?.ToString(), out var id)) return;
        chosen = xmlTemplates.FirstOrDefault(template => template.Info.Id == id);
        if (autoTemplateId is not null || chosen is not null) templateChanged = chosen?.Info.Id != autoTemplateId;
        LoadXmlReading();
    }

    private string XmlEntryDescription(PickRow line)
    {
        // The description the XML template of the supplier says (labels of the linked elements); a template without one gets the plain text below.
        if (chosen?.Definition.ProductDescription is { Length: > 0 } template && reading is not null)
            return InvoiceXmlDescription.RenderRow(template, reading.Extraction, line.Cells, invoiceNumber, invoiceSupplierName);
        string Field(string meaning) => reading?.Extraction.Fields.FirstOrDefault(field => field.Meaning == meaning)?.Value ?? "";
        return $"Factura {(invoiceNumber.Length > 0 ? invoiceNumber : Field(InvoiceFieldMeanings.InvoiceNumber))}" +
            (invoiceSupplierName.Length > 0 ? $" · {invoiceSupplierName}" : "") + " (preluată din XML)";
    }
}
