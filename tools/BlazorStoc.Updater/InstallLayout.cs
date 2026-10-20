using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorStoc.Updater;

/// <summary>
/// The folders of an installation on the server (docs/PROPUNERE_ACTUALIZARE.md, section 3). The code of every installed version is in releases/&lt;version&gt;/app and is never
/// changed after it was put there; which one runs is decided by one line in current.txt, that the supervisor reads at every start. The data (database, files, keys) are NOT here:
/// they live in the data root of the application, so a new version never leaves them behind.
/// <code>
/// releases/&lt;version&gt;/app/...   the application as published       current.txt / previous.txt   the version that runs / the one before it
/// releases/&lt;version&gt;/release.json   the manifest of the package it came from
/// updates/staged/                packages uploaded from the application    state/   lock, status of the update that is running, history, check reports
/// updates/applied/               the packages of the updates that worked (the last three)
/// updater/                       the updater, settings.json, trusted-keys.json (administrators only)
/// scripts/                       supervisor.ps1 and data-root.ps1: run by the ordinary account the application runs as, so readable by Users, changeable by administrators only
/// private/                       migration-account.private.json (administrators only; the web application never reads it)
/// logs/                          updater-yyyyMMdd.log, supervisor logs
/// </code>
/// </summary>
public sealed partial class InstallLayout(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string ReleasesFolder => Path.Combine(Root, "releases");
    public string StagedFolder => Path.Combine(Root, "updates", "staged");
    public string AppliedFolder => Path.Combine(Root, "updates", "applied");
    public string StateFolder => Path.Combine(Root, "state");
    public string LogsFolder => Path.Combine(Root, "logs");
    public string UpdaterFolder => Path.Combine(Root, "updater");
    public string ScriptsFolder => Path.Combine(Root, "scripts");
    public string PrivateFolder => Path.Combine(Root, "private");
    public string CurrentFile => Path.Combine(Root, "current.txt");
    public string PreviousFile => Path.Combine(Root, "previous.txt");
    public string LockFile => Path.Combine(StateFolder, "update.lock");
    public string StatusFile => Path.Combine(StateFolder, "update-status.json");
    public string HistoryFile => Path.Combine(StateFolder, "update-history.jsonl");
    public string TrustedKeysFile => Path.Combine(UpdaterFolder, "trusted-keys.json");
    public string SettingsFile => Path.Combine(UpdaterFolder, "settings.json");
    public string MigrationAccountFile => Path.Combine(PrivateFolder, "migration-account.private.json");

    public string ReleaseFolder(string version) => Path.Combine(ReleasesFolder, Checked(version));
    public string AppFolder(string version) => Path.Combine(ReleaseFolder(version), "app");
    public string ManifestFile(string version) => Path.Combine(ReleaseFolder(version), "release.json");

    /// <summary>The version in current.txt, or null when nothing is installed (or the file names a version that is not on disk any more: never trusted).</summary>
    public string? Current => ReadPointer(CurrentFile);
    public string? Previous => ReadPointer(PreviousFile);

    /// <summary>Only a version that looks like major.minor.patch becomes a folder name: nothing from a package or a file reaches the path unchecked.</summary>
    public static string Checked(string version) =>
        VersionShape().IsMatch(version) ? version : throw new ArgumentException($"\"{version}\" is not a version (major.minor.patch).");

    public bool HasRelease(string version) => VersionShape().IsMatch(version) && File.Exists(Path.Combine(AppFolder(version), "BlazorStoc.dll"));

    public void EnsureFolders()
    {
        foreach (var folder in new[] { ReleasesFolder, StagedFolder, StateFolder, LogsFolder, UpdaterFolder, ScriptsFolder, PrivateFolder }) Directory.CreateDirectory(folder);
    }

    /// <summary>Switches the pointer: written beside the file and moved over it, so a reader (the supervisor) never sees half a line and a crash leaves the old or the new one.</summary>
    public void SetCurrent(string version) => WritePointer(CurrentFile, Checked(version));

    public void SetPrevious(string? version)
    {
        if (version is null) { if (File.Exists(PreviousFile)) File.Delete(PreviousFile); }
        else WritePointer(PreviousFile, Checked(version));
    }

    /// <summary>The installed versions on disk (folders that hold BlazorStoc.dll), newest first.</summary>
    public IReadOnlyList<string> InstalledVersions() =>
        !Directory.Exists(ReleasesFolder) ? [] :
        [.. Directory.EnumerateDirectories(ReleasesFolder).Select(Path.GetFileName).Where(name => name is not null && HasRelease(name!))
            .Select(name => name!).OrderByDescending(name => Version.Parse(name))];

    private static string? ReadPointer(string file)
    {
        if (!File.Exists(file)) return null;
        var text = File.ReadAllText(file).Trim();
        return VersionShape().IsMatch(text) ? text : null;
    }

    private static void WritePointer(string file, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, version + "\r\n", new UTF8Encoding(false));
        File.Move(temporary, file, overwrite: true);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionShape();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
