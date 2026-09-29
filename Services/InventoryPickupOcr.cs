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
public sealed record InventoryPickupScanRow(int Page, string RawCode, int? RecognizedValue, bool Uncertain);
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
// rasterizes each page, finds the printed table grid by its known column geometry (InventoryPdfLayout) and by
// scanning the image for the horizontal/vertical rule lines InventoryPdfWriter draws around every cell, reads the
// printed "Cod produs" cell with classic OCR (Tesseract) and the handwritten "Valoare reala" cell by segmenting
// its ink into individual digit blobs and classifying each with a small ONNX handwritten-digit model. No general
// document-layout detection is used (see TODO.md's "Decizie tehnica pentru recunoasterea scrisului de mana").
public sealed class InventoryPickupOcrService : IInventoryPickupOcrService, IDisposable
{
    // 300 DPI keeps a full A4 page under 2500x3500px (fast for OpenCV) while giving Tesseract and the digit
    // classifier enough resolution to read 11-12pt printed text and pen strokes reliably.
    private const int RenderDpi = 300;
    private const double LineFillRatio = 0.65;
    private const double DividerFillRatio = 0.55;
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

    private async Task<List<InventoryPickupScanRow>> ScanPageAsync(SKBitmap page, int pageNumber, CancellationToken cancellationToken)
    {
        var scale = RenderDpi / 72.0;
        var pageWidthPoints = page.Width / scale;
        var columns = InventoryPdfLayout.ComputeColumns(pageWidthPoints);
        var debug = Environment.GetEnvironmentVariable("INVENTORY_OCR_DEBUG") == "1";

        using var grayRaw = ToGrayMat(page);
        // A real scan is never perfectly straight (paper feed skew), and confirmed real user scans went from a
        // fraction of a degree up to a couple of degrees. Beyond a fraction of a degree, the small dilation in
        // FindHorizontalLines below is no longer enough - the whole page is deskewed here first, before any other
        // geometry is computed, so every downstream step (line detection, column calibration, cell cropping)
        // works against an upright page exactly like the one FindSkewDegrees was tuned against.
        var skewDegrees = FindSkewDegrees(grayRaw);
        if (debug) Console.WriteLine($"[debug] estimated skew = {skewDegrees:F2} degrees");
        using var gray = Math.Abs(skewDegrees) >= MinCorrectedSkewDegrees ? Rotate(grayRaw, skewDegrees, Scalar.White) : grayRaw.Clone();
        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        // Expected column x-positions, assuming the scan reproduces the generated PDF at exactly the requested
        // DPI. A real scan of a printed page rarely does (printer "fit to printable area" scaling, minor
        // registration drift), so these are only a starting point for CalibrateColumns below, not used directly.
        var approxLeft = ToPixel(columns.CodeX, scale, gray.Cols);
        var approxRight = ToPixel(columns.RightEdge, scale, gray.Cols);
        var lines = FindHorizontalLines(binary, approxLeft, approxRight);
        if (debug) Console.WriteLine($"[debug] gray {gray.Rows}x{gray.Cols}, approxLeft={approxLeft} approxRight={approxRight}, lines ({lines.Count}): {string.Join(",", lines)}");
        if (lines.Count < 2) return [];

        var (xLeft, xStock, xReal, xRight) = CalibrateColumns(binary, columns, scale, lines[0], lines[1]);
        if (debug) Console.WriteLine($"[debug] xLeft={xLeft} xStock={xStock} xReal={xReal} xRight={xRight}");
        var rows = new List<InventoryPickupScanRow>();
        for (var i = 0; i + 1 < lines.Count; i++)
        {
            var top = lines[i];
            var bottom = lines[i + 1];
            var height = bottom - top;
            if (height < gray.Rows * 0.008) continue; // touching duplicate line, not a real row
            if (!HasVerticalDivider(binary, xStock, top, bottom) || !HasVerticalDivider(binary, xReal, top, bottom))
            {
                if (debug) Console.WriteLine($"[debug] {top}-{bottom}: no divider");
                continue;
            }

            // The vertical inset only needs to clear the rule lines' own stroke width (a handful of pixels
            // regardless of DPI): rows can be as short as one text line, so scaling this from PDF points the way
            // the horizontal inset does would eat a large share of a short row's actual content.
            var horizontalPad = (int)Math.Round(CellPaddingPoints * scale);
            var verticalPad = Math.Min(6, height / 6);
            var codeCell = SafeCrop(gray, xLeft + horizontalPad, top + verticalPad, xStock - xLeft - 2 * horizontalPad, height - 2 * verticalPad);
            var realCell = SafeCrop(gray, xReal + horizontalPad, top + verticalPad, xRight - xReal - 2 * horizontalPad, height - 2 * verticalPad);
            if (codeCell is null || realCell is null) { codeCell?.Dispose(); realCell?.Dispose(); continue; }

            string code;
            try { code = await ReadCodeAsync(codeCell, cancellationToken).ConfigureAwait(false); }
            finally { codeCell.Dispose(); }
            if (debug) Console.WriteLine($"[debug] {top}-{bottom}: code='{code}'");

            if (TextNormalization.SameUniqueValue(code, InventoryPickupOcrRules.HeaderCode)) { realCell.Dispose(); continue; }
            if (code.Length == 0) { realCell.Dispose(); continue; }

            var (hasInk, value, uncertain) = ReadHandwrittenNumber(realCell);
            if (debug) Console.WriteLine($"[debug] {top}-{bottom}: hasInk={hasInk} value={value} uncertain={uncertain}");
            realCell.Dispose();
            if (!hasInk) continue; // empty cell: "neinventariat", no row at all (subtask 1.2)
            rows.Add(new InventoryPickupScanRow(pageNumber, code, value, uncertain));
        }
        return rows;
    }

