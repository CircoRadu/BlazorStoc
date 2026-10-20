namespace BlazorStoc.Services;

// Selection logic for the "Inventar" page (category ↔ subcategories ↔ "select all"), independent of the UI so it can
// be unit tested. A category without subcategories has nothing to select and is always reported unchecked.
public sealed class InventorySelectionState
{
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);

    public bool ExcludeZeroStock { get; set; }

    public bool IsSelected(string category, string subcategory) => selected.Contains(Key(category, subcategory));

    public void SetSubcategory(string category, string subcategory, bool value)
    {
        var key = Key(category, subcategory);
        if (value) selected.Add(key); else selected.Remove(key);
    }

    public void SetCategory(string category, IReadOnlyList<string> subcategories, bool value)
    {
        foreach (var subcategory in subcategories) SetSubcategory(category, subcategory, value);
    }

    public (bool Checked, bool Indeterminate) CategoryState(string category, IReadOnlyList<string> subcategories)
    {
        if (subcategories.Count == 0) return (false, false);
        var count = subcategories.Count(subcategory => IsSelected(category, subcategory));
        return count == 0 ? (false, false) : count == subcategories.Count ? (true, false) : (false, true);
    }

    public void SetAll(IReadOnlyList<(string Category, IReadOnlyList<string> Subcategories)> categories, bool value)
    {
        foreach (var (category, subcategories) in categories) SetCategory(category, subcategories, value);
    }

    public (bool Checked, bool Indeterminate) AllState(IReadOnlyList<(string Category, IReadOnlyList<string> Subcategories)> categories)
    {
        var total = categories.Sum(entry => entry.Subcategories.Count);
        if (total == 0) return (false, false);
        var count = categories.Sum(entry => entry.Subcategories.Count(subcategory => IsSelected(entry.Category, subcategory)));
        return count == 0 ? (false, false) : count == total ? (true, false) : (false, true);
    }

    public int Count(IReadOnlyList<(string Category, IReadOnlyList<string> Subcategories)> categories) =>
        categories.Sum(entry => entry.Subcategories.Count(subcategory => IsSelected(entry.Category, subcategory)));

    public IReadOnlyList<InventorySelectionItem> ToItems(IReadOnlyList<(string Category, IReadOnlyList<string> Subcategories)> categories) =>
        categories.SelectMany(entry => entry.Subcategories
                .Where(subcategory => IsSelected(entry.Category, subcategory))
                .Select(subcategory => new InventorySelectionItem(entry.Category, subcategory)))
            .ToList();

    private static string Key(string category, string subcategory) => InventoryRules.Key(category, subcategory);
}
