-- version: 59
-- description: Nomenclator Analizor de risc (risk_analyzers) si analizorul ales la analiza de risc (risk_analyses.analyzer_id)
-- note: Nomenclator -> Analizor de risc: the register of risk analyzers (the person or firm that makes risk analyses). An analysis points to one (NULL for analyses made before this migration). An analyzer used by an analysis cannot be deleted.
-- column: risk_analyzers.id
-- column: risk_analyzers.name
-- column: risk_analyzers.name_key
-- column: risk_analyzers.notes
-- column: risk_analyzers.version
-- column: risk_analyzers.created_utc
-- column: risk_analyzers.updated_utc
-- column: risk_analyses.analyzer_id
-- statement
CREATE TABLE IF NOT EXISTS `risk_analyzers` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(120) NOT NULL,
  `name_key` VARCHAR(120) NOT NULL,
  `notes` VARCHAR(500) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_risk_analyzers_key` (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `risk_analyses` ADD COLUMN IF NOT EXISTS `analyzer_id` BIGINT NULL
-- statement
ALTER TABLE `risk_analyses` ADD INDEX IF NOT EXISTS `ix_risk_analyses_analyzer` (`analyzer_id`)
-- statement
ALTER TABLE `risk_analyses` ADD CONSTRAINT `fk_risk_analyses_analyzer` FOREIGN KEY IF NOT EXISTS (`analyzer_id`) REFERENCES `risk_analyzers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
