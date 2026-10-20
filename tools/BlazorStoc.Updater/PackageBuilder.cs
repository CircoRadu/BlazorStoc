using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>
/// Makes a package (.bsu, a zip): manifest.json, its signature (manifest.sig, base64 of the 64 bytes r||s), the public key of the signer (signer.pub, base64 SPKI) and the
/// published application under app/. The migrations, the schema version and the version come from the application itself ("--describe-release" printed by the published
/// BlazorStoc.dll), so the manifest cannot disagree with what is inside.
/// </summary>
public static class PackageBuilder
{
    public static async Task<PackageManifest> PackAsync(string appFolder, string describeJson, string outFile, SigningKeyStore keys, string platform, string runtime,
        string? minFromVersion, DateTime builtUtc, string? notes = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path.Combine(appFolder, "BlazorStoc.dll"))) throw new InvalidOperationException($"{appFolder} is not a published BlazorStoc (BlazorStoc.dll is missing).");
        var (version, schemaVersion, migrations) = ReadDescription(describeJson);
        if (!Version.TryParse(version, out _)) throw new InvalidOperationException($"The version \"{version}\" is not major.minor.patch.");
        if (minFromVersion is not null && !Version.TryParse(minFromVersion, out _)) throw new InvalidOperationException($"The minimum version \"{minFromVersion}\" is not major.minor.patch.");

        var root = Path.GetFullPath(appFolder);
        var files = new List<PackageFile>();
        foreach (var full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (!PackageLayout.IsSafeRelativePath(relative)) throw new InvalidOperationException($"The path \"{relative}\" cannot be part of a package.");
            if (PackageLayout.LooksLikeSecret(relative))
                throw new InvalidOperationException($"\"{relative}\" looks like a private file (passwords, keys, data or logs of the build machine) and must not be in a release; fix the publish, do not pack it.");
            await using var stream = File.OpenRead(full);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            files.Add(new PackageFile(relative, hash, new FileInfo(full).Length));
        }
        if (files.Count == 0) throw new InvalidOperationException("The published folder is empty.");

        var manifest = new PackageManifest(PackageManifest.CurrentFormat, PackageManifest.ProductName, version, minFromVersion, platform, runtime, builtUtc, schemaVersion, migrations, files, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, PackageManifest.Json);
        using var key = keys.LoadPrivate();
        var signature = key.SignData(manifestBytes, HashAlgorithmName.SHA256);

        var partial = outFile + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        if (File.Exists(partial)) File.Delete(partial);
        await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
        {
            Add(zip, PackageLayout.ManifestEntry, manifestBytes);
            Add(zip, PackageLayout.SignatureEntry, Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
            Add(zip, PackageLayout.SignerEntry, Encoding.ASCII.GetBytes(Convert.ToBase64String(keys.LoadPublic())));
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(PackageLayout.AppPrefix + file.Path, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(builtUtc, TimeSpan.Zero);
                await using var target = entry.Open();
                await using var source = File.OpenRead(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
        File.Move(partial, outFile, overwrite: true);
        return manifest;
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var target = entry.Open();
        target.Write(content);
    }

    private static (string Version, int SchemaVersion, List<PackageMigration> Migrations) ReadDescription(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray()
            .Select(item => new PackageMigration(item.GetProperty("version").GetInt32(), item.GetProperty("description").GetString() ?? "", item.GetProperty("checksum").GetString() ?? ""))
            .OrderBy(migration => migration.Version).ToList();
        if (migrations.Count == 0 || migrations.Any(migration => migration.Checksum.Length != 64)) throw new InvalidOperationException("The description of the release has no migrations or a migration without a checksum.");
        return (root.GetProperty("version").GetString() ?? "", root.GetProperty("schemaVersion").GetInt32(), migrations);
    }
}
