using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using CvRect = OpenCvSharp.Rect;

namespace BlazorStoc.Services;

// One row read from the scanned form: the printed "Cod produs" text and, if the handwritten "Valoare reala" cell
// was filled in, the recognized number. Uncertain rows are still returned (never silently dropped) so the review
// page can show them for the user to inspect and correct (TODO.md Task 1, decizia privind selectia implicita).
// Number is the row's printed "Nr. crt." (its running number inside the subcategory table), null on forms without that
// column or when it could not be settled; it is display-only and is never stored.
public sealed record InventoryPickupScanRow(int Page, string RawCode, int? RecognizedValue, bool Uncertain, int? Number = null);
public sealed record InventoryPickupScanResult(int PageCount, IReadOnlyList<InventoryPickupScanRow> Rows, IReadOnlyList<int> UnreadablePages);

public sealed class InventoryPickupOcrException(string message) : Exception(message);

public static class InventoryPickupOcrRules
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;
    public const int MaxPages = 50;
    public const string NotAPdfMessage = "Fisierul trebuie sa fie un PDF.";
    public const string TooLargeMessage = "Fisierul depaseste dimensiunea maxima acceptata (20 MB).";
    public const string TooManyPagesMessage = "Fisierul depaseste numarul maxim de pagini acceptat (50).";
    public const string UnreadableMessage = "Fisierul nu a putut fi citit ca PDF valid.";
    public const string NoTableFoundMessage =
        "Nu a fost gasit niciun tabel recunoscut in fisier. Incarca situatia de inventar generata de aplicatie (pagina Inventar).";
    public const string HeaderCode = "Cod produs";
}

public interface IInventoryPickupOcrService
{
    Task<InventoryPickupScanResult> ScanAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

// Reads a scanned copy of the situatia de inventar PDF (Components/Pages/Inventory.razor + InventoryPdfWriter):
// rasterizes each page, deskews it, and finds the table grid in the scan itself: the long horizontal rule lines
// InventoryPdfWriter draws give the rows, and the vertical rules that run through a whole table (anchored at its
// "Cod produs" header row) give its columns - no fixed position or cell size is assumed (InventoryPdfLayout is only a
// fallback for broken rules). A faint scan or pencil is contrast-stretched. It then reads the printed "Nr. crt." and
// "Cod produs" cells with classic OCR (Tesseract) and the handwritten "Valoare reala" cell by segmenting its ink into
// individual digit blobs and classifying each with a small ONNX handwritten-digit model.
public sealed class InventoryPickupOcrService : IInventoryPickupOcrService, IDisposable
{
    // 300 DPI keeps a full A4 page under 2500x3500px (fast for OpenCV) while giving Tesseract and the digit
    // classifier enough resolution to read 11-12pt printed text and pen strokes reliably.
    private const int RenderDpi = 300;
    private const double LineFillRatio = 0.65;
    private const double DividerFillRatio = 0.55;
    // Grid detection (FindGridLines / FindDividers), all relative to the page or to the row - never absolute positions:
    // a horizontal rule must span at least this share of the page width; the opening kernel used to isolate rules is
    // the page width divided by GridLineOpeningDivisor; a vertical rule must cover this share of its row band's height
    // (after widening it by DividerDetectionDilationWidth pixels to absorb residual skew).
    private const double MinGridLineWidthRatio = 0.30;
    private const int GridLineOpeningDivisor = 25;
    private const double DetectedDividerFillRatio = 0.75;
    private const int DividerDetectionDilationWidth = 5;
    // Contrast handling for faint scans and pencil: ink contrast = paper level minus the darkest 1% of pixels. Below
    // MinUsableContrast there is no ink at all (paper noise); below FaintInkContrast (graphite pencil, a washed-out
    // scan, a nearly dry pen) the image is stretched before thresholding.
    private const double ContrastInkPercentile = 0.01;
    private const int MinUsableContrast = 25;
    private const int FaintInkContrast = 140;
    private const double CellPaddingPoints = 4;
    private const double DigitConfidenceThreshold = 0.80;

    // Skew detection/correction (see FindSkewDegrees): angles below this are left uncorrected (rotating a
    // perfectly straight page still blurs it slightly through interpolation, for no benefit), and the search
    // never looks beyond this many degrees either side of upright - real paper-feed skew confirmed on user scans
    // so far stays a few degrees at most; a page rotated further than that is not a simple feed skew any more.
    private const double MinCorrectedSkewDegrees = 0.6;
    private const double MaxSearchedSkewDegrees = 8.0;

    // A fresh TesseractEngine per cell (rather than one reused instance) - the charlesw/Tesseract wrapper's engine
    // does not reliably reset its internal state between many sequential Process() calls (later cells on the same
    // page came back as garbage in testing against the real sample form). Engine construction is not free, but
    // this runs a handful of times per uploaded file, never in a hot path.
    private readonly SemaphoreSlim tesseractGate = new(1, 1);
    private readonly string tessdataPath;
    private readonly Lazy<InferenceSession> digitModel;

