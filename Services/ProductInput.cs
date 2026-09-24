using System.ComponentModel.DataAnnotations;

namespace BlazorStoc.Services;

public sealed class ProductInput
{
    // Holds the manufacturer's product code; the property keeps its historical name for storage and archive snapshots.
    [Required(ErrorMessage = "Completează codul produsului.")]
    [StringLength(100, ErrorMessage = "Codul produsului poate avea cel mult 100 de caractere.")]
    public string Name { get; set; } = "";
    [StringLength(1000, ErrorMessage = "Descrierea poate avea cel mult 1.000 de caractere.")]
    public string Description { get; set; } = "";
    [Required(ErrorMessage = "Completează categoria.")]
    [StringLength(100, ErrorMessage = "Categoria poate avea cel mult 100 de caractere.")]
    public string Category { get; set; } = "";
    [Required(ErrorMessage = "Completează subcategoria.")]
    [StringLength(100, ErrorMessage = "Subcategoria poate avea cel mult 100 de caractere.")]
    public string Subcategory { get; set; } = "";
    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public ProductInput Validated(Product? original = null)
    {
        var normalized = new ProductInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Description = TextNormalization.ForStorage(Description),
            Category = TextNormalization.ForObjectNameOrCode(Category),
            Subcategory = TextNormalization.ForObjectNameOrCode(Subcategory),
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new ProductOperationException(string.Join(" ", results.Select(r => r.ErrorMessage)));
        if (original is not null && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new ProductOperationException(reasonError);
        return normalized;
    }
    public static ProductInput From(Product product) => new() { Name = product.Name, Description = product.Description,
        Category = product.Category, Subcategory = product.Subcategory };
}

public sealed class ProductOperationException(string message) : Exception(message);

public static class ProductCode
{
    public const string Label = "Cod produs";
    public const string ConcurrentDuplicateMessage =
        "Codul produsului a fost folosit între timp pentru alt produs. Actualizează catalogul și introdu alt cod.";

    public static string DuplicateMessage(string existingCode, string category, string subcategory) =>
        $"Codul produsului «{existingCode}» există deja în catalog (categoria «{category}», subcategoria «{subcategory}»). Introdu alt cod.";

    // The internal numeric identifier stays in EntityId and is never part of the visible target.
    public static string AuditTarget(Product product) => product.Name;

    public static string AuditIdentification(Product product) => AuditDetails.Identification(
        (Label, product.Name), ("Categorie", product.Category), ("Subcategorie", product.Subcategory));

    public static AuditChange[] AuditChanges(Product before, Product after) =>
    [
        new(Label, before.Name, after.Name),
        new("Categorie", before.Category, after.Category),
        new("Subcategorie", before.Subcategory, after.Subcategory),
        new("Descriere", before.Description, after.Description)
    ];
}

public static class ProductRules
{
    public static void CheckCurrent(Product? current, Product original)
    {
        // Also compare fields: legacy clients may change data without incrementing the version.
        // Stock is excluded: it changes only through stock movements and must not invalidate an open product edit.
        if (current is null || current with { Quantity = original.Quantity } != original)
            throw new ProductOperationException("Produsul a fost modificat sau șters între timp. Închide formularul, actualizează catalogul și reia operația.");
    }
    public static void CheckDelete(Product current, bool hasStockMovements)
    {
        if (current.Quantity != 0)
            throw new ProductOperationException("Poți șterge doar un produs cu stoc zero.");
        if (hasStockMovements)
            throw new ProductOperationException("Produsul are mișcări de stoc asociate și nu poate fi șters. Istoricul trebuie păstrat.");
    }
}
