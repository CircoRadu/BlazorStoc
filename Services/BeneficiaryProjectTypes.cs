namespace BlazorStoc.Services;

// The nomenclator "Proiecte obligatorii" (Nomenclator -> Proiecte obligatorii): the types of project every beneficiary has ("Reparații sisteme existente"). Each active type gives each
// beneficiary one project with the name of the type (made when the beneficiary is made, when the type is added or activated, and by the check at start-up for the beneficiaries that
// already exist); projects.mandatory_type_id marks it. A type is never deleted: it is deactivated (the projects already made stay, no new ones are made). Everyone who works with
// stock reads the list; only an administrator changes it.
public sealed record BeneficiaryProjectType(int Id, string Name, bool Active, int SortOrder, long Version);

public class BeneficiaryProjectTypeOperationException(string message) : Exception(message);

public static class BeneficiaryProjectTypeRules
{
    public const int NameMaximumLength = ProjectRules.NameMaximumLength;
    public const string StaleMessage = "Tipul de proiect a fost modificat între timp. Actualizează lista și reia operația.";
    public const string NameRequiredMessage = "Completează denumirea.";
    public static string NameTooLongMessage => $"Denumirea poate avea cel mult {NameMaximumLength} de caractere.";
    public const string DuplicateMessage = "Există deja un tip de proiect cu această denumire (litera mare/mică, diacriticele și separatorii nu contează).";
    public const string NoChangeMessage = "Denumirea nu s-a modificat.";
    public const string MandatoryProjectMessage = "Acesta este un proiect obligatoriu din Nomenclator → Proiecte obligatorii: nu se poate redenumi sau șterge aici (tipul se dezactivează din Nomenclator).";

    public static string Key(string text) => SystemTypeRules.Key(text);
    public static string Clean(string? text) => SystemTypeRules.Clean(text);

    // Null when the text can be the name of a type (ignoreId = the type being renamed).
    public static string? NameProblem(string text, IEnumerable<BeneficiaryProjectType> types, int? ignoreId = null)
    {
        if (text.Length == 0 || Key(text).Length == 0) return NameRequiredMessage;
        if (text.Length > NameMaximumLength) return NameTooLongMessage;
        var key = Key(text);
        return types.Any(type => type.Id != ignoreId && Key(type.Name) == key) ? DuplicateMessage : null;
    }
}

public interface IBeneficiaryProjectTypeRepository
{
    // All types by their order (inactive ones too; the page filters).
    Task<IReadOnlyList<BeneficiaryProjectType>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<BeneficiaryProjectType> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<BeneficiaryProjectType> RenameAsync(BeneficiaryProjectType original, string name, CancellationToken cancellationToken = default);
    Task<BeneficiaryProjectType> SetActiveAsync(BeneficiaryProjectType original, bool active, CancellationToken cancellationToken = default);
    // direction -1 = earlier in the order, +1 = later.
    Task<BeneficiaryProjectType> MoveAsync(BeneficiaryProjectType original, int direction, CancellationToken cancellationToken = default);
    // Makes the projects the active types ask for where they are missing, for every beneficiary (the check at start-up); returns how many projects were made.
    Task<int> EnsureProjectsAsync(CancellationToken cancellationToken = default);
}
