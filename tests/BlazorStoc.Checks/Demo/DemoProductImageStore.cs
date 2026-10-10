// Test double of the in-memory suite (tests/BlazorStoc.Checks): an in-memory implementation of the repository used instead of MariaDB.
namespace BlazorStoc.Services;

public sealed class DemoProductImageStore : IProductImageStore
{
    private readonly object gate = new();
    private readonly Dictionary<int, ProductImageData> images = [];

    public Task<bool> ExistsAsync(int productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(images.ContainsKey(productId));
    }

    public Task<ProductImageData?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(images.GetValueOrDefault(productId));
    }

    public Task SaveAsync(int productId, ProductImageData image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentType = ProductImageRules.DetectContentType(image.Content);
        lock (gate) images[productId] = image with { ContentType = contentType, Content = image.Content.ToArray() };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) images.Remove(productId);
        return Task.CompletedTask;
    }

    public async Task ArchiveDeleteAsync(int productId, ArchiveOperation operation,
        Func<ArchiveFileRecord?, CancellationToken, Task> commitDatabase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commitDatabase);
        await commitDatabase(null, cancellationToken).ConfigureAwait(false);
        lock (gate) images.Remove(productId);
    }
}
