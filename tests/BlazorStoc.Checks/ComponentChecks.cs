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

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IProductRepository>(repository);
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
        check(radios.Count == 2 && radios[0].HasAttribute("checked") && !radios[1].HasAttribute("checked") && Auto(editing) == "" && editing.Find("#product-change-reason").HasAttribute("disabled"),
            "Product edit: the reason is chosen with radio buttons; the generated one is selected first, empty until something is changed, and the written one is disabled");
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
        check(!written.Find("#product-change-reason").HasAttribute("disabled"), "Product edit: choosing the written reason enables its field");
        written.Find("#product-change-reason").Input("Motiv scris de mine");
        written.Find("form").Submit();
        written.WaitForAssertion(() => { if (!written.Markup.Contains("Motiv scris de mine")) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(written.Markup.Contains("Motiv scris de mine"), "Product edit: the confirmation shows the written reason when it is the one chosen");
        var noChange = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Original, existing).Add(p => p.Groups, stale));
        noChange.Find("form").Submit();
        check(noChange.Markup.Contains("Nu ai făcut nicio modificare."), "Product edit: with the generated reason and no change there is nothing to save");
        var noCode = context.Render<ProductEditor>(parameters => parameters.Add(p => p.Staging, true).Add(p => p.Groups, stale));
        check(noCode.FindAll("#product-image-search").Count == 0, "Product form: without a code there is no picture search link");
    }
}
