using System.Text.Json;

namespace BlazorStoc.Updater;

/// <summary>A file of the application inside a package: its path under "app/" (forward slashes), SHA-256 (lower case hex) and size in bytes.</summary>
public sealed record PackageFile(string Path, string Sha256, long Size);

/// <summary>A migration the release brings (taken from the application itself, "--describe-release"): version, description and checksum of its file.</summary>
public sealed record PackageMigration(int Version, string Description, string Checksum);

/// <summary>
/// What a package says about itself. It is signed as the exact bytes of manifest.json, so nothing in the package is trusted before the signature and the hashes
/// of this file are checked. MinFromVersion: the oldest installed version this package can be installed over (null = any). Platform and Runtime: what the
/// server must have (the files are published for it).
/// </summary>
public sealed record PackageManifest(int FormatVersion, string Product, string Version, string? MinFromVersion, string Platform, string Runtime, DateTime BuiltUtc,
    int SchemaVersion, IReadOnlyList<PackageMigration> Migrations, IReadOnlyList<PackageFile> Files, string? Notes = null)
{
    public const int CurrentFormat = 1;
    public const string ProductName = "BlazorStoc";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

/// <summary>The names inside a package: the three files of the package itself and the application under "app/".</summary>
public static class PackageLayout
{
    public const string ManifestEntry = "manifest.json";
    public const string SignatureEntry = "manifest.sig";
    public const string SignerEntry = "signer.pub";
    public const string AppPrefix = "app/";
    public const string Extension = ".bsu";

    /// <summary>
    /// A path that is safe to extract: relative, forward slashes only, no empty, "." or ".." segment, no drive or stream marker (":"), no control character,
    /// no trailing dot or space (Windows drops them, so two names would become one).
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':')) return false;
        if (path.Any(char.IsControl)) return false;
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')) return false;
        }
        return true;
    }

    /// <summary>The files a release must never carry: private files of the accounts, keys, the data and logs of the machine it was built on.</summary>
    public static bool LooksLikeSecret(string path)
    {
        var lower = path.ToLowerInvariant();
        var first = lower.Split('/')[0];
        return first is "local-secrets" or "data" or "keys" or "keys-test" or "logs" or "database"
            || lower.EndsWith(".private.json", StringComparison.Ordinal) || lower.EndsWith(".private.cnf", StringComparison.Ordinal)
            || lower.EndsWith(".pfx", StringComparison.Ordinal) || lower.EndsWith(".snk", StringComparison.Ordinal)
            || System.IO.Path.GetFileName(lower) is ".env" || System.IO.Path.GetFileName(lower).StartsWith(".env.", StringComparison.Ordinal);
    }
}
