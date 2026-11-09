-- version: 83
-- description: Propuneri de modificari si functionalitati noi (modulul Propuneri)
-- note: proposals holds one proposal (kind, the page it was made for, title, description, benefit, importance wanted by the author, state, version it was implemented in); proposal_notes is the thread of answers between the administrator and the author; proposal_files holds the automatic capture of the page and the pasted or chosen pictures as bytes (carried by the database backup). author_seen_utc is when the author last opened the proposal (the notification tab shows the proposals changed since).
-- column: proposals.id
-- column: proposals.number
-- column: proposals.state
-- column: proposal_notes.id
-- column: proposal_notes.proposal_id
-- column: proposal_files.id
-- column: proposal_files.proposal_id
-- statement
CREATE TABLE IF NOT EXISTS `proposals` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `number` VARCHAR(20) NOT NULL DEFAULT '',
  `kind` VARCHAR(30) NOT NULL DEFAULT 'Modificare',
  `area_route` VARCHAR(500) NOT NULL DEFAULT '',
  `area_title` VARCHAR(300) NOT NULL DEFAULT '',
  `title` VARCHAR(200) NOT NULL,
  `description` TEXT NOT NULL,
  `benefit` TEXT NOT NULL,
  `wanted_importance` VARCHAR(20) NOT NULL DEFAULT 'Medie',
  `state` VARCHAR(20) NOT NULL DEFAULT 'Nouă',
  `user_name` VARCHAR(100) NOT NULL DEFAULT '',
  `user_role` VARCHAR(100) NOT NULL DEFAULT '',
  `app_version` VARCHAR(40) NOT NULL DEFAULT '',
  `implemented_version` VARCHAR(40) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `decided_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `author_seen_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_proposals_state` (`state`),
  KEY `ix_proposals_user` (`user_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `proposal_notes` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `proposal_id` INT NOT NULL,
  `author` VARCHAR(100) NOT NULL DEFAULT '',
  `author_role` VARCHAR(100) NOT NULL DEFAULT '',
  `body` TEXT NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_proposal_notes_proposal` (`proposal_id`),
  CONSTRAINT `fk_proposal_notes_proposal` FOREIGN KEY (`proposal_id`) REFERENCES `proposals` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `proposal_files` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `proposal_id` INT NOT NULL,
  `kind` VARCHAR(20) NOT NULL DEFAULT 'pasted',
  `file_name` VARCHAR(200) NOT NULL DEFAULT '',
  `content_type` VARCHAR(100) NOT NULL DEFAULT '',
  `size_bytes` INT NOT NULL DEFAULT 0,
  `content` LONGBLOB NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_proposal_files_proposal` (`proposal_id`),
  CONSTRAINT `fk_proposal_files_proposal` FOREIGN KEY (`proposal_id`) REFERENCES `proposals` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
