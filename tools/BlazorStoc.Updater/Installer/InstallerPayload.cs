using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>What the installer says about itself (installer.json inside its payload): the version of the application it brings, the SHA-256 of the package inside, when it was made.</summary>
public sealed record InstallerInfo(string Version, string PackageSha256, DateTime BuiltUtc);

/// <summary>
/// The payload of BlazorStoc-Actualizare-&lt;version&gt;.exe (docs/PROPUNERE_ACTUALIZARE.md, section 4): a small launcher executable with a zip appended to its end, then the length of the zip
/// (8 bytes) and a marker (16 bytes). The zip holds the signed package (package.bsu, stored), the updater of that version (updater/..., the framework-dependent build with its scripts)
/// and installer.json. The launcher reads its own file; nothing is downloaded and nothing of the application on the server is needed to start it.
/// The zip is NOT signed by itself: what protects the server is the signature of the package inside, checked by the updater (the installer is a vehicle, like any file an administrator
/// copies to a server; release.ps1 prints its SHA-256 for comparing the copy with the original).
/// </summary>
public sealed class InstallerPayload : IDisposable
{
    public const string Marker = "BSU-INSTALLER-01";
    public const string PackageEntry = "package.bsu";
    public const string InfoEntry = "installer.json";
    public const string UpdaterPrefix = "updater/";
    private const int TrailerBytes = 8 + 16;

    private readonly FileStream file;
    private readonly ZipArchive archive;

    private InstallerPayload(FileStream file, ZipArchive archive, InstallerInfo info)
    {
        this.file = file;
        this.archive = archive;
        Info = info;
    }

    public InstallerInfo Info { get; }

    /// <summary>The names of the files of the updater inside the payload (without the prefix).</summary>
    public IReadOnlyList<string> UpdaterFiles => [.. archive.Entries.Where(entry => entry.FullName.StartsWith(UpdaterPrefix, StringComparison.Ordinal) && entry.Name.Length > 0).Select(entry => entry.FullName[UpdaterPrefix.Length..])];

    public void Dispose()
    {
        archive.Dispose();
        file.Dispose();
    }

    // ---- making the installer (on the build machine) ----

