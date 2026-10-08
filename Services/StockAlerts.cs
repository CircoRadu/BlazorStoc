using System.Globalization;

namespace BlazorStoc.Services;

// Three stock notifications that need a little data of their own:
//  - "Stoc sub minim": a product with a minimum stock whose stock fell below it. One notification per PRODUCT; it expires on the date of the last
//    movement of the product or of the setting of the minimum (the later one), so the warning starts at once (default threshold 0 days) and is closed by
//    the engine as soon as an entry brings the stock back to the minimum or above (or the minimum is removed).
//  - "Rezervare fara miscare": a reservation nobody changed (no exit consumed or lowered it) for StaleReservationDays; one notification per reservation.
//  - "Deficit la un proiect cu termen apropiat": a project that has a deadline and still has things to buy (the purchase list of its situation is not
//    empty); the notification expires on the deadline, so the template threshold says how many days before it the warning starts.

public sealed class StockAlertException(string message) : Exception(message);

public interface IProductMinStockRepository
{
    // Null when the product has no minimum stock.
    Task<int?> GetAsync(int productId, CancellationToken cancellationToken = default);
    Task SetAsync(int productId, int minimum, CancellationToken cancellationToken = default);
    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);
}

public interface IProjectDeadlineRepository
{
    Task<DateOnly?> GetAsync(int projectId, CancellationToken cancellationToken = default);
    Task SetAsync(int projectId, DateOnly deadline, CancellationToken cancellationToken = default);
    Task RemoveAsync(int projectId, CancellationToken cancellationToken = default);
}

public sealed record BelowMinimumItem(int ProductId, string ProductName, int Quantity, int Minimum, DateOnly Since);
public sealed record StaleReservationItem(int ReservationId, int ProjectId, string ProjectName, int ProductId, string ProductName, int Quantity, DateOnly Since);
public sealed record ProjectDeadlineItem(int ProjectId, string ProjectName, DateOnly Deadline);

// Read only and without the operator check of the repositories: the evaluation runs for whoever opens the application first.
public interface IStockAlertReader
{
    Task<IReadOnlyList<BelowMinimumItem>> GetBelowMinimumAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaleReservationItem>> GetStaleReservationsAsync(int olderThanDays, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDeadlineItem>> GetProjectDeadlinesAsync(CancellationToken cancellationToken = default);
}

public static class StockAlertRules
{
    public static readonly string MinimumMessage = $"Stocul minim trebuie să fie între 1 și {StockMovementRules.MaxQuantity} buc.";
    public const string DeadlineBeforeTodayMessage = "Termenul nu poate fi înainte de data de azi.";
    public static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed class MinStockSource(IStockAlertReader reader, string key = ExpirySourceKeys.MinStock) : IExpirySource
{
    public const string ProductName = "produs";
    public const string StockName = "stoc";
    public const string MinimumName = "stoc minim";
    public const string MissingName = "lipsa pana la minim";

    public string Key => key;
    public string Category => "Stoc";
    public string EventName => "Stoc sub minim";
    public string DateLabel => "ultimei mișcări a stocului";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Stocul produsului a revenit la minim sau peste (sau stocul minim a fost scos).";
    public string DefaultSubject => "Stoc sub minim – <produs>";
    public string DefaultBody => "Produsul <produs> are stocul <stoc> buc., sub minimul de <stoc minim> buc. (lipsesc <lipsa pana la minim> buc. până la minim).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProductName, "Codul produsului", "Cablu UTP"),
        new(StockName, "Stocul actual, în bucăți", "3"),
        new(MinimumName, "Stocul minim setat, în bucăți", "10"),
        new(MissingName, "Câte bucăți lipsesc până la minim", "7")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Data ultimei mișcări a stocului s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static string Label(string product, int stock, int minimum) => $"Stoc sub minim la {product}: {stock} buc. din minim {minimum} buc.";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetBelowMinimumAsync(cancellationToken).ConfigureAwait(false))
        .Select(item => new ExpiryInstance(item.ProductId, Label(item.ProductName, item.Quantity, item.Minimum), item.Since,
            new Dictionary<string, string>
            {
                [ProductName] = item.ProductName, [StockName] = StockAlertRules.Invariant(item.Quantity), [MinimumName] = StockAlertRules.Invariant(item.Minimum),
                [MissingName] = StockAlertRules.Invariant(item.Minimum - item.Quantity)
            }, $"/produse/{item.ProductId}/miscari")).ToList();
}

public sealed class StaleReservationSource(IStockAlertReader reader, string key = ExpirySourceKeys.StaleReservation) : IExpirySource
{
    // Days without any change of a reservation after which the warning is due.
    public const int StaleReservationDays = 30;
    public const string ProductName = "produs";
    public const string ProjectName = "proiect";
    public const string QuantityName = "cantitate rezervata";

