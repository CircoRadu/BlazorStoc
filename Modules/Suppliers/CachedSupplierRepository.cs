namespace BlazorStoc.Services;

// The register of suppliers for one circuit (scoped): the list is read once and kept for a short time, because the pickup and the template pages
// ask for it several times while an invoice is processed. Every write through this circuit drops it; another user's change shows within the time.
public sealed class CachedSupplierRepository(ISupplierRepository inner, TimeProvider? clock = null) : ISupplierRepository
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private IReadOnlyList<Supplier>? cached;
    private DateTimeOffset readAt;

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken cancellationToken = default)
    {
        if (cached is not null && time.GetUtcNow() - readAt < Lifetime) return cached;
        var list = await inner.GetSuppliersAsync(cancellationToken).ConfigureAwait(false);
        cached = list; readAt = time.GetUtcNow();
        return list;
    }

    public Task<Supplier?> GetAsync(int id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);

    public async Task<Supplier> CreateAsync(SupplierInput input, CancellationToken cancellationToken = default) { try { return await inner.CreateAsync(input, cancellationToken).ConfigureAwait(false); } finally { cached = null; } }
    public async Task<Supplier> UpdateAsync(Supplier original, SupplierInput input, CancellationToken cancellationToken = default) { try { return await inner.UpdateAsync(original, input, cancellationToken).ConfigureAwait(false); } finally { cached = null; } }
    public async Task DeleteAsync(Supplier original, string reason, CancellationToken cancellationToken = default) { try { await inner.DeleteAsync(original, reason, cancellationToken).ConfigureAwait(false); } finally { cached = null; } }
    public async Task<Supplier> AddAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) { try { return await inner.AddAliasAsync(supplier, alias, cancellationToken).ConfigureAwait(false); } finally { cached = null; } }
    public async Task<Supplier> RemoveAliasAsync(Supplier supplier, string alias, CancellationToken cancellationToken = default) { try { return await inner.RemoveAliasAsync(supplier, alias, cancellationToken).ConfigureAwait(false); } finally { cached = null; } }
}
