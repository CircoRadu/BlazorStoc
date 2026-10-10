-- version: 7
-- description: Puncte de lucru extinse: punct principal real, descriere, coordonate optionale, fotografii (service_photos) si arhivarea lor
-- note: The main work point becomes a real row (is_primary=1, one per beneficiary; the rows themselves are created by the
-- note: application at startup and with the beneficiary: their normalized address is computed in C#). Description, optional
-- note: coordinates (both or neither) and the photos of a work point (files on disk, rows here; later also of interventions).
-- column: beneficiary_work_points.description
-- column: beneficiary_work_points.is_primary
-- column: beneficiary_work_points.latitude
-- column: beneficiary_work_points.longitude
-- column: beneficiary_work_points.primary_beneficiary_id
-- column: service_photos.id
-- column: service_photos.work_point_id
-- column: service_photos.intervention_id
-- column: service_photos.relative_path
-- column: service_photos.original_name
-- column: service_photos.content_type
-- column: service_photos.byte_length
-- column: service_photos.sha256
-- column: service_photos.caption
-- column: service_photos.uploaded_by
-- column: service_photos.uploaded_utc
-- column: archive_work_points.archive_id
-- column: archive_work_points.original_id
-- column: archive_work_points.beneficiary_id
-- column: archive_work_points.name
-- column: archive_work_points.address
-- column: archive_work_points.phone
-- column: archive_work_points.contact_person
-- column: archive_work_points.description
-- column: archive_work_points.latitude
-- column: archive_work_points.longitude
-- column: archive_work_points.is_primary
-- column: archive_work_points.version
-- column: archive_service_photos.archive_id
-- column: archive_service_photos.original_id
-- column: archive_service_photos.work_point_id
-- column: archive_service_photos.intervention_id
-- column: archive_service_photos.original_name
-- column: archive_service_photos.content_type
-- column: archive_service_photos.byte_length
-- column: archive_service_photos.sha256
-- column: archive_service_photos.caption
-- column: archive_service_photos.uploaded_by
-- column: archive_service_photos.uploaded_utc
-- statement
ALTER TABLE `beneficiary_work_points`
    ADD COLUMN IF NOT EXISTS `description` VARCHAR(2000) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS `is_primary` TINYINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `latitude` DECIMAL(9,6) NULL,
    ADD COLUMN IF NOT EXISTS `longitude` DECIMAL(9,6) NULL
-- statement
ALTER TABLE `beneficiary_work_points` ADD COLUMN IF NOT EXISTS `primary_beneficiary_id` BIGINT AS (IF(`is_primary` = 1, `beneficiary_id`, NULL)) PERSISTENT
-- statement
ALTER TABLE `beneficiary_work_points` ADD UNIQUE INDEX IF NOT EXISTS `uq_work_points_primary` (`primary_beneficiary_id`)
-- statement
ALTER TABLE `beneficiary_work_points` ADD CONSTRAINT IF NOT EXISTS `ck_work_points_coordinates` CHECK ((`latitude` IS NULL) = (`longitude` IS NULL))
-- statement
CREATE TABLE IF NOT EXISTS `service_photos` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `work_point_id` BIGINT NULL,
  `intervention_id` BIGINT NULL,
  `relative_path` VARCHAR(120) NOT NULL,
  `original_name` VARCHAR(255) NOT NULL,
  `content_type` VARCHAR(100) NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` VARCHAR(64) NOT NULL,
  `caption` VARCHAR(200) NOT NULL DEFAULT '',
  `uploaded_by` VARCHAR(100) NOT NULL,
  `uploaded_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_photos_work_point` (`work_point_id`, `sha256`),
  UNIQUE KEY `uq_service_photos_intervention` (`intervention_id`, `sha256`),
  CONSTRAINT `fk_service_photos_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_service_photos_owner` CHECK ((`work_point_id` IS NOT NULL) + (`intervention_id` IS NOT NULL) = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_work_points` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `address` LONGTEXT NOT NULL,
  `phone` LONGTEXT NOT NULL,
  `contact_person` LONGTEXT NOT NULL,
  `description` LONGTEXT NOT NULL,
  `latitude` DECIMAL(9,6) NULL,
  `longitude` DECIMAL(9,6) NULL,
  `is_primary` TINYINT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_work_points_original` (`original_id`),
  CONSTRAINT `fk_archive_work_points_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_service_photos` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NULL,
  `intervention_id` BIGINT NULL,
  `original_name` LONGTEXT NOT NULL,
  `content_type` LONGTEXT NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` LONGTEXT NOT NULL,
  `caption` LONGTEXT NOT NULL,
  `uploaded_by` LONGTEXT NOT NULL,
  `uploaded_utc` LONGTEXT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_service_photos_original` (`original_id`),
  CONSTRAINT `fk_archive_service_photos_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
