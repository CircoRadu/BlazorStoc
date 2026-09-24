using MySqlConnector;

namespace BlazorStoc.Services;

public sealed partial class MariaProductRepository
{
    public async Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(category, "Denumire categorie");
        var id = await WriteAsync(async (connection, transaction, userId) =>
        {
            await using (var command = Command(connection, transaction,
                "SELECT categorie_nume FROM categorie ORDER BY id_categorie FOR UPDATE"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    if (TextNormalization.SameUniqueValue(reader.GetString(0), name))
                        throw new ProductOperationException($"Categoria «{reader.GetString(0)}» există deja.");
            await using var insert = Command(connection, transaction,
                "INSERT INTO categorie(id_user,categorie_nume) VALUES(@user,@name)", ("@user", userId), ("@name", name));
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
        var result = await WriteAsync(async (connection, transaction, userId) =>
        {
            var categories = new List<(int Id, string Name)>();
            await using (var command = Command(connection, transaction,
                "SELECT id_categorie,categorie_nume FROM categorie ORDER BY id_categorie FOR UPDATE"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    categories.Add((reader.GetInt32(0), reader.GetString(1)));
            var parent = categories.FirstOrDefault(item => TextNormalization.SameUniqueValue(item.Name, requestedCategory));
            if (parent.Id == 0) throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
            await using (var command = Command(connection, transaction, """
                SELECT s.subcategorie_nume,c.categorie_nume FROM subcategorie s
                INNER JOIN categorie c ON c.id_categorie=s.id_categorie ORDER BY s.id_subcategorie FOR UPDATE
                """))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    if (TextNormalization.SameUniqueValue(reader.GetString(0), name))
                        throw new ProductOperationException($"Subcategoria «{reader.GetString(0)}» există deja în categoria «{reader.GetString(1)}».");
            await using var insert = Command(connection, transaction,
                "INSERT INTO subcategorie(id_categorie,id_user,subcategorie_nume) VALUES(@category,@user,@name)",
                ("@category", parent.Id), ("@user", userId), ("@name", name));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (Id: checked((int)insert.LastInsertedId), Group: new ProductGroup(parent.Name, name));
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
        var result = await WriteAsync(async (connection, transaction, _) =>
        {
            var categories = new List<(int Id, string Name)>();
            await using (var command = Command(connection, transaction,
                "SELECT id_categorie,categorie_nume FROM categorie ORDER BY id_categorie FOR UPDATE"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    categories.Add((reader.GetInt32(0), reader.GetString(1)));
            var source = categories.FirstOrDefault(item => TextNormalization.SameUniqueValue(item.Name, originalCategory));
            if (source.Id == 0) throw new ProductOperationException("Categoria nu mai există. Actualizează lista.");
            var duplicate = categories.FirstOrDefault(item => item.Id != source.Id && TextNormalization.SameUniqueValue(item.Name, name));
            if (duplicate.Id != 0) throw new ProductOperationException($"Categoria «{duplicate.Name}» există deja.");
            await using var update = Command(connection, transaction,
                "UPDATE categorie SET categorie_nume=@name WHERE id_categorie=@id",
                ("@name", name), ("@id", source.Id));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return source;
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Category,
            result.Id.ToString(), name, [new("Denumire", result.Name, name)], motif, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory,
        string targetCategory, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken).ConfigureAwait(false);
        var name = ProductGroupManagementRules.Name(newSubcategory, "Denumire subcategorie");
        var requestedCategory = ProductGroupManagementRules.Name(targetCategory, "Categorie");
        var motif = ProductGroupManagementRules.Reason(reason);
        var result = await WriteAsync(async (connection, transaction, _) =>
        {
            var categories = new List<(int Id, string Name)>();
            await using (var command = Command(connection, transaction,
                "SELECT id_categorie,categorie_nume FROM categorie ORDER BY id_categorie FOR UPDATE"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    categories.Add((reader.GetInt32(0), reader.GetString(1)));
            var destination = categories.FirstOrDefault(item => TextNormalization.SameUniqueValue(item.Name, requestedCategory));
            if (destination.Id == 0) throw new ProductOperationException("Categoria destinație nu mai există. Actualizează lista.");

            var subcategories = new List<(int Id, string Name, int CategoryId, string Category)>();
            await using (var command = Command(connection, transaction, """
                SELECT s.id_subcategorie,s.subcategorie_nume,s.id_categorie,c.categorie_nume
                FROM subcategorie s INNER JOIN categorie c ON c.id_categorie=s.id_categorie
                ORDER BY s.id_subcategorie FOR UPDATE
                """))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    subcategories.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
            var source = subcategories.FirstOrDefault(item =>
                TextNormalization.SameUniqueValue(item.Name, original.Subcategory) &&
                TextNormalization.SameUniqueValue(item.Category, original.Category));
            if (source.Id == 0) throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista.");
            var duplicate = subcategories.FirstOrDefault(item => item.Id != source.Id && TextNormalization.SameUniqueValue(item.Name, name));
            if (duplicate.Id != 0) throw new ProductOperationException($"Subcategoria «{duplicate.Name}» există deja în categoria «{duplicate.Category}».");
            await using var update = Command(connection, transaction, """
                UPDATE subcategorie SET id_categorie=@category,subcategorie_nume=@name WHERE id_subcategorie=@id
                """, ("@category", destination.Id), ("@name", name), ("@id", source.Id));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var updateProducts = Command(connection, transaction, """
                UPDATE produs SET id_categorie=@category,produs_versiune=produs_versiune+1 WHERE id_subcategorie=@subcategory
                """, ("@category", destination.Id), ("@subcategory", source.Id));
            await updateProducts.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return (source, After: new ProductGroup(destination.Name, name));
        }, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Subcategory,
            result.source.Id.ToString(), $"{result.After.Category} / {result.After.Subcategory}",
            [new("Denumire", result.source.Name, result.After.Subcategory),
             new("Categorie", result.source.Category, result.After.Category)], motif, cancellationToken).ConfigureAwait(false);
        return result.After;
    }
}
