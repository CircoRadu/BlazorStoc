namespace BlazorStoc.Services;

// Texts about an invoice template shown by more than one screen.
public static class InvoiceTemplateText
{
    public static string SupplierLabel(InvoiceTemplateInfo info) =>
        info.SupplierName.Length > 0 ? info.SupplierName : info.SupplierCui.Length > 0 ? "CUI " + info.SupplierCui : "furnizor nespecificat";
}
