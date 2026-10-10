using BlazorStoc.Checks;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using System.Text.Json;


// Reports synchronously in the reporter's own execution context (Progress<T> would post to the thread pool).
sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}


static class Assert
{
    // Runs the action and returns the expected exception (null when none, or a different one, was raised).
    public static async Task<TException?> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); return null; }
        catch (TException exception) { return exception; }
        catch (Exception) { return null; }
    }
}


sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null) Console.WriteLine(exception);
    }
}


sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan span) => now += span;
}


sealed class FakeChangeSource : IChangeEventSource
{
    private readonly List<StoredChange> events = [];
    public bool FailNextRead { get; set; }
    public void Add(string type, string action, string id, int? projectId = null) =>
        events.Add(new(events.Count + 1, type, action, id, projectId, null, null, DateTime.UtcNow));
    public Task EnsureAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<long> LatestIdAsync(CancellationToken cancellationToken) => Task.FromResult<long>(events.Count);
    public Task<IReadOnlyList<StoredChange>> ReadAfterAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        if (FailNextRead) { FailNextRead = false; throw new InvalidOperationException("Source unavailable"); }
        return Task.FromResult<IReadOnlyList<StoredChange>>(events.Where(change => change.Id > afterId).Take(limit).ToList());
    }
    public Task PurgeAsync(long throughId, DateTime olderThanUtc, CancellationToken cancellationToken) => Task.CompletedTask;
}


sealed class TestAccessControl(bool administrator, string username, bool productOperator = true) : IAccessControl
{
    public Task<bool> IsAdministratorAsync(CancellationToken cancellationToken = default) => Task.FromResult(administrator);
    public Task<bool> CanManageProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(productOperator);
    public Task<bool> CanManageBeneficiariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string?> GetUsernameAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(username);
    public Task EnsureAdministratorAsync(CancellationToken cancellationToken = default) => administrator
        ? Task.CompletedTask
        : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureProductOperatorAsync(CancellationToken cancellationToken = default) => productOperator ? Task.CompletedTask : Task.FromException(new AccessDeniedException("Test access denied"));
    public Task EnsureBeneficiaryOperatorAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}


