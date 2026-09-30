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
        ]),
        new(3, "Vehicule: date de expirare ITP, asigurare si rovinieta (vehiculele existente primesc date din anul urmator)",
        [
            """
            ALTER TABLE `vehicles`
                ADD COLUMN IF NOT EXISTS `itp_expiry` DATE NOT NULL DEFAULT '2027-03-15' AFTER `version`,
                ADD COLUMN IF NOT EXISTS `insurance_expiry` DATE NOT NULL DEFAULT '2027-06-30' AFTER `itp_expiry`,
                ADD COLUMN IF NOT EXISTS `rovinieta_expiry` DATE NOT NULL DEFAULT '2027-09-30' AFTER `insurance_expiry`
            """
        ],
        [("vehicles", "itp_expiry"), ("vehicles", "insurance_expiry"), ("vehicles", "rovinieta_expiry")]),
        new(4, "Notificari de expirare: sabloane si notificari (starea preluat/amanat este globala)",
        [
            """
            CREATE TABLE IF NOT EXISTS `notification_templates` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `source_key` VARCHAR(60) NOT NULL,
              `subject` VARCHAR(200) NOT NULL,
              `body` VARCHAR(2000) NOT NULL,
              `threshold_days` INT NOT NULL,
              `is_active` TINYINT NOT NULL DEFAULT 1,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `expiry_notifications` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `template_id` BIGINT NOT NULL,
              `source_key` VARCHAR(60) NOT NULL,
              `object_id` BIGINT NOT NULL,
              `expiry_date` DATE NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              `acknowledged_by` VARCHAR(100) NULL,
              `acknowledged_utc` VARCHAR(40) NULL,
              `snooze_until` DATE NULL,
              `snooze_days` INT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_expiry_notifications_0` (`template_id`, `object_id`, `expiry_date`),
              CONSTRAINT `fk_expiry_notifications_0` FOREIGN KEY (`template_id`) REFERENCES `notification_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("notification_templates", "id"), ("notification_templates", "source_key"), ("notification_templates", "subject"),
            ("notification_templates", "body"), ("notification_templates", "threshold_days"), ("notification_templates", "is_active"),
            ("notification_templates", "version"),
            ("expiry_notifications", "id"), ("expiry_notifications", "template_id"), ("expiry_notifications", "source_key"),
            ("expiry_notifications", "object_id"), ("expiry_notifications", "expiry_date"), ("expiry_notifications", "created_utc"),
            ("expiry_notifications", "acknowledged_by"), ("expiry_notifications", "acknowledged_utc"), ("expiry_notifications", "snooze_until"),
            ("expiry_notifications", "snooze_days"), ("expiry_notifications", "version")
        ]),
        new(5, "Notificari: stare rezolvata cu motiv si instantaneu, o singura notificare pe eveniment, un singur sablon activ pe eveniment",
        [
            """
            ALTER TABLE `expiry_notifications`
                ADD COLUMN IF NOT EXISTS `resolved_by` VARCHAR(100) NULL,
                ADD COLUMN IF NOT EXISTS `resolved_utc` VARCHAR(40) NULL,
                ADD COLUMN IF NOT EXISTS `resolved_reason` VARCHAR(500) NULL,
                ADD COLUMN IF NOT EXISTS `resolved_auto` TINYINT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS `object_label` VARCHAR(300) NULL,
                ADD COLUMN IF NOT EXISTS `snapshot_values` TEXT NULL,
                ADD COLUMN IF NOT EXISTS `snapshot_subject` VARCHAR(500) NULL,
                ADD COLUMN IF NOT EXISTS `snapshot_body` TEXT NULL,
                ADD COLUMN IF NOT EXISTS `snapshot_source` VARCHAR(200) NULL
            """,
            // The foreign key on template_id used the old unique key as its index: give it its own before that key is replaced.
            "ALTER TABLE `expiry_notifications` ADD INDEX IF NOT EXISTS `ix_expiry_notifications_template` (`template_id`)",
            "ALTER TABLE `expiry_notifications` DROP INDEX IF EXISTS `uq_expiry_notifications_0`, ADD UNIQUE INDEX IF NOT EXISTS `uq_expiry_notifications_event` (`source_key`, `object_id`, `expiry_date`)",
            "ALTER TABLE `notification_templates` ADD COLUMN IF NOT EXISTS `active_source_key` VARCHAR(60) AS (IF(`is_active` = 1, `source_key`, NULL)) PERSISTENT",
            "ALTER TABLE `notification_templates` ADD UNIQUE INDEX IF NOT EXISTS `uq_notification_templates_active` (`active_source_key`)"
        ],
        [
            ("expiry_notifications", "resolved_by"), ("expiry_notifications", "resolved_utc"), ("expiry_notifications", "resolved_reason"),
            ("expiry_notifications", "resolved_auto"), ("expiry_notifications", "object_label"), ("expiry_notifications", "snapshot_values"),
            ("expiry_notifications", "snapshot_subject"), ("expiry_notifications", "snapshot_body"), ("expiry_notifications", "snapshot_source"),
            ("notification_templates", "active_source_key")
        ]),
        // One row (id 1), written by the application (the migrator has no data rights): a missing row means the defaults.
        new(6, "Setari notificari: curatarea periodica a notificarilor rezolvate (comutator, perioada in luni, ultima rulare)",
        [
            """
            CREATE TABLE IF NOT EXISTS `notification_settings` (
              `id` TINYINT NOT NULL,
              `purge_enabled` TINYINT NOT NULL DEFAULT 0,
              `purge_months` INT NOT NULL DEFAULT 12,
              `last_purge_utc` VARCHAR(40) NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("notification_settings", "id"), ("notification_settings", "purge_enabled"), ("notification_settings", "purge_months"),
            ("notification_settings", "last_purge_utc"), ("notification_settings", "version")
        ]),
        // The main work point becomes a real row (is_primary=1, one per beneficiary; the rows themselves are created by the
        // application at startup and with the beneficiary: their normalized address is computed in C#). Description, optional
        // coordinates (both or neither) and the photos of a work point (files on disk, rows here; later also of interventions).
        new(7, "Puncte de lucru extinse: punct principal real, descriere, coordonate optionale, fotografii (service_photos) si arhivarea lor",
        [
            """
            ALTER TABLE `beneficiary_work_points`
                ADD COLUMN IF NOT EXISTS `description` VARCHAR(2000) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS `is_primary` TINYINT NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS `latitude` DECIMAL(9,6) NULL,
                ADD COLUMN IF NOT EXISTS `longitude` DECIMAL(9,6) NULL
            """,
            "ALTER TABLE `beneficiary_work_points` ADD COLUMN IF NOT EXISTS `primary_beneficiary_id` BIGINT AS (IF(`is_primary` = 1, `beneficiary_id`, NULL)) PERSISTENT",
            "ALTER TABLE `beneficiary_work_points` ADD UNIQUE INDEX IF NOT EXISTS `uq_work_points_primary` (`primary_beneficiary_id`)",
            "ALTER TABLE `beneficiary_work_points` ADD CONSTRAINT IF NOT EXISTS `ck_work_points_coordinates` CHECK ((`latitude` IS NULL) = (`longitude` IS NULL))",
            """
            CREATE TABLE IF NOT EXISTS `service_photos` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `work_point_id` BIGINT NULL,
              `intervention_id` BIGINT NULL,
              `relative_path` VARCHAR(120) NOT NULL,
              `original_name` VARCHAR(255) NOT NULL,
              `content_type` VARCHAR(100) NOT NULL,
              `byte_length` BIGINT NOT NULL,
              `sha256` VARCHAR(64) NOT NULL,
              `caption` VARCHAR(200) NOT NULL DEFAULT '',
              `uploaded_by` VARCHAR(100) NOT NULL,
              `uploaded_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_service_photos_work_point` (`work_point_id`, `sha256`),
              UNIQUE KEY `uq_service_photos_intervention` (`intervention_id`, `sha256`),
              CONSTRAINT `fk_service_photos_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `ck_service_photos_owner` CHECK ((`work_point_id` IS NOT NULL) + (`intervention_id` IS NOT NULL) = 1)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `archive_work_points` (
              `archive_id` VARCHAR(64) NOT NULL,
              `original_id` BIGINT NOT NULL,
              `beneficiary_id` BIGINT NOT NULL,
              `name` LONGTEXT NOT NULL,
              `address` LONGTEXT NOT NULL,
              `phone` LONGTEXT NOT NULL,
              `contact_person` LONGTEXT NOT NULL,
              `description` LONGTEXT NOT NULL,
              `latitude` DECIMAL(9,6) NULL,
              `longitude` DECIMAL(9,6) NULL,
              `is_primary` TINYINT NOT NULL,
              `version` BIGINT NOT NULL,
              PRIMARY KEY (`archive_id`),
              KEY `ix_archive_work_points_original` (`original_id`),
              CONSTRAINT `fk_archive_work_points_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `archive_service_photos` (
              `archive_id` VARCHAR(64) NOT NULL,
              `original_id` BIGINT NOT NULL,
              `work_point_id` BIGINT NULL,
              `intervention_id` BIGINT NULL,
              `original_name` LONGTEXT NOT NULL,
              `content_type` LONGTEXT NOT NULL,
              `byte_length` BIGINT NOT NULL,
              `sha256` LONGTEXT NOT NULL,
              `caption` LONGTEXT NOT NULL,
              `uploaded_by` LONGTEXT NOT NULL,
              `uploaded_utc` LONGTEXT NOT NULL,
              PRIMARY KEY (`archive_id`),
              KEY `ix_archive_service_photos_original` (`original_id`),
              CONSTRAINT `fk_archive_service_photos_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("beneficiary_work_points", "description"), ("beneficiary_work_points", "is_primary"), ("beneficiary_work_points", "latitude"),
            ("beneficiary_work_points", "longitude"), ("beneficiary_work_points", "primary_beneficiary_id"),
            ("service_photos", "id"), ("service_photos", "work_point_id"), ("service_photos", "intervention_id"), ("service_photos", "relative_path"),
            ("service_photos", "original_name"), ("service_photos", "content_type"), ("service_photos", "byte_length"), ("service_photos", "sha256"),
            ("service_photos", "caption"), ("service_photos", "uploaded_by"), ("service_photos", "uploaded_utc"),
            ("archive_work_points", "archive_id"), ("archive_work_points", "original_id"), ("archive_work_points", "beneficiary_id"),
            ("archive_work_points", "name"), ("archive_work_points", "address"), ("archive_work_points", "phone"), ("archive_work_points", "contact_person"),
            ("archive_work_points", "description"), ("archive_work_points", "latitude"), ("archive_work_points", "longitude"),
            ("archive_work_points", "is_primary"), ("archive_work_points", "version"),
            ("archive_service_photos", "archive_id"), ("archive_service_photos", "original_id"), ("archive_service_photos", "work_point_id"),
            ("archive_service_photos", "intervention_id"), ("archive_service_photos", "original_name"), ("archive_service_photos", "content_type"),
            ("archive_service_photos", "byte_length"), ("archive_service_photos", "sha256"), ("archive_service_photos", "caption"),
            ("archive_service_photos", "uploaded_by"), ("archive_service_photos", "uploaded_utc")
        ]),
        // Maintenance contracts of a beneficiary and their coverage of work points. The unique key on active_work_point_id stands in for a
        // partial unique index (MariaDB has none): it is equal to work_point_id while the contract is active and NULL otherwise, so a work
        // point is in at most one ACTIVE contract and in any number of Off ones. The application sets it in the same transaction that
        // activates/deactivates the contract or adds/removes/moves a point.
        new(8, "Contracte de mentenanta: contracte (numar, data, ciclicitate, expirare, On/Off), acoperirea punctelor de lucru cu scadenta si arhivarea contractelor",
        [
            """
            CREATE TABLE IF NOT EXISTS `service_contracts` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `beneficiary_id` BIGINT NOT NULL,
              `contract_number` VARCHAR(30) NOT NULL,
              `contract_date` DATE NOT NULL,
              `cycle_months` TINYINT NOT NULL,
              `valid_until` DATE NULL,
              `is_active` TINYINT NOT NULL DEFAULT 1,
              `notes` VARCHAR(1000) NOT NULL DEFAULT '',
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_service_contracts_number` (`beneficiary_id`, `contract_number`, `contract_date`),
              CONSTRAINT `fk_service_contracts_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `ck_service_contracts_cycle` CHECK (`cycle_months` BETWEEN 1 AND 12),
              CONSTRAINT `ck_service_contracts_valid_until` CHECK (`valid_until` IS NULL OR `valid_until` >= `contract_date`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `service_contract_points` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `contract_id` BIGINT NOT NULL,
              `work_point_id` BIGINT NOT NULL,
              `active_work_point_id` BIGINT NULL,
              `cycle_months` TINYINT NULL,
              `next_due` DATE NOT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_service_contract_points_pair` (`contract_id`, `work_point_id`),
              UNIQUE KEY `uq_service_contract_points_active` (`active_work_point_id`),
              KEY `ix_service_contract_points_work_point` (`work_point_id`),
              CONSTRAINT `fk_service_contract_points_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `fk_service_contract_points_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `ck_service_contract_points_cycle` CHECK (`cycle_months` IS NULL OR `cycle_months` BETWEEN 1 AND 12),
              CONSTRAINT `ck_service_contract_points_active` CHECK (`active_work_point_id` IS NULL OR `active_work_point_id` = `work_point_id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `archive_service_contracts` (
              `archive_id` VARCHAR(64) NOT NULL,
              `original_id` BIGINT NOT NULL,
              `beneficiary_id` BIGINT NOT NULL,
              `contract_number` LONGTEXT NOT NULL,
              `contract_date` DATE NOT NULL,
              `cycle_months` INT NOT NULL,
              `valid_until` DATE NULL,
              `is_active` TINYINT NOT NULL,
              `notes` LONGTEXT NOT NULL,
              `version` BIGINT NOT NULL,
              PRIMARY KEY (`archive_id`),
              KEY `ix_archive_service_contracts_original` (`original_id`),
              CONSTRAINT `fk_archive_service_contracts_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("service_contracts", "id"), ("service_contracts", "beneficiary_id"), ("service_contracts", "contract_number"),
            ("service_contracts", "contract_date"), ("service_contracts", "cycle_months"), ("service_contracts", "valid_until"),
            ("service_contracts", "is_active"), ("service_contracts", "notes"), ("service_contracts", "version"),
            ("service_contract_points", "id"), ("service_contract_points", "contract_id"), ("service_contract_points", "work_point_id"),
            ("service_contract_points", "active_work_point_id"), ("service_contract_points", "cycle_months"),
            ("service_contract_points", "next_due"), ("service_contract_points", "version"),
            ("archive_service_contracts", "archive_id"), ("archive_service_contracts", "original_id"), ("archive_service_contracts", "beneficiary_id"),
            ("archive_service_contracts", "contract_number"), ("archive_service_contracts", "contract_date"), ("archive_service_contracts", "cycle_months"),
            ("archive_service_contracts", "valid_until"), ("archive_service_contracts", "is_active"), ("archive_service_contracts", "notes"),
            ("archive_service_contracts", "version")
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
