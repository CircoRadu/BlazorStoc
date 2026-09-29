using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BlazorStoc.Services;

// A beneficiary's work point. The main one is not stored: it is derived from the beneficiary's own address and phone
// (taken from ANAF or typed at creation), so it changes only when the beneficiary is edited by hand. Only the additional
// work points are rows (beneficiary_work_points); those can be edited and deleted.
public sealed record WorkPoint(int Id, int BeneficiaryId, string Name, string Address, string Phone = "",
    string ContactPerson = "", long Version = 0, bool IsPrimary = false);

public sealed class WorkPointInput : IValidatableObject
{
    [StringLength(200, ErrorMessage = "Numele punctului de lucru poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(300, ErrorMessage = "Adresa poate avea cel mult 300 de caractere.")]
    public string Address { get; set; } = "";

    [StringLength(20, ErrorMessage = "Numărul de telefon poate avea cel mult 20 de caractere.")]
    public string Phone { get; set; } = "";

    [StringLength(200, ErrorMessage = "Numele persoanei de contact poate avea cel mult 200 de caractere.")]
    public string ContactPerson { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new("Completează numele punctului de lucru.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address)) yield return new("Completează adresa.", [nameof(Address)]);
        if (!string.IsNullOrWhiteSpace(Phone) && !BeneficiaryRules.PhonePattern.IsMatch(BeneficiaryRules.NormalizePhone(Phone)))
            yield return new("Numărul de telefon trebuie să conțină 7–15 cifre, opțional precedate de +.", [nameof(Phone)]);
    }

    public WorkPointInput Validated()
    {
        var normalized = new WorkPointInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Address = TextNormalization.ForObjectNameOrCode(Address),
            Phone = BeneficiaryRules.NormalizePhone(Phone),
            ContactPerson = TextNormalization.ForObjectNameOrCode(ContactPerson)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new WorkPointOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        return normalized;
    }

    public static WorkPointInput From(WorkPoint value) => new()
    {
        Name = value.Name, Address = value.Address, Phone = value.Phone, ContactPerson = value.ContactPerson
    };
}

public sealed class WorkPointOperationException(string message) : Exception(message);

public interface IWorkPointRepository
{
    /// <summary>The additional work points of a beneficiary (the main one is derived, see <see cref="WorkPointRules.Primary"/>).</summary>
    Task<IReadOnlyList<WorkPoint>> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<WorkPoint> CreateAsync(int beneficiaryId, WorkPointInput input, CancellationToken cancellationToken = default);
    Task<WorkPoint> UpdateAsync(WorkPoint original, WorkPointInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(WorkPoint original, CancellationToken cancellationToken = default);
}

public static class AddressNormalization
{
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["STR"] = "STRADA", ["BD"] = "BULEVARDUL", ["BDUL"] = "BULEVARDUL", ["BLVD"] = "BULEVARDUL", ["BULEVARD"] = "BULEVARDUL",
        ["AL"] = "ALEEA", ["SOS"] = "SOSEA", ["BL"] = "BLOC", ["SC"] = "SCARA", ["AP"] = "APARTAMENT", ["ET"] = "ETAJ",
        ["JUD"] = "JUDETUL", ["MUN"] = "MUNICIPIUL", ["ORS"] = "ORASUL", ["COM"] = "COMUNA", ["SECT"] = "SECTOR"
    };

    // Words that carry no address information ("nr. 5" and "5" are the same house number).
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal) { "NR", "NUMAR", "NUMARUL" };

    /// <summary>
    /// Comparison key of an address: no diacritics, upper case, punctuation and repeated spaces ignored, common
    /// abbreviations expanded ("Str. Florilor nr. 5" and "strada FLORILOR 5" give the same key).
    /// </summary>
    public static string Key(string? address)
    {
        var text = TextNormalization.ForStorage(address).ToUpperInvariant();
        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text) cleaned.Append(char.IsLetterOrDigit(character) ? character : ' ');
        var tokens = cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !Noise.Contains(token))
            .Select(token => Abbreviations.TryGetValue(token, out var full) ? full : token);
        return string.Join(' ', tokens);
    }

    public static bool Same(string? left, string? right)
    {
        var key = Key(left);
        return key.Length > 0 && string.Equals(key, Key(right), StringComparison.Ordinal);
    }
}

public static class WorkPointRules
{
    public const string PrimaryName = "Punct de lucru principal";
    // Edits need no reason from the user; the audit trail receives this generated one. Deleting is one step with a fixed reason.
    public const string GeneratedEditReason = "Editare punct de lucru";
    public const string DeleteReason = "Punctul de lucru nu va mai fi folosit";

    public static WorkPoint Primary(Beneficiary beneficiary) =>
        new(0, beneficiary.Id, PrimaryName, beneficiary.Address, beneficiary.Phone, IsPrimary: true);

    public static WorkPointOperationException DuplicateAddress(string existingName) =>
        new($"Există deja un punct de lucru cu această adresă: «{existingName}».");

    public static BeneficiaryOperationException BeneficiaryAddressTaken(string workPointName) =>
        new($"Adresa coincide cu cea a punctului de lucru «{workPointName}». Modifică sau șterge mai întâi acel punct de lucru.");

    public static WorkPointOperationException Changed() =>
        new("Punctul de lucru s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static string Target(int beneficiaryId, string beneficiaryName, string workPointName) =>
        $"#{beneficiaryId} · {beneficiaryName} · punct de lucru «{workPointName}»";

    public static string Identification(WorkPoint value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Nume", value.Name), ("Adresă", value.Address), ("Telefon", value.Phone), ("Persoană de contact", value.ContactPerson)
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(WorkPoint before, WorkPoint after) =>
    [
        new("Nume", before.Name, after.Name), new("Adresă", before.Address, after.Address),
        new("Telefon", before.Phone, after.Phone), new("Persoană de contact", before.ContactPerson, after.ContactPerson)
    ];

    public static void CheckCurrent(WorkPoint? current, WorkPoint original)
    {
        if (current is null || current != original) throw Changed();
    }
}
