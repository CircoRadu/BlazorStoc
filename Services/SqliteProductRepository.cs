using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed partial class SqliteProductRepository(SqliteLocalStore store, IAccessControl? accessControl = null,
    IArchiveService? archiveService = null, IProductImageStore? imageStore = null) : IProductRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly IProductImageStore images = imageStore ?? new SqliteProductImageStore(store);
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT p.id,c.name,s.name,p.name,p.description,p.quantity,p.version
            FROM products p
            INNER JOIN categories c ON c.id=p.category_id
            INNER JOIN subcategories s ON s.id=p.subcategory_id
            ORDER BY s.name,p.name,p.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) products.Add(ReadProduct(reader));
        return products;
    }

    public async Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = SqliteLocalStore.Command(connection, null, """
            SELECT c.name,COALESCE(s.name,'')
            FROM categories c LEFT JOIN subcategories s ON s.category_id=c.id
            ORDER BY c.name,s.name
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var groups = new List<ProductGroup>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            groups.Add(new(reader.GetString(0), reader.GetString(1)));
        return groups;
    }

    public async Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureUniqueProductNameAsync(connection, transaction, value.Name, null, cancellationToken).ConfigureAwait(false);
            var group = await ResolveGroupAsync(connection, transaction, value.Category, value.Subcategory, cancellationToken).ConfigureAwait(false);
            await using var insert = SqliteLocalStore.Command(connection, transaction, """
                INSERT INTO products(category_id,subcategory_id,name,normalized_name,description,quantity,version)
                VALUES(@category,@subcategory,@name,@normalized,@description,@quantity,0);
                SELECT last_insert_rowid();
                """, ("@category", group.CategoryId), ("@subcategory", group.SubcategoryId),
                ("@name", value.Name), ("@normalized", TextNormalization.UniquenessKey(value.Name)),
                ("@description", value.Description), ("@quantity", value.Quantity));
            var id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var product = new Product(id, group.Category, group.Subcategory, value.Name, value.Description, value.Quantity);
            await RecordGroupsAsync(connection, transaction, actor, group, cancellationToken).ConfigureAwait(false);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Product, AuditActions.Create, $"#{product.Id} · {product.Name}", AuditDetails.Identification(
                    ("Denumire", product.Name), ("Categorie", product.Category), ("Subcategorie", product.Subcategory),
                    ("Cantitate", product.Quantity.ToString())), string.Empty, product.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return product;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(original);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            ProductRules.CheckCurrent(await GetAsync(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false), original);
            await EnsureUniqueProductNameAsync(connection, transaction, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
            var group = await ResolveGroupAsync(connection, transaction, value.Category, value.Subcategory, cancellationToken).ConfigureAwait(false);
            var version = checked(original.Version + 1);
            await using var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE products SET category_id=@category,subcategory_id=@subcategory,name=@name,
                    normalized_name=@normalized,description=@description,quantity=@quantity,version=@version
                WHERE id=@id AND version=@oldVersion
                """, ("@category", group.CategoryId), ("@subcategory", group.SubcategoryId),
                ("@name", value.Name), ("@normalized", TextNormalization.UniquenessKey(value.Name)),
                ("@description", value.Description), ("@quantity", value.Quantity), ("@version", version),
                ("@id", original.Id), ("@oldVersion", original.Version));
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProductOperationException("Produsul s-a schimbat între timp. Actualizează catalogul.");
            var product = new Product(original.Id, group.Category, group.Subcategory, value.Name, value.Description, value.Quantity, version);
            await RecordGroupsAsync(connection, transaction, actor, group, cancellationToken).ConfigureAwait(false);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Product, AuditActions.Edit, $"#{product.Id} · {product.Name}",
                AuditDetails.Changes(ProductAuditChanges(original, product)), value.Reason, product.Id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return product;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError) throw new ProductOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Product(original, motif), async (operation, token) =>
        {
            await images.ArchiveDeleteAsync(original.Id, operation, async (file, archiveToken) =>
            {
                await using var connection = await store.OpenConnectionAsync(archiveToken).ConfigureAwait(false);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable, archiveToken).ConfigureAwait(false);
                try
                {
                    var current = await GetAsync(connection, transaction, original.Id, archiveToken).ConfigureAwait(false);
                    ProductRules.CheckCurrent(current, original);
                    await using (var relations = SqliteLocalStore.Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE product_id=@id)", ("@id", original.Id)))
                        ProductRules.CheckDelete(original,
                            Convert.ToBoolean(await relations.ExecuteScalarAsync(archiveToken).ConfigureAwait(false)));
                    await SqliteArchivePersistence.InsertAsync(connection, transaction, operation, file, archiveToken)
                        .ConfigureAwait(false);
                    await using var delete = SqliteLocalStore.Command(connection, transaction,
                        "DELETE FROM products WHERE id=@id AND version=@version",
                        ("@id", original.Id), ("@version", original.Version));
                    if (await delete.ExecuteNonQueryAsync(archiveToken).ConfigureAwait(false) != 1)
                        throw new ProductOperationException("Produsul s-a schimbat între timp. Actualizează catalogul.");
                    await SqliteLocalStore.InsertAuditAsync(connection, transaction,
                        new(operation.ActorUsername, operation.ActorRole, AuditEntities.Product, AuditActions.Delete,
                            operation.Request.Target, operation.Request.Details, operation.Request.Motif,
                            original.Id.ToString(), operation.Id), archiveToken).ConfigureAwait(false);
                    await transaction.CommitAsync(archiveToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Product?> GetAsync(SqliteConnection connection, SqliteTransaction transaction, int id,
        CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT p.id,c.name,s.name,p.name,p.description,p.quantity,p.version
            FROM products p INNER JOIN categories c ON c.id=p.category_id
            INNER JOIN subcategories s ON s.id=p.subcategory_id WHERE p.id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadProduct(reader) : null;
    }

    private static Product ReadProduct(SqliteDataReader reader) => new(reader.GetInt32(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6));

    private static async Task EnsureUniqueProductNameAsync(SqliteConnection connection, SqliteTransaction transaction,
        string name, int? excludedId, CancellationToken token)
    {
        await using var command = SqliteLocalStore.Command(connection, transaction, """
            SELECT p.name,c.name,s.name FROM products p
            INNER JOIN categories c ON c.id=p.category_id INNER JOIN subcategories s ON s.id=p.subcategory_id
            WHERE p.normalized_name=@normalized AND (@id IS NULL OR p.id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(name)), ("@id", excludedId));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new ProductOperationException($"Produsul «{reader.GetString(0)}» există deja în categoria «{reader.GetString(1)}», subcategoria «{reader.GetString(2)}».");
    }

    private static async Task<(int CategoryId, int SubcategoryId, string Category, string Subcategory,
        bool CategoryCreated, bool SubcategoryCreated)> ResolveGroupAsync(SqliteConnection connection,
        SqliteTransaction transaction, string category, string subcategory, CancellationToken token)
    {
        var categoryKey = TextNormalization.UniquenessKey(category);
        int categoryId;
        await using (var selectCategory = SqliteLocalStore.Command(connection, transaction,
            "SELECT id,name FROM categories WHERE normalized_name=@key", ("@key", categoryKey)))
        await using (var reader = await selectCategory.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                categoryId = reader.GetInt32(0);
                category = reader.GetString(1);
            }
            else
                throw new ProductOperationException("Categoria selectată nu mai există. Actualizează lista și reia salvarea.");
        }

        var subcategoryKey = TextNormalization.UniquenessKey(subcategory);
        await using (var selectSubcategory = SqliteLocalStore.Command(connection, transaction, """
            SELECT s.id,s.name,s.category_id,c.name FROM subcategories s
            INNER JOIN categories c ON c.id=s.category_id WHERE s.normalized_name=@key
            """, ("@key", subcategoryKey)))
        await using (var reader = await selectSubcategory.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (reader.GetInt32(2) != categoryId)
                    throw new ProductOperationException($"Subcategoria «{reader.GetString(1)}» există deja în categoria «{reader.GetString(3)}».");
                return (categoryId, reader.GetInt32(0), category, reader.GetString(1), false, false);
            }
        }
        throw new ProductOperationException("Subcategoria selectată nu mai există în categoria aleasă. Actualizează lista și reia salvarea.");
    }

    private static async Task RecordGroupsAsync(SqliteConnection connection, SqliteTransaction transaction,
        (string Username, string Role) actor,
        (int CategoryId, int SubcategoryId, string Category, string Subcategory, bool CategoryCreated, bool SubcategoryCreated) group,
        CancellationToken token)
    {
        if (group.CategoryCreated)
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Category, AuditActions.Create, group.Category,
                AuditDetails.Identification(("Denumire", group.Category)), string.Empty, group.CategoryId.ToString()), token).ConfigureAwait(false);
        if (group.SubcategoryCreated)
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Subcategory, AuditActions.Create, $"{group.Category} / {group.Subcategory}",
                AuditDetails.Identification(("Denumire", group.Subcategory), ("Categorie", group.Category)),
                string.Empty, group.SubcategoryId.ToString()), token).ConfigureAwait(false);
    }

    private static AuditChange[] ProductAuditChanges(Product before, Product after) =>
    [
        new("Denumire", before.Name, after.Name), new("Categorie", before.Category, after.Category),
        new("Subcategorie", before.Subcategory, after.Subcategory), new("Descriere", before.Description, after.Description),
        new("Cantitate", before.Quantity.ToString(), after.Quantity.ToString())
    ];

    private Task EnsureOperatorAsync(CancellationToken token) =>
        accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
