namespace BlazorStoc.Services;

// Target level of a product in a vehicle ("nivel tinta"): how many pieces the vehicle should hold. "Completeaza la nivel" fills the missing pieces (target minus
// what the vehicle holds) with one exit operation from the warehouse to the vehicle. The level never changes the stock by itself.
public sealed record VehicleTarget(int VehicleId, int ProductId, string ProductName, int Target);

public sealed class VehicleTargetException(string message) : Exception(message);

public interface IVehicleTargetRepository
{
    Task<IReadOnlyList<VehicleTarget>> GetForVehicleAsync(int vehicleId, CancellationToken cancellationToken = default);
    // Sets (adds or changes) the target of a product in a vehicle; each change is journaled with the old and the new value.
    Task SetAsync(int vehicleId, int productId, int target, CancellationToken cancellationToken = default);
    Task RemoveAsync(int vehicleId, int productId, CancellationToken cancellationToken = default);
}

public static class VehicleTargetRules
{
    public static readonly string QuantityMessage = $"Nivelul țintă trebuie să fie între 1 și {StockMovementRules.MaxQuantity} buc.";
    public static int Missing(int target, int held) => Math.Max(0, target - Math.Max(0, held));
}
