using System.IO.Compression;
using System.Security.Cryptography;

namespace BlazorStoc.Updater;

/// <summary>
/// Puts a package that <see cref="PackageVerifier"/> accepted on the disk: only the files of the manifest, each to a path that stays inside the destination, each hashed again
/// while it is written (what was verified is what is written, not what the file became in between), and the manifest next to them as release.json. Written to a folder of its own
/// ("&lt;version&gt;.partial"); the caller moves it into place when the old version is stopped.
/// </summary>
public static class PackageExtractor
{
    public static async Task ExtractAsync(string packagePath, PackageManifest manifest, string releaseFolder, CancellationToken cancellationToken = default)
    {
        var app = Path.Combine(releaseFolder, "app");
        if (Directory.Exists(releaseFolder)) Directory.Delete(releaseFolder, recursive: true);
        Directory.CreateDirectory(app);
        var appRoot = Path.GetFullPath(app) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(packagePath);
        foreach (var file in manifest.Files)
        {
            var target = Path.GetFullPath(Path.Combine(app, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase)) throw new UpdateStepException($"\"{file.Path}\" would be written outside the folder of the version.");
            var entry = zip.GetEntry(PackageLayout.AppPrefix + file.Path) ?? throw new UpdateStepException($"\"{file.Path}\" is not in the package.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long written = 0;
            await using (var source = entry.Open())
            await using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > file.Size) throw new UpdateStepException($"\"{file.Path}\" is larger than the manifest says.");
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (written != file.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateStepException($"\"{file.Path}\" does not match the manifest after it was written.");
        }
        var manifestEntry = zip.GetEntry(PackageLayout.ManifestEntry) ?? throw new UpdateStepException("The package has no manifest.");
        await using var manifestSource = manifestEntry.Open();
        await using var manifestTarget = File.Create(Path.Combine(releaseFolder, "release.json"));
        await manifestSource.CopyToAsync(manifestTarget, cancellationToken).ConfigureAwait(false);
    }
}
