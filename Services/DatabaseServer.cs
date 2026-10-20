using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MySqlConnector;

namespace BlazorStoc.Services;

// Setari -> Baza de date -> Conexiune: the server of the database itself (the Windows service of this installation): start, stop, restart, the port it listens on, and
// the application that is restarted to take another port. Nothing here takes a service name typed by the user: only the one in Database:ServerServiceName
// (default BlazorStocMariaDB, installed by tools\install-mariadb-service.ps1) is controlled, and only when it is on this computer and is the server the application uses.

public enum ServerServiceStatus { NotFound, Stopped, Starting, Stopping, Running, Other }

public sealed record PortOccupant(int ProcessId, string? ProcessName)
{
    public override string ToString() => ProcessName is { Length: > 0 } name ? $"{name} (PID {ProcessId})" : $"un proces (PID {ProcessId})";
}

public interface IServerServices
{
    ServerServiceStatus Status(string name);
    /// <summary>The command line of the service as registered (readable without special rights), or null when there is no such service.</summary>
    string? ImagePath(string name);
    Task StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken);
    Task StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IPortInspector
{
    /// <summary>The process that listens on the TCP port on this computer, or null when nothing does.</summary>
    PortOccupant? Occupant(int port);
    bool IsFree(int port);
    /// <summary>A free port at or above <paramref name="start"/> (and not in <paramref name="avoid"/>), or null.</summary>
    int? FindFree(int start, params int[] avoid);
}

public interface IApplicationRestart
{
    /// <summary>True when something brings the application back after it exits with a failure code (the supervisor of the preview, later the Windows service).</summary>
    bool Supported { get; }
    void Request(string reason);
}

public sealed record DatabaseServerState(string ServiceName, ServerServiceStatus Service, bool Local, string? IniPath, int? IniPort, bool CanControl, string? Reason,
    bool Reachable, PortOccupant? Occupant, string? PortProblem, int? SuggestedPort, bool ApplicationRestartSupported, int ConnectionPort);

public static class DatabaseServerRules
{
    public const int MinimumPort = 1024;
    public const int MaximumPort = 65535;
    public const string DefaultServiceName = "BlazorStocMariaDB";
    public const string StopWord = "OPRESTE BAZA";
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    public const string LockHeldMessage = DatabaseConnectionRules.LockHeldMessage;
    public const string NotControllableMessage = "Serverul bazei de date nu poate fi controlat din aplicație; vezi motivul din pagină.";

    public static string? ValidatePort(int port, int currentPort, int? applicationPort, IPortInspector ports)
    {
        if (port < MinimumPort || port > MaximumPort) return $"Portul serverului trebuie să fie între {MinimumPort} și {MaximumPort}.";
        if (port == currentPort) return "Serverul ascultă deja pe acest port.";
        if (port == applicationPort) return $"Portul {port} este portul aplicației; alege altul.";
        if (ports.Occupant(port) is { } occupant) return $"Portul {port} este ocupat de {occupant}. Alege alt port.";
        return ports.IsFree(port) ? null : $"Portul {port} este ocupat pe acest calculator. Alege alt port.";
    }

    public static string StateText(ServerServiceStatus status) => status switch
    {
        ServerServiceStatus.Running => "pornit",
        ServerServiceStatus.Stopped => "oprit",
        ServerServiceStatus.Starting => "pornește",
        ServerServiceStatus.Stopping => "se oprește",
        ServerServiceStatus.NotFound => "serviciu negăsit",
        _ => "stare necunoscută",
    };

    /// <summary>Why the server cannot be controlled from the page, or null when it can. The three conditions of the proposal: the host is this computer, the service exists, and it
    /// serves the port the application connects to.</summary>
    public static string? WhyNotControllable(bool local, string host, string serviceName, ServerServiceStatus service, int? iniPort, int connectionPort) =>
        !local ? $"Serverul bazei de date este pe alt calculator ({host}). Pornirea, oprirea și portul se schimbă pe acel calculator; apoi pune aici, în conexiune, portul nou."
        : service == ServerServiceStatus.NotFound ? $"Pe acest calculator nu există serviciul Windows „{serviceName}”. Instalează-l cu tools\\install-mariadb-service.ps1 (sau pune în Database:ServerServiceName numele serviciului baza de date) ca să poți porni, opri și schimba portul de aici."
        : iniPort is null ? $"Serviciul „{serviceName}” nu indică un fișier de configurare al serverului (--defaults-file), deci portul lui nu se poate citi."
        : iniPort != connectionPort ? $"Serviciul „{serviceName}” folosește portul {iniPort}, dar aplicația se conectează la {connectionPort}: este alt server decât al serviciului. Corectează portul din conexiune."
        : null;
}

