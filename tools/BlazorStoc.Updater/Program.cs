using System.Text.Json;
using BlazorStoc.Updater;

// BlazorStoc.Updater (docs/PROPUNERE_ACTUALIZARE.md). Commands:
//   -- on a build machine --
//   keygen [--key-dir <folder>] [--replace]        makes the signing key of this machine (DPAPI, current user) and prints its fingerprint
//   fingerprint [--key-dir <folder>]               prints the fingerprint of the key of this machine
//   pack --app <published folder> --describe <file> --out <file.bsu> [--min-from <version>] [--notes-file <file>] [--platform win-x64] [--runtime net9.0] [--key-dir <folder>]
//   verify <file.bsu> [--trusted <fingerprint>[,<fingerprint>...]]
//   -- on the server (--install-root <folder>, or the folder that holds updater/) --
//   init [--port 5087] [--db-port 3307] [--maria-root <folder>] [--maria-bin <bin folder>] [--maria-zip <file>] [--scripts-dir <folder>] [--migration-account-file <file>] [--env NAME=value;...]
//        [--no-task] [--no-database] [--no-service] [--no-supervisor]   makes the folders, copies the updater and the scripts, chooses the ports (the given ones when free on this server, else the next free
//        ones) and writes them to settings.json, protects the installation, registers the scheduled task the application uses to ask for an update, makes the database server (MariaDB, its Windows
//        service, accounts, schema, private files) and registers the supervisor of the application (as administrator)
//   refresh [--from <folder>]                      replaces updater/ and scripts/ of the installation by this updater and its scripts (the installer does it after an update); --from: the updater to copy (default: this one)
//   make-installer --launcher <exe> --package <file.bsu> --updater <published updater folder> --out <exe> --version <v> [--prerequisites <folder of .exe installers>]   the installer: launcher + package + updater
//        + the installers of the .NET runtimes and the Visual C++ runtime (release.ps1)
//   run-requests                                   handles the requests the application left in updates/staged (what the scheduled task runs)
//   task register | remove | status                the scheduled task of the application (needs an administrator to register or remove)
//   install <file.bsu> [--actor <name>] [--reason <text>] [--trust <fingerprint>] [--reinstall] [--allow-downgrade] [--health-timeout <seconds>]
//   rollback [--restore-db <backup file name>] [--actor <name>] [--reason <text>]
//   status                                         the installed versions, the last update, the signers trusted, what /health says
//   trust list | add <fingerprint> [--note <text>] | remove <fingerprint>
// Exit codes: 0 ok; 1 error; 2 the package is not valid; 3 valid but the signer is not trusted (verify); 4 install refused (nothing changed); 5 install rolled back (the old version answers; the rollback command itself returns 0);
// 6 failed (the application may be down).
return await Cli.RunAsync(args);

/// <summary>Makes the engine of a command; the checks give their own (a fake application), so a whole command line runs without a real application.</summary>
public delegate UpdateEngine EngineFactory(InstallLayout layout, UpdaterSettings settings, IUpdateLog log, UpdateOptions options);

