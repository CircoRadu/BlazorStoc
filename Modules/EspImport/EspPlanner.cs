using System.Globalization;
using System.Text.RegularExpressions;
using BlazorStoc.Services;

namespace BlazorStoc.Modules.EspImport;

// The plan of the import, built without writing anything: every product of the export with a positive quantity is matched with the BlazorStoc catalog (by code, and through the model and the
// parameters of the subcategories that have them) and gets an action (an existing product receives the quantity, a new one is created) and, when there is something to settle, a problem.
// Pure code over a snapshot of the catalog, so it is testable without a database.

/// <summary>The catalog of BlazorStoc as the planner needs it.</summary>
public sealed class EspCatalog
{
    public required IReadOnlyList<Product> Products { get; init; }
    public required IReadOnlyList<ProductGroup> Groups { get; init; }
    public required IReadOnlyList<SubcategoryParameter> Parameters { get; init; }
    public required IReadOnlyList<ParameterFamily> Families { get; init; }
    public required IReadOnlyDictionary<int, string> BaseModels { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<ProductParameterChoice>> Choices { get; init; }
    /// <summary>The products of subcategories with parameters that lack a required value (stock movements are refused for them until they are completed).</summary>
    public IReadOnlySet<int> Incomplete { get; init; } = new HashSet<int>();

    private Dictionary<string, Product>? byKey;
    public Dictionary<string, Product> ByKey => byKey ??= Products.GroupBy(product => TextNormalization.UniquenessKey(product.Name)).ToDictionary(group => group.Key, group => group.First());

    public static string Key(string? value) => TextNormalization.UniquenessKey(value);

    public string? CategoryName(string name) => Groups.Select(group => group.Category).FirstOrDefault(category => Key(category) == Key(name));

    public ProductGroup? Group(string category, string subcategory) =>
        Groups.FirstOrDefault(group => Key(group.Category) == Key(category) && group.Subcategory.Length > 0 && Key(group.Subcategory) == Key(subcategory));

    public bool NameTaken(string name) => Groups.Any(group => Key(group.Category) == Key(name) || (group.Subcategory.Length > 0 && Key(group.Subcategory) == Key(name)));

    /// <summary>The slots a product of the subcategory must settle (empty when the subcategory has no parameters).</summary>
    public IReadOnlyList<ParameterSlot> Slots(string category, string subcategory)
    {
        bool Same(string parameterCategory, string parameterSubcategory) => Key(parameterCategory) == Key(category) && (parameterSubcategory.Length == 0 || Key(parameterSubcategory) == Key(subcategory));
        var parameters = Parameters.Where(parameter => Same(parameter.Category, parameter.Subcategory)).ToList();
        if (parameters.Count == 0) return [];
        var families = Families.Where(family => Same(family.Category, family.Subcategory)).ToList();
        return ProductParameterRules.Slots(parameters, families);
    }
}

/// <summary>The value chosen for one slot: an existing value of the parameter, or a new one (typed, added to the list when the import is applied).</summary>
public sealed record EspSlotChoice(int ParameterId, int? ValueId, string? NewValue);

public sealed class EspPlanItem
{
    public required int OldId { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required int Quantity { get; init; }
    public required string OldCategory { get; init; }
    public required string OldSubcategory { get; init; }
    public bool HasImage { get; init; }

    public string TargetCategory { get; set; } = "";
    public string TargetSubcategory { get; set; } = "";
    public bool NewCategory { get; set; }
    public bool NewSubcategory { get; set; }
    /// <summary>The category of the product in the old database differs from the one of its subcategory (the subcategory wins).</summary>
    public bool CategoryCorrected { get; set; }

    public IReadOnlyList<ParameterSlot> Slots { get; set; } = [];
    public string BaseModel { get; set; } = "";
    /// <summary>Slot index (in Slots) -> chosen value.</summary>
    public Dictionary<int, EspSlotChoice> Choices { get; set; } = [];
    /// <summary>The parameters were proposed by the import (from the code or from an existing variant) and not chosen by the user.</summary>
    public bool Proposed { get; set; }

    public bool Omit { get; set; }
    public Product? Existing { get; set; }
    public string FinalCode { get; set; } = "";
    public List<string> Problems { get; } = [];
    public List<string> Notes { get; } = [];
    public string SuggestNote { get; set; } = "";

    public bool HasProblems => !Omit && Problems.Count > 0;
    public bool IsTaken => !Omit && Problems.Count == 0;
    public bool Creates => IsTaken && Existing is null;
    public bool NeedsParameters => Slots.Count > 0;
    /// <summary>The product exists but lacks its required parameters: they are chosen here and written to it (its code becomes "model - value") before the quantity is received.</summary>
    public bool Completes { get; set; }
    /// <summary>Taken without parameters: the product is created (or left) incomplete and receives only its initial quantity; it is frozen in BlazorStoc (no other movement) until it is completed.</summary>
    public bool Frozen { get; set; }
    public bool IsFrozen => IsTaken && Frozen && Slots.Count > 0;
}

public sealed class EspAnalysis
{
    public required string Id { get; init; }
    public required string SourceName { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required EspExport Export { get; init; }
    public required EspCatalog Catalog { get; set; }
    public required List<EspPlanItem> Items { get; init; }

    public int TakenProducts => Items.Count(item => item.IsTaken);
    public int TakenPieces => Items.Where(item => item.IsTaken).Sum(item => item.Quantity);
    public int ProblemProducts => Items.Count(item => item.HasProblems);
    public int OmittedProducts => Items.Count(item => item.Omit);
    public int FrozenProducts => Items.Count(item => item.IsFrozen);
    public int ProposedProducts => Items.Count(item => item.IsTaken && item.Proposed);
    public int NewProducts => Items.Count(item => item.Creates);
    public int ExistingProducts => Items.Count(item => item.IsTaken && item.Existing is not null);
    public int NewCategories => Items.Where(item => item.IsTaken && item.NewCategory).Select(item => EspCatalog.Key(item.TargetCategory)).Distinct().Count();
    public int NewSubcategories => Items.Where(item => item.IsTaken && item.NewSubcategory).Select(item => EspCatalog.Key(item.TargetSubcategory)).Distinct().Count();

    /// <summary>The products of BlazorStoc that no product of the export reaches: after the point zero they have no stock.</summary>
    public IReadOnlyList<Product> NotInExport()
    {
        var reached = Items.Where(item => !item.Omit && item.Existing is not null).Select(item => item.Existing!.Id).ToHashSet();
        return [.. Catalog.Products.Where(product => !reached.Contains(product.Id))];
    }

    public bool CanApply => Items.Count > 0 && ProblemProducts == 0 && TakenProducts > 0;
}

public static class EspPlanner
{
    private const int NameMaximumLength = 100;

