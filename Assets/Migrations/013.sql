-- version: 13
-- description: Furnizori si facturi de furnizor: registrul de furnizori, facturile si legatura intrarilor de stoc cu factura
-- note: The register of suppliers (Administrare -> Furnizori), the invoices stock entries are taken from and the link of an entry to its
-- note: invoice (null = a free entry). A supplier is unique by its tax id (`normalized_cui`: the digits of a Romanian CUI, the VAT identifier
-- note: with country prefix for another member state); an invoice number is unique per supplier. Nothing is deleted with its supplier or
-- note: invoice (RESTRICT): a supplier that has invoices cannot be deleted.
-- column: suppliers.id
-- column: suppliers.name
-- column: suppliers.cui
-- column: suppliers.normalized_cui
-- column: suppliers.country
-- column: suppliers.address
-- column: suppliers.phone
-- column: suppliers.registry_number
-- column: suppliers.postal_code
-- column: suppliers.caen_code
-- column: suppliers.source
-- column: suppliers.verified_utc
-- column: suppliers.created_by
-- column: suppliers.created_utc
-- column: suppliers.version
-- column: archive_suppliers.archive_id
-- column: archive_suppliers.original_id
-- column: archive_suppliers.name
-- column: archive_suppliers.cui
-- column: archive_suppliers.country
-- column: archive_suppliers.source
-- column: archive_suppliers.version
-- column: supplier_invoices.id
-- column: supplier_invoices.supplier_id
-- column: supplier_invoices.number
-- column: supplier_invoices.normalized_number
-- column: supplier_invoices.issue_date
-- column: supplier_invoices.created_by
-- column: supplier_invoices.created_utc
-- column: stock_movements.invoice_id
-- column: archive_stock_movements.invoice_id
-- statement
CREATE TABLE IF NOT EXISTS `suppliers` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `name` VARCHAR(200) NOT NULL,
  `cui` VARCHAR(20) NOT NULL,
  `normalized_cui` VARCHAR(30) NOT NULL,
  `country` VARCHAR(2) NOT NULL DEFAULT 'RO',
  `address` VARCHAR(300) NOT NULL DEFAULT '',
  `phone` VARCHAR(20) NOT NULL DEFAULT '',
  `registry_number` VARCHAR(40) NOT NULL DEFAULT '',
  `postal_code` VARCHAR(10) NOT NULL DEFAULT '',
  `caen_code` VARCHAR(4) NOT NULL DEFAULT '',
  `source` VARCHAR(1) NOT NULL DEFAULT 'M',
  `verified_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_suppliers_cui` (`normalized_cui`),
  KEY `ix_suppliers_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `archive_suppliers` (
  `archive_id` VARCHAR(64) NOT NULL,
  `original_id` BIGINT NOT NULL,
  `name` VARCHAR(200) NOT NULL,
  `cui` VARCHAR(20) NOT NULL,
  `country` VARCHAR(2) NOT NULL,
  `source` VARCHAR(1) NOT NULL,
  `version` BIGINT NOT NULL,
  PRIMARY KEY (`archive_id`),
  KEY `ix_archive_suppliers_original` (`original_id`),
  CONSTRAINT `fk_archive_suppliers_0` FOREIGN KEY (`archive_id`) REFERENCES `archive_operations` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `supplier_invoices` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `supplier_id` BIGINT NOT NULL,
  `number` VARCHAR(50) NOT NULL,
  `normalized_number` VARCHAR(50) NOT NULL,
  `issue_date` VARCHAR(10) NOT NULL,
  `created_by` VARCHAR(100) NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_supplier_invoices_number` (`supplier_id`, `normalized_number`),
  CONSTRAINT `fk_supplier_invoices_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `invoice_id` BIGINT NULL
-- statement
ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_invoice` (`invoice_id`)
-- statement
ALTER TABLE `stock_movements` ADD CONSTRAINT `fk_stock_movements_invoice` FOREIGN KEY IF NOT EXISTS (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
-- statement
ALTER TABLE `archive_stock_movements` ADD COLUMN IF NOT EXISTS `invoice_id` BIGINT NULL
