-- version: 31
-- description: Backup NAS: varsta maxima si ultima eroare
-- note: Backup NAS: how old a backup may get before the notification, and the last failed backup (cause and time) for the notification text.
-- column: backup_nas_settings.max_age_days
-- column: backup_nas_settings.last_error_utc
-- column: backup_nas_settings.last_error
-- statement
ALTER TABLE `backup_nas_settings`
    ADD COLUMN IF NOT EXISTS `max_age_days` INT NOT NULL DEFAULT 2,
    ADD COLUMN IF NOT EXISTS `last_error_utc` VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS `last_error` VARCHAR(500) NULL
