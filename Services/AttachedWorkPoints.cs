using System.ComponentModel.DataAnnotations;

namespace BlazorStoc.Services;

// An attached work point ("Punct de lucru arondat"): a component of a work point, e.g. the primary-school building and the kindergarten of a
// school. It has a name and at least an address or GPS coordinates; the phone and the contact person are optional. Its address is never taken
// from the parent work point: what is not filled stays empty. Rows of work_point_attached (migration 88).
public sealed record AttachedWorkPoint(int Id, int WorkPointId, string Name, string Address = "", string Phone = "", string ContactPerson = "",
    long Version = 0, decimal? Latitude = null, decimal? Longitude = null)
{
    public bool HasCoordinates => Latitude is not null && Longitude is not null && !(Latitude == 0 && Longitude == 0);
    public bool HasAddress => Address.Length > 0;
}

/// <summary>An attached work point with the names of its parents (the map pin and the search result show them).</summary>
public sealed record AttachedWorkPointPlace(AttachedWorkPoint Point, int BeneficiaryId, string BeneficiaryName, string WorkPointName)
{
    public string Title => $"{BeneficiaryName} · {WorkPointName} · {Point.Name}";
}

public sealed class AttachedWorkPointInput : IValidatableObject
{
    [StringLength(200, ErrorMessage = "Numele punctului arondat poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(300, ErrorMessage = "Adresa poate avea cel mult 300 de caractere.")]
    public string Address { get; set; } = "";

    [StringLength(20, ErrorMessage = "Numărul de telefon poate avea cel mult 20 de caractere.")]
    public string Phone { get; set; } = "";

    [StringLength(200, ErrorMessage = "Numele persoanei de contact poate avea cel mult 200 de caractere.")]
    public string ContactPerson { get; set; } = "";

    // The coordinates are present when UseCoordinates is on ("latitude, longitude" as pasted from a map).
    public bool UseCoordinates { get; set; }

    [StringLength(60, ErrorMessage = "Coordonatele sunt prea lungi.")]
    public string CoordinatesText { get; set; } = "";

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new("Completează numele punctului arondat.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address) && !UseCoordinates) yield return new(AttachedWorkPointRules.NeedsLocationMessage, [nameof(Address)]);
        if (!string.IsNullOrWhiteSpace(Phone) && !BeneficiaryRules.PhonePattern.IsMatch(BeneficiaryRules.NormalizePhone(Phone)))
            yield return new("Numărul de telefon trebuie să conțină 7–15 cifre, opțional precedate de +.", [nameof(Phone)]);
        if (UseCoordinates && !WorkPointCoordinates.TryParse(CoordinatesText, out _, out _, out var coordinatesError))
            yield return new(coordinatesError!, [nameof(CoordinatesText)]);
    }

    public AttachedWorkPointInput Validated()
    {
        decimal? latitude = null, longitude = null;
        if (UseCoordinates && WorkPointCoordinates.TryParse(CoordinatesText, out var parsedLatitude, out var parsedLongitude, out _))
            (latitude, longitude) = (parsedLatitude, parsedLongitude);
        var normalized = new AttachedWorkPointInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Address = TextNormalization.ForObjectNameOrCode(Address),
            Phone = BeneficiaryRules.NormalizePhone(Phone),
            ContactPerson = TextNormalization.ForObjectNameOrCode(ContactPerson),
            UseCoordinates = latitude is not null,
            CoordinatesText = latitude is null ? "" : WorkPointCoordinates.Format(latitude, longitude),
            Latitude = latitude, Longitude = longitude
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new WorkPointOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        return normalized;
    }

    public static AttachedWorkPointInput From(AttachedWorkPoint value) => new()
    {
        Name = value.Name, Address = value.Address, Phone = value.Phone, ContactPerson = value.ContactPerson,
        UseCoordinates = value.HasCoordinates, CoordinatesText = WorkPointCoordinates.Format(value.Latitude, value.Longitude),
        Latitude = value.Latitude, Longitude = value.Longitude
    };
}

public interface IAttachedWorkPointRepository
{
    /// <summary>The attached work points of one work point, by name.</summary>
    Task<IReadOnlyList<AttachedWorkPoint>> GetAsync(int workPointId, CancellationToken cancellationToken = default);
    /// <summary>The attached work points of all the work points of a beneficiary (one query), by work point then name.</summary>
    Task<IReadOnlyList<AttachedWorkPoint>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<AttachedWorkPoint> CreateAsync(WorkPoint parent, AttachedWorkPointInput input, CancellationToken cancellationToken = default);
    Task<AttachedWorkPoint> UpdateAsync(AttachedWorkPoint original, AttachedWorkPointInput input, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion, in one step (the audit trail receives <see cref="AttachedWorkPointRules.DeleteReason"/>).</summary>
    Task DeleteAsync(AttachedWorkPoint original, CancellationToken cancellationToken = default);
    /// <summary>What the extended search of beneficiaries looks in, in one query: the work points (the main one only when it was given a name or a contact) and the attached work points.</summary>
    Task<IReadOnlyList<PlaceSearchEntry>> GetSearchEntriesAsync(CancellationToken cancellationToken = default);
    /// <summary>Every attached work point with the names of its parents (the map pins, the search).</summary>
    Task<IReadOnlyList<AttachedWorkPointPlace>> GetAllPlacesAsync(CancellationToken cancellationToken = default);
}

public static class AttachedWorkPointRules
{
    public const string NeedsLocationMessage = "Completează cel puțin adresa sau coordonatele GPS.";
    public const string DeleteReason = "Punctul arondat nu va mai fi folosit";
    public const string GeneratedEditReason = "Editare punct arondat";
    public const string RelationType = "PunctArondat";

    public static WorkPointOperationException LastLocation() =>
        new("Adresa sau coordonatele GPS rămase nu pot fi șterse: punctul arondat se șterge doar odată cu totul.");

    public static WorkPointOperationException DuplicateName(string name) =>
        new($"Există deja un punct arondat cu numele «{name}» la acest punct de lucru.");

    public static WorkPointOperationException Changed() =>
        new("Punctul arondat s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static string NameKey(string? name) => TextNormalization.UniquenessKey(name);

    public static string Target(int beneficiaryId, string beneficiaryName, string workPointName, string name) =>
        $"{WorkPointRules.Target(beneficiaryId, beneficiaryName, workPointName)} · punct arondat «{name}»";

    public static string Identification(AttachedWorkPoint value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Nume", value.Name), ("Adresă", value.Address), ("Telefon", value.Phone), ("Persoană de contact", value.ContactPerson),
        ("Coordonate", WorkPointCoordinates.Format(value.Latitude, value.Longitude))
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(AttachedWorkPoint before, AttachedWorkPoint after) =>
    [
        new("Nume", before.Name, after.Name), new("Adresă", before.Address, after.Address),
        new("Telefon", before.Phone, after.Phone), new("Persoană de contact", before.ContactPerson, after.ContactPerson),
        new("Coordonate", Label(before), Label(after))
    ];

    private static string Label(AttachedWorkPoint value) => value.HasCoordinates ? WorkPointCoordinates.Format(value.Latitude, value.Longitude) : "fără coordonate";

    public static void CheckCurrent(AttachedWorkPoint? current, AttachedWorkPoint original)
    {
        if (current is null || current != original) throw Changed();
    }

    /// <summary>Words of a name that carry no meaning for a comparison ("nr", "scoala", "corp"...), so "Grădinița nr. 5" and "Gradinita 5" are the same name.</summary>
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "NR", "NUMAR", "NUMARUL", "DE", "LA", "SI", "CU", "DIN" };

    /// <summary>Key to compare two names of places: no diacritics, upper case, punctuation and filler words ignored.</summary>
    public static string SimilarityKey(string? name)
    {
        var text = TextNormalization.ForStorage(name).ToUpperInvariant();
        var cleaned = new System.Text.StringBuilder(text.Length);
        foreach (var character in text) cleaned.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => !Filler.Contains(token)));
    }

    /// <summary>Two names look alike: equal keys, or one key contains the other when the shorter one has at least 4 characters.</summary>
    public static bool Similar(string? left, string? right)
    {
        var a = SimilarityKey(left);
        var b = SimilarityKey(right);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal);
    }
}
