using System.Globalization;

namespace BlazorStoc.Services;

// A demarcation line of the line table of an invoice (Produse -> Preluare factura): a horizontal line at Y on a page, between Left and Right
// (page points). The rows of the table are the boxes between two consecutive lines of a page; what is above the first line and below the last
// line of a page is not read. The lines are the user's to move, resize, delete and add; the rows are read again from them each time.
public sealed record InvoiceSeparator(string Id, int Page, double Y, double Left, double Right);

// What a template reads from a file for the invoice pickup: the extraction (fields, columns, rows) and the demarcation lines of the rows.
public sealed record InvoicePickupReading(InvoiceExtraction Extraction, IReadOnlyList<InvoiceSeparator> Separators, string NameCodeSeparator);

public static partial class InvoiceTableReader
{
    private const double MinSeparatorBand = 1;

    // The demarcation lines the rows were read between: the top of the first row of each page and the bottom of every row. Where the table is
    // ruled these are its rules; where the rows are told apart only by the space between them, the lines are drawn in that space.
    public static List<InvoiceSeparator> SeparatorsFromRows(IReadOnlyList<InvoiceTableRow> rows, IReadOnlyList<InvoiceColumn> columns)
    {
        var separators = new List<InvoiceSeparator>();
        if (rows.Count == 0 || columns.Count == 0) return separators;
        var left = columns.Min(column => column.Left);
        var right = columns.Max(column => column.Right);
        var counter = 0;
        foreach (var page in rows.GroupBy(row => row.Page).OrderBy(group => group.Key))
        {
            var last = double.NegativeInfinity;
            foreach (var row in page.OrderBy(row => row.Top))
            {
                if (row.Bottom <= row.Top) continue;
                if (row.Top - last > MinSeparatorBand) Add(page.Key, row.Top);
                if (row.Bottom - last > MinSeparatorBand) Add(page.Key, row.Bottom);
            }
            void Add(int number, double y)
            {
                counter++;
                separators.Add(new InvoiceSeparator("s" + counter.ToString(CultureInfo.InvariantCulture), number, y, left, right));
                last = y;
            }
        }
        return separators;
    }

    // Reads the rows between the demarcation lines: on each page the boxes between consecutive lines are the rows (a box without text is not a
    // row); nothing above the first or below the last line of the page is read. The columns are the ones of the template.
    public static List<InvoiceTableRow> ReadBySeparators(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, IReadOnlyList<InvoiceSeparator> separators, char? hint)
    {
        var rows = new List<InvoiceTableRow>();
        if (columns.Count == 0) return rows;
        var indexColumn = columns.ToList().FindIndex(column => column.Meaning == InvoiceColumnMeanings.Index);
        foreach (var page in document.Pages.OrderBy(page => page.Number))
        {
            var lines = separators.Where(separator => separator.Page == page.Number).Select(separator => separator.Y).OrderBy(y => y).ToList();
            if (lines.Count < 2) continue;
            var cells = AssignCells(page, columns, lines[0]).Where(cell => cell.Segment.CenterY < lines[^1]).ToList();
            var bands = lines.Zip(lines.Skip(1), (top, bottom) => (Top: top, Bottom: bottom)).Where(band => band.Bottom - band.Top >= MinSeparatorBand).ToList();
            var filled = bands.Select(band => cells.Where(cell => cell.Segment.CenterY >= band.Top && cell.Segment.CenterY < band.Bottom).ToList())
                .Select((inBand, index) => (Index: index, Cells: inBand)).Where(item => item.Cells.Count > 0 && !IsColumnNumberRow(item.Cells.Select(cell => cell.Segment.Text))).ToList();
            // A column whose text sits at another height than its row is read by order: its k-th value belongs to the k-th row.
            var sequences = new Dictionary<int, List<string>>();
            for (var column = 0; column < columns.Count; column++)
            {
                if (columns[column].RowMapping != InvoiceRowMapping.Sequence) continue;
                var items = cells.Where(cell => cell.Column == column).GroupBy(cell => cell.Segment.Line).OrderBy(group => group.Key)
                    .Select(group => InvoiceLayout.TextOf(group.SelectMany(cell => cell.Segment.Words))).ToList();
                if (items.Count == filled.Count) sequences[column] = items;
            }
            for (var order = 0; order < filled.Count; order++)
            {
                var (index, inBand) = filled[order];
                var values = new Dictionary<string, string>();
                for (var column = 0; column < columns.Count; column++)
                {
                    if (sequences.TryGetValue(column, out var ordered)) { values[columns[column].Id] = ordered[order]; continue; }
                    var words = inBand.Where(cell => cell.Column == column).SelectMany(cell => cell.Segment.Words).ToList();
                    values[columns[column].Id] = words.Count == 0 ? "" : InvoiceLayout.TextOf(words);
                }
                int? number = indexColumn >= 0 ? ParseIndex(values[columns[indexColumn].Id]) : null;
                rows.Add(new InvoiceTableRow(page.Number, number, values, [], bands[index].Top, bands[index].Bottom));
            }
        }
        return Validate(rows, columns, hint);
    }
}

// Reads an invoice for the pickup: the template (or the automatic proposal) gives the fields and the columns, the demarcation lines give the rows.
public static class InvoicePickupReader
{
    public static InvoicePickupReading Read(InvoiceTemplateDefinition definition, InvoiceDocument document)
    {
        var extraction = InvoiceTemplateEngine.Apply(definition, document);
        var separators = InvoiceTableReader.SeparatorsFromRows(extraction.Rows, extraction.Columns);
        return new InvoicePickupReading(extraction, separators, definition.Table?.NameCodeSeparator ?? "");
    }

    // The rows between the (edited) demarcation lines, with the columns the template read.
    public static IReadOnlyList<InvoiceTableRow> Reread(InvoicePickupReading reading, InvoiceDocument document, IReadOnlyList<InvoiceSeparator> separators)
    {
        var hint = InvoiceValues.DecimalStyle(document.AllWords.Select(word => word.Text));
        IReadOnlyList<InvoiceTableRow> rows = InvoiceTableReader.ReadBySeparators(document, reading.Extraction.Columns, separators, hint);
        if (reading.NameCodeSeparator.Length > 0) rows = InvoiceTemplateEngine.SplitNameCode(rows, reading.Extraction.Columns, reading.NameCodeSeparator);
        return rows;
    }
}
