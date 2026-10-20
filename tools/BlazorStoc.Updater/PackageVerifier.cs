using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>
/// The outcome of checking a package. Ok: the structure, the signature and every hash are right. SignerFingerprint: the key that signed it. Trusted: the signer is in the list
/// the caller trusts (null when no list was given). A package is installed only when Ok and Trusted are both true; a valid package of an unknown signer is the case in
/// which the person who installs is asked to compare the fingerprint (docs/PROPUNERE_ACTUALIZARE.md, "Cheile de incredere").
/// </summary>
public sealed record PackageVerification(bool Ok, string? SignerFingerprint, bool? Trusted, PackageManifest? Manifest, IReadOnlyList<string> Problems)
{
    public bool Installable => Ok && Trusted != false;
}

/// <summary>
/// Checks a package before anything of it is extracted or run: only the expected entries and safe names; the signature over the exact bytes of manifest.json; the manifest
/// itself; every file of the package listed in it (and only those) with the size and the SHA-256 it says. Nothing is written to disk. Reads are bounded, so a hostile
/// package (a zip bomb, an endless entry) is refused instead of filling the disk or the memory.
/// </summary>
public static class PackageVerifier
{
    private const int SmallEntryLimit = 1 << 20;            // manifest, signature, public key
    private const long TotalLimit = 4L * 1024 * 1024 * 1024; // the whole application, uncompressed

