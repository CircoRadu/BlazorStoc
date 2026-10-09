namespace BlazorStoc.Services;

// A product of the catalog offered as the closest to a row of an invoice, with how close it is (0..1).
public sealed record ProductCandidate(Product Product, double Score);

// What the catalog says about one row of an invoice: the product with exactly the same code, else the closest ones.
// Via says where an exact product came from (see InvoiceProductMatcher.Via*); Note is a warning for the user when the sources do not agree.
public sealed record InvoiceProductMatch(string Code, Product? Exact, IReadOnlyList<ProductCandidate> Alternatives, string Via = "", string Note = "");

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

    // Spaces, tab, backslash (char 92) and the punctuation that separates the words of an invoice line.
    private static readonly char[] Separators = [' ', (char)9, ',', ';', ':', '(', ')', '[', ']', '"', '/', (char)92];

    public const string ViaCode ="cod", ViaName = "denumire", ViaSupplierCode = "furnizor";
    public const string DisagreementNote = "Codul furnizorului, codul și denumirea duc la produse diferite: alege produsul potrivit.";

    // The product of a row, from what is known in this order: the link the supplier's own code already has (supplierCodes: key of the code -> product), the code of the row
    // as the code of a product, and, when neither gives a product, the codes of the catalog written inside the name ("Intrerupator 10A cod GS-778" is GS-778).
    // When these point to different products none is taken: they are all offered (the choice is the user's, and a link made earlier is only a proposal).
    public static InvoiceProductMatch Match(string code, string name, IReadOnlyList<Product> products, IReadOnlyDictionary<string, int>? supplierCodes = null, string? supplierCode = null)
    {
        var key = Compact(code);
        var strong = new List<(Product Product, string Via)>();
        if (!string.IsNullOrWhiteSpace(supplierCode) && supplierCodes is not null && supplierCodes.TryGetValue(SupplierProductCodeRules.Key(supplierCode), out var linkedId)
            && products.FirstOrDefault(product => product.Id == linkedId) is { } linked)
            strong.Add((linked, ViaSupplierCode));
        var exact = key.Length == 0 ? null : products.FirstOrDefault(product => Compact(product.Name) == key);
        if (exact is not null && strong.All(item => item.Product.Id != exact.Id)) strong.Add((exact, ViaCode));
        if (exact is null)
            foreach (var found in CodesInName(name, products))
                if (strong.All(item => item.Product.Id != found.Id)) strong.Add((found, ViaName));
        if (key.Length == 0 && strong.Count == 0) return new InvoiceProductMatch(code, null, []);
        var taken = strong.Select(item => item.Product.Id).ToHashSet();
        var words = Words(name);
        var candidates = products.Where(product => !taken.Contains(product.Id))
            .Select(product => new ProductCandidate(product, Math.Max(CodeScore(key, Compact(product.Name)), 0.8 * Overlap(words, Words(product.Name + " " + product.Description)))))
            .Where(candidate => candidate.Score >= MinScore)
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Product.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAlternatives).ToList();
        if (strong.Count == 1) return new InvoiceProductMatch(code, strong[0].Product, candidates, strong[0].Via);
        if (strong.Count == 0) return new InvoiceProductMatch(code, null, candidates);
        // Disagreement: every product the sources point to comes first, in the order of trust, then the closest ones.
        var offered = strong.Select((item, index) => new ProductCandidate(item.Product, 1.0 - 0.01 * index)).Concat(candidates).Take(MaxAlternatives + strong.Count).ToList();
        return new InvoiceProductMatch(code, null, offered, "", DisagreementNote);
    }

    // The products of the catalog whose code (it must have a digit, to be a code and not a word) is written in the text, as a whole word or as up to three words
    // together ("GS 778" is GS-778); the longest codes first.
    public static IReadOnlyList<Product> CodesInName(string? name, IReadOnlyList<Product> products)
    {
        var tokens = TextNormalization.UniquenessKey(name).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('.', '-', '*')).Where(token => token.Length > 0).ToArray();
        if (tokens.Length == 0) return [];
        var windows = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start < tokens.Length; start++)
            for (var length = 1; length <= 3 && start + length <= tokens.Length; length++)
                windows.Add(Compact(string.Concat(tokens.Skip(start).Take(length))));
        return [.. products.Select(product => (Product: product, Code: Compact(product.Name)))
            .Where(item => item.Code.Length >= 4 && item.Code.Any(char.IsDigit) && windows.Contains(item.Code))
            .OrderByDescending(item => item.Code.Length).ThenBy(item => item.Product.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Product)];
    }

    // The code that the text itself calls a code ("... cod GS-778") or that has the look of one (letters, digits and a dash: "DS-UPS1000"); null when there is none.
    // Used to propose the name of a new product, where no product of the catalog says which word is the code.
    public static string? CodeInText(string? text)
    {
        var tokens = (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(token => token.Trim('.', ':', '*')).ToArray();
        for (var index = 0; index < tokens.Length - 1; index++)
            if (tokens[index].Equals("cod", StringComparison.OrdinalIgnoreCase) && tokens[index + 1].Length >= 2 && tokens[index + 1].Any(char.IsLetterOrDigit)) return tokens[index + 1];
        return tokens.FirstOrDefault(token => token.Length is >= 4 and <= 40 && token.Any(char.IsDigit) && token.Any(char.IsLetter) && token.Contains('-'));
    }

    // Letters and digits only, upper case, without diacritics: "DS-UPS 1000" and "ds-ups1000" are the same code.
    public static string Compact(string? value) => new([.. TextNormalization.UniquenessKey(value).Where(char.IsLetterOrDigit)]);

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
