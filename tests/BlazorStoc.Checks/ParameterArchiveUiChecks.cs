using BlazorStoc.Components.Shared;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The parameters and tags panel of a subcategory (migration 56): used definitions are archived instead of deleted, with the products affected listed first; the archive
// tab restores them (a product whose model lost the baked text is listed with an automatic proposal), the retention period gates giving up, and unused definitions are deleted for good.
public static class ParameterArchiveUiChecks
{
    private static void Expect(bool condition) { if (!condition) throw new InvalidOperationException("The panel does not show the expected state yet."); }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "archive.admin");
        var parameters = new DemoProductParameterRepository();
        var tags = new DemoProductTagRepository();
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IProductParameterRepository>(parameters);
        context.Services.AddSingleton<IProductTagRepository>(tags);
        context.Services.AddSingleton<ICatalogSettings, DemoCatalogSettings>();

        var group = new ProductGroup("Supraveghere", "Camere IP");
        var lens = await parameters.AddParameterAsync(group, "Lentila", "mm", ParameterKind.Number);
        var color = await parameters.AddParameterAsync(group, "Culoare", "", ParameterKind.Text);
        var spare = await parameters.AddParameterAsync(group, "Rezerva", "", ParameterKind.Text);
        parameters.SeedUse(lens.Id, 1, "CAM-1 - 2.8 mm");
        parameters.SeedUse(lens.Id, 2, "CAM-2 - 2.8 mm");
        parameters.SeedBroken(2);
        var poe = await tags.AddTagAsync(group, "PoE");
        await tags.SetProductTagsAsync(1, [poe.Id]);

        var panel = context.Render<SubcategoryParametersPanel>(builder => builder.Add(item => item.Group, group));
        panel.WaitForAssertion(() => Expect(panel.FindAll("button.row-icon.archive").Count == 4), TimeSpan.FromSeconds(10));
        check(panel.FindAll("button.row-icon.delete").All(button => !(button.GetAttribute("aria-label") ?? "").StartsWith("Șterge parametrul", StringComparison.Ordinal))
              && panel.FindAll("button.row-icon.archive").Count == 4,
            "Parameters panel: parameters and tags have an archive icon instead of a delete icon");

        // A used parameter: the dialog lists the products renamed, offers no deletion, and asks for the reason.
        panel.Find("button[aria-label='Arhivează parametrul Lentila']").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".archive-dialog").Count == 1), TimeSpan.FromSeconds(10));
        check(panel.FindAll(".archive-preview tbody tr").Count == 2 && panel.Find(".archive-preview").TextContent.Contains("Cod nou", StringComparison.Ordinal)
              && panel.FindAll(".archive-dialog .editor-actions button.danger").All(button => button.TextContent.Trim() != "Șterge definitiv"),
            "Parameters panel: archiving a used parameter lists the products whose code changes and does not offer deletion");
        panel.Find(".archive-dialog .editor-actions button.primary").Click();
        check(panel.FindAll(".archive-dialog").Count == 1 && panel.FindAll(".archive-dialog .validation-message").Count > 0, "Parameters panel: archiving asks for a reason");
        panel.Find(".archive-dialog textarea").Input("Lentila nu se mai foloseste");
        panel.Find(".archive-dialog .editor-actions button.primary").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".archive-dialog").Count == 0 && panel.FindAll("#section-archive tbody tr").Count == 1), TimeSpan.FromSeconds(10));
        check(panel.FindAll("button[aria-label='Arhivează parametrul Lentila']").Count == 0 && panel.Find("#section-archive tbody tr").TextContent.Contains("Lentila", StringComparison.Ordinal)
              && panel.Find("#section-archive tbody tr").TextContent.Contains("2 produse", StringComparison.Ordinal),
            "Parameters panel: the archived parameter leaves the live list and appears in the archive with its products");

        // Giving up the restore is not available yet; the history opens.
        check(panel.Find("#section-archive button.giveup").HasAttribute("disabled") && (panel.Find("#section-archive button.giveup").GetAttribute("title") ?? "").Contains("devine posibilă din", StringComparison.Ordinal),
            "Parameters panel: giving up the restore is disabled during the retention period and says when it becomes possible");
        panel.Find("#section-archive button.history").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".usage-dialog").Count == 1), TimeSpan.FromSeconds(10));
        check(panel.FindAll(".usage-table tbody tr").Count == 2 && panel.Find(".usage-table").TextContent.Contains("Arhivat", StringComparison.Ordinal),
            "Parameters panel: the history dialog lists where the parameter was used");
        panel.Find(".usage-dialog .editor-actions button").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".usage-dialog").Count == 0));

        // Restore: the product whose model lost the baked text is listed with a proposal; the restore applies with the automatic proposal.
        panel.Find("#section-archive button.restore").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".restore-dialog").Count == 1), TimeSpan.FromSeconds(10));
        check(panel.FindAll(".restore-table tbody tr").Count == 2 && panel.FindAll(".restore-table tr.restore-problem").Count == 1
              && panel.Find(".restore-table tr.restore-problem").TextContent.Contains("Textul copt nu mai e la finalul modelului", StringComparison.Ordinal)
              && panel.Find(".restore-table tr.restore-problem .restore-new").TextContent.Contains("CAM-2", StringComparison.Ordinal),
            "Parameters panel: the restore lists the product with the problem and shows the proposed name under the current one");
        panel.Find(".restore-dialog textarea").Input("Lentila revine");
        panel.Find(".restore-dialog .editor-actions button.primary").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".restore-dialog").Count == 0 && panel.FindAll("button[aria-label='Arhivează parametrul Lentila']").Count == 1), TimeSpan.FromSeconds(10));
        check(parameters.LastRestoreModels.ContainsKey(2) && !parameters.LastRestoreModels.ContainsKey(1) && panel.FindAll("#section-archive tbody tr").Count == 0,
            "Parameters panel: the restore sends the model chosen for the product that needed a decision and the parameter returns to the live list");

        // A manual rename replaces the proposal.
        panel.Find("button[aria-label='Arhivează parametrul Lentila']").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".archive-dialog").Count == 1));
        panel.Find(".archive-dialog textarea").Input("Din nou in arhiva");
        panel.Find(".archive-dialog .editor-actions button.primary").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll("#section-archive tbody tr").Count == 1), TimeSpan.FromSeconds(10));
        panel.Find("#section-archive button.restore").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".restore-dialog").Count == 1));
        panel.Find(".restore-table tr.restore-problem input[role=switch]").Change(true);
        panel.WaitForAssertion(() => Expect(panel.FindAll(".restore-table tr.restore-problem input[aria-label^='Modelul produsului']").Count == 1));
        panel.Find(".restore-table tr.restore-problem input[aria-label^='Modelul produsului']").Input("CAM-2 nou");
        check(panel.Find(".restore-table tr.restore-problem .restore-new").TextContent.Contains("CAM-2 nou", StringComparison.Ordinal),
            "Parameters panel: a model typed by hand shows the name it gives the product");
        panel.Find(".restore-table tr.restore-problem input[aria-label^='Modelul produsului']").Input("");
        check(panel.Find(".restore-dialog .editor-actions button.primary").HasAttribute("disabled"), "Parameters panel: the restore cannot be applied while a typed model is empty");
        panel.Find(".restore-dialog .editor-actions button.act-close").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".restore-dialog").Count == 0));

        // After the retention period the restore can be given up; the parameter is then unused and can be deleted for good.
        parameters.SeedArchivedAt(MariaTimeText.Format(DateTime.UtcNow.AddDays(-40)));
        var later = context.Render<SubcategoryParametersPanel>(builder => builder.Add(item => item.Group, group));
        later.WaitForAssertion(() => Expect(later.FindAll("#section-archive tbody tr").Count == 1), TimeSpan.FromSeconds(10));
        check(!later.Find("#section-archive button.giveup").HasAttribute("disabled"), "Parameters panel: giving up the restore is available after the retention period");
        later.Find("#section-archive button.giveup").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".delete-confirmation-dialog").Count == 1));
        ConfirmDeletion(later);
        later.WaitForAssertion(() => Expect(later.FindAll("#section-archive tbody tr").Count == 0), TimeSpan.FromSeconds(10));
        later.Find("#archive-tab-unused").Click();
        later.WaitForAssertion(() => Expect(later.FindAll("#section-archive tbody tr").Count == 1));
        check(later.Find("#section-archive tbody tr").TextContent.Contains("0 produse", StringComparison.Ordinal) && later.FindAll("#section-archive button.row-icon.delete").Count == 1,
            "Parameters panel: after giving up the restore the parameter is in the unused tab, where it can be deleted for good");
        later.Find("#section-archive button.row-icon.delete").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".delete-confirmation-dialog").Count == 1));
        ConfirmDeletion(later);
        later.WaitForAssertion(() => Expect(later.FindAll("#section-archive tbody tr").Count == 0), TimeSpan.FromSeconds(10));
        check((await parameters.GetArchivedAsync()).Parameters.Count == 0, "Parameters panel: an unused archived parameter is deleted for good");

        // An unused live parameter: archiving offers a choice, and deletion goes through the deletion dialog.
        later.Find("button[aria-label='Arhivează parametrul Rezerva']").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".archive-dialog").Count == 1));
        check(later.FindAll(".archive-unused").Count == 1 && later.FindAll(".archive-dialog .editor-actions button.danger").Any(button => button.TextContent.Trim() == "Șterge definitiv"),
            "Parameters panel: an unused parameter offers to archive it or to delete it for good");
        later.FindAll(".archive-dialog .editor-actions button.danger").First(button => button.TextContent.Trim() == "Șterge definitiv").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".archive-dialog").Count == 0 && later.FindAll(".delete-confirmation-dialog").Count == 1));
        ConfirmDeletion(later);
        later.WaitForAssertion(() => Expect(later.FindAll("button[aria-label='Arhivează parametrul Rezerva']").Count == 0), TimeSpan.FromSeconds(10));
        check((await parameters.GetParametersAsync()).All(item => item.Id != spare.Id), "Parameters panel: deleting for good from the archive dialog removes the unused parameter");

        // A tag some product has: archived, listed in the archive, restored.
        later.Find("button[aria-label='Arhivează eticheta PoE']").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".archive-dialog").Count == 1));
        check(later.Find(".archive-dialog").TextContent.Contains("1 produse au eticheta activă", StringComparison.Ordinal), "Parameters panel: the tag dialog says how many products have the tag");
        later.Find(".archive-dialog textarea").Input("Eticheta nu mai e folosita");
        later.Find(".archive-dialog .editor-actions button.primary").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".archive-dialog").Count == 0 && later.FindAll("button[aria-label='Arhivează eticheta PoE']").Count == 0), TimeSpan.FromSeconds(10));
        later.Find("#archive-tab-used").Click();
        later.WaitForAssertion(() => Expect(later.FindAll("#section-archive tbody tr").Count == 1));
        check(later.Find("#section-archive tbody tr").TextContent.Contains("Etichetă", StringComparison.Ordinal) && later.FindAll("#section-archive button.history").Count == 0,
            "Parameters panel: an archived tag is listed with its products and has no usage history");
        later.Find("#section-archive button.restore").Click();
        later.WaitForAssertion(() => Expect(later.FindAll(".restore-dialog").Count == 1));
        later.Find(".restore-dialog textarea").Input("Eticheta revine");
        later.Find(".restore-dialog .editor-actions button.primary").Click();
        later.WaitForAssertion(() => Expect(later.FindAll("button[aria-label='Arhivează eticheta PoE']").Count == 1), TimeSpan.FromSeconds(10));
        check((await tags.GetProductTagIdsAsync(1)).Count == 1, "Parameters panel: the restored tag is back on the product that had it");

        // The panel of a category (no subcategory): the same sections, a note that the subcategories inherit, and the definitions are added to the category.
        var categoryGroup = new ProductGroup("Supraveghere", "");
        var categoryPanel = context.Render<SubcategoryParametersPanel>(builder => builder.Add(item => item.Group, categoryGroup));
        categoryPanel.WaitForAssertion(() => Expect(categoryPanel.FindAll("details.def-section").Count == 4), TimeSpan.FromSeconds(10));
        check(categoryPanel.Find(".subcategory-parameters h2").TextContent.Contains("Supraveghere", StringComparison.Ordinal) && categoryPanel.FindAll(".category-inheritance-note").Count == 1
              && categoryPanel.FindAll("button.row-icon.archive").Count == 0,
            "Parameters panel: the panel of a category names the category, says the subcategories inherit and starts without definitions");
        categoryPanel.Find("#section-parameters .def-toolbar button").Click();
        categoryPanel.Find("#new-parameter-name").Input("Garantie");
        categoryPanel.Find("#section-parameters .def-form button.primary").Click();
        categoryPanel.WaitForAssertion(() => Expect(categoryPanel.FindAll("button[aria-label='Arhivează parametrul Garantie']").Count == 1), TimeSpan.FromSeconds(10));
        check((await parameters.GetParametersAsync()).Any(item => item.Name == "Garantie" && item.Subcategory == "" && item.Category == "Supraveghere")
              && panel.FindAll("button[aria-label='Arhivează parametrul Garantie']").Count == 0,
            "Parameters panel: a parameter added in the panel of a category belongs to the category, not to a subcategory");
        await Task.CompletedTask;
    }

    // The deletion dialog: choose the default reason, continue, type the confirmation word, confirm.
    private static void ConfirmDeletion(IRenderedComponent<SubcategoryParametersPanel> panel)
    {
        panel.Find(".delete-confirmation-dialog input[type=radio]").Change(DeleteConfirmationRules.DefaultChoice);
        panel.Find(".delete-confirmation-dialog .editor-actions button.primary").Click();
        panel.WaitForAssertion(() => Expect(panel.FindAll(".delete-confirmation-input input").Count == 1));
        panel.Find(".delete-confirmation-input input").Input(DeleteConfirmationRules.ConfirmationWord);
        panel.Find(".delete-confirmation-dialog .editor-actions button.primary").Click();
    }
}

