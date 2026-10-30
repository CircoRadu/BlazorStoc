-- version: 60
-- description: Mentenanta inregistrata fara contract (tichet ActivityApp validat ca mentenanta la un punct fara contract activ): service_interventions.without_contract
-- note: A maintenance intervention normally has the contract that covers the point and may move its due date. An operator who validates an ActivityApp ticket as maintenance for a point that has no active
-- note: contract yet (a contract not recorded yet) records it without contract: without_contract = 1, no contract, no planned or new due date. The check of the table is widened accordingly.
-- column: service_interventions.without_contract
-- statement
ALTER TABLE `service_interventions` ADD COLUMN IF NOT EXISTS `without_contract` TINYINT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `service_interventions` DROP CONSTRAINT IF EXISTS `ck_service_interventions_contract`
-- statement
ALTER TABLE `service_interventions` ADD CONSTRAINT `ck_service_interventions_contract` CHECK ((`kind` = 'M' AND `without_contract` = 0 AND `contract_id` IS NOT NULL AND `contract_label` IS NOT NULL) OR (`kind` = 'M' AND `without_contract` = 1 AND `contract_id` IS NULL AND `contract_label` IS NULL AND `planned_due` IS NULL AND `next_due_basis` IS NULL AND `next_due_set` IS NULL) OR (`kind` = 'C' AND `without_contract` = 0 AND `contract_id` IS NULL AND `contract_label` IS NULL AND `planned_due` IS NULL AND `next_due_basis` IS NULL AND `next_due_set` IS NULL))
