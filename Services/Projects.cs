using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BlazorStoc.Services;

// A project always belongs to one beneficiary; its name is unique only within that beneficiary.
// "Observations" is the project's general text field, separate from the list of ProjectObservation entries.
public sealed record Project(int Id, int BeneficiaryId, string Name, string Observations, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record ProjectObservation(int Id, int ProjectId, string Name, string Content, string Author, long Version,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

// Metadata only: the file content lives in the server file structure, never as a database BLOB.
public sealed record ProjectObservationFile(int Id, int ObservationId, string OriginalName, string StoredName,
    string ContentType, long SizeBytes, string Sha256, string Author, DateTime UploadedAtUtc);

public sealed class ProjectOperationException(string message) : Exception(message);

public sealed class ProjectInput
{
    public int BeneficiaryId { get; set; }

    [Required(ErrorMessage = "Completează denumirea proiectului.")]
    [StringLength(ProjectRules.NameMaximumLength, ErrorMessage = "Denumirea proiectului poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(ProjectRules.TextMaximumLength, ErrorMessage = "Observațiile pot avea cel mult 4.000 de caractere.")]
    public string Observations { get; set; } = "";

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public ProjectInput Validated(bool requiresReason = false)
    {
        var normalized = new ProjectInput
        {
            BeneficiaryId = BeneficiaryId,
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Observations = TextNormalization.ForStorage(Observations),
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        ProjectRules.Validate(normalized);
        if (normalized.BeneficiaryId <= 0)
            throw new ProjectOperationException("Selectează beneficiarul proiectului.");
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new ProjectOperationException(reasonError);
        return normalized;
    }

    public static ProjectInput From(Project project) =>
        new() { BeneficiaryId = project.BeneficiaryId, Name = project.Name, Observations = project.Observations };
}

public sealed class ProjectObservationInput
{
    [Required(ErrorMessage = "Completează denumirea observației.")]
    [StringLength(ProjectRules.NameMaximumLength, ErrorMessage = "Denumirea observației poate avea cel mult 200 de caractere.")]
    public string Name { get; set; } = "";

    [StringLength(ProjectRules.ObservationContentMaximumLength, ErrorMessage = "Conținutul observației poate avea cel mult 8.000 de caractere.")]
    public string Content { get; set; } = "";

    [StringLength(ChangeReasonRules.MaximumLength, ErrorMessage = ChangeReasonRules.TooLongMessage)]
    public string Reason { get; set; } = "";

    public ProjectObservationInput Validated(bool requiresReason = false)
    {
        var normalized = new ProjectObservationInput
        {
            Name = TextNormalization.ForObjectNameOrCode(Name),
            Content = TextNormalization.ForStorage(Content),
            Reason = ChangeReasonRules.Normalize(Reason)
        };
        ProjectRules.Validate(normalized);
        if (requiresReason && ChangeReasonRules.ValidationError(normalized.Reason) is { } reasonError)
            throw new ProjectOperationException(reasonError);
        return normalized;
    }

    public static ProjectObservationInput From(ProjectObservation observation) =>
        new() { Name = observation.Name, Content = observation.Content };
}

public static class ProjectRules
{
    public const int NameMaximumLength = 200;
    public const int TextMaximumLength = 4000;
    public const int ObservationContentMaximumLength = 8000;
    public const string ConcurrentDuplicateMessage =
        "Denumirea a fost folosită între timp pentru alt proiect al aceluiași beneficiar. Actualizează lista și alege altă denumire.";

    // Stored next to the beneficiary id so the database can enforce (beneficiary, normalized name) uniqueness.
    public static string NormalizedName(string? name) => TextNormalization.UniquenessKey(name);

    public static string DuplicateMessage(string existingName, string beneficiaryName) =>
        $"Beneficiarul «{beneficiaryName}» are deja proiectul «{existingName}». Alege altă denumire.";

    public static void EnsureUniqueName(IEnumerable<Project> projects, int beneficiaryId, string name, int? excludedId,
        string beneficiaryName)
    {
        var duplicate = projects.FirstOrDefault(project => project.BeneficiaryId == beneficiaryId &&
            project.Id != excludedId && TextNormalization.SameUniqueValue(project.Name, name));
        if (duplicate is not null)
            throw new ProjectOperationException(DuplicateMessage(duplicate.Name, beneficiaryName));
    }

    public static Project Create(int id, ProjectInput validated, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        return new(id, validated.BeneficiaryId, validated.Name, validated.Observations, 0, nowUtc, nowUtc);
    }

    public static Project Edited(Project original, ProjectInput validated, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        return original with
        {
            BeneficiaryId = validated.BeneficiaryId,
            Name = validated.Name,
            Observations = validated.Observations,
            Version = checked(original.Version + 1),
            UpdatedAtUtc = nowUtc
        };
    }

    public static ProjectObservation CreateObservation(int id, int projectId, ProjectObservationInput validated,
        string author, DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (projectId <= 0) throw new ProjectOperationException("Observația trebuie asociată unui proiect existent.");
        return new(id, projectId, validated.Name, validated.Content, RequireAuthor(author), 0, nowUtc, nowUtc);
    }

    public static ProjectObservation EditedObservation(ProjectObservation original, ProjectObservationInput validated,
        DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        return original with
        {
            Name = validated.Name,
            Content = validated.Content,
            Version = checked(original.Version + 1),
            UpdatedAtUtc = nowUtc
        };
    }

    public static void CheckCurrent(Project? current, Project original)
    {
        if (current is null || current != original)
            throw new ProjectOperationException("Proiectul a fost modificat sau șters între timp. Actualizează pagina și reia operația.");
    }

    public static void CheckCurrent(ProjectObservation? current, ProjectObservation original)
    {
        if (current is null || current != original)
            throw new ProjectOperationException("Observația a fost modificată sau ștearsă între timp. Actualizează pagina și reia operația.");
    }

    public static void CheckBeneficiaryExists(Beneficiary? beneficiary)
    {
        if (beneficiary is null)
            throw new ProjectOperationException("Beneficiarul selectat nu mai există. Actualizează lista și alege alt beneficiar.");
    }

    internal static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestampurile proiectelor trebuie exprimate în UTC.", nameof(value));
    }

    internal static string RequireAuthor(string? author)
    {
        var value = (author ?? "").Trim();
        return value.Length > 0 ? value : throw new ProjectOperationException("Autorul observației este obligatoriu.");
    }

    internal static void Validate(object normalized)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(normalized, new ValidationContext(normalized), results, true))
            throw new ProjectOperationException(string.Join(" ", results.Select(result => result.ErrorMessage)));
    }
}

public static class ProjectFileRules
{
    public const int OriginalNameMaximumLength = 255;

    public static ProjectObservationFile Create(int id, int observationId, string originalName, string contentType,
        long sizeBytes, string sha256, string author, DateTime uploadedAtUtc)
    {
        ProjectRules.RequireUtc(uploadedAtUtc);
        if (observationId <= 0) throw new ProjectOperationException("Fișierul trebuie asociat unei observații existente.");
        if (sizeBytes <= 0) throw new ProjectOperationException("Fișierul este gol.");
        if (string.IsNullOrWhiteSpace(contentType)) throw new ProjectOperationException("Tipul fișierului este necunoscut.");
        var hash = (sha256 ?? "").Trim().ToLowerInvariant();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ProjectOperationException("Hash-ul fișierului trebuie să fie un SHA-256 hexazecimal.");
        var displayName = SafeOriginalName(originalName);
        return new(id, observationId, displayName, NewStoredName(Path.GetExtension(displayName)), contentType.Trim(),
            sizeBytes, hash, ProjectRules.RequireAuthor(author), uploadedAtUtc);
    }

    // Keeps only the file name part for display and download; path segments and control characters are dropped.
    public static string SafeOriginalName(string? originalName)
    {
        var name = (originalName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var result = new StringBuilder(name.Length);
        foreach (var character in name.Trim())
            if (!char.IsControl(character) && Array.IndexOf(Path.GetInvalidFileNameChars(), character) < 0)
                result.Append(character);
        var value = result.ToString().Trim().TrimEnd('.');
        if (value.Length == 0) throw new ProjectOperationException("Numele fișierului nu este valid.");
        if (value.Length > OriginalNameMaximumLength)
        {
            var extension = Path.GetExtension(value);
            if (extension.Length > 20) extension = "";
            value = value[..(OriginalNameMaximumLength - extension.Length)] + extension;
        }
        return value;
    }

    // The internal name never derives from user input beyond a validated extension.
    public static string NewStoredName(string? extension)
    {
        var suffix = (extension ?? "").ToLowerInvariant();
        if (suffix.Length is < 2 or > 11 || suffix[0] != '.' || !suffix[1..].All(char.IsAsciiLetterOrDigit)) suffix = "";
        return Guid.NewGuid().ToString("N") + suffix;
    }

    public const long MaximumBytes = 20 * 1024 * 1024;
    public const int MaximumFilesPerObservation = 20;
    private static readonly HashSet<string> AcceptedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "application/pdf",
        "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain", "text/csv"
    };

    // The declared content type is checked against a signature sniffed from the actual bytes; browser-declared
    // types and extensions are never trusted alone.
    public static string DetectContentType(ReadOnlySpan<byte> content, string declaredContentType, string fileName)
    {
        if (content.Length == 0) throw new ProjectOperationException("Fișierul este gol.");
        if (content.Length > MaximumBytes) throw new ProjectOperationException("Fișierul poate avea cel mult 20 MB.");
        var sniffed = Sniff(content);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (sniffed is not null)
        {
            if (!AcceptedContentTypes.Contains(sniffed)) throw new ProjectOperationException("Formatul fișierului nu este acceptat.");
            return sniffed;
        }
        // Formats without a reliable binary signature (Office/CSV/plain text ZIP-or-text containers) fall back to
        // the declared type only when it is already on the accepted list and matches a plausible extension.
        var declared = (declaredContentType ?? "").Trim().ToLowerInvariant();
        if (AcceptedContentTypes.Contains(declared) && extension.Length > 0) return declared;
        throw new ProjectOperationException("Formatul fișierului nu a putut fi verificat. Folosește unul dintre formatele acceptate.");
    }

    private static string? Sniff(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (content.Length >= 3 && content[0] == 255 && content[1] == 216 && content[2] == 255) return "image/jpeg";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.Length >= 6 && (content[..6].SequenceEqual("GIF87a"u8) || content[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (content.Length >= 5 && content[..5].SequenceEqual("%PDF-"u8)) return "application/pdf";
        return null;
    }
}

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<Project?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<Project> CreateAsync(ProjectInput input, CancellationToken cancellationToken = default);
    Task<Project> UpdateAsync(Project original, ProjectInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(Project original, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectObservation>> GetObservationsAsync(int projectId, CancellationToken cancellationToken = default);
    Task<ProjectObservation?> GetObservationAsync(int id, CancellationToken cancellationToken = default);
    Task<ProjectObservation> CreateObservationAsync(int projectId, ProjectObservationInput input, string author,
        CancellationToken cancellationToken = default);
    Task<ProjectObservation> UpdateObservationAsync(ProjectObservation original, ProjectObservationInput input,
        CancellationToken cancellationToken = default);
    Task DeleteObservationAsync(ProjectObservation original, string reason, CancellationToken cancellationToken = default);
}

public sealed record ProjectFileContent(byte[] Content, string ContentType, string OriginalName);

public interface IProjectFileStore
{
    Task<IReadOnlyList<ProjectObservationFile>> GetFilesAsync(int observationId, CancellationToken cancellationToken = default);
    Task<ProjectFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default);
    Task<ProjectObservationFile> SaveAsync(int observationId, string originalName, string declaredContentType,
        byte[] content, string author, CancellationToken cancellationToken = default);
    Task DeleteAsync(int fileId, string reason, CancellationToken cancellationToken = default);
}