/// <summary>The lines of my.ini that matter here, read and changed without touching anything else in the file.</summary>
public static class MyIni
{
    private static readonly Regex PortLine = new(@"^\s*port\s*=\s*(\d+)\s*(?:[#;].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool InServerSection(string section) => string.Equals(section, "mysqld", StringComparison.OrdinalIgnoreCase) || string.Equals(section, "mariadbd", StringComparison.OrdinalIgnoreCase);

    public static int? ReadPort(string content)
    {
        var section = "";
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            if (InServerSection(section) && PortLine.Match(line) is { Success: true } match && int.TryParse(match.Groups[1].Value, out var port)) return port;
        }
        return null;
    }

    public static string? ReadValue(string content, string key)
    {
        var section = "";
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            var separator = line.IndexOf('=');
            if (InServerSection(section) && separator > 0 && string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase)) return line[(separator + 1)..].Trim();
        }
        return null;
    }

    /// <summary>The content with the port of the server section replaced (added under its header when there is none); the line ending of the file is kept.</summary>
    public static string WithPort(string content, int port)
    {
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var section = "";
        var headerAt = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); if (InServerSection(section) && headerAt < 0) headerAt = index; continue; }
            if (InServerSection(section) && PortLine.IsMatch(line)) { lines[index] = $"port={port}"; return string.Join(newline, lines); }
        }
        if (headerAt < 0) { lines.Add("[mysqld]"); headerAt = lines.Count - 1; }
        lines.Insert(headerAt + 1, $"port={port}");
        return string.Join(newline, lines);
    }

    /// <summary>The my.ini named by the command line of the service (<c>"--defaults-file=C:\...\my.ini"</c>), or null.</summary>
    public static string? DefaultsFile(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        // The three ways a path with spaces is written: the whole argument in quotes (what mariadbd --install registers), the value in quotes, or no quotes at all.
        var match = Regex.Match(imagePath, "\"--defaults-file=([^\"]+)\"|--defaults-file=\"([^\"]+)\"|--defaults-file=(\\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value : null;
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsServerServices : IServerServices
{
    public ServerServiceStatus Status(string name)
    {
        try
        {
            using var controller = new ServiceController(name);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServerServiceStatus.Running,
                ServiceControllerStatus.Stopped => ServerServiceStatus.Stopped,
                ServiceControllerStatus.StartPending => ServerServiceStatus.Starting,
                ServiceControllerStatus.StopPending => ServerServiceStatus.Stopping,
                _ => ServerServiceStatus.Other,
            };
        }
        catch (InvalidOperationException) { return ServerServiceStatus.NotFound; }
    }

    public string? ImagePath(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
        return key?.GetValue("ImagePath") as string;
    }

    public Task StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var controller = new ServiceController(name);
        if (controller.Status == ServiceControllerStatus.Running) return;
        if (controller.Status == ServiceControllerStatus.Stopped) controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }, cancellationToken);

    public Task StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var controller = new ServiceController(name);
        if (controller.Status == ServiceControllerStatus.Stopped) return;
        if (controller.Status == ServiceControllerStatus.Running) controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }, cancellationToken);
}

/// <summary>Used where there is no Windows (tests, other systems): no service is found, so the page explains why nothing can be controlled.</summary>
public sealed class NoServerServices : IServerServices
{
    public ServerServiceStatus Status(string name) => ServerServiceStatus.NotFound;
    public string? ImagePath(string name) => null;
    public Task StartAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) => throw new PlatformNotSupportedException();
    public Task StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken) => throw new PlatformNotSupportedException();
}

