using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;

namespace BlazorStoc.Checks;

// Minimal PDF text extractor used only by the automated checks, so a generated report can be verified by its actual
// rendered text instead of trusting the code that produced it. PdfSharp renders plain ASCII with a simple WinAnsi
// font ("(text) Tj") and any text needing characters outside WinAnsi (Romanian diacritics) with an embedded
// Type0/CID font ("<hex> Tj"), decoded back to Unicode through that font's own /ToUnicode CMap. This is not a
// general-purpose PDF reader; it only understands what PdfSharp itself writes.
internal static class PdfTextExtractor
{
    public static string ExtractText(PdfPage page)
    {
        var fontMaps = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal);
        if (page.Resources?.Elements.GetDictionary("/Font") is { } fontDictionary)
            foreach (var key in fontDictionary.Elements.Keys)
                if (fontDictionary.Elements.GetDictionary(key)?.Elements.GetDictionary("/ToUnicode") is { } toUnicode)
                    fontMaps[key] = ParseToUnicodeCMap(Encoding.Latin1.GetString(toUnicode.Stream.UnfilteredValue));

        var builder = new StringBuilder();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
        {
            var content = Encoding.Latin1.GetString(page.Contents.Elements.GetDictionary(i)!.Stream.UnfilteredValue);
            AppendContentText(content, fontMaps, builder);
        }
        return builder.ToString();
    }

    private static void AppendContentText(string content, IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> fontMaps, StringBuilder builder)
    {
        IReadOnlyDictionary<int, string>? currentMap = null;
        foreach (Match token in Regex.Matches(content,
                     @"/(F\d+)\s+[\d.]+\s+Tf|\(((?:[^()\\]|\\.)*)\)\s*Tj|<([0-9A-Fa-f]+)>\s*Tj"))
        {
            if (token.Groups[1].Success) { fontMaps.TryGetValue("/" + token.Groups[1].Value, out currentMap); continue; }
            if (token.Groups[2].Success)
            {
                builder.Append(token.Groups[2].Value.Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\"));
                continue;
            }
            var hex = token.Groups[3].Value;
            for (var i = 0; i + 4 <= hex.Length; i += 4)
            {
                var code = Convert.ToInt32(hex.Substring(i, 4), 16);
                builder.Append(currentMap is not null && currentMap.TryGetValue(code, out var text) ? text : "?");
            }
        }
    }

    // "/BaseFont" per font resource key ("/F0", "/F1", …), so a test can tell which key is the bold face.
    public static IReadOnlyDictionary<string, string> FontBaseNames(PdfPage page)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (page.Resources?.Elements.GetDictionary("/Font") is { } fontDictionary)
            foreach (var key in fontDictionary.Elements.Keys)
                result[key] = fontDictionary.Elements.GetDictionary(key)?.Elements.GetName("/BaseFont") ?? "";
        return result;
    }

    // Every "/Fn size Tf" operator on the page, in order, so a test can confirm which font resource was used at
    // which size (headings at 14, everything else at 12).
    public static IReadOnlyList<(string FontKey, double Size)> FontUsage(PdfPage page)
    {
        var usage = new List<(string, double)>();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
        {
            var content = Encoding.Latin1.GetString(page.Contents.Elements.GetDictionary(i)!.Stream.UnfilteredValue);
            foreach (Match match in Regex.Matches(content, @"/(F\d+)\s+([\d.]+)\s+Tf"))
                usage.Add(("/" + match.Groups[1].Value, double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
        }
        return usage;
    }

    // Raw (decompressed) content stream text, for checks on drawing operators such as fill colour ("1 0 0 rg").
    public static string ExtractRawContent(PdfPage page)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
            builder.Append(Encoding.Latin1.GetString(page.Contents.Elements.GetDictionary(i)!.Stream.UnfilteredValue));
        return builder.ToString();
    }

    private static IReadOnlyDictionary<int, string> ParseToUnicodeCMap(string cmap)
    {
        var map = new Dictionary<int, string>();
        foreach (Match section in Regex.Matches(cmap, @"beginbfrange([\s\S]*?)endbfrange"))
            foreach (Match range in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
            {
                var start = Convert.ToInt32(range.Groups[1].Value, 16);
                var end = Convert.ToInt32(range.Groups[2].Value, 16);
                var unicodeStart = Convert.ToInt32(range.Groups[3].Value, 16);
                for (var code = start; code <= end; code++)
                    map[code] = char.ConvertFromUtf32(unicodeStart + (code - start));
            }
        foreach (Match section in Regex.Matches(cmap, @"beginbfchar([\s\S]*?)endbfchar"))
            foreach (Match pair in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                map[Convert.ToInt32(pair.Groups[1].Value, 16)] = char.ConvertFromUtf32(Convert.ToInt32(pair.Groups[2].Value, 16));
        return map;
    }
}
