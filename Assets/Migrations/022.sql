-- version: 22
-- description: Nomenclator: tipuri de sisteme si denumirile lor alternative
-- note: Nomenclator -> Tipuri de sisteme: the types (name, order, active) and their alternative names.
-- column: system_types.id
-- column: system_types.name
-- column: system_types.name_key
-- column: system_types.active
-- column: system_types.sort_order
-- column: system_types.version
-- column: system_types.created_utc
-- column: system_types.updated_utc
-- column: system_type_aliases.id
-- column: system_type_aliases.system_type_id
-- column: system_type_aliases.alias
-- column: system_type_aliases.alias_key
-- column: system_type_aliases.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `system_types` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(100) NOT NULL,
  `name_key` VARCHAR(100) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `sort_order` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_system_types_key` (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `system_type_aliases` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `system_type_id` BIGINT NOT NULL,
  `alias` VARCHAR(100) NOT NULL,
  `alias_key` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_system_type_aliases_key` (`alias_key`),
  CONSTRAINT `fk_system_type_aliases_type` FOREIGN KEY (`system_type_id`) REFERENCES `system_types` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
