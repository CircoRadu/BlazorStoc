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
        var fields = AssignLeadingTaxCode(RegistryAsTaxCode(PromoteTableTotal(InvoiceFieldFinder.Find(document, consumed, hint), table, hint), document));
        foreach (var page in document.Pages.Where(page => page.Words.Count < InvoiceAnalysisRules.MinTextWords))
            warnings.Add($"Pagina {page.Number} conține foarte puțin text citibil.");
        if (document.Pages.Any(page => page.Source == InvoiceSources.Ocr))
            warnings.Add("Cel puțin o pagină a fost citită prin recunoașterea imaginii (OCR): verifică atent valorile.");
        return new InvoiceAnalysis(document.Pages, fields, table, warnings);
    }

    // In the PDF printed from e-Factura the "Nr. inregistrare" of the seller is the CUI/CIF (the CompanyID of the party), not the number of the trade registry.
    // It becomes the tax code of the supplier; the "Identificatorul TVA" (the same code with the VAT prefix) stays as a field without a role of its own, so that a
    // template does not read two fields with the same role.
    private static List<InvoiceHeaderField> RegistryAsTaxCode(List<InvoiceHeaderField> fields, InvoiceDocument document)
    {
        if (!InvoiceEFacturaPdf.IsExport(document.AllWords.Select(word => word.Text))) return fields;
        var registry = fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.SupplierRegistry && InvoiceValues.NormalizeCui(field.Value).Length > 0);
        if (registry is null) return fields;
        return fields.Select(field => field == registry ? field with { Meaning = InvoiceFieldMeanings.SupplierCui }
            : field.Meaning == InvoiceFieldMeanings.SupplierCui ? field with { Meaning = "" } : field).ToList();
    }

    // An invoice whose supplier has no heading (the letterhead at the top, no "Furnizor"/"Vanzator", or a heading OCR could not read) leaves
    // its tax code without a party. When no supplier tax code was found, the first tax code that belongs to no party, in reading order, is
    // the supplier's: the supplier's block comes first on the page.
    private static List<InvoiceHeaderField> AssignLeadingTaxCode(List<InvoiceHeaderField> fields)
    {
        if (fields.Any(field => field.Meaning == InvoiceFieldMeanings.SupplierCui)) return fields;
        var first = fields
            .Where(field => field.Meaning.Length == 0 && field.Section.Length == 0 && InvoiceValues.NormalizeCui(field.Value).Length > 0 &&
                            InvoiceVocabulary.MatchFieldLabelPrefix(InvoiceVocabulary.Tokens(field.Label)) is { Meaning: "@cui" } match &&
                            match.Words == InvoiceVocabulary.Tokens(field.Label).Length)
            .OrderBy(field => field.LabelBox.Page).ThenBy(field => field.LabelBox.Y).ThenBy(field => field.LabelBox.X)
            .FirstOrDefault();
        return first is null ? fields
            : fields.Select(field => field == first ? field with { Meaning = InvoiceFieldMeanings.SupplierCui, Section = InvoiceVocabulary.SupplierSection } : field).ToList();
    }

    // Many invoices close the table with a bare "Total" line whose amounts stand under the value and VAT columns ("Total  2,328.64  489.02"):
    // the word alone says nothing (it may be the total with VAT), but its amount under the value column, or equal to the sum of the rows, is
    // the total without VAT. Applied only when no total without VAT was found by its label.
    private static List<InvoiceHeaderField> PromoteTableTotal(List<InvoiceHeaderField> fields, InvoiceTable? table, char? hint)
    {
        var value = table?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
        if (table is null || value is null || fields.Any(field => field.Meaning == InvoiceFieldMeanings.TotalNet)) return fields;
        var sum = table.Rows.Sum(row => row.Cells.TryGetValue(value.Id, out var cell) ? InvoiceValues.ParseNumber(cell, hint) ?? 0 : 0);
        var candidates = fields
            .Where(field => field.Meaning.Length == 0 && InvoiceVocabulary.Tokens(field.Label) is ["total"] && InvoiceValues.ParseNumber(field.Value, hint) is not null &&
                            field.LabelBox.Page >= table.HeaderPage)
            .ToList();
        var chosen = candidates.FirstOrDefault(field => Math.Abs(InvoiceValues.ParseNumber(field.Value, hint)!.Value - sum) <= 0.05m)
            ?? candidates.FirstOrDefault(field => InvoiceLayout.Overlap(field.ValueBox.X, field.ValueBox.Right, value.Left, value.Right) > 0.5 * field.ValueBox.Width);
        return chosen is null ? fields : fields.Select(field => field == chosen ? field with { Meaning = InvoiceFieldMeanings.TotalNet } : field).ToList();
    }
}
