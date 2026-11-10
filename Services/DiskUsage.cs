using System.IO.Enumeration;
using Microsoft.Extensions.Hosting;

namespace BlazorStoc.Services;

// Setari -> Baza de date -> Spatiu: how much room the application takes on disk, by kind of file, and how much the partitions still have free.
// Read-only; administrators only. Every figure is measured from the folders the application itself uses, so the table closes to the whole.

/// <summary>One kind of file: what it is, where it lives, how much it takes. <see cref="Bytes"/> is null when the place is not readable from here.</summary>
public sealed record DiskUsageCategory(string Id, string Title, string Note, IReadOnlyList<string> Paths, long? Bytes, int Files, IReadOnlyList<long>? PathBytes = null);

/// <summary>A partition used by the application: its size and its free space.</summary>
public sealed record DiskUsageDrive(string Name, long TotalBytes, long FreeBytes, long ApplicationBytes);

public sealed record DiskUsageReport(DateTime MeasuredUtc, IReadOnlyList<DiskUsageCategory> Categories, IReadOnlyList<DiskUsageDrive> Drives, string? Note)
{
    public long TotalBytes => Categories.Sum(category => category.Bytes ?? 0);
}

public interface IDiskUsageService
{
    Task<DiskUsageReport> MeasureAsync(CancellationToken cancellationToken = default);
}

/// <summary>Pure folder arithmetic, apart from the service so it can be checked on temporary folders.</summary>
public static class DiskUsageMeasure
{
    /// <summary>Size and file count of a folder, leaving out the folders in <paramref name="excluded"/> and every link; unreadable entries are skipped. Null when the folder does not exist.</summary>
    public static (long Bytes, int Files)? Folder(string path, IReadOnlyCollection<string> excluded, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return null;
        var skip = excluded.Select(Normalize).ToArray();
        long bytes = 0;
        var files = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        var entries = new FileSystemEnumerable<(long Length, bool IsFile)>(path, (ref FileSystemEntry entry) => (entry.IsDirectory ? 0 : entry.Length, !entry.IsDirectory), options)
        {
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !IsExcluded(Normalize(entry.ToFullPath()), skip),
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && !IsExcluded(Normalize(entry.Directory.ToString()), skip)
        };
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytes += entry.Length;
            files++;
        }
        return (bytes, files);
    }

    public static bool IsExcluded(string normalizedPath, IReadOnlyList<string> normalizedExcluded) =>
        normalizedExcluded.Any(root => normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase) || normalizedPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

public sealed class DiskUsageService(IConfiguration configuration, IAccessControl access, IDatabaseStatusService status, IHostEnvironment? environment = null) : IDiskUsageService
{
    public async Task<DiskUsageReport> MeasureAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        string? note = null;

        string? dataDirectory = null;
        try
        {
            var database = await status.ReadDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (database.Reachable && database.RunsOnThisMachine && database.DataDirectory.Length > 0) dataDirectory = database.DataDirectory;
            else if (database.Reachable && !database.RunsOnThisMachine) note = "Baza de date rulează pe alt calculator: fișierele ei nu sunt pe discul acesta și nu sunt numărate.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not AccessDeniedException) { }

        var dataRoot = BlazorStoc.Startup.AppDataRoot.Combine(configuration, "");
        var contentRoot = environment?.ContentRootPath ?? AppContext.BaseDirectory;
        var images = new[] { MariaAssetPaths.ProductImages(configuration), MariaAssetPaths.ServicePhotos(configuration) };
        var added = new[] { MariaAssetPaths.ProjectFiles(configuration), MariaAssetPaths.EntityFiles(configuration), MariaAssetPaths.ArchiveFiles(configuration) };
        var backups = new[] { MariaAssetPaths.DatabaseBackups(configuration), MariaAssetPaths.ElcomturBackups(configuration) };
        var database2 = dataDirectory is null ? [] : new[] { dataDirectory };

        // Each wider folder is measured without the narrower ones, so nothing is counted twice and the categories add up.
        var named = images.Concat(added).Concat(backups).Concat(database2).ToArray();
        var rows = await Task.Run(() => new[]
        {
            Row("database", "Baza de date", dataDirectory is null ? "Fișierele serverului bazei de date (tabele, jurnale)." : "Fișierele serverului bazei de date (tabele, jurnale).", database2, [], dataDirectory is null, cancellationToken),
            Row("images", "Imagini", "Imaginile produselor și fotografiile de service.", images, [], false, cancellationToken),
            Row("files", "Fișiere adăugate din aplicație", "Documentele proiectelor, fișierele atașate entităților și arhiva.", added, [], false, cancellationToken),
            Row("backups", "Backup-uri", "Pachetele de backup ale bazei de date și copiile Elcomtur.", backups, [], false, cancellationToken),
            Row("application", "Fișierele aplicației", "Programul, resursele și fișierele lui de lucru.", [contentRoot], named, false, cancellationToken),
            Row("other", "Alte fișiere ale aplicației", "Restul directorului de date: programele bazei de date, chei, jurnale.", [dataRoot], named.Append(contentRoot).ToArray(), false, cancellationToken)
        }, cancellationToken).ConfigureAwait(false);

        var categories = rows.Where(row => !(row.Id == "database" && row.Bytes is null && dataDirectory is null)).ToList();
        var drives = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
            for (var index = 0; index < category.Paths.Count; index++)
            {
                var root = Path.GetPathRoot(Path.GetFullPath(category.Paths[index]));
                if (string.IsNullOrEmpty(root)) continue;
                drives[root] = drives.GetValueOrDefault(root) + (category.PathBytes is { } sizes && index < sizes.Count ? sizes[index] : 0);
            }
        var driveRows = new List<DiskUsageDrive>();
        foreach (var (root, bytes) in drives)
        {
            try
            {
                var drive = new DriveInfo(root);
                driveRows.Add(new DiskUsageDrive(root, drive.TotalSize, drive.AvailableFreeSpace, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return new DiskUsageReport(DateTime.UtcNow, categories, driveRows, note);
    }

    private static DiskUsageCategory Row(string id, string title, string note, IReadOnlyList<string> paths, IReadOnlyList<string> excluded, bool skipped, CancellationToken cancellationToken)
    {
        if (skipped) return new DiskUsageCategory(id, title, note, paths, null, 0);
        long total = 0;
        var perPath = new List<long>();
        var files = 0;
        var any = false;
        foreach (var path in paths)
        {
            var measured = DiskUsageMeasure.Folder(path, excluded.Where(root => !root.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray(), cancellationToken);
            if (measured is null) { perPath.Add(0); continue; }
            perPath.Add(measured.Value.Bytes);
            any = true;
            total += measured.Value.Bytes;
            files += measured.Value.Files;
        }
        return new DiskUsageCategory(id, title, note, paths, any ? total : 0, files, perPath);
    }
}
