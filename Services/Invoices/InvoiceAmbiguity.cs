namespace BlazorStoc.Services;

// Characters other than letters and digits that text recognition (OCR) makes out of a stroke or a stain of a letter or a digit, and that a product line
// of an invoice hardly ever has: a value that holds one of them was probably read wrongly and is marked for the user to check.
// The usual punctuation of a line ( - . , / : ( ) + % " ' & # * x ) is left out on purpose: it is real so often that a mark on it would be noise.
public static class InvoiceAmbiguity
{
    // What each one stands for in the picture (kept next to the characters, as the message names them):
    //   | ¦ !  ¡      a vertical stroke: the letters I and l, the digits 1 and 7, a border line of the table that was read as text
    //   [ ] { }       a bracket: a parenthesis, the letters I, J, T, the digit 1
    //   ` ´           an accent or a speck: an apostrophe, the digit 1, a comma, a dot
    //   ~ ^ ¬ _       a smudge or a rule: a dash, an underline of the table, a stain above or below the line
    //   § ¤ ¢ £ ¥     a curly letter or digit: S, 5, 8, C, E, Y, 2
    //   © ® ™         a round letter: C, O, G, 0
    //   \             a slanted stroke: a slash, the digit 1, the letter l
    public const string Characters = "|¦!¡[]{}`´~^¬_§¤¢£¥©®™\\";

    public const string FlagPrefix = "Caractere nesigure din OCR";

    // The distinct ambiguous characters of a text, in the order they appear ("" when there are none).
    public static string Found(string? text) => string.IsNullOrEmpty(text) ? "" : new string(text.Where(Characters.Contains).Distinct().ToArray());

    public static string Flag(string characters) => $"{FlagPrefix} ({characters}): verifică valorile rândului";

    // The rows read from a page that was recognised from its picture get a flag when a cell holds one of the characters; a page with real text is trusted.
    public static List<InvoiceTableRow> Mark(List<InvoiceTableRow> rows, InvoiceDocument document)
    {
        var ocrPages = document.Pages.Where(page => page.Source == InvoiceSources.Ocr).Select(page => page.Number).ToHashSet();
        if (ocrPages.Count == 0) return rows;
        return [.. rows.Select(row =>
        {
            if (!ocrPages.Contains(row.Page)) return row;
            var found = Found(string.Concat(row.Cells.Values));
            return found.Length == 0 ? row : row with { Flags = [.. row.Flags, Flag(found)] };
        })];
    }
}
