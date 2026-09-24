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
    AppMode mode, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public bool IsDemo => mode.IsDemo;
    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect("/");
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
        if (string.Equals(Username, configuredUsername, StringComparison.Ordinal)) return InvalidLogin();
        try
        {
            var result = await authenticator.AuthenticateAsync(Username, Password ?? "", HttpContext.RequestAborted);
            if (result.Status == AuthenticationStatus.Inactive) return InvalidLogin("Contul este inactiv. Contactează administratorul pentru reactivare.");
            if (result.Status == AuthenticationStatus.Success && result.User is not null)
                return await SignInAsync(result.User.Username, result.User.DisplayName, result.User.Role, result.User.Id);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Database user authentication failed ({ErrorType}).", exception.GetType().Name);
        }
        return InvalidLogin();
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
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        await AuditRecorder.RecordSessionAsync(auditTrail, username, role, true, HttpContext.RequestAborted);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    private IActionResult InvalidLogin(string message = "Utilizator sau parolă incorectă.")
    {
        Error = message;
        return Page();
    }
}
