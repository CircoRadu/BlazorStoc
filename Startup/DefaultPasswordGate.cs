using BlazorStoc.Services;

namespace BlazorStoc.Startup;

/// <summary>
/// While an account of the database still has the password the installation gave it (it is public), the administrator is taken to Setari -> Baza de date -> Conexiune and the other
/// pages stay closed to the administrator until the passwords are changed. Only an answer from the server counts (a database that does not answer never locks anyone out), only
/// administrators are held (they are the ones who can change the passwords), and the pages of the sign-in, the help, the static files and the settings stay open.
/// </summary>
public static class DefaultPasswordGate
{
    public const string TargetPath = "/setari";
    public const string TargetUrl = "/setari?tab=baza-de-date&subtab=conexiune";

    public static bool IsAllowedPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.TrimEnd('/').ToLowerInvariant();
        if (normalized.Length == 0) return false;
        return normalized == TargetPath || normalized.StartsWith("/account/", StringComparison.Ordinal) || normalized.StartsWith("/ajutor", StringComparison.Ordinal) ||
               normalized.StartsWith("/_blazor", StringComparison.Ordinal) || normalized.StartsWith("/_framework", StringComparison.Ordinal) || normalized.StartsWith("/api/session/", StringComparison.Ordinal) ||
               normalized.StartsWith("/health", StringComparison.Ordinal) || Path.HasExtension(normalized);
    }

    public static IApplicationBuilder UseDefaultPasswordGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var isPageRequest = HttpMethods.IsGet(context.Request.Method) && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
        if (isPageRequest && context.User.Identity?.IsAuthenticated == true && context.User.IsInRole(AccessRoles.Administrator) && !IsAllowedPath(context.Request.Path.Value))
        {
            var accounts = context.RequestServices.GetRequiredService<IDatabaseAccountService>();
            bool any;
            try { any = await accounts.AnyDefaultPasswordAsync(context.RequestAborted); }
            catch (Exception exception) when (exception is not OperationCanceledException) { any = false; }
            if (any) { context.Response.Redirect(TargetUrl); return; }
        }
        await next();
    });
}
