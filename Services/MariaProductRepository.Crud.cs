using System.Data;
using System.Text.Json;
using MySqlConnector;

namespace BlazorStoc.Services;

public sealed partial class MariaProductRepository
{
    public async Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT c.categorie_nume, COALESCE(s.subcategorie_nume, '')
            FROM categorie c LEFT JOIN subcategorie s ON s.id_categorie=c.id_categorie
            ORDER BY c.categorie_nume, s.subcategorie_nume
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var groups = new List<ProductGroup>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) groups.Add(new(Text(reader, 0), Text(reader, 1)));
        return groups;
    }

    public async Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        var value = input.Validated();
        var result = await WriteAsync(async (connection, transaction, userId) =>
        {
            await EnsureUniqueProductNameAsync(connection, transaction, value.Name, null, cancellationToken).ConfigureAwait(false);
            var group = await ResolveGroup(connection, transaction, userId, value, cancellationToken).ConfigureAwait(false);
            var (categoryId, subcategoryId, category, subcategory) = (group.CategoryId, group.SubcategoryId, group.Category, group.Subcategory);
            await using var command = Command(connection, transaction, """
                INSERT INTO produs (id_categorie,id_subcategorie,id_user,produs_denumire,produs_descriere,produs_cantitate,produs_versiune)
                VALUES (@category,@subcategory,@user,@name,@description,@quantity,0)
                """, ("@category", categoryId), ("@subcategory", subcategoryId), ("@user", userId),
                ("@name", value.Name), ("@description", value.Description), ("@quantity", value.Quantity));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var product = new Product(checked((int)command.LastInsertedId), category, subcategory, value.Name, value.Description, value.Quantity);
            await Audit(connection, transaction, userId, "create", null, product, value.Reason, cancellationToken).ConfigureAwait(false);
            return (Product: product, Group: group);
        }, cancellationToken).ConfigureAwait(false);
        var product = result.Product;
        await RecordCreatedGroupsAsync(result.Group, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordCreateAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            ProductCode.AuditTarget(product), ProductCode.AuditIdentification(product), cancellationToken).ConfigureAwait(false);
        return product;
    }

    public async Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        var value = input.Validated(original);
        var result = await WriteAsync(async (connection, transaction, userId) =>
        {
            var current = await GetLocked(connection, transaction, original.Id, cancellationToken).ConfigureAwait(false);
            ProductRules.CheckCurrent(current, original);
            await EnsureUniqueProductNameAsync(connection, transaction, value.Name, original.Id, cancellationToken).ConfigureAwait(false);
            var group = await ResolveGroup(connection, transaction, userId, value, cancellationToken).ConfigureAwait(false);
            var (categoryId, subcategoryId, category, subcategory) = (group.CategoryId, group.SubcategoryId, group.Category, group.Subcategory);
            var version = checked(original.Version + 1);
            await using var command = Command(connection, transaction, """
                UPDATE produs SET id_categorie=@category,id_subcategorie=@subcategory,
                    produs_denumire=@name,produs_descriere=@description,produs_cantitate=@quantity,produs_versiune=@version
                WHERE id_produs=@id AND produs_versiune=@oldVersion
                """, ("@category", categoryId), ("@subcategory", subcategoryId), ("@name", value.Name),
                ("@description", value.Description), ("@quantity", value.Quantity), ("@version", version),
                ("@id", original.Id), ("@oldVersion", original.Version));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ProductOperationException("Produsul s-a schimbat între timp. Actualizează catalogul.");
            var product = new Product(original.Id, category, subcategory, value.Name, value.Description, value.Quantity, version);
            await Audit(connection, transaction, userId, "update", current, product, value.Reason, cancellationToken).ConfigureAwait(false);
            return (Product: product, Group: group);
        }, cancellationToken).ConfigureAwait(false);
        var product = result.Product;
        await RecordCreatedGroupsAsync(result.Group, cancellationToken).ConfigureAwait(false);
        await AuditRecorder.RecordEditAsync(auditTrail, accessControl, AuditEntities.Product, product.Id.ToString(),
            ProductCode.AuditTarget(product), ProductCode.AuditChanges(original, product), value.Reason, cancellationToken).ConfigureAwait(false);
        return product;
    }

    public async Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default)
    {
        await EnsureProductOperatorAsync(cancellationToken);
        var motif = ChangeReasonRules.Normalize(reason);
        if (ChangeReasonRules.ValidationError(motif) is { } reasonError)
            throw new ProductOperationException(reasonError);
        await archiver.ExecuteAsync(ArchiveRequests.Product(original, motif), async (operation, token) =>
        {
            async Task CommitDatabaseAsync(ArchiveFileRecord? file, CancellationToken archiveToken)
            {
                await WriteAsync(async (connection, transaction, userId) =>
                {
                    var current = await GetLocked(connection, transaction, original.Id, archiveToken).ConfigureAwait(false);
                    ProductRules.CheckCurrent(current, original);
                    await using var relations = Command(connection, transaction,
                        "SELECT EXISTS(SELECT 1 FROM io WHERE id_produs=@id)", ("@id", original.Id));
                    ProductRules.CheckDelete(current!, Convert.ToBoolean(
                        await relations.ExecuteScalarAsync(archiveToken).ConfigureAwait(false)));
                    await MariaArchivePersistence.InsertAsync(connection, transaction, operation, file is null ? [] : [file], archiveToken)
                        .ConfigureAwait(false);
                    await Audit(connection, transaction, userId, "delete", current, null,
                        "Ștergere produs arhivat", archiveToken).ConfigureAwait(false);
                    await using var command = Command(connection, transaction,
                        "DELETE FROM produs WHERE id_produs=@id AND produs_versiune=@version",
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

    private async Task<T> WriteAsync<T>(Func<MySqlConnection, MySqlTransaction, int, Task<T>> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!string.Equals(configuration["Database:Name"] ?? "BlazorStoc", "BlazorStoc", StringComparison.Ordinal))
            throw new ProductOperationException("Modificările sunt permise numai în baza BlazorStoc. Verifică numele bazei configurate.");
        var userId = configuration.GetValue<int>("Database:ApplicationUserId");
        if (userId <= 0) throw new ProductOperationException("Salvarea nu este configurată. Administratorul trebuie să asocieze contul web cu un utilizator al bazei de date.");
        await using var connection = CreateConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        // Fail on truncation/unrepresentable characters even when the server defaults to non-strict mode.
        await using (var strict = new MySqlCommand("SET SESSION sql_mode = CONCAT_WS(',', @@sql_mode, 'STRICT_ALL_TABLES')", connection))
            await strict.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
        try
        {
            await using (var actor = Command(connection, transaction, "SELECT id_user FROM `user` WHERE id_user=@id FOR UPDATE", ("@id", userId)))
                if (await actor.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
                    throw new ProductOperationException("Utilizatorul asociat contului web nu există în baza de date. Verifică configurarea cu administratorul.");
            var result = await action(connection, transaction, userId).ConfigureAwait(false);
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
            SELECT p.id_produs,c.categorie_nume,s.subcategorie_nume,p.produs_denumire,p.produs_descriere,
                COALESCE(p.produs_cantitate,0),p.produs_versiune
            FROM produs p INNER JOIN categorie c ON p.id_categorie=c.id_categorie
                INNER JOIN subcategorie s ON p.id_subcategorie=s.id_subcategorie
            WHERE p.id_produs=@id FOR UPDATE
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false) ? new Product(reader.GetInt32(0), Text(reader, 1), Text(reader, 2),
            Text(reader, 3), Text(reader, 4), reader.GetInt32(5), reader.GetInt64(6)) : null;
    }

    private static async Task<(int CategoryId, int SubcategoryId, string Category, string Subcategory,
        bool CategoryCreated, bool SubcategoryCreated)> ResolveGroup(
        MySqlConnection connection, MySqlTransaction transaction, int userId, ProductInput value, CancellationToken token)
    {
        async Task<(int Id, string Name, bool Created)> Resolve(bool subcategory, int parentId)
        {
            var name = subcategory ? value.Subcategory : value.Category;
            var sql = subcategory
                ? "SELECT id_subcategorie,subcategorie_nume FROM subcategorie WHERE id_categorie=@parent AND UPPER(subcategorie_nume)=UPPER(@name) ORDER BY id_subcategorie LIMIT 1 FOR UPDATE"
                : "SELECT id_categorie,categorie_nume FROM categorie WHERE UPPER(categorie_nume)=UPPER(@name) ORDER BY id_categorie LIMIT 1 FOR UPDATE";
            await using (var find = Command(connection, transaction, sql, ("@parent", parentId), ("@name", name)))
            await using (var reader = await find.ExecuteReaderAsync(token).ConfigureAwait(false))
                if (await reader.ReadAsync(token).ConfigureAwait(false)) return (reader.GetInt32(0), reader.GetString(1), false);
            if (!subcategory)
            {
                await using var allCategories = Command(connection, transaction,
                    "SELECT id_categorie,categorie_nume FROM categorie ORDER BY id_categorie FOR UPDATE");
                await using var categoryReader = await allCategories.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await categoryReader.ReadAsync(token).ConfigureAwait(false))
                    if (TextNormalization.SameUniqueValue(categoryReader.GetString(1), name))
                        return (categoryReader.GetInt32(0), categoryReader.GetString(1), false);
            }
            else
            {
                string? duplicateName = null, duplicateCategory = null;
                await using var allSubcategories = Command(connection, transaction, """
                    SELECT s.id_subcategorie,s.subcategorie_nume,s.id_categorie,c.categorie_nume
                    FROM subcategorie s INNER JOIN categorie c ON c.id_categorie=s.id_categorie
                    ORDER BY s.id_subcategorie
                    FOR UPDATE
                    """);
                await using var subcategoryReader = await allSubcategories.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await subcategoryReader.ReadAsync(token).ConfigureAwait(false))
                {
                    var existingName = subcategoryReader.GetString(1);
                    if (!TextNormalization.SameUniqueValue(existingName, name)) continue;
                    if (subcategoryReader.GetInt32(2) == parentId)
                        return (subcategoryReader.GetInt32(0), existingName, false);
                    duplicateName ??= existingName;
                    duplicateCategory ??= subcategoryReader.GetString(3);
                }
                if (duplicateName is not null)
                    throw new ProductOperationException($"Subcategoria «{duplicateName}» există deja în categoria «{duplicateCategory}».");
            }
            throw new ProductOperationException(subcategory
                ? "Subcategoria selectată nu mai există în categoria aleasă. Actualizează lista și reia salvarea."
                : "Categoria selectată nu mai există. Actualizează lista și reia salvarea.");
        }
        var (categoryId, category, categoryCreated) = await Resolve(false, 0).ConfigureAwait(false);
        var (subcategoryId, subcategoryName, subcategoryCreated) = await Resolve(true, categoryId).ConfigureAwait(false);
        return (categoryId, subcategoryId, category, subcategoryName, categoryCreated, subcategoryCreated);
    }

    private async Task RecordCreatedGroupsAsync((int CategoryId, int SubcategoryId, string Category, string Subcategory,
        bool CategoryCreated, bool SubcategoryCreated) group, CancellationToken cancellationToken)
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
            SELECT p.id_produs,p.produs_denumire,c.categorie_nume,s.subcategorie_nume
            FROM produs p
            INNER JOIN categorie c ON p.id_categorie=c.id_categorie
            INNER JOIN subcategorie s ON p.id_subcategorie=s.id_subcategorie
            WHERE (@id IS NULL OR p.id_produs<>@id)
            ORDER BY p.id_produs
            FOR UPDATE
            """, ("@id", excludedId is null ? DBNull.Value : excludedId.Value));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var existingName = reader.GetString(1);
            if (!TextNormalization.SameUniqueValue(existingName, name)) continue;
            throw new ProductOperationException(ProductCode.DuplicateMessage(existingName, reader.GetString(2), reader.GetString(3)));
        }
    }

    private static async Task Audit(MySqlConnection connection, MySqlTransaction transaction, int userId, string operation,
        Product? before, Product? after, string reason, CancellationToken token)
    {
        var entry = JsonSerializer.Serialize(new { Source = "BlazorStoc", Operation = operation, Before = before, After = after, Reason = reason });
        await using var command = Command(connection, transaction, "INSERT INTO log (id_user,log_command) VALUES (@user,@entry)",
            ("@user", userId), ("@entry", entry));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static MySqlCommand Command(MySqlConnection connection, MySqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private Task EnsureProductOperatorAsync(CancellationToken token) => accessControl?.EnsureProductOperatorAsync(token) ?? Task.CompletedTask;
}
