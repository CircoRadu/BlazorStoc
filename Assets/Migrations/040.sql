-- version: 40
-- description: Tipuri de utilizatori cu permisiuni: tipul (nume, descriere, tip de sistem), permisiunile lui si legatura utilizatorului cu tipul
-- note: A user has exactly one type (web_users.user_type_id); the text column web_users.role stays for compatibility and always holds the name of a system type
-- note: (Administrator / Utilizator) for those users. The permission keys are "module.action" from Services/Permissions.cs. The migrator account has no data
-- note: rights, so the two system types and their permissions are written by UserTypeSeeder with the application account at startup (idempotent).
-- column: user_types.id
-- column: user_types.name
-- column: user_types.normalized_name
-- column: user_types.description
-- column: user_types.is_system
-- column: user_types.version
-- column: user_type_permissions.user_type_id
-- column: user_type_permissions.permission_key
-- column: web_users.user_type_id
-- statement
CREATE TABLE IF NOT EXISTS `user_types` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(60) NOT NULL,
  `normalized_name` VARCHAR(191) NOT NULL,
  `description` VARCHAR(300) NOT NULL DEFAULT '',
  `is_system` TINYINT NOT NULL DEFAULT 0,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_user_types_name` (`normalized_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `user_type_permissions` (
  `user_type_id` BIGINT NOT NULL,
  `permission_key` VARCHAR(80) NOT NULL,
  PRIMARY KEY (`user_type_id`, `permission_key`),
  CONSTRAINT `fk_user_type_permissions_type` FOREIGN KEY (`user_type_id`) REFERENCES `user_types` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `web_users` ADD COLUMN IF NOT EXISTS `user_type_id` BIGINT NULL
-- statement
ALTER TABLE `web_users` ADD CONSTRAINT `fk_web_users_user_type` FOREIGN KEY IF NOT EXISTS (`user_type_id`) REFERENCES `user_types` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
