using BlazorStoc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorStoc.Pages.Account;

// Where the cookie handler sends a signed-in user whose type lacks the permission of a page or of an endpoint; the refusal is journaled.
public class AccessDeniedModel(IAuditTrail auditTrail, IAccessControl access) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public async Task OnGetAsync()
    {
        var path = ReturnUrl is { Length: > 0 } && ReturnUrl.StartsWith('/') ? ReturnUrl.Split('?')[0] : "necunoscută";
        await AuditRecorder.RecordActionAsync(auditTrail, access, AuditEntities.User, AuditActions.AccessDenied, string.Empty, $"Pagina {path}",
            AuditDetails.Identification(("Adresă", path)), string.Empty, HttpContext.RequestAborted);
    }
}
