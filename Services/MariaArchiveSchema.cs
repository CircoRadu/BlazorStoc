using MySqlConnector;

namespace BlazorStoc.Services;

// Subtask 2.6/2.9 (Task 2): the real migrated MariaDB database already has every archive_* table, audit_events and
// app_metadata (migration\schema-mariadb.sql, verified against the live instance in this cycle - 27 tables, columns
// match exactly). The application account (blazorstoc_dev) has no DDL rights and must not receive any just to let
// old initialization code pass, so this no longer creates or alters any table. It only confirms, with plain SELECTs
// the app account already has, that the tables the app depends on are present, and logs a clear warning otherwise -
// it never throws, so demo mode and an otherwise-working MariaDB connection are never blocked by this check.
// Schema changes belong to an explicit migration using the dedicated blazorstoc_migrator account (Subtask 2.9;
// see docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md and docs/PROJECT_STATE.md for how that account was created and what
// it is scoped to), never to this startup path.
public static class MariaArchiveSchema
{
    // Internal, not private: Subtask 2.2 (Task 2) reuses this exact table list as the scope of the canonical
    // row-hash comparison run around the mariadb-dump export (CanonicalRowHasher), so both checks always agree on
    // what "the whole live database" means.
    // The tables of the migrated base database. Only these are checked at startup: that check runs BEFORE the schema
    // migrations, so a table a migration creates must not be required there.
    private static readonly string[] BaselineTables =
    [
        "app_metadata", "audit_events", "archive_operations", "archive_beneficiaries", "archive_files",
        "archive_products", "archive_project_observation_files", "archive_project_observations", "archive_projects",
        "archive_relations", "archive_stock_movements", "archive_vehicles", "archive_web_users",
        "beneficiaries", "categories", "subcategories", "products", "projects", "project_observations",
        "project_observation_files", "product_images", "product_locks", "stock_movements", "stock_movement_history",
        "vehicles", "web_users", "change_events"
    ];

    // Tables created by schema migrations (MariaSchemaMigrations). Until 30.09.2026 "beneficiary_work_points" (migration 2)
    // was missing from the list used by backup/restore, so backups exported it in dump.sql but left it out of the
    // manifest and of the canonical row hash. Add every future migration-created table here.
    private static readonly string[] MigratedTables = ["beneficiary_work_points", "notification_templates", "expiry_notifications", "notification_settings",
        "service_photos", "archive_work_points", "archive_service_photos", "service_contracts", "service_contract_points", "archive_service_contracts", "service_interventions", "archive_service_interventions",
        "invoice_templates", "invoice_template_versions"];

    internal static readonly string[] RequiredTables = [.. BaselineTables, .. MigratedTables];

    public static async Task InitializeAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()", connection);
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) present.Add(reader.GetString(0));
        var missing = BaselineTables.Where(table => !present.Contains(table)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Baza MariaDB configurata nu are tabelele asteptate ({string.Join(", ", missing)}). " +
                "Schema trebuie livrata/migrata separat (vezi docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md); " +
                "contul aplicatiei nu are drepturi de creare a tabelelor.");
    }
}
