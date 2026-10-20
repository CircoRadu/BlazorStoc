using System.Threading.RateLimiting;

namespace BlazorStoc.Startup;

// The limit of the "login" policy (attached to the Razor pages: Account/Login, AccessDenied, Logout). Only the submission of the login form can guess a password, so
// only that is counted: 10 submissions per minute for each address. Opening the login page, being sent to "Acces refuzat" or signing out use none of the
// permits (before, every one of those did, so a few page views and one typing mistake were enough to be refused).
internal static class LoginRateLimit
{
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static bool IsLoginSubmission(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments("/Account/Login", StringComparison.OrdinalIgnoreCase);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        IsLoginSubmission(context.Request)
            ? RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit, Window = Window, QueueLimit = 0 })
            : RateLimitPartition.GetNoLimiter("not-a-login-submission");
}
