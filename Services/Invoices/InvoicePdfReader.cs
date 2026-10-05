using OpenCvSharp;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace BlazorStoc.Services;

public sealed class InvoiceAnalysisException(string message) : Exception(message);

public static class InvoiceAnalysisRules
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;
    public const int MaxPages = 30;
    public const string NotAPdfMessage = "Fișierul trebuie să fie un PDF.";
    public const string TooLargeMessage = "Fișierul depășește dimensiunea maximă acceptată (20 MB).";
    public const string TooManyPagesMessage = "Fișierul depășește numărul maxim de pagini acceptat (30).";
    public const string UnreadableMessage = "Fișierul nu a putut fi citit ca PDF valid.";
    public const string NoWordsMessage = "Nu s-a găsit niciun text în fișier (nici în PDF, nici prin recunoașterea imaginii).";
    // A page with fewer words than this has no usable text layer (a scan, or a picture saved as PDF): it is read with OCR.
    public const int MinTextWords = 8;
    public const int PreviewDpi = 110;
    public const int OcrDpi = 300;
}

// The pages of a read file: the words of each page and a picture of each page (PNG) for the interactive template.
public sealed record InvoiceReadResult(InvoiceDocument Document, IReadOnlyList<byte[]> PagePreviews, byte[]? SourcePdf = null);

public interface IInvoicePdfReader
{
    Task<InvoiceReadResult> ReadAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}

// Reads the words of an invoice PDF with their coordinates, page by page: a page with a text layer is read directly (PdfPig: exact
// text, no recognition errors, coordinates of the upright page whatever its /Rotate), a page without one is rasterised, deskewed,
// turned upright and read with OCR (Tesseract, Romanian + English when the Romanian data is installed).
public sealed partial class InvoicePdfReader : IInvoicePdfReader, IInvoiceNumberRereader
{
    private readonly SemaphoreSlim tesseractGate = new(1, 1);
    private readonly string tessdataPath;
    private readonly string language;

    public InvoicePdfReader(IWebHostEnvironmentTessdataPath tessdata)
    {
        tessdataPath = tessdata.Path;
        language = File.Exists(Path.Combine(tessdataPath, "ron.traineddata")) ? "ron+eng" : "eng";
    }

    public async Task<InvoiceReadResult> ReadAsync(Stream pdfStream, CancellationToken cancellationToken = default)
    {
        using var buffered = new MemoryStream();
        await pdfStream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        if (buffered.Length == 0) throw new InvoiceAnalysisException(InvoiceAnalysisRules.UnreadableMessage);
        if (buffered.Length > InvoiceAnalysisRules.MaxFileSizeBytes) throw new InvoiceAnalysisException(InvoiceAnalysisRules.TooLargeMessage);
        var bytes = buffered.ToArray();
        if (!LooksLikePdf(bytes)) throw new InvoiceAnalysisException(InvoiceAnalysisRules.NotAPdfMessage);

        var textPages = ReadTextPages(bytes);
        if (textPages.Count > InvoiceAnalysisRules.MaxPages) throw new InvoiceAnalysisException(InvoiceAnalysisRules.TooManyPagesMessage);

        var pages = new List<InvoicePageData>();
        var previews = new List<byte[]>();
        for (var index = 0; index < textPages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var textPage = textPages[index];
            using var preview = Render(bytes, index, InvoiceAnalysisRules.PreviewDpi);
            var previewPng = Encode(preview);
            var uncovered = textPage.Words.Count >= InvoiceAnalysisRules.MinTextWords ? UncoveredInk(preview, textPage, InvoiceAnalysisRules.PreviewDpi) : (Share: 1.0, Area: 1.0);
            if (Environment.GetEnvironmentVariable("INVOICE_OCR_DEBUG") == "1") Console.WriteLine($"[ocr] page {index + 1} text layer: {textPage.Words.Count} words, ink outside them {uncovered.Share:P0} of the ink, {uncovered.Area:P2} of the page");
            if (textPage.Words.Count >= InvoiceAnalysisRules.MinTextWords && !IsPictureWithStrayText(uncovered)) { previews.Add(previewPng); pages.Add(textPage); continue; }
            var ocr = await ReadOcrPageAsync(bytes, index, cancellationToken).ConfigureAwait(false);
            // A page that has neither text nor recognisable words keeps whatever text it had (possibly none).
            if (ocr.Page.Words.Count > textPage.Words.Count)
            {
                // The words are in the frame of the straightened scan, so the picture shown under them is the straightened scan too:
                // its lines are horizontal and vertical, and the elements drawn on it sit where the text is.
                previews.Add(Straighten(previewPng, ocr.Skew, ocr.Turn));
                pages.Add(ocr.Page);
            }
            else { previews.Add(previewPng); pages.Add(textPage); }
        }
        if (pages.All(page => page.Words.Count == 0)) throw new InvoiceAnalysisException(InvoiceAnalysisRules.NoWordsMessage);
        return new InvoiceReadResult(new InvoiceDocument(pages), previews, bytes);
    }

