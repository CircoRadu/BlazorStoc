-- version: 88
-- description: Puncte de lucru arondate: componente ale unui punct de lucru (cladiri, corpuri) cu nume, adresa si/sau coordonate GPS, telefon, persoana de contact; arhivare la stergere
-- note: work_point_attached holds the attached work points of a work point (a school with a primary-school building and a kindergarten). The name is required and unique inside its work point (name_key);
-- note: the address is NOT inherited from the parent (empty stays empty); at least the address or the coordinates must be filled (ck_work_point_attached_where), coordinates are both or neither.
-- note: archive_work_point_attached receives a deleted attached work point (the ones of a deleted work point or beneficiary travel as relations of that archive).
-- column: work_point_attached.id
-- column: work_point_attached.work_point_id
-- column: work_point_attached.name
-- column: work_point_attached.name_key
-- column: work_point_attached.address
-- column: work_point_attached.phone
-- column: work_point_attached.contact_person
-- column: work_point_attached.latitude
-- column: work_point_attached.longitude
-- column: work_point_attached.version
-- column: archive_work_point_attached.archive_id
-- column: archive_work_point_attached.original_id
-- column: archive_work_point_attached.work_point_id
-- column: archive_work_point_attached.name
-- column: archive_work_point_attached.address
-- column: archive_work_point_attached.phone
-- column: archive_work_point_attached.contact_person
-- column: archive_work_point_attached.latitude
-- column: archive_work_point_attached.longitude
-- column: archive_work_point_attached.version
-- statement
CREATE TABLE IF NOT EXISTS `work_point_attached` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `work_point_id` BIGINT NOT NULL,
  `name` VARCHAR(200) NOT NULL,
  `name_key` VARCHAR(200) NOT NULL,
  `address` VARCHAR(300) NOT NULL DEFAULT '',
  `phone` VARCHAR(20) NOT NULL DEFAULT '',
  `contact_person` VARCHAR(200) NOT NULL DEFAULT '',
  `latitude` DECIMAL(9,6) NULL,
  `longitude` DECIMAL(9,6) NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_work_point_attached_name` (`work_point_id`, `name_key`),
  CONSTRAINT `fk_work_point_attached_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_work_point_attached_coordinates` CHECK ((`latitude` IS NULL) = (`longitude` IS NULL)),
  CONSTRAINT `ck_work_point_attached_where` CHECK (`address` <> '' OR `latitude` IS NOT NULL)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_work_point_attached` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `name` LONGTEXT NOT NULL,
  `address` LONGTEXT NOT NULL,
  `phone` LONGTEXT NOT NULL,
  `contact_person` LONGTEXT NOT NULL,
  `latitude` DECIMAL(9,6) NULL,
  `longitude` DECIMAL(9,6) NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_work_point_attached_original` (`original_id`),
  CONSTRAINT `fk_archive_work_point_attached_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
