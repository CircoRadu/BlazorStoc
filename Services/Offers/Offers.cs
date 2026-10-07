using System.Globalization;

namespace BlazorStoc.Services;

// Module Oferte: the offers taken over from a devize-oferta (.xlsx) read with a template. An offer belongs to a beneficiary and a project (and, through
// the category of the offer, to a component of the project). The same offer number again is a REVISION (1, 2, 3 ...) of the same offer, with the
// differences shown; the exits already made for the project are never linked automatically. The lines in pieces may be tied to a product of the catalog
// (the confirmed ties are remembered for the next offers); the others are "de achizitionat" (no product yet) or "in afara stocului" (meters, hours).

public sealed record OfferRecord(int Id, string Number, int Revision, string Title, string Category, int BeneficiaryId, string BeneficiaryName,
    int ProjectId, string ProjectName, int? SystemTypeId, string? SystemTypeName, int? TemplateId, string FileName, string CreatedBy, string CreatedUtc);

public sealed record OfferLineRecord(int Id, int OfferId, int Order, string Section, string Number, string ProductType, string Name, string Unit,
    decimal Quantity, bool InStock, int? ProductId, string? ProductName);

public sealed class OfferImportLine
{
    public string Section { get; set; } = "";
    public string Number { get; set; } = "";
    public string ProductType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public bool InStock { get; set; }
    // The product of the catalog the line is tied to (only a line in pieces); none = "de achizitionat".
    public int? ProductId { get; set; }
}

public sealed class OfferImportRequest
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string FileName { get; set; } = "";
    public int? TemplateId { get; set; }
    public int BeneficiaryId { get; set; }
    // The text the offer has for the beneficiary: remembered as an alternative name when it differs from the name chosen (RememberBeneficiaryAlias).
    public string BeneficiaryText { get; set; } = "";
    public bool RememberBeneficiaryAlias { get; set; }
    // An existing project, or the name of a project to create (title of the offer by default).
    public int? ProjectId { get; set; }
    public string NewProjectName { get; set; } = "";
    // The component (type of system) of the project the offer is for; added to the project, or reactivated when it was taken out.
    public int? SystemTypeId { get; set; }
    public List<OfferImportLine> Lines { get; set; } = [];
}

// Differences from the previous revision of the same offer, lines matched by section and name.
public sealed record OfferDiff(int PreviousRevision, int Added, int Removed, int QuantityChanged, int Unchanged, IReadOnlyList<string> Details);
public sealed record OfferImportResult(OfferRecord Offer, bool IsRevision, OfferDiff? Diff, int LinkedLines, int ToPurchaseLines, int OutOfStockLines, int ProjectId);

public class OfferException(string message) : Exception(message);

public static class OfferMessages
{
    public const string NumberRequired = "Completează numărul ofertei.";
    public const string BeneficiaryRequired = "Alege beneficiarul ofertei.";
    public const string ProjectRequired = "Alege proiectul sau completează denumirea proiectului nou.";
    public const string NoLines = "Oferta nu are nicio linie de importat. Verifică șablonul și secțiunile importate.";
    public const string BeneficiaryMissing = "Beneficiarul ales nu mai există.";
    public const string ProjectMissing = "Proiectul ales nu mai există sau nu aparține beneficiarului.";
    public static string LineProductMissing(int line) => $"Linia {line}: produsul ales nu mai există.";
}

public interface IOfferRepository
{
    // The latest revision of the offer with this number (any beneficiary); null when it was never taken over.
    Task<OfferRecord?> GetLatestAsync(string number, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfferLineRecord>> GetLinesAsync(int offerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfferRecord>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default);
    // The differences the lines would make against the latest revision of the number (null when there is none).
    Task<OfferDiff?> DiffAsync(string number, IReadOnlyList<OfferImportLine> lines, CancellationToken cancellationToken = default);
    // Name key -> product the line was tied to in an earlier offer (confirmed ties are remembered).
    Task<IReadOnlyDictionary<string, int>> GetRememberedMatchesAsync(CancellationToken cancellationToken = default);
    // The beneficiary an alternative name (from an earlier offer) belongs to.
    Task<int?> FindBeneficiaryByAliasAsync(string text, CancellationToken cancellationToken = default);
    // Takes the offer over (revision when the number exists); the project and the component are created or added before, in the same call.
    Task<OfferImportResult> ImportAsync(OfferImportRequest request, CancellationToken cancellationToken = default);
}

public static class OfferLineRules
{
    public const int KeyLength = 190;
    // The identity of a line for revisions and remembered ties: its text without case, diacritics and punctuation.
    public static string LineKey(string name)
    {
        var key = OfferRules.Key(name);
        return key.Length <= KeyLength ? key : key[..KeyLength];
    }
}

// Ranks the products of the catalog for the text of an offer line (searches the whole text: model codes weigh more than ordinary words).
public static class OfferProductMatcher
{
    public const double MinScore = 0.5;
    public const double AutoScore = 0.9;
    public const int MaxCandidates = 5;