    public static EspAnalysis Build(EspExport export, EspCatalog catalog, string id, DateTime createdUtc)
    {
        var categories = export.Categories.ToDictionary(category => category.Id, category => category.Name);
        var subcategories = export.Subcategories.ToDictionary(subcategory => subcategory.Id);
        var items = new List<EspPlanItem>();
        foreach (var product in export.Products.OrderBy(product => product.Id))
        {
            subcategories.TryGetValue(product.SubcategoryId, out var subcategory);
            var subcategoryCategory = subcategory is not null && categories.TryGetValue(subcategory.CategoryId, out var own) ? own : null;
            categories.TryGetValue(product.CategoryId, out var productCategory);
            var categoryName = subcategoryCategory ?? productCategory ?? "Fără categorie";
            items.Add(new EspPlanItem
            {
                OldId = product.Id, Code = product.Code, Description = product.Description, Quantity = product.Quantity, OldCategory = categoryName,
                OldSubcategory = subcategory?.Name ?? "Fără subcategorie", HasImage = export.Images.ContainsKey(product.Id),
                CategoryCorrected = subcategoryCategory is not null && productCategory is not null && EspCatalog.Key(subcategoryCategory) != EspCatalog.Key(productCategory)
            });
        }
        AssignTargets(items, catalog);
        foreach (var item in items) { Suggest(item, catalog); Evaluate(item, catalog); }
        CheckDuplicates(items);
        return new EspAnalysis { Id = id, SourceName = export.SourceName, CreatedUtc = createdUtc, Export = export, Catalog = catalog, Items = items };
    }