    private static Mat ToGrayMat(SKBitmap bitmap)
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
    private static double FindSkewDegrees(Mat grayFullRes)
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

    private static Mat Rotate(Mat source, double angleDegrees, Scalar borderFill)
    {
        using var rotationMatrix = Cv2.GetRotationMatrix2D(new Point2f(source.Cols / 2f, source.Rows / 2f), angleDegrees, 1.0);
        var destination = new Mat();
        Cv2.WarpAffine(source, destination, rotationMatrix, source.Size(), InterpolationFlags.Linear, BorderTypes.Constant, borderFill);
        return destination;
    }

    private static int ToPixel(double points, double scale, int max) => Math.Clamp((int)Math.Round(points * scale), 0, max - 1);

    // A printed-then-scanned page rarely lands pixel-exact on the geometry computed from the PDF's own point
    // coordinates (printers commonly scale content a percent or two to fit their printable area). Rather than
    // trusting the computed positions outright, this finds the table's actual left/right border near where they
    // are expected and re-derives the internal dividers from the same column FRACTIONS InventoryPdfWriter used -
    // those fractions are invariant to a uniform print/scan scale, so this still never redetects the layout from
    // scratch (TODO.md's "fara detectie de layout"), it only recalibrates the one page-wide scale factor.
    private static (int Left, int Stock, int Real, int Right) CalibrateColumns(Mat binary, InventoryTableColumns points, double scale, int sampleTop, int sampleBottom)
    {
        var approxLeft = ToPixel(points.CodeX, scale, binary.Cols);
        var approxRight = ToPixel(points.RightEdge, scale, binary.Cols);
        var searchRadius = Math.Max(20, (int)(binary.Cols * 0.03));
        var left = FindSolidVerticalNear(binary, approxLeft, searchRadius, sampleTop, sampleBottom) ?? approxLeft;
        var right = FindSolidVerticalNear(binary, approxRight, searchRadius, sampleTop, sampleBottom) ?? approxRight;
        var stockFraction = (points.StockX - points.PageMargin) / points.ContentWidth;
        var realFraction = (points.RealX - points.PageMargin) / points.ContentWidth;
        var stock = left + (int)Math.Round(stockFraction * (right - left));
        var real = left + (int)Math.Round(realFraction * (right - left));
        return (left, stock, real, right);
    }

    private static int? FindSolidVerticalNear(Mat binary, int approxX, int radius, int top, int bottom)
    {
        var height = bottom - top;
        if (height <= 0) return null;
        var threshold = height * 0.9;
        var best = -1;
        var bestCount = -1.0;
        var lastColumn = binary.Cols - 1;
        for (var x = Math.Max(0, approxX - radius); x <= Math.Min(lastColumn, approxX + radius); x++)
        {
            using var column = binary.SubMat(top, bottom, x, x + 1);
            var count = Cv2.CountNonZero(column);
            if (count >= threshold && count > bestCount) { bestCount = count; best = x; }
        }
        return best < 0 ? null : best;
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

    // Segments the cell's ink into per-digit blobs and classifies each with the embedded MNIST model.
    // HasInk distinguishes a genuinely empty cell (no row at all - subtask 1.2's "neinventariat") from one that
    // has handwriting but could not be read with confidence (still returned as an uncertain row - subtask 1.2/1.3
    // - never silently dropped just because segmentation or classification struggled).
    private (bool HasInk, int? Value, bool Uncertain) ReadHandwrittenNumber(Mat cell)
    {
        using var binary = new Mat();
        Cv2.Threshold(cell, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        var cellArea = cell.Rows * cell.Cols;
        if (Cv2.CountNonZero(binary) < cellArea * 0.001) return (false, null, false); // no ink at all: not counted yet
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);
        var minArea = cellArea * 0.008;
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
