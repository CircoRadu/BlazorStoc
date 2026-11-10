-- version: 84
-- description: Rezultatele tichetelor ActivityApp decise (preluare separata de modulul Mentenanta)
-- note: activity_ticket_outcomes holds what the operator decided for a ticket (maintenance or service on demand) until the maintenance module records the intervention; pending = decided and not applied yet
-- column: activity_ticket_outcomes.id
-- column: activity_ticket_outcomes.ticket_id
-- column: activity_ticket_outcomes.status
-- column: activity_ticket_outcomes.intervention_id
-- statement
CREATE TABLE IF NOT EXISTS `activity_ticket_outcomes` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `ticket_id` BIGINT NOT NULL,
  `beneficiary_id` INT NOT NULL,
  `work_point_id` INT NOT NULL,
  `category` VARCHAR(1) NOT NULL DEFAULT 'M',
  `performed_on` DATE NOT NULL,
  `notes` TEXT NOT NULL,
  `component_ids` VARCHAR(500) NOT NULL DEFAULT '',
  `status` VARCHAR(10) NOT NULL DEFAULT 'pending',
  `intervention_id` INT NULL,
  `error` VARCHAR(500) NOT NULL DEFAULT '',
  `decided_by` VARCHAR(100) NOT NULL DEFAULT '',
  `decided_utc` DATETIME NOT NULL,
  `claimed_utc` DATETIME NULL,
  `applied_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_activity_ticket_outcomes_ticket` (`ticket_id`),
  KEY `ix_activity_ticket_outcomes_status` (`status`, `performed_on`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
