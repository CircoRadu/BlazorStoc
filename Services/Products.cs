using MySqlConnector;

namespace BlazorStoc.Services;

public sealed record AppMode(bool IsDemo);
public sealed record Product(int Id, string Category, string Subcategory, string Name, string Description, int Quantity, long Version = 0);
public sealed record ProductGroup(string Category, string Subcategory);
public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task CreateCategoryAsync(string category, CancellationToken cancellationToken = default);
    Task<ProductGroup> CreateSubcategoryAsync(string category, string subcategory, CancellationToken cancellationToken = default);
    Task RenameCategoryAsync(string originalCategory, string newCategory, string reason, CancellationToken cancellationToken = default);
    Task<ProductGroup> UpdateSubcategoryAsync(ProductGroup original, string newSubcategory, string targetCategory,
        string reason, CancellationToken cancellationToken = default);
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
    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        // The version is checked under a row lock before every update or deletion.
        const string sql = """
            SELECT p.id_produs, c.categorie_nume, s.subcategorie_nume,
                   p.produs_denumire, p.produs_descriere, COALESCE(p.produs_cantitate, 0), p.produs_versiune
            FROM produs p
            INNER JOIN categorie c ON p.id_categorie = c.id_categorie
            INNER JOIN subcategorie s ON p.id_subcategorie = s.id_subcategorie
            ORDER BY p.id_subcategorie, p.produs_denumire, p.id_produs
            """;
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            products.Add(new Product(reader.GetInt32(0), Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4), reader.GetInt32(5), reader.GetInt64(6)));
        return products;
    }
    private static string Text(MySqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
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
    public Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return Task.FromResult<IReadOnlyList<Product>>(products.ToArray());
    }
    internal static List<Product> InitialProducts() => new()
        {
            new(1,"Scule electrice","Găurire","Mașină de găurit cu acumulator","18 V · mandrină de 13 mm · set cu două acumulatoare",12),
            new(2,"Scule electrice","Tăiere","Polizor unghiular","Disc 125 mm · putere 900 W",8),
            new(3,"Echipamente de protecție","Protecție cap","Cască de protecție albă","Reglaj cu rotiță · utilizare pe șantier",34),
            new(4,"Consumabile","Fixare","Șurub autoforant 4,8 × 25","Cutie pentru montaj profile metalice",96),
            new(5,"Măsurare","Distanțe","Telemetru laser","Domeniu 0,2–50 m · husă inclusă",0),
            new(6,"Scule de mână","Strângere","Set chei combinate","12 piese · dimensiuni 8–19 mm",7),
            new(7,"Echipamente de protecție","Protecție mâini","Mănuși de lucru","Mărimea 10 · acoperire nitril",52),
            new(8,"Consumabile","Tăiere","Disc diamantat 230 mm","Pentru beton și zidărie",-2),
            new(9,"Măsurare","Nivelare","Nivelă cu bulă 60 cm","Corp aluminiu · trei fiole",5),
            new(10,"Scule electrice","Găurire","Ciocan rotopercutor SDS Plus","Putere 800 W · energie de impact 2,7 J",3),
            new(11,"Scule de mână","Tăiere","Cutter profesional","Lamă segmentată de 18 mm",0),
            new(12,"Consumabile","Fixare","Diblu nylon 8 × 40","Pentru fixări în zidărie",120)
        };
}
