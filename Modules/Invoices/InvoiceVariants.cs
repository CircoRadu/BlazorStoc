namespace BlazorStoc.Services;

// Invoice pickup and required parameters (migration 37): the supplier writes the model ("DS-2CD1043") and the catalog holds one product per variant
// ("DS-2CD1043 - 2.8 mm", "DS-2CD1043 - 4 mm"). A row whose code is the model of such products is not a product yet: the user chooses the variant.
public static class InvoiceVariants
{
    // The match of a row turned into "a model with variants" when its code is the model of products with parameters, or when the product it found
    // (by code or by the link of the supplier's code) is one of them. The variant proposed is the one the supplier's link already named, else the single
    // variant whose values are written in the name of the row; the user sees it chosen and can change it.
    public static InvoiceProductMatch Apply(InvoiceProductMatch match, string name, IReadOnlyList<Product> products, IReadOnlyDictionary<int, string> baseModels)
    {
        if (baseModels.Count == 0) return match;
        var model = ModelOf(match, products, baseModels);
        if (model is null) return match;
        var variants = Variants(model, products, baseModels);
        if (variants.Count == 0) return match;
        // What the row says wins over what an earlier invoice linked: the same code of the supplier is bought in several variants.
        var byText = ProposeByText(variants, model, name);
        var linked = match.Exact is { } exact && variants.Any(item => item.Id == exact.Id) ? exact : null;
        var proposal = byText ?? linked;
        return match with
        {
            Exact = null, Alternatives = [], Variants = variants, BaseModel = model, VariantProposal = proposal?.Id,
            Note = byText is not null ? "Varianta a fost propusă după denumirea rândului." : linked is not null ? "Varianta a fost legată de factura anterioară a furnizorului." : ""
        };
    }

    private static string? ModelOf(InvoiceProductMatch match, IReadOnlyList<Product> products, IReadOnlyDictionary<int, string> baseModels)
    {
        if (match.Exact is { } exact && baseModels.TryGetValue(exact.Id, out var exactModel)) return exactModel;
        var key = InvoiceProductMatcher.Compact(match.Code);
        if (key.Length == 0) return null;
        return products.Where(product => baseModels.ContainsKey(product.Id)).Select(product => baseModels[product.Id])
            .FirstOrDefault(model => InvoiceProductMatcher.Compact(model) == key);
    }

    public static IReadOnlyList<Product> Variants(string model, IReadOnlyList<Product> products, IReadOnlyDictionary<int, string> baseModels) =>
        [.. products.Where(product => baseModels.TryGetValue(product.Id, out var other) && InvoiceProductMatcher.Compact(other) == InvoiceProductMatcher.Compact(model))
            .OrderBy(product => product.Name, StringComparer.CurrentCultureIgnoreCase)];

    // The values of a variant, as they are written in its code after the model ("2.8 mm", "alb").
    public static IReadOnlyList<string> Values(Product variant, string model) =>
        variant.Name.Length > model.Length + ProductParameterRules.Separator.Length && variant.Name.StartsWith(model + ProductParameterRules.Separator, StringComparison.Ordinal)
            ? variant.Name[(model.Length + ProductParameterRules.Separator.Length)..].Split(ProductParameterRules.Separator, StringSplitOptions.RemoveEmptyEntries)
            : [];

    // The one variant all of whose values are written in the text ("camera 2.8mm alba" has "2.8 mm"; the text "alb" is found in "alba" too, so the longest
    // set of values wins); null when no variant, or several equally good ones, fit.
    public static Product? ProposeByText(IReadOnlyList<Product> variants, string model, string text)
    {
        var compact = InvoiceProductMatcher.Compact(text);
        if (compact.Length == 0) return null;
        var fitting = variants.Select(variant => (Variant: variant, Values: Values(variant, model)))
            .Where(item => item.Values.Count > 0 && item.Values.All(value => InvoiceProductMatcher.Compact(value) is { Length: > 0 } part && compact.Contains(part, StringComparison.Ordinal)))
            .Select(item => (item.Variant, Weight: item.Values.Sum(value => InvoiceProductMatcher.Compact(value).Length))).ToList();
        if (fitting.Count == 0) return null;
        var best = fitting.Max(item => item.Weight);
        var top = fitting.Where(item => item.Weight == best).ToList();
        return top.Count == 1 ? top[0].Variant : null;
    }
}
