using MySqlConnector;

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
    private MySqlConnection CreateConnection() => DatabaseConnections.Create(configuration);
    public async Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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

public sealed class DemoProductStore
{
    internal readonly object Gate = new();
    internal List<Product> Products { get; }
    internal List<string> Categories { get; }
    internal List<ProductGroup> Groups { get; }
    internal int NextId { get; set; } = 13;

    public DemoProductStore()
    {
        Products = DemoProductRepository.InitialProducts();
        Categories = Products.Select(product => product.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Groups = Products
            .Select(product => new ProductGroup(product.Category, product.Subcategory))
            .GroupBy(ProductGroupKey)
            .Select(group => group.First())
            .ToList();
    }

    private static string ProductGroupKey(ProductGroup group) =>
        $"{TextNormalization.UniquenessKey(group.Category)}\u001f{TextNormalization.UniquenessKey(group.Subcategory)}";
}

public sealed partial class DemoProductRepository(IAccessControl? accessControl = null, IAuditTrail? auditTrail = null,
    DemoProductStore? sharedStore = null, IArchiveService? archiveService = null) : IProductRepository
{
    private readonly IArchiveService archiver = archiveService ?? new ArchiveService(accessControl);
    private readonly DemoProductStore store = sharedStore ?? new DemoProductStore();
    private object gate => store.Gate;
    private List<Product> products => store.Products;
    private List<string> categories => store.Categories;
    private List<ProductGroup> groups => store.Groups;
    public Task<Product?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult(products.FirstOrDefault(product => product.Id == id));
    }
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult<IReadOnlyList<Product>>(products.ToArray());
    }
    internal static List<Product> InitialProducts() => new()
        {
            new(1,"Scule electrice","Gaurire","Masina de gaurit cu acumulator","18 V · mandrina de 13 mm · set cu doua acumulatoare",12),
            new(2,"Scule electrice","Taiere","Polizor unghiular","Disc 125 mm · putere 900 W",8),
            new(3,"Echipamente de protectie","Protectie cap","Casca de protectie alba","Reglaj cu rotita · utilizare pe santier",34),
            new(4,"Consumabile","Fixare","Surub autoforant 4,8 × 25","Cutie pentru montaj profile metalice",96),
            new(5,"Masurare","Distante","Telemetru laser","Domeniu 0,2–50 m · husa inclusa",0),
            new(6,"Scule de mana","Strangere","Set chei combinate","12 piese · dimensiuni 8–19 mm",7),
            new(7,"Echipamente de protectie","Protectie maini","Manusi de lucru","Marimea 10 · acoperire nitril",52),
            new(8,"Consumabile","Taiere","Disc diamantat 230 mm","Pentru beton si zidarie",-2),
            new(9,"Masurare","Nivelare","Nivela cu bula 60 cm","Corp aluminiu · trei fiole",5),
            new(10,"Scule electrice","Gaurire","Ciocan rotopercutor SDS Plus","Putere 800 W · energie de impact 2,7 J",3),
            new(11,"Scule de mana","Taiere","Cutter profesional","Lama segmentata de 18 mm",0),
            new(12,"Consumabile","Fixare","Diblu nylon 8 × 40","Pentru fixari in zidarie",120)
        };
}
