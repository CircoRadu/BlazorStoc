-- version: 35
-- description: Setari backup: zilele saptamanii
-- note: The days of the week of the scheduled backup (mask: Monday = 1 ... Sunday = 64; 127 = every day, as before).
-- column: backup_settings.schedule_days
-- statement
ALTER TABLE `backup_settings` ADD COLUMN IF NOT EXISTS `schedule_days` INT NOT NULL DEFAULT 127
