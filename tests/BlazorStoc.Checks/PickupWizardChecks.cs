using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The invoice pickup wizard (Produse -> Preluare factura) with real services on a generated invoice: values that are not numbers, the window
// with the invoice, and the work kept when going back and forth between the steps.
public static class PickupWizardChecks
{
    private sealed class TestTessdata : IWebHostEnvironmentTessdataPath
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata");
    }

    private sealed class NoTemplates : IInvoiceTemplateService
    {
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InvoiceTemplateRecord>>([]);
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "pickup.admin");
        var store = new InvoiceAnalysisStore(TimeProvider.System);
        var repository = new DemoProductRepository(access);
        var invoice = InvoiceFixtures.Make(new InvoiceSpec("ro-lines", 4, SupplierName: "Furnizor Test SRL", SupplierCui: "RO12345678", Number: "FT 1"), InvoiceFixtures.MakeRows(4, 5));

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IInvoiceAnalysisStore>(store);
        context.Services.AddScoped<IInvoiceAnalysisService>(_ => new InvoiceAnalysisService(new InvoicePdfReader(new TestTessdata()), store, access));
        context.Services.AddSingleton<IInvoiceTemplateService>(new NoTemplates());
        context.Services.AddSingleton<IProductRepository>(repository);
        context.Services.AddSingleton<IStockMovementRepository>(new FakeInventoryStockMovementRepository(new Dictionary<int, int>()));
        context.Services.AddSingleton<IProductImageStore>(new DemoProductImageStore());
        context.Services.AddScoped<UnsavedChanges>();

        var cut = context.Render<InvoicePickup>();
        cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(invoice.Pdf, "factura-test.pdf", null, "application/pdf"));
        cut.WaitForAssertion(() => { if (!cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor") && !button.HasAttribute("disabled"))) throw new Exception("pending"); }, TimeSpan.FromSeconds(60));
        var rowsRead = cut.FindAll("tbody tr").Count;
        check(rowsRead == invoice.Rows.Count, $"Pickup wizard: step 1 reads the rows of the generated invoice ({rowsRead} of {invoice.Rows.Count})");

        void Click(string text) => cut.FindAll("button").First(button => button.TextContent.Contains(text)).Click();
        void WaitStep2() => cut.WaitForAssertion(() => { if (cut.FindAll("table.pickup-match").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        AngleSharp.Dom.IElement Cell(int row, string columnTitle) => cut.FindAll("table.pickup-match tbody tr")[row].QuerySelectorAll("input.pickup-cell").First(input => (input.GetAttribute("aria-label") ?? "").StartsWith(columnTitle, StringComparison.OrdinalIgnoreCase));

        Click("Pasul următor");
        WaitStep2();
        var titles = cut.FindAll("table.pickup-match thead th").Select(th => th.TextContent.Trim()).ToArray();
        var quantityTitle = titles.First(title => title.StartsWith("Cantit", StringComparison.OrdinalIgnoreCase));
        var priceTitle = titles.First(title => title.StartsWith("Pre", StringComparison.OrdinalIgnoreCase));
        check(cut.FindAll("input.pickup-cell.invalid").Count == 0, "Pickup wizard: values read as numbers have no red background");

        // Quantity, prices and taxes take only numbers: digits and one comma (the browser refuses the rest as it is typed; the server cleans what arrives).
        check(Cell(0, quantityTitle).HasAttribute("data-numeric") && Cell(0, priceTitle).HasAttribute("data-numeric") && !cut.FindAll("table.pickup-match textarea").Any(area => area.HasAttribute("data-numeric")) &&
              cut.FindAll("table.pickup-match input.pickup-cell[data-numeric]").All(input => (input.GetAttribute("aria-label") ?? "").Split(',')[0] is not ("Cod" or "UM")),
            "Pickup wizard: quantity, price and tax fields are marked as numeric fields (the name, the code and the unit are not)");
        Cell(2, quantityTitle).Change("1a2,3,4b");
        check(Cell(2, quantityTitle).GetAttribute("value") == "12,34", "Pickup wizard: letters and a second comma are removed from a numeric field");
        Cell(2, quantityTitle).Change("1");

        // A quantity and a price that are not numbers turn red; a number turns them back; an empty price is not a number either.
        Cell(0, quantityTitle).Change("doua");
        Cell(1, priceTitle).Change(",");
        check(Cell(0, quantityTitle).ClassList.Contains("invalid") && Cell(1, priceTitle).ClassList.Contains("invalid") && cut.FindAll("input.pickup-cell.invalid").Count == 2,
            "Pickup wizard: a quantity or a price that is not read as a number has a red background (and only those)");
        // One generic message per row, with an exclamation icon, under the name field of the row (not under each red field).
        var warnings = cut.FindAll("table.pickup-match .pickup-row-warning");
        check(warnings.Count == 2 && warnings.All(warning => warning.TextContent.Contains("Atenție: unele valori din acest rând nu sunt numere") && warning.QuerySelector(".pickup-warning-icon")?.TextContent == "!") &&
              warnings.All(warning => warning.ParentElement!.ClassList.Contains("pickup-col-name")),
            "Pickup wizard: a generic warning with an exclamation icon appears under the name field of each row that has a value that is not a number");
        Cell(1, quantityTitle).Change("");
        check(cut.FindAll(".pickup-row-warning").Count == 2, "Pickup wizard: several wrong values on one row give one warning, not one for each");
        Cell(1, quantityTitle).Change("3");
        Cell(0, quantityTitle).Change("2");
        check(!Cell(0, quantityTitle).ClassList.Contains("invalid") && Cell(1, priceTitle).ClassList.Contains("invalid") && cut.FindAll(".pickup-row-warning").Count == 1, "Pickup wizard: correcting the values of a row removes its red backgrounds and its warning");
        Cell(1, priceTitle).Change("");
        check(Cell(1, priceTitle).ClassList.Contains("invalid"), "Pickup wizard: a missing price is red too");
        Cell(1, priceTitle).Change("12,5");

        var link = cut.Find("a[href*='/produse/preluare-factura/factura/']");
        var viewerHref = link.GetAttribute("href")!;
        check(link.GetAttribute("target") == "invoice-viewer" && (link.GetAttribute("onclick") ?? "").Contains("window.open") && link.TextContent.Contains("fereastră nouă"),
            "Pickup wizard: step 2 offers the invoice in a window of its own (detachable)");

        // A product prepared for row 0, then back to step 1 and forward again: the corrections and the prepared product are still there.
        cut.FindAll("button").First(button => button.TextContent.Contains("Pregătește un produs nou")).Click();
        cut.WaitForAssertion(() => cut.Find("#product-category"), TimeSpan.FromSeconds(10));
        cut.WaitForAssertion(() => { if (cut.FindAll("#product-subcategory option").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Produs nou pregătit")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        check(cut.Markup.Contains("Produs nou pregătit"), "Pickup wizard: a product can be prepared for a row of step 2");
        var preparedName = cut.Find(".pickup-exact strong").TextContent;

        Click("Pasul precedent");
        cut.WaitForAssertion(() => cut.Find("#invoice-pickup-file"), TimeSpan.FromSeconds(10));
        Click("Pasul următor");
        WaitStep2();
        check(cut.Markup.Contains("Produs nou pregătit") && cut.Find(".pickup-exact strong").TextContent == preparedName && Cell(0, quantityTitle).GetAttribute("value") == "2" && Cell(1, priceTitle).GetAttribute("value") == "12,5",
            "Pickup wizard: going back to step 1 and forward again keeps the prepared product and the corrected cells");

        // Step 3, an edited description, then back to step 2 and forward again: nothing is lost.
        Click("Pasul următor");
        cut.WaitForAssertion(() => cut.Find("textarea"), TimeSpan.FromSeconds(10));
        cut.Find("textarea").Input("Descriere scrisa de utilizator");
        Click("Pasul precedent");
        WaitStep2();
        check(cut.Markup.Contains("Produs nou pregătit") && Cell(0, quantityTitle).GetAttribute("value") == "2", "Pickup wizard: going from step 3 back to step 2 keeps the work");
        Click("Pasul următor");
        cut.WaitForAssertion(() => cut.Find("textarea"), TimeSpan.FromSeconds(10));
        check(cut.FindAll("textarea").Any(area => area.GetAttribute("value") == "Descriere scrisa de utilizator") && cut.Markup.Contains(preparedName),
            "Pickup wizard: the edited description of step 3 survives going back to step 2 and forward again");

        check(!cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor")) && cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul precedent")),
            "Pickup wizard: the last step has no next-step button (only Pasul precedent and Finalizează preluarea)");

        // The window with the invoice: the pages of the file under pickup, for its owner only.
        var sessionId = Guid.Parse(viewerHref.Split('/').Last());
        var viewer = context.Render<InvoiceViewer>(parameters => parameters.Add(p => p.SessionId, sessionId));
        viewer.WaitForAssertion(() => { if (viewer.FindAll("img").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        check(viewer.Markup.Contains("factura-test.pdf") && viewer.FindAll("img").All(image => (image.GetAttribute("src") ?? "").StartsWith($"/media/invoice-analysis/{sessionId}/")),
            "Invoice window shows the pages of the file under pickup");
        var gone = context.Render<InvoiceViewer>(parameters => parameters.Add(p => p.SessionId, Guid.NewGuid()));
        gone.WaitForAssertion(() => { if (!gone.Markup.Contains("nu mai este disponibilă")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        check(gone.FindAll("img").Count == 0, "Invoice window of an unknown or expired file shows no pages");
    }
}
