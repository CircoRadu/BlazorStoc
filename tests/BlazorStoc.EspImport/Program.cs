using System.Text;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;

// One-time import of the stocked products of the old ESP_Stoc database (prepared by artifacts/esp-import: produse_import.csv + imagini/).
// Default run is a simulation (reads the register, writes nothing); --apply writes: categories and subcategories (created by the product
// repository as needed), the product (code + description, no parameters), its image and an entry movement "Preluare din baza veche".
// Products whose code already exists are skipped, so a second run does not duplicate anything.
//   dotnet run --project tests/BlazorStoc.EspImport -c Release -- [--apply] [--csv path] [--images dir] [--config path]
const string Origin = "Preluare din baza veche";
var apply = args.Contains("--apply");
string Arg(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var csvPath = Arg("--csv", Path.Combine(repo, "artifacts", "esp-import", "produse_import.csv"));
var imagesRoot = Arg("--images", Path.Combine(repo, "artifacts", "esp-import"));
var configPath = Arg("--config", Path.Combine(repo, "local-secrets", "application-connection.private.json"));

var configuration = new ConfigurationBuilder().AddJsonFile(configPath, optional: false).Build();
var products = new MariaProductRepository(configuration, null, new MariaAuditTrail(configuration), null, new FileProductImageStore(null!, configuration));
var movements = new MariaStockMovementRepository(configuration, null, new MariaAuditTrail(configuration));
var images = new FileProductImageStore(null!, configuration);

var rows = ReadCsv(csvPath);
var header = rows[0]; int Col(string name) => Array.IndexOf(header, name);
int cCat = Col("categorie"), cSub = Col("subcategorie"), cCode = Col("cod_produs"), cDesc = Col("descriere"), cQty = Col("bucati"), cImg = Col("fisier_imagine"), cOld = Col("id_vechi");
var items = rows.Skip(1).Where(r => r.Length > cImg).Select(r => (Old: r[cOld], Cat: r[cCat], Sub: r[cSub], Code: r[cCode], Desc: r[cDesc], Qty: int.Parse(r[cQty]), Img: r[cImg])).ToList();

var problems = new List<string>();
// BlazorStoc keeps subcategory names unique across the whole register (and different from every category name). A name used under several
// categories, or equal to a category, gets its category in brackets: "Surse (Interfonie/Telefonie)". The names can be changed later in the app.
var categoryNames = items.Select(i => TextNormalization.UniquenessKey(i.Cat)).ToHashSet();
var clashing = items.Select(i => (i.Cat, i.Sub)).Distinct().GroupBy(g => TextNormalization.UniquenessKey(g.Sub)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
var renamed = new SortedSet<string>();
items = [.. items.Select(i =>
{
    var key = TextNormalization.UniquenessKey(i.Sub);
    if (!clashing.Contains(key) && !categoryNames.Contains(key)) return i;
    var name = $"{i.Sub} ({i.Cat})";
    renamed.Add($"{i.Sub} -> {name}");
    return i with { Sub = name };
})];
var existing = (await products.GetProductsAsync()).Select(p => TextNormalization.UniquenessKey(p.Name)).ToHashSet();
var codes = new HashSet<string>();
foreach (var item in items)
{
    if (!codes.Add(TextNormalization.UniquenessKey(item.Code))) problems.Add($"cod dublat in fisier dupa normalizare: {item.Code}");
    if (!File.Exists(Path.Combine(imagesRoot, item.Img))) problems.Add($"imagine lipsa: {item.Img}");
    if (item.Qty <= 0) problems.Add($"{item.Code}: bucati {item.Qty}");
}
foreach (var line in renamed) Console.WriteLine("REDENUMIRE subcategorie: " + line);

var toCreate = items.Where(i => !existing.Contains(TextNormalization.UniquenessKey(i.Code))).ToList();
Console.WriteLine($"{(apply ? "APLICARE" : "SIMULARE")}: {items.Count} produse in fisier, {items.Count - toCreate.Count} exista deja (sarite), {toCreate.Count} de creat, "
    + $"{toCreate.Select(i => i.Cat).Distinct().Count()} categorii, {toCreate.Select(i => (i.Cat, i.Sub)).Distinct().Count()} subcategorii, {toCreate.Sum(i => i.Qty)} bucati.");
foreach (var problem in problems) Console.WriteLine("PROBLEMA: " + problem);
if (problems.Count > 0) { Console.WriteLine("Oprit: corecteaza problemele de mai sus."); return 1; }
if (!apply) { Console.WriteLine("Nimic scris. Ruleaza cu --apply pentru import."); return 0; }

// The product repository only places a product in an existing group, so the categories and subcategories are created first.
var groups = await products.GetGroupsAsync();
var haveCategories = groups.Select(g => TextNormalization.UniquenessKey(g.Category)).ToHashSet();
var haveGroups = groups.Where(g => g.Subcategory.Length > 0).Select(g => (TextNormalization.UniquenessKey(g.Category), TextNormalization.UniquenessKey(g.Subcategory))).ToHashSet();
foreach (var category in toCreate.Select(i => i.Cat).Distinct())
    if (haveCategories.Add(TextNormalization.UniquenessKey(category))) await products.CreateCategoryAsync(category);
foreach (var (category, subcategory) in toCreate.Select(i => (i.Cat, i.Sub)).Distinct())
    if (haveGroups.Add((TextNormalization.UniquenessKey(category), TextNormalization.UniquenessKey(subcategory)))) await products.CreateSubcategoryAsync(category, subcategory);

int created = 0, failed = 0, pieces = 0;
foreach (var item in toCreate)
{
    try
    {
        var product = await products.CreateAsync(new ProductInput { Name = item.Code, Description = item.Desc, Category = item.Cat, Subcategory = item.Sub });
        await images.SaveAsync(product.Id, new ProductImageData(await File.ReadAllBytesAsync(Path.Combine(imagesRoot, item.Img)), "image/png", Path.GetFileName(item.Img)));
        await movements.CreateAsync(product.Id, new StockMovementInput
        {
            Kind = StockMovementKind.Entry, Date = StockMovementRules.Today, Quantity = item.Qty, Description = Origin,
            FreeType = FreeEntryType.Adjustment, Reference = $"{Origin} (P{int.Parse(item.Old):0000})"
        });
        created++; pieces += item.Qty;
    }
    catch (Exception exception) when (exception is ProductOperationException or StockMovementOperationException or ProductImageException)
    {
        failed++; Console.WriteLine($"ESEC {item.Code}: {exception.Message}");
    }
}
Console.WriteLine($"Gata: {created} produse create, {pieces} bucati, {failed} esecuri.");
return failed == 0 ? 0 : 2;

static List<string[]> ReadCsv(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
    var rows = new List<string[]>(); var fields = new List<string>(); var field = new StringBuilder(); var quoted = false;
    for (var i = 0; i < text.Length; i++)
    {
        var c = text[i];
        if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = false; } else field.Append(c); }
        else if (c == '"') quoted = true;
        else if (c == ';') { fields.Add(field.ToString()); field.Clear(); }
        else if (c == '\n' || c == '\r') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; fields.Add(field.ToString()); field.Clear(); if (fields.Count > 1 || fields[0].Length > 0) rows.Add([.. fields]); fields.Clear(); }
        else field.Append(c);
    }
    if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); rows.Add([.. fields]); }
    return rows;
}
