using System.Security.Claims;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace BlazorStoc.Pages.Account;

/// <summary>The sign-in cookie of a user: one place for the sign-in page and for the page where the user chooses the new password at the first sign-in. No "stay signed in": the cookie ends with the browser (and after 8 hours).</summary>
public static class AccountSession
{
    public static async Task SignInAsync(HttpContext http, IAuditTrail audit, string username, string displayName, string role, int? userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, username), new(ClaimTypes.GivenName, displayName), new(ClaimTypes.Role, role) };
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties());
        await AuditRecorder.RecordSessionAsync(audit, username, role, true, http.RequestAborted, AuditRecorder.FormatAddress(http.Connection.RemoteIpAddress));
    }
}

/// <summary>Proof that the password (and the code, when it is asked) of an account were right and that the account has to choose a new password before the session is made: a protected cookie that lives 10 minutes.</summary>
public sealed record PasswordChangeTicket(int UserId, string Username, string DisplayName, string Role)
{
    public const string CookieName = "BlazorStoc.PwdChange";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("BlazorStoc.Login.PasswordChange").ToTimeLimitedDataProtector();

    public void Issue(HttpContext http, IDataProtectionProvider provider) =>
        http.Response.Cookies.Append(CookieName, Protector(provider).Protect(System.Text.Json.JsonSerializer.Serialize(this), Lifetime),
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = http.Request.IsHttps, MaxAge = Lifetime, Path = "/" });

    public static PasswordChangeTicket? Read(HttpContext http, IDataProtectionProvider provider)
    {
        try { return http.Request.Cookies[CookieName] is { Length: > 0 } token ? System.Text.Json.JsonSerializer.Deserialize<PasswordChangeTicket>(Protector(provider).Unprotect(token)) : null; }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) { return null; }
    }

    public static void Clear(HttpContext http) => http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
}