public static class Cli
{
    public static async Task<int> RunAsync(string[] args, EngineFactory? engineFactory = null, IInstallLocationStore? location = null, IPortProbe? ports = null, IScriptRunner? scripts = null)
    {
        try
        {
            if (args.Length == 0) return Usage();
            var options = Options.Parse(args[1..]);
            location ??= new RegistryInstallLocation();
            var keys = new SigningKeyStore(options.Get("key-dir") ?? SigningKeyStore.DefaultFolder);
            switch (args[0])
            {
                case "keygen":
                    var fingerprint = keys.Generate(options.Has("replace"));
                    Console.WriteLine($"Signing key made in {keys.Folder}");
                    Console.WriteLine($"Fingerprint: {SigningKeyStore.Display(fingerprint)}");
                    return 0;
                case "fingerprint":
                    Console.WriteLine(keys.Exists ? SigningKeyStore.Display(SigningKeyStore.Fingerprint(keys.LoadPublic())) : "(no signing key on this machine)");
                    return keys.Exists ? 0 : 1;
                case "pack":
                    var manifest = await PackageBuilder.PackAsync(options.Require("app"), await File.ReadAllTextAsync(options.Require("describe")), options.Require("out"), keys,
                        options.Get("platform") ?? "win-x64", options.Get("runtime") ?? "net9.0", options.Get("min-from"), DateTime.UtcNow,
                        options.Get("notes-file") is { } notesFile ? await File.ReadAllTextAsync(notesFile) : null);
                    Console.WriteLine($"Package {options.Require("out")}: version {manifest.Version}, schema {manifest.SchemaVersion}, {manifest.Files.Count} files, {new FileInfo(options.Require("out")).Length / 1048576.0:F1} MB");
                    Console.WriteLine($"Signed with: {SigningKeyStore.Display(SigningKeyStore.Fingerprint(keys.LoadPublic()))}");
                    return 0;
                case "verify":
                    return await VerifyAsync(Positional(args, "verify needs the package file"), options);
                case "init":
                    return await InitAsync(options, location, ports ?? new SystemPortProbe(), scripts ?? new PowerShellScriptRunner());
                case "refresh":
                    return Refresh(options, location);
                case "make-installer":
                    return await MakeInstallerAsync(options);
                case "install":
                    return await InstallAsync(Positional(args, "install needs the package file"), options, engineFactory, location);
                case "rollback":
                    return await RollbackAsync(options, engineFactory, location);
                case "status":
                    return await StatusAsync(options, location);
                case "run-requests":
                    return await RunRequestsAsync(options, engineFactory, location);
                case "uninstall":
                    return await UninstallAsync(options, location, scripts ?? new PowerShellScriptRunner());
                case "create-admin":
                    return await CreateAdminAsync(options, location);
                case "task":
                    return await TaskAsync(args, options, location);
                case "trust":
                    return Trust(args, options, location);
                default:
                    return Usage();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static string Positional(string[] args, string message) => args.Length > 1 && !args[1].StartsWith("--") ? args[1] : throw new ArgumentException(message);

    // ---- verify ----

    private static async Task<int> VerifyAsync(string package, Options options)
    {
        var trusted = options.Get("trusted")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(TrustedKeys.Normalise).ToArray();
        var result = await PackageVerifier.VerifyAsync(package, trusted);
        if (result.SignerFingerprint is not null) Console.WriteLine($"Signer: {SigningKeyStore.Display(result.SignerFingerprint)}");
        if (result.Manifest is { } manifest) Console.WriteLine($"Version {manifest.Version}, schema {manifest.SchemaVersion}, {manifest.Files.Count} files, built {manifest.BuiltUtc:yyyy-MM-dd HH:mm} UTC");
        foreach (var problem in result.Problems) Console.WriteLine($"PROBLEM: {problem}");
        if (!result.Ok) { Console.WriteLine("The package is NOT valid."); return 2; }
        if (result.Trusted == false) { Console.WriteLine("The package is valid but the signer is not trusted."); return 3; }
        Console.WriteLine(result.Trusted == true ? "The package is valid and the signer is trusted." : "The package is valid (no list of trusted signers was given).");
        return 0;
    }

    // ---- the server ----

    private static InstallLayout Layout(Options options, IInstallLocationStore location)
    {
        var root = options.Get("install-root") ?? Environment.GetEnvironmentVariable("BLAZORSTOC_INSTALL_ROOT");
        if (root is null)
        {
            var own = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (own.Name.Equals("updater", StringComparison.OrdinalIgnoreCase) && own.Parent is not null) root = own.Parent.FullName;
        }
        root ??= location.Read();
        return new InstallLayout(root ?? throw new ArgumentException("--install-root is required (or run the updater from the updater folder of the installation, or run \"init\" on this server first)"));
    }

    private static async Task<int> RunRequestsAsync(Options options, EngineFactory? engineFactory, IInstallLocationStore location)
    {
        var (engine, layout, log, _) = Engine(options, engineFactory, location);
        var taken = await new RequestProcessor(layout, engine, log).ProcessAsync();
        log.Info(taken == 0 ? "No request was waiting." : $"{taken} request(s) handled.");
        return 0;
    }

    private static async Task<int> TaskAsync(string[] args, Options options, IInstallLocationStore location)
    {
        var layout = Layout(options, location);
        var settings = UpdaterSettings.Load(layout);
        var action = args.Length > 1 ? args[1] : "status";
        Console.WriteLine(action switch
        {
            "register" => await UpdaterTask.RegisterAsync(layout, settings),
            "remove" => await UpdaterTask.RemoveAsync(layout),
            "status" => await UpdaterTask.StatusAsync(layout),
            _ => throw new ArgumentException("task register | remove | status")
        });
        return 0;
    }

    // The administrator account typed at the installer: the details come in the environment of this process (never on a command line) and go to the application on its standard input.
    // Removes what "init" and the installer made: the supervisor and the updater task, the firewall rule, the database server (service), the registry entry and the files. Without --remove-data the data
    // stay (the instance folder of the database, the files and keys of the application in the data root); with it nothing of them is left.
    private static async Task<int> UninstallAsync(Options options, IInstallLocationStore location, IScriptRunner scripts)
    {
        var layout = Layout(options, location);
        if (!File.Exists(layout.SettingsFile)) throw new InvalidOperationException($"{layout.Root} is not an installation made by the updater.");
        var settings = UpdaterSettings.Load(layout);
        foreach (var pair in settings.Environment ?? [])
            if (System.Environment.GetEnvironmentVariable(pair.Key) is null) System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        var removeData = options.Has("remove-data");
        var left = new List<string>();
        if (options.Get("backup-to") is { } backupTo)
        {
            // The restore package comes first: when it cannot be made nothing is removed.
            if (!removeData) throw new ArgumentException("--backup-to goes with --remove-data.");
            var version = layout.Current ?? throw new InvalidOperationException("No version is installed, so the restore package cannot be made: nothing was removed. Repair the installation first, or remove without the package.");
            Directory.CreateDirectory(backupTo);
            Console.WriteLine("Making the restore package (the database and the files of the application) ...");
            string packageName;
            try { packageName = await new ProcessReleaseTools(layout, settings, new FileUpdateLog(layout.LogsFolder)).BackupAsync(layout.AppFolder(version), System.Environment.UserName, CancellationToken.None); }
            catch (UpdateStepException exception) { throw new InvalidOperationException($"The restore package was not made, so nothing was removed: {exception.Message}"); }
            var source = Path.Combine(UpdaterSettings.DataRoot(), "assets", "database-backups", packageName);
            if (!File.Exists(source)) throw new InvalidOperationException($"The restore package {packageName} was made but is not where expected ({source}): nothing was removed.");
            var target = Path.Combine(backupTo, packageName);
            File.Copy(source, target, overwrite: true);
            if (new FileInfo(target).Length != new FileInfo(source).Length) throw new InvalidOperationException($"The copy of the restore package to {target} is not complete: nothing was removed.");
            Console.WriteLine($"Restore package saved: {target} ({new FileInfo(target).Length / 1048576.0:F1} MB). To use it: install BlazorStoc again, copy the file into the backup folder of the new installation ({Path.Combine(UpdaterSettings.DataRoot(), "assets", "database-backups")}) and restore it from the backup page of the application. The encryption keys of the application are not in the package.");
        }
        var port = settings.Port.ToString();

        Console.WriteLine("Stopping the application and removing its supervisor ...");
        var supervisor = Path.Combine(layout.ScriptsFolder, "supervisor.ps1");
        if (File.Exists(supervisor))
        {
            await scripts.RunAsync(supervisor, ["-Action", "stop", "-Port", port, "-InstallRoot", layout.Root], CancellationToken.None);
            await scripts.RunAsync(supervisor, ["-Action", "uninstall", "-Port", port, "-InstallRoot", layout.Root], CancellationToken.None);
        }
        try { Console.WriteLine(await UpdaterTask.RemoveAsync(layout)); }
        catch (InvalidOperationException exception) { Console.WriteLine($"The scheduled task of the updater: {exception.Message}"); }
        var firewall = Path.Combine(layout.ScriptsFolder, "firewall.ps1");
        if (File.Exists(firewall)) await scripts.RunAsync(firewall, ["-Action", "remove", "-Port", port], CancellationToken.None);

        var instance = settings.MariaRoot ?? Path.Combine(layout.Root, "mariadb");
        if (settings.MariaServiceName is { } service && File.Exists(Path.Combine(layout.ScriptsFolder, "uninstall-database.ps1")))
        {
            var arguments = new List<string> { "-Root", instance, "-ServiceName", service };
            if (removeData) arguments.Add("-RemoveData");
            var code = await scripts.RunAsync(Path.Combine(layout.ScriptsFolder, "uninstall-database.ps1"), arguments, CancellationToken.None);
            if (code != 0) left.Add($"{instance} (the database server could not be removed completely, exit code {code})");
        }

        if (string.Equals(location.Read(), layout.Root, StringComparison.OrdinalIgnoreCase)) location.Clear();

        if (removeData) RemoveDataRoot(UpdaterSettings.DataRoot(), left);
        foreach (var entry in Directory.EnumerateFileSystemEntries(layout.Root).ToArray())
        {
            // The data of the database stay unless --remove-data was given.
            if (!removeData && string.Equals(entry, Path.GetFullPath(instance), StringComparison.OrdinalIgnoreCase)) continue;
            TryDelete(entry, left);
        }
        if (removeData || Directory.EnumerateFileSystemEntries(layout.Root).Count() == 0) TryDelete(layout.Root, left);

        Console.WriteLine(removeData ? "BlazorStoc was removed, with its data." : $"BlazorStoc was removed. The data were kept: the database in {instance} and the files and keys of the application in {UpdaterSettings.DataRoot()}; a new installation reuses them.");
        foreach (var path in left) Console.WriteLine($"Left behind (remove by hand): {path}");
        return left.Count == 0 ? 0 : 6;
    }

    // Only what the installation itself made in the data root: never the whole folder, which may be shared.
    private static void RemoveDataRoot(string dataRoot, List<string> left)
    {
        if (!Directory.Exists(dataRoot)) return;
        foreach (var name in new[] { "assets", "application-connection.private.json", "backup-account.private.json", "restore-account.private.json", "database-connection.override.json", "admin.private.cnf", "logs", "update-journal.json" })
        {
            var path = Path.Combine(dataRoot, name);
            if (File.Exists(path) || Directory.Exists(path)) TryDelete(path, left);
        }
        if (!Directory.EnumerateFileSystemEntries(dataRoot).Any()) TryDelete(dataRoot, left);
    }

    private static void TryDelete(string path, List<string> left)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
        }
        left.Add(path);
    }

    private static async Task<int> CreateAdminAsync(Options options, IInstallLocationStore location)
    {
        var layout = Layout(options, location);
        var settings = UpdaterSettings.Load(layout);
        var username = System.Environment.GetEnvironmentVariable("BLAZORSTOC_ADMIN_USER");
        var password = System.Environment.GetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new ArgumentException("create-admin needs BLAZORSTOC_ADMIN_USER and BLAZORSTOC_ADMIN_PASSWORD in the environment.");
        if (layout.Current is not { } version) throw new InvalidOperationException("No version is installed: the application that makes the account is the installed one.");
        var tools = new ProcessReleaseTools(layout, settings, new FileUpdateLog(layout.LogsFolder));
        var (code, message) = await tools.CreateAdministratorAsync(layout.AppFolder(version), username, System.Environment.GetEnvironmentVariable("BLAZORSTOC_ADMIN_NAME") ?? username, password, CancellationToken.None);
        Console.WriteLine(message);
        return code;
    }

    private static async Task<int> InitAsync(Options options, IInstallLocationStore location, IPortProbe ports, IScriptRunner scripts)
    {
        var layout = Layout(options, location);
        layout.EnsureFolders();
        InstallTool(layout, options.Get("from"), options.Get("scripts-dir"));
        var provisioner = new ServerProvisioner(layout, scripts, Console.Out);
        var withDatabase = !options.Has("no-database");
        var isNew = !File.Exists(layout.SettingsFile);
        if (isNew)
        {
            // The initial ports are kept when they are free on this server now, else the next free ones are taken; they are written to the settings and used by everything that follows.
            var (application, database) = provisioner.ChoosePorts(IntOption(options, "port", ServerProvisioner.DefaultApplicationPort), IntOption(options, "db-port", ServerProvisioner.DefaultDatabasePort), withDatabase, ports);
            new UpdaterSettings(Port: application.Chosen, Dotnet: UpdaterTask.DotnetPath(new UpdaterSettings()), MariaRoot: options.Get("maria-root") ?? (withDatabase ? Path.Combine(layout.Root, "mariadb") : null),
                Environment: ParseEnvironment(options.Get("env")), DatabasePort: database?.Chosen, MariaServiceName: withDatabase ? ServerProvisioner.DefaultServiceName : null).Save(layout);
        }
        else Console.WriteLine($"The installation was made before: its ports stay as they are (application {UpdaterSettings.Load(layout).Port}, database {UpdaterSettings.Load(layout).DatabasePort?.ToString() ?? "not made by the installer"}).");
        if (options.Get("migration-account-file") is { } account)
        {
            File.Copy(account, layout.MigrationAccountFile, overwrite: true);
            Console.WriteLine($"The account that changes the schema: copied to {layout.MigrationAccountFile}");
        }
        var restricted = Acl.ProtectInstallation(layout);
        Console.WriteLine($"Installation folder ready: {layout.Root}");
        if (restricted) RegisterLocation(layout, location);
        if (options.Has("no-task")) Console.WriteLine("The scheduled task of the application was not registered (--no-task): Setari -> Actualizare cannot start the updater.");
        else if (!restricted) Console.WriteLine("The scheduled task of the application was NOT registered (it needs an administrator): run init again as administrator, signed in as the account the application runs as.");
        else Console.WriteLine(await UpdaterTask.RegisterAsync(layout, UpdaterSettings.Load(layout)));
        Console.WriteLine(restricted ? "Protected: only Administrators and SYSTEM can change the installation; updater/ and private/ are closed to everyone else; Users can write only updates/staged, logs/ and state/supervisor."
            : "NOTE: not run by an administrator (or not Windows): the folders were NOT protected (any local user may be able to change current.txt and the releases); run init again as administrator.");

        var settings = UpdaterSettings.Load(layout);
        // The data root the application will use (App__DataRoot given with --env) is where the private files of the accounts are written.
        foreach (var pair in settings.Environment ?? [])
            if (System.Environment.GetEnvironmentVariable(pair.Key) is null) System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        // The supervisor runs as the ordinary account, which cannot read updater/: the variables of the application (for example App__DataRoot) are also in scripts/environment.json.
        if (settings.Environment is { Count: > 0 })
            File.WriteAllText(Path.Combine(layout.ScriptsFolder, "environment.json"), JsonSerializer.Serialize(settings.Environment));
        if (settings.DatabasePort is not null)
        {
            var code = await provisioner.ProvisionDatabaseAsync(settings, options.Get("maria-bin"), options.Get("maria-zip"), options.Has("no-service")).ConfigureAwait(false);
            if (code != 0) { Console.Error.WriteLine($"The database server could not be made (exit code {code}, see above). Fix the cause and run the installer with --init again: what was done is kept."); return code; }
        }
        else Console.WriteLine("The database server was not made by the installer (--no-database): the application account files must be put in the data root by hand (docs/OPERARE_ACTUALIZARE.md).");
        if (options.Has("no-supervisor")) Console.WriteLine("The supervisor of the application was not registered (--no-supervisor).");
        else
        {
            var code = await provisioner.RegisterSupervisorAsync(settings).ConfigureAwait(false);
            if (code != 0) { Console.Error.WriteLine($"The supervisor could not be registered (exit code {code}, see above)."); return code; }
        }
        if (options.Has("no-firewall")) Console.WriteLine("The firewall was not changed (--no-firewall): the application is reachable from other computers only if you open its port.");
        else
        {
            var code = await provisioner.OpenFirewallAsync(settings).ConfigureAwait(false);
            if (code != 0) Console.Error.WriteLine($"The firewall rule for port {settings.Port} could not be made (exit code {code}, see above): the other computers of the network cannot reach the application until the port is opened.");
        }
        Console.WriteLine(layout.Current is { } current ? $"Installed: {current}" : "No version is installed yet: run install <package.bsu>.");
        Console.WriteLine($"Application port: {settings.Port}{(settings.DatabasePort is { } databasePort ? $", database port: {databasePort}" : "")} (updater/settings.json).");
        return 0;
    }

    private static int IntOption(Options options, string name, int fallback) =>
        options.Get(name) is not { } text ? fallback : int.TryParse(text, out var value) && value is >= PortSelector.Minimum and <= PortSelector.Maximum ? value : throw new ArgumentException($"--{name} is a port between {PortSelector.Minimum} and {PortSelector.Maximum}.");

    // The installer finds the installation of this server through HKLM (only an administrator can write it): "init" is what writes it.
    private static void RegisterLocation(InstallLayout layout, IInstallLocationStore location)
    {
        try
        {
            if (!string.Equals(location.Read(), layout.Root, StringComparison.OrdinalIgnoreCase)) location.Write(layout.Root);
            Console.WriteLine($"Registered as the installation of this server: {layout.Root}");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Console.WriteLine($"The installation could not be registered in the registry ({exception.Message}); the installer will need --install-root {layout.Root}.");
        }
    }

    // The scripts of the supervisor and the updater with its scripts go into the installation. The supervisor runs as the ordinary account of the application (its scheduled task), so its
    // scripts are not in updater/ (closed to everyone but administrators). The updater that the scheduled task runs must sit in updater/: from a folder any user can write to, anyone could replace it.
    private static void InstallTool(InstallLayout layout, string? fromDirectory, string? scriptsDirectory)
    {
        var ownFolder = (fromDirectory ?? AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var scripts = scriptsDirectory ?? Path.Combine(ownFolder, "scripts");
        foreach (var (name, folder) in new[] { ("supervisor.ps1", layout.ScriptsFolder), ("data-root.ps1", layout.ScriptsFolder), ("updater-task.ps1", layout.UpdaterFolder),
                     ("provision-database.ps1", layout.ScriptsFolder), ("uninstall-database.ps1", layout.ScriptsFolder), ("firewall.ps1", layout.ScriptsFolder), ("database-accounts.json", layout.ScriptsFolder), ("schema-mariadb.sql", layout.ScriptsFolder), ("triggers-mariadb.sql", layout.ScriptsFolder) })
        {
            var source = Path.Combine(scripts, name);
            if (!File.Exists(source)) throw new ArgumentException($"{source} was not found (--scripts-dir).");
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, name), overwrite: true);
        }
        if (!string.Equals(Path.GetFullPath(ownFolder), Path.GetFullPath(layout.UpdaterFolder), StringComparison.OrdinalIgnoreCase)) CopyUpdater(ownFolder, layout.UpdaterFolder);
    }