public sealed partial class NetstatPortInspector : IPortInspector
{
    [GeneratedRegex(@"^\s*TCP\s+\S+:(\d+)\s+\S+:0\s+\S+\s+(\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ListenerLine();

    public PortOccupant? Occupant(int port)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netstat.exe", "-ano -p tcp") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            foreach (var line in output.Split('\n'))
            {
                var match = ListenerLine().Match(line.TrimEnd('\r'));
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var listening) || listening != port) continue;
                var pid = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                string? name = null;
                try { name = Process.GetProcessById(pid).ProcessName; } catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return new PortOccupant(pid, name);
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { }
        return null;
    }

    public bool IsFree(int port) => !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

    public int? FindFree(int start, params int[] avoid)
    {
        for (var port = Math.Max(start, DatabaseServerRules.MinimumPort); port <= DatabaseServerRules.MaximumPort && port < start + 200; port++)
            if (!avoid.Contains(port) && IsFree(port)) return port;
        return null;
    }
}

/// <summary>The restart of the application: it exits with a failure code, which the supervisor (preview) or the recovery actions of the Windows service answer with a start.</summary>
public sealed class ApplicationRestart(IHostApplicationLifetime lifetime, IConfiguration configuration, ILogger<ApplicationRestart> logger) : IApplicationRestart
{
    public const int ExitCode = 75;

    public bool Supported => !string.IsNullOrWhiteSpace(configuration["App:Supervisor"]);

    public void Request(string reason)
    {
        logger.LogWarning("The application restarts: {Reason}", reason);
        // A short delay so the page that asked gets its answer first.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
            // The host stops in order (requests finish, services stop) and only then the process ends with the code that asks the supervisor for a restart.
            lifetime.ApplicationStopped.Register(() => Environment.Exit(ExitCode));
            lifetime.StopApplication();
        });
    }
}

