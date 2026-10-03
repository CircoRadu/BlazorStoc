using BlazorStoc.Services;

// Corpus report of the invoice template engine. Usage:
//   dotnet run --project tests/BlazorStoc.InvoiceCorpus -- <file.pdf | directory> [--words] [--transfer]
// Default: for each PDF, the source of every page, the table found (columns with their meaning, rows with their cells and flags) and the
// header fields. --words also lists every word with its position. --transfer makes a template from each file (the analysis as proposed)
// and applies it to every other file, comparing what it reads with what the analysis of that file proposes - the test of a template on
// an invoice of another supplier with the same layout.
var target = args.FirstOrDefault(argument => !argument.StartsWith("--")) ?? ".";   // the file or directory comes first; the value after --template is read separately
var showWords = args.Contains("--words");
var transfer = args.Contains("--transfer");
var templateIndex = Array.IndexOf(args, "--template");
var templateFile = templateIndex >= 0 && templateIndex + 1 < args.Length ? args[templateIndex + 1] : null;
var files = Directory.Exists(target) ? Directory.GetFiles(target, "*.pdf", SearchOption.AllDirectories).OrderBy(file => file).ToArray() : [target];
var reader = new InvoicePdfReader(new CorpusTessdata());
var loaded = new List<(string Name, InvoiceDocument Document, InvoiceAnalysis Analysis)>();
foreach (var file in files)
{
    Console.WriteLine($"==== {Path.GetFileName(file)}");
    try
    {
        await using var stream = File.OpenRead(file);
        var read = await reader.ReadAsync(stream);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var analysis = InvoiceAnalyzer.Analyze(read.Document);
        Console.WriteLine($"  analysis {timer.ElapsedMilliseconds} ms");
        loaded.Add((Path.GetFileName(file), read.Document, analysis));
        foreach (var page in read.Document.Pages)
        {
            Console.WriteLine($"  page {page.Number}: {page.Source}, {page.Width:F0}x{page.Height:F0} pt, {page.Words.Count} words");
            if (showWords)
                foreach (var word in page.Words.OrderBy(word => Math.Round(word.CenterY / 3)).ThenBy(word => word.X))
                    Console.WriteLine($"    y={word.Y,6:F1} x={word.X,6:F1} w={word.Width,5:F1} h={word.Height,4:F1} o={word.Order,4} {word.Text}");
        }
        if (transfer) continue;
        if (templateFile is not null)
        {
            // The saved template (its JSON, e.g. the definition column of invoice_templates) applied as the pickup does: fields, rows, demarcation lines.
            var definition = InvoiceTemplateJson.Deserialize(File.ReadAllText(templateFile));
            var match = InvoiceTemplateEngine.Match(definition, analysis.SupplierCui, read.Document);
            Console.WriteLine($"  TEMPLATE MATCH layout {match.Score:P0}, supplier match {match.SupplierMatch}, file supplier CUI '{analysis.SupplierCui}'");
            var reading = InvoicePickupReader.Read(definition, read.Document);
            foreach (var field in reading.Extraction.Fields) Console.WriteLine($"  TEMPLATE FIELD {field.Meaning,-18} {field.Name} = {field.Value}");
            foreach (var column in reading.Extraction.Columns) Console.WriteLine($"  TEMPLATE COLUMN {column.Id} x={column.Left,6:F1}-{column.Right,6:F1} [{column.Meaning}] {column.Label}");
            foreach (var row in reading.Extraction.Rows)
                Console.WriteLine($"  TEMPLATE ROW p{row.Page} y={row.Top:F1}-{row.Bottom:F1}: " + string.Join(" | ", reading.Extraction.Columns.Select(column => row.Cells[column.Id])));
            foreach (var line in reading.Separators) Console.WriteLine($"  TEMPLATE LINE p{line.Page} y={line.Y:F1} x={line.Left:F0}-{line.Right:F0}");
            foreach (var warning in reading.Extraction.Warnings) Console.WriteLine("  TEMPLATE WARN " + warning);
            continue;
        }
        if (analysis.Table is { } table)
        {
            Console.WriteLine($"  TABLE page {table.HeaderPage}, header y={table.HeaderTop:F1}-{table.HeaderBottom:F1}, index={table.HasIndexColumn}, confidence={table.Confidence}");
            foreach (var column in table.Columns)
                Console.WriteLine($"    {column.Id} x={column.Left,6:F1}-{column.Right,6:F1} [{column.Meaning,-12}] {column.Label}");
            foreach (var row in table.Rows)
                Console.WriteLine($"    row p{row.Page} #{row.Number}: " + string.Join(" | ", table.Columns.Select(column => row.Cells[column.Id])) + (row.Flags.Count > 0 ? "   !! " + string.Join("; ", row.Flags) : ""));
        }
        else Console.WriteLine("  NO TABLE");
        foreach (var field in analysis.Fields)
            Console.WriteLine($"  FIELD [{field.Confidence:F1}] {field.Section,-8} {field.Meaning,-18} {field.Label} = {field.Value}");
        foreach (var warning in analysis.Warnings) Console.WriteLine("  WARN " + warning);
    }
    catch (Exception exception) { Console.WriteLine("  ERROR: " + exception); }
}

