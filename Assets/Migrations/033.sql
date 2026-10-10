-- version: 33
-- description: Setari backup: programare, vechime, pastrare
-- note: The backup settings (schedule, age for the notification, retention) leave the NAS table: they concern the local backup, with or without a NAS.
-- column: backup_settings.id
-- column: backup_settings.schedule_enabled
-- column: backup_settings.schedule_time
-- column: backup_settings.last_scheduled_date
-- column: backup_settings.max_age_days
-- column: backup_settings.retention_enabled
-- column: backup_settings.retention_days
-- column: backup_settings.last_error_utc
-- column: backup_settings.last_error
-- column: backup_settings.updated_by
-- column: backup_settings.updated_utc
-- statement
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
