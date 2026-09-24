namespace BlazorStoc.Services;

public static class DeleteConfirmationRules
{
    public const string DefaultChoice = "default";
    public const string CustomChoice = "custom";
    public const string ConfirmationWord = "sterge";

    // Feminine subjects use the Romanian definite article "-a" (e.g. "Observația"); every other subject in the
    // app uses the masculine/neuter "-ul" (e.g. "Produsul", "Beneficiarul"), so the ending selects agreement.
    public static string DefaultReason(string subject)
    {
        var required = RequiredSubject(subject);
        var participle = required.EndsWith('a') ? "folosită" : "folosit";
        return $"{required} nu va mai fi {participle}";
    }

    public static string? ValidateReason(string? choice, string? customReason)
    {
        if (choice is not (DefaultChoice or CustomChoice)) return "Selectează motivul ștergerii.";
        return choice == CustomChoice ? ChangeReasonRules.ValidationError(customReason) : null;
    }

    public static string ResolveReason(string subject, string choice, string? customReason)
    {
        if (ValidateReason(choice, customReason) is { } error) throw new ArgumentException(error, nameof(choice));
        return choice == DefaultChoice ? DefaultReason(subject) : ChangeReasonRules.Normalize(customReason);
    }

    public static bool IsConfirmationValid(string? value) =>
        string.Equals(value?.Trim(), ConfirmationWord, StringComparison.Ordinal);

    private static string RequiredSubject(string? value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("Denumirea tipului de obiect este obligatorie.", nameof(value))
        : value.Trim();
}
