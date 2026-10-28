using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Tags of a subcategory (migration 53): each product of the subcategory has every tag On or Off. A tag is never required, is not part of the product code
// and never blocks the stock; the catalog filters by the tags a product has On.
public sealed record SubcategoryTag(int Id, string Category, string Subcategory, string Name, int Position, long Version, int ProductCount);

public interface IProductTagRepository
{
    // Every tag of every subcategory with the number of products that have it On.
    Task<IReadOnlyList<SubcategoryTag>> GetTagsAsync(CancellationToken cancellationToken = default);
    // The product operators add tags; the administrators rename and delete them (a tag may be deleted while products have it: they just lose it).
    Task<SubcategoryTag> AddTagAsync(ProductGroup group, string name, CancellationToken cancellationToken = default);
    Task<SubcategoryTag> RenameTagAsync(SubcategoryTag original, string name, string reason, CancellationToken cancellationToken = default);
    Task DeleteTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default);
    // The tags a product has On, and for every product at once (the catalog filters by them).
    Task<IReadOnlyList<int>> GetProductTagIdsAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetAllProductTagsAsync(CancellationToken cancellationToken = default);
    // Replaces the tags of a product with the given ones (only tags of its own subcategory count); journaled when something changed.
    Task SetProductTagsAsync(int productId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default);
}

public static class ProductTagRules
{
    public const int NameMaximumLength = 50;

    public static string Name(string? value)
    {
        var name = TextNormalization.ForObjectNameOrCode(value);
        if (name.Length == 0) throw new ProductOperationException("Completează denumirea etichetei.");
        if (name.Length > NameMaximumLength) throw new ProductOperationException($"Denumirea etichetei poate avea cel mult {NameMaximumLength} de caractere.");
        return name;
    }
}

