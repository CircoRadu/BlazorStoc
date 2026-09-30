using System.Security.Cryptography;

namespace BlazorStoc.Services;

// A photo of a work point (later also of an intervention: exactly one of WorkPointId / InterventionId is set). The file is on disk
// under the service-photos asset directory (StoredName is relative to it), the row is in service_photos. Like the product images
// and the project files, the photos are not part of the database backup package.
public sealed record ServicePhoto(int Id, int? WorkPointId, int? InterventionId, string OriginalName, string StoredName, string ContentType,
    long SizeBytes, string Sha256, string Caption, string UploadedBy, DateTime UploadedUtc);

public sealed record ServicePhotoContent(byte[] Content, string ContentType, string OriginalName);

public interface IServicePhotoStore
{
    Task<IReadOnlyList<ServicePhoto>> GetForWorkPointAsync(int workPointId, CancellationToken cancellationToken = default);
    /// <summary>Number of photos per work point (id) of a beneficiary; work points without photos are absent.</summary>
    Task<IReadOnlyDictionary<int, int>> CountsForBeneficiaryAsync(int beneficiaryId, CancellationToken cancellationToken = default);
    Task<ServicePhotoContent?> GetContentAsync(int photoId, CancellationToken cancellationToken = default);
    Task<ServicePhoto> AddToWorkPointAsync(int workPointId, string originalName, byte[] content, string caption, CancellationToken cancellationToken = default);
    /// <summary>Archived deletion: the file moves to the archive directory together with the archived row.</summary>
    Task DeleteAsync(int photoId, CancellationToken cancellationToken = default);
}

public static class ServicePhotoRules
{
    public const long MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumPerOwner = 20;
    public const int CaptionMaximumLength = 200;

    // Only images, recognised by their bytes (the declared type and the extension are never trusted).
    public static string DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0) throw new WorkPointOperationException("Fișierul este gol.");
        if (content.Length > MaximumBytes) throw new WorkPointOperationException("Fotografia poate avea cel mult 10 MB.");
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (content.Length >= 3 && content[0] == 255 && content[1] == 216 && content[2] == 255) return "image/jpeg";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (content.Length >= 6 && (content[..6].SequenceEqual("GIF87a"u8) || content[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        throw new WorkPointOperationException("Formatul fotografiei nu este acceptat. Folosește JPG, PNG, WebP sau GIF.");
    }

    public static string Extension(string contentType) => contentType switch
    {
        "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", "image/gif" => ".gif",
        _ => throw new WorkPointOperationException("Formatul fotografiei nu este acceptat.")
    };

    // The internal name never derives from user input.
    public static string NewStoredName(string contentType) => Guid.NewGuid().ToString("N") + Extension(contentType);

    public static string SafeOriginalName(string? name)
    {
        try { return ProjectFileRules.SafeOriginalName(name); }
        catch (ProjectOperationException exception) { throw new WorkPointOperationException(exception.Message); }
    }

    public static string NormalizeCaption(string? caption)
    {
        var value = TextNormalization.ForObjectNameOrCode(caption);
        return value.Length <= CaptionMaximumLength ? value
            : throw new WorkPointOperationException($"Legenda poate avea cel mult {CaptionMaximumLength} de caractere.");
    }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public static WorkPointOperationException LimitReached() =>
        new($"Un punct de lucru poate avea cel mult {MaximumPerOwner} de fotografii.");

    public static WorkPointOperationException Duplicate() => new("Această fotografie a fost deja încărcată pentru punctul de lucru.");
}
