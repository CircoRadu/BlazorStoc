using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BlazorStoc.Services;
namespace BlazorStoc.Pages.Account;
public class LogoutModel(IAuditTrail auditTrail) : PageModel
{
    // Signing out is confirmed in the popup of the main layout; the old confirmation page is no longer shown.
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        var username = User.Identity?.Name ?? "necunoscut";
        var role = User.IsInRole(AccessRoles.Administrator) ? AccessRoles.Administrator : AccessRoles.LimitedUser;
        await HttpContext.SignOutAsync();
        await AuditRecorder.RecordSessionAsync(auditTrail, username, role, false, HttpContext.RequestAborted);
        return LocalRedirect("/Account/Login");
    }
}