public sealed class MariaProductTagRepository(IConfiguration configuration, IAccessControl? accessControl = null, IAuditTrail? auditTrail = null) : IProductTagRepository
{
    public async Task<IReadOnlyList<SubcategoryTag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT t.id,c.name,s.name,t.name,t.position,t.version,(SELECT COUNT(*) FROM product_tags pt WHERE pt.tag_id=t.id)
            FROM subcategory_tags t INNER JOIN subcategories s ON s.id=t.subcategory_id INNER JOIN categories c ON c.id=s.category_id
            ORDER BY c.name,s.name,t.position,t.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<SubcategoryTag>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new SubcategoryTag(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt64(5), checked((int)reader.GetInt64(6))));
        return result;
    }

    public async Task<SubcategoryTag> AddTagAsync(ProductGroup group, string name, CancellationToken cancellationToken = default)
    {
        await (accessControl?.EnsureAsync("categorii.edit", cancellationToken) ?? Task.CompletedTask).ConfigureAwait(false);
        var cleanName = ProductTagRules.Name(name);
        (SubcategoryTag Tag, int SubcategoryId) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var subcategoryId = await FindSubcategoryAsync(connection, transaction, group, cancellationToken).ConfigureAwait(false);
                // The name is unique across the ancestors and the descendants too: a child cannot repeat an inherited tag.
                var scope = SubcategoryAncestry.InList(await SubcategoryAncestry.NameScopeAsync(connection, transaction, subcategoryId, cancellationToken).ConfigureAwait(false));
                await using (var exists = Command(connection, transaction, $"SELECT 1 FROM subcategory_tags WHERE subcategory_id IN ({scope}) AND normalized_name=@key",
                                 ("@key", TextNormalization.UniquenessKey(cleanName))))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o etichetă «{cleanName}».");
                await using var position = Command(connection, transaction, "SELECT COALESCE(MAX(position),0)+1 FROM subcategory_tags WHERE subcategory_id=@sub", ("@sub", subcategoryId));
                var next = Convert.ToInt32(await position.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, "INSERT INTO subcategory_tags(subcategory_id,name,normalized_name,position,version) VALUES(@sub,@name,@key,@position,0)",
                    ("@sub", subcategoryId), ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@position", next));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (new SubcategoryTag(checked((int)insert.LastInsertedId), group.Category, group.Subcategory, cleanName, next, 0, 0), subcategoryId);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{group.Subcategory}» are deja o etichetă «{cleanName}».");
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.AddSubcategoryTag,
            result.SubcategoryId.ToString(CultureInfo.InvariantCulture), Target(group.Category, group.Subcategory), AuditDetails.Identification(("Etichetă", cleanName)), string.Empty, cancellationToken).ConfigureAwait(false);
        return result.Tag;
    }

    public async Task<SubcategoryTag> RenameTagAsync(SubcategoryTag original, string name, string reason, CancellationToken cancellationToken = default)
    {
        await (accessControl?.EnsureAdministratorAsync(cancellationToken) ?? Task.CompletedTask).ConfigureAwait(false);
        var cleanName = ProductTagRules.Name(name);
        var motif = ProductGroupManagementRules.Reason(reason);
        int subcategoryId;
        try
        {
            subcategoryId = await WriteAsync(async (connection, transaction) =>
            {
                var (version, sub) = await LockTagAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                if (version != original.Version) throw new ProductOperationException("Eticheta a fost modificată între timp. Actualizează lista și reia operația.");
                if (string.Equals(original.Name, cleanName, StringComparison.Ordinal)) throw new ProductOperationException("Nu ai modificat nimic.");
                var scope = SubcategoryAncestry.InList(await SubcategoryAncestry.NameScopeAsync(connection, transaction, sub, cancellationToken).ConfigureAwait(false));
                await using (var exists = Command(connection, transaction, $"SELECT 1 FROM subcategory_tags WHERE subcategory_id IN ({scope}) AND normalized_name=@key AND id<>@id",
                                 ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id)))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja o etichetă «{cleanName}».");
                await using var update = Command(connection, transaction, "UPDATE subcategory_tags SET name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                    ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return sub;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"Subcategoria «{original.Subcategory}» are deja o etichetă «{cleanName}».");
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory, subcategoryId.ToString(CultureInfo.InvariantCulture),
            Target(original.Category, original.Subcategory), [new("Denumire etichetă", original.Name, cleanName)], motif, cancellationToken, AuditActions.RenameSubcategoryTag).ConfigureAwait(false);
        return original with { Name = cleanName, Version = original.Version + 1 };
    }

    public async Task DeleteTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default)
    {
        await (accessControl?.EnsureAdministratorAsync(cancellationToken) ?? Task.CompletedTask).ConfigureAwait(false);
        var motif = ProductGroupManagementRules.Reason(reason);
        var (subcategoryId, products) = await WriteAsync(async (connection, transaction) =>
        {
            var (version, sub) = await LockTagAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            if (version != original.Version) throw new ProductOperationException("Eticheta a fost modificată între timp. Actualizează lista și reia operația.");
            int count;
            await using (var used = Command(connection, transaction, "SELECT COUNT(*) FROM product_tags WHERE tag_id=@id", ("@id", original.Id)))
                count = Convert.ToInt32(await used.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            await using var delete = Command(connection, transaction, "DELETE FROM subcategory_tags WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (sub, count);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, AuditEntities.Subcategory, AuditActions.DeleteSubcategoryTag, subcategoryId.ToString(CultureInfo.InvariantCulture),
            Target(original.Category, original.Subcategory), AuditDetails.Identification(("Etichetă", original.Name), ("Produse care o aveau", products.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> GetProductTagIdsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT tag_id FROM product_tags WHERE product_id=@id ORDER BY tag_id", ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(checked((int)reader.GetInt64(0)));
        return ids;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetAllProductTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT product_id,tag_id FROM product_tags ORDER BY product_id,tag_id");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var byProduct = new Dictionary<int, List<int>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var productId = checked((int)reader.GetInt64(0));
            if (!byProduct.TryGetValue(productId, out var list)) byProduct[productId] = list = [];
            list.Add(checked((int)reader.GetInt64(1)));
        }
        return byProduct.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value);
    }

    public async Task SetProductTagsAsync(int productId, IReadOnlyCollection<int> tagIds, CancellationToken cancellationToken = default)
    {
        if (accessControl is not null && !await accessControl.HasAsync("produse.edit", cancellationToken).ConfigureAwait(false)
            && !await accessControl.HasAsync("produse.add", cancellationToken).ConfigureAwait(false))
            await accessControl.EnsureAsync("produse.edit", cancellationToken).ConfigureAwait(false);
        var wanted = tagIds.Distinct().ToHashSet();
        var changed = await WriteAsync<(string, string, string)?>(async (connection, transaction) =>
        {
            string name; int subcategoryId;
            await using (var product = Command(connection, transaction, "SELECT name,subcategory_id FROM products WHERE id=@id FOR UPDATE", ("@id", productId)))
            await using (var reader = await product.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException("Produsul nu mai există. Actualizează lista și reia operația.");
                (name, subcategoryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)));
            }
            var own = new Dictionary<int, string>();
            // The tags of the subcategory and of every ancestor (migration 54).
            var chainList = SubcategoryAncestry.InList(await SubcategoryAncestry.ChainAsync(connection, transaction, subcategoryId, cancellationToken).ConfigureAwait(false));
            await using (var tags = Command(connection, transaction, $"SELECT id,name FROM subcategory_tags WHERE subcategory_id IN ({chainList})"))
            await using (var reader = await tags.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) own[checked((int)reader.GetInt64(0))] = reader.GetString(1);
            wanted.IntersectWith(own.Keys);
            var current = new HashSet<int>();
            await using (var existing = Command(connection, transaction, "SELECT tag_id FROM product_tags WHERE product_id=@id", ("@id", productId)))
            await using (var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) current.Add(checked((int)reader.GetInt64(0)));
            if (current.SetEquals(wanted)) return null;
            await using (var clear = Command(connection, transaction, "DELETE FROM product_tags WHERE product_id=@id", ("@id", productId)))
                await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var id in wanted)
            {
                await using var insert = Command(connection, transaction, "INSERT INTO product_tags(product_id,tag_id) VALUES(@product,@tag)", ("@product", productId), ("@tag", id));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            string Names(IEnumerable<int> ids) => ids.Count() == 0 ? "—" : string.Join(", ", ids.Where(own.ContainsKey).Select(id => own[id]).Order(StringComparer.CurrentCultureIgnoreCase));
            // A tag that is gone (deleted since) is skipped from the old text.
            return (name, Names(current), Names(wanted));
        }, cancellationToken).ConfigureAwait(false);
        if (changed is { } value)
            await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, productId.ToString(CultureInfo.InvariantCulture), value.Item1,
                [new("Etichete", value.Item2, value.Item3)], string.Empty, cancellationToken, AuditActions.EditProductTags).ConfigureAwait(false);
    }

    private static string Target(string category, string subcategory) => $"{category} / {subcategory}";

    private static async Task<int> FindSubcategoryAsync(MySqlConnection connection, MySqlTransaction transaction, ProductGroup group, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT s.id FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
            WHERE s.normalized_name=@sub AND c.normalized_name=@cat
            """, ("@sub", TextNormalization.UniquenessKey(group.Subcategory)), ("@cat", TextNormalization.UniquenessKey(group.Category)));
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is long id
            ? checked((int)id)
            : throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista și reia operația.");
    }

    private static async Task<(long Version, int SubcategoryId)> LockTagAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT version,subcategory_id FROM subcategory_tags WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Eticheta nu mai există. Actualizează lista și reia operația.");
        return (reader.GetInt64(0), checked((int)reader.GetInt64(1)));
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ProductOperationException(message), token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
}
