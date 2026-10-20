using BlazorStoc.Services;

namespace BlazorStoc.Modules.Invoices;

// The entry points of the invoice module behind the circuit breaker of the registry: every call is reported (success or fault); while the circuit is open a call fails at once with
// ModuleUnavailableException. The answers of the module (InvoiceAnalysisException, InvoiceTemplateOperationException, access denied) are not faults, and a fault that belongs to
// another module (the reading of the pages by the Ocr module) is counted for that module, not for this one (IBelongsToModule).
internal sealed class GuardedInvoiceAnalysisService(IInvoiceAnalysisService inner, ModuleRegistry modules) : IInvoiceAnalysisService
{
    private const string Id = InvoicesModule.ModuleId;

    // The synchronous members work on the session in memory (and run the analysis of an opened file); they complete at once.
    private T Sync<T>(Func<T> operation) => modules.RunAsync(Id, () => Task.FromResult(operation())).GetAwaiter().GetResult();

    public Task<InvoiceAnalysisSession> AnalyzeAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.AnalyzeAsync(pdf, fileName, cancellationToken));

    public Task<InvoiceAnalysisSession> OpenAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.OpenAsync(pdf, fileName, cancellationToken));

    public InvoiceAnalysisSession Analyze(Guid id) => Sync(() => inner.Analyze(id));

    public InvoiceAnalysisSession? Get(Guid id) => Sync(() => inner.Get(id));

    public void Discard(Guid id) => inner.Discard(id);

    public Task<InvoiceAnalysisSession> OpenXmlAsync(byte[] content, string fileName, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.OpenXmlAsync(content, fileName, cancellationToken));
}

internal sealed class GuardedInvoiceTemplateService(IInvoiceTemplateService inner, ModuleRegistry modules) : IInvoiceTemplateService
{
    private const string Id = InvoicesModule.ModuleId;

    public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.ListAsync(cancellationToken));
    public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(id, cancellationToken));
    public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAllAsync(cancellationToken));
    public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetModelAsync(id, cancellationToken));
    public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.CreateAsync(input, cancellationToken));
    public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SaveAsync(original, input, cancellationToken));
    public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.SetActiveAsync(original, active, cancellationToken));
    public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UpdateDetailsAsync(original, input, cancellationToken));
    public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DeleteAsync(original, reason, cancellationToken));
    public Task<int> RenameSupplierAsync(string supplierCui, string oldName, string newName, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RenameSupplierAsync(supplierCui, oldName, newName, cancellationToken));
    public Task<InvoiceTemplateInfo> LinkSupplierAsync(InvoiceTemplateInfo original, Supplier supplier, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.LinkSupplierAsync(original, supplier, cancellationToken));
}

// The stand-ins while the module is switched off or did not start. The rename of a supplier inside the templates is a courtesy of the supplier register (the templates are matched by
// the tax id anyway), so while the module is out it changes nothing and does not fail the rename of the supplier; everything else says the module is out.
internal sealed class UnavailableInvoiceAnalysisService : IInvoiceAnalysisService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(InvoicesModule.ModuleId));

    public Task<InvoiceAnalysisSession> AnalyzeAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) => Out<InvoiceAnalysisSession>();
    public Task<InvoiceAnalysisSession> OpenAsync(Stream pdf, string fileName, CancellationToken cancellationToken = default) => Out<InvoiceAnalysisSession>();
    public InvoiceAnalysisSession Analyze(Guid id) => throw new ModuleUnavailableException(InvoicesModule.ModuleId);
    public InvoiceAnalysisSession? Get(Guid id) => null;
    public void Discard(Guid id) { }
    public Task<InvoiceAnalysisSession> OpenXmlAsync(byte[] content, string fileName, CancellationToken cancellationToken = default) => Out<InvoiceAnalysisSession>();
}

internal sealed class UnavailableInvoiceTemplateService : IInvoiceTemplateService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(InvoicesModule.ModuleId));

    public Task<IReadOnlyList<InvoiceTemplateInfo>> ListAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<InvoiceTemplateInfo>>();
    public Task<InvoiceTemplateRecord?> GetAsync(int id, CancellationToken cancellationToken = default) => Out<InvoiceTemplateRecord?>();
    public Task<IReadOnlyList<InvoiceTemplateRecord>> GetAllAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<InvoiceTemplateRecord>>();
    public Task<InvoiceTemplateModel?> GetModelAsync(int id, CancellationToken cancellationToken = default) => Out<InvoiceTemplateModel?>();
    public Task<InvoiceTemplateRecord> CreateAsync(InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Out<InvoiceTemplateRecord>();
    public Task<InvoiceTemplateRecord> SaveAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Out<InvoiceTemplateRecord>();
    public Task<InvoiceTemplateInfo> SetActiveAsync(InvoiceTemplateInfo original, bool active, CancellationToken cancellationToken = default) => Out<InvoiceTemplateInfo>();
    public Task<InvoiceTemplateInfo> UpdateDetailsAsync(InvoiceTemplateInfo original, InvoiceTemplateInput input, CancellationToken cancellationToken = default) => Out<InvoiceTemplateInfo>();
    public Task DeleteAsync(InvoiceTemplateInfo original, string reason, CancellationToken cancellationToken = default) => Out<object>();
    public Task<int> RenameSupplierAsync(string supplierCui, string oldName, string newName, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<InvoiceTemplateInfo> LinkSupplierAsync(InvoiceTemplateInfo original, Supplier supplier, CancellationToken cancellationToken = default) => Out<InvoiceTemplateInfo>();
}
