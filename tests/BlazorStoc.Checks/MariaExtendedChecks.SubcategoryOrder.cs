using Microsoft.Extensions.Configuration;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

public static partial class MariaExtendedChecks
{
    // The order of the subcategories of a category: arranged by the administrator, kept by the repository, new ones at the end, journaled.
    private static async Task SubcategoryOrderAsync(IConfiguration configuration, IAccessControl admin, IAuditTrail audit)
    {
        var suffix = Suffix();
        var category = $"Ext Ordine Cat {suffix}";
        var products = new MariaProductRepository(configuration, admin, audit);
        await products.CreateCategoryAsync(category);
        foreach (var name in new[] { "Ccc", "Aaa", "Bbb" }) await products.CreateSubcategoryAsync(category, $"Ext {name} {suffix}");
        var since = DateTime.UtcNow;
        await Task.Delay(30);
        async Task<List<string>> Order() => (await products.GetGroupsAsync()).Where(group => group.Category == category).Select(group => group.Subcategory).ToList();

        // New subcategories keep the order they were created in (never arranged ones would be alphabetical only when all have the same position).
        var created = await Order();
        Check(created.SequenceEqual([$"Ext Ccc {suffix}", $"Ext Aaa {suffix}", $"Ext Bbb {suffix}"]), "Subcategories added one after the other keep that order");

        var wanted = new List<string> { $"Ext Bbb {suffix}", $"Ext Ccc {suffix}", $"Ext Aaa {suffix}" };
        await products.ReorderSubcategoriesAsync(category, wanted);
        Check((await Order()).SequenceEqual(wanted), "The administrator arranges the subcategories and the repository returns the arranged order");
        await products.CreateSubcategoryAsync(category, $"Ext Ddd {suffix}");
        Check((await Order()).Last() == $"Ext Ddd {suffix}", "A subcategory created after the arrangement goes at the end");

        await Rejects<ProductOperationException>(() => products.ReorderSubcategoriesAsync(category, wanted), "An incomplete list of subcategories (one was added meanwhile) is refused");
        var fullOrder = await Order();
        await Rejects<AccessDeniedException>(() => new MariaProductRepository(configuration, new TestAccessControl(false, "simplu"), audit).ReorderSubcategoriesAsync(category, fullOrder),
            "A user who is not the administrator cannot arrange the subcategories");
        Check((await audit.GetEventsAsync()).Count(item => item.TimestampUtc > since && item.Action == AuditActions.ReorderSubcategories) == 1,
            "The arrangement is journaled as \"Reordonare subcategorii\", once (an unchanged order is not an event)");
    }
}
