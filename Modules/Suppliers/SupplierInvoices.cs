using System.Globalization;

namespace BlazorStoc.Services;

// The invoice of a supplier that stock entries were taken from (Produse -> Preluare factura). Only what identifies the invoice is kept:
// the supplier, the number and the date of issue. The same number of the same supplier is recorded once. An entry in stock is tied to an
// invoice or is free (no invoice): StockMovement.InvoiceId is null for free entries.
public sealed record SupplierInvoice(int Id, int SupplierId, string SupplierName, string Number, DateOnly Date, string CreatedBy, DateTime CreatedUtc,
    int MovementCount = 0)
{
    public string Label => $"{Number} din {StockMovementRules.DisplayDate(Date)}";
}

public sealed class SupplierInvoiceInput
{
    public int? SupplierId { get; set; }
    public string Number { get; set; } = "";
    public DateOnly? Date { get; set; }
}

public sealed class SupplierInvoiceOperationException(string message) : Exception(message);

// An entry already taken from an invoice (for the pickup that continues a partly taken invoice).
public sealed record InvoiceEntry(int MovementId, int ProductId, string ProductName, int Quantity, DateOnly Date);

public interface ISupplierInvoiceRepository
{
    Task<IReadOnlyList<SupplierInvoice>> GetForSupplierAsync(int supplierId, CancellationToken cancellationToken = default);
    // Every invoice taken, newest first (the Facturi page).
    Task<IReadOnlyList<SupplierInvoice>> GetAllAsync(CancellationToken cancellationToken = default);
    // The invoice of the supplier with that number (any writing of it), or null: a partly taken invoice is continued, not recorded again.
    Task<SupplierInvoice?> FindAsync(int supplierId, string number, CancellationToken cancellationToken = default);
    // What was already taken from the invoice, oldest first.
    Task<IReadOnlyList<InvoiceEntry>> GetEntriesAsync(int invoiceId, CancellationToken cancellationToken = default);
    // Records the invoice; throws SupplierInvoiceOperationException when the supplier already has an invoice with that number.
    Task<SupplierInvoice> CreateAsync(SupplierInvoiceInput input, CancellationToken cancellationToken = default);
    // Administrator only. Changes the number, the date and/or the supplier of the invoice (each kind is its own event in the journal);
    // throws when the invoice was changed or deleted meanwhile, or when the supplier already has that number.
    Task<SupplierInvoice> UpdateAsync(SupplierInvoice original, SupplierInvoiceInput input, string reason, CancellationToken cancellationToken = default);
    // Administrator only. An invoice with entries in stock cannot be deleted.
    Task DeleteAsync(SupplierInvoice original, string reason, CancellationToken cancellationToken = default);
}

public static class SupplierInvoiceSearch
{
    // Text matches the number or the supplier (letters and digits only, any case); supplierId and the issue-date interval are optional.
    // "Without entries" keeps only the invoices nothing was taken from.
    public static IEnumerable<SupplierInvoice> Filter(IEnumerable<SupplierInvoice> invoices, string? text, int? supplierId = null,
        DateOnly? from = null, DateOnly? to = null, bool withoutEntries = false)
    {
        var key = SupplierRules.CompactKey(text);
        return invoices.Where(invoice =>
            (key.Length == 0 || SupplierRules.CompactKey(invoice.Number).Contains(key, StringComparison.Ordinal)
                || SupplierRules.CompactKey(invoice.SupplierName).Contains(key, StringComparison.Ordinal))
            && (supplierId is null || invoice.SupplierId == supplierId)
            && (from is null || invoice.Date >= from) && (to is null || invoice.Date <= to)
            && (!withoutEntries || invoice.MovementCount == 0));
    }
}

public static class SupplierInvoiceRules
{
    public const int MaxNumberLength = 50;
    public const string SupplierRequiredMessage = "Alege furnizorul facturii.";
    public const string NumberRequiredMessage = "Completează numărul facturii.";
    public const string DateRequiredMessage = "Alege data emiterii facturii.";
    public const string SupplierMissingMessage = "Furnizorul ales nu mai există. Actualizează lista și reia operația.";

    // Two writings of a number that differ only in spaces or letter case are the same invoice ("FT 1", "ft1").
    public static string NumberKey(string? number) => SupplierRules.CompactKey(number);

