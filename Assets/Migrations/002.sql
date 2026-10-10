-- version: 2
-- description: Beneficiari: puncte de lucru suplimentare
-- column: beneficiary_work_points.id
-- column: beneficiary_work_points.beneficiary_id
-- column: beneficiary_work_points.name
-- column: beneficiary_work_points.address
-- column: beneficiary_work_points.normalized_address
-- column: beneficiary_work_points.phone
-- column: beneficiary_work_points.contact_person
-- column: beneficiary_work_points.version
-- statement
CREATE TABLE IF NOT EXISTS `beneficiary_work_points` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `name` VARCHAR(200) NOT NULL,
  `address` VARCHAR(300) NOT NULL,
  `normalized_address` VARCHAR(400) NOT NULL,
  `phone` VARCHAR(20) NOT NULL DEFAULT '',
  `contact_person` VARCHAR(200) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_beneficiary_work_points_0` (`beneficiary_id`, `normalized_address`),
  CONSTRAINT `fk_beneficiary_work_points_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
