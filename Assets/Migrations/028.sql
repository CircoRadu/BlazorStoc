-- version: 28
-- description: Nivel tinta pe vehicul: completare la nivel
-- note: Nivel tinta pe vehicul: how many pieces of a product a vehicle should hold; used to fill the vehicle with one exit operation.
-- column: vehicle_target_levels.vehicle_id
-- column: vehicle_target_levels.product_id
-- column: vehicle_target_levels.target_quantity
-- column: vehicle_target_levels.updated_by
-- column: vehicle_target_levels.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `vehicle_target_levels` (
  `vehicle_id` BIGINT NOT NULL,
  `product_id` BIGINT NOT NULL,
  `target_quantity` INT NOT NULL,
  `updated_by` VARCHAR(100) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`vehicle_id`, `product_id`),
  KEY `ix_vehicle_target_levels_product` (`product_id`),
  CONSTRAINT `fk_vehicle_target_levels_vehicle` FOREIGN KEY (`vehicle_id`) REFERENCES `vehicles` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_vehicle_target_levels_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
