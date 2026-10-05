using BlazorStoc.Services;
using OpenCvSharp;

namespace BlazorStoc.Checks;

// The table grid as a scan gives it: faint or dotted rules found in pieces and slightly tilted, a barcode that looks like rules, a header that breaks
// onto two lines with an OCR error in one of them, and notes under the table that must not stretch its columns.
public static class GridChecks
{
    public static void Run(Action<bool, string> check)
    {
        Console.WriteLine("=== Table grid ===");

        // The pieces of one rule that drifts sideways down the page (a crooked scan) are one rule, even with gaps; unrelated rules are not joined.
        var joined = InvoicePdfReader.JoinTilted(
        [
            new InvoiceRule(true, 392.3, 299.5, 418.8), new InvoiceRule(true, 391.6, 453.6, 479.8), new InvoiceRule(true, 390.9, 500, 560),
            new InvoiceRule(true, 420.0, 300, 400), new InvoiceRule(true, 395.0, 300, 330), new InvoiceRule(false, 300.0, 18, 584)
        ]);
        var tilted = joined.Where(rule => rule.Vertical && rule.Position > 389 && rule.Position < 393).ToList();
        check(tilted.Count == 1 && tilted[0].From == 299.5 && tilted[0].To == 560 && Math.Abs(tilted[0].Position - 392.3) < 0.6,
            "Table grid: pieces of one tilted dotted rule, with gaps between them, are joined into one rule");
        check(joined.Any(rule => rule.Vertical && Math.Abs(rule.Position - 420) < 0.1) && joined.Count(rule => rule.Vertical) == 3 && joined.Count(rule => !rule.Vertical) == 1,
            "Table grid: rules that do not lie on the same line are not joined");

        // The row of column numbers as OCR read it ("0 1 2 3 4 5 6" -> "o 1 = a i. 5 6") is still that row; a row of goods is not.
        check(InvoiceTableReader.IsColumnNumberRow(["o", "1", "=", "a", "i.", "5", "6"]) && InvoiceTableReader.IsColumnNumberRow(["0", "1", "2", "3", "4", "5", "6"]) &&
              !InvoiceTableReader.IsColumnNumberRow(["1.", "Compot Mere", "buc", "1,00", "32,00", "32,00", "6,08"]) && !InvoiceTableReader.IsColumnNumberRow(["o", "1", "Compot"]),
            "Table grid: a row of column numbers with the figures misread by OCR is recognised, a row of goods is not");

        // Numbers read again cell by cell: which cells, how the line of text is cut out, what is taken off the ends.
        check(!InvoicePdfReader.NeedsReread("1,970.60", false) && !InvoicePdfReader.NeedsReread("19 %", false) && !InvoicePdfReader.NeedsReread("-", false) && !InvoicePdfReader.NeedsReread("38.33 |", false) &&
              !InvoicePdfReader.NeedsReread("", false) && InvoicePdfReader.NeedsReread("", true) && InvoicePdfReader.NeedsReread("ear tet", false) && InvoicePdfReader.NeedsReread("58 32", false) && InvoicePdfReader.NeedsReread("32 DD", false),
            "Number rereading: a cell that is a number (even with a scrap of border at its end) is left alone; text, a number with a gap and an empty cell of a column that always has one are read again");
        check(InvoicePdfReader.Tidy("38.33 |") == "38.33" && InvoicePdfReader.Tidy("437.06,") == "437.06" && InvoicePdfReader.Tidy("ear tet") == "ear tet" && InvoicePdfReader.Tidy("|") == "|",
            "Number rereading: the scraps of a border at the ends of a number are taken off, a text is not changed");
        check(InvoicePdfReader.WithoutBorderDigit("942.121", ["197.06", "512.06", "437.06"]) == "942.12" && InvoicePdfReader.WithoutBorderDigit("481.2701", ["481.2700", "2.0000"]) == "481.2701" &&
              InvoicePdfReader.WithoutBorderDigit("12.341", ["1.5"]) == "12.341",
            "Number rereading: a third decimal that is the border is dropped only when the rest of the column has two decimals");
        using (var cell = new Mat(70, 220, MatType.CV_8UC1, Scalar.White))
        {
            Cv2.Line(cell, new OpenCvSharp.Point(0, 3), new OpenCvSharp.Point(219, 3), Scalar.Black, 1);                       // a scrap of the rule above
            Cv2.Rectangle(cell, new OpenCvSharp.Rect(70, 22, 90, 26), Scalar.Black, -1);                                      // the figures of the cell
            Cv2.Rectangle(cell, new OpenCvSharp.Rect(70, 66, 90, 4), Scalar.Black, -1);                                       // the top of the next row, cut by the edge
            Cv2.Rectangle(cell, new OpenCvSharp.Rect(214, 15, 3, 40), Scalar.Black, -1);                                      // a dotted vertical rule at the right, apart from the figures
            var line = InvoicePdfReader.TightLine(cell);
            check(line is { } box && box.Y >= 12 && box.Y <= 22 && box.Bottom >= 48 && box.Bottom <= 58 && box.X >= 60 && box.Right <= 170,
                "Number rereading: the line of text of a cell is cut out without the scrap of the rule above, the top of the next row or the border at the side");
            using var empty = new Mat(70, 220, MatType.CV_8UC1, Scalar.White);
            check(InvoicePdfReader.TightLine(empty) is null, "Number rereading: a cell with no ink has no line");
        }

        // A barcode: bars of the same height two points apart are not borders.
        var bars = Enumerable.Range(0, 12).Select(index => new InvoiceRule(true, 374 + index * 2.5, 28.6, 54.7)).ToList();
        List<InvoiceRule> withBars = [.. bars, new InvoiceRule(true, 45, 300, 650), new InvoiceRule(true, 323, 300, 650)];
        var cleaned = InvoicePdfReader.RemoveBarcodeLikeRules(withBars);
        check(cleaned.Count == 2 && cleaned.All(rule => rule.To == 650), "Table grid: the bars of a barcode are not taken for the borders of a table");
        check(InvoicePdfReader.RemoveBarcodeLikeRules([new InvoiceRule(true, 45, 300, 650), new InvoiceRule(true, 47, 300, 650), new InvoiceRule(true, 323, 300, 650)]).Count == 3,
            "Table grid: a few parallel borders are kept");

        // Dotted rules on a light grey scan: found, with the text painted out so that its strokes are not rules.
        using var page = new Mat(1200, 800, MatType.CV_8UC1, Scalar.White);
        for (var y = 200; y < 900; y += 6) { page.Rectangle(new Point(300, y), new Point(301, y + 2), new Scalar(185), -1); }   // a dotted vertical line
        for (var x = 100; x < 700; x += 6) { page.Rectangle(new Point(x, 950), new Point(x + 2, 951), new Scalar(185), -1); }     // a dotted horizontal line
        for (var y = 400; y < 520; y += 4) page.Rectangle(new Point(500, y), new Point(501, y + 3), new Scalar(60), -1);           // strokes of a letter column (inside a word box)
        var found = InvoicePdfReader.FindFaintLines(page, [new InvoiceWord(1, "Nr", 490, 395, 30, 130, 0)]);
        check(found.Any(item => item.Vertical && Math.Abs(item.Box.X - 300) <= 3 && item.Box.Height >= 600) && found.Any(item => !item.Vertical && Math.Abs(item.Box.Y - 950) <= 3 && item.Box.Width >= 450),
            "Table grid: faint dotted vertical and horizontal rules are found");
        check(!found.Any(item => item.Vertical && Math.Abs(item.Box.X - 500) <= 6), "Table grid: the strokes of text are not taken for rules");
        using var blank = new Mat(1200, 800, MatType.CV_8UC1, Scalar.White);
        check(InvoicePdfReader.FindFaintLines(blank, []).Count == 0, "Table grid: a clean page has no faint rules");

        // Where a drawn grid ends: the borders that cross the header's bottom edge end together.
        var grid = new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, [], [new InvoiceRule(true, 34, 246, 342.7), new InvoiceRule(true, 44, 246, 342.7), new InvoiceRule(true, 280, 246, 343.0), new InvoiceRule(true, 332, 246, 342.7)]);
        InvoiceColumn[] columns = [new("c1", "a", InvoiceColumnMeanings.Name, 35, 100), new("c2", "b", InvoiceColumnMeanings.Quantity, 280, 340)];
        check(Math.Abs((InvoiceTableReader.TableBottomFromRules(grid, columns, 284) ?? 0) - 342.7) < 0.5, "Table grid: where the borders of the header end together is the bottom of the table");
        var broken = grid with { Rules = [new InvoiceRule(true, 34, 246, 410), new InvoiceRule(true, 44, 246, 470), new InvoiceRule(true, 280, 246, 360), new InvoiceRule(true, 332, 246, 640)] };
        check(InvoiceTableReader.TableBottomFromRules(broken, columns, 284) is null, "Table grid: borders that end at different places say nothing about where the table ends");

