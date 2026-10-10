using System.ComponentModel.DataAnnotations;

namespace BlazorStoc.Services;

// Role stays "Administrator" or "Utilizator" (Administrator exactly when the type is the system type of that name); the rights come from the type.
public sealed record WebUser(int Id, string Username, string DisplayName, string Role, bool IsActive, long Version, long? TypeId = null, string TypeName = "");
public sealed record AuthenticatedWebUser(int Id, string Username, string DisplayName, string Role);
public enum AuthenticationStatus { Success, InvalidCredentials, Inactive }
public sealed record AuthenticationResult(AuthenticationStatus Status, AuthenticatedWebUser? User = null);

public sealed class WebUserInput
{
    [Required(ErrorMessage = "Completează numele de utilizator.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Numele de utilizator trebuie să aibă între 3 și 100 de caractere.")]
    [RegularExpression(@"^[\p{L}0-9._-]+$", ErrorMessage = "Numele de utilizator poate conține litere, cifre, punct, cratimă și underscore.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Completează numele afișat.")]
    [StringLength(150, ErrorMessage = "Numele afișat poate avea cel mult 150 de caractere.")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Selectează nivelul de acces.")]
    public string Role { get; set; } = AccessRoles.LimitedUser;

    // The user type (Setari -> Tipuri de utilizatori). When empty the system type of Role is used.
    public long? TypeId { get; set; }

    public bool IsActive { get; set; } = true;

    public const int MinimumPasswordLength = 8;

    [StringLength(200, ErrorMessage = "Parola poate avea cel mult 200 de caractere.")]
    public string Password { get; set; } = "";

    // Typed a second time in the form only, to catch typing mistakes; never stored, journaled or sent to the repository.
    public string PasswordConfirmation { get; set; } = "";

    public string? PasswordConfirmationError() =>
        Password.Length > 0 && !string.Equals(Password, PasswordConfirmation ?? "", StringComparison.Ordinal)
            ? "Parola de confirmare nu coincide cu parola introdusă."
            : null;

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public WebUserInput Validated(bool isNew)
    {
        var normalized = new WebUserInput
        {
            Username = TextNormalization.ForObjectNameOrCode(Username),
            DisplayName = TextNormalization.ForObjectNameOrCode(DisplayName),
            Role = TextNormalization.ForStorage(Role),
            TypeId = TypeId,
            IsActive = IsActive,
            Password = Password ?? "",
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new UserOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        if (!AccessRoles.All.Contains(normalized.Role))
            throw new UserOperationException("Nivelul de acces selectat nu este valid.");
        if ((isNew || normalized.Password.Length > 0) && normalized.Password.Length < MinimumPasswordLength)
            throw new UserOperationException($"Parola trebuie să aibă minimum {MinimumPasswordLength} caractere.");
        if (isNew && !normalized.IsActive)
            throw new UserOperationException("Un utilizator nou trebuie creat activ; îl poți dezactiva ulterior.");
        if (!isNew && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new UserOperationException(reasonError);
        return normalized;
    }

    public static WebUserInput From(WebUser user) => new()
    {
        Username = user.Username,
        DisplayName = user.DisplayName,
        Role = user.Role,
        TypeId = user.TypeId,
        IsActive = user.IsActive
    };
}

public interface IUserAuthenticator
{
    Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<IReadOnlyList<WebUser>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<WebUser> CreateAsync(WebUserInput input, CancellationToken cancellationToken = default);
    Task<WebUser> UpdateAsync(WebUser original, WebUserInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(WebUser original, string reason, CancellationToken cancellationToken = default);
}

public sealed class UserOperationException(string message) : Exception(message);

public static class WebUserRules
{
    public static void CheckCurrent(WebUser? current, WebUser original)
    {
        if (current is null || current != original)
            throw new UserOperationException("Utilizatorul a fost modificat sau șters între timp. Actualizează lista și reia operația.");
    }
}
