namespace BlazorStoc.Services;

// The nomenclator "Analizor de risc" (risk_analyzers, migration 59): the person or firm that makes risk analyses. An analysis points to one of them.
// An analyzer that an analysis uses cannot be deleted. Read by everyone who can see the nomenclator or the analyses; changed with the nomenclator rights.
public sealed record RiskAnalyzer(int Id, string Name, string Notes, int UsedBy, long Version);

public class RiskAnalyzerOperationException(string message) : Exception(message);

public interface IRiskAnalyzerRepository
{
    /// <summary>Every analyzer, by name, with the number of analyses that use it.</summary>
    Task<IReadOnlyList<RiskAnalyzer>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RiskAnalyzer> CreateAsync(string name, string notes, CancellationToken cancellationToken = default);
    Task<RiskAnalyzer> UpdateAsync(RiskAnalyzer original, string name, string notes, CancellationToken cancellationToken = default);
    /// <summary>Refused while an analysis uses the analyzer.</summary>
    Task DeleteAsync(RiskAnalyzer original, string reason, CancellationToken cancellationToken = default);
}

public static class RiskAnalyzerRules
{
    public const int NameMaximumLength = 120;
    public const int NotesMaximumLength = 500;
    public const string StaleMessage = "Analizorul a fost modificat între timp. Actualizează lista și reia operația.";
    public const string NameRequiredMessage = "Completează denumirea analizorului.";
    public static string NameTooLongMessage => $"Denumirea poate avea cel mult {NameMaximumLength} de caractere.";
    public static string NotesTooLongMessage => $"Observațiile pot avea cel mult {NotesMaximumLength} de caractere.";
    public const string DuplicateMessage = "Există deja un analizor cu această denumire (litera mare/mică, diacriticele și separatorii nu contează).";
    public const string NoChangeMessage = "Datele analizorului nu s-au modificat.";
    public const string MissingMessage = "Alege analizorul de risc.";
    public const string UnknownMessage = "Analizorul ales nu mai există. Reîncarcă lista analizorilor.";

    public static string Clean(string? text) => SystemTypeRules.Clean(text);
    public static string Key(string text) => SystemTypeRules.Key(text);

    public static string? NameProblem(string name) =>
        name.Length == 0 || Key(name).Length == 0 ? NameRequiredMessage : name.Length > NameMaximumLength ? NameTooLongMessage : null;

    public static string? NotesProblem(string notes) => notes.Length > NotesMaximumLength ? NotesTooLongMessage : null;

    public static RiskAnalyzerOperationException InUse(string name, int count) =>
        new(count == 1
            ? $"Analizorul «{name}» este folosit de o analiză de risc și nu poate fi șters."
            : $"Analizorul «{name}» este folosit de {count} analize de risc și nu poate fi șters.");

    public static string Identification(string name, string notes) =>
        AuditDetails.Identification(new (string Field, string Value)[] { ("Denumire", name), ("Observații", notes) }.Where(item => item.Value.Length > 0).ToArray());
}
public static class RiskAnalyzerNavigation
{
    public const string Url = "/nomenclator?tab=analizor-risc";
    public static string PageUrl(int id) => Url;
}
