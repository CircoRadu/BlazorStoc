namespace BlazorStoc.Modules.Elcomtur;

// The entry points of the Elcomtur module behind the circuit breaker of the registry (a fault opens the circuit; the answers of the module, ElcomturOperationException and access denied, are not faults).
internal sealed class GuardedElcomturBalanceRepository(IElcomturBalanceRepository inner, ModuleRegistry modules) : IElcomturBalanceRepository
{
    private const string Id = ElcomturModule.ModuleId;
    public Task<BeneficiaryBalance?> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetAsync(beneficiaryId, cancellationToken));
    public Task<IReadOnlyList<BeneficiaryBalance>> GetOwedAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetOwedAsync(cancellationToken));
    public Task<ElcomturImportInfo?> GetInfoAsync(CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetInfoAsync(cancellationToken));
    public Task ReplaceAsync(string firm, DateOnly dataDate, IReadOnlyList<BeneficiaryBalance> balances, CancellationToken cancellationToken = default) =>
        modules.RunAsync(Id, () => inner.ReplaceAsync(firm, dataDate, balances, cancellationToken));
}

internal sealed class GuardedElcomturImportService(IElcomturImportService inner, ModuleRegistry modules) : IElcomturImportService
{
    private const string Id = ElcomturModule.ModuleId;
    public Task<string> UploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.UploadAsync(body, fileName, cancellationToken));
    public Task<IReadOnlyList<string>> GetFirmsAsync(string fileId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.GetFirmsAsync(fileId, cancellationToken));
    public Task<ElcomturReport> RunAsync(string fileId, string firm, bool apply, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.RunAsync(fileId, firm, apply, cancellationToken));
    public Task DiscardAsync(string fileId, CancellationToken cancellationToken = default) => modules.RunAsync(Id, () => inner.DiscardAsync(fileId, cancellationToken));
}

// Stand-ins used while the module is out: the pages that inject the services still load.
internal sealed class UnavailableElcomturBalanceRepository : IElcomturBalanceRepository
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ElcomturModule.ModuleId));
    public Task<BeneficiaryBalance?> GetAsync(int beneficiaryId, CancellationToken cancellationToken = default) => Out<BeneficiaryBalance?>();
    public Task<IReadOnlyList<BeneficiaryBalance>> GetOwedAsync(CancellationToken cancellationToken = default) => Out<IReadOnlyList<BeneficiaryBalance>>();
    public Task<ElcomturImportInfo?> GetInfoAsync(CancellationToken cancellationToken = default) => Out<ElcomturImportInfo?>();
    public Task ReplaceAsync(string firm, DateOnly dataDate, IReadOnlyList<BeneficiaryBalance> balances, CancellationToken cancellationToken = default) => Out<object>();
}

internal sealed class UnavailableElcomturImportService : IElcomturImportService
{
    private static Task<T> Out<T>() => Task.FromException<T>(new ModuleUnavailableException(ElcomturModule.ModuleId));
    public Task<string> UploadAsync(Stream body, string fileName, CancellationToken cancellationToken = default) => Out<string>();
    public Task<IReadOnlyList<string>> GetFirmsAsync(string fileId, CancellationToken cancellationToken = default) => Out<IReadOnlyList<string>>();
    public Task<ElcomturReport> RunAsync(string fileId, string firm, bool apply, CancellationToken cancellationToken = default) => Out<ElcomturReport>();
    public Task DiscardAsync(string fileId, CancellationToken cancellationToken = default) => Out<object>();
}
