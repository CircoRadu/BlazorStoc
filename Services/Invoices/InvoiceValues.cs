using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// Reading of the values found on invoices: numbers with either separator convention, dates in the usual formats, tax identifiers.
public static partial class InvoiceValues
{
    // Lower case, without diacritics, every other character than a letter or digit turned into a single space ("Nr. crt." -> "nr crt").
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            // Romanian letters that some encodings (legacy cedilla forms) leave undecomposed or that NFD leaves as is.
            var folded = character switch { 'ș' or 'ş' or 'Ș' or 'Ş' => 's', 'ț' or 'ţ' or 'Ț' or 'Ţ' => 't', 'ß' => 's', 'ø' or 'Ø' => 'o', _ => character };
            if (char.IsLetterOrDigit(folded))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(folded));
                pendingSpace = false;
            }
            else pendingSpace = true;
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"^[+-]?\d[\d .,' ]*$")]
    private static partial Regex NumberShape();

    [GeneratedRegex(@"^[\(\[]?\s*([+-]?\s*\d[\d .,' ]*)\s*[\)\]]?\s*(?:%|[A-Za-z]{2,4}\.?)?$")]
    private static partial Regex NumberWithSuffix();

    // decimalHint: '.' or ',' when the document's own numbers show which one is the decimal separator (see DecimalStyle), else null.
    public static decimal? ParseNumber(string? text, char? decimalHint = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim().Replace('−', '-');
        var match = NumberWithSuffix().Match(value);
        if (!match.Success) return null;
        value = match.Groups[1].Value.Replace(" ", "").Replace(" ", "").Replace("'", "");
        if (!NumberShape().IsMatch(value)) return null;
        var negative = value.StartsWith('-') || (text.TrimStart().StartsWith('(') && text.TrimEnd().EndsWith(')'));
        value = value.TrimStart('+', '-');
        var lastDot = value.LastIndexOf('.');
        var lastComma = value.LastIndexOf(',');
        char? decimalSeparator;
        if (lastDot >= 0 && lastComma >= 0) decimalSeparator = lastDot > lastComma ? '.' : ',';
        else if (lastDot < 0 && lastComma < 0) decimalSeparator = null;
        else
        {
            var separator = lastDot >= 0 ? '.' : ',';
            var count = value.Count(character => character == separator);
            if (count > 1) decimalSeparator = null;                               // 1.234.567: a thousands separator
            else if (decimalHint is { } hint && hint != separator) decimalSeparator = null; // the document uses the other one as decimal
            else decimalSeparator = separator;
        }
        string normalized;
        if (decimalSeparator is null) normalized = value.Replace(".", "").Replace(",", "");
        else
        {
            var index = value.LastIndexOf(decimalSeparator.Value);
            var integer = value[..index].Replace(".", "").Replace(",", "");
            normalized = integer + "." + value[(index + 1)..].Replace(".", "").Replace(",", "");
        }
        if (normalized.Length == 0 || normalized == ".") return null;
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return null;
        return negative ? -number : number;
    }

    // Which character the document writes decimals with, from the unambiguous numbers in it (two digits after the last separator,
    // or both separators present): null when the numbers do not tell.
    public static char? DecimalStyle(IEnumerable<string> texts)
    {
        int dot = 0, comma = 0;
        foreach (var text in texts)
        {
            var value = text.Trim();
            if (!NumberShape().IsMatch(value.TrimStart('-', '+'))) continue;
            var lastDot = value.LastIndexOf('.');
            var lastComma = value.LastIndexOf(',');
            if (lastDot >= 0 && lastComma >= 0) { if (lastDot > lastComma) dot++; else comma++; continue; }
            if (lastDot >= 0 && value.Length - lastDot - 1 is 1 or 2 && value.Count(c => c == '.') == 1) dot++;
            else if (lastComma >= 0 && value.Length - lastComma - 1 is 1 or 2 && value.Count(c => c == ',') == 1) comma++;
        }
        return dot == comma ? null : dot > comma ? '.' : ',';
    }

    public static bool LooksNumeric(string? text) => ParseNumber(text) is not null;

    [GeneratedRegex(@"(?<![\d])(\d{4})[-./](\d{1,2})[-./](\d{1,2})(?![\d])")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<![\d])(\d{1,2})[-./](\d{1,2})[-./](\d{4}|\d{2})(?![\d])")]
    private static partial Regex DayFirstDate();

    [GeneratedRegex(@"(?<![\d])(\d{1,2})\s*([A-Za-zÀ-ſ]{3,9})\.?\s*(\d{4})(?![\d])")]
    private static partial Regex NamedMonthDate();

    private static readonly string[] MonthPrefixes = ["ian", "feb", "mar", "apr", "mai", "iun", "iul", "aug", "sep", "oct", "noi", "dec"];
    private static readonly string[] EnglishMonthPrefixes = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (IsoDate().Match(text) is { Success: true } iso && TryDate(int.Parse(iso.Groups[1].Value), int.Parse(iso.Groups[2].Value), int.Parse(iso.Groups[3].Value), out var isoDate)) return isoDate;
        if (DayFirstDate().Match(text) is { Success: true } dayFirst)
        {
            var year = int.Parse(dayFirst.Groups[3].Value);
            if (year < 100) year += 2000;
            if (TryDate(year, int.Parse(dayFirst.Groups[2].Value), int.Parse(dayFirst.Groups[1].Value), out var date)) return date;
        }
        if (NamedMonthDate().Match(text) is { Success: true } named)
        {
            var month = Normalize(named.Groups[2].Value);
            var index = Array.FindIndex(MonthPrefixes, prefix => month.StartsWith(prefix, StringComparison.Ordinal));
            if (index < 0) index = Array.FindIndex(EnglishMonthPrefixes, prefix => month.StartsWith(prefix, StringComparison.Ordinal));
            if (index >= 0 && TryDate(int.Parse(named.Groups[3].Value), index + 1, int.Parse(named.Groups[1].Value), out var date)) return date;
        }
        return null;
    }

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (year is < 1990 or > 2100 || month is < 1 or > 12 || day is < 1 or > 31) return false;
        if (day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateOnly(year, month, day);
        return true;
    }

    [GeneratedRegex(@"^\s*(?:(?:cui|cif|cf|c\.i\.f\.|cod\s+fiscal|cod\s+tva|vat(?:\s+id)?|tva)\s*[:.]?\s*)?(?:RO)?\s*(\d{2,10})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CuiShape();

    // The digits of a Romanian tax identifier ("RO 22460883", "CUI: RO9178894" -> the digits); "" when the text is not one (a registry
    // number such as "J40/1/2020" is not).
    public static string NormalizeCui(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var match = CuiShape().Match(text);
        return match.Success ? match.Groups[1].Value.TrimStart('0') is { Length: >= 2 } digits ? digits : "" : "";
    }

    // The kind of value a text is, to pick how it is shown and validated.
    public static InvoiceValueKind KindOf(string? text, char? decimalHint = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return InvoiceValueKind.Text;
        if (ParseDate(text) is not null && text.Length <= 24) return InvoiceValueKind.Date;
        return ParseNumber(text, decimalHint) is not null ? InvoiceValueKind.Number : InvoiceValueKind.Text;
    }
}
