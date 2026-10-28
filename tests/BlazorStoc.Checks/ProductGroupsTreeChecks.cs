using BlazorStoc.Components.Pages;
using BlazorStoc.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorStoc.Checks;

// The categories page with a tree of subcategories: children under their parent, a handle and an add-child icon on every row, drops sent to the repository.
public static class ProductGroupsTreeChecks
{
    private sealed class RecordingTree(IReadOnlyList<SubcategoryNode> nodes) : ISubcategoryTree
    {
        public List<(ProductGroup Node, ProductGroup? Parent, string Category, int Position)> Moves { get; } = [];
        public Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default) => Task.FromResult(nodes);
        public Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default) => Task.FromResult(new ProductGroup(parent.Category, name));
        public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default)
        {
            Moves.Add((node, newParent, targetCategory, position));
            return Task.FromResult(new SubcategoryMoveResult(2, 0, false));
        }
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var access = new TestAccessControl(true, "groups.tree");
        var repository = new DemoProductRepository(access);
        var groups = (await repository.GetGroupsAsync()).Where(group => group.Subcategory.Length > 0).ToList();
        var category = groups[0].Category;
        var names = groups.Where(group => group.Category == category).Select(group => group.Subcategory).ToList();
        if (names.Count < 2) { check(false, "Categories tree: the demo catalog has a category with two subcategories"); return; }
        // The second subcategory becomes a child of the first one.
        var nodes = new List<SubcategoryNode> { new(1, category, names[0], null, 0, false, 0), new(2, category, names[1], names[0], 1, true, 0) };
        nodes.AddRange(names.Skip(2).Select((name, index) => new SubcategoryNode(3 + index, category, name, null, 0, true, 0)));
        var tree = new RecordingTree(nodes);

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddSingleton<IAccessControl>(access);
        context.Services.AddSingleton<IProductRepository>(repository);
        context.Services.AddSingleton<IProductParameterRepository>(new DemoProductParameterRepository());
        context.Services.AddSingleton<IProductTagRepository>(new DemoProductTagRepository());
        context.Services.AddSingleton<ISubcategoryTree>(tree);
        context.Services.AddSingleton<ICatalogSettings, DemoCatalogSettings>();
        context.Services.AddScoped<UnsavedChanges>();

        var page = context.Render<ProductGroups>();
        page.WaitForAssertion(() => { if (page.FindAll("tr[data-node-name]").Count < nodes.Count) throw new InvalidOperationException("pending"); }, TimeSpan.FromSeconds(10));
        var rows = page.FindAll("tr[data-node-name]");
        var child = rows.Single(row => row.GetAttribute("data-node-name") == names[1]);
        var parent = rows.Single(row => row.GetAttribute("data-node-name") == names[0]);
        check(rows.Count == nodes.Count && rows.All(row => row.QuerySelector("[data-subcategory-handle]") is not null),
            "Categories tree: every node, intermediate or leaf, has a row with a drag handle");
        check((child.QuerySelector("td:not(.drag-column)")?.GetAttribute("style") ?? "").Contains("padding-left") && child.GetAttribute("data-subcategory-index") is null
              && parent.GetAttribute("data-subcategory-index") is not null,
            "Categories tree: a child is shown indented under its parent and only the first-level rows keep the first-level order index");
        check(rows.All(row => row.QuerySelector("button.row-icon.addchild") is not null),
            "Categories tree: every row has the add-child icon");
        check(parent.QuerySelector("button.row-icon.delete") is null,
            "Categories tree: a subcategory that has children has no delete icon");

        // A drop: before a row (same parent), inside a row (as its child), on a category title (last on the first level).
        await page.InvokeAsync(() => page.Instance.DropSubcategory(category, names[1], category, names[0], "before"));
        await page.InvokeAsync(() => page.Instance.DropSubcategory(category, names[0], category, names[1], "inside"));
        await page.InvokeAsync(() => page.Instance.DropSubcategory(category, names[1], category, "", "end"));
        check(tree.Moves.Count == 3
              && tree.Moves[0] is { Parent: null, Position: 0 } && tree.Moves[0].Node.Subcategory == names[1]
              && tree.Moves[1].Parent?.Subcategory == names[1] && tree.Moves[1].Node.Subcategory == names[0]
              && tree.Moves[2] is { Parent: null, Position: int.MaxValue } && tree.Moves[2].Category == category,
            "Categories tree: a drop before a row, inside a row and on a category title asks the repository for the right parent and place");
    }
}
