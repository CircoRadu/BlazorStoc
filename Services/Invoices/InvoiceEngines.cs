namespace BlazorStoc.Services;

// The ways the line table of an invoice can be recognised, kept side by side so that they can be compared on the same files
// (tests/BlazorStoc.InvoiceCorpus --compare). "A" is the engine the application uses; "B" is the geometry-first variant under study:
// what decides between two readings of the table is where the header, the columns and the rows are, not the values that were read
// (an OCR error in an amount cannot change the choice), and the meaning of a column is not guessed from the numbers under it - the user
// names the columns that the labels did not.
public sealed record InvoiceEngineOptions(string Id, string Name, bool ValuesInScore, bool InferMeanings, bool GridFirst = false);

public static class InvoiceEngines
{
    public static readonly InvoiceEngineOptions A = new("A", "Actual: valorile citite intra in scor", ValuesInScore: true, InferMeanings: true);
    public static readonly InvoiceEngineOptions B = new("B", "Geometric: grila tabelului intai, valorile nu intra in scor", ValuesInScore: false, InferMeanings: true, GridFirst: true);

    public static IReadOnlyList<InvoiceEngineOptions> All { get; } = [A, B];

    // The engine the application reads invoices with: the geometric one (it found the tables of every invoice tried, and read rows that A lost).
    public static InvoiceEngineOptions Default => B;

    public static InvoiceEngineOptions? Find(string? id) => All.FirstOrDefault(engine => string.Equals(engine.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
}
