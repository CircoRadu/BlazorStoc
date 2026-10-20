-- version: 34
-- description: Setari backup: verificare ora pe internet
-- note: The check of the server clock against the internet time: the NTP servers (Settings -> Backup) and the last clock problem found (notification).
-- column: backup_settings.ntp_check_enabled
-- column: backup_settings.ntp_servers
-- column: backup_settings.clock_issue_utc
-- column: backup_settings.clock_skew_minutes
-- statement
ALTER TABLE `backup_settings`
    ADD COLUMN IF NOT EXISTS `ntp_check_enabled` TINYINT(1) NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS `ntp_servers` VARCHAR(400) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS `clock_issue_utc` VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS `clock_skew_minutes` INT NOT NULL DEFAULT 0