    /// <summary>Copies the launcher to <paramref name="outputExe"/> and appends the payload; returns the SHA-256 of the finished file.</summary>
    public static async Task<string> BuildAsync(string launcherExe, string outputExe, string packagePath, string updaterFolder, DateTime builtUtc, string version, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(launcherExe)) throw new ArgumentException($"The launcher {launcherExe} does not exist.");
        if (!File.Exists(packagePath)) throw new ArgumentException($"The package {packagePath} does not exist.");
        if (!Directory.Exists(updaterFolder) || !File.Exists(Path.Combine(updaterFolder, "BlazorStoc.Updater.dll"))) throw new ArgumentException($"{updaterFolder} does not hold BlazorStoc.Updater.dll (publish the updater first).");
        var packageSha = await Sha256Async(packagePath, cancellationToken).ConfigureAwait(false);
        var temporary = outputExe + ".payload.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputExe))!);
        try
        {
            await using (var zipStream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                var info = zip.CreateEntry(InfoEntry, CompressionLevel.Optimal);
                await using (var infoStream = info.Open()) await infoStream.WriteAsync(Encoding.UTF8.GetBytes(InfoJson(new InstallerInfo(version, packageSha, builtUtc))), cancellationToken).ConfigureAwait(false);
                // The package is already compressed: stored, so the payload is read straight from the file.
                var package = zip.CreateEntry(PackageEntry, CompressionLevel.NoCompression);
                await using (var packageStream = package.Open())
                await using (var source = File.OpenRead(packagePath)) await source.CopyToAsync(packageStream, cancellationToken).ConfigureAwait(false);
                foreach (var path in Directory.EnumerateFiles(updaterFolder, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(updaterFolder, path).Replace('\\', '/');
                    if (relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                    zip.CreateEntryFromFile(path, UpdaterPrefix + relative, CompressionLevel.Optimal);
                }
            }
            File.Copy(launcherExe, outputExe, overwrite: true);
            await using (var output = new FileStream(outputExe, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                await using (var payload = File.OpenRead(temporary)) await payload.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                var length = new FileInfo(temporary).Length;
                await output.WriteAsync(BitConverter.GetBytes(length), cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(Encoding.ASCII.GetBytes(Marker), cancellationToken).ConfigureAwait(false);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return await Sha256Async(outputExe, cancellationToken).ConfigureAwait(false);
    }

    // ---- reading it (in the launcher, on the server) ----

    /// <summary>Opens the payload of an installer file; null when the file has none (the launcher on its own, or a file that was cut short). Throws <see cref="InvalidDataException"/> when the payload is damaged.</summary>
    public static InstallerPayload? TryOpen(string exePath)
    {
        var file = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (file.Length < TrailerBytes) { file.Dispose(); return null; }
            var trailer = new byte[TrailerBytes];
            file.Seek(-TrailerBytes, SeekOrigin.End);
            file.ReadExactly(trailer);
            if (Encoding.ASCII.GetString(trailer, 8, 16) != Marker) { file.Dispose(); return null; }
            var length = BitConverter.ToInt64(trailer, 0);
            if (length <= 0 || length > file.Length - TrailerBytes) throw new InvalidDataException("The payload of the installer is cut short (the file was not copied completely?).");
            ZipArchive archive;
            try { archive = new ZipArchive(new SliceStream(file, file.Length - TrailerBytes - length, length), ZipArchiveMode.Read, leaveOpen: false); }
            catch (InvalidDataException) { throw new InvalidDataException("The payload of the installer cannot be read (the file is damaged)."); }
            try
            {
                var entry = archive.GetEntry(InfoEntry) ?? throw new InvalidDataException($"The payload of the installer has no {InfoEntry}.");
                using var stream = entry.Open();
                var info = ReadInfo(stream);
                if (archive.GetEntry(PackageEntry) is null) throw new InvalidDataException($"The payload of the installer has no {PackageEntry}.");
                if (archive.GetEntry(UpdaterPrefix + "BlazorStoc.Updater.dll") is null) throw new InvalidDataException("The payload of the installer has no updater.");
                return new InstallerPayload(file, archive, info);
            }
            catch { archive.Dispose(); throw; }
        }
        catch { file.Dispose(); throw; }
    }

    /// <summary>Writes the package to <paramref name="destination"/> and checks it against the SHA-256 in installer.json (a copy damaged on the way is found here, before anything is touched).</summary>
    public async Task ExtractPackageAsync(string destination, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await using (var source = archive.GetEntry(PackageEntry)!.Open())
        await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)) await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        var actual = await Sha256Async(destination, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, Info.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException("The package inside the installer does not match its checksum (the installer file is damaged: copy it again).");
        }
    }

    /// <summary>Writes the files of the updater into <paramref name="destination"/>; a name that would leave the folder is refused.</summary>
    public async Task ExtractUpdaterAsync(string destination, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(UpdaterPrefix, StringComparison.Ordinal) && entry.Name.Length > 0))
        {
            var relative = entry.FullName[UpdaterPrefix.Length..];
            if (relative.Contains('\\') || relative.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':'))) throw new InvalidDataException($"The installer holds a file with an unsafe name: {entry.FullName}.");
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"The installer holds a file with an unsafe name: {entry.FullName}.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    // Written and read by hand (no reflection): the launcher is trimmed.
    private static string InfoJson(InstallerInfo info) =>
        $$"""
        {
          "version": "{{JsonEncodedText.Encode(info.Version)}}",
          "packageSha256": "{{JsonEncodedText.Encode(info.PackageSha256)}}",
          "builtUtc": "{{JsonEncodedText.Encode(info.BuiltUtc.ToString("O"))}}"
        }
        """;

    private static InstallerInfo ReadInfo(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            return new InstallerInfo(root.GetProperty("version").GetString() ?? "", root.GetProperty("packageSha256").GetString() ?? "", root.GetProperty("builtUtc").GetDateTime());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw new InvalidDataException($"{InfoEntry} cannot be read."); }
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>A read-only window on a part of a seekable stream (the zip does not start at the beginning of the file).</summary>
    private sealed class SliceStream(Stream inner, long start, long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var left = length - position;
            if (left <= 0) return 0;
            inner.Seek(start + position, SeekOrigin.Begin);
            var read = inner.Read(buffer, offset, (int)Math.Min(count, left));
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            _ => length + offset
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
