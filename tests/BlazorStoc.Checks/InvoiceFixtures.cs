using System.Globalization;
using BlazorStoc.Services;
using OpenCvSharp;
using PDFtoImage;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace BlazorStoc.Checks;

// What a generated test invoice contains, so that the checks can compare what the engine reads with what was written.
internal sealed record FixtureRow(string Name, decimal Quantity, decimal Price, decimal Value, string Unit);

internal sealed record InvoiceSpec(
    string Style = "ro-lines",          // ro-lines: Romanian labels, ruled table, 1.234,56 | en-plain: English labels, no rules, 1,234.56
    int Rows = 5,
    int ExtraAddressLines = 0,           // lines above the table that vary from one invoice to the next
    int RowsFirstPage = 100,             // rows on the first page before the table continues on the next (no repeated header)
    bool Rotated = false,                // landscape content on a portrait page turned with /Rotate 90, as the ANAF e-Factura PDFs are
    int WrapNamesAt = 0,                 // names longer than this many characters are written on two lines (0 = never)
    bool RepeatHeader = false,           // the table header is written again at the top of the next page
    bool ColumnNumbers = false,          // a row of column numbers (0 1 2 3 ...) under the header, as many Romanian invoices print
    bool CenterRows = false,             // the cells of a row are vertically centred on its lines (the number sits in the middle of a wrapped name)
    string SupplierName = "Delta Instalatii SRL",
    string SupplierCui = "RO12345678",
    string Number = "DIS 1042",
    DateOnly? Date = null)
{
    public DateOnly IssueDate => Date ?? new DateOnly(2026, 10, 5);
    public bool Romanian => Style == "ro-lines";
}

internal sealed record GeneratedInvoice(byte[] Pdf, IReadOnlyList<FixtureRow> Rows, decimal TotalNet, decimal TotalVat, decimal Total);

// Writes invoices as PDF with a text layer (PdfSharp), in a few layouts that differ in everything the engine must not depend on: labels,
// language, number format, rules, number of address lines, pages, rotation. The scan fixtures rasterise one of them (see Scan).
internal static class InvoiceFixtures
{
    public static readonly string[] Units = ["buc", "m", "set", "ore"];

    public static List<FixtureRow> MakeRows(int count, int seed = 1)
    {
        var random = new Random(seed);
        var rows = new List<FixtureRow>();
        for (var i = 1; i <= count; i++)
        {
            var quantity = random.Next(1, 40);
            var price = Math.Round((decimal)(random.Next(300, 250000) / 100.0), 2);
            rows.Add(new FixtureRow($"Articol {i} {Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]} tip {random.Next(10, 99)}", quantity, price, quantity * price, Units[random.Next(Units.Length)]));
        }
        return rows;
    }

    private static readonly string[] Words = ["camera", "cablu", "switch", "sursa", "suport", "conector", "router", "senzor", "modul", "releu"];

    public static string Money(decimal value, bool romanian) =>
        value.ToString("#,##0.00", romanian ? new CultureInfo("ro-RO") : CultureInfo.InvariantCulture);

