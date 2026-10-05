using System.Text.RegularExpressions;
using OpenCvSharp;
using PDFtoImage;
using SkiaSharp;
using Tesseract;

namespace BlazorStoc.Services;

// Reads again the numbers of the table that the page-wide OCR did not read as numbers.
public interface IInvoiceNumberRereader
{
    Task<InvoiceReadResult> RereadNumbersAsync(InvoiceReadResult read, CancellationToken cancellationToken = default);
}

// The OCR reads a whole page and is tuned for text: in a cell of a table the digits of a blurry or small print come out as letters, signs and gaps ("ear tet" for
// 250.00, "58 32" for 58,32). Once the table is known (the geometry decides it, never what was read in the cells) each cell of a numeric column whose text is not a
// number is cut out of the picture of the page, cleaned of the rules of the table and read again with only the characters of a number allowed. The new reading
// replaces the words of that cell only when it is a number; a cell that is already a clean number is never touched.
public sealed partial class InvoicePdfReader
{
    private static readonly string[] NumericMeanings =
    [
        InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.VatRate, InvoiceColumnMeanings.VatAmount,
        InvoiceColumnMeanings.Value, InvoiceColumnMeanings.ValueWithVat, InvoiceColumnMeanings.Discount
    ];
    // The columns that every row of goods has a figure in: an empty cell there is a figure the OCR did not find.
    private static readonly string[] CoreMeanings = [InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value];

