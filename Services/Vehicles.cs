using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

public sealed record Vehicle(int Id, string PlateNumber, string Description, long Version = 0);

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

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public VehicleInput Validated(bool requiresReason = false)
    {
        var plate = VehiclePlate.TryNormalize(PlateNumber, out var canonical) ? canonical : TextNormalization.ForObjectNameOrCode(PlateNumber);
        var normalized = new VehicleInput
        {
            PlateNumber = plate,
            Description = TextNormalization.ForObjectNameOrCode(Description),
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new VehicleOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new VehicleOperationException(reasonError);
        return normalized;
    }

    public static VehicleInput From(Vehicle vehicle) => new() { PlateNumber = vehicle.PlateNumber, Description = vehicle.Description };
}

public sealed class VehicleOperationException(string message) : Exception(message);

public interface IVehicleRepository
{
    Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default);
    Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default);
    Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default);
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
    // Where links to a vehicle point (the movements table); it moves to the vehicle page when that page exists.
    public static string PageUrl(int vehicleId) => EditUrl(vehicleId);
}

public static class VehicleRules
{
    public const string ChangedMessage = "Vehiculul a fost modificat sau șters între timp. Actualizează lista și reia operația.";
    public const string ConcurrentMessage = "Vehiculul s-a schimbat între timp. Actualizează lista.";

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