    // OCR confuses look-alike characters: O/0, I/L/|/1, S/5, B/8, Z/2, G/6 are folded to one before numbers are compared.
    public static string OcrKey(string? number) =>
        new(NumberKey(number).Select(letter => letter switch { 'O' or 'Q' => '0', 'I' or 'L' => '1', 'S' => '5', 'B' => '8', 'Z' => '2', 'G' => '6', _ => letter }).ToArray());

    // The number read may be a misreading of the number of an invoice already taken: the same after folding look-alikes, or one character
    // different (inserted, missing or changed) in a number long enough for that to mean something. Never for identical numbers (those are the
    // same invoice, not a look-alike).
    public static bool LooksLike(string? read, string? stored)
    {
        var a = NumberKey(read); var b = NumberKey(stored);
        if (a.Length == 0 || b.Length == 0 || a == b) return false;
        var x = OcrKey(read); var y = OcrKey(stored);
        if (x == y) return true;
        if (Math.Min(x.Length, y.Length) < 4 || Math.Abs(x.Length - y.Length) > 1) return false;
        return EditDistanceAtMostOne(x, y);
    }

    private static bool EditDistanceAtMostOne(string x, string y)
    {
        if (x.Length < y.Length) (x, y) = (y, x);   // x is the longer
        var i = 0;
        while (i < y.Length && x[i] == y[i]) i++;
        if (i == y.Length) return true;               // y is a prefix of x (one character longer)
        return x.Length == y.Length ? x[(i + 1)..] == y[(i + 1)..] : x[(i + 1)..] == y[i..];
    }

    public static string DuplicateMessage(string supplier, string number, DateOnly date) =>
        $"Factura {number} a furnizorului «{supplier}» a fost deja preluată (emisă la {StockMovementRules.DisplayDate(date)}). O factură nu se preia de două ori.";

    public static SupplierInvoiceInput Validated(SupplierInvoiceInput input, DateOnly? today = null)
    {
        var errors = new List<string>();
        var number = TextNormalization.ForObjectNameOrCode(input.Number);
        if (input.SupplierId is null or <= 0) errors.Add(SupplierRequiredMessage);
        if (number.Length == 0) errors.Add(NumberRequiredMessage);
        else if (number.Length > MaxNumberLength) errors.Add($"Numărul facturii poate avea cel mult {MaxNumberLength} de caractere.");
        else if (NumberKey(number).Length == 0) errors.Add("Numărul facturii trebuie să conțină litere sau cifre.");
        if (input.Date is not { } date || date < StockMovementRules.EarliestDate) errors.Add(DateRequiredMessage);
        else if (date > (today ?? StockMovementRules.Today)) errors.Add("Data emiterii facturii nu poate fi în viitor.");
        if (errors.Count > 0) throw new SupplierInvoiceOperationException(string.Join(" ", errors));
        return new SupplierInvoiceInput { SupplierId = input.SupplierId, Number = number, Date = input.Date };
    }

    public const string StaleMessage = "Factura a fost modificată sau ștearsă între timp. Actualizează lista și reia operația.";
    public static string DeleteBlockedMessage(int movementCount) =>
        $"Factura are {movementCount} {(movementCount == 1 ? "intrare" : "intrări")} în stoc și nu poate fi ștearsă.";

    // The same invoice as the one the user opened (the row has no version: number, date and supplier are compared).
    public static bool SameAs(SupplierInvoice a, SupplierInvoice b) =>
        a.Id == b.Id && a.SupplierId == b.SupplierId && a.Number == b.Number && a.Date == b.Date;

    public static IReadOnlyList<AuditChange> Changes(SupplierInvoice before, SupplierInvoice after) =>
    [
        new("Furnizor", before.SupplierName, after.SupplierName), new("Număr factură", before.Number, after.Number),
        new("Data emiterii", StockMovementRules.DisplayDate(before.Date), StockMovementRules.DisplayDate(after.Date))
    ];

    public static string Target(string supplierName, string number) => $"Factura {number} · {supplierName}";

    public static string Identification(SupplierInvoice invoice) => AuditDetails.Identification(
        ("Furnizor", invoice.SupplierName), ("Număr factură", invoice.Number), ("Data emiterii", StockMovementRules.DisplayDate(invoice.Date)));

    public static string StorageDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
