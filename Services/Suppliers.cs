using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// How the data of a supplier got into the register. "Verified" means taken from ANAF and left unchanged.
public static class SupplierSources
{
    public const string Anaf = "A";                // taken from ANAF, unchanged
    public const string AnafEdited = "E";          // taken from ANAF, then changed by hand
    public const string ManualUnavailable = "U";   // typed by hand because ANAF could not answer (service error)
    public const string ManualNotFound = "N";      // typed by hand because ANAF does not know the tax id
    public const string Manual = "M";              // typed by hand (foreign suppliers are never looked up in ANAF)

    public static bool IsValid(string? source) => source is Anaf or AnafEdited or ManualUnavailable or ManualNotFound or Manual;
    public static bool IsVerified(string source) => source == Anaf;
    // The sources that deserve a new look at ANAF (everything that is not "verified").
    public static bool NeedsCheck(string source) => source is not Anaf and not Manual;

    public static string Label(string source) => source switch
    {
        Anaf => "Date preluate din ANAF",
        AnafEdited => "Preluate din ANAF, editate manual",
        ManualUnavailable => "Introdus manual (ANAF indisponibil)",
        ManualNotFound => "Introdus manual (CUI negăsit în ANAF)",
        _ => "Introdus manual"
    };

    public static string Icon(string source) => source == Anaf ? "✓" : source == Manual ? "✎" : "⚠";
    public static string CssClass(string source) => source == Anaf ? "ok" : "warn";
}

public static class SupplierCountries
{
    public const string Romania = "RO";

    // The prefix of the VAT identifier of each member state (Greece uses EL, Northern Ireland XI) and the name shown to the user.
    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        ("RO", "România"), ("AT", "Austria"), ("BE", "Belgia"), ("BG", "Bulgaria"), ("HR", "Croația"), ("CY", "Cipru"), ("CZ", "Cehia"),
        ("DK", "Danemarca"), ("EE", "Estonia"), ("FI", "Finlanda"), ("FR", "Franța"), ("DE", "Germania"), ("EL", "Grecia"), ("HU", "Ungaria"),
        ("IE", "Irlanda"), ("IT", "Italia"), ("LV", "Letonia"), ("LT", "Lituania"), ("LU", "Luxemburg"), ("MT", "Malta"), ("NL", "Țările de Jos"),
        ("PL", "Polonia"), ("PT", "Portugalia"), ("SK", "Slovacia"), ("SI", "Slovenia"), ("ES", "Spania"), ("SE", "Suedia"), ("XI", "Irlanda de Nord")
    ];

    public static bool IsValid(string? code) => All.Any(country => country.Code == code);
    public static string Name(string code) => All.FirstOrDefault(country => country.Code == code).Name ?? code;
}

// Identity: Country + Cui. A Romanian supplier (Country RO) is identified by the digits of its CUI (RO prefix, spaces and leading zeros do not
// matter); a supplier of another member state by its VAT identifier with the country prefix (for example DE123456789). The two forms cannot
// clash (the first has only digits). Name and the other fields are plain data: the same tax id is never two suppliers.
public sealed record Supplier(int Id, string Name, string Cui, string Country = SupplierCountries.Romania, long Version = 0,
    string Address = "", string Phone = "", string RegistryNumber = "", string PostalCode = "", string CaenCode = "",
    string Source = SupplierSources.Manual, DateTime? VerifiedUtc = null, int MovementCount = 0, int InvoiceCount = 0, int TemplateCount = 0)
{
    public bool IsExternal => Country != SupplierCountries.Romania;
    public string Identifier => IsExternal ? Cui : "CUI " + Cui;
    // What ties the supplier to the rest of the data: any of it forbids deleting it and changing its tax id.
    public bool InUse => InvoiceCount > 0 || MovementCount > 0 || TemplateCount > 0;
}

public sealed class SupplierInput : IValidatableObject
{
    public string Country { get; set; } = SupplierCountries.Romania;

    [StringLength(200, ErrorMessage = "Denumirea poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(20, ErrorMessage = "CUI-ul sau codul de TVA poate avea cel mult 20 de caractere.")]
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

    public string Source { get; set; } = SupplierSources.Manual;

    // The data was (re)read from ANAF in this edit: the journal then records "Reverificare furnizor ANAF" instead of "Modificare furnizor".
    public bool AnafRecheck { get; set; }

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public bool IsExternal => Country != SupplierCountries.Romania;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!SupplierCountries.IsValid(Country))
        {
            yield return new("Alege o țară din Uniunea Europeană.", [nameof(Country)]);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(Name)) yield return new("Completează denumirea.", [nameof(Name)]);
        if (SupplierRules.CuiProblem(Country, Cui) is { } problem) yield return new(problem, [nameof(Cui)]);
        if (Phone.Length > 0 && !BeneficiaryRules.PhonePattern.IsMatch(BeneficiaryRules.NormalizePhone(Phone)))
            yield return new("Numărul de telefon trebuie să conțină 7–15 cifre, opțional precedate de +.", [nameof(Phone)]);
        if (PostalCode.Length > 0 && !IsExternal && !BeneficiaryRules.PostalCodePattern.IsMatch(BeneficiaryRules.CompactValue(PostalCode)))
            yield return new("Codul poștal trebuie să conțină 5 sau 6 cifre.", [nameof(PostalCode)]);
        if (CaenCode.Length > 0 && !IsExternal && !BeneficiaryRules.CaenPattern.IsMatch(BeneficiaryRules.CompactValue(CaenCode)))
            yield return new("Codul CAEN trebuie să conțină 3 sau 4 cifre.", [nameof(CaenCode)]);
        if (!SupplierSources.IsValid(Source)) yield return new("Sursa datelor nu este validă.", [nameof(Source)]);
    }

