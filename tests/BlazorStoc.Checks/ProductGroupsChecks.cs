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
        context.Services.AddScoped<UnsavedChanges>();

        var page = context.Render<ProductGroups>();
        page.WaitForAssertion(() => { if (page.FindAll(".category-management-card").Count < 2) throw new Exception("pending"); }, TimeSpan.FromSeconds(10));
        var cards = page.FindAll(".category-management-card");
        var title = cards[1].QuerySelector(".category-title")!.TextContent;
        var existingSubcategories = cards[1].QuerySelectorAll("tbody tr").Length;

        // "+ Adaugă subcategorie" of the second category: the form opens inside that category, under its last subcategory, not at the top of the page.
        cards[1].QuerySelectorAll("button").First(button => button.TextContent.Contains("Adaugă subcategorie")).Click();
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
        page.FindAll("tbody button").First(button => button.TextContent.Contains("Editează")).Click();
        page.WaitForAssertion(() => page.Find("#subcategory-name"), TimeSpan.FromSeconds(5));
        check(page.Find("#subcategory-name").Closest(".category-management-card") is null, "Categories page: editing a subcategory opens the form above the list");

        // The reason of an edit: generated from the changes (one on each line) or written, chosen with radio buttons.
        string Auto(string id) => page.Find($"#{id}-auto").GetAttribute("value") ?? "";
        var original = page.Find("#subcategory-name").GetAttribute("value") ?? "";
        check(page.FindAll("input[type=radio][name='subcategory-reason-mode']").Count == 2 && Auto("subcategory-reason") == "", "Categories page: the reason of a subcategory edit is chosen with radio buttons, the generated one empty until a change");
        page.Find("#subcategory-name").Change(original + " modificata");
        check(Auto("subcategory-reason") == $"Subcategorie: {original} → {original} modificata", "Categories page: the generated reason shows the renamed subcategory");
        // The category of a subcategory is chosen directly in the form (radio buttons), not from a list that opens.
        check(page.FindAll("#subcategory-category").Count == 0 && page.FindAll("select").Count == 0, "Categories page: the category of a subcategory is not chosen from a drop-down list");
        var choices = page.FindAll("input[type=radio][name='subcategory-category']");
        var originalChoice = choices.Select((input, index) => (input, index)).First(item => item.input.HasAttribute("checked")).index;
        void ChooseCategory(int index) => page.FindAll("input[type=radio][name='subcategory-category']")[index].Change(true);
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
        page.FindAll("button").First(button => button.TextContent.Contains("Editează categoria")).Click();
        page.WaitForAssertion(() => page.Find("#category-name"), TimeSpan.FromSeconds(5));
        var categoryName = page.Find("#category-name").GetAttribute("value") ?? "";
        page.Find("#category-name").Change(categoryName + " noua");
        check(Auto("category-reason") == $"Categorie: {categoryName} → {categoryName} noua", "Categories page: the generated reason of a category rename");
    }
}
