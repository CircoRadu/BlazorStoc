using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using BlazorStoc.Services;

namespace BlazorStoc.Checks;

// Task 2 (after the SQLite removal): the scenarios that used to run against the local SQLite database, rebuilt on the
// isolated blazorstoc_test MariaDB database. Every section creates its own uniquely named data, removes it through the
// repositories (so the archive path is exercised too) and reports its own failure without stopping the others.
public static partial class MariaExtendedChecks
{
    private static readonly DateOnly? Expiry = new DateOnly(2027, 3, 15);
    private static int failures;

    public static async Task RunAsync(IConfiguration baseConfiguration)
    {
        Console.WriteLine("=== Task 2: extended MariaDB checks (blazorstoc_test) ===");
        var assets = Path.Combine(Path.GetTempPath(), "blazorstoc-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assets);
        var configuration = new ConfigurationBuilder().AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MariaAssetsRoot"] = assets }).Build();
        var admin = new TestAccessControl(true, "integration.tester");
        var audit = new MariaAuditTrail(configuration);
        await using var probe = await OpenRawAsync(configuration);
        try
        {
            await Section("Products: concurrency, stock, groups", () => ProductsAsync(configuration, admin, audit, probe));
            await Section("Beneficiaries: duplicates, reasons, archive", () => BeneficiariesAsync(configuration, admin, audit, probe));
            await Section("Users: reasons, password change, archive", () => UsersAsync(configuration, admin, audit, probe));
            await Section("Projects: concurrency, versions, files", () => ProjectsAsync(configuration, admin, audit, probe, assets));
            await Section("Stock movements: dates, concurrency, history, vehicles", () => MovementsAsync(configuration, admin, audit, probe));
            await Section("Vehicles: concurrency, expiry journal, archive", () => VehiclesAsync(configuration, admin, audit, probe));
            await Section("Product locks: contention, takeover, forced release", () => LocksAsync(configuration, admin, audit, probe));
            await Section("Work points: main point, description, coordinates, photos, archive, backfill", () => WorkPointsAsync(configuration, admin, audit, probe, assets));
            await Section("Maintenance contracts: coverage, one active contract per point, On/Off, moves, archive", () => ServiceContractsAsync(configuration, admin, audit, probe));
            await Section("Maintenance interventions: register, due date choices, corrections, photos, archive, guards", () => ServiceInterventionsAsync(configuration, admin, audit, probe, assets));
            await Section("Maintenance notifications: due dates and contract expiry sources, automatic close and reopen, threshold", () => MaintenanceNotificationsAsync(configuration, admin, audit, probe));
            await Section("Journal: server-side filtering, paging, window, ranges, removals, summary", () => AuditQueryAsync(configuration, audit, probe));
            await Section("Change events", () => ChangeEventsAsync(configuration, admin, audit));
            await Section("Expiry notifications: templates, engine, take over, reminder", () => NotificationsAsync(configuration, admin, audit, probe));
            await Section("Notification settings: clean-up of old resolved notifications", () => NotificationSettingsAsync(configuration, admin, audit, probe));
            await Section("Invoice templates: create, versions, unique names, concurrency, delete, journal", () => InvoiceTemplatesAsync(configuration, admin, audit, probe));
            await Section("Suppliers and invoices: unique tax id, sources, edits, invoices, entries tied to invoices, delete rules, journal, archive", () => SuppliersAsync(configuration, admin, audit, probe));
            await Section("Supplier product codes: link, change, journal, removal with the product", () => SupplierProductCodesAsync(configuration, admin, audit, probe));
            await Section("Categories: unique names together, deleting empty ones", () => CategoryNamesAndDeletionAsync(configuration, admin, audit));
            await Section("Supplier recognition: template link, aliases, recognition log, CSV export", () => SupplierRecognitionAsync(configuration, admin, audit, probe));
            await Section("Usage scenarios: invoice flows, exits to beneficiary and vehicles, over-stock exits, regularization, concurrency", () => UsageScenariosAsync(configuration, admin, audit, probe));
            await Section("Exit flow: journal by destination, reference, filters, repeated exit, use above the vehicle quantity", () => ExitFlowAsync(configuration, admin, audit, probe));
            await Section("Offer import: project, component, lines, revision, remembered ties", () => OfferImportAsync(configuration, admin, audit, probe));
            await Section("Reservations: free stock, consumption by exit, warning and lowering, release with the component", () => ReservationsAsync(configuration, admin, audit, probe));
            await Section("Vehicle target levels: set, change, missing pieces, remove, journal", () => VehicleTargetsAsync(configuration, admin, audit, probe));
            await Section("Stock alerts: minimum stock, stale reservation, project deadline, consumption export", () => StockAlertsAsync(configuration, admin, audit, probe));
            await Section("NAS backup settings: encrypted password, administrators only, journal", () => NasBackupSettingsAsync(configuration, admin, audit, probe));
            await Section("Default notification templates: made once per event, existing and deleted ones left alone, journal", () => DefaultTemplatesAsync(configuration, admin, audit, probe));
            await Section("Offer templates: save, refuse, change, journal", () => OfferTemplatesAsync(configuration, admin, audit, probe));
            await Section("Project components: add, state, archive, reactivate", () => ProjectComponentsAsync(configuration, admin, audit, probe));
            await Section("System types: uniqueness, alternative names, deactivation, journal", () => SystemTypesAsync(configuration, admin, audit, probe));
            await Section("Storno, return, net consumption, consumption note", () => StornoReturnNoteAsync(configuration, admin, audit, probe));
            await Section("Exit operation: grouping, all or nothing, preview, repeated line", () => ExitOperationAsync(configuration, admin, audit, probe));
            await Section("To regularize: cause, list, notification, regularization", () => ToRegularizeAsync(configuration, admin, audit, probe));
            await Section("Required parameters: definition, values, product code, blocked until complete, value change, journal", () => ProductParametersAsync(configuration, admin, audit, probe));
            await Section("Free entries and invoice linking: reasons, awaited invoice, repeated product, overrun, attach/detach, notification", () => FreeEntriesAsync(configuration, admin, audit, probe));
        }
        finally
        {
            try { Directory.Delete(assets, true); } catch (IOException) { }
        }
        if (failures > 0) throw new Exception($"{failures} extended MariaDB section(s) failed.");
        Console.WriteLine("=== Extended MariaDB checks: all sections passed. ===");
    }

    private static async Task Section(string name, Func<Task> body)
    {
        // MARIA_ONLY=<text of a section name> runs only the sections whose name contains it (a targeted run).
        if (Environment.GetEnvironmentVariable("MARIA_ONLY") is { Length: > 0 } only && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
        Console.WriteLine("--- " + name + " ---");
        try { await body(); }
        catch (Exception exception)
        {
            failures++;
            Console.WriteLine($"FAIL [{name}]: {exception.GetType().Name}: {exception.Message}");
            Console.WriteLine(exception.StackTrace);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Check failed: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static async Task<T?> Rejects<T>(Func<Task> operation, string message) where T : Exception
    {
        try { await operation(); }
        catch (T exception) { Console.WriteLine("PASS: " + message); return exception; }
        throw new Exception("Check failed (not rejected): " + message);
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];
    private static string Letters() => new(Enumerable.Range(0, 3).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());

    // ---- Beneficiaries ---------------------------------------------------------------------------------------------
    private static BeneficiaryInput Legal(string name, string cui) => new() { Name = name, Cui = cui, Address = "Strada Test 1, Bucuresti", Phone = "0721000111" };

    // ---- Raw helpers -----------------------------------------------------------------------------------------------------------------
    private static async Task<MySqlConnection> OpenRawAsync(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"] ?? "127.0.0.1",
            Port = configuration.GetValue<uint>("Database:Port", 3307),
            Database = configuration["Database:Name"] ?? "blazorstoc_test",
            UserID = configuration["Database:User"] ?? "",
            Password = configuration["Database:Password"],
            SslMode = Enum.Parse<MySqlSslMode>(configuration["Database:SslMode"] ?? "Required", true),
            CharacterSet = configuration["Database:CharSet"] ?? "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 20
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    private static async Task<int> ExecuteAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<long> ScalarLongAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
    }
}
