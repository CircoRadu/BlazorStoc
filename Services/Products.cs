using MySqlConnector;
using static BlazorStoc.Services.MariaDb;

namespace BlazorStoc.Services;

public sealed record Product(int Id, string Category, string Subcategory, string Name, string Description, int Quantity, long Version = 0);
public sealed record ProductGroup(string Category, string Subcategory);
public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default);
    Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory, CancellationToken cancellationToken = default);
    Task RenameCategoryAsync(string originalCategory, string newCategory, string reason, CancellationToken cancellationToken = default);
    Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory, string targetCategory,
        string reason, CancellationToken cancellationToken = default);
    // Only an empty category (no subcategory, no product) and an empty subcategory (no product) can be deleted.
    Task DeleteCategoryAsync(string category, string reason, CancellationToken cancellationToken = default);
    // The order of the categories (side menu and catalog page): every category once, in the wanted order. Administrator only.
    Task ReorderCategoriesAsync(IReadOnlyList<string> orderedCategories, CancellationToken cancellationToken = default);
    // The order of the subcategories of one category: every subcategory of it once, in the wanted order. Administrator only.
    Task ReorderSubcategoriesAsync(string category, IReadOnlyList<string> orderedSubcategories, CancellationToken cancellationToken = default);
    Task DeleteSubcategoryAsync(ProductGroup group, string reason, CancellationToken cancellationToken = default);
    Task<Product> CreateAsync(ProductInput input, CancellationToken cancellationToken = default);
    Task<Product> UpdateAsync(Product original, ProductInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product original, string reason, CancellationToken cancellationToken = default);
}

public static class ProductSearch
{
    public static IEnumerable<Product> Filter(IEnumerable<Product> products, string query, string field, string category, string stock)
    {
        query = query.Trim();
        return products.Where(p =>
            (category.Length == 0 || p.Category == category) &&
            (stock != "zero" || p.Quantity == 0) && (stock != "negative" || p.Quantity < 0) &&
            (stock != "positive" || p.Quantity > 0) &&
            (query.Length == 0 ||
             (field != "description" && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             (field != "name" && p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))));
    }
}

public sealed partial class MariaProductRepository(IConfiguration configuration, IAccessControl? accessControl = null,
    IAuditTrail? auditTrail = null, IArchiveService? archiveService = null, IProductImageStore? imageStore = null) : IProductRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly IProductImageStore? images = imageStore;
    public async Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT p.id,c.name,s.name,p.name,p.description,p.quantity,p.version
            FROM products p
            INNER JOIN categories c ON c.id=p.category_id
            INNER JOIN subcategories s ON s.id=p.subcategory_id
            WHERE p.id=@id
            """, connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadProduct(reader) : null;
    }
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await MariaDb.OpenAsync(configuration, cancellationToken).ConfigureAwait(false);
        const string sql = """
            SELECT p.id,c.name,s.name,p.name,p.description,p.quantity,p.version
            FROM products p
            INNER JOIN categories c ON c.id=p.category_id
            INNER JOIN subcategories s ON s.id=p.subcategory_id
            ORDER BY s.name,p.name,p.id
            """;
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) products.Add(ReadProduct(reader));
        return products;
    }
    // id/category_id/subcategory_id/quantity/version are BIGINT on the real schema; MySqlConnector requires
    // GetInt64 for those columns (GetInt32 throws InvalidCastException), so the public int fields are narrowed
    // with a checked cast, matching the pattern already used elsewhere in the Maria repositories.
    private static Product ReadProduct(MySqlDataReader reader) => new(checked((int)reader.GetInt64(0)), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), checked((int)reader.GetInt64(5)), reader.GetInt64(6));
}