    public static GeneratedInvoice Make(InvoiceSpec spec, IReadOnlyList<FixtureRow>? rows = null)
    {
        // Registers the embedded PT Sans with PdfSharp (the same resolver as the inventory report).
        typeof(InventoryPdfWriter).GetMethod("EnsureFontResolver", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
        rows ??= MakeRows(spec.Rows);
        var totalNet = rows.Sum(row => row.Value);
        var vat = Math.Round(totalNet * 0.21m, 2);
        var total = totalNet + vat;
        using var document = new PdfDocument();
        var ro = spec.Romanian;
        var regular = new XFont("PT Sans", 9, XFontStyleEx.Regular);
        var bold = new XFont("PT Sans", 9, XFontStyleEx.Bold);
        var title = new XFont("PT Sans", 16, XFontStyleEx.Bold);
        var pen = new XPen(XColors.Black, 0.6);

        PdfPage page = null!;
        XGraphics g = null!;
        double width = 842, height = 595; // landscape drawing area when Rotated, else A4 portrait below
        void NewPage()
        {
            page = document.AddPage();
            if (spec.Rotated)
            {
                page.Size = PdfSharp.PageSize.A4;
                page.Rotate = 90;
                g = XGraphics.FromPdfPage(page);
                // The content is drawn as a landscape page; /Rotate 90 turns the portrait sheet so that it reads upright.
                g.TranslateTransform(0, 842);
                g.RotateTransform(-90);
                width = 842; height = 595;
            }
            else
            {
                page.Size = PdfSharp.PageSize.A4;
                g = XGraphics.FromPdfPage(page);
                width = 595; height = 842;
            }
        }
        double Right(string text, XFont font, double x) => x - g.MeasureString(text, font).Width;
        void Text(string text, double x, double y, XFont? font = null) => g.DrawString(text, font ?? regular, XBrushes.Black, x, y, XStringFormats.TopLeft);
        void RightText(string text, double rightEdge, double y, XFont? font = null) => Text(text, Right(text, font ?? regular, rightEdge), y, font);

        NewPage();
        var left = 36.0;
        var y = 34.0;
        Text(ro ? "FACTURA FISCALA" : "INVOICE", left, y, title);
        y += 30;
        var blockTop = y;
        var mid = width / 2 + 10;
        // Supplier block (left), buyer block (right), invoice data (right, under the buyer).
        Text(ro ? "FURNIZOR" : "Seller", left, y, bold);
        Text(ro ? "CUMPARATOR" : "Buyer", mid, y, bold);
        y += 15;
        Text(ro ? "Denumire:" : "Name", left, y); Text(spec.SupplierName, left + (ro ? 62 : 62), y);
        Text(ro ? "Denumire:" : "Name", mid, y); Text("Electric Standard Prest SRL", mid + 62, y);
        y += 13;
        Text(ro ? "CUI:" : "VAT ID", left, y); Text(spec.SupplierCui, left + 62, y);
        Text(ro ? "CUI:" : "VAT ID", mid, y); Text("RO9178894", mid + 62, y);
        y += 13;
        Text(ro ? "Nr. reg. com.:" : "Reg. no", left, y); Text("J20/100/2010", left + 62, y);
        Text(ro ? "Adresa:" : "Address", mid, y); Text("Str. Libertatii 39, Simeria", mid + 62, y);
        for (var i = 0; i < spec.ExtraAddressLines; i++) { y += 13; Text(ro ? "Sediu:" : "Office", left, y); Text($"Strada Exemplu {i + 1}, Timisoara", left + 62, y); }
        y += 22;
        Text(ro ? "Factura nr.:" : "Invoice No", left, y, bold); Text(spec.Number, left + 70, y, bold);
        Text(ro ? "Data emiterii:" : "Invoice date", mid, y);
        Text(ro ? spec.IssueDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : spec.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mid + 70, y);
        y += 13;
        Text(ro ? "Moneda:" : "Currency", left, y); Text(ro ? "RON" : "EUR", left + 70, y);
        Text(ro ? "Data scadenta:" : "Due date", mid, y);
        Text(ro ? spec.IssueDate.AddDays(30).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : spec.IssueDate.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), mid + 70, y);
        y += 30;

        // Table: the columns of the style, numbers right-aligned; every row is one line.
        var right = width - 36;
        (string Kind, string Head)[] columns = spec.Style switch
        {
            "ro-lines" => [("index", "Nr. crt."), ("name", "Denumirea produselor sau serviciilor"), ("unit", "U.M."), ("qty", "Cantitate"), ("price", "Pret unitar (fara TVA)"), ("value", "Valoare (fara TVA)")],
            "en-noindex" => [("name", "Description"), ("qty", "Qty"), ("price", "Unit price"), ("value", "Amount")],
            // Hungarian labels: nothing in the dictionary, the table has to be found by its structure.
            "hu-plain" => [("index", "Sorszam"), ("name", "Megnevezes"), ("unit", "Me."), ("qty", "Mennyiseg"), ("price", "Egysegar"), ("value", "Osszesen")],
            _ => [("index", "Item"), ("name", "Description"), ("unit", "Unit"), ("qty", "Qty"), ("price", "Unit price"), ("value", "Amount")]
        };
        var widths = new Dictionary<string, double> { ["index"] = 38, ["unit"] = 45, ["qty"] = 55, ["price"] = 60, ["value"] = 60 };
        var leftEdges = new Dictionary<string, double>();
        var cursor = right;
        foreach (var column in columns.Reverse().Where(item => item.Kind != "name" && item.Kind != "index")) { cursor -= widths[column.Kind]; leftEdges[column.Kind] = cursor; }
        var nameLeft = left + (columns.Any(item => item.Kind == "index") ? widths["index"] : 0);
        leftEdges["name"] = nameLeft;
        leftEdges["index"] = left;
        var rightEdgeOf = new Dictionary<string, double>();
        foreach (var column in columns) rightEdgeOf[column.Kind] = (column.Kind switch { "index" => left + widths["index"], "name" => cursor, "value" => right, _ => leftEdges[column.Kind] + widths[column.Kind] }) - (column.Kind == "value" ? 4 : 6);
        var rowHeight = 15.0;
        var lines = ro;
        // Two lines for the labels of the numeric columns (as most invoices wrap them: "Pret unitar" / "(fara TVA)").
        var headerHeight = 2 * 11.0 + 6;
        void Header(double top)
        {
            if (lines) g.DrawRectangle(pen, XBrushes.WhiteSmoke, left, top, right - left, headerHeight);
            foreach (var (kind, head) in columns)
            {
                if (kind is "index" or "name" or "unit") { Text(head, leftEdges[kind] + 3, top + 4, bold); continue; }
                var split = head.IndexOf(' ', StringComparison.Ordinal);
                var cut = head.LastIndexOf(" (", StringComparison.Ordinal) is var parenthesis and > 0 ? parenthesis : split;
                if (cut < 0) { RightText(head, rightEdgeOf[kind], top + 4, bold); continue; }
                RightText(head[..cut], rightEdgeOf[kind], top + 4, bold);
                RightText(head[(cut + 1)..], rightEdgeOf[kind], top + 15, bold);
            }
            if (lines) foreach (var (kind, _) in columns.Skip(1)) g.DrawLine(pen, leftEdges[kind], top, leftEdges[kind], top + headerHeight);
        }
        Header(y);
        y += headerHeight;
        if (spec.ColumnNumbers)
        {
            var number = 0;
            foreach (var (kind, _) in columns)
            {
                if (kind is "index" or "name" or "unit") Text(number.ToString(CultureInfo.InvariantCulture), leftEdges[kind] + 4, y + 2);
                else RightText(number.ToString(CultureInfo.InvariantCulture), rightEdgeOf[kind], y + 2);
                number++;
            }
            y += 14;
        }
        var onPage = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if ((onPage >= spec.RowsFirstPage && document.PageCount == 1) || y + 2 * rowHeight > height - 120)
            {
                NewPage();
                y = 40;
                onPage = 0;
                if (spec.RepeatHeader) { Header(y); y += headerHeight; }
            }
            var parts = Wrap(row.Name, spec.WrapNamesAt);
            var thisHeight = rowHeight + (parts.Count - 1) * 11;
            var middle = spec.CenterRows ? (parts.Count - 1) * 11 / 2.0 : 0;
            if (lines) g.DrawRectangle(pen, left, y, right - left, thisHeight);
            foreach (var (kind, _) in columns)
                switch (kind)
                {
                    case "index": Text((i + 1).ToString(CultureInfo.InvariantCulture), leftEdges[kind] + 4, y + 3 + middle); break;
                    case "name": for (var part = 0; part < parts.Count; part++) Text(parts[part], leftEdges[kind] + 3, y + 3 + part * 11); break;
                    case "unit": Text(row.Unit, leftEdges[kind] + 3, y + 3 + middle); break;
                    case "qty": RightText(row.Quantity.ToString("0", CultureInfo.InvariantCulture), rightEdgeOf[kind], y + 3 + middle); break;
                    case "price": RightText(Money(row.Price, ro), rightEdgeOf[kind], y + 3 + middle); break;
                    case "value": RightText(Money(row.Value, ro), rightEdgeOf[kind], y + 3 + middle); break;
                }
            if (lines) foreach (var (kind, _) in columns.Skip(1)) g.DrawLine(pen, leftEdges[kind], y, leftEdges[kind], y + thisHeight);
            y += thisHeight;
            onPage++;
        }
        y += 14;
        if (y > height - 100) { NewPage(); y = 40; }
        var labelX = right - 215;
        Text(ro ? "Total fara TVA:" : "Subtotal", labelX, y, bold); RightText(Money(totalNet, ro), right - 4, y, bold); y += 14;
        Text(ro ? "Total TVA:" : "VAT", labelX, y, bold); RightText(Money(vat, ro), right - 4, y, bold); y += 14;
        Text(ro ? "Total de plata:" : "Total amount due", labelX, y, bold); RightText(Money(total, ro), right - 4, y, bold);

