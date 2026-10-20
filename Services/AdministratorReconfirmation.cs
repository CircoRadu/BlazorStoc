namespace BlazorStoc.Services;

/// <summary>
/// The password the administrator types again before an operation on the database or its accounts. The administrator configured in the private file confirms with the
/// password of that file (no SQL); an administrator from the database with the password of the own account, checked by the same authenticator as the sign-in (the
/// database in force still answers at this point). After five wrong passwords in a row the confirmation is refused for a minute.
/// </summary>
public sealed class AdministratorReconfirmation(IConfiguration configuration, IAccessControl access, IUserAuthenticator? authenticator, ILogger logger)
{
    public async Task ConfirmAsync(string? typed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(typed)) throw new DatabaseSettingsException(DatabaseConnectionRules.AdminPasswordRequiredMessage);
        var user = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) ?? "";
        if (AdminPasswordGate.IsBlocked(user)) throw new DatabaseSettingsException(DatabaseConnectionRules.TooManyAttemptsMessage);
        var configuredUser = configuration["Authentication:Username"] ?? "admin";
        var confirmed = string.Equals(user, configuredUser, StringComparison.Ordinal)
            ? DatabaseConnectionRules.PasswordMatches(configuration, typed)
            : await PasswordOfDatabaseAccountMatchesAsync(user, typed, cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            AdminPasswordGate.Failed(user);
            throw new DatabaseSettingsException(DatabaseConnectionRules.AdminPasswordMessage);
        }
        AdminPasswordGate.Succeeded(user);
    }

    private async Task<bool> PasswordOfDatabaseAccountMatchesAsync(string user, string typed, CancellationToken cancellationToken)
    {
        if (authenticator is null || user.Length == 0) return false;
        try
        {
            var result = await authenticator.AuthenticateAsync(user, typed, cancellationToken).ConfigureAwait(false);
            return result.Status == AuthenticationStatus.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("The password of the database account could not be checked ({ErrorType}).", exception.GetType().Name);
            return false;
        }
    }
}
