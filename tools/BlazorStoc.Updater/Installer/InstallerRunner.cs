namespace BlazorStoc.Updater;

/// <summary>Exit codes of the installer: 0-6 are the updater's (0 ok, 2 package not valid, 3 signer not trusted, 4 refused, 5 rolled back, 6 failed); the installer's own start at 10.</summary>
public static class InstallerExit
{
    public const int NotAdministrator = 10;
    public const int NoPayload = 11;
    public const int DamagedPayload = 12;
    public const int NoInstallation = 13;
    public const int NoDotnet = 14;
    public const int Cancelled = 15;
    public const int BadArguments = 16;
    public const int NoAdministrator = 17;
    public const int PrerequisiteFailed = 18;
}

/// <summary>What the installer needs from the machine it runs on; the launcher has the real one (<see cref="ConsoleInstallerHost"/>), the checks a fake that runs the updater in the same process.</summary>
public interface IInstallerHost
{
    string ExePath { get; }
    bool IsAdministrator { get; }
    /// <summary>True when a person can answer a question at the console.</summary>
    bool Interactive { get; }
    /// <summary>The installation this server registered (init), if any.</summary>
    string? RegisteredInstallRoot { get; }
    /// <summary>Where a server without an installation gets one when no folder is named (null: none, the folder has to be named).</summary>
    string? DefaultInstallRoot { get; }
    /// <summary>Asks the person at the console for the first administrator account of the application (user name, password, password again); null when nobody can be asked.</summary>
    AdministratorAccount? AskAdministrator();
    void Info(string message);
    void Error(string message);
    string? ReadLine();
    /// <summary>Starts this installer again with administrator rights (Windows asks) and returns its exit code; null when that was refused or failed.</summary>
    int? Elevate(IReadOnlyList<string> arguments);
    /// <summary>The dotnet that runs the updater (the one asked for, else the one Windows has), null when there is none.</summary>
    string? FindDotnet(string? requested);
    /// <summary>True when the Visual C++ 2015-2022 runtime (x64) is installed: the database server and the native libraries of the application need it.</summary>
    bool VisualCppRuntimeInstalled { get; }
    /// <summary>True when the file is an installer signed by Microsoft (the prerequisites inside the installer are checked before they are run).</summary>
    bool IsSignedByMicrosoft(string file);
    /// <summary>Runs an installer of a prerequisite and returns its exit code.</summary>
    Task<int> RunPrerequisiteAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    /// <summary>Null when the .NET runtimes the updater and the application need (Microsoft.NETCore.App 9 and Microsoft.AspNetCore.App 9) are there, else what is missing.</summary>
    Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken);
    /// <summary>A new folder only administrators can use, for the updater and the package taken out of the payload.</summary>
    string CreateWorkFolder();
    void DeleteWorkFolder(string folder);
    /// <summary>Runs "dotnet &lt;updater dll&gt; &lt;arguments&gt;" with the console of the installer (its output and its questions are the ones of the person at the console).</summary>
    Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null);
}

/// <summary>
/// BlazorStoc-Actualizare-&lt;version&gt;.exe (docs/PROPUNERE_ACTUALIZARE.md, section 4). It takes the updater and the signed package out of its own file into a folder only administrators can use and
/// runs that updater: the code that updates is the one that came with the package, not the one on the server, and it works whether the application runs or not. After a good result the updater
/// of the installation (updater/, scripts/) is replaced by this one, so the next update from the application also runs the new engine. Nothing here decides whether a package may be
/// installed: signature, signer, version, room, backup, migrations, health and rollback are the updater's.
/// </summary>
public sealed class InstallerRunner(IInstallerHost host)
{
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        InstallerArguments arguments;
        try { arguments = InstallerArguments.Parse(args); }
        catch (ArgumentException exception)
        {
            host.Error(exception.Message);
            host.Info(InstallerArguments.Usage);
            return InstallerExit.BadArguments;
        }
        if (arguments.Mode == InstallerMode.Help) { host.Info(InstallerArguments.Usage); return 0; }

