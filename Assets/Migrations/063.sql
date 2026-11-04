-- version: 63
-- description: Lista tipurilor de tichete citita din ActivityApp se pastreaza (activity_import_settings.known_types)
-- note: Setari -> Preluare ActivityApp -> Tipuri de tichete: the types read from ActivityApp (chosen or not) are kept as the last list read, so the list is complete when the page opens, not only the chosen types.
-- column: activity_import_settings.known_types
-- column: activity_import_settings.known_types_utc
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `known_types` LONGTEXT NULL
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `known_types_utc` VARCHAR(40) NULL
