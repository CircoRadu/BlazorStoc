-- version: 77
-- description: Contracte de mentenanta cu componente: data de referinta, inghetare si unitati (familii / componente discrete)
-- note: A contract now has a reference date (the day the cycle is counted from; mandatory for a new or edited contract), an optional global cycle (cycle_months may be NULL) and UNITS: a unit is a maintenance FAMILY (its members can be switched On/Off in service_contract_unit_members) or a DISCRETE component; cycle and reference date are global (NULL on the unit) or given per unit, never per member of a family. is_frozen / missing_info are recomputed by the application at every change of the contract: a frozen contract (incomplete data) produces no due dates, no due notifications and its interventions do not move dates. Every contract that exists when the migration runs is frozen at once (the column is added with the default 1, then the default is set to 0; the migration account has no UPDATE right), as it has no reference date and no units and shows in the "Contracte incomplete" tab of the notifications.
-- column: service_contracts.reference_date
-- column: service_contracts.is_frozen
-- column: service_contracts.missing_info
-- column: archive_service_contracts.reference_date
-- column: archive_service_contracts.is_frozen
-- column: archive_service_contracts.missing_info
-- column: service_contract_units.id
-- column: service_contract_units.contract_id
-- column: service_contract_units.family_id
-- column: service_contract_units.component_id
-- column: service_contract_units.cycle_months
-- column: service_contract_units.reference_date
-- column: service_contract_units.is_active
-- column: service_contract_units.position
-- column: service_contract_units.version
-- column: service_contract_unit_members.unit_id
-- column: service_contract_unit_members.component_id
-- column: service_contract_unit_members.is_active
-- statement
ALTER TABLE `service_contracts` ADD COLUMN IF NOT EXISTS `reference_date` DATE NULL
-- statement
ALTER TABLE `service_contracts` ADD COLUMN IF NOT EXISTS `is_frozen` TINYINT(1) NOT NULL DEFAULT 1
-- statement
ALTER TABLE `service_contracts` ALTER COLUMN `is_frozen` SET DEFAULT 0
-- statement
ALTER TABLE `service_contracts` ADD COLUMN IF NOT EXISTS `missing_info` VARCHAR(500) NOT NULL DEFAULT ''
-- statement
ALTER TABLE `service_contracts` MODIFY COLUMN `cycle_months` TINYINT NULL
-- statement
ALTER TABLE `archive_service_contracts` MODIFY COLUMN `cycle_months` TINYINT NULL
-- statement
ALTER TABLE `archive_service_contracts` ADD COLUMN IF NOT EXISTS `reference_date` DATE NULL
-- statement
ALTER TABLE `archive_service_contracts` ADD COLUMN IF NOT EXISTS `is_frozen` TINYINT(1) NOT NULL DEFAULT 0
-- statement
ALTER TABLE `archive_service_contracts` ADD COLUMN IF NOT EXISTS `missing_info` VARCHAR(500) NOT NULL DEFAULT ''
-- statement
CREATE TABLE IF NOT EXISTS `service_contract_units` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `contract_id` BIGINT NOT NULL,
  `family_id` BIGINT NULL,
  `component_id` BIGINT NULL,
  `cycle_months` TINYINT NULL,
  `reference_date` DATE NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  `position` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_contract_units_family` (`contract_id`, `family_id`),
  UNIQUE KEY `uq_service_contract_units_component` (`contract_id`, `component_id`),
  KEY `ix_service_contract_units_family` (`family_id`),
  KEY `ix_service_contract_units_component` (`component_id`),
  CONSTRAINT `fk_service_contract_units_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_contract_units_family` FOREIGN KEY (`family_id`) REFERENCES `maintenance_families` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_contract_units_component` FOREIGN KEY (`component_id`) REFERENCES `maintenance_components` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_service_contract_units_kind` CHECK ((`family_id` IS NULL) <> (`component_id` IS NULL)),
  CONSTRAINT `ck_service_contract_units_cycle` CHECK (`cycle_months` IS NULL OR `cycle_months` BETWEEN 1 AND 12)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `service_contract_unit_members` (
  `unit_id` BIGINT NOT NULL,
  `component_id` BIGINT NOT NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`unit_id`, `component_id`),
  KEY `ix_service_contract_unit_members_component` (`component_id`),
  CONSTRAINT `fk_service_contract_unit_members_unit` FOREIGN KEY (`unit_id`) REFERENCES `service_contract_units` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_contract_unit_members_component` FOREIGN KEY (`component_id`) REFERENCES `maintenance_components` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
