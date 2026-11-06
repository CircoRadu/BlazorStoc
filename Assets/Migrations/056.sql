-- version: 56
-- description: Produse: arhivarea parametrilor si etichetelor, parametri si etichete pe categorie, jurnal de utilizare
-- note: A parameter, a parameter value, a family or a tag is archived (archived_utc set) instead of deleted once products use it. For a parameter that products carry, the value text is moved into the name of each product (baked_text is appended to base_model) and the link is kept with archived=1; restoring reverses it. category_id lets a category own parameters, families and tags (subcategory_id is then NULL); exactly one of the two is set, checked by the code. parameter_usage_log keeps, without foreign keys, where a parameter was used, so the history survives the deletion of the products. restore_retention_days is the number of days (30 to 360) an archived definition must wait before the restore can be given up for good.
-- column: subcategory_parameters.category_id
-- column: subcategory_parameters.archived_utc
-- column: subcategory_parameters.archived_by
-- column: parameter_values.archived_utc
-- column: parameter_values.archived_by
-- column: parameter_families.category_id
-- column: parameter_families.archived_utc
-- column: parameter_families.archived_by
-- column: subcategory_tags.category_id
-- column: subcategory_tags.archived_utc
-- column: subcategory_tags.archived_by
-- column: product_parameter_values.archived
-- column: product_parameter_values.baked_text
-- column: product_parameter_values.baked_position
-- column: catalog_settings.restore_retention_days
-- column: parameter_usage_log.id
-- column: parameter_usage_log.definition_kind
-- column: parameter_usage_log.definition_id
-- column: parameter_usage_log.parameter_name
-- column: parameter_usage_log.value_text
-- column: parameter_usage_log.product_id
-- column: parameter_usage_log.product_name
-- column: parameter_usage_log.node_path
-- column: parameter_usage_log.action
-- column: parameter_usage_log.actor
-- column: parameter_usage_log.reason
-- column: parameter_usage_log.occurred_utc
-- statement
ALTER TABLE `subcategory_parameters` MODIFY COLUMN `subcategory_id` BIGINT NULL
-- statement
ALTER TABLE `subcategory_parameters` ADD COLUMN IF NOT EXISTS `category_id` BIGINT NULL
-- statement
ALTER TABLE `subcategory_parameters` ADD COLUMN IF NOT EXISTS `archived_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `subcategory_parameters` ADD COLUMN IF NOT EXISTS `archived_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `subcategory_parameters` ADD UNIQUE INDEX IF NOT EXISTS `uq_subcategory_parameters_category_name` (`category_id`, `normalized_name`)
-- statement
ALTER TABLE `subcategory_parameters` ADD CONSTRAINT `fk_subcategory_parameters_category` FOREIGN KEY IF NOT EXISTS (`category_id`) REFERENCES `categories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
-- statement
ALTER TABLE `parameter_values` ADD COLUMN IF NOT EXISTS `archived_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `parameter_values` ADD COLUMN IF NOT EXISTS `archived_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `parameter_families` MODIFY COLUMN `subcategory_id` BIGINT NULL
-- statement
ALTER TABLE `parameter_families` ADD COLUMN IF NOT EXISTS `category_id` BIGINT NULL
-- statement
ALTER TABLE `parameter_families` ADD COLUMN IF NOT EXISTS `archived_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `parameter_families` ADD COLUMN IF NOT EXISTS `archived_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `parameter_families` ADD UNIQUE INDEX IF NOT EXISTS `uq_parameter_families_category_name` (`category_id`, `normalized_name`)
-- statement
ALTER TABLE `parameter_families` ADD CONSTRAINT `fk_parameter_families_category` FOREIGN KEY IF NOT EXISTS (`category_id`) REFERENCES `categories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
-- statement
ALTER TABLE `subcategory_tags` MODIFY COLUMN `subcategory_id` BIGINT NULL
-- statement
ALTER TABLE `subcategory_tags` ADD COLUMN IF NOT EXISTS `category_id` BIGINT NULL
-- statement
ALTER TABLE `subcategory_tags` ADD COLUMN IF NOT EXISTS `archived_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `subcategory_tags` ADD COLUMN IF NOT EXISTS `archived_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `subcategory_tags` ADD UNIQUE INDEX IF NOT EXISTS `uq_subcategory_tags_category_name` (`category_id`, `normalized_name`)
-- statement
ALTER TABLE `subcategory_tags` ADD CONSTRAINT `fk_subcategory_tags_category` FOREIGN KEY IF NOT EXISTS (`category_id`) REFERENCES `categories` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
-- statement
ALTER TABLE `product_parameter_values` ADD COLUMN IF NOT EXISTS `archived` TINYINT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `product_parameter_values` ADD COLUMN IF NOT EXISTS `baked_text` VARCHAR(200) NULL
-- statement
ALTER TABLE `product_parameter_values` ADD COLUMN IF NOT EXISTS `baked_position` INT NULL
-- statement
ALTER TABLE `catalog_settings` ADD COLUMN IF NOT EXISTS `restore_retention_days` INT NOT NULL DEFAULT 30
-- statement
CREATE TABLE IF NOT EXISTS `parameter_usage_log` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `definition_kind` VARCHAR(20) NOT NULL,
  `definition_id` BIGINT NOT NULL,
  `parameter_name` VARCHAR(100) NOT NULL,
  `value_text` VARCHAR(120) NOT NULL DEFAULT '',
  `product_id` BIGINT NULL,
  `product_name` VARCHAR(300) NOT NULL DEFAULT '',
  `node_path` VARCHAR(600) NOT NULL DEFAULT '',
  `action` VARCHAR(30) NOT NULL,
  `actor` VARCHAR(100) NOT NULL DEFAULT '',
  `reason` VARCHAR(500) NOT NULL DEFAULT '',
  `occurred_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_parameter_usage_log_definition` (`definition_kind`, `definition_id`, `id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
