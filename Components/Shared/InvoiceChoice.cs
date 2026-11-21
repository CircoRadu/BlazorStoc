using BlazorStoc.Services;

namespace BlazorStoc.Components.Shared;

// The choice of an invoice of a supplier in the forms that link an entry to one (the form of an entry, the window that attaches entries to an invoice): a list searched while typing, by the
// number, the date or what the label says, instead of a plain drop-down list.
public static class InvoiceChoice
{
    public static IReadOnlyList<SelectOption> Options(IEnumerable<SupplierInvoice> invoices) =>
        [.. invoices.Select(invoice => new SelectOption(invoice.Id,
            $"{invoice.Label} ({invoice.MovementCount} {(invoice.MovementCount == 1 ? "intrare" : "intrări")})",
            $"{invoice.Number} {StockMovementRules.DisplayDate(invoice.Date)} {invoice.Label}"))];

    public static string Placeholder(int? supplierId, int invoiceCount) =>
        supplierId is null ? "Alege întâi furnizorul" : invoiceCount == 0 ? "Furnizorul nu are facturi" : "Caută sau alege factura…";
}