    public string Key => key;
    public string Category => "Stoc";
    public string EventName => "Rezervare fără mișcare";
    public string DateLabel => "termenului de revizuire a rezervării";
    public int DefaultThresholdDays => 0;
    public string RemovedReason => "Rezervarea a fost consumată, eliberată sau modificată.";
    public string DefaultSubject => "Rezervare fără mișcare – <produs>";
    public string DefaultBody =>
        "Proiectul <proiect> ține rezervate <cantitate rezervata> buc. din <produs> fără nicio mișcare de peste " + "30 de zile. " +
        "Revizuirea era așteptată până la <data expirare> (zile de depășire: <zile depasire>).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProductName, "Codul produsului rezervat", "Cablu UTP"),
        new(ProjectName, "Proiectul care ține rezervarea", "Sediu nou"),
        new(QuantityName, "Bucățile rezervate", "5")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Termenul de revizuire a rezervării s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static string Label(string project, string product, int quantity) => $"Rezervare fără mișcare: {quantity} buc. {product} pentru proiectul {project}";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetStaleReservationsAsync(StaleReservationDays, cancellationToken).ConfigureAwait(false))
        .Select(item => new ExpiryInstance(item.ReservationId, Label(item.ProjectName, item.ProductName, item.Quantity), item.Since.AddDays(StaleReservationDays),
            new Dictionary<string, string>
            {
                [ProductName] = item.ProductName, [ProjectName] = item.ProjectName, [QuantityName] = StockAlertRules.Invariant(item.Quantity)
            }, $"/proiecte/{item.ProjectId}/situatie")).ToList();
}

public sealed class ProjectDeficitSource(IStockAlertReader reader, IProjectSituationReader situations, string key = ExpirySourceKeys.ProjectDeficit) : IExpirySource
{
    public const string ProjectName = "proiect";
    public const string DeadlineName = "termen proiect";
    public const string ItemsName = "numar repere de achizitionat";

    public string Key => key;
    public string Category => "Proiecte";
    public string EventName => "Deficit la un proiect cu termen apropiat";
    public string DateLabel => "termenului proiectului";
    public int DefaultThresholdDays => 14;
    public string RemovedReason => "Proiectul nu mai are deficit (sau termenul lui a fost scos).";
    public string DefaultSubject => "Deficit la proiectul <proiect>";
    public string DefaultBody =>
        "Proiectul <proiect> are termen la <termen proiect> și încă <numar repere de achizitionat> repere de achiziționat (vezi lista de achiziție din situația proiectului). " +
        "Zile rămase: <zile ramase>; zile de depășire: <zile depasire>.";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(ProjectName, "Numele proiectului", "Sediu nou"),
        new(DeadlineName, "Termenul proiectului, dd.mm.yyyy", "30.11.2026"),
        new(ItemsName, "Câte repere sunt pe lista de achiziție", "4")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Termenul proiectului s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    public static string Label(string project, DateOnly deadline) => $"Deficit la proiectul {project}, termen {StockMovementRules.DisplayDate(deadline)}";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ExpiryInstance>();
        foreach (var project in await reader.GetProjectDeadlinesAsync(cancellationToken).ConfigureAwait(false))
        {
            // A failure here (for example a user without access) makes the whole source unreadable, so no notification is closed by mistake.
            var situation = await situations.GetAsync(project.ProjectId, cancellationToken).ConfigureAwait(false);
            var items = situation.Purchase.Sum(group => group.Items.Count);
            if (items == 0) continue;
            result.Add(new ExpiryInstance(project.ProjectId, Label(project.ProjectName, project.Deadline), project.Deadline,
                new Dictionary<string, string>
                {
                    [ProjectName] = project.ProjectName, [DeadlineName] = StockMovementRules.DisplayDate(project.Deadline), [ItemsName] = StockAlertRules.Invariant(items)
                }, $"/proiecte/{project.ProjectId}/situatie"));
        }
        return result;
    }
}