    public SupplierInput Validated(bool requiresReason = false)
    {
        var external = IsExternal;
        var normalized = new SupplierInput
        {
            Country = Country,
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Cui = SupplierRules.NormalizeCui(Country, Cui),
            Address = TextNormalization.ForObjectNameOrCode(Address),
            Phone = BeneficiaryRules.NormalizePhone(Phone),
            RegistryNumber = TextNormalization.ForObjectNameOrCode(RegistryNumber).ToUpperInvariant(),
            PostalCode = BeneficiaryRules.CompactValue(PostalCode),
            CaenCode = BeneficiaryRules.CompactValue(CaenCode),
            // A foreign supplier is never looked up in ANAF: its data is always "introdus manual".
            Source = external ? SupplierSources.Manual : Source,
            AnafRecheck = !external && AnafRecheck,
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new SupplierOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new SupplierOperationException(reasonError);
        return normalized;
    }

    public static SupplierInput From(Supplier supplier) => new()
    {
        Country = supplier.Country, Name = supplier.Name, Cui = supplier.Cui, Address = supplier.Address, Phone = supplier.Phone,
        RegistryNumber = supplier.RegistryNumber, PostalCode = supplier.PostalCode, CaenCode = supplier.CaenCode, Source = supplier.Source
    };
}

public sealed class SupplierOperationException(string message) : Exception(message);

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default);
    Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default);
    Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default);
    // Administrator only; refused while the supplier has invoices, stock entries or invoice templates.
    Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default);
}

public static class SupplierSearch
{
    public static IEnumerable<Supplier> Filter(IEnumerable<Supplier> suppliers, string query, bool onlyToCheck = false)
    {
        query = query.Trim();
        var list = onlyToCheck ? suppliers.Where(supplier => SupplierSources.NeedsCheck(supplier.Source)) : suppliers;
        if (query.Length == 0) return list;
        // "RO 9178" finds the CUI 9178… (the RO prefix is not stored); "de123" finds the VAT identifier DE123….
        var key = SupplierRules.CompactKey(query);
        var digitsKey = key.StartsWith("RO", StringComparison.Ordinal) && key.Length > 2 ? key[2..] : key;
        return list.Where(supplier =>
            supplier.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (key.Length > 0 && (SupplierRules.CompactKey(supplier.Cui).Contains(key, StringComparison.Ordinal) || SupplierRules.CompactKey(supplier.Cui).Contains(digitsKey, StringComparison.Ordinal))) ||
            supplier.Address.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (BeneficiaryRules.NormalizePhone(query) is { Length: > 0 } phone && supplier.Phone.Contains(phone, StringComparison.Ordinal)));
    }
}

public static class SupplierNavigation
{
    public static string SupplierUrl(int supplierId) => $"/furnizori/{supplierId}";
}

public static partial class SupplierRules
{
    // A VAT identifier of another member state: the country prefix (2 letters) followed by 2-12 letters, digits or the few symbols some states use.
    [GeneratedRegex(@"^[A-Z]{2}[0-9A-Z+*.]{2,12}$")] private static partial Regex ForeignVatPattern();

