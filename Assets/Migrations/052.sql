-- version: 52
-- description: Produse: familii de parametri (cel putin 2 parametri ai aceleiasi subcategorii; un produs are exact unul activ din familie)
-- note: a family groups parameters of one subcategory (for example "Lentila": Fixa and Varifocala). A product of the subcategory has a value for exactly one member of each family and for every parameter outside the families. Deleting a family only ungroups its parameters (family_id becomes NULL).
-- column: parameter_families.id
-- column: parameter_families.subcategory_id
-- column: parameter_families.name
-- column: parameter_families.normalized_name
-- column: parameter_families.position
-- column: parameter_families.version
-- column: subcategory_parameters.family_id
-- statement
CREATE TABLE IF NOT EXISTS `parameter_families` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `subcategory_id` BIGINT NOT NULL,
  `name` VARCHAR(100) NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `position` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_parameter_families_name` (`subcategory_id`, `normalized_name`),
  CONSTRAINT `fk_parameter_families_subcategory` FOREIGN KEY (`subcategory_id`) REFERENCES `subcategories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `subcategory_parameters` ADD COLUMN IF NOT EXISTS `family_id` BIGINT NULL
-- statement
ALTER TABLE `subcategory_parameters` ADD CONSTRAINT `fk_subcategory_parameters_family` FOREIGN KEY IF NOT EXISTS (`family_id`) REFERENCES `parameter_families` (`id`) ON DELETE SET NULL ON UPDATE NO ACTION
