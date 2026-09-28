using System.Data;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed partial class MariaProductRepository
{
    public async Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT c.name,COALESCE(s.name,'')
            FROM categories c LEFT JOIN subcategories s ON s.category_id=c.id
            ORDER BY c.name,s.name
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var groups = new List<ProductGroup>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            groups.Add(new(reader.GetString(0), reader.GetString(1)));
        return groups;
    }

    public async Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated();
        (Product Product, GroupResolution Group) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                await EnsureUniqueProductNameAsync(connection, transaction, value.Name, null, cancellationToken).ConfigureAwait(false);
                var group = await ResolveGroupAsync(connection, transaction, value.Category, value.Subcategory, cancellationToken).ConfigureAwait(false);
                await using var insert = Command(connection, transaction, """
                    INSERT INTO products(category_id,subcategory_id,name,normalized_name,description,quantity,version)
                    VALUES(@category,@subcategory,@name,@normalized,@description,@quantity,0)
                    """, ("@category", group.CategoryId), ("@subcategory", group.SubcategoryId),
                    ("@name", value.Name), ("@normalized", TextNormalization.UniquenessKey(value.Name)),
                    ("@description", value.Description), ("@quantity", 0));
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                var product = new Product(checked((int)insert.LastInsertedId), group.Category, group.Subcategory, value.Name, value.Description, 0);
                return (Product: product, Group: group);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (IsDuplicateKey(exception))
        {
            throw new ProductOperationException(ProductCode.ConcurrentDuplicateMessage);
        }
        var product = result.Product;
        await RecordCreatedGroupsAsync(result.Group, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            ProductCode.AuditTarget(product), ProductCode.AuditIdentification(product), cancellationToken).ConfigureAwait(false);
        return product;
    }

    public async Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var value = input.Validated(original);
        (Product Product, GroupResolution Group) result;
        try
        {
            result = await WriteAsync(async (connection, transaction) =>
            {
                var current = await GetLocked(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
                ProductRules.CheckCurrent(current, original);
                await EnsureUniqueProductNameAsync(connection, transaction, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
                var group = await ResolveGroupAsync(connection, transaction, value.Category, value.Subcategory, cancellationToken).ConfigureAwait(false);
                var version = checked(original.Version + 1);
                await using var command = Command(connection, transaction, """
                    UPDATE products SET category_id=@category,subcategory_id=@subcategory,name=@name,
                        normalized_name=@normalized,description=@description,version=@version
                    WHERE id=@id AND version=@oldVersion
                    """, ("@category", group.CategoryId), ("@subcategory", group.SubcategoryId), ("@name", value.Name),
                    ("@normalized", TextNormalization.UniquenessKey(value.Name)), ("@description", value.Description),
                    ("@version", version), ("@id", original.Id), ("@oldVersion", original.Version));
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new ProductOperationException("Produsul s-a schimbat între timp. Actualizează catalogul.");
                var product = new Product(original.Id, group.Category, group.Subcategory, value.Name, value.Description, current!.Quantity, version);
                return (Product: product, Group: group);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MySqlException exception) when (IsDuplicateKey(exception))
        {
            throw new ProductOperationException(ProductCode.ConcurrentDuplicateMessage);
        }
        var product = result.Product;
        await RecordCreatedGroupsAsync(result.Group, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            ProductCode.AuditTarget(product), ProductCode.AuditChanges(original, product), value.Reason, cancellationToken).ConfigureAwait(false);
        return product;
    }

    public async Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new ProductOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Product(original, motif), async (operation, token) =>
        {
            async Task CommitDatabaseAsync(ArchiveFileRecord? file, CancellationToken archiveToken)
            {
                await WriteAsync(async (connection, transaction) =>
                {
                    var current = await GetLocked(connection, transaction, original.Id, archiveToken).ConfigureAwait(false);
                    ProductRules.CheckCurrent(current, original);
                    await using var relations = Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM stock_movements WHERE product_id=@id)", ("@id", original.Id));
                    ProductRules.CheckDelete(current!, Convert.ToBoolean(
                        await relations.ExecuteScalarAsync(archiveToken).ConfigureAwait(false)));
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, file is null ? [] : [file], archiveToken)
                        .ConfigureAwait(false);
                    await using var command = Command(connection, transaction,
                        "DELETE FROM products WHERE id=@id AND version=@version",
                        ("@id", original.Id), ("@version", original.Version));
                    if (await command.ExecuteNonQueryAsync(archiveToken).ConfigureAwait(false) != 1)
                        throw new ProductOperationException("Produsul s-a schimbat între timp. Actualizează catalogul.");
                    await MariaArchivePersistence.InsertAuditAsync(connection, transaction, operation, archiveToken)
                        .ConfigureAwait(false);
                    return true;
                }, archiveToken).ConfigureAwait(false);
            }

            if (images is null)
                await CommitDatabaseAsync(null, token).ConfigureAwait(false);
            else
                await images.ArchiveDeleteAsync(original.Id, operation, CommitDatabaseAsync, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    // The group a product belongs to; Created flags mirror SqliteProductRepository's ResolveGroupAsync shape (a
    // product save never creates a category/subcategory - only CreateCategoryAsync/CreateSubcategoryAsync do - so
    // both flags are always false here, kept only so RecordCreatedGroupsAsync has a single, reusable shape).
    private readonly record struct GroupResolution(int CategoryId, int SubcategoryId, string Category, string Subcategory,
        bool CategoryCreated, bool SubcategoryCreated);

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new ProductOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        // Subtask 2.10: the old check here validated a Database:ApplicationUserId/legacy `user` row before writing;
        // that concept is gone (the real schema has no such table, the operator is a username string via
        // IAccessControl/IAuditTrail like SQLite). What remains worth checking before ever opening a connection is
        // that the database is actually configured at all, so a missing setup fails fast with a clear message
        // instead of a raw connection-timeout/authentication error.
        if (string.IsNullOrWhiteSpace(configuration["Database:Password"]))
            throw new ProductOperationException("Salvarea nu este configurată. Administratorul trebuie să configureze conexiunea la baza de date.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Product?> GetLocked(MySqlConnection connection, MySqlTransaction transaction, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT p.id,c.name,s.name,p.name,p.description,p.quantity,p.version
            FROM products p INNER JOIN categories c ON c.id=p.category_id
                INNER JOIN subcategories s ON s.id=p.subcategory_id
            WHERE p.id=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? ReadProduct(reader) : null;
    }

    private static async Task<GroupResolution> ResolveGroupAsync(MySqlConnection connection,
        MySqlTransaction transaction, string category, string subcategory, CancellationToken token)
    {
        var categoryKey = TextNormalization.UniquenessKey(category);
        int categoryId;
        await using (var selectCategory = Command(connection, transaction,
            "SELECT id,name FROM categories WHERE normalized_name=@key", ("@key", categoryKey)))
        await using (var reader = await selectCategory.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                categoryId = checked((int)reader.GetInt64(0));
                category = reader.GetString(1);
            }
            else
                throw new ProductOperationException("Categoria selectată nu mai există. Actualizează lista și reia salvarea.");
        }

        var subcategoryKey = TextNormalization.UniquenessKey(subcategory);
        await using (var selectSubcategory = Command(connection, transaction, """
            SELECT s.id,s.name,s.category_id,c.name FROM subcategories s
            INNER JOIN categories c ON c.id=s.category_id WHERE s.normalized_name=@key
            """, ("@key", subcategoryKey)))
        await using (var reader = await selectSubcategory.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var subcategoryCategoryId = checked((int)reader.GetInt64(2));
                if (subcategoryCategoryId != categoryId)
                    throw new ProductOperationException($"Subcategoria «{reader.GetString(1)}» există deja în categoria «{reader.GetString(3)}».");
                return new GroupResolution(categoryId, checked((int)reader.GetInt64(0)), category, reader.GetString(1), false, false);
            }
        }
        throw new ProductOperationException("Subcategoria selectată nu mai există în categoria aleasă. Actualizează lista și reia salvarea.");
    }

    private async Task RecordCreatedGroupsAsync(GroupResolution group, CancellationToken cancellationToken)
    {
        if (group.CategoryCreated)
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Category,
                group.CategoryId.ToString(), group.Category,
                AuditDetails.Identification(("Denumire", group.Category)), cancellationToken).ConfigureAwait(false);
        if (group.SubcategoryCreated)
            await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Subcategory,
                group.SubcategoryId.ToString(), $"{group.Category} / {group.Subcategory}",
                AuditDetails.Identification(("Denumire", group.Subcategory), ("Categorie", group.Category)),
                cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureUniqueProductNameAsync(MySqlConnection connection, MySqlTransaction transaction,
        string name, int? excludedId, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT p.name,c.name,s.name FROM products p
            INNER JOIN categories c ON c.id=p.category_id INNER JOIN subcategories s ON s.id=p.subcategory_id
            WHERE p.normalized_name=@normalized AND (@id IS NULL OR p.id<>@id) LIMIT 1
            """, ("@normalized", TextNormalization.UniquenessKey(name)), ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new ProductOperationException(ProductCode.DuplicateMessage(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    // The pre-check above closes most races, but two concurrent saves can still both pass it and hit
    // uq_products_1 (normalized_name); MySqlConnector surfaces that as error 1062, translated to the same
    // message SqliteProductRepository uses for its own concurrent-duplicate race (SQLITE_CONSTRAINT on the
    // equivalent unique index).
    private static bool IsDuplicateKey(MySqlException exception) => exception.Number == 1062;

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureProductOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