    // Letters and digits only, upper case: the form in which two tax ids are compared.
    public static string CompactKey(string? value) =>
        new(TextNormalization.ForStorage(value).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    // The digits of a Romanian CUI/CIF: "RO 9178894", "ro9178894" and "9178894" are the same one; leading zeros do not count.
    public static string CuiDigits(string? value)
    {
        var key = CompactKey(value);
        if (key.StartsWith("RO", StringComparison.Ordinal)) key = key[2..];
        return key.All(char.IsAsciiDigit) ? key.TrimStart('0') : "";
    }

    // The value stored: digits for a Romanian supplier; the VAT identifier with the country prefix (added when missing) for another state.
    public static string NormalizeCui(string country, string? value)
    {
        if (country == SupplierCountries.Romania) return CuiDigits(value);
        var key = CompactKey(value);
        return key.StartsWith(country, StringComparison.Ordinal) ? key : country + key;
    }

    // The uniqueness key of the supplier (the stored normalized_cui).
    public static string IdentityKey(string country, string? cui) => NormalizeCui(country, cui);

    // Control digit of a Romanian CUI (weights 753217532 over the digits before the last one, right aligned): catches typing and OCR mistakes
    // without ANAF.
    public static bool HasValidCheckDigit(string digits)
    {
        if (digits.Length is < 2 or > 10 || !digits.All(char.IsAsciiDigit)) return false;
        const string weights = "753217532";
        var body = digits[..^1].PadLeft(9, '0');
        var sum = 0;
        for (var index = 0; index < 9; index++) sum += (body[index] - '0') * (weights[index] - '0');
        var check = sum * 10 % 11;
        if (check == 10) check = 0;
        return check == digits[^1] - '0';
    }

    // Why the tax id cannot be accepted, or null.
    public static string? CuiProblem(string country, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return country == SupplierCountries.Romania ? "Completează CUI-ul." : "Completează codul de TVA.";
        if (country == SupplierCountries.Romania)
        {
            var digits = CuiDigits(value);
            if (digits.Length is < 2 or > 10) return "CUI-ul trebuie să conțină 2–10 cifre, opțional precedate de RO.";
            return HasValidCheckDigit(digits) ? null : "CUI-ul nu are cifra de control corectă: verifică-l (o cifră greșită sau lipsă).";
        }
        var vat = NormalizeCui(country, value);
        return ForeignVatPattern().IsMatch(vat) ? null : $"Codul de TVA trebuie să înceapă cu {country} și să aibă 2–12 litere sau cifre după prefix.";
    }

    public static string DuplicateMessage(string? existingName) => string.IsNullOrWhiteSpace(existingName)
        ? "Există deja un furnizor cu acest CUI / cod de TVA."
        : $"Există deja un furnizor cu acest CUI / cod de TVA: «{existingName}».";

    public static string DeleteBlockedMessage(int invoices, int movements, int templates)
    {
        var parts = new List<string>();
        if (invoices > 0) parts.Add(invoices == 1 ? "o factură" : $"{invoices} facturi");
        if (movements > 0) parts.Add(movements == 1 ? "o intrare de stoc" : $"{movements} intrări de stoc");
        if (templates > 0) parts.Add(templates == 1 ? "un șablon de factură" : $"{templates} șabloane de factură");
        return $"Furnizorul are {string.Join(", ", parts)} legate și nu poate fi șters.";
    }

    public const string CuiLockedMessage = "Furnizorul are facturi, intrări de stoc sau șabloane legate: țara și CUI-ul nu mai pot fi schimbate. Poți corecta celelalte date.";
    public const string StaleMessage = "Furnizorul a fost modificat sau șters între timp. Actualizează lista și reia operația.";

    public static void CheckCurrent(Supplier? current, Supplier original)
    {
        if (current is null || current.Version != original.Version) throw new SupplierOperationException(StaleMessage);
    }

    public static string DisplayTime(DateTime? utc) => utc is { } value ? value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "—";

    public static IReadOnlyList<AuditChange> Changes(Supplier before, Supplier after) =>
    [
        new("Țara", SupplierCountries.Name(before.Country), SupplierCountries.Name(after.Country)), new("Denumire", before.Name, after.Name),
        new("CUI / cod TVA", before.Cui, after.Cui), new("Adresă", before.Address, after.Address), new("Telefon", before.Phone, after.Phone),
        new("Nr. Registrul Comerțului", before.RegistryNumber, after.RegistryNumber), new("Cod poștal", before.PostalCode, after.PostalCode),
        new("Cod CAEN", before.CaenCode, after.CaenCode), new("Sursa datelor", SupplierSources.Label(before.Source), SupplierSources.Label(after.Source))
    ];

    public static string Identification(Supplier value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Țara", SupplierCountries.Name(value.Country)), ("Denumire", value.Name), ("CUI / cod TVA", value.Cui), ("Adresă", value.Address),
        ("Telefon", value.Phone), ("Nr. Registrul Comerțului", value.RegistryNumber), ("Cod poștal", value.PostalCode),
        ("Cod CAEN", value.CaenCode), ("Sursa datelor", SupplierSources.Label(value.Source))
    }.Where(item => item.Value.Length > 0).ToArray());

    public static string Target(Supplier value) => $"#{value.Id} · {value.Name}";

    // The source after an edit (Romanian suppliers), from what the form knows about ANAF:
    //  - a snapshot of ANAF's data for the CUI now in the form: unchanged -> verified, changed by hand -> edited;
    //  - otherwise the failure of the last attempt (service error / CUI unknown to ANAF);
    //  - otherwise what the record already was (only while its CUI did not change), else plain manual.
    public static string ResolveSource(bool external, bool hasSnapshotForThisCui, bool snapshotUnchanged, AnafLookupOutcome? failure, string? original)
    {
        if (external) return SupplierSources.Manual;
        if (hasSnapshotForThisCui) return snapshotUnchanged ? SupplierSources.Anaf : SupplierSources.AnafEdited;
        if (failure == AnafLookupOutcome.Unavailable) return SupplierSources.ManualUnavailable;
        if (failure == AnafLookupOutcome.NotFound) return SupplierSources.ManualNotFound;
        return SupplierSources.IsValid(original) && original != SupplierSources.Anaf ? original! : SupplierSources.Manual;
    }
}
