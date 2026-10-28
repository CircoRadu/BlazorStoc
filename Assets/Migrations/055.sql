-- version: 55
-- description: Produse: setari de catalog (adancimea maxima a arborelui de subcategorii)
-- note: catalog_settings has one row (id 1). max_subcategory_depth is the largest number of subcategory levels under a category (1 = no children, at most 15; 10 until the administrator changes it). Only the administrator changes it, and never below the depth the tree already has.
-- column: catalog_settings.id
-- column: catalog_settings.max_subcategory_depth
-- column: catalog_settings.updated_by
-- column: catalog_settings.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `catalog_settings` (
  `id` INT NOT NULL,
  `max_subcategory_depth` INT NOT NULL DEFAULT 10,
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