    public InventoryPickupOcrService(IWebHostEnvironmentTessdataPath tessdataPath)
    {
        this.tessdataPath = tessdataPath.Path;
        digitModel = new Lazy<InferenceSession>(() => new InferenceSession(EmbeddedResources.Read("Assets.Models.mnist-12.onnx")));
    }

    public async Task<InventoryPickupScanResult> ScanAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        using var buffered = new MemoryStream();
        await pdfStream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        if (buffered.Length == 0) throw new InventoryPickupOcrException(InventoryPickupOcrRules.UnreadableMessage);
        buffered.Position = 0;

        List<SKBitmap> pages;
        try
        {
            // PDFtoImage supports Windows/Linux/macOS (PDFium + SkiaSharp); the analyzer only wants the
            // supported-platform list spelled out explicitly at the call site.
#pragma warning disable CA1416
            pages = Conversion.ToImages(buffered, leaveOpen: true, options: new RenderOptions(Dpi: RenderDpi, Grayscale: true)).ToList();
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InventoryPickupOcrException(InventoryPickupOcrRules.UnreadableMessage);
        }
        try
        {
            if (pages.Count == 0) throw new InventoryPickupOcrException(InventoryPickupOcrRules.UnreadableMessage);
            if (pages.Count > InventoryPickupOcrRules.MaxPages) throw new InventoryPickupOcrException(InventoryPickupOcrRules.TooManyPagesMessage);

            var rows = new List<InventoryPickupScanRow>();
            var unreadablePages = new List<int>();
            for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageRows = await ScanPageAsync(pages[pageIndex], pageIndex + 1, cancellationToken).ConfigureAwait(false);
                if (pageRows.Count == 0) unreadablePages.Add(pageIndex + 1);
                rows.AddRange(pageRows);
            }
            if (rows.Count == 0) throw new InventoryPickupOcrException(InventoryPickupOcrRules.NoTableFoundMessage);
            return new InventoryPickupScanResult(pages.Count, rows, unreadablePages);
        }
        finally
        {
            foreach (var bitmap in pages) bitmap.Dispose();
        }
    }

    // One data row read from the page before its "Nr. crt." is settled (see ScanPageAsync): Block numbers the tables of
    // the page (a table starts at its "Cod produs" header row) and Index is the row's position inside its table.
    private sealed record PendingRow(int Block, int Index, string Code, int? Value, bool Uncertain, int? PrintedNumber);

    private async Task<List<InventoryPickupScanRow>> ScanPageAsync(SKBitmap page, int pageNumber, CancellationToken cancellationToken)
    {
        var scale = RenderDpi / 72.0;
        var pageWidthPoints = page.Width / scale;
        var columns = InventoryPdfLayout.ComputeColumns(pageWidthPoints);
        var debug = Environment.GetEnvironmentVariable("INVENTORY_OCR_DEBUG") == "1";

        using var grayRaw = ToGrayMat(page);
        // A real scan is never perfectly straight (paper feed skew), and confirmed real user scans went from a
        // fraction of a degree up to a couple of degrees. The whole page is deskewed first, before any other
        // geometry is computed, so every downstream step (grid detection, cell cropping) works against an upright
        // page.
        var skewDegrees = FindSkewDegrees(grayRaw);
        if (debug) Console.WriteLine($"[debug] estimated skew = {skewDegrees:F2} degrees");
        using var deskewed = Math.Abs(skewDegrees) >= MinCorrectedSkewDegrees ? Rotate(grayRaw, skewDegrees, Scalar.White) : grayRaw.Clone();
        // A washed-out scan (faint print, pencil) is stretched to the full gray range first, so the rule lines and text
        // survive the thresholding below; a page with normal contrast, or none at all, is left untouched.
        var pageContrast = InkContrast(deskewed);
        using var stretchedPage = pageContrast < FaintInkContrast ? StretchContrast(deskewed) : null;
        var gray = stretchedPage ?? deskewed;
        if (debug) Console.WriteLine($"[debug] page ink contrast = {pageContrast}{(stretchedPage is not null ? " (faint: stretched)" : "")}");
        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        // The table grid is found in the deskewed page itself: long horizontal rule lines give the row edges (and how
        // far the table extends), and the vertical rule lines inside each row band give that row's column edges. No
        // position or cell size is assumed. The geometry computed from the generated PDF (InventoryPdfLayout) is only
        // a fallback for a row whose vertical rules cannot be told apart (faint or broken lines), see below.
        var gridLines = FindGridLines(binary);
        var legacyLeft = ToPixel(columns.NumberX, scale, gray.Cols);
        var legacyRight = ToPixel(columns.RightEdge, scale, gray.Cols);
        if (gridLines.Count < 2)
        {
            // No long rule lines at all: fall back to searching only where the generated layout puts the table.
            gridLines = FindHorizontalLines(binary, legacyLeft, legacyRight)
                .Select(y => new GridLine(y, legacyLeft, legacyRight)).ToList();
            if (debug) Console.WriteLine($"[debug] no grid lines detected, legacy lines: {gridLines.Count}");
        }
        if (debug) Console.WriteLine($"[debug] gray {gray.Rows}x{gray.Cols}, grid lines ({gridLines.Count}): {string.Join(", ", gridLines.Select(line => $"y={line.Y} x={line.Left}-{line.Right}"))}");
        if (gridLines.Count < 2) return [];

        using var verticalSource = new Mat();
        Cv2.Dilate(binary, verticalSource, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(DividerDetectionDilationWidth, 1)));

        // Table blocks: consecutive row bands that carry the table's own left and right borders. A band between two
        // tables (a subcategory heading's text) has no borders, so it ends a block. The column structure is then read
        // ONCE per block, from the vertical rules that run through the whole block starting at its header row: text
        // strokes inside a short row can look like a rule, but only a real rule stays continuous down the table.
        var blocks = new List<List<TableBand>>();
        List<TableBand>? currentBlock = null;
        for (var i = 0; i + 1 < gridLines.Count; i++)
        {
            var top = gridLines[i].Y;
            var bottom = gridLines[i + 1].Y;
            var rowLeft = Math.Max(gridLines[i].Left, gridLines[i + 1].Left);
            var rowRight = Math.Min(gridLines[i].Right, gridLines[i + 1].Right);
            var isRow = bottom - top >= gray.Rows * 0.008 && rowRight - rowLeft >= gray.Cols * MinGridLineWidthRatio &&
                        HasBorders(verticalSource, top, bottom, rowLeft, rowRight);
            if (!isRow)
            {
                if (debug && bottom - top >= gray.Rows * 0.008) Console.WriteLine($"[debug] {top}-{bottom}: between tables (no borders)");
                currentBlock = null;
                continue;
            }
            if (currentBlock is null) { currentBlock = []; blocks.Add(currentBlock); }
            currentBlock.Add(new TableBand(top, bottom, rowLeft, rowRight));
        }

        var codeFraction = (columns.CodeX - columns.PageMargin) / columns.ContentWidth;
        var stockFraction = (columns.StockX - columns.PageMargin) / columns.ContentWidth;
        var realFraction = (columns.RealX - columns.PageMargin) / columns.ContentWidth;
        var horizontalPad = (int)Math.Round(CellPaddingPoints * scale);
        var pending = new List<PendingRow>();
        var hasNumberColumn = false;
        for (var blockNumber = 1; blockNumber <= blocks.Count; blockNumber++)
        {
            var bands = blocks[blockNumber - 1];
            var blockLeft = bands.Min(band => band.Left);
            var blockRight = bands.Max(band => band.Right);
            // Vertical rules along the whole block. Five = Nr. crt. | Cod produs | Valoare stoc | Valoare reala; four = the
            // same table without the leading "Nr. crt." column (forms printed before that column existed).
            var dividers = FindDividers(verticalSource, bands[0].Top, bands[^1].Bottom, blockLeft, blockRight);
            int xLeft, xCode, xStock, xReal, xRight;
            bool blockHasNumber;
            var fallbackColumns = false;
            if (dividers.Count == 5)
            {
                (xLeft, xCode, xStock, xReal, xRight) = (dividers[0], dividers[1], dividers[2], dividers[3], dividers[4]);
                blockHasNumber = true;
            }
            else if (dividers.Count == 4)
            {
                (xLeft, xStock, xReal, xRight) = (dividers[0], dividers[1], dividers[2], dividers[3]);
                xCode = xLeft;
                blockHasNumber = false;
            }
            else
            {
                // Not a complete set of rules (faint or broken ones): use the block's horizontal extent and the same
                // column FRACTIONS the generated PDF used (invariant to print/scan scale).
                xLeft = blockLeft;
                xRight = blockRight;
                xCode = xLeft + (int)Math.Round(codeFraction * (xRight - xLeft));
                xStock = xLeft + (int)Math.Round(stockFraction * (xRight - xLeft));
                xReal = xLeft + (int)Math.Round(realFraction * (xRight - xLeft));
                blockHasNumber = true;
                fallbackColumns = true;
            }
            if (debug) Console.WriteLine($"[debug] table {blockNumber}: rows {bands[0].Top}-{bands[^1].Bottom}, {dividers.Count} rules ({string.Join(",", dividers)}) -> xLeft={xLeft} xCode={xCode} xStock={xStock} xReal={xReal} xRight={xRight} numberColumn={blockHasNumber}{(fallbackColumns ? " (fallback fractions)" : "")}");

            var indexInBlock = 0;
            foreach (var band in bands)
            {
                var top = band.Top;
                var bottom = band.Bottom;
                var height = bottom - top;
                // The vertical inset only needs to clear the rule lines' own stroke width (a handful of pixels
                // regardless of DPI): rows can be as short as one text line, so scaling this from PDF points the way
                // the horizontal inset does would eat a large share of a short row's actual content.
                var verticalPad = Math.Min(6, height / 6);
                var codeCell = SafeCrop(gray, xCode + horizontalPad, top + verticalPad, xStock - xCode - 2 * horizontalPad, height - 2 * verticalPad);
                var realCell = SafeCrop(gray, xReal + horizontalPad, top + verticalPad, xRight - xReal - 2 * horizontalPad, height - 2 * verticalPad);
                if (codeCell is null || realCell is null) { codeCell?.Dispose(); realCell?.Dispose(); continue; }

                string code;
                try { code = await ReadCodeAsync(codeCell, cancellationToken).ConfigureAwait(false); }
                finally { codeCell.Dispose(); }
                if (debug) Console.WriteLine($"[debug] {top}-{bottom}: code='{code}'");

                // The header row ("Cod produs") is not data; the rows after it are numbered from 1 within this table.
                if (TextNormalization.SameUniqueValue(code, InventoryPickupOcrRules.HeaderCode)) { realCell.Dispose(); indexInBlock = 0; continue; }
                indexInBlock++;
                if (code.Length == 0) { realCell.Dispose(); continue; }

                var (hasInk, value, uncertain) = ReadHandwrittenWithContrast(realCell, debug);
                if (debug) Console.WriteLine($"[debug] {top}-{bottom}: hasInk={hasInk} value={value} uncertain={uncertain}");
                realCell.Dispose();
                if (!hasInk) continue; // empty cell: "neinventariat", no row at all (subtask 1.2)

                int? printedNumber = null;
                if (blockHasNumber)
                {
                    hasNumberColumn = true;
                    using var numberCell = SafeCrop(gray, xLeft + horizontalPad, top + verticalPad, xCode - xLeft - 2 * horizontalPad, height - 2 * verticalPad);
                    if (numberCell is not null) printedNumber = await ReadPrintedNumberAsync(numberCell, cancellationToken).ConfigureAwait(false);
                    if (debug) Console.WriteLine($"[debug] {top}-{bottom}: printed number={printedNumber?.ToString() ?? "?"} (index {indexInBlock} in table {blockNumber})");
                }
                pending.Add(new PendingRow(blockNumber, indexInBlock, code, value, uncertain, printedNumber));
            }
        }

        // "Nr. crt." is settled per table from the sequence: the numbers run 1, 2, 3... down a table (continuing on the
        // next page when a subcategory does not fit), so each read number implies the table's starting offset
        // (number - position). The offset most reads agree on wins, which corrects an isolated misread digit and fills
        // in a row whose number could not be read at all. A table where nothing was read keeps no number.
        var offsets = pending.Where(row => row.PrintedNumber is not null)
            .GroupBy(row => row.Block)
            .ToDictionary(group => group.Key, group => group
                .GroupBy(row => row.PrintedNumber!.Value - row.Index)
                .OrderByDescending(offsetGroup => offsetGroup.Count()).ThenBy(offsetGroup => offsetGroup.Key)
                .First().Key);
        return pending.Select(row => new InventoryPickupScanRow(pageNumber, row.Code, row.Value, row.Uncertain,
            hasNumberColumn && offsets.TryGetValue(row.Block, out var offset) && row.Index + offset > 0 ? row.Index + offset : null)).ToList();
    }

    internal static Mat ToGrayMat(SKBitmap bitmap)
    {
        using var normalized = bitmap.ColorType == SKColorType.Gray8 ? null : bitmap.Copy(SKColorType.Gray8);
        var source = normalized ?? bitmap;
        using var mat = Mat.FromPixelData(source.Height, source.Width, MatType.CV_8UC1, source.GetPixels(), (long)source.RowBytes);
        return mat.Clone();
    }

    // Classic projection-profile skew estimation: at the correct upright angle, the page's horizontal rule lines
    // and printed text lines each land on a narrow band of rows, so the ink-per-row profile alternates sharply
    // between near-empty and near-full rows (high variance). At any other angle that same ink smears across more
    // rows (lower variance). Searching for the angle that maximizes this variance is standard and does not depend
    // on the table geometry at all, unlike the line/column detection below - it works the same whether the page
    // has one table or eight scattered across it. Runs on a small downscaled copy purely for speed; the angle
    // found is then applied to the full-resolution page once by the caller.
    internal static double FindSkewDegrees(Mat grayFullRes)
    {
        using var binary = new Mat();
        Cv2.Threshold(grayFullRes, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var small = new Mat();
        Cv2.Resize(binary, small, new Size(), 0.25, 0.25, InterpolationFlags.Area);

        var bestAngle = 0.0;
        var bestScore = -1.0;
        foreach (var angle in SearchAngles(-MaxSearchedSkewDegrees, MaxSearchedSkewDegrees, 1.0))
        {
            var score = ProjectionProfileVariance(small, angle);
            if (score > bestScore) { bestScore = score; bestAngle = angle; }
        }
        // Refine around the coarse best angle at a tenth of a degree; the coarse pass above only guarantees
        // landing within half a degree of the true skew, not enough to keep the residual under the tolerance
        // FindHorizontalLines' own dilation absorbs.
        foreach (var angle in SearchAngles(bestAngle - 1.0, bestAngle + 1.0, 0.1))
        {
            var score = ProjectionProfileVariance(small, angle);
            if (score > bestScore) { bestScore = score; bestAngle = angle; }
        }
        return bestAngle;
    }

    private static IEnumerable<double> SearchAngles(double from, double to, double step)
    {
        for (var angle = from; angle <= to + step / 2; angle += step) yield return angle;
    }

    private static double ProjectionProfileVariance(Mat binarySmall, double angleDegrees)
    {
        using var rotated = Rotate(binarySmall, angleDegrees, Scalar.Black);
        using var rowSums = new Mat();
        Cv2.Reduce(rotated, rowSums, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32S);
        rowSums.GetArray(out int[] sums);
        var mean = sums.Average();
        return sums.Average(value => (value - mean) * (double)(value - mean));
    }

    internal static Mat Rotate(Mat source, double angleDegrees, Scalar borderFill)
    {
        using var rotationMatrix = Cv2.GetRotationMatrix2D(new Point2f(source.Cols / 2f, source.Rows / 2f), angleDegrees, 1.0);
        var destination = new Mat();
        Cv2.WarpAffine(source, destination, rotationMatrix, source.Size(), InterpolationFlags.Linear, BorderTypes.Constant, borderFill);
        return destination;
    }

    private static int ToPixel(double points, double scale, int max) => Math.Clamp((int)Math.Round(points * scale), 0, max - 1);

    // A grid rule line found in the page: its row (y) and horizontal extent.
    private sealed record GridLine(int Y, int Left, int Right);

    // The table grid is read from the scan itself, not from the generated PDF's geometry, because a printed-then-
    // scanned page rarely lands on those coordinates (printer scaling, feed offset - a real Konica Minolta scan of
    // 30.09.2026 had the table's left border ~88 px, 7 mm, from where the PDF puts it). Long horizontal rules are
    // isolated with a morphological opening whose kernel is a fraction of the PAGE width (text strokes are far shorter
    // and disappear), merged into lines by row, and each keeps the extent of its longest unbroken stretch.
    private static List<GridLine> FindGridLines(Mat binary)
    {
        var minLength = (int)(binary.Cols * MinGridLineWidthRatio);
        using var dilated = new Mat();
        Cv2.Dilate(binary, dilated, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, LineDetectionDilationHeight)));
        using var opened = new Mat();
        Cv2.MorphologyEx(dilated, opened, MorphTypes.Open, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(15, binary.Cols / GridLineOpeningDivisor), 1)));

        var candidates = new List<int>();
        int openedRows = opened.Rows, openedCols = opened.Cols;
        for (var y = 0; y < openedRows; y++)
        {
            using var row = opened.SubMat(y, y + 1, 0, openedCols);
            if (Cv2.CountNonZero(row) >= minLength) candidates.Add(y);
        }

        var lines = new List<GridLine>();
        void Flush(int runStart, int runEnd)
        {
            using var band = opened.SubMat(runStart, runEnd + 1, 0, opened.Cols);
            using var presence = new Mat();
            Cv2.Reduce(band, presence, ReduceDimension.Row, ReduceTypes.Max, -1);
            presence.GetArray(out byte[] columns);
            var (start, end) = LongestSegment(columns, 12);
            if (end - start + 1 >= minLength) lines.Add(new GridLine((runStart + runEnd) / 2, start, end));
        }
        var runStart = -1;
        var previous = -10;
        foreach (var y in candidates)
        {
            if (y > previous + 1)
            {
                if (runStart >= 0) Flush(runStart, previous);
                runStart = y;
            }
            previous = y;
        }
        if (runStart >= 0) Flush(runStart, previous);
        return lines;
    }

    // Longest stretch of non-zero entries, bridging gaps of up to maxGap zeros (a scanned rule has small breaks).
    private static (int Start, int End) LongestSegment(byte[] present, int maxGap)
    {
        var bestStart = 0;
        var bestEnd = -1;
        var start = -1;
        var last = -1;
        for (var i = 0; i < present.Length; i++)
        {
            if (present[i] == 0) continue;
            if (start < 0 || i - last > maxGap)
            {
                if (start >= 0 && last - start > bestEnd - bestStart) { bestStart = start; bestEnd = last; }
                start = i;
            }
            last = i;
        }
        if (start >= 0 && last - start > bestEnd - bestStart) { bestStart = start; bestEnd = last; }
        return (bestStart, bestEnd);
    }

    // The x positions of the vertical rules inside one row band: columns whose ink covers most of the band's height
    // (text strokes cover at most about half of it), neighbouring columns merged into one rule at their midpoint.
    // The band's own top/bottom rules are excluded by a small inset, and only the row's horizontal extent is searched.
    private static List<int> FindDividers(Mat verticalSource, int top, int bottom, int left, int right)
    {
        var inset = Math.Min(8, (bottom - top) / 6);
        var bandTop = top + inset;
        var bandBottom = bottom - inset;
        var dividers = new List<int>();
        if (bandBottom <= bandTop) return dividers;
        var from = Math.Max(0, left - 8);
        var to = Math.Min(verticalSource.Cols, right + 9);
        using var band = verticalSource.SubMat(bandTop, bandBottom, from, to);
        using var counts = new Mat();
        Cv2.Reduce(band, counts, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);
        counts.GetArray(out int[] sums);
        var threshold = 255.0 * (bandBottom - bandTop) * DetectedDividerFillRatio;
        var runStart = -1;
        var previous = -10;
        for (var x = 0; x < sums.Length; x++)
        {
            if (sums[x] < threshold) continue;
            if (x > previous + 2)
            {
                if (runStart >= 0) dividers.Add(from + (runStart + previous) / 2);
                runStart = x;
            }
            previous = x;
        }
        if (runStart >= 0) dividers.Add(from + (runStart + previous) / 2);
        return dividers;
    }
    // A rule line perfectly horizontal in the printed page lands on a single image row after scanning only if the
    // paper fed in dead straight. Even a fraction of a degree of skew (unavoidable on a real scanner/photocopier,
    // confirmed against a real user scan where well under half a degree was enough) spreads that same line's ink
    // over a dozen rows, none of which alone reaches the fill ratio below - the table was otherwise read correctly
    // by eye but zero lines were detected. A small vertical dilation merges a few rows' ink together before the
    // fill-ratio check, tolerating that spread without loosening the ratio itself (which would risk false
    // positives from content that is not a rule line).
    private const int LineDetectionDilationHeight = 7;

    // Horizontal rule lines: image rows where most pixels across the table width are ink. Adjacent ink rows
    // (a rule is a few pixels thick after scanning, or merged further by the dilation above) are merged into a
    // single line at their midpoint.
    private static List<int> FindHorizontalLines(Mat binary, int xLeft, int xRight)
    {
        var width = xRight - xLeft;
        if (width <= 0) return [];
        var threshold = (int)(width * LineFillRatio);
        using var dilated = new Mat();
        Cv2.Dilate(binary, dilated, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, LineDetectionDilationHeight)));
        var candidates = new List<int>();
        var rows = dilated.Rows;
        for (var y = 0; y < rows; y++)
        {
            using var strip = dilated.SubMat(y, y + 1, xLeft, xRight);
            if (Cv2.CountNonZero(strip) >= threshold) candidates.Add(y);
        }
        var lines = new List<int>();
        var runStart = -1;
        var previous = -10;
        foreach (var y in candidates)
        {
            if (y > previous + 1)
            {
                if (runStart >= 0) lines.Add((runStart + previous) / 2);
                runStart = y;
            }
            previous = y;
        }
        if (runStart >= 0) lines.Add((runStart + previous) / 2);
        return lines;
    }

    // Searches a small neighbourhood around the calibrated x rather than that exact column: a real scan's rule
    // lines drift by a few pixels from row to row (paper skew, slight non-uniform print/scan scaling), enough to
    // miss an exact column even right after calibrating against the page's first table (observed against the
    // real sample form - a later table's rows can land a handful of pixels off from an earlier one's).
    private static bool HasVerticalDivider(Mat binary, int x, int top, int bottom, int tolerance = 6)
    {
        var height = bottom - top;
        if (height <= 0) return false;
        var threshold = height * DividerFillRatio;
        var lastColumn = binary.Cols - 1;
        for (var candidate = Math.Max(0, x - tolerance); candidate <= Math.Min(lastColumn, x + tolerance); candidate++)
        {
            using var strip = binary.SubMat(top, bottom, candidate, candidate + 1);
            if (Cv2.CountNonZero(strip) >= threshold) return true;
        }
        return false;
    }

    private static Mat? SafeCrop(Mat source, int x, int y, int width, int height)
    {
        x = Math.Max(x, 0);
        y = Math.Max(y, 0);
        width = Math.Min(width, source.Cols - x);
        height = Math.Min(height, source.Rows - y);
        return width <= 0 || height <= 0 ? null : new Mat(source, new CvRect(x, y, width, height)).Clone();
    }

    // A row of a table: the y of its top/bottom rule lines and the horizontal extent they share.
    private sealed record TableBand(int Top, int Bottom, int Left, int Right);

    // A row band belongs to a table when both of the table's outer borders run through it (the row rectangle is drawn
    // with all four sides). Independent of any expected position: the borders are looked for where the band's own
    // horizontal rules end.
    private static bool HasBorders(Mat verticalSource, int top, int bottom, int left, int right) =>
        CoversBand(verticalSource, left, top, bottom) && CoversBand(verticalSource, right, top, bottom);

    private static bool CoversBand(Mat verticalSource, int x, int top, int bottom)
    {
        var inset = Math.Min(8, (bottom - top) / 6);
        var bandTop = top + inset;
        var bandBottom = bottom - inset;
        if (bandBottom <= bandTop) return false;
        var from = Math.Max(0, x - 6);
        var to = Math.Min(verticalSource.Cols, x + 7);
        if (to <= from) return false;
        using var band = verticalSource.SubMat(bandTop, bandBottom, from, to);
        using var counts = new Mat();
        Cv2.Reduce(band, counts, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);
        counts.GetArray(out int[] sums);
        var threshold = 255.0 * (bandBottom - bandTop) * DetectedDividerFillRatio;
        return sums.Any(sum => sum >= threshold);
    }

    // Gray level below which the given share of the cell's/page's pixels lie (0 = darkest).
    private static int GrayPercentile(Mat gray, double fraction)
    {
        using var histogram = new Mat();
        Cv2.CalcHist([gray], [0], null, histogram, 1, [256], [new Rangef(0, 256)]);
        histogram.GetArray(out float[] bins);
        var target = fraction * gray.Rows * gray.Cols;
        float accumulated = 0;
        for (var level = 0; level < bins.Length; level++)
        {
            accumulated += bins[level];
            if (accumulated >= target) return level;
        }
        return 255;
    }

    // Ink contrast of an image: the paper level (median) minus the level of its darkest ink (1st percentile).
    private static int InkContrast(Mat gray) => GrayPercentile(gray, 0.5) - GrayPercentile(gray, ContrastInkPercentile);

    // Stretches the ink..paper range to the full 0..255 range so faint pen strokes / a washed-out scan become clearly
    // dark before thresholding. Returns null when the image has no usable contrast at all (a blank cell: stretching
    // would only turn paper noise into "ink").
    private static Mat? StretchContrast(Mat gray)
    {
        var ink = GrayPercentile(gray, ContrastInkPercentile);
        var paper = GrayPercentile(gray, 0.5);
        if (paper - ink < MinUsableContrast) return null;
        var stretched = new Mat();
        gray.ConvertTo(stretched, MatType.CV_8UC1, 255.0 / (paper - ink), -ink * 255.0 / (paper - ink));
        return stretched;
    }

    // Reads a handwritten cell; when the read is unsure (or a faint pen left nothing readable) it is retried once on a
    // contrast-stretched copy of the cell, and the retry is kept when it is at least as good (a confident reading
    // beats an uncertain one). A very faint cell is stretched up front. A cell with no contrast at all stays empty.
    private (bool HasInk, int? Value, bool Uncertain) ReadHandwrittenWithContrast(Mat cell, bool debug)
    {
        var first = ReadHandwrittenNumber(cell);
        var contrast = InkContrast(cell);
        if (contrast < MinUsableContrast) return first;
        var faint = contrast < FaintInkContrast;
        if (!faint && first.HasInk && !first.Uncertain) return first;
        using var stretched = StretchContrast(cell);
        if (stretched is null) return first;
        var second = ReadHandwrittenNumber(stretched, thickenStrokes: true);
        if (debug) Console.WriteLine($"[debug]   contrast {contrast}: first=({first.HasInk},{first.Value},{first.Uncertain}) stretched=({second.HasInk},{second.Value},{second.Uncertain})");
        if (!first.HasInk) return second;                     // the faint pen was not seen at all
        if (!second.HasInk) return first;
        // More digits found after stretching means strokes the plain read lost (a thin pencil "1" simply vanishes and
        // the rest reads as a sure, wrong number), so that read wins even if the plain one looked confident.
        static int DigitCount(int? value) => value is int number ? number.ToString(System.Globalization.CultureInfo.InvariantCulture).Length : 0;
        if (DigitCount(second.Value) > DigitCount(first.Value)) return second;
        if (first.Uncertain && !second.Uncertain) return second;
        return first;
    }

    private async Task<string> ReadCodeAsync(Mat cell, CancellationToken cancellationToken)
    {
        using var codeMat = new Mat();
        Cv2.Threshold(cell, codeMat, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Cv2.CopyMakeBorder(codeMat, codeMat, 8, 8, 8, 8, BorderTypes.Constant, Scalar.White);
        var bytes = codeMat.ImEncode(".png");
        await tesseractGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.LstmOnly);
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            return TextNormalization.ForObjectNameOrCode(page.GetText());
        }
        finally { tesseractGate.Release(); }
    }

    // Reads the printed "Nr. crt." digits of one cell with Tesseract restricted to digits; null when nothing usable.
    private async Task<int?> ReadPrintedNumberAsync(Mat cell, CancellationToken cancellationToken)
    {
        using var numberMat = new Mat();
        Cv2.Threshold(cell, numberMat, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Cv2.CopyMakeBorder(numberMat, numberMat, 8, 8, 8, 8, BorderTypes.Constant, Scalar.White);
        var bytes = numberMat.ImEncode(".png");
        await tesseractGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.LstmOnly);
            engine.SetVariable("tessedit_char_whitelist", "0123456789");
            using var pix = Pix.LoadFromMemory(bytes);
            using var page = engine.Process(pix, PageSegMode.SingleLine);
            var digits = new string(page.GetText().Where(char.IsAsciiDigit).ToArray());
            return digits.Length is > 0 and <= 4 && int.TryParse(digits, out var number) ? number : null;
        }
        finally { tesseractGate.Release(); }
    }
    // Segments the cell's ink into per-digit blobs and classifies each with the embedded MNIST model.
    // HasInk distinguishes a genuinely empty cell (no row at all - subtask 1.2's "neinventariat") from one that
    // has handwriting but could not be read with confidence (still returned as an uncertain row - subtask 1.2/1.3
    // - never silently dropped just because segmentation or classification struggled).
    private (bool HasInk, int? Value, bool Uncertain) ReadHandwrittenNumber(Mat cell, bool thickenStrokes = false)
    {
        using var binary = new Mat();
        Cv2.Threshold(cell, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        var cellArea = cell.Rows * cell.Cols;
        if (Cv2.CountNonZero(binary) < cellArea * 0.001) return (false, null, false); // no ink at all: not counted yet
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
        // Pencil and dry-pen strokes are thin and break into fragments: thickening joins them into one stroke per digit.
        if (thickenStrokes) Cv2.Dilate(binary, binary, Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3)));

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);
        // A pencil or dry-pen "1" is a thin stroke: on the faint path (thickened strokes) the area floor is lower, and
        // the height filter below (a digit is at least 30% of the cell tall) still rejects specks.
        var minArea = cellArea * (thickenStrokes ? 0.002 : 0.008);
        var blobs = new List<(CvRect Box, double CentroidX)>();
        var statsRows = stats.Rows;
        for (var label = 1; label < statsRows; label++) // label 0 is the background
        {
            var area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
            if (area < minArea) continue;
            var box = new CvRect(
                stats.At<int>(label, (int)ConnectedComponentsTypes.Left), stats.At<int>(label, (int)ConnectedComponentsTypes.Top),
                stats.At<int>(label, (int)ConnectedComponentsTypes.Width), stats.At<int>(label, (int)ConnectedComponentsTypes.Height));
            // A digit fills a clear majority of the cell's height but never all of it; a sliver this wide relative
            // to its height is a leftover fragment of the cell's border rather than a stroke.
            if (box.Height < cell.Rows * 0.3 || box.Height > cell.Rows * 0.98) continue;
            blobs.Add((box, centroids.At<double>(label, 0)));
        }
        if (blobs.Count == 0) return (true, null, true); // ink present but nothing segmentable: needs a look

        blobs.Sort((a, b) => a.CentroidX.CompareTo(b.CentroidX));
        var digitsText = "";
        var uncertain = false;
        foreach (var blob in blobs)
        {
            using var digitMat = new Mat(binary, blob.Box);
            var (digit, confidence) = ClassifyDigit(digitMat);
            if (confidence < DigitConfidenceThreshold) uncertain = true;
            digitsText += digit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return int.TryParse(digitsText, out var value) ? (true, value, uncertain) : (true, null, true);
    }

    // Resizes an isolated digit blob into the 28x28, [0,1], white-ink-on-black-background input the embedded
    // ONNX Model Zoo "mnist-12" model expects (see its model card for the exact preprocessing).
    private (int Digit, double Confidence) ClassifyDigit(Mat digitBinary)
    {
        var side = Math.Max(digitBinary.Rows, digitBinary.Cols);
        var square = Mat.Zeros(side, side, MatType.CV_8UC1).ToMat();
        var xOffset = (side - digitBinary.Cols) / 2;
        var yOffset = (side - digitBinary.Rows) / 2;
        digitBinary.CopyTo(new Mat(square, new CvRect(xOffset, yOffset, digitBinary.Cols, digitBinary.Rows)));
        using var padded = new Mat();
        Cv2.CopyMakeBorder(square, padded, side / 5, side / 5, side / 5, side / 5, BorderTypes.Constant, Scalar.Black);
        using var resized = new Mat();
        Cv2.Resize(padded, resized, new Size(28, 28), interpolation: InterpolationFlags.Area);
        square.Dispose();

        var input = new DenseTensor<float>([1, 1, 28, 28]);
        for (var y = 0; y < 28; y++)
            for (var x = 0; x < 28; x++)
                input[0, 0, y, x] = resized.At<byte>(y, x) / 255f;

        var inputName = digitModel.Value.InputMetadata.Keys.First();
        using var results = digitModel.Value.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);
        var logits = results.First().AsEnumerable<float>().ToArray();
        var probabilities = Softmax(logits);
        var best = 0;
        for (var i = 1; i < probabilities.Length; i++)
            if (probabilities[i] > probabilities[best]) best = i;
        return (best, probabilities[best]);
    }

    private static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exp = logits.Select(value => Math.Exp(value - max)).ToArray();
        var sum = exp.Sum();
        return exp.Select(value => (float)(value / sum)).ToArray();
    }

    public void Dispose()
    {
        if (digitModel.IsValueCreated) digitModel.Value.Dispose();
        tesseractGate.Dispose();
    }
}

// Tesseract needs a real directory on disk holding the .traineddata files (Assets/Tessdata, copied to the output
// directory - see BlazorStoc.csproj), unlike the ONNX model and PDF font, which ship as embedded resources.
public interface IWebHostEnvironmentTessdataPath { string Path { get; } }
public sealed class TessdataPath(IWebHostEnvironment environment) : IWebHostEnvironmentTessdataPath
{
    public string Path { get; } = System.IO.Path.Combine(environment.ContentRootPath, "Assets", "Tessdata");
}

internal static class EmbeddedResources
{
    public static byte[] Read(string relativeName)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        var name = $"{assembly.GetName().Name}.{relativeName}";
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resursa incorporata „{relativeName}” lipseste.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
