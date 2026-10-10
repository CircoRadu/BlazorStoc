-- version: 25
-- description: Oferte preluate: oferta, liniile, potrivirile retinute si denumirile alternative ale beneficiarilor
-- note: Offers taken over from a devize-oferta: the offer (a revision per row, same number = same offer), its lines, the ties between offer lines and
-- note: catalog products that were confirmed (remembered for the next offers) and the alternative names of the beneficiaries seen in offers.
-- column: offers.id
-- column: offers.number
-- column: offers.number_key
-- column: offers.revision
-- column: offers.title
-- column: offers.category
-- column: offers.beneficiary_id
-- column: offers.project_id
-- column: offers.system_type_id
-- column: offers.template_id
-- column: offers.file_name
-- column: offers.created_by
-- column: offers.created_utc
-- column: offer_lines.id
-- column: offer_lines.offer_id
-- column: offer_lines.line_order
-- column: offer_lines.section
-- column: offer_lines.number
-- column: offer_lines.product_type
-- column: offer_lines.name
-- column: offer_lines.name_key
-- column: offer_lines.unit
-- column: offer_lines.quantity
-- column: offer_lines.in_stock
-- column: offer_lines.product_id
-- column: offer_line_matches.name_key
-- column: offer_line_matches.product_id
-- column: offer_line_matches.confirmed_by
-- column: offer_line_matches.confirmed_utc
-- column: beneficiary_aliases.id
-- column: beneficiary_aliases.beneficiary_id
-- column: beneficiary_aliases.alias
-- column: beneficiary_aliases.alias_key
-- column: beneficiary_aliases.created_by
-- column: beneficiary_aliases.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `offers` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `number` VARCHAR(60) NOT NULL,
  `number_key` VARCHAR(60) NOT NULL,
  `revision` INT NOT NULL,
  `title` VARCHAR(300) NOT NULL DEFAULT '',
  `category` VARCHAR(120) NOT NULL DEFAULT '',
  `beneficiary_id` BIGINT NOT NULL,
  `project_id` BIGINT NOT NULL,
  `system_type_id` BIGINT NULL,
  `template_id` BIGINT NULL,
  `file_name` VARCHAR(260) NOT NULL DEFAULT '',
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_offers_number_revision` (`number_key`, `revision`),
  KEY `ix_offers_project` (`project_id`),
  CONSTRAINT `fk_offers_project` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `offer_lines` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `offer_id` BIGINT NOT NULL,
  `line_order` INT NOT NULL,
  `section` VARCHAR(100) NOT NULL DEFAULT '',
  `number` VARCHAR(20) NOT NULL DEFAULT '',
  `product_type` VARCHAR(100) NOT NULL DEFAULT '',
  `name` TEXT NOT NULL,
  `name_key` VARCHAR(190) NOT NULL,
  `unit` VARCHAR(30) NOT NULL DEFAULT '',
  `quantity` DECIMAL(14,3) NOT NULL,
  `in_stock` TINYINT(1) NOT NULL DEFAULT 1,
  `product_id` BIGINT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_offer_lines_offer` (`offer_id`, `line_order`),
  KEY `ix_offer_lines_product` (`product_id`),
  CONSTRAINT `fk_offer_lines_offer` FOREIGN KEY (`offer_id`) REFERENCES `offers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `offer_line_matches` (
  `name_key` VARCHAR(190) NOT NULL,
  `product_id` BIGINT NOT NULL,
  `confirmed_by` VARCHAR(100) NOT NULL,
  `confirmed_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`name_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `beneficiary_aliases` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `alias` VARCHAR(200) NOT NULL,
  `alias_key` VARCHAR(200) NOT NULL,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_beneficiary_aliases_key` (`alias_key`),
  CONSTRAINT `fk_beneficiary_aliases_beneficiary` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
