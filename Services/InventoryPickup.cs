using System.Globalization;

namespace BlazorStoc.Services;

// One row of the review list built from a scanned form: either matched to a live product (ProductId set) or not
// found in the current catalogue (subtask 1.3's "Produse negasite in catalog"). RecognizedValue is editable in
// the UI before sending, which is why it travels as plain data here rather than being folded into a computed
// Difference up front - the page recomputes Difference after every correction (subtask 1.3).
public sealed record InventoryPickupLine(int? ProductId, string RawCode, string? ProductName, string? Category,
    string? Subcategory, int? CurrentStock, int? RecognizedValue, bool Uncertain)
{
    public int? Difference => CurrentStock is not null && RecognizedValue is not null ? RecognizedValue - CurrentStock : null;
    public bool FoundInCatalog => ProductId is not null;
}

public sealed record InventoryPickupReview(IReadOnlyList<InventoryPickupLine> Lines, IReadOnlyList<InventoryPickupLine> NotFound);
public sealed record InventoryPickupAppliedLine(int ProductId, string ProductName, int Difference);
public sealed record InventoryPickupFailedLine(string ProductName, string Error);
public sealed record InventoryPickupApplyResult(IReadOnlyList<InventoryPickupAppliedLine> Applied, IReadOnlyList<InventoryPickupFailedLine> Failed);

public static class InventoryPickupRules
{
    public const string NoRowsMessage = "Nu s-a gasit nicio modificare de stoc in fisierul incarcat.";
    public const string NoSelectionMessage = "Selecteaza cel putin un produs pentru a trimite modificari in stoc.";
    public const string PartialFailureMessage = "O parte dintre modificari nu au putut fi aplicate; vezi lista de mai jos.";

    // Extends StockMovementRules.SuggestedDescription's "Corecție stoc {data}" for the exit/StockCorrection case
    // with a mention of the inventory pickup, and covers the entry (stock found in excess) case the same way -
    // entries otherwise have no description suggestion of their own.
    public static string MovementDescription(DateOnly date) =>
        $"Corecție stoc (inventar) {StockMovementRules.DisplayDate(date)}";

    // A normalized product name is matched exactly first; OCR misreads a handful of characters (confirmed against
    // the real sample form), so a close, unambiguous match is also accepted rather than sending everything else to
    // "Produse negasite in catalog". The limit stays tight enough that two different short names never collide.
    public static Product? MatchProduct(string rawCode, IReadOnlyList<Product> products)
    {
        var key = TextNormalization.UniquenessKey(rawCode);
        if (key.Length == 0) return null;
        foreach (var product in products)
            if (TextNormalization.UniquenessKey(product.Name) == key) return product;

        Product? best = null;
        var bestDistance = int.MaxValue;
        var ambiguous = false;
        var limit = Math.Max(2, key.Length / 6);
        foreach (var product in products)
        {
            var distance = LevenshteinDistance(key, TextNormalization.UniquenessKey(product.Name));
            if (distance > limit) continue;
            if (distance < bestDistance) { bestDistance = distance; best = product; ambiguous = false; }
            else if (distance == bestDistance) ambiguous = true;
        }
        return ambiguous ? null : best;
    }

    internal static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}

public interface IInventoryPickupBuilder
{
    Task<InventoryPickupReview> BuildAsync(IReadOnlyList<InventoryPickupScanRow> rows, CancellationToken cancellationToken = default);
}

// Matches OCR-scanned rows against the live catalogue and computes the warehouse-stock difference for each,
// using the same "in depozit" value (excludes vehicles) the situatia de inventar PDF itself was built from
// (InventoryReportBuilder), since "Valoare reala" is what the user physically counted in the warehouse.
public sealed class InventoryPickupBuilder(IProductRepository products, IStockMovementRepository movements) : IInventoryPickupBuilder
{
    public async Task<InventoryPickupReview> BuildAsync(IReadOnlyList<InventoryPickupScanRow> rows, CancellationToken cancellationToken = default)
    {
        var productsTask = products.GetProductsAsync(cancellationToken);
        var inVehiclesTask = movements.GetQuantitiesInVehiclesAsync(cancellationToken);
        await Task.WhenAll(productsTask, inVehiclesTask).ConfigureAwait(false);
        var allProducts = await productsTask;
        var inVehicles = await inVehiclesTask;

        var lines = new List<InventoryPickupLine>();
        var notFound = new List<InventoryPickupLine>();
        foreach (var row in rows)
        {
            var product = InventoryPickupRules.MatchProduct(row.RawCode, allProducts);
            if (product is null)
            {
                notFound.Add(new InventoryPickupLine(null, row.RawCode, null, null, null, null, row.RecognizedValue, row.Uncertain));
                continue;
            }
            var warehouseStock = StockMovementRules.WarehouseStock(product.Quantity, inVehicles.GetValueOrDefault(product.Id));
            var line = new InventoryPickupLine(product.Id, row.RawCode, product.Name, product.Category, product.Subcategory,
                warehouseStock, row.RecognizedValue, row.Uncertain);
            // Only rows with an actual difference need a decision from the user; an exact, confident match to the
            // current stock has nothing to correct (subtask 1.3). An uncertain read always needs a look, even if
            // the (possibly wrong) recognized value happens to equal the current stock.
            if (line.Uncertain || line.Difference != 0) lines.Add(line);
        }
        return new InventoryPickupReview(lines, notFound);
    }
}

public interface IInventoryPickupApplier
{
    Task<InventoryPickupApplyResult> ApplyAsync(IReadOnlyList<InventoryPickupLine> selectedLines, CancellationToken cancellationToken = default);
}

// Applies confirmed differences as ordinary stock movements - no new movement type or schema (TODO.md's decision):
// a surplus is an Entry, a shortfall an Exit with the existing ExitDestination.StockCorrection. Each line is its
// own movement/transaction; a failure on one does not roll back the others, and is reported back explicitly
// (subtask 1.4 accepts either a single cross-product transaction or clear reporting of partial failure).
public sealed class InventoryPickupApplier(IStockMovementRepository movements, ILogger<InventoryPickupApplier> logger) : IInventoryPickupApplier
{
    public async Task<InventoryPickupApplyResult> ApplyAsync(IReadOnlyList<InventoryPickupLine> selectedLines, CancellationToken cancellationToken = default)
    {
        var applied = new List<InventoryPickupAppliedLine>();
        var failed = new List<InventoryPickupFailedLine>();
        var today = StockMovementRules.Today;
        var description = InventoryPickupRules.MovementDescription(today);
        foreach (var line in selectedLines)
        {
            if (line.ProductId is null || line.Difference is null || line.Difference == 0) continue;
            var difference = line.Difference.Value;
            var input = new StockMovementInput
            {
                Kind = difference > 0 ? StockMovementKind.Entry : StockMovementKind.Exit,
                Destination = difference > 0 ? null : ExitDestination.StockCorrection,
                Quantity = Math.Abs(difference),
                Date = today,
                Description = description
            };
            try
            {
                await movements.CreateAsync(line.ProductId.Value, input, cancellationToken).ConfigureAwait(false);
                applied.Add(new InventoryPickupAppliedLine(line.ProductId.Value, line.ProductName ?? "", difference));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError("Inventory pickup movement failed for product {ProductId} ({ErrorType}).",
                    line.ProductId, exception.GetType().Name);
                failed.Add(new InventoryPickupFailedLine(line.ProductName ?? line.RawCode, "Modificarea nu a putut fi salvata."));
            }
        }
        return new InventoryPickupApplyResult(applied, failed);
    }
}
