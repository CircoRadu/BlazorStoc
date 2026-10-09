using System.Globalization;

namespace BlazorStoc.Services;

// Required parameters of a subcategory (migration 37): a product of such a subcategory is saved as "<model> - <value> - <value>"
// (values in the order of the parameters) and cannot be saved, received or moved in stock without a value for each parameter.
public enum ParameterKind { Number = 1, Text = 2 }

public sealed record ParameterListValue(int Id, string Value, int ProductCount);

public sealed record SubcategoryParameter(int Id, string Category, string Subcategory, string Name, string Unit, ParameterKind Kind,
    int Position, long Version, IReadOnlyList<ParameterListValue> Values)
{
    public string Display(string value) => ProductParameterRules.Display(Unit, value);
    public int ProductCount => Values.Sum(value => value.ProductCount);
}

public sealed record ProductParameterChoice(int ParameterId, int ValueId);

// The model written by the user and the values chosen for a product (empty for a product of a subcategory without parameters).
public sealed record ProductParameterState(string BaseModel, IReadOnlyList<ProductParameterChoice> Choices);

public sealed record ParameterAffectedProduct(int ProductId, string OldName, string NewName, int Quantity);

// What changing a used value does: the products renamed and the names that would collide with other products (then nothing is changed).
public sealed record ParameterValueChange(string OldValue, string NewValue, IReadOnlyList<ParameterAffectedProduct> Affected, IReadOnlyList<string> Conflicts)
{
    public bool CanApply => Conflicts.Count == 0 && !string.Equals(OldValue, NewValue, StringComparison.Ordinal);
}

public interface IProductParameterRepository
{
    // Every parameter of every subcategory, with its values and how many products use each value.
    Task<IReadOnlyList<SubcategoryParameter>> GetParametersAsync(CancellationToken cancellationToken = default);
    // Adding a parameter is allowed to the product operators; the products already in the subcategory become incomplete (blocked until completed).
    Task<SubcategoryParameter> AddParameterAsync(ProductGroup group, string name, string unit, ParameterKind kind, CancellationToken cancellationToken = default);
    // Administrators only. The unit is part of the product names, so it changes only while no product uses the parameter.
    Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, string reason, CancellationToken cancellationToken = default);
    // Administrators only, and only while the subcategory has no products.
    Task DeleteParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default);
    Task<ParameterListValue> AddValueAsync(int parameterId, string value, CancellationToken cancellationToken = default);
    // A value attached to a product cannot be deleted.
    Task DeleteValueAsync(int valueId, string reason, CancellationToken cancellationToken = default);
    Task<ParameterValueChange> PreviewValueChangeAsync(int valueId, string newValue, CancellationToken cancellationToken = default);
    // Administrators only: the value and the names of the products that use it change together, or nothing changes.
    Task<ParameterValueChange> ChangeValueAsync(int valueId, string newValue, string reason, CancellationToken cancellationToken = default);
    Task<ProductParameterState> GetProductStateAsync(int productId, CancellationToken cancellationToken = default);
    // Products of a subcategory with parameters that miss a value (stock movements are refused for them).
    Task<IReadOnlySet<int>> GetIncompleteProductIdsAsync(CancellationToken cancellationToken = default);
    // The model (without the values) of each product that has parameters: product id -> model; the invoice pickup groups the variants by it.
    Task<IReadOnlyDictionary<int, string>> GetBaseModelsAsync(CancellationToken cancellationToken = default);
}

public static class ProductParameterRules
{
    public const int NameMaximumLength = 50;
    public const int UnitMaximumLength = 20;
    public const int ValueMaximumLength = 60;
    public const int ProductNameMaximumLength = 100;
    public const string Separator = " - ";
    public const string ValueUsedMessage = "Valoarea este atașată unor produse și nu poate fi ștearsă.";

    public static string Name(string? value)
    {
        var name = TextNormalization.ForObjectNameOrCode(value);
        if (name.Length == 0) throw new ProductOperationException("Completează denumirea parametrului.");
        if (name.Length > NameMaximumLength) throw new ProductOperationException($"Denumirea parametrului poate avea cel mult {NameMaximumLength} de caractere.");
        return name;
    }

    public static string Unit(string? value)
    {
        var unit = TextNormalization.ForObjectNameOrCode(value);
        if (unit.Length > UnitMaximumLength) throw new ProductOperationException($"Unitatea de măsură poate avea cel mult {UnitMaximumLength} de caractere.");
        return unit;
    }

    // A number is written the same way whatever the user typed ("2,8", "2.80" and "02.8" are the value 2.8); a text only loses its extra spaces.
    public static string Value(ParameterKind kind, string parameterName, string? raw)
    {
        var text = TextNormalization.ForObjectNameOrCode(raw);
        if (text.Length == 0) throw new ProductOperationException($"Completează valoarea parametrului «{parameterName}».");
        if (text.Length > ValueMaximumLength) throw new ProductOperationException($"Valoarea poate avea cel mult {ValueMaximumLength} de caractere.");
        if (kind == ParameterKind.Text) return text;
        var number = text.Replace(',', '.');
        if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            throw new ProductOperationException($"Parametrul «{parameterName}» cere un număr (de exemplu 2,8). «{text}» nu este număr.");
        return parsed.ToString("0.############", CultureInfo.InvariantCulture);
    }

    public static string Key(string value) => TextNormalization.UniquenessKey(value);

    public static string Display(string unit, string value) => unit.Length == 0 ? value : $"{value} {unit}";

    public static string ComposeName(string baseModel, IEnumerable<string> displays) =>
        string.Join(Separator, new[] { baseModel }.Concat(displays));

    public static string BaseModel(string? value)
    {
        var model = TextNormalization.ForObjectNameOrCode(value);
        if (model.Length == 0) throw new ProductOperationException("Completează modelul produsului.");
        return model;
    }

    public static string CheckNameLength(string name) => name.Length <= ProductNameMaximumLength
        ? name
        : throw new ProductOperationException($"Codul produsului rezultat «{name}» depășește {ProductNameMaximumLength} de caractere. Scurtează modelul.");

    public static string MissingMessage(string productName, IEnumerable<string> missing) =>
        $"Produsul «{productName}» nu are completate caracteristicile obligatorii ale subcategoriei ({string.Join(", ", missing)}). Editează produsul și alege valorile înainte de orice mișcare de stoc.";

    public static string RequiredMessage(string subcategory, IEnumerable<string> parameters) =>
        $"Subcategoria «{subcategory}» cere modelul și valoarea fiecărui parametru: {string.Join(", ", parameters)}.";

    public static string ConflictMessage(IEnumerable<string> names) =>
        $"Modificarea nu se poate face: ar rezulta coduri care există deja în catalog ({string.Join(", ", names)}). Combinarea variantelor nu este disponibilă.";

    public static string Summary(IEnumerable<(string Name, string Display)> values) =>
        string.Join("; ", values.Select(value => $"{value.Name}: {value.Display}"));
}
