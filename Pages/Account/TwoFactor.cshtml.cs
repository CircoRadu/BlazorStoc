using System.Security.Claims;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorStoc.Pages.Account;

/// <summary>"Autentificatorul meu": the signed-in user sees the state of the authenticator the administrator associated with the account. Associating, revoking and the new recovery codes are done by an administrator.</summary>
public class TwoFactorModel(IConfiguration configuration, ITwoFactorStore store, ILogger<TwoFactorModel> logger) : PageModel
{
    public TwoFactorStatus Status { get; private set; } = new(false, false);
    public TwoFactorMode Mode { get; private set; }
    public string? Error { get; private set; }

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public async Task<IActionResult> OnGetAsync()
    {
        Mode = AccessSecurityRules.Read(configuration).Mode;
        try { Status = await store.GetStatusAsync(UserId, HttpContext.RequestAborted); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("Authenticator state could not be read ({ErrorType}).", exception.GetType().Name);
            Error = "Starea autentificatorului nu a putut fi citită.";
        }
        return Page();
    }

    public string Text => Status switch
    {
        { Active: true } => "Ai un autentificator activ. Când autentificarea cere cod, îl scrii pe pagina de autentificare, după utilizator și parolă. Dacă schimbi sau pierzi telefonul, anunță un administrator: el revocă autentificatorul și ți-l asociază din nou.",
        { Suspended: true } => "Autentificatorul tău este suspendat (contul a fost dezactivat). Un administrator îl reactivează sau ți-l asociază din nou.",
        { Exists: true } => "Asocierea autentificatorului a început dar nu a fost confirmată. Un administrator o termină cu tine, cu telefonul în mână.",
        _ => "Nu ai încă un autentificator. Un administrator ți-l asociază: scanezi codul QR cu o aplicație de autentificare (Google Authenticator, Microsoft Authenticator, Aegis) și îi spui primul cod, ca să confirme."
    };
}
