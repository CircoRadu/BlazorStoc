-- version: 9
-- description: Registru de interventii: interventii de mentenanta si la cerere (snapshot, scadenta inchisa si aleasa), legatura fotografiilor si arhivarea lor
-- note: The register of interventions. kind M = maintenance (under an active contract, moves the due date of the point), C = on demand
-- note: (any work point, outside the cycle). The work point name/address and the contract label are snapshots taken when it is recorded.
-- note: A maintenance intervention that moved the due date keeps the due it closed (planned_due), how the next one was chosen
-- note: (next_due_basis E from the date performed / P from the planned date / O chosen) and the due it set (next_due_set); one entered
-- note: with a date older than the latest maintenance intervention of the point moves nothing and has all three empty.
-- column: service_interventions.id
-- column: service_interventions.kind
-- column: service_interventions.beneficiary_id
-- column: service_interventions.work_point_id
-- column: service_interventions.contract_id
-- column: service_interventions.work_point_name
-- column: service_interventions.work_point_address
-- column: service_interventions.contract_label
-- column: service_interventions.performed_on
-- column: service_interventions.planned_due
-- column: service_interventions.next_due_basis
-- column: service_interventions.next_due_set
-- column: service_interventions.notes
-- column: service_interventions.recorded_by
-- column: service_interventions.recorded_utc
-- column: service_interventions.version
-- column: archive_service_interventions.archive_id
-- column: archive_service_interventions.original_id
-- column: archive_service_interventions.kind
-- column: archive_service_interventions.beneficiary_id
-- column: archive_service_interventions.work_point_id
-- column: archive_service_interventions.contract_id
-- column: archive_service_interventions.work_point_name
-- column: archive_service_interventions.work_point_address
-- column: archive_service_interventions.contract_label
-- column: archive_service_interventions.performed_on
-- column: archive_service_interventions.planned_due
-- column: archive_service_interventions.next_due_basis
-- column: archive_service_interventions.next_due_set
-- column: archive_service_interventions.notes
-- column: archive_service_interventions.recorded_by
-- column: archive_service_interventions.recorded_utc
-- column: archive_service_interventions.version
-- statement
CREATE TABLE IF NOT EXISTS `service_interventions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `kind` CHAR(1) NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `contract_id` BIGINT NULL,
  `work_point_name` VARCHAR(200) NOT NULL,
  `work_point_address` VARCHAR(300) NOT NULL,
  `contract_label` VARCHAR(60) NULL,
  `performed_on` DATE NOT NULL,
  `planned_due` DATE NULL,
  `next_due_basis` CHAR(1) NULL,
  `next_due_set` DATE NULL,
  `notes` VARCHAR(2000) NOT NULL DEFAULT '',
  `recorded_by` VARCHAR(100) NOT NULL,
  `recorded_utc` VARCHAR(40) NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_service_interventions_beneficiary` (`beneficiary_id`, `performed_on`),
  KEY `ix_service_interventions_work_point` (`work_point_id`, `kind`, `performed_on`),
  KEY `ix_service_interventions_contract` (`contract_id`),
  CONSTRAINT `fk_service_interventions_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_interventions_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_interventions_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_service_interventions_kind` CHECK (`kind` IN ('M', 'C')),
  CONSTRAINT `ck_service_interventions_contract` CHECK ((`kind` = 'M' AND `contract_id` IS NOT NULL AND `contract_label` IS NOT NULL) OR (`kind` = 'C' AND `contract_id` IS NULL AND `contract_label` IS NULL AND `planned_due` IS NULL AND `next_due_basis` IS NULL AND `next_due_set` IS NULL)),
  CONSTRAINT `ck_service_interventions_due` CHECK ((`planned_due` IS NULL) = (`next_due_basis` IS NULL) AND (`next_due_basis` IS NULL) = (`next_due_set` IS NULL)),
  CONSTRAINT `ck_service_interventions_basis` CHECK (`next_due_basis` IS NULL OR `next_due_basis` IN ('E', 'P', 'O')),
  CONSTRAINT `ck_service_interventions_next_due` CHECK (`next_due_set` IS NULL OR `next_due_set` > `performed_on`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `service_photos` ADD FOREIGN KEY IF NOT EXISTS `fk_service_photos_intervention` (`intervention_id`) REFERENCES `service_interventions` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
-- statement
CREATE TABLE IF NOT EXISTS `archive_service_interventions` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `kind` VARCHAR(1) NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `contract_id` BIGINT NULL,
  `work_point_name` LONGTEXT NOT NULL,
  `work_point_address` LONGTEXT NOT NULL,
  `contract_label` LONGTEXT NULL,
  `performed_on` DATE NOT NULL,
  `planned_due` DATE NULL,
  `next_due_basis` VARCHAR(1) NULL,
  `next_due_set` DATE NULL,
  `notes` LONGTEXT NOT NULL,
  `recorded_by` LONGTEXT NOT NULL,
  `recorded_utc` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_service_interventions_original` (`original_id`),
  CONSTRAINT `fk_archive_service_interventions_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
