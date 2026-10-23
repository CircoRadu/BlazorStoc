-- version: 49
-- description: Preluare ActivityApp: nomenclatorul cuvintelor cheie de mentenanta, evenimentele de validat si programarea sincronizarii (interval sau ore fixe)
-- note: activity_import_keywords is the nomenclator of the words that, found in what was done in a ticket (the resolution of the closing event), show that maintenance was also done: such a ticket
-- note: is recorded as a maintenance intervention when the beneficiary has an active maintenance contract covering the work point (it then moves the next due date), else as on demand.
-- note: keyword_key is the keyword without case, diacritics or punctuation (unique).
-- note: activity_import_suggestions: a ticket whose closing text holds a word that only resembles a keyword (typed incompletely or wrongly, often from the mobile application) waits here for
-- note: the administrator to say whether it is maintenance (status pending, confirmed, rejected); it is taken after the decision. The notification source lists the pending ones.
-- note: sync_mode is "interval" (every sync_interval_minutes, inside the time window) or "times" (at the fixed hours of sync_times, a list HH:mm separated by comma).
-- column: activity_import_keywords.id
-- column: activity_import_keywords.keyword
-- column: activity_import_keywords.keyword_key
-- column: activity_import_keywords.active
-- column: activity_import_keywords.created_by
-- column: activity_import_keywords.created_utc
-- column: activity_import_keywords.version
-- column: activity_import_settings.sync_mode
-- column: activity_import_settings.sync_times
-- column: activity_import_suggestions.id
-- column: activity_import_suggestions.ticket_id
-- column: activity_import_suggestions.type_id
-- column: activity_import_suggestions.beneficiary_id
-- column: activity_import_suggestions.work_point_id
-- column: activity_import_suggestions.ticket_number
-- column: activity_import_suggestions.title
-- column: activity_import_suggestions.resolution
-- column: activity_import_suggestions.keyword
-- column: activity_import_suggestions.found
-- column: activity_import_suggestions.performed_on
-- column: activity_import_suggestions.status
-- column: activity_import_suggestions.created_utc
-- column: activity_import_suggestions.decided_by
-- column: activity_import_suggestions.decided_utc
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `sync_mode` VARCHAR(10) NOT NULL DEFAULT 'interval', ADD COLUMN IF NOT EXISTS `sync_times` VARCHAR(200) NOT NULL DEFAULT ''
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_suggestions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `ticket_id` BIGINT NOT NULL,
  `type_id` BIGINT NOT NULL,
  `beneficiary_id` INT NOT NULL,
  `work_point_id` INT NOT NULL,
  `ticket_number` VARCHAR(100) NOT NULL DEFAULT '',
  `title` VARCHAR(500) NOT NULL DEFAULT '',
  `resolution` TEXT NULL,
  `keyword` VARCHAR(100) NOT NULL,
  `found` VARCHAR(200) NOT NULL,
  `performed_on` DATE NOT NULL,
  `status` VARCHAR(10) NOT NULL DEFAULT 'pending',
  `created_utc` DATETIME NOT NULL,
  `decided_by` VARCHAR(100) NULL,
  `decided_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_activity_import_suggestions_ticket` (`ticket_id`),
  KEY `ix_activity_import_suggestions_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_keywords` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `keyword` VARCHAR(60) NOT NULL,
  `keyword_key` VARCHAR(60) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `created_utc` DATETIME NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_activity_import_keywords_key` (`keyword_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