if (transfer)
{
    var failures = 0;
    foreach (var source in loaded)
        foreach (var destination in loaded.Where(item => item.Name != source.Name))
        {
            var draft = InvoiceTemplateDraft.FromAnalysis(source.Analysis);
            var definition = draft.ToDefinition(source.Document);
            // The template goes through its JSON form, as when it is saved and read back.
            definition = InvoiceTemplateJson.Deserialize(InvoiceTemplateJson.Serialize(definition));
            var extraction = InvoiceTemplateEngine.Apply(definition, destination.Document);
            var match = InvoiceTemplateEngine.Match(definition, source.Analysis.SupplierCui, destination.Document);
            Console.WriteLine($"---- template of [{source.Name}] on [{destination.Name}]: layout match {match.Score:P0}");
            foreach (var field in extraction.Fields.Where(field => field.Meaning.Length > 0))
            {
                var expected = destination.Analysis.Fields.FirstOrDefault(item => item.Meaning == field.Meaning)?.Value ?? "";
                var same = Normalize(expected) == Normalize(field.Value);
                if (!same) failures++;
                Console.WriteLine($"  {(same ? "ok  " : "DIFF")} {field.Meaning,-18} read='{field.Value}' expected='{expected}'");
            }
            if (destination.Analysis.Table is { } table && extraction.Columns.Count > 0)
            {
                string Cell(InvoiceTableRow row, IReadOnlyList<InvoiceColumn> columns, string meaning) =>
                    columns.FirstOrDefault(column => column.Meaning == meaning) is { } found && row.Cells.TryGetValue(found.Id, out var text) ? text : "";
                var expectedRows = table.Rows.Select(row => string.Join("|", new[] { InvoiceColumnMeanings.Name, InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value }.Select(meaning => Normalize(Cell(row, table.Columns, meaning))))).ToList();
                var readRows = extraction.Rows.Select(row => string.Join("|", new[] { InvoiceColumnMeanings.Name, InvoiceColumnMeanings.Quantity, InvoiceColumnMeanings.UnitPrice, InvoiceColumnMeanings.Value }.Select(meaning => Normalize(Cell(row, extraction.Columns, meaning))))).ToList();
                var rowsOk = expectedRows.SequenceEqual(readRows);
                if (!rowsOk) failures++;
                Console.WriteLine($"  {(rowsOk ? "ok  " : "DIFF")} rows: read {readRows.Count}, expected {expectedRows.Count}");
                if (!rowsOk)
                {
                    foreach (var row in readRows) Console.WriteLine("      read     " + row);
                    foreach (var row in expectedRows) Console.WriteLine("      expected " + row);
                }
            }
            foreach (var warning in extraction.Warnings) Console.WriteLine("  WARN " + warning);
        }
    Console.WriteLine(failures == 0 ? "TRANSFER: all fields and rows match" : $"TRANSFER: {failures} difference(s)");
}

static string Normalize(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

sealed class CorpusTessdata : IWebHostEnvironmentTessdataPath
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata");
}
