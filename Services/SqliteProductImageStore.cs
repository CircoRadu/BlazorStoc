using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed class SqliteProductImageStore(SqliteLocalStore store) : IProductImageStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> ExistsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null,
            "SELECT EXISTS(SELECT 1 FROM product_images WHERE product_id=@id)", ("@id", productId));
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<ProductImageData?> GetAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT relative_path,content_type,file_name FROM product_images WHERE product_id=@id
            """, ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var relativePath = reader.GetString(0);
        var contentType = reader.GetString(1);
        var fileName = reader.GetString(2);
        var path = Resolve(relativePath);
        if (!File.Exists(path)) return null;
        var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new(content, contentType, fileName);
    }

    public async Task SaveAsync(int productId, ProductImageData image, CancellationToken cancellationToken = default)
    {
        if (productId <= 0) throw new ProductImageException("Produsul trebuie salvat înaintea imaginii.");
        var contentType = ProductImageRules.DetectContentType(image.Content);
        var extension = ProductImageRules.Extension(contentType);
        var relativePath = $"product-{productId}-{Guid.NewGuid():N}{extension}";
        var destination = Resolve(relativePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(store.ProductImagesPath);
            await File.WriteAllBytesAsync(destination, image.Content, cancellationToken).ConfigureAwait(false);
            string? previousPath = null;
            try
            {
                await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using (var previous = SqliteLocalStore.Command(connection, null,
                    "SELECT relative_path FROM product_images WHERE product_id=@id", ("@id", productId)))
                    previousPath = await previous.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
                await using var command = SqliteLocalStore.Command(connection, null, """
                    INSERT INTO product_images(product_id,relative_path,content_type,file_name,byte_length,updated_utc)
                    VALUES(@id,@path,@contentType,@fileName,@length,@updated)
                    ON CONFLICT(product_id) DO UPDATE SET relative_path=excluded.relative_path,
                        content_type=excluded.content_type,file_name=excluded.file_name,
                        byte_length=excluded.byte_length,updated_utc=excluded.updated_utc
                    """, ("@id", productId), ("@path", relativePath), ("@contentType", contentType),
                    ("@fileName", Path.GetFileName(image.FileName)), ("@length", image.Content.LongLength),
                    ("@updated", DateTime.UtcNow.ToString("O")));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                File.Delete(destination);
                throw;
            }
            if (!string.IsNullOrWhiteSpace(previousPath) && !string.Equals(previousPath, relativePath, StringComparison.Ordinal))
            {
                var previous = Resolve(previousPath);
                if (File.Exists(previous)) File.Delete(previous);
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new ProductImageException("Produsul nu mai există. Actualizează catalogul înainte să salvezi imaginea.");
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(int productId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            string? relativePath;
            await using (var select = SqliteLocalStore.Command(connection, null,
                "SELECT relative_path FROM product_images WHERE product_id=@id", ("@id", productId)))
                relativePath = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            await using var delete = SqliteLocalStore.Command(connection, null,
                "DELETE FROM product_images WHERE product_id=@id", ("@id", productId));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                var path = Resolve(relativePath);
                if (File.Exists(path)) File.Delete(path);
            }
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
            string? relativePath = null, contentType = null, fileName = null;
            long byteLength = 0;
            await using (var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
            await using (var select = SqliteLocalStore.Command(connection, null, """
                SELECT relative_path,content_type,file_name,byte_length
                FROM product_images WHERE product_id=@id
                """, ("@id", productId)))
            await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    relativePath = reader.GetString(0);
                    contentType = reader.GetString(1);
                    fileName = reader.GetString(2);
                    byteLength = reader.GetInt64(3);
                }

            if (relativePath is not null)
                prepared = await ArchiveFileSafety.PrepareAsync(store.ProductImagesPath, store.ArchiveFilesPath,
                    relativePath, operation, "product_image", productId.ToString(), contentType!, fileName!,
                    byteLength, cancellationToken).ConfigureAwait(false);
            try
            {
                await commitDatabase(prepared, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ArchiveFileSafety.Rollback(store.ArchiveFilesPath, prepared);
                throw;
            }
            await ArchiveFileSafety.CompleteAsync(store.ProductImagesPath, store.ArchiveFilesPath, prepared,
                cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private string Resolve(string relativePath)
    {
        var root = Path.GetFullPath(store.ProductImagesPath) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(store.ProductImagesPath, relativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ProductImageException("Calea imaginii nu este validă.");
        return path;
    }
}
