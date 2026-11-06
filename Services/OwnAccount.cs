using Microsoft.AspNetCore.Identity;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

public sealed record OwnPasswordResult(bool Success, string Message);

public interface IOwnAccountService
{
    /// <summary>The signed-in user changes the password of their own account: the current password is checked first, the new one follows the same rules as in Utilizatori.</summary>
    Task<OwnPasswordResult> ChangePasswordAsync(string currentPassword, string newPassword, string confirmation, CancellationToken cancellationToken = default);
    /// <summary>The first sign-in after an administrator set the password of the account (or created it): the user chooses a new password. The caller proves the sign-in with the ticket of the login page; the new password must follow the policy and differ from the one the administrator set.</summary>
    Task<OwnPasswordResult> ChangeForcedAsync(int userId, string newPassword, string confirmation, CancellationToken cancellationToken = default);
    /// <summary>False for the administrator of the configuration: that password is written in the settings file, not in the database.</summary>
    Task<bool> CanChangePasswordAsync(CancellationToken cancellationToken = default);
}

/// <summary>The menu of the signed-in user (top of every page): their own password. It needs no right from the permission matrix, only the sign-in; it only ever touches the row of the signed-in user.</summary>
public sealed class OwnAccountService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, ILogger<OwnAccountService> logger) : IOwnAccountService
{
    public const string WrongCurrentMessage = "Parola actuală nu este corectă.";
    public static string TooManyAttempts(int minutes) => $"Prea multe încercări. Schimbarea parolei este blocată încă {Math.Max(1, minutes)} min.";
    private static readonly object Subject = new();
    private static readonly PasswordHasher<object> Hasher = new();
    // Wrong current passwords per user: a stolen open session must not be a way to guess the password of the account (5 wrong attempts lock 10 minutes, doubling).
    private static readonly FailureThrottle<string> Attempts = new();

    private bool IsConfiguredAdministrator(string username) =>
        string.Equals(username, configuration["Authentication:Username"] ?? "admin", StringComparison.Ordinal);

    public async Task<bool> CanChangePasswordAsync(CancellationToken cancellationToken = default) =>
        await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false) is { Length: > 0 } name && !IsConfiguredAdministrator(name);

    public async Task<OwnPasswordResult> ChangePasswordAsync(string currentPassword, string newPassword, string confirmation, CancellationToken cancellationToken = default)
    {
        var username = await access.GetUsernameAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(username)) return new(false, "Nu ești autentificat.");
        if (IsConfiguredAdministrator(username))
            return new(false, "Parola administratorului din configurare nu se schimbă de aici: este scrisă în fișierul de configurare al aplicației.");
        var key = username.ToUpperInvariant();
        if (Attempts.LockedUntil(key) is { } until) return new(false, TooManyAttempts((int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes)));
        currentPassword ??= ""; newPassword ??= ""; confirmation ??= "";
        if (currentPassword.Length == 0) return new(false, "Scrie parola actuală.");
        if (PasswordPolicy.Validate(newPassword) is { } policyProblem) return new(false, policyProblem);
        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal)) return new(false, "Parola nouă și confirmarea ei nu coincid.");
        if (string.Equals(newPassword, currentPassword, StringComparison.Ordinal)) return new(false, "Parola nouă trebuie să fie diferită de cea actuală.");

        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        int id; string hash; long version; bool active;
        await using (var read = Command(connection, "SELECT id,password_hash,version,is_active FROM web_users WHERE normalized_username=@name LIMIT 1", ("@name", TextNormalization.UniquenessKey(username))))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(false, "Contul nu mai există.");
            id = reader.GetInt32(0); hash = reader.GetString(1); version = reader.GetInt64(2); active = reader.GetBoolean(3);
        }
        if (!active) return new(false, "Contul este inactiv.");
        if (Hasher.VerifyHashedPassword(Subject, hash, currentPassword) == PasswordVerificationResult.Failed)
        {
            Attempts.Fail(key);
            await TryRecordAsync(AuditActions.ChangeOwnPasswordFailed, id, username, "Schimbarea parolei proprii a fost refuzată: parola actuală scrisă este greșită.", cancellationToken).ConfigureAwait(false);
            return new(false, WrongCurrentMessage);
        }
        await using (var update = Command(connection, "UPDATE web_users SET password_hash=@hash,version=version+1,must_change_password=0 WHERE id=@id AND version=@version",
            ("@hash", Hasher.HashPassword(Subject, newPassword)), ("@id", id), ("@version", version)))
        {
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return new(false, "Contul s-a schimbat între timp. Încearcă din nou.");
        }
        Attempts.Clear(key);
        await TryRecordAsync(AuditActions.ChangeOwnPassword, id, username, "Utilizatorul și-a schimbat parola (parola nu se înregistrează).", cancellationToken).ConfigureAwait(false);
        return new(true, "Parola a fost schimbată. Folosește-o la următoarea autentificare.");
    }

    public async Task<OwnPasswordResult> ChangeForcedAsync(int userId, string newPassword, string confirmation, CancellationToken cancellationToken = default)
    {
        newPassword ??= ""; confirmation ??= "";
        await using var connection = await OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        string username, role, hash; long version; bool active, flagged;
        await using (var read = Command(connection, "SELECT username,role,password_hash,version,is_active,must_change_password FROM web_users WHERE id=@id LIMIT 1", ("@id", userId)))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(false, "Contul nu mai există.");
            username = reader.GetString(0); role = reader.GetString(1); hash = reader.GetString(2); version = reader.GetInt64(3); active = reader.GetBoolean(4); flagged = reader.GetBoolean(5);
        }
        if (!active) return new(false, "Contul este inactiv.");
        if (!flagged) return new(false, "Parola nu mai trebuie schimbată acum. Autentifică-te din nou.");
        if (PasswordPolicy.Validate(newPassword) is { } policyProblem) return new(false, policyProblem);
        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal)) return new(false, "Parola nouă și confirmarea ei nu coincid.");
        if (Hasher.VerifyHashedPassword(Subject, hash, newPassword) != PasswordVerificationResult.Failed)
            return new(false, "Parola nouă trebuie să fie diferită de cea stabilită de administrator.");
        await using (var update = Command(connection, "UPDATE web_users SET password_hash=@hash,version=version+1,must_change_password=0 WHERE id=@id AND version=@version",
            ("@hash", Hasher.HashPassword(Subject, newPassword)), ("@id", userId), ("@version", version)))
        {
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return new(false, "Contul s-a schimbat între timp. Autentifică-te din nou.");
        }
        try
        {
            await audit.RecordAsync(new(username, role, AuditEntities.User, AuditActions.ForcedPasswordChange, username,
                "Utilizatorul și-a ales parola nouă la prima autentificare (parola nu se înregistrează).", string.Empty, string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}).", AuditActions.ForcedPasswordChange, exception.GetType().Name);
        }
        return new(true, "Parola a fost schimbată.");
    }

    private async Task TryRecordAsync(string action, int id, string username, string details, CancellationToken cancellationToken)
    {
        try { await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.User, action, id.ToString(), username, details, "", cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}).", action, exception.GetType().Name);
        }
    }
}
