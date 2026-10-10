-- version: 38
-- description: Categorii: ordinea de afisare (sort_order)
-- note: The order of the categories in the side menu and on the catalog page, set by drag and drop (administrator only). 0 = never arranged: such
-- note: categories keep the alphabetical order (the order is sort_order, then name); an arrangement writes 1..n for all of them.
-- column: categories.sort_order
-- statement
ALTER TABLE `categories` ADD COLUMN IF NOT EXISTS `sort_order` INT NOT NULL DEFAULT 0
