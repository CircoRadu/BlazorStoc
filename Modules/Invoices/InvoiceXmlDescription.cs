namespace BlazorStoc.Services;

// The description template of the stock entry of a product taken from an XML invoice: the same text with <label> marks as the PDF templates,
// the labels being the elements the XML template links (the header values and the values of a line).
public static class InvoiceXmlDescription
{
    // Header labels: the name of the field the reader gives (InvoiceXmlReader.Read) and the id of the target that must be linked.
    private static readonly (string Label, string TargetId)[] HeaderLabels =
    [
        ("Număr factură", "number"), ("Data facturii", "date"), ("Furnizor", "supplierName"), ("CUI furnizor", "supplierCui"), ("Moneda", "currency"), ("Total factură", "total")
    ];

    // Line labels: the label, the id of the cell of the row.
    private static readonly (string Label, string CellId)[] LineLabels =
    [
        ("Cod furnizor", "code"), ("Denumire", "name"), ("Cantitate", "quantity"), ("UM", "unit"), ("Preț unitar", "unitPrice"), ("Valoare", "value")
    ];

    // The labels the template can use: the number of the invoice always, the others when the template links them.
    public static IReadOnlyList<string> HeaderOf(InvoiceXmlMapping mapping) =>
        [.. HeaderLabels.Where(item => item.TargetId == "number" || Linked(mapping, item.TargetId)).Select(item => item.Label)];

    public static IReadOnlyList<string> LinesOf(InvoiceXmlMapping mapping) =>
        [.. LineLabels.Where(item => Linked(mapping, item.CellId)).Select(item => item.Label)];

    public static IReadOnlyList<string> Labels(InvoiceXmlMapping mapping) => [.. HeaderOf(mapping), .. LinesOf(mapping)];

    // The text a new template starts with (the one the pickup wrote until now).
    public static string Default(InvoiceXmlMapping mapping)
    {
        var header = HeaderOf(mapping);
        return "Factura <Număr factură>" + (header.Contains("Furnizor") ? " · <Furnizor>" : "") + " (preluată din XML)";
    }

    public static IReadOnlyList<string> Problems(string template, InvoiceXmlMapping mapping)
    {
        var problems = new List<string>();
        problems.AddRange(InvoiceProductDescription.OperationProblems(template));
        if ((template ?? "").Length > InvoiceProductDescription.MaxLength) problems.Add($"Descrierea intrării în stoc a produsului poate avea cel mult {InvoiceProductDescription.MaxLength} de caractere.");
        foreach (var mark in InvoiceProductDescription.UnknownMarks(template ?? "", Labels(mapping))) problems.Add($"Eticheta <{mark}> din descrierea intrării în stoc nu există în șablon.");
        return problems;
    }

    // The description of the entry of one row: header values from what the file says (the number and the supplier as the user edited them), the line values
    // from the cells of the row. A label the row has no value for is left as written.
    public static string RenderRow(string template, InvoiceExtraction extraction, IReadOnlyDictionary<string, string> cells, string number = "", string supplier = "") =>
        InvoiceProductDescription.Render(template, label =>
        {
            var key = InvoiceValues.Normalize(label);
            if (key == InvoiceValues.Normalize("Număr factură") && number.Length > 0) return number;
            if (key == InvoiceValues.Normalize("Furnizor") && supplier.Length > 0) return supplier;
            if (extraction.Fields.FirstOrDefault(field => field.Found && InvoiceValues.Normalize(field.Name) == key) is { } found) return found.Value;
            if (LineLabels.FirstOrDefault(item => InvoiceValues.Normalize(item.Label) == key) is { Label: not null } line)
                return cells.TryGetValue(line.CellId, out var cell) && cell.Length > 0 ? cell : null;
            return null;
        });

    // A readable sample: the first row of the file read with the template, a note in ‹ › where there is nothing to show.
    public static string RenderSample(string template, InvoiceExtraction? preview) =>
        InvoiceProductDescription.Render(template, label =>
        {
            var key = InvoiceValues.Normalize(label);
            if (preview?.Fields.FirstOrDefault(field => field.Found && InvoiceValues.Normalize(field.Name) == key) is { } found) return found.Value;
            if (LineLabels.FirstOrDefault(item => InvoiceValues.Normalize(item.Label) == key) is not { Label: not null } line)
                return HeaderLabels.Any(item => InvoiceValues.Normalize(item.Label) == key) ? "‹" + label.ToLowerInvariant() + "›" : null;
            return preview?.Rows.FirstOrDefault() is { } row && row.Cells.TryGetValue(line.CellId, out var cell) && cell.Length > 0 ? cell : "‹" + label.ToLowerInvariant() + "›";
        });

    private static bool Linked(InvoiceXmlMapping mapping, string id) => InvoiceXmlRules.Alternatives(InvoiceXmlTree.PathOf(mapping, id)).Any();
}
