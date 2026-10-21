namespace BlazorStoc.Updater;

public enum InstallerMode { Install, Repair, Rollback, Status, Check, Init, Uninstall, Help }

/// <summary>The command line of BlazorStoc-Actualizare-&lt;version&gt;.exe. Original is what was typed (an installer that is not elevated starts itself again, elevated, with the same words).</summary>
public sealed record InstallerArguments(InstallerMode Mode, IReadOnlyList<string> Original, string? InstallRoot = null, string? Reason = null, string? Trust = null, string? Trusted = null,
    bool AllowDowngrade = false, bool Yes = false, bool NoTask = false, bool NoPause = false, string? Port = null, string? MariaRoot = null,
    string? MigrationAccountFile = null, string? Env = null, string? Dotnet = null, string? RestoreDb = null, string? HealthTimeout = null,
    string? DbPort = null, string? MariaBin = null, string? MariaZip = null, bool NoDatabase = false, bool NoService = false, bool NoSupervisor = false, bool NoInstall = false,
    string? AdminUser = null, string? AdminName = null, bool NoAdmin = false, bool RemoveData = false, bool NoFirewall = false, string? BackupTo = null)
{
    public bool Init => Mode == InstallerMode.Init;

    /// <summary>Where a new server is installed when a person at the console accepts the question (a double click on the file).</summary>
    public const string DefaultInstallRoot = @"C:\BlazorStoc";

    public const string Usage = """
        BlazorStoc-Actualizare-<version>.exe  [what to do]  [options]

        A double click on the file (or --gui) opens the installation window (Romanian): the same installer, with questions in a window. --gui-preview shows the window with a made-up
        installation. Options given next to --gui (--install-root, --port, --db-port, --no-database and the others of --init) fill the window or go on to the installer.

        what to do (default: update to the version inside this file):
          --repair                 install this version again over the one that runs (the application is broken)
          --rollback               go back to the previous version (--restore-db <backup file> also restores the database)
          --uninstall              remove BlazorStoc from this server: the application, its supervisor and tasks, the firewall rule, the database server (Windows service) and the files. The data are
                                   kept (the database instance folder, the files and keys of the application); --remove-data deletes them too (irreversible; at a console you must type STERGE).
                                   --backup-to <folder> (with --remove-data) first saves a restore package there: the backup of the database with the files of the application; the
                                   installer stops, and removes nothing, if the package cannot be made. At a console without --backup-to it asks for the folder.
          --status                 what is installed, the last update, what /health answers
          --check                  verify the file only (nothing is installed, no administrator needed); --trusted <fingerprint> names the signer expected
          --init                   first installation on a new server, all in one (a plain run does it by itself on a server without an installation, in C:\BlazorStoc or
                                   in the empty folder named by --install-root; --init is for the options below, or to name a folder that is not empty): the installation folder (updater, scripts, protection, the scheduled task through which the
                                   application asks for updates), the free ports of the application and of the database (the initial ones, 5087 and 3307, or --port and
                                   --db-port, when they are free on this server at that moment; else the next free ones), the database server (MariaDB, its Windows service,
                                   accounts, schema), the supervisor of the application and the version inside this file. The ports chosen are written to updater/settings.json.
                                   Also: --maria-root <folder of the database instance>, --maria-bin <bin folder of a MariaDB already unpacked>, --maria-zip <MariaDB archive
                                   downloaded beforehand, when the server has no Internet>, --migration-account-file, --env "NAME=value;...", --no-task, --no-database (the database is
                                   somewhere else), --no-service (the database as a hidden process, for tests), --no-supervisor, --no-install (prepare only, install no version).
                                   The administrator account of the application: the installer asks for a user name, a password and the password again (the second one is
                                   checked as you type) and makes that user, as administrator, in the users of the application. Without a console: --admin-user <name>
                                   [--admin-name <display name>] with the password in the environment variable BLAZORSTOC_ADMIN_PASSWORD; --no-admin makes no account.
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
        string[] flagNames = ["repair", "rollback", "status", "check", "help", "yes", "init", "no-task", "no-pause", "allow-downgrade", "no-database", "no-service", "no-supervisor", "no-install", "no-admin", "uninstall", "remove-data", "no-firewall"];
        string[] valueNames = ["install-root", "reason", "trust", "trusted", "port", "db-port", "maria-root", "maria-bin", "maria-zip", "migration-account-file", "env", "dotnet", "restore-db", "health-timeout", "admin-user", "admin-name", "backup-to"];
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
        var modes = new[] { "repair", "rollback", "status", "check", "init", "uninstall", "help" }.Where(flags.Contains).ToArray();
        if (modes.Length > 1) throw new ArgumentException($"--{modes[0]} and --{modes[1]} cannot be used together.");
        var mode = modes.Length == 0 ? InstallerMode.Install : Enum.Parse<InstallerMode>(modes[0], ignoreCase: true);
        if (values.ContainsKey("backup-to") && !(mode == InstallerMode.Uninstall && flags.Contains("remove-data"))) throw new ArgumentException("--backup-to goes with --uninstall --remove-data.");
        if (flags.Contains("remove-data") && mode != InstallerMode.Uninstall) throw new ArgumentException("--remove-data goes with --uninstall.");
        if (flags.Contains("no-firewall") && mode != InstallerMode.Init) throw new ArgumentException("--no-firewall goes with --init.");
        if (values.ContainsKey("restore-db") && mode != InstallerMode.Rollback) throw new ArgumentException("--restore-db goes with --rollback.");
        string[] initFlags = ["no-task", "no-database", "no-service", "no-supervisor", "no-install", "no-admin"];
        string[] initValues = ["port", "db-port", "maria-root", "maria-bin", "maria-zip", "migration-account-file", "env", "admin-user", "admin-name"];
        if (mode != InstallerMode.Init && (initFlags.Any(flags.Contains) || initValues.Any(values.ContainsKey)))
            throw new ArgumentException("--port, --db-port, --maria-root, --maria-bin, --maria-zip, --migration-account-file, --env, --no-task, --no-database, --no-service, --no-supervisor, --no-install, --no-admin, --admin-user and --admin-name go with --init.");
        if (values.ContainsKey("admin-user") && flags.Contains("no-admin")) throw new ArgumentException("--admin-user and --no-admin cannot be used together.");
        foreach (var name in new[] { "port", "db-port" })
            if (values.TryGetValue(name, out var port) && !(int.TryParse(port, out var number) && number is >= 1024 and <= 65535)) throw new ArgumentException($"--{name} is a port between 1024 and 65535.");
        if (values.TryGetValue("health-timeout", out var seconds) && !(int.TryParse(seconds, out var parsed) && parsed > 0)) throw new ArgumentException("--health-timeout is a number of seconds.");
        string? Value(string name) => values.GetValueOrDefault(name);
        return new InstallerArguments(mode, [.. args], Value("install-root"), Value("reason"), Value("trust"), Value("trusted"), flags.Contains("allow-downgrade"), flags.Contains("yes"),
            flags.Contains("no-task"), flags.Contains("no-pause"), Value("port"), Value("maria-root"), Value("migration-account-file"), Value("env"), Value("dotnet"), Value("restore-db"), Value("health-timeout"),
            Value("db-port"), Value("maria-bin"), Value("maria-zip"), flags.Contains("no-database"), flags.Contains("no-service"), flags.Contains("no-supervisor"), flags.Contains("no-install"),
            Value("admin-user"), Value("admin-name"), flags.Contains("no-admin"), flags.Contains("remove-data"), flags.Contains("no-firewall"), Value("backup-to"));
    }
}
