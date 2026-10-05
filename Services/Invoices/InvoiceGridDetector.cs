namespace BlazorStoc.Services;

// Finds the line table of a page by its geometry alone: no dictionary of labels, no position, no number of columns is assumed.
//   1. The data lines: lines with at least two figures. Consecutive data lines whose figures sit under each other (the same right edges or centres)
//      are the rows of a table; the longest such run on the page, with a header over it, is the table.
//   2. The header: the lines of labels directly above the first data line, down from the top of the block (not the row of column numbers, not the
//      lines of text that belong to the first row), as far as the lines follow each other.
//   3. The columns: the cells between the vertical rules that cross the first row when the table is drawn with them, else the stretches of the page
//      that the words of the table do not cover (the gutters).
//   4. The meaning of a column is a proposal taken from its label when the label says something; the running numbers are recognised by their values.
// The result is a header band (HeaderBand) that the row reading of InvoiceTableReader continues from.
internal static class InvoiceGridDetector
{
    private const double MaxRowGap = 6.0;        // lines of height between two data lines of the same table (a row of several lines of text)
    private const double HeaderGap = 2.0;        // lines of height between two lines of the header
    private const double FirstGap = 4.2;         // lines of height between the header and the first row (or the row of column numbers)
    private const double Alignment = 6.0;        // points: the figures of two rows are under each other when their edges are this close
    private const double GutterMinimum = 3.5;    // points: the smallest empty stretch that separates two columns when there are no rules

    internal static HeaderBand? Find(InvoiceDocument document)
    {
        var bands = document.Pages.Select(FindOnPage).Where(band => band is not null).Select(band => band!).ToList();
        if (bands.Count == 0) return null;
        var best = bands.OrderByDescending(band => band.Score).First();
        // A table that goes on over several pages repeats its header: the table starts where its header is first found, not on the page that has the most rows.
        var labels = best.Cells.Select(cell => cell.Label).ToList();
        var earliest = bands.Where(band => band.Page < best.Page && InvoiceTableReader.SameLabels(labels, band.Cells.Select(cell => cell.Label).ToList())).OrderBy(band => band.Page).FirstOrDefault();
        return earliest ?? best;
    }

    private sealed record DataLine(int Index, TextLine Line, List<Segment> Numbers, List<Segment> All);

    internal static HeaderBand? FindOnPage(InvoicePageData page)
    {
        var lines = InvoiceLayout.BuildLines(page.Words);
        if (lines.Count < 3) return null;
        var segments = lines.Select((line, index) => InvoiceLayout.Segments(line, index)).ToList();
        var typical = Math.Max(2, TextLine.Median(lines.Select(line => line.Height)));

        var data = new List<DataLine>();
        for (var index = 0; index < lines.Count; index++)
        {
            var numbers = segments[index].Where(segment => InvoiceValues.LooksNumeric(segment.Text)).ToList();
            if (numbers.Count >= 2 && !IsNumbering(segments[index]) && lines[index].Height >= 0.5 * typical) data.Add(new DataLine(index, lines[index], numbers, segments[index]));
        }

        // The rows of a table: data lines that follow each other with their figures under each other.
        var chains = new List<List<DataLine>>();
        foreach (var line in data)
        {
            if (chains.Count > 0 && line.Line.Y - chains[^1][^1].Line.Y <= MaxRowGap * typical && Aligned(chains[^1][^1], line)) chains[^1].Add(line);
            else chains.Add([line]);
        }

        // A row whose figures the OCR read badly has fewer than two of them and was not a data line: it is still a row of the table when the figure that was
        // read lies under one of the table's (the first row of a scan is often like that), so the run is extended upwards over such lines.
        foreach (var chain in chains)
            for (var above = chain[0].Index - 1; above >= 0; above--)
            {
                if (lines[above].Height < 0.4 * typical) continue;
                var figures = segments[above].Where(segment => InvoiceValues.LooksNumeric(segment.Text) && segment.Text.Any(char.IsAsciiDigit)).ToList();
                var edges = chain.Take(4).SelectMany(item => item.Numbers).ToList();
                if (figures.Count == 0 || chain[0].Line.Y - lines[above].Y > MaxRowGap * typical || IsNumbering(segments[above]) ||
                    !figures.Any(figure => edges.Any(edge => Math.Abs(figure.Right - edge.Right) <= Alignment || Math.Abs((figure.X + figure.Right) / 2 - (edge.X + edge.Right) / 2) <= Alignment))) break;
                chain.Insert(0, new DataLine(above, lines[above], figures, segments[above]));
            }

        HeaderBand? best = null;
        foreach (var chain in chains)
            if (Candidate(page, lines, segments, chain, typical) is { } band && (best is null || band.Score > best.Score)) best = band;
        return best;
    }

