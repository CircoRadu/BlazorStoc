-- version: 21
-- description: Storno de operatie si retur legat de iesire
-- note: Storno of an exit operation (the rows stay as a trace: when, by whom, why) and the exit a return from a beneficiary is tied to.
-- column: stock_movements.voided_utc
-- column: stock_movements.void_reason
-- column: stock_movements.voided_by
-- column: stock_movements.return_of_movement_id
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `voided_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `void_reason` VARCHAR(500) NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `voided_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `return_of_movement_id` BIGINT NULL
-- statement
ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_return_of` (`return_of_movement_id`)
