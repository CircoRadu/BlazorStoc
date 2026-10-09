using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The categories page (Categorii și subcategorii): where the form of a subcategory opens.
public static class ProductGroupsChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "groups.admin");
        var repository = new DemoProductRepository(access);
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IProductRepository>(repository);
        context.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
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

        var page = context.Render<ProductGroups>();
        page.WaitForAssertion(() => { if (page.FindAll(".category-management-card").Count < 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        var cards = page.FindAll(".category-management-card");
        var title = cards[1].QuerySelector(".category-title")!.TextContent;
        var existingSubcategories = cards[1].QuerySelectorAll("tbody tr").Length;

        // The header of a category has icons: add subcategory, edit, delete; the delete icon is there but disabled for a category that is not empty, and says why in its tooltip.
        var deleteIcons = page.FindAll("button.row-icon.delete").Where(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Șterge categoria")).ToList();
        check(cards.All(card => card.QuerySelectorAll("button.row-icon.add").Length == 1 && card.QuerySelectorAll("button.row-icon.edit").Length >= 1) && deleteIcons.Count == cards.Count
              && deleteIcons.Any(button => button.HasAttribute("disabled") && (button.GetAttribute("title") ?? "").Contains("fără subcategorii și fără produse")),
            "Categories page: every category has the add-subcategory, edit and delete icons; delete is inactive with an explanation in its tooltip when the category is not empty");

        // "+ Adaugă subcategorie" of the second category: the form opens inside that category, under its last subcategory, not at the top of the page.
        cards[1].QuerySelectorAll("button.row-icon.add").First().Click();
        page.WaitForAssertion(() => page.Find("#subcategory-name"), TimeSpan.FromSeconds(5));
        check(page.FindAll("#subcategory-name").Count == 1, "Categories page: one form for the new subcategory");
        var card = page.FindAll(".category-management-card").First(item => item.QuerySelector(".category-title")!.TextContent == title);
        var form = card.QuerySelector("section.product-editor");
        check(form is not null && form.QuerySelector("#subcategory-name") is not null && form.QuerySelectorAll("*").Any(item => item.TextContent.Contains($"Adaugă subcategorie în „{title}”")),
            "Categories page: the form of a new subcategory opens inside its category");
        check(form!.PreviousElementSibling is { } before && before.ClassList.Contains("table-scroll") && before.QuerySelectorAll("tbody tr").Length == existingSubcategories && form.NextElementSibling is null,
            "Categories page: the form of a new subcategory is under the last subcategory of the category");
        check(page.FindAll(".category-management-card").Where(item => item != card).All(item => item.QuerySelector("#subcategory-name") is null) &&
              page.FindAll("#subcategory-name").All(field => field.Closest(".category-management-card") is not null),
            "Categories page: the form of a new subcategory is not above the list or in another category");

        // Saved from there, the subcategory is created in that category.
        page.Find("#subcategory-name").Change("Subcategorie noua test");
        page.Find("section.product-editor .editor-actions button.primary").Click();
        page.WaitForAssertion(() => { if (page.FindAll("#subcategory-name").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        var groups = await repository.GetGroupsAsync();
        check(groups.Any(group => group.Category == title && group.Subcategory == "Subcategorie noua test"), "Categories page: the subcategory typed in the form is created in its category");

        // Editing an existing subcategory still opens the form above the list.
        page.WaitForAssertion(() => { if (page.FindAll("tbody button").Count == 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        page.FindAll("tbody button.row-icon.edit").First().Click();
        page.WaitForAssertion(() => page.Find("#subcategory-name"), TimeSpan.FromSeconds(5));
        check(page.Find("#subcategory-name").Closest(".category-management-card") is null, "Categories page: editing a subcategory opens the form above the list");

        // The reason of an edit: generated from the changes (one on each line) or written, chosen with radio buttons.
        string Auto(string id) => page.Find($"#{id}-auto").GetAttribute("value") ?? "";
        var original = page.Find("#subcategory-name").GetAttribute("value") ?? "";
        check(page.FindAll("input[type=radio][name='subcategory-reason-mode']").Count == 2 && Auto("subcategory-reason") == "", "Categories page: the reason of a subcategory edit is chosen with radio buttons, the generated one empty until a change");
        page.Find("#subcategory-name").Change(original + " modificata");
        check(Auto("subcategory-reason") == $"Subcategorie: {original} → {original} modificata", "Categories page: the generated reason shows the renamed subcategory");
        // The category of a subcategory (only when it is moved) is picked or typed in a field with filtering, not chosen from radio buttons.
        check(page.FindAll("input[type=radio][name='subcategory-category']").Count == 0 && page.FindAll("input#subcategory-category[role=combobox]").Count == 1 && page.FindAll("select").Count == 0,
            "Categories page: the category of a subcategory is picked or typed in a filtering field");
        page.Find("#subcategory-category").Click();
        var originalChoice = page.FindAll("#subcategory-category-list li[role=option]").Select((item, index) => (item, index)).First(entry => entry.item.GetAttribute("aria-selected") == "true").index;
        void ChooseCategory(int index) { page.Find("#subcategory-category").Click(); page.Find($"#subcategory-category-option-{index}").MouseDown(); }
        ChooseCategory(originalChoice == 0 ? 1 : 0);
        check(Auto("subcategory-reason").Split('\n') is [var renamed, var moved] && renamed.StartsWith("Subcategorie: ") && moved.StartsWith("Categorie: "), "Categories page: renaming and moving a subcategory are two lines of the generated reason");
        page.Find("#subcategory-name").Change(original);
        ChooseCategory(originalChoice);
        check(Auto("subcategory-reason") == "", "Categories page: putting the changes back empties the generated reason");

        // Moved to another category and cancelled: leaving without saving closes the form.
        ChooseCategory(originalChoice == 0 ? 1 : 0);
        page.FindAll("button").First(button => button.TextContent.Contains("Anulează")).Click();
        page.WaitForAssertion(() => page.Find(".unsaved-changes-dialog"), TimeSpan.FromSeconds(5));
        page.Find(".unsaved-changes-dialog button.danger").Click();
        page.WaitForAssertion(() => { if (page.FindAll("#subcategory-name").Count != 0 || page.FindAll(".unsaved-changes-dialog").Count != 0) throw new Exception("pending"); }, TimeSpan.FromSeconds(5));
        check(true, "Categories page: a subcategory moved to another category and left without saving closes its form");
        page.FindAll("button.row-icon.edit").First(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Editează categoria")).Click();
        page.WaitForAssertion(() => page.Find("#category-name"), TimeSpan.FromSeconds(5));
        var categoryName = page.Find("#category-name").GetAttribute("value") ?? "";
        page.Find("#category-name").Change(categoryName + " noua");
        check(Auto("category-reason") == $"Categorie: {categoryName} → {categoryName} noua", "Categories page: the generated reason of a category rename");

        // Required parameters of a subcategory: a panel opened from the row, parameters and their values added by the user.
        var parametersPage = context.Render<ProductGroups>();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".category-management-list"), TimeSpan.FromSeconds(5));
        var firstSubcategory = parametersPage.FindAll("tbody button").First(button => (button.GetAttribute("aria-label") ?? "").StartsWith("Parametri obligatori"));
        check(firstSubcategory.TextContent.Contains("Parametri") && parametersPage.FindAll(".subcategory-parameters").Count == 0, "Categories page: each subcategory has a button for its required parameters and the panel is closed");
        firstSubcategory.Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".subcategory-parameters"), TimeSpan.FromSeconds(5));
        parametersPage.Find("#new-parameter-name").Input("Lentila");
        parametersPage.FindAll("input[type=radio][name='new-parameter-kind']")[1].Change(true);
        parametersPage.Find("#new-parameter-unit").Input("mm");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă parametrul")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".parameter-card"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".parameter-card-heading").TextContent.Contains("Lentila") && parametersPage.Find(".parameter-card-heading").TextContent.Contains("Număr"), "Categories page: a number parameter with its unit is added to the subcategory");
        parametersPage.Find(".parameter-add input").Input("2,8");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă valoarea")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".parameter-card tbody tr"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".parameter-card tbody tr").TextContent.Contains("2.8 mm"), "Categories page: a value is added to the parameter and shown with its unit, in one form (2,8 → 2.8)");
        parametersPage.Find(".parameter-add input").Input("2.80");
        parametersPage.FindAll("button").First(button => button.TextContent.Contains("Adaugă valoarea")).Click();
        parametersPage.WaitForAssertion(() => parametersPage.Find(".subcategory-parameters .error-banner"), TimeSpan.FromSeconds(5));
        check(parametersPage.Find(".subcategory-parameters .error-banner").TextContent.Contains("există deja"), "Categories page: the same value written another way is refused");
        check(parametersPage.FindAll(".parameter-card button.row-icon.edit").Count == 2 && parametersPage.FindAll(".parameter-card button.row-icon.delete").Count == 2,
            "Categories page: an administrator sees the icons to edit and delete the parameter and its values");
    }
}
