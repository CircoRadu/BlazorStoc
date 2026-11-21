namespace BlazorStoc.Components.Shared;

/// <summary>What a column header (SortTh) needs from the table it orders; implemented by Paged.</summary>
public interface IPagedSort
{
    bool CanSort(string key);

    /// <summary>"ascending", "descending" or null (the table is not ordered by this column).</summary>
    string? DirectionOf(string key);

    Task ToggleAsync(string key);
}
