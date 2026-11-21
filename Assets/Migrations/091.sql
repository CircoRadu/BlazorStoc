-- version: 91
-- description: Nomenclator: tipuri obligatorii de proiecte ale beneficiarilor
-- note: Nomenclator -> Proiecte obligatorii: every beneficiary gets one project for each active type of this list (the application makes the first type, "Reparații sisteme existente", when the list is empty: a migration account only changes the structure). projects.mandatory_type_id marks the project that came from a type; the application creates the missing ones for the existing beneficiaries when it starts and for the new ones when they are made.
-- column: beneficiary_project_types.id
-- column: beneficiary_project_types.name
-- column: beneficiary_project_types.name_key
-- column: beneficiary_project_types.active
-- column: beneficiary_project_types.sort_order
-- column: beneficiary_project_types.version
-- column: beneficiary_project_types.created_utc
-- column: beneficiary_project_types.updated_utc
-- column: projects.mandatory_type_id
-- statement
CREATE TABLE IF NOT EXISTS `beneficiary_project_types` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(200) NOT NULL,
  `name_key` VARCHAR(200) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `sort_order` INT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_beneficiary_project_types_key` (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `projects` ADD COLUMN IF NOT EXISTS `mandatory_type_id` BIGINT NULL
-- statement
ALTER TABLE `projects` ADD KEY IF NOT EXISTS `ix_projects_mandatory_type` (`mandatory_type_id`)
-- statement
ALTER TABLE `projects` ADD CONSTRAINT `fk_projects_mandatory_type` FOREIGN KEY IF NOT EXISTS (`mandatory_type_id`) REFERENCES `beneficiary_project_types` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
