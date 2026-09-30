namespace BlazorStoc.Services;

// Which requests are moved to the waiting page ("/intretinere") while a backup/restore holds the operation lock.
public static class MaintenanceSupport
{
    public const string WaitingPath = "/intretinere";

    // Only browser page navigations (GET asking for HTML) are redirected. Everything the running page itself needs -
    // the live circuit, framework and static files, the maintenance status endpoint, sign-in/out - passes untouched.
    private static readonly string[] PassThroughPrefixes =
    [
        WaitingPath, "/api/maintenance", "/Account", "/_blazor", "/_framework", "/_content", "/hubs", "/health", "/media"
    ];

    public static bool ShouldRedirect(HttpRequest request, OperationLockInfo? active)
    {
        if (active is null || !HttpMethods.IsGet(request.Method)) return false;
        var path = request.Path.Value ?? "/";
        if (Path.HasExtension(path)) return false; // scripts, styles, images, icons
        foreach (var prefix in PassThroughPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return request.Headers.Accept.Any(value => value is not null && value.Contains("text/html", StringComparison.OrdinalIgnoreCase));
    }

    public static string WaitingUrl(HttpRequest request) =>
        $"{WaitingPath}?returnUrl={Uri.EscapeDataString(request.Path + request.QueryString)}";

    // The page to come back to: a local path only (never another site), and never the waiting page itself.
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.StartsWith("/\\", StringComparison.Ordinal) && !returnUrl.StartsWith(WaitingPath, StringComparison.OrdinalIgnoreCase)
            ? returnUrl
            : "/";
}
