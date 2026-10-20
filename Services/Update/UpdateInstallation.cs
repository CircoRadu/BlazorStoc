using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Services.Update;

/// <summary>The kinds of request the application leaves for the updater (tools/BlazorStoc.Updater/UpdateRequests.cs has the same two).</summary>
public static class UpdateRequestKinds
{
    public const string Check = "check";
    public const string Apply = "apply";
}

/// <summary>request-&lt;id&gt;.json in updates\staged: what the application asks of the updater (the updater trusts nothing in it, see UpdateRequests.cs there).</summary>
public sealed record UpdateRequestDocument(string Id, string Kind, string Package, string? Sha256, string Actor, string Reason, DateTime RequestedUtc);

/// <summary>check-&lt;id&gt;.json in state: what the updater found in an uploaded package. Blocking: null = can be installed, else invalid / untrusted / refused; Detail says why.</summary>
public sealed record PackageCheckDocument(string Id, string Package, string Sha256, long SizeBytes, bool Ok, IReadOnlyList<string> Problems, string? Version, string? Running,
    int? SchemaVersion, int? RunningSchemaVersion, int? NewMigrations, DateTime? BuiltUtc, string? MinFromVersion, string? Signer, bool? Trusted, bool CanInstall, string? Blocking,
    string? Detail, int FileCount, long TotalBytes, string? Notes, DateTime CheckedUtc);

/// <summary>update-status.json in state: the update that is running or the last one that ran. Phase "done" = finished (Outcome says how).</summary>
public sealed record UpdateStatusDocument(string Phase, string? From, string To, string Actor, string Reason, DateTime StartedUtc, DateTime UpdatedUtc, string? Outcome, string Message, string? Backup);

/// <summary>A line of update-history.jsonl in state. Via: "aplicatie" or "installer". Kind: "install" or "rollback".</summary>
public sealed record UpdateHistoryDocument(DateTime AtUtc, string? From, string To, string Outcome, string Actor, string Reason, string? Signer, string? Backup, double Seconds, string Message,
    string? Package = null, string Via = "installer", string Kind = "install");

/// <summary>
/// The installation the application runs from (the folder the supervisor was started with: App:InstallRoot, set by tools/supervisor.ps1 -InstallRoot). Without it the application runs
/// from a development checkout and cannot update itself. Everything here is READ-ONLY except updates\staged: the account the application runs as may write nothing else of the
/// installation (tools/BlazorStoc.Updater/Acl.cs).
/// </summary>
public sealed partial class UpdateInstallation(string? root)
{
    public const string Setting = "App:InstallRoot";

    public UpdateInstallation(IConfiguration configuration) : this(configuration[Setting]) { }

    public string? Root { get; } = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);

    /// <summary>True when the folder is an installation made by the updater (it has the releases folder).</summary>
    public bool IsManaged => Root is not null && Directory.Exists(Path.Combine(Root, "releases"));

    public string StagedFolder => Path.Combine(Require(), "updates", "staged");
    public string StateFolder => Path.Combine(Require(), "state");
    public string StatusFile => Path.Combine(StateFolder, "update-status.json");
    public string HistoryFile => Path.Combine(StateFolder, "update-history.jsonl");

    /// <summary>The scheduled task that runs the updater for this installation; the same rule as UpdaterTask.NameFor in the updater (a check compares them).</summary>
    public string TaskName => TaskNameFor(Require());

    public static string TaskNameFor(string installRoot)
    {
        var normalised = Path.GetFullPath(installRoot).TrimEnd('\\', '/').ToLowerInvariant();
        return "BlazorStocUpdater-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..8].ToLowerInvariant();
    }

    public string? Current => ReadPointer("current.txt");
    public string? Previous => ReadPointer("previous.txt");

    /// <summary>The versions on disk (releases\&lt;version&gt;\app\BlazorStoc.dll), newest first.</summary>
    public IReadOnlyList<string> InstalledVersions()
    {
        var releases = Path.Combine(Require(), "releases");
        if (!Directory.Exists(releases)) return [];
        return [.. Directory.EnumerateDirectories(releases).Select(Path.GetFileName).OfType<string>()
            .Where(name => VersionShape().IsMatch(name) && File.Exists(Path.Combine(releases, name, "app", "BlazorStoc.dll")))
            .OrderByDescending(name => Version.Parse(name))];
    }

    public UpdateStatusDocument? Status() => ReadJson<UpdateStatusDocument>(StatusFile);

    /// <summary>The last entries of the history of updates, newest first.</summary>
    public IReadOnlyList<UpdateHistoryDocument> History(int take = 20)
    {
        var all = AllHistory();
        return [.. all.OrderByDescending(entry => entry.AtUtc).Take(take)];
    }

    /// <summary>Every entry of the history, oldest first (lines that cannot be read are skipped).</summary>
    public IReadOnlyList<UpdateHistoryDocument> AllHistory()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return [];
            using var stream = new FileStream(HistoryFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var entries = new List<UpdateHistoryDocument>();
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { if (JsonSerializer.Deserialize<UpdateHistoryDocument>(line, UpdateJson.Options) is { } entry) entries.Add(entry); }
                catch (JsonException) { }
            }
            return [.. entries.OrderBy(entry => entry.AtUtc)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    public PackageCheckDocument? Check(string id) => IdShape().IsMatch(id) ? ReadJson<PackageCheckDocument>(Path.Combine(StateFolder, "check-" + id + ".json")) : null;

    public string RequestPath(string id) => Path.Combine(StagedFolder, "request-" + id + ".json");

    private string Require() => Root ?? throw new InvalidOperationException("Aplicația nu rulează dintr-o instalare făcută de Updater.");

    private string? ReadPointer(string name)
    {
        try
        {
            var file = Path.Combine(Require(), name);
            if (!File.Exists(file)) return null;
            var text = File.ReadAllText(file).Trim();
            return VersionShape().IsMatch(text) ? text : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static T? ReadJson<T>(string file) where T : class
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, UpdateJson.Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")] private static partial Regex VersionShape();
    [GeneratedRegex("^[0-9a-f]{32}$")] private static partial Regex IdShape();
}

public static class UpdateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
