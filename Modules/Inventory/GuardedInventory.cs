using BlazorStoc.Services;

namespace BlazorStoc.Modules.Inventory;

// The entry points of the inventory module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails
// at once with ModuleUnavailableException. The answers of the module (InventoryOperationException, access denied) are not faults. Only its own pages use them, and the
// router replaces those pages while the module is out, so there are no stand-ins.
internal sealed class GuardedInventoryReportBuilder(IInventoryReportBuilder inner, ModuleRegistry modules) : IInventoryReportBuilder
{
    public Task<InventoryReport> BuildAsync(InventoryRequest request, CancellationToken cancellationToken = default) =>
        modules.RunAsync(InventoryModule.ModuleId, () => inner.BuildAsync(request, cancellationToken));
}

internal sealed class GuardedInventoryPickupBuilder(IInventoryPickupBuilder inner, ModuleRegistry modules) : IInventoryPickupBuilder
{
    public Task<InventoryPickupReview> BuildAsync(IReadOnlyList<InventoryPickupScanRow> rows, CancellationToken cancellationToken = default) =>
        modules.RunAsync(InventoryModule.ModuleId, () => inner.BuildAsync(rows, cancellationToken));
}

internal sealed class GuardedInventoryPickupApplier(IInventoryPickupApplier inner, ModuleRegistry modules) : IInventoryPickupApplier
{
    public Task<InventoryPickupApplyResult> ApplyAsync(IReadOnlyList<InventoryPickupLine> selectedLines, CancellationToken cancellationToken = default) =>
        modules.RunAsync(InventoryModule.ModuleId, () => inner.ApplyAsync(selectedLines, cancellationToken));
}
