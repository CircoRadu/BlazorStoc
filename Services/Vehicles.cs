using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// The three expiry dates are required by the editor; they are nullable here only so that a vehicle can be built without them.
public sealed record Vehicle(int Id, string PlateNumber, string Description, long Version = 0,
    DateOnly? ItpExpiry = null, DateOnly? InsuranceExpiry = null, DateOnly? RovinietaExpiry = null);

// Registration number in the form "AA-OOO-AAA": one or two letters (county; "B" for Bucharest), two or three digits,
// three letters (for example HD-01-FDG, HD-233-VDG, B-123-ABC). The stored form is upper case with hyphens.
public static partial class VehiclePlate
{
    public const string FormatMessage =
        "Numărul de înmatriculare trebuie să respecte forma AA-OOO-AAA (județ, 2–3 cifre, 3 litere), de exemplu HD-01-FDG, HD-233-VDG sau B-123-ABC.";

    [GeneratedRegex(@"^([A-Z]{1,2})-?([0-9]{2,3})-?([A-Z]{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    // Accepts lower case, surrounding spaces and missing hyphens; returns the canonical form or false when invalid.
    public static bool TryNormalize(string? value, out string plate)
    {
        var text = TextNormalization.ForStorage(value).Replace(" ", "").ToUpperInvariant();
        var match = Pattern().Match(text);
        plate = match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value}" : string.Empty;
        return match.Success;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class VehiclePlateAttribute : ValidationAttribute
{
    public VehiclePlateAttribute() : base(VehiclePlate.FormatMessage) { }
    public override bool IsValid(object? value) => value is not string text || text.Trim().Length == 0 || VehiclePlate.TryNormalize(text, out _);
}

public sealed class VehicleInput
{
    public const int DescriptionMaximumLength = 100;

    [Required(ErrorMessage = "Completează numărul de înmatriculare.")]
    [StringLength(20, ErrorMessage = "Numărul de înmatriculare este prea lung.")]
    [VehiclePlate]
    public string PlateNumber { get; set; } = "";

    [Required(ErrorMessage = "Completează descrierea vehiculului.")]
    [StringLength(DescriptionMaximumLength, ErrorMessage = "Descrierea poate avea cel mult 100 de caractere.")]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Alege data de expirare a ITP.")]
    public DateOnly? ItpExpiry { get; set; }

    [Required(ErrorMessage = "Alege data de expirare a asigurării.")]
    public DateOnly? InsuranceExpiry { get; set; }

    [Required(ErrorMessage = "Alege data de expirare a rovinietei.")]
    public DateOnly? RovinietaExpiry { get; set; }

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public VehicleInput Validated(bool requiresReason = false)
    {
        var plate = VehiclePlate.TryNormalize(PlateNumber, out var canonical) ? canonical : TextNormalization.ForObjectNameOrCode(PlateNumber);
        var normalized = new VehicleInput
        {
            PlateNumber = plate,
            Description = TextNormalization.ForObjectNameOrCode(Description),
            ItpExpiry = ItpExpiry,
            InsuranceExpiry = InsuranceExpiry,
            RovinietaExpiry = RovinietaExpiry,
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new VehicleOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        foreach (var (date, label) in new[] { (ItpExpiry, "ITP"), (InsuranceExpiry, "asigurării"), (RovinietaExpiry, "rovinietei") })
            if (date is { } value && (value < StockMovementRules.EarliestDate || value > VehicleRules.LatestExpiry))
                throw new VehicleOperationException($"Data de expirare a {label} nu este validă.");
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new VehicleOperationException(reasonError);
        return normalized;
    }

    public static VehicleInput From(Vehicle vehicle) => new()
    {
        PlateNumber = vehicle.PlateNumber, Description = vehicle.Description,
        ItpExpiry = vehicle.ItpExpiry, InsuranceExpiry = vehicle.InsuranceExpiry, RovinietaExpiry = vehicle.RovinietaExpiry
    };
}

public enum VehicleExpiryKind { Itp, Insurance, Rovinieta }

// One editable expiry row of the vehicle page; the label is also used to generate the change reason.
public sealed record VehicleExpiryField(VehicleExpiryKind Kind, string Label);

public sealed class VehicleOperationException(string message) : Exception(message);

// The choice made in the transfer dialog of the vehicle page: quantity (0 for whole-vehicle operations) and target vehicle.
public sealed record VehicleTransferChoice(int Quantity, int? TargetVehicleId);

public interface IVehicleRepository
{
    Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default);
    // auditAction names the exact operation in the journal (null = a plain "Editare").
    Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default, string? auditAction = null);
    Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default);
}

public static class VehicleSearch
{
    public static IEnumerable<Vehicle> Filter(IEnumerable<Vehicle> vehicles, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return vehicles;
        var compact = query.Replace("-", "").Replace(" ", "");
        return vehicles.Where(vehicle =>
            vehicle.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            vehicle.PlateNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (compact.Length > 0 && vehicle.PlateNumber.Replace("-", "").Contains(compact, StringComparison.OrdinalIgnoreCase)));
    }
}

public static class VehicleNavigation
{
    // The event stores the vehicle id; the administration page opens the editor of that vehicle.
    public static string EditUrl(int vehicleId) => $"/vehicule?edit={vehicleId}";
    // The vehicle page (its equipment); links from lists, the movements table and the journal point here.
    public static string PageUrl(int vehicleId) => $"/vehicule/{vehicleId}";
    public static string EquipmentUrl(int vehicleId) => $"/vehicule/{vehicleId}/echipamente";
}

public static class VehicleRules
{
    public static string Target(Vehicle vehicle) => $"#{vehicle.Id} · {vehicle.PlateNumber}";

