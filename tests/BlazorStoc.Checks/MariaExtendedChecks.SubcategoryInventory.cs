using Microsoft.Extensions.Configuration;
using BlazorStoc.Modules.Inventory;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The inventory report on the tree of subcategories: a selected node with children stands for every leaf under it, each leaf titled with its path.
    // The category is named "Ext Arbore Cat ..." so that the tree section removes it at its next run.
    private static async Task SubcategoryInventoryAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var products = new MariaProductRepository(configuration, admin, audit);
        var movements = new MariaStockMovementRepository(configuration, admin, audit);
        var category = $"Ext Arbore Cat I {suffix}";
        await products.CreateCategoryAsync(category);
        var root = await products.CreateSubcategoryAsync(category, $"Ext Inv radacina {suffix}");
        var first = await products.CreateChildAsync(root, $"Ext Inv frunza1 {suffix}", false);
        var second = await products.CreateChildAsync(root, $"Ext Inv frunza2 {suffix}", false);
        await products.CreateAsync(new ProductInput { Name = $"INV1-{suffix}", Category = category, Subcategory = first.Subcategory });
        await products.CreateAsync(new ProductInput { Name = $"INV2-{suffix}", Category = category, Subcategory = second.Subcategory });

        var builder = new InventoryReportBuilder(products, movements, products);
        var report = await builder.BuildAsync(new InventoryRequest([new InventorySelectionItem(category, root.Subcategory)], false));
        var sections = report.Categories.Single().Subcategories;
        Check(sections.Count == 2 && sections.Select(item => item.Lines.Single().Code).OrderBy(code => code).SequenceEqual([$"INV1-{suffix}", $"INV2-{suffix}"]),
            "Inventory: selecting a subcategory with children reports every leaf under it");
        Check(sections[0].Subcategory == $"{root.Subcategory} › {first.Subcategory}" && sections[1].Subcategory == $"{root.Subcategory} › {second.Subcategory}",
            "Inventory: each leaf is titled with its path, in the order of the tree");
        var onlyOne = await builder.BuildAsync(new InventoryRequest([new InventorySelectionItem(category, second.Subcategory)], false));
        Check(onlyOne.Categories.Single().Subcategories.Single().Lines.Single().Code == $"INV2-{suffix}", "Inventory: a selected leaf reports just its products");
    }
}
