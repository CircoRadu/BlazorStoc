namespace BlazorStoc.Services;

// The entry points of the suppliers module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails
// at once with ModuleUnavailableException. The answers of the module (SupplierOperationException, SupplierInvoiceOperationException, access denied) are not faults.
internal sealed class GuardedSupplierRepository(ISupplierRepository inner, BlazorStoc.Modules.ModuleRegistry modules) : ISupplierRepository
{
    private const string Id = BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId;

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetSuppliersAsync(cancellationToken));
    public Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));
    public Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));
    public Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateAsync(original, input, cancellationToken));
    public Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
    public Task<Supplier> AddAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.AddAliasAsync(supplier, alias, cancellationToken));
    public Task<Supplier> RemoveAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RemoveAliasAsync(supplier, alias, cancellationToken));
}

internal sealed class GuardedSupplierInvoiceRepository(ISupplierInvoiceRepository inner, BlazorStoc.Modules.ModuleRegistry modules) : ISupplierInvoiceRepository
{
    private const string Id = BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId;

    public Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetForSupplierAsync(supplierId, cancellationToken));
    public Task<IReadOnlyList<SupplierInvoice>> GetAllAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAllAsync(cancellationToken));
    public Task<SupplierInvoice?> FindAsync(int supplierId, string number, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.FindAsync(supplierId, number, cancellationToken));
    public Task<IReadOnlyList<InvoiceEntry>> GetEntriesAsync(int invoiceId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetEntriesAsync(invoiceId, cancellationToken));
    public Task<SupplierInvoice> CreateAsync(SupplierInvoiceInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));
    public Task<SupplierInvoice> UpdateAsync(SupplierInvoice original, SupplierInvoiceInput input, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateAsync(original, input, reason, cancellationToken));
    public Task DeleteAsync(SupplierInvoice original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
}

internal sealed class GuardedSupplierProductCodes(ISupplierProductCodes inner, BlazorStoc.Modules.ModuleRegistry modules) : ISupplierProductCodes
{
    private const string Id = BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId;

    public Task<IReadOnlyDictionary<string, int>> GetAsync(int supplierId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(supplierId, cancellationToken));
    public Task<bool> LinkAsync(int supplierId, string code, int productId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.LinkAsync(supplierId, code, productId, cancellationToken));
}

internal sealed class GuardedSupplierRecognitionLog(ISupplierRecognitionLog inner, BlazorStoc.Modules.ModuleRegistry modules) : ISupplierRecognitionLog
{
    private const string Id = BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId;

    public Task RecordAsync(SupplierRecognitionEntry entry, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RecordAsync(entry, cancellationToken));
    public Task<SupplierRecognitionSummary> SummaryAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SummaryAsync(cancellationToken));
    public Task<string> ExportCsvAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ExportCsvAsync(cancellationToken));
}

// The stand-ins while the module is switched off or did not start: every call says the module is out (the pages that inject the services are still created).
internal sealed class UnavailableSupplierRepository : ISupplierRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new BlazorStoc.Modules.ModuleUnavailableException(BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId));

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<Supplier>>();
    public Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<Supplier?>();
    public Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default) => Out<Supplier>();
    public Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default) => Out<Supplier>();
    public Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default) => Out<object>();
    public Task<Supplier> AddAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => Out<Supplier>();
    public Task<Supplier> RemoveAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) => Out<Supplier>();
}

internal sealed class UnavailableSupplierInvoiceRepository : ISupplierInvoiceRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new BlazorStoc.Modules.ModuleUnavailableException(BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId));

    public Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<SupplierInvoice>>();
    public Task<IReadOnlyList<SupplierInvoice>> GetAllAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<SupplierInvoice>>();
    public Task<SupplierInvoice?> FindAsync(int supplierId, string number, CancellationToken cancellationToken = default) => Out<SupplierInvoice?>();
    public Task<IReadOnlyList<InvoiceEntry>> GetEntriesAsync(int invoiceId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<InvoiceEntry>>();
    public Task<SupplierInvoice> CreateAsync(SupplierInvoiceInput input, CancellationToken cancellationToken = default) => Out<SupplierInvoice>();
    public Task<SupplierInvoice> UpdateAsync(SupplierInvoice original, SupplierInvoiceInput input, string reason, CancellationToken cancellationToken = default) => Out<SupplierInvoice>();
    public Task DeleteAsync(SupplierInvoice original, string reason, CancellationToken cancellationToken = default) => Out<object>();
}

internal sealed class UnavailableSupplierProductCodes : ISupplierProductCodes
{
    private static Task<T> Out<T>() => Task.FromException<T>(new BlazorStoc.Modules.ModuleUnavailableException(BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId));

    public Task<IReadOnlyDictionary<string, int>> GetAsync(int supplierId, CancellationToken cancellationToken = default) => Out<IReadOnlyDictionary<string, int>>();
    public Task<bool> LinkAsync(int supplierId, string code, int productId, CancellationToken cancellationToken = default) => Out<bool>();
}

internal sealed class UnavailableSupplierRecognitionLog : ISupplierRecognitionLog
{
    private static Task<T> Out<T>() => Task.FromException<T>(new BlazorStoc.Modules.ModuleUnavailableException(BlazorStoc.Modules.Suppliers.SuppliersModule.ModuleId));

    public Task RecordAsync(SupplierRecognitionEntry entry, CancellationToken cancellationToken = default) => Out<object>();
    public Task<SupplierRecognitionSummary> SummaryAsync(CancellationToken cancellationToken = default) => Out<SupplierRecognitionSummary>();
    public Task<string> ExportCsvAsync(CancellationToken cancellationToken = default) => Out<string>();
}
