using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace BlazorStoc.Services;

public interface IConsumptionNoteTotalWriter
{
    // One document for several notes of the same beneficiary: a section per project and the total per product.
    byte[] Write(IReadOnlyList<ConsumptionNoteDetails> notes, DateTime generatedLocal);
    // One PDF with the pages of all the given PDFs, in order (print all notes at once).
    byte[] Merge(IEnumerable<byte[]> documents);
}

// The "bon total" of the wizard (PROP-0045): the equipment of several projects of a beneficiary in one document. The numbers of the project notes it summarizes are in the
// header (each project keeps its own numbered note in the register); quantities only. Same embedded font and PdfSharp drawing as the note of a project.
public sealed class ConsumptionNoteTotalWriter : IConsumptionNoteTotalWriter
{
    private const double PageMargin = 40;
    private const double LineHeight = 16;
    private const double CellPadding = 5;
    private static readonly bool FontResolverReady = PdfFonts.EnsureResolver();
    private static readonly XFont NormalFont = new(PdfFonts.FamilyName, 11, XFontStyleEx.Regular);
    private static readonly XFont BoldFont = new(PdfFonts.FamilyName, 11, XFontStyleEx.Bold);
    private static readonly XFont TitleFont = new(PdfFonts.FamilyName, 16, XFontStyleEx.Bold);
    private static readonly XFont FooterFont = new(PdfFonts.FamilyName, 9, XFontStyleEx.Regular);
    private static readonly XSolidBrush BlackBrush = new(XColors.Black);
    private static readonly XPen RulePen = new(XColors.Black, 0.75);

    public byte[] Write(IReadOnlyList<ConsumptionNoteDetails> notes, DateTime generatedLocal)
    {
        _ = FontResolverReady;
        if (notes.Count == 0) throw new ArgumentException("Nu există bonuri.", nameof(notes));
        var first = notes[0].Note;
        using var document = new PdfDocument();
        PdfPage page = null!;
        XGraphics gfx = null!;
        double y = 0, width = 0, bottom = 0;

        void NewPage()
        {
            gfx?.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            width = page.Width.Point - 2 * PageMargin;
            bottom = page.Height.Point - PageMargin - 24;
            y = PageMargin;
        }
        void Line(string text, XFont font, double height = LineHeight)
        {
            gfx.DrawString(text, font, BlackBrush, new XRect(PageMargin, y, width, height), XStringFormats.TopLeft);
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
        void Table(IReadOnlyList<(string Name, int Quantity)> items)
        {
            double[] widths = [40, width - 40 - 90, 90];
            var x = new[] { PageMargin, PageMargin + widths[0], PageMargin + widths[0] + widths[1] };
            void Header()
            {
                gfx.DrawRectangle(RulePen, PageMargin, y, width, LineHeight + 2);
                string[] titles = ["Nr.", "Produs", "Cantitate"];
                for (var i = 0; i < 3; i++)
                {
                    if (i > 0) gfx.DrawLine(RulePen, x[i], y, x[i], y + LineHeight + 2);
                    gfx.DrawString(titles[i], BoldFont, BlackBrush, new XRect(x[i] + CellPadding, y + 1, widths[i] - CellPadding, LineHeight), XStringFormats.TopLeft);
                }
                y += LineHeight + 2;
            }
            if (y + 3 * LineHeight > bottom) NewPage();
            Header();
            var number = 0;
            foreach (var item in items)
            {
                number++;
                var product = Wrap(item.Name, NormalFont, widths[1] - 2 * CellPadding);
                var height = product.Count * LineHeight + 2 * CellPadding;
                if (y + height > bottom) { NewPage(); Header(); }
                gfx.DrawRectangle(RulePen, PageMargin, y, width, height);
                for (var i = 1; i < 3; i++) gfx.DrawLine(RulePen, x[i], y, x[i], y + height);
                gfx.DrawString(number.ToString(CultureInfo.InvariantCulture), NormalFont, BlackBrush, new XRect(x[0] + CellPadding, y + CellPadding, widths[0], LineHeight), XStringFormats.TopLeft);
                gfx.DrawString(item.Quantity.ToString(CultureInfo.InvariantCulture), NormalFont, BlackBrush, new XRect(x[2] + CellPadding, y + CellPadding, widths[2], LineHeight), XStringFormats.TopLeft);
                for (var i = 0; i < product.Count; i++)
                    gfx.DrawString(product[i], NormalFont, BlackBrush, new XRect(x[1] + CellPadding, y + CellPadding + i * LineHeight, widths[1], LineHeight), XStringFormats.TopLeft);
                y += height;
            }
        }

        var numbers = string.Join(", ", notes.Select(item => item.Note.Id.ToString(CultureInfo.InvariantCulture)));
        NewPage();
        Line("Bon de consum total", TitleFont, 24);
        Line($"Data și ora generării: {generatedLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}", BoldFont);
        y += 6;
        Line($"Beneficiar: {first.BeneficiaryName}{(string.IsNullOrWhiteSpace(first.BeneficiaryCui) ? "" : $" (CUI {first.BeneficiaryCui})")}", NormalFont);
        foreach (var line in Wrap($"Bonuri de consum cuprinse: nr. {numbers}", NormalFont, width)) Line(line, NormalFont);
        if (!string.IsNullOrWhiteSpace(first.Operator)) Line($"Operator: {first.Operator}", NormalFont);
        y += 8;

        foreach (var item in notes)
        {
            if (y + 4 * LineHeight > bottom) NewPage();
            Line($"Proiect: {item.Note.ProjectName} (bon nr. {item.Note.Id})", BoldFont);
            Table(item.Lines.Select(line => (line.ProductName, line.Quantity)).ToArray());
            y += 12;
        }
        if (y + 4 * LineHeight > bottom) NewPage();
        Line("Total general pe produs", BoldFont);
        Table(notes.SelectMany(item => item.Lines).GroupBy(line => line.ProductName)
            .Select(group => (group.Key, group.Sum(line => line.Quantity))).OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase).ToArray());

        var boxHeight = 70.0;
        if (y + boxHeight + 24 > bottom) NewPage();
        y += 24;
        var half = width / 2 - 10;
        gfx.DrawRectangle(RulePen, PageMargin, y, half, boxHeight);
        gfx.DrawRectangle(RulePen, PageMargin + half + 20, y, half, boxHeight);
        gfx.DrawString("Predat de (nume, semnătură)", NormalFont, BlackBrush, new XRect(PageMargin + CellPadding, y + CellPadding, half, LineHeight), XStringFormats.TopLeft);
        gfx.DrawString("Primit de (nume, semnătură)", NormalFont, BlackBrush, new XRect(PageMargin + half + 20 + CellPadding, y + CellPadding, half, LineHeight), XStringFormats.TopLeft);
        gfx.DrawString($"Bon total · bonurile {numbers} · generat la {generatedLocal.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}", FooterFont, BlackBrush,
            new XRect(PageMargin, page.Height.Point - PageMargin, width, 12), XStringFormats.TopLeft);
        gfx.Dispose();
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    public byte[] Merge(IEnumerable<byte[]> documents)
    {
        using var output = new PdfDocument();
        foreach (var bytes in documents)
        {
            using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            foreach (var sourcePage in source.Pages) output.AddPage(sourcePage);
        }
        using var stream = new MemoryStream();
        output.Save(stream);
        return stream.ToArray();
    }
}
