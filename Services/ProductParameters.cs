using System.Globalization;

namespace BlazorStoc.Services;

// Required parameters of a subcategory (migration 37): a product of such a subcategory is saved as "<model> - <value> - <value>"
// (values in the order of the parameters) and cannot be saved, received or moved in stock without a value for each parameter.
// Interval: each value of the list has a start and an end number, written "start-end" (2.8-12 for a varifocal lens of 2.8 to 12 mm).
// Fixed: a parameter with one value, its own name (a "Fisheye" lens); it has no list to extend and no unit, and it works only inside a family,
// where choosing it gives the family its value (a standalone fixed parameter is not required and does not appear on products).
public enum ParameterKind { Number = 1, Text = 2, Interval = 3, Fixed = 4 }

public sealed record ParameterListValue(int Id, string Value, int ProductCount);

// FamilyId: the family the parameter belongs to (null = a parameter of its own, required for every product). A family groups at least two parameters of
// a subcategory; a product has a value for exactly one member of each family (a camera is Fixed or Varifocal, not both).
public sealed record SubcategoryParameter(int Id, string Category, string Subcategory, string Name, string Unit, ParameterKind Kind,
    int Position, long Version, IReadOnlyList<ParameterListValue> Values, int? FamilyId = null)
{
    public string Display(string value) => ProductParameterRules.Display(Unit, value);
    public int ProductCount => Values.Sum(value => value.ProductCount);
}

public sealed record ParameterFamily(int Id, string Category, string Subcategory, string Name, int Position, long Version, IReadOnlyList<int> ParameterIds);

