-- version: 20
-- description: Iesiri: operatia din care fac parte (operation_id)
-- note: Every exit belongs to an operation (the id of its first exit; a single exit is an operation of one line). Older movements keep none.
-- column: stock_movements.operation_id
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `operation_id` BIGINT NULL
-- statement
ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_operation` (`operation_id`)
