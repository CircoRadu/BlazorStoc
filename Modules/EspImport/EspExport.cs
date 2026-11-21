using System.IO.Compression;
using System.Text;

namespace BlazorStoc.Modules.EspImport;

// What is read from an export of the old ESPStoc database (a HeidiSQL / mysqldump file, alone or in a ZIP): the categories, the subcategories, the products and, for the products with a
// positive quantity only, their image. Nothing else of the old database is read (movements, log, users and the backup copy table are skipped).
public sealed record EspCategory(int Id, string Name);
public sealed record EspSubcategory(int Id, int CategoryId, string Name);
public sealed record EspProduct(int Id, int CategoryId, int SubcategoryId, string Code, string Description, int Quantity);

public sealed class EspExport
{
    public required string SourceName { get; init; }
    public required IReadOnlyList<EspCategory> Categories { get; init; }
    public required IReadOnlyList<EspSubcategory> Subcategories { get; init; }
    /// <summary>The products with a positive quantity (the only ones that are taken).</summary>
    public required IReadOnlyList<EspProduct> Products { get; init; }
    public required int TotalProducts { get; init; }
    public required int ZeroProducts { get; init; }
    /// <summary>Old product id -> image bytes, for the taken products that have one.</summary>
    public required IReadOnlyDictionary<int, byte[]> Images { get; init; }
}

public sealed class EspExportException(string message) : Exception(message), IExpectedAnswer;

public static class EspExportReader
{
    private const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private static readonly string[] FirstPassTables = ["categorie", "subcategorie", "produs"];

    /// <summary>Reads the export from a file on disk (a .sql or a .zip that holds one .sql).</summary>
    public static async Task<EspExport> ReadAsync(string path, string sourceName, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new EspExportException("Fișierul nu există.");
        var length = new FileInfo(path).Length;
        if (length == 0 || length > MaximumBytes) throw new EspExportException("Fișierul exportului este gol sau prea mare.");
        var isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!isZip && !path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) throw new EspExportException("Se acceptă un fișier .sql sau un .zip care conține exportul .sql.");

        return await Task.Run(async () =>
        {
            var tables = await ReadTablesAsync(path, isZip, FirstPassTables, null, cancellationToken).ConfigureAwait(false);
            var categories = tables["categorie"].Select(row => new EspCategory(ToInt(row[0]), Text(row[2]))).ToList();
            var subcategories = tables["subcategorie"].Select(row => new EspSubcategory(ToInt(row[0]), ToInt(row[1]), Text(row[3]))).ToList();
            var all = tables["produs"].Select(row => new EspProduct(ToInt(row[0]), ToInt(row[1]), ToInt(row[2]), Text(row[4]).Trim(), Text(row[5]), ToInt(row[6]))).ToList();
            if (categories.Count == 0 || all.Count == 0)
                throw new EspExportException("Exportul nu pare a fi o bază ESPStoc: nu s-au găsit tabelele categorie, subcategorie și produs cu date.");
            var taken = all.Where(product => product.Quantity > 0).ToList();
            var wanted = taken.Select(product => product.Id).ToHashSet();
            var imageTables = await ReadTablesAsync(path, isZip, ["imagine"], row => row.Length > 2 && wanted.Contains(ToInt(row[2])), cancellationToken).ConfigureAwait(false);
            var images = new Dictionary<int, byte[]>();
            foreach (var row in imageTables["imagine"])
                if (row.Length > 4 && row[4] is byte[] bytes && bytes.Length > 0) images.TryAdd(ToInt(row[2]), bytes);
            return new EspExport
            {
                SourceName = sourceName, Categories = categories, Subcategories = subcategories, Products = taken, Images = images,
                TotalProducts = all.Count, ZeroProducts = all.Count - taken.Count
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int ToInt(object? value) => value switch { int i => i, long l => checked((int)l), null => 0, _ => int.TryParse(value.ToString(), out var parsed) ? parsed : 0 };
    private static string Text(object? value) => value switch { null => "", string s => s, byte[] b => Encoding.UTF8.GetString(b), _ => value.ToString() ?? "" };

    private static async Task<Dictionary<string, List<object?[]>>> ReadTablesAsync(string path, bool isZip, IEnumerable<string> tables, Func<object?[], bool>? keep, CancellationToken token)
    {
        var wanted = tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = wanted.ToDictionary(name => name, _ => new List<object?[]>(), StringComparer.OrdinalIgnoreCase);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        Stream? entryStream = null;
        ZipArchive? archive = null;
        try
        {
            Stream source = file;
            if (isZip)
            {
                archive = new ZipArchive(file, ZipArchiveMode.Read);
                var entry = archive.Entries.FirstOrDefault(item => item.FullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    ?? throw new EspExportException("Arhiva nu conține niciun fișier .sql.");
                entryStream = entry.Open();
                source = entryStream;
            }
            using var reader = new StreamReader(source, new UTF8Encoding(false), true, 1 << 20);
            string? line;
            var builder = new StringBuilder();
            while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) is not null)
            {
                if (!line.StartsWith("INSERT INTO `", StringComparison.Ordinal)) continue;
                var close = line.IndexOf('`', 13);
                if (close < 0) continue;
                var table = line[13..close];
                if (!wanted.Contains(table)) continue;
                builder.Clear();
                builder.Append(line);
                while (!StatementComplete(builder))
                {
                    var next = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (next is null) break;
                    builder.Append('\n').Append(next);
                }
                var text = builder.ToString();
                var values = text.IndexOf("VALUES", close, StringComparison.Ordinal);
                if (values < 0) continue;
                foreach (var row in SqlValues.Parse(text, values + 6))
                    if (keep is null || keep(row)) result[table].Add(row);
            }
        }
        finally
        {
            if (entryStream is not null) await entryStream.DisposeAsync().ConfigureAwait(false);
            archive?.Dispose();
        }
        return result;
    }

    // A statement is complete when it ends with ';' outside a quoted string. Strings are quoted with ' and escaped by \ or ''.
    private static bool StatementComplete(StringBuilder text)
    {
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '\'') { if (i + 1 < text.Length && text[i + 1] == '\'') i++; else inString = false; }
            }
            else if (c == '\'') inString = true;
        }
        if (inString) return false;
        var end = text.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        return end >= 0 && text[end] == ';';
    }
}

