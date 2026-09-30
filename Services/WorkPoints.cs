using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// A beneficiary's work point (row of beneficiary_work_points). Every beneficiary has exactly one MAIN work point (IsPrimary): a real
// row created with the beneficiary, whose address and phone follow the beneficiary (they change only when the beneficiary is
// edited); its name, description, contact person, coordinates and photos are edited like those of the additional ones. The main
// one cannot be deleted on its own. Coordinates are optional and always both or neither. Photos are kept in service_photos.
public sealed record WorkPoint(int Id, int BeneficiaryId, string Name, string Address, string Phone = "",
    string ContactPerson = "", long Version = 0, bool IsPrimary = false, string Description = "",
    decimal? Latitude = null, decimal? Longitude = null)
{
    public bool HasCoordinates => Latitude is not null && Longitude is not null;
}

public sealed class WorkPointInput : IValidatableObject
{
    public const int DescriptionMaximumLength = 2000;

    [StringLength(200, ErrorMessage = "Numele punctului de lucru poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(300, ErrorMessage = "Adresa poate avea cel mult 300 de caractere.")]
    public string Address { get; set; } = "";

    [StringLength(20, ErrorMessage = "Numărul de telefon poate avea cel mult 20 de caractere.")]
    public string Phone { get; set; } = "";

    [StringLength(200, ErrorMessage = "Numele persoanei de contact poate avea cel mult 200 de caractere.")]
    public string ContactPerson { get; set; } = "";

    [StringLength(DescriptionMaximumLength, ErrorMessage = "Descrierea poate avea cel mult 2000 de caractere.")]
    public string Description { get; set; } = "";

    // The "Coordonate" switch: off means the point has no coordinates (whatever is typed in the field is ignored and stored as none).
    public bool UseCoordinates { get; set; }

    // "latitude, longitude" as pasted from a map; see WorkPointCoordinates.TryParse.
    [StringLength(60, ErrorMessage = "Coordonatele sunt prea lungi.")]
    public string CoordinatesText { get; set; } = "";

    // Filled by Validated().
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new("Completează numele punctului de lucru.", [nameof(Name)]);
        if (string.IsNullOrWhiteSpace(Address)) yield return new("Completează adresa.", [nameof(Address)]);
        if (!string.IsNullOrWhiteSpace(Phone) && !BeneficiaryRules.PhonePattern.IsMatch(BeneficiaryRules.NormalizePhone(Phone)))
            yield return new("Numărul de telefon trebuie să conțină 7–15 cifre, opțional precedate de +.", [nameof(Phone)]);
        if (UseCoordinates && !WorkPointCoordinates.TryParse(CoordinatesText, out _, out _, out var coordinatesError))
            yield return new(coordinatesError!, [nameof(CoordinatesText)]);
    }

    public WorkPointInput Validated()
    {
        decimal? latitude = null, longitude = null;
        if (UseCoordinates && WorkPointCoordinates.TryParse(CoordinatesText, out var parsedLatitude, out var parsedLongitude, out _))
            (latitude, longitude) = (parsedLatitude, parsedLongitude);
        var normalized = new WorkPointInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Address = TextNormalization.ForObjectNameOrCode(Address),
            Phone = BeneficiaryRules.NormalizePhone(Phone),
            ContactPerson = TextNormalization.ForObjectNameOrCode(ContactPerson),
            Description = TextNormalization.ForStorage((Description ?? "").Replace("\r\n", "\n")),
            UseCoordinates = UseCoordinates,
            CoordinatesText = UseCoordinates ? CoordinatesText : "",
            Latitude = latitude, Longitude = longitude
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new WorkPointOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        return normalized;
    }

    public static WorkPointInput From(WorkPoint value) => new()
    {
        Name = value.Name, Address = value.Address, Phone = value.Phone, ContactPerson = value.ContactPerson, Description = value.Description,
        UseCoordinates = value.HasCoordinates, CoordinatesText = WorkPointCoordinates.Format(value.Latitude, value.Longitude),
        Latitude = value.Latitude, Longitude = value.Longitude
    };
}

public sealed class WorkPointOperationException(string message) : Exception(message);

// Coordinates typed or pasted from a map. Accepted: "45.7489, 21.2087", "45.7489 21.2087", "45,7489 21,2087" (decimal comma with
// a space or semicolon between the two numbers). Latitude -90..90, longitude -180..180, stored with 6 decimals.
public static class WorkPointCoordinates
{
    private static readonly Regex DotPair = new(@"^\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out decimal latitude, out decimal longitude, out string? error)
    {
        latitude = longitude = 0;
        error = null;
        var value = (text ?? "").Trim();
        if (value.Length == 0) { error = "Completează coordonatele (latitudine, longitudine) sau oprește comutatorul „Coordonate”."; return false; }
        string[] parts;
        if (DotPair.Match(value) is { Success: true } pair) parts = [pair.Groups[1].Value, pair.Groups[2].Value];
        else parts = value.Replace(';', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Replace(',', '.')).ToArray();
        if (parts.Length != 2 ||
            !decimal.TryParse(parts[0], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out latitude) ||
            !decimal.TryParse(parts[1], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out longitude))
        {
            error = "Coordonatele trebuie scrise ca «latitudine, longitudine», de exemplu 45.7489, 21.2087.";
            return false;
        }
        if (latitude is < -90m or > 90m) { error = "Latitudinea trebuie să fie între -90 și 90."; return false; }
        if (longitude is < -180m or > 180m) { error = "Longitudinea trebuie să fie între -180 și 180."; return false; }
        latitude = Math.Round(latitude, 6, MidpointRounding.AwayFromZero);
        longitude = Math.Round(longitude, 6, MidpointRounding.AwayFromZero);
        return true;
    }

    public static string Format(decimal? latitude, decimal? longitude) =>
        latitude is null || longitude is null ? "" : $"{latitude.Value.ToString("0.######", CultureInfo.InvariantCulture)}, {longitude.Value.ToString("0.######", CultureInfo.InvariantCulture)}";
}

public interface IWorkPointRepository
{
    /// <summary>All the work points of a beneficiary, the main one first (a real row, created with the beneficiary).</summary>
    Task<IReadOnlyList<WorkPoint>> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<WorkPoint> CreateAsync(int beneficiaryId, WorkPointInput input, CancellationToken cancellationToken = default);
    /// <summary>The address and the phone of the main work point are not taken from the input (they follow the beneficiary).</summary>
    Task<WorkPoint> UpdateAsync(WorkPoint original, WorkPointInput input, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion (the photos of the point go to the archive with it). The main work point cannot be deleted.</summary>
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
    public const string PhotoDeleteReason = "Fotografia nu va mai fi folosită";

    // Only used when a beneficiary has no main work point row yet (before the startup backfill): shown read-only, Id 0.
    public static WorkPoint Primary(Beneficiary beneficiary) =>
        new(0, beneficiary.Id, PrimaryName, beneficiary.Address, beneficiary.Phone, IsPrimary: true);

    public static WorkPointOperationException DuplicateAddress(string existingName) =>
        new($"Există deja un punct de lucru cu această adresă: «{existingName}».");

    public static BeneficiaryOperationException BeneficiaryAddressTaken(string workPointName) =>
        new($"Adresa coincide cu cea a punctului de lucru «{workPointName}». Modifică sau șterge mai întâi acel punct de lucru.");

    public static WorkPointOperationException Changed() =>
        new("Punctul de lucru s-a schimbat între timp. Actualizează pagina și reia operația.");

    public static WorkPointOperationException PrimaryNotDeletable() =>
        new("Punctul de lucru principal nu poate fi șters separat: el dispare odată cu beneficiarul.");

    public static string Target(int beneficiaryId, string beneficiaryName, string workPointName) =>
        $"#{beneficiaryId} · {beneficiaryName} · punct de lucru «{workPointName}»";

    public static string Identification(WorkPoint value) => AuditDetails.Identification(new (string Field, string Value)[]
    {
        ("Nume", value.Name), ("Adresă", value.Address), ("Telefon", value.Phone), ("Persoană de contact", value.ContactPerson),
        ("Descriere", value.Description), ("Coordonate", WorkPointCoordinates.Format(value.Latitude, value.Longitude))
    }.Where(item => item.Value.Length > 0).ToArray());

    public static IReadOnlyList<AuditChange> Changes(WorkPoint before, WorkPoint after) =>
    [
        new("Nume", before.Name, after.Name), new("Adresă", before.Address, after.Address),
        new("Telefon", before.Phone, after.Phone), new("Persoană de contact", before.ContactPerson, after.ContactPerson),
        new("Descriere", before.Description, after.Description),
        new("Coordonate", CoordinatesLabel(before), CoordinatesLabel(after))
    ];

    private static string CoordinatesLabel(WorkPoint value) => value.HasCoordinates ? WorkPointCoordinates.Format(value.Latitude, value.Longitude) : "fără coordonate";

    // The journal names the exact operation: an edit that changes only the description, or only the coordinates, has its own name.
    public static string EditAction(WorkPoint before, WorkPoint after)
    {
        var descriptionChanged = before.Description != after.Description;
        var coordinatesChanged = before.Latitude != after.Latitude || before.Longitude != after.Longitude;
        var othersChanged = before.Name != after.Name || before.Address != after.Address || before.Phone != after.Phone || before.ContactPerson != after.ContactPerson;
        if (othersChanged || (descriptionChanged && coordinatesChanged) || (!descriptionChanged && !coordinatesChanged)) return AuditActions.EditWorkPoint;
        return descriptionChanged ? AuditActions.EditWorkPointDescription : AuditActions.EditWorkPointCoordinates;
    }

    public static void CheckCurrent(WorkPoint? current, WorkPoint original)
    {
        if (current is null || current != original) throw Changed();
    }
}
