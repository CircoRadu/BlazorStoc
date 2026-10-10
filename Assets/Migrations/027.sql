-- version: 27
-- description: Rezervari pe proiect: stoc liber = stoc - rezervari
-- note: Rezervari pe proiect: pieces of a product held for a project (and optionally one of its components); they never change the stock.
-- column: project_reservations.id
-- column: project_reservations.project_id
-- column: project_reservations.project_component_id
-- column: project_reservations.component_key
-- column: project_reservations.product_id
-- column: project_reservations.quantity
-- column: project_reservations.version
-- column: project_reservations.created_by
-- column: project_reservations.created_utc
-- column: project_reservations.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `project_reservations` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `project_id` BIGINT NOT NULL,
  `project_component_id` BIGINT NULL,
  `component_key` BIGINT NOT NULL DEFAULT 0,
  `product_id` BIGINT NOT NULL,
  `quantity` INT NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_project_reservations` (`project_id`, `product_id`, `component_key`),
  KEY `ix_project_reservations_product` (`product_id`),
  CONSTRAINT `fk_project_reservations_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_project_reservations_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
