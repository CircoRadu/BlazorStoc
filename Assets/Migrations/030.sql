-- version: 30
-- description: Configurare backup NAS
-- note: Backup pe NAS: one settings row (path, account, password kept encrypted, copy and schedule switches, last result).
-- column: backup_nas_settings.id
-- column: backup_nas_settings.unc_path
-- column: backup_nas_settings.username
-- column: backup_nas_settings.password_protected
-- column: backup_nas_settings.copy_enabled
-- column: backup_nas_settings.schedule_enabled
-- column: backup_nas_settings.schedule_time
-- column: backup_nas_settings.last_scheduled_date
-- column: backup_nas_settings.last_attempt_utc
-- column: backup_nas_settings.last_ok
-- column: backup_nas_settings.last_message
-- column: backup_nas_settings.updated_by
-- column: backup_nas_settings.updated_utc
-- statement
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
