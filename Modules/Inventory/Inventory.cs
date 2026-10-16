using System.Globalization;

namespace BlazorStoc.Services;

public sealed record InventorySelectionItem(string Category, string Subcategory);
public sealed record InventoryLine(string Code, int Quantity)
{
    public bool IsNegative => Quantity < 0;
}
public sealed record InventorySubcategorySection(string Subcategory, IReadOnlyList<InventoryLine> Lines);
public sealed record InventoryCategorySection(string Category, IReadOnlyList<InventorySubcategorySection> Subcategories);

public sealed record InventoryReport(DateTime GeneratedAtUtc, IReadOnlyList<InventoryCategorySection> Categories,
    int SelectedCategoryCount, int SelectedSubcategoryCount, int ProductCount, int NegativeCount);

public sealed record InventoryRequest(IReadOnlyList<InventorySelectionItem> Selection, bool ExcludeZeroStock);

public sealed class InventoryOperationException(string message) : Exception(message);

public static class InventoryRules
{
    public const string NoSelectionMessage = "Selectează cel puțin o categorie sau subcategorie.";
    public const string InvalidSelectionMessage =
        "Selecția nu mai corespunde structurii curente a catalogului. Actualizează pagina și reia selecția.";
    public const string NoProductsMessage = "Nu există produse de inventariat pentru selecția făcută.";

    public static string FileName(DateTime generatedLocal) =>
        $"Inventar_{generatedLocal:yyyy-MM-dd}_{generatedLocal:HHmm}.pdf";

    // Deduplicated selection whose every pair still exists in the current catalogue; throws otherwise so a stale
    // selection (structure changed between page load and the click) is reported instead of generating a partial file.
    public static IReadOnlyList<InventorySelectionItem> Validated(IReadOnlyList<InventorySelectionItem>? items,
        IReadOnlyList<ProductGroup> groups)
    {
        if (items is null || items.Count == 0) throw new InventoryOperationException(NoSelectionMessage);
        var known = groups.Where(group => group.Subcategory.Length > 0)
            .Select(group => Key(group.Category, group.Subcategory)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<InventorySelectionItem>();
        foreach (var item in items)
        {
            var key = Key(item.Category, item.Subcategory);
            if (!known.Contains(key)) throw new InventoryOperationException(InvalidSelectionMessage);
            if (seen.Add(key)) result.Add(item);
        }
        return result;
    }

    internal static string Key(string category, string subcategory) =>
        $"{TextNormalization.UniquenessKey(category)}\u001f{TextNormalization.UniquenessKey(subcategory)}";
}

public interface IInventoryReportBuilder
{
    Task<InventoryReport> BuildAsync(InventoryRequest request, CancellationToken cancellationToken = default);
}

// Reads a coherent snapshot of the catalogue and builds the sections and lines shown in the PDF; the warehouse
// value excludes what is held by vehicles (Task 2's "Ieșire spre vehicul", confirmed by the user for this report).
public sealed class InventoryReportBuilder(IProductRepository products, IStockMovementRepository movements) : IInventoryReportBuilder
{
    public async Task<InventoryReport> BuildAsync(InventoryRequest request, CancellationToken cancellationToken = default)
    {
        var groupsTask = products.GetGroupsAsync(cancellationToken);
        var productsTask = products.GetProductsAsync(cancellationToken);
        var inVehiclesTask = movements.GetQuantitiesInVehiclesAsync(cancellationToken);
        await Task.WhenAll(groupsTask, productsTask, inVehiclesTask).ConfigureAwait(false);
        var groups = await groupsTask;
        var allProducts = await productsTask;
        var inVehicles = await inVehiclesTask;

        var selection = InventoryRules.Validated(request.Selection, groups);
        var canonical = groups.Where(group => group.Subcategory.Length > 0)
            .GroupBy(group => InventoryRules.Key(group.Category, group.Subcategory))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var chosen = selection.Select(item => canonical[InventoryRules.Key(item.Category, item.Subcategory)]).ToList();

        var productsBySubcategory = allProducts.ToLookup(product => InventoryRules.Key(product.Category, product.Subcategory));
        var sections = new List<InventoryCategorySection>();
        var totalProducts = 0;
        var totalNegative = 0;

        foreach (var categoryGroup in chosen
            .GroupBy(group => group.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var subcategorySections = new List<InventorySubcategorySection>();
            foreach (var group in categoryGroup.OrderBy(group => group.Subcategory, StringComparer.CurrentCultureIgnoreCase))
            {
                var lines = productsBySubcategory[InventoryRules.Key(group.Category, group.Subcategory)]
                    .Select(product => new InventoryLine(product.Name,
                        StockMovementRules.WarehouseStock(product.Quantity, inVehicles.GetValueOrDefault(product.Id))))
                    .Where(line => !request.ExcludeZeroStock || line.Quantity != 0)
                    // Products with negative stock come first: they are the ones to count and regularize (see the "De regularizat" tab).
                    .OrderBy(line => line.IsNegative ? 0 : 1).ThenBy(line => line.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (lines.Count == 0) continue;
                subcategorySections.Add(new InventorySubcategorySection(group.Subcategory, lines));
                totalProducts += lines.Count;
                totalNegative += lines.Count(line => line.IsNegative);
            }
            if (subcategorySections.Count == 0) continue;
            sections.Add(new InventoryCategorySection(categoryGroup.Key, subcategorySections));
        }

        if (totalProducts == 0) throw new InventoryOperationException(InventoryRules.NoProductsMessage);

        var selectedCategoryCount = chosen.Select(group => group.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new InventoryReport(DateTime.UtcNow, sections, selectedCategoryCount, chosen.Count, totalProducts, totalNegative);
    }
}