    /// <summary>What the installer runs after an update: this updater and its scripts replace the ones of the installation (settings.json, the trusted signers and the task are not touched).</summary>
    private static int Refresh(Options options, IInstallLocationStore location)
    {
        var layout = Layout(options, location);
        if (!File.Exists(layout.SettingsFile)) throw new InvalidOperationException($"{layout.Root} is not an installation made by the updater (run init).");
        InstallTool(layout, options.Get("from"), options.Get("scripts-dir"));
        Console.WriteLine($"The updater and the scripts of {layout.Root} are now the ones of this updater.");
        return 0;
    }

    private static async Task<int> MakeInstallerAsync(Options options)
    {
        var output = options.Require("out");
        var sha = await InstallerPayload.BuildAsync(options.Require("launcher"), output, options.Require("package"), options.Require("updater"), DateTime.UtcNow, options.Require("version"), CancellationToken.None, options.Get("prerequisites"));
        Console.WriteLine($"Installer {output}: {new FileInfo(output).Length / 1048576.0:F1} MB");
        Console.WriteLine($"SHA-256: {sha}");
        return 0;
    }

    // The files of the updater (its dll, dependencies, runtime configuration) go to updater/; the scripts are copied separately and the sub-folder "scripts" is not repeated.
    private static void CopyUpdater(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("scripts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // "NAME=value;NAME2=value2": the variables the application needs when the updater starts it or its tools (for example App__DataRoot when the data are not in the folder of the machine).
    private static Dictionary<string, string>? ParseEnvironment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0) throw new ArgumentException($"--env expects NAME=value pairs separated by \";\" (\"{pair}\").");
            values[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }
        return values;
    }

