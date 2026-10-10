-- version: 41
-- description: Subcategorii: ordinea de afisare in categorie (sort_order)
-- note: The order of the subcategories of a category (side menu, catalog page, inventory), set by drag and drop (administrator only). 0 = never arranged:
-- note: such subcategories keep the alphabetical order (the order is sort_order, then name); an arrangement writes 1..n for all the subcategories of the category.
-- column: subcategories.sort_order
-- statement
ALTER TABLE `subcategories` ADD COLUMN IF NOT EXISTS `sort_order` INT NOT NULL DEFAULT 0
