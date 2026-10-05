using BlazorStoc.Services;

// Corpus report of the invoice template engine. Usage:
//   dotnet run --project tests/BlazorStoc.InvoiceCorpus -- <file.pdf | directory> [--words] [--transfer] [--header] [--compare ...]
// Default: for each PDF, the source of every page, the table found (columns with their meaning, rows with their cells and flags) and the
// header fields. --words also lists every word with its position. --transfer makes a template from each file (the analysis as proposed)
// and applies it to every other file, comparing what it reads with what the analysis of that file proposes - the test of a template on
// an invoice of another supplier with the same layout.
var target = args.FirstOrDefault(argument => !argument.StartsWith("--")) ?? ".";   // the file or directory comes first; the value after --template is read separately
var showWords = args.Contains("--words");
var transfer = args.Contains("--transfer");
var templateIndex = Array.IndexOf(args, "--template");
var templateFile = templateIndex >= 0 && templateIndex + 1 < args.Length ? args[templateIndex + 1] : null;
// --compare [--engine A|B|both] [--references <dir>] [--write-reference]: the engines side by side (InvoiceEngines), each measured against
// the reference geometry of the file (<references>/<name>.reference.json, by default a "references" folder next to the PDF) by the lines between
// the rows and the edges of the columns, never by the values read. --write-reference writes a draft reference from the first engine when the
// file has none; the draft is then corrected by hand (move a line, fix an edge, rename a label).
var compare = args.Contains("--compare") || args.Contains("--write-reference");
var writeReference = args.Contains("--write-reference");
var engineIndex = Array.IndexOf(args, "--engine");
var engineName = engineIndex >= 0 && engineIndex + 1 < args.Length ? args[engineIndex + 1] : "both";
var engines = engineName.Equals("both", StringComparison.OrdinalIgnoreCase) ? InvoiceEngines.All
    : InvoiceEngines.Find(engineName) is { } chosenEngine ? [chosenEngine] : throw new ArgumentException($"Motor necunoscut: {engineName} (A, B sau both).");
var referencesIndex = Array.IndexOf(args, "--references");
var referencesDirectory = referencesIndex >= 0 && referencesIndex + 1 < args.Length ? args[referencesIndex + 1] : null;
var scores = new Dictionary<string, List<(string File, double Score)>>();
// The files the user left out (a layout the application does not have to read) are listed in <directory>/exclude.txt, one file name per line (# starts a comment).
var excludeList = Directory.Exists(target) ? Path.Combine(target, "exclude.txt") : null;
var excluded = excludeList is not null && File.Exists(excludeList)
    ? new HashSet<string>(File.ReadAllLines(excludeList).Select(line => line.Split('#')[0].Trim()).Where(line => line.Length > 0), StringComparer.OrdinalIgnoreCase)
    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var files = Directory.Exists(target) ? Directory.GetFiles(target, "*.pdf", SearchOption.AllDirectories).Where(file => !excluded.Contains(Path.GetFileName(file))).OrderBy(file => file).ToArray() : [target];