    private static (UpdateEngine Engine, InstallLayout Layout, FileUpdateLog Log, UpdaterSettings Settings) Engine(Options options, EngineFactory? engineFactory, IInstallLocationStore location)
    {
        var layout = Layout(options, location);
        var settings = UpdaterSettings.Load(layout);
        // The task through which the application starts the updater has the environment of the account, not the one of the application: the variables the settings name apply here too
        // (the saved port is looked for in the data root of the application), unless the account already sets them.
        foreach (var pair in settings.Environment ?? [])
            if (System.Environment.GetEnvironmentVariable(pair.Key) is null) System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        var log = new FileUpdateLog(layout.LogsFolder);
        var timeout = int.TryParse(options.Get("health-timeout"), out var seconds) ? seconds : settings.HealthTimeoutSeconds;
        var updateOptions = new UpdateOptions(TimeSpan.FromSeconds(timeout), TimeSpan.FromSeconds(2), settings.KeepReleases);
        var engine = engineFactory?.Invoke(layout, settings, log, updateOptions)
            ?? new UpdateEngine(layout, new SupervisorHost(layout, settings, log), new ProcessReleaseTools(layout, settings, log), log, updateOptions);
        return (engine, layout, log, settings);
    }

    private static async Task<int> InstallAsync(string package, Options options, EngineFactory? engineFactory, IInstallLocationStore location)
    {
        var (engine, _, log, _) = Engine(options, engineFactory, location);
        var trust = options.Get("trust");
        if (trust is null && options.Has("trust-package-signer"))
        {
            // First installation: the signer of the package that came inside the installer is trusted (the package is verified again by the engine, which refuses it when it is damaged).
            var check = await PackageVerifier.VerifyAsync(Path.GetFullPath(package));
            if (check.Ok && check.SignerFingerprint is { } signer)
            {
                Console.WriteLine($"First installation: the signer of the package inside this file is trusted from now on ({SigningKeyStore.Display(signer)}). If this file did not come from your build machine, cancel and compare its SHA-256 with the one release.ps1 printed.");
                trust = signer;
            }
        }
        var request = new InstallRequest(Path.GetFullPath(package), options.Get("actor") ?? Environment.UserName, options.Get("reason") ?? "installer",
            options.Has("reinstall"), options.Has("allow-downgrade"), trust, ConfirmUnknownSigner, UpdateVia.Installer, UpdateKind.Install, await Sha256Async(package));
        var result = await engine.InstallAsync(request);
        log.Info($"Result: {result.Outcome} - {result.Message}");
        return ExitCodeOf(result);
    }