/// <summary>Reads the value tuples of an INSERT ... VALUES statement: numbers, strings (with the MariaDB escapes), NULL and binary literals (_binary 0x..., 0x..., X'...').</summary>
internal static class SqlValues
{
    public static IEnumerable<object?[]> Parse(string text, int index)
    {
        var rows = new List<object?[]>();
        var i = index;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '(')
            {
                i++;
                var row = new List<object?>();
                while (true)
                {
                    SkipSpace(text, ref i);
                    row.Add(ReadValue(text, ref i));
                    SkipSpace(text, ref i);
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] == ')') { i++; break; }
                    throw new EspExportException("Exportul are un rând de date pe care nu l-am putut citi.");
                }
                rows.Add([.. row]);
            }
            else if (c == ';') break;
            else i++;
        }
        return rows;
    }

    private static void SkipSpace(string text, ref int i) { while (i < text.Length && char.IsWhiteSpace(text[i])) i++; }

    private static object? ReadValue(string text, ref int i)
    {
        var c = text[i];
        if (c == '\'') return ReadString(text, ref i);
        if (string.CompareOrdinal(text, i, "_binary", 0, 7) == 0)
        {
            i += 7;
            SkipSpace(text, ref i);
            if (text[i] == '\'') return Encoding.Latin1.GetBytes(ReadString(text, ref i));
            return ReadHex(text, ref i);
        }
        if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X')) return ReadHex(text, ref i);
        if ((c == 'X' || c == 'x') && i + 1 < text.Length && text[i + 1] == '\'')
        {
            var end = text.IndexOf('\'', i + 2);
            var bytes = Convert.FromHexString(text.AsSpan(i + 2, end - i - 2));
            i = end + 1;
            return bytes;
        }
        var start = i;
        while (i < text.Length && text[i] != ',' && text[i] != ')') i++;
        var token = text[start..i].Trim();
        if (token.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return null;
        if (long.TryParse(token, out var integer)) return integer;
        if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var real)) return real;
        return token;
    }

    private static byte[] ReadHex(string text, ref int i)
    {
        i += 2;
        var start = i;
        while (i < text.Length && Uri.IsHexDigit(text[i])) i++;
        var span = text.AsSpan(start, i - start);
        return span.Length == 0 ? [] : Convert.FromHexString(span.Length % 2 == 0 ? span : ("0" + span.ToString()));
    }

    private static string ReadString(string text, ref int i)
    {
        i++;
        var builder = new StringBuilder();
        while (true)
        {
            var c = text[i];
            if (c == '\\')
            {
                var next = text[i + 1];
                builder.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '0' => '\0', 'Z' => '\u001a', 'b' => '\b', _ => next });
                i += 2;
            }
            else if (c == '\'')
            {
                if (i + 1 < text.Length && text[i + 1] == '\'') { builder.Append('\''); i += 2; }
                else { i++; return builder.ToString(); }
            }
            else { builder.Append(c); i++; }
        }
    }
}
