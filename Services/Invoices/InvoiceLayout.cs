namespace BlazorStoc.Services;

// A line of text: the words of a page whose centres are at about the same height, left to right.
internal sealed class TextLine
{
    public required int Page { get; init; }
    public required List<InvoiceWord> Words { get; init; }
    public double Y => Words.Average(word => word.CenterY);
    public double Height => Median(Words.Select(word => word.Height));
    public double Left => Words.Min(word => word.X);
    public double Right => Words.Max(word => word.Right);
    public string Text => string.Join(' ', Words.Select(word => word.Text));

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}

// A run of words on a line with only word-sized gaps between them: a label, a value, a cell.
internal sealed class Segment
{
    public required List<InvoiceWord> Words { get; init; }
    public required int Line { get; init; }
    public double X => Words.Min(word => word.X);
    public double Right => Words.Max(word => word.Right);
    public double Y => Words.Average(word => word.Y);
    public double CenterY => Words.Average(word => word.CenterY);
    public double Height => TextLine.Median(Words.Select(word => word.Height));
    public string Text => string.Join(' ', Words.Select(word => word.Text));
    public InvoiceBox Box => InvoiceLayout.Union(Words);
}

internal static class InvoiceLayout
{
    // Words whose centres are within this share of the smaller word height are on the same line.
    private const double LineTolerance = 0.55;
    // Two words of a line belong to the same segment when the gap between them is at most this share of the text height
    // (an ordinary space is about a third of it; columns and label/value separations are far wider).
    public const double SegmentGap = 0.6;

    public static List<TextLine> BuildLines(IEnumerable<InvoiceWord> words)
    {
        var ordered = words.OrderBy(word => word.Page).ThenBy(word => word.CenterY).ThenBy(word => word.X).ToList();
        var lines = new List<List<InvoiceWord>>();
        foreach (var word in ordered)
        {
            var current = lines.Count == 0 ? null : lines[^1];
            if (current is not null && current[0].Page == word.Page)
            {
                var centre = current.Average(item => item.CenterY);
                // The tolerance follows the height of the line's own text, not of the new word: a hyphen or a dot is tiny and sits low.
                var height = TextLine.Median(current.Select(item => item.Height));
                if (Math.Abs(word.CenterY - centre) <= LineTolerance * Math.Max(1, height)) { current.Add(word); continue; }
            }
            lines.Add([word]);
        }
        return lines.Select(list => new TextLine { Page = list[0].Page, Words = list.OrderBy(word => word.X).ToList() }).ToList();
    }

    public static List<Segment> Segments(TextLine line, int lineIndex, double gapFactor = SegmentGap)
    {
        var result = new List<Segment>();
        List<InvoiceWord>? current = null;
        var limit = Math.Max(1, line.Height) * gapFactor;
        foreach (var word in line.Words)
        {
            if (current is not null && word.X - current[^1].Right <= limit) { current.Add(word); continue; }
            current = [word];
            result.Add(new Segment { Words = current, Line = lineIndex });
        }
        return result;
    }

    public static InvoiceBox Union(IReadOnlyCollection<InvoiceWord> words)
    {
        var left = words.Min(word => word.X);
        var top = words.Min(word => word.Y);
        var right = words.Max(word => word.Right);
        var bottom = words.Max(word => word.Bottom);
        return new InvoiceBox(words.First().Page, left, top, right - left, bottom - top);
    }

    // Reading-order text of some words: line by line, each line left to right.
    public static string TextOf(IEnumerable<InvoiceWord> words) =>
        string.Join(' ', BuildLines(words).Select(line => line.Text));

    public static double Overlap(double leftA, double rightA, double leftB, double rightB) =>
        Math.Max(0, Math.Min(rightA, rightB) - Math.Max(leftA, leftB));
}
