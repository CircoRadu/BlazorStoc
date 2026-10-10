using System.Reflection;
using System.Text.RegularExpressions;
using BlazorStoc.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorStoc.Checks;

// Checks that guard the structure of the application against mistakes the other checks cannot see: the routes of the pages (a page whose
// @page line was damaged still compiles and still renders in a test, but has no address) and the pure steps split out of long methods
// (the numbers of the OCR rows, the over-stock of an exit).
public static class StructureChecks
{
    public static void Run(Action<bool, string> check)
    {
        Routes(check);
        SettleNumbers(check);
        OverStock(check);
    }

    // ---- routes ----

    private static void Routes(Action<bool, string> check)
    {
        var assembly = typeof(MariaSchemaMigrations).Assembly;
        var routed = assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>().Select(route => (Type: type, route.Template))).ToList();
        string[] TemplatesOf(string typeName) => routed.Where(item => item.Type.Name == typeName).Select(item => item.Template).Order().ToArray();

        check(TemplatesOf("ProductMovements").SequenceEqual(["/produse/{Id:int}", "/produse/{Id:int}/miscari"]),
            "Routes: the product movements page answers at /produse/{id} and /produse/{id}/miscari");
        check(TemplatesOf("InvoicePickup").SequenceEqual(["/produse/preluare-factura"]),
            "Routes: the invoice pickup page answers at /produse/preluare-factura");
        check(routed.Count >= 30 && routed.GroupBy(item => item.Template, StringComparer.OrdinalIgnoreCase).All(group => group.Count() == 1),
            "Routes: every page has its own address (no two pages share a route)");

