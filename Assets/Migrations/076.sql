-- version: 76
-- description: Nomenclator Mentenanta: familii de componente si componente ale interventiilor de mentenanta
-- note: Nomenclator -> Componente mentenanta: families (e.g. Securitate fizica) and components (efractie, TVCI, ...). A component belongs to at most one family (family_id NULL = a discrete component); alternative names belong to components. Used by the maintenance contracts and by the maintenance interventions (and the tickets taken by an operator). Never deleted: a family or component that is no longer offered is deactivated.
-- column: maintenance_families.id
-- column: maintenance_families.name
-- column: maintenance_families.name_key
-- column: maintenance_families.active
-- column: maintenance_families.sort_order
-- column: maintenance_families.version
-- column: maintenance_components.id
-- column: maintenance_components.family_id
-- column: maintenance_components.name
-- column: maintenance_components.name_key
-- column: maintenance_components.active
-- column: maintenance_components.sort_order
-- column: maintenance_components.version
-- column: maintenance_component_aliases.id
-- column: maintenance_component_aliases.component_id
-- column: maintenance_component_aliases.alias
-- column: maintenance_component_aliases.alias_key
-- statement
CREATE TABLE IF NOT EXISTS `maintenance_families` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(100) NOT NULL,
  `name_key` VARCHAR(100) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `sort_order` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_maintenance_families_key` (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `maintenance_components` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `family_id` BIGINT NULL,
  `name` VARCHAR(100) NOT NULL,
  `name_key` VARCHAR(100) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `sort_order` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_maintenance_components_key` (`name_key`),
  KEY `ix_maintenance_components_family` (`family_id`),
  CONSTRAINT `fk_maintenance_components_family` FOREIGN KEY (`family_id`) REFERENCES `maintenance_families` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `maintenance_component_aliases` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `component_id` BIGINT NOT NULL,
  `alias` VARCHAR(100) NOT NULL,
  `alias_key` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_maintenance_component_aliases_key` (`alias_key`),
  CONSTRAINT `fk_maintenance_component_aliases_component` FOREIGN KEY (`component_id`) REFERENCES `maintenance_components` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
