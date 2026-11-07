-- version: 73
-- description: Raportare erori: rapoartele utilizatorilor (bug_reports) si imaginile lor (bug_report_files)
-- note: bug_reports holds one report (title, description, severity, state, the page it was made on, the technical details as JSON); bug_report_files holds the automatic capture of the page and the pasted or chosen pictures as bytes (carried by the database backup). Nothing leaves the application: the administrator may export reports as a ZIP.
-- column: bug_reports.id
-- column: bug_reports.number
-- column: bug_reports.state
-- column: bug_report_files.id
-- column: bug_report_files.report_id
-- statement
CREATE TABLE IF NOT EXISTS `bug_reports` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `number` VARCHAR(20) NOT NULL DEFAULT '',
  `title` VARCHAR(200) NOT NULL,
  `description` TEXT NOT NULL,
  `severity` VARCHAR(20) NOT NULL DEFAULT 'Important',
  `state` VARCHAR(20) NOT NULL DEFAULT 'Nou',
  `route` VARCHAR(500) NOT NULL DEFAULT '',
  `page_title` VARCHAR(300) NOT NULL DEFAULT '',
  `app_version` VARCHAR(40) NOT NULL DEFAULT '',
  `user_name` VARCHAR(100) NOT NULL DEFAULT '',
  `user_role` VARCHAR(100) NOT NULL DEFAULT '',
  `tech_json` MEDIUMTEXT NULL,
  `admin_note` TEXT NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `resolved_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_bug_reports_state` (`state`),
  KEY `ix_bug_reports_user` (`user_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `bug_report_files` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `report_id` INT NOT NULL,
  `kind` VARCHAR(20) NOT NULL DEFAULT 'pasted',
  `file_name` VARCHAR(200) NOT NULL DEFAULT '',
  `content_type` VARCHAR(100) NOT NULL DEFAULT '',
  `size_bytes` INT NOT NULL DEFAULT 0,
  `content` LONGBLOB NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_bug_report_files_report` (`report_id`),
  CONSTRAINT `fk_bug_report_files_report` FOREIGN KEY (`report_id`) REFERENCES `bug_reports` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
