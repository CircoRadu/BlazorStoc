using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Updater;

/// <summary>The two things the page Setari -> Actualizare of the application may ask of the updater (docs/PROPUNERE_ACTUALIZARE.md, section 4).</summary>
public static class RequestKinds
{
    /// <summary>Look at an uploaded package and write a report (state/check-&lt;id&gt;.json): nothing is changed.</summary>
    public const string Check = "check";
    /// <summary>Install an uploaded package that was checked and confirmed by an administrator.</summary>
    public const string Apply = "apply";
}

/// <summary>
/// updates/staged/request-&lt;id&gt;.json, written by the application (the account it runs as can write only updates/staged) and read by the updater task, which runs with the rights
/// of an administrator. Because the writer has fewer rights than the reader, nothing in it is trusted: the package is a file NAME inside updates/staged (never a path), the updater
/// verifies the signature and every hash itself, the hash given here only proves that the package is the very file the administrator was shown, and a downgrade or a reinstall
/// cannot be asked for in this way.
/// </summary>
public sealed record UpdateRequestFile(string Id, string Kind, string Package, string? Sha256, string Actor, string Reason, DateTime RequestedUtc);

/// <summary>
/// state/check-&lt;id&gt;.json: what the updater found in an uploaded package, for the application to show before the administrator confirms. Blocking: null when the package can be
/// installed, else a code (invalid, untrusted, refused) and Detail says why.
/// </summary>
public sealed record PackageCheckReport(string Id, string Package, string Sha256, long SizeBytes, bool Ok, IReadOnlyList<string> Problems, string? Version, string? Running,
    int? SchemaVersion, int? RunningSchemaVersion, int? NewMigrations, DateTime? BuiltUtc, string? MinFromVersion, string? Signer, bool? Trusted, bool CanInstall, string? Blocking,
    string? Detail, int FileCount, long TotalBytes, string? Notes, DateTime CheckedUtc);

public static class BlockingCodes
{
    public const string Invalid = "invalid";
    public const string Untrusted = "untrusted";
    public const string Refused = "refused";
}

/// <summary>Runs the requests that are waiting in updates/staged, oldest first. One run of the updater task handles all of them.</summary>
public sealed partial class RequestProcessor(InstallLayout layout, UpdateEngine engine, IUpdateLog log, Func<DateTime>? utcNow = null)
{
    public const string RequestPrefix = "request-";
    private const int MaxRequestBytes = 8 * 1024;
    private static readonly TimeSpan Leftover = TimeSpan.FromDays(2);

    private DateTime Now => (utcNow ?? (() => DateTime.UtcNow))();

    public static string RequestFileName(string id) => RequestPrefix + id + ".json";
    public static string CheckFileName(string id) => "check-" + id + ".json";

