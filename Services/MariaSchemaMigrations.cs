using MySqlConnector;

namespace BlazorStoc.Services;

// MariaDB schema changes. The application account (blazorstoc_dev) has no DDL rights; the dedicated account
// blazorstoc_migrator (CREATE/ALTER/INDEX/DROP/REFERENCES on the BlazorStoc schema only, no data rights, Subtask 2.9)
// applies them. Every statement is idempotent (ADD COLUMN IF NOT EXISTS), so no version table is needed - the
// migrator cannot write to app_metadata - and running the list twice, or on a database that already has the
// changes, does nothing. A change is added here AND to database/mariadb/schema-mariadb.sql (fresh installs).
public sealed record MariaMigration(int Version, string Description, IReadOnlyList<string> Statements,
    IReadOnlyList<(string Table, string Column)> ExpectedColumns);

public static class MariaSchemaMigrations
{
    public static readonly IReadOnlyList<MariaMigration> All =
    [
        new(1, "Beneficiari: persoana fizica / juridica, adresa, telefon, date ANAF",
        [
            """
            ALTER TABLE `beneficiaries`
                ADD COLUMN IF NOT EXISTS `kind` VARCHAR(2) NOT NULL DEFAULT 'PJ' AFTER `normalized_cui`,
                ADD COLUMN IF NOT EXISTS `address` VARCHAR(300) NOT NULL DEFAULT '' AFTER `kind`,
                ADD COLUMN IF NOT EXISTS `phone` VARCHAR(20) NOT NULL DEFAULT '' AFTER `address`,
                ADD COLUMN IF NOT EXISTS `registry_number` VARCHAR(40) NOT NULL DEFAULT '' AFTER `phone`,
                ADD COLUMN IF NOT EXISTS `postal_code` VARCHAR(10) NOT NULL DEFAULT '' AFTER `registry_number`,
                ADD COLUMN IF NOT EXISTS `caen_code` VARCHAR(4) NOT NULL DEFAULT '' AFTER `postal_code`,
                ADD COLUMN IF NOT EXISTS `anaf_verified` TINYINT NOT NULL DEFAULT 0 AFTER `caen_code`
            """
        ],
        [
            ("beneficiaries", "kind"), ("beneficiaries", "address"), ("beneficiaries", "phone"), ("beneficiaries", "registry_number"),
            ("beneficiaries", "postal_code"), ("beneficiaries", "caen_code"), ("beneficiaries", "anaf_verified")
        ]),
        new(2, "Beneficiari: puncte de lucru suplimentare",
        [
            """
            CREATE TABLE IF NOT EXISTS `beneficiary_work_points` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `beneficiary_id` BIGINT NOT NULL,
              `name` VARCHAR(200) NOT NULL,
              `address` VARCHAR(300) NOT NULL,
              `normalized_address` VARCHAR(400) NOT NULL,
              `phone` VARCHAR(20) NOT NULL DEFAULT '',
              `contact_person` VARCHAR(200) NOT NULL DEFAULT '',
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_beneficiary_work_points_0` (`beneficiary_id`, `normalized_address`),
              CONSTRAINT `fk_beneficiary_work_points_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("beneficiary_work_points", "id"), ("beneficiary_work_points", "beneficiary_id"), ("beneficiary_work_points", "name"),
            ("beneficiary_work_points", "address"), ("beneficiary_work_points", "normalized_address"),
            ("beneficiary_work_points", "phone"), ("beneficiary_work_points", "contact_person"), ("beneficiary_work_points", "version")
        ])
    ];
}

public sealed record MariaMigrationReport(bool MigratorConfigured, IReadOnlyList<string> Applied, IReadOnlyList<string> MissingColumns);

public sealed class MariaSchemaMigrator(IConfiguration configuration, ILogger<MariaSchemaMigrator> logger)
{
    public bool MigratorConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Database:MigratorUser"]) && !string.IsNullOrWhiteSpace(configuration["Database:MigratorPassword"]);

    /// <summary>Columns the code expects that the live schema does not have yet (checked with the application account).</summary>
    public async Task<IReadOnlyList<string>> FindMissingColumnsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand("""
            SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) existing.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return MariaSchemaMigrations.All.SelectMany(migration => migration.ExpectedColumns)
            .Select(column => $"{column.Table}.{column.Column}").Where(column => !existing.Contains(column)).ToArray();
    }

    /// <summary>Applies every migration with the migrator account, then re-checks the schema with the application account.</summary>
    public async Task<MariaMigrationReport> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!MariaDatabaseGuard.IsAllowedDatabase(configuration))
            throw new InvalidOperationException("Migrarea schemei este permisa numai pe baza BlazorStoc.");
        if (!MigratorConfigured) return new(false, [], await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false));
        var applied = new List<string>();
        var before = await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false);
        if (before.Count > 0)
        {
            await using var connection = DatabaseConnections.CreateMigrator(configuration);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            foreach (var migration in MariaSchemaMigrations.All)
            {
                foreach (var statement in migration.Statements)
                {
                    await using var command = new MySqlCommand(statement, connection) { CommandTimeout = 120 };
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                applied.Add($"{migration.Version}: {migration.Description}");
                logger.LogInformation("MariaDB schema migration {Version} applied ({Description}).", migration.Version, migration.Description);
            }
        }
        return new(true, applied, await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false));
    }
}
