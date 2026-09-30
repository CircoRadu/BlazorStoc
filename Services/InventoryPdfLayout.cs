using PdfSharp.Drawing;

namespace BlazorStoc.Services;

// The four column boundaries of the inventory table (Nr. crt. | Cod produs | Valoare stoc | Valoare reala), in PDF
// points from the page's top-left corner. Identical on every page and every table, because InventoryPdfWriter
// computes them once for the whole document from the (fixed) A4 page width, not from the report's content.
public readonly record struct InventoryTableColumns(double PageMargin, double ContentWidth, double NumberX, double CodeX, double StockX, double RealX, double RightEdge)
{
    public double NumberColumnWidth => CodeX - NumberX;
    public double CodeColumnWidth => StockX - CodeX;
    public double StockColumnWidth => RealX - StockX;
    public double RealColumnWidth => RightEdge - RealX;
}

// Shared between InventoryPdfWriter (draws the table) and the "Preluare inventar" OCR pipeline, which only uses it
// as a fallback when a scanned row's vertical rules cannot be told apart: the columns are normally found in the scan
// itself (InventoryPickupOcrService.FindGridLines / FindDividers), so both sides still agree on the column FRACTIONS.
public static class InventoryPdfLayout
{
    public const double PageMargin = 40;
    public const string NumberHeader = "Nr. crt.";

    public static InventoryTableColumns ComputeColumns(double pageWidthPoints)
    {
        InventoryPdfWriter.EnsureFontResolver();
        using var gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont(InventoryPdfWriter.FamilyName, 12, XFontStyleEx.Regular);
        var contentWidth = pageWidthPoints - 2 * PageMargin;
        var numberColumnWidth = Math.Max(gfx.MeasureString(NumberHeader, font).Width, gfx.MeasureString("0000", font).Width) + 16;
        var stockColumnWidth = Math.Max(contentWidth * 0.18, gfx.MeasureString("-000000", font).Width + 16);
        var realColumnWidth = Math.Max(contentWidth * 0.18, gfx.MeasureString("000000", font).Width + 16);
        var codeColumnWidth = contentWidth - numberColumnWidth - stockColumnWidth - realColumnWidth;
        var numberX = PageMargin;
        var codeX = numberX + numberColumnWidth;
        var stockX = codeX + codeColumnWidth;
        var realX = stockX + stockColumnWidth;
        var rightEdge = numberX + contentWidth;
        return new InventoryTableColumns(PageMargin, contentWidth, numberX, codeX, stockX, realX, rightEdge);
    }
}
