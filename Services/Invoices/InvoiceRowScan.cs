using RowAnchor = (int Number, double Y, double Height, BlazorStoc.Services.InvoiceTableReader.BodyCell Anchor);

namespace BlazorStoc.Services;

public static partial class InvoiceTableReader
{
    private static InvoiceTableRead ReadRows(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, int headerPage, double headerBottom,
        char? hint, string split, bool useIndex, bool ruled = false) =>
        new RowScan(document, columns, headerPage, headerBottom, hint, split, useIndex, ruled).Read();

    // One reading of the rows of a table, in three steps: (1) every page from the header on is turned into the cells under its columns and the
    // anchors that tell where its rows are (the running numbers, the rules of the table, or the amounts), (2) every anchored page becomes rows
    // with the text of its cells, (3) the rows are tidied (running numbers of a ruled table, totals taken off the end) and the columns widened.
    private sealed class RowScan
    {
        private readonly InvoiceDocument document;
        private readonly int headerPage;
        private readonly double headerBottom;
        private readonly char? hint;
        private readonly string split;
        private readonly bool ruled;
        private readonly int indexColumn;
        private List<InvoiceColumn> refined;
        private List<InvoiceTableRow> rows = [];
        private bool usedIndex;
        private int expected = 1;
        private readonly List<(int Page, List<BodyCell> Cells, List<RowAnchor> Anchors, List<(double Top, double Bottom)>? Bands)> table = [];
        private readonly List<BodyCell> rowCells = [];
        // How far the number line of a row is from where its text starts (Top) or from the middle of its text (Mid): the strategy that fits the
        // layout of the table puts the number on the line the text is aligned to, so a table with names centred on the number is not cut at the number.
        private double misalignment;
        private double numberRowFloor;
        // The table has ended on a page when the totals (or another line that opens the footer) follow its rows: a following page that does not repeat the header is
        // then not a continuation (it is another table, notes, an annex).
        private bool ended;
        private readonly HashSet<InvoiceWord> consumed = [];
        // The row of column numbers under the header of the first page (the second line of a two-line header), when there is one: a following page may repeat only it.
        private readonly NumberHeader? headerNumbers;

        public RowScan(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, int headerPage, double headerBottom, char? hint, string split,
            bool useIndex, bool ruled)
        {
            this.document = document;
            this.headerPage = headerPage;
            this.headerBottom = headerBottom;
            this.hint = hint;
            this.split = split;
            this.ruled = ruled;
            refined = columns.ToList();
            indexColumn = useIndex ? refined.FindIndex(column => column.Meaning == InvoiceColumnMeanings.Index) : -1;
            usedIndex = indexColumn >= 0 && !ruled;
            headerNumbers = document.Pages.FirstOrDefault(item => item.Number == headerPage) is { } firstPage ? FindNumberHeader(firstPage, headerBottom - 1, headerBottom + 60) : null;
        }

        public InvoiceTableRead Read()
        {
            CollectPages();
            BuildRows();
            NumberRowsOfRuledTable();
            DropTrailingRowsWithoutIdentity();
            // The columns become as wide as the text that was found under them (a name runs past its heading), so that the same columns
            // read the next invoice of the supplier.
            refined = WidenColumns(refined, rowCells);
            var checkedRows = InvoiceAmbiguity.Mark(Validate(rows, refined, hint), document);
            var score = GeometricScore(checkedRows, usedIndex);
            return new InvoiceTableRead(refined, checkedRows, split, usedIndex, score - 0.15 * misalignment, consumed);
        }

        // ---- step 1: the cells and the anchors of every page ----

        private void CollectPages()
        {
            foreach (var page in document.Pages.Where(page => page.Number >= headerPage).OrderBy(page => page.Number))
            {
                // On a following page the table starts under its repeated header, when there is one.
                var startY = headerBottom;
                var pageColumns = (IReadOnlyList<InvoiceColumn>)refined;
                if (page.Number != headerPage && !ContinuesTable(page, out startY, out pageColumns)) break;
                CollectPage(page, pageColumns, startY);
            }
        }

