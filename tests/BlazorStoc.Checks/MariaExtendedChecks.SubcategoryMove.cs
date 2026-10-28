using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // Moving and arranging the nodes of the tree (drag and drop): a node takes everything under it along, to any branch or category; conflicts are refused.
    // The categories are named "Ext Arbore Cat ..." so that the tree section removes them at its next run.
    private static async Task SubcategoryMoveAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var since = DateTime.UtcNow.AddSeconds(-1);
        var products = new MariaProductRepository(configuration, admin, audit);
        var parameters = new MariaProductParameterRepository(configuration, admin);
        var tags = new MariaProductTagRepository(configuration, admin);
        var catA = $"Ext Arbore Cat M {suffix}";
        var catB = $"Ext Arbore Cat N {suffix}";
        await products.CreateCategoryAsync(catA);
        await products.CreateCategoryAsync(catB);
        async Task<ProductGroup> Root(string category, string name) => await products.CreateSubcategoryAsync(category, $"Ext Mut {name} {suffix}");
        async Task<List<SubcategoryNode>> Tree(string category) => (await products.GetTreeAsync()).Where(item => item.Category == category).ToList();
        List<string> Names(IEnumerable<SubcategoryNode> nodes) => nodes.Select(item => item.Name.Replace($" {suffix}", "")).ToList();

        var a1 = await Root(catA, "a1");
        var a2 = await Root(catA, "a2");
        var a3 = await Root(catA, "a3");
        var c1 = await products.CreateChildAsync(a1, $"Ext Mut c1 {suffix}", false);
        var g1 = await products.CreateChildAsync(c1, $"Ext Mut g1 {suffix}", false);

        // A new place among the same siblings.
        var reordered = await products.MoveAsync(a3, null, catA, 0);
        Check(reordered.OnlyReordered && Names((await Tree(catA)).Where(item => item.Depth == 0)).SequenceEqual(["Ext Mut a3", "Ext Mut a1", "Ext Mut a2"]), "A root is arranged to another place among its siblings");

        // A node takes its whole structure along to another branch.
        var moved = await products.MoveAsync(c1, a2, catA, 0);
        var afterBranch = await Tree(catA);
        Check(moved.NodesMoved == 2 && !moved.OnlyReordered && afterBranch.Single(item => item.Name == c1.Subcategory).Parent == a2.Subcategory
              && afterBranch.Single(item => item.Name == g1.Subcategory) is { Parent: { } grandParent, Depth: 2 } && grandParent == c1.Subcategory, "A node moved to another branch takes everything under it along");

        // ... and to another category (the structure and its products change category together).
        await products.MoveAsync(a2, null, catB, int.MaxValue);
        var inB = await Tree(catB);
        Check(Names(inB).SequenceEqual(["Ext Mut a2", "Ext Mut c1", "Ext Mut g1"]) && (await Tree(catA)).All(item => item.Name != a2.Subcategory && item.Name != c1.Subcategory),
            "A node moved to another category takes its branch along");

        // Conflicts.
        var a2InB = new ProductGroup(catB, a2.Subcategory);
        var g1InB = new ProductGroup(catB, g1.Subcategory);
        await Rejects<ProductOperationException>(() => products.MoveAsync(a2InB, g1InB, catB, 0), "A node cannot be moved into its own subtree");
        await Rejects<ProductOperationException>(() => products.MoveAsync(a2InB, a2InB, catB, 0), "A node cannot be moved under itself");
        await Rejects<AccessDeniedException>(() => new MariaProductRepository(configuration, new TestAccessControl(false, "simplu"), audit).MoveAsync(a1, null, catB, 0), "Only the administrator moves nodes");

        var b1 = await Root(catB, "b1");
        var onB1 = await products.CreateAsync(new ProductInput { Name = $"MUT-{suffix}", Category = catB, Subcategory = b1.Subcategory });
        await Rejects<ProductOperationException>(() => products.MoveAsync(a1, b1, catB, 0), "A subcategory with products does not receive children by a move");
        Check(onB1.Subcategory == b1.Subcategory, "The product stays on its leaf");

        var b2 = await Root(catB, "b2");
        await parameters.AddParameterAsync(b2, "Culoare mutare", "", ParameterKind.Text);
        var a4 = await Root(catA, "a4");
        await parameters.AddParameterAsync(a4, "culoare MUTARE", "", ParameterKind.Text);
        await Rejects<ProductOperationException>(() => products.MoveAsync(a4, b2, catB, 0), "A parameter of the moved branch that repeats one of the new ancestors is refused");
        var a5 = await Root(catA, "a5");
        await tags.AddTagAsync(a5, "Eticheta mutare");
        await tags.AddTagAsync(b2, "eticheta MUTARE");
        await Rejects<ProductOperationException>(() => products.MoveAsync(a5, b2, catB, 0), "A tag of the moved branch that repeats one of the new ancestors is refused");

        // Products that would lose the values they inherit from the parent keep the branch where it is.
        var a6 = await Root(catA, "a6");
        var size = await parameters.AddParameterAsync(a6, "Marime mutare", "", ParameterKind.Text);
        var big = await parameters.AddValueAsync(size.Id, "mare");
        var c6 = await products.CreateChildAsync(a6, $"Ext Mut c6 {suffix}", false);
        await products.CreateAsync(new ProductInput { Name = $"MUT6-{suffix}", Category = catA, Subcategory = c6.Subcategory, BaseModel = $"MUT6-{suffix}", Parameters = [new ProductParameterChoice(size.Id, big.Id)] });
        await Rejects<ProductOperationException>(() => products.MoveAsync(c6, null, catB, 0), "A branch whose products have values inherited from the parent is not detached from that parent");
        Check((await Tree(catA)).Single(item => item.Name == c6.Subcategory).Parent == a6.Subcategory, "A refused move changes nothing");

        // A branch without such links moves freely (a2's branch has none).
        await products.MoveAsync(new ProductGroup(catB, c1.Subcategory), b2, catB, 0);
        Check((await Tree(catB)).Single(item => item.Name == c1.Subcategory).Parent == b2.Subcategory, "An empty branch moves under another parent");

        var events = await audit.GetEventsAsync();
        Check(events.Count(item => item.TimestampUtc > since && item.Action == AuditActions.MoveSubcategory) == 3
              && events.Count(item => item.TimestampUtc > since && item.Action == AuditActions.ReorderSubcategories) >= 1,
            "Moves between branches and categories are journaled as \"Mutare subcategorie\", a new place among siblings as the reorder");
    }
}
