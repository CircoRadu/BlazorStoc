using System.ComponentModel.DataAnnotations;

namespace BlazorStoc.Services;

// Role stays "Administrator" or "Utilizator" (Administrator exactly when the type is the system type of that name); the rights come from the type.
public sealed record WebUser(int Id, string Username, string DisplayName, string Role, bool IsActive, long Version, long? TypeId = null, string TypeName = "", [property: System.Text.Json.Serialization.JsonIgnore] bool MustChangePassword = false);
public sealed record AuthenticatedWebUser(int Id, string Username, string DisplayName, string Role, bool MustChangePassword = false);

/// <summary>The rules of every password of a web account (the one an administrator sets and the one a user chooses): minimum 8 characters with a lower case letter, an upper case letter, a digit and a special character.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 200;
    public const string Summary = "minimum 8 caractere, cu cel puțin o literă mică, o literă mare, o cifră și un caracter special";

    /// <summary>One condition of a password: the key (the page of the first sign-in checks the same keys in the browser), the text shown, what is said when it is missing, and the test.</summary>
    public sealed record Rule(string Key, string Text, string Missing, Func<string, bool> Test);

    public static readonly IReadOnlyList<Rule> Rules =
    [
        new("length", $"Minimum {MinimumLength} caractere", $"minimum {MinimumLength} caractere", password => password.Length >= MinimumLength),
        new("lower", "Cel puțin o literă mică", "o literă mică", password => password.Any(char.IsLower)),
        new("upper", "Cel puțin o literă mare", "o literă mare", password => password.Any(char.IsUpper)),
        new("digit", "Cel puțin o cifră", "o cifră", password => password.Any(char.IsDigit)),
        new("special", "Cel puțin un caracter special", "un caracter special", password => password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)))
    ];

    /// <summary>Null when the password follows the rules, otherwise what is missing.</summary>
    public static string? Validate(string? password)
    {
        password ??= "";
        var missing = Rules.Where(rule => !rule.Test(password)).Select(rule => rule.Missing).ToList();
        if (password.Length > MaximumLength) return $"Parola poate avea cel mult {MaximumLength} de caractere.";
        return missing.Count == 0 ? null : "Parola trebuie să aibă " + Summary + ". Lipsește: " + string.Join(", ", missing) + ".";
    }
}
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

    public const int MinimumPasswordLength = PasswordPolicy.MinimumLength;

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
        if ((isNew || normalized.Password.Length > 0) && PasswordPolicy.Validate(normalized.Password) is { } passwordProblem)
            throw new UserOperationException(passwordProblem);
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
