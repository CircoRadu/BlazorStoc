namespace BlazorStoc.Services;

// The nomenclator "Componente mentenanta": the components of the maintenance interventions, shared by the maintenance contracts and by the tickets taken by an operator. A FAMILY
// (e.g. "Securitate fizica") groups components (efractie, TVCI, control acces ...); a component belongs to at most one family (a component without a family is DISCRETE). Families and
// components have a name, an order and an active flag; components also have alternative names (what a ticket says: "CCTV" for "TVCI"). Nothing is deleted: what is no longer offered is
// deactivated. Everyone who works with stock reads the lists; only an administrator changes them. The names of families, components and alternative names share one namespace.
public sealed record MaintenanceFamily(int Id, string Name, bool Active, int SortOrder, long Version);

public sealed record MaintenanceComponent(int Id, int? FamilyId, string Name, bool Active, int SortOrder, IReadOnlyList<string> Aliases, long Version)
{
    public bool IsDiscrete => FamilyId is null;
}

public sealed record MaintenanceCatalog(IReadOnlyList<MaintenanceFamily> Families, IReadOnlyList<MaintenanceComponent> Components)
{
    public IReadOnlyList<MaintenanceComponent> MembersOf(int familyId) => [.. Components.Where(component => component.FamilyId == familyId)];
    public IReadOnlyList<MaintenanceComponent> Discrete => [.. Components.Where(component => component.FamilyId is null)];
    public MaintenanceFamily? Family(int? id) => id is null ? null : Families.FirstOrDefault(family => family.Id == id);
}

public class MaintenanceComponentOperationException(string message) : Exception(message);

public static class MaintenanceComponentRules
{
    public const int NameMaximumLength = 100;
    public const string StaleMessage = "Familia sau componenta a fost modificată între timp. Actualizează lista și reia operația.";
    public const string NameRequiredMessage = "Completează denumirea.";
    public static string NameTooLongMessage => $"Denumirea poate avea cel mult {NameMaximumLength} de caractere.";
    public const string DuplicateMessage = "Există deja o familie, o componentă sau o denumire alternativă cu această denumire (litera mare/mică, diacriticele și separatorii nu contează).";
    public const string NoChangeMessage = "Denumirea nu s-a modificat.";
    public const string AliasOfItselfMessage = "Denumirea alternativă este chiar denumirea componentei.";
    public const string InactiveFamilyMessage = "Familia aleasă este inactivă: activeaz-o sau alege altă familie.";
    public const string SameFamilyMessage = "Componenta este deja în această familie.";

    // The identity of a name: without case, diacritics and anything that is not a letter or a digit (same rule as the system types).
    public static string Key(string text) => SystemTypeRules.Key(text);
    public static string Clean(string? text) => SystemTypeRules.Clean(text);

    // Null when the text can be the name of a family or of a component or an alternative name (ignoreFamilyId / ignoreComponentId = the one being changed).
    public static string? NameProblem(string text, MaintenanceCatalog catalog, int? ignoreFamilyId = null, int? ignoreComponentId = null)
    {
        if (text.Length == 0 || Key(text).Length == 0) return NameRequiredMessage;
        if (text.Length > NameMaximumLength) return NameTooLongMessage;
        var key = Key(text);
        if (catalog.Families.Any(family => family.Id != ignoreFamilyId && Key(family.Name) == key)) return DuplicateMessage;
        if (catalog.Components.Any(component => component.Id != ignoreComponentId && (Key(component.Name) == key || component.Aliases.Any(alias => Key(alias) == key)))) return DuplicateMessage;
        return null;
    }

    public static string? AliasProblem(string text, MaintenanceComponent component, MaintenanceCatalog catalog)
    {
        if (text.Length == 0 || Key(text).Length == 0) return NameRequiredMessage;
        if (text.Length > NameMaximumLength) return NameTooLongMessage;
        if (Key(component.Name) == Key(text)) return AliasOfItselfMessage;
        return NameProblem(text, catalog);
    }

    /// <summary>The component whose name or alternative name is the text (as in a ticket); null when none.</summary>
    public static MaintenanceComponent? Find(MaintenanceCatalog catalog, string text)
    {
        var key = Key(text);
        return key.Length == 0 ? null : catalog.Components.FirstOrDefault(component => Key(component.Name) == key || component.Aliases.Any(alias => Key(alias) == key));
    }

    public static string FamilyText(MaintenanceCatalog catalog, MaintenanceComponent component) => catalog.Family(component.FamilyId)?.Name ?? "Componentă discretă";
}

public interface IMaintenanceComponentRepository
{
    /// <summary>All families and components by their order (inactive ones too; the pages filter).</summary>
    Task<MaintenanceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<MaintenanceFamily> CreateFamilyAsync(string name, CancellationToken cancellationToken = default);
    Task<MaintenanceFamily> RenameFamilyAsync(MaintenanceFamily original, string name, CancellationToken cancellationToken = default);
    Task<MaintenanceFamily> SetFamilyActiveAsync(MaintenanceFamily original, bool active, CancellationToken cancellationToken = default);
    /// <summary>direction -1 = earlier in the order, +1 = later (among the families).</summary>
    Task MoveFamilyAsync(MaintenanceFamily original, int direction, CancellationToken cancellationToken = default);
    /// <summary>A new component at the end of its family (familyId null = a discrete component).</summary>
    Task<MaintenanceComponent> CreateComponentAsync(string name, int? familyId, CancellationToken cancellationToken = default);
    Task<MaintenanceComponent> RenameComponentAsync(MaintenanceComponent original, string name, CancellationToken cancellationToken = default);
    Task<MaintenanceComponent> SetComponentActiveAsync(MaintenanceComponent original, bool active, CancellationToken cancellationToken = default);
    /// <summary>direction -1 / +1 within the family of the component (or among the discrete ones).</summary>
    Task MoveComponentAsync(MaintenanceComponent original, int direction, CancellationToken cancellationToken = default);
    /// <summary>The component goes to the end of another family (or becomes discrete with familyId null).</summary>
    Task<MaintenanceComponent> ChangeFamilyAsync(MaintenanceComponent original, int? familyId, CancellationToken cancellationToken = default);
    Task<MaintenanceComponent> AddAliasAsync(MaintenanceComponent component, string alias, CancellationToken cancellationToken = default);
    Task<MaintenanceComponent> RemoveAliasAsync(MaintenanceComponent component, string alias, CancellationToken cancellationToken = default);
}
