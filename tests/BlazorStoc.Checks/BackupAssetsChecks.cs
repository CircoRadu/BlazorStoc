using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorStoc.Services;
using Microsoft.Extensions.Configuration;

namespace BlazorStoc.Checks;

// The images and documents inside a backup package (Services/BackupAssets.cs): what goes into the package, how it is verified, and how a restoration puts the files in place (and back).
// Only the file side is exercised here, on temporary folders; the database side of a restoration is exercised against the test database (MariaDB sections) and by a real run.
public static class BackupAssetsChecks
{
    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "blazorstoc-assets-" + Guid.NewGuid().ToString("N"));

    private static (IReadOnlyList<BackupAssetStore> Stores, string Folder) Rig()
    {
        var folder = NewFolder();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:MariaProductImagesPath"] = Path.Combine(folder, "live", "images"),
            ["Database:MariaProjectFilesPath"] = Path.Combine(folder, "live", "projects"),
            ["Database:MariaServicePhotosPath"] = Path.Combine(folder, "live", "photos"),
            ["Database:MariaArchiveFilesPath"] = Path.Combine(folder, "live", "archive")
        }).Build();
        return (BackupAssets.Stores(configuration), folder);
    }

    private static void Put(BackupAssetStore store, string relative, byte[] bytes)
    {
        var path = Path.Combine(store.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string Read(BackupAssetStore store, string relative) => File.ReadAllText(Path.Combine(store.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static async Task<(string Zip, IReadOnlyList<BackupAssetEntry> Entries)> PackAsync(IReadOnlyList<BackupAssetStore> stores, string zipPath)
    {
        var files = BackupAssets.Scan(stores);
        IReadOnlyList<BackupAssetEntry> entries;
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) entries = await BackupAssets.AddAsync(archive, stores, files, CancellationToken.None);
        return (zipPath, entries);
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        var (stores, folder) = Rig();
        try
        {
            check(stores.Select(store => store.Key).SequenceEqual(["product-images", "project-files", "service-photos", "archive-files"]) && stores[0].Root.EndsWith("images", StringComparison.Ordinal),
                "Backup files: the four asset folders of the application are the ones in the package, under the configured paths");
            var defaults = BackupAssets.Stores(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:DataRoot"] = Path.Combine(folder, "data") }).Build());
            check(defaults.All(store => store.Root.Contains(Path.Combine("assets", store.Key), StringComparison.OrdinalIgnoreCase)) && defaults.All(store => !store.Root.Contains("data-protection-keys")),
                "Backup files: by default the folders are assets/<name> of the data root, and the keys that protect the saved passwords are not among them");

            Put(stores[0], "product-1.png", [137, 80, 78, 71, 0, 255, 1]);
            Put(stores[0], "sub/dir/product-2.jpg", new byte[200_000]);
            Put(stores[1], "7/contract ținută.pdf", Encoding.UTF8.GetBytes("pdf"));
            Put(stores[3], "empty.bin", []);
            var packageZip = Path.Combine(folder, "package.zip");
            var (_, entries) = await PackAsync(stores, packageZip);
            check(entries.Count == 4 && entries.Any(entry => entry.Store == "project-files" && entry.Path == "7/contract ținută.pdf" && entry.Size == 3) && entries.Any(entry => entry.Path == "empty.bin" && entry.Size == 0)
                && entries.All(entry => entry.Sha256.Length == 64), "Backup files: every file goes into the package with its size and SHA-256 (nested folders, non-ASCII names, empty files)");
            using (var archive = ZipFile.OpenRead(packageZip))
            {
                var list = await BackupAssets.ReadListAsync(archive, CancellationToken.None);
                check(archive.GetEntry("assets/product-images/sub/dir/product-2.jpg") is { Length: 200_000 } && list is not null && list.SequenceEqual(entries) && await BackupAssets.VerifyAsync(archive, list, CancellationToken.None),
                    "Backup files: the package holds the files under assets/<folder>/ and a list that checks out against them");
            }

            var signature = BackupAssets.Signature(entries);
            check(signature == BackupAssets.Signature(entries.Reverse()) && signature != BackupAssets.Signature(entries.Skip(1)) && signature != BackupAssets.Signature([.. entries.Take(3), entries[3] with { Sha256 = new string('0', 64) }])
                && await BackupAssets.LiveSignatureAsync(stores, CancellationToken.None) == signature,
                "Backup files: the signature depends on the files and their bytes, not on the order, and the files of the server give the same one as the package");
            Put(stores[0], "product-1.png", [137, 80, 78, 71, 0, 255, 2]);
            check(await BackupAssets.LiveSignatureAsync(stores, CancellationToken.None) != signature, "Backup files: one byte changed in a file changes the signature of the server (a package with the same database is then not 'identical')");
            Put(stores[0], "product-1.png", [137, 80, 78, 71, 0, 255, 1]);

            // what changed while the package was written
            var before = BackupAssets.Scan(stores);
            check(BackupAssets.Unchanged(before, BackupAssets.Scan(stores)), "Backup files: nothing changed between two listings");
            Put(stores[2], "new.jpg", [1]);
            check(!BackupAssets.Unchanged(before, BackupAssets.Scan(stores)), "Backup files: a file that appeared while the package was written is noticed");
            File.Delete(Path.Combine(stores[2].Root, "new.jpg"));
            File.AppendAllText(Path.Combine(stores[1].Root, "7", "contract ținută.pdf"), "x");
            check(!BackupAssets.Unchanged(before, BackupAssets.Scan(stores)), "Backup files: a file that grew while the package was written is noticed");
            Put(stores[1], "7/contract ținută.pdf", Encoding.UTF8.GetBytes("pdf"));

            await VerificationAsync(check, folder, packageZip, entries);
            await RestorationAsync(check, folder, stores, packageZip, entries);
            ManifestAndSpace(check, folder);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static async Task VerificationAsync(Action<bool, string> check, string folder, string packageZip, IReadOnlyList<BackupAssetEntry> entries)
    {
        // damaged content, missing entry, extra entry, unsafe or unknown names: each makes the package unusable
        async Task<bool> Verdict(Action<ZipArchive> damage, IReadOnlyList<BackupAssetEntry>? list = null)
        {
            var copy = Path.Combine(folder, "copy-" + Guid.NewGuid().ToString("N") + ".zip");
            File.Copy(packageZip, copy);
            using (var editable = ZipFile.Open(copy, ZipArchiveMode.Update)) damage(editable);
            using var archive = ZipFile.OpenRead(copy);
            return await BackupAssets.VerifyAsync(archive, list ?? entries, CancellationToken.None);
        }
        static void Rewrite(ZipArchive archive, string name, byte[] bytes)
        {
            archive.GetEntry(name)!.Delete();
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(bytes);
        }
        check(await Verdict(_ => { }), "Backup files: an untouched package verifies");
        check(!await Verdict(archive => Rewrite(archive, "assets/product-images/product-1.png", [137, 80, 78, 71, 0, 255, 9])), "Backup files: a file whose bytes changed (same size) is found by its checksum");
        check(!await Verdict(archive => archive.GetEntry("assets/product-images/product-1.png")!.Delete()), "Backup files: a file missing from the package is found");
        check(!await Verdict(archive => archive.CreateEntry("assets/product-images/extra.png")), "Backup files: a file in the package that the list does not name is found");
        check(!await Verdict(_ => { }, [.. entries.Take(3), entries[3] with { Path = "../escape.bin" }]) && !await Verdict(_ => { }, [.. entries.Take(3), entries[3] with { Store = "data-protection-keys" }])
            && !await Verdict(_ => { }, [.. entries, entries[0]]), "Backup files: a list with a name that leaves the folder, a folder that is not an asset folder, or a repeated file is refused");
        check(BackupAssets.IsSafeRelative("a/b.png") && !BackupAssets.IsSafeRelative("../a") && !BackupAssets.IsSafeRelative("a/../b") && !BackupAssets.IsSafeRelative("/a") && !BackupAssets.IsSafeRelative("a\\b") && !BackupAssets.IsSafeRelative("c:/a") && !BackupAssets.IsSafeRelative("a//b") && !BackupAssets.IsSafeRelative(""),
            "Backup files: only relative names without '..', drive letters, backslashes or empty parts are safe");
    }

    private static async Task RestorationAsync(Action<bool, string> check, string folder, IReadOnlyList<BackupAssetStore> stores, string packageZip, IReadOnlyList<BackupAssetEntry> entries)
    {
        // the server now holds other files than the package (a file only here, one changed, a whole folder the package does not have)
        Put(stores[0], "only-here.png", [5]);
        Put(stores[0], "product-1.png", [9, 9]);
        Put(stores[2], "photo.jpg", [7, 7, 7]);
        File.Delete(Path.Combine(stores[3].Root, "empty.bin"));
        StagedAssets staged;
        using (var archive = ZipFile.OpenRead(packageZip)) staged = await BackupAssets.StageAsync(archive, entries, stores, CancellationToken.None);
        check(staged.Items.Count == 4 && staged.Items.All(item => Directory.Exists(item.Staging) && item.Staging.Contains(".restore-")) && File.Exists(Path.Combine(staged.Items[0].Staging, "sub", "dir", "product-2.jpg"))
            && new FileInfo(Path.Combine(staged.Items[0].Staging, "product-2.jpg")).Exists == false && Read(stores[0], "only-here.png").Length > 0 && File.ReadAllBytes(Path.Combine(stores[0].Root, "product-1.png")).Length == 2
            && Directory.GetFiles(staged.Items[2].Staging).Length == 0,
            "Restoration: the files of the package are taken out beside the folders; the folders that run are not touched, and a folder the package has no files for is prepared empty");

        var swap = AssetSwap.Apply(staged);
        check(File.ReadAllBytes(Path.Combine(stores[0].Root, "product-1.png")).Length == 7 && !File.Exists(Path.Combine(stores[0].Root, "only-here.png")) && !File.Exists(Path.Combine(stores[2].Root, "photo.jpg"))
            && Read(stores[1], "7/contract ținută.pdf") == "pdf" && File.Exists(Path.Combine(stores[3].Root, "empty.bin")) && Directory.Exists(BackupAssets.AsidePath(stores[0].Root, staged.Id)),
            "Restoration: the folders now hold exactly the files of the package (extra files gone, changed ones replaced, missing ones back); the old folders are kept aside");
        check(await BackupAssets.LiveSignatureAsync(stores, CancellationToken.None) == BackupAssets.Signature(entries), "Restoration: the folders have the signature of the package");

        check(swap.Rollback() && File.ReadAllBytes(Path.Combine(stores[0].Root, "product-1.png")).Length == 2 && File.Exists(Path.Combine(stores[0].Root, "only-here.png")) && File.Exists(Path.Combine(stores[2].Root, "photo.jpg"))
            && !File.Exists(Path.Combine(stores[3].Root, "empty.bin")) && !Directory.Exists(BackupAssets.AsidePath(stores[0].Root, staged.Id)) && !Directory.Exists(staged.Items[0].Staging),
            "Restoration: if the database cannot be switched the old folders come back as they were and nothing of the package is left");

        // a folder that cannot be moved (a file in it is held open) stops the switch halfway: the folders already switched come back
        StagedAssets second;
        using (var archive = ZipFile.OpenRead(packageZip)) second = await BackupAssets.StageAsync(archive, entries, stores, CancellationToken.None);
        FileStream held = new(Path.Combine(stores[2].Root, "photo.jpg"), FileMode.Open, FileAccess.Read, FileShare.None);
        var failed = false;
        try { AssetSwap.Apply(second); }
        catch (IOException) { failed = true; }
        finally { held.Dispose(); }
        check(failed && File.ReadAllBytes(Path.Combine(stores[0].Root, "product-1.png")).Length == 2 && File.Exists(Path.Combine(stores[0].Root, "only-here.png")) && File.Exists(Path.Combine(stores[2].Root, "photo.jpg"))
            && stores.All(store => Directory.Exists(store.Root)),
            "Restoration: a folder that cannot be moved stops the switch and every folder already switched is put back");
        second.Discard();
        check(Directory.GetDirectories(Path.GetDirectoryName(stores[0].Root)!).All(path => !path.Contains(".restore-") && !path.Contains(".replaced-")), "Restoration: after the failed switch and the cleanup nothing is left beside the folders");

        // success: the old folders are removed afterwards
        StagedAssets third;
        using (var archive = ZipFile.OpenRead(packageZip)) third = await BackupAssets.StageAsync(archive, entries, stores, CancellationToken.None);
        var committed = AssetSwap.Apply(third);
        committed.Commit();
        check(stores.All(store => !Directory.Exists(BackupAssets.AsidePath(store.Root, third.Id))) && await BackupAssets.LiveSignatureAsync(stores, CancellationToken.None) == BackupAssets.Signature(entries),
            "Restoration: after a restoration that worked nothing is left beside the folders");

        // names that would write outside the folder never reach the disk
        var unsafeZip = Path.Combine(folder, "unsafe.zip");
        using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) archive.CreateEntry("assets/product-images/../../evil.txt");
        var thrown = false;
        using (var archive = ZipFile.OpenRead(unsafeZip))
            try { await BackupAssets.StageAsync(archive, [new BackupAssetEntry("product-images", "../../evil.txt", 0, new string('0', 64))], stores, CancellationToken.None); }
            catch (InvalidDataException) { thrown = true; }
        check(thrown && !File.Exists(Path.Combine(folder, "evil.txt")) && Directory.GetDirectories(Path.GetDirectoryName(stores[0].Root)!, "*.restore-*").Length == 0, "Restoration: a name that leaves the folder is refused and the preparation leaves nothing behind");
    }

    private static void ManifestAndSpace(Action<bool, string> check, string folder)
    {
        var old = JsonSerializer.Deserialize<BackupManifest>("""{"createdAtUtc":"2026-10-01T10:00:00Z","operatorName":"a","operatorRole":"Administrator","kind":"Manual","tableCount":3,"rowCounts":{"products":1},"schemaVersion":"x","contentSha256":"h","canonicalHash":"c"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var current = new BackupManifest(DateTime.UtcNow, "a", "Administrator", "Manual", 1, new Dictionary<string, int> { ["products"] = 1 }, "x", "h", "c", null, 3, 1234, "sig");
        var round = JsonSerializer.Deserialize<BackupManifest>(JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        check(old is { FileCount: null, AssetsSha256: null } && round is { FileCount: 3, FilesBytes: 1234, AssetsSha256: "sig" },
            "Backup files: a manifest written before the files existed is read as a package without files, and the new fields survive a round trip");

        var dir = Path.Combine(folder, "space");
        Directory.CreateDirectory(dir);
        check(BackupDiskSpace.EstimateRequiredBytes(dir, 500_000_000) >= 500_000_000 + 40_000_000 && BackupDiskSpace.EstimateRequiredBytes(dir, 0) < BackupDiskSpace.EstimateRequiredBytes(dir, 500_000_000),
            "Backup files: the room a backup needs counts the images and documents too (the first package that holds them is far bigger than the earlier ones)");
        check(BackupAssetRules.RestoredNote(0).Contains("golite") && BackupAssetRules.RestoredNote(12).Contains("12") && RestoreRules.StageMessage(RestoreStage.Importing).Contains("imaginile") && BackupRules.StageMessage(BackupStage.Saving).Contains("imaginile"),
            "Backup files: the messages of the stages and of the result say the images and files are part of it");
    }
}
