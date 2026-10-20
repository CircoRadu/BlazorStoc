using System.Text.Json;

namespace BlazorStoc.Updater;

public sealed record TrustedKey(string Fingerprint, DateTime AddedUtc, string AddedBy, string Note);

/// <summary>
/// The signers this server trusts (trusted-keys.json in the updater folder, kept for administrators, see <see cref="Acl"/>): the fingerprints of public keys. A package is
/// installed only when its signer is here. A key is added by an administrator at the installer, who compares the fingerprint with the one printed on the build machine
/// (docs/PROPUNERE_ACTUALIZARE.md, section 7); the web application has no way to add one.
/// </summary>
public sealed class TrustedKeys(string file)
{
    private sealed record Document(List<TrustedKey> Keys);

    public IReadOnlyList<TrustedKey> Load()
    {
        if (!File.Exists(file)) return [];
        try { return JsonSerializer.Deserialize<Document>(File.ReadAllText(file), InstallLayout.Json)?.Keys ?? []; }
        catch (JsonException) { throw new InvalidOperationException($"{file} cannot be read; no signer is trusted until it is repaired (or removed, which starts a new list)."); }
    }

    public IReadOnlyList<string> Fingerprints() => [.. Load().Select(key => key.Fingerprint)];

    public bool Contains(string fingerprint) => Load().Any(key => string.Equals(key.Fingerprint, Normalise(fingerprint), StringComparison.Ordinal));

    /// <summary>Adds a fingerprint (64 hex characters; spaces are ignored). Adding one that is already there changes nothing.</summary>
    public TrustedKey Add(string fingerprint, string addedBy, string note, DateTime nowUtc)
    {
        var normal = Normalise(fingerprint);
        if (normal.Length != 64 || !normal.All(Uri.IsHexDigit)) throw new ArgumentException("A fingerprint is 64 hexadecimal characters (spaces are ignored).");
        var keys = Load().ToList();
        if (keys.FirstOrDefault(key => key.Fingerprint == normal) is { } existing) return existing;
        var added = new TrustedKey(normal, nowUtc, addedBy, note);
        keys.Add(added);
        Save(keys);
        return added;
    }

    public bool Remove(string fingerprint)
    {
        var normal = Normalise(fingerprint);
        var keys = Load().ToList();
        if (keys.RemoveAll(key => key.Fingerprint == normal) == 0) return false;
        Save(keys);
        return true;
    }

    public static string Normalise(string fingerprint) => new string(fingerprint.Where(character => !char.IsWhiteSpace(character) && character != ':').ToArray()).ToLowerInvariant();

    private void Save(List<TrustedKey> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(keys), InstallLayout.Json));
        File.Move(temporary, file, overwrite: true);
    }
}
