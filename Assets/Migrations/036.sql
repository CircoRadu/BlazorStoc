-- version: 36
-- description: Furnizori: legatura dintre codul intern al furnizorului si produsul din catalog
-- note: The internal code a supplier gives a product on its invoices, linked to the product of the catalog; one row per supplier and code, removed with either of them.
-- column: supplier_product_codes.id
-- column: supplier_product_codes.supplier_id
-- column: supplier_product_codes.code_key
-- column: supplier_product_codes.code
-- column: supplier_product_codes.product_id
-- column: supplier_product_codes.created_by
-- column: supplier_product_codes.created_utc
-- column: supplier_product_codes.updated_by
-- column: supplier_product_codes.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `supplier_product_codes` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `supplier_id` BIGINT NOT NULL,
  `code_key` VARCHAR(100) NOT NULL,
  `code` VARCHAR(100) NOT NULL,
  `product_id` BIGINT NOT NULL,
  `created_by` VARCHAR(100) NOT NULL DEFAULT '',
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_supplier_product_codes_key` (`supplier_id`, `code_key`),
  KEY `ix_supplier_product_codes_product` (`product_id`),
  CONSTRAINT `fk_supplier_product_codes_supplier` FOREIGN KEY (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_supplier_product_codes_product` FOREIGN KEY (`product_id`) REFERENCES `products` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