    private static bool Aligned(DataLine left, DataLine right)
    {
        var matches = left.Numbers.Count(a => right.Numbers.Any(b => Math.Abs(a.Right - b.Right) <= Alignment || Math.Abs((a.X + a.Right) / 2 - (b.X + b.Right) / 2) <= Alignment));
        return matches >= Math.Max(2, (int)Math.Ceiling(0.6 * Math.Min(left.Numbers.Count, right.Numbers.Count)));
    }

    // The row of column numbers ("0 1 2 3 4 5 6", "8=(5 x 6)", "(445)*3", a lone "6"), whole or in the pieces an OCR gives it.
    private static bool IsNumbering(IReadOnlyList<Segment> line)
    {
        if (line.Count == 0) return false;
        var pattern = new System.Text.RegularExpressions.Regex(@"^[\(\)\d\s+*=x×./\-]+$");
        return line.All(segment => segment.Text.Trim().Length <= 18 && pattern.IsMatch(segment.Text.Trim()) && !System.Text.RegularExpressions.Regex.IsMatch(segment.Text, @"\d[.,]\d{2}\b")) &&
               (line.Count >= 3 || line.All(segment => segment.Text.Trim().Length <= 8));
    }

    private static HeaderBand? Candidate(InvoicePageData page, IReadOnlyList<TextLine> lines, IReadOnlyList<List<Segment>> segments, List<DataLine> chain, double typical)
    {
        var first = chain[0];
        var minX = chain.SelectMany(item => item.All).Min(segment => segment.X);
        var maxX = chain.SelectMany(item => item.All).Max(segment => segment.Right);
        bool InTable(Segment segment) => segment.Right >= minX - 8 && segment.X <= maxX + 8;

        // Where the header ends: above the row of column numbers when there is one (what lies between that row and the first figures is the first row's
        // text), above a rule drawn between the header and the first row, else directly above the first data line.
        var start = first.Index - 1;
        var numbering = -1;
        for (var index = first.Index - 1; index >= 0 && first.Line.Y - lines[index].Y <= 14 * typical; index--)
            if (IsNumbering(segments[index])) { numbering = index; break; }
        if (numbering >= 0) start = numbering - 1;
        else if (HorizontalRuleBetween(page, lines, start, first.Index, minX, maxX) is { } ruleAbove)
            while (start >= 0 && lines[start].Y > ruleAbove - 0.5 && lines[start].Y < first.Line.Y) start--;
        while (start >= 0 && IsNumbering(segments[start])) start--;   // the pieces of a row of column numbers

        // The rows start at the first line under the header: the text of the first row that lies above its figures is part of the rows.
        var bodyStart = first.Index;
        if (numbering >= 0) { bodyStart = numbering + 1; while (bodyStart < first.Index && IsNumbering(segments[bodyStart])) bodyStart++; }
        else if (start + 1 < first.Index && HorizontalRuleBetween(page, lines, first.Index - 1, first.Index, minX, maxX) is null) bodyStart = Math.Max(start + 1, first.Index - 0);
        var bodyEnd = chain[^1].Index;
        foreach (var segment in Enumerable.Range(bodyStart, bodyEnd - bodyStart + 1).SelectMany(index => segments[index]))
        {
            minX = Math.Min(minX, segment.X);
            maxX = Math.Max(maxX, segment.Right);
        }

        var header = new List<int>();
        for (var index = start; index >= 0 && header.Count < 8; index--)
        {
            var below = header.Count == 0 ? (numbering >= 0 ? lines[numbering] : first.Line) : lines[header[^1]];
            if (below.Y - lines[index].Y > (header.Count == 0 ? FirstGap : HeaderGap) * Math.Max(1, Math.Max(below.Height, lines[index].Height)) + (header.Count == 0 && numbering >= 0 ? 12 * typical : 0)) break;
            if (lines[index].Height < 0.4 * typical) continue;   // a speck the OCR took for a word
            var inTable = segments[index].Where(InTable).ToList();
            if (inTable.Count == 0 || IsNumbering(inTable) || inTable.Count(segment => InvoiceValues.LooksNumeric(segment.Text)) * 2 > inTable.Count) break;
            if (inTable.Sum(segment => segment.Words.Count) < segments[index].Sum(segment => segment.Words.Count) * 0.5) break;   // text that runs beside the table, not over it
            header.Add(index);
        }
        if (header.Count == 0) return null;
        header.Reverse();
        var headerSegments = header.SelectMany(index => segments[index]).Where(InTable).ToList();
        if (headerSegments.Count < 3) return null;

        // The columns.
        var bodyWords = Enumerable.Range(bodyStart, bodyEnd - bodyStart + 1).SelectMany(index => segments[index]).Where(InTable).SelectMany(segment => segment.Words).ToList();
        var allWords = headerSegments.SelectMany(segment => segment.Words).Concat(bodyWords).ToList();
        // The table is as wide as its header and its rows together (a column that has a label but no figures, like a running number left blank, is part of it).
        var tableLeft = Math.Min(minX, headerSegments.Min(segment => segment.X));
        var tableRight = Math.Max(maxX, headerSegments.Max(segment => segment.Right));
        var ruled = RuledBorders(page, lines[header[0]].Y, chain[0].Line.Y, chain[^1].Line.Y, tableLeft, tableRight);
        var edges = ruled ?? Gutters(allWords);
        if (edges is null || edges.Count < 3) return null;

        var cells = new List<HeaderCell>();
        var headerWords = headerSegments.SelectMany(segment => segment.Words).ToList();
        for (var column = 0; column + 1 < edges.Count; column++)
        {
            var left = edges[column];
            var right = edges[column + 1];
            var labelWords = headerWords.Where(word => word.CenterX >= left && word.CenterX < right).ToList();
            var bodyInside = bodyWords.Where(word => word.CenterX >= left && word.CenterX < right).ToList();
            if (labelWords.Count == 0 && bodyInside.Count == 0) continue;
            var label = "";
            foreach (var line in labelWords.GroupBy(word => header.FindIndex(index => lines[index].Words.Contains(word))).OrderBy(group => group.Key))
            {
                var text = string.Join(' ', line.OrderBy(word => word.X).Select(word => word.Text));
                label = label.EndsWith('-') ? label[..^1] + text : label.Length == 0 ? text : label + " " + text;   // a word broken at the end of a line
            }
            var (meaning, score) = InvoiceVocabulary.MatchColumn(label);
            var (closedMeaning, closedScore) = InvoiceVocabulary.MatchColumn(label.Replace(" ", ""));
            if (closedScore > score) { meaning = closedMeaning; score = closedScore; }
            var inset = ruled is null ? 0 : InvoiceTableReader.RuleInset;
            cells.Add(new HeaderCell(label, left + inset, right - inset, labelWords.Count == 0 ? lines[header[0]].Words.Min(word => word.Y) : labelWords.Min(word => word.Y),
                labelWords.Count == 0 ? lines[header[^1]].Words.Max(word => word.Bottom) : labelWords.Max(word => word.Bottom), meaning, score));
        }
        if (cells.Count < 3) return null;

        // The running numbers are recognised by their values: the first column that counts 1, 2, 3 ... down the rows.
        if (cells.All(cell => cell.Meaning != InvoiceColumnMeanings.Index))
        {
            var numbered = cells.FindIndex(cell => RunningNumbers(page, lines, segments, chain, cell));
            if (numbered >= 0) cells[numbered] = cells[numbered] with { Meaning = InvoiceColumnMeanings.Index, MatchScore = 1 };
        }

        var headerTop = header.SelectMany(index => lines[index].Words).Where(word => word.CenterX >= minX - 8 && word.CenterX <= maxX + 8).Min(word => word.Y);
        var headerBottom = header.SelectMany(index => lines[index].Words).Where(word => word.CenterX >= minX - 8 && word.CenterX <= maxX + 8).Max(word => word.Bottom);
        // The table of an invoice has amounts in several columns on every row: rows times the figures on a row, and what its header says.
        var figures = chain.Average(item => item.Numbers.Count);
        var score2 = chain.Count * Math.Min(8, figures) / 2 + cells.Count(cell => cell.Meaning.Length > 0 && cell.Meaning != InvoiceColumnMeanings.Ignore) + (ruled is null ? 0 : 2);
        return new HeaderBand(page.Number, cells, headerTop, headerBottom, score2);
    }

