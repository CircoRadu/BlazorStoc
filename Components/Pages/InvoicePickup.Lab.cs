using System.Globalization;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace BlazorStoc.Components.Pages;

// Invoice pickup, OCR laboratory (Invoices:Lab = true): the reading measured against the reference of the file, the reference saved from the page.
public partial class InvoicePickup
{
    // ---- OCR laboratory (Invoices:Lab = true): the reading against the reference of the file, the reference saved from the page ----

    private InvoiceLabResult? labResult;
    private InvoiceReference? labReference;
    private string? labNotice;
    private void RefreshLab()
    {
        if (!Lab.Enabled || session is null) { labResult = null; labReference = null; return; }
        try { labReference = InvoiceLab.LoadReference(Lab.ReferencesDirectory, session.FileName); }
        catch (Exception exception) when (exception is InvalidOperationException or System.Text.Json.JsonException or IOException) { labReference = null; labNotice = "Fișierul de referință nu poate fi citit: " + exception.Message; }
        labResult = InvoiceLab.Evaluate(session.Document, labReference);
    }

    private void SaveReference()
    {
        if (!Lab.Enabled || reading is null || session is null) return;
        if (Lab.ReferencesDirectory is not { } directory) { labNotice = "Folderul pentru referințe nu este configurat (Invoices:LabReferencesDirectory)."; return; }
        try
        {
            var reference = InvoiceLab.BuildReference(session.FileName, overlay?.HeaderPage ?? 1, overlay?.HeaderBottom ?? 0, reading.Extraction.Columns, separators);
            labNotice = "Referința a fost salvată: " + InvoiceLab.SaveReference(directory, reference);
            RefreshLab();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.LogError("Invoice reference saving failed ({ErrorType}).", exception.GetType().Name);
            labNotice = "Referința nu a putut fi salvată: folderul nu poate fi scris.";
        }
    }

    private static string Pct(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
