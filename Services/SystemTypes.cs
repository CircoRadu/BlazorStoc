using System.Globalization;
using System.Text;

namespace BlazorStoc.Services;

// The nomenclator "Tipuri de sisteme" (antiefractie, TVCI, control acces, incendiu, retelistica ...): the systems a project is made of. A type has a
// name, an order, an active flag and alternative names (an offer says "CCTV" for "TVCI"). It is never deleted: a type that is no longer offered is
// deactivated (existing uses keep it, new choices do not list it). Everyone who works with stock reads it; only an administrator changes it.
public sealed record SystemType(int Id, string Name, bool Active, int SortOrder, IReadOnlyList<string> Aliases, long Version);

public class SystemTypeOperationException(string message) : Exception(message);

public static class SystemTypeRules
{
    public const int NameMaximumLength = 100;
    public const string StaleMessage = "Tipul de sistem a fost modificat între timp. Actualizează lista și reia operația.";
    public const string NameRequiredMessage = "Completează denumirea.";
    public static string NameTooLongMessage => $"Denumirea poate avea cel mult {NameMaximumLength} de caractere.";
    public const string DuplicateMessage = "Există deja un tip de sistem sau o denumire alternativă cu această denumire (litera mare/mică, diacriticele și separatorii nu contează).";
    public const string NoChangeMessage = "Denumirea nu s-a modificat.";
    public const string AliasOfItselfMessage = "Denumirea alternativă este chiar denumirea tipului.";

    // The identity of a name: without case, diacritics and anything that is not a letter or a digit ("Control acces" = "control-acces" = "CONTROL ACCES").
    public static string Key(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }

    public static string Clean(string? text) => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // Null when the text can be a name or an alternative name of the type (ignoreId = the type being changed).
    public static string? NameProblem(string text, IEnumerable<SystemType> types, int? ignoreId = null)
    {
        if (text.Length == 0 || Key(text).Length == 0) return NameRequiredMessage;
        if (text.Length > NameMaximumLength) return NameTooLongMessage;
        var key = Key(text);
        foreach (var type in types)
        {
            if (type.Id == ignoreId) continue;
            if (Key(type.Name) == key || type.Aliases.Any(alias => Key(alias) == key)) return DuplicateMessage;
        }
        return null;
    }

    public static string? AliasProblem(string text, SystemType type, IEnumerable<SystemType> types)
    {
        if (text.Length == 0 || Key(text).Length == 0) return NameRequiredMessage;
        if (text.Length > NameMaximumLength) return NameTooLongMessage;
        if (Key(type.Name) == Key(text)) return AliasOfItselfMessage;
        return NameProblem(text, types, ignoreId: null);
    }

    public static string Target(SystemType type) => type.Name;
}

public interface ISystemTypeRepository
{
    // All types by their order (inactive ones too; the page filters).
    Task<IReadOnlyList<SystemType>> GetAllAsync(CancellationToken cancellationToken = default);
    // The type whose name or alternative name is the text (as in an offer); null when none.
    Task<SystemType?> FindAsync(string text, CancellationToken cancellationToken = default);
    Task<SystemType> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<SystemType> RenameAsync(SystemType original, string name, CancellationToken cancellationToken = default);
    Task<SystemType> SetActiveAsync(SystemType original, bool active, CancellationToken cancellationToken = default);
    // direction -1 = earlier in the order, +1 = later.
    Task<SystemType> MoveAsync(SystemType original, int direction, CancellationToken cancellationToken = default);
    Task<SystemType> AddAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default);
    Task<SystemType> RemoveAliasAsync(SystemType type, string alias, CancellationToken cancellationToken = default);
}

public static class SystemTypeNavigation
{
    public const string Url = "/nomenclator";
    public static string PageUrl(int id) => $"{Url}?tip={id}";
}
