namespace BlazorStoc.Services;

// Turns the words of an invoice into a proposal of template: the line table with its columns and meanings, and the label/value pairs
// of the rest of the page. The proposal is only a starting point - the user confirms or corrects it in the interactive template.
public static class InvoiceAnalyzer
{
    public static InvoiceAnalysis Analyze(InvoiceDocument document)
    {
        var hint = InvoiceValues.DecimalStyle(document.AllWords.Select(word => word.Text));
        var warnings = new List<string>();
        var (table, consumed) = InvoiceTableReader.Detect(document, hint);
        if (table is null) warnings.Add("Nu s-a găsit tabelul cu liniile facturii; poți adăuga coloanele manual.");
        else if (table.ByStructure) warnings.Add("Antetul tabelului nu a fost recunoscut după etichete (alt limbaj sau alt format): tabelul a fost găsit după numerotarea liniilor. Verifică sensul fiecărei coloane.");
        else if (table.Rows.Count == 0) warnings.Add("Antetul tabelului a fost găsit, dar nu s-au putut citi rânduri sub el.");
        var fields = InvoiceFieldFinder.Find(document, consumed, hint);
        foreach (var page in document.Pages.Where(page => page.Words.Count < InvoiceAnalysisRules.MinTextWords))
            warnings.Add($"Pagina {page.Number} conține foarte puțin text citibil.");
        if (document.Pages.Any(page => page.Source == InvoiceSources.Ocr))
            warnings.Add("Cel puțin o pagină a fost citită prin recunoașterea imaginii (OCR): verifică atent valorile.");
        return new InvoiceAnalysis(document.Pages, fields, table, warnings);
    }
}
