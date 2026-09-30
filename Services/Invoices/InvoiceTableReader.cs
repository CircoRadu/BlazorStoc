using System.Globalization;

namespace BlazorStoc.Services;

// The cell of a table header: the words of one column's heading, possibly on several lines ("Pretul net al" + "articolului").
internal sealed record HeaderCell(string Label, double Left, double Right, double Top, double Bottom, string Meaning, double MatchScore);

internal sealed record HeaderBand(int Page, IReadOnlyList<HeaderCell> Cells, double Top, double Bottom, double Score);

public static class InvoiceRowSplit
{
    // Rows start at the line of their running number ("top": the usual layout) or halfway between two running numbers ("mid":
    // the number is vertically centred in a row of several lines).
    public const string Top = "top";
    public const string Mid = "mid";
}

public sealed record InvoiceTableRead(IReadOnlyList<InvoiceColumn> Columns, IReadOnlyList<InvoiceTableRow> Rows, string RowSplit, bool UsedIndex, double Score,
    IReadOnlySet<InvoiceWord> Consumed);

// Finds the line table of an invoice and reads its rows. Nothing here assumes a position, a number of columns or their order: the header
// is recognised by what its cells say (InvoiceVocabulary), the columns are the header cells widened by the text that falls under them,
// and the rows are told apart by the running number when there is one, else by the values of the most reliable numeric column.
public static class InvoiceTableReader
{
    public const string ArithmeticFlag = "Cantitate × preț ≠ valoare";
    private const double HeaderLineGap = 1.9;
    private const double MinHeaderScore = 4;

    // ---- header ----

    internal static HeaderBand? FindHeader(InvoicePageData page, double minScore = MinHeaderScore)
    {
        var lines = InvoiceLayout.BuildLines(page.Words);
        HeaderBand? best = null;
        for (var start = 0; start < lines.Count; start++)
        {
            var band = new List<TextLine> { lines[start] };
            if (IsMostlyNumeric(lines[start])) continue;
            var next = start + 1;
            while (band.Count < 4 && next < lines.Count && lines[next].Y - band[^1].Y <= HeaderLineGap * Math.Max(1, band[^1].Height) && !IsMostlyNumeric(lines[next]))
            {
                band.Add(lines[next]);
                next++;
            }
            // Longer bands win a tie: the second line of a header ("articolului", "facturata") must not be left to the body.
            for (var length = 1; length <= band.Count; length++)
            {
                var candidate = Evaluate(page.Number, band.Take(length).ToList());
                if (candidate is null || candidate.Score < minScore) continue;
                if (best is null || candidate.Score >= best.Score) best = candidate;
            }
        }
        return best;
    }

    private static bool IsMostlyNumeric(TextLine line)
    {
        var segments = InvoiceLayout.Segments(line, 0);
        if (segments.Count == 0) return false;
        var numeric = segments.Count(segment => InvoiceValues.LooksNumeric(segment.Text));
        return numeric * 10 >= segments.Count * 3;
    }

    internal static HeaderBand? Evaluate(int page, IReadOnlyList<TextLine> lines)
    {
        var cells = BuildCells(lines);
        if (cells.Count < 3) return null;
        var meanings = cells.Where(cell => cell.Meaning.Length > 0 && cell.Meaning != InvoiceColumnMeanings.Ignore).Select(cell => cell.Meaning).Distinct().ToList();
        var hasIdentity = meanings.Contains(InvoiceColumnMeanings.Name) || meanings.Contains(InvoiceColumnMeanings.Code);
        var hasAmount = meanings.Contains(InvoiceColumnMeanings.Quantity) || meanings.Contains(InvoiceColumnMeanings.Value) || meanings.Contains(InvoiceColumnMeanings.UnitPrice);
        if (!hasIdentity || !hasAmount || meanings.Count < 3) return null;
        var score = meanings.Sum(InvoiceVocabulary.ColumnWeight) + 0.05 * cells.Count;
        var words = lines.SelectMany(line => line.Words).ToList();
        return new HeaderBand(page, cells, words.Min(word => word.Y), words.Max(word => word.Bottom), score);
    }