    private static readonly char[] Separators = [' ', (char)9, (char)10, (char)13, ',', ';', ':', '(', ')', '/', '"', '\''];

    private static IEnumerable<string> Tokens(string? text) =>
        (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(word => OfferRules.Key(word)).Where(word => word.Length >= 2).Distinct();

    // The share of the product's own words (model codes count triple) found in the text of the line.
    public static IReadOnlyList<ProductCandidate> Rank(string lineText, IReadOnlyList<Product> products)
    {
        var lineKey = OfferRules.Key(lineText);
        var lineTokens = Tokens(lineText).ToHashSet();
        var result = new List<ProductCandidate>();
        foreach (var product in products)
        {
            var words = Tokens(product.Name).ToList();
            if (words.Count == 0) continue;
            var total = 0.0; var found = 0.0;
            foreach (var word in words)
            {
                var weight = word.Any(char.IsDigit) && word.Any(char.IsLetter) || word.Count(char.IsDigit) >= 3 ? 3.0 : 1.0;
                total += weight;
                if (lineTokens.Contains(word) || (word.Length >= 5 && lineKey.Contains(word, StringComparison.Ordinal))) found += weight;
            }
            var score = total == 0 ? 0 : found / total;
            // The whole name of the product inside the text of the line is the best sign.
            if (OfferRules.Key(product.Name).Length >= 5 && lineKey.Contains(OfferRules.Key(product.Name), StringComparison.Ordinal)) score = Math.Max(score, 1.0);
            if (score >= MinScore) result.Add(new ProductCandidate(product, score));
        }
        return [.. result.OrderByDescending(item => item.Score).ThenBy(item => item.Product.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxCandidates)];
    }
}

public sealed record BeneficiaryCandidate(Beneficiary Beneficiary, double Score);

// Ranks the existing beneficiaries for the name an offer gives (legal forms and filler words are ignored: "SC Telesystem SRL" = "Telesystem").
public static class OfferBeneficiaryMatcher
{
    public const double MinScore = 0.34;
    public const int MaxCandidates = 5;
    private static readonly HashSet<string> Filler = ["sc", "srl", "sa", "pfa", "ii", "if", "ra", "snc", "scs", "de", "si", "al", "a"];
    private static readonly char[] Separators = [' ', (char)9, (char)10, (char)13, ',', ';', ':', '(', ')', '/', '.', '-'];

    private static HashSet<string> Tokens(string? text) =>
        [.. (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(word => OfferRules.Key(word)).Where(word => word.Length > 0 && !Filler.Contains(word))];

    public static IReadOnlyList<BeneficiaryCandidate> Rank(string text, IReadOnlyList<Beneficiary> beneficiaries)
    {
        var wanted = Tokens(text);
        var wantedKey = string.Concat(wanted.OrderBy(word => word, StringComparer.Ordinal));
        if (wanted.Count == 0) return [];
        var result = new List<BeneficiaryCandidate>();
        foreach (var beneficiary in beneficiaries)
        {
            var tokens = Tokens(beneficiary.Name);
            if (tokens.Count == 0) continue;
            var common = tokens.Count(wanted.Contains);
            var score = (double)common / Math.Max(tokens.Count, wanted.Count);
            var key = string.Concat(tokens.OrderBy(word => word, StringComparer.Ordinal));
            if (key == wantedKey) score = 1.0;
            else if (common > 0 && (tokens.IsSubsetOf(wanted) || wanted.IsSubsetOf(tokens))) score = Math.Max(score, 0.8);
            if (score >= MinScore) result.Add(new BeneficiaryCandidate(beneficiary, score));
        }
        return [.. result.OrderByDescending(item => item.Score).ThenBy(item => item.Beneficiary.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxCandidates)];
    }
}

public static class OfferDiffRules
{
    // Lines matched by section and text; a line present in both with another quantity is changed.
    public static OfferDiff Compare(int previousRevision, IReadOnlyList<OfferLineRecord> previous, IReadOnlyList<OfferImportLine> current)
    {
        string Key(string section, string name) => OfferRules.Key(section) + "|" + OfferLineRules.LineKey(name);
        var before = previous.GroupBy(line => Key(line.Section, line.Name)).ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var after = current.GroupBy(line => Key(line.Section, line.Name)).ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var details = new List<string>();
        int added = 0, removed = 0, changed = 0, same = 0;
        foreach (var (key, quantity) in after)
        {
            var name = current.First(line => Key(line.Section, line.Name) == key).Name.Split('\n')[0];
            if (!before.TryGetValue(key, out var old)) { added++; details.Add($"+ {name} ({Format(quantity)})"); }
            else if (old != quantity) { changed++; details.Add($"~ {name}: {Format(old)} → {Format(quantity)}"); }
            else same++;
        }
        foreach (var (key, quantity) in before)
            if (!after.ContainsKey(key)) { removed++; details.Add($"− {previous.First(line => Key(line.Section, line.Name) == key).Name.Split('\n')[0]} ({Format(quantity)})"); }
        return new OfferDiff(previousRevision, added, removed, changed, same, details);
    }

    public static string Format(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);
}
