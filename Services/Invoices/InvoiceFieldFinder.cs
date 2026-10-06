using System.Globalization;

namespace BlazorStoc.Services;

// Finds the label/value pairs of the invoice outside its line table: "Nr. factura  DIP44919", "Data emitere  2026-09-25", "Nume  Dipol
// Connect SRL" under the VANZATOR heading, totals whose label is above the value. A pair is found when the label is in the
// dictionary, ends with a colon, or simply has a value run to its right (the weaker the evidence, the lower the confidence).
internal static class InvoiceFieldFinder
{
    private sealed record Heading(int Page, string Section, double X, double Y);

    public static List<InvoiceHeaderField> Find(InvoiceDocument document, IReadOnlySet<InvoiceWord> excluded, char? hint)
    {
        var fields = new List<InvoiceHeaderField>();
        foreach (var page in document.Pages)
        {
            var words = page.Words.Where(word => !excluded.Contains(word)).ToList();
            var lines = InvoiceLayout.BuildLines(words);
            var segmentsByLine = lines.Select((line, index) => MergeLabelSegments(InvoiceLayout.Segments(line, index))).ToList();
            var headings = new List<Heading>();
            foreach (var segment in segmentsByLine.SelectMany(segments => segments))
                if ((InvoiceVocabulary.MatchSection(segment.Text) ?? InvoiceVocabulary.MatchSectionLead(segment.Text)) is { } section)
                    headings.Add(new Heading(page.Number, section, segment.X, segment.Y));

            var used = new HashSet<Segment>();
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var segments = segmentsByLine[lineIndex];
                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (used.Contains(segment) || InvoiceVocabulary.MatchSection(segment.Text) is not null) continue;
                    var next = i + 1 < segments.Count ? segments[i + 1] : null;
                    var found = TryPair(segment, next, lineIndex, segmentsByLine, lines, used, hint);
                    if (found is null) continue;
                    var (label, value, meaningKey, confidence, consumedNext) = found.Value;
                    if (consumedNext && next is not null) used.Add(next);
                    used.Add(segment);
                    var section = SectionOf(headings, label.Box);
                    // "Furnizor: SC TELESYSTEM SRL": the party word is the label and the rest the party's name.
                    if (meaningKey is null && InvoiceVocabulary.MatchSection(label.Text) is { } leadSection) { section = leadSection; meaningKey = "@name"; confidence = 0.9; }
                    var meaning = InvoiceVocabulary.ResolveFieldMeaning(meaningKey, section);
                    // A total is an amount: a sentence that happens to follow the words "de plata" (a note under the table) is not the total.
                    if (meaning is InvoiceFieldMeanings.Total or InvoiceFieldMeanings.TotalNet or InvoiceFieldMeanings.TotalVat && !System.Text.RegularExpressions.Regex.IsMatch(value.Text.Trim(), @"^[-+]?\d")) meaning = "";
                    // A total is the amount alone, without the currency written after it ("32.68 RON"), as a template reads it.
                    var valueText = meaning is InvoiceFieldMeanings.Total or InvoiceFieldMeanings.TotalNet or InvoiceFieldMeanings.TotalVat ? InvoiceValues.WithoutCurrency(value.Text) : value.Text;
                    fields.Add(new InvoiceHeaderField($"f{page.Number}_{fields.Count + 1}", label.Text, valueText, label.Box, value.Box,
                        meaning, section ?? "", confidence, InvoiceValues.KindOf(value.Text, hint)));
                }
            }
        }
        // The buyer's data (name, CUI, address, ...) is not read: the pickup needs the supplier and the invoice, and the buyer's tax id could be taken for the supplier's.
        return LimitSectionsToPartyBlocks(KeepBestPerMeaning(SeparateSeries(fields)))
            .Where(field => field.Section != InvoiceVocabulary.BuyerSection && InvoiceVocabulary.MatchSection(field.Label) != InvoiceVocabulary.BuyerSection).ToList();
    }

    // "Seria DC-FF-AV-TSY nr. 111094321": the label "seria" is the series, not the number. When the number follows on the same line behind "nr.",
    // it is the invoice number and the series stays a field without a meaning (the user can still use it).
    private static List<InvoiceHeaderField> SeparateSeries(List<InvoiceHeaderField> fields)
    {
        var result = fields.ToList();
        for (var i = 0; i < result.Count; i++)
        {
            var series = result[i];
            if (series.Meaning != InvoiceFieldMeanings.InvoiceNumber || InvoiceValues.Normalize(series.Label) is not ("seria" or "serie")) continue;
            var numberIndex = result.FindIndex(item => item.Meaning.Length == 0 && InvoiceValues.Normalize(item.Label) == "nr" && item.LabelBox.Page == series.ValueBox.Page
                && Math.Abs(item.LabelBox.Y - series.ValueBox.Y) <= Math.Max(series.ValueBox.Height, 4) && item.LabelBox.X >= series.ValueBox.X
                && System.Text.RegularExpressions.Regex.IsMatch(item.Value.Trim(), @"^\d{1,12}$"));
            if (numberIndex < 0) continue;
            result[i] = series with { Meaning = "", Confidence = Math.Min(series.Confidence, 0.6) };
            result[numberIndex] = result[numberIndex] with { Meaning = InvoiceFieldMeanings.InvoiceNumber, Confidence = 0.8 };
        }
        return result;
    }

    // A section is the block of lines under a VANZATOR/CUMPARATOR heading, not everything below it: invoice-level fields (number, dates,
    // totals) and any field under the last party line (totals, payment notes) do not belong to a party, even when they sit in its column.
    private static List<InvoiceHeaderField> LimitSectionsToPartyBlocks(List<InvoiceHeaderField> fields)
    {
        static bool IsParty(string meaning) => meaning.StartsWith(InvoiceVocabulary.SupplierSection + ".", StringComparison.Ordinal) ||
                                               meaning.StartsWith(InvoiceVocabulary.BuyerSection + ".", StringComparison.Ordinal);
        // The block runs from the heading down through party lines that follow each other (gap up to 30 points); a party line far below
        // (the bank account in the payment notes) keeps its party but does not stretch the block over the totals in between.
        // The buyer's lines have no meaning any more, but they still make up the buyer's block (they are removed after the limit).
        var bottoms = fields.Where(field => IsParty(field.Meaning) || (field.Section == InvoiceVocabulary.BuyerSection && field.Meaning.Length == 0))
            .GroupBy(field => (field.LabelBox.Page, field.Section))
            .ToDictionary(group => group.Key, group =>
            {
                var bottom = double.MinValue;
                foreach (var field in group.OrderBy(field => field.LabelBox.Y))
                {
                    if (bottom != double.MinValue && field.LabelBox.Y - bottom > 30) break;
                    bottom = Math.Max(bottom, field.LabelBox.Y + field.LabelBox.Height);
                }
                return bottom;
            });
        return fields.Select(field =>
        {
            if (field.Section.Length == 0 || IsParty(field.Meaning)) return field;
            var partyLine = field.Meaning.Length == 0 && bottoms.TryGetValue((field.LabelBox.Page, field.Section), out var bottom) && field.LabelBox.Y <= bottom + 4;
            return partyLine ? field : field with { Section = "" };
        }).ToList();
    }

    private sealed record Part(string Text, InvoiceBox Box);

    // Two neighbouring runs whose words together are a longer dictionary label ("VALOARE TOTALA" + "fara TVA") are one label: the gap
    // between the words of a label is not always smaller than the gap between a label and its value.
    private static List<Segment> MergeLabelSegments(List<Segment> segments)
    {
        var result = new List<Segment>();
        for (var i = 0; i < segments.Count; i++)
        {
            var current = segments[i];
            while (i + 1 < segments.Count)
            {
                var next = segments[i + 1];
                var tokens = InvoiceVocabulary.Tokens(current.Text);
                if (tokens.Length == 0 || InvoiceVocabulary.MatchFieldLabelPrefix(tokens).Words != tokens.Length) break;
                if (next.X - current.Right > 1.3 * Math.Max(1, current.Height)) break;
                var combined = InvoiceVocabulary.Tokens(current.Text + " " + next.Text);
                if (InvoiceVocabulary.MatchFieldLabelPrefix(combined).Words <= tokens.Length) break;
                current = new Segment { Words = [.. current.Words, .. next.Words], Line = current.Line };
                i++;
            }
            result.Add(current);
        }
        return result;
    }

    private static readonly HashSet<string> Connectors = ["fara", "tva", "cu", "de", "si", "la", "din", "pentru"];

    // A value is something with a digit or at least a real word: a stray "cu" or "fara TVA" is the rest of a label.
    private static bool IsPlausibleValue(string text)
    {
        var tokens = InvoiceVocabulary.Tokens(text);
        if (tokens.Length == 0) return false;
        if (text.Any(char.IsDigit)) return true;
        return tokens.Any(token => token.Length >= 3 && !Connectors.Contains(token));
    }

    private static (Part Label, Part Value, string? Meaning, double Confidence, bool ConsumedNext)? TryPair(Segment segment, Segment? next, int lineIndex,
        List<List<Segment>> segmentsByLine, List<TextLine> lines, HashSet<Segment> used, char? hint)
    {
        var tokens = InvoiceVocabulary.Tokens(segment.Text);
        if (tokens.Length == 0) return null;

        // 1) "Label: value" - the colon ends the label.
        var colonIndex = segment.Words.FindIndex(word => word.Text.EndsWith(':') || word.Text.Contains(':'));
        if (colonIndex >= 0 && colonIndex < segment.Words.Count)
        {
            var colonWord = segment.Words[colonIndex];
            var labelWords = segment.Words.Take(colonIndex).ToList();
            var colonText = colonWord.Text;
            var before = colonText[..colonText.IndexOf(':')];
            var after = colonText[(colonText.IndexOf(':') + 1)..];
            var valueWords = segment.Words.Skip(colonIndex + 1).ToList();
            // "Hunedoara, Cod fiscal: 9178894": a label that does not match as a whole but ends with one that does ("Cod fiscal") - the
            // words before it belong to the previous value on the line.
            var allLabelWords = labelWords.Select(word => word.Text).Append(before).Where(text => text.Length > 0).ToList();
            for (var skip = 0; skip < allLabelWords.Count - 1; skip++)
            {
                var whole = InvoiceVocabulary.Tokens(string.Join(' ', allLabelWords.Skip(skip)));
                if (whole.Length > 0 && InvoiceVocabulary.MatchFieldLabelPrefix(whole).Words == whole.Length)
                {
                    if (skip > 0) labelWords = labelWords.Skip(skip).ToList();
                    break;
                }
                if (skip == 0 && InvoiceVocabulary.MatchFieldLabelPrefix(InvoiceVocabulary.Tokens(string.Join(' ', allLabelWords))).Words > 0) break;
            }
            var labelText = string.Join(' ', labelWords.Select(word => word.Text).Append(before).Where(text => text.Length > 0));
            if (labelText.Length > 0 && labelText.Split(' ').Length <= 6)
            {
                var labelBoxWords = labelWords.Count > 0 ? labelWords : [colonWord];
                string valueText;
                InvoiceBox? valueBox = null;
                if (after.Length > 0 || valueWords.Count > 0)
                {
                    valueText = string.Join(' ', (after.Length > 0 ? new[] { after } : []).Concat(valueWords.Select(word => word.Text)));
                    valueBox = valueWords.Count > 0 ? InvoiceLayout.Union(valueWords) : colonWord is { } cw ? new InvoiceBox(cw.Page, cw.X + cw.Width * 0.3, cw.Y, cw.Width * 0.7, cw.Height) : null;
                }
                else if (next is not null && !used.Contains(next)) { valueText = next.Text; valueBox = next.Box; }
                else return null;
                if (valueBox is not null)
                {
                    var (words, meaning) = InvoiceVocabulary.MatchFieldLabelPrefix(InvoiceVocabulary.Tokens(labelText));
                    return (new Part(labelText, InvoiceLayout.Union(labelBoxWords)), new Part(valueText, valueBox), words > 0 ? meaning : null, words > 0 ? 1.0 : 0.6,
                        after.Length == 0 && valueWords.Count == 0);
                }
            }
        }

        // 2) A dictionary label at the start of the run: the rest of the run is the value, else the next run, else the run below.
        var (labelTokens, labelMeaning) = InvoiceVocabulary.MatchFieldLabelPrefix(tokens);
        if (labelTokens > 0)
        {
            var split = SplitWords(segment.Words, labelTokens);
            if (split is not null)
            {
                var (labelWords, valueWords) = split.Value;
                var confidence = labelTokens >= 2 ? 1.0 : 0.85;
                if (valueWords.Count > 0 && !LooksLikeLabel(InvoiceVocabulary.Tokens(string.Join(' ', valueWords.Select(word => word.Text)))) && IsPlausibleValue(string.Join(' ', valueWords.Select(word => word.Text))))
                    return (new Part(string.Join(' ', labelWords.Select(word => word.Text)), InvoiceLayout.Union(labelWords)),
                        new Part(string.Join(' ', valueWords.Select(word => word.Text)), InvoiceLayout.Union(valueWords)), labelMeaning, confidence, false);
                if (valueWords.Count == 0)
                {
                    if (next is not null && !used.Contains(next) && !LooksLikeLabel(InvoiceVocabulary.Tokens(next.Text)) && IsPlausibleValue(next.Text) &&
                        InvoiceVocabulary.MatchFieldLabelPrefix(InvoiceVocabulary.Tokens(next.Text)).Words == 0)
                        return (new Part(segment.Text, segment.Box), new Part(next.Text, next.Box), labelMeaning, confidence, true);
                    var below = ValueBelow(segment, lineIndex, segmentsByLine, lines, used, hint);
                    if (below is not null) return (new Part(segment.Text, segment.Box), new Part(below.Text, below.Box), labelMeaning, confidence * 0.9, false);
                }
            }
        }
        else if (next is not null && !used.Contains(next) && segment.Words.Count <= 4 && !tokens.Any(token => token.Any(char.IsAsciiDigit)) &&
                 next.X - segment.Right <= 25 * Math.Max(1, segment.Height) && !LooksLikeLabel(InvoiceVocabulary.Tokens(next.Text)) &&
                 InvoiceVocabulary.MatchSection(next.Text) is null)
        {
            // Nothing known about the label: a short text followed by another run on the same line.
            return (new Part(segment.Text, segment.Box), new Part(next.Text, next.Box), null, 0.3, true);
        }
        return null;
    }

    // A run that is a dictionary label, or one followed only by connecting words ("VALOARE TOTALA cu": the rest of the label is on the next line).
    private static bool LooksLikeLabel(string[] tokens)
    {
        if (tokens.Length == 0) return false;
        var words = InvoiceVocabulary.MatchFieldLabelPrefix(tokens).Words;
        return words > 0 && tokens.Skip(words).All(Connectors.Contains);
    }

    // The first `tokenCount` normalised tokens of a run as a group of words (the label) and the words after them (the value).
    private static (List<InvoiceWord> Label, List<InvoiceWord> Value)? SplitWords(List<InvoiceWord> words, int tokenCount)
    {
        var count = 0;
        for (var index = 0; index < words.Count; index++)
        {
            count += InvoiceVocabulary.Tokens(words[index].Text).Length;
            if (count == tokenCount) return (words.Take(index + 1).ToList(), words.Skip(index + 1).ToList());
            if (count > tokenCount) return null;
        }
        return null;
    }

    // A label with its value on the line under it (the totals block): the nearest run below, within two lines, that overlaps the label
    // horizontally and holds a number.
    private static Segment? ValueBelow(Segment label, int lineIndex, List<List<Segment>> segmentsByLine, List<TextLine> lines, HashSet<Segment> used, char? hint)
    {
        for (var below = lineIndex + 1; below <= Math.Min(lineIndex + 3, segmentsByLine.Count - 1); below++)
        {
            if (lines[below].Y - lines[lineIndex].Y > 4.5 * Math.Max(1, label.Height)) break;
            Segment? best = null;
            var bestOverlap = 0.0;
            foreach (var candidate in segmentsByLine[below])
            {
                if (used.Contains(candidate) || InvoiceValues.ParseNumber(candidate.Text, hint) is null) continue;
                var overlap = InvoiceLayout.Overlap(label.X, label.Right, candidate.X, candidate.Right);
                if (overlap > bestOverlap) { bestOverlap = overlap; best = candidate; }
            }
            if (best is not null) return best;
        }
        return null;
    }

    private static string? SectionOf(List<Heading> headings, InvoiceBox label)
    {
        Heading? best = null;
        foreach (var heading in headings.Where(item => item.Page == label.Page && item.Y <= label.Y + 1))
        {
            // The heading spans from its own x to the next heading on the same row (or the page's right edge).
            var right = headings.Where(item => item.Page == heading.Page && item.X > heading.X + 5 && Math.Abs(item.Y - heading.Y) < 12)
                .Select(item => item.X).DefaultIfEmpty(double.MaxValue).Min();
            // With another party's heading on the same row, the party's own column is the left part of that span: what starts in its
            // right part (the invoice number/date block that many invoices put between the two parties) belongs to neither.
            var limit = right == double.MaxValue ? right : heading.X + 0.6 * (right - heading.X);
            if (label.X < heading.X - 8 || label.X >= limit) continue;
            if (best is null || heading.Y > best.Y) best = heading;
        }
        return best?.Section;
    }

    // One field per meaning: the most certain one, the first in reading order; the others stay as fields without a meaning.
    private static List<InvoiceHeaderField> KeepBestPerMeaning(List<InvoiceHeaderField> fields)
    {
        var result = new List<InvoiceHeaderField>();
        foreach (var group in fields.GroupBy(field => field.Meaning))
        {
            if (group.Key.Length == 0 || group.Key == InvoiceFieldMeanings.Custom) { result.AddRange(group); continue; }
            var best = group.OrderByDescending(field => field.Confidence).ThenBy(field => field.LabelBox.Page).ThenBy(field => field.LabelBox.Y).First();
            result.Add(best);
            result.AddRange(group.Where(field => field != best).Select(field => field with { Meaning = "" }));
        }
        return result.OrderBy(field => field.LabelBox.Page).ThenBy(field => Math.Round(field.LabelBox.Y / 4)).ThenBy(field => field.LabelBox.X)
            .Select((field, index) => field with { Id = "f" + (index + 1).ToString(CultureInfo.InvariantCulture) }).ToList();
    }
}
