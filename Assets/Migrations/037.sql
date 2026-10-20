-- version: 37
-- description: Produse: parametri obligatori pe subcategorie (model + valori), valorile alese pe produs
-- column: subcategory_parameters.id
-- column: subcategory_parameters.subcategory_id
-- column: subcategory_parameters.name
-- column: subcategory_parameters.normalized_name
-- column: subcategory_parameters.unit
-- column: subcategory_parameters.kind
-- column: subcategory_parameters.position
-- column: subcategory_parameters.version
-- column: parameter_values.id
-- column: parameter_values.parameter_id
-- column: parameter_values.value
-- column: parameter_values.normalized_value
-- column: products.base_model
-- column: product_parameter_values.product_id
-- column: product_parameter_values.parameter_id
-- column: product_parameter_values.value_id
-- statement
CREATE TABLE IF NOT EXISTS `subcategory_parameters` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `subcategory_id` BIGINT NOT NULL,
  `name` VARCHAR(100) NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `unit` VARCHAR(40) NOT NULL DEFAULT '',
  `kind` TINYINT NOT NULL,
  `position` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_subcategory_parameters_name` (`subcategory_id`, `normalized_name`),
  CONSTRAINT `fk_subcategory_parameters_subcategory` FOREIGN KEY (`subcategory_id`) REFERENCES `subcategories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `parameter_values` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `parameter_id` BIGINT NOT NULL,
  `value` VARCHAR(120) NOT NULL,
  `normalized_value` VARCHAR(512) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_parameter_values_value` (`parameter_id`, `normalized_value`),
  CONSTRAINT `fk_parameter_values_parameter` FOREIGN KEY (`parameter_id`) REFERENCES `subcategory_parameters` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `products` ADD COLUMN IF NOT EXISTS `base_model` VARCHAR(200) NULL
-- statement
CREATE TABLE IF NOT EXISTS `product_parameter_values` (
  `product_id` BIGINT NOT NULL,
  `parameter_id` BIGINT NOT NULL,
  `value_id` BIGINT NOT NULL,
  PRIMARY KEY (`product_id`, `parameter_id`),
  KEY `ix_product_parameter_values_value` (`value_id`),
  CONSTRAINT `fk_product_parameter_values_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_product_parameter_values_parameter` FOREIGN KEY (`parameter_id`) REFERENCES `subcategory_parameters` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_product_parameter_values_value` FOREIGN KEY (`value_id`) REFERENCES `parameter_values` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
