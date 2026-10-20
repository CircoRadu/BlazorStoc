-- version: 23
-- description: Proiecte: componente (tipuri de sisteme) cu stare si arhivare
-- note: The components of a project: the system types it is made of, with a simple state; taken out = archived with a reason (never deleted).
-- column: project_components.id
-- column: project_components.project_id
-- column: project_components.system_type_id
-- column: project_components.state
-- column: project_components.archived_utc
-- column: project_components.archive_reason
-- column: project_components.version
-- column: project_components.created_utc
-- column: project_components.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `project_components` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `project_id` BIGINT NOT NULL,
  `system_type_id` BIGINT NOT NULL,
  `state` TINYINT NOT NULL DEFAULT 1,
  `archived_utc` VARCHAR(40) NULL,
  `archive_reason` VARCHAR(500) NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_project_components` (`project_id`, `system_type_id`),
  CONSTRAINT `fk_project_components_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_project_components_type` FOREIGN KEY (`system_type_id`) REFERENCES `system_types` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
