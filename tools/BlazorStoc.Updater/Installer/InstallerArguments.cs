namespace BlazorStoc.Updater;

public enum InstallerMode { Install, Repair, Rollback, Status, Check, Init, Help }

/// <summary>The command line of BlazorStoc-Actualizare-&lt;version&gt;.exe. Original is what was typed (an installer that is not elevated starts itself again, elevated, with the same words).</summary>
public sealed record InstallerArguments(InstallerMode Mode, IReadOnlyList<string> Original, string? InstallRoot = null, string? Reason = null, string? Trust = null, string? Trusted = null,
    bool AllowDowngrade = false, bool Yes = false, bool NoTask = false, bool NoPause = false, string? Port = null, string? MariaRoot = null,
    string? MigrationAccountFile = null, string? Env = null, string? Dotnet = null, string? RestoreDb = null, string? HealthTimeout = null)
{
    public bool Init => Mode == InstallerMode.Init;

    public const string Usage = """
        BlazorStoc-Actualizare-<version>.exe  [what to do]  [options]

        what to do (default: update to the version inside this file):
          --repair                 install this version again over the one that runs (the application is broken)
          --rollback               go back to the previous version (--restore-db <backup file> also restores the database)
          --status                 what is installed, the last update, what /health answers
          --check                  verify the file only (nothing is installed, no administrator needed); --trusted <fingerprint> names the signer expected
          --init                   first step on a new server: makes the installation folder (the updater, the scripts, the protection, the scheduled task through which the
                                   application asks for updates) and registers it for this server; installs no version. Needs --install-root; also --port,
                                   --maria-root, --migration-account-file, --env "NAME=value;...", --no-task. Then register the supervisor and run this file again to install.
        options:
          --install-root <folder>  the installation (default: the one this server registered with "init")
          --reason <text>          why (goes to the journal of the application)
          --trust <fingerprint>    trust the signer of the package: the whole fingerprint printed by release.ps1 (spaces optional); without it a person at the console is asked
          --allow-downgrade        accept a version older than the one that runs
          --yes                    do not ask for confirmation
          --dotnet <path>          the dotnet to run the updater with
          --health-timeout <s>     how long the new version has to answer /health
          --no-pause               do not wait for Enter at the end
        """;

    public static InstallerArguments Parse(IReadOnlyList<string> args)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] flagNames = ["repair", "rollback", "status", "check", "help", "yes", "init", "no-task", "no-pause", "allow-downgrade"];
        string[] valueNames = ["install-root", "reason", "trust", "trusted", "port", "maria-root", "migration-account-file", "env", "dotnet", "restore-db", "health-timeout"];
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument is "-h" or "-?" or "/?") { flags.Add("help"); continue; }
            if (!argument.StartsWith("--")) throw new ArgumentException($"\"{argument}\" is not understood (options start with --; --help lists them).");
            var name = argument[2..];
            if (flagNames.Contains(name, StringComparer.OrdinalIgnoreCase)) flags.Add(name);
            else if (valueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                values[name] = index + 1 < args.Count && !args[index + 1].StartsWith("--") ? args[++index] : throw new ArgumentException($"--{name} needs a value.");
            else throw new ArgumentException($"--{name} is not an option of this installer (--help lists them).");
        }
        var modes = new[] { "repair", "rollback", "status", "check", "init", "help" }.Where(flags.Contains).ToArray();
        if (modes.Length > 1) throw new ArgumentException($"--{modes[0]} and --{modes[1]} cannot be used together.");
        var mode = modes.Length == 0 ? InstallerMode.Install : Enum.Parse<InstallerMode>(modes[0], ignoreCase: true);
        if (values.ContainsKey("restore-db") && mode != InstallerMode.Rollback) throw new ArgumentException("--restore-db goes with --rollback.");
        if (mode != InstallerMode.Init && (flags.Contains("no-task") || values.ContainsKey("port") || values.ContainsKey("maria-root") || values.ContainsKey("migration-account-file") || values.ContainsKey("env")))
            throw new ArgumentException("--port, --maria-root, --migration-account-file, --env and --no-task go with --init.");
        if (values.TryGetValue("health-timeout", out var seconds) && !(int.TryParse(seconds, out var parsed) && parsed > 0)) throw new ArgumentException("--health-timeout is a number of seconds.");
        string? Value(string name) => values.GetValueOrDefault(name);
        return new InstallerArguments(mode, [.. args], Value("install-root"), Value("reason"), Value("trust"), Value("trusted"), flags.Contains("allow-downgrade"), flags.Contains("yes"),
            flags.Contains("no-task"), flags.Contains("no-pause"), Value("port"), Value("maria-root"), Value("migration-account-file"), Value("env"), Value("dotnet"), Value("restore-db"), Value("health-timeout"));
    }
}
