using System.Data;
using Microsoft.Data.Sqlite;

namespace BlazorStoc.Services;

public sealed partial class SqliteProductRepository
{
    public async Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(category, "Denumire categorie");
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var duplicate = SqliteLocalStore.Command(connection, transaction,
                "SELECT name FROM categories WHERE normalized_name=@key LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name))))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Categoria «{existing}» există deja.");
            int id;
            await using (var insert = SqliteLocalStore.Command(connection, transaction,
                "INSERT INTO categories(name,normalized_name) VALUES(@name,@key); SELECT last_insert_rowid();",
                ("@name", name), ("@key", TextNormalization.UniquenessKey(name))))
                id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Category, AuditActions.Create, name,
                AuditDetails.Identification(("Denumire", name)), string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }

    public async Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var requestedCategory = ProductGroupManagementRules.Name(category, "Categorie");
        var name = ProductGroupManagementRules.Name(subcategory, "Denumire subcategorie");
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            int categoryId;
            string storedCategory;
            await using (var findCategory = SqliteLocalStore.Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(requestedCategory))))
            await using (var reader = await findCategory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
                categoryId = reader.GetInt32(0); storedCategory = reader.GetString(1);
            }
            await using (var duplicate = SqliteLocalStore.Command(connection, transaction, """
                SELECT s.name,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
                WHERE s.normalized_name=@key LIMIT 1
                """, ("@key", TextNormalization.UniquenessKey(name))))
            await using (var reader = await duplicate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException($"Subcategoria «{reader.GetString(0)}» există deja în categoria «{reader.GetString(1)}».");
            int id;
            await using (var insert = SqliteLocalStore.Command(connection, transaction,
                "INSERT INTO subcategories(category_id,name,normalized_name) VALUES(@category,@name,@key); SELECT last_insert_rowid();",
                ("@category", categoryId), ("@name", name), ("@key", TextNormalization.UniquenessKey(name))))
                id = checked((int)(long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!);
            var result = new ProductGroup(storedCategory, name);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Subcategory, AuditActions.Create, $"{result.Category} / {result.Subcategory}",
                AuditDetails.Identification(("Denumire", result.Subcategory), ("Categorie", result.Category)),
                string.Empty, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }

    public async Task RenameCategoryAsync(string originalCategory, string newCategory, string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(newCategory, "Denumire categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            int id;
            string before;
            await using (var find = SqliteLocalStore.Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(originalCategory))))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
                id = reader.GetInt32(0);
                before = reader.GetString(1);
            }
            await using (var duplicate = SqliteLocalStore.Command(connection, transaction,
                "SELECT name FROM categories WHERE normalized_name=@key AND id<>@id LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Categoria «{existing}» există deja.");
            await using (var update = SqliteLocalStore.Command(connection, transaction,
                "UPDATE categories SET name=@name,normalized_name=@key WHERE id=@id",
                ("@name", name), ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Category, AuditActions.Edit, name,
                AuditDetails.Changes(new AuditChange("Denumire", before, name)), motif, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory,
        string targetCategory, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(newSubcategory, "Denumire subcategorie");
        var requestedCategory = ProductGroupManagementRules.Name(targetCategory, "Categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        var actor = await SqliteRepositoryAudit.ActorAsync(accessControl, cancellationToken).ConfigureAwait(false);
        await using var connection = await store.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            int id;
            string beforeName;
            string beforeCategory;
            await using (var find = SqliteLocalStore.Command(connection, transaction, """
                SELECT s.id,s.name,c.name FROM subcategories s
                INNER JOIN categories c ON c.id=s.category_id
                WHERE s.normalized_name=@subcategory AND c.normalized_name=@category
                """, ("@subcategory", TextNormalization.UniquenessKey(original.Subcategory)),
                ("@category", TextNormalization.UniquenessKey(original.Category))))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
                id = reader.GetInt32(0);
                beforeName = reader.GetString(1);
                beforeCategory = reader.GetString(2);
            }
            int categoryId;
            string category;
            await using (var findCategory = SqliteLocalStore.Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(requestedCategory))))
            await using (var reader = await findCategory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria destinație nu mai există. Actualizează lista.");
                categoryId = reader.GetInt32(0);
                category = reader.GetString(1);
            }
            await using (var duplicate = SqliteLocalStore.Command(connection, transaction,
                "SELECT name FROM subcategories WHERE normalized_name=@key AND id<>@id LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Subcategoria «{existing}» există deja.");
            await using (var update = SqliteLocalStore.Command(connection, transaction, """
                UPDATE subcategories SET category_id=@category,name=@name,normalized_name=@key WHERE id=@id
                """, ("@category", categoryId), ("@name", name),
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var updateProducts = SqliteLocalStore.Command(connection, transaction, """
                UPDATE products SET category_id=@category,version=version+1 WHERE subcategory_id=@subcategory
                """, ("@category", categoryId), ("@subcategory", id)))
                await updateProducts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var result = new ProductGroup(category, name);
            await SqliteLocalStore.InsertAuditAsync(connection, transaction, new(actor.Username, actor.Role,
                AuditEntities.Subcategory, AuditActions.Edit, $"{result.Category} / {result.Subcategory}",
                AuditDetails.Changes(new AuditChange("Denumire", beforeName, result.Subcategory),
                    new AuditChange("Categorie", beforeCategory, result.Category)), motif, id.ToString()), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