        // Whether a following page goes on with the table, and where its rows start and in which columns.
        private bool ContinuesTable(InvoicePageData page, out double startY, out IReadOnlyList<InvoiceColumn> pageColumns)
        {
            pageColumns = refined;
            startY = 0;
            // The repeated header also tells where the columns are on this page (a scan of another page may be shifted or scaled).
            var repeated = RepeatedHeader(page, refined);
            // Or only its second line, the row of column numbers: the table goes on under it, and the figures say where the columns are.
            var numbersHere = repeated is null && headerNumbers is not null && FindNumberHeader(page, double.NegativeInfinity, double.PositiveInfinity) is { } found && ColumnsFromNumbers(refined, headerNumbers, found) is { } byNumbers
                ? (Header: found, Columns: byNumbers) : ((NumberHeader Header, IReadOnlyList<InvoiceColumn> Columns)?)null;
            if (repeated is null && numbersHere is null && (ended || InvoiceGridDetector.FindOnPage(page) is not null || !RulesFitColumns(page, refined))) return false;   // a page with a table of its own is not a continuation
            ended = false;
            startY = repeated?.Bottom ?? numbersHere?.Header.Bottom ?? 0;
            if (repeated is not null) pageColumns = ColumnsOnPage(refined, repeated);
            else if (numbersHere is not null) pageColumns = numbersHere.Value.Columns;
            return true;
        }

        private void CollectPage(InvoicePageData page, IReadOnlyList<InvoiceColumn> pageColumns, double startY)
        {
            var cells = AssignCells(page, pageColumns, startY);
            cells = InsideZones(cells, refined, page.Number, headerPage);
            // Under the bottom of a drawn grid there is no table: the notes and the totals that follow it are not rows, and do not widen the columns.
            if (page.Number == headerPage && TableBottomFromRules(page, pageColumns, startY) is { } gridBottom) cells = cells.Where(cell => cell.Segment.CenterY <= gridBottom + 3).ToList();
            var floor = 0.0;
            if (!ruled) cells = WithoutColumnNumberRow(cells, out floor);
            if (page.Number == headerPage) numberRowFloor = floor;
            if (cells.Count == 0) return;
            var anchors = new List<RowAnchor>();
            if (ruled)
            {
                CollectRuledPage(page, pageColumns, startY, cells);
                return;
            }
            if (indexColumn >= 0) anchors = RunningNumberAnchors(page, cells);
            else if (page.Number == headerPage || rows.Count > 0)
            {
                // Without running numbers the rows are told apart by their amounts, so the totals under the table must be cut off first:
                // the first line that opens the footer ("Subtotal", "Total amount due", "Total fara TVA") ends the table.
                var footer = cells.Where(cell => InvoiceVocabulary.IsFooterStart(cell.Segment.Text)).Select(cell => (double?)cell.Segment.CenterY).Min();
                if (footer is { } footerY) { ended = true; cells = cells.Where(cell => cell.Segment.CenterY < footerY - 0.3 * Math.Max(1, cell.Segment.Height)).ToList(); }
                anchors = ReferenceAnchors(cells, refined, hint);
            }
            if (anchors.Count == 0) { if (page.Number == headerPage && indexColumn >= 0) { usedIndex = false; } return; }
            table.Add((page.Number, cells, anchors, null));
        }

        // The rows are the numbers 1, 2, 3... of the running-number column, in order.
        private List<RowAnchor> RunningNumberAnchors(InvoicePageData page, List<BodyCell> cells)
        {
            var anchors = new List<RowAnchor>();
            // A zone that starts under the first rows (the user cut off a row that is not goods) starts the running numbers at the first number in it.
            if (page.Number == headerPage && expected == 1 && refined[indexColumn].ZoneTop > 0 && cells.Where(cell => cell.Column == indexColumn).OrderBy(cell => cell.Segment.CenterY).Select(cell => ParseIndex(cell.Segment.Text)).FirstOrDefault(number => number is not null) is { } firstNumber)
                expected = firstNumber;
            foreach (var cell in cells.Where(cell => cell.Column == indexColumn).OrderBy(cell => cell.Segment.CenterY))
            {
                var number = ParseRunningNumber(cell.Segment.Text, expected);
                if (number == expected) { anchors.Add((number.Value, cell.Segment.CenterY, cell.Segment.Height, cell)); expected++; }
            }
            return anchors;
        }

