-- version: 24
-- description: Oferte: sabloane de devize-oferta (xlsx)
-- note: Module Oferte: templates of offer sheets (.xlsx); the definition (columns by letter, header labels, sections, ignored rows) is JSON.
-- column: offer_templates.id
-- column: offer_templates.name
-- column: offer_templates.name_key
-- column: offer_templates.active
-- column: offer_templates.definition
-- column: offer_templates.version
-- column: offer_templates.created_by
-- column: offer_templates.created_utc
-- column: offer_templates.updated_by
-- column: offer_templates.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `offer_templates` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(120) NOT NULL,
  `name_key` VARCHAR(120) NOT NULL,
  `active` TINYINT(1) NOT NULL DEFAULT 1,
  `definition` LONGTEXT NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `updated_by` VARCHAR(100) NOT NULL,
  `updated_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_offer_templates_key` (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
