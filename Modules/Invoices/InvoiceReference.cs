using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlazorStoc.Services;

// The geometry of the line table of one invoice as the user wants it (the "ground truth" the engines are measured against): where the header
// ends, the columns (their labels and edges) and the lines that separate the rows. Written by tests/BlazorStoc.InvoiceCorpus --write-reference
// from what an engine found, then corrected by hand (move a line, fix a column edge, rename a label), and kept next to the invoices, outside
// the repository: it holds no values from the invoice, only positions and the labels of the table header.
public sealed class InvoiceReference
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public string File { get; set; } = "";
    public int HeaderPage { get; set; } = 1;
    public double HeaderBottom { get; set; }
    public List<ReferenceColumn> Columns { get; set; } = [];
    // The lines between the rows, page by page: the first of a page is the top of its first row, the last the bottom of its last row.
    public List<ReferenceSeparator> Separators { get; set; } = [];

    // Rows are the boxes between two lines of the same page.
    [JsonIgnore]
    public int RowCount => Separators.GroupBy(separator => separator.Page).Sum(page => Math.Max(0, page.Count() - 1));

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static InvoiceReference FromJson(string json)
    {
        var reference = JsonSerializer.Deserialize<InvoiceReference>(json, Options) ?? throw new InvalidOperationException("Fisierul de referinta nu poate fi citit.");
        if (reference.Schema != CurrentSchema) throw new InvalidOperationException("Fisierul de referinta are o versiune necunoscuta a formatului.");
        return reference;
    }

    // The geometry an engine found, as a first draft of the reference.
    public static InvoiceReference From(string file, InvoiceTable table, IReadOnlyList<InvoiceSeparator> separators) => new()
    {
        File = file, HeaderPage = table.HeaderPage, HeaderBottom = Math.Round(table.HeaderBottom, 1),
        Columns = [.. table.Columns.Select(column => new ReferenceColumn { Label = column.Label, Meaning = column.Meaning, Left = Math.Round(column.Left, 1), Right = Math.Round(column.Right, 1) })],
        Separators = [.. separators.Select(separator => new ReferenceSeparator { Page = separator.Page, Y = Math.Round(separator.Y, 1) })]
    };
}

public sealed class ReferenceColumn
{
    public string Label { get; set; } = "";
    public string Meaning { get; set; } = InvoiceColumnMeanings.Ignore;
    public double Left { get; set; }
    public double Right { get; set; }
}

public sealed class ReferenceSeparator
{
    public int Page { get; set; } = 1;
    public double Y { get; set; }
}

// How close an engine came to the reference, by geometry: no value read in a cell enters it.
public sealed record InvoiceReferenceMatch(bool TableFound, double HeaderBottomError, int RowsExpected, int RowsFound,
    double SeparatorPrecision, double SeparatorRecall, double ColumnPrecision, double ColumnRecall, int ColumnMeaningsRight, int ColumnsMatched)
{
    public static double F1(double precision, double recall) => precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

    public double SeparatorF1 => F1(SeparatorPrecision, SeparatorRecall);
    public double ColumnF1 => F1(ColumnPrecision, ColumnRecall);
    public double RowCountScore => TableFound ? 1.0 - Math.Abs(RowsFound - RowsExpected) / (double)Math.Max(1, Math.Max(RowsFound, RowsExpected)) : 0;

    // One number to compare engines by: the lines between the rows weigh most, then the columns, then the number of rows.
    public double Score => TableFound ? 0.5 * SeparatorF1 + 0.3 * ColumnF1 + 0.2 * RowCountScore : 0;
}

public static class InvoiceReferenceComparer
{
    public const double SeparatorTolerance = 3;   // points: a line within this distance of the reference line is that line
    public const double ColumnTolerance = 6;      // points: a column whose both edges are within this of the reference column's is that column

    public static InvoiceReferenceMatch Compare(InvoiceReference reference, InvoiceTable? table, IReadOnlyList<InvoiceSeparator> separators)
    {
        if (table is null) return new InvoiceReferenceMatch(false, double.NaN, reference.RowCount, 0, 0, 0, 0, 0, 0, 0);

        // Lines: each reference line is matched to the nearest unused line of the same page within the tolerance.
        var used = new HashSet<InvoiceSeparator>();
        var matchedSeparators = 0;
        foreach (var wanted in reference.Separators)
        {
            var found = separators.Where(item => item.Page == wanted.Page && !used.Contains(item) && Math.Abs(item.Y - wanted.Y) <= SeparatorTolerance)
                .OrderBy(item => Math.Abs(item.Y - wanted.Y)).FirstOrDefault();
            if (found is null) continue;
            used.Add(found);
            matchedSeparators++;
        }

        // Columns: matched by both edges.
        var usedColumns = new HashSet<InvoiceColumn>();
        var matchedColumns = 0;
        var meaningsRight = 0;
        foreach (var wanted in reference.Columns)
        {
            // The same column: the two extents overlap along most of the narrower one (a reading may give a column the whole room up to its neighbours,
            // another only the width of its label; both are that column).
            bool Same(InvoiceColumn item) => !usedColumns.Contains(item) && Overlap(item.Left, item.Right, wanted.Left, wanted.Right) >= 0.6 * Math.Max(1, Math.Min(item.Right - item.Left, wanted.Right - wanted.Left)) &&
                Math.Abs((item.Left + item.Right) / 2 - (wanted.Left + wanted.Right) / 2) <= Math.Max(ColumnTolerance, 0.5 * Math.Max(item.Right - item.Left, wanted.Right - wanted.Left));
            var found = table.Columns.Where(Same).OrderBy(item => Math.Abs(item.Left - wanted.Left) + Math.Abs(item.Right - wanted.Right)).FirstOrDefault();
            if (found is null) continue;
            usedColumns.Add(found);
            matchedColumns++;
            if (found.Meaning == wanted.Meaning) meaningsRight++;
        }

        return new InvoiceReferenceMatch(true, Math.Abs(table.HeaderBottom - reference.HeaderBottom), reference.RowCount, RowCountOf(separators),
            separators.Count == 0 ? 0 : matchedSeparators / (double)separators.Count, reference.Separators.Count == 0 ? 0 : matchedSeparators / (double)reference.Separators.Count,
            table.Columns.Count == 0 ? 0 : matchedColumns / (double)table.Columns.Count, reference.Columns.Count == 0 ? 0 : matchedColumns / (double)reference.Columns.Count,
            meaningsRight, matchedColumns);
    }

    private static double Overlap(double a1, double a2, double b1, double b2) => Math.Max(0, Math.Min(a2, b2) - Math.Max(a1, b1));

    private static int RowCountOf(IReadOnlyList<InvoiceSeparator> separators) => separators.GroupBy(separator => separator.Page).Sum(page => Math.Max(0, page.Count() - 1));
}
