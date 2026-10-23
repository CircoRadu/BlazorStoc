-- version: 44
-- description: Preluare tichete ActivityApp: setarile conexiunii (cont doar citire), tipurile de tichete alese de administrator si starea structurii sursei
-- note: One row of settings (id 1), written only by the administrator (Setari -> Preluare ActivityApp). The password of the read-only ActivityApp account is stored
-- note: encrypted (ASP.NET Data Protection) and never shown again. activity_import_types keeps the ticket types the administrator chose, by the ID of the type in
-- note: ActivityApp (the name is the one seen at the choice; a different name for the same ID means "changed in source"). The structure columns remember the last check of
-- note: the ActivityApp tables against the contract of the module (fingerprint, status, report). Nothing here writes to ActivityApp.
-- column: activity_import_settings.id
-- column: activity_import_settings.enabled
-- column: activity_import_settings.server_name
-- column: activity_import_settings.server_port
-- column: activity_import_settings.database_name
-- column: activity_import_settings.username
-- column: activity_import_settings.password_protected
-- column: activity_import_settings.since_date
-- column: activity_import_settings.window_from_hour
-- column: activity_import_settings.window_to_hour
-- column: activity_import_settings.page_size
-- column: activity_import_settings.fill_empty_fields
-- column: activity_import_settings.last_test_utc
-- column: activity_import_settings.last_test_ok
-- column: activity_import_settings.last_test_message
-- column: activity_import_settings.structure_fingerprint
-- column: activity_import_settings.structure_status
-- column: activity_import_settings.structure_report
-- column: activity_import_settings.structure_checked_utc
-- column: activity_import_settings.updated_by
-- column: activity_import_settings.updated_utc
-- column: activity_import_types.source_type_id
-- column: activity_import_types.source_name
-- column: activity_import_types.active
-- column: activity_import_types.destination
-- column: activity_import_types.chosen_by
-- column: activity_import_types.chosen_utc
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_settings` (
  `id` INT NOT NULL,
  `enabled` TINYINT(1) NOT NULL DEFAULT 0,
  `server_name` VARCHAR(200) NOT NULL DEFAULT '',
  `server_port` INT NOT NULL DEFAULT 1433,
  `database_name` VARCHAR(128) NOT NULL DEFAULT 'TIPActivity',
  `username` VARCHAR(128) NOT NULL DEFAULT '',
  `password_protected` LONGTEXT NULL,
  `since_date` DATE NULL,
  `window_from_hour` INT NOT NULL DEFAULT 0,
  `window_to_hour` INT NOT NULL DEFAULT 24,
  `page_size` INT NOT NULL DEFAULT 200,
  `fill_empty_fields` TINYINT(1) NOT NULL DEFAULT 0,
  `last_test_utc` DATETIME NULL,
  `last_test_ok` TINYINT(1) NULL,
  `last_test_message` VARCHAR(500) NOT NULL DEFAULT '',
  `structure_fingerprint` VARCHAR(64) NOT NULL DEFAULT '',
  `structure_status` VARCHAR(20) NOT NULL DEFAULT '',
  `structure_report` LONGTEXT NULL,
  `structure_checked_utc` DATETIME NULL,
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` DATETIME NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_types` (
  `source_type_id` BIGINT NOT NULL,
  `source_name` VARCHAR(200) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `destination` VARCHAR(20) NOT NULL DEFAULT 'OnDemand',
  `chosen_by` VARCHAR(100) NOT NULL,
  `chosen_utc` DATETIME NOT NULL,
  PRIMARY KEY (`source_type_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
