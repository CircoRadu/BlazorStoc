using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

public static class BeneficiaryKinds
{
    public const string Individual = "PF";
    public const string Legal = "PJ";
    public static bool IsValid(string? kind) => kind is Individual or Legal;
}

// Kind PF: Name is the full name (the identity), Cui and the company fields are empty. Kind PJ: Cui is the identity and
// Name is the company name. AnafVerified records that the company data was taken from ANAF and left unchanged.
public sealed record Beneficiary(int Id, string Name, string Cui, long Version = 0, string Kind = BeneficiaryKinds.Legal,
    string Address = "", string Phone = "", string RegistryNumber = "", string PostalCode = "", string CaenCode = "",
    bool AnafVerified = false)
{
    public bool IsIndividual => Kind == BeneficiaryKinds.Individual;
    public string Identifier => IsIndividual ? "Persoană fizică" : Cui;
}

public sealed class BeneficiaryInput : IValidatableObject
{
    public string Kind { get; set; } = BeneficiaryKinds.Legal;

    [StringLength(200, ErrorMessage = "Numele poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(12, ErrorMessage = "CUI-ul poate avea cel mult 12 caractere.")]
    public string Cui { get; set; } = "";

    [StringLength(300, ErrorMessage = "Adresa poate avea cel mult 300 de caractere.")]
    public string Address { get; set; } = "";

    [StringLength(20, ErrorMessage = "Numărul de telefon poate avea cel mult 20 de caractere.")]
    public string Phone { get; set; } = "";

    [StringLength(40, ErrorMessage = "Numărul din Registrul Comerțului poate avea cel mult 40 de caractere.")]
    public string RegistryNumber { get; set; } = "";

    [StringLength(10, ErrorMessage = "Codul poștal poate avea cel mult 10 caractere.")]
    public string PostalCode { get; set; } = "";

    [StringLength(4, ErrorMessage = "Codul CAEN poate avea cel mult 4 caractere.")]
    public string CaenCode { get; set; } = "";

    public bool AnafVerified { get; set; }

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public bool IsIndividual => Kind == BeneficiaryKinds.Individual;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!BeneficiaryKinds.IsValid(Kind))
        {
            yield return new("Alege persoană fizică sau persoană juridică.", [nameof(Kind)]);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(Name))
            yield return new(IsIndividual ? "Completează numele complet." : "Completează denumirea.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address)) yield return new("Completează adresa.", [nameof(Address)]);
        if (string.IsNullOrWhiteSpace(Phone)) yield return new("Completează numărul de telefon.", [nameof(Phone)]);
        else if (!BeneficiaryRules.PhonePattern.IsMatch(BeneficiaryRules.NormalizePhone(Phone)))
            yield return new("Numărul de telefon trebuie să conțină 7–15 cifre, opțional precedate de +.", [nameof(Phone)]);
        if (IsIndividual) yield break;
        if (string.IsNullOrWhiteSpace(Cui)) yield return new("Completează CUI-ul.", [nameof(Cui)]);
        else if (!BeneficiaryRules.CuiPattern.IsMatch(BeneficiaryRules.CompactValue(Cui)))
            yield return new("CUI-ul trebuie să conțină 2–10 cifre, opțional precedate de RO.", [nameof(Cui)]);
        if (PostalCode.Length > 0 && !BeneficiaryRules.PostalCodePattern.IsMatch(BeneficiaryRules.CompactValue(PostalCode)))
            yield return new("Codul poștal trebuie să conțină 5 sau 6 cifre.", [nameof(PostalCode)]);
        if (CaenCode.Length > 0 && !BeneficiaryRules.CaenPattern.IsMatch(BeneficiaryRules.CompactValue(CaenCode)))
            yield return new("Codul CAEN trebuie să conțină 3 sau 4 cifre.", [nameof(CaenCode)]);
    }

    public BeneficiaryInput Validated(bool requiresReason = false)
    {
        var individual = IsIndividual;
        var normalized = new BeneficiaryInput
        {
            Kind = Kind,
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Cui = individual ? "" : BeneficiaryRules.CompactValue(Cui),
            Address = TextNormalization.ForObjectNameOrCode(Address),
            Phone = BeneficiaryRules.NormalizePhone(Phone),
            RegistryNumber = individual ? "" : TextNormalization.ForObjectNameOrCode(RegistryNumber).ToUpperInvariant(),
            PostalCode = individual ? "" : BeneficiaryRules.CompactValue(PostalCode),
            CaenCode = individual ? "" : BeneficiaryRules.CompactValue(CaenCode),
            AnafVerified = !individual && AnafVerified,
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new BeneficiaryOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new BeneficiaryOperationException(reasonError);
        return normalized;
    }

    public static BeneficiaryInput From(Beneficiary beneficiary) => new()
    {
        Kind = beneficiary.Kind, Name = beneficiary.Name, Cui = beneficiary.Cui, Address = beneficiary.Address,
        Phone = beneficiary.Phone, RegistryNumber = beneficiary.RegistryNumber, PostalCode = beneficiary.PostalCode,
        CaenCode = beneficiary.CaenCode, AnafVerified = beneficiary.AnafVerified
    };
}

public sealed class BeneficiaryOperationException(string message) : Exception(message);

public interface IBeneficiaryRepository
{
    Task<IReadOnlyList<Beneficiary>> GetBeneficiariesAsync(CancellationToken cancellationToken = default);
    Task<Beneficiary> CreateAsync(BeneficiaryInput input, CancellationToken cancellationToken = default);
    Task<Beneficiary> UpdateAsync(Beneficiary original, BeneficiaryInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Beneficiary original, string reason, CancellationToken cancellationToken = default);
}

public static class BeneficiarySearch
{
    public static IEnumerable<Beneficiary> Filter(IEnumerable<Beneficiary> beneficiaries, string query)
    {
        query = query.Trim();
        return query.Length == 0 ? beneficiaries : beneficiaries.Where(beneficiary =>
            beneficiary.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            beneficiary.Cui.Contains(query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
            beneficiary.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (BeneficiaryRules.NormalizePhone(query) is { Length: > 0 } phone && beneficiary.Phone.Contains(phone, StringComparison.Ordinal)));
    }
}

public static class BeneficiaryRules
{
    public static readonly Regex CuiPattern = new(@"^(?i:RO)?[0-9]{2,10}$", RegexOptions.Compiled);
    public static readonly Regex PhonePattern = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled);
    public static readonly Regex PostalCodePattern = new(@"^[0-9]{5,6}$", RegexOptions.Compiled);
    public static readonly Regex CaenPattern = new(@"^[0-9]{3,4}$", RegexOptions.Compiled);

    /// <summary>Removes every whitespace character (CUI, postal code, CAEN).</summary>
    public static string CompactValue(string? value) => string.Concat(TextNormalization.ForObjectNameOrCode(value).Where(c => !char.IsWhiteSpace(c)));

    /// <summary>Keeps digits and a leading +: "0744 123.456" and "0744-123-456" both become "0744123456".</summary>
    // ANAF often returns only the local part of a phone number ("260681" for "+40254260681", without the country and the area code): a full number that ends
    // with what ANAF returned is the same number, not a different value typed by hand.
    public static bool PhoneCoveredBy(string? anafPhone, string? ownPhone)
    {
        var anaf = new string((anafPhone ?? "").Where(char.IsDigit).ToArray());
        var own = new string((ownPhone ?? "").Where(char.IsDigit).ToArray());
        return anaf.Length >= 5 && own.EndsWith(anaf, StringComparison.Ordinal);
    }

    public static string NormalizePhone(string? value)
    {
        var text = CompactValue(value);
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return text.StartsWith('+') ? "+" + digits : digits;
    }

    // The stored uniqueness key: the CUI digits for a legal person ("RO123", "ro 123" and "123" are the same one, as for suppliers), the full
    // name (prefixed, so it can never equal a CUI) for an individual.
    public static string IdentityKey(BeneficiaryInput value) => value.IsIndividual
        ? "PF:" + TextNormalization.UniquenessKey(value.Name)
        : SupplierRules.CuiDigits(value.Cui) is { Length: > 0 } digits ? digits : TextNormalization.UniquenessKey(value.Cui);

    public static BeneficiaryOperationException DuplicateIdentity(BeneficiaryInput value, string? existingName) => new(value.IsIndividual
        ? (string.IsNullOrWhiteSpace(existingName) ? "Există deja o persoană fizică cu acest nume." : $"Există deja o persoană fizică cu acest nume: «{existingName}».")
        : DuplicateCuiMessage(existingName));

    public static string DuplicateNameMessage(string existingName, string existingCui) => string.IsNullOrWhiteSpace(existingCui)
        ? $"Beneficiarul «{existingName}» există deja."
        : $"Beneficiarul «{existingName}» există deja și are CUI «{existingCui}».";

    public static IReadOnlyList<AuditChange> Changes(Beneficiary before, Beneficiary after) =>
    [
        new("Tip", KindLabel(before.Kind), KindLabel(after.Kind)), new("Denumire", before.Name, after.Name), new("CUI", before.Cui, after.Cui),
        new("Adresă", before.Address, after.Address), new("Telefon", before.Phone, after.Phone),
        new("Nr. Registrul Comerțului", before.RegistryNumber, after.RegistryNumber), new("Cod poștal", before.PostalCode, after.PostalCode),
        new("Cod CAEN", before.CaenCode, after.CaenCode), new("Date ANAF", YesNo(before.AnafVerified), YesNo(after.AnafVerified))
    ];

    public static string Identification(Beneficiary value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Tip", KindLabel(value.Kind)), ("Denumire", value.Name), ("CUI", value.Cui), ("Adresă", value.Address), ("Telefon", value.Phone),
        ("Nr. Registrul Comerțului", value.RegistryNumber), ("Cod poștal", value.PostalCode), ("Cod CAEN", value.CaenCode),
        ("Date ANAF", YesNo(value.AnafVerified))
    }.Where(item => item.Value.Length > 0).ToArray());

    public static string KindLabel(string kind) => kind == BeneficiaryKinds.Individual ? "Persoană fizică" : "Persoană juridică";
    private static string YesNo(bool value) => value ? "preluate din ANAF" : "introduse manual";

    public static void CheckCurrent(Beneficiary? current, Beneficiary original)
    {
        if (current is null || current != original)
            throw new BeneficiaryOperationException("Beneficiarul a fost modificat sau șters între timp. Actualizează lista și reia operația.");
    }

    // Single wording for every storage mode; the name is the one stored in the database, not the one just typed.
    public static string DuplicateCuiMessage(string? existingName) =>
        string.IsNullOrWhiteSpace(existingName)
            ? "Există deja un beneficiar cu acest CUI."
            : $"Există deja un beneficiar cu acest CUI: «{existingName}».";

    public static void CheckDelete(bool hasStockMovements)
    {
        if (hasStockMovements)
            throw new BeneficiaryOperationException("Beneficiarul are mișcări de stoc asociate și nu poate fi șters.");
    }

    // Live projects must be moved to another beneficiary or archived first; archived projects do not block deletion.
    public static void CheckNoLiveProjects(int liveProjectCount)
    {
        if (liveProjectCount > 0)
            throw new BeneficiaryOperationException(liveProjectCount == 1
                ? "Beneficiarul are un proiect asociat și nu poate fi șters. Mută sau arhivează mai întâi proiectul."
                : $"Beneficiarul are {liveProjectCount} proiecte asociate și nu poate fi șters. Mută sau arhivează mai întâi proiectele.");
    }
}

