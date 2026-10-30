using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Services;

/// <summary>One file of the images and documents a backup package carries: Store is one of the asset folders, Path is relative to it (always with "/"), Sha256 is lower case hex.</summary>
public sealed record BackupAssetEntry(string Store, string Path, long Size, string Sha256);

/// <summary>An asset folder of the application: Key names it inside the package (assets/&lt;Key&gt;/...), Root is where it lives on this server.</summary>
public sealed record BackupAssetStore(string Key, string Root);

public static class BackupAssetRules
{
    public const string FilesChangedMessage = "Imaginile sau fisierele au fost modificate in timpul copiei de siguranta; pachetul nu a fost salvat. Reincearca.";
    public const string FilesCorruptMessage = "Imaginile si fisierele din pachet sunt deteriorate sau lipsesc (sume de control necorespunzatoare); restaurarea a fost oprita.";
    public const string FilesFailedMessage = "Imaginile si fisierele din pachet nu au putut fi pregatite pentru restaurare; nimic nu a fost modificat.";
    public const string FilesRoomMessage = "Nu este loc pe disc pentru imaginile si fisierele din pachet; restaurarea a fost oprita si nimic nu a fost modificat.";
    public const string NoFilesNote = "Pachetul este mai vechi si nu contine imagini si fisiere; cele de acum au ramas neschimbate.";

    public static string RestoredNote(int count) => count == 0
        ? "Pachetul nu contine imagini si fisiere; folderele lor au fost golite, ca sa corespunda bazei restaurate."
        : $"Au fost restaurate si {count} imagini si fisiere.";
}

/// <summary>What the files looked like when they were listed: if anything differs after the package was written, somebody changed a file in between.</summary>
internal sealed record AssetFileState(string Store, string Path, long Length, long WrittenTicks);

/// <summary>
/// The images and documents of the application (product images, project files, service photos, the archive of deleted files) as part of a backup package. They are the folders the
/// stores of the application write to (MariaAssetPaths); the keys that protect the saved passwords (assets/data-protection-keys), the packages themselves and the run state are NOT
/// part of it: a package is copied to a NAS and handed around, and with the keys it would open every saved secret. In the package: assets/&lt;store&gt;/&lt;path&gt; for each file, and
/// assets.json (the list with sizes and SHA-256); manifest.json carries only the number of files, their size and one signature over the list, so listing packages stays cheap.
/// A restoration extracts the files next to their folders first, switches the folders (the old ones are kept aside), then switches the database; if that fails the old folders come back.
/// </summary>
internal static class BackupAssets
{
    public const string EntryPrefix = "assets/";
    public const string ListEntry = "assets.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Keys = ["product-images", "project-files", "service-photos", "archive-files", "entity-files"];

    public static IReadOnlyList<BackupAssetStore> Stores(IConfiguration configuration) =>
    [
        new(Keys[0], MariaAssetPaths.ProductImages(configuration)),
        new(Keys[1], MariaAssetPaths.ProjectFiles(configuration)),
        new(Keys[2], MariaAssetPaths.ServicePhotos(configuration)),
        new(Keys[3], MariaAssetPaths.ArchiveFiles(configuration)),
        new(Keys[4], MariaAssetPaths.EntityFiles(configuration))
    ];

