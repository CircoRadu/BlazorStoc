-- version: 29
-- description: Stoc minim pe produs si termen pe proiect
-- note: Stoc minim pe produs si termen pe proiect: the data of the notifications "Stoc sub minim" and "Deficit la un proiect cu termen apropiat".
-- column: product_min_stock.product_id
-- column: product_min_stock.min_quantity
-- column: product_min_stock.set_date
-- column: product_min_stock.updated_by
-- column: product_min_stock.updated_utc
-- column: project_deadlines.project_id
-- column: project_deadlines.deadline
-- column: project_deadlines.updated_by
-- column: project_deadlines.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `product_min_stock` (
  `product_id` BIGINT NOT NULL,
  `min_quantity` INT NOT NULL,
  `set_date` VARCHAR(10) NOT NULL,
  `updated_by` VARCHAR(100) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`product_id`),
  CONSTRAINT `fk_product_min_stock_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `project_deadlines` (
  `project_id` BIGINT NOT NULL,
  `deadline` VARCHAR(10) NOT NULL,
  `updated_by` VARCHAR(100) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`project_id`),
  CONSTRAINT `fk_project_deadlines_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
