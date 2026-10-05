using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The OCR laboratory: the two table engines side by side, the reference geometry they are measured against, and the file store of templates.
public static class OcrLabChecks
{
    public static async Task RunAsync(Action<bool, string> check, InvoicePdfReader reader)
    {
        Console.WriteLine("=== OCR laboratory ===");
        check(InvoiceEngines.All.Select(engine => engine.Id).SequenceEqual(["A", "B"]) && InvoiceEngines.Find("b") == InvoiceEngines.B && InvoiceEngines.Find("x") is null &&
              InvoiceEngines.A.ValuesInScore && InvoiceEngines.A.InferMeanings && !InvoiceEngines.B.ValuesInScore && InvoiceEngines.B.GridFirst && !InvoiceEngines.A.GridFirst && InvoiceEngines.Default == InvoiceEngines.B,
            "OCR lab: engine A scores with the values; engine B looks for the table by its geometry first and does not score by values; B is the default");

        // Both engines read generated invoices of several layouts the same way.
        InvoiceSpec[] specs = [new(Rows: 6), new("en-plain", 7), new("ro-lines", 4, ExtraAddressLines: 3), new("en-plain", 35, RowsFirstPage: 20, RepeatHeader: true)];
        var identical = true;
        InvoiceDocument? sampleDocument = null;
        InvoiceAnalysis? sampleAnalysis = null;
        foreach (var spec in specs)
        {
            var invoice = InvoiceFixtures.Make(spec, InvoiceFixtures.MakeRows(spec.Rows, 5));
            using var stream = new MemoryStream(invoice.Pdf);
            var read = await reader.ReadAsync(stream);
            var a = InvoiceAnalyzer.Analyze(read.Document, InvoiceEngines.A);
            var b = InvoiceAnalyzer.Analyze(read.Document, InvoiceEngines.B);
            if (a.Table is null || b.Table is null || a.Table.Rows.Count != invoice.Rows.Count || b.Table.Rows.Count != invoice.Rows.Count ||
                !a.Table.Rows.Select(row => row.Top).SequenceEqual(b.Table.Rows.Select(row => row.Top))) identical = false;
            sampleDocument ??= read.Document;
            sampleAnalysis ??= a;
        }
        check(identical, "OCR lab: on clean generated invoices (ruled, plain, extra address lines, two pages) both engines find the rows and put them in the same places");

        // The reference: what an engine found, written as JSON, read back, and compared.
        var table = sampleAnalysis!.Table!;
        var separators = InvoiceTableReader.SeparatorsFromRows(table.Rows, table.Columns, sampleDocument!.Pages);
        var reference = InvoiceReference.From("proba.pdf", table, separators);
        var back = InvoiceReference.FromJson(reference.ToJson());
        check(back.Columns.Count == table.Columns.Count && back.Separators.Count == separators.Count && back.RowCount == table.Rows.Count && back.File == "proba.pdf" &&
              !reference.ToJson().Contains("Cells", StringComparison.OrdinalIgnoreCase),
            "OCR lab: a reference keeps the header, the columns and the lines between the rows (no cell values) and survives its JSON form");
        var same = InvoiceReferenceComparer.Compare(back, table, separators);
        check(same.TableFound && same.Score > 0.999 && same.SeparatorF1 > 0.999 && same.ColumnF1 > 0.999 && same.RowsFound == same.RowsExpected && same.ColumnMeaningsRight == same.ColumnsMatched,
            "OCR lab: a reading identical to the reference scores 1");
        back.Separators[2].Y += 10;   // a line that is 10 points away from where the reference wants it
        var moved = InvoiceReferenceComparer.Compare(back, table, separators);
        check(moved.Score < same.Score && moved.SeparatorRecall < 1 && moved.SeparatorPrecision < 1 && moved.ColumnF1 > 0.999 && moved.RowsFound == moved.RowsExpected,
            "OCR lab: a line out of place lowers the score of the lines only");
        back.Columns[1].Left += 150; back.Columns[1].Right += 150;   // the column is somewhere else on the page
        var column = InvoiceReferenceComparer.Compare(back, table, separators);
        check(column.ColumnRecall < 1 && column.ColumnF1 < 1 && column.Score < moved.Score, "OCR lab: a column out of place lowers the score of the columns");
        check(InvoiceReferenceComparer.Compare(back, null, []).Score == 0 && !InvoiceReferenceComparer.Compare(back, null, []).TableFound, "OCR lab: no table found scores 0");
        // Values do not count: the same geometry with other text in every cell scores the same.
        var changed = table with { Rows = [.. table.Rows.Select(row => row with { Cells = row.Cells.ToDictionary(cell => cell.Key, _ => "x") })] };
        check(Math.Abs(InvoiceReferenceComparer.Compare(InvoiceReference.FromJson(reference.ToJson()), changed, separators).Score - 1) < 0.0001, "OCR lab: what is read in the cells does not change the score");

        // The file store of templates.
        var directory = Path.Combine(Path.GetTempPath(), "blazorstoc-templates-" + Guid.NewGuid().ToString("N"));
        try
        {
            var definition = InvoiceTemplateDraft.FromAnalysis(sampleAnalysis).ToDefinition(sampleDocument);
            var store = new FileInvoiceTemplateStore(directory);
            var model = new byte[] { 37, 80, 68, 70, 1, 2, 3 };
            InvoiceTemplateInput Input(string name, bool withModel = false) => new()
            {
                Name = name, SupplierName = "Furnizor Test SRL", SupplierCui = "RO12345678", Definition = definition,
                ModelFileName = withModel ? "proba.pdf" : "", ModelContent = withModel ? model : null
            };
            var created = await store.CreateAsync(Input("Sablon proba", withModel: true), "lab");
            var second = await store.CreateAsync(Input("Alt sablon"), "lab");
            check(created.Info.Id == 1 && second.Info.Id == 2 && created.Info.Active && created.Info.Version == 0 && Directory.GetFiles(directory, "*.template.json").Length == 2 &&
                  File.Exists(Path.Combine(directory, "1.model.pdf")), "OCR lab: a template is one JSON file and its model PDF next to it, with the next free number");
            var reopened = new FileInvoiceTemplateStore(directory);   // another instance reads what is on disk
            var all = await reopened.GetAllAsync();
            var json = File.ReadAllText(Path.Combine(directory, "1.template.json"));
            check(all.Count == 2 && all[0].Info.Name == "Alt sablon" && InvoiceTemplateJson.Serialize(all.First(item => item.Info.Id == 1).Definition) == InvoiceTemplateJson.Serialize(definition) &&
                  json.Contains("\"definition\"") && json.Contains("\"supplierCui\": \"12345678\""),
                "OCR lab: templates come back from the folder as they were saved, the definition readable in the JSON file");
            var stored = await reopened.GetModelAsync(1);
            check(stored is not null && stored.Content.SequenceEqual(model) && stored.FileName == "proba.pdf" && await reopened.GetModelAsync(2) is null, "OCR lab: the model invoice is kept with its template");
            var duplicate = false;
            try { await store.CreateAsync(Input(" SABLON proba "), "lab"); }
            catch (InvoiceTemplateOperationException exception) { duplicate = exception.Message == InvoiceTemplateRules.DuplicateMessage; }
            check(duplicate, "OCR lab: the same name for the same supplier is refused");
            var off = await store.SetActiveAsync(created.Info, false, "lab");
            var stale = false;
            try { await store.SetActiveAsync(created.Info, true, "lab"); }   // read before the change: the version is old
            catch (InvoiceTemplateOperationException exception) { stale = exception.Message == InvoiceTemplateRules.ConcurrentMessage; }
            check(!off.Active && off.Version == 1 && stale && !(await new FileInvoiceTemplateStore(directory).GetAsync(1))!.Info.Active,
                "OCR lab: switching a template off is kept, and a change made on an old version is refused");
            var saved = await store.SaveAsync(off, Input("Sablon proba 2"), "lab");
            check(saved.Info.Version == 2 && saved.Info.Name == "Sablon proba 2" && await store.GetModelAsync(1) is not null, "OCR lab: saving a template again replaces it and keeps its model");
            await store.DeleteAsync(saved.Info);
            check((await store.ListAsync()).Count == 1 && !File.Exists(Path.Combine(directory, "1.model.pdf")) && !File.Exists(Path.Combine(directory, "1.template.json")),
                "OCR lab: deleting a template removes its files");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