    // The y of the nearest long horizontal rule between two lines (the border between a header and the first row), or null.
    private static double? HorizontalRuleBetween(InvoicePageData page, IReadOnlyList<TextLine> lines, int upperIndex, int lowerIndex, double minX, double maxX)
    {
        if (page.Rules is not { Count: > 0 } rules || upperIndex < 0) return null;
        var top = lines[upperIndex].Y;
        var bottom = lines[lowerIndex].Y;
        var found = rules.Where(rule => !rule.Vertical && rule.Position > top && rule.Position < bottom && InvoiceLayout.Overlap(minX, maxX, rule.From, rule.To) >= 0.6 * (maxX - minX))
            .Select(rule => (double?)rule.Position).Max();
        return found;
    }

    // The vertical rules that cross the first row, as the edges of the columns (including the outer ones), or null when they do not frame the table.
    private static List<double>? RuledBorders(InvoicePageData page, double headerTop, double firstRowY, double lastRowY, double minX, double maxX)
    {
        if (page.Rules is not { Count: > 0 } rules) return null;
        // A border runs along the header or along a good part of the rows (a dotted rule is seen in pieces, a scan may show it only under the header).
        bool Runs(InvoiceRule rule) => Math.Min(rule.To, lastRowY + 4) - Math.Max(rule.From, headerTop - 4) >= Math.Min(35, 0.4 * Math.Max(1, lastRowY - headerTop));
        var positions = rules.Where(rule => rule.Vertical && rule.Position >= minX - 30 && rule.Position <= maxX + 30 && Runs(rule))
            .Select(rule => rule.Position).OrderBy(position => position).ToList();
        var edges = new List<double>();
        foreach (var position in positions) if (edges.Count == 0 || position - edges[^1] > 3) edges.Add(position);
        return edges.Count >= 4 && edges[0] <= minX + 10 && edges[^1] >= maxX - 10 ? edges : null;
    }