// What a product must choose: one slot per parameter of its own and one per family (the family takes the place of its first member in the code of the product).
public sealed record ParameterSlot(ParameterFamily? Family, IReadOnlyList<SubcategoryParameter> Members, int Position)
{
    public string Name => Family?.Name ?? Members[0].Name;
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
    // The kind may change too, as long as every value of the parameter is already written the way the new kind stores it (anything fits a text).
    Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, ParameterKind kind, string reason, CancellationToken cancellationToken = default);
    Task UpdateParameterAsync(SubcategoryParameter original, string name, string unit, string reason, CancellationToken cancellationToken = default) =>
        UpdateParameterAsync(original, name, unit, original.Kind, reason, cancellationToken);
    // Administrators only.
    // Allowed with products: they lose the value and the code is rewritten without it (refused, with the clashing names, when two codes would become equal).
    Task DeleteParameterAsync(SubcategoryParameter original, string reason, CancellationToken cancellationToken = default);
    Task<ParameterDeletionPreview> PreviewParameterDeletionAsync(SubcategoryParameter parameter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ParameterDeletionPreview([], []));
    Task<ParameterListValue> AddValueAsync(int parameterId, string value, CancellationToken cancellationToken = default);
    // A value attached to a product cannot be deleted.
    Task DeleteValueAsync(int valueId, string reason, CancellationToken cancellationToken = default);
    Task<ParameterValueChange> PreviewValueChangeAsync(int valueId, string newValue, CancellationToken cancellationToken = default);
    // Administrators only: the value and the names of the products that use it change together, or nothing changes.
    Task<ParameterValueChange> ChangeValueAsync(int valueId, string newValue, string reason, CancellationToken cancellationToken = default);
    // Families of parameters (administrators change them). A family has at least two parameters of its subcategory; a parameter is in at most one family.
    Task<IReadOnlyList<ParameterFamily>> GetFamiliesAsync(CancellationToken cancellationToken = default);
    // Refused (with the products listed) when a product of the subcategory already has values for two or more of the members: they are corrected first.
    Task<ParameterFamily> CreateFamilyAsync(ProductGroup group, string name, IReadOnlyList<int> parameterIds, CancellationToken cancellationToken = default);
    Task<ParameterFamily> UpdateFamilyAsync(ParameterFamily original, string name, IReadOnlyList<int> parameterIds, string reason, CancellationToken cancellationToken = default);
    // Ungroups the parameters (each becomes required on its own again, so the products that had only one of them become incomplete).
    Task DeleteFamilyAsync(ParameterFamily original, string reason, CancellationToken cancellationToken = default);
    Task<ProductParameterState> GetProductStateAsync(int productId, CancellationToken cancellationToken = default);
    // Products of a subcategory with parameters that miss a value (stock movements are refused for them).
    Task<IReadOnlySet<int>> GetIncompleteProductIdsAsync(CancellationToken cancellationToken = default);
    // The model (without the values) of each product that has parameters: product id -> model; the invoice pickup groups the variants by it.
    Task<IReadOnlyDictionary<int, string>> GetBaseModelsAsync(CancellationToken cancellationToken = default);
    // The chosen values of every product that has parameters: product id -> choices (the catalog shows them as tags and filters by them).
    Task<IReadOnlyDictionary<int, IReadOnlyList<ProductParameterChoice>>> GetAllChoicesAsync(CancellationToken cancellationToken = default);
    // The required parameters a product still lacks (empty = complete), worded as the refusal of a stock operation words them.
    async Task<IReadOnlyList<string>> GetMissingAsync(Product product, CancellationToken cancellationToken = default)
    {
        bool Same(string category, string subcategory) => string.Equals(category, product.Category, StringComparison.OrdinalIgnoreCase) && string.Equals(subcategory, product.Subcategory, StringComparison.OrdinalIgnoreCase);
        var parameters = (await GetParametersAsync(cancellationToken).ConfigureAwait(false)).Where(parameter => Same(parameter.Category, parameter.Subcategory)).ToList();
        if (parameters.Count == 0) return [];
        var families = (await GetFamiliesAsync(cancellationToken).ConfigureAwait(false)).Where(family => Same(family.Category, family.Subcategory)).ToList();
        var state = await GetProductStateAsync(product.Id, cancellationToken).ConfigureAwait(false);
        return ProductParameterRules.Problems(ProductParameterRules.Slots(parameters, families), state.Choices.Select(choice => choice.ParameterId).ToHashSet());
    }
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
        if (kind is ParameterKind.Text or ParameterKind.Fixed) return text;
        if (kind == ParameterKind.Interval) return Interval(parameterName, text);
        var number = text.Replace(',', '.');
        if (!decimal.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            throw new ProductOperationException($"Parametrul «{parameterName}» cere un număr (de exemplu 2,8). «{text}» nu este număr.");
        return parsed.ToString("0.############", CultureInfo.InvariantCulture);
    }

    // "2,8-12", "2.80 - 12.0", "2,8–12" and "2,8..12" are the interval 2.8 to 12, stored as "2.8-12"; negative ends work ("-5--2"). The start is below the end.
    private static readonly System.Text.RegularExpressions.Regex IntervalPattern = new(
        @"^\s*(-?\d+(?:[.,]\d+)?)\s*(?:-|–|—|\.\.)\s*(-?\d+(?:[.,]\d+)?)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string Interval(string parameterName, string text)
    {
        var match = IntervalPattern.Match(text);
        if (!match.Success || !TryNumber(match.Groups[1].Value, out var start) || !TryNumber(match.Groups[2].Value, out var end))
            throw new ProductOperationException($"Parametrul «{parameterName}» cere un interval cu început și sfârșit (de exemplu 2,8-12). «{text}» nu este interval.");
        if (start >= end) throw new ProductOperationException($"La parametrul «{parameterName}» începutul intervalului trebuie să fie mai mic decât sfârșitul ({text}).");
        return IntervalText(start, end);
    }

    public static string IntervalText(decimal start, decimal end) => $"{Number(start)}-{Number(end)}";

    private static string Number(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    private static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    // Start and end of a stored interval value (null when the text is not one), for sorting and for the two input fields.
    public static (decimal Start, decimal End)? IntervalParts(string value) =>
        IntervalPattern.Match(value) is { Success: true } match && TryNumber(match.Groups[1].Value, out var start) && TryNumber(match.Groups[2].Value, out var end) ? (start, end) : null;

    // The unit of measure belongs to the numeric kinds (a number and an interval); a text has none.
    public static bool UsesUnit(ParameterKind kind) => kind is ParameterKind.Number or ParameterKind.Interval;

    public static string KindLabel(ParameterKind kind) => kind switch { ParameterKind.Number => "Număr", ParameterKind.Interval => "Interval", ParameterKind.Fixed => "Valoare fixă", _ => "Text liber" };

    public const string FixedKindChangeMessage = "Tipul «Valoare fixă» nu se schimbă după creare (și nici un alt tip nu devine «Valoare fixă»). Șterge parametrul și creează-l din nou.";
    public const string FixedValueMessage = "Un parametru cu valoare fixă are o singură valoare, egală cu denumirea lui: nu se adaugă, nu se schimbă și nu se șterg valori.";
    public const string FixedRenameUsedMessage = "Valoarea parametrului este denumirea lui și face parte din codurile produselor: denumirea nu se mai schimbă cât timp există produse care îl folosesc.";
    public const string FixedLeaveFamilyMessage = "Parametrul cu valoare fixă «{0}» este folosit de produse și lucrează numai în familie: nu poate fi scos din familie sau familia desfăcută.";

    public const int FamilyMinimumMembers = 2;

    // The slots of a subcategory in the order of the code: a parameter of its own, or a family at the position of its first member.
    public static IReadOnlyList<ParameterSlot> Slots(IEnumerable<SubcategoryParameter> parameters, IEnumerable<ParameterFamily> families)
    {
        var familyList = families.ToList();
        var slots = new List<ParameterSlot>();
        foreach (var group in parameters.OrderBy(parameter => parameter.Position).ThenBy(parameter => parameter.Id).GroupBy(parameter => parameter.FamilyId))
        {
            var family = group.Key is { } familyId ? familyList.FirstOrDefault(item => item.Id == familyId) : null;
            if (family is null) slots.AddRange(group.Where(parameter => parameter.Kind != ParameterKind.Fixed).Select(parameter => new ParameterSlot(null, [parameter], parameter.Position)));
            else slots.Add(new ParameterSlot(family, group.ToList(), group.Min(parameter => parameter.Position)));
        }
        return slots.OrderBy(slot => slot.Position).ThenBy(slot => slot.Members[0].Id).ToList();
    }

    // What is missing or wrong for a product with these chosen parameters (empty = complete): a parameter of its own without value, a family without any
    // member chosen, a family with two or more members chosen.
    public static IReadOnlyList<string> Problems(IReadOnlyList<ParameterSlot> slots, IReadOnlySet<int> chosenParameterIds)
    {
        var problems = new List<string>();
        foreach (var slot in slots)
        {
            var chosen = slot.Members.Where(member => chosenParameterIds.Contains(member.Id)).ToList();
            if (slot.Family is null) { if (chosen.Count == 0) problems.Add(slot.Name); }
            else if (chosen.Count == 0) problems.Add($"{slot.Name} (alege una dintre: {string.Join(", ", slot.Members.Select(member => member.Name))})");
            else if (chosen.Count > 1) problems.Add($"{slot.Name}: un singur parametru poate fi activ, nu {string.Join(" și ", chosen.Select(member => member.Name))}");
        }
        return problems;
    }

    // The parameters whose values go in the code of a product, in the order of the slots (one member per family).
    public static IReadOnlyList<SubcategoryParameter> CodeOrder(IReadOnlyList<ParameterSlot> slots, IReadOnlySet<int> chosenParameterIds) =>
        slots.Select(slot => slot.Members.FirstOrDefault(member => chosenParameterIds.Contains(member.Id))).OfType<SubcategoryParameter>().ToList();

    public static string FamilyName(string? value)
    {
        var name = TextNormalization.ForObjectNameOrCode(value);
        if (name.Length == 0) throw new ProductOperationException("Completează denumirea familiei de parametri.");
        if (name.Length > NameMaximumLength) throw new ProductOperationException($"Denumirea familiei poate avea cel mult {NameMaximumLength} de caractere.");
        return name;
    }

    public static string FamilyTooSmallMessage => $"O familie are cel puțin {FamilyMinimumMembers} parametri. Alege cel puțin {FamilyMinimumMembers} parametri ai subcategoriei.";

    public static string FamilyConflictMessage(string family, IEnumerable<string> products) =>
        $"Familia «{family}» nu se poate forma: produse ale subcategoriei au deja valori pentru mai mulți dintre parametrii aleși ({string.Join(", ", products)}). Corectează întâi aceste produse (alege câte o singură valoare).";

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

// What deleting a parameter does to the products that have a value for it: they lose it (and its part of the code). Conflicts = names that would clash.
public sealed record ParameterDeletionPreview(IReadOnlyList<ParameterAffectedProduct> Affected, IReadOnlyList<string> Conflicts)
{
    public bool CanApply => Conflicts.Count == 0;
}