    internal static List<HeaderCell> BuildCells(IReadOnlyList<TextLine> lines)
    {
        var cells = new List<(List<string> Texts, List<InvoiceWord> Words)>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            foreach (var segment in InvoiceLayout.Segments(lines[lineIndex], lineIndex))
            {
                var best = -1;
                var bestShare = 0.0;
                if (lineIndex > 0)
                    for (var index = 0; index < cells.Count; index++)
                    {
                        var left = cells[index].Words.Min(word => word.X);
                        var right = cells[index].Words.Max(word => word.Right);
                        var overlap = InvoiceLayout.Overlap(left, right, segment.X, segment.Right);
                        var share = overlap / Math.Max(1, Math.Min(right - left, segment.Right - segment.X));
                        if (share > bestShare) { bestShare = share; best = index; }
                    }
                if (best >= 0 && bestShare >= 0.3)
                {
                    cells[best].Texts.Add(segment.Text);
                    cells[best].Words.AddRange(segment.Words);
                }
                else cells.Add(([segment.Text], [.. segment.Words]));
            }
        }
        return cells.Select(cell =>
            {
                var label = string.Join(' ', cell.Texts);
                var (meaning, score) = InvoiceVocabulary.MatchColumn(label);
                return new HeaderCell(label, cell.Words.Min(word => word.X), cell.Words.Max(word => word.Right), cell.Words.Min(word => word.Y),
                    cell.Words.Max(word => word.Bottom), meaning, score);
            })
            .OrderBy(cell => cell.Left).ToList();
    }

    // The header cells as table columns, in reading order, with the meaning the dictionary gives them ("ignore" when unknown).
    internal static List<InvoiceColumn> ColumnsFrom(HeaderBand band)
    {
        var columns = new List<InvoiceColumn>();
        var used = new HashSet<string>();
        var index = 1;
        foreach (var cell in band.Cells)
        {
            var meaning = cell.Meaning.Length == 0 ? InvoiceColumnMeanings.Ignore : cell.Meaning;
            // Two columns never share a meaning (the first keeps it): a second "Cantitate"-like header is something else.
            if (meaning != InvoiceColumnMeanings.Ignore && !used.Add(meaning)) meaning = InvoiceColumnMeanings.Ignore;
            columns.Add(new InvoiceColumn("c" + index++.ToString(CultureInfo.InvariantCulture), cell.Label, meaning, cell.Left, cell.Right));
        }
        return columns;
    }

    // A header found without the dictionary: the table is where a running number 1, 2, 3 ... goes down the left side of a page; its header is
    // the line of labels just above the first number. Used only when no header is recognised by its labels.
    internal static HeaderBand? FindStructuralHeader(InvoicePageData page)
    {
        var lines = InvoiceLayout.BuildLines(page.Words);
        var segments = lines.Select((line, index) => InvoiceLayout.Segments(line, index)).ToList();
        var numbers = new List<(int Line, Segment Segment, int Value)>();
        for (var i = 0; i < segments.Count; i++)
            foreach (var segment in segments[i])
                if (segment.X < page.Width * 0.4 && ParseIndex(segment.Text) is { } value) numbers.Add((i, segment, value));

        List<(int Line, Segment Segment, int Value)>? bestChain = null;
        foreach (var start in numbers.Where(item => item.Value == 1))
        {
            var chain = new List<(int Line, Segment Segment, int Value)> { start };
            while (true)
            {
                var last = chain[^1];
                var next = numbers.Where(item => item.Value == last.Value + 1 && item.Line > last.Line && Math.Abs(item.Segment.Right - start.Segment.Right) <= 8 + 2 * start.Segment.Height &&
                                                 lines[item.Line].Y - lines[last.Line].Y <= 8 * Math.Max(1, start.Segment.Height))
                    .OrderBy(item => item.Line).FirstOrDefault();
                if (next.Segment is null) break;
                chain.Add(next);
            }
            if (chain.Count >= 2 && (bestChain is null || chain.Count > bestChain.Count)) bestChain = chain;
        }
        if (bestChain is null) return null;

        var first = bestChain[0];
        // The line above the first number that looks like labels: several runs, not numbers; the line above it joins when it is part of a two-line header.
        var headerLine = -1;
        for (var i = first.Line - 1; i >= Math.Max(0, first.Line - 3); i--)
        {
            if (segments[i].Count >= 3 && !IsMostlyNumeric(lines[i]) && lines[first.Line].Y - lines[i].Y <= 5 * Math.Max(1, first.Segment.Height)) { headerLine = i; break; }
        }
        if (headerLine < 0) return null;
        var band = new List<TextLine> { lines[headerLine] };
        if (headerLine > 0 && segments[headerLine - 1].Count >= 2 && !IsMostlyNumeric(lines[headerLine - 1]) &&
            lines[headerLine].Y - lines[headerLine - 1].Y <= HeaderLineGap * Math.Max(1, lines[headerLine].Height) && lines[headerLine - 1].Y >= lines[headerLine].Y - 2.5 * lines[headerLine].Height)
            band.Insert(0, lines[headerLine - 1]);
        var cells = BuildCells(band);
        if (cells.Count < 3) return null;
        // The header cell above the running numbers is the index column (added when the numbers have no label over them).
        var indexCell = cells.FindIndex(cell => InvoiceLayout.Overlap(cell.Left, cell.Right, first.Segment.X, first.Segment.Right) > 0);
        if (indexCell >= 0) cells[indexCell] = cells[indexCell] with { Meaning = InvoiceColumnMeanings.Index };
        else cells.Insert(0, new HeaderCell("Nr.", first.Segment.X - 2, first.Segment.Right + 2, band[0].Words.Min(word => word.Y), band[^1].Words.Max(word => word.Bottom), InvoiceColumnMeanings.Index, 1));
        var words = band.SelectMany(line => line.Words).ToList();
        return new HeaderBand(page.Number, cells, words.Min(word => word.Y), words.Max(word => word.Bottom), 0);
    }

    // The meaning the numbers of a column show when its label did not give one: on every row quantity x unit price = value; the widest text
    // column is the name, a short one the unit. Only columns still without a meaning change, and only when something is missing.
    internal static List<InvoiceColumn> InferMeanings(IReadOnlyList<InvoiceColumn> columns, IReadOnlyList<InvoiceTableRow> rows, char? hint)
    {
        var result = columns.ToList();
        if (rows.Count < 2) return result;
        var meanings = columns.Select(column => column.Meaning).ToHashSet();
        bool Free(int index) => result[index].Meaning == InvoiceColumnMeanings.Ignore;
        IEnumerable<string> Cells(int index) => rows.Select(row => row.Cells.GetValueOrDefault(columns[index].Id, "")).Where(text => text.Length > 0);
        bool MostlyNumeric(int index)
        {
            var cells = Cells(index).ToList();
            return cells.Count >= rows.Count * 0.8 && cells.Count(text => InvoiceValues.ParseNumber(text, hint) is not null) >= cells.Count * 0.9;
        }
        if (!meanings.Contains(InvoiceColumnMeanings.Name) && !meanings.Contains(InvoiceColumnMeanings.Code))
        {
            var name = Enumerable.Range(0, result.Count).Where(Free).Where(index => !MostlyNumeric(index))
                .Select(index => (Index: index, Length: Cells(index).DefaultIfEmpty("").Average(text => text.Length))).OrderByDescending(item => item.Length).FirstOrDefault();
            if (name.Length >= 6) result[name.Index] = result[name.Index] with { Meaning = InvoiceColumnMeanings.Name };
        }
        if (!meanings.Contains(InvoiceColumnMeanings.Unit))
        {
            var unit = Enumerable.Range(0, result.Count).Where(Free).Where(index => !MostlyNumeric(index))
                .Select(index => (Index: index, Length: Cells(index).DefaultIfEmpty("").Average(text => text.Length))).Where(item => item.Length is > 0 and <= 5).OrderBy(item => item.Index).FirstOrDefault();
            if (unit.Length > 0) result[unit.Index] = result[unit.Index] with { Meaning = InvoiceColumnMeanings.Unit };
        }
        if (!(meanings.Contains(InvoiceColumnMeanings.Quantity) && meanings.Contains(InvoiceColumnMeanings.UnitPrice) && meanings.Contains(InvoiceColumnMeanings.Value)))
        {
            var numeric = Enumerable.Range(0, result.Count).Where(MostlyNumeric).ToList();
            decimal? Number(int column, InvoiceTableRow row) => InvoiceValues.ParseNumber(row.Cells.GetValueOrDefault(columns[column].Id, ""), hint);
            foreach (var product in numeric)
                foreach (var a in numeric.Where(index => index != product))
                    foreach (var b in numeric.Where(index => index > a && index != product))
                    {
                        var ok = rows.Count(row => Number(a, row) is { } x && Number(b, row) is { } y && Number(product, row) is { } z && Math.Abs(x * y - z) <= Math.Max(0.05m, Math.Abs(z) * 0.001m));
                        if (ok < Math.Max(2, rows.Count * 0.8)) continue;
                        // The quantity is the smaller of the two on most rows.
                        var quantityFirst = rows.Count(row => Number(a, row) is { } x && Number(b, row) is { } y && Math.Abs(x) <= Math.Abs(y)) * 2 >= rows.Count;
                        var (quantity, price) = quantityFirst ? (a, b) : (b, a);
                        var wanted = new (int Column, string Meaning)[] { (quantity, InvoiceColumnMeanings.Quantity), (price, InvoiceColumnMeanings.UnitPrice), (product, InvoiceColumnMeanings.Value) };
                        // Nothing already named is overwritten: a column keeps the meaning its label gave it.
                        if (wanted.Any(item => !Free(item.Column) && result[item.Column].Meaning != item.Meaning) || wanted.Any(item => result.Where((column, index) => index != item.Column).Any(column => column.Meaning == item.Meaning))) continue;
                        foreach (var (column, meaning) in wanted) result[column] = result[column] with { Meaning = meaning };
                        return result;
                    }
        }
        return result;
    }

    // ---- whole table of a document ----

    public static (InvoiceTable? Table, IReadOnlySet<InvoiceWord> Consumed) Detect(InvoiceDocument document, char? decimalHint)
    {
        HeaderBand? best = null;
        foreach (var page in document.Pages)
        {
            var band = FindHeader(page);
            if (band is not null && (best is null || band.Score > best.Score + 0.01)) best = band;
        }
        var structural = false;
        if (best is null)
        {
            // The labels say nothing (another language, unusual wording): the table is found by its structure - a running number 1, 2, 3
            // down the left side and a line of labels above it.
            foreach (var page in document.Pages)
            {
                var band = FindStructuralHeader(page);
                if (band is not null) { best = band; structural = true; break; }
            }
        }
        if (best is null) return (null, new HashSet<InvoiceWord>());
        var columns = ColumnsFrom(best);
        var read = ReadRows(document, columns, best.Page, best.Bottom, decimalHint);
        // Columns the labels did not name are given the meaning their numbers show: quantity x price = value on every row.
        var inferred = InferMeanings(read.Columns, read.Rows, decimalHint);
        if (inferred.Zip(read.Columns).Any(pair => pair.First.Meaning != pair.Second.Meaning))
            read = ReadRows(document, inferred, best.Page, best.Bottom, decimalHint);
        var consumed = new HashSet<InvoiceWord>(read.Consumed);
        var left = read.Columns.Min(column => column.Left) - 6;
        var right = read.Columns.Max(column => column.Right) + 6;
        foreach (var word in document.Pages.First(page => page.Number == best.Page).Words)
            if (word.CenterY >= best.Top - 0.5 && word.CenterY <= best.Bottom + 0.5 && word.CenterX >= left && word.CenterX <= right) consumed.Add(word);
        var confidence = structural ? 0.3 : Math.Min(1.0, best.Score / 8.0) * 0.5 + 0.5 * RowsOkShare(read.Rows);
        return (new InvoiceTable(best.Page, best.Top, best.Bottom, read.Columns, read.Rows, read.UsedIndex, Math.Round(confidence, 2), structural), consumed);
    }

    private static double RowsOkShare(IReadOnlyList<InvoiceTableRow> rows) => rows.Count == 0 ? 0 : rows.Count(row => row.Flags.Count == 0) / (double)rows.Count;

    // ---- rows ----

    private sealed record BodyCell(int Column, Segment Segment);

    // Reads the rows of the table that starts under headerBottom on headerPage and continues on the following pages. Both row
    // strategies are tried and the one whose rows pass the arithmetic checks best is kept.
    public static InvoiceTableRead ReadRows(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, int headerPage, double headerBottom,
        char? decimalHint, string? forcedSplit = null)
    {
        InvoiceTableRead? best = null;
        foreach (var split in forcedSplit is null ? new[] { InvoiceRowSplit.Top, InvoiceRowSplit.Mid } : [forcedSplit])
        {
            var read = ReadRows(document, columns, headerPage, headerBottom, decimalHint, split, useIndex: true);
            if (read.Rows.Count == 0 && columns.Any(column => column.Meaning == InvoiceColumnMeanings.Index))
                read = ReadRows(document, columns, headerPage, headerBottom, decimalHint, split, useIndex: false);
            if (best is null || read.Score > best.Score + 0.001) best = read;
        }
        // Rows that still fail the arithmetic may have a column that sits at another height than its row: read it by order instead.
        if (best!.Rows.Any(row => row.Flags.Contains(ArithmeticFlag)))
            foreach (var meaning in new[] { InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value })
            {
                var index = best.Columns.ToList().FindIndex(column => column.Meaning == meaning && column.RowMapping == InvoiceRowMapping.Band);
                if (index < 0) continue;
                var trial = best.Columns.ToList();
                trial[index] = trial[index] with { RowMapping = InvoiceRowMapping.Sequence };
                var read = ReadRows(document, trial, headerPage, headerBottom, decimalHint, best.RowSplit, best.UsedIndex);
                if (read.Score > best.Score + 0.001) best = read;
            }
        return best;
    }

    private static InvoiceTableRead ReadRows(InvoiceDocument document, IReadOnlyList<InvoiceColumn> columns, int headerPage, double headerBottom,
        char? hint, string split, bool useIndex)
    {
        var refined = columns.ToList();
        var rows = new List<InvoiceTableRow>();
        var indexColumn = useIndex ? refined.FindIndex(column => column.Meaning == InvoiceColumnMeanings.Index) : -1;
        var expected = 1;
        var usedIndex = indexColumn >= 0;
        var table = new List<(int Page, List<BodyCell> Cells, List<(int Number, double Y, double Height, BodyCell Anchor)> Anchors)>();
        var rowCells = new List<BodyCell>();
        var consumed = new HashSet<InvoiceWord>();

        foreach (var page in document.Pages.Where(page => page.Number >= headerPage).OrderBy(page => page.Number))
        {
            // On a following page the table starts under its repeated header, when there is one.
            var startY = page.Number == headerPage ? headerBottom : FindHeader(page, 3)?.Bottom ?? 0;
            var cells = AssignCells(page, refined, startY);
            if (cells.Count == 0) continue;
            var anchors = new List<(int Number, double Y, double Height, BodyCell Anchor)>();
            if (indexColumn >= 0)
            {
                foreach (var cell in cells.Where(cell => cell.Column == indexColumn).OrderBy(cell => cell.Segment.CenterY))
                {
                    var number = ParseIndex(cell.Segment.Text);
                    if (number == expected) { anchors.Add((number.Value, cell.Segment.CenterY, cell.Segment.Height, cell)); expected++; }
                }
            }
            else if (page.Number == headerPage || rows.Count > 0)
            {
                // Without running numbers the rows are told apart by their amounts, so the totals under the table must be cut off first:
                // the first line that opens the footer ("Subtotal", "Total amount due", "Total fara TVA") ends the table.
                var footer = cells.Where(cell => InvoiceVocabulary.IsFooterStart(cell.Segment.Text)).Select(cell => (double?)cell.Segment.CenterY).Min();
                if (footer is { } footerY) cells = cells.Where(cell => cell.Segment.CenterY < footerY - 0.3 * Math.Max(1, cell.Segment.Height)).ToList();
                anchors = ReferenceAnchors(cells, refined, hint);
            }
            if (anchors.Count == 0) { if (page.Number == headerPage && indexColumn >= 0) { usedIndex = false; } continue; }
            table.Add((page.Number, cells, anchors));
        }

        foreach (var (pageNumber, cells, anchors) in table)
        {
            var bands = RowBands(cells, anchors, split, firstRowOpen: pageNumber == headerPage);
            // A column whose text is vertically offset from its row is read by order: its k-th value belongs to the k-th row.
            var sequences = new Dictionary<int, List<string>>();
            for (var column = 0; column < refined.Count; column++)
            {
                if (refined[column].RowMapping != InvoiceRowMapping.Sequence) continue;
                var items = cells.Where(cell => cell.Column == column && cell.Segment.CenterY < bands[^1].Bottom).GroupBy(cell => cell.Segment.Line)
                    .OrderBy(group => group.Key).Select(group => InvoiceLayout.TextOf(group.SelectMany(cell => cell.Segment.Words))).ToList();
                if (items.Count == anchors.Count) sequences[column] = items;
            }
            for (var index = 0; index < anchors.Count; index++)
            {
                var (top, bottom) = bands[index];
                var inRow = cells.Where(cell => cell.Segment.CenterY >= top && cell.Segment.CenterY < bottom).ToList();
                foreach (var cell in inRow) consumed.UnionWith(cell.Segment.Words);
                rowCells.AddRange(inRow);
                var values = new Dictionary<string, string>();
                for (var column = 0; column < refined.Count; column++)
                {
                    if (sequences.TryGetValue(column, out var ordered)) { values[refined[column].Id] = ordered[index]; continue; }
                    var words = inRow.Where(cell => cell.Column == column).SelectMany(cell => cell.Segment.Words).ToList();
                    values[refined[column].Id] = words.Count == 0 ? "" : InvoiceLayout.TextOf(words);
                }
                rows.Add(new InvoiceTableRow(pageNumber, usedIndex ? anchors[index].Number : null, values, [], double.IsNegativeInfinity(top) ? (pageNumber == headerPage ? headerBottom : 0) : top, bottom));
            }
        }

        // Rows told apart by their amounts: the totals under the table have amounts but no text, so trailing rows without a name or a code are not rows.
        if (!usedIndex)
        {
            var identity = refined.Where(column => column.Meaning is InvoiceColumnMeanings.Name or InvoiceColumnMeanings.Code).Select(column => column.Id).ToList();
            if (identity.Count > 0)
                while (rows.Count > 0 && identity.All(id => string.IsNullOrWhiteSpace(rows[^1].Cells.GetValueOrDefault(id, "")))) rows.RemoveAt(rows.Count - 1);
        }

        // The columns become as wide as the text that was found under them (a name runs past its heading), so that the same columns
        // read the next invoice of the supplier.
        refined = WidenColumns(refined, rowCells);
        var checkedRows = Validate(rows, refined, hint);
        return new InvoiceTableRead(refined, checkedRows, split, usedIndex, Score(checkedRows, refined, hint), consumed);
    }

    private static int? ParseIndex(string text)
    {
        var trimmed = text.Trim().TrimEnd('.', ')', ':');
        return trimmed.Length is > 0 and <= 4 && trimmed.All(char.IsAsciiDigit) && int.TryParse(trimmed, out var number) ? number : null;
    }

    // Rows without a running number: one row per value of the first numeric column that has any (amount, quantity, price).
    private static List<(int Number, double Y, double Height, BodyCell Anchor)> ReferenceAnchors(List<BodyCell> cells, IReadOnlyList<InvoiceColumn> columns, char? hint)
    {
        foreach (var meaning in new[] { InvoiceColumnMeanings.Value, InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.ValueWithVat })
        {
            var column = columns.ToList().FindIndex(item => item.Meaning == meaning);
            if (column < 0) continue;
            var numeric = cells.Where(cell => cell.Column == column && InvoiceValues.ParseNumber(cell.Segment.Text, hint) is not null)
                .OrderBy(cell => cell.Segment.CenterY).ToList();
            if (numeric.Count == 0) continue;
            var result = new List<(int, double, double, BodyCell)>();
            foreach (var cell in numeric)
                if (result.Count == 0 || cell.Segment.CenterY - result[^1].Item2 > 0.8 * Math.Max(1, cell.Segment.Height))
                    result.Add((result.Count + 1, cell.Segment.CenterY, cell.Segment.Height, cell));
            return result;
        }
        return [];
    }

    // The vertical range of each row. The last row ends where the lines stop following each other at the pitch of the table (or at a
    // line that opens the footer: totals, payment instructions, notes).
    private static List<(double Top, double Bottom)> RowBands(List<BodyCell> cells, List<(int Number, double Y, double Height, BodyCell Anchor)> anchors, string split, bool firstRowOpen)
    {
        var height = Math.Max(1, anchors.Average(anchor => anchor.Height));
        var bands = new List<(double, double)>();
        for (var index = 0; index < anchors.Count; index++)
        {
            // The first row of the table takes what is above its number (down to the header); on a following page nothing above it is a row.
            var top = index == 0 ? (firstRowOpen ? double.NegativeInfinity : anchors[0].Y - (split == InvoiceRowSplit.Mid ? 1.0 : 0.45) * height)
                : split == InvoiceRowSplit.Mid ? (anchors[index - 1].Y + anchors[index].Y) / 2 : anchors[index].Y - 0.45 * height;
            var bottom = index + 1 < anchors.Count
                ? split == InvoiceRowSplit.Mid ? (anchors[index].Y + anchors[index + 1].Y) / 2 : anchors[index + 1].Y - 0.45 * height
                : LastRowEnd(cells, anchors, height);
            bands.Add((top, bottom));
        }
        return bands;
    }

    private static double LastRowEnd(List<BodyCell> cells, List<(int Number, double Y, double Height, BodyCell Anchor)> anchors, double height)
    {
        var last = anchors[^1].Y;
        var pitch = anchors.Count >= 2
            ? TextLine.Median(anchors.Zip(anchors.Skip(1), (a, b) => b.Y - a.Y))
            : 2.2 * height;
        var limit = Math.Max(pitch, 1.7 * height);
        var lines = cells.Select(cell => cell.Segment).Where(segment => segment.CenterY > last - 0.5 * height)
            .GroupBy(segment => Math.Round(segment.CenterY / Math.Max(1, 0.5 * height)))
            .Select(group => (Y: group.Average(segment => segment.CenterY), Text: string.Join(' ', group.OrderBy(segment => segment.X).Select(segment => segment.Text))))
            .OrderBy(line => line.Y).ToList();
        var end = last + 0.5 * height;
        foreach (var line in lines)
        {
            if (line.Y <= end) continue;
            if (line.Y - end > limit - 0.5 * height || InvoiceVocabulary.IsFooterStart(line.Text)) break;
            end = line.Y + 0.5 * height;
        }
        return end + 0.2 * height;
    }

    // Which column each text run of the body belongs to: the one whose header it overlaps most; a run that straddles several headers
    // is split word by word; a run under no header joins the nearest one if it is close.
    private static List<BodyCell> AssignCells(InvoicePageData page, IReadOnlyList<InvoiceColumn> columns, double startY)
    {
        var left = columns.Min(column => column.Left) - 6;
        var right = columns.Max(column => column.Right) + 6;
        var words = page.Words.Where(word => word.CenterY > startY && word.CenterX >= left && word.CenterX <= right).ToList();
        var result = new List<BodyCell>();
        var lines = InvoiceLayout.BuildLines(words);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            foreach (var segment in InvoiceLayout.Segments(lines[lineIndex], lineIndex))
            {
                var overlaps = columns.Select((column, index) => (Index: index, Overlap: InvoiceLayout.Overlap(column.Left, column.Right, segment.X, segment.Right)))
                    .Where(item => item.Overlap > 0).OrderByDescending(item => item.Overlap).ToList();
                if (overlaps.Count >= 2 && overlaps[1].Overlap > 0.25 * (segment.Right - segment.X))
                {
                    // Straddles two columns: each word goes to the column it overlaps most.
                    foreach (var group in segment.Words.GroupBy(word => NearestColumn(columns, word.X, word.Right)))
                        result.Add(new BodyCell(group.Key, new Segment { Words = group.ToList(), Line = lineIndex }));
                }
                else if (overlaps.Count >= 1) result.Add(new BodyCell(overlaps[0].Index, segment));
                else
                {
                    var nearest = NearestColumn(columns, segment.X, segment.Right);
                    var distance = Math.Min(Math.Abs(segment.X - columns[nearest].Right), Math.Abs(columns[nearest].Left - segment.Right));
                    if (distance <= 25) result.Add(new BodyCell(nearest, segment));
                }
            }
        return result;
    }

    private static int NearestColumn(IReadOnlyList<InvoiceColumn> columns, double x, double right)
    {
        var best = 0;
        var bestScore = double.NegativeInfinity;
        for (var index = 0; index < columns.Count; index++)
        {
            var overlap = InvoiceLayout.Overlap(columns[index].Left, columns[index].Right, x, right);
            var distance = Math.Max(0, Math.Max(columns[index].Left - right, x - columns[index].Right));
            var score = overlap > 0 ? overlap : -distance;
            if (score > bestScore) { bestScore = score; best = index; }
        }
        return best;
    }

    private static List<InvoiceColumn> WidenColumns(List<InvoiceColumn> columns, List<BodyCell> cells)
    {
        var widened = new List<InvoiceColumn>();
        for (var index = 0; index < columns.Count; index++)
        {
            var own = cells.Where(cell => cell.Column == index).ToList();
            if (own.Count == 0) { widened.Add(columns[index]); continue; }
            widened.Add(columns[index] with { Left = Math.Min(columns[index].Left, own.Min(cell => cell.Segment.X)), Right = Math.Max(columns[index].Right, own.Max(cell => cell.Segment.Right)) });
        }
        return widened;
    }

    // ---- checks ----

    private static int ColumnOf(IReadOnlyList<InvoiceColumn> columns, string meaning) => columns.ToList().FindIndex(column => column.Meaning == meaning);

    internal static List<InvoiceTableRow> Validate(List<InvoiceTableRow> rows, IReadOnlyList<InvoiceColumn> columns, char? hint)
    {
        string? Cell(InvoiceTableRow row, string meaning) { var index = ColumnOf(columns, meaning); return index < 0 ? null : row.Cells[columns[index].Id]; }
        var result = new List<InvoiceTableRow>();
        foreach (var row in rows)
        {
            var flags = new List<string>();
            var quantity = InvoiceValues.ParseNumber(Cell(row, InvoiceColumnMeanings.Quantity), hint);
            var price = InvoiceValues.ParseNumber(Cell(row, InvoiceColumnMeanings.UnitPrice), hint);
            var value = InvoiceValues.ParseNumber(Cell(row, InvoiceColumnMeanings.Value), hint);
            if (ColumnOf(columns, InvoiceColumnMeanings.Name) >= 0 && string.IsNullOrWhiteSpace(Cell(row, InvoiceColumnMeanings.Name)))
                flags.Add("Denumire lipsă");
            if (quantity is not null && price is not null && value is not null)
            {
                var difference = Math.Abs(quantity.Value * price.Value - value.Value);
                if (difference > Math.Max(0.05m, Math.Abs(value.Value) * 0.001m)) flags.Add(ArithmeticFlag);
            }
            else if (ColumnOf(columns, InvoiceColumnMeanings.Quantity) >= 0 && quantity is null && value is not null)
                flags.Add("Cantitate necitită");
            result.Add(row with { Flags = flags });
        }
        return result;
    }

    private static double Score(IReadOnlyList<InvoiceTableRow> rows, IReadOnlyList<InvoiceColumn> columns, char? hint)
    {
        if (rows.Count == 0) return 0;
        var score = 0.0;
        foreach (var row in rows)
        {
            score += 1;
            score -= row.Flags.Count * 0.6;
            var filled = row.Cells.Values.Count(value => value.Length > 0);
            score += 0.05 * filled;
        }
        return score;
    }
}
