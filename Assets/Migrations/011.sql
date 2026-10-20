-- version: 11
-- description: Sabloane de facturi: factura PDF folosita ca model
-- note: The invoice (PDF) a template was made from, kept with the template so that editing it shows the same invoice again. A row is added
-- note: when a different file is saved with a new version; the latest row is the model in use.
-- column: invoice_template_models.id
-- column: invoice_template_models.template_id
-- column: invoice_template_models.version_number
-- column: invoice_template_models.file_name
-- column: invoice_template_models.byte_length
-- column: invoice_template_models.sha256
-- column: invoice_template_models.content
-- column: invoice_template_models.created_by
-- column: invoice_template_models.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `invoice_template_models` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `template_id` BIGINT NOT NULL,
  `version_number` INT NOT NULL,
  `file_name` VARCHAR(255) NOT NULL,
  `byte_length` BIGINT NOT NULL,
  `sha256` CHAR(64) NOT NULL,
  `content` LONGBLOB NOT NULL,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_invoice_template_models_template` (`template_id`, `id`),
  CONSTRAINT `fk_invoice_template_models_template` FOREIGN KEY (`template_id`) REFERENCES `invoice_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
