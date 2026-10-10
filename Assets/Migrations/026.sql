-- version: 26
-- description: Iesiri legate de componente: componenta, in afara ofertei, lamurirea la scoaterea componentei
-- note: Exits to a project tied to a component of the project (or marked "in afara ofertei"); component_settled = how an exit left on a taken-out component was cleared.
-- column: stock_movements.project_component_id
-- column: stock_movements.outside_offer
-- column: stock_movements.component_settled
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `project_component_id` BIGINT NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `outside_offer` TINYINT(1) NOT NULL DEFAULT 0
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `component_settled` TINYINT NULL
-- statement
ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_component` (`project_component_id`)
