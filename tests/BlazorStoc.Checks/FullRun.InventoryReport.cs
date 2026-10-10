#nullable enable
#pragma warning disable CS1998, CS8600, CS8601, CS8602, CS8603, CS8604, CS8605, CS8618, CS8619, CS8620, CS8625, CS8629, CS8714
using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;

public static partial class FullRun
{
    // Lines 1439-1612 of the former Program.cs.
    internal static async Task InventoryReportAsync(string[] args)
    {

        // Task 1: inventory report (selection logic, the report builder, PDF generation and its journal event).
        {
            // Selection logic (InventorySelectionState): category <-> subcategories <-> "select all", independent of the UI.
            var electric = ("Scule electrice", (IReadOnlyList<string>)new List<string> { "Găurire", "Tăiere" });
            var consumables = ("Consumabile", (IReadOnlyList<string>)new List<string> { "Fixare" });
            var emptyCategory = ("Categorie fără subcategorii", (IReadOnlyList<string>)new List<string>());
            var allCategories = new List<(string, IReadOnlyList<string>)> { electric, consumables, emptyCategory };
            var selection = new InventorySelectionState();
            Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: false } &&
                  selection.AllState(allCategories) is { Checked: false, Indeterminate: false },
                "Nothing selected at the start is neither checked nor indeterminate");
            selection.SetSubcategory("Scule electrice", "Găurire", true);
            Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: false, Indeterminate: true },
                "Selecting only one of two subcategories makes the category checkbox indeterminate");
            selection.SetSubcategory("Scule electrice", "Tăiere", true);
            Check(selection.CategoryState("Scule electrice", electric.Item2) is { Checked: true, Indeterminate: false },
                "Selecting every subcategory checks the category checkbox");
            selection.SetCategory("Scule electrice", electric.Item2, false);
            Check(!selection.IsSelected("Scule electrice", "Găurire") && !selection.IsSelected("Scule electrice", "Tăiere"),
                "Deselecting a category propagates to all of its subcategories");
            Check(selection.CategoryState("Categorie fără subcategorii", emptyCategory.Item2) is { Checked: false, Indeterminate: false },
                "A category without subcategories is never checked or indeterminate");
            selection.SetAll(allCategories, true);
            Check(selection.AllState(allCategories) is { Checked: true, Indeterminate: false } &&
                  selection.Count(allCategories) == 3 && selection.IsSelected("Consumabile", "Fixare"),
                "\"Select all categories\" selects every subcategory, ignoring categories without subcategories");
            selection.SetSubcategory("Consumabile", "Fixare", false);
            Check(selection.AllState(allCategories) is { Checked: false, Indeterminate: true },
                "\"Select all\" becomes indeterminate once one subcategory is cleared");
            Check(selection.ToItems(allCategories).Count == 2 && selection.ToItems(allCategories)
                .All(item => item.Category == "Scule electrice"), "ToItems returns exactly the selected pairs");

            // Report builder: a small fake catalogue with a negative, a zero and a vehicle-held product.
            var products = new List<Product>
            {
                new(1, "Scule electrice", "Găurire", "Mașină de găurit", "desc", 5, 1),
                new(2, "Scule electrice", "Tăiere", "Polizor", "desc", 0, 1),
                new(3, "Consumabile", "Fixare", "Șurub", "desc", -3, 1),
                new(4, "Consumabile", "Fixare", "Diblu", "desc", 10, 1),
                new(5, "Consumabile", "Ambalare", "Folie", "desc", 2, 1)
            };
            var groups = new List<ProductGroup>
            {
                new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere"),
                new("Consumabile", "Fixare"), new("Consumabile", "Ambalare")
            };
            var inVehicles = new Dictionary<int, int> { [4] = 4 }; // Diblu: 10 total, 4 in a vehicle -> 6 in the warehouse.
            var builder = new InventoryReportBuilder(new FakeInventoryProductRepository(products, groups), new FakeInventoryStockMovementRepository(inVehicles));

            var report = await builder.BuildAsync(new InventoryRequest(
                [new("Scule electrice", "Găurire"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
            Check(report.Categories.Count == 2 && report.SelectedCategoryCount == 2 && report.SelectedSubcategoryCount == 2,
                "The report contains only the selected categories and subcategories");
            var electricSection = report.Categories.Single(c => c.Category == "Scule electrice");
            Check(electricSection.Subcategories.Single().Subcategory == "Găurire" &&
                  electricSection.Subcategories.Single().Lines.Single() == new InventoryLine("Mașină de găurit", 5),
                "An unselected subcategory (Tăiere) of a partially selected category is left out of the report");
            var consumablesSection = report.Categories.Single(c => c.Category == "Consumabile");
            var fixareLines = consumablesSection.Subcategories.Single().Lines;
            Check(fixareLines.Select(line => line.Code).SequenceEqual(["Șurub", "Diblu"]), "Products with negative stock come first, the others by code, case-insensitively");
            Check(fixareLines.Single(line => line.Code == "Diblu").Quantity == 6, "The warehouse value excludes the quantity held by vehicles");
            Check(fixareLines.Single(line => line.Code == "Șurub") is { Quantity: -3, IsNegative: true }, "Negative stock is kept and flagged");
            Check(report.ProductCount == 3 && report.NegativeCount == 1, "The report totals count every line and the negative ones separately");
            Check(!report.Categories.Any(c => c.Category == "Consumabile" && c.Subcategories.Any(s => s.Subcategory == "Ambalare")),
                "An unselected subcategory does not appear even when its category is otherwise selected");

            var zeroFiltered = await builder.BuildAsync(new InventoryRequest(
                [new("Scule electrice", "Găurire"), new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
            Check(zeroFiltered.Categories.Single().Subcategories.Single().Subcategory == "Găurire",
                "Excluding zero stock omits an emptied subcategory (Tăiere's only product has quantity 0) but keeps a non-empty one");

            try
            {
                await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Tăiere")], ExcludeZeroStock: true));
                throw new Exception("Empty result after filtering accepted");
            }
            catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoProductsMessage, "A selection left empty by the zero-stock filter is reported, not silently generated"); }

            try
            {
                await builder.BuildAsync(new InventoryRequest([], ExcludeZeroStock: false));
                throw new Exception("Empty selection accepted");
            }
            catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.NoSelectionMessage, "An empty selection is rejected before any catalogue access"); }

            try
            {
                await builder.BuildAsync(new InventoryRequest([new("Categorie inexistentă", "Subcategorie inexistentă")], ExcludeZeroStock: false));
                throw new Exception("Stale selection accepted");
            }
            catch (InventoryOperationException exception) { Check(exception.Message == InventoryRules.InvalidSelectionMessage, "A selection that no longer matches the catalogue is reported instead of generating a partial file"); }

            var duplicateSelection = await builder.BuildAsync(new InventoryRequest(
                [new("Consumabile", "Fixare"), new("Consumabile", "Fixare")], ExcludeZeroStock: false));
            Check(duplicateSelection.SelectedSubcategoryCount == 1, "A repeated selection entry is counted once");

            Check(InventoryRules.FileName(new DateTime(2026, 9, 25, 14, 8, 0)) == "Inventar_2026-09-25_1408.pdf",
                "The proposed file name uses the local moment of generation");

            // PDF generation: the file starts with %PDF, has the expected texts (including a diacritic code, extracted
            // through the embedded font's own ToUnicode map), the negative row is red, and headings use the bold 14pt font.
            var pdfWriter = new InventoryPdfWriter();
            var generatedLocal = new DateTime(2026, 9, 25, 14, 8, 0);
            var pdfBytes = pdfWriter.Write(report, generatedLocal);
            Check(pdfBytes.Length > 4 && pdfBytes[0] == (byte)'%' && pdfBytes[1] == (byte)'P' && pdfBytes[2] == (byte)'D' && pdfBytes[3] == (byte)'F',
                "The generated file starts with the PDF signature");
            using (var pdfStream = new MemoryStream(pdfBytes))
            {
                var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
                Check(pdfDocument.PageCount >= 1, "The generated PDF has at least one page");
                var pageText = PdfTextExtractor.ExtractText(pdfDocument.Pages[0]);
                Check(pageText.Contains("Inventar") && pageText.Contains("Generat la: 25.09.2026 14:08"),
                    "The first page has the title and the local generation moment in the dd.MM.yyyy HH:mm form");
                Check(pageText.Contains("Scule electrice") && pageText.Contains("Găurire") && pageText.Contains("Consumabile") && pageText.Contains("Fixare"),
                    "The selected category and subcategory names appear on the page");
                Check(pageText.Contains("Nr. crt.") && pageText.Contains("Cod produs") && pageText.Contains("Valoare stoc") && pageText.Contains("Valoare reală"),
                    "The table header has the four required columns (Nr. crt., Cod produs, Valoare stoc, Valoare reală)");
                Check(pageText.Contains("Șurub"), "A product code with Romanian diacritics is extracted correctly from the embedded font");
                var fontNames = PdfTextExtractor.FontBaseNames(pdfDocument.Pages[0]);
                var fontUsage = PdfTextExtractor.FontUsage(pdfDocument.Pages[0]);
                Check(fontUsage.Any(usage => usage.Size == 14 && fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
                    "Category and subcategory names use a bold 14pt font");
                Check(fontUsage.Any(usage => usage.Size == 12 && !fontNames[usage.FontKey].Contains("Bold", StringComparison.OrdinalIgnoreCase)),
                    "The rest of the text uses a normal 12pt font");
                Check(PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
                    "The page sets the red fill colour for the negative-stock row");
            }

            var noNegativeReport = await builder.BuildAsync(new InventoryRequest([new("Scule electrice", "Găurire")], ExcludeZeroStock: false));
            var noNegativeBytes = pdfWriter.Write(noNegativeReport, generatedLocal);
            using (var pdfStream = new MemoryStream(noNegativeBytes))
            {
                var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
                Check(!PdfTextExtractor.ExtractRawContent(pdfDocument.Pages[0]).Contains("1 0 0 rg"),
                    "A report with no negative stock never sets the red fill colour");
            }

            // A large catalogue produces more than one page, with the table header repeated on the following pages.
            var manyLines = Enumerable.Range(1, 300).Select(index => new InventoryLine($"Produs {index:000}", 1)).ToArray();
            var largeReport = new InventoryReport(DateTime.UtcNow,
                [new InventoryCategorySection("Categorie mare", [new InventorySubcategorySection("Subcategorie mare", manyLines)])],
                1, 1, manyLines.Length, 0);
            var largeBytes = pdfWriter.Write(largeReport, generatedLocal);
            using (var pdfStream = new MemoryStream(largeBytes))
            {
                var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);
                Check(pdfDocument.PageCount > 1, "A catalogue of 300 products spans more than one page");
                Check(Enumerable.Range(0, pdfDocument.PageCount).Count(index => PdfTextExtractor.ExtractText(pdfDocument.Pages[index]).Contains("Cod produs")) > 1,
                    "The table header is repeated on the pages that continue the table");
                // Data rows are InventoryPdfWriter.RowHeight tall (room for handwriting), so a page holds far fewer rows than at
                // the old single-line height (16pt: about 45 rows on an A4 page); the rows are still all there.
                var rowsPerPage = Enumerable.Range(0, pdfDocument.PageCount).Select(index => System.Text.RegularExpressions.Regex.Matches(PdfTextExtractor.ExtractText(pdfDocument.Pages[index]), "Produs [0-9]{3}").Count).ToArray();
                var pageCapacity = (int)((842 - 2 * 40 - 30) / InventoryPdfWriter.RowHeight);
                Check(InventoryPdfWriter.RowHeight >= 30 && rowsPerPage.Sum() == 300 && rowsPerPage.Max() <= pageCapacity && pdfDocument.PageCount >= 300 / pageCapacity,
                    "Table rows are tall enough for handwriting: a page holds at most " + pageCapacity + " of them and none is lost across pages");
                Check($"Pagina {pdfDocument.PageCount} din {pdfDocument.PageCount}" is { } lastPageLabel &&
                      PdfTextExtractor.ExtractText(pdfDocument.Pages[pdfDocument.PageCount - 1]).Contains(lastPageLabel),
                    "Each page has a \"Pagina x din y\" footer");
            }

            // The journal event: written only after a successful generation, with a summary but no product data.
            var inventoryAudit = new TestAuditTrail();
            var inventoryAccess = new TestAccessControl(false, "gestionar.stoc");
            var auditDetails = AuditDetails.Identification(
                ("Categorii selectate", report.SelectedCategoryCount.ToString()), ("Subcategorii selectate", report.SelectedSubcategoryCount.ToString()),
                ("Stoc 0 exclus", "nu"), ("Produse în situație", report.ProductCount.ToString()),
                ("Din care cu stoc negativ", report.NegativeCount.ToString()), ("Fișier", "Inventar_2026-09-25_1408.pdf"));
            await AuditRecorder.RecordGenerateAsync(inventoryAudit, inventoryAccess, AuditEntities.Inventory, "Situație de inventar", auditDetails, CancellationToken.None);
            var inventoryEvent = inventoryAudit.Entries.Single();
            Check(inventoryEvent.ActorUsername == "gestionar.stoc" && inventoryEvent.EntityType == AuditEntities.Inventory &&
                  inventoryEvent.EntityId.Length == 0 && !inventoryEvent.Details.Contains("Mașină de găurit") && !inventoryEvent.Details.Contains("Șurub") &&
                  inventoryEvent.Details.Contains("Produse în situație: 3") && inventoryEvent.Details.Contains("Din care cu stoc negativ: 1"),
                "A successful generation writes exactly one event with the user, a summary and no product data or link");
            Check(AuditActions.Normalize(AuditActions.Generate) == "Generare", "The generation action is named \"Generare\" in the journal");
        }
    }
}
