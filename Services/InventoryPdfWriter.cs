using System.Globalization;
using System.Reflection;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace BlazorStoc.Services;

public interface IInventoryPdfWriter
{
    byte[] Write(InventoryReport report, DateTime generatedLocal);
}

// Embeds PT Sans (SIL Open Font License, Assets/Fonts) so the PDF never depends on fonts installed on the server
// and renders Romanian diacritics correctly. PdfSharp 6.x does not ship MigraDoc yet, so the report is laid out
// directly with XGraphics (see docs/PROJECT_STATE.md for the library decision).
public sealed class InventoryPdfWriter : IInventoryPdfWriter
{
    internal const string FamilyName = "PT Sans";
    private const double PageMargin = 40;
    private const double LineHeight = 16;
    // Height of a data row of the table: room for a handwritten "Valoare reala" that the scan/OCR can read (the text
    // lines themselves keep LineHeight, a row taller than its text is centered vertically).
    public const double RowHeight = 34;
    private const double HeadingLineHeight = 20;
    private const double SectionGap = 10;
    private const double CategoryGap = 12;
    private const double SubcategoryGap = 8;
    private const double FooterReserve = 30;
    private const double CellPadding = 5;

    // Static field initializers run in declaration order and, unlike an explicit static constructor, are guaranteed
    // to run before the XFont fields below are initialized — the resolver must already be in place by then.
    private static readonly bool FontResolverReady = EnsureFontResolver();
    private static readonly XFont NormalFont = new(FamilyName, 12, XFontStyleEx.Regular);
    private static readonly XFont HeadingFont = new(FamilyName, 14, XFontStyleEx.Bold);
    private static readonly XSolidBrush BlackBrush = new(XColors.Black);
    private static readonly XSolidBrush RedBrush = new(XColors.Red);
    private static readonly XPen RulePen = new(XColors.Black, 0.75);

