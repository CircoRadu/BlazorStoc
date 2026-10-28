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
        await EnsureTestSchemaCurrentAsync(probe, configuration);
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
            await Section("Risk analyses: one active per point, renewals and history, notification, replace, guards, archive", () => RiskAnalysesAsync(configuration, admin, audit, probe));
            await Section("User types: system types, catalog permissions, seeding, assignment", () => UserTypesAsync(configuration, admin, audit, probe));
            await Section("Stock permissions: each kind of movement asks for its own key", () => StockPermissionsAsync(configuration, admin, audit));
            await Section("Module state: modules stopped by hand are saved, put back at restart, journal", () => ModuleStateAsync(configuration, admin, audit, probe));
            await Section("Activity import: settings with encrypted password, test, structure state, ticket types, journal, administrator only", () => ActivityImportAsync(configuration, admin, audit, probe));
            await Section("Activity import partners: simulation, application, ties, problems, journal, administrator only", () => ActivityPartnersAsync(configuration, admin, audit, probe));
            await Section("Activity import tickets: simulation, application, date limit, destinations, source detail, journal, administrator only", () => ActivityTicketsAsync(configuration, admin, audit, probe));
            await Section("Activity import maintenance: keywords nomenclator, tickets that are also maintenance, events to validate, decisions, journal", () => ActivityMaintenanceAsync(configuration, admin, audit, probe));
            await Section("Activity import synchronization: by hand and scheduled, backoff, alerts, deleted in the source (only signalled), worker", () => ActivitySyncAsync(configuration, admin, audit, probe));
            await Section("Schema ledger: applied migrations are recorded, read back, never overwritten, drift of an edited file", () => SchemaLedgerAsync(configuration, probe));
            await Section("Module permissions: registers are read by anyone with a permission, add/edit/delete each ask for their key", () => ModulePermissionsAsync(configuration, admin, audit));
            await Section("Subcategory order: arranged by the administrator, new ones at the end, journal", () => SubcategoryOrderAsync(configuration, admin, audit));
            await Section("Subcategory tree: children, inherited parameters and tags, leaf-only products, leaf turning into a parent, journal", () => SubcategoryTreeAsync(configuration, admin, audit, probe));
            await Section("Category parameters: parameters, families and tags of a category, inherited by its tree, archive, unique names", () => CategoryParametersAsync(configuration, admin, audit));
            await Section("Definition relocation: merge, preview as rolled-back run, incomplete products, families, blocks", () => DefinitionRelocationAsync(configuration, admin, audit));
            await Section("Subcategory move: drag and drop of nodes, whole branch moves, conflicts refused, journal", () => SubcategoryMoveAsync(configuration, admin, audit));
            await Section("Subcategory inventory: a node stands for its leaves, leaves titled with their path", () => SubcategoryInventoryAsync(configuration, admin, audit));
            await Section("Branch move keeps definitions: copy, merge, verification, incomplete products", () => BranchMoveKeepsDefinitionsAsync(configuration, admin, audit));
            await Section("Leaf promotion: a leaf becomes a category, the new name is checked first, definitions stay, journal", () => LeafPromotionAsync(configuration, admin, audit));
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
            await Section("Database settings: server status, connection test, refusals, no password in the journal", () => DatabaseSettingsAsync(configuration, audit, probe));
            await Section("Database accounts: status, change of password, interrupted change, reset, export and import (a throw-away account)", () => DatabaseAccountsAsync(configuration, audit));
            await Section("NAS backup settings: encrypted password, administrators only, journal", () => NasBackupSettingsAsync(configuration, admin, audit, probe));
            await Section("Journal display setting: default 500, range, administrators only, journal", () => JournalDisplayAsync(configuration, admin, audit, probe));
            await Section("Default notification templates: made once per event, existing and deleted ones left alone, journal", () => DefaultTemplatesAsync(configuration, admin, audit, probe));
            await Section("Offer templates: save, refuse, change, journal", () => OfferTemplatesAsync(configuration, admin, audit, probe));
            await Section("Project components: add, state, archive, reactivate", () => ProjectComponentsAsync(configuration, admin, audit, probe));
            await Section("System types: uniqueness, alternative names, deactivation, journal", () => SystemTypesAsync(configuration, admin, audit, probe));
            await Section("Storno, return, net consumption, consumption note", () => StornoReturnNoteAsync(configuration, admin, audit, probe));
            await Section("Exit operation: grouping, all or nothing, preview, repeated line", () => ExitOperationAsync(configuration, admin, audit, probe));
            await Section("To regularize: cause, list, notification, regularization", () => ToRegularizeAsync(configuration, admin, audit, probe));
            await Section("Required parameters: definition, values, product code, blocked until complete, value change, journal", () => ProductParametersAsync(configuration, admin, audit, probe));
            await Section("Parameter families and intervals: grouping rules, one active member, incomplete products, journal", () => ParameterFamiliesAsync(configuration, admin, audit, probe));
            await Section("Subcategory tags: add, rename, delete, On/Off on products, journal", () => SubcategoryTagsAsync(configuration, admin, audit, probe));
            await Section("Free entries and invoice linking: reasons, awaited invoice, repeated product, overrun, attach/detach, notification", () => FreeEntriesAsync(configuration, admin, audit, probe));
            await Section("Backup worker: real backup, lock, cancellation, restoration up to the swap, NAS copier in a worker process", () => BackupWorkerAsync(configuration, admin, audit));
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
    // The isolated test database (never the real one) is brought up to the latest migration here: a migration whose columns are missing is run with the
    // test account (the statements are idempotent), so a new migration does not need a separate step before the checks.
    private static async Task EnsureTestSchemaCurrentAsync(MySqlConnection probe, IConfiguration configuration)
    {
        if (!string.Equals(configuration["Database:Name"], "blazorstoc_test", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var migration in MariaSchemaMigrations.All.OrderBy(item => item.Version))
        {
            var missing = false;
            foreach (var (table, column) in migration.ExpectedColumns)
            {
                await using var look = new MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@t AND column_name=@c", probe);
                look.Parameters.AddWithValue("@t", table); look.Parameters.AddWithValue("@c", column);
                if (Convert.ToInt64(await look.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0) { missing = true; break; }
            }
            if (!missing) continue;
            Console.WriteLine($"Test schema: applying migration {migration.Version} ({migration.Description})");
            // The test account has no DDL rights: a migrator account (MARIA_TEST_MIGRATOR_CONFIG_PATH, a file like migration-account.private.json whose
            // account may alter blazorstoc_test) runs the statements; without it the migration is tried with the test account.
            await using var migrator = await OpenMigratorForTestAsync(configuration) ?? probe;
            foreach (var statement in migration.Statements)
            {
                await using var command = new MySqlCommand(statement, migrator) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<MySqlConnection?> OpenMigratorForTestAsync(IConfiguration configuration)
    {
        if (Environment.GetEnvironmentVariable("MARIA_TEST_MIGRATOR_CONFIG_PATH") is not { Length: > 0 } path || !File.Exists(path)) return null;
        var file = new ConfigurationBuilder().AddJsonFile(path).Build();
        var builder = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = configuration["Database:Host"], Port = uint.Parse(configuration["Database:Port"] ?? "3306", System.Globalization.CultureInfo.InvariantCulture),
            Database = configuration["Database:Name"], UserID = file["Database:User"], Password = file["Database:Password"], SslMode = MySqlConnector.MySqlSslMode.Required
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

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
