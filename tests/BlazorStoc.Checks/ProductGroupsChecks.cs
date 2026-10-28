using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The categories page (Categorii și subcategorii): where the form of a subcategory opens.
public static class ProductGroupsChecks
{
    // For WaitForAssertion: throws until the page shows what the check waits for.
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("The page does not show the expected state yet."); }

    public static async Task RunAsync(Action<bool, string> check)
    {
        await ProductGroupsTreeChecks.RunAsync(check);
        var access = new TestAccessControl(true, "groups.admin");
        var repository = new DemoProductRepository(access);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IProductRepository>(repository);
        context.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
        context.Services.AddSingleton<IProductTagRepository>(new DemoProductTagRepository());
        context.Services.AddSingleton<ISubcategoryTree>(provider => new DemoSubcategoryTree(provider.GetRequiredService<IProductRepository>()));
        context.Services.AddSingleton<ICatalogSettings, DemoCatalogSettings>();
        context.Services.AddScoped<UnsavedChanges>();

        // The names of categories and subcategories are unique all together (a subcategory is not named like a category, nor the other way round).
        var uniqueRepository = new DemoProductRepository(access);
        var known = await uniqueRepository.GetGroupsAsync();
        var someCategory = known[0].Category;
        var someSubcategory = known[0].Subcategory;
        async Task<bool> Rejected(Func<Task> action) { try { await action(); return false; } catch (ProductOperationException) { return true; } }
        check(await Rejected(() => uniqueRepository.CreateSubcategoryAsync(someCategory, someCategory.ToUpperInvariant()))
              && await Rejected(() => uniqueRepository.CreateCategoryAsync(someSubcategory))
              && await Rejected(() => uniqueRepository.RenameCategoryAsync(someCategory, someSubcategory, "test"))
              && await Rejected(() => uniqueRepository.UpdateSubcategoryAsync(known[0], someCategory, someCategory, "test")),
            "Categories: a subcategory cannot be named like a category and a category not like a subcategory (create and rename, case aside)");

        // Only an empty category (no subcategory, no product) and an empty subcategory (no product) are deleted, with a reason.
        var usedProduct = (await uniqueRepository.GetProductsAsync()).First();
        await uniqueRepository.CreateCategoryAsync("Categorie goala test");
        await uniqueRepository.CreateSubcategoryAsync("Categorie goala test", "Subcategorie goala test");
        var deleteCategoryBlocked = await Rejected(() => uniqueRepository.DeleteCategoryAsync("Categorie goala test", "test"));
        var deleteUsedSubcategoryBlocked = await Rejected(() => uniqueRepository.DeleteSubcategoryAsync(new ProductGroup(usedProduct.Category, usedProduct.Subcategory), "test"));
        await uniqueRepository.DeleteSubcategoryAsync(new ProductGroup("Categorie goala test", "Subcategorie goala test"), "nu mai e folosita");
        await uniqueRepository.DeleteCategoryAsync("Categorie goala test", "nu mai e folosita");
        var afterDelete = await uniqueRepository.GetGroupsAsync();
        check(deleteCategoryBlocked && deleteUsedSubcategoryBlocked && await Rejected(() => uniqueRepository.DeleteCategoryAsync(usedProduct.Category, "test"))
              && afterDelete.All(group => group.Category != "Categorie goala test" && group.Subcategory != "Subcategorie goala test"),
            "Categories: a category with subcategories or products and a subcategory with products cannot be deleted; an empty subcategory and then the empty category are deleted");

        // The order of the categories (side menu and this page) is arranged by the administrator only; the repository returns the arranged order.
        var arrangedBefore = (await uniqueRepository.GetGroupsAsync()).Select(group => group.Category).Distinct().ToList();
        var reversed = Enumerable.Reverse(arrangedBefore).ToList();
        await uniqueRepository.ReorderCategoriesAsync(reversed);
        var arrangedAfter = (await uniqueRepository.GetGroupsAsync()).Select(group => group.Category).Distinct().ToList();
        var limitedRefused = false;
        try { await new DemoProductRepository(new TestAccessControl(false, "groups.limited")).ReorderCategoriesAsync(reversed); }
        catch (AccessDeniedException) { limitedRefused = true; }
        check(arrangedBefore.Count > 1 && arrangedAfter.SequenceEqual(reversed) && limitedRefused
              && await Rejected(() => uniqueRepository.ReorderCategoriesAsync(reversed.Take(reversed.Count - 1).ToList())),
            "Categories: the administrator arranges the order of the categories and the repository returns it; a limited user and an incomplete list are refused");

        var page = context.Render<ProductGroups>();
        page.WaitForAssertion(() => { if (page.FindAll(".category-management-card").Count < 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        var cards = page.FindAll(".category-management-card");
        var title = cards[1].QuerySelector(".category-title")!.TextContent;
        var existingSubcategories = cards[1].QuerySelectorAll("tbody tr").Length;

        check(page.FindAll("[data-category-handle]").Count == cards.Count, "Categories page: the administrator has a drag handle on every category card");

        // The header of a category has icons: add subcategory, edit, delete; the delete icon is there but disabled for a category that is not empty, and says why in its tooltip.
        var deleteIcons = page.FindAll("button.row-icon.delete").Where(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Șterge categoria")).ToList();
        check(cards.All(card => card.QuerySelectorAll("button.row-icon.add").Length == 1 && card.QuerySelectorAll("button.row-icon.edit").Length >= 1) && deleteIcons.Count == cards.Count
              && deleteIcons.Any(button => button.HasAttribute("disabled") && (button.GetAttribute("title") ?? "").Contains("fără subcategorii și fără produse")),
            "Categories page: every category has the add-subcategory, edit and delete icons; delete is inactive with an explanation in its tooltip when the category is not empty");

        // "+ Adaugă subcategorie" of the second category: the form opens inside that category, under its last subcategory, not at the top of the page.
        cards[1].QuerySelectorAll("button.row-icon.add").First().Click();
        page.WaitForAssertion(() => page.Find("#subcategory-name"), TimeSpan.FromSeconds(5));
        check(page.FindAll("#subcategory-name").Count == 1, "Categories page: one form for the new subcategory");
        var form = page.Find(".delete-overlay > section.group-editor-dialog");
        check(form.QuerySelector("#subcategory-name") is not null && form.QuerySelectorAll("*").Any(item => item.TextContent.Contains($"Adaugă subcategorie în „{title}”")),
            "Categories page: the form of a new subcategory opens as a popup for its category");
        check(page.FindAll(".category-management-card").All(item => item.QuerySelector("#subcategory-name") is null),
            "Categories page: the form of a new subcategory is a popup over the page, not inside a category card");

        // Saved from there, the subcategory is created in that category.
        page.Find("#subcategory-name").Change("Subcategorie noua test");
        page.Find("section.group-editor-dialog .editor-actions button.primary").Click();
        page.WaitForAssertion(() => { if (page.FindAll("#subcategory-name").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        var groups = await repository.GetGroupsAsync();
        check(groups.Any(group => group.Category == title && group.Subcategory == "Subcategorie noua test"), "Categories page: the subcategory typed in the form is created in its category");

        // Editing an existing subcategory opens the same popup, with only the name (moving is done by dragging).
        page.WaitForAssertion(() => { if (page.FindAll("tbody button").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        page.FindAll("tbody button.row-icon.edit").First().Click();
        page.WaitForAssertion(() => page.Find("#subcategory-name"), TimeSpan.FromSeconds(5));
        check(page.Find("#subcategory-name").Closest(".group-editor-dialog") is not null, "Categories page: editing a subcategory opens the form as a popup");

        // The reason of an edit: generated from the changes (one on each line) or written, chosen with radio buttons.
        string Auto(string id) => page.Find($"#{id}-auto").GetAttribute("value") ?? "";
        var original = page.Find("#subcategory-name").GetAttribute("value") ?? "";
        check(page.FindAll("input[type=radio][name='subcategory-reason-mode']").Count == 2 && Auto("subcategory-reason") == "", "Categories page: the reason of a subcategory edit is chosen with radio buttons, the generated one empty until a change");
        page.Find("#subcategory-name").Change(original + " modificata");
        check(Auto("subcategory-reason") == $"Subcategorie: {original} → {original} modificata", "Categories page: the generated reason shows the renamed subcategory");
        // Only the name is edited: the category of a subcategory is not a field (the move is done by dragging).
        check(page.FindAll("#subcategory-category").Count == 0 && page.FindAll("input[type=radio][name='subcategory-category']").Count == 0,
            "Categories page: editing a subcategory changes only its name, without a category field");
        page.Find("#subcategory-name").Change(original);
        check(Auto("subcategory-reason") == "", "Categories page: putting the name back empties the generated reason");

        // Changed and cancelled: leaving without saving closes the form.
        page.Find("#subcategory-name").Change(original + " modificata");
        page.FindAll("button").First(button => button.TextContent.Contains("Anulează")).Click();
        page.WaitForAssertion(() => page.Find(".unsaved-changes-dialog"), TimeSpan.FromSeconds(5));
        page.Find(".unsaved-changes-dialog button.danger").Click();
        page.WaitForAssertion(() => { if (page.FindAll("#subcategory-name").Count != 0 || page.FindAll(".unsaved-changes-dialog").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(true, "Categories page: a renamed subcategory left without saving closes its form");
        page.FindAll("button.row-icon.edit").First(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Editează categoria")).Click();
        page.WaitForAssertion(() => page.Find("#category-name"), TimeSpan.FromSeconds(5));
        var categoryName = page.Find("#category-name").GetAttribute("value") ?? "";
        page.Find("#category-name").Change(categoryName + " noua");
        check(Auto("category-reason") == $"Categorie: {categoryName} → {categoryName} noua", "Categories page: the generated reason of a category rename");

        // Required parameters of a subcategory: a panel opened from the row, parameters and their values added by the user.
        var parametersPage = context.Render<ProductGroups>();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".category-management-list"), TimeSpan.FromSeconds(5));
        var firstSubcategory = parametersPage.FindAll("tbody button").First(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Parametri și etichete"));
        check(firstSubcategory.ClassList.Contains("params") && parametersPage.FindAll(".subcategory-parameters").Count == 0, "Categories page: each subcategory has one icon for its parameters and tags and the panel is closed");
        firstSubcategory.Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".subcategory-parameters"), TimeSpan.FromSeconds(5));
        check(parametersPage.FindAll("details.def-section").Count == 3 && parametersPage.FindAll("details.def-section[open]").Count == 0
              && parametersPage.FindAll("details.def-section > summary").Select(item => item.TextContent).SequenceEqual(["Etichete0", "Familii de parametri0", "Parametri0"]),
            "Categories page: parameters and tags are three collapsible sections (Etichete, Familii de parametri, Parametri), closed when the window opens");
        void ClickButton(string text) => parametersPage.FindAll("button").First(button => button.TextContent.Trim() == text).Click();
        check(parametersPage.FindAll("#new-parameter-name").Count == 0, "Categories page: the form of a new parameter appears only after the add button");
        ClickButton("Adaugă parametru");
        parametersPage.Find("#new-parameter-name").Input("Lentila");
        parametersPage.FindAll("fieldset.parameter-kind input[type=checkbox]")[1].Change(true);   // the kind is chosen with two switches (turning one on turns the other off)
        parametersPage.Find("#new-parameter-unit").Input("mm");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă parametrul")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".parameter-row"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".parameter-row").TextContent.Contains("Lentila") && parametersPage.Find(".parameter-row").TextContent.Contains("Număr") && parametersPage.Find(".parameter-row").TextContent.Contains("mm"),
            "Categories page: a number parameter with its unit is added to the subcategory and listed in the table");
        parametersPage.Find(".parameter-row button.row-icon.view").Click();
        parametersPage.Find(".parameter-add input").Input("2,8");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă valoarea")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".parameter-card tbody tr"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".parameter-card tbody tr").TextContent.Contains("2.8 mm"), "Categories page: a value is added to the parameter and shown with its unit, in one form (2,8 → 2.8)");
        parametersPage.Find(".parameter-add input").Input("2.80");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă valoarea")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".subcategory-parameters .error-banner"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".subcategory-parameters .error-banner").TextContent.Contains("există deja"), "Categories page: the same value written another way is refused");
        check(parametersPage.FindAll(".parameter-row button.row-icon.edit").Count == 1 && parametersPage.FindAll(".parameter-row button.row-icon.delete").Count == 1
              && parametersPage.FindAll(".parameter-card button.row-icon.edit").Count == 1 && parametersPage.FindAll(".parameter-card button.row-icon.delete").Count == 1,
            "Categories page: an administrator sees the icons to edit and delete the parameter and its values");

        // A second parameter of the Interval kind (a start and an end number per value), then a family of the two.
        check(parametersPage.FindAll(".family-card").Count == 0 && parametersPage.FindAll("#new-family-name").Count == 0 && parametersPage.Find("#family-needs-parameters").TextContent.Contains("cel puțin 2 parametri"),
            "Categories page: the family section is always there; with one parameter it explains that a family needs at least two and has no form yet");
        ClickButton("Adaugă parametru");
        parametersPage.Find("#new-parameter-name").Input("Varifocala");
        parametersPage.FindAll("fieldset.parameter-kind input[type=checkbox]")[2].Change(true);   // Text, Number, Interval, Fixed: four switches, one on at a time
        parametersPage.Find("#new-parameter-unit").Input("mm");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă parametrul")).Click();
        parametersPage.WaitForAssertion(() => Expect(parametersPage.FindAll(".parameter-row").Count == 2), TimeSpan.FromSeconds(5));
        parametersPage.FindAll(".parameter-row")[1].QuerySelector("button.row-icon.view")!.Click();
        var intervalCard = parametersPage.FindAll(".parameter-card")[1];
        check(parametersPage.FindAll(".parameter-row")[1].TextContent.Contains("Interval") && intervalCard.QuerySelectorAll(".interval-inputs input").Length == 2, "Categories page: an interval parameter has two number fields (start and end) for a new value");
        var intervalFields = intervalCard.QuerySelectorAll(".interval-inputs input");
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll(".interval-inputs input")[0].Input("2,8");
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll(".interval-inputs input")[1].Input("12");
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll("button.act-confirm").Last().Click();
        parametersPage.WaitForAssertion(() => Expect(parametersPage.FindAll(".parameter-card")[1].TextContent.Contains("2.8-12 mm")), TimeSpan.FromSeconds(5));
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll(".interval-inputs input")[0].Input("12");
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll(".interval-inputs input")[1].Input("2,8");
        parametersPage.FindAll(".parameter-card")[1].QuerySelectorAll("button.act-confirm").Last().Click();
        parametersPage.WaitForAssertion(() => Expect(parametersPage.Find(".subcategory-parameters .error-banner").TextContent.Contains("mai mic")), TimeSpan.FromSeconds(5));
        check(intervalFields.Length == 2, "Categories page: an interval whose start is above its end is refused with a clear message");

        ClickButton("Adaugă familie");
        parametersPage.WaitForAssertion(() => parametersPage.Find("#new-family-name"), TimeSpan.FromSeconds(5));
        parametersPage.Find("#new-family-name").Input("Lentila familie");

        var familyToggles = parametersPage.FindAll("fieldset.parameter-kind")[0].QuerySelectorAll("input[type=checkbox]");
        check(familyToggles.Length == 2 && parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă familia")).HasAttribute("disabled"), "Categories page: the family form lists the free parameters and needs at least two");
        parametersPage.FindAll("fieldset.parameter-kind")[0].QuerySelectorAll("input[type=checkbox]")[0].Change(true);
        check(parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă familia")).HasAttribute("disabled"), "Categories page: one parameter is not enough for a family");
        parametersPage.FindAll("fieldset.parameter-kind")[0].QuerySelectorAll("input[type=checkbox]")[1].Change(true);
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă familia")).Click();
        parametersPage.WaitForAssertion(() => Expect(parametersPage.Find(".family-card").TextContent.Contains("Lentila familie")), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".family-card").TextContent.Contains("Lentila") && parametersPage.Find(".family-card").TextContent.Contains("Varifocala") && parametersPage.FindAll(".parameter-row")[0].TextContent.Contains("Lentila familie"),
            "Categories page: the family groups the two parameters and each shows its family");
    }
}