        using var stream = new MemoryStream();
        document.Save(stream);
        Dump(stream.ToArray(), $"{spec.Style}-{rows.Count}rows{(spec.Rotated ? "-rotated" : "")}{(spec.RowsFirstPage < rows.Count ? "-2pages" : "")}");
        return new GeneratedInvoice(stream.ToArray(), rows, totalNet, vat, total);
    }

    // The name cut at a word boundary into lines of about `width` characters (one line when width is 0 or the name is short).
    private static List<string> Wrap(string text, int width)
    {
        if (width <= 0 || text.Length <= width) return [text];
        var cut = text.LastIndexOf(' ', width);
        if (cut <= 0) return [text];
        return [text[..cut], .. Wrap(text[(cut + 1)..], width)];
    }

    // The page rendered to a picture and saved as a PDF that has only that picture: what a scanner produces. rotation: 0/90/180/270 turns
    // the sheet, skewDegrees tilts it, inkStrength below 1 makes the ink pale (a washed-out scan, pencil), blurSigma (pixels) softens it (out of focus).
    // strayText: real text (a text layer) drawn over the picture at the top and the bottom, as a browser prints the date, the title and the address
    // of the page around a picture of an invoice.
    public static byte[] Scan(byte[] textPdf, double skewDegrees = 0, int rotation = 0, int dpi = 200, double inkStrength = 1.0, double blurSigma = 0, bool strayText = false)
    {
        using var input = new MemoryStream(textPdf);
#pragma warning disable CA1416
        using var bitmap = Conversion.ToImage(input, new Index(0), leaveOpen: true, options: new RenderOptions(Dpi: dpi, Grayscale: true));
#pragma warning restore CA1416
        using var gray = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC1);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                gray.Set(y, x, (byte)Math.Round(255 - (255 - bitmap.GetPixel(x, y).Red) * inkStrength));
        var current = gray.Clone();
        if (Math.Abs(skewDegrees) > 0.01)
        {
            using var matrix = Cv2.GetRotationMatrix2D(new Point2f(current.Cols / 2f, current.Rows / 2f), skewDegrees, 1.0);
            var tilted = new Mat();
            Cv2.WarpAffine(current, tilted, matrix, current.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);
            current.Dispose();
            current = tilted;
        }
        if (rotation != 0)
        {
            var turned = new Mat();
            Cv2.Rotate(current, turned, rotation switch { 90 => RotateFlags.Rotate90Clockwise, 180 => RotateFlags.Rotate180, _ => RotateFlags.Rotate90Counterclockwise });
            current.Dispose();
            current = turned;
        }
        if (blurSigma > 0) Cv2.GaussianBlur(current, current, new Size(0, 0), blurSigma);
        var png = current.ImEncode(".png");
        var pixelWidth = current.Cols;
        var pixelHeight = current.Rows;
        current.Dispose();

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pixelWidth * 72.0 / dpi);
        page.Height = XUnit.FromPoint(pixelHeight * 72.0 / dpi);
        using (var g = XGraphics.FromPdfPage(page))
        using (var image = XImage.FromStream(new MemoryStream(png)))
        {
            g.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            if (strayText)
            {
                var small = new XFont("PT Sans", 7, XFontStyleEx.Regular);
                g.DrawString("10/5/26, 12:11 PM   Images of invoices, invoice models, invoice templates | Example site", small, XBrushes.Black, 20, 8, XStringFormats.TopLeft);
                g.DrawString("https://example.test/about/invoice-images/   2/25   printed from the browser", small, XBrushes.Black, 20, page.Height.Point - 16, XStringFormats.TopLeft);
            }
        }
        using var output = new MemoryStream();
        document.Save(output);
        Dump(output.ToArray(), $"scan-skew{skewDegrees}-rot{rotation}-ink{inkStrength}-blur{blurSigma}-dpi{dpi}");
        return output.ToArray();
    }

    // INVOICE_DUMP_FIXTURES=<directory> keeps the generated PDFs, to look at them or to run the corpus report on them.
    private static void Dump(byte[] pdf, string name)
    {
        if (Environment.GetEnvironmentVariable("INVOICE_DUMP_FIXTURES") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf);
    }
}
