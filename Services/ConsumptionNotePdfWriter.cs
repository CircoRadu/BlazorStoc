using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace BlazorStoc.Services;

public interface IConsumptionNotePdfWriter
{
    byte[] Write(ExitOperationDetails operation, DateTime generatedLocal);
}

// Bon de consum / aviz de predare of an exit operation: recipient, project, the products with quantities, the reference and room for the two
// signatures. Same embedded font and PdfSharp drawing as the inventory sheet.
public sealed class ConsumptionNotePdfWriter : IConsumptionNotePdfWriter
{
    private const double PageMargin = 40;
    private const double LineHeight = 16;
    private const double CellPadding = 5;
    private static readonly bool FontResolverReady = InventoryPdfWriter.EnsureFontResolver();
    private static readonly XFont NormalFont = new(InventoryPdfWriter.FamilyName, 11, XFontStyleEx.Regular);
    private static readonly XFont BoldFont = new(InventoryPdfWriter.FamilyName, 11, XFontStyleEx.Bold);
    private static readonly XFont TitleFont = new(InventoryPdfWriter.FamilyName, 16, XFontStyleEx.Bold);
    private static readonly XSolidBrush BlackBrush = new(XColors.Black);
    private static readonly XSolidBrush RedBrush = new(XColors.Red);
    private static readonly XPen RulePen = new(XColors.Black, 0.75);

    public byte[] Write(ExitOperationDetails operation, DateTime generatedLocal)
    {
        _ = FontResolverReady;
        using var document = new PdfDocument();
        PdfPage page = null!;
        XGraphics gfx = null!;
        double y = 0, width = 0, bottom = 0;

        void NewPage()
        {
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            width = page.Width.Point - 2 * PageMargin;
            bottom = page.Height.Point - PageMargin - 24;
            y = PageMargin;
        }
        void Line(string text, XFont font, XBrush? brush = null, double height = LineHeight)
        {
            gfx.DrawString(text, font, brush ?? BlackBrush, new XRect(PageMargin, y, width, height), XStringFormats.TopLeft);
            y += height;
        }
        List<string> Wrap(string text, XFont font, double maxWidth)
        {
            var lines = new List<string>();
            var current = string.Empty;
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > maxWidth) { lines.Add(current); current = word; }
                else current = candidate;
            }
            if (current.Length > 0 || lines.Count == 0) lines.Add(current);
            return lines;
        }

        NewPage();
        Line("Bon de consum / aviz de predare", TitleFont, height: 24);
        Line($"Operația nr. {operation.OperationId} · Data: {StockMovementRules.DisplayDate(operation.Date)}", BoldFont);
        if (operation.VoidedUtc is not null)
            Line($"OPERAȚIE STORNATĂ{(string.IsNullOrWhiteSpace(operation.VoidReason) ? "" : $" — motiv: {operation.VoidReason}")}", BoldFont, RedBrush);
        y += 6;
        if (operation.Destination is { } destination) Line($"Destinația: {StockMovementRules.DestinationLabel(destination)}", NormalFont);
        if (operation.BeneficiaryName is not null)
            Line($"Beneficiar: {operation.BeneficiaryName}{(string.IsNullOrWhiteSpace(operation.BeneficiaryCui) ? "" : $" (CUI {operation.BeneficiaryCui})")}", NormalFont);
        if (operation.ProjectName is not null) Line($"Proiect: {operation.ProjectName}", NormalFont);
        if (operation.VehiclePlate is not null) Line($"Vehicul: {operation.VehiclePlate}", NormalFont);
        if (!string.IsNullOrWhiteSpace(operation.Reference)) Line($"Referință: {operation.Reference}", NormalFont);
        Line($"Operator: {operation.Operator}", NormalFont);
        y += 8;

        double[] columnWidths = [30, width * 0.34, 55, 80, width - 30 - width * 0.34 - 55 - 80];
        double[] columnX = new double[5];
        void Header()
        {
            var x = PageMargin;
            for (var i = 0; i < 5; i++) { columnX[i] = x; x += columnWidths[i]; }
            string[] titles = ["Nr.", "Produs", "Cant.", "Sursa", "Descriere"];
            gfx.DrawRectangle(RulePen, PageMargin, y, width, LineHeight + 2);
            for (var i = 0; i < 5; i++)
            {
                if (i > 0) gfx.DrawLine(RulePen, columnX[i], y, columnX[i], y + LineHeight + 2);
                gfx.DrawString(titles[i], BoldFont, BlackBrush, new XRect(columnX[i] + CellPadding, y + 1, columnWidths[i] - CellPadding, LineHeight), XStringFormats.TopLeft);
            }
            y += LineHeight + 2;
        }

        // The columns are laid out when the first page is drawn (the width is the same on every page).
        Header();
        var number = 0;
        foreach (var item in operation.Lines)
        {
            number++;
            var product = Wrap(item.ProductName, NormalFont, columnWidths[1] - 2 * CellPadding);
            var description = Wrap(item.Description, NormalFont, columnWidths[4] - 2 * CellPadding);
            var source = Wrap(item.Source, NormalFont, columnWidths[3] - 2 * CellPadding);
            var rows = Math.Max(product.Count, Math.Max(description.Count, source.Count));
            var height = rows * LineHeight + 2 * CellPadding;
            if (y + height > bottom) { NewPage(); Header(); }
            gfx.DrawRectangle(RulePen, PageMargin, y, width, height);
            for (var i = 1; i < 5; i++) gfx.DrawLine(RulePen, columnX[i], y, columnX[i], y + height);
            gfx.DrawString(number.ToString(CultureInfo.InvariantCulture), NormalFont, BlackBrush, new XRect(columnX[0] + CellPadding, y + CellPadding, columnWidths[0], LineHeight), XStringFormats.TopLeft);
            gfx.DrawString(item.Quantity.ToString(CultureInfo.InvariantCulture), NormalFont, BlackBrush, new XRect(columnX[2] + CellPadding, y + CellPadding, columnWidths[2], LineHeight), XStringFormats.TopLeft);
            void Cell(List<string> lines, int column)
            {
                for (var i = 0; i < lines.Count; i++)
                    gfx.DrawString(lines[i], NormalFont, BlackBrush, new XRect(columnX[column] + CellPadding, y + CellPadding + i * LineHeight, columnWidths[column], LineHeight), XStringFormats.TopLeft);
            }
            Cell(product, 1); Cell(source, 3); Cell(description, 4);
            y += height;
        }

        // Signatures: handed over by / received by.
        var boxHeight = 70.0;
        if (y + boxHeight + 24 > bottom) NewPage();
        y += 24;
        var half = width / 2 - 10;
        gfx.DrawRectangle(RulePen, PageMargin, y, half, boxHeight);
        gfx.DrawRectangle(RulePen, PageMargin + half + 20, y, half, boxHeight);
        gfx.DrawString("Predat de (nume, semnătură)", NormalFont, BlackBrush, new XRect(PageMargin + CellPadding, y + CellPadding, half, LineHeight), XStringFormats.TopLeft);
        gfx.DrawString("Primit de (nume, semnătură)", NormalFont, BlackBrush, new XRect(PageMargin + half + 20 + CellPadding, y + CellPadding, half, LineHeight), XStringFormats.TopLeft);

        var footerFont = new XFont(InventoryPdfWriter.FamilyName, 9, XFontStyleEx.Regular);
        gfx.DrawString($"Generat la {generatedLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}", footerFont, BlackBrush,
            new XRect(PageMargin, page.Height.Point - PageMargin, width, 12), XStringFormats.TopLeft);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
