using MySqlConnector;

namespace BlazorStoc.Services;

// Notification "Factura asteptata": free entries recorded as "achizitie fara factura" (FreeEntryType.AwaitedInvoice) that no invoice was tied to.
// One notification per GROUP (supplier + date of the entry), not per product: the object id is derived from both, the date it expires on is the
// entry date plus InvoiceWaitDays, and the template threshold (Setari -> Notificari) says how many days before that term the warning starts.
// When every entry of the group is tied to an invoice (or deleted) the group is no longer an instance and the engine closes the notification.

public sealed record AwaitedEntryItem(int MovementId, int SupplierId, string SupplierName, DateOnly Date, string ProductName, int Quantity);

// Read-only and without the operator check of the movement repository: the evaluation runs for whoever opens the application first.
public interface IAwaitedEntryReader
{
    Task<IReadOnlyList<AwaitedEntryItem>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class MariaAwaitedEntryReader(IConfiguration configuration) : IAwaitedEntryReader
{
    public async Task<IReadOnlyList<AwaitedEntryItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            SELECT m.id,m.free_supplier_id,s.name,m.movement_date,p.name,m.quantity
            FROM stock_movements m INNER JOIN suppliers s ON s.id=m.free_supplier_id INNER JOIN products p ON p.id=m.product_id
            WHERE m.kind=1 AND m.invoice_id IS NULL AND m.free_entry_type=1
            ORDER BY m.movement_date,m.id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<AwaitedEntryItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), reader.GetString(2),
                StockMovementRules.ParseStorageDate(reader.GetString(3)), reader.GetString(4), checked((int)reader.GetInt64(5))));
        return result;
    }
}

public sealed class AwaitedInvoiceSource(IAwaitedEntryReader reader, string key = ExpirySourceKeys.AwaitedInvoice) : IExpirySource
{
    // Days after the entry by which the invoice is expected.
    public const int InvoiceWaitDays = 14;
    public const string SupplierName = "furnizor";
    public const string ReceptionName = "data receptie";
    public const string ProductsName = "numar produse";

    public string Key => key;
    public string Category => "Stoc";
    public string EventName => "Factură așteptată";
    public string DateLabel => "termenului de primire a facturii";
    public int DefaultThresholdDays => 7;
    public string RemovedReason => "Toate intrările acestui furnizor din acea zi au primit factură (sau au fost șterse).";
    public string DefaultSubject => "Factură așteptată – <furnizor>";
    public string DefaultBody =>
        "De la furnizorul <furnizor> s-au primit produse la data de <data receptie> (<numar produse> produse) fără factură. " +
        "Factura era așteptată până la <data expirare> (zile rămase: <zile ramase>; zile de depășire: <zile depasire>).";
    public IReadOnlyList<ExpiryPlaceholder> Placeholders { get; } =
    [
        new(SupplierName, "Numele furnizorului", "Demo Furnizor SRL"),
        new(ReceptionName, "Data intrării fără factură, dd.mm.yyyy", "18.01.2026"),
        new(ProductsName, "Câte produse din grup așteaptă încă factura", "5")
    ];

    public string DateChangedReason(DateOnly from, DateOnly to, ExpiryInstance current) =>
        $"Termenul de primire a facturii s-a modificat de la {StockMovementRules.DisplayDate(from)} la {StockMovementRules.DisplayDate(to)}.";

    // Stable and positive: the supplier and the entry date identify the group.
    public static int ObjectId(int supplierId, DateOnly date) => unchecked(supplierId * 1_000_003 + date.DayNumber) & 0x7FFFFFFF;

    public static string Label(string supplier, DateOnly date, int products) =>
        $"Factură așteptată de la {supplier}, recepție din {StockMovementRules.DisplayDate(date)} ({StockMovementRules.ProductsLabel(products)})";

    public async Task<IReadOnlyList<ExpiryInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) =>
        (await reader.GetAsync(cancellationToken).ConfigureAwait(false)).GroupBy(item => (item.SupplierId, item.Date))
            .Select(group =>
            {
                var first = group.First();
                var products = group.Select(item => item.ProductName).Distinct().Count();
                return new ExpiryInstance(ObjectId(group.Key.SupplierId, group.Key.Date), Label(first.SupplierName, group.Key.Date, products),
                    group.Key.Date.AddDays(InvoiceWaitDays),
                    new Dictionary<string, string>
                    {
                        [SupplierName] = first.SupplierName, [ReceptionName] = StockMovementRules.DisplayDate(group.Key.Date),
                        [ProductsName] = products.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }, $"/facturi?tab=fara-factura&furnizor={group.Key.SupplierId}");
            }).ToList();
}
