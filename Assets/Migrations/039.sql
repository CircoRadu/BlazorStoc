-- version: 39
-- description: Analize de risc: analiza unui punct de lucru (numar, intocmit de, data initiala, ultima reinnoire, valabilitate in luni), istoricul reinnoirilor si arhivarea
-- note: A risk analysis belongs to a beneficiary AND one of its work points. The unique key on active_work_point_id stands in for a partial
-- note: unique index (MariaDB has none): it is equal to work_point_id while the analysis is active and NULL otherwise, so a work point has at
-- note: most one ACTIVE analysis and any number of Off ones. The application sets it in the same transaction that activates/deactivates the analysis.
-- note: last_renewal_date is a copy of the newest row of risk_analysis_renewals (the initial date when there is none); the expiry date is never
-- note: stored: last_renewal_date + validity_months months.
-- column: risk_analyses.id
-- column: risk_analyses.beneficiary_id
-- column: risk_analyses.work_point_id
-- column: risk_analyses.registration_number
-- column: risk_analyses.author
-- column: risk_analyses.initial_date
-- column: risk_analyses.last_renewal_date
-- column: risk_analyses.validity_months
-- column: risk_analyses.is_active
-- column: risk_analyses.active_work_point_id
-- column: risk_analyses.notes
-- column: risk_analyses.version
-- column: risk_analysis_renewals.id
-- column: risk_analysis_renewals.risk_analysis_id
-- column: risk_analysis_renewals.renewal_date
-- column: risk_analysis_renewals.previous_renewal_date
-- column: risk_analysis_renewals.recorded_by
-- column: risk_analysis_renewals.recorded_utc
-- column: risk_analysis_renewals.notes
-- column: archive_risk_analyses.archive_id
-- column: archive_risk_analyses.original_id
-- column: archive_risk_analyses.beneficiary_id
-- column: archive_risk_analyses.work_point_id
-- column: archive_risk_analyses.registration_number
-- column: archive_risk_analyses.author
-- column: archive_risk_analyses.initial_date
-- column: archive_risk_analyses.last_renewal_date
-- column: archive_risk_analyses.validity_months
-- column: archive_risk_analyses.is_active
-- column: archive_risk_analyses.notes
-- column: archive_risk_analyses.version
-- statement
CREATE TABLE IF NOT EXISTS `risk_analyses` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `registration_number` VARCHAR(30) NOT NULL,
  `author` VARCHAR(120) NOT NULL,
  `initial_date` DATE NOT NULL,
  `last_renewal_date` DATE NOT NULL,
  `validity_months` SMALLINT NOT NULL DEFAULT 36,
  `is_active` TINYINT NOT NULL DEFAULT 1,
  `active_work_point_id` BIGINT NULL,
  `notes` VARCHAR(1000) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_risk_analyses_number` (`beneficiary_id`, `registration_number`),
  UNIQUE KEY `uq_risk_analyses_active` (`active_work_point_id`),
  KEY `ix_risk_analyses_work_point` (`work_point_id`),
  CONSTRAINT `fk_risk_analyses_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `fk_risk_analyses_work_point` FOREIGN KEY (`work_point_id`) REFERENCES `beneficiary_work_points` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_risk_analyses_validity` CHECK (`validity_months` BETWEEN 1 AND 120),
  CONSTRAINT `ck_risk_analyses_renewal` CHECK (`last_renewal_date` >= `initial_date`),
  CONSTRAINT `ck_risk_analyses_active` CHECK (`active_work_point_id` IS NULL OR `active_work_point_id` = `work_point_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `risk_analysis_renewals` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `risk_analysis_id` BIGINT NOT NULL,
  `renewal_date` DATE NOT NULL,
  `previous_renewal_date` DATE NOT NULL,
  `recorded_by` VARCHAR(100) NOT NULL,
  `recorded_utc` VARCHAR(40) NOT NULL,
  `notes` VARCHAR(1000) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `ix_risk_analysis_renewals_analysis` (`risk_analysis_id`, `renewal_date`),
  CONSTRAINT `fk_risk_analysis_renewals_analysis` FOREIGN KEY (`risk_analysis_id`) REFERENCES `risk_analyses` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION,
  CONSTRAINT `ck_risk_analysis_renewals_dates` CHECK (`renewal_date` > `previous_renewal_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_risk_analyses` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `beneficiary_id` BIGINT NOT NULL,
  `work_point_id` BIGINT NOT NULL,
  `registration_number` LONGTEXT NOT NULL,
  `author` LONGTEXT NOT NULL,
  `initial_date` DATE NOT NULL,
  `last_renewal_date` DATE NOT NULL,
  `validity_months` INT NOT NULL,
  `is_active` TINYINT NOT NULL,
  `notes` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_risk_analyses_original` (`original_id`),
  CONSTRAINT `fk_archive_risk_analyses_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
