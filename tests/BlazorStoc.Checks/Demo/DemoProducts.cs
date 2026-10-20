// Test double of the in-memory suite (tests/BlazorStoc.Checks): an in-memory implementation of the repository used instead of MariaDB.
namespace BlazorStoc.Services;

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
