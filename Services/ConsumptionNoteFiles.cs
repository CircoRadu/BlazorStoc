using System.Globalization;
using System.Text;

namespace BlazorStoc.Services;

// The one place that names the file of a consumption note and builds the addresses of viewing, downloading, printing and archiving: every zone of the program that
// offers a note (the table of notes, the wizard "Generează bon de consum", a link of the bon in the pages) uses it, so the name is the same everywhere:
// "<nr bon> <beneficiar> <proiect> generat <dd.MM.yyyy HH.mm>.pdf". A total note over several projects is named with the first and the last number of its notes.
public static class ConsumptionNoteFiles
{
    public const string AllProjectsLabel = "toate proiectele";
    private const int MaximumPart = 60;
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    public static string FileName(long number, string beneficiary, string? project, DateTime generatedLocal) =>
        FileName(number.ToString(CultureInfo.InvariantCulture), beneficiary, project, generatedLocal);

    public static string FileName(ConsumptionNote note) => FileName(note.Id, note.BeneficiaryName, note.ProjectName, Local(note.CreatedUtc));

    // The name of the total note made of the given notes (one beneficiary): "12-14 Beneficiar toate proiectele generat ...".
    public static string TotalFileName(IReadOnlyList<ConsumptionNote> notes, DateTime generatedLocal)
    {
        var first = notes.Min(note => note.Id); var last = notes.Max(note => note.Id);
        var number = first == last ? first.ToString(CultureInfo.InvariantCulture) : $"{first}-{last}";
        return FileName(number, notes[0].BeneficiaryName, AllProjectsLabel, generatedLocal);
    }

    // The name of the zip that holds several notes.
    public static string ZipName(DateTime generatedLocal) => $"bonuri de consum generat {generatedLocal.ToString("dd.MM.yyyy HH.mm", CultureInfo.InvariantCulture)}.zip";

    public static string MergedName(DateTime generatedLocal) => $"bonuri de consum generat {generatedLocal.ToString("dd.MM.yyyy HH.mm", CultureInfo.InvariantCulture)}.pdf";

    private static string FileName(string number, string beneficiary, string? project, DateTime generatedLocal) =>
        $"{number} {Part(beneficiary)} {Part(string.IsNullOrWhiteSpace(project) ? "fara proiect" : project)} generat {generatedLocal.ToString("dd.MM.yyyy HH.mm", CultureInfo.InvariantCulture)}.pdf";

    // The time of generation as the user sees it (the stored time is UTC).
    public static DateTime Local(DateTime createdUtc) => DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc).ToLocalTime();

    // A part of the name without the characters Windows refuses, without line breaks, with single spaces and a bounded length.
    public static string Part(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text.Trim())
        {
            if (char.IsControl(character) || Array.IndexOf(Forbidden, character) >= 0) builder.Append(' ');
            else builder.Append(character);
        }
        var clean = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (clean.Length > MaximumPart) clean = clean[..MaximumPart].TrimEnd();
        return clean.Length == 0 ? "-" : clean;
    }

    public static string PdfUrl(int id, bool download = false) => $"/api/bonuri/{id}/bon.pdf{(download ? "?descarca=true" : "")}";
    public static string ZipUrl(IEnumerable<int> ids) => $"/api/bonuri/arhiva.zip?ids={string.Join(',', ids)}";
    public static string MergedUrl(IEnumerable<int> ids) => $"/api/bonuri/toate.pdf?ids={string.Join(',', ids)}";
    public static string TotalUrl(IEnumerable<int> ids, bool download = false) => $"/api/bonuri/total.pdf?ids={string.Join(',', ids)}{(download ? "&descarca=true" : "")}";

    // Content-Disposition with the name in both forms (plain for old clients, RFC 5987 for the diacritics).
    public static string ContentDisposition(string fileName, bool attachment)
    {
        var plain = new string(fileName.Select(c => c < 128 && c >= 32 && c != '"' && c != '\\' ? c : '_').ToArray());
        return $"{(attachment ? "attachment" : "inline")}; filename=\"{plain}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }
}
