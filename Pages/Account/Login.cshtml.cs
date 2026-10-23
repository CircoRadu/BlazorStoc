using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorStoc.Pages.Account;
[AllowAnonymous]
public class LoginModel(IConfiguration configuration, IUserAuthenticator authenticator, IAuditTrail auditTrail,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    // "Păstrează-mă autentificat": the cookie outlives the browser and lasts RememberDays; without it the session cookie ends with the browser (and after 8 hours).
    [BindProperty] public bool RememberMe { get; set; }
    public const int RememberDays = 30;
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Reason { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect("/");
        if (Reason == "inchisa") Error = LoginMessages.SessionClosed;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var expected = configuration["Authentication:Password"];
        var configuredUsername = configuration["Authentication:Username"] ?? "admin";
        if (!string.IsNullOrEmpty(expected) && string.Equals(Username, configuredUsername, StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
        {
            return await SignInAsync(configuredUsername, "Administrator configurat", AccessRoles.Administrator, null);
        }
        // A wrong password for the configured administrator is rejected without querying SQL.
        if (string.Equals(Username, configuredUsername, StringComparison.Ordinal)) return await RefusedAsync("parolă greșită");
        try
        {
            var result = await authenticator.AuthenticateAsync(Username, Password ?? "", HttpContext.RequestAborted);
            if (result.Status == AuthenticationStatus.Inactive) return await RefusedAsync("cont inactiv", "Contul este inactiv. Contactează administratorul pentru reactivare.");
            if (result.Status == AuthenticationStatus.Success && result.User is not null)
                return await SignInAsync(result.User.Username, result.User.DisplayName, result.User.Role, result.User.Id);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Database user authentication failed ({ErrorType}).", exception.GetType().Name);
            // A database that does not answer (or a backup/restore that holds it) is not a wrong password: say so, so nobody keeps retyping credentials.
            if (LoginMessages.IsDatabaseUnavailable(exception)) return InvalidLogin(LoginMessages.DatabaseUnavailable);
            return InvalidLogin();   // an unexpected fault is not a refused sign-in: nothing to journal
        }
        return await RefusedAsync("utilizator sau parolă incorecte");
    }

    // A refused sign-in is journaled (user name typed, reason, address; never the password); a journal that cannot be written must not change what the user sees.
    private async Task<IActionResult> RefusedAsync(string reason, string message = LoginMessages.WrongCredentials)
    {
        try
        {
            await AuditRecorder.RecordLoginFailedAsync(auditTrail, Username, reason, HttpContext.RequestAborted,
                AuditRecorder.FormatAddress(HttpContext.Connection.RemoteIpAddress));
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning("Failed sign-in could not be journaled ({ErrorType}).", exception.GetType().Name); }
        return InvalidLogin(message);
    }

    private async Task<IActionResult> SignInAsync(string username, string displayName, string role, int? userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.GivenName, displayName),
            new(ClaimTypes.Role, role)
        };
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties();
        if (RememberMe)
        {
            properties.IsPersistent = true;
            properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(RememberDays);
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
        await AuditRecorder.RecordSessionAsync(auditTrail, username, role, true, HttpContext.RequestAborted,
            AuditRecorder.FormatAddress(HttpContext.Connection.RemoteIpAddress), RememberMe ? RememberDays : null);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    private IActionResult InvalidLogin(string message = LoginMessages.WrongCredentials)
    {
        Error = message;
        return Page();
    }
}