    internal static bool EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not InventoryFontResolver) GlobalFontSettings.FontResolver = new InventoryFontResolver();
        return true;
    }

    public byte[] Write(InventoryReport report, DateTime generatedLocal)
    {
        using var document = new PdfDocument();
        var pages = new List<(PdfPage Page, XGraphics Graphics)>();
        PdfPage page = null!;
        XGraphics gfx = null!;
        double y = 0, contentWidth = 0, bottomLimit = 0;

        void NewPage()
        {
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            pages.Add((page, gfx));
            contentWidth = page.Width.Point - 2 * PageMargin;
            bottomLimit = page.Height.Point - PageMargin - FooterReserve;
            y = PageMargin;
        }

        bool EnsureSpace(double needed)
        {
            if (y + needed <= bottomLimit) return false;
            NewPage();
            return true;
        }

        void DrawLine(string text, XFont font, XBrush brush, double height)
        {
            gfx.DrawString(text, font, brush, new XRect(PageMargin, y, contentWidth, height), XStringFormats.TopLeft);
            y += height;
        }

        NewPage();
        DrawLine("Inventar", NormalFont, BlackBrush, LineHeight);
        DrawLine($"Generat la: {generatedLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}", NormalFont, BlackBrush, LineHeight);
        y += SectionGap;

        // Column geometry is shared with the OCR pickup pipeline (InventoryPdfLayout), which re-crops a scanned
        // copy of this exact PDF at the same positions; keep both in sync instead of duplicating the formula.
        var columns = InventoryPdfLayout.ComputeColumns(page.Width.Point);
        var numberColumnWidth = columns.NumberColumnWidth;
        var codeColumnWidth = columns.CodeColumnWidth;
        var stockColumnWidth = columns.StockColumnWidth;
        var realColumnWidth = columns.RealColumnWidth;
        var numberX = columns.NumberX;
        var codeX = columns.CodeX;
        var stockX = columns.StockX;
        var realX = columns.RealX;

        // Every cell has all four sides drawn (not just a header rule), so the printed sheet is easy to follow while
        // counting by hand: vertical dividers between the four columns, plus the row's top and bottom edges.
        void DrawRowGrid(double top, double height)
        {
            gfx.DrawRectangle(RulePen, PageMargin, top, contentWidth, height);
            gfx.DrawLine(RulePen, codeX, top, codeX, top + height);
            gfx.DrawLine(RulePen, stockX, top, stockX, top + height);
            gfx.DrawLine(RulePen, realX, top, realX, top + height);
        }

        void DrawTableHeader()
        {
            EnsureSpace(LineHeight);
            var top = y;
            gfx.DrawString(InventoryPdfLayout.NumberHeader, NormalFont, BlackBrush, new XRect(numberX + CellPadding, top, numberColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
            gfx.DrawString("Cod produs", NormalFont, BlackBrush, new XRect(codeX + CellPadding, top, codeColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
            gfx.DrawString("Valoare stoc", NormalFont, BlackBrush, new XRect(stockX + CellPadding, top, stockColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
            gfx.DrawString("Valoare reală", NormalFont, BlackBrush, new XRect(realX + CellPadding, top, realColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
            DrawRowGrid(top, LineHeight);
            y += LineHeight;
        }

        foreach (var category in report.Categories)
        {
            // A category label never ends up alone at the bottom of a page: reserve room for it, the first
            // subcategory label, the table header and at least one row before drawing it.
            EnsureSpace(HeadingLineHeight + HeadingLineHeight + LineHeight + RowHeight);
            y += CategoryGap;
            DrawLine(category.Category, HeadingFont, BlackBrush, HeadingLineHeight);

            foreach (var subcategory in category.Subcategories)
            {
                EnsureSpace(HeadingLineHeight + LineHeight + RowHeight);
                DrawLine(subcategory.Subcategory, HeadingFont, BlackBrush, HeadingLineHeight);
                DrawTableHeader();

                // "Nr. crt.": running number of the product inside its subcategory, from 1, restarting for every
                // subcategory and continuing across a page break. Printed only, never stored (the pickup page shows it
                // back next to the code, read from the scan, to ease matching the paper against the screen).
                var rowNumber = 0;
                foreach (var line in subcategory.Lines)
                {
                    rowNumber++;
                    var codeLines = WrapText(gfx, line.Code, NormalFont, codeColumnWidth - 2 * CellPadding);
                    var textHeight = codeLines.Count * LineHeight;
                    var rowHeight = Math.Max(RowHeight, textHeight + 2 * CellPadding);
                    if (EnsureSpace(rowHeight)) DrawTableHeader();
                    var brush = line.IsNegative ? RedBrush : BlackBrush;
                    var top = y;
                    gfx.DrawString(rowNumber.ToString(CultureInfo.InvariantCulture), NormalFont, brush, new XRect(numberX + CellPadding, top + (rowHeight - LineHeight) / 2, numberColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
                    for (var i = 0; i < codeLines.Count; i++)
                        gfx.DrawString(codeLines[i], NormalFont, brush, new XRect(codeX + CellPadding, top + (rowHeight - textHeight) / 2 + i * LineHeight, codeColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
                    gfx.DrawString(line.Quantity.ToString(CultureInfo.InvariantCulture), NormalFont, brush, new XRect(stockX + CellPadding, top + (rowHeight - LineHeight) / 2, stockColumnWidth - CellPadding, LineHeight), XStringFormats.TopLeft);
                    DrawRowGrid(top, rowHeight);
                    y += rowHeight;
                }
                y += SubcategoryGap;
            }
        }

        for (var i = 0; i < pages.Count; i++)
        {
            var (footerPage, footerGraphics) = pages[i];
            footerGraphics.DrawString($"Pagina {i + 1} din {pages.Count}", NormalFont, BlackBrush,
                new XRect(PageMargin, footerPage.Height.Point - PageMargin, contentWidth, LineHeight), XStringFormats.BottomCenter);
        }
        foreach (var (_, graphics) in pages) graphics.Dispose();

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    // Greedy word wrap that also breaks a single overly long word by character, so a long product code never
    // overflows the column width.
    private static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        foreach (var rawWord in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = rawWord;
            while (gfx.MeasureString(word, font).Width > maxWidth && word.Length > 1)
            {
                var cut = word.Length;
                while (cut > 1 && gfx.MeasureString(word[..cut], font).Width > maxWidth) cut--;
                Append(lines, word[..cut], gfx, font, maxWidth);
                word = word[cut..];
            }
            Append(lines, word, gfx, font, maxWidth);
        }
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static void Append(List<string> lines, string word, XGraphics gfx, XFont font, double maxWidth)
    {
        if (word.Length == 0) return;
        if (lines.Count == 0) { lines.Add(word); return; }
        var candidate = $"{lines[^1]} {word}";
        if (gfx.MeasureString(candidate, font).Width <= maxWidth) lines[^1] = candidate;
        else lines.Add(word);
    }

    private sealed class InventoryFontResolver : IFontResolver
    {
        private const string RegularFace = "PTSansRegular";
        private const string BoldFace = "PTSansBold";
        private static readonly Lazy<byte[]> Regular = new(() => ReadResource("PTSans-Regular.ttf"));
        private static readonly Lazy<byte[]> Bold = new(() => ReadResource("PTSans-Bold.ttf"));

        public byte[] GetFont(string faceName) => faceName switch
        {
            RegularFace => Regular.Value,
            BoldFace => Bold.Value,
            _ => throw new InvalidOperationException($"Font necunoscut: {faceName}")
        };

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            string.Equals(familyName, FamilyName, StringComparison.OrdinalIgnoreCase)
                ? new FontResolverInfo(isBold ? BoldFace : RegularFace)
                : null;

        private static byte[] ReadResource(string fileName)
        {
            var assembly = typeof(InventoryFontResolver).Assembly;
            var name = $"{assembly.GetName().Name}.Assets.Fonts.{fileName}";
            using var resourceStream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Fontul „{fileName}” lipsește din resursele aplicației.");
            using var memory = new MemoryStream();
            resourceStream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
