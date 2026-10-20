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
    void Info(string message);
    void Error(string message);
    string? ReadLine();
    /// <summary>Starts this installer again with administrator rights (Windows asks) and returns its exit code; null when that was refused or failed.</summary>
    int? Elevate(IReadOnlyList<string> arguments);
    /// <summary>The dotnet that runs the updater (the one asked for, else the one Windows has), null when there is none.</summary>
    string? FindDotnet(string? requested);
    /// <summary>Null when the .NET runtime the updater needs is there, else what is missing.</summary>
    Task<string?> RuntimeProblemAsync(string dotnet, CancellationToken cancellationToken);
    /// <summary>A new folder only administrators can use, for the updater and the package taken out of the payload.</summary>
    string CreateWorkFolder();
    void DeleteWorkFolder(string folder);
    /// <summary>Runs "dotnet &lt;updater dll&gt; &lt;arguments&gt;" with the console of the installer (its output and its questions are the ones of the person at the console).</summary>
    Task<int> RunUpdaterAsync(string dotnet, string updaterDll, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
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

        var dotnet = host.FindDotnet(arguments.Dotnet);
        if (dotnet is null) return Fail(InstallerExit.NoDotnet, "dotnet was not found on this machine (the .NET 9 runtime the application needs also runs this updater). Install it, or give the path with --dotnet.");
        if (await host.RuntimeProblemAsync(dotnet, cancellationToken).ConfigureAwait(false) is { } runtimeProblem) return Fail(InstallerExit.NoDotnet, runtimeProblem);

        InstallLayout? layout = null;
        if (arguments.Mode != InstallerMode.Check)
        {
            var root = arguments.InstallRoot ?? host.RegisteredInstallRoot;
            if (root is null)
                return Fail(arguments.Init ? InstallerExit.BadArguments : InstallerExit.NoInstallation, arguments.Init ? "--init needs --install-root <folder>." :
                    "No installation is registered on this server. Name the folder with --install-root <folder>, or, on a new server, add --init (see --help).");
            layout = new InstallLayout(root);
            if (!arguments.Init && !File.Exists(layout.SettingsFile))
                return Fail(InstallerExit.NoInstallation, $"{layout.Root} is not an installation made by the updater. On a new server run this installer with --init --install-root <folder>.");
            host.Info($"Installation: {layout.Root}. Running version: {layout.Current ?? "none"}; previous: {layout.Previous ?? "none"}.");
        }

        if (arguments.Mode is InstallerMode.Install or InstallerMode.Repair or InstallerMode.Rollback or InstallerMode.Init && !Confirm(arguments, info, layout!)) return Fail(InstallerExit.Cancelled, "Cancelled; nothing was changed.");

        var work = host.CreateWorkFolder();
        try
        {
            var updaterFolder = Path.Combine(work, "updater");
            await payload.ExtractUpdaterAsync(updaterFolder, cancellationToken).ConfigureAwait(false);
            var package = Path.Combine(work, InstallerPayload.PackageEntry);
            if (arguments.Mode is InstallerMode.Install or InstallerMode.Repair or InstallerMode.Check) await payload.ExtractPackageAsync(package, cancellationToken).ConfigureAwait(false);
            var updaterDll = Path.Combine(updaterFolder, "BlazorStoc.Updater.dll");

            if (arguments.Mode == InstallerMode.Check)
            {
                var verify = new List<string> { "verify", package };
                if (arguments.Trusted is not null) verify.AddRange(["--trusted", arguments.Trusted]);
                return await host.RunUpdaterAsync(dotnet, updaterDll, verify, cancellationToken).ConfigureAwait(false);
            }

            if (arguments.Init)
            {
                var initialised = await host.RunUpdaterAsync(dotnet, updaterDll, InitArguments(arguments, layout!), cancellationToken).ConfigureAwait(false);
                if (initialised == 0) NextSteps(arguments, layout!, info);
                return initialised;
            }
            var code = await host.RunUpdaterAsync(dotnet, updaterDll, CommandArguments(arguments, layout!, package), cancellationToken).ConfigureAwait(false);
            if (code == 0 && arguments.Mode != InstallerMode.Status) await RefreshUpdaterAsync(dotnet, updaterDll, layout!, cancellationToken).ConfigureAwait(false);
            Explain(arguments.Mode, code, info);
            return code;
        }
        catch (InvalidDataException exception) { return Fail(InstallerExit.DamagedPayload, exception.Message); }
        finally { host.DeleteWorkFolder(work); }
    }

    private bool Confirm(InstallerArguments arguments, InstallerInfo info, InstallLayout layout)
    {
        var what = arguments.Mode switch
        {
            InstallerMode.Repair => $"Reinstall version {info.Version} over {layout.Current ?? "(nothing)"} in {layout.Root}",
            InstallerMode.Rollback => layout.Previous is { } previous ? $"Go back from {layout.Current ?? "(nothing)"} to {previous} in {layout.Root}" : $"Go back to the previous version in {layout.Root} (none is recorded)",
            InstallerMode.Init => $"Make the installation in {layout.Root} (folders, protection, the scheduled task of the updater)",
            _ => $"Install version {info.Version} over {layout.Current ?? "(nothing)"} in {layout.Root}"
        };
        host.Info(what + arguments.Mode switch
        {
            InstallerMode.Init => ".",
            InstallerMode.Rollback => ". The application is stopped for a few minutes" + (arguments.RestoreDb is { } backup ? $" and the database is restored from {backup}." : "."),
            _ => ". If the package is accepted, the application is stopped for a few minutes and the database is backed up."
        });
        if (arguments.Yes) return true;
        if (!host.Interactive)
        {
            host.Error("Nobody can be asked at this console: add --yes to go on.");
            return false;
        }
        host.Info("Press Enter to go on, or type n and Enter to cancel.");
        var answer = host.ReadLine()?.Trim();
        return answer is not null && !answer.StartsWith('n') && !answer.StartsWith('N');
    }

    private void NextSteps(InstallerArguments arguments, InstallLayout layout, InstallerInfo info)
    {
        host.Info($"""

            The installation is ready in {layout.Root}. Next, on this server:
              1. Register the supervisor of the application, signed in as the account the application runs as (an administrator console):
                   powershell -ExecutionPolicy Bypass -File "{Path.Combine(layout.ScriptsFolder, "supervisor.ps1")}" -Action install -Port {arguments.Port ?? "5087"} -InstallRoot "{layout.Root}"
              2. Run this installer again, without --init, to install version {info.Version}. The first time it will ask you to confirm the signer: compare the fingerprint with
                 the one release.ps1 printed on the build machine (or give it with --trust <fingerprint>).
            """);
    }

    private static List<string> InitArguments(InstallerArguments arguments, InstallLayout layout)
    {
        var list = new List<string> { "init", "--install-root", layout.Root };
        Add(list, "--port", arguments.Port); Add(list, "--maria-root", arguments.MariaRoot); Add(list, "--migration-account-file", arguments.MigrationAccountFile); Add(list, "--env", arguments.Env);
        if (arguments.NoTask) list.Add("--no-task");
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
                list.AddRange(["install", package, "--install-root", layout.Root, "--reason", arguments.Reason ?? (arguments.Mode == InstallerMode.Repair ? "repair with the installer" : "installer")]);
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
