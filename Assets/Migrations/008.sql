-- version: 8
-- description: Contracte de mentenanta: contracte (numar, data, ciclicitate, expirare, On/Off), acoperirea punctelor de lucru cu scadenta si arhivarea contractelor
-- note: Maintenance contracts of a beneficiary and their coverage of work points. The unique key on active_work_point_id stands in for a
-- note: partial unique index (MariaDB has none): it is equal to work_point_id while the contract is active and NULL otherwise, so a work
-- note: point is in at most one ACTIVE contract and in any number of Off ones. The application sets it in the same transaction that
-- note: activates/deactivates the contract or adds/removes/moves a point.
-- column: service_contracts.id
-- column: service_contracts.beneficiary_id
-- column: service_contracts.contract_number
-- column: service_contracts.contract_date
-- column: service_contracts.cycle_months
-- column: service_contracts.valid_until
-- column: service_contracts.is_active
-- column: service_contracts.notes
-- column: service_contracts.version
-- column: service_contract_points.id
-- column: service_contract_points.contract_id
-- column: service_contract_points.work_point_id
-- column: service_contract_points.active_work_point_id
-- column: service_contract_points.cycle_months
-- column: service_contract_points.next_due
-- column: service_contract_points.version
-- column: archive_service_contracts.archive_id
-- column: archive_service_contracts.original_id
-- column: archive_service_contracts.beneficiary_id
-- column: archive_service_contracts.contract_number
-- column: archive_service_contracts.contract_date
-- column: archive_service_contracts.cycle_months
-- column: archive_service_contracts.valid_until
-- column: archive_service_contracts.is_active
-- column: archive_service_contracts.notes
-- column: archive_service_contracts.version
-- statement
CREATE TABLE IF NOT EXISTS `service_contracts` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `contract_number` VARCHAR(30) NOT NULL,
  `contract_date` DATE NOT NULL,
  `cycle_months` TINYINT NOT NULL,
  `valid_until` DATE NULL,
  `is_active` TINYINT NOT NULL DEFAULT 1,
  `notes` VARCHAR(1000) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_contracts_number` (`beneficiary_id`, `contract_number`, `contract_date`),
  CONSTRAINT `fk_service_contracts_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_service_contracts_cycle` CHECK (`cycle_months` BETWEEN 1 AND 12),
  CONSTRAINT `ck_service_contracts_valid_until` CHECK (`valid_until` IS NULL OR `valid_until` >= `contract_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `service_contract_points` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `contract_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `active_work_point_id` BIGINT NULL,
  `cycle_months` TINYINT NULL,
  `next_due` DATE NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_contract_points_pair` (`contract_id`, `work_point_id`),
  UNIQUE KEY `uq_service_contract_points_active` (`active_work_point_id`),
  KEY `ix_service_contract_points_work_point` (`work_point_id`),
  CONSTRAINT `fk_service_contract_points_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_contract_points_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_service_contract_points_cycle` CHECK (`cycle_months` IS NULL OR `cycle_months` BETWEEN 1 AND 12),
  CONSTRAINT `ck_service_contract_points_active` CHECK (`active_work_point_id` IS NULL OR `active_work_point_id` = `work_point_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_service_contracts` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `contract_number` LONGTEXT NOT NULL,
  `contract_date` DATE NOT NULL,
  `cycle_months` INT NOT NULL,
  `valid_until` DATE NULL,
  `is_active` TINYINT NOT NULL,
  `notes` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_service_contracts_original` (`original_id`),
  CONSTRAINT `fk_archive_service_contracts_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
