using BlazorStoc.Services;

namespace BlazorStoc.Startup;

public sealed record SessionPing(string? CircuitId);

/// <summary>
/// The page of a session reports the address and the browser it connects from (the circuit itself does not know them): one small request per session, made by the tracker in the
/// layout. The request is accepted only for the user it belongs to (OpenSessionRegistry.SetNetwork) and only with the header of the application, so a form on another site cannot send it.
/// </summary>
public static class SessionEndpoints
{
    public const string HeaderName = "X-BlazorStoc-Session";

    public static void MapSessionPing(this IEndpointRouteBuilder app)
    {
        // Where the browser of a closed session is sent: signed out, then to the sign-in page with the reason. Valid only with the token given when the administrator closed the session.
        app.MapGet("/Account/SessionClosed", async (HttpContext context, string? t, OpenSessionRegistry registry) =>
        {
            if (!registry.ConsumeCloseToken(t)) return Results.LocalRedirect("/");
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context);
            return Results.LocalRedirect("/Account/Login?reason=inchisa");
        }).AllowAnonymous();
        app.MapPost("/api/session/ping", (HttpContext context, SessionPing body, OpenSessionRegistry registry) =>
        {
            if (!context.Request.Headers.ContainsKey(HeaderName) || string.IsNullOrEmpty(body.CircuitId)) return Results.BadRequest();
            var address = context.Connection.RemoteIpAddress;
            if (address is { IsIPv4MappedToIPv6: true }) address = address.MapToIPv4();
            var accepted = registry.SetNetwork(body.CircuitId, address?.ToString(), UserAgentText.Summarize(context.Request.Headers.UserAgent.ToString()), context.User.Identity?.Name);
            return accepted ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization();
    }
}