    // How much of the ink of a page the words of its text layer do not explain. A page printed from a browser to PDF, or a scan with a line of
    // text stamped over it, has a handful of real words and the rest of the page (the whole invoice) is a picture: its text layer cannot be
    // trusted, however many words it has. Ink is what Otsu calls dark; the boxes of the words (and the drawn rules) are painted out; what
    // remains, as a share of all the ink and as a share of the page, tells whether a picture of text is there.
    internal static (double Share, double Area) UncoveredInk(SKBitmap picture, InvoicePageData page, double dpi)
    {
        using var gray = InventoryPickupOcrService.ToGrayMat(picture);
        using var ink = new Mat();
        Cv2.Threshold(gray, ink, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        var total = Cv2.CountNonZero(ink);
        if (total == 0) return (0, 0);
        var scale = dpi / 72.0;
        var bounds = new OpenCvSharp.Rect(0, 0, ink.Cols, ink.Rows);
        foreach (var word in page.Words)
        {
            var box = new OpenCvSharp.Rect((int)(word.X * scale) - 2, (int)(word.Y * scale) - 2, (int)(word.Width * scale) + 5, (int)(word.Height * scale) + 5).Intersect(bounds);
            if (box.Width > 0 && box.Height > 0) ink[box].SetTo(Scalar.Black);
        }
        foreach (var rule in page.Rules ?? [])
        {
            var box = (rule.Vertical ? new OpenCvSharp.Rect((int)(rule.Position * scale) - 3, (int)(rule.From * scale) - 1, 7, (int)((rule.To - rule.From) * scale) + 3)
                                     : new OpenCvSharp.Rect((int)(rule.From * scale) - 1, (int)(rule.Position * scale) - 3, (int)((rule.To - rule.From) * scale) + 3, 7)).Intersect(bounds);
            if (box.Width > 0 && box.Height > 0) ink[box].SetTo(Scalar.Black);
        }
        var left = Cv2.CountNonZero(ink);
        return (left / (double)total, left / ((double)ink.Cols * ink.Rows));
    }

    // Set from what the files of the corpus measured (the debug line "text layer"): pages with a real text layer leave 0-18% of their ink unexplained
    // (45% for a proforma with large graphics), pages that are a picture with a few words of text on top leave 96-98%.
    internal static bool IsPictureWithStrayText((double Share, double Area) uncovered) => uncovered.Share >= 0.7 && uncovered.Area >= 0.01;

    private static bool LooksLikePdf(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 1024);
        var head = System.Text.Encoding.Latin1.GetString(bytes, 0, length);
        return head.Contains("%PDF-", StringComparison.Ordinal);
    }

    // ---- text layer ----

