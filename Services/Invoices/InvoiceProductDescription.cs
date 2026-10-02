using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// The description template of a product taken from an invoice: free text with <label> marks, the same way the notification templates
// work. A mark names a label of the template (a used header field or a used table column); "Cod produs" exists in every template and has
// no value while the template is made: the product codes are read when an invoice is really imported with the template.
public static partial class InvoiceProductDescription
{
    public const string ProductCodeLabel = "Cod produs";
    public const int MaxLength = 400;

    [GeneratedRegex("<([^<>\\r\\n]{1,80})>")]
    private static partial Regex Mark();

    // The labels that can be put in the description: the product code first, then the used header fields and the used table columns.
    public static IReadOnlyList<(string Label, string Description)> Labels(InvoiceTemplateDraft draft)
    {
        var result = new List<(string, string)> { (ProductCodeLabel, "Codul produsului (se citește la importul facturii)") };
        void Add(string label, string description)
        {
            if (label.Length > 0 && result.All(item => InvoiceValues.Normalize(item.Item1) != InvoiceValues.Normalize(label))) result.Add((label, description));
        }
        foreach (var field in draft.Fields.Where(field => field.Use))
            Add(InvoiceTemplateDraft.EffectiveLabel(field), "Câmp din antet");
        foreach (var column in draft.Columns.Where(column => column.Use && column.Meaning is not (InvoiceColumnMeanings.Ignore or InvoiceColumnMeanings.Index or InvoiceColumnMeanings.Code)))
            Add(InvoiceVocabulary.ColumnTitle(column.Meaning), "Coloană din tabel");
        return result;
    }

    // The text with every mark replaced by its value (value returns null for a label it does not know: the mark is left as written).
    public static string Render(string template, Func<string, string?> value) =>
        Mark().Replace(template ?? "", match => value(match.Groups[1].Value.Trim()) ?? match.Value);

    // The marks of the text that are not labels of the template.
    public static IReadOnlyList<string> UnknownMarks(string template, IEnumerable<string> labels)
    {
        var known = labels.Select(InvoiceValues.Normalize).ToHashSet();
        return Mark().Matches(template ?? "").Select(match => match.Groups[1].Value.Trim()).Where(mark => !known.Contains(InvoiceValues.Normalize(mark))).Distinct().ToList();
    }

    // A readable sample for the preview: field values from what the template reads, the first table row for columns, a note for the code.
    public static string RenderSample(InvoiceTemplateDraft draft, InvoiceExtraction? preview)
    {
        string? Lookup(string label)
        {
            var key = InvoiceValues.Normalize(label);
            if (key == InvoiceValues.Normalize(ProductCodeLabel)) return "‹cod produs›";
            if (preview is null) return null;
            if (preview.Fields.FirstOrDefault(field => field.Found && InvoiceValues.Normalize(field.Name) == key) is { } found) return found.Value;
            var column = preview.Columns.FirstOrDefault(item => InvoiceValues.Normalize(InvoiceVocabulary.ColumnTitle(item.Meaning)) == key);
            var row = preview.Rows.FirstOrDefault();
            return column is not null && row is not null && row.Cells.TryGetValue(column.Id, out var cell) ? cell : null;
        }
        return Render(draft.ProductDescription, Lookup);
    }

}
