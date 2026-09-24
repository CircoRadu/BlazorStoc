namespace BlazorStoc.Services;

public static class ChangeReasonRules
{
    public const int MaximumLength = 500;
    public const string RequiredMessage = "Completează câmpul «Motivare modificare».";
    public const string TooLongMessage = "Motivarea modificării poate avea cel mult 500 de caractere.";

    public static string Normalize(string? value) => TextNormalization.ForStorage(value ?? string.Empty);

    public static string? ValidationError(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0) return RequiredMessage;
        return normalized.Length > MaximumLength ? TooLongMessage : null;
    }
}
