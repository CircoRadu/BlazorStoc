using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Bon de consum / aviz de predare of a project (a stored consumption note): every line with the date of its movement, long product names continue on the next row inside the table,
// the diacritics are right, a long note goes to more pages without losing a line, an inactive note says so; the search of the list. Pure PDF drawing and rules, no database.
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
        var lines = new List<ConsumptionNoteLine>
        {
            new(1, new DateOnly(2026, 10, 20), 11, "Cameră IP exterior ăâîșț", "Camere IP", 3),
            new(2, new DateOnly(2026, 10, 22), 12, longText, "Cabluri", 1),
            new(3, new DateOnly(2026, 10, 24), 13, "Sursă 12V 5A", "Surse", 2)
        };
        var note = new ConsumptionNote(42, 7, "Servicii Industriale SA", "RO1234567", 9, "Proiect Șantier ăâîșț", new DateOnly(2026, 10, 24), true, "admin", new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc), lines.Count);
        var pdf = writer.Write(new ConsumptionNoteDetails(note, lines), new DateTime(2026, 10, 24, 14, 0, 0));
        var text = Words(pdf, out var pages);
        check(text.Contains("Bon de consum", StringComparison.Ordinal) && text.Contains("aviz de predare", StringComparison.Ordinal) && text.Contains("nr. 42", StringComparison.Ordinal) && text.Contains("24.10.2026", StringComparison.Ordinal)
              && text.Contains("Servicii Industriale SA", StringComparison.Ordinal) && text.Contains("RO1234567", StringComparison.Ordinal) && text.Contains("Operator: admin", StringComparison.Ordinal),
            "Consumption note: the title, the number, the date, the beneficiary with the CUI, the project and the operator are on the note");
        check(text.Contains("20.10.2026", StringComparison.Ordinal) && text.Contains("22.10.2026", StringComparison.Ordinal) && text.Contains("Camere", StringComparison.Ordinal) && text.Contains("ăâîșț", StringComparison.Ordinal)
              && text.Contains("Șantier", StringComparison.Ordinal) && text.Contains("semnătură", StringComparison.Ordinal) && !text.Contains("INACTIV", StringComparison.Ordinal),
            "Consumption note: every line shows the date of its movement and its subcategory, the diacritics are right and there is room for the signatures");
        check(text.Contains("cuvânt1", StringComparison.Ordinal) && text.Contains("cuvânt40", StringComparison.Ordinal) && text.Contains("SFÂRȘIT-LUNG", StringComparison.Ordinal),
            "Consumption note: a long product name continues on the next rows, none of it is lost");
        var many = Enumerable.Range(1, 70).Select(index => new ConsumptionNoteLine(index, new DateOnly(2026, 10, 1).AddDays(index % 20), index, $"Produs lung numarul {index} cu denumire destul de lunga ca sa treaca pe doua randuri in coloana", "Subcategorie", 1 + index % 4)).ToList();
        var big = writer.Write(new ConsumptionNoteDetails(note with { LineCount = many.Count, Active = false }, many), new DateTime(2026, 10, 24, 14, 0, 0));
        var bigText = Words(big, out var bigPages);
        check(pages == 1 && bigPages >= 2 && many.All(line => bigText.Contains($"numarul {line.ProductId} ", StringComparison.Ordinal)) && bigText.Contains("INACTIV", StringComparison.Ordinal),
            "Consumption note: a long note goes on to the next pages without losing a line, and an inactive note says so");

        var other = note with { Id = 5, ProjectName = "Alt proiect", BeneficiaryName = "Firma Test", Active = false, Operator = "elena" };
        check(ConsumptionNoteRules.Matches(note, null) && ConsumptionNoteRules.Matches(note, "  ") && ConsumptionNoteRules.Matches(note, "industriale") && ConsumptionNoteRules.Matches(note, "santier proiect") && ConsumptionNoteRules.Matches(note, "ȘANTIER") && ConsumptionNoteRules.Matches(note, "42")
              && ConsumptionNoteRules.Matches(note, "24.10.2026") && ConsumptionNoteRules.Matches(note, "ro1234 admin") && ConsumptionNoteRules.Matches(note, "activ") && !ConsumptionNoteRules.Matches(note, "inactiv")
              && ConsumptionNoteRules.Matches(other, "inactiv elena") && !ConsumptionNoteRules.Matches(other, "industriale"),
            "Consumption notes: the search matches the number, the date, the beneficiary, the CUI, the project, the state and the operator, all words together");

        // PROP-0045: the one helper that names the files, the total note and the merge for printing.
        var generated = new DateTime(2026, 11, 20, 14, 5, 0);
        check(ConsumptionNoteFiles.FileName(17, "Missys Guest House S.R.L.", "Hotel: etaj 1/2", generated) == "17 Missys Guest House S.R.L. Hotel etaj 1 2 generat 20.11.2026 14.05.pdf",
            "Consumption note file: \"<nr> <beneficiar> <proiect> generat <data ora>.pdf\", the characters Windows refuses are replaced");
        check(ConsumptionNoteFiles.FileName(3, new string('A', 200), null, generated).Length < 120 && ConsumptionNoteFiles.Part("  ?:*  ") == "-"
              && ConsumptionNoteFiles.FileName(note) == ConsumptionNoteFiles.FileName(42, note.BeneficiaryName, note.ProjectName, ConsumptionNoteFiles.Local(note.CreatedUtc)),
            "Consumption note file: a long name is shortened, an empty one gets a dash and a stored note is named by the same rule");
        check(ConsumptionNoteFiles.ContentDisposition("7 Șantier x.pdf", attachment: true).Contains("filename*=UTF-8''7%20%C8%98antier%20x.pdf", StringComparison.Ordinal)
              && ConsumptionNoteFiles.TotalFileName([note, note with { Id = 44, ProjectName = "Alt proiect" }], generated).StartsWith("42-44 Servicii Industriale SA toate proiectele generat", StringComparison.Ordinal),
            "Consumption note file: the Content-Disposition carries the name with diacritics and a total note is named with the first and the last number");
        var totalWriter = new ConsumptionNoteTotalWriter();
        var second = note with { Id = 43, ProjectId = 10, ProjectName = "Proiect Beta" };
        var totalBytes = totalWriter.Write([new ConsumptionNoteDetails(note, lines), new ConsumptionNoteDetails(second, [new ConsumptionNoteLine(1, new DateOnly(2026, 10, 25), 11, "Cameră IP exterior ăâîșț", "Camere IP", 2)])], generated);
        var totalText = Words(totalBytes, out _);
        check(totalText.Contains("Bon de consum total", StringComparison.Ordinal) && totalText.Contains("Proiect Beta", StringComparison.Ordinal) && totalText.Contains("Total general pe produs", StringComparison.Ordinal)
              && totalText.Contains("42, 43", StringComparison.Ordinal),
            "Consumption note: the total note has a section per project, the numbers of the notes and a grand total per product");
        var merged = totalWriter.Merge([pdf, totalBytes]);
        Words(merged, out var mergedPages); Words(totalBytes, out var totalPages);
        check(mergedPages == pages + totalPages, "Consumption note: the merged PDF for printing keeps every page of every note");
        return Task.CompletedTask;
    }
}
