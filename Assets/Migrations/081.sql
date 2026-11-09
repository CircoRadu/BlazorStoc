-- version: 81
-- description: Taskuri ale utilizatorilor pe beneficiar (modulul Taskuri)
-- note: One row per task given by an administrator to ONE user (assigned_user_id) on a beneficiary. status: 1 = In viitor, 2 = In lucru, 3 = Finalizat. accepted_utc / accepted_by stay NULL until the user takes the task over (a new or moved task is shown on a red background). due_date is optional. The files of a task are rows of entity_files with owner_kind 'task' (no foreign key: the table is shared by several kinds of owner), removed by the application together with the task. A beneficiary or a user with tasks cannot be deleted (RESTRICT).
-- column: beneficiary_tasks.id
-- column: beneficiary_tasks.beneficiary_id
-- column: beneficiary_tasks.assigned_user_id
-- column: beneficiary_tasks.title
-- column: beneficiary_tasks.description
-- column: beneficiary_tasks.status
-- column: beneficiary_tasks.due_date
-- column: beneficiary_tasks.created_utc
-- column: beneficiary_tasks.created_by
-- column: beneficiary_tasks.accepted_utc
-- column: beneficiary_tasks.accepted_by
-- column: beneficiary_tasks.finished_utc
-- column: beneficiary_tasks.version
-- statement
CREATE TABLE IF NOT EXISTS `beneficiary_tasks` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `assigned_user_id` BIGINT NOT NULL,
  `title` VARCHAR(150) NOT NULL,
  `description` TEXT NOT NULL,
  `status` TINYINT NOT NULL DEFAULT 1,
  `due_date` DATE NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `created_by` VARCHAR(191) NOT NULL,
  `accepted_utc` VARCHAR(40) NULL,
  `accepted_by` VARCHAR(191) NULL,
  `finished_utc` VARCHAR(40) NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_beneficiary_tasks_beneficiary` (`beneficiary_id`),
  KEY `ix_beneficiary_tasks_user_status` (`assigned_user_id`, `status`),
  CONSTRAINT `fk_beneficiary_tasks_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_beneficiary_tasks_user` FOREIGN KEY (`assigned_user_id`) REFERENCES `web_users` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_beneficiary_tasks_status` CHECK (`status` IN (1, 2, 3))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `task_settings` (
  `setting_key` VARCHAR(64) NOT NULL,
  `setting_value` VARCHAR(191) NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
