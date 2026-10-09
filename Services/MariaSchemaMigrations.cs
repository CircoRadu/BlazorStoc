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
        ]),
        // The register of interventions. kind M = maintenance (under an active contract, moves the due date of the point), C = on demand
        // (any work point, outside the cycle). The work point name/address and the contract label are snapshots taken when it is recorded.
        // A maintenance intervention that moved the due date keeps the due it closed (planned_due), how the next one was chosen
        // (next_due_basis E from the date performed / P from the planned date / O chosen) and the due it set (next_due_set); one entered
        // with a date older than the latest maintenance intervention of the point moves nothing and has all three empty.
        new(9, "Registru de interventii: interventii de mentenanta si la cerere (snapshot, scadenta inchisa si aleasa), legatura fotografiilor si arhivarea lor",
        [
            """
            CREATE TABLE IF NOT EXISTS `service_interventions` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `kind` CHAR(1) NOT NULL,
              `beneficiary_id` BIGINT NOT NULL,
              `work_point_id` BIGINT NOT NULL,
              `contract_id` BIGINT NULL,
              `work_point_name` VARCHAR(200) NOT NULL,
              `work_point_address` VARCHAR(300) NOT NULL,
              `contract_label` VARCHAR(60) NULL,
              `performed_on` DATE NOT NULL,
              `planned_due` DATE NULL,
              `next_due_basis` CHAR(1) NULL,
              `next_due_set` DATE NULL,
              `notes` VARCHAR(2000) NOT NULL DEFAULT '',
              `recorded_by` VARCHAR(100) NOT NULL,
              `recorded_utc` VARCHAR(40) NOT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              KEY `ix_service_interventions_beneficiary` (`beneficiary_id`, `performed_on`),
              KEY `ix_service_interventions_work_point` (`work_point_id`, `kind`, `performed_on`),
              KEY `ix_service_interventions_contract` (`contract_id`),
              CONSTRAINT `fk_service_interventions_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `fk_service_interventions_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `fk_service_interventions_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
              CONSTRAINT `ck_service_interventions_kind` CHECK (`kind` IN ('M', 'C')),
              CONSTRAINT `ck_service_interventions_contract` CHECK ((`kind` = 'M' AND `contract_id` IS NOT NULL AND `contract_label` IS NOT NULL) OR (`kind` = 'C' AND `contract_id` IS NULL AND `contract_label` IS NULL AND `planned_due` IS NULL AND `next_due_basis` IS NULL AND `next_due_set` IS NULL)),
              CONSTRAINT `ck_service_interventions_due` CHECK ((`planned_due` IS NULL) = (`next_due_basis` IS NULL) AND (`next_due_basis` IS NULL) = (`next_due_set` IS NULL)),
              CONSTRAINT `ck_service_interventions_basis` CHECK (`next_due_basis` IS NULL OR `next_due_basis` IN ('E', 'P', 'O')),
              CONSTRAINT `ck_service_interventions_next_due` CHECK (`next_due_set` IS NULL OR `next_due_set` > `performed_on`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            "ALTER TABLE `service_photos` ADD FOREIGN KEY IF NOT EXISTS `fk_service_photos_intervention` (`intervention_id`) REFERENCES `service_interventions` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION",
            """
            CREATE TABLE IF NOT EXISTS `archive_service_interventions` (
              `archive_id` VARCHAR(64) NOT NULL,
              `original_id` BIGINT NOT NULL,
              `kind` VARCHAR(1) NOT NULL,
              `beneficiary_id` BIGINT NOT NULL,
              `work_point_id` BIGINT NOT NULL,
              `contract_id` BIGINT NULL,
              `work_point_name` LONGTEXT NOT NULL,
              `work_point_address` LONGTEXT NOT NULL,
              `contract_label` LONGTEXT NULL,
              `performed_on` DATE NOT NULL,
              `planned_due` DATE NULL,
              `next_due_basis` VARCHAR(1) NULL,
              `next_due_set` DATE NULL,
              `notes` LONGTEXT NOT NULL,
              `recorded_by` LONGTEXT NOT NULL,
              `recorded_utc` LONGTEXT NOT NULL,
              `version` BIGINT NOT NULL,
              PRIMARY KEY (`archive_id`),
              KEY `ix_archive_service_interventions_original` (`original_id`),
              CONSTRAINT `fk_archive_service_interventions_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("service_interventions", "id"), ("service_interventions", "kind"), ("service_interventions", "beneficiary_id"), ("service_interventions", "work_point_id"),
            ("service_interventions", "contract_id"), ("service_interventions", "work_point_name"), ("service_interventions", "work_point_address"),
            ("service_interventions", "contract_label"), ("service_interventions", "performed_on"), ("service_interventions", "planned_due"),
            ("service_interventions", "next_due_basis"), ("service_interventions", "next_due_set"), ("service_interventions", "notes"),
            ("service_interventions", "recorded_by"), ("service_interventions", "recorded_utc"), ("service_interventions", "version"),
            ("archive_service_interventions", "archive_id"), ("archive_service_interventions", "original_id"), ("archive_service_interventions", "kind"),
            ("archive_service_interventions", "beneficiary_id"), ("archive_service_interventions", "work_point_id"), ("archive_service_interventions", "contract_id"),
            ("archive_service_interventions", "work_point_name"), ("archive_service_interventions", "work_point_address"), ("archive_service_interventions", "contract_label"),
            ("archive_service_interventions", "performed_on"), ("archive_service_interventions", "planned_due"), ("archive_service_interventions", "next_due_basis"),
            ("archive_service_interventions", "next_due_set"), ("archive_service_interventions", "notes"), ("archive_service_interventions", "recorded_by"),
            ("archive_service_interventions", "recorded_utc"), ("archive_service_interventions", "version")
        ]),
        // Templates that read supplier invoices (Settings -> Facturi): the current definition (JSON, positions as fractions of the page) and
        // every saved version of it. A name is unique per supplier tax id (the application also compares without letter case).
        new(10, "Sabloane de facturi: sabloane (furnizor, definitie JSON) si versiunile lor salvate",
        [
            """
            CREATE TABLE IF NOT EXISTS `invoice_templates` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `name` VARCHAR(120) NOT NULL,
              `supplier_name` VARCHAR(200) NOT NULL DEFAULT '',
              `supplier_cui` VARCHAR(20) NOT NULL DEFAULT '',
              `source_kind` VARCHAR(10) NOT NULL DEFAULT 'text',
              `version_number` INT NOT NULL DEFAULT 1,
              `definition` LONGTEXT NOT NULL,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_invoice_templates_name` (`supplier_cui`, `name`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `invoice_template_versions` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `template_id` BIGINT NOT NULL,
              `version_number` INT NOT NULL,
              `definition` LONGTEXT NOT NULL,
              `note` VARCHAR(300) NOT NULL DEFAULT '',
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_invoice_template_versions_number` (`template_id`, `version_number`),
              CONSTRAINT `fk_invoice_template_versions_template` FOREIGN KEY (`template_id`) REFERENCES `invoice_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("invoice_templates", "id"), ("invoice_templates", "name"), ("invoice_templates", "supplier_name"), ("invoice_templates", "supplier_cui"),
            ("invoice_templates", "source_kind"), ("invoice_templates", "version_number"), ("invoice_templates", "definition"), ("invoice_templates", "created_by"),
            ("invoice_templates", "created_utc"), ("invoice_templates", "updated_by"), ("invoice_templates", "updated_utc"), ("invoice_templates", "version"),
            ("invoice_template_versions", "id"), ("invoice_template_versions", "template_id"), ("invoice_template_versions", "version_number"),
            ("invoice_template_versions", "definition"), ("invoice_template_versions", "note"), ("invoice_template_versions", "created_by"), ("invoice_template_versions", "created_utc")
        ]),
        // The invoice (PDF) a template was made from, kept with the template so that editing it shows the same invoice again. A row is added
        // when a different file is saved with a new version; the latest row is the model in use.
        new(11, "Sabloane de facturi: factura PDF folosita ca model",
        [
            """
            CREATE TABLE IF NOT EXISTS `invoice_template_models` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `template_id` BIGINT NOT NULL,
              `version_number` INT NOT NULL,
              `file_name` VARCHAR(255) NOT NULL,
              `byte_length` BIGINT NOT NULL,
              `sha256` CHAR(64) NOT NULL,
              `content` LONGBLOB NOT NULL,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              KEY `ix_invoice_template_models_template` (`template_id`, `id`),
              CONSTRAINT `fk_invoice_template_models_template` FOREIGN KEY (`template_id`) REFERENCES `invoice_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("invoice_template_models", "id"), ("invoice_template_models", "template_id"), ("invoice_template_models", "version_number"), ("invoice_template_models", "file_name"),
            ("invoice_template_models", "byte_length"), ("invoice_template_models", "sha256"), ("invoice_template_models", "content"), ("invoice_template_models", "created_by"),
            ("invoice_template_models", "created_utc")
        ])
        ,
        // Versioning of invoice templates was given up (nothing keeps the earlier versions): a template is replaced when saved. A supplier can
        // have several templates and chooses which ones are used when invoices are read: `active` (every existing template stays in use).
        new(12, "Sabloane de facturi: coloana active (sablon folosit la citirea facturilor)",
        [
            "ALTER TABLE `invoice_templates` ADD COLUMN IF NOT EXISTS `active` TINYINT(1) NOT NULL DEFAULT 1"
        ],
        [
            ("invoice_templates", "active")
        ]),
        // The register of suppliers (Administrare -> Furnizori), the invoices stock entries are taken from and the link of an entry to its
        // invoice (null = a free entry). A supplier is unique by its tax id (`normalized_cui`: the digits of a Romanian CUI, the VAT identifier
        // with country prefix for another member state); an invoice number is unique per supplier. Nothing is deleted with its supplier or
        // invoice (RESTRICT): a supplier that has invoices cannot be deleted.
        new(13, "Furnizori si facturi de furnizor: registrul de furnizori, facturile si legatura intrarilor de stoc cu factura",
        [
            """
            CREATE TABLE IF NOT EXISTS `suppliers` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `name` VARCHAR(200) NOT NULL,
              `cui` VARCHAR(20) NOT NULL,
              `normalized_cui` VARCHAR(30) NOT NULL,
              `country` VARCHAR(2) NOT NULL DEFAULT 'RO',
              `address` VARCHAR(300) NOT NULL DEFAULT '',
              `phone` VARCHAR(20) NOT NULL DEFAULT '',
              `registry_number` VARCHAR(40) NOT NULL DEFAULT '',
              `postal_code` VARCHAR(10) NOT NULL DEFAULT '',
              `caen_code` VARCHAR(4) NOT NULL DEFAULT '',
              `source` VARCHAR(1) NOT NULL DEFAULT 'M',
              `verified_utc` VARCHAR(40) NOT NULL DEFAULT '',
              `created_by` VARCHAR(100) NOT NULL DEFAULT '',
              `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
              `version` BIGINT NOT NULL DEFAULT 0,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_suppliers_cui` (`normalized_cui`),
              KEY `ix_suppliers_name` (`name`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `archive_suppliers` (
              `archive_id` VARCHAR(64) NOT NULL,
              `original_id` BIGINT NOT NULL,
              `name` VARCHAR(200) NOT NULL,
              `cui` VARCHAR(20) NOT NULL,
              `country` VARCHAR(2) NOT NULL,
              `source` VARCHAR(1) NOT NULL,
              `version` BIGINT NOT NULL,
              PRIMARY KEY (`archive_id`),
              KEY `ix_archive_suppliers_original` (`original_id`),
              CONSTRAINT `fk_archive_suppliers_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `supplier_invoices` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `supplier_id` BIGINT NOT NULL,
              `number` VARCHAR(50) NOT NULL,
              `normalized_number` VARCHAR(50) NOT NULL,
              `issue_date` VARCHAR(10) NOT NULL,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_supplier_invoices_number` (`supplier_id`, `normalized_number`),
              CONSTRAINT `fk_supplier_invoices_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `invoice_id` BIGINT NULL",
            "ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_invoice` (`invoice_id`)",
            "ALTER TABLE `stock_movements` ADD CONSTRAINT `fk_stock_movements_invoice` FOREIGN KEY IF NOT EXISTS (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION",
            "ALTER TABLE `archive_stock_movements` ADD COLUMN IF NOT EXISTS `invoice_id` BIGINT NULL"
        ],
        [
            ("suppliers", "id"), ("suppliers", "name"), ("suppliers", "cui"), ("suppliers", "normalized_cui"), ("suppliers", "country"),
            ("suppliers", "address"), ("suppliers", "phone"), ("suppliers", "registry_number"), ("suppliers", "postal_code"), ("suppliers", "caen_code"),
            ("suppliers", "source"), ("suppliers", "verified_utc"), ("suppliers", "created_by"), ("suppliers", "created_utc"), ("suppliers", "version"),
            ("archive_suppliers", "archive_id"), ("archive_suppliers", "original_id"), ("archive_suppliers", "name"), ("archive_suppliers", "cui"),
            ("archive_suppliers", "country"), ("archive_suppliers", "source"), ("archive_suppliers", "version"),
            ("supplier_invoices", "id"), ("supplier_invoices", "supplier_id"), ("supplier_invoices", "number"), ("supplier_invoices", "normalized_number"),
            ("supplier_invoices", "issue_date"), ("supplier_invoices", "created_by"), ("supplier_invoices", "created_utc"),
            ("stock_movements", "invoice_id"), ("archive_stock_movements", "invoice_id")
        ])
        ,
        // A template is tied to its supplier by id (the name shown is the supplier's own, so a rename in the register reaches the template); the
        // tax id column stays for matching. Templates of a supplier already in the register are linked here; a supplier with templates cannot be deleted (RESTRICT).
        new(14, "Sabloane de facturi: legatura cu furnizorul prin supplier_id",
        [
            "ALTER TABLE `invoice_templates` ADD COLUMN IF NOT EXISTS `supplier_id` BIGINT NULL",
            "ALTER TABLE `invoice_templates` ADD INDEX IF NOT EXISTS `ix_invoice_templates_supplier` (`supplier_id`)",
            "ALTER TABLE `invoice_templates` ADD CONSTRAINT `fk_invoice_templates_supplier` FOREIGN KEY IF NOT EXISTS (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION"
        ],
        [
            ("invoice_templates", "supplier_id")
        ])
        ,
        // Other names a supplier is written with on invoices; `alias_key` (the name without legal form, dots, case) is unique: an alias points to one supplier.
        new(15, "Furnizori: denumiri alternative (alias-uri) pentru recunoasterea numelui de pe factura",
        [
            """
            CREATE TABLE IF NOT EXISTS `supplier_aliases` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `supplier_id` BIGINT NOT NULL,
              `alias` VARCHAR(200) NOT NULL,
              `alias_key` VARCHAR(200) NOT NULL,
              `created_by` VARCHAR(100) NOT NULL DEFAULT '',
              `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_supplier_aliases_key` (`alias_key`),
              KEY `ix_supplier_aliases_supplier` (`supplier_id`),
              CONSTRAINT `fk_supplier_aliases_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("supplier_aliases", "id"), ("supplier_aliases", "supplier_id"), ("supplier_aliases", "alias"), ("supplier_aliases", "alias_key"),
            ("supplier_aliases", "created_by"), ("supplier_aliases", "created_utc")
        ])
        ,
        // What the recognition of the supplier proposed for an invoice and what the user chose (to see where the reading goes wrong); one row per invoice, removed with it.
        new(16, "Furnizori: jurnal de recunoastere a furnizorului per factura preluata",
        [
            """
            CREATE TABLE IF NOT EXISTS `supplier_recognitions` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `invoice_id` BIGINT NOT NULL,
              `method` VARCHAR(10) NOT NULL,
              `confidence` VARCHAR(10) NOT NULL,
              `read_name` VARCHAR(200) NOT NULL DEFAULT '',
              `read_cui` VARCHAR(30) NOT NULL DEFAULT '',
              `recognized_supplier_id` BIGINT NULL,
              `chosen_supplier_id` BIGINT NULL,
              `corrected` TINYINT(1) NOT NULL DEFAULT 0,
              `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_supplier_recognitions_invoice` (`invoice_id`),
              CONSTRAINT `fk_supplier_recognitions_invoice` FOREIGN KEY (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("supplier_recognitions", "id"), ("supplier_recognitions", "invoice_id"), ("supplier_recognitions", "method"), ("supplier_recognitions", "confidence"),
            ("supplier_recognitions", "read_name"), ("supplier_recognitions", "read_cui"), ("supplier_recognitions", "recognized_supplier_id"),
            ("supplier_recognitions", "chosen_supplier_id"), ("supplier_recognitions", "corrected"), ("supplier_recognitions", "created_utc")
        ])
        ,
        // Whether the user chose another template than the one proposed automatically for the invoice.
        new(17, "Furnizori: jurnalul de recunoastere retine si schimbarea sablonului propus",
        [
            "ALTER TABLE `supplier_recognitions` ADD COLUMN IF NOT EXISTS `template_changed` TINYINT(1) NOT NULL DEFAULT 0"
        ],
        [
            ("supplier_recognitions", "template_changed")
        ])
        ,
        // Entries without invoice: why (1 = awaited invoice ... 5 = other), the supplier it comes from (required for an awaited invoice) and a free
        // reference. `supplier_invoice_lines` keeps the quantity written on the invoice for a product (known from the automatic pickup or typed by hand),
        // to warn when the entries of the product on that invoice exceed it.
        new(18, "Intrari libere: motiv, furnizor, referinta; cantitati facturate pe factura",
        [
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `free_entry_type` TINYINT NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `free_supplier_id` BIGINT NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `reference` VARCHAR(200) NULL",
            "ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_free_supplier` (`free_supplier_id`)",
            "ALTER TABLE `stock_movements` ADD CONSTRAINT `fk_stock_movements_free_supplier` FOREIGN KEY IF NOT EXISTS (`free_supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION",
            """
            CREATE TABLE IF NOT EXISTS `supplier_invoice_lines` (
              `invoice_id` BIGINT NOT NULL,
              `product_id` BIGINT NOT NULL,
              `quantity` INT NOT NULL,
              PRIMARY KEY (`invoice_id`, `product_id`),
              CONSTRAINT `fk_invoice_lines_invoice` FOREIGN KEY (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("stock_movements", "free_entry_type"), ("stock_movements", "free_supplier_id"), ("stock_movements", "reference"),
            ("supplier_invoice_lines", "invoice_id"), ("supplier_invoice_lines", "product_id"), ("supplier_invoice_lines", "quantity")
        ])
        ,
        // Exits over the stock: the optional cause (1 = entry not yet recorded, 2 = wrong stock in the database).
        new(19, "Iesiri peste stoc: cauza optionala",
        [
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `over_stock_cause` TINYINT NULL"
        ],
        [
            ("stock_movements", "over_stock_cause")
        ])
        ,
        // Every exit belongs to an operation (the id of its first exit; a single exit is an operation of one line). Older movements keep none.
        new(20, "Iesiri: operatia din care fac parte (operation_id)",
        [
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `operation_id` BIGINT NULL",
            "ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_operation` (`operation_id`)"
        ],
        [
            ("stock_movements", "operation_id")
        ])
        ,
        // Storno of an exit operation (the rows stay as a trace: when, by whom, why) and the exit a return from a beneficiary is tied to.
        new(21, "Storno de operatie si retur legat de iesire",
        [
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `voided_utc` VARCHAR(40) NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `void_reason` VARCHAR(500) NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `voided_by` VARCHAR(100) NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `return_of_movement_id` BIGINT NULL",
            "ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_return_of` (`return_of_movement_id`)"
        ],
        [
            ("stock_movements", "voided_utc"), ("stock_movements", "void_reason"), ("stock_movements", "voided_by"), ("stock_movements", "return_of_movement_id")
        ])
        ,
        // Nomenclator -> Tipuri de sisteme: the types (name, order, active) and their alternative names.
        new(22, "Nomenclator: tipuri de sisteme si denumirile lor alternative",
        [
            """
            CREATE TABLE IF NOT EXISTS `system_types` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `name` VARCHAR(100) NOT NULL,
              `name_key` VARCHAR(100) NOT NULL,
              `active` TINYINT(1) NOT NULL DEFAULT 1,
              `sort_order` INT NOT NULL DEFAULT 0,
              `version` BIGINT NOT NULL DEFAULT 0,
              `created_utc` VARCHAR(40) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_system_types_key` (`name_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `system_type_aliases` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `system_type_id` BIGINT NOT NULL,
              `alias` VARCHAR(100) NOT NULL,
              `alias_key` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_system_type_aliases_key` (`alias_key`),
              CONSTRAINT `fk_system_type_aliases_type` FOREIGN KEY (`system_type_id`) REFERENCES `system_types` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("system_types", "id"), ("system_types", "name"), ("system_types", "name_key"), ("system_types", "active"), ("system_types", "sort_order"),
            ("system_types", "version"), ("system_types", "created_utc"), ("system_types", "updated_utc"),
            ("system_type_aliases", "id"), ("system_type_aliases", "system_type_id"), ("system_type_aliases", "alias"), ("system_type_aliases", "alias_key"),
            ("system_type_aliases", "created_utc")
        ])
        ,
        // The components of a project: the system types it is made of, with a simple state; taken out = archived with a reason (never deleted).
        new(23, "Proiecte: componente (tipuri de sisteme) cu stare si arhivare",
        [
            """
            CREATE TABLE IF NOT EXISTS `project_components` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `project_id` BIGINT NOT NULL,
              `system_type_id` BIGINT NOT NULL,
              `state` TINYINT NOT NULL DEFAULT 1,
              `archived_utc` VARCHAR(40) NULL,
              `archive_reason` VARCHAR(500) NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              `created_utc` VARCHAR(40) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_project_components` (`project_id`, `system_type_id`),
              CONSTRAINT `fk_project_components_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
              CONSTRAINT `fk_project_components_type` FOREIGN KEY (`system_type_id`) REFERENCES `system_types` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("project_components", "id"), ("project_components", "project_id"), ("project_components", "system_type_id"), ("project_components", "state"),
            ("project_components", "archived_utc"), ("project_components", "archive_reason"), ("project_components", "version"),
            ("project_components", "created_utc"), ("project_components", "updated_utc")
        ])
        ,
        // Module Oferte: templates of offer sheets (.xlsx); the definition (columns by letter, header labels, sections, ignored rows) is JSON.
        new(24, "Oferte: sabloane de devize-oferta (xlsx)",
        [
            """
            CREATE TABLE IF NOT EXISTS `offer_templates` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `name` VARCHAR(120) NOT NULL,
              `name_key` VARCHAR(120) NOT NULL,
              `active` TINYINT(1) NOT NULL DEFAULT 1,
              `definition` LONGTEXT NOT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_offer_templates_key` (`name_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("offer_templates", "id"), ("offer_templates", "name"), ("offer_templates", "name_key"), ("offer_templates", "active"), ("offer_templates", "definition"),
            ("offer_templates", "version"), ("offer_templates", "created_by"), ("offer_templates", "created_utc"), ("offer_templates", "updated_by"), ("offer_templates", "updated_utc")
        ])
        ,
        // Offers taken over from a devize-oferta: the offer (a revision per row, same number = same offer), its lines, the ties between offer lines and
        // catalog products that were confirmed (remembered for the next offers) and the alternative names of the beneficiaries seen in offers.
        new(25, "Oferte preluate: oferta, liniile, potrivirile retinute si denumirile alternative ale beneficiarilor",
        [
            """
            CREATE TABLE IF NOT EXISTS `offers` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `number` VARCHAR(60) NOT NULL,
              `number_key` VARCHAR(60) NOT NULL,
              `revision` INT NOT NULL,
              `title` VARCHAR(300) NOT NULL DEFAULT '',
              `category` VARCHAR(120) NOT NULL DEFAULT '',
              `beneficiary_id` BIGINT NOT NULL,
              `project_id` BIGINT NOT NULL,
              `system_type_id` BIGINT NULL,
              `template_id` BIGINT NULL,
              `file_name` VARCHAR(260) NOT NULL DEFAULT '',
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_offers_number_revision` (`number_key`, `revision`),
              KEY `ix_offers_project` (`project_id`),
              CONSTRAINT `fk_offers_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `offer_lines` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `offer_id` BIGINT NOT NULL,
              `line_order` INT NOT NULL,
              `section` VARCHAR(100) NOT NULL DEFAULT '',
              `number` VARCHAR(20) NOT NULL DEFAULT '',
              `product_type` VARCHAR(100) NOT NULL DEFAULT '',
              `name` TEXT NOT NULL,
              `name_key` VARCHAR(190) NOT NULL,
              `unit` VARCHAR(30) NOT NULL DEFAULT '',
              `quantity` DECIMAL(14,3) NOT NULL,
              `in_stock` TINYINT(1) NOT NULL DEFAULT 1,
              `product_id` BIGINT NULL,
              PRIMARY KEY (`id`),
              KEY `ix_offer_lines_offer` (`offer_id`, `line_order`),
              KEY `ix_offer_lines_product` (`product_id`),
              CONSTRAINT `fk_offer_lines_offer` FOREIGN KEY (`offer_id`) REFERENCES `offers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `offer_line_matches` (
              `name_key` VARCHAR(190) NOT NULL,
              `product_id` BIGINT NOT NULL,
              `confirmed_by` VARCHAR(100) NOT NULL,
              `confirmed_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`name_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `beneficiary_aliases` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `beneficiary_id` BIGINT NOT NULL,
              `alias` VARCHAR(200) NOT NULL,
              `alias_key` VARCHAR(200) NOT NULL,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_beneficiary_aliases_key` (`alias_key`),
              CONSTRAINT `fk_beneficiary_aliases_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("offers", "id"), ("offers", "number"), ("offers", "number_key"), ("offers", "revision"), ("offers", "title"), ("offers", "category"), ("offers", "beneficiary_id"),
            ("offers", "project_id"), ("offers", "system_type_id"), ("offers", "template_id"), ("offers", "file_name"), ("offers", "created_by"), ("offers", "created_utc"),
            ("offer_lines", "id"), ("offer_lines", "offer_id"), ("offer_lines", "line_order"), ("offer_lines", "section"), ("offer_lines", "number"), ("offer_lines", "product_type"),
            ("offer_lines", "name"), ("offer_lines", "name_key"), ("offer_lines", "unit"), ("offer_lines", "quantity"), ("offer_lines", "in_stock"), ("offer_lines", "product_id"),
            ("offer_line_matches", "name_key"), ("offer_line_matches", "product_id"), ("offer_line_matches", "confirmed_by"), ("offer_line_matches", "confirmed_utc"),
            ("beneficiary_aliases", "id"), ("beneficiary_aliases", "beneficiary_id"), ("beneficiary_aliases", "alias"), ("beneficiary_aliases", "alias_key"),
            ("beneficiary_aliases", "created_by"), ("beneficiary_aliases", "created_utc")
        ])
        ,
        // Exits to a project tied to a component of the project (or marked "in afara ofertei"); component_settled = how an exit left on a taken-out component was cleared.
        new(26, "Iesiri legate de componente: componenta, in afara ofertei, lamurirea la scoaterea componentei",
        [
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `project_component_id` BIGINT NULL",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `outside_offer` TINYINT(1) NOT NULL DEFAULT 0",
            "ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `component_settled` TINYINT NULL",
            "ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_component` (`project_component_id`)"
        ],
        [
            ("stock_movements", "project_component_id"), ("stock_movements", "outside_offer"), ("stock_movements", "component_settled")
        ])
        ,
        // Rezervari pe proiect: pieces of a product held for a project (and optionally one of its components); they never change the stock.
        new(27, "Rezervari pe proiect: stoc liber = stoc - rezervari",
        [
            """
            CREATE TABLE IF NOT EXISTS `project_reservations` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `project_id` BIGINT NOT NULL,
              `project_component_id` BIGINT NULL,
              `component_key` BIGINT NOT NULL DEFAULT 0,
              `product_id` BIGINT NOT NULL,
              `quantity` INT NOT NULL,
              `version` BIGINT NOT NULL DEFAULT 0,
              `created_by` VARCHAR(100) NOT NULL,
              `created_utc` VARCHAR(40) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `ux_project_reservations` (`project_id`, `product_id`, `component_key`),
              KEY `ix_project_reservations_product` (`product_id`),
              CONSTRAINT `fk_project_reservations_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
              CONSTRAINT `fk_project_reservations_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("project_reservations", "id"), ("project_reservations", "project_id"), ("project_reservations", "project_component_id"), ("project_reservations", "component_key"),
            ("project_reservations", "product_id"), ("project_reservations", "quantity"), ("project_reservations", "version"), ("project_reservations", "created_by"),
            ("project_reservations", "created_utc"), ("project_reservations", "updated_utc")
        ]),
        // Nivel tinta pe vehicul: how many pieces of a product a vehicle should hold; used to fill the vehicle with one exit operation.
        new(28, "Nivel tinta pe vehicul: completare la nivel",
        [
            """
            CREATE TABLE IF NOT EXISTS `vehicle_target_levels` (
              `vehicle_id` BIGINT NOT NULL,
              `product_id` BIGINT NOT NULL,
              `target_quantity` INT NOT NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`vehicle_id`, `product_id`),
              KEY `ix_vehicle_target_levels_product` (`product_id`),
              CONSTRAINT `fk_vehicle_target_levels_vehicle` FOREIGN KEY (`vehicle_id`) REFERENCES `vehicles` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
              CONSTRAINT `fk_vehicle_target_levels_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("vehicle_target_levels", "vehicle_id"), ("vehicle_target_levels", "product_id"), ("vehicle_target_levels", "target_quantity"),
            ("vehicle_target_levels", "updated_by"), ("vehicle_target_levels", "updated_utc")
        ]),
        // Stoc minim pe produs si termen pe proiect: the data of the notifications "Stoc sub minim" and "Deficit la un proiect cu termen apropiat".
        new(29, "Stoc minim pe produs si termen pe proiect",
        [
            """
            CREATE TABLE IF NOT EXISTS `product_min_stock` (
              `product_id` BIGINT NOT NULL,
              `min_quantity` INT NOT NULL,
              `set_date` VARCHAR(10) NOT NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`product_id`),
              CONSTRAINT `fk_product_min_stock_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """,
            """
            CREATE TABLE IF NOT EXISTS `project_deadlines` (
              `project_id` BIGINT NOT NULL,
              `deadline` VARCHAR(10) NOT NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`project_id`),
              CONSTRAINT `fk_project_deadlines_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("product_min_stock", "product_id"), ("product_min_stock", "min_quantity"), ("product_min_stock", "set_date"), ("product_min_stock", "updated_by"),
            ("product_min_stock", "updated_utc"), ("project_deadlines", "project_id"), ("project_deadlines", "deadline"), ("project_deadlines", "updated_by"),
            ("project_deadlines", "updated_utc")
        ])
        ,
        // Backup pe NAS: one settings row (path, account, password kept encrypted, copy and schedule switches, last result).
        new(30, "Configurare backup NAS",
        [
            """
            CREATE TABLE IF NOT EXISTS `backup_nas_settings` (
              `id` TINYINT NOT NULL,
              `unc_path` VARCHAR(500) NOT NULL,
              `username` VARCHAR(100) NOT NULL,
              `password_protected` TEXT NULL,
              `copy_enabled` TINYINT(1) NOT NULL DEFAULT 0,
              `schedule_enabled` TINYINT(1) NOT NULL DEFAULT 0,
              `schedule_time` VARCHAR(5) NOT NULL DEFAULT '02:00',
              `last_scheduled_date` VARCHAR(10) NULL,
              `last_attempt_utc` VARCHAR(40) NULL,
              `last_ok` TINYINT(1) NULL,
              `last_message` VARCHAR(500) NULL,
              `updated_by` VARCHAR(100) NOT NULL,
              `updated_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("backup_nas_settings", "id"), ("backup_nas_settings", "unc_path"), ("backup_nas_settings", "username"), ("backup_nas_settings", "password_protected"),
            ("backup_nas_settings", "copy_enabled"), ("backup_nas_settings", "schedule_enabled"), ("backup_nas_settings", "schedule_time"),
            ("backup_nas_settings", "last_scheduled_date"), ("backup_nas_settings", "last_attempt_utc"), ("backup_nas_settings", "last_ok"),
            ("backup_nas_settings", "last_message"), ("backup_nas_settings", "updated_by"), ("backup_nas_settings", "updated_utc")
        ])
        ,
        // Backup NAS: how old a backup may get before the notification, and the last failed backup (cause and time) for the notification text.
        new(31, "Backup NAS: varsta maxima si ultima eroare",
        [
            """
            ALTER TABLE `backup_nas_settings`
                ADD COLUMN IF NOT EXISTS `max_age_days` INT NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS `last_error_utc` VARCHAR(40) NULL,
                ADD COLUMN IF NOT EXISTS `last_error` VARCHAR(500) NULL
            """
        ],
        [
            ("backup_nas_settings", "max_age_days"), ("backup_nas_settings", "last_error_utc"), ("backup_nas_settings", "last_error")
        ]),
        // The events that already got their starting template: a template the administrator deletes is not made again.
        new(32, "Sabloane de notificare implicite: evidenta evenimentelor tratate",
        [
            """
            CREATE TABLE IF NOT EXISTS `notification_template_seeds` (
              `source_key` VARCHAR(60) NOT NULL,
              `seeded_utc` VARCHAR(40) NOT NULL,
              PRIMARY KEY (`source_key`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """
        ],
        [
            ("notification_template_seeds", "source_key"), ("notification_template_seeds", "seeded_utc")
        ]),
        // The backup settings (schedule, age for the notification, retention) leave the NAS table: they concern the local backup, with or without a NAS.
        new(33, "Setari backup: programare, vechime, pastrare",
        [
            """
            CREATE TABLE IF NOT EXISTS `backup_settings` (
              `id` INT NOT NULL,
              `schedule_enabled` TINYINT(1) NOT NULL DEFAULT 0,
              `schedule_time` VARCHAR(5) NOT NULL DEFAULT '02:00',
              `last_scheduled_date` VARCHAR(10) NULL,
              `max_age_days` INT NOT NULL DEFAULT 2,
              `retention_enabled` TINYINT(1) NOT NULL DEFAULT 0,
              `retention_days` INT NOT NULL DEFAULT 30,
              `last_error_utc` VARCHAR(40) NULL,
              `last_error` VARCHAR(500) NULL,
              `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
              `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """
        ],
        [
            ("backup_settings", "id"), ("backup_settings", "schedule_enabled"), ("backup_settings", "schedule_time"), ("backup_settings", "last_scheduled_date"),
            ("backup_settings", "max_age_days"), ("backup_settings", "retention_enabled"), ("backup_settings", "retention_days"), ("backup_settings", "last_error_utc"),
            ("backup_settings", "last_error"), ("backup_settings", "updated_by"), ("backup_settings", "updated_utc")
        ]),
        // The check of the server clock against the internet time: the NTP servers (Settings -> Backup) and the last clock problem found (notification).
        new(34, "Setari backup: verificare ora pe internet",
        [
            """
            ALTER TABLE `backup_settings`
                ADD COLUMN IF NOT EXISTS `ntp_check_enabled` TINYINT(1) NOT NULL DEFAULT 1,
                ADD COLUMN IF NOT EXISTS `ntp_servers` VARCHAR(400) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS `clock_issue_utc` VARCHAR(40) NULL,
                ADD COLUMN IF NOT EXISTS `clock_skew_minutes` INT NOT NULL DEFAULT 0
            """
        ],
        [
            ("backup_settings", "ntp_check_enabled"), ("backup_settings", "ntp_servers"), ("backup_settings", "clock_issue_utc"), ("backup_settings", "clock_skew_minutes")
        ]),
        // The days of the week of the scheduled backup (mask: Monday = 1 ... Sunday = 64; 127 = every day, as before).
        new(35, "Setari backup: zilele saptamanii",
        [
            "ALTER TABLE `backup_settings` ADD COLUMN IF NOT EXISTS `schedule_days` INT NOT NULL DEFAULT 127"
        ],
        [
            ("backup_settings", "schedule_days")
        ]),
        // The internal code a supplier gives a product on its invoices, linked to the product of the catalog; one row per supplier and code, removed with either of them.
        new(36, "Furnizori: legatura dintre codul intern al furnizorului si produsul din catalog",
        [
            """
            CREATE TABLE IF NOT EXISTS `supplier_product_codes` (
              `id` BIGINT NOT NULL AUTO_INCREMENT,
              `supplier_id` BIGINT NOT NULL,
              `code_key` VARCHAR(100) NOT NULL,
              `code` VARCHAR(100) NOT NULL,
              `product_id` BIGINT NOT NULL,
              `created_by` VARCHAR(100) NOT NULL DEFAULT '',
              `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
              `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
              `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
              PRIMARY KEY (`id`),
              UNIQUE KEY `uq_supplier_product_codes_key` (`supplier_id`, `code_key`),
              KEY `ix_supplier_product_codes_product` (`product_id`),
              CONSTRAINT `fk_supplier_product_codes_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
              CONSTRAINT `fk_supplier_product_codes_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
            """
        ],
        [
            ("supplier_product_codes", "id"), ("supplier_product_codes", "supplier_id"), ("supplier_product_codes", "code_key"), ("supplier_product_codes", "code"),
            ("supplier_product_codes", "product_id"), ("supplier_product_codes", "created_by"), ("supplier_product_codes", "created_utc"),
            ("supplier_product_codes", "updated_by"), ("supplier_product_codes", "updated_utc")
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
        await NormalizeBeneficiaryKeysAsync(cancellationToken).ConfigureAwait(false);
        await LinkTemplateSuppliersAsync(cancellationToken).ConfigureAwait(false);
        return new(true, applied, await FindMissingColumnsAsync(cancellationToken).ConfigureAwait(false));
    }

    // Templates of a supplier already in the register are linked to it by id (migration 14 adds the column; the migrator account has no UPDATE
    // right, so the link is written with the application account). Idempotent.
    private async Task LinkTemplateSuppliersAsync(CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            UPDATE `invoice_templates` t INNER JOIN `suppliers` s ON s.normalized_cui COLLATE utf8mb4_nopad_bin = t.supplier_cui COLLATE utf8mb4_nopad_bin AND s.country = 'RO'
            SET t.supplier_id = s.id WHERE t.supplier_id IS NULL
            """, connection) { CommandTimeout = 60 };
        try { await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
        catch (MySqlException exception) when (exception.Number == 1054) { }   // the column is not there yet (migration not applied)
    }

    // The key of a legal-person beneficiary is the CUI digits ("RO123" and "123" are one). Rows saved with the old key are rewritten with the
    // application account (the migrator account has no UPDATE right); IGNORE keeps the old key of a pair that would collide. Idempotent.
    private async Task NormalizeBeneficiaryKeysAsync(CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnections.Create(configuration);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand("""
            UPDATE IGNORE `beneficiaries`
            SET `normalized_cui` = TRIM(LEADING '0' FROM IF(`normalized_cui` LIKE 'RO%', SUBSTRING(`normalized_cui`, 3), `normalized_cui`))
            WHERE `normalized_cui` REGEXP '^(RO)?0*[0-9]+$' AND (`normalized_cui` LIKE 'RO%' OR `normalized_cui` LIKE '0%')
            """, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
