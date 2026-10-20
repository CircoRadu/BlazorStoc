namespace BlazorStoc.Services;

// The three expiry notification sources of the vehicles module (ITP, insurance, road tax): one instance per vehicle that has the date.

public sealed class VehicleExpirySource(IVehicleRepository vehicles, VehicleExpiryKind kind, string key, string eventName) : IExpirySource
{
    public const string PlateName = "numar autovehicul";
    public const string DescriptionName = "descriere autovehicul";
    public string Key => key;
    public string Category => "Autovehicule";
    public string EventName => eventName;
    public string RemovedReason => "Vehiculul a fost șters.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(PlateName, "Numărul de înmatriculare", "TS-01-ABC"),
        new(DescriptionName, "Descrierea autovehiculului", "Dacia Duster")
    ];

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ExpiryInstance>();
        foreach (var vehicle in await vehicles.GetVehiclesAsync(cancellationToken).ConfigureAwait(false))
            if (VehicleRules.GetExpiry(vehicle, kind) is { } expiry)
                result.Add(new(vehicle.Id, $"{vehicle.PlateNumber} · {vehicle.Description}", expiry,
                    new Dictionary<string, string> { [PlateName] = vehicle.PlateNumber, [DescriptionName] = vehicle.Description },
                    $"/vehicule/{vehicle.Id}"));
        return result;
    }
}
