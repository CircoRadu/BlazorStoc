-- version: 19
-- description: Iesiri peste stoc: cauza optionala
-- note: Exits over the stock: the optional cause (1 = entry not yet recorded, 2 = wrong stock in the database).
-- column: stock_movements.over_stock_cause
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `over_stock_cause` TINYINT NULL
