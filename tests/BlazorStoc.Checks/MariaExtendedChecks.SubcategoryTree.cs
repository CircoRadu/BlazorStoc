using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The tree of subcategories (migration 54): children on several levels, inherited parameters and tags, products on leaves only, a leaf turning into a parent.
    private static async Task SubcategoryTreeAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit, MySqlConnection probe)
    {
        // The trees of earlier runs are removed first: the depth checks below depend on the depth the whole tree has.
        await RemoveTreeLeftoversAsync(probe);
        var suffix = Suffix();
        var since = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin);   // not journaled here: other sections count these events
        var tags = new MariaProductTagRepository(configuration, admin);
        var category = $"Ext Arbore Cat {suffix}";
        var rootName = $"Ext Arbore Radacina {suffix}";
        var childName = $"Ext Arbore Copil {suffix}";
        var grandName = $"Ext Arbore Nepot {suffix}";
        await products.CreateCategoryAsync(category);
        var root = await products.CreateSubcategoryAsync(category, rootName);
        var colour = await parameters.AddParameterAsync(root, "Culoare arbore", "", ParameterKind.Text);
        var red = await parameters.AddValueAsync(colour.Id, "rosu");
        await tags.AddTagAsync(root, "Eticheta radacina");

        // A leaf with a product turns into a parent only when its products move into the new child.
        var first = await products.CreateAsync(new ProductInput { Name = $"ARB-{suffix}", Category = category, Subcategory = rootName,
            BaseModel = $"ARB-{suffix}", Parameters = [new ProductParameterChoice(colour.Id, red.Id)] });
        await Rejects<ProductOperationException>(() => products.CreateChildAsync(root, childName, false), "A leaf with products does not get a child without moving its products");
        var child = await products.CreateChildAsync(root, childName, true);
        var moved = (await products.GetProductsAsync()).Single(item => item.Id == first.Id);
        Check(moved.Subcategory == childName && moved.Category == category, "The products of the former leaf move into the new child");
        var state = await parameters.GetProductStateAsync(first.Id);
        Check(state.Choices.Count == 1 && state.Choices[0].ValueId == red.Id, "The parameter values of the moved product are kept (inherited from the parent)");

        // Products go on leaves only.
        await Rejects<ProductOperationException>(() => products.CreateAsync(new ProductInput { Name = $"ARB2-{suffix}", Category = category, Subcategory = rootName,
            BaseModel = $"ARB2-{suffix}", Parameters = [new ProductParameterChoice(colour.Id, red.Id)] }), "A product cannot be attached to a subcategory that has children");

        // The child inherits the required parameter of the parent: a product without the value is refused, with it is accepted.
        await Rejects<ProductOperationException>(() => products.CreateAsync(new ProductInput { Name = $"ARB3-{suffix}", Category = category, Subcategory = childName }),
            "A product of a child needs the required parameters inherited from its parent");
        var second = await products.CreateAsync(new ProductInput { Name = $"ARB3-{suffix}", Category = category, Subcategory = childName,
            BaseModel = $"ARB3-{suffix}", Parameters = [new ProductParameterChoice(colour.Id, red.Id)] });
        Check(second.Name.Contains("rosu", StringComparison.OrdinalIgnoreCase), "The code of a product of the child contains the value of the inherited parameter");

        // The child adds its own parameter and a deeper level; names cannot repeat an inherited parameter or tag.
        await Rejects<ProductOperationException>(() => parameters.AddParameterAsync(child, " culoare arbore ", "", ParameterKind.Text), "A child cannot redefine a parameter inherited from its parent");
        await Rejects<ProductOperationException>(() => tags.AddTagAsync(child, "ETICHETA radacina"), "A child cannot redefine a tag inherited from its parent");
        var size = await parameters.AddParameterAsync(child, "Marime arbore", "", ParameterKind.Text);
        var big = await parameters.AddValueAsync(size.Id, "mare");
        var grand = await products.CreateChildAsync(child, grandName, true);
        var third = await products.CreateAsync(new ProductInput { Name = $"ARB4-{suffix}", Category = category, Subcategory = grandName,
            BaseModel = $"ARB4-{suffix}", Parameters = [new ProductParameterChoice(colour.Id, red.Id), new ProductParameterChoice(size.Id, big.Id)] });
        Check(third.Name.Contains("rosu", StringComparison.OrdinalIgnoreCase) && third.Name.Contains("mare", StringComparison.OrdinalIgnoreCase), "A grandchild inherits the parameters of every ancestor and the code follows the order root, child, own");
        var thirdTags = await tags.GetTagsAsync();
        var rootTag = thirdTags.Single(item => item.Category == category && item.Subcategory == rootName);
        await tags.SetProductTagsAsync(third.Id, [rootTag.Id]);
        Check((await tags.GetProductTagIdsAsync(third.Id)).SequenceEqual([rootTag.Id]), "A product of a grandchild can have On a tag defined at the root");

        // The tree, in order, with depths and leaves.
        var tree = (await products.GetTreeAsync()).Where(item => item.Category == category).ToList();
        Check(tree.Select(item => (item.Name, item.Depth, item.IsLeaf)).SequenceEqual([(rootName, 0, false), (childName, 1, false), (grandName, 2, true)]), "The tree lists parent, child and grandchild with their depth and leaf flag");
        Check(tree[1].Parent == rootName && tree[0].Parent is null, "Each node knows its parent");

        // Guards.
        await Rejects<ProductOperationException>(() => products.DeleteSubcategoryAsync(root, "Ext stergere"), "A subcategory with children is not deleted");
        await Rejects<ProductOperationException>(() => products.CreateChildAsync(root, $"ext arbore COPIL {suffix}", true), "A child name already used in the tree is refused");
        await Rejects<ProductOperationException>(() => products.UpdateSubcategoryAsync(grand, grandName, category + " alta", "Ext mutare"), "A child cannot change its category alone");

        var events = await audit.GetEventsAsync();
        Check(events.Count(item => item.TimestampUtc > since && item.Action == AuditActions.AddChildSubcategory) == 2
              && events.Count(item => item.TimestampUtc > since && item.Action == AuditActions.MoveProductsToChildSubcategory) == 2,
            "Adding a child and moving the products are journaled as their own actions");

        // The maximum depth comes from the settings (1 to 15, 10 by default), only the administrator changes it, never below the depth the tree already has.
        var settings = new MariaCatalogSettings(configuration, admin, audit);
        Check(await settings.GetMaxSubcategoryDepthAsync() == CatalogSettingsRules.DefaultSubcategoryDepth, "The maximum depth is 10 until the administrator changes it");
        await Rejects<ProductOperationException>(() => settings.SaveMaxSubcategoryDepthAsync(CatalogSettingsRules.MaxSubcategoryDepth + 1), "A maximum depth above 15 is refused");
        await Rejects<ProductOperationException>(() => settings.SaveMaxSubcategoryDepthAsync(0), "A maximum depth below 1 is refused");
        await Rejects<ProductOperationException>(() => settings.SaveMaxSubcategoryDepthAsync(2), "The maximum depth is not lowered below the depth the tree already has (3 levels here)");
        await Rejects<AccessDeniedException>(() => new MariaCatalogSettings(configuration, new TestAccessControl(false, "simplu"), audit).SaveMaxSubcategoryDepthAsync(5), "Only the administrator sets the maximum depth");
        await settings.SaveMaxSubcategoryDepthAsync(3);
        await Rejects<ProductOperationException>(() => products.CreateChildAsync(grand, $"Ext Arbore Strănepot {suffix}", true), "A child below the maximum depth set in the settings is refused");
        await settings.SaveMaxSubcategoryDepthAsync(CatalogSettingsRules.MaxSubcategoryDepth);
        var deeper = await products.CreateChildAsync(grand, $"Ext Arbore Stranepot {suffix}", true);
        Check((await products.GetTreeAsync()).Single(item => item.Name == deeper.Subcategory).Depth == 3, "With a higher maximum the tree goes one level deeper");
        await settings.SaveMaxSubcategoryDepthAsync(CatalogSettingsRules.DefaultSubcategoryDepth);
        Check((await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.Action == AuditActions.SetMaxSubcategoryDepth) == 3, "Each change of the maximum depth is journaled as its own action");
    }

    private static async Task RemoveTreeLeftoversAsync(MySqlConnection probe)
    {
        const string categories = "(SELECT id FROM categories WHERE name LIKE 'Ext Arbore Cat %')";
        foreach (var sql in new[] { $"DELETE FROM products WHERE category_id IN {categories}" })
        {
            await using var command = new MySqlCommand(sql, probe);
            await command.ExecuteNonQueryAsync();
        }
        // Deepest first: a subcategory that is nobody's parent goes, until none is left.
        int removed;
        do
        {
            await using var command = new MySqlCommand($"DELETE s FROM subcategories s WHERE s.category_id IN {categories} AND NOT EXISTS(SELECT 1 FROM (SELECT parent_id FROM subcategories WHERE parent_id IS NOT NULL) p WHERE p.parent_id=s.id)", probe);
            removed = await command.ExecuteNonQueryAsync();
        } while (removed > 0);
        await using var delete = new MySqlCommand("DELETE FROM categories WHERE name LIKE 'Ext Arbore Cat %'", probe);
        await delete.ExecuteNonQueryAsync();
    }
}
