namespace BlazorStoc.Services;

/// <summary>
/// The unit codes of the electronic invoice (UN/ECE Recommendation 20, the Peppol BIS Billing 3.0 code list, https://docs.peppol.eu/poacc/billing/3.0/codelist/UNECERec20/).
/// A code is shown the way the stock counts it when it is a common one, otherwise by the name the list gives it.
/// The lists are data, not code: Assets/Data/unit-codes-names.tsv (the whole code list) and Assets/Data/unit-codes-short.tsv (the common codes
/// written in the short Romanian form the stock uses), both "code TAB text" per line, embedded in the assembly.
/// </summary>
public static class InvoiceUnitCodes
{
    private static readonly Dictionary<string, string> Short = EmbeddedData.ReadTable("unit-codes-short.tsv");
    private static readonly Dictionary<string, string> Names = EmbeddedData.ReadTable("unit-codes-names.tsv");

    /// <summary>The text shown for a unit code; a value that is not in the list is returned as the file has it.</summary>
    public static string Display(string code)
    {
        var key = code.Trim();
        if (key.Length == 0) return code;
        if (Short.TryGetValue(key, out var shortName)) return shortName;
        return Names.TryGetValue(key, out var name) ? name : code;
    }

    /// <summary>The name the code list gives to a code (null when the code is not in the list).</summary>
    public static string? NameOf(string code) => Names.TryGetValue(code.Trim(), out var name) ? name : null;

    internal static int NameCount => Names.Count;
}