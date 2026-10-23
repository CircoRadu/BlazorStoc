-- version: 50
-- description: Preluare ActivityApp: citire incrementala, beneficiari de verificat, coada tichetelor in asteptare, discard si validarea oricarei potriviri cu un cuvant cheie
-- note: source_watermark is the latest modification date of a closing event seen by the last successful ticket import (the next one reads from one day before it); null = read everything.
-- note: activity_import_partner_checks is the queue of the partners of ActivityApp that need a human look: ANAF could not answer (anaf_unavailable), does not know the CUI (cui_unknown), the CUI has
-- note: the wrong form (cui_invalid_form) or the address is missing (missing_address). Status pending, resolved (verified or accepted) or discarded. beneficiary_id is empty while no beneficiary exists.
-- note: activity_import_tickets keeps the resolved tickets that cannot be taken yet (waiting_beneficiary: the beneficiary is still to be verified), the ones taken (imported, with the intervention) and
-- note: the ones discarded for good (discarded: never read again). The fields hold what the ticket import needs to plan the ticket again without reading ActivityApp.
-- note: activity_import_suggestions gets the intervention made at once for the ticket (the operator confirms it as maintenance, keeps it on demand or discards it) and the kind of match (exact, approximate).
-- column: activity_import_settings.source_watermark
-- column: activity_import_partner_checks.id
-- column: activity_import_partner_checks.partner_id
-- column: activity_import_partner_checks.name
-- column: activity_import_partner_checks.cui_as_typed
-- column: activity_import_partner_checks.reason
-- column: activity_import_partner_checks.status
-- column: activity_import_partner_checks.beneficiary_id
-- column: activity_import_partner_checks.note
-- column: activity_import_partner_checks.created_utc
-- column: activity_import_partner_checks.last_check_utc
-- column: activity_import_partner_checks.decided_by
-- column: activity_import_partner_checks.decided_utc
-- column: activity_import_tickets.ticket_id
-- column: activity_import_tickets.type_id
-- column: activity_import_tickets.partner_id
-- column: activity_import_tickets.work_point_id
-- column: activity_import_tickets.event_id
-- column: activity_import_tickets.event_status
-- column: activity_import_tickets.ticket_date
-- column: activity_import_tickets.event_end
-- column: activity_import_tickets.event_modified
-- column: activity_import_tickets.ticket_number
-- column: activity_import_tickets.title
-- column: activity_import_tickets.problem
-- column: activity_import_tickets.executor
-- column: activity_import_tickets.report
-- column: activity_import_tickets.state
-- column: activity_import_tickets.intervention_id
-- column: activity_import_tickets.created_utc
-- column: activity_import_tickets.updated_utc
-- column: activity_import_tickets.decided_by
-- column: activity_import_tickets.decided_utc
-- column: activity_import_suggestions.intervention_id
-- column: activity_import_suggestions.kind
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `source_watermark` DATETIME NULL
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_partner_checks` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `partner_id` BIGINT NOT NULL,
  `name` VARCHAR(300) NOT NULL DEFAULT '',
  `cui_as_typed` VARCHAR(100) NOT NULL DEFAULT '',
  `reason` VARCHAR(30) NOT NULL,
  `status` VARCHAR(10) NOT NULL DEFAULT 'pending',
  `beneficiary_id` INT NULL,
  `note` VARCHAR(500) NOT NULL DEFAULT '',
  `created_utc` DATETIME NOT NULL,
  `last_check_utc` DATETIME NULL,
  `decided_by` VARCHAR(100) NULL,
  `decided_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_activity_import_partner_checks_partner` (`partner_id`),
  KEY `ix_activity_import_partner_checks_status` (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_tickets` (
  `ticket_id` BIGINT NOT NULL,
  `type_id` BIGINT NOT NULL,
  `partner_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL DEFAULT 0,
  `event_id` BIGINT NOT NULL DEFAULT 0,
  `event_status` BIGINT NULL,
  `ticket_date` DATETIME NULL,
  `event_end` DATETIME NULL,
  `event_modified` DATETIME NULL,
  `ticket_number` VARCHAR(100) NOT NULL DEFAULT '',
  `title` VARCHAR(500) NOT NULL DEFAULT '',
  `problem` TEXT NULL,
  `executor` VARCHAR(200) NOT NULL DEFAULT '',
  `report` MEDIUMTEXT NULL,
  `state` VARCHAR(24) NOT NULL,
  `intervention_id` INT NULL,
  `created_utc` DATETIME NOT NULL,
  `updated_utc` DATETIME NOT NULL,
  `decided_by` VARCHAR(100) NULL,
  `decided_utc` DATETIME NULL,
  PRIMARY KEY (`ticket_id`),
  KEY `ix_activity_import_tickets_state` (`state`),
  KEY `ix_activity_import_tickets_partner` (`partner_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `activity_import_suggestions` ADD COLUMN IF NOT EXISTS `intervention_id` INT NULL, ADD COLUMN IF NOT EXISTS `kind` VARCHAR(12) NOT NULL DEFAULT 'approximate'
