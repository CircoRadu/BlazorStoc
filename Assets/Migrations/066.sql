-- version: 66
-- description: Ora verificarii zilnice a preluarii automate Elcomtur, aleasa din setari: elcomtur_auto_settings.scheduled_hour
-- note: The hour (0-23, local time of the server) from which the daily check is due; 4 (04:00) for the installations that existed before the choice.
-- column: elcomtur_auto_settings.scheduled_hour
-- statement
ALTER TABLE `elcomtur_auto_settings` ADD COLUMN IF NOT EXISTS `scheduled_hour` TINYINT NOT NULL DEFAULT 4