public interface IDatabaseServerService
{
    Task<DatabaseServerState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> StopAsync(string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> StartAsync(string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> RestartAsync(string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> ChangePortAsync(int port, string administratorPassword, CancellationToken cancellationToken = default);
    Task<DatabaseSaveResult> RestartApplicationAsync(string administratorPassword, CancellationToken cancellationToken = default);
}

public sealed class DatabaseServerService(IConfiguration configuration, IAccessControl access, IAuditTrail audit, IOperationLockService locks, IDatabaseSettingsService settings,
    IServerServices services, IPortInspector ports, IApplicationRestart restart, ILogger<DatabaseServerService> logger, IUserAuthenticator? authenticator = null,
    Func<string, int, CancellationToken, Task<string?>>? verify = null, Func<string, bool>? isLocalHost = null, TimeSpan? readyTimeout = null,
    Func<string, int, CancellationToken, Task<bool>>? reach = null) : IDatabaseServerService
{
    private readonly Func<string, int, CancellationToken, Task<bool>> canConnect = reach ?? CanConnectAsync;
    private readonly Func<string, int, CancellationToken, Task<string?>> verifyConnection = verify ?? VerifyAsync(configuration);
    private readonly Func<string, bool> localHost = isLocalHost ?? IsLocalHost;
    private readonly TimeSpan ready = readyTimeout ?? DatabaseServerRules.ReadyTimeout;
    private string ServiceName => configuration["Database:ServerServiceName"] is { Length: > 0 } configured ? configured : DatabaseServerRules.DefaultServiceName;
    private string Host => configuration["Database:Host"] ?? "127.0.0.1";
    private int ConnectionPort => configuration.GetValue("Database:Port", 3307);

    public async Task<DatabaseServerState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var name = ServiceName;
        var local = localHost(Host);
        var service = local ? services.Status(name) : ServerServiceStatus.NotFound;
        var iniPath = local && service != ServerServiceStatus.NotFound ? MyIni.DefaultsFile(services.ImagePath(name)) : null;
        int? iniPort = null;
        if (iniPath is not null && File.Exists(iniPath))
        {
            try { iniPort = MyIni.ReadPort(await File.ReadAllTextAsync(iniPath, cancellationToken).ConfigureAwait(false)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        var reason = DatabaseServerRules.WhyNotControllable(local, Host, name, service, iniPort, ConnectionPort);
        var reachable = await canConnect(Host, ConnectionPort, cancellationToken).ConfigureAwait(false);
        PortOccupant? occupant = null;
        string? problem = null;
        int? suggested = null;
        // A stopped service whose port is held by another program: something answers on the port, but it is not the server, so it does not count as reachable.
        if (local && service is ServerServiceStatus.Stopped or ServerServiceStatus.Other && iniPort is { } port && ports.Occupant(port) is { } found)
        {
            occupant = found;
            reachable = false;
            suggested = ports.FindFree(port + 1, ConnectionPort, configuration.GetValue<int?>("Hosting:AppPort") ?? 0);
            problem = $"Portul {port} este ocupat de {found}, de aceea serverul „{name}” nu poate porni. Schimbă portul serverului{(suggested is { } free ? $" (propus: {free})" : "")} sau închide programul care îl ține.";
        }
        return new(name, service, local, iniPath, iniPort, reason is null, reason, reachable, occupant, problem, suggested, restart.Supported, ConnectionPort);
    }

    public Task<DatabaseSaveResult> StopAsync(string administratorPassword, CancellationToken cancellationToken = default) =>
        RunAsync(AuditActions.StopDatabaseServer, "Oprire server bază de date", administratorPassword, cancellationToken, async state =>
        {
            if (state.Service == ServerServiceStatus.Stopped) return new DatabaseSaveResult(true, "Serverul era deja oprit.");
            await services.StopAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            MySqlConnection.ClearAllPools();
            return new DatabaseSaveResult(true, "Serverul bazei de date a fost oprit. Cât timp e oprit, aplicația nu are date; pornește-l din aceeași pagină.");
        });

    public Task<DatabaseSaveResult> StartAsync(string administratorPassword, CancellationToken cancellationToken = default) =>
        RunAsync(AuditActions.StartDatabaseServer, "Pornire server bază de date", administratorPassword, cancellationToken, async state =>
        {
            if (state.Occupant is { } occupant) throw new DatabaseSettingsException(state.PortProblem ?? $"Portul este ocupat de {occupant}.");
            return await StartAndCheckAsync(state, state.IniPort ?? state.ConnectionPort, "Serverul bazei de date a pornit și răspunde.", cancellationToken).ConfigureAwait(false);
        });

    public Task<DatabaseSaveResult> RestartAsync(string administratorPassword, CancellationToken cancellationToken = default) =>
        RunAsync(AuditActions.RestartDatabaseServer, "Repornire server bază de date", administratorPassword, cancellationToken, async state =>
        {
            if (state.Service != ServerServiceStatus.Stopped) await services.StopAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            MySqlConnection.ClearAllPools();
            return await StartAndCheckAsync(state, state.IniPort ?? state.ConnectionPort, "Serverul bazei de date a fost repornit și răspunde.", cancellationToken).ConfigureAwait(false);
        });

    public async Task<DatabaseSaveResult> ChangePortAsync(int port, string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.CanControl || state.IniPath is null || state.IniPort is not { } oldPort) throw new DatabaseSettingsException(state.Reason ?? DatabaseServerRules.NotControllableMessage);
        if (DatabaseServerRules.ValidatePort(port, oldPort, configuration.GetValue<int?>("Hosting:AppPort"), ports) is { } problem) throw new DatabaseSettingsException(problem);

        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await using var handle = await locks.TryAcquireAsync("Schimbare port server baza de date", actor, AccessRoles.Administrator, cancellationToken).ConfigureAwait(false)
            ?? throw new DatabaseSettingsException(DatabaseServerRules.LockHeldMessage);
        using var owner = MaintenanceGate.EnterOwnerScope();

        var original = await File.ReadAllTextAsync(state.IniPath, cancellationToken).ConfigureAwait(false);
        var backup = $"{state.IniPath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
        var wasRunning = state.Service is ServerServiceStatus.Running or ServerServiceStatus.Starting;
        try
        {
            await File.WriteAllTextAsync(backup, original, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await WriteAtomicAsync(state.IniPath, MyIni.WithPort(original, port), cancellationToken).ConfigureAwait(false);
            if (state.Service != ServerServiceStatus.Stopped) await services.StopAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            await services.StartAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            if (await WaitUntilReadyAsync(port, cancellationToken).ConfigureAwait(false) is { } failure)
                throw new DatabaseSettingsException($"Serverul nu a răspuns pe portul {port}: {failure}{LogTail(original, state.IniPath)}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("The port of the database server could not be changed ({ErrorType}).", exception.GetType().Name);
            var restored = await RestoreAsync(state, original, wasRunning, oldPort, cancellationToken).ConfigureAwait(false);
            await RecordAsync(AuditActions.ChangeDatabaseServerPort, "Port server bază de date",
                $"Schimbarea portului {oldPort} → {port} a eșuat și s-a revenit la configurația anterioară: {exception.Message}", cancellationToken).ConfigureAwait(false);
            return new(false, $"Portul serverului nu a fost schimbat: {(exception is DatabaseSettingsException ? exception.Message : "serverul nu a putut porni pe portul nou")}. " +
                (restored is null ? $"S-a revenit la portul {oldPort}." : $"Nu s-a putut reveni complet la portul {oldPort}: {restored}"), RolledBack: true);
        }

        // The server answers on the new port: the application follows it (the connection and the file of the administration account of this installation).
        await settings.ApplyServerPortAsync(port, cancellationToken).ConfigureAwait(false);
        if (DatabaseAdministrationAccount.Read(configuration) is { } admin && admin.Port == oldPort)
            DatabaseAdministrationAccount.Write(configuration, admin with { Port = port });
        await RecordAsync(AuditActions.ChangeDatabaseServerPort, "Port server bază de date", AuditDetails.Changes(new AuditChange("Port server bază de date", oldPort.ToString(), port.ToString())), cancellationToken).ConfigureAwait(false);
        return new(true, $"Serverul bazei de date ascultă acum pe portul {port}; aplicația s-a conectat la el. Scripturile și fișierele de dezvoltare cu portul vechi se actualizează cu tools\\sync-dev-port.ps1 -Port {port}.");
    }

    public async Task<DatabaseSaveResult> RestartApplicationAsync(string administratorPassword, CancellationToken cancellationToken = default)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        if (!restart.Supported)
            return new(false, "Această instanță nu rulează sub un supervizor sau serviciu care o repornește, deci nu se poate reporni de aici. Oprește-o și pornește-o din nou; portul salvat se aplică la pornire.");
        await RecordAsync(AuditActions.RestartApplication, "Repornire aplicație", "Repornire cerută din Setări → Baza de date (de exemplu pentru un port nou al aplicației).", cancellationToken).ConfigureAwait(false);
        restart.Request("cerută din Setări → Baza de date");
        return new(true, "Aplicația repornește; pagina se reîncarcă singură când răspunde.");
    }

    // The common frame of stop, start and restart: access, reconfirmation, the state, the lock of maintenance, one journal entry that names the exact operation.
    private async Task<DatabaseSaveResult> RunAsync(string action, string target, string administratorPassword, CancellationToken cancellationToken,
        Func<DatabaseServerState, Task<DatabaseSaveResult>> operation)
    {
        await access.EnsureAdministratorAsync(cancellationToken).ConfigureAwait(false);
        await new AdministratorReconfirmation(configuration, access, authenticator, logger).ConfirmAsync(administratorPassword, cancellationToken).ConfigureAwait(false);
        var state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!state.CanControl) throw new DatabaseSettingsException(state.Reason ?? DatabaseServerRules.NotControllableMessage);
        var actor = (await RepositoryAudit.ActorAsync(access, cancellationToken).ConfigureAwait(false)).Username;
        await using var handle = await locks.TryAcquireAsync(target, actor, AccessRoles.Administrator, cancellationToken).ConfigureAwait(false)
            ?? throw new DatabaseSettingsException(DatabaseServerRules.LockHeldMessage);
        using var owner = MaintenanceGate.EnterOwnerScope();
        try
        {
            var result = await operation(state).ConfigureAwait(false);
            await RecordAsync(action, target, $"Serviciu {state.ServiceName}, port {state.IniPort}: {(result.Success ? "reușit" : "eșuat")}. {result.Message}", cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or DatabaseSettingsException or AccessDeniedException))
        {
            logger.LogError("Database server operation '{Action}' failed ({ErrorType}).", action, exception.GetType().Name);
            await RecordAsync(action, target, $"Serviciu {state.ServiceName}: eșuat ({exception.GetType().Name}).", cancellationToken).ConfigureAwait(false);
            throw new DatabaseSettingsException(exception is System.ServiceProcess.TimeoutException or System.TimeoutException
                ? $"Serviciul „{state.ServiceName}” nu și-a schimbat starea în {(int)DatabaseServerRules.ServiceTimeout.TotalSeconds} de secunde. Verifică-l în Servicii (services.msc)."
                : $"Operația a eșuat: {exception.Message}");
        }
    }

    private async Task<DatabaseSaveResult> StartAndCheckAsync(DatabaseServerState state, int port, string success, CancellationToken cancellationToken)
    {
        await services.StartAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
        if (await WaitUntilReadyAsync(port, cancellationToken).ConfigureAwait(false) is { } failure)
            throw new DatabaseSettingsException($"Serviciul a pornit, dar serverul nu răspunde pe portul {port}: {failure}{LogTail(null, state.IniPath)}");
        MySqlConnection.ClearAllPools();
        return new(true, success);
    }

    // null = the server answers with the account of the application; otherwise the last thing it said.
    private async Task<string?> WaitUntilReadyAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ready;
        string? last = "nu răspunde";
        do
        {
            last = await verifyConnection(Host, port, cancellationToken).ConfigureAwait(false);
            if (last is null) return null;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1000, Math.Max(10, ready.TotalMilliseconds / 20))), cancellationToken).ConfigureAwait(false);
        } while (DateTime.UtcNow < deadline);
        return last;
    }

