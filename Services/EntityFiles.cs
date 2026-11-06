namespace BlazorStoc.Services;

// A file attached to a work point, a maintenance contract, an intervention, a risk analysis or the beneficiary itself (a free file). The file is on disk under the
// entity-files asset directory (StoredName is relative to it), the row is in entity_files; like the photos, the files are
// carried by the backup package with the other assets.
public sealed record EntityFile(int Id, string OwnerKind, int OwnerId, string OriginalName, string StoredName, string ContentType,
    long SizeBytes, string Sha256, string UploadedBy, DateTime UploadedUtc);

public sealed record EntityFileContent(byte[] Content, string ContentType, string OriginalName);

public static class EntityFileOwners
{
    public const string WorkPoint = "workpoint", Contract = "contract", Intervention = "intervention", Risk = "risk", Beneficiary = "beneficiary";

    public static string Label(string kind) => kind switch
    {
        WorkPoint => "punct de lucru", Contract => "contract de mentenanță", Intervention => "intervenție", Risk => "analiză de risc", Beneficiary => "beneficiar", _ => kind
    };
}

public interface IEntityFileStore
{
    Task<IReadOnlyList<EntityFile>> GetAsync(string ownerKind, int ownerId, CancellationToken cancellationToken = default);
    Task<EntityFileContent?> GetContentAsync(int fileId, CancellationToken cancellationToken = default);
    /// <summary>Every file of the beneficiary: the free ones and those of its work points, contracts, interventions and risk analyses (the ones the user may see).</summary>
    Task<IReadOnlyList<BeneficiaryFileItem>> GetForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<EntityFile> AddAsync(string ownerKind, int ownerId, string originalName, string declaredContentType, byte[] content, CancellationToken cancellationToken = default);
    /// <summary>Changes the displayed name only; the extension (and so the type) stays the same.</summary>
    Task<EntityFile> RenameAsync(int fileId, string newName, CancellationToken cancellationToken = default);
    Task DeleteAsync(int fileId, CancellationToken cancellationToken = default);
}

public static class EntityFileRules
{
    public const int MaximumPerOwner = 20;
    public const long MaximumBytes = ProjectFileRules.MaximumBytes;

    // Same accepted formats and checks as the files of the project observations (bytes are sniffed, the declared type is never trusted alone).
    public static string DetectContentType(byte[] content, string declaredContentType, string fileName)
    {
        try { return ProjectFileRules.DetectContentType(content, declaredContentType, fileName); }
        catch (ProjectOperationException exception) { throw new WorkPointOperationException(exception.Message); }
    }

    public static string SafeOriginalName(string? name)
    {
        try { return ProjectFileRules.SafeOriginalName(name); }
        catch (ProjectOperationException exception) { throw new WorkPointOperationException(exception.Message); }
    }

    public static WorkPointOperationException LimitReached() => new($"Un obiect poate avea cel mult {MaximumPerOwner} de fișiere.");
    public static WorkPointOperationException Duplicate() => new("Acest fișier a fost deja încărcat aici.");
}

// One row of the "Fișiere beneficiar" table: the file, the kind of object it belongs to and that object's name (the name is empty for a free file).
public sealed record BeneficiaryFileItem(EntityFile File, string OwnerName)
{
    public bool IsFree => File.OwnerKind == EntityFileOwners.Beneficiary;
    // The title of the section of the beneficiary page the file belongs to (the section the link opens).
    public string SectionTitle => File.OwnerKind switch
    {
        EntityFileOwners.WorkPoint => "Puncte de lucru", EntityFileOwners.Contract => "Contracte de mentenanță",
        EntityFileOwners.Intervention => "Intervenții", EntityFileOwners.Risk => "Analize de risc", _ => ""
    };
}

// A file name split for editing: only the part before the extension is edited, the extension stays as it is.
public static class FileNameParts
{
    public static (string Stem, string Extension) Split(string name)
    {
        var extension = Path.GetExtension(name);
        return (name[..^extension.Length], extension);
    }
}
