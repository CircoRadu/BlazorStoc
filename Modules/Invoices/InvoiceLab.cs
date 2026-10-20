using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Services;

// The OCR laboratory inside the invoice pickup page (only when switched on: Invoices:Lab = true): the reading is measured
// against the reference geometry of the file, and what the user corrected on the page can be saved as that reference.
public sealed class InvoiceLabSettings(IConfiguration configuration)
{
    public bool Enabled => string.Equals(configuration["Invoices:Lab"], "true", StringComparison.OrdinalIgnoreCase);

    // Where the reference files are kept (outside the repository, next to the invoices: Invoices:LabReferencesDirectory).
    public string? ReferencesDirectory => string.IsNullOrWhiteSpace(configuration["Invoices:LabReferencesDirectory"]) ? null : configuration["Invoices:LabReferencesDirectory"]!.Trim();
}

public sealed record InvoiceLabResult(InvoiceTable? Table, IReadOnlyList<InvoiceSeparator> Separators, int RowsWithProblems, InvoiceReferenceMatch? Match);

public static class InvoiceLab
{
    // The reference of an uploaded file: <folder>/<file name without extension>.reference.json (the name the corpus tool uses).
    public static string ReferencePath(string directory, string fileName) =>
        Path.Combine(directory, Path.GetFileNameWithoutExtension(Path.GetFileName(fileName)) + ".reference.json");

    public static InvoiceReference? LoadReference(string? directory, string fileName)
    {
        if (directory is null) return null;
        var path = ReferencePath(directory, fileName);
        return File.Exists(path) ? InvoiceReference.FromJson(File.ReadAllText(path)) : null;
    }

    public static string SaveReference(string directory, InvoiceReference reference)
    {
        Directory.CreateDirectory(directory);
        var path = ReferencePath(directory, reference.File);
        File.WriteAllText(path, reference.ToJson());
        return path;
    }

    // What the user has on the page - the columns of the reading, the header, the demarcation lines as they were corrected - as the reference.
    public static InvoiceReference BuildReference(string fileName, int headerPage, double headerBottom, IEnumerable<InvoiceColumn> columns, IEnumerable<InvoiceSeparator> separators) => new()
    {
        File = Path.GetFileName(fileName), HeaderPage = headerPage, HeaderBottom = Math.Round(headerBottom, 1),
        Columns = [.. columns.Select(column => new ReferenceColumn { Label = column.Label, Meaning = column.Meaning, Left = Math.Round(column.Left, 1), Right = Math.Round(column.Right, 1) })],
        Separators = [.. separators.OrderBy(separator => separator.Page).ThenBy(separator => separator.Y).Select(separator => new ReferenceSeparator { Page = separator.Page, Y = Math.Round(separator.Y, 1) })]
    };

    // The reading of the document, measured against the reference when there is one.
    public static InvoiceLabResult Evaluate(InvoiceDocument document, InvoiceReference? reference)
    {
        var analysis = InvoiceAnalyzer.Analyze(document);
        var separators = analysis.Table is { } table ? InvoiceTableReader.SeparatorsFromRows(table.Rows, table.Columns, document.Pages) : [];
        var problems = analysis.Table?.Rows.Count(row => row.Flags.Count > 0) ?? 0;
        return new InvoiceLabResult(analysis.Table, separators, problems, reference is null ? null : InvoiceReferenceComparer.Compare(reference, analysis.Table, separators));
    }
}
