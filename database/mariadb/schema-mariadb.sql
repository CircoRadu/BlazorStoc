-- Copie in repository a Livrare-DDL-MariaDB/schema-mariadb.sql (29.09.2026).
-- Diferenta fata de livrare: tabelul `beneficiaries` are coloanele kind, address, phone, registry_number, postal_code,
-- caen_code, anaf_verified (beneficiar persoana fizica/juridica, preluare ANAF). O baza MariaDB existenta trebuie
-- completata manual cu ALTER TABLE ... ADD COLUMN (vezi docs/TESTE_RAMASE.md).

CREATE TABLE `app_metadata` (
  `key` VARCHAR(191) NOT NULL,
  `value` LONGTEXT NOT NULL,
  PRIMARY KEY (`key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_operations` (
  `id` VARCHAR(64) NOT NULL,
  `entity_type` VARCHAR(64) NOT NULL,
  `original_id` VARCHAR(128) NOT NULL,
  `original_version` BIGINT NOT NULL,
  `deleted_utc` VARCHAR(40) NOT NULL,
  `actor_username` VARCHAR(191) NOT NULL,
  `actor_role` LONGTEXT NOT NULL,
  `motif` LONGTEXT NOT NULL,
  `target` LONGTEXT NOT NULL,
  `details` LONGTEXT NOT NULL,
  `data_json` LONGTEXT NOT NULL,
  `protected_data_json` LONGTEXT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_archive_operations_actor` (`actor_username`,`deleted_utc` DESC),
  KEY `ix_archive_operations_deleted` (`deleted_utc` DESC),
  KEY `ix_archive_operations_object` (`entity_type`,`original_id`),
  CHECK (original_version >= 0),
  CHECK (json_valid(data_json)),
  CHECK (protected_data_json IS NULL OR json_valid(protected_data_json))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `audit_events` (
  `id` VARCHAR(64) NOT NULL,
  `timestamp_utc` VARCHAR(40) NOT NULL,
  `actor_username` LONGTEXT NOT NULL,
  `actor_role` LONGTEXT NOT NULL,
  `entity_type` LONGTEXT NOT NULL,
  `action` LONGTEXT NOT NULL,
  `target` LONGTEXT NOT NULL,
  `details` LONGTEXT NOT NULL,
  `motif` LONGTEXT NOT NULL DEFAULT '',
  `entity_id` LONGTEXT NOT NULL DEFAULT '',
  `archive_operation_id` VARCHAR(64) NULL,
  PRIMARY KEY (`id`),
  KEY `ix_audit_events_archive_operation` (`archive_operation_id`),
  KEY `ix_audit_events_timestamp` (`timestamp_utc` DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `beneficiaries` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` LONGTEXT NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `cui` LONGTEXT NOT NULL,
  `normalized_cui` VARCHAR(191) NOT NULL,
  `kind` VARCHAR(2) NOT NULL DEFAULT 'PJ',
  `address` VARCHAR(300) NOT NULL DEFAULT '',
  `phone` VARCHAR(20) NOT NULL DEFAULT '',
  `registry_number` VARCHAR(40) NOT NULL DEFAULT '',
  `postal_code` VARCHAR(10) NOT NULL DEFAULT '',
  `caen_code` VARCHAR(4) NOT NULL DEFAULT '',
  `anaf_verified` TINYINT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_beneficiaries_0` (`normalized_cui`),
  UNIQUE KEY `uq_beneficiaries_1` (`normalized_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `beneficiary_work_points` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `name` VARCHAR(200) NOT NULL,
  `address` VARCHAR(300) NOT NULL,
  `normalized_address` VARCHAR(400) NOT NULL,
  `phone` VARCHAR(20) NOT NULL DEFAULT '',
  `contact_person` VARCHAR(200) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_beneficiary_work_points_0` (`beneficiary_id`, `normalized_address`),
  CONSTRAINT `fk_beneficiary_work_points_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `categories` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` LONGTEXT NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_categories_0` (`normalized_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `change_events` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `entity_type` LONGTEXT NOT NULL,
  `action` LONGTEXT NOT NULL,
  `entity_id` LONGTEXT NOT NULL,
  `project_id` BIGINT NULL,
  `observation_id` BIGINT NULL,
  `beneficiary_id` BIGINT NULL,
  `created_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `vehicles` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `plate_number` LONGTEXT NOT NULL,
  `normalized_plate` VARCHAR(191) NOT NULL,
  `description` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_vehicles_0` (`normalized_plate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `web_users` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `username` LONGTEXT NOT NULL,
  `normalized_username` VARCHAR(191) NOT NULL,
  `display_name` LONGTEXT NOT NULL,
  `password_hash` LONGTEXT NOT NULL,
  `role` LONGTEXT NOT NULL,
  `is_active` BIGINT NOT NULL DEFAULT 1,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_web_users_0` (`normalized_username`),
  CHECK (role IN ('Administrator','Utilizator'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_beneficiaries` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `cui` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_beneficiaries_original` (`original_id`),
  CONSTRAINT `fk_archive_beneficiaries_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_files` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `archive_id` VARCHAR(64) NOT NULL,
  `relation_type` VARCHAR(64) NOT NULL,
  `original_relation_id` VARCHAR(128) NOT NULL,
  `live_relative_path` LONGTEXT NOT NULL,
  `archive_relative_path` LONGTEXT NOT NULL,
  `content_type` LONGTEXT NOT NULL,
  `file_name` LONGTEXT NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `content_hash` LONGTEXT NOT NULL,
  `archived_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_archive_files_object` (`relation_type`,`original_relation_id`),
  UNIQUE KEY `uq_archive_files_1` (`archive_id`,`relation_type`,`original_relation_id`),
  CONSTRAINT `fk_archive_files_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CHECK (byte_length >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_products` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `category` LONGTEXT NOT NULL,
  `subcategory` LONGTEXT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `description` LONGTEXT NOT NULL,
  `quantity` BIGINT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_products_original` (`original_id`),
  CONSTRAINT `fk_archive_products_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_project_observation_files` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `observation_id` BIGINT NOT NULL,
  `original_name` LONGTEXT NOT NULL,
  `content_type` LONGTEXT NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` LONGTEXT NOT NULL,
  `author` LONGTEXT NOT NULL,
  `uploaded_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_project_observation_files_original` (`original_id`),
  CONSTRAINT `fk_archive_project_observation_files_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_project_observations` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `project_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `content` LONGTEXT NOT NULL,
  `author` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_project_observations_original` (`original_id`),
  CONSTRAINT `fk_archive_project_observations_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_projects` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `observations` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_projects_original` (`original_id`),
  CONSTRAINT `fk_archive_projects_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_relations` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `archive_id` VARCHAR(64) NOT NULL,
  `relation_type` VARCHAR(64) NOT NULL,
  `original_relation_id` VARCHAR(128) NOT NULL,
  `data_json` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_archive_relations_object` (`relation_type`,`original_relation_id`),
  UNIQUE KEY `uq_archive_relations_1` (`archive_id`,`relation_type`,`original_relation_id`),
  CONSTRAINT `fk_archive_relations_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CHECK (json_valid(data_json))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_stock_movements` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `product_id` BIGINT NOT NULL,
  `kind` BIGINT NOT NULL,
  `quantity` BIGINT NOT NULL,
  `movement_date` LONGTEXT NOT NULL,
  `description` LONGTEXT NOT NULL,
  `beneficiary_id` BIGINT NULL,
  `project_id` BIGINT NULL,
  `operator` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  `destination` BIGINT NULL,
  `vehicle_id` BIGINT NULL,
  `source_vehicle_id` BIGINT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_stock_movements_original` (`original_id`),
  CONSTRAINT `fk_archive_stock_movements_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_vehicles` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `plate_number` LONGTEXT NOT NULL,
  `description` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_vehicles_original` (`original_id`),
  CONSTRAINT `fk_archive_vehicles_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `archive_web_users` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `username` LONGTEXT NOT NULL,
  `display_name` LONGTEXT NOT NULL,
  `role` LONGTEXT NOT NULL,
  `is_active` BIGINT NOT NULL,
  `version` BIGINT NOT NULL,
  `password_hash` LONGTEXT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_web_users_original` (`original_id`),
  CONSTRAINT `fk_archive_web_users_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `projects` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `observations` LONGTEXT NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` LONGTEXT NOT NULL,
  `updated_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_projects_beneficiary` (`beneficiary_id`),
  UNIQUE KEY `uq_projects_1` (`beneficiary_id`,`normalized_name`),
  CONSTRAINT `fk_projects_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `subcategories` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `category_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_subcategories_0` (`normalized_name`),
  CONSTRAINT `fk_subcategories_0` FOREIGN KEY (`category_id`) REFERENCES `categories` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `products` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `category_id` BIGINT NOT NULL,
  `subcategory_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `normalized_name` VARCHAR(512) NOT NULL,
  `description` LONGTEXT NOT NULL DEFAULT '',
  `quantity` BIGINT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_products_group` (`category_id`,`subcategory_id`),
  UNIQUE KEY `uq_products_1` (`normalized_name`),
  CONSTRAINT `fk_products_0` FOREIGN KEY (`subcategory_id`) REFERENCES `subcategories` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_products_1` FOREIGN KEY (`category_id`) REFERENCES `categories` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `project_observations` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `project_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `content` LONGTEXT NOT NULL DEFAULT '',
  `author` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_project_observations_project` (`project_id`,`created_utc` DESC,`id`),
  CONSTRAINT `fk_project_observations_0` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `product_images` (
  `product_id` BIGINT NOT NULL,
  `relative_path` LONGTEXT NOT NULL,
  `content_type` LONGTEXT NOT NULL,
  `file_name` LONGTEXT NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `updated_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`product_id`),
  CONSTRAINT `fk_product_images_0` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `product_locks` (
  `product_id` BIGINT NOT NULL,
  `owner_username` LONGTEXT NOT NULL,
  `session_id` LONGTEXT NOT NULL,
  `acquired_utc` LONGTEXT NOT NULL,
  `renewed_utc` LONGTEXT NOT NULL,
  `expires_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`product_id`),
  CONSTRAINT `fk_product_locks_0` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `project_observation_files` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `observation_id` BIGINT NOT NULL,
  `relative_path` LONGTEXT NOT NULL,
  `original_name` LONGTEXT NOT NULL,
  `content_type` LONGTEXT NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` LONGTEXT NOT NULL,
  `author` LONGTEXT NOT NULL,
  `uploaded_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_project_observation_files_observation` (`observation_id`),
  CONSTRAINT `fk_project_observation_files_0` FOREIGN KEY (`observation_id`) REFERENCES `project_observations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `stock_movements` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `product_id` BIGINT NOT NULL,
  `beneficiary_id` BIGINT NULL,
  `quantity` BIGINT NOT NULL,
  `created_utc` LONGTEXT NOT NULL,
  `project_id` BIGINT NULL,
  `kind` BIGINT NOT NULL DEFAULT 1,
  `movement_date` VARCHAR(40) NOT NULL DEFAULT '',
  `description` LONGTEXT NOT NULL DEFAULT '',
  `operator` LONGTEXT NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  `updated_utc` LONGTEXT NOT NULL DEFAULT '',
  `destination` BIGINT NULL,
  `vehicle_id` BIGINT NULL,
  `source_vehicle_id` BIGINT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_stock_movements_source_vehicle` (`source_vehicle_id`),
  KEY `ix_stock_movements_vehicle` (`vehicle_id`),
  KEY `ix_stock_movements_product` (`product_id`,`movement_date`,`id`),
  KEY `ix_stock_movements_project` (`project_id`),
  KEY `ix_stock_movements_beneficiary` (`beneficiary_id`),
  CONSTRAINT `fk_stock_movements_0` FOREIGN KEY (`source_vehicle_id`) REFERENCES `vehicles` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_stock_movements_1` FOREIGN KEY (`vehicle_id`) REFERENCES `vehicles` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_stock_movements_2` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_stock_movements_3` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_stock_movements_4` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;

CREATE TABLE `stock_movement_history` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `movement_id` BIGINT NOT NULL,
  `actor` LONGTEXT NOT NULL,
  `timestamp_utc` LONGTEXT NOT NULL,
  `changes` LONGTEXT NOT NULL,
  `stock_correction` BIGINT NOT NULL,
  `reason` LONGTEXT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_stock_movement_history_movement` (`movement_id`,`id`),
  CONSTRAINT `fk_stock_movement_history_0` FOREIGN KEY (`movement_id`) REFERENCES `stock_movements` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin;