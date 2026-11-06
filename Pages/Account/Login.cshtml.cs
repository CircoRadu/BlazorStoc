using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlazorStoc.Pages.Account;
[AllowAnonymous]
public class LoginModel(IConfiguration configuration, IUserAuthenticator authenticator, IAuditTrail auditTrail,
    ILogger<LoginModel> logger, ITwoFactorStore twoFactor, IDataProtectionProvider protection) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    // The code of the authenticator application (or a recovery code): asked on this same page, together with the user and the password, when the settings require it for this visitor
    // (Setari -> Autentificare). All three are checked together and a refusal never says which of them was wrong.
    [BindProperty] public string Code { get; set; } = "";
    public bool CodeRequired { get; private set; }
    // There is no "stay signed in": the session cookie ends with the browser (and after 8 hours), and every sign-out means a full sign-in again.
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Reason { get; set; }

    // Wrong attempts per typed user name (whether or not the name exists, so a lock tells nothing about the accounts): 5 wrong attempts lock the name for 10 minutes, doubling at every lock.
    private static readonly FailureThrottle<string> Attempts = new();

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect("/");
        CodeRequired = AccessSecurityRules.IsCodeRequired(configuration, HttpContext.Connection.RemoteIpAddress);
        if (Reason == "inchisa") Error = LoginMessages.SessionClosed;
        if (Reason == "parola") Error = LoginMessages.PasswordChangeExpired;
        return Page();
    }

    private enum PasswordStatus { Ok, Wrong, Inactive, DatabaseDown, Fault }
    private sealed record PasswordOutcome(PasswordStatus Status, string? Username = null, string? DisplayName = null, string? Role = null, int? UserId = null, string Reason = "utilizator sau parolă incorecte", bool MustChange = false);

    // The first factor: the administrator of the configuration, then the accounts of the database. A wrong password for the configured administrator is rejected without querying SQL.
    private async Task<PasswordOutcome> CheckPasswordAsync()
    {
        var expected = configuration["Authentication:Password"];
        var configuredUsername = configuration["Authentication:Username"] ?? "admin";
        if (!string.IsNullOrEmpty(expected) && string.Equals(Username, configuredUsername, StringComparison.Ordinal) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return new(PasswordStatus.Ok, configuredUsername, "Administrator configurat", AccessRoles.Administrator, null);
        if (string.Equals(Username, configuredUsername, StringComparison.Ordinal)) return new(PasswordStatus.Wrong, Reason: "parolă greșită");
        try
        {
            var result = await authenticator.AuthenticateAsync(Username, Password ?? "", HttpContext.RequestAborted);
            if (result.Status == AuthenticationStatus.Inactive) return new(PasswordStatus.Inactive, Reason: "cont inactiv");
            if (result.Status == AuthenticationStatus.Success && result.User is not null)
                return new(PasswordStatus.Ok, result.User.Username, result.User.DisplayName, result.User.Role, result.User.Id, MustChange: result.User.MustChangePassword);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Database user authentication failed ({ErrorType}).", exception.GetType().Name);
            // A database that does not answer (or a backup/restore that holds it) is not a wrong password: say so, so nobody keeps retyping credentials.
            return new(LoginMessages.IsDatabaseUnavailable(exception) ? PasswordStatus.DatabaseDown : PasswordStatus.Fault);
        }
        return new(PasswordStatus.Wrong);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        CodeRequired = AccessSecurityRules.IsCodeRequired(configuration, address);
        var key = (Username ?? "").Trim().ToUpperInvariant();
        if (CodeRequired && Attempts.LockedUntil(key) is { } until)
        {
            await JournalRefusalAsync("autentificare blocată după prea multe încercări");
            return InvalidLogin(LoginMessages.TooManyAttempts((int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes)));
        }
        var outcome = await CheckPasswordAsync();
        if (outcome.Status == PasswordStatus.DatabaseDown) return InvalidLogin(LoginMessages.DatabaseUnavailable);
        if (outcome.Status == PasswordStatus.Fault) return InvalidLogin();   // an unexpected fault is not a refused sign-in: nothing to journal

        if (!CodeRequired)
        {
            if (outcome.Status == PasswordStatus.Inactive) return await RefusedAsync(outcome.Reason, "Contul este inactiv. Contactează administratorul pentru reactivare.");
            if (outcome.Status != PasswordStatus.Ok) return await RefusedAsync(outcome.Reason);
            return await FinishAsync(outcome);
        }

        // Password and code are always both checked, so the time of the answer tells nothing; the code of an account is only spent when the password was right.
        string? failure;
        if (outcome.Status == PasswordStatus.Ok)
        {
            TwoFactorCheck check;
            try { check = await twoFactor.VerifyAsync(outcome.UserId ?? 0, Code ?? "", AuditRecorder.FormatAddress(address), HttpContext.RequestAborted); }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError("Authenticator code could not be checked ({ErrorType}).", exception.GetType().Name);
                return InvalidLogin(LoginMessages.IsDatabaseUnavailable(exception) ? LoginMessages.DatabaseUnavailable : LoginMessages.WrongCredentialsOrCode);
            }
            failure = check switch
            {
                TwoFactorCheck.Valid or TwoFactorCheck.ValidRecoveryCode => null,
                TwoFactorCheck.NotEnrolled => "fără autentificator înrolat",
                TwoFactorCheck.Suspended => "autentificator suspendat",
                TwoFactorCheck.LockedOut => "autentificatorul este blocat după prea multe coduri greșite",
                _ => "cod de autentificator greșit"
            };
            if (check == TwoFactorCheck.ValidRecoveryCode) logger.LogInformation("A recovery code was used to sign in.");
        }
        else
        {
            for (var i = 0; i < 3; i++) Totp.Compute(new byte[Totp.SecretBytes], i);   // the work of a code check, so a wrong password costs the same time
            failure = outcome.Reason;
        }
        if (failure is null)
        {
            Attempts.Clear(key);
            return await FinishAsync(outcome);
        }
        Attempts.Fail(key);
        return await RefusedAsync(failure, LoginMessages.WrongCredentialsOrCode);
    }

    // A refused sign-in is journaled (user name typed, reason, address; never the password); a journal that cannot be written must not change what the user sees.
    private async Task<IActionResult> RefusedAsync(string reason, string message = LoginMessages.WrongCredentials)
    {
        await JournalRefusalAsync(reason);
        return InvalidLogin(message);
    }

    private async Task JournalRefusalAsync(string reason)
    {
        try
        {
            await AuditRecorder.RecordLoginFailedAsync(auditTrail, Username, reason, HttpContext.RequestAborted,
                AuditRecorder.FormatAddress(HttpContext.Connection.RemoteIpAddress));
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning("Failed sign-in could not be journaled ({ErrorType}).", exception.GetType().Name); }
    }

    // Everything was right: the session is made, unless the account has to choose a new password first (created by an administrator, or its password set by one): then only a ticket is
    // given and the user is sent to the page where the new password is chosen; the session is made there.
    private async Task<IActionResult> FinishAsync(PasswordOutcome user)
    {
        if (user.MustChange && user.UserId is not null)
        {
            new PasswordChangeTicket(user.UserId.Value, user.Username!, user.DisplayName!, user.Role!).Issue(HttpContext, protection);
            return LocalRedirect("/Account/ChangePassword" + (Url.IsLocalUrl(ReturnUrl) ? "?ReturnUrl=" + Uri.EscapeDataString(ReturnUrl!) : ""));
        }
        await AccountSession.SignInAsync(HttpContext, auditTrail, user.Username!, user.DisplayName!, user.Role!, user.UserId);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    private IActionResult InvalidLogin(string message = LoginMessages.WrongCredentials)
    {
        Error = message;
        return Page();
    }
}