    internal static List<InvoicePageData> ReadTextPages(byte[] bytes)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            var result = new List<InvoicePageData>();
            foreach (var page in document.GetPages())
            {
                if (result.Count >= InvoiceAnalysisRules.MaxPages + 1) break;
                result.Add(ReadTextPage(page));
            }
            if (result.Count == 0) throw new InvoiceAnalysisException(InvoiceAnalysisRules.UnreadableMessage);
            return result;
        }
        catch (InvoiceAnalysisException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvoiceAnalysisException(InvoiceAnalysisRules.UnreadableMessage);
        }
    }

    private static InvoicePageData ReadTextPage(UglyToad.PdfPig.Content.Page page)
    {
        var order = new Dictionary<UglyToad.PdfPig.Content.Letter, int>(ReferenceEqualityComparer.Instance);
        var letters = page.Letters;
        for (var i = 0; i < letters.Count; i++) order[letters[i]] = i;
        var words = new List<InvoiceWord>();
        foreach (var word in page.GetWords(NearestNeighbourWordExtractor.Instance))
        {
            var text = word.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var box = word.BoundingBox;
            var height = Math.Max(1.0, box.Top - box.Bottom);
            var first = word.Letters.Min(letter => order.GetValueOrDefault(letter, int.MaxValue));
            var bold = word.Letters.Count(letter => letter.FontDetails?.IsBold == true) * 2 > word.Letters.Count;
            words.Add(new InvoiceWord(page.Number, text, box.Left, page.Height - box.Top, Math.Max(0.5, box.Right - box.Left), height, first, bold));
        }
        return new InvoicePageData(page.Number, page.Width, page.Height, InvoiceSources.Text, words.OrderBy(word => word.Order).ToList(), DrawnRules(page));
    }

    // The lines drawn in the page (the borders of a table): vector strokes and thin filled rectangles, in the frame of the words (points, from the top).
    // A page that is turned (/Rotate) has none: the lines are in the unturned frame of the page.
    internal static List<InvoiceRule> DrawnRules(UglyToad.PdfPig.Content.Page page)
    {
        var pieces = new List<InvoiceRule>();
        try
        {
            if (page.Rotation.Value != 0) return [];
            const double Thin = 1.8, Slack = 0.7, MinLength = 6;
            void Add(double x1, double y1, double x2, double y2)
            {
                var top1 = page.Height - y1;
                var top2 = page.Height - y2;
                if (Math.Abs(y1 - y2) <= Slack && Math.Abs(x1 - x2) >= MinLength) pieces.Add(new InvoiceRule(false, (top1 + top2) / 2, Math.Min(x1, x2), Math.Max(x1, x2)));
                else if (Math.Abs(x1 - x2) <= Slack && Math.Abs(y1 - y2) >= MinLength) pieces.Add(new InvoiceRule(true, (x1 + x2) / 2, Math.Min(top1, top2), Math.Max(top1, top2)));
            }
            foreach (var path in page.Paths)
            {
                if (path.IsClipping) continue;
                foreach (var subpath in path)
                {
                    var current = default(UglyToad.PdfPig.Core.PdfPoint);
                    var points = new List<UglyToad.PdfPig.Core.PdfPoint>();
                    foreach (var command in subpath.Commands)
                    {
                        if (command is UglyToad.PdfPig.Core.PdfSubpath.Move move) { current = move.Location; points.Clear(); points.Add(current); }
                        else if (command is UglyToad.PdfPig.Core.PdfSubpath.Line line)
                        {
                            if (path.IsStroked) Add(line.From.X, line.From.Y, line.To.X, line.To.Y);
                            points.Add(line.To);
                        }
                    }
                    // A thin filled rectangle is a line; a filled box of any size with its edges is not (its edges are not lines of the table).
                    if (path.IsFilled && !path.IsStroked)
                    {
                        var box = subpath.GetBoundingRectangle();
                        if (box is { } rectangle)
                        {
                            if (rectangle.Height <= Thin && rectangle.Width >= MinLength) pieces.Add(new InvoiceRule(false, page.Height - (rectangle.Bottom + rectangle.Top) / 2, rectangle.Left, rectangle.Right));
                            else if (rectangle.Width <= Thin && rectangle.Height >= MinLength) pieces.Add(new InvoiceRule(true, (rectangle.Left + rectangle.Right) / 2, page.Height - rectangle.Top, page.Height - rectangle.Bottom));
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return []; }   // the lines are a hint: a page whose drawing cannot be read has none
        return MergeRulePieces(pieces);
    }

    // Pieces of one rule on the same line (a rule drawn in segments, a cell border drawn for each cell) are one rule.
    internal static List<InvoiceRule> MergeRulePieces(IEnumerable<InvoiceRule> pieces)
    {
        var result = new List<InvoiceRule>();
        foreach (var piece in pieces.OrderBy(item => item.Vertical).ThenBy(item => item.Position).ThenBy(item => item.From))
        {
            var index = result.FindIndex(rule => rule.Vertical == piece.Vertical && Math.Abs(rule.Position - piece.Position) <= 1.0 && piece.From <= rule.To + 2 && piece.To >= rule.From - 2);
            if (index < 0) result.Add(piece);
            else result[index] = result[index] with { From = Math.Min(result[index].From, piece.From), To = Math.Max(result[index].To, piece.To) };
        }
        return result;
    }

    // ---- pictures ----

    private static SKBitmap Render(byte[] bytes, int pageIndex, int dpi)
    {
        using var stream = new MemoryStream(bytes);
#pragma warning disable CA1416
        return Conversion.ToImage(stream, new Index(pageIndex), leaveOpen: true, options: new RenderOptions(Dpi: dpi));
#pragma warning restore CA1416
    }

    // The coloured picture turned the way the OCR page was (same skew angle, same quarter turn), white where the rotation uncovers the corners.
    private static byte[] Straighten(byte[] png, double skewDegrees, RotateFlags? turn)
    {
        if (skewDegrees == 0 && turn is null) return png;
        using var picture = Cv2.ImDecode(png, ImreadModes.Color);
        if (picture.Empty()) return png;
        using var straight = skewDegrees != 0 ? InventoryPickupOcrService.Rotate(picture, skewDegrees, Scalar.White) : picture.Clone();
        if (turn is null) return straight.ImEncode(".png");
        using var turned = new Mat();
        Cv2.Rotate(straight, turned, turn.Value);
        return turned.ImEncode(".png");
    }

    private const double MinScanSkewDegrees = 0.15;
    private const double MaxScanSkewDegrees = 15.0;

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ---- OCR ----

    private sealed record OcrRead(List<InvoiceWord> Words, int Width, int Height, double Score);

    // The page read by OCR, with how the scan had to be turned to be upright (skew in degrees, then maybe a quarter/half turn).
    private sealed record OcrPage(InvoicePageData Page, double Skew, RotateFlags? Turn);

    private async Task<OcrPage> ReadOcrPageAsync(byte[] bytes, int pageIndex, CancellationToken cancellationToken)
    {
        // 300 DPI whatever the scan's own resolution: a 600 DPI scan is read as well at 300 (tested: same score, twice as fast), a 150 DPI one gains
        // nothing from more, and the pixel sizes used below (ruled lines, sharpening radius) are tuned for it. INVOICE_OCR_DPI overrides it for experiments.
        var ocrDpi = Environment.GetEnvironmentVariable("INVOICE_OCR_DPI") is { Length: > 0 } forced ? int.Parse(forced) : InvoiceAnalysisRules.OcrDpi;
        SKBitmap bitmap;
        try
        {
            using var stream = new MemoryStream(bytes);
#pragma warning disable CA1416
            bitmap = Conversion.ToImage(stream, new Index(pageIndex), leaveOpen: true, options: new RenderOptions(Dpi: ocrDpi, Grayscale: true));
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvoiceAnalysisException(InvoiceAnalysisRules.UnreadableMessage);
        }
        using (bitmap)
        {
            using var gray = InventoryPickupOcrService.ToGrayMat(bitmap);
            var skew = InventoryPickupOcrService.FindSkewDegrees(gray, MaxScanSkewDegrees);
            using var upright = Math.Abs(skew) >= MinScanSkewDegrees ? InventoryPickupOcrService.Rotate(gray, skew, Scalar.White) : gray.Clone();
            var debug = Environment.GetEnvironmentVariable("INVOICE_OCR_DEBUG") == "1";
            if (Environment.GetEnvironmentVariable("INVOICE_OCR_DUMP_DIR") is { Length: > 0 } dumpDirectory)
                Cv2.ImWrite(Path.Combine(dumpDirectory, $"page{pageIndex + 1}-upright.png"), upright);
            var best = await RecognizeAsync(upright, pageIndex + 1, cancellationToken).ConfigureAwait(false);
            if (debug) Console.WriteLine($"[ocr] page {pageIndex + 1} as scanned: {best.Words.Count} words, score {best.Score:F1}, mean confidence {(best.Words.Count == 0 ? 0 : best.Words.Average(word => word.Confidence)):F2}");
            // The same page prepared for reading: without its ruled lines (a table's borders make Tesseract drop or garble the words in the
            // cells) and, when the ink is faint (pencil, a washed-out scan), with the contrast stretched. A variant is kept only when it
            // reads better than the picture as scanned.
            var source = upright;
            using var withoutLines = RemoveRuledLines(upright);
            using var stretched = InventoryPickupOcrService.InkContrast(upright) < InventoryPickupOcrService.FaintInkContrast ? InventoryPickupOcrService.StretchContrast(upright) : null;
            // A blurry scan: the same pictures with an unsharp mask (the soft letter edges made steeper).
            using var sharpened = Sharpen(upright);
            using var sharpenedWithoutLines = withoutLines is not null ? Sharpen(withoutLines) : null;
            var turnedByRotation = false;
            RotateFlags? turn = null;
            var variants = new List<(string Name, Mat Image)>();
            if (withoutLines is not null) variants.Add(("without ruled lines", withoutLines));
            if (stretched is not null) variants.Add(("contrast stretched", stretched));
            variants.Add(("sharpened", sharpened));
            if (sharpenedWithoutLines is not null) variants.Add(("sharpened, without ruled lines", sharpenedWithoutLines));
            if (withoutLines is not null && stretched is not null)
            {
                var both = RemoveRuledLines(stretched);
                if (both is not null) variants.Add(("stretched, without ruled lines", both));
            }
            foreach (var (name, image) in variants)
            {
                var read = await RecognizeAsync(image, pageIndex + 1, cancellationToken).ConfigureAwait(false);
                if (debug) Console.WriteLine($"[ocr] page {pageIndex + 1} {name}: {read.Words.Count} words, score {read.Score:F1}");
                if (read.Score > best.Score * 1.02) { best = read; source = image; }
            }
            // A page scanned sideways or upside down reads as noise: try the other orientations and keep the one that reads best.
            if (!IsGoodRead(best))
                foreach (var rotation in new[] { RotateFlags.Rotate90Clockwise, RotateFlags.Rotate180, RotateFlags.Rotate90Counterclockwise })
                {
                    using var turned = new Mat();
                    Cv2.Rotate(source, turned, rotation);
                    var read = await RecognizeAsync(turned, pageIndex + 1, cancellationToken).ConfigureAwait(false);
                    if (debug) Console.WriteLine($"[ocr] page {pageIndex + 1} turned {rotation}: {read.Words.Count} words, score {read.Score:F1}, mean confidence {(read.Words.Count == 0 ? 0 : read.Words.Average(word => word.Confidence)):F2}");
                    if (read.Score > best.Score) { best = read; turnedByRotation = true; turn = rotation; }
                }
            var scale = ocrDpi / 72.0;
            var words = best.Words.Select(word => word with
            {
                X = word.X / scale, Y = word.Y / scale, Width = word.Width / scale, Height = word.Height / scale
            }).ToList();
            // The ruled lines (in the frame of the words) are a hint for the table's columns; a page turned by a quarter has none.
            var rules = turnedByRotation ? [] : RulesOf(upright, scale, best.Words);
            if (debug) { Console.WriteLine($"[ocr] page {pageIndex + 1} rules: {rules.Count(rule => rule.Vertical)} vertical, {rules.Count(rule => !rule.Vertical)} horizontal"); foreach (var rule in rules.OrderBy(rule => rule.Vertical).ThenBy(rule => rule.Position)) Console.WriteLine($"[ocr]   {(rule.Vertical ? "V" : "H")} {rule.Position:F1} {rule.From:F0}-{rule.To:F0}"); }
            var appliedSkew = Math.Abs(skew) >= MinScanSkewDegrees ? skew : 0;
            var turnDegrees = turn switch { RotateFlags.Rotate90Clockwise => 90, RotateFlags.Rotate180 => 180, RotateFlags.Rotate90Counterclockwise => 270, _ => 0 };
            return new OcrPage(new InvoicePageData(pageIndex + 1, best.Width / scale, best.Height / scale, InvoiceSources.Ocr, words, rules, appliedSkew, turnDegrees), appliedSkew, turn);
        }
    }

    // Unsharp mask: the picture plus the difference between it and a blurred copy, which steepens the soft edges of the letters of a blurry
    // scan (the OCR separates touching strokes better). Radius is in pixels of the 300 DPI picture; amount 1 adds the full difference.
    internal static Mat Sharpen(Mat gray, double sigma = 1.6, double amount = 1.0)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(0, 0), sigma);
        var sharpened = new Mat();
        Cv2.AddWeighted(gray, 1.0 + amount, blurred, -amount, 0, sharpened);
        return sharpened;
    }

    // The picture without the long horizontal and vertical rules (table borders, underlines), or null when it has none worth removing.
    // Rules are found as long thin runs of dark pixels and painted white, slightly widened to take the anti-aliased edge too; the
    // letters that touch a rule lose a pixel row at most.
    internal static Mat? RemoveRuledLines(Mat gray) => FindRuledLines(gray, out _, out var cleaned) ? cleaned : null;

    // The rules of the picture as boxes in pixels (vertical and horizontal), and the picture with them painted white.
    private static bool FindRuledLines(Mat gray, out List<(OpenCvSharp.Rect Box, bool Vertical)> rules, out Mat? cleaned)
    {
        rules = [];
        cleaned = null;
        using var ink = new Mat();
        Cv2.Threshold(gray, ink, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var horizontalKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(60, gray.Cols / 16), 1));
        using var verticalKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, Math.Max(60, gray.Rows / 35)));
        using var horizontal = new Mat();
        using var vertical = new Mat();
        // A dotted or scratchy rule is first closed up (gaps of a few pixels bridged along its direction only), then kept when long enough.
        using var horizontalBridge = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(6, 1));
        using var verticalBridge = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 6));
        using var horizontalClosed = new Mat();
        using var verticalClosed = new Mat();
        Cv2.MorphologyEx(ink, horizontalClosed, MorphTypes.Close, horizontalBridge);
        Cv2.MorphologyEx(ink, verticalClosed, MorphTypes.Close, verticalBridge);
        Cv2.MorphologyEx(horizontalClosed, horizontal, MorphTypes.Open, horizontalKernel);
        Cv2.MorphologyEx(verticalClosed, vertical, MorphTypes.Open, verticalKernel);
        using var lines = new Mat();
        Cv2.BitwiseOr(horizontal, vertical, lines);
        if (Cv2.CountNonZero(lines) < 0.0004 * gray.Rows * gray.Cols) return false;
        foreach (var (mask, isVertical) in new[] { (horizontal, false), (vertical, true) })
        {
            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            rules.AddRange(contours.Select(contour => (Cv2.BoundingRect(contour), isVertical)));
        }
        using var widen = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.Dilate(lines, lines, widen, iterations: 1);
        cleaned = gray.Clone();
        cleaned.SetTo(Scalar.White, lines);
        return true;
    }

    // The rules of a picture in page points: pieces of one rule (a rule broken by a stamp or a fold) on the same line are joined.
    internal static List<InvoiceRule> RulesOf(Mat gray, double scale, IEnumerable<InvoiceWord>? words = null)
    {
        var boxes = new List<(OpenCvSharp.Rect Box, bool Vertical)>();
        if (FindRuledLines(gray, out var solid, out var cleaned)) boxes.AddRange(solid);
        cleaned?.Dispose();
        // A grid drawn with faint or dotted lines does not survive the threshold of the solid ones: it is looked for again on what is not text.
        if (words is not null) boxes.AddRange(FindFaintLines(gray, words));
        if (boxes.Count == 0) return [];
        var result = new List<InvoiceRule>();
        foreach (var vertical in new[] { true, false })
        {
            var pieces = boxes.Where(item => item.Vertical == vertical)
                .Select(item => vertical ? (Position: (item.Box.X + item.Box.Width / 2.0) / scale, From: item.Box.Y / scale, To: item.Box.Bottom / scale)
                                         : (Position: (item.Box.Y + item.Box.Height / 2.0) / scale, From: item.Box.X / scale, To: item.Box.Right / scale))
                .OrderBy(item => item.Position).ThenBy(item => item.From).ToList();
            foreach (var piece in pieces)
            {
                var index = result.FindIndex(rule => rule.Vertical == vertical && Math.Abs(rule.Position - piece.Position) <= 1.5 && piece.From <= rule.To + 12 && piece.To >= rule.From - 12);
                if (index < 0) result.Add(new InvoiceRule(vertical, piece.Position, piece.From, piece.To));
                else result[index] = result[index] with { From = Math.Min(result[index].From, piece.From), To = Math.Max(result[index].To, piece.To) };
            }
        }
        return RemoveBarcodeLikeRules(JoinTilted(result));
    }

    // A faint or dotted rule is found in pieces, and a scan that is a little crooked (a fraction of a degree is enough) makes the pieces of one rule drift
    // sideways as they go down the page: the pieces that lie on one slightly tilted line, even with gaps between them, are one rule. A piece joins the
    // line whose course (fitted through its pieces, or a tilt of up to about a degree when it has only one) passes through it.
    internal static List<InvoiceRule> JoinTilted(List<InvoiceRule> rules)
    {
        const double MaxGap = 140, MaxTilt = 0.02;
        var result = new List<InvoiceRule>();
        foreach (var vertical in new[] { true, false })
        {
            var groups = new List<List<InvoiceRule>>();
            foreach (var piece in rules.Where(rule => rule.Vertical == vertical).OrderBy(rule => rule.From))
            {
                var mid = (piece.From + piece.To) / 2;
                List<InvoiceRule>? target = null;
                var best = double.MaxValue;
                foreach (var group in groups)
                {
                    var gap = piece.From - group.Max(item => item.To);
                    if (gap < -2 || gap > MaxGap) continue;
                    var distance = Math.Abs(piece.Position - PositionAt(group, mid, MaxTilt));
                    var allowed = group.Count == 1 ? 1.5 + MaxTilt * Math.Abs(mid - (group[0].From + group[0].To) / 2) : 1.5;
                    if (distance <= allowed && distance < best) { best = distance; target = group; }
                }
                if (target is null) groups.Add([piece]); else target.Add(piece);
            }
            foreach (var group in groups)
            {
                var from = group.Min(item => item.From);
                result.Add(new InvoiceRule(vertical, group.Count == 1 ? group[0].Position : PositionAt(group, from, MaxTilt), from, group.Max(item => item.To)));
            }
        }
        return result;
    }

    // Where the line fitted through the pieces of a group is at a point along it (the fit is the least squares one of the position on the middle of each
    // piece, weighted by length; its tilt never exceeds maxTilt).
    private static double PositionAt(List<InvoiceRule> group, double along, double maxTilt)
    {
        var weights = group.Select(item => Math.Max(1, item.To - item.From)).ToList();
        var total = weights.Sum();
        var meanAlong = group.Select((item, index) => weights[index] * (item.From + item.To) / 2).Sum() / total;
        var meanPosition = group.Select((item, index) => weights[index] * item.Position).Sum() / total;
        var spread = group.Select((item, index) => weights[index] * Math.Pow((item.From + item.To) / 2 - meanAlong, 2)).Sum();
        var slope = spread < 1 ? 0 : Math.Clamp(group.Select((item, index) => weights[index] * ((item.From + item.To) / 2 - meanAlong) * (item.Position - meanPosition)).Sum() / spread, -maxTilt, maxTilt);
        return meanPosition + slope * (along - meanAlong);
    }

    // A barcode is a row of thin bars of the same height: five or more vertical rules within a few points of each other, all starting and ending alike, are
    // bars, not the borders of a table.
    internal static List<InvoiceRule> RemoveBarcodeLikeRules(List<InvoiceRule> rules)
    {
        var bars = new HashSet<InvoiceRule>();
        foreach (var rule in rules.Where(item => item.Vertical))
        {
            var alike = rules.Where(item => item.Vertical && Math.Abs(item.Position - rule.Position) <= 14 && Math.Abs(item.From - rule.From) <= 3 && Math.Abs(item.To - rule.To) <= 3).ToList();
            if (alike.Count >= 5) foreach (var item in alike) bars.Add(item);
        }
        return bars.Count == 0 ? rules : [.. rules.Where(rule => !bars.Contains(rule))];
    }

    // Faint and dotted rules: the picture is thresholded against its local background (a light grey dot counts), the words are painted out so that the
    // edges of the text cannot make a rule, gaps along the rule are bridged, and a run is kept when it is long and thin and a fair share of it is ink
    // (a dotted rule has dots along most of its length; scattered speckle of the paper has not).
    internal static List<(OpenCvSharp.Rect Box, bool Vertical)> FindFaintLines(Mat gray, IEnumerable<InvoiceWord> words)
    {
        var result = new List<(OpenCvSharp.Rect Box, bool Vertical)>();
        using var ink = new Mat();
        Cv2.AdaptiveThreshold(gray, ink, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 31, 14);
        foreach (var word in words)
        {
            var box = new OpenCvSharp.Rect((int)Math.Max(0, word.X - 3), (int)Math.Max(0, word.Y - 3), (int)word.Width + 6, (int)word.Height + 6);
            box = box.Intersect(new OpenCvSharp.Rect(0, 0, ink.Cols, ink.Rows));
            if (box.Width > 0 && box.Height > 0) ink[box].SetTo(Scalar.Black);
        }
        foreach (var vertical in new[] { true, false })
        {
            var length = vertical ? Math.Max(90, gray.Rows / 28) : Math.Max(120, gray.Cols / 14);
            using var bridge = Cv2.GetStructuringElement(MorphShapes.Rect, vertical ? new Size(1, 30) : new Size(30, 1));
            using var keep = Cv2.GetStructuringElement(MorphShapes.Rect, vertical ? new Size(1, length) : new Size(length, 1));
            using var closed = new Mat();
            using var opened = new Mat();
            Cv2.MorphologyEx(ink, closed, MorphTypes.Close, bridge);
            Cv2.MorphologyEx(closed, opened, MorphTypes.Open, keep);
            Cv2.FindContours(opened, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                var box = Cv2.BoundingRect(contour);
                if ((vertical ? box.Width : box.Height) > 6) continue;
                var strip = vertical ? new OpenCvSharp.Rect(Math.Max(0, box.X - 1), box.Y, box.Width + 2, box.Height) : new OpenCvSharp.Rect(box.X, Math.Max(0, box.Y - 1), box.Width, box.Height + 2);
                strip = strip.Intersect(new OpenCvSharp.Rect(0, 0, ink.Cols, ink.Rows));
                if (strip.Width <= 0 || strip.Height <= 0) continue;
                using var part = new Mat(ink, strip);
                // The share of the run that has ink in it, along its length (a dotted line has some in nearly every stretch of it).
                using var projection = new Mat();
                Cv2.Reduce(part, projection, vertical ? ReduceDimension.Column : ReduceDimension.Row, ReduceTypes.Max, MatType.CV_8U);
                var inked = Cv2.CountNonZero(projection);
                if (inked < 0.25 * (vertical ? box.Height : box.Width)) continue;
                result.Add((box, vertical));
            }
        }
        return result;
    }

    // A read is upright when enough words are there, confident, and mostly laid out as horizontal text (a sideways page comes back as tall boxes).
    private static bool IsGoodRead(OcrRead read)
    {
        var sized = read.Words.Where(word => word.Text.Count(char.IsLetterOrDigit) >= 3).ToList();
        return read.Words.Count >= 15 && read.Words.Average(word => word.Confidence) >= 0.6 && sized.Count > 0 && sized.Count(word => word.Width >= 1.1 * word.Height) >= 0.85 * sized.Count;
    }

    private async Task<OcrRead> RecognizeAsync(Mat image, int pageNumber, CancellationToken cancellationToken)
    {
        var png = image.ImEncode(".png");
        var words = new List<InvoiceWord>();
        await tesseractGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var engine = new TesseractEngine(tessdataPath, language, EngineMode.LstmOnly);
            using var pix = Pix.LoadFromMemory(png);
            using var page = engine.Process(pix, PageSegMode.Auto);
            using var iterator = page.GetIterator();
            iterator.Begin();
            var order = 0;
            do
            {
                if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rectangle)) continue;
                var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var confidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100.0;
                words.Add(new InvoiceWord(pageNumber, text, rectangle.X1, rectangle.Y1, Math.Max(1, rectangle.Width), Math.Max(1, rectangle.Height), order++, false, confidence));
            } while (iterator.Next(PageIteratorLevel.Word));
        }
        finally { tesseractGate.Release(); }
        // Reliable words (long enough, confident, wider than tall) weigh in the orientation choice: Tesseract reads a sideways page too, but
        // reports its words as tall boxes, and noise from a wrong orientation scores low.
        var score = words.Where(word => word.Text.Count(char.IsLetterOrDigit) >= 3 && word.Width >= 1.1 * word.Height).Sum(word => word.Confidence);
        return new OcrRead(words, image.Cols, image.Rows, score);
    }
}
