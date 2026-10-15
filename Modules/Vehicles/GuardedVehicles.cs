using BlazorStoc.Services;

namespace BlazorStoc.Modules.Vehicles;

// The entry points of the vehicles module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call
// fails at once with ModuleUnavailableException. The answers of the module (VehicleOperationException, VehicleTargetException, access denied) are not faults.
internal sealed class GuardedVehicleRepository(IVehicleRepository inner, ModuleRegistry modules) : IVehicleRepository
{
    private const string Id = VehiclesModule.ModuleId;

    public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetVehiclesAsync(cancellationToken));

    public Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));

    public Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));

    public Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default, string? auditAction = null) =>
        modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken, auditAction));

    public Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

internal sealed class GuardedVehicleTargetRepository(IVehicleTargetRepository inner, ModuleRegistry modules) : IVehicleTargetRepository
{
    private const string Id = VehiclesModule.ModuleId;

    public Task<IReadOnlyList<VehicleTarget>> GetForVehicleAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.GetForVehicleAsync(vehicleId, cancellationToken));

    public Task SetAsync(int vehicleId, int productId, int target, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.SetAsync(vehicleId, productId, target, cancellationToken));

    public Task RemoveAsync(int vehicleId, int productId, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.RemoveAsync(vehicleId, productId, cancellationToken));
}

// The stand-ins of the repositories while the module is switched off or did not start: every call says the module is out.
internal sealed class UnavailableVehicleRepository : IVehicleRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(VehiclesModule.ModuleId));

    public Task<IReadOnlyList<Vehicle>> GetVehiclesAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<Vehicle>>();
    public Task<Vehicle?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<Vehicle?>();
    public Task<Vehicle> CreateAsync(VehicleInput input, CancellationToken cancellationToken = default) => Out<Vehicle>();
    public Task<Vehicle> UpdateAsync(Vehicle original, VehicleInput input, CancellationToken cancellationToken = default, string? auditAction = null) => Out<Vehicle>();
    public Task DeleteAsync(Vehicle original, string reason, CancellationToken cancellationToken = default) => Out<object>();
}

internal sealed class UnavailableVehicleTargetRepository : IVehicleTargetRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(VehiclesModule.ModuleId));

    public Task<IReadOnlyList<VehicleTarget>> GetForVehicleAsync(int vehicleId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<VehicleTarget>>();
    public Task SetAsync(int vehicleId, int productId, int target, CancellationToken cancellationToken = default) => Out<object>();
    public Task RemoveAsync(int vehicleId, int productId, CancellationToken cancellationToken = default) => Out<object>();
}