    // After a failed change: the old my.ini back, the server started again on the old port; null = done, otherwise what is still wrong.
    private async Task<string?> RestoreAsync(DatabaseServerState state, string original, bool wasRunning, int oldPort, CancellationToken cancellationToken)
    {
        try
        {
            await WriteAtomicAsync(state.IniPath!, original, cancellationToken).ConfigureAwait(false);
            if (services.Status(state.ServiceName) != ServerServiceStatus.Stopped) await services.StopAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            if (!wasRunning) return null;
            await services.StartAsync(state.ServiceName, DatabaseServerRules.ServiceTimeout, cancellationToken).ConfigureAwait(false);
            return await WaitUntilReadyAsync(oldPort, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return exception.Message; }
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    // The last lines of the error log of the server (its path is in my.ini), so a failed start says why in the page.
    private static string LogTail(string? iniContent, string? iniPath)
    {
        try
        {
            if (iniPath is null) return "";
            var content = iniContent ?? File.ReadAllText(iniPath);
            if (MyIni.ReadValue(content, "log-error") is not { Length: > 0 } log || !File.Exists(log)) return "";
            var lines = File.ReadLines(log).Reverse().Where(line => line.Contains("[ERROR]", StringComparison.Ordinal)).Take(3).Reverse().Select(line => line.Length > 200 ? line[..200] : line).ToArray();
            return lines.Length == 0 ? "" : " Jurnalul serverului: " + string.Join(" | ", lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static Func<string, int, CancellationToken, Task<string?>> VerifyAsync(IConfiguration configuration) => async (host, port, cancellationToken) =>
    {
        var name = configuration["Database:Name"] ?? "BlazorStoc";
        var builder = DatabaseConnectionRules.TestBuilder(host, port, name, configuration["Database:User"] ?? "", configuration["Database:Password"],
            configuration["Database:SslMode"] ?? "Required", configuration["Database:CharSet"] ?? "utf8mb4");
        builder.ConnectionTimeout = 3;
        builder.Pooling = false;
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return DatabaseConnectionRules.Describe(exception, host, port, name); }
    };

    private static async Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1.5));
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or IOException) { return false; }
    }

    public static bool IsLocalHost(string host)
    {
        if (DatabaseConnectionRules.IsLoopback(host)) return true;
        if (string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var addresses = IPAddress.TryParse(host, out var parsed) ? [parsed] : Dns.GetHostAddresses(host);
            var local = NetworkInterface.GetAllNetworkInterfaces().SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses.Select(unicast => unicast.Address)).ToHashSet();
            return addresses.Any(address => IPAddress.IsLoopback(address) || local.Contains(address));
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException) { return false; }
    }

    private async Task RecordAsync(string action, string target, string details, CancellationToken cancellationToken)
    {
        try { await AuditRecorder.RecordActionAsync(audit, access, AuditEntities.Database, action, "", target, details, "", cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Journal entry '{Action}' could not be written ({ErrorType}): {Details}", action, exception.GetType().Name, details);
        }
    }
}
