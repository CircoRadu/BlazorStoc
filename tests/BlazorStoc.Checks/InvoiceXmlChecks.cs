using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace BlazorStoc.Checks;

// XML invoices (UBL / e-Factura): read directly with a mapping of paths, no OCR. The files are generated here; no invoice of a real supplier is in the repository.
public static class InvoiceXmlChecks
{
    private sealed class TestTessdata : IWebHostEnvironmentTessdataPath
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Tessdata");
    }

    private sealed class FixedTemplates(IReadOnlyList<InvoiceTemplateRecord> templates) : IInvoiceTemplateService
    {
        public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(templates);
        public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // A UBL 2.1 invoice with the given lines (code, name, quantity, unit code, price).
    public static string Ubl(string number = "FX 100", string date = "2026-10-05", string cui = "RO12345678", string name = "Furnizor Test SRL",
        IReadOnlyList<(string Code, string Name, string Quantity, string Unit, string Price)>? lines = null)
    {
        lines ??= [("A-1", "Cablu UTP cat6", "10.0000", "C62", "2.50"), ("B-2", "Priza dubla", "4.0000", "H87", "7.25")];
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\" xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">\n");
        builder.Append($"<cbc:ID>{number}</cbc:ID><cbc:IssueDate>{date}</cbc:IssueDate><cbc:DocumentCurrencyCode>RON</cbc:DocumentCurrencyCode>\n");
        builder.Append($"<cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme><cbc:CompanyID>{cui}</cbc:CompanyID></cac:PartyTaxScheme>");
        builder.Append($"<cac:PartyLegalEntity><cbc:RegistrationName>{name}</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>\n");
        builder.Append("<cac:LegalMonetaryTotal><cbc:PayableAmount>100.00</cbc:PayableAmount></cac:LegalMonetaryTotal>\n");
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            builder.Append($"<cac:InvoiceLine><cbc:ID>{index + 1}</cbc:ID><cbc:InvoicedQuantity unitCode=\"{line.Unit}\">{line.Quantity}</cbc:InvoicedQuantity><cbc:LineExtensionAmount>1.00</cbc:LineExtensionAmount>");
            builder.Append($"<cac:Item><cbc:Name>{line.Name}</cbc:Name><cac:SellersItemIdentification><cbc:ID>{line.Code}</cbc:ID></cac:SellersItemIdentification></cac:Item>");
            builder.Append($"<cac:Price><cbc:PriceAmount>{line.Price}</cbc:PriceAmount></cac:Price></cac:InvoiceLine>\n");
        }
        builder.Append("</Invoice>");
        return builder.ToString();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        Console.WriteLine("=== XML invoices ===");
        byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);
        InvoiceXmlMapping? picked = null;

        // UBL read completely with the standard paths.
        var document = InvoiceXmlReader.Parse(Bytes(Ubl()));
        var read = InvoiceXmlReader.Read(document, InvoiceXmlMapping.Ubl);
        string Field(string meaning) => read.Fields.First(field => field.Meaning == meaning).Value;
        check(Field(InvoiceFieldMeanings.InvoiceNumber) == "FX 100" && Field(InvoiceFieldMeanings.InvoiceDate) == "05.10.2026" && Field(InvoiceFieldMeanings.SupplierCui) == "RO12345678" &&
              Field(InvoiceFieldMeanings.SupplierName) == "Furnizor Test SRL" && Field(InvoiceFieldMeanings.Total) == "100.00" && Field(InvoiceFieldMeanings.Currency) == "RON",
            "XML invoice: the number, date (dd.mm.yyyy), supplier, tax id, currency and total are read with the UBL paths");
        check(read.Rows.Count == 2 && read.Rows[0].Cells["code"] == "A-1" && read.Rows[0].Cells["name"] == "Cablu UTP cat6" && read.Rows[0].Cells["quantity"] == "10" &&
              read.Rows[0].Cells["unit"] == "buc" && read.Rows[1].Cells["unitPrice"] == "7.25" && read.Warnings.Count == 0,
            "XML invoice: the lines are read completely (code, name, quantity, unit code turned into 'buc', price), without warnings");
        check(InvoiceXmlReader.Read(InvoiceXmlReader.Parse(Bytes(Ubl(lines: [("Z", "Produs", "10.000000", "H87", "168.200000"), ("Y", "Alt produs", "2.500", "H87", "5")]))), InvoiceXmlMapping.Ubl).Rows is { Count: 2 } numberRows && numberRows[0].Cells["quantity"] == "10" && numberRows[0].Cells["unitPrice"] == "168.20" && numberRows[1].Cells["quantity"] == "2.5" && numberRows[1].Cells["unitPrice"] == "5.00",
            "XML invoice: 10.000000 is shown as 10 (the zeros of the fixed decimals are dropped), the prices keep two decimals");
        check(InvoiceXmlReader.Supplier(document) == ("Furnizor Test SRL", "12345678"), "XML invoice: the supplier of the file is found for choosing its template");

        // A line without quantity is flagged; a file without any line gives a clear warning and no rows.
        var noQuantity = InvoiceXmlReader.Read(InvoiceXmlReader.Parse(Bytes(Ubl(lines: [("A-1", "Cablu", "", "C62", "1")]))), InvoiceXmlMapping.Ubl);
        check(noQuantity.Rows.Count == 1 && noQuantity.Rows[0].Flags.Contains("fără cantitate"), "XML invoice: a line without quantity is flagged");
        var noLines = InvoiceXmlReader.Read(InvoiceXmlReader.Parse(Bytes(Ubl(lines: []))), InvoiceXmlMapping.Ubl);
        check(noLines.Rows.Count == 0 && noLines.Warnings.Contains(InvoiceXmlRules.NoLinesMessage), "XML invoice: a file with no lines gives no rows and a clear message");
        var foreign = InvoiceXmlReader.Read(InvoiceXmlReader.Parse(Bytes("<root><a>1</a></root>")), InvoiceXmlMapping.Ubl);
        check(foreign.Rows.Count == 0 && foreign.Warnings.Contains(InvoiceXmlRules.NotInvoiceMessage), "XML invoice: an XML of another format is told not to be an invoice");

        // Invalid XML and files the server must not follow.
        string? failure = null;
        try { InvoiceXmlReader.Parse(Bytes("<Invoice><ID>1</Invoice>")); } catch (InvoiceAnalysisException exception) { failure = exception.Message; }
        check(failure == InvoiceXmlRules.InvalidMessage, "XML invoice: an invalid XML gets a clear message");
        failure = null;
        try { InvoiceXmlReader.Parse(Bytes("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><Invoice><ID>&e;</ID></Invoice>")); } catch (InvoiceAnalysisException exception) { failure = exception.Message; }
        check(failure == InvoiceXmlRules.InvalidMessage, "XML invoice: a DTD (external entity) is refused, never read");
        failure = null;
        try { InvoiceXmlReader.Parse(new byte[(int)InvoiceXmlRules.MaxFileSizeBytes + 1]); } catch (InvoiceAnalysisException exception) { failure = exception.Message; }
        check(failure == InvoiceXmlRules.TooLargeMessage, "XML invoice: a file over 5 MB is refused");

        // The ZIP of e-Factura holds the invoice and its signature: the invoice is found and read, the signature never.
        static byte[] Zip(params (string Name, byte[] Content)[] files)
        {
            using var memory = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                foreach (var (name, content) in files)
                {
                    using var entry = archive.CreateEntry(name).Open();
                    entry.Write(content);
                }
            return memory.ToArray();
        }
        var signature = Bytes("<?xml version=\"1.0\"?><Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"><SignatureValue>abc</SignatureValue></Signature>");
        var invoiceBytes = Bytes(Ubl());
        var (unpacked, unpackedName) = InvoiceXmlReader.Unpack(Zip(("semnatura_4263525347.xml", signature), ("4263525347.xml", invoiceBytes)), "e-factura.zip");
        check(unpackedName == "4263525347.xml" && unpacked.SequenceEqual(invoiceBytes), "XML invoice: from the ZIP of e-Factura the invoice is taken, not the signature, whatever the order");
        var (plain, plainName) = InvoiceXmlReader.Unpack(invoiceBytes, "factura.xml");
        check(plainName == "factura.xml" && plain.SequenceEqual(invoiceBytes), "XML invoice: a file that is not a ZIP is returned as it is");
        failure = null;
        try { InvoiceXmlReader.Unpack(Zip(("semnatura.xml", signature)), "x.zip"); } catch (InvoiceAnalysisException exception) { failure = exception.Message; }
        check(failure == InvoiceXmlRules.NoInvoiceInZipMessage, "XML invoice: a ZIP with only a signature gets a clear message");
        failure = null;
        try { InvoiceXmlReader.Unpack([(byte)'P', (byte)'K', 3, 4, 1, 2, 3, 4, 5, 6], "x.zip"); } catch (InvoiceAnalysisException exception) { failure = exception.Message; }
        check(failure is InvoiceXmlRules.InvalidZipMessage or InvoiceXmlRules.NoInvoiceInZipMessage, "XML invoice: a broken ZIP gets a clear message");

        // Visual linking: the tree of the example gives the paths (from the root, and from the first line for the line values) and the links are read back.
        {
            var linkDocument = InvoiceXmlReader.Parse(Bytes(Ubl()));
            var tree = InvoiceXmlTree.Build(linkDocument, InvoiceXmlMapping.Ubl)!;
            InvoiceXmlTreeNode? Find(InvoiceXmlTreeNode node, string path) => node.Path == path ? node : node.Children.Select(child => Find(child, path)).FirstOrDefault(found => found is not null);
            var unitNode = Find(tree, "InvoiceLine/InvoicedQuantity/@unitCode");
            var nameNode = Find(tree, "InvoiceLine/Item/Name");
            check(unitNode?.LinePath == "InvoicedQuantity/@unitCode" && nameNode?.LinePath == "Item/Name" && nameNode.InLine && nameNode.Value == "Cablu UTP cat6", "XML linking: the tree gives absolute paths and paths relative to the first line");
            check(Find(tree, "InvoiceLine")?.SimilarSiblings == 1 && Find(tree, "ID")?.LinePath is null, "XML linking: the other lines are counted, not repeated, and a header value has no line path");
            check(InvoiceXmlTree.Link("InvoicedQuantity | CreditedQuantity", "CreditedQuantity") == "CreditedQuantity | InvoicedQuantity" && InvoiceXmlTree.Link("A", "B") == "B", "XML linking: a new link replaces the path, a known alternative only goes first");
            var linked = InvoiceXmlTree.Linked(linkDocument, InvoiceXmlMapping.Ubl);
            check(linked["quantity"] is System.Xml.Linq.XElement quantityElement && quantityElement.Value == "10.0000" && linked["unit"] is System.Xml.Linq.XAttribute && linked.ContainsKey("lines") && linked["code"].ToString()!.Contains("A-1"), "XML linking: the elements the standard mapping reads from are found in the example");
            using var context = new BunitContext();
            var cut = context.Render<BlazorStoc.Components.Shared.InvoiceXmlLinker>(parameters => parameters
                .Add(item => item.Document, linkDocument).Add(item => item.Mapping, InvoiceXmlMapping.Ubl with { Name = "" })
                .Add(item => item.MappingChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<InvoiceXmlMapping>(new object(), changed => { picked = changed; })));
            check(cut.Markup.Contains("Denumirea produsului") && cut.Markup.Contains("nelegat"), "XML linking: an element without a link says so");
            var nameButton = cut.FindAll("button").First(button => button.TextContent.Trim() == "Alege" && button.ParentElement!.ParentElement!.TextContent.Contains("Denumirea produsului"));
            nameButton.Click();
            cut.FindAll("button.xml-node-main.pick").First(button => button.TextContent.Contains("Cablu UTP cat6")).Click();
            check(picked is { Name: "Item/Name" }, "XML linking: choosing a value in the tree links the active element of the template to its path");
        }

        // Matching a row with the catalog by the code written in the name, by the link of the supplier's own code, and what happens when they disagree.
        {
            Product Item(int id, string code, string description = "") => new(id, "Cat", "Sub", code, description, 0);
            var catalog = new List<Product> { Item(1, "GS-778"), Item(2, "INT-0099"), Item(3, "Cablu"), Item(4, "AB12") };
            var inName = InvoiceProductMatcher.Match("INT-0042", "Intrerupator 10A cod GS 778", catalog);
            check(inName.Exact?.Id == 1 && inName.Via == InvoiceProductMatcher.ViaName, "Row matching: the code of a product written in the name is found even when the supplier's own code is in the code cell");
            var wordOnly = InvoiceProductMatcher.Match("X-1", "Cablu flexibil 3x1.5", catalog);
            check(wordOnly.Exact is null && wordOnly.Note.Length == 0, "Row matching: a product whose code has no digit is not taken from an ordinary word of the name");
            var linkedRow = InvoiceProductMatcher.Match("INT-0042", "Intrerupator 10A", catalog, new Dictionary<string, int> { [SupplierProductCodeRules.Key("INT-0042")] = 2 }, "INT-0042");
            check(linkedRow.Exact?.Id == 2 && linkedRow.Via == InvoiceProductMatcher.ViaSupplierCode, "Row matching: the link made earlier for the supplier's code gives the product");
            var disagree = InvoiceProductMatcher.Match("INT-0042", "Intrerupator cod GS-778", catalog, new Dictionary<string, int> { [SupplierProductCodeRules.Key("INT-0042")] = 2 }, "INT-0042");
            check(disagree.Exact is null && disagree.Note == InvoiceProductMatcher.DisagreementNote && disagree.Alternatives.Take(2).Select(item => item.Product.Id).SequenceEqual([2, 1]), "Row matching: when the link and the name point to different products none is taken, both are offered");
            var byCell = InvoiceProductMatcher.Match("GS-778", "Intrerupator cod INT-0099", catalog);
            check(byCell.Exact?.Id == 1 && byCell.Via == InvoiceProductMatcher.ViaCode, "Row matching: the code cell as the code of a product stays the first choice, the name is not searched then");
            check(InvoiceProductMatcher.CodeInText("Intrerupator 10A cod GS-778") == "GS-778" && InvoiceProductMatcher.CodeInText("Sursa DS-UPS1000 rack") == "DS-UPS1000" && InvoiceProductMatcher.CodeInText("Cablu 3x1.5 flexibil") is null,
                "New product: the code of the name is the text called code or the one that looks like a code, nothing for an ordinary description");
            check(SupplierProductCodeRules.Key("INT-0042") == SupplierProductCodeRules.Key("int 0042") && !SupplierProductCodeRules.IsLinkable("A") && SupplierProductCodeRules.IsLinkable("AB"), "Supplier code link: the key ignores case, dashes and spaces; a one-character code is not linked");
        }

        // The unit codes come from the UN/ECE Rec 20 list used by Peppol: XPP is a piece, an uncommon code shows the name of the list, an unknown one stays as written.
        check(InvoiceUnitCodes.Display("XPP") == "buc" && InvoiceUnitCodes.NameOf("XPP") == "Piece", "XML invoice: unit code XPP is read as a piece (UN/ECE Rec 20)");
        check(InvoiceUnitCodes.Display("KJO") == "kilojoule" && InvoiceUnitCodes.Display("KWH") == "kWh" && InvoiceUnitCodes.Display("ZZ9") == "ZZ9", "XML invoice: an uncommon unit code shows the list name, an unknown one stays as written");

        // Own mapping: another structure, an attribute, '//' paths.
        const string custom = "<Factura><Antet nr=\"F-9\"><Data>2026-09-30</Data></Antet><Continut><Rand><Cod>Z9</Cod><Den>Tub</Den><Cant>3</Cant><UM>buc</UM></Rand><Rand><Cod>Z8</Cod><Den>Cot</Den><Cant>1</Cant><UM>buc</UM></Rand></Continut></Factura>";
        var mapping = new InvoiceXmlMapping("Antet/@nr", "Factura/Antet/Data", "", "", "", "", "//Rand", "Cod", "Den", "Cant", "UM", "", "");
        var own = InvoiceXmlReader.Read(InvoiceXmlReader.Parse(Bytes(custom)), mapping);
        check(own.Rows.Count == 2 && own.Fields.First(field => field.Meaning == InvoiceFieldMeanings.InvoiceNumber).Value == "F-9" && own.Rows[1].Cells["code"] == "Z8" &&
              own.Columns.Select(column => column.Id).SequenceEqual(["code", "name", "quantity", "unit"]),
            "XML invoice: an own mapping reads an attribute, '//' paths and only the columns it defines");
        check(InvoiceXmlRules.Validate(mapping) is null && InvoiceXmlRules.Validate(InvoiceXmlMapping.Ubl) is null, "XML invoice: valid mappings are accepted");
        check(InvoiceXmlRules.Validate(mapping with { Lines = "" }) == InvoiceXmlRules.MappingRequiredMessage && InvoiceXmlRules.Validate(null) == InvoiceXmlRules.MappingRequiredMessage &&
              InvoiceXmlRules.Validate(mapping with { Number = "A/@b/C" }) is { Length: > 0 } && InvoiceXmlRules.Validate(mapping with { Date = "a b" }) is { Length: > 0 },
            "XML invoice: a mapping without lines or with a path that cannot be followed is refused with a message");

        // The template with the XML mapping is saved as JSON like any template and not offered for PDF invoices.
        var definition = new InvoiceTemplateDefinition(InvoiceTemplateDefinition.CurrentSchema, InvoiceTemplateDefinition.XmlSourceKind, 0, 0, [], [], null, "", InvoiceXmlMapping.Ubl);
        var roundTrip = InvoiceTemplateJson.Deserialize(InvoiceTemplateJson.Serialize(definition));
        check(roundTrip.IsXml && roundTrip.Xml == InvoiceXmlMapping.Ubl && InvoiceTemplateRules.Describe(roundTrip).StartsWith("sursă: XML"), "XML invoice: the XML template survives saving as JSON and is described as XML");
        var info = new InvoiceTemplateInfo(1, "UBL furnizor", "Furnizor Test SRL", "12345678", "xml", true, 0, "t", DateTime.UtcNow, "t", DateTime.UtcNow);
        var record = new InvoiceTemplateRecord(info, definition);
        var emptyDocument = new InvoiceDocument([new InvoicePageData(1, 595, 842, InvoiceSources.Text, [])]);
        check(InvoiceTemplateSuggestions.Rank([record], emptyDocument).Count == 0, "XML invoice: an XML template is never proposed for a PDF invoice");

        check(InvoiceValues.NormalizeCui("ROZ2460883, sediu social:") == "22460883" && InvoiceValues.NormalizeCui("RO 2246088, Str. X") == "2246088" && InvoiceValues.NormalizeCui("J40/1/2020, sediu") == "",
            "Tax id: a code followed by the rest of the line ('RO..., sediu social:') is still read, a registry number is not");
        check(InvoiceValues.CleanCui("ROZ2460883, sediu social:") == "RO22460883" && InvoiceValues.CleanCui("Z2460883") == "22460883" && InvoiceValues.CleanCui("J40/1/2020") == "J40/1/2020",
            "Tax id: OCR letters are turned into digits (Z is 2) and the rest of the line is cut when the field is read");
        check(InvoiceValues.CleanCui("ROi2460o83") == "RO12460083" && InvoiceValues.CleanCui("RO|246O883") == "RO12460883" && InvoiceValues.CleanCui("l24608O3") == "12460803" && InvoiceValues.NormalizeCui("O2460883") == "",
            "Tax id: i, I, l and | are 1; o and O are 0 after RO or when not first");
        Wizard(check, record, Bytes);
        await Task.CompletedTask;
    }

    private static void Wizard(Action<bool, string> check, InvoiceTemplateRecord template, Func<string, byte[]> bytes)
    {
        var access = new TestAccessControl(true, "xml.admin");
        var store = new InvoiceAnalysisStore(TimeProvider.System);
        var repository = new DemoProductRepository(access);

        BunitContext Make(IReadOnlyList<InvoiceTemplateRecord> templates)
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddLogging();
            context.Services.AddSingleton<IAccessControl>(access);
            context.Services.AddSingleton<IInvoiceAnalysisStore>(store);
            context.Services.AddScoped<IInvoiceAnalysisService>(_ => new InvoiceAnalysisService(new InvoicePdfReader(new TestTessdata()), store, access));
            context.Services.AddSingleton<IInvoiceTemplateService>(new FixedTemplates(templates));
            context.Services.AddSingleton<IProductRepository>(repository);
            context.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
            context.Services.AddSingleton<IStockMovementRepository>(new FakeInventoryStockMovementRepository(new Dictionary<int, int>()));
            context.Services.AddSingleton<IProductImageStore>(new DemoProductImageStore());
            context.Services.AddSupplierFakes();
            context.Services.AddScoped<UnsavedChanges>();
            context.Services.AddSingleton(new InvoiceLabSettings(new ConfigurationBuilder().Build()));
            return context;
        }

        // No template for the supplier: the standard UBL paths read the file, with a notice, and the rows can be taken to the next step.
        using (var context = Make([]))
        {
            var cut = context.Render<InvoicePickup>();
            cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
            cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes(Ubl()), "factura.xml", null, "text/xml"));
            cut.WaitForAssertion(() => { if (cut.FindAll("tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(20));
            check(cut.FindAll("tbody tr").Count == 2 && cut.Markup.Contains("căile standard UBL") && cut.Markup.Contains("factură XML") && cut.FindAll("svg.sep-overlay").Count == 0,
                "XML pickup: a UBL file without a template is read with the standard paths (2 rows, notice, no page picture)");
            check(cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor") && !button.HasAttribute("aria-disabled")), "XML pickup: the rows can be taken to the next step");
        }

        // The template of the supplier (found by the tax id of the file) is applied by itself.
        using (var context = Make([template]))
        {
            var cut = context.Render<InvoicePickup>();
            cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
            cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes(Ubl()), "factura.xml", null, "text/xml"));
            cut.WaitForAssertion(() => { if (cut.FindAll("tbody tr").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(20));
            check(cut.Markup.Contains("Șablon XML detectat: UBL furnizor"), "XML pickup: the XML template of the supplier is detected by the tax id in the file");
        }

        // What blocks a step is told only when the user tries to leave it, not when the page has just been opened.
        using (var context = Make([]))
        {
            var cut = context.Render<InvoicePickup>();
            cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
            check(cut.FindAll(".invoice-warning, .error-banner").Count == 0 && cut.FindAll("button").Any(button => button.TextContent.Contains("Pasul următor") && button.HasAttribute("aria-disabled")),
                "Pickup: on a page just opened no error is shown, the next-step button only looks disabled");
            cut.FindAll("button").First(button => button.TextContent.Contains("Pasul următor")).Click();
            check(cut.FindAll(".invoice-warning").Any(item => item.TextContent.Contains("Alege fișierul facturii")), "Pickup: pressing the blocked next-step button says what is missing, under the buttons");
        }

        // The add-supplier window of the XML template editor shows the supplier form (it was an empty box once).
        using (var context = Make([]))
        {
            var cut = context.Render<BlazorStoc.Components.Shared.InvoiceXmlTemplateEditor>(parameters => parameters
                .Add(p => p.Sample, bytes(Ubl(cui: "RO99887766", name: "Necunoscut SRL"))).Add(p => p.SampleName, "factura.xml"));
            cut.WaitForAssertion(() => cut.FindAll("button").First(button => button.TextContent.Contains("Adaugă furnizorul")), TimeSpan.FromSeconds(10));
            cut.FindAll("button").First(button => button.TextContent.Contains("Adaugă furnizorul")).Click();
            check(cut.FindAll(".invoice-modal #supplier-cui").Count == 1, "XML template editor: the add-supplier window shows the supplier form");
        }

        // The editor is split in sections like the PDF one; the name is proposed as "<supplier> - xml <date>", the description of the stock entry has a starting text.
        using (var context = Make([]))
        {
            var cut = context.Render<BlazorStoc.Components.Shared.InvoiceXmlTemplateEditor>(parameters => parameters
                .Add(p => p.Sample, bytes(Ubl())).Add(p => p.SampleName, "factura.xml"));
            cut.WaitForAssertion(() => cut.Find("details.invoice-section"), TimeSpan.FromSeconds(10));
            var sections = cut.FindAll("details.invoice-section > summary").Select(item => item.TextContent).ToList();
            check(sections.Count == 4 && sections[2].Contains("descriere intrare în stoc") && sections[3].Contains("Salvare"), "XML template editor: the page is split in the sections of the PDF editor (links, preview, description of the stock entry, saving)");
            var nameValue = cut.Find("details.invoice-section:last-of-type input").GetAttribute("value") ?? "";
            check(nameValue.StartsWith("Furnizor Test SRL - xml ", StringComparison.Ordinal) || nameValue.StartsWith("xml ", StringComparison.Ordinal) || nameValue.Contains(" - xml " + DateTime.Today.ToString("dd.MM.yyyy")),
                "XML template editor: the name of a new template is proposed as supplier - xml date");
            check((cut.Find("#invoice-product-description").GetAttribute("value") ?? cut.Find("#invoice-product-description").TextContent).Contains("<Număr factură>"), "XML template editor: the description of the stock entry starts with a proposed text");
        }
        var read = InvoiceXmlReader.Read(InvoiceXmlReader.Parse(bytes(Ubl())), InvoiceXmlMapping.Ubl);
        check(InvoiceXmlDescription.RenderRow("<Cod furnizor> / <Denumire> x <Cantitate> din <Număr factură>", read, read.Rows[0].Cells) == "A-1 / Cablu UTP cat6 x 10 din FX 100"
            && InvoiceXmlDescription.Problems("<Altceva>", InvoiceXmlMapping.Ubl).Count == 1 && InvoiceXmlDescription.Problems(InvoiceXmlDescription.Default(InvoiceXmlMapping.Ubl), InvoiceXmlMapping.Ubl).Count == 0,
            "XML description: the labels of the linked elements are replaced by the values of the row; an unknown label is reported");
        check(InvoiceTemplateRules.SuggestedName("Acme SRL", true, new DateTime(2026, 10, 9)) == "Acme SRL - xml 09.10.2026" && InvoiceTemplateRules.SuggestedName("", false, new DateTime(2026, 10, 9)) == "pdf 09.10.2026",
            "Template name: proposed as supplier - xml/pdf date");

        // A weak layout match proposes a new template, a partial one only warns; the PDF printed from e-Factura points to the XML.
        check(InvoiceEFacturaPdf.IsExport("Nr. factura 111 Data emitere 2026-09-22 Identificatorul TVA RO22460883 TOTAL PLATA 49.00".Split(' '))
            && !InvoiceEFacturaPdf.IsExport("Factura fiscala nr 5 data 01.01.2026 total 10".Split(' ')), "e-Factura PDF: the printed export is recognised by its fixed labels, an ordinary invoice is not");
        foreach (var (score, weak, partial) in new[] { (0.04, true, false), (0.45, false, true), (0.85, false, false) })
        {
            using var barContext = new BunitContext();
            var bar = barContext.Render<BlazorStoc.Components.Shared.PickupPdfTemplateBar>(parameters => parameters
                .Add(p => p.Chosen, template).Add(p => p.Info, "Șablon detectat").Add(p => p.Score, score).Add(p => p.EFactura, score < 0.1));
            check(bar.FindAll("#weak-template-warning").Count == (weak || partial ? 1 : 0) && bar.FindAll("#weak-template-warning button").Count == (weak ? 1 : 0)
                && bar.FindAll("#efactura-pdf-notice").Count == (score < 0.1 ? 1 : 0), $"PDF template bar: a layout match of {score:P0} shows the right warning");
        }

        // An option of a combobox is chosen at the press of the mouse button (a click came too late: the field lost focus, the list closed and the click never arrived).
        using (var selectContext = new BunitContext())
        {
            int? chosenId = null;
            var combo = selectContext.Render<BlazorStoc.Components.Shared.SearchableSelect>(parameters => parameters
                .Add(p => p.Id, "combo").Add(p => p.Label, "Beneficiar")
                .Add(p => p.Options, new[] { new SelectOption(1, "Unu"), new SelectOption(2, "Doi") })
                .Add(p => p.SelectedIdChanged, (int? id) => { chosenId = id; }));
            combo.Find("input").Click();
            combo.Find("#combo-option-1").MouseDown();
            check(chosenId == 2, "Combobox: an option is chosen when the mouse button is pressed on it");
        }

        // A file that is not a valid XML, and one without lines, get messages; nothing is half-loaded.
        using (var context = Make([]))
        {
            var cut = context.Render<InvoicePickup>();
            cut.WaitForAssertion(() => cut.Find("input[type=file]"), TimeSpan.FromSeconds(10));
            cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes("<Invoice><ID>1</Invoice>"), "stricat.xml", null, "text/xml"));
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains(InvoiceXmlRules.InvalidMessage)) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(cut.FindAll("tbody tr").Count == 0, "XML pickup: an invalid XML shows the message and no rows");
            cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes(Ubl(lines: [])), "fara-linii.xml", null, "text/xml"));
            cut.WaitForAssertion(() => { if (!cut.Markup.Contains("Nicio linie în fișierul XML")) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
            check(cut.Markup.Contains("Nicio linie în fișierul XML"), "XML pickup: a file without lines says so and offers another template");
        }
    }
}