    public const string ChangedMessage = "Vehiculul a fost modificat sau șters între timp. Actualizează lista și reia operația.";
    public const string ConcurrentMessage = "Vehiculul s-a schimbat între timp. Actualizează lista.";

    // Existing vehicles received these dates (next year) when the columns were added; the same values are the column defaults.
    public static readonly DateOnly DefaultItpExpiry = new(2027, 3, 15);
    public static readonly DateOnly DefaultInsuranceExpiry = new(2027, 6, 30);
    public static readonly DateOnly DefaultRovinietaExpiry = new(2027, 9, 30);
    // Expiry dates may lie in the future; only an absurd year is rejected.
    public static readonly DateOnly LatestExpiry = new(2100, 12, 31);

    public static DateOnly? GetExpiry(Vehicle vehicle, VehicleExpiryKind kind) => kind switch
    {
        VehicleExpiryKind.Itp => vehicle.ItpExpiry, VehicleExpiryKind.Insurance => vehicle.InsuranceExpiry, _ => vehicle.RovinietaExpiry
    };

    public static void SetExpiry(VehicleInput input, VehicleExpiryKind kind, DateOnly? date)
    {
        switch (kind)
        {
            case VehicleExpiryKind.Itp: input.ItpExpiry = date; break;
            case VehicleExpiryKind.Insurance: input.InsuranceExpiry = date; break;
            default: input.RovinietaExpiry = date; break;
        }
    }

    public static string ExpiryAuditAction(VehicleExpiryKind kind) => kind switch
    {
        VehicleExpiryKind.Itp => AuditActions.ExpiryItp, VehicleExpiryKind.Insurance => AuditActions.ExpiryInsurance, _ => AuditActions.ExpiryRovinieta
    };

    public static string DisplayExpiry(DateOnly? date) => date is { } value ? StockMovementRules.DisplayDate(value) : "—";
    public static string StorageExpiry(DateOnly? date) => date is { } value ? StockMovementRules.StorageDate(value) : string.Empty;
    public static DateOnly? ParseStoredExpiry(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    public static string AuditIdentification(Vehicle vehicle) => AuditDetails.Identification(
        ("Număr de înmatriculare", vehicle.PlateNumber), ("Descriere", vehicle.Description),
        ("Expirare ITP", DisplayExpiry(vehicle.ItpExpiry)), ("Expirare asigurare", DisplayExpiry(vehicle.InsuranceExpiry)),
        ("Expirare rovinietă", DisplayExpiry(vehicle.RovinietaExpiry)));

    public static AuditChange[] Changes(Vehicle before, Vehicle after) =>
    [
        new("Număr de înmatriculare", before.PlateNumber, after.PlateNumber), new("Descriere", before.Description, after.Description),
        new("Expirare ITP", DisplayExpiry(before.ItpExpiry), DisplayExpiry(after.ItpExpiry)),
        new("Expirare asigurare", DisplayExpiry(before.InsuranceExpiry), DisplayExpiry(after.InsuranceExpiry)),
        new("Expirare rovinietă", DisplayExpiry(before.RovinietaExpiry), DisplayExpiry(after.RovinietaExpiry))
    ];

    public static void CheckCurrent(Vehicle? current, Vehicle original)
    {
        if (current is null || current != original) throw new VehicleOperationException(ChangedMessage);
    }

    // Single wording for every storage mode; the description is the one stored in the database, not the one just typed.
    public static string DuplicatePlateMessage(string? plate, string? existingDescription) =>
        string.IsNullOrWhiteSpace(plate)
            ? "Există deja un vehicul cu acest număr de înmatriculare."
            : string.IsNullOrWhiteSpace(existingDescription)
                ? $"Există deja un vehicul cu numărul de înmatriculare {plate}."
                : $"Există deja un vehicul cu numărul de înmatriculare {plate}: «{existingDescription}».";

    // Movements gain a vehicle in the exit form (the vehicle exit task); a vehicle used by a movement must not be deleted.
    public static void CheckDelete(bool hasStockMovements)
    {
        if (hasStockMovements)
            throw new VehicleOperationException("Vehiculul are mișcări de stoc asociate și nu poate fi șters.");
    }
}
