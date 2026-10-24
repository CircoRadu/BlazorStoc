using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Bon de consum / aviz de predare (Teste utilizator 02, "Bon"): a multiple exit lists every line, long descriptions continue on the next row inside the table, the diacritics are
// right, a long operation goes to more pages without losing a line. Pure PDF drawing, no database.
public static class ConsumptionNoteChecks
{
    private static string Words(byte[] pdf, out int pages)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        pages = document.NumberOfPages;
        return string.Join(" ", document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    public static Task RunAsync(Action<bool, string> check)
    {
        var writer = new ConsumptionNotePdfWriter();
        var longText = string.Join(" ", Enumerable.Range(1, 40).Select(index => $"cuvânt{index}")) + " SFÂRȘIT-LUNG";
        var lines = new List<ExitOperationLineInfo>
        {
            new(1, 11, "Cameră IP exterior ăâîșț", 3, "Depozit", "Montaj la intrarea principală"),
            new(2, 12, "Cablu UTP Cat6 305 m", 1, "Depozit", longText),
            new(3, 13, "Sursă 12V 5A", 2, "Mașina HD-03-ESP", "")
        };
        var operation = new ExitOperationDetails(42, new DateOnly(2026, 10, 24), ExitDestination.Beneficiary, "Servicii Industriale SA", "RO1234567", "Proiect Șantier ăâîșț", null, "Aviz 5", "admin", null, null, lines);
        var pdf = writer.Write(operation, new DateTime(2026, 10, 24, 14, 0, 0));
        var text = Words(pdf, out var pages);
        check(text.Contains("Bon de consum", StringComparison.Ordinal) && text.Contains("aviz de predare", StringComparison.Ordinal) && text.Contains("42", StringComparison.Ordinal) && text.Contains("24.10.2026", StringComparison.Ordinal)
              && text.Contains("Servicii Industriale SA", StringComparison.Ordinal) && text.Contains("RO1234567", StringComparison.Ordinal) && text.Contains("Aviz 5", StringComparison.Ordinal),
            "Consumption note: the title, the number, the date, the recipient with the CUI and the reference are on the note");
        check(lines.All(line => text.Contains(line.ProductName.Split(' ')[0], StringComparison.Ordinal)) && text.Contains("ăâîșț", StringComparison.Ordinal) && text.Contains("Șantier", StringComparison.Ordinal) && text.Contains("semnătură", StringComparison.Ordinal),
            "Consumption note: every line of a multiple exit is listed, the diacritics are right and there is room for the signatures");
        check(text.Contains("cuvânt1", StringComparison.Ordinal) && text.Contains("cuvânt40", StringComparison.Ordinal) && text.Contains("SFÂRȘIT-LUNG", StringComparison.Ordinal),
            "Consumption note: a long description continues on the next rows, none of it is lost");
        var many = Enumerable.Range(1, 70).Select(index => new ExitOperationLineInfo(index, index, $"Produs lung numarul {index} cu denumire destul de lunga ca sa treaca pe doua randuri in coloana", 1 + index % 4, "Depozit", $"Descriere {index}")).ToList();
        var big = writer.Write(operation with { Lines = many }, new DateTime(2026, 10, 24, 14, 0, 0));
        var bigText = Words(big, out var bigPages);
        check(pages == 1 && bigPages >= 2 && many.All(line => bigText.Contains($"numarul {line.MovementId} ", StringComparison.Ordinal)),
            "Consumption note: a long operation goes on to the next pages without losing a line");
        return Task.CompletedTask;
    }
}
