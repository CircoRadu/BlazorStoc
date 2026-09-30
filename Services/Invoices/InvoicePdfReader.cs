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
public sealed record InvoiceReadResult(InvoiceDocument Document, IReadOnlyList<byte[]> PagePreviews);

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
            previews.Add(Encode(preview));
            if (textPage.Words.Count >= InvoiceAnalysisRules.MinTextWords) { pages.Add(textPage); continue; }
            var ocrPage = await ReadOcrPageAsync(bytes, index, cancellationToken).ConfigureAwait(false);
            // A page that has neither text nor recognisable words keeps whatever text it had (possibly none).
            pages.Add(ocrPage.Words.Count > textPage.Words.Count ? ocrPage : textPage);
        }
        if (pages.All(page => page.Words.Count == 0)) throw new InvoiceAnalysisException(InvoiceAnalysisRules.NoWordsMessage);
        return new InvoiceReadResult(new InvoiceDocument(pages), previews);
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

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ---- OCR ----

    private sealed record OcrRead(List<InvoiceWord> Words, int Width, int Height, double Score);

    private async Task<InvoicePageData> ReadOcrPageAsync(byte[] bytes, int pageIndex, CancellationToken cancellationToken)
    {
        SKBitmap bitmap;
        try
        {
            using var stream = new MemoryStream(bytes);
#pragma warning disable CA1416
            bitmap = Conversion.ToImage(stream, new Index(pageIndex), leaveOpen: true, options: new RenderOptions(Dpi: InvoiceAnalysisRules.OcrDpi, Grayscale: true));
#pragma warning restore CA1416
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvoiceAnalysisException(InvoiceAnalysisRules.UnreadableMessage);
        }
        using (bitmap)
        {
            using var gray = InventoryPickupOcrService.ToGrayMat(bitmap);
            var skew = InventoryPickupOcrService.FindSkewDegrees(gray);
            using var upright = Math.Abs(skew) >= 0.6 ? InventoryPickupOcrService.Rotate(gray, skew, Scalar.White) : gray.Clone();
            var debug = Environment.GetEnvironmentVariable("INVOICE_OCR_DEBUG") == "1";
            var best = await RecognizeAsync(upright, pageIndex + 1, cancellationToken).ConfigureAwait(false);
            if (debug) Console.WriteLine($"[ocr] page {pageIndex + 1} as scanned: {best.Words.Count} words, score {best.Score:F1}, mean confidence {(best.Words.Count == 0 ? 0 : best.Words.Average(word => word.Confidence)):F2}");
            // A page scanned sideways or upside down reads as noise: try the other orientations and keep the one that reads best.
            if (!IsGoodRead(best))
                foreach (var rotation in new[] { RotateFlags.Rotate90Clockwise, RotateFlags.Rotate180, RotateFlags.Rotate90Counterclockwise })
                {
                    using var turned = new Mat();
                    Cv2.Rotate(upright, turned, rotation);
                    var read = await RecognizeAsync(turned, pageIndex + 1, cancellationToken).ConfigureAwait(false);
                    if (debug) Console.WriteLine($"[ocr] page {pageIndex + 1} turned {rotation}: {read.Words.Count} words, score {read.Score:F1}, mean confidence {(read.Words.Count == 0 ? 0 : read.Words.Average(word => word.Confidence)):F2}");
                    if (read.Score > best.Score) best = read;
                }
            var scale = InvoiceAnalysisRules.OcrDpi / 72.0;
            var words = best.Words.Select(word => word with
            {
                X = word.X / scale, Y = word.Y / scale, Width = word.Width / scale, Height = word.Height / scale
            }).ToList();
            return new InvoicePageData(pageIndex + 1, best.Width / scale, best.Height / scale, InvoiceSources.Ocr, words);
        }
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