    // Category and subcategory of each product. A subcategory name is unique in the whole register (and different from every category name), so a name that is already used elsewhere
    // gets its category in brackets ("Surse (Interfonie/Telefonie)"), the way the earlier import named them.
    private static void AssignTargets(List<EspPlanItem> items, EspCatalog catalog)
    {
        var created = new Dictionary<(string Category, string Subcategory), string>();
        var pending = new List<(string Category, string Subcategory, string CategoryName)>();
        foreach (var item in items)
        {
            var existingCategory = catalog.CategoryName(item.OldCategory);
            item.NewCategory = existingCategory is null;
            item.TargetCategory = existingCategory ?? item.OldCategory;
            var plain = catalog.Group(item.TargetCategory, item.OldSubcategory);
            var bracketed = plain is null ? catalog.Group(item.TargetCategory, $"{item.OldSubcategory} ({item.TargetCategory})") : null;
            if (plain is not null) item.TargetSubcategory = plain.Subcategory;
            else if (bracketed is not null) item.TargetSubcategory = bracketed.Subcategory;
            else
            {
                item.NewSubcategory = true;
                var pair = (EspCatalog.Key(item.TargetCategory), EspCatalog.Key(item.OldSubcategory));
                if (!created.ContainsKey(pair)) { created[pair] = item.OldSubcategory; pending.Add((pair.Item1, pair.Item2, item.TargetCategory)); }
            }
        }
        // A new name that another category also needs, or that already exists in the register, is written with the category.
        var clashing = pending.GroupBy(entry => entry.Subcategory).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        foreach (var item in items.Where(item => item.NewSubcategory))
        {
            var key = EspCatalog.Key(item.OldSubcategory);
            var taken = catalog.NameTaken(item.OldSubcategory) || clashing.Contains(key) || key == EspCatalog.Key(item.TargetCategory) || items.Any(other => EspCatalog.Key(other.OldCategory) == key);
            item.TargetSubcategory = taken ? $"{item.OldSubcategory} ({item.TargetCategory})" : item.OldSubcategory;
        }
    }

