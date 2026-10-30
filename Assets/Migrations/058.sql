-- version: 58
-- description: Fisiere atasate punctelor de lucru, contractelor de mentenanta, interventiilor si analizelor de risc (entity_files)
-- note: One table for the four owners; owner_kind is workpoint, contract, intervention or risk. The files are on disk under the entity-files asset directory (relative_path is relative to it).
-- column: entity_files.id
-- column: entity_files.owner_kind
-- column: entity_files.owner_id
-- column: entity_files.relative_path
-- column: entity_files.original_name
-- column: entity_files.content_type
-- column: entity_files.byte_length
-- column: entity_files.sha256
-- column: entity_files.uploaded_by
-- column: entity_files.uploaded_utc
-- statement
CREATE TABLE IF NOT EXISTS `entity_files` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `owner_kind` VARCHAR(20) NOT NULL,
  `owner_id` BIGINT NOT NULL,
  `relative_path` VARCHAR(120) NOT NULL,
  `original_name` VARCHAR(255) NOT NULL,
  `content_type` VARCHAR(100) NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` VARCHAR(64) NOT NULL,
  `uploaded_by` VARCHAR(100) NOT NULL,
  `uploaded_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_entity_files_owner_hash` (`owner_kind`, `owner_id`, `sha256`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
