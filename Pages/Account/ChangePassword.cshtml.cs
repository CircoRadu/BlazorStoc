using BlazorStoc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorStoc.Pages.Account;

/// <summary>The first sign-in after an administrator created the account or set its password (and every account that existed when the rule was introduced): the user has to choose a new password before the session is made.
/// The page is reached only with the ticket of the sign-in page (password and code were right); without it the visitor goes back to the sign-in.</summary>
[AllowAnonymous]
public class ChangePasswordModel(IOwnAccountService account, IAuditTrail auditTrail, IDataProtectionProvider protection, ILogger<ChangePasswordModel> logger) : PageModel
{
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Confirmation { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }
    public string Username { get; private set; } = "";

    public IActionResult OnGet()
    {
        var ticket = PasswordChangeTicket.Read(HttpContext, protection);
        if (ticket is null) return LocalRedirect("/Account/Login?reason=parola");
        Username = ticket.Username;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ticket = PasswordChangeTicket.Read(HttpContext, protection);
        if (ticket is null) return LocalRedirect("/Account/Login?reason=parola");
        Username = ticket.Username;
        OwnPasswordResult result;
        try { result = await account.ChangeForcedAsync(ticket.UserId, NewPassword, Confirmation, HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("The first password change failed ({ErrorType}).", exception.GetType().Name);
            Error = "Operația a eșuat. Încearcă din nou.";
            return Page();
        }
        if (!result.Success) { Error = result.Message; return Page(); }
        PasswordChangeTicket.Clear(HttpContext);
        await AccountSession.SignInAsync(HttpContext, auditTrail, ticket.Username, ticket.DisplayName, ticket.Role, ticket.UserId);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    public IActionResult OnPostCancel()
    {
        PasswordChangeTicket.Clear(HttpContext);
        return LocalRedirect("/Account/Login");
    }
}
