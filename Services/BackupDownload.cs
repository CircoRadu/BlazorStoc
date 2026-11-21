using System.IO.Compression;
using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Services;

/// <summary>A backup package ready to be sent to the browser: the stream (a temporary archive is deleted when it is closed), the name the browser saves it under and its type.</summary>
public sealed record BackupDownloadFile(Stream Content, string DownloadName, string ContentType, long Length, bool Archived) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed class BackupDownloadException(string message) : Exception(message);

public static class BackupDownloadRules
{
    public const string ChecksumSuffix = ".sha256";

    /// <summary>The name of a package of the backup folder: a .zip file name without a path, not a temporary file.</summary>
    public static bool IsPackageName(string? fileName) =>
        !string.IsNullOrEmpty(fileName) && fileName == Path.GetFileName(fileName) && fileName.Length <= 200 && !fileName.StartsWith('.')
        && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>The name of the archive made when the package has its checksum file beside it (two files make one archive).</summary>
    public static string ArchiveName(string packageName) => Path.GetFileNameWithoutExtension(packageName) + "-cu-suma-de-control.zip";

    /// <summary>The address the browser downloads the package from.</summary>
    public static string Url(string fileName) => "/api/backup/" + Uri.EscapeDataString(fileName);
}

/// <summary>
/// The download of a backup package of the server (administrator with the right to change the backup settings, like the copy to the NAS). A package alone is sent as it is; when its checksum
/// file is beside it, the two files go in one archive (stored, not compressed again: a package is already a zip). A package only on the NAS is first brought to the server by the
/// existing "fetch" step of the page, so the download always sends the copy that was checked by size and checksum.
/// </summary>
public interface IBackupDownloadService
{
    Task<BackupDownloadFile> OpenAsync(string fileName, CancellationToken cancellationToken = default);
}

public sealed class BackupDownloadService(IConfiguration configuration, IAccessControl access, IAuditTrail audit) : IBackupDownloadService
{
    public async Task<BackupDownloadFile> OpenAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await access.EnsureAsync("setari-backup.edit", cancellationToken).ConfigureAwait(false);
        if (!BackupDownloadRules.IsPackageName(fileName)) throw new BackupDownloadException("Pachet invalid.");
        var package = Path.Combine(MariaAssetPaths.DatabaseBackups(configuration), fileName);
        if (!File.Exists(package)) throw new BackupDownloadException("Pachetul nu mai este pe server. Actualizează lista.");
        var checksum = package + BackupDownloadRules.ChecksumSuffix;
        BackupDownloadFile result;
        try
        {
            if (File.Exists(checksum))
            {
                // The temporary archive is deleted when the response closes it.
                var temporary = Path.Combine(Path.GetTempPath(), $"blazorstoc-download-{Guid.NewGuid():N}.zip");
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        await AddAsync(archive, package, fileName, cancellationToken).ConfigureAwait(false);
                        await AddAsync(archive, checksum, fileName + BackupDownloadRules.ChecksumSuffix, cancellationToken).ConfigureAwait(false);
                    }
                    var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.DeleteOnClose | FileOptions.Asynchronous | FileOptions.SequentialScan);
                    result = new BackupDownloadFile(stream, BackupDownloadRules.ArchiveName(fileName), "application/zip", stream.Length, true);
                }
                catch { try { File.Delete(temporary); } catch (IOException) { } throw; }
            }
            else
            {
                var stream = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
                result = new BackupDownloadFile(stream, fileName, "application/zip", stream.Length, false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BackupDownloadException("Pachetul nu a putut fi citit de pe server. Reîncearcă.");
        }
        await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.DatabaseBackup, AuditActions.DownloadBackup, fileName, $"Pachet: {fileName}",
            AuditDetails.Identification(("Pachet", fileName), ("Arhivă cu suma de control", result.Archived ? "da" : "nu"), ("Dimensiune (octeți)", result.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))), "", cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task AddAsync(ZipArchive archive, string path, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        await using var target = entry.Open();
        await input.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
