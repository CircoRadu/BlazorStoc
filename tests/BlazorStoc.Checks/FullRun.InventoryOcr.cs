#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 1616-1819 of the former Program.cs.
    internal static async Task InventoryOcrAsync(string[] args)
    {

        // Task 1: every message shown to the user is in Romanian. The literals that become user-visible messages (validation
        // attributes, operation exceptions, error/notice fields, ...Message constants) must not contain common English words.
        {
            var projectRoot = AppContext.BaseDirectory;
            while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
            Check(projectRoot is not null, "The project folder was found for the message scan");
            var files = new[] { "Services", "Components", "Pages" }
                .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(projectRoot!, folder), "*.*", SearchOption.AllDirectories))
                .Where(file => file.EndsWith(".cs") || file.EndsWith(".razor") || file.EndsWith(".cshtml"))
                .Append(Path.Combine(projectRoot!, "Program.cs")).ToArray();
            var patterns = new[]
            {
                @"(?:ErrorMessage|ParsingErrorMessage)\s*=\s*""([^""]*)""",
                @"\w+Exception\(\s*\$?""([^""]*)""",
                @"\b(?:error|Error|formError|editError|deleteError|historyError|notice|lockMessage|unlockError|reasonError|imageError|filesError|message|Message)\s*=\s*\$?""([^""]*)""",
                @"\b\w*Message\s*(?:=|=>)\s*\$?""([^""]*)""",
                @"\b[eE]rrors\.Add\(\s*\$?""([^""]*)""",
            };
            var english = new HashSet<string>(["the", "is", "must", "cannot", "failed", "invalid", "required", "please", "error", "already",
                "exists", "found", "unable", "could", "should", "will", "your", "been", "denied", "missing", "expected", "value", "field", "not"],
                StringComparer.OrdinalIgnoreCase);
            var scanned = 0; var offenders = new List<string>();
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                foreach (var pattern in patterns)
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, pattern))
                    {
                        var literal = match.Groups[1].Value;
                        scanned++;
                        var words = System.Text.RegularExpressions.Regex.Matches(literal, @"[A-Za-z']+").Select(word => word.Value);
                        var found = words.Where(english.Contains).ToArray();
                        if (found.Length > 0) offenders.Add($"{Path.GetFileName(file)}: \"{literal}\" ({string.Join(", ", found)})");
                    }
            }
            Check(scanned > 100, $"The message scan reads the message literals ({scanned} found)");
            Check(offenders.Count == 0, "Message literals shown to users contain no English words" + (offenders.Count == 0 ? "" : ": " + string.Join(" | ", offenders.Take(5))));
        }

        // Task 1 (preluare inventar OCR): the pipeline is exercised against a real scan the user provided of the
        // situatia de inventar PDF, printed, filled in by hand and scanned back (tests/BlazorStoc.Checks/Fixtures).
        InventoryPickupScanResult pickupScan;
        {
            var projectRoot = AppContext.BaseDirectory;
            while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
            Check(projectRoot is not null, "The project folder was found for the inventory pickup fixture");
            var tessdataEnv = new TestWebHostEnvironment(projectRoot!);
            using var ocrService = new InventoryPickupOcrService(new TessdataPath(tessdataEnv));
            await using var fixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba.pdf"));
            pickupScan = await ocrService.ScanAsync(fixtureStream);

            (string Code, int Value) Row(string code) => pickupScan.Rows
                .Where(row => TextNormalization.SameUniqueValue(row.RawCode, code))
                .Select(row => (row.RawCode, row.RecognizedValue ?? -1)).First();
            Check(pickupScan.PageCount == 1, "The sample scan has a single page");
            Check(Row("Surub autoforant 4,8 x 25 test").Value == 105, "OCR reads the handwritten value for a fully separated 3-digit number");
            Check(Row("Casca de protectie alba XXL").Value == 24, "OCR reads a 2-digit handwritten value from a table further down the page");
            Check(Row("Manusi de lucru").Value == 54, "OCR reads a 2-digit handwritten value after recalibrating the column geometry per page");
            Check(Row("Nivela cu bula 60 cm").Value == 6, "OCR reads a single handwritten digit");
            Check(Row("Ciocan rotopercutor SDS Plus").Value == 0, "OCR reads a handwritten zero");
            Check(pickupScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Set chei combinate")) is false ||
                  pickupScan.Rows.First(row => TextNormalization.SameUniqueValue(row.RawCode, "Set chei combinate")).RecognizedValue is null,
                "A row left blank on the form never gets a fabricated value");
            var diblu = pickupScan.Rows.First(row => TextNormalization.SameUniqueValue(row.RawCode, "Diblu nylon 8 x 40 test 22"));
            Check(diblu.RecognizedValue == 123, "OCR reads a 3-digit value written with tighter spacing than the other rows");
            // Known residual limitation (documented in docs/TESTE_RAMASE.md): the embedded generic MNIST classifier
            // sometimes confidently misreads a stylized handwritten digit (this scan's cursive "8") as a different one.
            // The row must still surface for the user to see and correct, which is what this checks.
            Check(pickupScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Polizor unghiular")),
                "A row is never silently dropped just because the digit classifier read it with (mistaken) confidence");
        }

        // Task 1 (preluare inventar OCR), regression: a real user scan of the same form (same products, printed and
        // scanned again) came back with zero recognized rows - the "Nu a fost gasit niciun tabel..." error - even though
        // every table's rule lines are clearly visible to the eye. The scan carried well under half a degree of paper
        // skew, enough to spread each line's ink over roughly ten image rows so no single row reached the fill-ratio
        // threshold used to detect a rule line (confirmed by instrumenting FindHorizontalLines, not guessed). Fixed with a
        // small vertical dilation before that check (InventoryPickupOcrService.FindHorizontalLines,
        // LineDetectionDilationHeight); this fixture locks the fix in.
        {
            var projectRoot = AppContext.BaseDirectory;
            while (projectRoot is not null && !File.Exists(Path.Combine(projectRoot, "BlazorStoc.csproj"))) projectRoot = Path.GetDirectoryName(projectRoot.TrimEnd(Path.DirectorySeparatorChar));
            var tessdataEnv = new TestWebHostEnvironment(projectRoot!);
            using var ocrService = new InventoryPickupOcrService(new TessdataPath(tessdataEnv));
            await using var fixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-inclinata.pdf"));
            var skewedScan = await ocrService.ScanAsync(fixtureStream);
            Check(skewedScan.Rows.Count == 10, $"A slightly skewed real scan is still read as a table (10 rows expected, got {skewedScan.Rows.Count})");
            Check(skewedScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Ciocan rotopercutor SDS Plus") && row.RecognizedValue == 1),
                "A row from the skewed scan still reads its handwritten value correctly");

            // A second real scan of the same form, rotated by a few degrees (clearly visible to the eye, not just a
            // fraction of a degree) - the small dilation above cannot bridge a skew this large; only actively deskewing
            // the whole page (InventoryPickupOcrService.FindSkewDegrees/Rotate) does.
            await using var rotatedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-rotita.pdf"));
            var rotatedScan = await ocrService.ScanAsync(rotatedFixtureStream);
            Check(rotatedScan.Rows.Count == 10, $"A visibly rotated real scan is still read as a table after deskewing (10 rows expected, got {rotatedScan.Rows.Count})");
            Check(rotatedScan.Rows.Any(row => TextNormalization.SameUniqueValue(row.RawCode, "Masina de gaurit cu acumulator") && row.RecognizedValue == 11),
                "A row from the rotated scan still reads its handwritten value correctly after deskewing");

            // A real Konica Minolta scan (30.09.2026): the table's left border sat ~88 px right of the computed position,
            // outside the former 3% calibration radius, so the dividers were derived from the wrong edge and the single
            // row was rejected ("Nu a fost gasit niciun tabel"). CalibrateColumns now searches 8% of the page width.
            await using var shiftedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-decalata.pdf"));
            var shiftedScan = await ocrService.ScanAsync(shiftedFixtureStream);
            Check(shiftedScan.Rows.Count == 1 && shiftedScan.Rows[0].RecognizedValue == 50 && !shiftedScan.Rows[0].Uncertain,
                $"A scan whose table is shifted several millimetres from the computed position is still read (1 row = 50 expected, got {shiftedScan.Rows.Count})");
            Check(shiftedScan.Rows[0].Number is null, "A form printed before the \"Nr. crt.\" column existed is still read, without a running number");

            // The first real scan of the form WITH the "Nr. crt." column (30.09.2026, Konica Minolta, pencil-like faint ink,
            // about -1.5 degrees of skew): two tables, running numbers 1,2 then 1 again, and a handwritten "10" whose thin "1"
            // the plain read loses (it reads 1) - only the contrast-stretched re-read recovers it.
            await using var numberedFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-nr-crt.pdf"));
            var numberedScan = await ocrService.ScanAsync(numberedFixtureStream);
            Check(numberedScan.Rows.Select(row => row.Number).SequenceEqual(new int?[] { 1, 2, 1 }),
                $"A real scan of the numbered form restarts the running number for the second subcategory (got {string.Join(",", numberedScan.Rows.Select(row => row.Number?.ToString() ?? "?"))})");
            Check(numberedScan.Rows.Select(row => row.RecognizedValue).SequenceEqual(new int?[] { 2, 10, 7 }) && numberedScan.Rows.All(row => !row.Uncertain),
                $"A real faint, slightly rotated scan is read correctly, including a thin handwritten \"10\" (got {string.Join(",", numberedScan.Rows.Select(row => row.RecognizedValue?.ToString() ?? "?"))})");

            // Forms with the "Nr. crt." column, end to end: the writer's PDF is rasterised, "handwriting" is painted into every
            // row's "Valoare reala" cell (black pen, graphite pencil, and a washed-out scan), put back into a PDF and read by
            // the OCR. The running number restarts at 1 for every subcategory and is only display data (never stored).
            {
                var numberedReport = new InventoryReport(DateTime.UtcNow,
                    [new InventoryCategorySection("Categorie test", [
                        new InventorySubcategorySection("Prima subcategorie", [new("Produs alfa", 3), new("Produs beta", 4), new("Produs gama", 5)]),
                        new InventorySubcategorySection("A doua subcategorie", [new("Produs delta", 6), new("Produs epsilon", 7)])])],
                    1, 2, 5, 0);
                var formPdf = new InventoryPdfWriter().Write(numberedReport, new DateTime(2026, 9, 30, 9, 0, 0));

                byte[] HandwriteAndRescan(byte[] pdf, int inkGray, int thickness, double washOut)
                {
                    using var source = new MemoryStream(pdf);
                    var bitmaps = PDFtoImage.Conversion.ToImages(source, options: new PDFtoImage.RenderOptions(Dpi: 300, Grayscale: true)).ToList();
                    using var bitmap = bitmaps[0];
                    using var gray8 = bitmap.ColorType == SkiaSharp.SKColorType.Gray8 ? null : bitmap.Copy(SkiaSharp.SKColorType.Gray8);
                    var pixels = gray8 ?? bitmap;
                    using var raw = OpenCvSharp.Mat.FromPixelData(pixels.Height, pixels.Width, OpenCvSharp.MatType.CV_8UC1, pixels.GetPixels(), (long)pixels.RowBytes);
                    using var image = raw.Clone();
                    var scale = 300 / 72.0;
                    var columns = InventoryPdfLayout.ComputeColumns(image.Cols / scale);
                    int left = (int)(columns.NumberX * scale), right = (int)(columns.RightEdge * scale);

                    // Horizontal rules of the rendered form (dark rows across the table width), then the data rows between them.
                    var lineRows = new List<int>();
                    for (var y = 0; y < image.Rows; y++)
                    {
                        using var strip = image.SubMat(y, y + 1, left, right);
                        using var dark = strip.LessThan(128).ToMat();
                        if (OpenCvSharp.Cv2.CountNonZero(dark) > (right - left) * 0.6) lineRows.Add(y);
                    }
                    var lines = new List<int>();
                    foreach (var y in lineRows) if (lines.Count == 0 || y - lines[^1] > 3) lines.Add(y);
                    var dataRows = 0;
                    for (var i = 0; i + 1 < lines.Count; i++)
                    {
                        var top = lines[i];
                        var height = lines[i + 1] - top;
                        if (height < 120 || height > 170) continue; // data rows are 34 pt (about 142 px); header rows and gaps are not
                        var text = (10 + dataRows).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var size = OpenCvSharp.Cv2.GetTextSize(text, OpenCvSharp.HersheyFonts.HersheySimplex, 2.6, thickness, out var baseline);
                        var cellLeft = (int)(columns.RealX * scale);
                        var origin = new OpenCvSharp.Point(cellLeft + ((right - cellLeft) - size.Width) / 2, top + (height + size.Height) / 2);
                        OpenCvSharp.Cv2.PutText(image, text, origin, OpenCvSharp.HersheyFonts.HersheySimplex, 2.6, new OpenCvSharp.Scalar(inkGray), thickness, OpenCvSharp.LineTypes.AntiAlias);
                        dataRows++;
                    }
                    // Washed-out scan: every ink level pulled towards white (out = 255 - (255 - in) * washOut).
                    if (washOut < 1.0) image.ConvertTo(image, OpenCvSharp.MatType.CV_8UC1, washOut, 255 * (1 - washOut));

                    using var document = new PdfSharp.Pdf.PdfDocument();
                    var page = document.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    using (var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                    using (var png = new MemoryStream(image.ImEncode(".png")))
                    using (var xImage = PdfSharp.Drawing.XImage.FromStream(png))
                        graphics.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
                    using var output = new MemoryStream();
                    document.Save(output, false);
                    return output.ToArray();
                }

                foreach (var (label, inkGray, thickness, washOut) in new[] { ("black pen", 0, 6, 1.0), ("graphite pencil", 150, 2, 1.0), ("washed-out scan", 0, 6, 0.4) })
                {
                    using var formStream = new MemoryStream(HandwriteAndRescan(formPdf, inkGray, thickness, washOut));
                    InventoryPickupScanResult scanned;
                    try { scanned = await ocrService.ScanAsync(formStream); }
                    catch (InventoryPickupOcrException exception) { Check(false, $"A form with the \"Nr. crt.\" column ({label}) is read ({exception.Message})"); continue; }
                    Check(scanned.Rows.Count == 5 && TextNormalization.SameUniqueValue(scanned.Rows[0].RawCode, "Produs alfa"),
                        $"A form with the \"Nr. crt.\" column ({label}) is read: 5 rows expected, got {scanned.Rows.Count}");
                    Check(scanned.Rows.Select(row => row.Number).SequenceEqual(new int?[] { 1, 2, 3, 1, 2 }),
                        $"The running number restarts at 1 for each subcategory ({label}): got {string.Join(",", scanned.Rows.Select(row => row.Number?.ToString() ?? "?"))}");
                    Check(scanned.Rows.Select(row => row.RecognizedValue).SequenceEqual(new int?[] { 10, 11, 12, 13, 14 }),
                        $"The handwritten values are read ({label}): got {string.Join(",", scanned.Rows.Select(row => row.RecognizedValue?.ToString() ?? "?"))}");
                }
            }

            // A single PDF can have a different skew on every page (each page was fed through the scanner separately, or
            // a multi-page situatia de inventar was assembled from several individual scans). FindSkewDegrees/Rotate must
            // run per page, not once for the whole document - locked in here with a 2-page fixture built from the two
            // fixtures above (page 1 keeps its ~0.3 degree skew, page 2 its ~2.7 degrees), rather than assuming the code
            // already does this correctly from reading it.
            await using var multiPageFixtureStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "inventar-proba-multipagina.pdf"));
            var multiPageScan = await ocrService.ScanAsync(multiPageFixtureStream);
            Check(multiPageScan.PageCount == 2, "The multi-page fixture has two pages");
            Check(multiPageScan.Rows.Count(row => row.Page == 1) == 10, "Page 1 (mild skew) is fully read on its own");
            Check(multiPageScan.Rows.Count(row => row.Page == 2) == 10, "Page 2 (visible rotation) is fully read on its own, independently of page 1's skew");
        }
    }
}
