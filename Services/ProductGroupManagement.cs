namespace BlazorStoc.Services;

public static class ProductGroupManagementRules
{
    public const int MaximumNameLength = 100;

    public static string Name(string? value, string label)
    {
        var normalized = TextNormalization.ForObjectNameOrCode(value);
        if (normalized.Length == 0)
            throw new ProductOperationException($"Completează câmpul «{label}».");
        if (normalized.Length > MaximumNameLength)
            throw new ProductOperationException($"Câmpul «{label}» poate avea cel mult {MaximumNameLength} de caractere.");
        return normalized;
    }

    public static string Reason(string? value)
    {
        var normalized = ChangeReasonRules.Normalize(value);
        if (ChangeReasonRules.ValidationError(normalized) is { } error)
            throw new ProductOperationException(error);
        return normalized;
    }
}