    private static readonly Regex SuffixValue = new(@"^(?<model>.*?)[\s\-(]*(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>mm|tb|gb)\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    // "...-2.8-12mm": an interval is read only when it is a real one (start below end); "DS-1047-2.8mm" is the model DS-1047 and 2.8 mm, not the interval 1047-2.8.
    private static readonly Regex SuffixInterval = new(@"^(?<model>.*?)[\s\-(]*(?<value>\d+(?:[.,]\d+)?\s*[-–]\s*\d+(?:[.,]\d+)?)\s*(?<unit>mm|tb|gb)\s*\)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SuffixFisheye = new(@"^(?<model>.*?)[\s\-(]*fish\s?eye\s*\)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Proposes the model and the parameters for a product that goes in a subcategory with parameters: from the end of its code ("-2.8mm", "(4TB)", "Fisheye") or from the single existing variant of the same model.</summary>
    public static void Suggest(EspPlanItem item, EspCatalog catalog)
    {
        catalog.ByKey.TryGetValue(EspCatalog.Key(item.Code), out var exact);
        var incomplete = exact is not null && catalog.Incomplete.Contains(exact.Id);
        item.Slots = exact is not null ? (incomplete ? catalog.Slots(exact.Category, exact.Subcategory) : []) : item.NewSubcategory ? [] : catalog.Slots(item.TargetCategory, item.TargetSubcategory);
        item.Completes = incomplete && item.Slots.Count > 0;
        item.BaseModel = "";
        item.Choices = [];
        item.Proposed = false;
        item.SuggestNote = "";
        if (item.Slots.Count == 0 || (exact is not null && !item.Completes)) return;

        var model = item.Code;
        var fish = SuffixFisheye.Match(item.Code);
        var intervalMatch = SuffixInterval.Match(item.Code);
        var match = intervalMatch.Success && ProductParameterRules.IntervalParts(Regex.Replace(intervalMatch.Groups["value"].Value, @"\s+", "").Replace('–', '-')) is { } parts && parts.Start < parts.End
            ? intervalMatch : SuffixValue.Match(item.Code);
        if (fish.Success && fish.Groups["model"].Value.Trim().Length > 0)
        {
            model = fish.Groups["model"].Value.Trim();
            for (var index = 0; index < item.Slots.Count; index++)
                if (item.Slots[index].Members.FirstOrDefault(member => member.Kind == ParameterKind.Fixed && EspCatalog.Key(member.Name) == "FISHEYE") is { } member)
                    item.Choices[index] = new EspSlotChoice(member.Id, member.Values.FirstOrDefault()?.Id, null);
        }
        else if (match.Success && match.Groups["model"].Value.Trim().Length > 0)
        {
            model = match.Groups["model"].Value.Trim();
            var raw = Regex.Replace(match.Groups["value"].Value, @"\s+", "");
            var unit = match.Groups["unit"].Value;
            var interval = Regex.IsMatch(raw, @"^\d[\d.,]*[-–]\d");
            for (var index = 0; index < item.Slots.Count; index++)
            {
                var member = item.Slots[index].Members.FirstOrDefault(candidate => string.Equals(candidate.Unit, unit, StringComparison.OrdinalIgnoreCase)
                    && candidate.Kind == (interval ? ParameterKind.Interval : ParameterKind.Number));
                if (member is null) continue;
                try
                {
                    var text = ProductParameterRules.Value(member.Kind, member.Name, raw.Replace('–', '-'));
                    var value = member.Values.FirstOrDefault(candidate => string.Equals(candidate.Value, text, StringComparison.Ordinal));
                    item.Choices[index] = new EspSlotChoice(member.Id, value?.Id, value is null ? text : null);
                }
                catch (ProductOperationException) { }
                break;
            }
        }
        else
        {
            // No value in the code: the code is the model of an existing variant. Exactly one variant proposes its parameters; several leave the choice to the user.
            var variants = catalog.Products.Where(product => catalog.BaseModels.TryGetValue(product.Id, out var baseModel) && EspCatalog.Key(baseModel) == EspCatalog.Key(item.Code)
                && EspCatalog.Key(product.Category) == EspCatalog.Key(item.TargetCategory) && EspCatalog.Key(product.Subcategory) == EspCatalog.Key(item.TargetSubcategory)).ToList();
            if (variants.Count > 1) item.SuggestNote = $"Modelul există în BlazorStoc cu mai multe variante ({string.Join(", ", variants.Select(variant => variant.Name))}): alege parametrul.";
            else if (variants.Count == 1 && catalog.Choices.TryGetValue(variants[0].Id, out var choices))
            {
                for (var index = 0; index < item.Slots.Count; index++)
                    if (choices.FirstOrDefault(choice => item.Slots[index].Members.Any(member => member.Id == choice.ParameterId)) is { } choice)
                        item.Choices[index] = new EspSlotChoice(choice.ParameterId, choice.ValueId, null);
            }
        }
        if (item.Choices.Count > 0) { item.BaseModel = model; item.Proposed = true; }
        else if (match.Success || fish.Success) item.BaseModel = model;
    }

    /// <summary>Settles the final code, the product it reaches and the problems from the current choices.</summary>
    public static void Evaluate(EspPlanItem item, EspCatalog catalog)
    {
        item.Problems.Clear();
        item.Notes.Clear();
        if (item.SuggestNote.Length > 0) item.Notes.Add(item.SuggestNote);
        item.Existing = null;
        item.FinalCode = "";
        if (item.Omit) return;
        if (item.Frozen && item.Slots.Count > 0)
        {
            if (catalog.ByKey.TryGetValue(EspCatalog.Key(item.Code), out var frozenExisting))
            {
                item.Existing = frozenExisting;
                item.FinalCode = frozenExisting.Name;
                AddLocationNotes(item, frozenExisting);
            }
            else
            {
                item.FinalCode = TextNormalization.ForObjectNameOrCode(item.Code);
                if (item.FinalCode.Length == 0) item.Problems.Add("Codul produsului este gol.");
                else if (item.FinalCode.Length > NameMaximumLength) item.Problems.Add($"Codul depășește {NameMaximumLength} de caractere.");
            }
            item.Notes.Add("Preluat înghețat: rămâne fără parametri și nu primește nicio mișcare până nu este completat în editorul de produs.");
            return;
        }
        if (catalog.ByKey.TryGetValue(EspCatalog.Key(item.Code), out var exact) && !item.Completes)
        {
            item.Existing = exact;
            item.FinalCode = exact.Name;
            AddLocationNotes(item, exact);
            return;
        }
        if (item.Slots.Count == 0)
        {
            item.FinalCode = TextNormalization.ForObjectNameOrCode(item.Code);
            if (item.FinalCode.Length == 0) item.Problems.Add("Codul produsului este gol.");
            else if (item.FinalCode.Length > NameMaximumLength) item.Problems.Add($"Codul depășește {NameMaximumLength} de caractere.");
            return;
        }
        var missing = new List<string>();
        for (var index = 0; index < item.Slots.Count; index++)
            if (!item.Choices.TryGetValue(index, out var choice) || (choice.ValueId is null && string.IsNullOrWhiteSpace(choice.NewValue))) missing.Add(item.Slots[index].Name);
        if (TextNormalization.ForObjectNameOrCode(item.BaseModel).Length == 0) missing.Insert(0, "modelul");
        if (missing.Count > 0)
        {
            item.Problems.Add($"Alege {string.Join(", ", missing)} (subcategoria «{item.TargetSubcategory}» are parametri obligatorii).");
            return;
        }
        var displays = new List<string>();
        foreach (var parameter in CodeOrder(item))
        {
            var display = DisplayOf(item, parameter.Parameter, parameter.Choice);
            if (display is null) { item.Problems.Add($"Valoarea aleasă pentru «{parameter.Parameter.Name}» nu este validă."); return; }
            displays.Add(display);
        }
        var code = TextNormalization.ForObjectNameOrCode(ProductParameterRules.ComposeName(TextNormalization.ForObjectNameOrCode(item.BaseModel), displays));
        item.FinalCode = code;
        if (code.Length > NameMaximumLength) { item.Problems.Add($"Codul rezultat «{code}» depășește {NameMaximumLength} de caractere."); return; }
        if (item.Completes && catalog.ByKey.TryGetValue(EspCatalog.Key(item.Code), out var completed))
        {
            if (catalog.ByKey.TryGetValue(EspCatalog.Key(code), out var clash) && clash.Id != completed.Id)
            {
                item.Problems.Add($"Codul rezultat «{code}» există deja la alt produs: omite produsul sau alege alte valori.");
                return;
            }
            item.Existing = completed;
            item.Notes.Add($"Produsul existent «{completed.Name}» este incomplet: primește parametrii și codul devine «{code}».");
        }
        else if (catalog.ByKey.TryGetValue(EspCatalog.Key(code), out var composed))
        {
            item.Existing = composed;
            AddLocationNotes(item, composed);
        }
        if (item.Choices.Values.Any(choice => choice.NewValue is not null)) item.Notes.Add("Valoare nouă de parametru: se adaugă în lista parametrului la aplicare.");
    }

    private static void AddLocationNotes(EspPlanItem item, Product product)
    {
        if (EspCatalog.Key(product.Subcategory) != EspCatalog.Key(item.TargetSubcategory) || EspCatalog.Key(product.Category) != EspCatalog.Key(item.TargetCategory))
            item.Notes.Add($"În BlazorStoc produsul este în «{product.Category} / {product.Subcategory}»; rămâne acolo.");
    }

    internal static IEnumerable<(SubcategoryParameter Parameter, EspSlotChoice Choice)> CodeOrder(EspPlanItem item)
    {
        for (var index = 0; index < item.Slots.Count; index++)
            if (item.Choices.TryGetValue(index, out var choice) && item.Slots[index].Members.FirstOrDefault(member => member.Id == choice.ParameterId) is { } parameter)
                yield return (parameter, choice);
    }

    private static string? DisplayOf(EspPlanItem item, SubcategoryParameter parameter, EspSlotChoice choice)
    {
        string? text = choice.NewValue;
        if (choice.ValueId is { } valueId) text = parameter.Values.FirstOrDefault(value => value.Id == valueId)?.Value;
        if (text is null) return null;
        try
        {
            if (choice.ValueId is null) text = ProductParameterRules.Value(parameter.Kind, parameter.Name, text);
        }
        catch (ProductOperationException) { return null; }
        return ProductParameterRules.Display(parameter.Unit, text);
    }

    /// <summary>Two products of the export that reach the same product (or the same new code) are a problem on both: the user omits one or changes its parameters.</summary>
    public static void CheckDuplicates(IEnumerable<EspPlanItem> items)
    {
        foreach (var group in items.Where(item => !item.Omit && item.FinalCode.Length > 0).GroupBy(item => EspCatalog.Key(item.FinalCode)).Where(group => group.Count() > 1))
            foreach (var item in group)
                item.Problems.Add($"Același produs ca {string.Join(", ", group.Where(other => other != item).Select(other => $"«{other.Code}»"))}: omite unul sau schimbă parametrii.");
    }

    public static string ChoiceText(EspPlanItem item, int slotIndex)
    {
        if (!item.Choices.TryGetValue(slotIndex, out var choice)) return "";
        var parameter = item.Slots[slotIndex].Members.FirstOrDefault(member => member.Id == choice.ParameterId);
        if (parameter is null) return "";
        var text = choice.ValueId is { } id ? parameter.Values.FirstOrDefault(value => value.Id == id)?.Value : choice.NewValue;
        return text is null ? "" : $"{parameter.Name}: {ProductParameterRules.Display(parameter.Unit, text)}";
    }

    public static string Group(EspPlanItem item) => string.Create(CultureInfo.InvariantCulture, $"{item.TargetCategory} / {item.TargetSubcategory}");
}
