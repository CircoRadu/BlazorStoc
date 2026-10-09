namespace BlazorStoc.Services;

public static class ProductGroupManagementRules
{
    public const int MaximumNameLength = 100;
    public const string CategoryNotEmptyMessage = "Categoria nu este goală: are subcategorii sau produse și nu poate fi ștearsă.";
    public const string SubcategoryNotEmptyMessage = "Subcategoria are produse asociate și nu poate fi ștearsă.";

    // The names of categories and subcategories are unique all together: a subcategory cannot be named like a category and the other way round.
    public static string NameTakenBySubcategory(string subcategory, string category) => $"Numele «{subcategory}» este deja o subcategorie (în categoria «{category}»). Numele categoriilor și ale subcategoriilor sunt unice împreună.";
    public static string NameTakenByCategory(string category) => $"Numele «{category}» este deja o categorie. Numele categoriilor și ale subcategoriilor sunt unice împreună.";

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