    public static async Task<PackageVerification> VerifyAsync(string packagePath, IReadOnlyCollection<string>? trustedFingerprints = null, CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();
        PackageVerification Fail() => new(false, null, null, null, problems);
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(packagePath); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"The file is not a readable package ({exception.GetType().Name}).");
            return Fail();
        }
        using (zip)
        {
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') && entry.Length == 0) continue; // a folder entry
                var name = entry.FullName;
                var allowed = name is PackageLayout.ManifestEntry or PackageLayout.SignatureEntry or PackageLayout.SignerEntry
                    || (name.StartsWith(PackageLayout.AppPrefix, StringComparison.Ordinal) && PackageLayout.IsSafeRelativePath(name[PackageLayout.AppPrefix.Length..]));
                if (!allowed) { problems.Add($"The entry \"{name}\" is not allowed in a package."); continue; }
                if (!entries.TryAdd(name, entry)) problems.Add($"The entry \"{name}\" appears twice.");
            }
            if (problems.Count > 0) return Fail();
            foreach (var required in new[] { PackageLayout.ManifestEntry, PackageLayout.SignatureEntry, PackageLayout.SignerEntry })
                if (!entries.ContainsKey(required)) problems.Add($"The package has no {required}.");
            if (problems.Count > 0) return Fail();

            var manifestBytes = await ReadSmallAsync(entries[PackageLayout.ManifestEntry], cancellationToken).ConfigureAwait(false);
            var signatureText = await ReadSmallAsync(entries[PackageLayout.SignatureEntry], cancellationToken).ConfigureAwait(false);
            var signerText = await ReadSmallAsync(entries[PackageLayout.SignerEntry], cancellationToken).ConfigureAwait(false);
            if (manifestBytes is null || signatureText is null || signerText is null) { problems.Add("The manifest, the signature or the public key is too large."); return Fail(); }

            string? fingerprint = null;
            try
            {
                var signer = Convert.FromBase64String(Encoding.ASCII.GetString(signerText).Trim());
                var signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureText).Trim());
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(signer, out _);
                fingerprint = SigningKeyStore.Fingerprint(signer);
                if (!key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256)) { problems.Add("The signature does not match the manifest."); return new(false, fingerprint, null, null, problems); }
            }
            catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
            {
                problems.Add($"The signature or the public key cannot be read ({exception.GetType().Name}).");
                return Fail();
            }

            PackageManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, PackageManifest.Json); }
            catch (JsonException) { manifest = null; }
            if (manifest is null) { problems.Add("The manifest cannot be read."); return new(false, fingerprint, null, null, problems); }
            CheckManifest(manifest, problems);
            if (problems.Count > 0) return new(false, fingerprint, null, manifest, problems);

            var listed = manifest.Files.ToDictionary(file => PackageLayout.AppPrefix + file.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var name in entries.Keys.Where(name => name.StartsWith(PackageLayout.AppPrefix, StringComparison.Ordinal) && !listed.ContainsKey(name)))
                problems.Add($"\"{name}\" is in the package but not in the manifest.");
            foreach (var file in manifest.Files)
            {
                if (!entries.TryGetValue(PackageLayout.AppPrefix + file.Path, out var entry)) { problems.Add($"\"{file.Path}\" is in the manifest but not in the package."); continue; }
                var problem = await CheckFileAsync(entry, file, cancellationToken).ConfigureAwait(false);
                if (problem is not null) problems.Add(problem);
            }

            bool? trusted = trustedFingerprints is null ? null : trustedFingerprints.Contains(fingerprint, StringComparer.OrdinalIgnoreCase);
            return new(problems.Count == 0, fingerprint, trusted, manifest, problems);
        }
    }

    private static void CheckManifest(PackageManifest manifest, List<string> problems)
    {
        if (manifest.FormatVersion != PackageManifest.CurrentFormat) problems.Add($"The format {manifest.FormatVersion} of the package is not known (this tool reads {PackageManifest.CurrentFormat}).");
        if (manifest.Product != PackageManifest.ProductName) problems.Add($"The package is for \"{manifest.Product}\", not for {PackageManifest.ProductName}.");
        if (!Version.TryParse(manifest.Version, out _)) problems.Add($"The version \"{manifest.Version}\" is not major.minor.patch.");
        if (manifest.MinFromVersion is not null && !Version.TryParse(manifest.MinFromVersion, out _)) problems.Add($"The minimum version \"{manifest.MinFromVersion}\" is not major.minor.patch.");
        if (manifest.Files.Count == 0) problems.Add("The manifest lists no files.");
        if (manifest.Files.Any(file => !PackageLayout.IsSafeRelativePath(file.Path))) problems.Add("The manifest has a path that cannot be extracted safely.");
        if (manifest.Files.Any(file => PackageLayout.LooksLikeSecret(file.Path))) problems.Add("The manifest lists a private file (passwords, keys, data or logs).");
        if (manifest.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count) problems.Add("The manifest lists a file twice.");
        if (manifest.Files.Any(file => file.Size < 0 || file.Sha256.Length != 64)) problems.Add("The manifest has a file without a valid size or SHA-256.");
        if (manifest.Files.Sum(file => file.Size) > TotalLimit) problems.Add("The application is larger than the limit of a package.");
        if (!manifest.Files.Any(file => file.Path == "BlazorStoc.dll")) problems.Add("The package does not contain BlazorStoc.dll.");
        if (manifest.Migrations.Count == 0 || manifest.SchemaVersion != manifest.Migrations.Max(migration => migration.Version)) problems.Add("The schema version does not match the migrations of the manifest.");
    }

    private static async Task<string?> CheckFileAsync(ZipArchiveEntry entry, PackageFile file, CancellationToken cancellationToken)
    {
        if (entry.Length != file.Size) return $"\"{file.Path}\" has {entry.Length} bytes, the manifest says {file.Size}.";
        await using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > file.Size) return $"\"{file.Path}\" is larger than the manifest says.";
            hash.AppendData(buffer, 0, read);
        }
        if (total != file.Size) return $"\"{file.Path}\" is shorter than the manifest says.";
        return string.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Sha256, StringComparison.OrdinalIgnoreCase) ? null : $"\"{file.Path}\" does not match its SHA-256.";
    }

    private static async Task<byte[]?> ReadSmallAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > SmallEntryLimit) return null;
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > SmallEntryLimit) return null;
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}
