namespace BlazorStoc.Services;

public sealed record ProductImageData(byte[] Content, string ContentType, string FileName);
public sealed class ProductImageException(string message) : Exception(message);

public interface IProductImageStore
{
    Task<bool> ExistsAsync(int productId, CancellationToken cancellationToken = default);
    Task<ProductImageData?> GetAsync(int productId, CancellationToken cancellationToken = default);
    Task SaveAsync(int productId, ProductImageData image, CancellationToken cancellationToken = default);
    Task DeleteAsync(int productId, CancellationToken cancellationToken = default);
    Task ArchiveDeleteAsync(int productId, ArchiveOperation operation,
        Func<ArchiveFileRecord?, CancellationToken, Task> commitDatabase,
        CancellationToken cancellationToken = default);
}

public static class ProductImageRules
{
    public const long MaximumBytes = 5 * 1024 * 1024;
    public const string PlaceholderUrl = "/images/product-placeholder.png";

    public static string DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaximumBytes) throw new ProductImageException("Imaginea poate avea cel mult 5 MB.");
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (content.Length >= 3 && content[0] == 255 && content[1] == 216 && content[2] == 255) return "image/jpeg";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.Length >= 6 && (content[..6].SequenceEqual("GIF87a"u8) || content[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        throw new ProductImageException("Formatul imaginii nu este acceptat. Folosește JPG, PNG, WebP sau GIF.");
    }

    public static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => throw new ProductImageException("Formatul imaginii nu este acceptat.")
    };
}

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

// Subtask 2.7 (Task 2): this store is only used in MariaDB mode (demo mode uses SqliteProductImageStore, which
// keeps images in the SQLite database itself). Directories come from MariaAssetPaths (Services/MariaTimeText.cs),
// shared with MariaProjectFileStore's archive directory, so an unset key here can never silently resolve to the
// app's own SQLite-mode "data/..." directories and the two stores never disagree about where archived files live.
public sealed class FileProductImageStore(IWebHostEnvironment environment, IConfiguration configuration) : IProductImageStore
{
    // Kept for constructor-shape parity with other stores/DI even though the directories no longer derive from it
    // (Subtask 2.7 - they come from MariaAssetPaths, never from the app's own ContentRootPath).
    private readonly IWebHostEnvironment unusedEnvironment = environment;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string root = MariaAssetPaths.ProductImages(configuration);
    private readonly string archiveRoot = MariaAssetPaths.ArchiveFiles(configuration);

    public async Task<bool> ExistsAsync(int productId, CancellationToken cancellationToken = default) =>
        await FindAsync(productId, cancellationToken).ConfigureAwait(false) is not null;

    public async Task<ProductImageData?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(root)) return null;
            var path = Directory.EnumerateFiles(root, $"product-{productId}.*")
                .FirstOrDefault(item => !item.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            if (path is null) return null;
            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return new(content, ProductImageRules.DetectContentType(content), Path.GetFileName(path));
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(int productId, ProductImageData image, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) throw new ProductImageException("Produsul trebuie salvat înaintea imaginii.");
        var contentType = ProductImageRules.DetectContentType(image.Content);
        var extension = ProductImageRules.Extension(contentType);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(root);
            var destination = Path.Combine(root, $"product-{productId}{extension}");
            var temporary = destination + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporary, image.Content, cancellationToken).ConfigureAwait(false);
            foreach (var existing in Directory.EnumerateFiles(root, $"product-{productId}.*").Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                File.Delete(existing);
            File.Move(temporary, destination, true);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(int productId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var path in Directory.EnumerateFiles(root, $"product-{productId}.*")) File.Delete(path);
        }
        finally { gate.Release(); }
    }

    public async Task ArchiveDeleteAsync(int productId, ArchiveOperation operation,
        Func<ArchiveFileRecord?, CancellationToken, Task> commitDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitDatabase);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ArchiveFileRecord? prepared = null;
        try
        {
            var path = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, $"product-{productId}.*")
                    .FirstOrDefault(item => !item.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                : null;
            if (path is not null)
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                prepared = await ArchiveFileSafety.PrepareAsync(root, archiveRoot, Path.GetFileName(path), operation,
                    "product_image", productId.ToString(), ProductImageRules.DetectContentType(bytes),
                    Path.GetFileName(path), bytes.LongLength, cancellationToken).ConfigureAwait(false);
            }
            try
            {
                await commitDatabase(prepared, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ArchiveFileSafety.Rollback(archiveRoot, prepared);
                throw;
            }
            await ArchiveFileSafety.CompleteAsync(root, archiveRoot, prepared, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private async Task<string?> FindAsync(int productId, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateFiles(root, $"product-{productId}.*")
                .FirstOrDefault(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }
        finally { gate.Release(); }
    }
}
