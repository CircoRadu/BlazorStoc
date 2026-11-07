-- version: 79
-- description: Acte aditionale: data de referinta si componentele contractului inainte / dupa act
-- note: The kinds "referinta" and "componente" of an addendum keep the reference date of the contract and the set of units (families / components, with their On/Off elements, cycle and reference date) before and after the act as JSON, so the act can be cancelled and the history stays readable.
-- column: service_contract_addenda.reference_before
-- column: service_contract_addenda.reference_after
-- column: service_contract_addenda.units_before
-- column: service_contract_addenda.units_after
-- statement
ALTER TABLE `service_contract_addenda` ADD COLUMN IF NOT EXISTS `reference_before` DATE NULL
-- statement
ALTER TABLE `service_contract_addenda` ADD COLUMN IF NOT EXISTS `reference_after` DATE NULL
-- statement
ALTER TABLE `service_contract_addenda` ADD COLUMN IF NOT EXISTS `units_before` TEXT NULL
-- statement
ALTER TABLE `service_contract_addenda` ADD COLUMN IF NOT EXISTS `units_after` TEXT NULL
