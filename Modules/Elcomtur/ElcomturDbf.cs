using System.Globalization;
using System.Text;

namespace BlazorStoc.Modules.Elcomtur;

// One table of the Elcomtur program (dBase / Visual FoxPro .dbf): the rows as text by column name; deleted rows are left out, memo columns are not read.
public sealed record DbfTable(DateOnly? LastUpdate, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public static class DbfReader
{
    private static readonly Lazy<Encoding> Text = new(() =>
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1250); }
        catch (Exception) { return Encoding.Latin1; }
    });

    public static DbfTable Read(byte[] data)
    {
        if (data.Length < 33) throw new InvalidDataException("Tabelul Elcomtur este gol sau deteriorat.");
        var records = checked((int)BitConverter.ToUInt32(data, 4));
        int headerLength = BitConverter.ToUInt16(data, 8), recordLength = BitConverter.ToUInt16(data, 10);
        if (headerLength < 33 || recordLength < 2 || headerLength > data.Length) throw new InvalidDataException("Antetul tabelului Elcomtur nu este valid.");
        var columns = new List<(string Name, char Type, int Offset, int Length)>();
        var offset = 1;
        for (var position = 32; position + 32 <= headerLength && data[position] != 0x0D; position += 32)
        {
            var name = Encoding.ASCII.GetString(data, position, 11).TrimEnd('\0', ' ').ToUpperInvariant();
            int length = data[position + 16];
            columns.Add((name, (char)data[position + 11], offset, length));
            offset += length;
        }
        var rows = new List<IReadOnlyDictionary<string, string>>(records);
        for (var index = 0; index < records; index++)
        {
            var start = headerLength + (long)index * recordLength;
            if (start + recordLength > data.Length) break;
            if (data[start] == 0x2A) continue;
            var row = new Dictionary<string, string>(columns.Count, StringComparer.Ordinal);
            foreach (var (name, type, columnOffset, length) in columns)
            {
                if (type is 'M' or 'G' or 'P' or 'B' or 'T' or 'I' or 'Y') continue;
                row[name] = Text.Value.GetString(data, (int)start + columnOffset, length).Trim('\0', ' ');
            }
            rows.Add(row);
        }
        return new DbfTable(LastUpdate(data), rows);
    }

    // The date of the last change is kept in the header as year (since 2000 for 0..69, else 1900), month, day.
    private static DateOnly? LastUpdate(byte[] data)
    {
        int year = data[1], month = data[2], day = data[3];
        year += year < 70 ? 2000 : 1900;
        try { return new DateOnly(year, month, day); } catch (ArgumentOutOfRangeException) { return null; }
    }

    public static string Text_(IReadOnlyDictionary<string, string> row, string column) => row.TryGetValue(column, out var value) ? value : "";

    public static decimal Number(IReadOnlyDictionary<string, string> row, string column) =>
        decimal.TryParse(Text_(row, column), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    public static DateOnly? Date(IReadOnlyDictionary<string, string> row, string column) =>
        DateOnly.TryParseExact(Text_(row, column), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
}