    // Without rules: the columns are the stretches of the page covered by the words of the table, the edges halfway through the empty stretches.
    private static List<double>? Gutters(IReadOnlyList<InvoiceWord> words)
    {
        if (words.Count == 0) return null;
        var clusters = new List<(double Left, double Right)>();
        foreach (var word in words.OrderBy(item => item.X))
        {
            if (clusters.Count > 0 && word.X - clusters[^1].Right < GutterMinimum) clusters[^1] = (clusters[^1].Left, Math.Max(clusters[^1].Right, word.Right));
            else clusters.Add((word.X, word.Right));
        }
        if (clusters.Count < 3) return null;
        var edges = new List<double> { clusters[0].Left - 2 };
        for (var index = 0; index + 1 < clusters.Count; index++) edges.Add((clusters[index].Right + clusters[index + 1].Left) / 2);
        edges.Add(clusters[^1].Right + 2);
        return edges;
    }

    // A column that counts the rows: its figures in the data lines are 1, 2, 3 ... (read as a scan reads them) for most of them.
    private static bool RunningNumbers(InvoicePageData page, IReadOnlyList<TextLine> lines, IReadOnlyList<List<Segment>> segments, List<DataLine> chain, HeaderCell cell)
    {
        var values = new List<int>();
        foreach (var item in chain.Take(12))
            foreach (var segment in segments[item.Index].Where(segment => (segment.X + segment.Right) / 2 >= cell.Left && (segment.X + segment.Right) / 2 <= cell.Right))
                if (InvoiceTableReader.ParseRunningNumber(segment.Text, values.Count + 1) is { } number) values.Add(number);
        return values.Count >= Math.Min(2, chain.Count) && values.Count >= chain.Take(12).Count() * 0.6 && values.Zip(values.Skip(1), (a, b) => b >= a).All(ok => ok) && values[0] <= 2;
    }
}
