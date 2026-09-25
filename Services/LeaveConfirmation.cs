using Microsoft.AspNetCore.WebUtilities;

namespace BlazorStoc.Services;

// Task 6: while the "Adaugă produs" form is open, choosing another category, subcategory or "Toate produsele"
// in the products menu must be confirmed first. This decides whether a menu target really changes the selection.
public static class ProductMenuSelection
{
    public static bool IsSameSelection(string currentUri, string targetUri)
    {
        var current = Parse(currentUri);
        var target = Parse(targetUri);
        return current is not null && target is not null && current == target;
    }

    // Only the products page with the same category and subcategory (case-insensitive) counts as "the same".
    private static (string Path, string Category, string Subcategory)? Parse(string value)
    {
        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri)) return null;
        var text = uri.IsAbsoluteUri ? uri.PathAndQuery : value;
        var separator = text.IndexOf('?');
        var path = (separator < 0 ? text : text[..separator]).TrimEnd('/').ToLowerInvariant();
        var query = QueryHelpers.ParseQuery(separator < 0 ? string.Empty : text[separator..]);
        static string Read(IDictionary<string, Microsoft.Extensions.Primitives.StringValues> values, string key) =>
            values.TryGetValue(key, out var found) ? found.ToString().Trim().ToLowerInvariant() : string.Empty;
        return (path, Read(query, "categorie"), Read(query, "subcategorie"));
    }
}
