namespace BlazorStoc.Services;

// Edit and delete of a product are opened from the product page as /produse?edit=<id>&inapoi=<page>. The
// "inapoi" address tells the catalog page where to send the user when the action is closed, cancelled or saved.
public static class ReturnNavigation
{
    public const string QueryName = "inapoi";

    // Only same-site paths are accepted (never another host, a protocol-relative "//host" or a backslash form),
    // so the parameter cannot be used as an open redirect.
    public static string? Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!text.StartsWith('/') || text.StartsWith("//") || text.Contains('\\') || text.Any(char.IsControl)) return null;
        return Uri.TryCreate(text, UriKind.Relative, out _) ? text : null;
    }

    public static string EditUrl(int productId, string returnTo) => Build("edit", productId, returnTo);
    public static string DeleteUrl(int productId, string returnTo) => Build("sterge", productId, returnTo);

    private static string Build(string action, int productId, string returnTo) =>
        Safe(returnTo) is { } safe ? $"/produse?{action}={productId}&{QueryName}={Uri.EscapeDataString(safe)}" : $"/produse?{action}={productId}";
}
