using System.Security.Cryptography;
using System.Text;

namespace BlazorStoc.Updater;

/// <summary>
/// The signing key of the machine that builds a release (docs/PROPUNERE_ACTUALIZARE.md, section 7): ECDSA P-256 / SHA-256. The private key stays on the machine that
/// generated it, protected with DPAPI for the Windows user, outside the repository; the public key goes into every package it signs (signer.pub) and its fingerprint
/// is what the server trusts. Any machine that builds releases can have its own key; the server learns the fingerprint once (see PackageVerifier).
/// </summary>
public sealed class SigningKeyStore(string folder)
{
    private const string PrivateFile = "signing-key.private";
    private const string PublicFile = "signing-key.public";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BlazorStoc release signing key v1");

    public string Folder { get; } = folder;

    /// <summary>The folder of the current user: %LOCALAPPDATA%\BlazorStoc-release (not in the repository, not shared with other users of the machine).</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlazorStoc-release");

    public bool Exists => File.Exists(Path.Combine(Folder, PrivateFile)) && File.Exists(Path.Combine(Folder, PublicFile));

    /// <summary>Makes a new key. An existing key is never replaced without <paramref name="replace"/> (every server that trusts it would refuse the next release).</summary>
    public string Generate(bool replace = false)
    {
        if (Exists && !replace) throw new InvalidOperationException($"A signing key already exists in {Folder}; keep it (the servers trust its fingerprint) or ask for a new one explicitly.");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Folder);
        var secret = key.ExportPkcs8PrivateKey();
        try
        {
            File.WriteAllBytes(Path.Combine(Folder, PrivateFile), ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser));
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
        var publicKey = key.ExportSubjectPublicKeyInfo();
        File.WriteAllText(Path.Combine(Folder, PublicFile), Convert.ToBase64String(publicKey));
        return Fingerprint(publicKey);
    }

    /// <summary>The private key (opened for signing); throws when there is none or when it belongs to another Windows user.</summary>
    public ECDsa LoadPrivate()
    {
        var path = Path.Combine(Folder, PrivateFile);
        if (!File.Exists(path)) throw new FileNotFoundException($"There is no signing key in {Folder}; make one with: BlazorStoc.Updater keygen", path);
        var secret = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(secret, out _);
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public byte[] LoadPublic() => Convert.FromBase64String(File.ReadAllText(Path.Combine(Folder, PublicFile)).Trim());

    /// <summary>SHA-256 of the public key (SubjectPublicKeyInfo), lower case hex: what a server trusts and what the person who installs compares with the release output.</summary>
    public static string Fingerprint(byte[] publicKey) => Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();

    /// <summary>The fingerprint in groups of eight characters for reading aloud or comparing by eye.</summary>
    public static string Display(string fingerprint) =>
        string.Join(' ', Enumerable.Range(0, (fingerprint.Length + 7) / 8).Select(index => fingerprint.Substring(index * 8, Math.Min(8, fingerprint.Length - index * 8))));
}
