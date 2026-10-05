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

    // How far (points) above the first row / below the last row of a page a rule still counts as the border of the table.
    private const double EdgeRuleReach = 25;

    // The demarcation lines the rows were read between: the top of the first row of each page and the bottom of every row. Where the table is
    // ruled these are its rules (a row's text extent is not the row: a ruled row is the box between two rules, whatever the text inside);
    // where the rows are told apart only by the space between them, the lines are drawn in that space, midway between two rows.
    public static List<InvoiceSeparator> SeparatorsFromRows(IReadOnlyList<InvoiceTableRow> rows, IReadOnlyList<InvoiceColumn> columns, IReadOnlyList<InvoicePageData>? pages = null)
    {
        var separators = new List<InvoiceSeparator>();
        if (rows.Count == 0 || columns.Count == 0) return separators;
        var counter = 0;
        var firstPage = rows.Min(row => row.Page);
        // The row of column numbers under the header of the first page (the second line of a two-line header): the table starts under it, and a following page may repeat only it.
        var firstTop = rows.Where(row => row.Page == firstPage).Min(row => row.Top);
        var firstNumbers = pages?.FirstOrDefault(item => item.Number == firstPage) is { } firstData && double.IsFinite(firstTop) ? FindNumberHeader(firstData, firstTop - 80, firstTop + 60) : null;
        foreach (var page in rows.GroupBy(row => row.Page).OrderBy(group => group.Key))
        {
            // The columns as this page draws them (a following page may be shifted or scaled against the first one).
            var pageData = pages?.FirstOrDefault(item => item.Number == page.Key);
            var pageColumns = page.Key != firstPage && pageData is not null ? ColumnsOfFollowingPage(pageData, columns, firstNumbers) : columns;
            var left = pageColumns.Min(column => column.Left);
            var right = pageColumns.Max(column => column.Right);
            var width = right - left;
            var rules = (pageData?.Rules ?? [])
                .Where(rule => !rule.Vertical && InvoiceLayout.Overlap(left, right, rule.From, rule.To) >= 0.6 * width).Select(rule => rule.Position).OrderBy(y => y).ToList();
            var ordered = page.Where(row => row.Bottom > row.Top).OrderBy(row => row.Top).ToList();
            var last = double.NegativeInfinity;
            for (var index = 0; index < ordered.Count; index++)
            {
                var row = ordered[index];
                if (index == 0)
                {
                    // The first line is under the row of column numbers when the table has one (it is not a row of goods): a rule above that row is not the edge of the first row.
                    var numbersBottom = page.Key == firstPage && firstNumbers is not null ? firstNumbers.Bottom : double.NegativeInfinity;
                    var above = rules.Where(y => y <= row.Top + 2 && y >= row.Top - EdgeRuleReach && y >= numbersBottom - 1).DefaultIfEmpty(double.NaN).Max();
                    var top = numbersBottom > row.Top ? numbersBottom + 0.5 : row.Top;
                    Add(page.Key, double.IsNaN(above) ? top : above);
                }
                if (index + 1 < ordered.Count)
                {
                    var next = ordered[index + 1];
                    var middle = (row.Bottom + next.Top) / 2;
                    var between = rules.Where(y => y >= Math.Min(row.Bottom, next.Top) - 2 && y <= Math.Max(row.Bottom, next.Top) + 2).OrderBy(y => Math.Abs(y - middle)).Select(y => (double?)y).FirstOrDefault();
                    Add(page.Key, between ?? middle);
                }
                else
                {
                    var below = rules.Where(y => y >= row.Bottom - 2 && y <= row.Bottom + EdgeRuleReach).DefaultIfEmpty(double.NaN).Min();
                    Add(page.Key, double.IsNaN(below) ? row.Bottom : below);
                }
            }
            void Add(int number, double y)
            {
                if (y - last <= MinSeparatorBand) return;
                counter++;
                separators.Add(new InvoiceSeparator("s" + counter.ToString(CultureInfo.InvariantCulture), number, y, left, right));
                last = y;
            }
        }
        return separators;
    }

    // The columns of a following page as it draws them: where its repeated header puts them, else where its row of column numbers does (when it repeats only that
    // second line of the header), else where the first page has them.
    internal static IReadOnlyList<InvoiceColumn> ColumnsOfFollowingPage(InvoicePageData page, IReadOnlyList<InvoiceColumn> columns, NumberHeader? firstNumbers)
    {
        if (RepeatedHeader(page, columns) is { } repeated) return ColumnsOnPage(columns, repeated);
        if (firstNumbers is not null && FindNumberHeader(page, double.NegativeInfinity, double.PositiveInfinity) is { } numbers && ColumnsFromNumbers(columns, firstNumbers, numbers) is { } byNumbers) return byNumbers;
        return columns;
    }

    // Reads the rows between the demarcation lines: on each page the boxes between consecutive lines are the rows (a box without text is not a
    // row); nothing above the first or below the last line of the page is read. The columns are the ones of the template.
    public static List<InvoiceTableRow> ReadBySeparators(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, IReadOnlyList<InvoiceSeparator> separators, char? hint)
    {
        var rows = new List<InvoiceTableRow>();
        if (columns.Count == 0) return rows;
        var allColumns = columns;
        var indexColumn = columns.ToList().FindIndex(column => column.Meaning == InvoiceColumnMeanings.Index);
        var firstPage = separators.Count == 0 ? 0 : separators.Min(separator => separator.Page);
        // The row of column numbers near the first line of the first page (the second line of a two-line header).
        var firstLine = separators.Count == 0 ? double.NaN : separators.Where(separator => separator.Page == firstPage).Min(separator => separator.Y);
        var firstNumbers = document.Pages.FirstOrDefault(item => item.Number == firstPage) is { } firstData && !double.IsNaN(firstLine) ? FindNumberHeader(firstData, firstLine - 80, firstLine + 60) : null;
        foreach (var page in document.Pages.OrderBy(page => page.Number))
        {
            var lines = separators.Where(separator => separator.Page == page.Number).Select(separator => separator.Y).OrderBy(y => y).ToList();
            if (lines.Count < 2) continue;
            // On a following page the columns are where its repeated header (or only its row of column numbers) puts them (the template's are those of the first page).
            columns = page.Number != firstPage ? ColumnsOfFollowingPage(page, allColumns, firstNumbers) : allColumns;
            var cells = AssignCells(page, columns, lines[0]).Where(cell => cell.Segment.CenterY < lines[^1]).ToList();
            // A row of column numbers inside the first box (a line drawn above it) is not goods and does not join the first row.
            cells = WithoutColumnNumberRow(cells, out _);
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
        var separators = InvoiceTableReader.SeparatorsFromRows(extraction.Rows, extraction.Columns, document.Pages);
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
