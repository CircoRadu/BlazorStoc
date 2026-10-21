using System.Diagnostics;

namespace BlazorStoc.Updater;

/// <summary>Runs a PowerShell script of the installation with the console of this process; the checks give a fake that only records the call.</summary>
public interface IScriptRunner
{
    /// <summary>Returns the exit code of the script.</summary>
    Task<int> RunAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class PowerShellScriptRunner : IScriptRunner
{
    public async Task<int> RunAsync(string script, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // No redirection: what the script says is seen at the console of the installer.
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("powershell.exe could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}

/// <summary>
/// What a first installation makes besides the folders (docs/OPERARE_ACTUALIZARE.md, section 3): the ports, the database server with its Windows service, accounts and schema
/// (scripts/provision-database.ps1) and the supervisor that keeps the application running (scripts/supervisor.ps1). The ports are decided here, by <see cref="PortSelector"/>, and
/// written to updater/settings.json; the scripts get the ports that were chosen.
/// </summary>
public sealed class ServerProvisioner(InstallLayout layout, IScriptRunner scripts, TextWriter output)
{
    public const string DefaultServiceName = "BlazorStocMariaDB";
    public const int DefaultApplicationPort = 5087;
    public const int DefaultDatabasePort = 3307;

    /// <summary>The ports for a new installation: the initial ones when they are free on this server, else the next free ones (the two are never the same). Written down in the output.</summary>
    public (PortChoice Application, PortChoice? Database) ChoosePorts(int applicationPort, int databasePort, bool withDatabase, IPortProbe probe)
    {
        var application = PortSelector.Choose(applicationPort, [], probe);
        var database = withDatabase ? PortSelector.Choose(databasePort, [application.Chosen], probe) : null;
        output.WriteLine(application.Moved ? $"Application port: {application.Requested} is taken on this server, {application.Chosen} is free and will be used." : $"Application port: {application.Chosen} (free).");
        if (database is not null)
            output.WriteLine(database.Moved ? $"Database port: {database.Requested} is taken on this server, {database.Chosen} is free and will be used." : $"Database port: {database.Chosen} (free).");
        return (application, database);
    }

    /// <summary>Makes the database server (MariaDB distribution, Windows service, accounts, schema, private files) on the port in the settings.</summary>
    public async Task<int> ProvisionDatabaseAsync(UpdaterSettings settings, string? mariaBin, string? mariaZip, bool withoutService, CancellationToken cancellationToken = default)
    {
        var port = settings.DatabasePort ?? throw new InvalidOperationException("The settings carry no database port.");
        var script = Path.Combine(layout.ScriptsFolder, "provision-database.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException($"{script} is missing (run \"init\" again).");
        var arguments = new List<string>
        {
            "-Root", settings.MariaRoot ?? Path.Combine(layout.Root, "mariadb"), "-Port", port.ToString(), "-DataRoot", UpdaterSettings.DataRoot(),
            "-MigratorFile", layout.MigrationAccountFile, "-ResourcesDir", layout.ScriptsFolder, "-ServiceName", settings.MariaServiceName ?? DefaultServiceName
        };
        if (!string.IsNullOrWhiteSpace(mariaBin)) arguments.AddRange(["-MariaBinDir", mariaBin]);
        if (!string.IsNullOrWhiteSpace(mariaZip)) arguments.AddRange(["-MariaZip", mariaZip]);
        if (withoutService) arguments.Add("-NoService");
        return await scripts.RunAsync(script, arguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens the port of the application in the firewall for the computers of the local network (scripts/firewall.ps1).</summary>
    public async Task<int> OpenFirewallAsync(UpdaterSettings settings, CancellationToken cancellationToken = default)
    {
        var script = Path.Combine(layout.ScriptsFolder, "firewall.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException($"{script} is missing (run \"init\" again).");
        return await scripts.RunAsync(script, ["-Action", "add", "-Port", settings.Port.ToString()], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers the supervisor of the application (scheduled task: at logon and every 5 minutes) on the port in the settings and starts it. The version it runs is the one in current.txt.</summary>
    public async Task<int> RegisterSupervisorAsync(UpdaterSettings settings, CancellationToken cancellationToken = default)
    {
        var script = Path.Combine(layout.ScriptsFolder, "supervisor.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException($"{script} is missing (run \"init\" again).");
        return await scripts.RunAsync(script, ["-Action", "install", "-Port", settings.Port.ToString(), "-InstallRoot", layout.Root], cancellationToken).ConfigureAwait(false);
    }
}
