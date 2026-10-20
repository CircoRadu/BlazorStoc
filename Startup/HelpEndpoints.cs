using BlazorStoc.Help;

namespace BlazorStoc.Startup;

/// <summary>
/// The help window (wwwroot/help.js) asks for the HTML of a topic: by the address and tabs of the window it was opened from, or by key (a dialog,
/// a link between topics), or for a search. Signed-in users read everything; without signing in only the topics marked "Public: da" (Login, AccessDenied).
/// </summary>
public static class HelpEndpoints
{
    public static void MapHelp(this IEndpointRouteBuilder app)
    {
        var catalog = HelpCatalog.Default;

        app.MapGet("/ajutor/fereastra", (HttpContext context, string? ruta, string[]? tab) =>
        {
            var (topic, others) = catalog.Resolve(ruta ?? "/", tab);
            if (topic is null) return Fragment(HelpRenderer.Missing(ruta ?? "/"));
            if (!Allowed(context, topic)) return Results.StatusCode(StatusCodes.Status401Unauthorized);
            return Fragment(HelpRenderer.Topic(topic, catalog, others.Where(other => Allowed(context, other)).ToList()));
        }).AllowAnonymous();

        app.MapGet("/ajutor/subiect/{key}", (HttpContext context, string key) =>
        {
            var topic = catalog.Get(key);
            if (topic is null) return Fragment(HelpRenderer.Missing(key));
            if (!Allowed(context, topic)) return Results.StatusCode(StatusCodes.Status401Unauthorized);
            return Fragment(HelpRenderer.Topic(topic, catalog));
        }).AllowAnonymous();

        // The search is for the signed-in users only (the pages outside the application have one topic each).
        app.MapGet("/ajutor/cautare", (string? q) => Fragment(HelpRenderer.Results(catalog.Search(q ?? ""), (q ?? "").Trim())))
            .RequireAuthorization();

        app.MapGet("/ajutor/imagine/{name}", (HttpContext context, string name) =>
        {
            // The pictures of the public topics (Login, AccessDenied: <key>.<capture>.jpg) are readable without signing in too.
            var publicPicture = catalog.Topics.Values.Any(topic => topic.Public && name.StartsWith(topic.Key + ".", StringComparison.OrdinalIgnoreCase));
            if (context.User.Identity?.IsAuthenticated != true && !publicPicture) return Results.StatusCode(StatusCodes.Status401Unauthorized);
            var bytes = catalog.Image(name);
            context.Response.Headers.CacheControl = "private, max-age=300";
            return bytes is null ? Results.NotFound() : Results.File(bytes, ContentType(name));
        }).AllowAnonymous();
    }

    private static bool Allowed(HttpContext context, HelpTopic topic) => topic.Public || context.User.Identity?.IsAuthenticated == true;

    private static IResult Fragment(string html) => Results.Content(html, "text/html; charset=utf-8");

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };
}