    // (a lone dash or dot is how a table says "nothing here")
    [GeneratedRegex(@"^[+-]?\d[\d.,]*\s*(%|RON|LEI|EUR|USD)?$|^[-–—.]+$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanNumber();

    // A cell whose text is a number as it stands ("12", "1,970.60", "19 %", "100.00 RON") is not read again, nor is one that is a number once the scraps of the cell's
    // border at its ends are taken off ("38.33 |", "437.06,").
    internal static bool NeedsReread(string text, bool coreColumn)
    {
        var trimmed = Tidy(text);
        if (trimmed.Length == 0) return coreColumn;
        return !CleanNumber().IsMatch(trimmed);
    }

    // The text of a cell without the characters a border leaves at its ends (a bar read as "|", "!", "]", a stray comma), when what is left is a number; else as it is.
    internal static string Tidy(string text)
    {
        var trimmed = text.Trim();
        var stripped = trimmed.Trim(' ', '|', '!', '[', ']', '_', '\'', '`', '‘', '’', '"', '„', '“', ',', ';', ':').TrimEnd('.', ',', ' ');
        return stripped.Length > 0 && stripped.Any(char.IsAsciiDigit) && CleanNumber().IsMatch(stripped) ? stripped : trimmed;
    }

    private static readonly string[] TextMeanings = [InvoiceColumnMeanings.Index, InvoiceColumnMeanings.Code, InvoiceColumnMeanings.Name, InvoiceColumnMeanings.Unit];

    // The columns that hold figures, and whether every row has one in it (core: an empty cell is then a figure the OCR missed). A column the labels named as a
    // quantity, a price or an amount is one; a column whose label said nothing is one when it is to the right of the first column that most of its cells are
    // clean numbers in (the figures of an invoice are on the right of the names), and a column is core when nearly all its cells are clean numbers.
    internal static List<(InvoiceColumn Column, bool Core)> NumericColumns(InvoiceTable table)
    {
        var rows = table.Rows;
        double CleanShare(InvoiceColumn column) => rows.Count == 0 ? 0 : rows.Count(row => CleanNumber().IsMatch(row.Cells.GetValueOrDefault(column.Id, "").Trim())) / (double)rows.Count;
        var ordered = table.Columns.OrderBy(column => column.Left).ToList();
        var firstFigures = ordered.FindIndex(column => !TextMeanings.Contains(column.Meaning) && CleanShare(column) >= 0.6 && rows.Count(row => row.Cells.GetValueOrDefault(column.Id, "").Trim().Length > 0) >= 2);
        var result = new List<(InvoiceColumn, bool)>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var column = ordered[index];
            if (TextMeanings.Contains(column.Meaning)) continue;
            if (!NumericMeanings.Contains(column.Meaning) && (firstFigures < 0 || index < firstFigures)) continue;
            result.Add((column, CoreMeanings.Contains(column.Meaning) || CleanShare(column) >= 0.8));
        }
        return result;
    }

    // What a re-reading must look like to replace the old text: only the characters of a number, a figure in it.
    [GeneratedRegex(@"^-?\d[\d.,]*\d$|^\d$")]
    private static partial Regex ReadNumber();

    public async Task<InvoiceReadResult> RereadNumbersAsync(InvoiceReadResult read, CancellationToken cancellationToken = default)
    {
        if (read.SourcePdf is null || read.Document.Pages.All(page => page.Source != InvoiceSources.Ocr)) return read;
        var table = InvoiceAnalyzer.Analyze(read.Document).Table;
        if (table is null || table.Rows.Count == 0) return read;
        var hint = InvoiceValues.DecimalStyle(read.Document.AllWords.Select(word => word.Text));
        var debug = Environment.GetEnvironmentVariable("INVOICE_OCR_DEBUG") == "1";

        var numeric = NumericColumns(table);
        // Where a column is on a page: as the table has it on the page of its header; on a following page where the repeated header puts it (a scan of another page is
        // shifted or scaled), and a following page without a repeated header is not read again (the cells there are not where the columns say).
        var columnsOfPage = new Dictionary<int, IReadOnlyList<InvoiceColumn>?>();
        InvoiceColumn? OnPage(InvoicePageData page, InvoiceColumn column)
        {
            if (!columnsOfPage.TryGetValue(page.Number, out var columns))
                columnsOfPage[page.Number] = columns = page.Number == table.HeaderPage ? table.Columns
                    : InvoiceTableReader.RepeatedHeader(page, table.Columns) is { } repeated ? InvoiceTableReader.ColumnsOnPage(table.Columns, repeated) : null;
            return columns?.FirstOrDefault(item => item.Id == column.Id);
        }
        // The three columns the arithmetic of a row is checked on (quantity x price = value), when the table has them all.
        var quantityColumn = table.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Quantity);
        var priceColumn = table.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.UnitPrice);
        var valueColumn = table.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
        var arithmetic = quantityColumn is not null && priceColumn is not null && valueColumn is not null;
        // What is read again: a cell of a numeric column whose text is not a number; and, in a row that fails the arithmetic, the three cells of the arithmetic
        // even when each looks like a number (a "1" read as a "4" is a number).
        var jobs = new List<(InvoicePageData Page, int RowIndex, InvoiceColumn Column, InvoiceTableRow Row)>();
        var rowsToCheck = new HashSet<int>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var page = read.Document.Pages.FirstOrDefault(item => item.Number == row.Page);
            if (page is null || page.Source != InvoiceSources.Ocr || !double.IsFinite(row.Top) || !double.IsFinite(row.Bottom) || row.Bottom - row.Top < 4) continue;
            if (OnPage(page, table.Columns[0]) is null) continue;
            var failing = arithmetic && (row.Flags.Any(flag => flag == InvoiceTableReader.ArithmeticFlag || flag == InvoiceTableReader.UnreadQuantityFlag) ||
                new[] { quantityColumn!, priceColumn!, valueColumn! }.Any(column => NeedsReread(row.Cells.GetValueOrDefault(column.Id, ""), coreColumn: true)));
            if (failing) rowsToCheck.Add(rowIndex);
            foreach (var (column, core) in numeric)
                if (NeedsReread(row.Cells.GetValueOrDefault(column.Id, ""), core) || (failing && (column == quantityColumn || column == priceColumn || column == valueColumn))) jobs.Add((page, rowIndex, column, row));
            // A row that is checked by its arithmetic reads its three cells again even if they are not among the columns found numeric.
            if (failing)
                foreach (var column in new[] { quantityColumn!, priceColumn!, valueColumn! })
                    if (!jobs.Any(job => job.RowIndex == rowIndex && job.Column == column)) jobs.Add((page, rowIndex, column, row));
        }

        var readings = new Dictionary<(int RowIndex, string ColumnId), (string Text, double Confidence)>();
        var scale = InvoiceAnalysisRules.OcrDpi / 72.0;
        await tesseractGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // (with nothing to read again the engine is not even started: the scraps of borders are only taken off)
            using var engine = jobs.Count > 0 ? new TesseractEngine(tessdataPath, language, EngineMode.LstmOnly) : null;
            engine?.SetVariable("tessedit_char_whitelist", "0123456789.,-");
            foreach (var group in jobs.GroupBy(job => job.Page.Number))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var frame = RenderFrame(read.SourcePdf, group.First().Page);
                if (frame is null) continue;
                using var withoutRules = RemoveRuledLines(frame);
                var source = withoutRules ?? frame;
                foreach (var (page, rowIndex, tableColumn, row) in group)
                {
                    if (OnPage(page, tableColumn) is not { } column) continue;
                    var left = Math.Max(0, (int)Math.Round((column.Left + 1) * scale));
                    var right = Math.Min(source.Cols, (int)Math.Round((column.Right - 1) * scale));
                    var top = Math.Max(0, (int)Math.Round((row.Top + 1) * scale));
                    var bottom = Math.Min(source.Rows, (int)Math.Round((row.Bottom - 1) * scale));
                    if (right - left < 10 || bottom - top < 10) continue;
                    using var cell = new Mat(source, new OpenCvSharp.Rect(left, top, right - left, bottom - top));
                    // The box of a row also holds scraps of the rule above it and the top of the row below: only the line of text of this cell is cut out.
                    if (TightLine(cell) is not { } line) continue;
                    using var crop = new Mat(cell, line);
                    using var padded = new Mat();
                    Cv2.CopyMakeBorder(crop, padded, 12, 12, 12, 12, BorderTypes.Constant, Scalar.White);
                    using var scaled = new Mat();
                    if (padded.Rows < 90) Cv2.Resize(padded, scaled, new OpenCvSharp.Size(), 2.5, 2.5, InterpolationFlags.Cubic); else padded.CopyTo(scaled);
                    if (Environment.GetEnvironmentVariable("INVOICE_OCR_DUMP_DIR") is { Length: > 0 } dumpDirectory)
                        Cv2.ImWrite(Path.Combine(dumpDirectory, $"reread-p{page.Number}-r{row.Number}-{column.Meaning}-{left}x{top}.png"), scaled);
                    using var pix = Pix.LoadFromMemory(scaled.ImEncode(".png"));
                    // A line of text first; a single figure ("1") is often not found as a line, so a word and then a single character are tried when it gave no number.
                    var text = "";
                    double confidence = 0;
                    foreach (var mode in new[] { PageSegMode.SingleLine, PageSegMode.SingleWord, PageSegMode.SingleChar })
                    {
                        using var result = engine!.Process(pix, mode);
                        var attempt = (result.GetText() ?? "").Trim().Replace(" ", "").Replace("\n", "");
                        var attemptConfidence = result.GetMeanConfidence();
                        if (text.Length == 0 || (ReadNumber().IsMatch(attempt) && !ReadNumber().IsMatch(text))) { text = attempt; confidence = attemptConfidence; }
                        if (ReadNumber().IsMatch(text) && confidence >= 0.5) break;
                    }
                    if (debug) Console.WriteLine($"[reread] page {page.Number} row {row.Number ?? rowIndex + 1} {column.Meaning}: \"{row.Cells.GetValueOrDefault(column.Id, "")}\" -> \"{text}\" ({confidence:F2})");
                    text = WithoutBorderDigit(text, table.Rows.Select(other => other.Cells.GetValueOrDefault(column.Id, "").Trim()));
                    if (ReadNumber().IsMatch(text) && InvoiceValues.ParseNumber(text, hint) is not null) readings[(rowIndex, column.Id)] = (text, confidence);
                }
            }
        }
        finally { tesseractGate.Release(); }
        // What replaces what: in a row that fails the arithmetic, the combination of quantity, price and value (each as read at first or as read again) that adds up
        // and changes the fewest cells; everywhere else a cell that is not a number takes the new reading when it is confident (an empty cell only when it is very
        // confident).
        var chosen = new List<(int RowIndex, InvoiceColumn Column, string Text)>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var decided = new HashSet<string>();
            if (rowsToCheck.Contains(rowIndex))
            {
                var best = BestCombination(row, [quantityColumn!, priceColumn!, valueColumn!], rowIndex, readings, hint);
                if (best is not null)
                    foreach (var (column, text) in best) { decided.Add(column.Id); chosen.Add((rowIndex, column, text)); }
            }
            foreach (var (column, _) in numeric)
            {
                if (decided.Contains(column.Id)) continue;
                var old = row.Cells.GetValueOrDefault(column.Id, "");
                // A number with the scraps of a border at its ends is the number.
                if (Tidy(old) != old.Trim() && Tidy(old).Length > 0) { chosen.Add((rowIndex, column, Tidy(old))); continue; }
                if (!readings.TryGetValue((rowIndex, column.Id), out var reading)) continue;
                if (!NeedsReread(old, coreColumn: true) || reading.Confidence < (old.Trim().Length == 0 ? 0.8 : 0.5)) continue;
                chosen.Add((rowIndex, column, reading.Text));
            }
        }
        if (chosen.Count == 0) return read;
        var replacements = chosen.GroupBy(item => table.Rows[item.RowIndex].Page).ToDictionary(group => group.Key, group => group.Select(item => (item.Column, Row: table.Rows[item.RowIndex], item.Text)).ToList());

        var pages = new List<InvoicePageData>();
        foreach (var page in read.Document.Pages)
        {
            if (!replacements.TryGetValue(page.Number, out var changes)) { pages.Add(page); continue; }
            var words = page.Words.ToList();
            foreach (var (tableColumn, row, text) in changes)
            {
                if (OnPage(page, tableColumn) is not { } column) continue;
                bool InCell(InvoiceWord word) => word.CenterX >= column.Left - 1 && word.CenterX <= column.Right + 1 && word.CenterY >= row.Top && word.CenterY < row.Bottom;
                var inside = words.Where(InCell).ToList();
                var index = inside.Count > 0 ? words.IndexOf(inside[0]) : words.Count;
                var box = inside.Count > 0
                    ? (X: inside.Min(word => word.X), Y: inside.Min(word => word.Y), Right: inside.Max(word => word.Right), Bottom: inside.Max(word => word.Bottom))
                    : (X: column.Left + 1, Y: (row.Top + row.Bottom) / 2 - 4, Right: column.Right - 1, Bottom: (row.Top + row.Bottom) / 2 + 4);
                var order = inside.Count > 0 ? inside.Min(word => word.Order) : words.Count == 0 ? 0 : words.Max(word => word.Order) + 1;
                words.RemoveAll(InCell);
                words.Insert(Math.Min(index, words.Count), new InvoiceWord(page.Number, text, box.X, box.Y, Math.Max(1, box.Right - box.X), Math.Max(1, box.Bottom - box.Y), order));
            }
            pages.Add(page with { Words = words });
        }
        return read with { Document = new InvoiceDocument(pages) };
    }

    // The vertical border of a cell, close to the last figure, is read as a "1". When the other numbers of the column all have two decimals and the new reading has
    // three, the last of which is that "1", the reading is taken without it.
    [GeneratedRegex(@"^\d+[.,]\d{2}$")]
    private static partial Regex TwoDecimals();

    internal static string WithoutBorderDigit(string reading, IEnumerable<string> columnTexts)
    {
        if (!Regex.IsMatch(reading, @"^\d+[.,]\d{2}1$")) return reading;
        var others = columnTexts.Where(text => text.Length > 0 && CleanNumber().IsMatch(text) && text.Any(char.IsAsciiDigit)).ToList();
        var withDecimals = others.Where(text => text.Contains('.') || text.Contains(',')).ToList();
        var two = withDecimals.Count(text => TwoDecimals().IsMatch(text));
        return two >= 2 && withDecimals.All(text => TwoDecimals().IsMatch(text) || !Regex.IsMatch(text, @"[.,]\d{3,}$")) && two >= 0.7 * withDecimals.Count ? reading[..^1] : reading;
    }

    // For a row whose quantity x price does not give its value: the readings of the three cells (as they were, or as read again) that add up, with the fewest
    // cells changed. Only the cells that change are returned; null when no combination adds up or the cells as they were already do.
    private static List<(InvoiceColumn Column, string Text)>? BestCombination(InvoiceTableRow row, InvoiceColumn[] columns, int rowIndex,
        Dictionary<(int RowIndex, string ColumnId), (string Text, double Confidence)> readings, char? hint)
    {
        var options = new List<List<(string Text, decimal Value, bool Changed)>>();
        foreach (var column in columns)
        {
            var list = new List<(string Text, decimal Value, bool Changed)>();
            var old = Tidy(row.Cells.GetValueOrDefault(column.Id, ""));
            if (InvoiceValues.ParseNumber(old, hint) is { } oldValue) list.Add((old, oldValue, false));
            if (readings.TryGetValue((rowIndex, column.Id), out var reading) && reading.Confidence >= 0.5 && reading.Text != old && InvoiceValues.ParseNumber(reading.Text, hint) is { } newValue)
                list.Add((reading.Text, newValue, true));
            options.Add(list);
        }
        (int Changed, (string Text, decimal Value, bool Changed)[] Pick)? best = null;
        foreach (var quantity in options[0])
            foreach (var price in options[1])
                foreach (var value in options[2])
                {
                    if (Math.Abs(quantity.Value * price.Value - value.Value) > Math.Max(0.05m, Math.Abs(value.Value) * 0.001m)) continue;
                    var changed = (quantity.Changed ? 1 : 0) + (price.Changed ? 1 : 0) + (value.Changed ? 1 : 0);
                    if (best is null || changed < best.Value.Changed) best = (changed, [quantity, price, value]);
                }
        if (best is null || best.Value.Changed == 0) return null;
        return [.. best.Value.Pick.Select((pick, index) => (Column: columns[index], pick)).Where(item => item.pick.Changed).Select(item => (item.Column, item.pick.Text))];
    }

    // The rectangle of the one line of text in a cell: the rows of ink (Otsu) are grouped into runs, a run that is too low to be a line (a scrap of a dotted rule)
    // or that touches the edge of the cell (the top of the next row, the bottom of the one above) is not the cell's own, and of those left the one with the most
    // ink is the line. Null when the cell has no line of text.
    internal static OpenCvSharp.Rect? TightLine(Mat cell)
    {
        if (cell.Rows < 12 || cell.Cols < 12) return null;
        using var ink = new Mat();
        Cv2.Threshold(cell, ink, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var rowSums = new Mat();
        Cv2.Reduce(ink, rowSums, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32S);
        rowSums.GetArray(out int[] sums);
        var runs = new List<(int From, int To, long Ink)>();
        int? start = null;
        var lastInk = -10;
        for (var y = 0; y < sums.Length; y++)
        {
            var inked = sums[y] >= 2 * 255;
            if (inked) { start ??= y; lastInk = y; }
            else if (start is { } from && y - lastInk > 3) { runs.Add((from, lastInk, 0)); start = null; }
        }
        if (start is { } open) runs.Add((open, lastInk, 0));
        runs = [.. runs.Select(run => (run.From, run.To, Ink: sums.Skip(run.From).Take(run.To - run.From + 1).Sum(value => (long)value)))];
        var tall = runs.Where(run => run.To - run.From + 1 >= 14).ToList();
        // The text of a row that is cut at the top of its box starts at the edge: when no line is clear of the edges, the line with the most ink is taken.
        var lines = tall.Where(run => run.From > 0 && run.To < sums.Length - 1).ToList();
        if (lines.Count == 0) lines = tall;
        if (lines.Count == 0) return null;
        var chosen = lines.MaxBy(run => run.Ink);
        using var band = new Mat(ink, new OpenCvSharp.Rect(0, chosen.From, ink.Cols, chosen.To - chosen.From + 1));
        using var columnSums = new Mat();
        Cv2.Reduce(band, columnSums, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);
        columnSums.GetArray(out int[] across);
        var xs = Enumerable.Range(0, across.Length).Where(x => across[x] > 0).ToList();
        if (xs.Count == 0) return null;
        // A thin strip of ink apart from the rest and at the very edge of the cell is a scrap of a border (a dotted vertical rule), not a figure of the cell.
        var clusters = new List<(int From, int To)>();
        foreach (var x in xs)
            if (clusters.Count > 0 && x - clusters[^1].To <= 10) clusters[^1] = (clusters[^1].From, x); else clusters.Add((x, x));
        if (clusters.Count > 1)
        {
            var kept = clusters.Where(cluster => !(cluster.To - cluster.From + 1 <= 5 && (cluster.From <= 8 || cluster.To >= cell.Cols - 9))).ToList();
            if (kept.Count > 0) xs = [.. xs.Where(x => kept.Any(cluster => x >= cluster.From && x <= cluster.To))];
        }
        // The height again, from the ink between the figures' own columns only (a border at the side makes the line taller than it is).
        using var inner = new Mat(ink, new OpenCvSharp.Rect(xs[0], chosen.From, xs[^1] - xs[0] + 1, chosen.To - chosen.From + 1));
        using var innerRows = new Mat();
        Cv2.Reduce(inner, innerRows, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32S);
        innerRows.GetArray(out int[] innerSums);
        var inkRows = Enumerable.Range(0, innerSums.Length).Where(y => innerSums[y] > 0).ToList();
        var top = inkRows.Count == 0 ? chosen.From : chosen.From + inkRows[0];
        var bottom = inkRows.Count == 0 ? chosen.To : chosen.From + inkRows[^1];
        var x0 = Math.Max(0, xs[0] - 6);
        var x1 = Math.Min(cell.Cols - 1, xs[^1] + 6);
        var y0 = Math.Max(0, top - 5);
        var y1 = Math.Min(cell.Rows - 1, bottom + 5);
        return new OpenCvSharp.Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    // The picture of an OCR page in the frame its words are in: rendered again at the OCR resolution, straightened by the same angle, turned the same way.
    private static Mat? RenderFrame(byte[] pdf, InvoicePageData page)
    {
        SKBitmap bitmap;
        try
        {
            using var stream = new MemoryStream(pdf);
#pragma warning disable CA1416
            bitmap = Conversion.ToImage(stream, new Index(page.Number - 1), leaveOpen: true, options: new RenderOptions(Dpi: InvoiceAnalysisRules.OcrDpi, Grayscale: true));
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return null; }
        using (bitmap)
        {
            using var gray = InventoryPickupOcrService.ToGrayMat(bitmap);
            var upright = Math.Abs(page.OcrSkew) >= MinScanSkewDegrees ? InventoryPickupOcrService.Rotate(gray, page.OcrSkew, Scalar.White) : gray.Clone();
            if (page.OcrTurn == 0) return upright;
            using (upright)
            {
                var turned = new Mat();
                Cv2.Rotate(upright, turned, page.OcrTurn switch { 90 => RotateFlags.Rotate90Clockwise, 180 => RotateFlags.Rotate180, _ => RotateFlags.Rotate90Counterclockwise });
                return turned;
            }
        }
    }
}
