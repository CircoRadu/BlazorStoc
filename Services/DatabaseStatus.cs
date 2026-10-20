using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MySqlConnector;

namespace BlazorStoc.Services;

// Setari -> Baza de date -> Stare: what the database server reports about itself and where the application runs. Read-only; the application account
// (SELECT/INSERT/UPDATE/DELETE) is enough: VERSION(), a few @@variables, SHOW STATUS and information_schema.TABLES need no extra right.

public sealed record DatabaseServerStatus(
    bool Reachable, string? Problem, bool Maintenance, string? MaintenanceOperation,
    string Host, int Port, string Name, string AccountUser, string SslMode, bool RunsOnThisMachine,
    double LatencyMs, string Version, string ServerHost, string ServerOs, int ServerPort, string DataDirectory, string CurrentUser,
    DateTime? ServerTimeUtc, long UptimeSeconds, int ThreadsConnected, string SslCipher, int TableCount, long SizeBytes,
    IReadOnlyList<string> MissingColumns, bool MigratorConfigured, bool BackupAccountConfigured, bool RestoreAccountConfigured);

public sealed record AppRuntimeStatus(string Machine, string Os, string Framework, int ProcessId, DateTime StartedUtc, string Environment,
    IReadOnlyList<string> Urls, int? ConfiguredPort, string? HostingNote, int Sessions, long WorkingSetBytes, string ContentRoot);

/// <summary>The number of open interactive sessions (Blazor circuits).</summary>
public sealed class CircuitCounter
{
    private int count;
    public int Count => Volatile.Read(ref count);
    internal void Opened() => Interlocked.Increment(ref count);
    internal void Closed() => Interlocked.Decrement(ref count);
}

public sealed class CircuitCountHandler(CircuitCounter counter) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken) { counter.Opened(); return Task.CompletedTask; }
    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken) { counter.Closed(); return Task.CompletedTask; }
}

public interface IDatabaseStatusService
{
    Task<DatabaseServerStatus> ReadDatabaseAsync(CancellationToken cancellationToken = default);
    Task<AppRuntimeStatus> ReadApplicationAsync(CancellationToken cancellationToken = default);
}

public static class DatabaseStatusText
{
    public static string Duration(long seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} zile {span.Hours} h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h {span.Minutes} min";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes} min {span.Seconds} s";
        return $"{span.Seconds} s";
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.GetCultureInfo("ro-RO")) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.GetCultureInfo("ro-RO")) + " MB",
        >= 1L << 10 => (bytes / (double)(1L << 10)).ToString("0", CultureInfo.GetCultureInfo("ro-RO")) + " KB",
        _ => bytes + " B"
    };
}

public sealed class DatabaseStatusService(IConfiguration configuration, IAccessControl access, MariaSchemaMigrator? migrator, AppHostingInfo hosting,
    CircuitCounter circuits, IServer? server = null, IHostEnvironment? environment = null) : IDatabaseStatusService
{
    public async Task<DatabaseServerStatus> ReadDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureConfiguredAdministratorAsync(cancellationToken).ConfigureAwait(false);
        var host = configuration["Database:Host"] ?? "127.0.0.1";
        var port = configuration.GetValue("Database:Port", 3307);
        var name = configuration["Database:Name"] ?? "BlazorStoc";
        var user = configuration["Database:User"] ?? "blazorstoc_dev";
        var ssl = configuration["Database:SslMode"] ?? "Required";
        var local = DatabaseConnectionRules.IsLoopback(host) || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        var migratorConfigured = migrator?.MigratorConfigured ?? false;
        var backupAccount = Configured("Database:BackupUser", "Database:BackupPassword");
        var restoreAccount = Configured("Database:RestoreUser", "Database:RestorePassword");

        DatabaseServerStatus Unavailable(string problem, bool maintenance = false, string? operation = null) => new(
            false, problem, maintenance, operation, host, port, name, user, ssl, local, 0, "", "", "", 0, "", "", null, 0, 0, "", 0, 0, [],
            migratorConfigured, backupAccount, restoreAccount);

        if (MaintenanceGate.Active is { } active && !MaintenanceGate.IsOwner)
            return Unavailable($"{active.Operation} în curs (început de {active.OperatorName}). Datele sunt temporar indisponibile.", true, active.Operation);
        try
        {
            await using var connection = DatabaseConnections.Create(configuration);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var latencies = new List<double>();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var watch = Stopwatch.StartNew();
                await using var ping = new MySqlCommand("SELECT 1", connection);
                await ping.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
            string version = "", serverHost = "", serverOs = "", dataDirectory = "", currentUser = "";
            var serverPort = 0;
            DateTime? serverTime = null;
            await using (var command = new MySqlCommand("SELECT VERSION(), @@hostname, @@version_compile_os, @@port, @@datadir, CURRENT_USER(), UTC_TIMESTAMP()", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    version = reader.GetString(0); serverHost = reader.GetString(1); serverOs = reader.GetString(2); serverPort = Convert.ToInt32(reader.GetValue(3));
                    dataDirectory = reader.GetString(4); currentUser = reader.GetString(5); serverTime = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc);
                }
            }
            long uptime = 0; var threads = 0;
            await using (var command = new MySqlCommand("SHOW GLOBAL STATUS WHERE Variable_name IN ('Uptime','Threads_connected')", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var value = long.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                    if (reader.GetString(0) == "Uptime") uptime = value; else threads = (int)value;
                }
            var cipher = "";
            await using (var command = new MySqlCommand("SHOW SESSION STATUS LIKE 'Ssl_cipher'", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) cipher = reader.GetString(1);
            var tables = 0; long size = 0;
            await using (var command = new MySqlCommand("SELECT COUNT(*), COALESCE(SUM(DATA_LENGTH + INDEX_LENGTH), 0) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'", connection))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { tables = Convert.ToInt32(reader.GetValue(0)); size = Convert.ToInt64(reader.GetValue(1)); }
            IReadOnlyList<string> missing = migrator is null ? [] : await migrator.FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false);
            latencies.Sort();
            return new(true, null, false, null, host, port, name, user, ssl, local, latencies[latencies.Count / 2], version, serverHost, serverOs, serverPort,
                dataDirectory, currentUser, serverTime, uptime, threads, cipher, tables, size, missing, migratorConfigured, backupAccount, restoreAccount);
        }
        catch (MaintenanceInProgressException)
        {
            return Unavailable(MaintenanceGate.BlockedMessage, true, MaintenanceGate.Active?.Operation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(DatabaseConnectionRules.Describe(exception, host, port, name));
        }
    }

    public async Task<AppRuntimeStatus> ReadApplicationAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureConfiguredAdministratorAsync(cancellationToken).ConfigureAwait(false);
        using var process = Process.GetCurrentProcess();
        var urls = server?.Features.Get<IServerAddressesFeature>()?.Addresses.ToArray() ?? [];
        return new(Environment.MachineName, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, process.Id,
            process.StartTime.ToUniversalTime(), environment?.EnvironmentName ?? "", urls, hosting.ConfiguredPort, hosting.Note, circuits.Count,
            process.WorkingSet64, environment?.ContentRootPath ?? "");
    }

    private bool Configured(string userKey, string passwordKey) =>
        !string.IsNullOrWhiteSpace(configuration[userKey]) && !string.IsNullOrWhiteSpace(configuration[passwordKey]);
}