    private static async Task<int> RollbackAsync(Options options, EngineFactory? engineFactory, IInstallLocationStore location)
    {
        var (engine, _, log, _) = Engine(options, engineFactory, location);
        var result = await engine.RollbackAsync(options.Get("actor") ?? Environment.UserName, options.Get("reason") ?? "rollback", options.Get("restore-db"));
        log.Info($"Result: {result.Outcome} - {result.Message}");
        // For this command "rolled back" is what was asked for: the previous version answers, so it is a success.
        return result.Outcome == UpdateOutcome.RolledBack ? 0 : ExitCodeOf(result);
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static int ExitCodeOf(UpdateResult result) => result.Outcome switch { UpdateOutcome.Succeeded => 0, UpdateOutcome.Refused => 4, UpdateOutcome.RolledBack => 5, _ => 6 };

    /// <summary>An unknown signer is trusted only after a person typed the first characters of its fingerprint at this console (compared with the one printed on the build machine).</summary>
    private static bool ConfirmUnknownSigner(string fingerprint)
    {
        if (Console.IsInputRedirected) return false;
        Console.WriteLine();
        Console.WriteLine("This package is signed with a key this server has not seen:");
        Console.WriteLine("  " + SigningKeyStore.Display(fingerprint));
        Console.WriteLine("Compare it with the fingerprint printed by release.ps1 on the build machine. If it is the same, type its first 8 characters to trust it; anything else cancels.");
        Console.Write("> ");
        return string.Equals(Console.ReadLine()?.Trim().Replace(" ", ""), fingerprint[..8], StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> StatusAsync(Options options, IInstallLocationStore location)
    {
        var (_, layout, _, settings) = Engine(options, null, location);
        Console.WriteLine($"Installation: {layout.Root}");
        Console.WriteLine($"Running version (current.txt): {layout.Current ?? "-"}   previous: {layout.Previous ?? "-"}");
        Console.WriteLine($"On disk: {string.Join(", ", layout.InstalledVersions())}");
        var trusted = new TrustedKeys(layout.TrustedKeysFile).Load();
        Console.WriteLine($"Trusted signers: {trusted.Count}");
        var status = new UpdateEngine(layout, new SupervisorHost(layout, settings, new FileUpdateLog(layout.LogsFolder)), new ProcessReleaseTools(layout, settings, new FileUpdateLog(layout.LogsFolder)),
            new FileUpdateLog(layout.LogsFolder), UpdateOptions.Default).ReadStatus();
        if (status is not null) Console.WriteLine($"Last update: {status.From ?? "-"} -> {status.To}, phase {status.Phase}, outcome {status.Outcome ?? "(running or interrupted)"}, {status.UpdatedUtc:yyyy-MM-dd HH:mm} UTC by {status.Actor}: {status.Message}");
        var health = await new SupervisorHost(layout, settings, new FileUpdateLog(layout.LogsFolder)).ProbeAsync(CancellationToken.None);
        Console.WriteLine(health.Reachable ? $"/health: {health.StatusCode}, version {health.Version ?? "-"}" : "/health: no answer");
        return 0;
    }

    private static int Trust(string[] args, Options options, IInstallLocationStore location)
    {
        var layout = Layout(options, location);
        var keys = new TrustedKeys(layout.TrustedKeysFile);
        var action = args.Length > 1 ? args[1] : "list";
        switch (action)
        {
            case "list":
                foreach (var key in keys.Load()) Console.WriteLine($"{SigningKeyStore.Display(key.Fingerprint)}   added {key.AddedUtc:yyyy-MM-dd HH:mm} by {key.AddedBy}   {key.Note}");
                return 0;
            case "add":
                var added = keys.Add(args.Length > 2 ? args[2] : throw new ArgumentException("trust add needs the fingerprint"), options.Get("actor") ?? Environment.UserName, options.Get("note") ?? "added by hand", DateTime.UtcNow);
                Console.WriteLine($"Trusted: {SigningKeyStore.Display(added.Fingerprint)}");
                return 0;
            case "remove":
                var removed = keys.Remove(args.Length > 2 ? args[2] : throw new ArgumentException("trust remove needs the fingerprint"));
                Console.WriteLine(removed ? "Removed." : "That fingerprint was not trusted.");
                return removed ? 0 : 1;
            default:
                return Usage();
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("BlazorStoc.Updater keygen | fingerprint | pack | make-installer | verify | init | refresh | install <file.bsu> | rollback | status | run-requests | task | trust list|add|remove  (see the top of Program.cs)");
        return 1;
    }

    private sealed class Options(Dictionary<string, string?> values)
    {
        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("--")) continue;
                var name = args[index][2..];
                values[name] = index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[++index] : null;
            }
            return new Options(values);
        }

        public bool Has(string name) => values.ContainsKey(name);
        public string? Get(string name) => values.GetValueOrDefault(name);
        public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} is required");
    }
}