    /// <summary>Why a request is not acted on, or null when it is well formed. Everything that becomes a path or a command line is checked here.</summary>
    public static string? Validate(UpdateRequestFile request)
    {
        if (!IdShape().IsMatch(request.Id ?? "")) return "the id is not 32 hexadecimal characters";
        if (request.Kind is not (RequestKinds.Check or RequestKinds.Apply)) return $"the kind \"{request.Kind}\" is not known";
        if (!PackageName().IsMatch(request.Package ?? "")) return "the package must be the name of a .bsu file in updates/staged";
        if (request.Kind == RequestKinds.Apply && !HashShape().IsMatch(request.Sha256 ?? "")) return "an apply request must carry the SHA-256 of the package the administrator was shown";
        if (string.IsNullOrWhiteSpace(request.Actor) || request.Actor.Length > 100 || request.Actor.Any(char.IsControl)) return "the actor is empty, too long or has control characters";
        if (request.Kind == RequestKinds.Apply && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500 || request.Reason.Any(char.IsControl))) return "the reason is empty, too long or has control characters";
        return null;
    }

    /// <summary>Handles every waiting request and returns how many were taken (valid or not: an invalid one is removed too).</summary>
    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        layout.EnsureFolders();
        var taken = 0;
        foreach (var file in Directory.EnumerateFiles(layout.StagedFolder, RequestPrefix + "*.json").Order(StringComparer.Ordinal).ToList())
        {
            taken++;
            try { await HandleAsync(file, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                log.Error($"The request {Path.GetFileName(file)} could not be handled: {exception.Message}");
            }
            finally { TryDelete(file); }
        }
        Cleanup();
        return taken;
    }

    private async Task HandleAsync(string file, CancellationToken cancellationToken)
    {
        var info = new FileInfo(file);
        if (info.Length > MaxRequestBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { log.Warn($"{info.Name}: not a request (too big or a link); removed."); return; }
        UpdateRequestFile? request;
        try { request = JsonSerializer.Deserialize<UpdateRequestFile>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), InstallLayout.Json); }
        catch (JsonException) { request = null; }
        if (request is null) { log.Warn($"{info.Name}: not readable; removed."); return; }
        if (Validate(request) is { } problem) { log.Warn($"{info.Name}: refused, {problem}; removed."); return; }
        var path = Path.Combine(layout.StagedFolder, request.Package);
        if (!File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)) { log.Warn($"{info.Name}: the package {request.Package} is not in updates/staged; removed."); return; }

        if (request.Kind == RequestKinds.Check) await CheckAsync(request, path, cancellationToken).ConfigureAwait(false);
        else await ApplyAsync(request, path, cancellationToken).ConfigureAwait(false);
    }

    // ---- check ---------------------------------------------------------------------------------------------------------------------------------------

    private async Task CheckAsync(UpdateRequestFile request, string path, CancellationToken cancellationToken)
    {
        log.Info($"Checking {request.Package} for {request.Actor}.");
        var sha = await HashAsync(path, cancellationToken).ConfigureAwait(false);
        var size = new FileInfo(path).Length;
        var verification = await PackageVerifier.VerifyAsync(path, new TrustedKeys(layout.TrustedKeysFile).Fingerprints(), cancellationToken).ConfigureAwait(false);
        var running = engine.RunningVersion();
        PackageCheckReport report;
        if (!verification.Ok || verification.Manifest is not { } manifest)
        {
            report = new PackageCheckReport(request.Id, request.Package, sha, size, false, verification.Problems, null, running, null, null, null, null, null,
                verification.SignerFingerprint, verification.Trusted, false, BlockingCodes.Invalid, string.Join(" ", verification.Problems), 0, 0, null, Now);
        }
        else
        {
            var installed = RunningManifest(running);
            var preflight = engine.PreflightProblem(manifest, running);
            var blocking = verification.Trusted == false ? BlockingCodes.Untrusted : preflight is not null ? BlockingCodes.Refused : null;
            var detail = blocking == BlockingCodes.Untrusted ? $"The package is signed with a key this server does not know ({SigningKeyStore.Display(verification.SignerFingerprint!)})." : preflight;
            report = new PackageCheckReport(request.Id, request.Package, sha, size, true, [], manifest.Version, running, manifest.SchemaVersion, installed?.SchemaVersion,
                installed is null ? null : manifest.Migrations.Count(migration => migration.Version > installed.SchemaVersion), manifest.BuiltUtc, manifest.MinFromVersion,
                verification.SignerFingerprint, verification.Trusted, blocking is null, blocking, detail, manifest.Files.Count, manifest.Files.Sum(item => item.Size), manifest.Notes, Now);
        }
        Directory.CreateDirectory(layout.StateFolder);
        var target = Path.Combine(layout.StateFolder, CheckFileName(request.Id));
        await File.WriteAllTextAsync(target + ".tmp", JsonSerializer.Serialize(report, InstallLayout.Json), cancellationToken).ConfigureAwait(false);
        File.Move(target + ".tmp", target, overwrite: true);
        log.Info(report.CanInstall ? $"{request.Package}: version {report.Version}, can be installed." : $"{request.Package}: cannot be installed ({report.Blocking}: {report.Detail}).");
    }

    // The manifest the running version came from (releases/<version>/release.json): what schema the database is at, to count the migrations a package would add.
    private PackageManifest? RunningManifest(string? running)
    {
        try
        {
            return running is not null && File.Exists(layout.ManifestFile(running))
                ? JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(layout.ManifestFile(running)), PackageManifest.Json) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException) { return null; }
    }

    // ---- apply ---------------------------------------------------------------------------------------------------------------------------------------

    private async Task ApplyAsync(UpdateRequestFile request, string path, CancellationToken cancellationToken)
    {
        var sha = await HashAsync(path, cancellationToken).ConfigureAwait(false);
        var install = new InstallRequest(path, request.Actor.Trim(), request.Reason.Trim(), Via: UpdateVia.Application, PackageSha256: sha);
        if (!string.Equals(sha, request.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"{request.Package} is not the file the administrator confirmed (SHA-256 {sha}, confirmed {request.Sha256}).");
            engine.Reject(install, "The package in updates/staged is not the file that was checked and confirmed (its SHA-256 changed). Upload it again.");
            return;
        }
        log.Info($"Installing {request.Package} for {install.Actor} (from the application): {install.Reason}");
        var result = await engine.InstallAsync(install, cancellationToken).ConfigureAwait(false);
        log.Info($"Result: {result.Outcome} - {result.Message}");
        if (result.Outcome == UpdateOutcome.Succeeded) KeepApplied(path, result.To, sha);
    }

    // The package of an update that worked is kept with its hash (also the source for a repair by hand); the last three stay.
    private void KeepApplied(string path, string version, string sha)
    {
        try
        {
            Directory.CreateDirectory(layout.AppliedFolder);
            File.Move(path, Path.Combine(layout.AppliedFolder, $"BlazorStoc-{version}-{sha[..12]}.bsu"), overwrite: true);
            foreach (var old in new DirectoryInfo(layout.AppliedFolder).GetFiles("*.bsu").OrderByDescending(item => item.LastWriteTimeUtc).Skip(3)) TryDelete(old.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { log.Warn($"The package could not be moved to updates/applied: {exception.Message}"); }
    }

    // ---- housekeeping --------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Uploads, requests and reports nobody came back for are removed after two days.</summary>
    private void Cleanup()
    {
        var limit = Now - Leftover;
        foreach (var (folder, pattern) in new[] { (layout.StagedFolder, "*.bsu"), (layout.StagedFolder, "*.partial"), (layout.StagedFolder, "request-*.json"), (layout.StateFolder, "check-*.json") })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in new DirectoryInfo(folder).GetFiles(pattern).Where(item => item.LastWriteTimeUtc < limit)) TryDelete(file.FullName);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex IdShape();
    [GeneratedRegex("^[0-9a-fA-F]{64}$")] private static partial Regex HashShape();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.bsu$")] private static partial Regex PackageName();
}
