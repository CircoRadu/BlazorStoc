using AngleSharp.Html.Dom;
using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// Checks of Razor components, rendered with bUnit (no browser, no database): the product form of the invoice pickup.
public static class ComponentChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var repository = new DemoProductRepository(new TestAccessControl(true, "component.admin"));
        // The list the invoice pickup read when step 2 opened: a category created later, in the popup of a form, is not in it.
        var stale = await repository.GetGroupsAsync();
        await repository.CreateCategoryAsync("TVCI");
        await repository.CreateSubcategoryAsync("TVCI", "Generice");
        check(!stale.Any(group => group.Category == "TVCI"), "Component checks: the page's list of categories is older than the catalog");

        var parameterStore = new DemoProductParameterRepository();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IProductRepository>(repository);
        context.Services.AddSingleton<IProductParameterRepository>(parameterStore);
        context.Services.AddSingleton<IStockMovementRepository>(new FakeInventoryStockMovementRepository(new Dictionary<int, int>()));
        context.Services.AddSingleton<IProductImageStore>(new DemoProductImageStore());
        context.Services.AddScoped<UnsavedChanges>();

        string[] Options(IRenderedComponent<ProductEditor> cut, string id) =>
            cut.FindAll($"#{id} option").Select(option => option.GetAttribute("value") ?? "").ToArray();
        string Selected(IRenderedComponent<ProductEditor> cut, string id) => ((IHtmlSelectElement)cut.Find($"#{id}")).Value;

        // A new product prepared after a category was created in the popup of the previous form.
        var created = context.Render<ProductEditor>(parameters => parameters
            .Add(p => p.Staging, true).Add(p => p.Groups, stale));
        created.WaitForAssertion(() => { if (!Options(created, "product-category").Contains("TVCI")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(Options(created, "product-category").Contains("TVCI"),
            "Product form of the invoice pickup lists a category created after the page read its list");
        created.Find("#product-category").Change("TVCI");
        check(Options(created, "product-subcategory").Contains("Generice") && Selected(created, "product-subcategory") == "Generice",
            "Product form of the invoice pickup offers the subcategory of a category created after the page read its list");

        // A product already prepared, edited again: its category and subcategory stay chosen.
        var edited = context.Render<ProductEditor>(parameters => parameters
            .Add(p => p.Staging, true).Add(p => p.Groups, stale)
            .Add(p => p.InitialCategory, "TVCI").Add(p => p.InitialSubcategory, "Generice").Add(p => p.InitialName, "EBD3612"));
        edited.WaitForAssertion(() => { if (!Options(edited, "product-category").Contains("TVCI")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(Selected(edited, "product-category") == "TVCI" && Selected(edited, "product-subcategory") == "Generice",
            "Editing a prepared product keeps its category and subcategory chosen, also when the page's list is older");

        // A category that does not exist (anymore) is still replaced by an existing one.
        var unknown = context.Render<ProductEditor>(parameters => parameters
            .Add(p => p.Staging, true).Add(p => p.Groups, stale).Add(p => p.InitialCategory, "Inexistenta").Add(p => p.InitialSubcategory, "X"));
        unknown.WaitForAssertion(() => { if (Selected(unknown, "product-category").Length == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(Options(unknown, "product-category").Contains(Selected(unknown, "product-category")),
            "A prepared product whose category no longer exists gets an existing category, not an empty one");

        // The picture of the product can be searched on the internet by its code, in a popup window.
        var withCode = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale).Add(p => p.InitialName, "DS-3E0109P-E-M"));
        var search = withCode.Find("#product-image-search");
        check(search.GetAttribute("href") == "https://www.google.com/search?tbm=isch&q=DS-3E0109P-E-M" && (search.GetAttribute("onclick") ?? "").Contains("window.open") && search.GetAttribute("target") == "product-image-search",
            "Product form: the picture of the product can be searched on the internet by its code, in a popup window");
        var oddCode = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale).Add(p => p.InitialName, " A&B 1/2 "));
        check(oddCode.Find("#product-image-search").GetAttribute("href") == "https://www.google.com/search?tbm=isch&q=A%26B%201%2F2", "Product form: the code is encoded in the search address");
        var existing = (await repository.GetProductsAsync()).First();
        var editing = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Original, existing).Add(p => p.Groups, stale));
        check(editing.Find("#product-image-search").GetAttribute("href") == "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(existing.Name.Trim()),
            "Product form: the picture search by code is offered when editing an existing product too");
        // The reason of an edit: radio buttons between the summary generated from the changes (one change on a line, following the form) and the user's own text.
        string Auto(IRenderedComponent<ProductEditor> cut) => cut.Find("#product-change-reason-auto").GetAttribute("value") ?? "";
        var radios = editing.FindAll("input[type=radio][name='product-change-reason-mode']");
        check(radios.Count == 2 && radios[0].HasAttribute("checked") && !radios[1].HasAttribute("checked") && Auto(editing) == "" && editing.FindAll("#product-change-reason").Count == 0,
            "Product edit: the reason is chosen with radio buttons; the generated one is selected first, empty until something is changed, and the written field is hidden");
        editing.Find("#product-description").Input("Descriere noua pentru test");
        var oneChange = Auto(editing);
        check(oneChange.Split('\n').Length == 1 && oneChange.StartsWith("Descriere: ") && oneChange.Contains("Descriere noua pentru test") && oneChange.Contains(" → "),
            "Product edit: the generated reason shows a change as soon as it is made");
        editing.Find("#product-code").Input(existing.Name + "-X");
        var twoChanges = Auto(editing).Split('\n');
        check(twoChanges.Length == 2 && twoChanges[0].StartsWith(ProductCode.Label + ": ") && twoChanges[1].StartsWith("Descriere: "), "Product edit: each change is on its own line of the generated reason");
        editing.Find("#product-description").Input(existing.Description);
        check(Auto(editing).Split('\n') is [var only] && only.StartsWith(ProductCode.Label + ": "), "Product edit: a change put back to its original value leaves the generated reason");
        editing.Find("#product-code").Input(existing.Name);
        check(Auto(editing) == "", "Product edit: with every change put back the generated reason is empty again");
        editing.Find("#product-code").Input(existing.Name + "-Y");
        editing.Find("form").Submit();
        editing.WaitForAssertion(() => { if (!editing.Markup.Contains(existing.Name + "-Y")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(editing.Markup.Contains("Salvezi modificările produsului?") && editing.Markup.Contains(ProductCode.Label + ": "), "Product edit: the confirmation shows the generated reason when it is the one chosen");
        var written = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Original, existing).Add(p => p.Groups, stale));
        written.Find("#product-code").Input(existing.Name + "-Z");
        written.FindAll("input[type=radio][name='product-change-reason-mode']")[1].Change(true);
        check(written.FindAll("#product-change-reason").Count == 1, "Product edit: choosing the written reason shows its field");
        written.Find("#product-change-reason").Input("Motiv scris de mine");
        written.Find("form").Submit();
        written.WaitForAssertion(() => { if (!written.Markup.Contains("Motiv scris de mine")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(written.Markup.Contains("Motiv scris de mine"), "Product edit: the confirmation shows the written reason when it is the one chosen");
        var noChange = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Original, existing).Add(p => p.Groups, stale));
        noChange.Find("form").Submit();
        check(noChange.Markup.Contains("Nu ai făcut nicio modificare."), "Product edit: with the generated reason and no change there is nothing to save");
        var noCode = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale));
        check(noCode.FindAll("#product-image-search").Count == 0, "Product form: without a code there is no picture search link");

        // Required parameters of a subcategory: the form asks for the model and a value for each parameter, composes the code and refuses an incomplete product.
        await repository.CreateCategoryAsync("Camere video");
        await repository.CreateSubcategoryAsync("Camere video", "Camere IP");
        var lens = await parameterStore.AddParameterAsync(new ProductGroup("Camere video", "Camere IP"), "Lentila", "mm", ParameterKind.Number);
        var lens28 = await parameterStore.AddValueAsync(lens.Id, "2,8");
        await parameterStore.AddValueAsync(lens.Id, "4");
        StagedProduct? stagedParametric = null;
        var parametric = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale)
            .Add(p => p.InitialCategory, "Camere video").Add(p => p.InitialSubcategory, "Camere IP").Add(p => p.InitialName, "CAM-1")
            .Add(p => p.Staged, (StagedProduct item) => { stagedParametric = item; }));
        parametric.WaitForAssertion(() => parametric.Find($"#product-parameter-{lens.Id}"), TimeSpan.FromSeconds(5));
        check(parametric.Find("label[for=product-code]").TextContent.Contains("Model") && parametric.Find("#product-code").GetAttribute("value") == "CAM-1"
              && parametric.FindAll($"#product-parameter-{lens.Id} option").Count == 3 && parametric.Find("#product-composed-code").TextContent.Contains("incomplet"),
            "Product form: a subcategory with a required parameter asks for the model and a value (the typed code becomes the model)");
        parametric.Find("form").Submit();
        check(stagedParametric is null && parametric.Markup.Contains("cere modelul și valoarea fiecărui parametru"), "Product form: a product without the value of a required parameter is refused");
        parametric.Find($"#product-parameter-{lens.Id}").Change(lens28.Id.ToString());
        check(parametric.Find("#product-composed-code").TextContent.Contains("CAM-1 - 2.8 mm") && !parametric.Find("#product-composed-code").TextContent.Contains("incomplet"),
            "Product form: the code follows the model and the chosen values");
        parametric.Find($"input[aria-label='Valoare nouă pentru Lentila']").Input("6");
        parametric.FindAll("button").First(button => button.TextContent.Contains("Adaugă valoarea")).Click();
        parametric.WaitForAssertion(() => { if (!parametric.Find("#product-composed-code").TextContent.Contains("CAM-1 - 6 mm")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(parametric.FindAll($"#product-parameter-{lens.Id} option").Count == 4, "Product form: a new value is added to the list and chosen");
        parametric.Find($"#product-parameter-{lens.Id}").Change(lens28.Id.ToString());
        parametric.Find("form").Submit();
        parametric.WaitForAssertion(() => { if (stagedParametric is null) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(stagedParametric!.Input.Name == "CAM-1 - 2.8 mm" && stagedParametric.Input.BaseModel == "CAM-1" && stagedParametric.Input.Parameters is [{ } chosenParameter] && chosenParameter.ValueId == lens28.Id,
            "Product form: the prepared product carries the model, the composed code and the chosen values");
        var otherSubcategory = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale).Add(p => p.InitialCategory, "Consumabile").Add(p => p.InitialSubcategory, "Fixare"));
        otherSubcategory.WaitForAssertion(() => { if (Options(otherSubcategory, "product-category").Length == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(otherSubcategory.FindAll("#product-composed-code").Count == 0 && otherSubcategory.Find("label[for=product-code]").TextContent.Contains(ProductCode.Label),
            "Product form: a subcategory without parameters keeps the single code field");

        // Invoice pickup and required parameters: a row whose code is the model of products with parameters offers their variants.
        var v1 = new Product(901, "Camere video", "Camere IP", "DS-2CD1043 - 2.8 mm", "", 3);
        var v2 = new Product(902, "Camere video", "Camere IP", "DS-2CD1043 - 4 mm", "", 0);
        var other = new Product(903, "Camere video", "Camere IP", "ALT-100", "", 1);
        var models = new Dictionary<int, string> { [901] = "DS-2CD1043", [902] = "DS-2CD1043" };
        var catalogNow = new List<Product> { v1, v2, other };
        var plain = InvoiceProductMatcher.Match("DS-2CD1043", "DS-2CD1043 - Camera 4 mm", catalogNow);
        var viaModel = InvoiceVariants.Apply(plain, "DS-2CD1043 - Camera 4 mm", catalogNow, models);
        check(viaModel.Variants is { Count: 2 } && viaModel.BaseModel == "DS-2CD1043" && viaModel.Exact is null && viaModel.VariantProposal == v2.Id,
            "Invoice variants: the code of a row that is the model of products with parameters offers the variants and proposes the one written in the name");
        var unclear = InvoiceVariants.Apply(plain, "Camera IP 5 MP", catalogNow, models);
        check(unclear.Variants is { Count: 2 } && unclear.VariantProposal is null, "Invoice variants: when the name does not say the variant none is proposed");
        var both = InvoiceVariants.Apply(plain, "Camera 2.8 mm si 4 mm", catalogNow, models);
        check(both.VariantProposal is null || both.VariantProposal == v1.Id || both.VariantProposal == v2.Id, "Invoice variants: a name with two variants proposes at most one");
        var linked = InvoiceProductMatcher.Match("INT-77", "Camera", catalogNow, new Dictionary<string, int> { [SupplierProductCodeRules.Key("INT-77")] = v1.Id }, "INT-77");
        var viaLink = InvoiceVariants.Apply(linked, "Camera", catalogNow, models);
        check(viaLink.Variants is { Count: 2 } && viaLink.VariantProposal == v1.Id && viaLink.Note.Contains("anterioară"), "Invoice variants: a code of the supplier linked to a variant proposes that variant, but the others stay open");
        var textBeatsLink = InvoiceVariants.Apply(linked, "Camera 4 mm", catalogNow, models);
        check(textBeatsLink.VariantProposal == v2.Id, "Invoice variants: the variant written in the row wins over the one linked by an earlier invoice");
        var notModel = InvoiceVariants.Apply(InvoiceProductMatcher.Match("ALT-100", "Altceva", catalogNow), "Altceva", catalogNow, models);
        check(notModel.Variants is null && notModel.Exact?.Id == other.Id, "Invoice variants: a product without parameters is matched as before");
        check(InvoiceVariants.Apply(plain, "x", catalogNow, new Dictionary<int, string>()) == plain, "Invoice variants: with no product having parameters nothing changes");

        var variantCell = context.Render<Components.Shared.PickupProductCell>(parameters => parameters.Add(p => p.Match, viaModel).Add(p => p.ProductId, viaModel.VariantProposal)
            .Add(p => p.Incomplete, (IReadOnlySet<int>)new HashSet<int> { v1.Id }));
        check(variantCell.FindAll("input[type=radio]").Count == 3 && variantCell.FindAll("input[type=radio]").Count(radio => radio.HasAttribute("checked")) == 1
              && variantCell.Markup.Contains("DS-2CD1043 - 4 mm") && variantCell.Markup.Contains("Variantă nouă") && variantCell.Markup.Contains("Parametri necompletați"),
            "Pickup cell: the variants of the model are options, the proposed one is chosen, a new variant can be prepared and an incomplete variant is flagged");
        var pickedVariant = 0;
        var pickCell = context.Render<Components.Shared.PickupProductCell>(parameters => parameters.Add(p => p.Match, viaModel)
            .Add(p => p.OnPick, (int id) => { pickedVariant = id; }));
        pickCell.FindAll("input[type=radio]")[0].Change(true);
        check(pickedVariant == v1.Id, "Pickup cell: choosing a variant gives its product");

        // Ambiguous characters in the code and the description of a product (new product of the products page or of the pickup alike) are flagged, not refused.
        var doubtful = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale)
            .Add(p => p.InitialCategory, "Consumabile").Add(p => p.InitialSubcategory, "Fixare").Add(p => p.InitialName, "DS-2CD2T43G2-2L|").Add(p => p.InitialDescription, "Camera [IP]"));
        doubtful.WaitForAssertion(() => doubtful.Find("#product-code-doubt"), TimeSpan.FromSeconds(5));
        check(doubtful.Find("#product-code-doubt").TextContent.Contains('|') && doubtful.Find("#product-description-doubt").TextContent.Contains("[]") && !doubtful.Markup.Contains("factur"),
            "Product form: ambiguous characters in the code and in the description are flagged for checking");
        doubtful.Find("#product-code").Input("DS-2CD2T43G2-2L");
        doubtful.Find("#product-description").Input("Camera IP");
        check(doubtful.FindAll("#product-code-doubt").Count == 0 && doubtful.FindAll("#product-description-doubt").Count == 0, "Product form: the flags go away when the characters are corrected");

        // Usage scenarios of the entry form: negative stock must be settled (zero or the real quantity); an entry says where it comes from.
        var resolution = new NegativeStockResolution();
        var changes = 0;
        var panel = context.Render<Components.Shared.NegativeStockPanel>(parameters => parameters.Add(p => p.Resolution, resolution).Add(p => p.Stock, -3)
            .Add(p => p.Changed, () => { changes++; }));
        check(panel.Markup.Contains("Stoc curent -3") && !resolution.IsResolved, "Negative stock panel: warns with the current stock and starts unresolved");
        panel.FindAll("button").First(button => button.TextContent.Contains("pe 0")).Click();
        check(resolution.IsResolved && resolution.RealQuantity == 0 && changes == 1, "Negative stock panel: \"set to zero\" resolves with the real quantity 0");
        panel.Find("input").Input("4");
        check(resolution.IsResolved && !resolution.Zero && resolution.RealQuantity == 4 && !panel.Markup.Contains("validation-message"), "Negative stock panel: a typed real quantity replaces the zero choice and is accepted at once (on every keystroke)");
        panel.Find("input").Input("-10");
        check(!resolution.IsResolved && panel.Markup.Contains("validation-message") && panel.Find("input").GetAttribute("aria-invalid") == "true",
            "Negative stock panel: a negative real quantity is flagged and does not resolve");
        panel.Find("input").Input("0");
        check(resolution.IsResolved && resolution.RealQuantity == 0 && !resolution.Zero && !panel.Markup.Contains("validation-message"), "Negative stock panel: a real quantity of 0 is valid");
        panel.Find("input").Input("1.5");
        check(!resolution.IsResolved && panel.Markup.Contains("validation-message"), "Negative stock panel: a quantity that is not a whole number is flagged");
        panel.Find("input").Input("4");

        IReadOnlyList<AnafFieldProposal>? chosenAnaf = null;
        var anafDialog = context.Render<Components.Shared.AnafDifferencesDialog>(parameters => parameters
            .Add(p => p.Items, new AnafFieldProposal[] { new("Adresă fiscală", "Str. Veche 1", "Str. Noua 2"), new("Cod poștal", "100", "200") })
            .Add(p => p.Applied, (IReadOnlyList<AnafFieldProposal> chosen) => { chosenAnaf = chosen; }));
        check(anafDialog.Markup.Contains("Str. Veche 1") && anafDialog.Markup.Contains("Str. Noua 2") && anafDialog.FindAll("input[role=switch]").Count == 2, "ANAF dialog: each different field shows the user's value, the ANAF value and a switch");
        anafDialog.FindAll("button").First(button => button.TextContent.Contains("Aplică")).Click();
        check(chosenAnaf is { Count: 0 }, "ANAF dialog: nothing is taken from ANAF unless the user switches it on (his own values are the default)");
        anafDialog.FindAll("input[role=switch]")[1].Change(true);
        anafDialog.FindAll("button").First(button => button.TextContent.Contains("Aplică")).Click();
        check(chosenAnaf is { Count: 1 } && chosenAnaf[0].Label == "Cod poștal", "ANAF dialog: only the fields switched on are returned");

        var memorySuppliers = new MemorySupplierRepository();
        var picked = await memorySuppliers.CreateAsync(new SupplierInput { Name = "Furnizor Componenta SRL", Cui = SupplierChecks.ValidCui(1234567) });
        using var entryContext = new BunitContext();
        entryContext.JSInterop.Mode = JSRuntimeMode.Loose;
        entryContext.Services.AddLogging();
        entryContext.Services.AddSingleton<ISupplierRepository>(memorySuppliers);
        entryContext.Services.AddSingleton<ISupplierInvoiceRepository>(new MemorySupplierInvoiceRepository(memorySuppliers));
        var entryInput = new StockMovementInput { Kind = StockMovementKind.Entry, Date = DateOnly.FromDateTime(DateTime.Today) };
        var origin = entryContext.Render<Components.Shared.EntryOriginPicker>(parameters => parameters.Add(p => p.Model, entryInput));
        check(origin.FindAll("[role=tab]").Count == 3 && origin.Markup.Contains("Preia din PDF"), "Entry origin: three tabs and the separate button for the PDF pickup");
        check(origin.Instance.ValidationError() == "Alege motivul intrării libere.", "Entry origin: a free entry needs its reason");
        entryInput.FreeType = FreeEntryType.AwaitedInvoice;
        check(origin.Instance.ValidationError() == StockMovementRules.AwaitedSupplierRequiredMessage, "Entry origin: an awaited invoice needs its supplier");
        entryInput.FreeSupplierId = picked.Id;
        check(origin.Instance.ValidationError() is null, "Entry origin: an awaited invoice with its supplier is complete");
        origin.FindAll("[role=tab]")[1].Click();
        check(entryInput.FreeType is null && entryInput.FreeSupplierId is null && origin.Instance.ValidationError() == "Alege factura.", "Entry origin: switching to an existing invoice clears the free reason and asks for the invoice");
        origin.FindAll("[role=tab]")[2].Click();
        check(origin.Instance.ValidationError() is { Length: > 0 }, "Entry origin: a new invoice typed by hand needs supplier, number and date");
    }
}
