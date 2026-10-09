namespace BlazorStoc.Services;

// A PDF that is the printed form of an e-Factura (the human-readable export of the XML): its labels are the same for every supplier, and the XML (or
// the ZIP) it comes from is read exactly, without OCR or template layout.
public static class InvoiceEFacturaPdf
{
    private static readonly string[] Labels = ["nr factura", "data emitere", "identificatorul tva", "total plata"];

    public static bool IsExport(IEnumerable<string> words)
    {
        var text = InvoiceValues.Normalize(string.Join(' ', words));
        return Labels.All(label => text.Contains(label, StringComparison.Ordinal));
    }

    public const string Notice = "Factura pare exportată în PDF din e-Factura. Dacă ai fișierul XML sau ZIP-ul ei, preia-l pe acela: se citește exact, fără potrivire de aspect.";
}
