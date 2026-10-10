-- version: 10
-- description: Sabloane de facturi: sabloane (furnizor, definitie JSON) si versiunile lor salvate
-- note: Templates that read supplier invoices (Settings -> Facturi): the current definition (JSON, positions as fractions of the page) and
-- note: every saved version of it. A name is unique per supplier tax id (the application also compares without letter case).
-- column: invoice_templates.id
-- column: invoice_templates.name
-- column: invoice_templates.supplier_name
-- column: invoice_templates.supplier_cui
-- column: invoice_templates.source_kind
-- column: invoice_templates.version_number
-- column: invoice_templates.definition
-- column: invoice_templates.created_by
-- column: invoice_templates.created_utc
-- column: invoice_templates.updated_by
-- column: invoice_templates.updated_utc
-- column: invoice_templates.version
-- column: invoice_template_versions.id
-- column: invoice_template_versions.template_id
-- column: invoice_template_versions.version_number
-- column: invoice_template_versions.definition
-- column: invoice_template_versions.note
-- column: invoice_template_versions.created_by
-- column: invoice_template_versions.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `invoice_templates` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(120) NOT NULL,
  `supplier_name` VARCHAR(200) NOT NULL DEFAULT '',
  `supplier_cui` VARCHAR(20) NOT NULL DEFAULT '',
  `source_kind` VARCHAR(10) NOT NULL DEFAULT 'text',
  `version_number` INT NOT NULL DEFAULT 1,
  `definition` LONGTEXT NOT NULL,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_by` VARCHAR(100) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_invoice_templates_name` (`supplier_cui`, `name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `invoice_template_versions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `template_id` BIGINT NOT NULL,
  `version_number` INT NOT NULL,
  `definition` LONGTEXT NOT NULL,
  `note` VARCHAR(300) NOT NULL DEFAULT '',
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_invoice_template_versions_number` (`template_id`, `version_number`),
  CONSTRAINT `fk_invoice_template_versions_template` FOREIGN KEY (`template_id`) REFERENCES `invoice_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
