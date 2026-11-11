using System.IO.Compression;
using System.Text;
using BlazorStoc.Modules.EspImport;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// The stock import from the old ESPStoc (Modules/EspImport), no database needed: the reader of the export (HeidiSQL and mysqldump styles, a ZIP, only the products with stock and their images),
// the planner (subcategory names, codes that match existing products, parameters proposed from the end of the code, duplicates, omitted products) and the module wiring. With ESPIMPORT_EXPORT set
// to a real export, the figures of that file are checked too.
public static class EspImportChecks
{
    private static string Hex(params byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string HeidiDump() => string.Join("\n",
        "-- HeidiSQL Dump",
        "CREATE TABLE IF NOT EXISTS `categorie` (...);",
        "INSERT INTO `categorie` (`id_categorie`, `id_user`, `categorie_nume`) VALUES",
        "\t(1, 1, 'Supraveghere Video');",
        "INSERT INTO `categorie` (`id_categorie`, `id_user`, `categorie_nume`) VALUES",
        "\t(2, 1, 'Retelistica');",
        "INSERT INTO `subcategorie` (`id_subcategorie`, `id_categorie`, `id_user`, `subcategorie_nume`) VALUES",
        "\t(3, 1, 1, 'Camere IP');",
        "INSERT INTO `subcategorie` (`id_subcategorie`, `id_categorie`, `id_user`, `subcategorie_nume`) VALUES",
        "\t(4, 1, 1, 'Hard disk');",
        "INSERT INTO `subcategorie` (`id_subcategorie`, `id_categorie`, `id_user`, `subcategorie_nume`) VALUES",
        "\t(5, 2, 1, 'Surse');",
        "INSERT INTO `imagine` (`id_imagine`, `id_user`, `id_produs`, `imagine_nume_fisier`, `imagine_data`) VALUES",
        $"\t(1, 1, 1, 'prod_img_1.png', _binary 0x{Hex(0x89, 0x50, 0x4E, 0x47)});",
        "INSERT INTO `imagine` (`id_imagine`, `id_user`, `id_produs`, `imagine_nume_fisier`, `imagine_data`) VALUES",
        $"\t(2, 1, 2, 'prod_img_2.png', _binary 0x{Hex(1, 2, 3)});",
        "INSERT INTO `produs` (`id_produs`, `id_categorie`, `id_subcategorie`, `id_user`, `produs_denumire`, `produs_descriere`, `produs_cantitate`) VALUES",
        "\t(1, 1, 3, 1, 'DS-2CD1141G0-I-2.8mm', '•Camera IP, \\'test\\'\\r\\nrand doi', 3);",
        "INSERT INTO `produs` (`id_produs`, `id_categorie`, `id_subcategorie`, `id_user`, `produs_denumire`, `produs_descriere`, `produs_cantitate`) VALUES",
        "\t(2, 1, 3, 1, 'FARA STOC', 'x', 0);",
        "INSERT INTO `produs` (`id_produs`, `id_categorie`, `id_subcategorie`, `id_user`, `produs_denumire`, `produs_descriere`, `produs_cantitate`) VALUES",
        "\t(3, 1, 4, 1, 'ST4000VX016 (4TB)', 'Hard disk', 5);",
        "INSERT INTO `produs` (`id_produs`, `id_categorie`, `id_subcategorie`, `id_user`, `produs_denumire`, `produs_descriere`, `produs_cantitate`) VALUES",
        "\t(4, 2, 5, 1, 'ZTP-1203B', 'Sursa', 2);",
        "INSERT INTO `log` (`log_timestamp`, `log_command`, `id_user`) VALUES",
        "\t('2022-05-03 12:40:06', 'ins|produs|INSERT INTO `produs` (x', 1);");

    private static string MysqldumpStyle() => string.Join("\n",
        "INSERT INTO `categorie` VALUES (1,1,'A'),(2,1,'B''s');",
        "INSERT INTO `subcategorie` VALUES (3,1,1,'S1'),(4,2,1,'S2');",
        "INSERT INTO `produs` VALUES (1,1,3,1,'P1','d1',4),(2,2,4,1,'P2','multi",
        "linie',0),(3,1,3,1,'P3',NULL,1);",
        "INSERT INTO `imagine` VALUES (1,1,1,'a.png',X'89504E47'),(2,1,3,'b.png',0x010203);");

    private static ParameterListValue V(int id, string value) => new(id, value, 0);

    private static EspCatalog Catalog(params Product[] products)
    {
        var lens = new[]
        {
            new SubcategoryParameter(30, "Supraveghere Video", "Camere IP", "Fixa", "mm", ParameterKind.Number, 1, 0, [V(54, "2.8"), V(55, "4")], 10),
            new SubcategoryParameter(31, "Supraveghere Video", "Camere IP", "Varifocala", "mm", ParameterKind.Interval, 2, 0, [V(56, "2.8-12")], 10),
            new SubcategoryParameter(32, "Supraveghere Video", "Camere IP", "Fisheye", "", ParameterKind.Fixed, 3, 0, [V(58, "Fisheye")], 10),
            new SubcategoryParameter(5, "Supraveghere Video", "Hard disk", "Capacitate", "TB", ParameterKind.Number, 1, 0, [V(6, "1"), V(9, "4"), V(11, "8")])
        };
        return new EspCatalog
        {
            Products = products,
            Groups = [new("Supraveghere Video", "Camere IP"), new("Supraveghere Video", "Hard disk"), new("Retelistica", "Surse"), new("Retelistica", "Switch"), new("Interfonie/Telefonie", "Surse (Interfonie/Telefonie)")],
            Parameters = lens,
            Families = [new ParameterFamily(10, "Supraveghere Video", "Camere IP", "Lentila", 1, 0, [30, 31, 32])],
            BaseModels = products.Where(product => product.Name.Contains(" - ")).ToDictionary(product => product.Id, product => product.Name[..product.Name.IndexOf(" - ", StringComparison.Ordinal)]),
            Choices = new Dictionary<int, IReadOnlyList<ProductParameterChoice>> { [1] = [new(30, 54)], [2] = [new(5, 11)] },
            Incomplete = products.Where(product => product.Name == "OLD-NO-PARAMS").Select(product => product.Id).ToHashSet()
        };
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "esp-import-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await ReaderAsync(check, folder);
            Planner(check);
            Wiring(check);
            await RealExportAsync(check);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static async Task ReaderAsync(Action<bool, string> check, string folder)
    {
        var heidi = Path.Combine(folder, "heidi.sql");
        await File.WriteAllTextAsync(heidi, HeidiDump(), new UTF8Encoding(false));
        var export = await EspExportReader.ReadAsync(heidi, "heidi.sql");
        check(export.Categories.Count == 2 && export.Subcategories.Count == 3, "EspImport: categories and subcategories are read");
        check(export.TotalProducts == 4 && export.Products.Count == 3 && export.ZeroProducts == 1, "EspImport: only the products with a positive quantity are taken");
        check(export.Products.First(product => product.Id == 1).Description == "•Camera IP, 'test'\r\nrand doi", "EspImport: escapes and special characters of a description are read");
        check(export.Images.Count == 1 && export.Images[1].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "EspImport: only the images of the taken products are read, as bytes");

        var dump = Path.Combine(folder, "dump.sql");
        await File.WriteAllTextAsync(dump, MysqldumpStyle(), new UTF8Encoding(false));
        var zip = Path.Combine(folder, "export.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(dump, "dump.sql");
        var fromZip = await EspExportReader.ReadAsync(zip, "export.zip");
        check(fromZip.Categories.Count == 2 && fromZip.Categories[1].Name == "B's", "EspImport: a ZIP with a mysqldump-style export is read (extended inserts, quote doubling)");
        check(fromZip.Products.Count == 2 && fromZip.Products.Any(product => product.Id == 3 && product.Description == "") && fromZip.TotalProducts == 3, "EspImport: a multi-line string and NULL are read, a zero quantity is left out");
        check(fromZip.Images.Count == 2 && fromZip.Images[1].Length == 4 && fromZip.Images[3].Length == 3, "EspImport: X'..' and 0x.. binary literals are read");

        var empty = Path.Combine(folder, "empty.sql");
        await File.WriteAllTextAsync(empty, "-- nothing here\n");
        var refused = false;
        try { await EspExportReader.ReadAsync(empty, "empty.sql"); } catch (EspExportException) { refused = true; }
        check(refused, "EspImport: a file that is not an ESPStoc export is refused with an answer");
        var wrong = false;
        try { await EspExportReader.ReadAsync(Path.Combine(folder, "x.txt"), "x.txt"); } catch (EspExportException) { wrong = true; }
        check(wrong, "EspImport: a missing or other file is refused");
    }

    private static EspExport ExportOf(params (int Id, string Category, string Sub, string Code, int Quantity)[] rows)
    {
        var categories = rows.Select(row => row.Category).Distinct().Select((name, index) => new EspCategory(index + 1, name)).ToList();
        var subs = rows.Select(row => (row.Category, row.Sub)).Distinct().Select((pair, index) => new EspSubcategory(index + 1, categories.First(category => category.Name == pair.Category).Id, pair.Sub)).ToList();
        return new EspExport
        {
            SourceName = "test.sql", Categories = categories, Subcategories = subs, TotalProducts = rows.Length, ZeroProducts = 0, Images = new Dictionary<int, byte[]> { [rows[0].Id] = [1] },
            Products = [.. rows.Select(row => new EspProduct(row.Id, categories.First(category => category.Name == row.Category).Id,
                subs.First(sub => sub.Name == row.Sub && sub.CategoryId == categories.First(category => category.Name == row.Category).Id).Id, row.Code, "d", row.Quantity))]
        };
    }

    private static void Planner(Action<bool, string> check)
    {
        var catalog = Catalog(
            new Product(1, "Supraveghere Video", "Camere IP", "DS-1047 - 2.8 mm", "", 7), new Product(2, "Supraveghere Video", "Hard disk", "ST8000VX - 8 TB", "", 4),
            new Product(3, "Retelistica", "Surse", "SDC-12", "", 2), new Product(4, "Supraveghere Video", "Camere IP", "OLD-NO-PARAMS", "", 1));
        var analysis = EspPlanner.Build(ExportOf(
            (10, "Supraveghere Video", "Camere IP", "DS-1047", 14),
            (11, "Supraveghere Video", "Camere IP", "DS-2CD1141G0-I-2.8mm", 3),
            (12, "Supraveghere Video", "Camere IP", "Mx-S26B (fisheye)", 1),
            (13, "Supraveghere Video", "Hard disk", "ST4000VX016 (4TB)", 5),
            (14, "Supraveghere Video", "Hard disk", "ST8000VX", 4),
            (15, "Supraveghere Video", "Camere IP", "OLD-NO-PARAMS", 6),
            (16, "Retelistica", "Surse", "sdc-12", 9),
            (17, "Interfonie/Telefonie", "Surse", "SURSA-INT", 3),
            (18, "Retelistica", "Switch", "SW-NOU", 2),
            (19, "Supraveghere Video", "Camere IP", "CAM-FARA-PARAMETRU", 1),
            (20, "Supraveghere Video", "Camere IP", "DS-1047 4mm", 1),
            (21, "Supraveghere Video", "Camere IP", "DS-1047-2.8mm", 2)), catalog, "a", DateTime.UtcNow);
        EspPlanItem Item(int id) => analysis.Items.First(item => item.OldId == id);

        check(Item(10).Existing?.Id == 1 && Item(10).Proposed && Item(10).FinalCode == "DS-1047 - 2.8 mm", "EspImport: a code that is the model of one existing variant is matched with it");
        check(Item(11).Existing is null && Item(11).FinalCode == "DS-2CD1141G0-I - 2.8 mm" && Item(11).Proposed && Item(11).Problems.Count == 0, "EspImport: the 2.8 mm at the end of a code gives the model and the Fixed lens value");
        check(Item(12).FinalCode == "Mx-S26B - Fisheye" && Item(12).Problems.Count == 0, "EspImport: a Fisheye ending selects the Fixed parameter of the family");
        check(Item(13).FinalCode == "ST4000VX016 - 4 TB" && Item(13).Problems.Count == 0 && Item(13).Existing is null, "EspImport: (4TB) gives the capacity value in TB");
        check(Item(14).Existing?.Id == 2, "EspImport: a model without a value reaches the single existing variant and its parameters");
        check(Item(15).Completes && Item(15).HasProblems && Item(15).Slots.Count == 1, "EspImport: an existing product that lacks its required parameters must get them before it receives the quantity");
        Item(15).BaseModel = "OLD-NO-PARAMS";
        Item(15).Choices = new() { [0] = new EspSlotChoice(30, 55, null) };
        EspPlanner.Evaluate(Item(15), catalog);
        check(Item(15).Existing?.Id == 4 && Item(15).FinalCode == "OLD-NO-PARAMS - 4 mm" && Item(15).Problems.Count == 0 && Item(15).Notes.Any(note => note.Contains("incomplet")), "EspImport: the chosen parameters complete the existing product (its code becomes model - value)");
        check(Item(14).Existing?.Id == 2 && !Item(14).Completes, "EspImport: a complete existing product is a plain link");
        check(Item(16).Existing?.Id == 3, "EspImport: codes match without regard to the case");
        check(Item(17).TargetSubcategory == "Surse (Interfonie/Telefonie)" && !Item(17).NewSubcategory, "EspImport: the subcategory named with its category in brackets is found");
        check(Item(18).Existing is null && Item(18).FinalCode == "SW-NOU" && Item(18).Problems.Count == 0, "EspImport: a new code in a subcategory without parameters is a new product");
        check(Item(19).HasProblems && Item(19).Problems[0].Contains("Alege"), "EspImport: a new product of a subcategory with parameters and no value in the code is a problem until the parameters are chosen");
        check(Item(20).FinalCode == "DS-1047 - 4 mm" && Item(20).Choices.Values.Any(choice => choice.ValueId == 55), "EspImport: 4mm is the existing value 4 of the Fixed lens");
        check(Item(21).HasProblems && Item(10).HasProblems && Item(21).Problems.Any(problem => problem.Contains("Același produs")), "EspImport: two products of the export that reach the same product are a problem on both");
        check(analysis.NotInExport().Count == 0, "EspImport: the products reached by the export are not listed as left without stock");

        // The user settles the duplicate by omitting one.
        Item(21).Omit = true;
        foreach (var item in analysis.Items) EspPlanner.Evaluate(item, catalog);
        EspPlanner.CheckDuplicates(analysis.Items);
        check(!Item(10).HasProblems && Item(21).Omit && !Item(21).HasProblems, "EspImport: omitting a product settles the duplicate");

        // Choosing the parameters by hand, with a new interval value.
        var chosen = Item(19);
        chosen.BaseModel = "CAM-FARA-PARAMETRU";
        chosen.Choices = new() { [0] = new EspSlotChoice(31, null, "3-9") };
        EspPlanner.Evaluate(chosen, catalog);
        check(chosen.Problems.Count == 0 && chosen.FinalCode == "CAM-FARA-PARAMETRU - 3-9 mm" && chosen.Notes.Any(note => note.Contains("Valoare nouă")), "EspImport: a typed new interval value gives the code and is announced");

        // A product whose parameters are not settled can be taken frozen: no problem, its code stays as it is, it gets only its quantity.
        var fresh = Item(19);
        fresh.BaseModel = ""; fresh.Choices = []; fresh.Frozen = true;
        EspPlanner.Evaluate(fresh, catalog);
        check(fresh.IsFrozen && fresh.Problems.Count == 0 && fresh.FinalCode == "CAM-FARA-PARAMETRU" && fresh.Existing is null && fresh.Notes.Any(note => note.Contains("înghețat")), "EspImport: a new product with no parameters chosen can be taken frozen (created without parameters)");
        var incompleteExisting = Item(15);
        incompleteExisting.Choices = []; incompleteExisting.BaseModel = ""; incompleteExisting.Frozen = true;
        EspPlanner.Evaluate(incompleteExisting, catalog);
        check(incompleteExisting.IsFrozen && incompleteExisting.Existing?.Id == 4 && incompleteExisting.FinalCode == "OLD-NO-PARAMS" && incompleteExisting.Problems.Count == 0, "EspImport: an existing incomplete product taken frozen stays as it is and receives only the quantity");
        check(analysis.FrozenProducts == 2, "EspImport: the frozen products are counted");
        Item(18).Frozen = true;
        EspPlanner.Evaluate(Item(18), catalog);
        check(!Item(18).IsFrozen && Item(18).Existing is null && Item(18).FinalCode == "SW-NOU", "EspImport: freezing means nothing for a product of a subcategory without parameters");

        // New subcategories with the same name in two categories get their category.
        var two = EspPlanner.Build(ExportOf((30, "Supraveghere Video", "Accesorii", "A1", 1), (31, "Retelistica", "Accesorii", "A2", 1), (32, "Retelistica", "Switch", "A3", 1)), catalog, "b", DateTime.UtcNow);
        check(two.Items.Where(item => item.NewSubcategory).Select(item => item.TargetSubcategory).Distinct().Count() == 2 && two.Items.First(item => item.OldId == 30).TargetSubcategory == "Accesorii (Supraveghere Video)",
            "EspImport: a new subcategory name used in two categories is written with its category");
        check(two.NewSubcategories == 2 && two.NewCategories == 0 && two.TakenPieces == 3 && two.CanApply, "EspImport: the totals of the plan");
    }

    private static void Wiring(Action<bool, string> check)
    {
        var module = new EspImportModule();
        check(module.Id == "import-espstoc" && module.Nav.Count == 0, "EspImport: the module has the expected id and no menu entry");
        check(Permissions.IsAdminOnly("setari-import-espstoc.run") && Permissions.IsAdminOnly("setari-import-espstoc.view"), "EspImport: the permission module is reserved to the administrator");
        check(new[] { AuditActions.EspStart, AuditActions.EspDeleteMovements, AuditActions.EspDeleteInvoices, AuditActions.EspDeleteOffers, AuditActions.EspDeleteReservations, AuditActions.EspResetQuantities,
            AuditActions.EspImportStock, AuditActions.EspDeleteBackups, AuditActions.EspPurgeJournal, AuditActions.EspFinish }.Distinct().Count() == 10, "EspImport: every step of the point zero is its own journal action");
        check(EspPointZeroStore.Deletions.Select(item => item.Table).SequenceEqual(["stock_movement_history", "stock_movements", "supplier_recognitions", "supplier_invoice_lines", "supplier_invoices", "offer_lines", "offers", "project_reservations", "archive_stock_movements"]),
            "EspImport: the tables of the point zero are deleted children first");
    }

    private static async Task RealExportAsync(Action<bool, string> check)
    {
        var path = Environment.GetEnvironmentVariable("ESPIMPORT_EXPORT");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var export = await EspExportReader.ReadAsync(path, Path.GetFileName(path));
        check(export.Products.Count > 0 && export.Images.Count == export.Products.Count(product => export.Images.ContainsKey(product.Id)), $"EspImport (real export): {export.Products.Count} products with stock, {export.Products.Sum(product => product.Quantity)} pieces, {export.Images.Count} images");
        Console.WriteLine($"EspImport real export: total {export.TotalProducts}, taken {export.Products.Count}, pieces {export.Products.Sum(product => product.Quantity)}, images {export.Images.Count}");
    }
}
