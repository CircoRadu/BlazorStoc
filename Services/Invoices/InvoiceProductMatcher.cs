namespace BlazorStoc.Services;

// A product of the catalog offered as the closest to a row of an invoice, with how close it is (0..1).
public sealed record ProductCandidate(Product Product, double Score);

// What the catalog says about one row of an invoice: the product with exactly the same code, else the closest ones.
public sealed record InvoiceProductMatch(string Code, Product? Exact, IReadOnlyList<ProductCandidate> Alternatives);

// Invoice pickup, step 2: finds the products of the catalog that a row of the invoice stands for. A product is known by its code (Product.Name,
// "cod produs"); the code of a row is its code cell, or the start of its name ("DS-UPS1000 - Sursa neintreruptibila ..." is DS-UPS1000).
public static class InvoiceProductMatcher
{
    public const int MaxAlternatives = 5;
    public const double MinScore = 0.45;

    // The code a row stands for: its code cell when the template has one, else what the name has before " - " (or its first word).
    public static string CodeOf(string? codeCell, string? nameCell)
    {
        if (!string.IsNullOrWhiteSpace(codeCell)) return codeCell.Trim();
        var name = (nameCell ?? "").Trim();
        var cut = name.IndexOf(" - ", StringComparison.Ordinal);
        if (cut > 0) return name[..cut].Trim();
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space].Trim() : name;
    }

    public static InvoiceProductMatch Match(string code, string name, IReadOnlyList<Product> products)
    {
        var key = Compact(code);
        if (key.Length == 0) return new InvoiceProductMatch(code, null, []);
        var exact = products.FirstOrDefault(product => Compact(product.Name) == key);
        var words = Words(name);
        var candidates = products.Where(product => product != exact)
            .Select(product => new ProductCandidate(product, Math.Max(CodeScore(key, Compact(product.Name)), 0.8 * Overlap(words, Words(product.Name + " " + product.Description)))))
            .Where(candidate => candidate.Score >= MinScore)
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Product.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAlternatives).ToList();
        return new InvoiceProductMatch(code, exact, candidates);
    }

    // Letters and digits only, upper case, without diacritics: "DS-UPS 1000" and "ds-ups1000" are the same code.
    internal static string Compact(string? value) => new([.. TextNormalization.UniquenessKey(value).Where(char.IsLetterOrDigit)]);

    private static double CodeScore(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var longest = Math.Max(left.Length, right.Length);
        var shortest = Math.Min(left.Length, right.Length);
        // One code inside the other (a suffix, a variant): close, the more so the more they share.
        var contained = shortest >= 4 && (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal)) ? 0.85 + 0.1 * shortest / longest : 0;
        return Math.Max(contained, 1.0 - (double)Distance(left, right) / longest);
    }

    private static int Distance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

    private static HashSet<string> Words(string text) =>
        [.. System.Text.RegularExpressions.Regex.Matches(TextNormalization.UniquenessKey(text), @"[\p{L}\p{N}]{3,}").Select(match => match.Value)];

    // Share of the words of the shorter text found in the other.
    private static double Overlap(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        return (double)left.Count(right.Contains) / Math.Min(left.Count, right.Count);
    }
}
