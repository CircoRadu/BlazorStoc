using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace BlazorStoc.Services;

public interface IProjectSituationPdfWriter
{
    byte[] Write(ProjectSituation situation, DateTime generatedLocal);
}

// Situatia proiectului as PDF: a table per component, the leftovers outside the offer and the purchase list by supplier. Plain text rows (no prices).
public sealed class ProjectSituationPdfWriter : IProjectSituationPdfWriter
{
    private const double Margin = 40, RowHeight = 15;
    private static readonly bool FontResolverReady = InventoryPdfWriter.EnsureFontResolver();
    private static readonly XFont Normal = new(InventoryPdfWriter.FamilyName, 9, XFontStyleEx.Regular);
    private static readonly XFont Bold = new(InventoryPdfWriter.FamilyName, 9, XFontStyleEx.Bold);
    private static readonly XFont Heading = new(InventoryPdfWriter.FamilyName, 12, XFontStyleEx.Bold);
    private static readonly XFont Title = new(InventoryPdfWriter.FamilyName, 16, XFontStyleEx.Bold);
    private static readonly XSolidBrush Black = new(XColors.Black);

    public byte[] Write(ProjectSituation situation, DateTime generatedLocal)
    {
        _ = FontResolverReady;
        using var document = new PdfDocument();
        PdfPage page = null!;
        XGraphics gfx = null!;
        double y = 0, width = 0, bottom = 0;
        void NewPage()
        {
            page = document.AddPage(); page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            width = page.Width.Point - 2 * Margin; bottom = page.Height.Point - Margin - 20; y = Margin;
        }
        void Text(string text, XFont font, double x, double w, double height = RowHeight)
        {
            // Clipped to the cell: one line per row, the full text is on the page of the project.
            var shown = text;
            while (shown.Length > 1 && gfx.MeasureString(shown, font).Width > w) shown = shown[..^1];
            if (shown.Length < text.Length && shown.Length > 1) shown = shown[..^1] + "…";
            gfx.DrawString(shown, font, Black, new XRect(x, y, w, height), XStringFormats.TopLeft);
        }
        void Row(string[] cells, double[] widths, XFont font)
        {
            if (y + RowHeight > bottom) NewPage();
            var x = Margin;
            for (var i = 0; i < cells.Length; i++) { Text(cells[i], font, x + 2, widths[i] - 4); x += widths[i]; }
            y += RowHeight;
        }
        double[] Widths(params double[] share) { var sum = share.Sum(); return [.. share.Select(item => width * item / sum)]; }

        NewPage();
        gfx.DrawString("Situația proiectului", Title, Black, new XRect(Margin, y, width, 24), XStringFormats.TopLeft); y += 24;
        gfx.DrawString(situation.ProjectName, Bold, Black, new XRect(Margin, y, width, RowHeight), XStringFormats.TopLeft); y += RowHeight + 8;

        var lineWidths = Widths(14, 40, 9, 9, 9, 9, 10);
        foreach (var component in situation.Components)
        {
            if (y + 3 * RowHeight > bottom) NewPage();
            gfx.DrawString($"{component.Name} — predat {ProjectSituationRules.Format(component.Delivered)} din {ProjectSituationRules.Format(component.Needed)} (bucăți)", Heading, Black,
                new XRect(Margin, y, width, 18), XStringFormats.TopLeft); y += 20;
            Row(["Secțiune", "Denumire", "Necesar", "Predat", "Din stoc", "Deficit", "Stare"], lineWidths, Bold);
            foreach (var line in component.Lines)
                Row([line.Section, ProjectSituationRules.FirstLine(line.Name), $"{ProjectSituationRules.Format(line.Quantity)} {line.Unit}".Trim(),
                    line.InStock ? ProjectSituationRules.Format(line.Delivered) : "-", line.InStock ? ProjectSituationRules.Format(line.FromStock) : "-",
                    line.InStock ? ProjectSituationRules.Format(line.Deficit) : "-", ProjectSituationRules.StateLabel(line.State)], lineWidths, Normal);
            y += 10;
        }
        if (situation.OutsideOffer.Count > 0)
        {
            if (y + 3 * RowHeight > bottom) NewPage();
            gfx.DrawString("În afara ofertei", Heading, Black, new XRect(Margin, y, width, 18), XStringFormats.TopLeft); y += 20;
            var outsideWidths = Widths(80, 20);
            Row(["Produs", "Predat"], outsideWidths, Bold);
            foreach (var item in situation.OutsideOffer) Row([item.ProductName, item.Quantity.ToString(CultureInfo.InvariantCulture)], outsideWidths, Normal);
            y += 10;
        }
        if (situation.Purchase.Count > 0)
        {
            if (y + 3 * RowHeight > bottom) NewPage();
            gfx.DrawString("Lista de achiziție", Heading, Black, new XRect(Margin, y, width, 18), XStringFormats.TopLeft); y += 20;
            var purchaseWidths = Widths(80, 20);
            foreach (var group in situation.Purchase)
            {
                if (y + 3 * RowHeight > bottom) NewPage();
                Row([group.Supplier, "Cantitate"], purchaseWidths, Bold);
                foreach (var item in group.Items) Row([item.Name, $"{ProjectSituationRules.Format(item.Quantity)} {item.Unit}".Trim()], purchaseWidths, Normal);
                y += 6;
            }
        }
        var footer = new XFont(InventoryPdfWriter.FamilyName, 8, XFontStyleEx.Regular);
        gfx.DrawString($"Generat la {generatedLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}", footer, Black, new XRect(Margin, page.Height.Point - Margin, width, 12), XStringFormats.TopLeft);
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }
}
