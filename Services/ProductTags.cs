using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Tags of a subcategory (migration 53): each product of the subcategory has every tag On or Off. A tag is never required, is not part of the product code
// and never blocks the stock; the catalog filters by the tags a product has On.
public sealed record SubcategoryTag(int Id, string Category, string Subcategory, string Name, int Position, long Version, int ProductCount);

// A tag taken out of use (migration 56): the products keep their On/Off, but the tag is no longer offered, shown or filtered by. Unused = no product has it.
public sealed record ArchivedTag(int Id, string Category, string Subcategory, string Name, long Version, string ArchivedUtc, string ArchivedBy, int ProductCount)
{
    public bool Unused => ProductCount == 0;
}

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
    // Archive (administrators only): a tag some product has is archived instead of deleted; an archived tag no product has can be deleted for good, and one that
    // products still have can have its restore given up once the retention period set in Catalog has passed (the products lose it).
    Task<IReadOnlyList<ArchivedTag>> GetArchivedTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArchivedTag>>([]);
    Task ArchiveTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task RestoreTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task GiveUpTagRestoreAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task DeleteArchivedTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
            SELECT t.id,c.name,COALESCE(s.name,''),t.name,t.position,t.version,(SELECT COUNT(*) FROM product_tags pt WHERE pt.tag_id=t.id)
            FROM subcategory_tags t LEFT JOIN subcategories s ON s.id=t.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,t.category_id)
            WHERE t.archived_utc IS NULL
            ORDER BY c.name,COALESCE(s.name,''),t.position,t.id
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
        (SubcategoryTag Tag, DefinitionOwner Owner) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var owner = await DefinitionOwners.FindAsync(connection, transaction, group, cancellationToken).ConfigureAwait(false);
                // The name is unique across the ancestors and the descendants too: a child cannot repeat an inherited tag; a category's tag is inherited by its whole tree.
                var (scope, scopeParameters) = await DefinitionOwners.NameScopeAsync(connection, transaction, owner, cancellationToken).ConfigureAwait(false);
                await using (var exists = Command(connection, transaction, $"SELECT 1 FROM subcategory_tags WHERE {scope} AND normalized_name=@key",
                                 [("@key", TextNormalization.UniquenessKey(cleanName)), .. scopeParameters]))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"{owner.Label} are deja o etichetă «{cleanName}».");
                await using var position = Command(connection, transaction, $"SELECT COALESCE(MAX(position),0)+1 FROM subcategory_tags WHERE {owner.Column}=@owner", ("@owner", owner.ColumnValue));
                var next = Convert.ToInt32(await position.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                await using var insert = Command(connection, transaction, $"INSERT INTO subcategory_tags({owner.Column},name,normalized_name,position,version) VALUES(@owner,@name,@key,@position,0)",
                    ("@owner", owner.ColumnValue), ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@position", next));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return (new SubcategoryTag(checked((int)insert.LastInsertedId), group.Category, group.Subcategory, cleanName, next, 0, 0), owner);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"{(string.IsNullOrWhiteSpace(group.Subcategory) ? "Categoria" : "Subcategoria")} «{(string.IsNullOrWhiteSpace(group.Subcategory) ? group.Category : group.Subcategory)}» are deja o etichetă «{cleanName}».");
        }
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, result.Owner.AuditEntity, AuditActions.AddSubcategoryTag,
            result.Owner.AuditId, result.Owner.Target, AuditDetails.Identification(("Etichetă", cleanName)), string.Empty, cancellationToken).ConfigureAwait(false);
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
                var owner = await DefinitionOwners.OfRowAsync(connection, transaction, "subcategory_tags", original.Id, cancellationToken).ConfigureAwait(false);
                var (scope, scopeParameters) = await DefinitionOwners.NameScopeAsync(connection, transaction, owner, cancellationToken).ConfigureAwait(false);
                await using (var exists = Command(connection, transaction, $"SELECT 1 FROM subcategory_tags WHERE {scope} AND normalized_name=@key AND id<>@id",
                                 [("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id), .. scopeParameters]))
                    if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
                        throw new ProductOperationException($"{owner.Label} are deja o etichetă «{cleanName}».");
                await using var update = Command(connection, transaction, "UPDATE subcategory_tags SET name=@name,normalized_name=@key,version=version+1 WHERE id=@id",
                    ("@name", cleanName), ("@key", TextNormalization.UniquenessKey(cleanName)), ("@id", original.Id));
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return sub;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            throw new ProductOperationException($"{(original.Subcategory.Length == 0 ? "Categoria" : "Subcategoria")} «{(original.Subcategory.Length == 0 ? original.Category : original.Subcategory)}» are deja o etichetă «{cleanName}».");
        }
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, OwnerKey.Entity(subcategoryId), OwnerKey.Id(subcategoryId),
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
            // A tag some product has is never deleted: it is archived (migration 56).
            if (count > 0) throw new ProductOperationException(ParameterArchiveRules.UsedCannotDeleteMessage);
            await using var delete = Command(connection, transaction, "DELETE FROM subcategory_tags WHERE id=@id", ("@id", original.Id));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (sub, count);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(subcategoryId), AuditActions.DeleteSubcategoryTag, OwnerKey.Id(subcategoryId),
            Target(original.Category, original.Subcategory), AuditDetails.Identification(("Etichetă", original.Name), ("Produse care o aveau", products.ToString(CultureInfo.InvariantCulture))), motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> GetProductTagIdsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT pt.tag_id FROM product_tags pt INNER JOIN subcategory_tags t ON t.id=pt.tag_id WHERE pt.product_id=@id AND t.archived_utc IS NULL ORDER BY pt.tag_id", ("@id", productId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ids = new List<int>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(checked((int)reader.GetInt64(0)));
        return ids;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetAllProductTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, "SELECT pt.product_id,pt.tag_id FROM product_tags pt INNER JOIN subcategory_tags t ON t.id=pt.tag_id WHERE t.archived_utc IS NULL ORDER BY pt.product_id,pt.tag_id");
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
            string name; int subcategoryId, categoryId;
            await using (var product = Command(connection, transaction, "SELECT name,subcategory_id,category_id FROM products WHERE id=@id FOR UPDATE", ("@id", productId)))
            await using (var reader = await product.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) throw new ProductOperationException("Produsul nu mai există. Actualizează lista și reia operația.");
                (name, subcategoryId, categoryId) = (reader.GetString(0), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)));
            }
            var own = new Dictionary<int, string>();
            // The tags of the subcategory and of every ancestor (migration 54).
            var chainList = SubcategoryAncestry.InList(await SubcategoryAncestry.ChainAsync(connection, transaction, subcategoryId, cancellationToken).ConfigureAwait(false));
            await using (var tags = Command(connection, transaction, $"SELECT id,name FROM subcategory_tags WHERE {DefinitionOwners.ChainSql(chainList)} AND archived_utc IS NULL", ("@chainCategory", categoryId)))
            await using (var reader = await tags.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) own[checked((int)reader.GetInt64(0))] = reader.GetString(1);
            wanted.IntersectWith(own.Keys);
            var current = new HashSet<int>();
            await using (var existing = Command(connection, transaction, "SELECT pt.tag_id FROM product_tags pt INNER JOIN subcategory_tags t ON t.id=pt.tag_id WHERE pt.product_id=@id AND t.archived_utc IS NULL", ("@id", productId)))
            await using (var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) current.Add(checked((int)reader.GetInt64(0)));
            if (current.SetEquals(wanted)) return null;
            // The links to archived tags stay (they come back with the tag).
            await using (var clear = Command(connection, transaction, "DELETE pt FROM product_tags pt INNER JOIN subcategory_tags t ON t.id=pt.tag_id WHERE pt.product_id=@id AND t.archived_utc IS NULL", ("@id", productId)))
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

    // ---- archive (migration 56) ---------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ArchivedTag>> GetArchivedTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = Command(connection, null, """
            SELECT t.id,c.name,COALESCE(s.name,''),t.name,t.version,t.archived_utc,COALESCE(t.archived_by,''),(SELECT COUNT(*) FROM product_tags pt WHERE pt.tag_id=t.id)
            FROM subcategory_tags t LEFT JOIN subcategories s ON s.id=t.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,t.category_id)
            WHERE t.archived_utc IS NOT NULL ORDER BY c.name,COALESCE(s.name,''),t.position,t.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<ArchivedTag>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ArchivedTag(checked((int)reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6), checked((int)reader.GetInt64(7))));
        return result;
    }

    private sealed record TagRow(long Version, int SubcategoryId, string? ArchivedUtc, string Name, string Target, int Products);

    private static async Task<TagRow> LockTagRowAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT t.version,COALESCE(t.subcategory_id,-t.category_id),t.archived_utc,t.name,c.name,COALESCE(s.name,''),(SELECT COUNT(*) FROM product_tags pt WHERE pt.tag_id=t.id)
            FROM subcategory_tags t LEFT JOIN subcategories s ON s.id=t.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,t.category_id) WHERE t.id=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Eticheta nu mai există. Actualizează lista și reia operația.");
        return new TagRow(reader.GetInt64(0), checked((int)reader.GetInt64(1)), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), Target(reader.GetString(4), reader.GetString(5)),
            checked((int)reader.GetInt64(6)));
    }

    private async Task<(TagRow Row, int Changed)> ChangeStateAsync(int id, long version, bool expectArchived, string setSql, string reason, string action, string verb, CancellationToken token,
        Func<MySqlConnection, MySqlTransaction, TagRow, Task<int>>? extra = null)
    {
        await (accessControl?.EnsureAdministratorAsync(token) ?? Task.CompletedTask).ConfigureAwait(false);
        var by = accessControl is null ? "" : await accessControl.GetUsernameAsync(token).ConfigureAwait(false) ?? "";
        var (row, changed) = await WriteAsync(async (connection, transaction) =>
        {
            var tag = await LockTagRowAsync(connection, transaction, id, token).ConfigureAwait(false);
            if (tag.Version != version) throw new ProductOperationException("Eticheta a fost modificată între timp. Actualizează lista și reia operația.");
            if ((tag.ArchivedUtc is not null) != expectArchived)
                throw new ProductOperationException(expectArchived ? ParameterArchiveRules.NotArchivedMessage : ParameterArchiveRules.AlreadyArchivedMessage);
            var changed = extra is null ? 0 : await extra(connection, transaction, tag).ConfigureAwait(false);
            if (setSql.Length > 0)
            {
                await using var update = Command(connection, transaction, setSql, ("@now", MariaTimeText.Format(DateTime.UtcNow)), ("@by", by), ("@id", id));
                await update.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            return (tag, changed);
        }, token).ConfigureAwait(false);
        await AuditRecorder.RecordActionAsync(auditTrail, accessControl, OwnerKey.Entity(row.SubcategoryId), action, OwnerKey.Id(row.SubcategoryId), row.Target,
            AuditDetails.Identification(("Etichetă", row.Name), (verb, (changed == 0 ? row.Products : changed).ToString(CultureInfo.InvariantCulture))), reason, token).ConfigureAwait(false);
        return (row, changed);
    }

    public async Task ArchiveTagAsync(SubcategoryTag original, string reason, CancellationToken cancellationToken = default) =>
        await ChangeStateAsync(original.Id, original.Version, false, "UPDATE subcategory_tags SET archived_utc=@now,archived_by=@by,version=version+1 WHERE id=@id",
            ProductGroupManagementRules.Reason(reason), AuditActions.ArchiveSubcategoryTag, "Produse care o au", cancellationToken).ConfigureAwait(false);

    public async Task RestoreTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) =>
        await ChangeStateAsync(original.Id, original.Version, true, "UPDATE subcategory_tags SET archived_utc=NULL,archived_by=NULL,version=version+1 WHERE id=@id",
            ProductGroupManagementRules.Reason(reason), AuditActions.RestoreSubcategoryTag, "Produse care o au", cancellationToken).ConfigureAwait(false);

    public async Task GiveUpTagRestoreAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) =>
        await ChangeStateAsync(original.Id, original.Version, true, "UPDATE subcategory_tags SET version=version+1 WHERE id=@id",
            ProductGroupManagementRules.Reason(reason), AuditActions.GiveUpTagRestore, "Produse care o aveau", cancellationToken, async (connection, transaction, tag) =>
            {
                var retention = await MariaCatalogSettings.ReadRetentionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                var allowed = ParameterArchiveRules.GiveUpAllowedFrom(MariaTimeText.Parse(tag.ArchivedUtc!), retention);
                if (DateTime.UtcNow < allowed) throw new ProductOperationException(ParameterArchiveRules.GiveUpTooEarlyMessage(allowed));
                await using var drop = Command(connection, transaction, "DELETE FROM product_tags WHERE tag_id=@id", ("@id", original.Id));
                return await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

    public async Task DeleteArchivedTagAsync(ArchivedTag original, string reason, CancellationToken cancellationToken = default) =>
        await ChangeStateAsync(original.Id, original.Version, true, "", ProductGroupManagementRules.Reason(reason), AuditActions.DeleteUnusedTag, "Produse care o aveau", cancellationToken,
            async (connection, transaction, tag) =>
            {
                if (tag.Products > 0) throw new ProductOperationException(ParameterArchiveRules.StillLinkedMessage);
                await using var delete = Command(connection, transaction, "DELETE FROM subcategory_tags WHERE id=@id", ("@id", original.Id));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);

    private static string Target(string category, string subcategory) => subcategory.Length == 0 ? category : $"{category} / {subcategory}";

    private static async Task<(long Version, int SubcategoryId)> LockTagAsync(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT version,COALESCE(subcategory_id,-category_id) FROM subcategory_tags WHERE id=@id FOR UPDATE", ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Eticheta nu mai există. Actualizează lista și reia operația.");
        return (reader.GetInt64(0), checked((int)reader.GetInt64(1)));
    }

    private Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token) =>
        MariaDb.WriteAsync(configuration, action, message => new ProductOperationException(message), token,
            "Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
}
