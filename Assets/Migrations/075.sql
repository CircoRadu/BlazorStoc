-- version: 75
-- description: Acte aditionale la contractele de mentenanta (service_contract_addenda)
-- note: One row per addendum of a maintenance contract: number + date, the date it takes effect, the kinds of effect (valabilitate, ciclicitate, puncte, altele), the text of the other clauses, the values before and after (validity, cycle) and the coverage of the contract before and after as JSON. The effect is applied to the contract in the same transaction that stores the row; status Aplicat or Anulat (a cancelled addendum restores the values it changed). No file is attached to an addendum.
-- column: service_contract_addenda.id
-- column: service_contract_addenda.contract_id
-- column: service_contract_addenda.addendum_number
-- column: service_contract_addenda.addendum_date
-- column: service_contract_addenda.status
-- column: service_contract_addenda.version
-- statement
CREATE TABLE IF NOT EXISTS `service_contract_addenda` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `contract_id` BIGINT NOT NULL,
  `addendum_number` VARCHAR(30) NOT NULL,
  `addendum_date` DATE NOT NULL,
  `effective_date` DATE NULL,
  `kinds` VARCHAR(200) NOT NULL DEFAULT '',
  `summary` VARCHAR(1000) NOT NULL DEFAULT '',
  `prev_valid_until` DATE NULL,
  `new_valid_until` DATE NULL,
  `prev_cycle_months` TINYINT NULL,
  `new_cycle_months` TINYINT NULL,
  `points_before` MEDIUMTEXT NULL,
  `points_after` MEDIUMTEXT NULL,
  `status` VARCHAR(12) NOT NULL DEFAULT 'Aplicat',
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `cancelled_by` VARCHAR(100) NOT NULL DEFAULT '',
  `cancelled_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `cancel_reason` VARCHAR(500) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_contract_addenda_number` (`contract_id`, `addendum_number`, `addendum_date`),
  CONSTRAINT `fk_service_contract_addenda_contract` FOREIGN KEY (`contract_id`) REFERENCES `service_contracts` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