        InstallerPayload? payload;
        try { payload = InstallerPayload.TryOpen(host.ExePath); }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            host.Error(exception.Message);
            return InstallerExit.DamagedPayload;
        }
        if (payload is null)
        {
            host.Error("This file carries no package (it is the launcher alone). Use the file made by release.ps1: BlazorStoc-Actualizare-<version>.exe.");
            return InstallerExit.NoPayload;
        }
        using (payload) return await RunAsync(arguments, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunAsync(InstallerArguments arguments, InstallerPayload payload, CancellationToken cancellationToken)
    {
        var info = payload.Info;
        host.Info($"BlazorStoc installer: version {info.Version}, made {info.BuiltUtc:dd.MM.yyyy HH:mm} UTC, package SHA-256 {info.PackageSha256[..16]}...");
        if (arguments.Mode != InstallerMode.Check && !host.IsAdministrator)
        {
            host.Info("This needs administrator rights: Windows will ask for them.");
            return host.Elevate(arguments.Original) ?? Fail(InstallerExit.NotAdministrator, "The installer was not started as administrator (the request was refused or cancelled). Run it again and allow it, or open an elevated console.");
        }

        // What the application needs and the server may lack (the .NET 9 runtimes, the Visual C++ runtime) is installed from the installers inside this file; --check changes nothing.
        if (arguments.Mode is InstallerMode.Install or InstallerMode.Repair or InstallerMode.Init && await InstallPrerequisitesAsync(payload, arguments, cancellationToken).ConfigureAwait(false) is { } prerequisiteCode) return prerequisiteCode;

        var dotnet = host.FindDotnet(arguments.Dotnet);
        if (dotnet is null) return Fail(InstallerExit.NoDotnet, "dotnet was not found on this machine (the .NET 9 runtime the application needs also runs this updater). Install it, or give the path with --dotnet.");
        if (await host.RuntimeProblemAsync(dotnet, cancellationToken).ConfigureAwait(false) is { } runtimeProblem) return Fail(InstallerExit.NoDotnet, runtimeProblem);

        InstallLayout? layout = null;
        if (arguments.Mode != InstallerMode.Check)
        {
            var root = arguments.InstallRoot ?? host.RegisteredInstallRoot;
            // A server with no installation registered is a new server: the default folder is used (nothing to name).
            if (root is null && arguments.Mode == InstallerMode.Install) root = host.DefaultInstallRoot;
            if (root is null)
                return Fail(arguments.Init ? InstallerExit.BadArguments : InstallerExit.NoInstallation, arguments.Init ? "--init needs --install-root <folder>." :
                    "No installation is registered on this server. Name the folder with --install-root <folder>.");
            layout = new InstallLayout(root);
            // No installation in a folder that does not exist or is empty: this is the first installation of this server, so the installer makes everything (--init is implied). A folder
            // with other things in it is never turned into an installation by accident.
            if (arguments.Mode == InstallerMode.Install && !File.Exists(layout.SettingsFile) && IsMissingOrEmpty(layout.Root)) arguments = arguments with { Mode = InstallerMode.Init };
            if (!arguments.Init && !File.Exists(layout.SettingsFile))
                return Fail(InstallerExit.NoInstallation, $"{layout.Root} is not an installation made by the updater. To make one there run this installer with --init --install-root <folder>.");
            host.Info($"Installation: {layout.Root}. Running version: {layout.Current ?? "none"}; previous: {layout.Previous ?? "none"}.");
        }

        if (arguments.Mode == InstallerMode.Uninstall && arguments.RemoveData && arguments.BackupTo is null && !arguments.Yes && host.Interactive)
        {
            host.Info("Save a restore package (the database and the files of the application) before everything is deleted? Type the folder to save it in (outside the installation), or press Enter for none:");
            if (host.ReadLine()?.Trim().Trim('"') is { Length: > 0 } folder) arguments = arguments with { BackupTo = folder };
        }
        if (arguments.Mode is InstallerMode.Install or InstallerMode.Repair or InstallerMode.Rollback or InstallerMode.Init or InstallerMode.Uninstall && !Confirm(arguments, info, layout!)) return Fail(InstallerExit.Cancelled, "Cancelled; nothing was changed.");

        // The first administrator account of the application is asked for now, before anything is made, so the installation then runs without waiting for anyone.
        AdministratorAccount? administrator = null;
        if (arguments.Init && !arguments.NoInstall && !arguments.NoAdmin)
        {
            administrator = ObtainAdministrator(arguments, out var problem);
            if (administrator is null) return Fail(InstallerExit.NoAdministrator, problem);
        }

        var work = host.CreateWorkFolder();
        try
        {
            var updaterFolder = Path.Combine(work, "updater");
            await payload.ExtractUpdaterAsync(updaterFolder, cancellationToken).ConfigureAwait(false);
            var package = Path.Combine(work, InstallerPayload.PackageEntry);
            if (arguments.Mode is InstallerMode.Install or InstallerMode.Repair or InstallerMode.Check || arguments.Init && !arguments.NoInstall) await payload.ExtractPackageAsync(package, cancellationToken).ConfigureAwait(false);
            var updaterDll = Path.Combine(updaterFolder, "BlazorStoc.Updater.dll");

            if (arguments.Mode == InstallerMode.Uninstall)
            {
                var uninstall = new List<string> { "uninstall", "--install-root", layout!.Root };
                if (arguments.RemoveData) uninstall.Add("--remove-data");
                Add(uninstall, "--backup-to", arguments.BackupTo);
                var removed = await host.RunUpdaterAsync(dotnet, updaterDll, uninstall, cancellationToken).ConfigureAwait(false);
                host.Info(removed == 0 ? "Done: BlazorStoc is removed from this server." : $"The removal stopped with code {removed} (see above); what is left can be removed by hand.");
                return removed;
            }
            if (arguments.Mode == InstallerMode.Check)
            {
                var verify = new List<string> { "verify", package };
                if (arguments.Trusted is not null) verify.AddRange(["--trusted", arguments.Trusted]);
                return await host.RunUpdaterAsync(dotnet, updaterDll, verify, cancellationToken).ConfigureAwait(false);
            }

            if (arguments.Init)
            {
                // The installation, the free ports, the database server and the supervisor (the updater's "init"), then the version of this file the way an update installs it.
                var initialised = await host.RunUpdaterAsync(dotnet, updaterDll, InitArguments(arguments, layout!), cancellationToken).ConfigureAwait(false);
                if (initialised != 0) return initialised;
                if (arguments.NoInstall) { NextSteps(layout!, info); return 0; }
                // The first installation trusts the signer of the package inside this file (the updater verifies the package and adds that signer): the file is what the administrator
                // brought and compared (SHA-256 printed by release.ps1), so nobody types a fingerprint. A later package signed by another key is still refused until it is confirmed.
                var commands = CommandArguments(arguments, layout!, package);
                if (arguments.Trust is null) commands.Add("--trust-package-signer");
                var first = await host.RunUpdaterAsync(dotnet, updaterDll, commands, cancellationToken).ConfigureAwait(false);
                Explain(InstallerMode.Install, first, info);
                if (first != 0) return first;
                if (administrator is not null) await MakeAdministratorAsync(dotnet, updaterDll, layout!, administrator, cancellationToken).ConfigureAwait(false);
                host.Info($"The installation is complete: {layout!.Root}. The ports are in {layout.SettingsFile}; the application answers on the port of the application written there.");
                return first;
            }
            var code = await host.RunUpdaterAsync(dotnet, updaterDll, CommandArguments(arguments, layout!, package), cancellationToken).ConfigureAwait(false);
            if (code == 0 && arguments.Mode != InstallerMode.Status) await RefreshUpdaterAsync(dotnet, updaterDll, layout!, cancellationToken).ConfigureAwait(false);
            Explain(arguments.Mode, code, info);
            return code;
        }
        catch (InvalidDataException exception) { return Fail(InstallerExit.DamagedPayload, exception.Message); }
        finally { host.DeleteWorkFolder(work); }
    }

    private static bool IsMissingOrEmpty(string folder) => !Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any();

    private static readonly int[] InstallerSuccessCodes = [0, 1638, 1641, 3010];

    /// <summary>
    /// Installs from the installers inside this file what the machine lacks: the .NET 9 runtime and the ASP.NET Core 9 runtime (when dotnet is missing or one of them is), the Visual C++
    /// runtime (when it is not registered). Each installer is checked for the Microsoft signature before it is run, and run silently without a restart. Returns an exit code when something
    /// failed, null when everything is there.
    /// </summary>
    private async Task<int?> InstallPrerequisitesAsync(InstallerPayload payload, InstallerArguments arguments, CancellationToken cancellationToken)
    {
        var dotnet = host.FindDotnet(arguments.Dotnet);
        var runtimeMissing = dotnet is null || await host.RuntimeProblemAsync(dotnet, cancellationToken).ConfigureAwait(false) is not null;
        var vcMissing = !host.VisualCppRuntimeInstalled;
        if (!runtimeMissing && !vcMissing) return null;
        var files = payload.PrerequisiteFiles;
        var runtimes = files.Where(name => name.StartsWith("dotnet-runtime-", StringComparison.OrdinalIgnoreCase)).Concat(files.Where(name => name.StartsWith("aspnetcore-runtime-", StringComparison.OrdinalIgnoreCase))).ToArray();
        var vc = files.Where(name => name.StartsWith("vc_redist", StringComparison.OrdinalIgnoreCase)).ToArray();
        var wanted = (runtimeMissing ? runtimes : []).Concat(vcMissing ? vc : []).ToArray();
        if (wanted.Length == 0) return null;   // nothing inside this file for it: the check that follows says what is missing

        var folder = host.CreateWorkFolder();
        try
        {
            await payload.ExtractPrerequisitesAsync(folder, cancellationToken).ConfigureAwait(false);
            foreach (var name in wanted)
            {
                var file = Path.Combine(folder, name);
                if (!host.IsSignedByMicrosoft(file)) return Fail(InstallerExit.PrerequisiteFailed, $"{name} inside this file is not signed by Microsoft: it is NOT run. The installer file is not the one release.ps1 made (compare its SHA-256).");
                host.Info($"Installing {name} (needed by the application; this takes a minute) ...");
                var code = await host.RunPrerequisiteAsync(file, ["/install", "/quiet", "/norestart"], cancellationToken).ConfigureAwait(false);
                if (!InstallerSuccessCodes.Contains(code)) return Fail(InstallerExit.PrerequisiteFailed, $"{name} ended with code {code}: install it by hand (Windows Update must be able to finish; a restart may be pending) and run the installer again.");
                if (code == 3010) host.Info($"{name} asks for a restart of the computer at some point; the installation goes on.");
            }
            host.Info("The prerequisites are installed.");
            return null;
        }
        finally { host.DeleteWorkFolder(folder); }
    }

    // From --admin-user (+ BLAZORSTOC_ADMIN_PASSWORD, for a console nobody sits at) or from the questions at the console.
    private AdministratorAccount? ObtainAdministrator(InstallerArguments arguments, out string problem)
    {
        problem = "";
        if (arguments.AdminUser is { } username)
        {
            var password = Environment.GetEnvironmentVariable("BLAZORSTOC_ADMIN_PASSWORD");
            problem = AdministratorRules.UsernameProblem(username) ?? (password is null ? "The password of --admin-user is given in the environment variable BLAZORSTOC_ADMIN_PASSWORD (not on the command line)." : AdministratorRules.PasswordProblem(password)) ?? "";
            return problem.Length > 0 ? null : new AdministratorAccount(username.Trim(), arguments.AdminName?.Trim() is { Length: > 0 } name ? name : username.Trim(), password!);
        }
        if (host.Interactive) return host.AskAdministrator();
        problem = "The administrator account of the application needs a user name and a password: run the installer at a console, or give --admin-user <name> with the password in BLAZORSTOC_ADMIN_PASSWORD, or --no-admin to make no account (the sign-in \"admin\" of the configuration remains).";
        return null;
    }

    private async Task MakeAdministratorAsync(string dotnet, string updaterDll, InstallLayout layout, AdministratorAccount account, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string> { ["BLAZORSTOC_ADMIN_USER"] = account.Username, ["BLAZORSTOC_ADMIN_NAME"] = account.DisplayName, ["BLAZORSTOC_ADMIN_PASSWORD"] = account.Password };
        var code = await host.RunUpdaterAsync(dotnet, updaterDll, ["create-admin", "--install-root", layout.Root], cancellationToken, environment).ConfigureAwait(false);
        host.Info(code switch
        {
            0 => $"Administrator account \"{account.Username}\" made in the users of the application: sign in with it and the password you typed.",
            4 => $"A user named \"{account.Username}\" exists already in the application: it was kept as it is, its password is NOT the one typed now.",
            _ => $"The version is installed, but the administrator account could not be made (exit code {code}, see above). Sign in with the user \"admin\" of the configuration (its password is in application-connection.private.json, see above) and make the account in Utilizatori."
        });
    }

    private bool Confirm(InstallerArguments arguments, InstallerInfo info, InstallLayout layout)
    {
        var what = arguments.Mode switch
        {
            InstallerMode.Repair => $"Reinstall version {info.Version} over {layout.Current ?? "(nothing)"} in {layout.Root}",
            InstallerMode.Rollback => layout.Previous is { } previous ? $"Go back from {layout.Current ?? "(nothing)"} to {previous} in {layout.Root}" : $"Go back to the previous version in {layout.Root} (none is recorded)",
            InstallerMode.Uninstall => arguments.RemoveData ? $"REMOVE BlazorStoc from {layout.Root} AND DELETE ITS DATA (the database and the files of the application)"
                : $"Remove BlazorStoc from {layout.Root} (the application, its supervisor and tasks, the firewall rule, the database server service); the data are kept",
            InstallerMode.Init => arguments.NoInstall ? $"Prepare the installation in {layout.Root} (folders, protection, ports, database server, supervisor), without installing a version"
                : $"Install everything in {layout.Root}: folders, protection, free ports, the database server (Windows service), the supervisor and version {info.Version}",
            _ => $"Install version {info.Version} over {layout.Current ?? "(nothing)"} in {layout.Root}"
        };
        host.Info(what + arguments.Mode switch
        {
            InstallerMode.Uninstall => ".",
            InstallerMode.Init => arguments.NoInstall ? "." : ". The database server is downloaded when it is not on this machine (about 100 MB), unless --maria-zip or --maria-bin say otherwise.",
            InstallerMode.Rollback => ". The application is stopped for a few minutes" + (arguments.RestoreDb is { } backup ? $" and the database is restored from {backup}." : "."),
            _ => ". If the package is accepted, the application is stopped for a few minutes and the database is backed up."
        });
        if (arguments.Yes) return true;
        if (arguments.Mode == InstallerMode.Uninstall && arguments.RemoveData && host.Interactive)
        {
            host.Info("This cannot be undone. Type STERGE and Enter to delete the data too, anything else cancels.");
            return string.Equals(host.ReadLine()?.Trim(), "STERGE", StringComparison.Ordinal);
        }
        if (!host.Interactive)
        {
            host.Error("Nobody can be asked at this console: add --yes to go on.");
            return false;
        }
        host.Info("Press Enter to go on, or type n and Enter to cancel.");
        var answer = host.ReadLine()?.Trim();
        return answer is not null && !answer.StartsWith('n') && !answer.StartsWith('N');
    }

    private void NextSteps(InstallLayout layout, InstallerInfo info)
    {
        host.Info($"""

            The installation is ready in {layout.Root} (ports, database server and supervisor are made; see updater/settings.json). No version is installed yet. Run this installer again,
            without --init, to install version {info.Version}. The first time it will ask you to confirm the signer: compare the fingerprint with the one release.ps1 printed on the build
            machine (or give it with --trust <fingerprint>).
            """);
    }

    private static List<string> InitArguments(InstallerArguments arguments, InstallLayout layout)
    {
        var list = new List<string> { "init", "--install-root", layout.Root };
        Add(list, "--port", arguments.Port); Add(list, "--db-port", arguments.DbPort); Add(list, "--maria-root", arguments.MariaRoot); Add(list, "--maria-bin", arguments.MariaBin);
        Add(list, "--maria-zip", arguments.MariaZip); Add(list, "--migration-account-file", arguments.MigrationAccountFile); Add(list, "--env", arguments.Env);
        if (arguments.NoTask) list.Add("--no-task");
        if (arguments.NoDatabase) list.Add("--no-database");
        if (arguments.NoService) list.Add("--no-service");
        if (arguments.NoSupervisor) list.Add("--no-supervisor");
        if (arguments.NoFirewall) list.Add("--no-firewall");
        return list;
    }

    private static List<string> CommandArguments(InstallerArguments arguments, InstallLayout layout, string package)
    {
        var list = new List<string>();
        switch (arguments.Mode)
        {
            case InstallerMode.Status:
                list.AddRange(["status", "--install-root", layout.Root]);
                break;
            case InstallerMode.Rollback:
                list.AddRange(["rollback", "--install-root", layout.Root, "--reason", arguments.Reason ?? "rollback with the installer"]);
                Add(list, "--restore-db", arguments.RestoreDb);
                break;
            default:
                list.AddRange(["install", package, "--install-root", layout.Root, "--reason", arguments.Reason ?? (arguments.Mode == InstallerMode.Repair ? "repair with the installer" : arguments.Init ? "first installation" : "installer")]);
                Add(list, "--trust", arguments.Trust);
                if (arguments.Mode == InstallerMode.Repair) list.Add("--reinstall");
                if (arguments.AllowDowngrade) list.Add("--allow-downgrade");
                break;
        }
        if (arguments.Mode != InstallerMode.Status) Add(list, "--health-timeout", arguments.HealthTimeout);
        return list;
    }

    private async Task RefreshUpdaterAsync(string dotnet, string updaterDll, InstallLayout layout, CancellationToken cancellationToken)
    {
        var code = await host.RunUpdaterAsync(dotnet, updaterDll, ["refresh", "--install-root", layout.Root], cancellationToken).ConfigureAwait(false);
        if (code != 0) host.Info($"The version is installed, but the updater of the installation ({layout.UpdaterFolder}) could not be replaced by the one of this installer (is the scheduled task running it?). Run this installer again with --status when nothing is running.");
    }

    private void Explain(InstallerMode mode, int code, InstallerInfo info)
    {
        if (mode == InstallerMode.Status) return;
        host.Info(code switch
        {
            0 => mode == InstallerMode.Rollback ? "Done: the previous version is running." : $"Done: BlazorStoc {info.Version} is installed and answering.",
            2 => "The package is not valid; nothing was changed.",
            3 => "The package is valid but its signer is not trusted by this server; nothing was changed. Run again with --trust <fingerprint> after comparing it with the one printed by release.ps1.",
            4 => "Refused; nothing was changed (the reason is above).",
            5 => "The new version did not start; the previous version was put back and answers. Read the log in logs\\updater-<date>.log of the installation.",
            6 => "The update failed and the application may be down. Read logs\\updater-<date>.log of the installation, then run this installer with --rollback or --repair.",
            _ => $"The updater stopped with code {code} (see above)."
        });
    }

    private int Fail(int code, string message)
    {
        host.Error(message);
        return code;
    }

    private static void Add(List<string> list, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) list.AddRange([name, value]);
    }
}
