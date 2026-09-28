using PdfSharp.Drawing;

namespace BlazorStoc.Services;

// The three column boundaries of the inventory table (Cod produs | Valoare stoc | Valoare reala), in PDF points
// from the page's top-left corner. Identical on every page and every table, because InventoryPdfWriter computes
// them once for the whole document from the (fixed) A4 page width, not from the report's content.
public readonly record struct InventoryTableColumns(double PageMargin, double ContentWidth, double CodeX, double StockX, double RealX, double RightEdge)
{
    public double CodeColumnWidth => StockX - CodeX;
    public double StockColumnWidth => RealX - StockX;
    public double RealColumnWidth => RightEdge - RealX;
}

// Shared between InventoryPdfWriter (draws the table) and the "Preluare inventar" OCR pipeline (crops a scanned
// copy of the same table back out of an image): both must agree on where the columns are, since the OCR side
// never redetects the layout from the scan (TODO.md Task 1, decizia tehnica) - it replays this exact geometry.
public static class InventoryPdfLayout
{
    public const double PageMargin = 40;

    public static InventoryTableColumns ComputeColumns(double pageWidthPoints)
    {
        InventoryPdfWriter.EnsureFontResolver();
        using var gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont(InventoryPdfWriter.FamilyName, 12, XFontStyleEx.Regular);
        var contentWidth = pageWidthPoints - 2 * PageMargin;
        var stockColumnWidth = Math.Max(contentWidth * 0.18, gfx.MeasureString("-000000", font).Width + 16);
        var realColumnWidth = Math.Max(contentWidth * 0.18, gfx.MeasureString("000000", font).Width + 16);
        var codeColumnWidth = contentWidth - stockColumnWidth - realColumnWidth;
        var codeX = PageMargin;
        var stockX = codeX + codeColumnWidth;
        var realX = stockX + stockColumnWidth;
        var rightEdge = codeX + contentWidth;
        return new InventoryTableColumns(PageMargin, contentWidth, codeX, stockX, realX, rightEdge);
    }
}
