using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace BlazorStoc.Services;

// Own reader of .xlsx files (no library): sheets in their order, shared and inline strings, numbers, and merged ranges. The value of a merged range
// is kept in its top-left cell only (as Excel stores it); Merge(row, column) tells which range a cell belongs to. The old .xls format is not supported.
public sealed record XlsxCell(string Text, double? Number);

public sealed record XlsxMerge(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public bool Contains(int row, int column) => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;
}

public sealed class XlsxSheet(string name, IReadOnlyDictionary<(int Row, int Column), XlsxCell> cells, IReadOnlyList<XlsxMerge> merges)
{
    public string Name { get; } = name;
    public IReadOnlyList<XlsxMerge> Merges { get; } = merges;
    public int LastRow { get; } = cells.Count == 0 ? 0 : cells.Keys.Max(key => key.Row);
    public int LastColumn { get; } = cells.Count == 0 ? 0 : cells.Keys.Max(key => key.Column);

    public XlsxCell? Cell(int row, int column) => cells.GetValueOrDefault((row, column));
    public string Text(int row, int column) => Cell(row, column)?.Text ?? string.Empty;
    public double? Number(int row, int column) => Cell(row, column)?.Number;
    public XlsxMerge? MergeAt(int row, int column) => Merges.FirstOrDefault(merge => merge.Contains(row, column));
    public bool RowIsEmpty(int row) => !Enumerable.Range(1, LastColumn).Any(column => Text(row, column).Trim().Length > 0);
}

public sealed class XlsxWorkbook(IReadOnlyList<XlsxSheet> sheets)
{
    public IReadOnlyList<XlsxSheet> Sheets { get; } = sheets;
    public XlsxSheet? Sheet(string? name) => string.IsNullOrWhiteSpace(name) ? Sheets.FirstOrDefault() : Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));
}

public class XlsxFormatException(string message) : Exception(message);

public static class XlsxReader
{
    public const long MaxPartBytes = 40L * 1024 * 1024;
    public const string NotXlsxMessage = "Fișierul nu este un registru Excel .xlsx valid. Fișierele .xls (format vechi) nu se pot citi: salvează-le ca .xlsx din Excel.";
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static XlsxWorkbook Read(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var workbook = Load(archive, "xl/workbook.xml") ?? throw new XlsxFormatException(NotXlsxMessage);
            var relations = Load(archive, "xl/_rels/workbook.xml.rels");
            var targets = relations?.Root?.Elements(PackageRel + "Relationship")
                .ToDictionary(item => (string)item.Attribute("Id")!, item => (string)item.Attribute("Target")!) ?? [];
            var shared = ReadSharedStrings(Load(archive, "xl/sharedStrings.xml"));
            var sheets = new List<XlsxSheet>();
            foreach (var sheet in workbook.Root?.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
            {
                var name = (string?)sheet.Attribute("name") ?? $"Foaia {sheets.Count + 1}";
                var id = (string?)sheet.Attribute(Rel + "id");
                if (id is null || !targets.TryGetValue(id, out var target)) continue;
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                var document = Load(archive, path);
                if (document is not null) sheets.Add(ReadSheet(name, document, shared));
            }
            if (sheets.Count == 0) throw new XlsxFormatException(NotXlsxMessage);
            return new XlsxWorkbook(sheets);
        }
        catch (InvalidDataException) { throw new XlsxFormatException(NotXlsxMessage); }
        catch (XmlException) { throw new XlsxFormatException(NotXlsxMessage); }
    }

    private static XDocument? Load(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        if (entry.Length > MaxPartBytes) throw new XlsxFormatException("Fișierul Excel este prea mare.");
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static List<string> ReadSharedStrings(XDocument? document) =>
        document?.Root?.Elements(Main + "si").Select(StringOf).ToList() ?? [];

    // The text of a string item: all its text runs (rich text), without the phonetic runs.
    private static string StringOf(XElement item) =>
        string.Concat(item.Descendants(Main + "t").Where(text => text.Ancestors(Main + "rPh").Any() == false).Select(text => text.Value));

    private static XlsxSheet ReadSheet(string name, XDocument document, List<string> shared)
    {
        var cells = new Dictionary<(int, int), XlsxCell>();
        foreach (var cell in document.Descendants(Main + "c"))
        {
            if (!TryParseReference((string?)cell.Attribute("r"), out var row, out var column)) continue;
            var type = (string?)cell.Attribute("t");
            var raw = cell.Element(Main + "v")?.Value;
            string? text = null; double? number = null;
            switch (type)
            {
                case "s": if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < shared.Count) text = shared[index]; break;
                case "inlineStr": text = cell.Element(Main + "is") is { } inline ? StringOf(inline) : null; break;
                case "str": case "e": text = raw; break;
                case "b": text = raw == "1" ? "TRUE" : "FALSE"; break;
                default:
                    if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    { number = parsed; text = FormatNumber(parsed); }
                    break;
            }
            if (!string.IsNullOrEmpty(text)) cells[(row, column)] = new XlsxCell(text, number);
        }
        var merges = new List<XlsxMerge>();
        foreach (var merge in document.Descendants(Main + "mergeCell"))
        {
            var parts = ((string?)merge.Attribute("ref") ?? "").Split(':');
            if (parts.Length == 2 && TryParseReference(parts[0], out var firstRow, out var firstColumn) && TryParseReference(parts[1], out var lastRow, out var lastColumn))
                merges.Add(new XlsxMerge(firstRow, firstColumn, lastRow, lastColumn));
        }
        return new XlsxSheet(name, cells, merges);
    }

    // Numbers as typed in Excel (the stored 25638.799999999999 is shown as 25638.8).
    public static string FormatNumber(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    public static bool TryParseReference(string? reference, out int row, out int column)
    {
        row = 0; column = 0;
        if (string.IsNullOrEmpty(reference)) return false;
        var index = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }
        return column > 0 && index < reference.Length && int.TryParse(reference[index..], NumberStyles.None, CultureInfo.InvariantCulture, out row) && row > 0;
    }

    public static string ColumnLetter(int column)
    {
        var letters = string.Empty;
        for (; column > 0; column = (column - 1) / 26) letters = (char)('A' + (column - 1) % 26) + letters;
        return letters;
    }

    public static int ColumnIndex(string? letters)
    {
        var column = 0;
        foreach (var character in (letters ?? string.Empty).Trim())
        {
            if (!char.IsAsciiLetter(character)) return 0;
            column = column * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }
        return column;
    }
}