var reader = new InvoicePdfReader(new CorpusTessdata());
var loaded = new List<(string Name, InvoiceDocument Document, InvoiceAnalysis Analysis)>();
foreach (var file in files)
{
    Console.WriteLine($"==== {Path.GetFileName(file)}");
    try
    {
        await using var stream = File.OpenRead(file);
        var read = await reader.ReadAsync(stream);
        // --reread: the numbers of the table that the OCR did not read as numbers are read again cell by cell (what the application does after reading a file).
        if (args.Contains("--reread")) read = await reader.RereadNumbersAsync(read, engineName.Equals("both", StringComparison.OrdinalIgnoreCase) ? null : engines[0]);
        var imagesIndex = Array.IndexOf(args, "--images");
        if (imagesIndex >= 0 && imagesIndex + 1 < args.Length)
        {
            // The page pictures the engine works on, to look at (what the rules and the words were found on).
            Directory.CreateDirectory(args[imagesIndex + 1]);
            for (var pageIndex = 0; pageIndex < read.PagePreviews.Count; pageIndex++)
                await File.WriteAllBytesAsync(Path.Combine(args[imagesIndex + 1], Path.GetFileNameWithoutExtension(file) + $"-p{pageIndex + 1}.png"), read.PagePreviews[pageIndex]);
        }
        if (compare)
        {
            // The engines side by side on this file, measured against the reference of the file when there is one.
            var referenceFile = Path.Combine(referencesDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "references"), Path.GetFileNameWithoutExtension(file) + ".reference.json");
            var reference = File.Exists(referenceFile) ? InvoiceReference.FromJson(File.ReadAllText(referenceFile)) : null;
            foreach (var engine in engines)
            {
                var timer0 = System.Diagnostics.Stopwatch.StartNew();
                var analysis0 = InvoiceAnalyzer.Analyze(read.Document, engine);
                var separators0 = analysis0.Table is { } found ? InvoiceTableReader.SeparatorsFromRows(found.Rows, found.Columns, read.Document.Pages) : [];
                var flagged = analysis0.Table?.Rows.Count(row => row.Flags.Count > 0) ?? 0;
                if (writeReference && reference is null && engine == engines[0] && analysis0.Table is { } draftTable)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(referenceFile)!);
                    File.WriteAllText(referenceFile, InvoiceReference.From(Path.GetFileName(file), draftTable, separators0).ToJson());
                    Console.WriteLine($"  [{engine.Id}] draft reference written: {referenceFile}");
                }
                var rowsText = $"rows {analysis0.Table?.Rows.Count.ToString() ?? "-"}";
                if (reference is null) Console.WriteLine($"  [{engine.Id}] table={(analysis0.Table is null ? "NO" : "yes")} {rowsText}, lines {separators0.Count}, rows with problems in the values {flagged}, {timer0.ElapsedMilliseconds} ms (no reference)");
                else
                {
                    var match = InvoiceReferenceComparer.Compare(reference, analysis0.Table, separators0);
                    Console.WriteLine($"  [{engine.Id}] table={(match.TableFound ? "yes" : "NO")} rows {match.RowsFound}/{match.RowsExpected}, lines P={match.SeparatorPrecision:F2} R={match.SeparatorRecall:F2}, " +
                        $"columns P={match.ColumnPrecision:F2} R={match.ColumnRecall:F2} (meaning right {match.ColumnMeaningsRight}/{match.ColumnsMatched}), score {match.Score:F3}; rows with problems in the values {flagged}, {timer0.ElapsedMilliseconds} ms");
                    if (!scores.TryGetValue(engine.Id, out var list)) scores[engine.Id] = list = [];
                    list.Add((Path.GetFileName(file), match.Score));
                }
            }
            continue;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var analysis = InvoiceAnalyzer.Analyze(read.Document, engineName.Equals("both", StringComparison.OrdinalIgnoreCase) ? null : engines[0]);
        Console.WriteLine($"  analysis {timer.ElapsedMilliseconds} ms");
        loaded.Add((Path.GetFileName(file), read.Document, analysis));
        foreach (var page in read.Document.Pages)
        {
            Console.WriteLine($"  page {page.Number}: {page.Source}, {page.Width:F0}x{page.Height:F0} pt, {page.Words.Count} words");
            if (args.Contains("--header"))
            {
                // Why a header is or is not found: the rules the OCR drew, and every candidate band with its cells, their meanings and the score.
                Console.WriteLine($"    rules: {page.Rules?.Count(rule => rule.Vertical) ?? 0} vertical, {page.Rules?.Count(rule => !rule.Vertical) ?? 0} horizontal");
                foreach (var rule in page.Rules ?? []) Console.WriteLine($"      {(rule.Vertical ? "V" : "H")} at {rule.Position:F1} from {rule.From:F1} to {rule.To:F1}");
                var layoutLines = InvoiceLayout.BuildLines(page.Words);
                foreach (var line in layoutLines) Console.WriteLine($"    line y={line.Y:F1} h={line.Height:F1} numeric={InvoiceTableReader.IsMostlyNumericForDebug(line)}: {string.Join(' ', line.Words.Select(word => word.Text))}");
                for (var start = 0; start < layoutLines.Count; start++)
                    for (var length = 1; length <= 3 && start + length <= layoutLines.Count; length++)
                    {
                        var slice = layoutLines.Skip(start).Take(length).ToList();
                        foreach (var withRules in new[] { false, true })
                        {
                            var cells = withRules && page.Rules is { Count: > 0 } ? InvoiceTableReader.BuildRuledCells(slice, page.Rules) : InvoiceTableReader.BuildCells(slice);
                            if (cells is null || cells.Count < 3) continue;
                            var known = cells.Count(cell => cell.Meaning.Length > 0 && cell.Meaning != InvoiceColumnMeanings.Ignore);
                            if (known < 2) continue;
                            var band = InvoiceTableReader.Evaluate(page.Number, slice, withRules ? page.Rules : null);
                            Console.WriteLine($"    BAND lines {start}..{start + length - 1} rules={withRules} score={(band is null ? "none" : band.Score.ToString("F2"))}: " +
                                string.Join(" | ", cells.Select(cell => $"[{cell.Meaning}:{cell.MatchScore:F1}] {cell.Label}")));
                        }
                    }
            }
            if (showWords)
                foreach (var word in page.Words.OrderBy(word => Math.Round(word.CenterY / 3)).ThenBy(word => word.X))
                    Console.WriteLine($"    y={word.Y,6:F1} x={word.X,6:F1} w={word.Width,5:F1} h={word.Height,4:F1} o={word.Order,4} {word.Text}");
        }
        var overlayIndex = Array.IndexOf(args, "--overlay");
        if (overlayIndex >= 0 && overlayIndex + 1 < args.Length && analysis.Table is not null)
        {
            // The columns the pickup shows, drawn on the page picture: where each column is, with its id and meaning; columns with the same extent are drawn one inside the other.
            var proposalForOverlay = InvoiceTemplateDraft.FromAnalysis(analysis).ToDefinition(read.Document);
            var shown = InvoicePickupReader.Read(proposalForOverlay, read.Document, engineName.Equals("both", StringComparison.OrdinalIgnoreCase) ? null : engines[0]);
            Directory.CreateDirectory(args[overlayIndex + 1]);
            var page = analysis.Table.HeaderPage;
            using var picture = OpenCvSharp.Cv2.ImDecode(read.PagePreviews[page - 1], OpenCvSharp.ImreadModes.Color);
            var scale = picture.Cols / read.Document.Pages.First(item => item.Number == page).Width;
            var top = analysis.Table.HeaderTop;
            var bottom = shown.Separators.Where(separator => separator.Page == page).Select(separator => separator.Y).DefaultIfEmpty(top + 120).Max();
            var palette = new OpenCvSharp.Scalar[] { new(0, 0, 220), new(0, 140, 0), new(200, 80, 0), new(0, 160, 200), new(160, 0, 160), new(120, 120, 0), new(0, 90, 160), new(90, 90, 90) };
            var seen = new List<(double Left, double Right)>();
            for (var index = 0; index < shown.Extraction.Columns.Count; index++)
            {
                var column = shown.Extraction.Columns[index];
                var nested = seen.Count(item => Math.Abs(item.Left - column.Left) < 1 && Math.Abs(item.Right - column.Right) < 1);
                seen.Add((column.Left, column.Right));
                var inset = nested * 5;
                var colour = palette[index % palette.Length];
                OpenCvSharp.Cv2.Rectangle(picture, new OpenCvSharp.Rect((int)(column.Left * scale) + inset, (int)(top * scale) + inset, Math.Max(2, (int)((column.Right - column.Left) * scale) - 2 * inset), Math.Max(2, (int)((bottom - top) * scale) - 2 * inset)), colour, nested > 0 ? 3 : 2);
                OpenCvSharp.Cv2.PutText(picture, $"{column.Id}:{column.Meaning}", new OpenCvSharp.Point((int)(column.Left * scale) + 3 + inset, (int)(top * scale) - 6 - nested * 14), OpenCvSharp.HersheyFonts.HersheySimplex, 0.4, colour, 1);
            }
            var crop = new OpenCvSharp.Rect(0, Math.Max(0, (int)(top * scale) - 60), picture.Cols, Math.Min(picture.Rows - Math.Max(0, (int)(top * scale) - 60), (int)((bottom - top) * scale) + 90));
            using var cropped = new OpenCvSharp.Mat(picture, crop);
            var overlayFile = Path.Combine(args[overlayIndex + 1], Path.GetFileNameWithoutExtension(file) + "-overlay.png");
            OpenCvSharp.Cv2.ImWrite(overlayFile, cropped);
            Console.WriteLine($"  OVERLAY written: {overlayFile}; columns: {string.Join(", ", shown.Extraction.Columns.Select(column => $"{column.Id}:{column.Meaning}[{column.Left:F0}-{column.Right:F0}]"))}");
        }
        if (args.Contains("--pickup") && analysis.Table is not null)
        {
            // The reading of the pickup page without a saved template: the automatic proposal made a template, read with the demarcation lines of the rows.
            var proposal = InvoiceTemplateDraft.FromAnalysis(analysis).ToDefinition(read.Document);
            var reading = InvoicePickupReader.Read(proposal, read.Document, engineName.Equals("both", StringComparison.OrdinalIgnoreCase) ? null : engines[0]);
            Console.WriteLine($"  PICKUP extraction rows: {string.Join(", ", reading.Extraction.Rows.Select(row => $"top {row.Top:F1} bottom {row.Bottom:F1}"))}; header bottom {analysis.Table!.HeaderBottom:F1}");
            Console.WriteLine($"  PICKUP columns: {string.Join(", ", reading.Extraction.Columns.Select(column => $"{column.Meaning} {column.Left:F0}-{column.Right:F0} zone {column.ZoneTop:F1}/{column.ZoneBottom:F1}"))}; analysis rows top {string.Join(",", analysis.Table!.Rows.Select(row => row.Top.ToString("F1")))}");
            Console.WriteLine($"  PICKUP lines: {string.Join(", ", reading.Separators.Select(separator => $"p{separator.Page} y={separator.Y:F1}"))}");
            foreach (var row in InvoicePickupReader.Reread(reading, read.Document, reading.Separators))
                Console.WriteLine($"  PICKUP row p{row.Page} #{row.Number}: {string.Join(" | ", row.Cells.Values)}");
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
            if (args.Contains("--header") && InvoiceTableReader.RuledBands(read.Document, table.Columns, table.HeaderPage, table.HeaderBottom) is { } bands)
                foreach (var band in bands) Console.WriteLine($"    ruled box y={band.Top:F1}-{band.Bottom:F1}");
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

if (compare && scores.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("==== SUMMARY (files with a reference)");
    foreach (var (engineId, list) in scores.OrderBy(item => item.Key))
        Console.WriteLine($"  engine {engineId}: mean score {list.Average(item => item.Score):F3} over {list.Count} file(s), {list.Count(item => item.Score >= 0.999)} exact");
    if (scores.Count == 2)
    {
        var first = scores.First().Value.ToDictionary(item => item.File, item => item.Score);
        var second = scores.Last().Value.ToDictionary(item => item.File, item => item.Score);
        foreach (var name in first.Keys.Where(second.ContainsKey).Where(name => Math.Abs(first[name] - second[name]) > 0.0005))
            Console.WriteLine($"  differs: {name}: {scores.First().Key} {first[name]:F3} vs {scores.Last().Key} {second[name]:F3}");
    }
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
