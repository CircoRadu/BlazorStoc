-- version: 15
-- description: Furnizori: denumiri alternative (alias-uri) pentru recunoasterea numelui de pe factura
-- note: Other names a supplier is written with on invoices; `alias_key` (the name without legal form, dots, case) is unique: an alias points to one supplier.
-- column: supplier_aliases.id
-- column: supplier_aliases.supplier_id
-- column: supplier_aliases.alias
-- column: supplier_aliases.alias_key
-- column: supplier_aliases.created_by
-- column: supplier_aliases.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `supplier_aliases` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `supplier_id` BIGINT NOT NULL,
  `alias` VARCHAR(200) NOT NULL,
  `alias_key` VARCHAR(200) NOT NULL,
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_supplier_aliases_key` (`alias_key`),
  KEY `ix_supplier_aliases_supplier` (`supplier_id`),
  CONSTRAINT `fk_supplier_aliases_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
