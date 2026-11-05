-- version: 64
-- description: Preluarea automata a backup-ului Elcomtur de pe un director (NAS): setari si rezultatul ultimei verificari (elcomtur_auto_settings)
-- note: Setari -> Preluare Elcomtur: one row (id 1). Once a day at 04:00 the newest file of the directory that matches the mask is looked for; when it is newer than the last import it is copied to the server, checked and imported.
-- note: The password of the account is kept encrypted (Data Protection) and is never shown again, logged or audited.
-- column: elcomtur_auto_settings.id
-- column: elcomtur_auto_settings.enabled
-- column: elcomtur_auto_settings.directory_path
-- column: elcomtur_auto_settings.username
-- column: elcomtur_auto_settings.password_protected
-- column: elcomtur_auto_settings.file_mask
-- column: elcomtur_auto_settings.firm_folder
-- column: elcomtur_auto_settings.firm_name
-- column: elcomtur_auto_settings.alert_days
-- column: elcomtur_auto_settings.last_scheduled_date
-- column: elcomtur_auto_settings.last_check_utc
-- column: elcomtur_auto_settings.last_check_ok
-- column: elcomtur_auto_settings.last_message
-- column: elcomtur_auto_settings.failing_since_utc
-- column: elcomtur_auto_settings.last_file_name
-- column: elcomtur_auto_settings.last_file_modified_utc
-- column: elcomtur_auto_settings.last_import_utc
-- column: elcomtur_auto_settings.last_data_date
-- column: elcomtur_auto_settings.last_summary
-- column: elcomtur_auto_settings.updated_by
-- column: elcomtur_auto_settings.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `elcomtur_auto_settings` (
  `id` INT NOT NULL,
  `enabled` TINYINT(1) NOT NULL DEFAULT 0,
  `directory_path` VARCHAR(500) NOT NULL DEFAULT '',
  `username` VARCHAR(100) NOT NULL DEFAULT '',
  `password_protected` TEXT NULL,
  `file_mask` VARCHAR(200) NOT NULL DEFAULT '',
  `firm_folder` VARCHAR(50) NOT NULL DEFAULT '',
  `firm_name` VARCHAR(100) NOT NULL DEFAULT '',
  `alert_days` INT NOT NULL DEFAULT 3,
  `last_scheduled_date` VARCHAR(10) NULL,
  `last_check_utc` VARCHAR(40) NULL,
  `last_check_ok` TINYINT(1) NULL,
  `last_message` VARCHAR(500) NULL,
  `failing_since_utc` VARCHAR(40) NULL,
  `last_file_name` VARCHAR(260) NULL,
  `last_file_modified_utc` VARCHAR(40) NULL,
  `last_import_utc` VARCHAR(40) NULL,
  `last_data_date` VARCHAR(10) NULL,
  `last_summary` VARCHAR(500) NULL,
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
