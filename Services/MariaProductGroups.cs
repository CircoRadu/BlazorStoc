using MySqlConnector;

namespace BlazorStoc.Services;

public sealed partial class MariaProductRepository
{
    public async Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(category, "Denumire categorie");
        var id = await WriteAsync(async (connection, transaction) =>
        {
            await using (var duplicate = Command(connection, transaction,
                "SELECT name FROM categories WHERE normalized_name=@key LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name))))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Categoria «{existing}» există deja.");
            await using var insert = Command(connection, transaction,
                "INSERT INTO categories(name,normalized_name) VALUES(@name,@key)",
                ("@name", name), ("@key", TextNormalization.UniquenessKey(name)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return checked((int)insert.LastInsertedId);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Category, id.ToString(), name,
            AuditDetails.Identification(("Denumire", name)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var requestedCategory = ProductGroupManagementRules.Name(category, "Categorie");
        var name = ProductGroupManagementRules.Name(subcategory, "Denumire subcategorie");
        var result = await WriteAsync(async (connection, transaction) =>
        {
            int categoryId;
            string storedCategory;
            await using (var findCategory = Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(requestedCategory))))
            await using (var reader = await findCategory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
                categoryId = checked((int)reader.GetInt64(0));
                storedCategory = reader.GetString(1);
            }
            await using (var duplicate = Command(connection, transaction, """
                SELECT s.name,c.name FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
                WHERE s.normalized_name=@key LIMIT 1
                """, ("@key", TextNormalization.UniquenessKey(name))))
            await using (var reader = await duplicate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException($"Subcategoria «{reader.GetString(0)}» există deja în categoria «{reader.GetString(1)}».");
            await using var insert = Command(connection, transaction,
                "INSERT INTO subcategories(category_id,name,normalized_name) VALUES(@category,@name,@key)",
                ("@category", categoryId), ("@name", name), ("@key", TextNormalization.UniquenessKey(name)));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Id: checked((int)insert.LastInsertedId), Group: new ProductGroup(storedCategory, name));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Subcategory, result.Id.ToString(),
            $"{result.Group.Category} / {result.Group.Subcategory}",
            AuditDetails.Identification(("Denumire", result.Group.Subcategory), ("Categorie", result.Group.Category)),
            cancellationToken).ConfigureAwait(false);
        return result.Group;
    }

    public async Task RenameCategoryAsync(string originalCategory, string newCategory, string reason,
        CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(newCategory, "Denumire categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        var result = await WriteAsync(async (connection, transaction) =>
        {
            int id;
            string before;
            await using (var find = Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(originalCategory))))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
                id = checked((int)reader.GetInt64(0));
                before = reader.GetString(1);
            }
            await using (var duplicate = Command(connection, transaction,
                "SELECT name FROM categories WHERE normalized_name=@key AND id<>@id LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Categoria «{existing}» există deja.");
            await using (var update = Command(connection, transaction,
                "UPDATE categories SET name=@name,normalized_name=@key WHERE id=@id",
                ("@name", name), ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Id: id, Before: before);
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Category,
            result.Id.ToString(), name, [new("Denumire", result.Before, name)], motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory,
        string targetCategory, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(newSubcategory, "Denumire subcategorie");
        var requestedCategory = ProductGroupManagementRules.Name(targetCategory, "Categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        var result = await WriteAsync(async (connection, transaction) =>
        {
            int id;
            string beforeName;
            string beforeCategory;
            await using (var find = Command(connection, transaction, """
                SELECT s.id,s.name,c.name FROM subcategories s
                INNER JOIN categories c ON c.id=s.category_id
                WHERE s.normalized_name=@subcategory AND c.normalized_name=@category
                """, ("@subcategory", TextNormalization.UniquenessKey(original.Subcategory)),
                ("@category", TextNormalization.UniquenessKey(original.Category))))
            await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
                id = checked((int)reader.GetInt64(0));
                beforeName = reader.GetString(1);
                beforeCategory = reader.GetString(2);
            }
            int categoryId;
            string category;
            await using (var findCategory = Command(connection, transaction,
                "SELECT id,name FROM categories WHERE normalized_name=@key",
                ("@key", TextNormalization.UniquenessKey(requestedCategory))))
            await using (var reader = await findCategory.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new ProductOperationException("Categoria destinație nu mai există. Actualizează lista.");
                categoryId = checked((int)reader.GetInt64(0));
                category = reader.GetString(1);
            }
            await using (var duplicate = Command(connection, transaction,
                "SELECT name FROM subcategories WHERE normalized_name=@key AND id<>@id LIMIT 1",
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                if (await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string existing)
                    throw new ProductOperationException($"Subcategoria «{existing}» există deja.");
            await using (var update = Command(connection, transaction, """
                UPDATE subcategories SET category_id=@category,name=@name,normalized_name=@key WHERE id=@id
                """, ("@category", categoryId), ("@name", name),
                ("@key", TextNormalization.UniquenessKey(name)), ("@id", id)))
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using (var updateProducts = Command(connection, transaction, """
                UPDATE products SET category_id=@category,version=version+1 WHERE subcategory_id=@subcategory
                """, ("@category", categoryId), ("@subcategory", id)))
                await updateProducts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Id: id, BeforeName: beforeName, BeforeCategory: beforeCategory, After: new ProductGroup(category, name));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory,
            result.Id.ToString(), $"{result.After.Category} / {result.After.Subcategory}",
            [new("Denumire", result.BeforeName, result.After.Subcategory),
             new("Categorie", result.BeforeCategory, result.After.Category)], motif, cancellationToken).ConfigureAwait(false);
        return result.After;
    }
}