sealed class FakeInventoryProductRepository(IReadOnlyList<Product> products, IReadOnlyList<ProductGroup> groups) : IProductRepository
{
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(products);
    public Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(products.FirstOrDefault(product => product.Id == id));
    public Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) => Task.FromResult(groups);
    public Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RenameCategoryAsync(string originalCategory, string newCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory, string targetCategory, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteCategoryAsync(string category, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task ReorderCategoriesAsync(IReadOnlyList<string> orderedCategories, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteSubcategoryAsync(ProductGroup group, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}


sealed class FakeInventoryStockMovementRepository(IReadOnlyDictionary<int, int> inVehicles) : IStockMovementRepository
{
    public Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default) => Task.FromResult(inVehicles);
    public Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> RegularizeNegativeStockAsync(int productId, int realWarehouseQuantity, string context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExitOperationDetails?> GetOperationAsync(int operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task VoidExitOperationAsync(int operationId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ReturnableExit>> GetReturnableExitsAsync(int productId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReturnableExit>>([]);
    public Task<IReadOnlyList<NetConsumption>> GetNetConsumptionAsync(int? projectId, int? beneficiaryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NetConsumption>>([]);
    public Task<IReadOnlyList<ExitLinePreview>> PreviewExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExitOperationResult> CreateExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RegularizationItem>> GetToRegularizeAsync(RegularizationQuery? query = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RegularizationItem>>([]);
    public Task<IReadOnlyList<FreeEntry>> GetFreeEntriesAsync(FreeEntryQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FreeEntry>>([]);
    public Task<IReadOnlyList<StockMovement>> AttachToInvoiceAsync(IReadOnlyList<StockMovement> entries, int invoiceId, string reason, bool viaPickup = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement> DetachFromInvoiceAsync(StockMovement entry, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}


// Records every movement CreateAsync receives (Task 1's inventory pickup applier checks), and can be told to fail
// for specific products to exercise partial-failure reporting without touching a real database.
sealed class FakeInventoryPickupMovementRepository(IReadOnlyDictionary<int, int> inVehicles, ISet<int>? failProductIds = null) : IStockMovementRepository
{
    public List<(int ProductId, StockMovementInput Input)> Created { get; } = [];
    public Task<IReadOnlyDictionary<int, int>> GetQuantitiesInVehiclesAsync(CancellationToken cancellationToken = default) => Task.FromResult(inVehicles);
    public Task<StockMovementResult> CreateAsync(int productId, StockMovementInput input, CancellationToken cancellationToken = default)
    {
        if (failProductIds?.Contains(productId) == true) throw new StockMovementOperationException("Esec de test");
        Created.Add((productId, input));
        var movement = new StockMovement(Created.Count, productId, input.Kind, input.Quantity ?? 0,
            input.Date ?? DateOnly.FromDateTime(DateTime.Now), input.Description, null, null, null, null,
            "test", 1, DateTime.UtcNow, DateTime.UtcNow, false, input.Destination);
        return Task.FromResult(new StockMovementResult(movement, 0));
    }
    public Task<StockMovementPage> GetPageAsync(int productId, StockMovementQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement?> GetAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovementHistoryEntry>> GetHistoryAsync(int movementId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProjectStockMovement>> GetForProjectAsync(int projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleStock>> GetVehicleStocksAsync(int productId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<VehicleEquipment>> GetVehicleEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<StockMovement>> TransferFromVehicleAsync(VehicleTransfer transfer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<int, int>> GetMovementCountsByVehicleAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> UpdateAsync(StockMovement original, StockMovementInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> DeleteAsync(StockMovement original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovementResult> RegularizeNegativeStockAsync(int productId, int realWarehouseQuantity, string context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExitOperationDetails?> GetOperationAsync(int operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task VoidExitOperationAsync(int operationId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ReturnableExit>> GetReturnableExitsAsync(int productId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReturnableExit>>([]);
    public Task<IReadOnlyList<NetConsumption>> GetNetConsumptionAsync(int? projectId, int? beneficiaryId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NetConsumption>>([]);
    public Task<IReadOnlyList<ExitLinePreview>> PreviewExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ExitOperationResult> CreateExitOperationAsync(IReadOnlyList<ExitOperationLine> lines, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RegularizationItem>> GetToRegularizeAsync(RegularizationQuery? query = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RegularizationItem>>([]);
    public Task<IReadOnlyList<FreeEntry>> GetFreeEntriesAsync(FreeEntryQuery query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FreeEntry>>([]);
    public Task<IReadOnlyList<StockMovement>> AttachToInvoiceAsync(IReadOnlyList<StockMovement> entries, int invoiceId, string reason, bool viaPickup = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StockMovement> DetachFromInvoiceAsync(StockMovement entry, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}


sealed class TestAuditTrail : IAuditTrail
{
    public List<AuditWrite> Entries { get; } = [];
    public Task RecordAsync(AuditWrite entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<AuditEvent>> GetEventsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
    public Task<AuditPage> QueryAsync(AuditQuery query, int page, int pageSize, int? window = null, CancellationToken cancellationToken = default) => Task.FromResult(AuditQueryRules.Page([], query, page, pageSize, window));
    public Task<AuditSummary> SummaryAsync(DateTime todayStartUtc, CancellationToken cancellationToken = default) => Task.FromResult(AuditQueryRules.Summary([], todayStartUtc));
    public Task<IReadOnlyDictionary<string, DateTime>> RemovalTimesAsync(IEnumerable<AuditEvent> events, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, DateTime>>(new Dictionary<string, DateTime>());
}


sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "BlazorStoc.Checks";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}


sealed class TestExpirySource(string key, List<ExpiryInstance> instances) : IExpirySource
{
    public bool Fail { get; set; }
    public List<ExpiryInstance> Instances { get; } = instances;
    public string Key => key;
    public string Category => "Categorie test";
    public string EventName => "Eveniment test";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } = [new("obiect", "Denumirea obiectului", "Obiect test")];
    public Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        Fail ? Task.FromException<IReadOnlyList<ExpiryInstance>>(new InvalidOperationException("source unavailable")) : Task.FromResult<IReadOnlyList<ExpiryInstance>>(Instances.ToArray());
}


sealed class FakeMaintenanceReader(IReadOnlyList<MaintenanceDueItem> due, IReadOnlyList<ContractExpiryItem> expiries) : IMaintenanceNotificationReader
{
    public Task<IReadOnlyList<MaintenanceDueItem>> GetDueAsync(CancellationToken cancellationToken = default) => Task.FromResult(due);
    public Task<IReadOnlyList<ContractExpiryItem>> GetContractExpiriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(expiries);
}


static class MapPinTestExtensions
{
    public static MapPinType With(this MapPinType type, Action<MapPinType> change) { var copy = type.Clone(); change(copy); return copy; }
}
