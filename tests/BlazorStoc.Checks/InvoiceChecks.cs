using System.Globalization;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Checks of the invoice template engine (Settings -> Facturi): values, dictionary, analysis of generated invoices in several layouts (text
// layer, rotated page, two pages, scans), templates applied to other invoices, the real sample invoices when they are available, the
// session store, the journal. Invoices are generated here (InvoiceFixtures), so the repository carries no invoice of a real supplier.
public static class InvoiceChecks
{
    private sealed class TestTessdata : IWebHostEnvironmentTessdataPath
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== Invoice templates ===");
        Values(check);
        Vocabulary(check);
        var reader = new InvoicePdfReader(new TestTessdata());

        async Task<(InvoiceDocument Document, InvoiceAnalysis Analysis)> Analyze(byte[] pdf)
        {
            using var stream = new MemoryStream(pdf);
            var read = await reader.ReadAsync(stream);
            return (read.Document, InvoiceAnalyzer.Analyze(read.Document));
        }

        string Field(InvoiceAnalysis analysis, string meaning) => analysis.Fields.FirstOrDefault(field => field.Meaning == meaning)?.Value ?? "";
        string Cell(InvoiceTable table, InvoiceTableRow row, string meaning) =>
            table.Columns.FirstOrDefault(column => column.Meaning == meaning) is { } column ? row.Cells[column.Id] : "";
        bool RowsMatch(InvoiceAnalysis analysis, IReadOnlyList<FixtureRow> expected, char decimalSeparator)
        {
            if (analysis.Table is not { } table || table.Rows.Count != expected.Count) return false;
            for (var i = 0; i < expected.Count; i++)
            {
                var row = table.Rows[i];
                if (!string.Equals(Cell(table, row, InvoiceColumnMeanings.Name), expected[i].Name, StringComparison.Ordinal)) return false;
                if (InvoiceValues.ParseNumber(Cell(table, row, InvoiceColumnMeanings.Quantity), decimalSeparator) != expected[i].Quantity) return false;
                if (InvoiceValues.ParseNumber(Cell(table, row, InvoiceColumnMeanings.UnitPrice), decimalSeparator) != expected[i].Price) return false;
                if (InvoiceValues.ParseNumber(Cell(table, row, InvoiceColumnMeanings.Value), decimalSeparator) != expected[i].Value) return false;
                if (row.Flags.Count > 0) return false;
            }
            return true;
        }

        // ---- ruled table, Romanian labels, 1.234,56 ----
        var roSpec = new InvoiceSpec(Rows: 6);
        var ro = InvoiceFixtures.Make(roSpec);
        var (roDocument, roAnalysis) = await Analyze(ro.Pdf);
        check(roDocument.Pages.Count == 1 && roDocument.Pages[0].Source == InvoiceSources.Text && roDocument.Pages[0].Words.Count > 40, "Invoice analysis: a generated PDF is read from its text layer");
        check(roAnalysis.Table is { } roTable && roTable.Columns.Select(column => column.Meaning).SequenceEqual(
                [InvoiceColumnMeanings.Index, InvoiceColumnMeanings.Name, InvoiceColumnMeanings.Unit, InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value]) && roTable.HasIndexColumn,
            "Invoice analysis: the table header is recognised by its labels (index, name, unit, quantity, price, value) in a ruled Romanian invoice");
        check(RowsMatch(roAnalysis, ro.Rows, ','), "Invoice analysis: every row of a ruled Romanian invoice is read exactly (names, quantities, prices, values with 1.234,56)");
        check(Field(roAnalysis, InvoiceFieldMeanings.InvoiceNumber) == "DIS 1042" && Field(roAnalysis, InvoiceFieldMeanings.InvoiceDate) == "05.10.2026" &&
              Field(roAnalysis, InvoiceFieldMeanings.DueDate) == "04.11.2026" && Field(roAnalysis, InvoiceFieldMeanings.Currency) == "RON",
            "Invoice analysis: number, issue date, due date and currency are found by their labels");
        check(Field(roAnalysis, InvoiceFieldMeanings.SupplierName) == "Delta Instalatii SRL" && Field(roAnalysis, InvoiceFieldMeanings.BuyerName) == "Electric Standard Prest SRL" &&
              roAnalysis.SupplierCui == "12345678" && InvoiceValues.NormalizeCui(Field(roAnalysis, InvoiceFieldMeanings.BuyerCui)) == "9178894",
            "Invoice analysis: the supplier and the buyer are told apart by their headings (name, tax id)");
        check(InvoiceValues.ParseNumber(Field(roAnalysis, InvoiceFieldMeanings.TotalNet), ',') == ro.TotalNet && InvoiceValues.ParseNumber(Field(roAnalysis, InvoiceFieldMeanings.Total), ',') == ro.Total,
            "Invoice analysis: the totals are found (total without VAT, total to pay)");

        // ---- borderless table, English labels, 1,234.56, more lines above the table ----
        var en = InvoiceFixtures.Make(new InvoiceSpec("en-plain", 8, 3, SupplierName: "Northern Cables Ltd", SupplierCui: "RO22334455", Number: "INV-2026-77", Date: new DateOnly(2026, 11, 20)));
        var (_, enAnalysis) = await Analyze(en.Pdf);
        check(enAnalysis.Table is { } enTable && enTable.Columns.Count == 6 && enTable.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Quantity) &&
              enTable.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.UnitPrice) && enTable.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Value) &&
              enTable.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Name),
            "Invoice analysis: an English table without rules is recognised too (Item, Description, Unit, Qty, Unit price, Amount)");
        check(RowsMatch(enAnalysis, en.Rows, '.'), "Invoice analysis: every row of an English invoice without rules is read exactly (1,234.56)");
        check(Field(enAnalysis, InvoiceFieldMeanings.InvoiceNumber) == "INV-2026-77" && Field(enAnalysis, InvoiceFieldMeanings.InvoiceDate) == "2026-11-20" &&
              Field(enAnalysis, InvoiceFieldMeanings.Currency) == "EUR" && Field(enAnalysis, InvoiceFieldMeanings.SupplierName) == "Northern Cables Ltd",
            "Invoice analysis: the fields of an English invoice are found by their English labels");

        // ---- no running number: the rows are told apart by their amounts, the totals under the table are not rows ----
        var noIndex = InvoiceFixtures.Make(new InvoiceSpec("en-noindex", 7, 1, SupplierName: "Plain Goods Ltd", SupplierCui: "RO31415926", Number: "PG-5"), InvoiceFixtures.MakeRows(7, 5));
        var (_, noIndexAnalysis) = await Analyze(noIndex.Pdf);
        check(noIndexAnalysis.Table is { HasIndexColumn: false } noIndexTable && noIndexTable.Rows.Count == 7 && RowsMatch(noIndexAnalysis, noIndex.Rows, '.') && noIndexTable.Rows.All(row => row.Number is null),
            "Invoice analysis: a table without a running number is read row by row from its amounts, and its totals are not taken for rows");

        // ---- labels the dictionary does not know (Hungarian): found by the structure, meanings inferred from the arithmetic ----
        var unknown = InvoiceFixtures.Make(new InvoiceSpec("hu-plain", 6, SupplierName: "Kelet Kft", SupplierCui: "RO27182818", Number: "KK-9"), InvoiceFixtures.MakeRows(6, 9));
        var (_, unknownAnalysis) = await Analyze(unknown.Pdf);
        check(unknownAnalysis.Table is { ByStructure: true } unknownTable && unknownTable.HasIndexColumn && unknownTable.Columns.Count == 6 && unknownAnalysis.Warnings.Any(warning => warning.Contains("nu a fost recunoscut", StringComparison.Ordinal)),
            "Invoice analysis: a table whose header labels are not in the dictionary is found by its running numbers, and the user is warned to check the column meanings");
        check(unknownAnalysis.Table is { } structured && structured.Columns.Select(column => column.Meaning).SequenceEqual(
                  [InvoiceColumnMeanings.Index, InvoiceColumnMeanings.Name, InvoiceColumnMeanings.Unit, InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value]) && RowsMatch(unknownAnalysis, unknown.Rows, '.'),
            "Invoice analysis: without help from the labels, name, unit, quantity, unit price and value are told from the text and from quantity x price = value; every row is exact");

        // ---- names on two lines, with and without rules ----
        foreach (var style in new[] { "ro-lines", "en-plain" })
        {
            var wrapped = InvoiceFixtures.Make(new InvoiceSpec(style, 7, WrapNamesAt: 22), InvoiceFixtures.MakeRows(7, 13));
            var (_, wrappedAnalysis) = await Analyze(wrapped.Pdf);
            check(RowsMatch(wrappedAnalysis, wrapped.Rows, style == "ro-lines" ? ',' : '.') && wrapped.Rows.All(row => row.Name.Length > 22),
                $"Invoice analysis: names written on two lines stay in their row ({style}: 7 rows, names joined exactly, amounts on the first line)");
        }

        // ---- two pages, the table continues without a repeated header ----
        var two = InvoiceFixtures.Make(new InvoiceSpec(Rows: 38, RowsFirstPage: 22));
        var (twoDocument, twoAnalysis) = await Analyze(two.Pdf);
        check(twoDocument.Pages.Count >= 2 && twoAnalysis.Table is { } twoTable && twoTable.Rows.Count == 38 && twoTable.Rows.Select(row => row.Number).SequenceEqual(Enumerable.Range(1, 38).Select(n => (int?)n)) &&
              twoTable.Rows.Any(row => row.Page == 2), "Invoice analysis: a table that continues on the next page (no repeated header) is read as one, numbered 1..38");
        check(RowsMatch(twoAnalysis, two.Rows, ','), "Invoice analysis: the rows of a two-page table are exact");
        var big = InvoiceFixtures.Make(new InvoiceSpec("en-plain", 150, RowsFirstPage: 35, RepeatHeader: true), InvoiceFixtures.MakeRows(150, 23));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var (bigDocument, bigAnalysis) = await Analyze(big.Pdf);
        timer.Stop();
        check(bigDocument.Pages.Count >= 4 && RowsMatch(bigAnalysis, big.Rows, '.') && bigAnalysis.Table!.Rows.Select(row => row.Number).SequenceEqual(Enumerable.Range(1, 150).Select(n => (int?)n)),
            $"Invoice analysis: a long invoice (150 rows over {bigDocument.Pages.Count} pages) is read exactly and completely (reading and analysis took {timer.ElapsedMilliseconds} ms)");
        var repeated = InvoiceFixtures.Make(new InvoiceSpec("en-plain", 35, RowsFirstPage: 20, RepeatHeader: true), InvoiceFixtures.MakeRows(35, 17));
        var (_, repeatedAnalysis) = await Analyze(repeated.Pdf);
        check(RowsMatch(repeatedAnalysis, repeated.Rows, '.') && repeatedAnalysis.Table!.Rows.Any(row => row.Page == 2),
            "Invoice analysis: a table that continues under a repeated header is read as one (35 rows over two pages, the repeated header is not a row)");

        // ---- a page turned with /Rotate 90 (landscape content on a portrait sheet) ----
        var rotated = InvoiceFixtures.Make(new InvoiceSpec(Rows: 5, Rotated: true), InvoiceFixtures.MakeRows(5, 7));
        var (rotatedDocument, rotatedAnalysis) = await Analyze(rotated.Pdf);
        check(rotatedDocument.Pages[0].Width > rotatedDocument.Pages[0].Height && RowsMatch(rotatedAnalysis, rotated.Rows, ',') && Field(rotatedAnalysis, InvoiceFieldMeanings.InvoiceNumber) == "DIS 1042",
            "Invoice analysis: a page turned with /Rotate is upright in the words' coordinates, so the table and the fields are read as on an upright page");

        // ---- column order and labels are data: another order of the same labels ----
        // (covered by the English layout above, whose columns are in another order than the Romanian one)

        // ---- a template made from one invoice reads another (other supplier, other number of rows, other height of the table) ----
        var otherRows = InvoiceFixtures.MakeRows(9, 11);
        var other = InvoiceFixtures.Make(new InvoiceSpec(Rows: 9, ExtraAddressLines: 3, SupplierName: "Sigma Electro SRL", SupplierCui: "RO99887766", Number: "SE 77", Date: new DateOnly(2026, 12, 1)), otherRows);
        var (otherDocument, otherAnalysis) = await Analyze(other.Pdf);
        var draft = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        check(draft.Problems().Count == 0 && draft.Fields.Count(field => field.Use) >= 8 && draft.Columns.Count(column => column.Use) == 6,
            "Invoice template: the proposal of the analysis is a valid template (recognised fields and columns used, nothing else)");
        var definition = InvoiceTemplateJson.Deserialize(InvoiceTemplateJson.Serialize(draft.ToDefinition(roDocument)));
        check(definition.Fields.Any(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber && field.Mode == InvoiceFieldModes.Right && field.LabelText.Length > 0) &&
              definition.Table is { } saved && saved.Columns.All(column => column.Left is >= 0 and <= 1 && column.Right is >= 0 and <= 1) && definition.Anchors.Count > 5,
            "Invoice template: the template is stored with fractional positions, label-anchored fields and anchors, and survives its JSON form");
        var extraction = InvoiceTemplateEngine.Apply(definition, otherDocument);
        string Extracted(string meaning) => extraction.Fields.FirstOrDefault(field => field.Meaning == meaning)?.Value ?? "";
        check(Extracted(InvoiceFieldMeanings.InvoiceNumber) == "SE 77" && Extracted(InvoiceFieldMeanings.InvoiceDate) == "01.12.2026" && Extracted(InvoiceFieldMeanings.SupplierName) == "Sigma Electro SRL" &&
              InvoiceValues.NormalizeCui(Extracted(InvoiceFieldMeanings.SupplierCui)) == "99887766" && InvoiceValues.ParseNumber(Extracted(InvoiceFieldMeanings.Total), ',') == other.Total,
            "Invoice template: applied to an invoice of another supplier with longer values and a table three lines lower, the fields are read from the labels' new places");
        var extractedTable = new InvoiceTable(1, 0, 0, extraction.Columns, extraction.Rows, true, 1);
        check(extraction.Rows.Count == 9 && extraction.Rows.Select((row, index) =>
                  Cell(extractedTable, row, InvoiceColumnMeanings.Name) == otherRows[index].Name && InvoiceValues.ParseNumber(Cell(extractedTable, row, InvoiceColumnMeanings.Value), ',') == otherRows[index].Value).All(ok => ok),
            "Invoice template: applied to an invoice with more rows, the rows are read by the template's columns (9 rows, names and values exact)");
        var match = InvoiceTemplateEngine.Match(definition, roAnalysis.SupplierCui, otherDocument);
        var englishMatch = InvoiceTemplateEngine.Match(definition, roAnalysis.SupplierCui, enAnalysis is null ? roDocument : (await Analyze(en.Pdf)).Document);
        check(match.Score >= 0.6 && !match.SupplierMatch && englishMatch.Score < match.Score && InvoiceTemplateEngine.Match(definition, "12345678", roDocument).SupplierMatch,
            "Invoice template: the match score is high for the same layout, lower for another one, and the supplier is recognised by its tax id in the file");
        var englishExtraction = InvoiceTemplateEngine.Apply(definition, (await Analyze(en.Pdf)).Document);
        check(englishExtraction.Warnings.Any(warning => warning.Contains("seamănă puțin", StringComparison.Ordinal)),
            "Invoice template: a file with another layout is reported as such instead of being read silently");
        var suggestions = InvoiceTemplateSuggestions.Rank(
            [new InvoiceTemplateRecord(new InvoiceTemplateInfo(1, "Delta", "Delta Instalatii SRL", "12345678", "text", 1, 0, "a", DateTime.UtcNow, "a", DateTime.UtcNow), definition)], otherDocument);
        check(suggestions.Count == 1 && suggestions[0].Match.Score >= InvoiceTemplateSuggestions.MinLayoutScore, "Invoice template: a saved template is suggested for a file with the same layout");

        // a template placed on a file keeps the user's regions (draft from definition)
        var redraft = InvoiceTemplateDraft.FromDefinition(definition, otherDocument, "Delta", "12345678");
        check(redraft.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber).Value == "SE 77" && redraft.Columns.Count(column => column.Use) == 6 && redraft.Problems().Count == 0,
            "Invoice template: a saved template opened on another file shows that file's values in its regions");

        // ---- drafts: problems that stop a save ----
        var broken = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        foreach (var field in broken.Fields) field.Use = false;
        foreach (var column in broken.Columns) column.Use = false;
        check(broken.Problems().Count == 1, "Invoice template: a draft with nothing used cannot be saved");
        broken.Columns.First(column => column.Meaning == InvoiceColumnMeanings.Quantity).Use = true;
        check(broken.Problems().Any(problem => problem.Contains("denumirea sau cu codul", StringComparison.Ordinal)), "Invoice template: a table without a name or code column cannot be saved");
        var twice = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber).Use = true;
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceDate).Meaning = InvoiceFieldMeanings.InvoiceNumber;
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber && field.Id != twice.Fields.First(f => f.Meaning == InvoiceFieldMeanings.InvoiceNumber).Id).Use = true;
        check(twice.Problems().Any(problem => problem.Contains("mai multe câmpuri", StringComparison.Ordinal)), "Invoice template: one meaning on two used fields is refused");

        // code taken from the name
        var codeDefinition = definition with { Table = definition.Table! with { NameCodeSeparator = " " } };
        var codeExtraction = InvoiceTemplateEngine.Apply(codeDefinition, roDocument);
        check(codeExtraction.Rows[0].Cells.ContainsKey("code") && codeExtraction.Rows[0].Cells["code"] == "Articol", "Invoice template: the product code can be taken from the start of the name (separator configured in the template)");

        await ScansAsync(check, reader, Field, Cell, RowsMatch);
        await RealSamplesAsync(check, reader);
        Store(check);
        await ServiceAsync(check);
        AuditRules(check);
    }

    // ---- values ----
    private static void Values(Action<bool, string> check)
    {
        check(InvoiceValues.ParseNumber("1.234,56") == 1234.56m && InvoiceValues.ParseNumber("1,234.56") == 1234.56m && InvoiceValues.ParseNumber("1 234,56") == 1234.56m &&
              InvoiceValues.ParseNumber("-46.60") == -46.60m && InvoiceValues.ParseNumber("155.63") == 155.63m && InvoiceValues.ParseNumber("32.68 RON") == 32.68m &&
              InvoiceValues.ParseNumber("21%") == 21m && InvoiceValues.ParseNumber("(12,50)") == -12.50m && InvoiceValues.ParseNumber("1.234.567") == 1234567m &&
              InvoiceValues.ParseNumber("abc") is null && InvoiceValues.ParseNumber("") is null && InvoiceValues.ParseNumber("12-34") is null && InvoiceValues.ParseNumber("RO9178894") is null,
            "Invoice values: numbers are read with either separator convention, suffixes, brackets, thousands separators; text is not a number");
        check(InvoiceValues.ParseNumber("20.000", '.') == 20m && InvoiceValues.ParseNumber("20.000", ',') == 20000m && InvoiceValues.ParseNumber("1,234", '.') == 1234m &&
              InvoiceValues.DecimalStyle(["155.63", "20.000", "4078.50"]) == '.' && InvoiceValues.DecimalStyle(["1.234,56", "12,50", "100"]) == ',' && InvoiceValues.DecimalStyle(["1", "2"]) is null,
            "Invoice values: an ambiguous 20.000 follows the decimal style of the document (quantity 20 with dots, 20000 with commas)");
        check(InvoiceValues.ParseDate("2026-09-25") == new DateOnly(2026, 9, 25) && InvoiceValues.ParseDate("25.09.2026") == new DateOnly(2026, 9, 25) && InvoiceValues.ParseDate("25/09/26") == new DateOnly(2026, 9, 25) &&
              InvoiceValues.ParseDate("25 sept 2026") == new DateOnly(2026, 9, 25) && InvoiceValues.ParseDate("Sept 2026") is null && InvoiceValues.ParseDate("31.02.2026") is null && InvoiceValues.ParseDate("x") is null,
            "Invoice values: dates in the usual formats (ISO, dd.mm.yyyy, dd/mm/yy, with a month name) are read, impossible dates are not");
        check(InvoiceValues.NormalizeCui("RO 22460883") == "22460883" && InvoiceValues.NormalizeCui("CUI: RO9178894") == "9178894" && InvoiceValues.NormalizeCui("9178894") == "9178894" && InvoiceValues.NormalizeCui("J40/1/2020") == "" && InvoiceValues.NormalizeCui(null) == "",
            "Invoice values: a tax id is reduced to its digits, other identifiers are not taken for one");
        check(InvoiceValues.Normalize("Șablon ȚARĂ Înregistrare, Nr.crt.") == "sablon tara inregistrare nr crt" && InvoiceValues.Normalize(" ") == "", "Invoice values: text is compared without case, diacritics and punctuation");
    }

    private static void Vocabulary(Action<bool, string> check)
    {
        (string, string)[] columns =
        [
            ("Nr. crt.", InvoiceColumnMeanings.Index), ("Linia", InvoiceColumnMeanings.Index), ("Denumirea produselor sau serviciilor", InvoiceColumnMeanings.Name),
            ("Nume articol/Descriere articol", InvoiceColumnMeanings.Name), ("U.M.", InvoiceColumnMeanings.Unit), ("UM", InvoiceColumnMeanings.Unit), ("Cantitate facturata", InvoiceColumnMeanings.Quantity),
            ("Cantitatea", InvoiceColumnMeanings.Quantity), ("Qty", InvoiceColumnMeanings.Quantity), ("Pretul net al articolului", InvoiceColumnMeanings.UnitPrice), ("Pret unitar (fara TVA)", InvoiceColumnMeanings.UnitPrice),
            ("Unit price", InvoiceColumnMeanings.UnitPrice), ("Valoare neta", InvoiceColumnMeanings.Value), ("Valoare (fara TVA)", InvoiceColumnMeanings.Value), ("Amount", InvoiceColumnMeanings.Value),
            ("Valoare TVA", InvoiceColumnMeanings.VatAmount), ("Cota TVA", InvoiceColumnMeanings.VatRate), ("Cod articol", InvoiceColumnMeanings.Code), ("Cantitate de baza", InvoiceColumnMeanings.Ignore),
            ("Tara provenient", InvoiceColumnMeanings.Ignore), ("Moneda", InvoiceColumnMeanings.Currency), ("Xyzzy", "")
        ];
        var wrong = columns.Where(item => InvoiceVocabulary.MatchColumn(item.Item1).Meaning != item.Item2).Select(item => $"{item.Item1} -> {InvoiceVocabulary.MatchColumn(item.Item1).Meaning}").ToList();
        check(wrong.Count == 0, "Invoice vocabulary: column headers in Romanian and English are given the right meaning" + (wrong.Count > 0 ? " (wrong: " + string.Join("; ", wrong) + ")" : ""));
        check(InvoiceVocabulary.MatchSection("VANZATOR") == InvoiceVocabulary.SupplierSection && InvoiceVocabulary.MatchSection("Cumpărător:") == InvoiceVocabulary.BuyerSection && InvoiceVocabulary.MatchSection("Seller") == InvoiceVocabulary.SupplierSection &&
              InvoiceVocabulary.MatchSection("Vanzator este o firma mare din oras") is null,
            "Invoice vocabulary: the headings of the supplier and buyer blocks are recognised, long sentences are not headings");
        check(InvoiceVocabulary.IsFooterStart("Total plata") && InvoiceVocabulary.IsFooterStart("Instructiuni de plata") && !InvoiceVocabulary.IsFooterStart("Totalizator camera video"),
            "Invoice vocabulary: the lines that end a table (totals, payment instructions) are told from product names that start alike");
        var (words, meaning) = InvoiceVocabulary.MatchFieldLabelPrefix(InvoiceVocabulary.Tokens("Data scadenta 2026-11-21"));
        check(words == 2 && meaning == InvoiceFieldMeanings.DueDate, "Invoice vocabulary: the longest label wins (Data scadenta is the due date, not the date)");
    }

    // ---- scans: the same invoice as a picture, read with OCR ----
    private static async Task ScansAsync(Action<bool, string> check, InvoicePdfReader reader, Func<InvoiceAnalysis, string, string> field,
        Func<InvoiceTable, InvoiceTableRow, string, string> cell, Func<InvoiceAnalysis, IReadOnlyList<FixtureRow>, char, bool> rowsMatch)
    {
        var english = InvoiceFixtures.MakeRows(6, 3);
        var source = InvoiceFixtures.Make(new InvoiceSpec("en-plain", 6, SupplierName: "Scan Test Ltd", SupplierCui: "RO55667788", Number: "SC-4471", Date: new DateOnly(2026, 8, 14)), english);
        var variants = new (string Name, byte[] Pdf)[]
        {
            ("straight scan", InvoiceFixtures.Scan(source.Pdf)),
            ("tilted scan (1.4 degrees)", InvoiceFixtures.Scan(source.Pdf, skewDegrees: 1.4)),
            ("scan turned 90 degrees", InvoiceFixtures.Scan(source.Pdf, rotation: 90)),
            ("scan upside down", InvoiceFixtures.Scan(source.Pdf, rotation: 180))
        };
        foreach (var (name, pdf) in variants)
        {
            using var stream = new MemoryStream(pdf);
            var read = await reader.ReadAsync(stream);
            var analysis = InvoiceAnalyzer.Analyze(read.Document);
            var page = read.Document.Pages[0];
            var table = analysis.Table;
            var rows = table?.Rows ?? [];
            // OCR is not exact: what counts is that the page is recognised as a scan, upright, with its table and most rows right.
            var valueOk = rows.Count(row => table is not null && english.Any(expected => InvoiceValues.ParseNumber(cell(table, row, InvoiceColumnMeanings.Value), '.') == expected.Value));
            check(page.Source == InvoiceSources.Ocr && page.Width < page.Height && analysis.Warnings.Any(warning => warning.Contains("OCR", StringComparison.Ordinal)),
                $"Invoice scan ({name}): a page without text is read with OCR, turned upright, and the user is told to check the values");
            check(table is not null && rows.Count == english.Count && valueOk >= english.Count - 1,
                $"Invoice scan ({name}): the table is found and the rows are read ({valueOk} of {english.Count} values exact, {rows.Count} rows)");
            check(field(analysis, InvoiceFieldMeanings.InvoiceNumber).Contains("4471", StringComparison.Ordinal) && field(analysis, InvoiceFieldMeanings.Currency).Contains("EUR", StringComparison.Ordinal),
                $"Invoice scan ({name}): the invoice number and currency are read from the picture");
        }

        // Romanian text with diacritics is read with the Romanian data (ron + eng together).
        var romanian = InvoiceFixtures.Make(new InvoiceSpec("ro-lines", 4, SupplierName: "Întreprinderea Țăranu și Ștefănescu SRL", SupplierCui: "RO40404040", Number: "ȘT 12"), InvoiceFixtures.MakeRows(4, 21));
        using var romanianStream = new MemoryStream(InvoiceFixtures.Scan(romanian.Pdf, dpi: 250));
        var romanianRead = await reader.ReadAsync(romanianStream);
        var romanianAnalysis = InvoiceAnalyzer.Analyze(romanianRead.Document);
        var romanianName = field(romanianAnalysis, InvoiceFieldMeanings.SupplierName);
        var installed = File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata", "ron.traineddata"));
        // OCR is not exact (a fast model may miss one mark), but with the Romanian data several diacritics of the name are read.
        check(installed && romanianName.Count(character => "ăâîșțĂÂÎȘȚ".Contains(character)) >= 2 && romanianName.Contains("Ștefănescu", StringComparison.Ordinal),
            $"Invoice scan (Romanian): the diacritics of a supplier's name are read with the Romanian OCR data (read: {romanianName})");
    }

    // ---- the real sample invoices (kept outside the repository: they hold a supplier's data) ----
    private static async Task RealSamplesAsync(Action<bool, string> check, InvoicePdfReader reader)
    {
        var directory = Environment.GetEnvironmentVariable("INVOICE_CORPUS_DIR") ?? @"D:\_BlazTest\Facturi furnizori";
        if (!Directory.Exists(directory) || Directory.GetFiles(directory, "*.pdf").Length == 0)
        {
            Console.WriteLine($"SKIP: real sample invoices not found ({directory}; set INVOICE_CORPUS_DIR)");
            return;
        }
        var loaded = new List<(string Name, InvoiceDocument Document, InvoiceAnalysis Analysis)>();
        foreach (var file in Directory.GetFiles(directory, "*.pdf").OrderBy(name => name))
        {
            await using var stream = File.OpenRead(file);
            var read = await reader.ReadAsync(stream);
            loaded.Add((Path.GetFileName(file), read.Document, InvoiceAnalyzer.Analyze(read.Document)));
        }
        foreach (var (name, _, analysis) in loaded)
        {
            var table = analysis.Table;
            var hint = '.';
            var value = table?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
            var sum = table is null || value is null ? 0 : table.Rows.Sum(row => InvoiceValues.ParseNumber(row.Cells[value.Id], hint) ?? 0);
            var totalNet = InvoiceValues.ParseNumber(analysis.Fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.TotalNet)?.Value, hint);
            check(table is { Rows.Count: > 0 } && table.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Name) && totalNet is not null && Math.Abs(sum - totalNet.Value) <= 0.05m &&
                  analysis.SupplierCui.Length > 0 && analysis.Fields.Any(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber),
                $"Real sample invoice [{name}]: table, rows summing to the total without VAT ({sum:0.00}), supplier tax id and number are found");
        }
        var failures = new List<string>();
        foreach (var source in loaded)
            foreach (var destination in loaded.Where(item => item.Name != source.Name))
            {
                var definition = InvoiceTemplateJson.Deserialize(InvoiceTemplateJson.Serialize(InvoiceTemplateDraft.FromAnalysis(source.Analysis).ToDefinition(source.Document)));
                var extraction = InvoiceTemplateEngine.Apply(definition, destination.Document);
                foreach (var field in extraction.Fields.Where(item => item.Meaning.Length > 0))
                {
                    var expected = destination.Analysis.Fields.FirstOrDefault(item => item.Meaning == field.Meaning)?.Value ?? "";
                    if (expected.Length > 0 && expected != field.Value) failures.Add($"{source.Name} -> {destination.Name}: {field.Meaning} '{field.Value}' != '{expected}'");
                }
                var destinationTable = destination.Analysis.Table;
                if (destinationTable is not null && extraction.Rows.Count != destinationTable.Rows.Count) failures.Add($"{source.Name} -> {destination.Name}: {extraction.Rows.Count} rows, expected {destinationTable.Rows.Count}");
            }
        check(failures.Count == 0, "Real sample invoices: a template made from each of them reads the others (fields and rows)" + (failures.Count > 0 ? ": " + string.Join("; ", failures) : ""));
    }

    // ---- the store of analysis sessions ----
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void Store(Action<bool, string> check)
    {
        var clock = new TestClock();
        var store = new InvoiceAnalysisStore(clock);
        var document = new InvoiceDocument([new InvoicePageData(1, 100, 100, InvoiceSources.Text, [])]);
        var read = new InvoiceReadResult(document, [[1, 2, 3]]);
        var analysis = new InvoiceAnalysis(document.Pages, [], null, []);
        var first = store.Add("ana", "a.pdf", read, analysis);
        check(store.Get(first.Id, "ana") is not null && store.Get(first.Id, "bob") is null && store.Get(Guid.NewGuid(), "ana") is null, "Invoice sessions: a file under analysis is visible only to the user who uploaded it");
        clock.Now = clock.Now.AddMinutes(29);
        check(store.Get(first.Id, "ana") is not null, "Invoice sessions: using a session keeps it alive");
        clock.Now = clock.Now.AddMinutes(29);
        check(store.Get(first.Id, "ana") is not null, "Invoice sessions: a session used every half hour does not expire");
        clock.Now = clock.Now.AddMinutes(31);
        check(store.Get(first.Id, "ana") is null && store.Count == 0, "Invoice sessions: an abandoned session is removed from memory after 30 minutes");
        var ids = Enumerable.Range(0, 5).Select(i => { clock.Now = clock.Now.AddSeconds(1); return store.Add("ana", $"{i}.pdf", read, analysis).Id; }).ToList();
        check(store.Count == InvoiceAnalysisStore.MaxSessionsPerOwner && store.Get(ids[0], "ana") is null && store.Get(ids[^1], "ana") is not null, "Invoice sessions: a user keeps at most three files in memory, the oldest are dropped");
        store.Remove(ids[^1]);
        check(store.Get(ids[^1], "ana") is null, "Invoice sessions: a session is removed as soon as the template is saved or abandoned");
    }

    // ---- the service: administrator only, unique names, journal ----
    private sealed class MemoryTemplateStore : IInvoiceTemplateStore
    {
        private readonly List<InvoiceTemplateRecord> records = [];
        private readonly Dictionary<int, List<InvoiceTemplateVersionInfo>> versions = [];
        private int nextId = 1;
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateInfo>>(records.Select(item => item.Info).ToList());
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(records.FirstOrDefault(item => item.Info.Id == id));
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateRecord>>(records.ToList());
        public Task<IReadOnlyList<InvoiceTemplateVersionInfo>> GetVersionsAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateVersionInfo>>(versions[id].OrderByDescending(item => item.VersionNumber).ToList());
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
        {
            var clean = InvoiceTemplateRules.Clean(input);
            if (clean.Definition is null) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
            var record = new InvoiceTemplateRecord(new InvoiceTemplateInfo(nextId++, clean.Name, clean.SupplierName, clean.SupplierCui, clean.Definition!.SourceKind, 1, 0, actor, DateTime.UtcNow, actor, DateTime.UtcNow), clean.Definition);
            records.Add(record);
            versions[record.Info.Id] = [new InvoiceTemplateVersionInfo(1, clean.Note, actor, DateTime.UtcNow)];
            return Task.FromResult(record);
        }
        public Task<InvoiceTemplateRecord> SaveNewVersionAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
        {
            var clean = InvoiceTemplateRules.Clean(input);
            var index = records.FindIndex(item => item.Info.Id == original.Id && item.Info.Version == original.Version);
            if (index < 0) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            var info = original with { Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui, VersionNumber = original.VersionNumber + 1, Version = original.Version + 1, UpdatedBy = actor };
            records[index] = new InvoiceTemplateRecord(info, clean.Definition!);
            versions[original.Id].Add(new InvoiceTemplateVersionInfo(info.VersionNumber, clean.Note, actor, DateTime.UtcNow));
            return Task.FromResult(records[index]);
        }
        public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
        {
            var clean = InvoiceTemplateRules.Clean(input);
            var index = records.FindIndex(item => item.Info.Id == original.Id && item.Info.Version == original.Version);
            if (index < 0) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            var info = original with { Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui, Version = original.Version + 1, UpdatedBy = actor };
            records[index] = records[index] with { Info = info };
            return Task.FromResult(info);
        }
        public Task DeleteAsync(InvoiceTemplateInfo original, CancellationToken cancellationToken = default)
        {
            if (records.RemoveAll(item => item.Info.Id == original.Id && item.Info.Version == original.Version) != 1) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            versions.Remove(original.Id);
            return Task.CompletedTask;
        }
    }

    private static InvoiceTemplateDefinition SampleDefinition() =>
        new(InvoiceTemplateDefinition.CurrentSchema, InvoiceSources.Text, 595, 842, [],
            [new InvoiceTemplateField("f1", InvoiceFieldMeanings.InvoiceNumber, "Număr factură", true, 1, 0.1, 0.1, 0.1, 0.02, "Nr. factura", "text", false, InvoiceFieldModes.Right, 0.05, 0.1)],
            new InvoiceTemplateTable(1, 0.3, 0.32, InvoiceRowSplit.Top, true, "",
                [new InvoiceTemplateColumn("c1", "Denumire", InvoiceColumnMeanings.Name, true, 0.1, 0.5, InvoiceRowMapping.Band, false)]));

    private static async Task ServiceAsync(Action<bool, string> check)
    {
        var trail = new TestAuditTrail();
        var store = new MemoryTemplateStore();
        var admin = new InvoiceTemplateService(store, new TestAccessControl(true, "ana"), trail);
        var limited = new InvoiceTemplateService(store, new TestAccessControl(false, "ion"), trail);
        async Task<bool> Throws<T>(Func<Task> action) where T : Exception { try { await action(); return false; } catch (T) { return true; } }

        var created = await admin.CreateAsync(new InvoiceTemplateInput { Name = "  Delta PDF  ", SupplierName = "Delta SRL", SupplierCui = "RO 12345678", Note = "prima", Definition = SampleDefinition() });
        check(created.Info.Name == "Delta PDF" && created.Info.SupplierCui == "12345678" && created.Info.VersionNumber == 1 && trail.Entries.Last() is
              { Action: var createAction, EntityType: var createEntity, EntityId: var createId, Details: var createDetails } && createAction == AuditActions.CreateInvoiceTemplate && createEntity == AuditEntities.InvoiceTemplate &&
              createId == created.Info.Id.ToString(CultureInfo.InvariantCulture) && createDetails.Contains("Delta SRL", StringComparison.Ordinal) && createDetails.Contains("câmpuri folosite: 1", StringComparison.Ordinal),
            "Invoice template service: a template is saved with its supplier's tax id reduced to digits and the journal names the exact operation");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "delta pdf", SupplierName = "Delta SRL", SupplierCui = "12345678", Definition = SampleDefinition() })),
            "Invoice template service: a name is unique per supplier without regard to case");
        var sameNameOtherSupplier = await admin.CreateAsync(new InvoiceTemplateInput { Name = "Delta PDF", SupplierName = "Alt furnizor", SupplierCui = "99999999", Definition = SampleDefinition() });
        check(sameNameOtherSupplier.Info.Id != created.Info.Id, "Invoice template service: the same name may exist for another supplier (several templates per supplier, names unique inside one)");
        var second = await admin.CreateAsync(new InvoiceTemplateInput { Name = "Delta scanat", SupplierName = "Delta SRL", SupplierCui = "12345678", Definition = SampleDefinition() with { SourceKind = InvoiceSources.Ocr } });
        check(second.Info.SourceKind == InvoiceSources.Ocr && (await admin.ListAsync()).Count(item => item.SupplierCui == "12345678") == 2, "Invoice template service: a supplier can have several templates (for example a text PDF and a scan)");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = " ", Definition = SampleDefinition() })) &&
              await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = new string('x', 121), Definition = SampleDefinition() })) &&
              await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "Fără conținut" })),
            "Invoice template service: an empty name, a name over 120 characters and an empty definition are refused");
        check(await Throws<AccessDeniedException>(() => limited.CreateAsync(new InvoiceTemplateInput { Name = "X", Definition = SampleDefinition() })) &&
              await Throws<AccessDeniedException>(() => limited.DeleteAsync(created.Info, "motiv")), "Invoice template service: only an administrator changes templates");

        var saved = await admin.SaveNewVersionAsync(created.Info, new InvoiceTemplateInput { Name = "Delta PDF v2", SupplierName = "Delta SRL", SupplierCui = "12345678", Note = "a doua", Definition = SampleDefinition() });
        check(saved.Info.VersionNumber == 2 && (await admin.GetVersionsAsync(created.Info.Id)).Select(item => item.VersionNumber).SequenceEqual([2, 1]) && trail.Entries.Last() is { Action: var versionAction, Details: var versionDetails } &&
              versionAction == AuditActions.EditInvoiceTemplate && versionDetails.Contains("Versiune: 1 → 2", StringComparison.Ordinal) && versionDetails.Contains("Denumire: Delta PDF → Delta PDF v2", StringComparison.Ordinal),
            "Invoice template service: a new version keeps the earlier one, and the journal has the version and what changed");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.SaveNewVersionAsync(created.Info, new InvoiceTemplateInput { Name = "Cu versiune veche", Definition = SampleDefinition() })),
            "Invoice template service: saving over a template that was changed in the meantime is refused");
        var renamed = await admin.UpdateDetailsAsync(saved.Info, new InvoiceTemplateInput { Name = "Delta PDF v2", SupplierName = "Delta Instalatii SRL", SupplierCui = "12345678" });
        check(renamed.SupplierName == "Delta Instalatii SRL" && renamed.VersionNumber == 2 && trail.Entries.Last() is { Action: var detailsAction, Details: var detailsText } && detailsAction == AuditActions.EditInvoiceTemplateDetails &&
              detailsText.Contains("Furnizor: Delta SRL (CUI 12345678) → Delta Instalatii SRL (CUI 12345678)", StringComparison.Ordinal),
            "Invoice template service: changing only the name or supplier is its own journal operation and does not make a version");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.UpdateDetailsAsync(renamed, new InvoiceTemplateInput { Name = "Delta scanat", SupplierName = "Delta SRL", SupplierCui = "12345678" })),
            "Invoice template service: renaming to a name already used by the supplier is refused");
        await admin.DeleteAsync(renamed, "Nu mai este folosit");
        check(trail.Entries.Last() is { Action: var deleteAction, Motif: var motif, EntityId: var deletedId } && deleteAction == AuditActions.DeleteInvoiceTemplate && motif == "Nu mai este folosit" && deletedId == created.Info.Id.ToString(CultureInfo.InvariantCulture) &&
              (await admin.GetAsync(created.Info.Id)) is null, "Invoice template service: deleting is journaled with its reason and removes the template");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.DeleteAsync(renamed, "din nou")), "Invoice template service: deleting a template twice is refused");
    }

    // ---- journal rules ----
    private static void AuditRules(Action<bool, string> check)
    {
        var actions = new[] { AuditActions.CreateInvoiceTemplate, AuditActions.EditInvoiceTemplate, AuditActions.EditInvoiceTemplateDetails, AuditActions.DeleteInvoiceTemplate };
        check(actions.Distinct().Count() == 4 && actions.Take(3).All(AuditActions.IsCreateOrEdit) && !AuditActions.IsCreateOrEdit(AuditActions.DeleteInvoiceTemplate) && AuditActions.IsDeletion(AuditActions.DeleteInvoiceTemplate) &&
              AuditActions.IsDeletion(AuditActions.Delete) && !AuditActions.IsDeletion(AuditActions.Edit) && actions.All(action => AuditFilterOptions.Actions.Any(option => option.Value == action)) &&
              AuditFilterOptions.Entities.Any(option => option.Value == AuditEntities.InvoiceTemplate),
            "Invoice templates journal: four distinct exact operations, in the journal filter; creations and edits link to the template, deletions do not");
        var created = new AuditEvent(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5), "ana", AccessRoles.Administrator, AuditEntities.InvoiceTemplate, AuditActions.CreateInvoiceTemplate, "Delta", "d", "", "17");
        var deleted = created with { Id = Guid.NewGuid(), TimestampUtc = DateTime.UtcNow, Action = AuditActions.DeleteInvoiceTemplate };
        check(AuditNavigation.TargetUrl(created) == "/setari?tab=facturi&subtab=sabloane&sablonfactura=17" && AuditNavigation.TargetUrl(created, AuditNavigation.RemovalTimes([created, deleted])) is null,
            "Invoice templates journal: an event links to the saved template in Settings, and stops linking once the template is deleted");
    }
}
