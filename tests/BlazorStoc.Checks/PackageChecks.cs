using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlazorStoc.Updater;

namespace BlazorStoc.Checks;

// Stage 2 of the application update (docs/PROPUNERE_ACTUALIZARE.md): the signing key, the package (manifest, signature, files) and its verification. Nothing here
// reaches a database or the real key of the machine: the keys are made in a temporary folder and the "application" is a handful of files.
public static class PackageChecks
{
    private static readonly string Describe = JsonSerializer.Serialize(new
    {
        version = "0.0.7",
        schemaVersion = 2,
        migrations = new[]
        {
            new { version = 1, description = "first", checksum = new string('1', 64) },
            new { version = 2, description = "second", checksum = new string('2', 64) }
        }
    });

    // The changelog of the packages (Assets/Data/changelog.txt): its format, the versions an update went through and the notes of a version.
    private static void ChangelogChecks(Action<bool, string> check)
    {
        var sample = BlazorStoc.Services.Update.UpdateChangelog.Parse("# comentariu\n## 0.3.0 | 03.01.2026\n- trei a\n- trei b\n## 0.2.0 | 02.01.2026\n- doi\n## 0.1.0\n- unu\n");
        check(sample.Count == 3 && sample[0].Version == "0.3.0" && sample[0].Date == "03.01.2026" && sample[0].Items.Count == 2 && sample[2].Date == "",
            "Changelog: sections are read with version, date and one line per change");
        string Versions(string? from, string? to) => string.Join(",", BlazorStoc.Services.Update.UpdateChangelog.Between(sample, from, to).Select(entry => entry.Version));
        check(Versions("0.1.0", "0.3.0") == "0.3.0,0.2.0" && Versions(null, "0.3.0") == "0.3.0" && Versions("0.3.0", "0.1.0") == "0.1.0" && Versions("0.1.0", "9.9.9") == "" && Versions("0.1.0", "?") == "",
            "Changelog: an update shows the versions after its origin up to its target (newest first); a rollback or an unknown version shows only its own");
        check(BlazorStoc.Services.Update.UpdateChangelog.NotesFor(sample, "0.2.0") == "- doi" && BlazorStoc.Services.Update.UpdateChangelog.NotesFor(sample, "0.9.0") is null,
            "Changelog: the notes of a version are its lines; a version without a section has none");
        var current = BlazorStoc.Services.Update.UpdateChangelog.Versions;
        check(current.Count > 0 && current.All(entry => entry.Items.Count > 0) && BlazorStoc.Services.Update.UpdateChangelog.NotesFor(BlazorStoc.Services.AppVersion.Current) is not null,
            "Changelog: the embedded changelog has a section with changes for the version of the application");
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        ChangelogChecks(check);
        var folder = Path.Combine(Path.GetTempPath(), "blazorstoc-pkg-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var app = Path.Combine(folder, "app");
            Directory.CreateDirectory(Path.Combine(app, "wwwroot"));
            File.WriteAllText(Path.Combine(app, "BlazorStoc.dll"), "dll of the application");
            File.WriteAllText(Path.Combine(app, "wwwroot", "app.css"), "body { color: #192d31 }");
            File.WriteAllText(Path.Combine(app, "appsettings.json"), "{}");

            var keys = new SigningKeyStore(Path.Combine(folder, "key-a"));
            var other = new SigningKeyStore(Path.Combine(folder, "key-b"));
            var fingerprint = keys.Generate();
            var otherFingerprint = other.Generate();
            check(Regex(fingerprint) && fingerprint != otherFingerprint && keys.Exists && SigningKeyStore.Fingerprint(keys.LoadPublic()) == fingerprint,
                "Package: a signing key has a 64-hex fingerprint of its public key and two keys differ");
            check(Throws(() => keys.Generate()) && Throws(() => new SigningKeyStore(Path.Combine(folder, "nowhere")).LoadPrivate()) && SigningKeyStore.Fingerprint(keys.LoadPublic()) == fingerprint,
                "Package: an existing key is not replaced without being asked, and a missing key is an error");
            check(!File.ReadAllText(Path.Combine(keys.Folder, "signing-key.private")).Contains("BEGIN", StringComparison.Ordinal) && new FileInfo(Path.Combine(keys.Folder, "signing-key.private")).Length > 100,
                "Package: the private key on disk is protected (not readable PKCS#8 text)");

            var package = Path.Combine(folder, "BlazorStoc-0.0.7.bsu");
            var built = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
            var manifest = await PackageBuilder.PackAsync(app, Describe, package, keys, "win-x64", "net9.0", "0.0.5", built);
            check(manifest.Version == "0.0.7" && manifest.SchemaVersion == 2 && manifest.MinFromVersion == "0.0.5" && manifest.Migrations.Count == 2
                && manifest.Files.Select(file => file.Path).SequenceEqual(["BlazorStoc.dll", "appsettings.json", "wwwroot/app.css"]) && !File.Exists(package + ".partial"),
                "Package: the manifest has the version, the schema, the migrations and the files in a fixed order");

            var good = await PackageVerifier.VerifyAsync(package, [fingerprint]);
            check(good.Ok && good.Trusted == true && good.Installable && good.SignerFingerprint == fingerprint && good.Manifest?.Files.Count == 3 && good.Problems.Count == 0,
                "Package: a package checked against its own signer is valid, trusted and installable");
            var unknown = await PackageVerifier.VerifyAsync(package, [otherFingerprint]);
            check(unknown.Ok && unknown.Trusted == false && !unknown.Installable && unknown.SignerFingerprint == fingerprint,
                "Package: a valid package of a signer that is not in the trusted list is not installable");
            check((await PackageVerifier.VerifyAsync(package)).Trusted is null, "Package: without a list of trusted signers the answer says nothing about trust");

            // Tampering. Each one starts from a copy of the good package.
            var flipped = Mutate(package, folder, zip => Replace(zip, "app/wwwroot/app.css", "body { color: #000000 }"));   // the same length, other content
            check(await Rejects(flipped, "SHA-256"), "Package: a file changed inside the package is refused (hash)");
            var longer = Mutate(package, folder, zip => Replace(zip, "app/appsettings.json", "{ \"a\": 1 }"));
            check(await Rejects(longer, "bytes"), "Package: a file of another size is refused");
            var forgedManifest = Mutate(package, folder, zip => Replace(zip, PackageLayout.ManifestEntry, Encoding.UTF8.GetString(ReadEntry(zip, PackageLayout.ManifestEntry)).Replace("0.0.7", "9.9.9")));
            check(await Rejects(forgedManifest, "signature"), "Package: a manifest changed after signing is refused (signature)");
            var otherSigner = Mutate(package, folder, zip => Replace(zip, PackageLayout.SignerEntry, Convert.ToBase64String(other.LoadPublic())));
            check(await Rejects(otherSigner, "signature"), "Package: the public key of another signer cannot be put in place of the real one");
            var extra = Mutate(package, folder, zip => Replace(zip, "app/evil.dll", "malware"));
            check(await Rejects(extra, "not in the manifest"), "Package: a file that the manifest does not list is refused");
            var missing = Mutate(package, folder, zip => zip.GetEntry("app/appsettings.json")!.Delete());
            check(await Rejects(missing, "not in the package"), "Package: a listed file that is missing is refused");
            var traversal = Mutate(package, folder, zip => Replace(zip, "app/../outside.dll", "x"));
            var other1 = Mutate(package, folder, zip => Replace(zip, "readme.txt", "x"));
            var drive = Mutate(package, folder, zip => Replace(zip, "app/c:/windows/x.dll", "x"));
            var backslash = Mutate(package, folder, zip => Replace(zip, "app/a\\b.dll", "x"));
            check(await Rejects(traversal, "not allowed") && await Rejects(other1, "not allowed") && await Rejects(drive, "not allowed") && await Rejects(backslash, "not allowed"),
                "Package: entries that climb out (\"..\"), carry a drive or a backslash, or sit outside app/ are refused before anything is read");
            var notZip = Path.Combine(folder, "not-a-package.bsu");
            File.WriteAllText(notZip, "this is not a zip");
            check(await Rejects(notZip, "not a readable package"), "Package: a file that is not a package is refused");

            // The packer refuses what a release must never carry.
            var secretApp = Path.Combine(folder, "secret-app");
            Directory.CreateDirectory(Path.Combine(secretApp, "local-secrets"));
            File.WriteAllText(Path.Combine(secretApp, "BlazorStoc.dll"), "x");
            File.WriteAllText(Path.Combine(secretApp, "local-secrets", "application-connection.private.json"), "{}");
            check(await ThrowsAsync(() => PackageBuilder.PackAsync(secretApp, Describe, Path.Combine(folder, "secret.bsu"), keys, "win-x64", "net9.0", null, built)) && !File.Exists(Path.Combine(folder, "secret.bsu")),
                "Package: a publish that contains local-secrets is refused and nothing is written");
            File.Delete(Path.Combine(secretApp, "local-secrets", "application-connection.private.json"));
            Directory.Delete(Path.Combine(secretApp, "local-secrets"));
            File.WriteAllText(Path.Combine(secretApp, "migration-account.private.json"), "{}");
            check(await ThrowsAsync(() => PackageBuilder.PackAsync(secretApp, Describe, Path.Combine(folder, "secret.bsu"), keys, "win-x64", "net9.0", null, built)),
                "Package: a private file of an account is refused wherever it sits in the publish");
            check(PackageLayout.LooksLikeSecret("data/anaf-configuration.json") && PackageLayout.LooksLikeSecret("keys/x.xml") && PackageLayout.LooksLikeSecret("logs/a.log") && PackageLayout.LooksLikeSecret(".env")
                && !PackageLayout.LooksLikeSecret("Assets/Data/units.tsv") && !PackageLayout.LooksLikeSecret("wwwroot/app.css"),
                "Package: the list of what a release must not carry covers data, keys, logs and .env but not the application's own Assets/Data");
            check(await ThrowsAsync(() => PackageBuilder.PackAsync(app, Describe.Replace("0.0.7", "seven"), Path.Combine(folder, "bad.bsu"), keys, "win-x64", "net9.0", null, built))
                && await ThrowsAsync(() => PackageBuilder.PackAsync(Path.Combine(folder, "empty"), Describe, Path.Combine(folder, "bad.bsu"), keys, "win-x64", "net9.0", null, built)),
                "Package: a version that is not major.minor.patch, or a folder that is not a published application, is refused");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static bool Regex(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-f]{64}$");

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException) { return true; }
    }

    private static async Task<bool> ThrowsAsync(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException) { return true; }
    }

    private static async Task<bool> Rejects(string package, string reason)
    {
        var result = await PackageVerifier.VerifyAsync(package, null);
        return !result.Ok && !result.Installable && result.Problems.Any(problem => problem.Contains(reason, StringComparison.OrdinalIgnoreCase));
    }

    private static string Mutate(string source, string folder, Action<ZipArchive> change)
    {
        var copy = Path.Combine(folder, "m-" + Guid.NewGuid().ToString("N") + ".bsu");
        File.Copy(source, copy);
        using (var zip = ZipFile.Open(copy, ZipArchiveMode.Update)) change(zip);
        return copy;
    }

    private static void Replace(ZipArchive zip, string name, string content)
    {
        zip.GetEntry(name)?.Delete();
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
