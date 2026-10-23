-- version: 45
-- description: Preluare ActivityApp, etapa beneficiari si puncte de lucru: harta identitatilor, rularile (simulare/aplicare) si problemele
-- note: activity_import_map ties an ActivityApp row (entity "partner" or "workpoint", its ID there) to the row it became or was linked to in BlazorStoc (local_id);
-- note: origin "created" = the import created it, "linked" = it already existed and was only tied; content_hash is the SHA-256 of the source fields at the last import.
-- note: activity_import_runs keeps each simulation and each application (who, when, outcome, the report as JSON). activity_import_issues keeps what could not be imported
-- note: (invalid or duplicate tax id, no address, duplicate work point...) until the administrator marks it resolved. Nothing here writes to ActivityApp.
-- column: activity_import_map.entity
-- column: activity_import_map.source_id
-- column: activity_import_map.local_id
-- column: activity_import_map.origin
-- column: activity_import_map.content_hash
-- column: activity_import_map.imported_utc
-- column: activity_import_runs.id
-- column: activity_import_runs.kind
-- column: activity_import_runs.started_utc
-- column: activity_import_runs.finished_utc
-- column: activity_import_runs.started_by
-- column: activity_import_runs.status
-- column: activity_import_runs.report
-- column: activity_import_issues.id
-- column: activity_import_issues.run_id
-- column: activity_import_issues.entity
-- column: activity_import_issues.source_id
-- column: activity_import_issues.source_name
-- column: activity_import_issues.message
-- column: activity_import_issues.created_utc
-- column: activity_import_issues.resolved
-- column: activity_import_issues.resolved_by
-- column: activity_import_issues.resolved_utc
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_map` (
  `entity` VARCHAR(20) NOT NULL,
  `source_id` BIGINT NOT NULL,
  `local_id` INT NOT NULL,
  `origin` VARCHAR(10) NOT NULL,
  `content_hash` CHAR(64) NOT NULL DEFAULT '',
  `imported_utc` DATETIME NOT NULL,
  PRIMARY KEY (`entity`, `source_id`),
  KEY `ix_activity_import_map_local` (`entity`, `local_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_runs` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `kind` VARCHAR(20) NOT NULL,
  `started_utc` DATETIME NOT NULL,
  `finished_utc` DATETIME NULL,
  `started_by` VARCHAR(100) NOT NULL,
  `status` VARCHAR(20) NOT NULL,
  `report` LONGTEXT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `activity_import_issues` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `run_id` BIGINT NOT NULL,
  `entity` VARCHAR(20) NOT NULL,
  `source_id` BIGINT NOT NULL,
  `source_name` VARCHAR(200) NOT NULL DEFAULT '',
  `message` VARCHAR(500) NOT NULL,
  `created_utc` DATETIME NOT NULL,
  `resolved` TINYINT(1) NOT NULL DEFAULT 0,
  `resolved_by` VARCHAR(100) NOT NULL DEFAULT '',
  `resolved_utc` DATETIME NULL,
  PRIMARY KEY (`id`),
  KEY `ix_activity_import_issues_open` (`resolved`, `entity`, `source_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
