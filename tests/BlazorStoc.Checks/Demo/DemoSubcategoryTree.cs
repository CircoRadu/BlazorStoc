using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The component checks run on flat catalogs: every subcategory of the repository is a root and a leaf.
sealed class DemoSubcategoryTree(IProductRepository products) : ISubcategoryTree
{
    public async Task<IReadOnlyList<SubcategoryNode>> GetTreeAsync(CancellationToken cancellationToken = default) =>
        (await products.GetGroupsAsync(cancellationToken)).Where(group => group.Subcategory.Length > 0)
            .Select((group, index) => new SubcategoryNode(index + 1, group.Category, group.Subcategory, null, 0, true, 0)).ToList();

    public Task<ProductGroup> CreateChildAsync(ProductGroup parent, string name, bool moveProducts, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The demo catalog has no tree.");

    public Task<SubcategoryMoveResult> MoveAsync(ProductGroup node, ProductGroup? newParent, string targetCategory, int position, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The demo catalog has no tree.");
}

sealed class DemoCatalogSettings : ICatalogSettings
{
    private int depth = CatalogSettingsRules.DefaultSubcategoryDepth;
    public Task<int> GetMaxSubcategoryDepthAsync(CancellationToken cancellationToken = default) => Task.FromResult(depth);
    public Task SaveMaxSubcategoryDepthAsync(int value, CancellationToken cancellationToken = default) { depth = value; return Task.CompletedTask; }
}