    /// <summary>Every file of the folders, in a fixed order, with size and time of the last write.</summary>
    public static IReadOnlyList<AssetFileState> Scan(IReadOnlyList<BackupAssetStore> stores)
    {
        var files = new List<AssetFileState>();
        foreach (var store in stores)
        {
            if (!Directory.Exists(store.Root)) continue;
            foreach (var path in Directory.EnumerateFiles(store.Root, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(path);
                files.Add(new AssetFileState(store.Key, Path.GetRelativePath(store.Root, path).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc.Ticks));
            }
        }
        return [.. files.OrderBy(file => file.Store, StringComparer.Ordinal).ThenBy(file => file.Path, StringComparer.Ordinal)];
    }

    public static bool Unchanged(IReadOnlyList<AssetFileState> before, IReadOnlyList<AssetFileState> after) => before.SequenceEqual(after);

    /// <summary>Writes the files and the list into the archive and returns the list.</summary>
    public static async Task<IReadOnlyList<BackupAssetEntry>> AddAsync(ZipArchive archive, IReadOnlyList<BackupAssetStore> stores, IReadOnlyList<AssetFileState> files, CancellationToken token)
    {
        var entries = new List<BackupAssetEntry>(files.Count);
        var buffer = new byte[81920];
        foreach (var file in files)
        {
            var root = stores.First(store => store.Key == file.Store).Root;
            var source = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long size = 0;
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, useAsync: true))
            await using (var output = archive.CreateEntry(EntryPrefix + file.Store + "/" + file.Path, CompressionLevel.Fastest).Open())
            {
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    size += read;
                }
            }
            entries.Add(new BackupAssetEntry(file.Store, file.Path, size, Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()));
        }
        await using (var list = archive.CreateEntry(ListEntry, CompressionLevel.Optimal).Open())
            await JsonSerializer.SerializeAsync(list, entries, Json, token).ConfigureAwait(false);
        return entries;
    }

    /// <summary>One value for the whole list: equal lists (same files, same bytes) have the same signature.</summary>
    public static string Signature(IEnumerable<BackupAssetEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Store, StringComparer.Ordinal).ThenBy(entry => entry.Path, StringComparer.Ordinal))
            text.Append(entry.Store).Append('/').Append(entry.Path).Append('\t').Append(entry.Size).Append('\t').Append(entry.Sha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>The signature of the files that are on this server now (every file is read).</summary>
    public static async Task<string> LiveSignatureAsync(IReadOnlyList<BackupAssetStore> stores, CancellationToken token)
    {
        var entries = new List<BackupAssetEntry>();
        foreach (var file in Scan(stores))
        {
            var path = Path.Combine(stores.First(store => store.Key == file.Store).Root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            entries.Add(new BackupAssetEntry(file.Store, file.Path, file.Length, Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).ToLowerInvariant()));
        }
        return Signature(entries);
    }

    public static async Task<IReadOnlyList<BackupAssetEntry>?> ReadListAsync(ZipArchive archive, CancellationToken token)
    {
        try
        {
            if (archive.GetEntry(ListEntry) is not { } entry) return null;
            await using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<List<BackupAssetEntry>>(stream, Json, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException) { return null; }
    }

    public static bool IsSafeRelative(string path) =>
        path.Length > 0 && !path.Contains('\\') && !path.Contains(':') && !path.StartsWith('/') && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    /// <summary>True when the archive holds exactly the files of the list, with their sizes and SHA-256, and the list is made of known folders and safe names.</summary>
    public static async Task<bool> VerifyAsync(ZipArchive archive, IReadOnlyList<BackupAssetEntry> entries, CancellationToken token)
    {
        var expected = new Dictionary<string, BackupAssetEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (!Keys.Contains(entry.Store) || !IsSafeRelative(entry.Path) || !expected.TryAdd(EntryPrefix + entry.Store + "/" + entry.Path, entry)) return false;
        var present = archive.Entries.Where(item => item.FullName.StartsWith(EntryPrefix, StringComparison.Ordinal)).ToList();
        if (present.Count != expected.Count) return false;
        foreach (var item in present)
        {
            if (!expected.TryGetValue(item.FullName, out var entry) || item.Length != entry.Size) return false;
            await using var stream = item.Open();
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>Free space check for the extra copy a restoration needs while it prepares the files next to the folders.</summary>
    public static bool HasRoomFor(IReadOnlyList<BackupAssetStore> stores, long bytes)
    {
        foreach (var group in stores.GroupBy(store => Path.GetPathRoot(Path.GetFullPath(store.Root))))
        {
            if (group.Key is null) continue;
            if (new DriveInfo(group.Key).AvailableFreeSpace < bytes + 50_000_000) return false;
        }
        return true;
    }

    /// <summary>Takes the files out of the archive into a new folder beside each asset folder (nothing of the running folders is touched). The folders the package has no files for are made empty.</summary>
    public static async Task<StagedAssets> StageAsync(ZipArchive archive, IReadOnlyList<BackupAssetEntry> entries, IReadOnlyList<BackupAssetStore> stores, CancellationToken token)
    {
        var staged = new StagedAssets(Guid.NewGuid().ToString("N")[..8]);
        try
        {
            foreach (var store in stores)
            {
                var staging = StagingPath(store.Root, staged.Id);
                Directory.CreateDirectory(staging);
                staged.Add(store, staging);
                var prefix = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var entry in entries.Where(entry => entry.Store == store.Key))
                {
                    var target = Path.GetFullPath(Path.Combine(staging, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (!IsSafeRelative(entry.Path) || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Nume de fisier nesigur in pachet.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = (archive.GetEntry(EntryPrefix + entry.Store + "/" + entry.Path) ?? throw new InvalidDataException("Fisier lipsa din pachet.")).Open();
                    await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                }
            }
            return staged;
        }
        catch
        {
            staged.Discard();
            throw;
        }
    }

    internal static string StagingPath(string root, string id) => root.TrimEnd('\\', '/') + ".restore-" + id;
    internal static string AsidePath(string root, string id) => root.TrimEnd('\\', '/') + ".replaced-" + id;

    internal static void MoveFolder(string from, string to)
    {
        // A file that is being served at this moment keeps the folder busy for a short while.
        for (var attempt = 1; ; attempt++)
        {
            try { Directory.Move(from, to); return; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 5) { Thread.Sleep(400); }
        }
    }
}

/// <summary>The files of a package, taken out beside the asset folders and waiting to take their place.</summary>
internal sealed class StagedAssets(string id)
{
    private readonly List<(BackupAssetStore Store, string Staging)> items = [];
    public string Id { get; } = id;
    public IReadOnlyList<(BackupAssetStore Store, string Staging)> Items => items;
    public void Add(BackupAssetStore store, string staging) => items.Add((store, staging));

    public void Discard()
    {
        foreach (var (_, staging) in items)
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>The switch of the asset folders: each running folder is renamed aside, the prepared one takes its name. <see cref="Rollback"/> puts everything back; <see cref="Commit"/> removes what was put aside.</summary>
internal sealed class AssetSwap
{
    private sealed class Step(string root, string staging) { public string Root { get; } = root; public string Staging { get; } = staging; public string? Aside { get; set; } public bool Installed { get; set; } }

    private readonly List<Step> steps = [];


    public static AssetSwap Apply(StagedAssets staged)
    {
        var swap = new AssetSwap();
        try
        {
            foreach (var (store, staging) in staged.Items)
            {
                var step = new Step(store.Root, staging);
                swap.steps.Add(step);
                if (Directory.Exists(store.Root))
                {
                    step.Aside = BackupAssets.AsidePath(store.Root, staged.Id);
                    BackupAssets.MoveFolder(store.Root, step.Aside);
                }
                BackupAssets.MoveFolder(staging, store.Root);
                step.Installed = true;
            }
            return swap;
        }
        catch
        {
            swap.Rollback();
            throw;
        }
    }

    /// <summary>Puts the folders that ran before back (best effort for each folder); returns false if one could not be put back.</summary>
    public bool Rollback()
    {
        var ok = true;
        foreach (var step in Enumerable.Reverse(steps))
        {
            try
            {
                if (step.Installed) { BackupAssets.MoveFolder(step.Root, step.Staging); step.Installed = false; }
                if (step.Aside is not null && Directory.Exists(step.Aside) && !Directory.Exists(step.Root)) BackupAssets.MoveFolder(step.Aside, step.Root);
                if (Directory.Exists(step.Staging)) Directory.Delete(step.Staging, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ok = false; }
        }
        return ok;
    }

    public void Commit()
    {
        foreach (var step in steps)
            try { if (step.Aside is not null && Directory.Exists(step.Aside)) Directory.Delete(step.Aside, recursive: true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
