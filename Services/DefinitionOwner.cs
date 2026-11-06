using System.Globalization;
using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

// Who owns a parameter, a family or a tag (migration 56): a subcategory, or a category (then every subcategory under it inherits the definition). A ProductGroup with
// an empty Subcategory names a category.
internal readonly record struct DefinitionOwner(int CategoryId, int? SubcategoryId, string Category, string Subcategory)
{
    public bool IsCategory => SubcategoryId is null;
    public string Target => IsCategory ? Category : $"{Category} / {Subcategory}";
    public string Label => IsCategory ? $"Categoria «{Category}»" : $"Subcategoria «{Subcategory}»";
    public string AuditEntity => IsCategory ? AuditEntities.Category : AuditEntities.Subcategory;
    public string AuditId => (SubcategoryId ?? CategoryId).ToString(CultureInfo.InvariantCulture);
    // The column that holds the owner in the three definition tables, and its value.
    public string Column => IsCategory ? "category_id" : "subcategory_id";
    public int ColumnValue => SubcategoryId ?? CategoryId;
}

// The owner of a definition as one number for the journal code: a subcategory id, or the negated id of a category.
internal static class OwnerKey
{
    public static int Of(DefinitionOwner owner) => owner.SubcategoryId ?? -owner.CategoryId;
    public static string Entity(int key) => key < 0 ? AuditEntities.Category : AuditEntities.Subcategory;
    public static string Id(int key) => Math.Abs(key).ToString(CultureInfo.InvariantCulture);
}

internal static class DefinitionOwners
{
    public static async Task<DefinitionOwner> FindAsync(MySqlConnection connection, MySqlTransaction transaction, ProductGroup group, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(group.Subcategory))
        {
            await using var category = Command(connection, transaction, "SELECT id FROM categories WHERE normalized_name=@cat", ("@cat", TextNormalization.UniquenessKey(group.Category)));
            return await category.ExecuteScalarAsync(token).ConfigureAwait(false) is long categoryId
                ? new DefinitionOwner(checked((int)categoryId), null, group.Category, "")
                : throw new ProductOperationException("Categoria nu mai există. Actualizează lista și reia operația.");
        }
        await using var command = Command(connection, transaction, """
            SELECT s.id,s.category_id FROM subcategories s INNER JOIN categories c ON c.id=s.category_id
            WHERE s.normalized_name=@sub AND c.normalized_name=@cat
            """, ("@sub", TextNormalization.UniquenessKey(group.Subcategory)), ("@cat", TextNormalization.UniquenessKey(group.Category)));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Subcategoria nu mai există. Actualizează lista și reia operația.");
        return new DefinitionOwner(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(0)), group.Category, group.Subcategory);
    }

    // The owner of an existing definition, read from its row (subcategory_id or category_id), with the names for the journal.
    public static async Task<DefinitionOwner> OfRowAsync(MySqlConnection connection, MySqlTransaction? transaction, string table, int id, CancellationToken token)
    {
        await using var command = Command(connection, transaction, $"""
            SELECT c.id,s.id,c.name,COALESCE(s.name,'') FROM {table} d
            LEFT JOIN subcategories s ON s.id=d.subcategory_id INNER JOIN categories c ON c.id=COALESCE(s.category_id,d.category_id) WHERE d.id=@id
            """, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new ProductOperationException("Definiția nu mai există. Actualizează lista și reia operația.");
        return new DefinitionOwner(checked((int)reader.GetInt64(0)), reader.IsDBNull(1) ? null : checked((int)reader.GetInt64(1)), reader.GetString(2), reader.GetString(3));
    }

    // The WHERE fragment (columns of the definition table, no alias) that holds the definitions a new name must differ from: the owner and everything above and below it.
    // A subcategory: itself, its ancestors and descendants, and the definitions of its category. A category: its own and those of all its subcategories.
    public static async Task<(string Sql, (string, object?)[] Parameters)> NameScopeAsync(MySqlConnection connection, MySqlTransaction transaction, DefinitionOwner owner, CancellationToken token)
    {
        if (owner.IsCategory)
            return ("(category_id=@scopeCategory OR subcategory_id IN (SELECT id FROM subcategories WHERE category_id=@scopeCategory))", [("@scopeCategory", owner.CategoryId)]);
        var scope = SubcategoryAncestry.InList(await SubcategoryAncestry.NameScopeAsync(connection, transaction, owner.SubcategoryId!.Value, token).ConfigureAwait(false));
        return ($"(subcategory_id IN ({scope}) OR category_id=@scopeCategory)", [("@scopeCategory", owner.CategoryId)]);
    }

    // The definitions a product inherits: its subcategory chain and its category. The tables are not aliased.
    public static string ChainSql(string chainList) => $"(subcategory_id IN ({chainList}) OR category_id=@chainCategory)";
}
