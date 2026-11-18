-- version: 93
-- description: Bonuri de consum ale proiectelor beneficiarilor (generate la cerere, active sau inactive, sterse de utilizator)
-- note: Beneficiar -> Bonuri de consum si Administrare -> Bonuri de consum: a consumption note belongs to one beneficiary and one project and keeps a copy of the equipment handed over to the project at the time it was generated (one line per exit movement, net of returns). The tables are new: they start empty with AUTO_INCREMENT 1, so the update leaves no consumption note and the numbering starts at 1 (the former per-operation note was never stored).
-- column: consumption_notes.id
-- column: consumption_notes.beneficiary_id
-- column: consumption_notes.project_id
-- column: consumption_notes.beneficiary_name
-- column: consumption_notes.beneficiary_cui
-- column: consumption_notes.project_name
-- column: consumption_notes.note_date
-- column: consumption_notes.active
-- column: consumption_notes.created_by
-- column: consumption_notes.created_utc
-- column: consumption_note_lines.id
-- column: consumption_note_lines.note_id
-- column: consumption_note_lines.position
-- column: consumption_note_lines.movement_date
-- column: consumption_note_lines.product_id
-- column: consumption_note_lines.product_name
-- column: consumption_note_lines.subcategory
-- column: consumption_note_lines.quantity
-- statement
CREATE TABLE IF NOT EXISTS `consumption_notes` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `project_id` BIGINT NOT NULL,
  `beneficiary_name` VARCHAR(512) NOT NULL,
  `beneficiary_cui` VARCHAR(100) NOT NULL DEFAULT '',
  `project_name` VARCHAR(512) NOT NULL,
  `note_date` VARCHAR(40) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_consumption_notes_beneficiary` (`beneficiary_id`),
  KEY `ix_consumption_notes_project` (`project_id`),
  CONSTRAINT `fk_consumption_notes_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_consumption_notes_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin AUTO_INCREMENT=1
-- statement
CREATE TABLE IF NOT EXISTS `consumption_note_lines` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `note_id` BIGINT NOT NULL,
  `position` INT NOT NULL,
  `movement_date` VARCHAR(40) NOT NULL,
  `product_id` BIGINT NULL,
  `product_name` VARCHAR(512) NOT NULL,
  `subcategory` VARCHAR(512) NOT NULL DEFAULT '',
  `quantity` INT NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_consumption_note_lines_note` (`note_id`,`position`),
  CONSTRAINT `fk_consumption_note_lines_note` FOREIGN KEY (`note_id`) REFERENCES `consumption_notes` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin AUTO_INCREMENT=1
