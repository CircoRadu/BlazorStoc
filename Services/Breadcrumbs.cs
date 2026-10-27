using System.Text.RegularExpressions;

namespace BlazorStoc.Services;

// One step of the route shown in the top bar (main page / section / subsection / page). The last step has no link.
public sealed record Crumb(string Label, string? Href = null);

// The route of a page, worked out from its address alone (plus the category and subcategory of the opened product): the layout does not know what a page
// loads, so detail pages carry a generic last step ("Fișa beneficiarului") except the product, which the layout looks up.
public static partial class Breadcrumbs
{
    [GeneratedRegex(@"^/produse/(\d+)(/miscari)?$")]
    private static partial Regex ProductPath();

    public static int? ProductId(string path) =>
        ProductPath().Match(Normalize(path)) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/').ToLowerInvariant() : "/";

    private static Crumb[] Chain(Crumb a) => [a];
    private static Crumb[] Chain(Crumb a, Crumb b) => [a, b];
    private static Crumb[] Chain(Crumb a, Crumb b, Crumb c) => [a, b, c];
    private static Crumb[] Chain(Crumb a, Crumb b, Crumb c, Crumb d) => [a, b, c, d];
    private static Crumb[] Chain(Crumb a, Crumb b, Crumb c, Crumb d, Crumb e) => [a, b, c, d, e];

    public static string CatalogUrl(string category, string? subcategory = null)
    {
        var url = $"/produse?categorie={Uri.EscapeDataString(category)}";
        return string.IsNullOrWhiteSpace(subcategory) ? url : $"{url}&subcategorie={Uri.EscapeDataString(subcategory)}";
    }

    // Steps after "Meniu principal"; empty on the main page.
    public static IReadOnlyList<Crumb> For(string path, string category = "", string subcategory = "", Product? product = null)
    {
        path = Normalize(path);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return [];

        if (ProductId(path) is not null)
        {
            if (product is null) return Chain(new("Produse"), new("Stoc", "/produse"), new("Produs"));
            return Chain(new("Produse"), new("Stoc", "/produse"), new(product.Category, CatalogUrl(product.Category)),
                new(product.Subcategory, CatalogUrl(product.Category, product.Subcategory)), new(product.Name));
        }

        var id = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "produse" when parts.Length == 1:
                return category.Length == 0 ? Chain(new("Produse"), new("Stoc"))
                    : subcategory.Length == 0 ? Chain(new("Produse"), new("Stoc", "/produse"), new(category))
                    : Chain(new("Produse"), new("Stoc", "/produse"), new(category, CatalogUrl(category)), new(subcategory));
            case "produse" when parts.Length >= 2 && parts[1] == "preluare-factura":
                return parts.Length > 2 ? Chain(new("Produse"), new("Preluare factură", "/produse/preluare-factura"), new("Factură")) : Chain(new("Produse"), new("Preluare factură"));
            case "iesiri": return Chain(new("Produse"), new("Ieșire multiplă"));
            case "consum": return Chain(new("Produse"), new("Export consum"));
            case "inventar": return Chain(new("Inventar"), parts.Length > 1 ? new("Preluare inventar") : new("Generare situație"));
            case "categorii": return Chain(new("Administrare"), new("Categorii"));
            case "beneficiari": return parts.Length == 1 ? Chain(new("Administrare"), new("Beneficiari")) : Chain(new("Administrare"), new("Beneficiari", "/beneficiari"), new("Fișa beneficiarului"));
            case "proiecte":
                var project = new Crumb("Proiect", $"/proiecte/{id}");
                if (parts.Length == 2) return Chain(new("Administrare"), new("Beneficiari", "/beneficiari"), new("Proiect"));
                return Chain(new("Administrare"), new("Beneficiari", "/beneficiari"), project, new(parts[2] switch { "observatii" => "Observație", "echipamente" => "Echipamente", "situatie" => "Situație", _ => "Detalii" }));
            case "observatii": return Chain(new("Administrare"), new("Beneficiari", "/beneficiari"), new("Observație"));
            case "furnizori": return parts.Length == 1 ? Chain(new("Administrare"), new("Furnizori")) : Chain(new("Administrare"), new("Furnizori", "/furnizori"), new("Fișa furnizorului"));
            case "facturi": return Chain(new("Administrare"), new("Facturi"));
            case "vehicule":
                return parts.Length == 1 ? Chain(new("Administrare"), new("Vehicule"))
                    : parts.Length == 2 ? Chain(new("Administrare"), new("Vehicule", "/vehicule"), new("Vehicul"))
                    : Chain(new("Administrare"), new("Vehicule", "/vehicule"), new("Vehicul", $"/vehicule/{id}"), new("Echipamente"));
            case "nomenclator": return Chain(new("Administrare"), new("Nomenclator"));
            case "mentenanta": return parts.Length == 1 ? Chain(new("Mentenanță")) : Chain(new("Mentenanță", "/mentenanta"), new("Harta"));
            case "intretinere": return Chain(new("Întreținere"));
            case "oferte":
                return parts.Length == 1 ? Chain(new("Oferte")) : Chain(new("Oferte", "/oferte"), new(parts[1] == "sabloane" ? "Șabloane" : "Preluare oferte"));
            case "analize-risc": return Chain(new("Analize de risc"));
            case "notificari": return Chain(new("Notificări"));
            case "jurnal": return parts.Length == 1 ? Chain(new("Jurnal")) : Chain(new("Jurnal", "/jurnal"), new("Jurnal complet"));
            case "setari": return Chain(new("Setări"));
            case "utilizatori": return parts.Length == 1 ? Chain(new("Utilizatori")) : Chain(new("Utilizatori", "/utilizatori"), new("Utilizator"));
            default: return [];
        }
    }
}