        private static string Plain(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

        // Rows are the boxes between the table's horizontal rules, on every page of the table (a following page has its own rules,
        // under its repeated header): a box with no text is not a row, nor is the row of column numbers.
        private void CollectRuledPage(InvoicePageData page, IReadOnlyList<InvoiceColumn> pageColumns, double startY, List<BodyCell> cells)
        {
            var pageBands = RuledBands(page, pageColumns, startY);
            if (pageBands is not { Count: >= 1 }) return;
            var anchors = new List<RowAnchor>();
            var ruledRows = new List<(double Top, double Bottom)>();
            foreach (var (top, bottom) in pageBands)
            {
                var inBox = cells.Where(cell => cell.Segment.CenterY >= top && cell.Segment.CenterY < bottom).ToList();
                if (inBox.Count == 0 || IsColumnNumberRow(inBox.Select(cell => cell.Segment.Text))) continue;
                // The totals box ends the table.
                if (inBox.Any(cell => InvoiceVocabulary.IsFooterStart(cell.Segment.Text))) { ended = true; break; }   // the totals box ends the table on this page; a following page may continue it
                // A box with text but no figure outside the name and the code (no running number, no quantity, no amount) is not goods: when its text
                // starts at the top of the box it carries on the row above (a description under a product), when it starts further down it is a note
                // written in the room left under the table. The first box is kept as it is (it may be a row that has only a name).
                // A box that says again the labels of the columns (a header repeated inside the table, as the blocks of an inventory form have) is neither.
                if (ruledRows.Count > 0 && inBox.Count(cell => Plain(pageColumns[cell.Column].Label).Length >= 3 && Plain(cell.Segment.Text) == Plain(pageColumns[cell.Column].Label)) >= 2) continue;
                if (ruledRows.Count > 0 && !inBox.Any(cell => pageColumns[cell.Column].Meaning is not (InvoiceColumnMeanings.Name or InvoiceColumnMeanings.Code) && cell.Segment.Text.Any(char.IsAsciiDigit)))
                {
                    var first = inBox.MinBy(cell => cell.Segment.CenterY)!;
                    if (first.Segment.CenterY - top <= 1.5 * Math.Max(1, first.Segment.Height)) ruledRows[^1] = (ruledRows[^1].Top, bottom);
                    continue;
                }
                ruledRows.Add((top, bottom));
                anchors.Add((ruledRows.Count, (top + bottom) / 2, Math.Max(1, inBox.Average(cell => cell.Segment.Height)), inBox[0]));
            }
            if (anchors.Count > 0) table.Add((page.Number, cells, anchors, ruledRows));
        }

        // ---- step 2: the rows of every anchored page ----

        private void BuildRows()
        {
            foreach (var (pageNumber, cells, anchors, ruledRowBands) in table)
            {
                var bands = ruledRowBands ?? RowBands(cells, anchors, split, firstRowOpen: pageNumber == headerPage);
                var sequences = ColumnsReadByOrder(cells, anchors, bands);
                for (var index = 0; index < anchors.Count; index++)
                {
                    var (top, bottom) = bands[index];
                    var inRow = cells.Where(cell => cell.Segment.CenterY >= top && cell.Segment.CenterY < bottom).ToList();
                    if (ruledRowBands is null && refined.FindIndex(item => item.Meaning == InvoiceColumnMeanings.Name) is var nameColumn and >= 0)
                    {
                        var ys = inRow.Where(cell => cell.Column == nameColumn).Select(cell => cell.Segment.CenterY).ToList();
                        if (ys.Count > 0) misalignment += Math.Abs(anchors[index].Y - (split == InvoiceRowSplit.Mid ? (ys.Min() + ys.Max()) / 2 : ys.Min())) / Math.Max(1, anchors[index].Height);
                    }
                    foreach (var cell in inRow) consumed.UnionWith(cell.Segment.Words);
                    if (pageNumber == headerPage) rowCells.AddRange(inRow);   // only the first page widens the columns of the template
                    var values = ValuesOf(inRow, sequences, index);
                    rows.Add(new InvoiceTableRow(pageNumber, usedIndex ? anchors[index].Number : null, values, [], double.IsNegativeInfinity(top) ? (pageNumber == headerPage ? Math.Max(headerBottom, numberRowFloor) : 0) : top, bottom));
                }
            }
        }

        // A column whose text is vertically offset from its row is read by order: its k-th value belongs to the k-th row.
        private Dictionary<int, List<string>> ColumnsReadByOrder(List<BodyCell> cells, List<RowAnchor> anchors, List<(double Top, double Bottom)> bands)
        {
            var sequences = new Dictionary<int, List<string>>();
            for (var column = 0; column < refined.Count; column++)
            {
                if (refined[column].RowMapping != InvoiceRowMapping.Sequence) continue;
                var items = cells.Where(cell => cell.Column == column && cell.Segment.CenterY < bands[^1].Bottom).GroupBy(cell => cell.Segment.Line)
                    .OrderBy(group => group.Key).Select(group => InvoiceLayout.TextOf(group.SelectMany(cell => cell.Segment.Words))).ToList();
                if (items.Count == anchors.Count) sequences[column] = items;
            }
            return sequences;
        }

        // The text of every column in one row.
        private Dictionary<string, string> ValuesOf(List<BodyCell> inRow, Dictionary<int, List<string>> sequences, int index)
        {
            var values = new Dictionary<string, string>();
            for (var column = 0; column < refined.Count; column++)
            {
                if (sequences.TryGetValue(column, out var ordered)) { values[refined[column].Id] = ordered[index]; continue; }
                var words = inRow.Where(cell => cell.Column == column).SelectMany(cell => cell.Segment.Words).ToList();
                values[refined[column].Id] = words.Count == 0 ? "" : InvoiceLayout.TextOf(words);
                // A scan reads the digit 1 of a quantity as a bracket or a bar.
                if (refined[column].Meaning == InvoiceColumnMeanings.Quantity && values[refined[column].Id] is "]" or "[" or "|" or "!" or "l" or "I") values[refined[column].Id] = "1";
            }
            return values;
        }

        // ---- step 3: tidying ----

        // Rows told apart by the rules of the table still carry the running numbers of their "Nr. crt." column when it has them (read as a scan gives them:
        // "„1", "|2|"): they are the numbers of the rows, and the table says it has them when most rows do.
        private void NumberRowsOfRuledTable()
        {
            if (!ruled || rows.Count < 2) return;
            var numberColumn = refined.FindIndex(column => column.Meaning == InvoiceColumnMeanings.Index);
            if (numberColumn < 0) return;
            var numbered = rows.Select(row => row with { Number = ParseRunningNumber(row.Cells.GetValueOrDefault(refined[numberColumn].Id, ""), 2) }).ToList();
            if (numbered.Count(row => row.Number is not null) >= numbered.Count * 0.8) { rows = numbered; usedIndex = true; }
        }

        // Rows told apart by their amounts: the totals under the table have amounts but no text, so trailing rows without a name or a code are not rows.
        private void DropTrailingRowsWithoutIdentity()
        {
            if (usedIndex) return;
            var identity = refined.Where(column => column.Meaning is InvoiceColumnMeanings.Name or InvoiceColumnMeanings.Code).Select(column => column.Id).ToList();
            if (identity.Count > 0)
                while (rows.Count > 0 && identity.All(id => string.IsNullOrWhiteSpace(rows[^1].Cells.GetValueOrDefault(id, "")))) rows.RemoveAt(rows.Count - 1);
        }
    }
}