        var root = FindApplicationRoot();
        if (root is null) { check(false, "Routes: the application folder (BlazorStoc.csproj) was not found from the test output"); return; }
        var declared = new List<(string File, string Template)>();
        var damaged = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "^\uFEFF?@page\\s+\"([^\"]+)\"", RegexOptions.Multiline))
                declared.Add((Path.GetFileNameWithoutExtension(file), match.Groups[1].Value));
            // A directive that lost its @ is plain text to Razor: it compiles, and the page simply has no route / injection / attribute.
            if (Regex.IsMatch(source, "\\A\uFEFF?(page|using|inject|attribute|rendermode|implements|inherits|layout)\\s")) damaged.Add(Path.GetFileName(file));
        }
        check(damaged.Count == 0, "Routes: no .razor file starts with a directive missing its @" + (damaged.Count == 0 ? "" : " (" + string.Join(", ", damaged) + ")"));
        var missing = declared.Where(item => !routed.Any(route => route.Type.Name == item.File && route.Template == item.Template)).Select(item => $"{item.File} {item.Template}").ToList();
        var extra = routed.Where(route => !declared.Any(item => item.File == route.Type.Name && item.Template == route.Template)).Select(item => $"{item.Type.Name} {item.Template}").ToList();
        check(declared.Count >= 30 && missing.Count == 0 && extra.Count == 0,
            $"Routes: the @page lines of the {declared.Count} routes in the .razor files are the routes of the compiled pages" +
            (missing.Count + extra.Count == 0 ? "" : $" (missing: {string.Join(", ", missing)}; unexpected: {string.Join(", ", extra)})"));
    }

    private static string? FindApplicationRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BlazorStoc.csproj"))) return directory.FullName;
        return null;
    }

    // ---- "Nr. crt." of the OCR rows ----

    private static InventoryPickupOcrService.PendingRow Row(int block, int index, int? printed) =>
        new(block, index, $"COD-{block}-{index}", 1, false, printed);

    private static int?[] Numbers(IReadOnlyList<InventoryPickupOcrService.PendingRow> rows, bool hasNumberColumn = true) =>
        InventoryPickupOcrService.SettleNumbers(rows, hasNumberColumn, 1).Select(row => row.Number).ToArray();

    private static void SettleNumbers(Action<bool, string> check)
    {
        check(Numbers([Row(1, 1, 1), Row(1, 2, 2), Row(1, 3, 3)]).SequenceEqual([1, 2, 3]),
            "Nr. crt.: numbers read in order are kept");
        check(Numbers([Row(1, 1, 1), Row(1, 2, 2), Row(1, 3, 9), Row(1, 4, 4)]).SequenceEqual([1, 2, 3, 4]),
            "Nr. crt.: an isolated misread digit is corrected by the offset most rows agree on");
        check(Numbers([Row(1, 1, 1), Row(1, 2, null), Row(1, 3, 3)]).SequenceEqual([1, 2, 3]),
            "Nr. crt.: a row whose number could not be read gets it from its position");
        check(Numbers([Row(1, 1, 5), Row(1, 2, 6), Row(1, 3, 7)]).SequenceEqual([5, 6, 7]),
            "Nr. crt.: a table that continues from the previous page keeps the numbers it prints");
        check(Numbers([Row(1, 1, 1), Row(1, 2, 2), Row(2, 1, null), Row(2, 2, null)]).SequenceEqual([1, 2, null, null]),
            "Nr. crt.: a table where nothing was read keeps no number, while another table's numbers are kept");
        check(Numbers([Row(1, 1, 1), Row(1, 2, 2)], hasNumberColumn: false).SequenceEqual([null, null]),
            "Nr. crt.: without a number column on the page no row gets a number");
        check(Numbers([Row(1, 1, 0), Row(1, 2, 1)]).SequenceEqual([null, 1]),
            "Nr. crt.: a number that would not be positive is dropped");
        check(Numbers([Row(1, 1, 1), Row(1, 2, 4)]).SequenceEqual([1, 2]),
            "Nr. crt.: when two offsets are equally supported the smaller one wins (the result does not depend on the order of the rows)");
        var settled = InventoryPickupOcrService.SettleNumbers([new(1, 1, "ABC", 7, true, 1)], true, 3).Single();
        check(settled.Page == 3 && settled.RawCode == "ABC" && settled.RecognizedValue == 7 && settled.Uncertain && settled.Number == 1,
            "Nr. crt.: the page, code, value and doubt of a row are carried over unchanged");
        check(InventoryPickupOcrService.SettleNumbers([], true, 1).Count == 0, "Nr. crt.: no rows give no rows");
    }

    // ---- exits over the stock ----

    private static StockMovement Movement(StockMovementKind kind, int quantity, int? sourceVehicleId = null) =>
        new(1, 1, kind, quantity, new DateOnly(2026, 10, 1), "test", null, null, null, null, "test", 0, DateTime.UtcNow, DateTime.UtcNow,
            Destination: kind == StockMovementKind.Exit ? ExitDestination.GenericSale : null, SourceVehicleId: sourceVehicleId);

    private static void OverStock(Action<bool, string> check)
    {
        var exit = StockMovementKind.Exit;
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 10), 4, 0, false) == 6,
            "Over stock: an exit of 10 from a warehouse that held 4 is 6 over");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 3), 4, 0, false) == 0
              && MariaStockMovementRepository.OverStockOf(Movement(exit, 4), 4, 0, false) == 0,
            "Over stock: an exit that the warehouse covers, up to the last piece, is not over");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 5), -2, 0, false) == 5,
            "Over stock: a warehouse already below zero counts as empty, so the whole exit is over");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 5, sourceVehicleId: 7), null, 2, false) == 3,
            "Over stock: using 5 from a vehicle that held 2 is 3 over");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 5, sourceVehicleId: 7), null, 5, false) == 0
              && MariaStockMovementRepository.OverStockOf(Movement(exit, 5, sourceVehicleId: 7), null, 9, false) == 0,
            "Over stock: what the vehicle holds covers the exit");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 5, sourceVehicleId: 7), null, 2, physicalMove: true) == 0,
            "Over stock: a move between vehicles or a return is never over (it is refused earlier)");
        check(MariaStockMovementRepository.OverStockOf(Movement(StockMovementKind.Entry, 9), null, 0, false) == 0,
            "Over stock: an entry is never over");
        check(MariaStockMovementRepository.OverStockOf(Movement(exit, 6, sourceVehicleId: 7), 1, 0, false) == 5,
            "Over stock: the warehouse figure is used when it is known, even if a vehicle is named");
    }
}