        // A scanned invoice with a dotted grid: columns from the borders, a header of two lines (one label garbled by the OCR), rows by running numbers,
        // and a note under the table that does not stretch the columns.
        double[] borders = [18, 45, 323, 353, 392, 456, 519, 582];
        var order = 0;
        InvoiceWord Word(string text, double x, double y, double width = 0) => new(1, text, x, y, width > 0 ? width : 5.2 * text.Length, 8, order++);
        List<InvoiceWord> words =
        [
            Word("NR.", 22, 306), Word("DENUMIREA", 120, 306), Word("PRODUSELOR", 180, 306), Word("U.M.", 328, 306), Word("CANTI-", 358, 306), Word("PRET", 400, 306), Word("UNITAR", 425, 306),
            Word("VALOAREA", 462, 306), Word("VAL.TVA", 530, 306),
            Word("CRT.", 22, 320), Word("ITATEA.", 358, 320), Word("(FARA", 400, 320), Word("TVA)", 425, 320), Word("-", 470, 320, 3), Word("LEI", 476, 320), Word("-", 530, 320, 3), Word("LEI-", 538, 320)
        ];
        (string Index, string Name, string Unit, string Quantity, string Price, string Value, string Vat)[] data =
        [
            ("1", "M25 Centrala", "BUC", "1", "942.12", "942.12", "197.85"), ("2", "M2 PMD5M Motion detector", "BUC", "10", "197.06", "1,970.60", "413.83"),
            ("3", "M2 SR 230 M Outdoor siren", "BUC", "1", "512.06", "512.06", "107.53"), ("4", "M2 K38M Keypad Wireless", "BUC", "1", "437.06", "437.06", "91.78")
        ];
        for (var row = 0; row < data.Length; row++)
        {
            var y = 345.0 + row * 15;
            words.AddRange([Word(data[row].Index, 28, y, 5), Word(data[row].Name, 50, y), Word(data[row].Unit, 330, y), Word(data[row].Quantity, 380, y, 5), Word(data[row].Price, 420, y), Word(data[row].Value, 480, y), Word(data[row].Vat, 545, y)]);
        }
        words.Add(Word("Societatea noastra declara pe propria raspundere ca produsele din prezenta factura corespund declaratiilor", 18, 480, 420));   // a note under the table
        var rules = new List<InvoiceRule> { new(false, 299.9, 17.8, 584.2) };
        // The borders are found in pieces: those at the left and the middle only under the header, the others crossing it.
        foreach (var (x, from, to) in new[] { (18.0, 336.0, 420.0), (45.0, 336.0, 420.0), (323.0, 336.0, 420.0), (353.0, 345.0, 420.0), (392.3, 299.5, 418.8), (455.5, 308.9, 403.4), (519.0, 299.5, 379.7), (581.6, 310.3, 421.9) })
            rules.Add(new InvoiceRule(true, x, from, to));
        rules.AddRange([new InvoiceRule(true, 455.0, 435, 567), new InvoiceRule(true, 518.3, 395.8, 475)]);
        var document = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, words, rules)]);
        var (table, _) = InvoiceTableReader.Detect(document, '.');
        check(table is not null && table.Columns.Count == 7 && Enumerable.Range(0, 7).All(index => Math.Abs(table.Columns[index].Left - (borders[index] + 1.5)) < 2.5 && Math.Abs(table.Columns[index].Right - (borders[index + 1] - 1.5)) < 2.5),
            "Table grid: the columns are the boxes between the borders found, also those found only under the header");
        check(table is not null && table.HeaderBottom >= 327 && table.HasIndexColumn && table.Rows.Count == 4 && table.Rows.Select(row => row.Number).SequenceEqual([1, 2, 3, 4]),
            "Table grid: a header of two lines is whole (the garbled label of the second does not cut it), and the rows follow the running numbers");
        check(table is not null && table.Rows.All(row => row.Cells.Values.All(text => !text.Contains("Societatea"))) && table.Columns.Max(column => column.Right) < 583,
            "Table grid: a note under the table is neither a row nor a reason to stretch a column");

        GeometricEngineChecks(check);
        RuledNotesChecks(check);
        NumberRowContinuationChecks(check);
        NumberRowSeparatorChecks(check);
        PickupColumnsChecks(check);
    }

    // A table with two header lines: the labels, and under them the number of each column ("0 1 2 3 4 5(3x4) 6"). A following page may repeat only the second one (to save
    // room): the table goes on there, and the numbers say where each column is (the page may be shifted, as a scan is).
    private static void NumberRowContinuationChecks(Action<bool, string> check)
    {
        var order = 0;
        InvoiceWord Word(int page, string text, double x, double y, double width = 0) => new(page, text, x, y, width > 0 ? width : 5.2 * text.Length, 7, order++);
        double[] xs = [62, 100, 215, 275, 340, 425, 495];
        string[] labels = ["Nr crt", "Denumire produs", "U.M.", "Cantitatea", "Pretul unitar", "Valoarea", "T.V.A"];
        string[] numbers = ["0", "1", "2", "3", "4", "5(3x4)", "6"];
        (string Index, string Name, string Unit, string Quantity, string Price, string Value, string Vat)[] first =
            [("1.", "Buchet Lalele", "Buc", "10", "36.30", "363.00", "87.12"), ("2.", "Trandafiri rosii", "Buc", "5", "12.50", "62.50", "15.00")];
        (string Index, string Name, string Unit, string Quantity, string Price, string Value, string Vat)[] second =
            [("3.", "Garoafe albe", "Buc", "20", "3.00", "60.00", "14.40"), ("4.", "Frezii galbene", "Buc", "2", "15.00", "30.00", "7.20")];
        var tableWidth = new[] { 60.0, 120, 30, 60, 50, 50, 60 };
        List<InvoiceWord> Page(int number, double shift, bool header, double top, (string Index, string Name, string Unit, string Quantity, string Price, string Value, string Vat)[] rows)
        {
            var words = new List<InvoiceWord>();
            var y = top;
            if (header)
            {
                for (var column = 0; column < 7; column++) words.Add(Word(number, labels[column], xs[column] + shift, y));
                y += 24;
            }
            for (var column = 0; column < 7; column++) words.Add(Word(number, numbers[column], xs[column] + shift + 2, y, numbers[column].Length * 4.5));
            y += 16;
            foreach (var row in rows)
            {
                string[] cells = [row.Index, row.Name, row.Unit, row.Quantity, row.Price, row.Value, row.Vat];
                for (var column = 0; column < 7; column++) words.Add(Word(number, cells[column], xs[column] + shift + (column >= 3 ? 8 : 0), y, column >= 3 ? 4.5 * cells[column].Length : 0));
                y += 16;
            }
            return words;
        }
        foreach (var shift in new[] { 0.0, 14.0, 45.0 })
        {
            var document = new InvoiceDocument([
                new InvoicePageData(1, 595, 842, InvoiceSources.Text, Page(1, 0, true, 247, first)),
                new InvoicePageData(2, 595, 842, InvoiceSources.Text, Page(2, shift, false, 60, second))]);
            var table = InvoiceTableReader.Detect(document, '.').Table;
            var rows = table?.Rows ?? [];
            string Cell(int row, string meaning) => table is null || row >= rows.Count ? "" : (table.Columns.FirstOrDefault(column => column.Meaning == meaning) is { } found ? rows[row].Cells.GetValueOrDefault(found.Id, "") : "");
            check(table is not null && rows.Count == 4 && rows.Select(row => row.Number).SequenceEqual([1, 2, 3, 4]) && rows.All(row => row.Cells.Values.All(text => !text.Contains("5(3x4)", StringComparison.Ordinal))),
                $"Table with two header lines (shift {shift}): a page that repeats only the row of column numbers continues the table, the row of numbers is not a row ({rows.Count} rows)");
            check(table is not null && rows.Count == 4 && Cell(2, InvoiceColumnMeanings.Quantity) == "20" && Cell(2, InvoiceColumnMeanings.UnitPrice) == "3.00" && Cell(3, InvoiceColumnMeanings.Value) == "30.00" && Cell(3, InvoiceColumnMeanings.Name) == "Frezii galbene",
                $"Table with two header lines (shift {shift}): the cells of the page that has only the numbers are put in the right columns ({(table is null ? "no table" : string.Join(" // ", rows.Select(row => string.Join(" | ", row.Cells.Values))))}; columns {(table is null ? "" : string.Join(", ", table.Columns.Select(column => $"{column.Meaning}:{column.Left:F0}-{column.Right:F0}")))})");
        }
    }

    // The pickup reads the rows between demarcation lines made from the first reading: the row of column numbers is not glued to the first row, also when a line is
    // drawn between the header and that row (the line is the edge of the table, not of the first row of goods).
    private static void NumberRowSeparatorChecks(Action<bool, string> check)
    {
        var order = 0;
        InvoiceWord Word(string text, double x, double y, double width = 0) => new(1, text, x, y, width > 0 ? width : 5.2 * text.Length, 7, order++);
        double[] xs = [62, 100, 215, 275, 340, 425, 495];
        string[] labels = ["Nr crt", "Denumire produs", "U.M.", "Cantitatea", "Pretul unitar", "Valoarea", "T.V.A"];
        string[] numbers = ["0", "1", "2", "3", "4", "5(3x4)", "6"];
        List<InvoiceWord> words = [];
        for (var column = 0; column < 7; column++) { words.Add(Word(labels[column], xs[column], 247)); words.Add(Word(numbers[column], xs[column] + 2, 271, numbers[column].Length * 4.5)); }
        string[][] rows = [["1.", "Buchet Lalele", "Buc", "10", "36.30", "363.00", "87.12"], ["2.", "Trandafiri rosii", "Buc", "5", "12.50", "62.50", "15.00"]];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < 7; column++) words.Add(Word(rows[row][column], xs[column] + (column >= 3 ? 8 : 0), 287 + row * 16, column >= 3 ? 4.5 * rows[row][column].Length : 0));
        foreach (var withRule in new[] { false, true })
        {
            var rules = withRule ? new List<InvoiceRule> { new(false, 262, 55, 540) } : [];
            var document = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, words, rules)]);
            var analysis = InvoiceAnalyzer.Analyze(document);
            var definition = InvoiceTemplateDraft.FromAnalysis(analysis).ToDefinition(document);
            var reading = InvoicePickupReader.Read(definition, document);
            var reread = InvoicePickupReader.Reread(reading, document, reading.Separators);
            check(reread.Count == 2 && reread.All(row => row.Cells.Values.All(text => !text.Contains("5(3x4)", StringComparison.Ordinal))) && reading.Separators.Count >= 2 && reading.Separators.Min(separator => separator.Y) >= 277,
                $"Pickup reading ({(withRule ? "a line between the header and the row of numbers" : "no drawn lines")}): the row of column numbers is not part of the first row ({reread.Count} rows, first line at {(reading.Separators.Count == 0 ? double.NaN : reading.Separators.Min(separator => separator.Y)):F1})");
        }
    }

    // The columns the pickup shows (the template made from the automatic reading, read on the same file) are the columns of that reading: a name that runs under the
    // whole width of its column does not shrink the column to the width of its heading.
    private static void PickupColumnsChecks(Action<bool, string> check)
    {
        var order = 0;
        InvoiceWord Word(string text, double x, double y, double width = 0) => new(1, text, x, y, width > 0 ? width : 5.2 * text.Length, 7, order++);
        double[] xs = [62, 100, 330, 375, 420, 480, 535];
        string[] labels = ["Nr crt", "Denumire produs", "U.M.", "Cant.", "Pret", "Valoare", "TVA"];
        List<InvoiceWord> words = [];
        for (var column = 0; column < 7; column++) words.Add(Word(labels[column], xs[column], 247));
        string[][] rows =
        [
            ["1", "Buchet Lalele de gradina mare", "buc", "10", "36.30", "363.00", "87.12"],
            ["2", "Trandafiri rosii pentru nunta", "buc", "5", "12.50", "62.50", "15.00"],
            ["3", "Garoafe albe", "buc", "2", "3.00", "6.00", "1.44"]
        ];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < 7; column++) words.Add(Word(rows[row][column], xs[column] + (column >= 3 ? 6 : 0), 275 + row * 16, column >= 3 ? 4.5 * rows[row][column].Length : 0));
        var document = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, words)]);
        var analysis = InvoiceAnalyzer.Analyze(document);
        var reading = InvoicePickupReader.Read(InvoiceTemplateDraft.FromAnalysis(analysis).ToDefinition(document), document);
        var name = analysis.Table?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name);
        var shown = reading.Extraction.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name);
        check(name is not null && shown is not null && name.Right - name.Left > 150 && Math.Abs(shown.Left - name.Left) <= 3 && Math.Abs(shown.Right - name.Right) <= 3 &&
              analysis.Table!.Columns.All(column => reading.Extraction.Columns.FirstOrDefault(other => other.Id == column.Id) is { } other && other.Left <= column.Left + 3 && other.Right >= column.Right - 3),
            "Pickup columns: the columns shown are as wide as the ones the automatic reading found (read " + Describe(analysis.Table?.Columns ?? []) + "; shown " + Describe(reading.Extraction.Columns) + ")");
        static string Describe(IEnumerable<InvoiceColumn> columns) => string.Join(", ", columns.Select(column => column.Meaning + " " + column.Left.ToString("F0") + "-" + column.Right.ToString("F0")));
    }

    // A table drawn with rules between its rows: a box with only text that starts at its top (a description under a product) belongs to the row above, and a note
    // written in the room left under the last row is not a row.
    private static void RuledNotesChecks(Action<bool, string> check)
    {
        var order = 0;
        InvoiceWord Word(string text, double x, double y, double width = 0) => new(1, text, x, y, width > 0 ? width : 5.0 * text.Length, 8, order++);
        double[] borders = [20, 50, 300, 340, 380, 440, 520];
        List<InvoiceWord> words =
        [
            Word("Nr.", 24, 304), Word("crt.", 24, 312), Word("Denumirea", 120, 308), Word("UM", 312, 308), Word("Cant", 350, 308), Word("Pret", 400, 308), Word("Valoarea", 470, 308),
            Word("1", 30, 326, 5), Word("Cablu HDMI 3 metri", 56, 326), Word("buc", 310, 326), Word("5", 355, 326, 5), Word("9.20", 400, 326, 20), Word("46.00", 470, 326, 25),
            Word("2", 30, 346, 5), Word("Switch 4 porturi PoE", 56, 346), Word("buc", 310, 346), Word("3", 355, 346, 5), Word("106.72", 400, 346, 30), Word("320.16", 470, 346, 30),
            Word("Detalii: carcasa metalica, montaj pe sina", 56, 363, 200),
            Word("3", 30, 386, 5), Word("Conector alimentare", 56, 386), Word("buc", 310, 386), Word("1", 355, 386, 5), Word("10.00", 400, 386, 25), Word("10.00", 470, 386, 25),
            Word("Cant", 350, 406), Word("Pret", 400, 406),   // the labels again in a box of their own: a header repeated inside the table
            Word("Atentie neplata la termen atrage penalizari", 56, 590, 200)
        ];
        List<InvoiceRule> rules = [];
        foreach (var y in new[] { 300.0, 320, 340, 360, 380, 400, 420, 640 }) rules.Add(new InvoiceRule(false, y, 20, 520));
        foreach (var x in borders) rules.Add(new InvoiceRule(true, x, 300, 640));
        var table = InvoiceTableReader.Detect(new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, words, rules)]), '.').Table;
        check(table is not null && table.Rows.Count == 3, $"Table grid: three rows in a ruled table with a description box, a repeated header and a note under the last row ({table?.Rows.Count ?? 0} rows)");
        check(table is not null && table.Rows.Count == 3 && table.Rows[1].Cells.Values.Any(text => text.Contains("Detalii", StringComparison.Ordinal) && text.Contains("Switch", StringComparison.Ordinal)),
            "Table grid: a box with only a description at its top is part of the row above it");
        check(table is not null && table.Rows.All(row => row.Cells.Values.All(text => !text.Contains("Atentie", StringComparison.Ordinal))), "Table grid: a note in the room under the last row is not a row");
        check(table is not null && table.Rows.All(row => row.Cells.Values.All(text => text != "Cant" && text != "Pret" && !text.Contains("Cant ", StringComparison.Ordinal))),
            "Table grid: a box that repeats the labels of the columns is neither a row nor a continuation");
    }

    // The geometric engine (B) finds a table by its structure: no dictionary of labels, no running number, no position is assumed.
    private static void GeometricEngineChecks(Action<bool, string> check)
    {
        var order = 0;
        InvoiceWord Word(int page, string text, double x, double y, double width = 0, double height = 7) => new(page, text, x, y, width > 0 ? width : 5.0 * text.Length, height, order++);
        string[] columnsX = ["x", "40", "250", "330", "400", "470"];

        // Labels that no dictionary knows, no running number, no rules: a table found from its rows of figures under each other and the labels over them.
        List<InvoiceWord> words = [Word(1, "Zorg", 40, 200), Word(1, "Blip", 250, 200), Word(1, "Krat", 330, 200), Word(1, "Plon", 400, 200), Word(1, "Wuxe", 470, 200)];
        string[][] rows = [["Cablu retea", "10", "2,50", "25,00"], ["Priza dubla", "3", "7,10", "21,30"], ["Mufa", "12", "1,10", "13,20"], ["Banda izolatoare", "5", "3,00", "15,00"]];
        for (var row = 0; row < rows.Length; row++)
        {
            var y = 225.0 + row * 14;
            words.AddRange([Word(1, rows[row][0], 40, y), Word(1, rows[row][1], 255, y, 10), Word(1, rows[row][2], 330, y, 22), Word(1, rows[row][3], 405, y, 28), Word(1, (double.Parse(rows[row][3].Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) * 1.21).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), 470, y, 30)]);
        }
        var unlabelled = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, words)]);
        var found = InvoiceTableReader.Detect(unlabelled, '.').Table;
        check(found is not null && found.Columns.Count >= 5 && found.Rows.Count == 4 && found.Rows[0].Cells.Values.Any(text => text.Contains("Cablu")) && found.Rows[3].Cells.Values.Any(text => text.Contains("izolatoare")),
            "Geometric engine: a table whose labels no dictionary knows is found from its rows of figures and the header over them");

        // Prose above the header, a larger gap before the first row: the header is the labels, not the paragraph over them.
        List<InvoiceWord> withProse = [.. words];
        for (var line = 0; line < 5; line++) withProse.Add(Word(1, "Conditii generale de livrare si plata valabile pentru toate comenzile onorate de furnizor in baza contractului", 40, 120 + line * 10, 450));
        var proseDocument = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, withProse)]);
        var withText = InvoiceTableReader.Detect(proseDocument, '.').Table;
        check(withText is not null && withText.HeaderTop >= 196 && withText.Columns.All(column => !column.Label.Contains("Conditii")) && withText.Rows.Count == 4, "Geometric engine: the paragraph above the header is not part of it");

        // A row of column numbers and the name of the first row above its figures (the rows are drawn with the figures lower): the name belongs to the row, not to the header.
        List<InvoiceWord> numbered = [Word(1, "Denumire", 40, 200), Word(1, "Cant.", 255, 200), Word(1, "Pret", 335, 200), Word(1, "Valoare", 410, 200), Word(1, "TVA", 480, 200),
            Word(1, "1", 40, 214, 4), Word(1, "2", 255, 214, 4), Word(1, "3", 335, 214, 4), Word(1, "4=2x3", 410, 214, 25), Word(1, "5", 480, 214, 4),
            Word(1, "IMPRIMANTA LASER MONOCROM", 40, 228), Word(1, "2", 258, 232, 5), Word(1, "504,12", 330, 232, 30), Word(1, "1.008,24", 405, 232, 35), Word(1, "211,73", 475, 232, 30),
            Word(1, "SCANNER PLAN A4", 40, 250), Word(1, "1", 258, 254, 5), Word(1, "199,00", 330, 254, 30), Word(1, "199,00", 410, 254, 30), Word(1, "41,79", 475, 254, 30)];
        var numberedTable = InvoiceTableReader.Detect(new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, numbered)]), '.').Table;
        check(numberedTable is not null && numberedTable.HeaderBottom < 212 && numberedTable.Rows.Count == 2 && numberedTable.Rows[0].Cells.Values.Any(text => text.Contains("IMPRIMANTA")),
            "Geometric engine: the row of column numbers is not a row, and the first row's name above its figures is not part of the header");

        // An annex on the next page with a table of its own is not the continuation of the table of the invoice.
        List<InvoiceWord> twoPages = [.. words.Where(word => word.Page == 1), Word(1, "Total", 40, 300), Word(1, "74,50", 405, 300, 28)];
        twoPages.AddRange([Word(2, "Poz", 40, 80), Word(2, "Produs", 120, 80), Word(2, "Garantie", 330, 80), Word(2, "Serie", 450, 80),
            Word(2, "1", 40, 100, 4), Word(2, "Cablu retea", 120, 100), Word(2, "24", 335, 100, 10), Word(2, "1234", 455, 100, 20), Word(2, "2", 40, 114, 4), Word(2, "Priza dubla", 120, 114), Word(2, "12", 335, 114, 10), Word(2, "5678", 455, 114, 20)]);
        var annexed = InvoiceTableReader.Detect(new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, twoPages.Where(word => word.Page == 1).ToList()),
            new InvoicePageData(2, 595, 842, InvoiceSources.Text, twoPages.Where(word => word.Page == 2).ToList())]), '.').Table;
        check(annexed is not null && annexed.HeaderPage == 1 && annexed.Rows.Count == 4 && annexed.Rows.All(row => row.Page == 1), "Geometric engine: the table of an annex on the next page is not read as part of the invoice's table");
    }
}
