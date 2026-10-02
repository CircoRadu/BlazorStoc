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
public sealed class InvoicePdfReader : IInvoicePdfReader
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
            if (textPage.Words.Count >= InvoiceAnalysisRules.MinTextWords) { previews.Add(previewPng); pages.Add(textPage); continue; }
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
        return new InvoicePageData(page.Number, page.Width, page.Height, InvoiceSources.Text, words.OrderBy(word => word.Order).ToList());
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
            var rules = turnedByRotation ? [] : RulesOf(upright, scale);
            if (debug) { Console.WriteLine($"[ocr] page {pageIndex + 1} rules: {rules.Count(rule => rule.Vertical)} vertical, {rules.Count(rule => !rule.Vertical)} horizontal"); foreach (var rule in rules.OrderBy(rule => rule.Vertical).ThenBy(rule => rule.Position)) Console.WriteLine($"[ocr]   {(rule.Vertical ? "V" : "H")} {rule.Position:F1} {rule.From:F0}-{rule.To:F0}"); }
            return new OcrPage(new InvoicePageData(pageIndex + 1, best.Width / scale, best.Height / scale, InvoiceSources.Ocr, words, rules), Math.Abs(skew) >= MinScanSkewDegrees ? skew : 0, turn);
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
    internal static List<InvoiceRule> RulesOf(Mat gray, double scale)
    {
        if (!FindRuledLines(gray, out var boxes, out var cleaned)) return [];
        cleaned?.Dispose();
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
