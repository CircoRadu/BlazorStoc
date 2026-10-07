namespace BlazorStoc.Services;

// Notification "Iesiri peste stoc nerezolvate": a product whose stock is negative after exits over the stock. One notification per PRODUCT
// (the object id is the product id); the date it expires on is the date of the oldest uncovered exit plus RegularizeWithinDays, and the template
// threshold (Setari -> Notificari) says how many days before that term the warning starts. When an entry or a regularization brings the product
// back to zero or above, it is no longer an instance and the engine closes the notification by itself.

// Read-only and without the operator check of the movement repository: the evaluation runs for whoever opens the application first.
public interface IOverStockReader
{
    Task<IReadOnlyList<RegularizationItem>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaOverStockReader(IConfiguration configuration) : IOverStockReader
{
    public async Task<IReadOnlyList<RegularizationItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await MariaStockMovementRepository.ReadToRegularizeAsync(connection, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class OverStockSource(IOverStockReader reader, string key = ExpirySourceKeys.OverStock) : IExpirySource
{
    // Days after the first uncovered exit by which the stock should be regularized.
    public const int RegularizeWithinDays = 14;
    public const string ProductName = "produs";
    public const string FirstExitName = "data prima iesire";
    public const string ExitsName = "numar iesiri";
    public const string CauseName = "cauza";

    public string Key => key;
    public string Category => "Stoc";
    public string EventName => "Ieșiri peste stoc nerezolvate";
    public string DateLabel => "termenului de regularizare a stocului";
    public int DefaultThresholdDays => 7;
    public string RemovedReason => "Stocul produsului nu mai este negativ (intrare operată sau stoc regularizat).";
    public string DefaultSubject => "Ieșiri peste stoc nerezolvate – <produs>";
    public string DefaultBody =>
        "Produsul <produs> are stoc negativ după <numar iesiri> ieșiri peste stoc, prima din <data prima iesire> (cauza: <cauza>). " +
        "Regularizarea era așteptată până la <data expirare> (zile rămase: <zile ramase>; zile de depășire: <zile depasire>).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProductName, "Codul produsului", "Cablu UTP"),
        new(FirstExitName, "Data primei ieșiri neacoperite, dd.mm.yyyy", "18.01.2026"),
        new(ExitsName, "Câte ieșiri peste stoc nu au fost acoperite încă", "2"),
        new(CauseName, "Cauza ieșirii peste stoc, dacă a fost indicată", "Intrare neoperată")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Termenul de regularizare s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static string Label(string product, DateOnly since) =>
        $"Stoc negativ la {product}, ieșiri peste stoc din {StockMovementRules.DisplayDate(since)}";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetAsync(cancellationToken).ConfigureAwait(false))
        .Select(item => new ExpiryInstance(item.ProductId, Label(item.ProductName, item.Since), item.Since.AddDays(RegularizeWithinDays),
            new Dictionary<string, string>
            {
                [ProductName] = item.ProductName, [FirstExitName] = StockMovementRules.DisplayDate(item.Since),
                [ExitsName] = item.OverStockExits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [CauseName] = item.Cause is { } cause ? StockMovementRules.CauseLabel(cause) : "necunoscută"
            }, $"/produse/{item.ProductId}/miscari")).ToList();
}
