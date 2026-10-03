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
        SeparatorsOnRules(check);
        ProductMatching(check);
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
        Separators(check, roDocument, roAnalysis, ro.Rows, "ruled Romanian invoice");
        var roDraft = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        var ownPrice = roDraft.Columns.First(column => column.Meaning == InvoiceColumnMeanings.UnitPrice).Label;
        check(roDraft.ProductDescription.StartsWith("<" + ownPrice + "> ", StringComparison.Ordinal) &&
              InvoiceProductDescription.UnknownMarks(roDraft.ProductDescription, InvoiceProductDescription.Labels(roDraft).Select(item => item.Label)).Count == 0 &&
              InvoiceProductDescription.ColumnLabels(roDraft).Select(item => item.Label).SequenceEqual(roDraft.Columns.Where(column => column.Use && column.Meaning != InvoiceColumnMeanings.Ignore).Select(column => column.Label)),
            "Invoice templates: a new template's product description starts as the unit price, invoice number, date and supplier; every used column is a label under the name it has in the template (its header text in the file), none is mandatory");
        check(roDraft.Fields.Where(field => field.Use && field.LabelText.Trim().Length > 0).All(field => InvoiceTemplateDraft.EffectiveLabel(field).StartsWith(field.LabelText.Trim().TrimEnd(':', ' '), StringComparison.Ordinal)),
            "Invoice analysis: the label of a found field is the text the file itself has beside it, not a title of a general vocabulary");
        // A used column keeps its own name as label, whatever its meaning ("Taxa verde" with the meaning VAT rate is <Taxa verde>).
        var vatDraft = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        vatDraft.Columns.Add(new DraftColumn { Id = "vat2", Label = "Taxa verde", Meaning = InvoiceColumnMeanings.VatRate, Use = true });
        check(InvoiceProductDescription.ColumnLabels(vatDraft).Any(item => item.Label == "Taxa verde" && item.Column?.Id == "vat2") &&
              !InvoiceProductDescription.ColumnLabels(vatDraft).Any(item => item.Label == InvoiceVocabulary.ColumnTitle(InvoiceColumnMeanings.VatRate)),
            "Invoice templates: a used column is offered under the name it has in the template, not under the title of its meaning in a general vocabulary");
        // Taking a column off the template takes its marks out of the description.
        vatDraft.ProductDescription = "<Taxa verde> si <Denumire> <Taxa verde>";
        check(InvoiceProductDescription.RemoveMark(vatDraft.ProductDescription, "Taxa verde") == "si <Denumire>", "Invoice templates: removing a label's marks from the description leaves the rest of the text");
        check(InvoiceProductDescription.RenameMark("<Preț> x <preț> ADUNARE{<Preț> <Cant>}", "Preț", "Pret unitar") == "<Pret unitar> x <Pret unitar> ADUNARE{<Pret unitar> <Cant>}",
            "Invoice templates: renaming a label renames its marks everywhere in the description, also inside ADUNARE{}");
        check(InvoiceProductDescription.RenderSample(roDraft, null).StartsWith("‹" + ownPrice.ToLowerInvariant() + "› ", StringComparison.Ordinal),
            "Invoice templates: the unit price label has no value in the sample when no row is read, like the product code");
        // Arithmetic in the description: ADUNARE{<label> <label>} is replaced by the sum of the values of its labels.
        string? SampleValue(string label) => label switch { "Preț unitar" => "437,06", "Cota TVA" => "91,78", "Cod produs" => "GS1", "Număr factură" => "x", _ => null };
        check(InvoiceProductDescription.Render("<Cod produs>: ADUNARE{<Preț unitar> <Cota TVA>} lei", SampleValue) == "GS1: 528.84 lei" &&
              InvoiceProductDescription.Render("ADUNARE{<Preț unitar><Cota TVA><Preț unitar>}", SampleValue) == "965.9" &&
              InvoiceProductDescription.Render("ADUNARE{<Preț unitar> <Număr factură>}", SampleValue) == "ADUNARE{437,06 x}" &&
              InvoiceProductDescription.Render("ADUNARE{<Preț unitar> <Necunoscut>}", SampleValue) == "ADUNARE{437,06 <Necunoscut>}",
            "Invoice templates: ADUNARE{} sums the values of the labels inside it, in any amount, and is left as written when a value is missing or is not a number");
        check(InvoiceProductDescription.OperationProblems("ADUNARE{<A> <B>} si ADUNARE{<A> <B> <C>}").Count == 0 &&
              InvoiceProductDescription.OperationProblems("ADUNARE{<A>}").Count == 1 && InvoiceProductDescription.OperationProblems("ADUNARE{<A> <B>").Count == 1 &&
              InvoiceProductDescription.OperationProblems("ADUNARE{<A> text <B>}").Count == 1 && InvoiceProductDescription.OperationProblems("<A> <B>").Count == 0,
            "Invoice templates: an ADUNARE{} with fewer than two labels, with text inside or not closed is a problem that stops the save");
        // The other operations: subtraction (first minus the rest), multiplication, division (exactly two labels, never by zero); the symbol stands between the labels.
        string? Number(string label) => label switch { "A" => "10", "B" => "4", "C" => "0,5", "Z" => "0", _ => null };
        check(InvoiceProductDescription.Render("SCADERE{<A> - <B>} INMULTIRE{<A> * <B> * <C>} IMPARTIRE{<A> / <B>} ADUNARE{<A> + <B>}", Number) == "6 20 2.5 14" &&
              InvoiceProductDescription.Render("SCADERE{<A> - <B> - <C>}", Number) == "5.5" &&
              InvoiceProductDescription.Render("IMPARTIRE{<A> / <Z>}", Number) == "IMPARTIRE{10 / 0}" &&
              InvoiceProductDescription.Render("IMPARTIRE{<A> / <Necunoscut>}", Number) == "IMPARTIRE{10 / <Necunoscut>}",
            "Invoice templates: SCADERE{}, INMULTIRE{} and IMPARTIRE{} calculate with the labels inside them; a division by zero or with a missing value is left as written");
        check(InvoiceProductDescription.OperationProblems("SCADERE{<A> - <B>} INMULTIRE{<A> * <B> * <C>} IMPARTIRE{<A> / <B>} ADUNARE{<A> + <B>}").Count == 0 &&
              InvoiceProductDescription.OperationProblems("IMPARTIRE{<A> / <B> / <C>}").Count == 1 && InvoiceProductDescription.OperationProblems("IMPARTIRE{<A>}").Count == 1 &&
              InvoiceProductDescription.OperationProblems("SCADERE{<A> + <B>}").Count == 1 && InvoiceProductDescription.OperationProblems("ADUNARE{<A> - <B>}").Count == 1,
            "Invoice templates: a division takes exactly two labels, and each operation accepts only its own symbol between the labels");
        roDraft.ProductDescription = "ADUNARE{<Cod produs>}";
        check(roDraft.Problems().Any(problem => problem.Contains("ADUNARE", StringComparison.Ordinal)), "Invoice templates: the problems of a template include its arithmetic operations");
        check(InvoiceProductDescription.Upgrade("<Valoare Preț unitar> x <Preț unitar> <Valoare Cod produs>", roDraft) == "<" + ownPrice + "> x <" + ownPrice + "> <Cod produs>",
            "Invoice templates: a description written with the general titles of the columns (<Valoare Preț unitar>, <Preț unitar>) is brought to the name each column has in the template");
        roDraft.ProductDescription = InvoiceProductDescription.Default(roDraft);
        // Editing a saved template shows it exactly as saved on a file of another layout (no alignment, no search of the header in the file).
        var savedDefinition = roDraft.ToDefinition(roDocument);
        var shiftedDocument = new InvoiceDocument([.. roDocument.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Y = word.Y + 37, X = word.X + 11 })] })]);
        var asSaved = InvoiceTemplateDraft.FromSaved(savedDefinition, shiftedDocument, "", "");
        check(asSaved.HeaderBottom == roDraft.HeaderBottom && asSaved.Columns.Zip(roDraft.Columns.OrderBy(column => column.Left)).All(pair => Math.Abs(pair.First.Left - pair.Second.Left) < 1e-6 && Math.Abs(pair.First.Right - pair.Second.Right) < 1e-6) &&
              asSaved.Fields.Zip(roDraft.Fields).All(pair => Math.Abs(pair.First.X - pair.Second.X) < 1e-6 && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-6) && asSaved.ProductDescription == roDraft.ProductDescription,
            "Invoice templates: a saved template is opened for editing exactly as saved (positions are the saved ones even on a shifted file), not aligned by the analysis");
        var aligned = InvoiceTemplateDraft.FromDefinition(savedDefinition, shiftedDocument, "", "");
        check(Math.Abs(aligned.HeaderBottom - roDraft.HeaderBottom) > 1, "Invoice templates: aligning the template with the file (the analysis the user asks for) does move the elements on a shifted file");
        // Where the rows ended is kept with the template (the columns are drawn down to it when the template is opened for editing).
        roDraft.BodyBottom = 400;
        var withBody = roDraft.ToDefinition(roDocument);
        var bodyJson = InvoiceTemplateJson.Serialize(withBody);
        var legacyJson = System.Text.RegularExpressions.Regex.Replace(bodyJson, ",\"bodyBottom\":[0-9.eE+-]+", "");
        check(Math.Abs(InvoiceTemplateDraft.FromSaved(InvoiceTemplateJson.Deserialize(bodyJson), roDocument, "", "").BodyBottom - 400) < 1e-6 &&
              legacyJson != bodyJson && InvoiceTemplateJson.Deserialize(legacyJson).Table!.BodyBottom == 0,
            "Invoice templates: the foot of the table body is saved with the template and read back; a template saved before it existed opens with it unknown");
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
        var (enDocument, _) = await Analyze(en.Pdf);
        Separators(check, enDocument, enAnalysis, en.Rows, "English invoice without rules");
        check(Field(enAnalysis, InvoiceFieldMeanings.InvoiceNumber) == "INV-2026-77" && Field(enAnalysis, InvoiceFieldMeanings.InvoiceDate) == "2026-11-20" &&
              Field(enAnalysis, InvoiceFieldMeanings.Currency) == "EUR" && Field(enAnalysis, InvoiceFieldMeanings.SupplierName) == "Northern Cables Ltd",
            "Invoice analysis: the fields of an English invoice are found by their English labels");

        // ---- the data zone of a column: anchored to the elements above and below it (their text), not to coordinates ----
        {
            var zoneInvoice = InvoiceFixtures.Make(new InvoiceSpec("en-plain", 6), InvoiceFixtures.MakeRows(6, 31));
            var (zoneDocument, zoneAnalysis) = await Analyze(zoneInvoice.Pdf);
            var zoneDraft = InvoiceTemplateDraft.FromAnalysis(zoneAnalysis);
            var zonePage = zoneDocument.Pages[0];
            var rowsOnPage = zoneAnalysis.Table!.Rows;
            // The zone of every column starts under row 2 and ends over row 5: rows 3 and 4 are what is read.
            foreach (var column in zoneDraft.Columns)
            {
                column.Top = rowsOnPage[2].Top;
                column.TopAnchor = InvoiceTemplateEngine.AnchorAbove(zonePage, column.Left, column.Right, column.Top);
                column.Bottom = rowsOnPage[4].Top;
                column.BottomAnchor = InvoiceTemplateEngine.AnchorBelow(zonePage, column.Left, column.Right, column.Bottom);
            }
            var zoneDefinition = zoneDraft.ToDefinition(zoneDocument);
            check(zoneDefinition.Table!.Columns.All(column => column.TopAnchor.Length > 0 && column.BottomAnchor.Length > 0 && column.Top > 0 && column.Bottom > column.Top),
                "Invoice templates: the data zone of a column is saved with the text of the element above and the element below it");
            string Names(InvoiceExtraction extraction) => string.Join("|", extraction.Rows.Select(row => row.Cells[extraction.Columns.First(column => column.Meaning == InvoiceColumnMeanings.Name).Id]));
            var expectedNames = string.Join("|", zoneInvoice.Rows.Skip(2).Take(2).Select(row => row.Name));
            check(Names(InvoiceTemplateEngine.Apply(zoneDefinition, zoneDocument)) == expectedNames,
                "Invoice templates: only the rows inside the data zone of the columns are read (from the element above its start to the element under its end)");
            // The same invoice with everything moved down and sideways: the zone follows the elements, not the coordinates.
            var movedDocument = new InvoiceDocument([.. zoneDocument.Pages.Select(page => page with { Words = [.. page.Words.Select(word => word with { Y = word.Y + 41, X = word.X + 9 })] })]);
            check(Names(InvoiceTemplateEngine.Apply(zoneDefinition, movedDocument)) == expectedNames,
                "Invoice templates: the data zone is found again from the elements above and below it when the invoice is shifted (not from fixed coordinates)");
            zoneDraft.Columns.ForEach(column => { column.TopAnchor = "text care nu exista"; column.BottomAnchor = "alt text care nu exista"; });
            check(InvoiceTemplateEngine.Apply(zoneDraft.ToDefinition(zoneDocument), zoneDocument).Rows.Count == zoneInvoice.Rows.Count,
                "Invoice templates: a zone whose anchor elements are not in the file is automatic (the whole table is read), never a fixed position");
        }

        {
            var framed = new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, [], [new InvoiceRule(false, 240.4, 67, 528), new InvoiceRule(false, 261.0, 67, 528), new InvoiceRule(false, 283.0, 67, 528)]);
            var (interiorTop, interiorBottom) = InvoiceTemplateDraft.HeaderInterior(framed, 243.8, 257.8, 67, 528);
            var (plainTop, plainBottom) = InvoiceTemplateDraft.HeaderInterior(new InvoicePageData(1, 595, 842, InvoiceSources.Text, []), 243.8, 257.8, 67, 528);
            check(Math.Abs(interiorTop - 241.9) < 0.01 && Math.Abs(interiorBottom - 259.5) < 0.01 && plainTop == 243.8 && plainBottom == 257.8,
                "Invoice templates: a header framed by rules spans the inside of its rectangle (kept away from the lines); without rules it keeps the extent of its words");
        }
        check(InvoiceVocabulary.MatchColumn("Taxa verde").Meaning != InvoiceColumnMeanings.VatRate && InvoiceVocabulary.MatchColumn("Taxa TVA").Meaning == InvoiceColumnMeanings.VatRate,
            "Invoice vocabulary: a heading that only starts with \"Taxa\" (\"Taxa verde\") is not the VAT rate; it stays a column of its own");
        {
            var ownColumns = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
            ownColumns.Columns.Add(new DraftColumn { Id = "g1", Label = "Taxa verde", Meaning = InvoiceColumnMeanings.Other, Use = true });
            ownColumns.Columns.Add(new DraftColumn { Id = "g2", Label = "Taxa mediu", Meaning = InvoiceColumnMeanings.Other, Use = true });
            check(!ownColumns.Problems().Any(problem => problem.Contains("sunt citite cu același rol", StringComparison.Ordinal)) &&
                  InvoiceProductDescription.ColumnLabels(ownColumns).Count(item => item.Label is "Taxa verde" or "Taxa mediu") == 2,
                "Invoice templates: several columns can have the meaning \"other column\"; each is a label under its own name");
        }
        // ---- the header cells are the columns: a cell whose zone was not drawn stops the save; the cell's extent is kept ----
        {
            var cellDraft = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
            cellDraft.Columns[0].ZoneDrawn = false;
            check(cellDraft.Problems().Any(problem => problem.Contains("nu are zona coloanei desenată", StringComparison.Ordinal)),
                "Invoice templates: a header cell whose column zone was not drawn is a problem that stops the save");
            cellDraft.Columns[0].ZoneDrawn = true;
            cellDraft.Columns[1].CellTop = cellDraft.HeaderTop + 1; cellDraft.Columns[1].CellBottom = cellDraft.HeaderBottom - 1;
            var cellBack = InvoiceTemplateDraft.FromSaved(cellDraft.ToDefinition(roDocument), roDocument, "", "");
            check(!cellDraft.Problems().Any(problem => problem.Contains("zona coloanei", StringComparison.Ordinal)) &&
                  cellBack.Columns.Any(column => column.Id == cellDraft.Columns[1].Id && Math.Abs(column.CellTop - cellDraft.Columns[1].CellTop) < 1e-6 && Math.Abs(column.CellBottom - cellDraft.Columns[1].CellBottom) < 1e-6 && column.ZoneDrawn),
                "Invoice templates: the extent of a header cell and the drawn zone survive saving and opening the template");
        }

        // ---- the table of a Romanian scan: a row of column numbers under the header, the cells of a row centred on its lines (name above and below the number) ----
        foreach (var style in new[] { "ro-lines", "en-plain" })
        {
            var centred = InvoiceFixtures.Make(new InvoiceSpec(style, 6, WrapNamesAt: 24, ColumnNumbers: true, CenterRows: true), InvoiceFixtures.MakeRows(6, 21));
            var (_, centredAnalysis) = await Analyze(centred.Pdf);
            check(RowsMatch(centredAnalysis, centred.Rows, style == "ro-lines" ? ',' : '.'),
                $"Invoice analysis ({style}): a row of column numbers under the header is not a row of goods, and the values of a row sit on the number line in the middle of its wrapped name (no shifting between rows)");
        }

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
        // The description of a stock entry is the template's text with the invoice's fields and the row's cells put in (step 3 of the invoice pickup).
        var descriptionDraft = InvoiceTemplateDraft.FromDefinition(definition, otherDocument, "", "");
        var firstRow = extraction.Rows[0];
        var renderedDescription = InvoiceProductDescription.RenderRow(descriptionDraft, extraction.Fields, new Dictionary<string, string>(firstRow.Cells) { ["code"] = otherRows[0].Name }, ',');
        check(renderedDescription.Length > 0 && !renderedDescription.Contains('<') && renderedDescription.Contains("SE 77", StringComparison.Ordinal) && renderedDescription.Contains("Sigma Electro SRL", StringComparison.Ordinal),
            "Invoice pickup: the description of an entry is generated from the template (invoice number and supplier from the fields, the row's values from its cells)");
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
            [new InvoiceTemplateRecord(new InvoiceTemplateInfo(1, "Delta", "Delta Instalatii SRL", "12345678", "text", true, 0, "a", DateTime.UtcNow, "a", DateTime.UtcNow), definition)], otherDocument);
        check(suggestions.Count == 1 && suggestions[0].Match.Score >= InvoiceTemplateSuggestions.MinLayoutScore, "Invoice template: a saved template is suggested for a file with the same layout");

        // a template placed on a file keeps the user's regions (draft from definition)
        var redraft = InvoiceTemplateDraft.FromDefinition(definition, otherDocument, "Delta", "12345678");
        check(redraft.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber).Value == "SE 77" && redraft.Columns.Count(column => column.Use) == 6 && redraft.Problems().Count == 0,
            "Invoice template: a saved template opened on another file shows that file's values in its regions");

        // ---- drafts: problems that stop a save ----
        var broken = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        foreach (var field in broken.Fields) field.Use = false;
        foreach (var column in broken.Columns) column.Use = false;
        broken.ProductDescription = "";
        check(broken.Problems().Count == 1, "Invoice template: a draft with nothing used cannot be saved");
        broken.Columns.First(column => column.Meaning == InvoiceColumnMeanings.Quantity).Use = true;
        check(broken.Problems().Any(problem => problem.Contains("denumirea sau cu codul", StringComparison.Ordinal)), "Invoice template: a table without a name or code column cannot be saved");
        var twice = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber).Use = true;
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceDate).Meaning = InvoiceFieldMeanings.InvoiceNumber;
        twice.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber && field.Id != twice.Fields.First(f => f.Meaning == InvoiceFieldMeanings.InvoiceNumber).Id).Use = true;
        check(twice.Problems().Any(problem => problem.Contains("sunt citite cu același rol", StringComparison.Ordinal)), "Invoice template: one meaning on two used fields is refused");
        var sameLabel = InvoiceTemplateDraft.FromAnalysis(roAnalysis);
        var labelled = sameLabel.Fields.Where(field => field.Use).Take(2).ToList();
        foreach (var field in labelled) { field.Meaning = InvoiceFieldMeanings.Custom; field.Name = "Aceeași etichetă"; }
        check(labelled.Count == 2 && sameLabel.Problems().Any(problem => problem.Contains("mai multe câmpuri", StringComparison.Ordinal)), "Invoice template: two used fields with the same label are refused");
        labelled[1].Name = "Altă etichetă";
        check(!sameLabel.Problems().Any(problem => problem.Contains("mai multe câmpuri", StringComparison.Ordinal)), "Invoice template: different labels are accepted again");

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

    // Produse -> Preluare factura: the rows of the table are read between demarcation lines drawn where the invoice has none, and are read
    // again when the user moves, deletes or adds a line; what is under the last line is not read.
    private static void Separators(Action<bool, string> check, InvoiceDocument document, InvoiceAnalysis analysis, IReadOnlyList<FixtureRow> expected, string label)
    {
        var definition = InvoiceTemplateDraft.FromAnalysis(analysis).ToDefinition(document);
        var reading = InvoicePickupReader.Read(definition, document);
        var lines = reading.Separators.OrderBy(item => item.Y).ToList();
        var nameColumn = reading.Extraction.Columns.First(column => column.Meaning == InvoiceColumnMeanings.Name).Id;
        var count = expected.Count;

        check(lines.Count == count + 1 && lines.Zip(lines.Skip(1)).All(pair => pair.Second.Y > pair.First.Y),
            $"Invoice separators: a {label} gets one demarcation line above the first row and one under every row ({count + 1} lines)");
        var again = InvoicePickupReader.Reread(reading, document, lines);
        check(again.Count == count && string.Join("|", again.Select(row => row.Cells[nameColumn])) == string.Join("|", expected.Select(row => row.Name)),
            $"Invoice separators: reading the {label} between the proposed lines gives its rows exactly");

        var merged = lines.Where((_, index) => index != 2).ToList();
        var afterDelete = InvoicePickupReader.Reread(reading, document, merged);
        check(afterDelete.Count == count - 1 && afterDelete[1].Cells[nameColumn].Contains(expected[1].Name) && afterDelete[1].Cells[nameColumn].Contains(expected[2].Name),
            $"Invoice separators: deleting a line of the {label} merges the two rows it told apart, and the information is read again");
        var restored = InvoicePickupReader.Reread(reading, document, [.. merged, lines[2] with { Id = "new" }]);
        check(restored.Count == count && restored[2].Cells[nameColumn] == expected[2].Name, $"Invoice separators: adding the deleted line again splits the rows again ({label})");

        var withoutLast = InvoicePickupReader.Reread(reading, document, lines.Take(count).ToList());
        check(withoutLast.Count == count - 1 && withoutLast[^1].Cells[nameColumn] == expected[count - 2].Name,
            $"Invoice separators: what is under the last line is not read (the last row of the {label} is dropped with its line)");
        var moved = lines.Select((item, index) => index == 0 ? item with { Y = lines[1].Y } : item).ToList();
        var afterMove = InvoicePickupReader.Reread(reading, document, moved);
        check(afterMove.Count == count - 1 && afterMove[0].Cells[nameColumn] == expected[1].Name,
            $"Invoice separators: moving the first line under the first row leaves it out of the reading ({label})");
        check(InvoicePickupReader.Reread(reading, document, [lines[0]]).Count == 0, $"Invoice separators: a single line makes no row ({label})");
    }
    // A scanned ruled table: its rows are read from the text, but the demarcation lines go on the table's own rules (above the first row, between
    // rows, under the last), not on the text of the rows; without rules they go midway between two rows.
    private static void SeparatorsOnRules(Action<bool, string> check)
    {
        var columns = new List<InvoiceColumn> { new("a", "Denumire", InvoiceColumnMeanings.Name, 90, 280), new("b", "Valoare", InvoiceColumnMeanings.Value, 280, 430) };
        InvoiceTableRow Row(double top, double bottom) => new(1, null, new Dictionary<string, string>(), [], top, bottom);
        // Rows' text extents (two-line, one-line, two-line rows) inside boxes ruled at 283 / 303.7 / 324.5 / 345.2 / 366.
        var rows = new[] { Row(287, 299), Row(308, 318), Row(328, 341), Row(349, 362) };
        InvoiceRule[] rules = [new(false, 283, 67, 528), new(false, 303.7, 67, 528), new(false, 324.5, 67, 528), new(false, 345.2, 67, 528), new(false, 366, 67, 528), new(false, 240, 400, 410), new(true, 89, 240, 400)];
        var page = new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, [], rules);
        var ruled = InvoiceTableReader.SeparatorsFromRows(rows, columns, [page]).Select(item => item.Y).ToList();
        check(ruled.SequenceEqual([283, 303.7, 324.5, 345.2, 366]), "Invoice separators: the lines of a scanned ruled table sit on its rules, not on the text of the rows");
        var plain = InvoiceTableReader.SeparatorsFromRows(rows, columns, [page with { Rules = null }]).Select(item => item.Y).ToList();
        check(plain.Count == 5 && plain[1] == 303.5 && plain[2] == 323 && plain[3] == 345 && plain[0] == 287 && plain[4] == 362,
            "Invoice separators: without rules the line between two rows is drawn midway between them");
    }

    // Invoice pickup, step 2: the product of a row is the one with the same code (whatever the spacing, case or dashes); without it the closest
    // products are offered, best first, at most five.
    private static void ProductMatching(Action<bool, string> check)
    {
        var catalog = new List<Product>
        {
            new(1, "C", "S", "DS-UPS1000", "Sursa neintreruptibila UPS 1000VA", 3), new(2, "C", "S", "DS-UPS1500", "Sursa neintreruptibila UPS 1500VA", 0),
            new(3, "C", "S", "DS-7616NXI-K1", "NVR 4K 16 porturi", 1), new(4, "C", "S", "TND-O1-5G", "Access Point Bridge", 2), new(5, "C", "S", "RACK-6U", "Rack perete 6U", 0),
        };
        check(InvoiceProductMatcher.CodeOf(null, "DS-UPS1000 - Sursa neintreruptibila - UPS") == "DS-UPS1000" && InvoiceProductMatcher.CodeOf("X1", "alt nume") == "X1" && InvoiceProductMatcher.CodeOf("", "ABC 12 foo") == "ABC",
            "Invoice product matching: the code of a row is its code cell, else the start of its name");
        var exact = InvoiceProductMatcher.Match("ds ups-1000", "DS UPS 1000 - Sursa", catalog);
        check(exact.Exact?.Id == 1 && exact.Alternatives.All(candidate => candidate.Product.Id != 1), "Invoice product matching: an exact code is found whatever the spacing, case or dashes, and is not offered as an alternative");
        var close = InvoiceProductMatcher.Match("DS-UPS1200", "Sursa neintreruptibila", catalog);
        check(close.Exact is null && close.Alternatives.Count >= 2 && close.Alternatives[0].Product.Id is 1 or 2 && close.Alternatives.Count <= InvoiceProductMatcher.MaxAlternatives &&
              close.Alternatives.Zip(close.Alternatives.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score), "Invoice product matching: without an exact code the closest products are offered, best first");
        check(InvoiceProductMatcher.Match("ZZZ-9", "Ceva fara legatura cu catalogul", catalog).Alternatives.Count == 0 && InvoiceProductMatcher.Match("", "", catalog).Exact is null,
            "Invoice product matching: nothing is offered when nothing is close");
    }

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
        // The header fields are grouped by party: the same label means the supplier's or the buyer's attribute by the heading it stands under.
        check(InvoiceVocabulary.ResolveFieldMeaning("@address", InvoiceVocabulary.BuyerSection) == "buyer.address" &&
              InvoiceVocabulary.ResolveFieldMeaning("@phone", InvoiceVocabulary.SupplierSection) == "supplier.phone" &&
              InvoiceVocabulary.ResolveFieldMeaning("@address", null) == "" && InvoiceVocabulary.ResolveFieldMeaning(InvoiceFieldMeanings.Total, "supplier") == InvoiceFieldMeanings.Total &&
              InvoiceVocabulary.FieldMeanings.Any(item => item.Key == "buyer.registry") && InvoiceVocabulary.FieldMeanings.Any(item => item.Key == "supplier.iban"),
            "Invoice vocabulary: a party attribute (address, phone, bank...) belongs to the supplier or to the buyer by its heading; invoice-level labels do not");
        check(InvoiceVocabulary.MatchSectionLead("Furnizor: SC TELESYSTEM SRL") == InvoiceVocabulary.SupplierSection &&
              InvoiceVocabulary.MatchSectionLead("Cumparator: ELECTRIC STANDARD PREST SRL") == InvoiceVocabulary.BuyerSection && InvoiceVocabulary.MatchSectionLead("Furnizor:") is null &&
              InvoiceVocabulary.MatchSectionLead("Total: 10") is null,
            "Invoice vocabulary: a heading followed by the party's name on the same line (Furnizor: SC ... SRL) opens its section");
        // A table drawn with ruled lines: its columns are the boxes between the vertical rules and its rows the boxes between the horizontal
        // ones, so a scan that misreads the running numbers ("]" for 1) and splits a heading ("N r.") still reads right.
        InvoiceWord Word(string text, double x, double y, int order) => new(1, text, x, y, Math.Max(4, text.Length * 4.0), 8, order);
        var ruledWords = new List<InvoiceWord>
        {
            Word("N r.", 45, 100, 0), Word("Denumire produs", 100, 100, 1), Word("U.M.", 305, 100, 2), Word("Cant.", 350, 100, 3), Word("Pret unitar", 410, 100, 4), Word("Valoare", 480, 100, 5),
            Word("]", 50, 122, 6), Word("Switch", 100, 122, 7), Word("Buc", 305, 122, 8), Word("2", 355, 122, 9), Word("10.00", 420, 122, 10), Word("20.00", 490, 122, 11),
            Word("2", 50, 142, 12), Word("Cablu", 100, 142, 13), Word("Buc", 305, 142, 14), Word("3", 355, 142, 15), Word("5.00", 425, 142, 16), Word("15.00", 490, 142, 17),
            Word("3", 50, 162, 18), Word("Router", 100, 162, 19), Word("Buc", 305, 162, 20), Word("1", 355, 162, 21), Word("30.00", 420, 162, 22), Word("30.00", 490, 162, 23),
            Word("TOTAL", 100, 182, 24), Word("65.00", 490, 182, 25)
        };
        var ruledRules = new List<InvoiceRule>();
        foreach (var y in new[] { 95.0, 115, 135, 155, 175, 195 }) ruledRules.Add(new InvoiceRule(false, y, 40, 540));
        foreach (var x in new[] { 40.0, 70, 300, 340, 400, 470, 540 }) ruledRules.Add(new InvoiceRule(true, x, 95, 195));
        var ruledDocument = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, ruledWords, ruledRules)]);
        var (ruledTable, _) = InvoiceTableReader.Detect(ruledDocument, '.');
        var ruledName = ruledTable?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Name);
        var ruledValue = ruledTable?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
        check(ruledTable is not null && ruledName is not null && ruledValue is not null && ruledTable.Rows.Count == 3 &&
              ruledTable.Rows.Select(row => row.Cells[ruledName.Id]).SequenceEqual(["Switch", "Cablu", "Router"]) &&
              ruledTable.Rows.Select(row => row.Cells[ruledValue.Id]).SequenceEqual(["20.00", "15.00", "30.00"]) &&
              ruledTable.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Index && Math.Abs(column.Left - 41.5) < 0.1 && Math.Abs(column.Right - 68.5) < 0.1),
            "Invoice tables: with ruled lines the columns are the insides of the boxes between the vertical rules (not over the lines) and the rows those between the horizontal ones (garbled running numbers, split headings and the totals box do not matter)");
        check(InvoiceTableReader.Detect(new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Ocr, ruledWords)]), '.').Table is not null,
            "Invoice tables: the same page without rules is still read from the spacing of its words (the rules are a hint, never required)");
        check(!InvoiceVocabulary.IsExtraPartyAttribute("supplier.name") && !InvoiceVocabulary.IsExtraPartyAttribute("buyer.cui") && InvoiceVocabulary.IsExtraPartyAttribute("buyer.address") &&
              !InvoiceVocabulary.IsExtraPartyAttribute(InvoiceFieldMeanings.Total),
            "Invoice vocabulary: name, tax code and registry number are imported by default, the other party attributes only when ticked");
        var descriptionDraft = new InvoiceTemplateDraft
        {
            Fields = [new DraftField { Id = "f1", Meaning = InvoiceFieldMeanings.InvoiceNumber, Name = "Număr factură", Use = true, Page = 1, Width = 10, Height = 5 },
                      new DraftField { Id = "f2", Name = "Observație", Use = false, Page = 1, Width = 10, Height = 5 }],
            Columns = [new DraftColumn { Id = "c1", Label = "Denumire", Meaning = InvoiceColumnMeanings.Name, Use = true }, new DraftColumn { Id = "c2", Label = "Cod produs", Meaning = InvoiceColumnMeanings.Code, Use = true }],
            HasTable = true, ProductDescription = "<Cod produs> - <denumire> (factura <numar factura>)"
        };
        var descriptionLabels = InvoiceProductDescription.Labels(descriptionDraft).Select(item => item.Label).ToList();
        check(descriptionLabels.Contains("Cod produs") && descriptionLabels.Contains("Denumire") && descriptionLabels.Contains("Număr factură") && !descriptionLabels.Contains("Observație") && descriptionLabels.Distinct().Count() == descriptionLabels.Count,
            "Product description: the labels offered are the header fields in use and the values of the used table columns, each once; there are no mandatory labels");
        check(InvoiceProductDescription.Render(descriptionDraft.ProductDescription, label => InvoiceValues.Normalize(label) switch { "cod produs" => "GS-1", "denumire" => "Router", "numar factura" => "640", _ => null }) == "GS-1 - Router (factura 640)" &&
              InvoiceProductDescription.Render("<nimic> ok", _ => null) == "<nimic> ok" && InvoiceProductDescription.UnknownMarks("<Cod produs> <Observație> <x>", descriptionLabels).SequenceEqual(["Observație", "x"]),
            "Product description: marks are replaced by their values (case and diacritics ignored), unknown marks are left and reported");
        check(descriptionDraft.Problems().Any(problem => problem.Contains("<denumire>", StringComparison.Ordinal)) == false && new InvoiceTemplateDraft { Fields = descriptionDraft.Fields, ProductDescription = "<Observație>" }.Problems().Any(problem => problem.Contains("<Observație>", StringComparison.Ordinal)),
            "Product description: a mark that is not an active label of the template is a problem that blocks saving");
    }

    // How concentrated the ink of a picture is in rows when it is turned by the given angle (highest when the text lines are horizontal).
    private static double InkRowVariance(OpenCvSharp.Mat gray, double degrees)
    {
        using var binary = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.Threshold(gray, binary, 0, 255, OpenCvSharp.ThresholdTypes.BinaryInv | OpenCvSharp.ThresholdTypes.Otsu);
        using var matrix = OpenCvSharp.Cv2.GetRotationMatrix2D(new OpenCvSharp.Point2f(binary.Cols / 2f, binary.Rows / 2f), degrees, 1.0);
        using var rotated = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.WarpAffine(binary, rotated, matrix, binary.Size());
        using var rows = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.Reduce(rotated, rows, OpenCvSharp.ReduceDimension.Column, OpenCvSharp.ReduceTypes.Sum, OpenCvSharp.MatType.CV_32S);
        rows.GetArray(out int[] sums);
        var mean = sums.Average();
        return sums.Average(value => (value - mean) * (double)(value - mean));
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
            ("tilted scan (3.5 degrees)", InvoiceFixtures.Scan(source.Pdf, skewDegrees: 3.5)),
            ("scan turned 90 degrees", InvoiceFixtures.Scan(source.Pdf, rotation: 90)),
            ("scan upside down", InvoiceFixtures.Scan(source.Pdf, rotation: 180)),
            ("pale scan (ink at 22%)", InvoiceFixtures.Scan(source.Pdf, inkStrength: 0.22)),
            ("blurry scan (out of focus)", InvoiceFixtures.Scan(source.Pdf, blurSigma: 1.8)),
            ("scan at 600 dpi", InvoiceFixtures.Scan(source.Pdf, dpi: 600))
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
            // The picture shown under the elements is the straightened scan: its text lines are the most concentrated at 0 degrees.
            if (name.StartsWith("tilted", StringComparison.Ordinal))
            {
                using var picture = OpenCvSharp.Cv2.ImDecode(read.PagePreviews[0], OpenCvSharp.ImreadModes.Grayscale);
                var straight = InkRowVariance(picture, 0);
                check(!picture.Empty() && straight >= InkRowVariance(picture, -1.0) && straight >= InkRowVariance(picture, 1.0) && straight >= InkRowVariance(picture, 0.5) * 0.98 && straight >= InkRowVariance(picture, -0.5) * 0.98,
                    $"Invoice scan ({name}): the picture shown to the user is rotated so that its lines are horizontal and vertical");
            }
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
        foreach (var (name, document, analysis) in loaded)
        {
            var table = analysis.Table;
            // The decimal separator is the one the invoice itself uses ("3.149,16" is Romanian style, "2,328.64" English style).
            var hint = InvoiceValues.DecimalStyle(document.AllWords.Select(word => word.Text));
            var value = table?.Columns.FirstOrDefault(column => column.Meaning == InvoiceColumnMeanings.Value);
            var sum = table is null || value is null ? 0 : table.Rows.Sum(row => InvoiceValues.ParseNumber(row.Cells[value.Id], hint) ?? 0);
            var totalNet = InvoiceValues.ParseNumber(analysis.Fields.FirstOrDefault(field => field.Meaning == InvoiceFieldMeanings.TotalNet)?.Value, hint);
            check(table is { Rows.Count: > 0 } && table.Columns.Any(column => column.Meaning == InvoiceColumnMeanings.Name) && totalNet is not null && Math.Abs(sum - totalNet.Value) <= 0.05m &&
                  analysis.SupplierCui.Length > 0 && analysis.Fields.Any(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber),
                $"Real sample invoice [{name}]: table, rows summing to the total without VAT ({sum:0.00}), supplier tax id and number are found");
        }
        var failures = new List<string>();
        foreach (var source in loaded)
            // A template belongs to a kind of file (the text of a PDF, or the recognised words of a scan): a supplier has one template for
            // each, so a template is tried on files of its own kind.
            foreach (var destination in loaded.Where(item => item.Name != source.Name && item.Analysis.Source == source.Analysis.Source))
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
        private readonly Dictionary<int, InvoiceTemplateModel> models = [];
        private int nextId = 1;
        public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(models.GetValueOrDefault(id));
        private void KeepModel(int id, int version, InvoiceTemplateInput clean, string actor)
        {
            if (clean.ModelContent is { Length: > 0 } content) models[id] = new InvoiceTemplateModel(clean.ModelFileName, content, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant(), version, actor, DateTime.UtcNow);
        }
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateInfo>>(records.Select(item => item.Info).ToList());
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(records.FirstOrDefault(item => item.Info.Id == id));
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateRecord>>(records.ToList());
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
        {
            var clean = InvoiceTemplateRules.Clean(input);
            if (clean.Definition is null) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.DefinitionRequiredMessage);
            var record = new InvoiceTemplateRecord(new InvoiceTemplateInfo(nextId++, clean.Name, clean.SupplierName, clean.SupplierCui, clean.Definition!.SourceKind, true, 0, actor, DateTime.UtcNow, actor, DateTime.UtcNow), clean.Definition);
            records.Add(record);
            KeepModel(record.Info.Id, 1, clean, actor);
            return Task.FromResult(record);
        }
        public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, string actor, CancellationToken cancellationToken = default)
        {
            var clean = InvoiceTemplateRules.Clean(input);
            var index = records.FindIndex(item => item.Info.Id == original.Id && item.Info.Version == original.Version);
            if (index < 0) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            var info = original with { Name = clean.Name, SupplierName = clean.SupplierName, SupplierCui = clean.SupplierCui, Version = original.Version + 1, UpdatedBy = actor };
            records[index] = new InvoiceTemplateRecord(info, clean.Definition!);
            KeepModel(original.Id, 1, clean, actor);
            return Task.FromResult(records[index]);
        }
        public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, string actor, CancellationToken cancellationToken = default)
        {
            var index = records.FindIndex(item => item.Info.Id == original.Id && item.Info.Version == original.Version);
            if (index < 0) throw new InvoiceTemplateOperationException(InvoiceTemplateRules.ConcurrentMessage);
            var info = original with { Active = active, Version = original.Version + 1, UpdatedBy = actor };
            records[index] = records[index] with { Info = info };
            return Task.FromResult(info);
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

        var created = await admin.CreateAsync(new InvoiceTemplateInput { Name = "  Delta PDF  ", SupplierName = "Delta SRL", SupplierCui = "RO 12345678", Definition = SampleDefinition() });
        check(created.Info.Name == "Delta PDF" && created.Info.SupplierCui == "12345678" && created.Info.Active && trail.Entries.Last() is
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

        // The template is tied to the supplier (name and tax id) and keeps the invoice it was made from.
        check(await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "Fără furnizor", SupplierCui = "12345678", Definition = SampleDefinition() })) &&
              await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "Fără CUI", SupplierName = "Delta SRL", Definition = SampleDefinition() })) &&
              await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "CUI greșit", SupplierName = "Delta SRL", SupplierCui = "J40/1/2020", Definition = SampleDefinition() })),
            "Invoice template service: a template without the supplier's name or a valid tax id is refused (it is tied to the supplier)");
        var firstPdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 };
        var withModel = await admin.CreateAsync(new InvoiceTemplateInput { Name = "Model", SupplierName = "Model SRL", SupplierCui = "RO 777001", ModelFileName = "factura-1.pdf", ModelContent = firstPdf, Definition = SampleDefinition() });
        var storedModel = await admin.GetModelAsync(withModel.Info.Id);
        check(storedModel is not null && storedModel.FileName == "factura-1.pdf" && storedModel.Content.SequenceEqual(firstPdf) && storedModel.VersionNumber == 1 && trail.Entries.Last().Details.Contains("factura-1.pdf", StringComparison.Ordinal),
            "Invoice template service: the invoice used as model is saved with the template and named in the journal");
        var secondPdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 9, 9 };
        var newModel = await admin.SaveAsync(withModel.Info, new InvoiceTemplateInput { Name = "Model", SupplierName = "Model SRL", SupplierCui = "777001", ModelFileName = "factura-noua.pdf", ModelContent = secondPdf, Definition = SampleDefinition() });
        var replaced = await admin.GetModelAsync(withModel.Info.Id);
        check(replaced is not null && replaced.FileName == "factura-noua.pdf" && replaced.VersionNumber == 1 && trail.Entries.Last().Details.Contains("Factură model: factura-1.pdf → factura-noua.pdf", StringComparison.Ordinal),
            "Invoice template service: a new PDF uploaded while editing replaces the model, and the journal shows the change");
        await admin.SaveAsync(newModel.Info, new InvoiceTemplateInput { Name = "Model", SupplierName = "Model SRL", SupplierCui = "777001", Definition = SampleDefinition() });
        check((await admin.GetModelAsync(withModel.Info.Id))?.FileName == "factura-noua.pdf", "Invoice template service: a version saved without a new file keeps the model");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.CreateAsync(new InvoiceTemplateInput { Name = "Mare", SupplierName = "Mare SRL", SupplierCui = "888001", ModelContent = new byte[InvoiceTemplateRules.MaxModelBytes + 1], Definition = SampleDefinition() })),
            "Invoice template service: a model over 15 MB is refused");

        var saved = await admin.SaveAsync(created.Info, new InvoiceTemplateInput { Name = "Delta PDF v2", SupplierName = "Delta SRL", SupplierCui = "12345678", Definition = SampleDefinition() });
        check(trail.Entries.Last() is { Action: var versionAction, Details: var versionDetails } &&
              versionAction == AuditActions.EditInvoiceTemplate && !versionDetails.Contains("Versiune", StringComparison.Ordinal) && versionDetails.Contains("Denumire: Delta PDF → Delta PDF v2", StringComparison.Ordinal),
            "Invoice template service: saving replaces the template (no version is kept), and the journal has what changed");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.SaveAsync(created.Info, new InvoiceTemplateInput { Name = "Cu versiune veche", Definition = SampleDefinition() })),
            "Invoice template service: saving over a template that was changed in the meantime is refused");
        var renamed = await admin.UpdateDetailsAsync(saved.Info, new InvoiceTemplateInput { Name = "Delta PDF v2", SupplierName = "Delta Instalatii SRL", SupplierCui = "12345678" });
        check(renamed.SupplierName == "Delta Instalatii SRL" && trail.Entries.Last() is { Action: var detailsAction, Details: var detailsText } && detailsAction == AuditActions.EditInvoiceTemplateDetails &&
              detailsText.Contains("Furnizor: Delta SRL (CUI 12345678) → Delta Instalatii SRL (CUI 12345678)", StringComparison.Ordinal),
            "Invoice template service: changing only the name or supplier is its own journal operation");
        check(await Throws<InvoiceTemplateOperationException>(() => admin.UpdateDetailsAsync(renamed, new InvoiceTemplateInput { Name = "Delta scanat", SupplierName = "Delta SRL", SupplierCui = "12345678" })),
            "Invoice template service: renaming to a name already used by the supplier is refused");
        var switchedOff = await admin.SetActiveAsync(renamed, false);
        check(!switchedOff.Active && !(await admin.GetAsync(created.Info.Id))!.Info.Active && trail.Entries.Last() is { Action: var offAction, Details: var offDetails } && offAction == AuditActions.DeactivateInvoiceTemplate &&
              offDetails.Contains("Utilizat la citirea facturilor: da → nu", StringComparison.Ordinal),
            "Invoice template service: a template can be switched off (not used when invoices are read); the journal names the exact operation");
        check(InvoiceTemplateSuggestions.Rank([(await admin.GetAsync(created.Info.Id))!], new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, [])])).Count == 0,
            "Invoice template service: a template that is switched off is never proposed for a file");
        var switchedOn = await admin.SetActiveAsync(switchedOff, true);
        check(switchedOn.Active && trail.Entries.Last().Action == AuditActions.ActivateInvoiceTemplate && await Throws<AccessDeniedException>(() => limited.SetActiveAsync(switchedOn, false)),
            "Invoice template service: it can be switched on again, only by an administrator");
        renamed = switchedOn;
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
