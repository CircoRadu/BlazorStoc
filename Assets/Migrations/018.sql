-- version: 18
-- description: Intrari libere: motiv, furnizor, referinta; cantitati facturate pe factura
-- note: Entries without invoice: why (1 = awaited invoice ... 5 = other), the supplier it comes from (required for an awaited invoice) and a free
-- note: reference. `supplier_invoice_lines` keeps the quantity written on the invoice for a product (known from the automatic pickup or typed by hand),
-- note: to warn when the entries of the product on that invoice exceed it.
-- column: stock_movements.free_entry_type
-- column: stock_movements.free_supplier_id
-- column: stock_movements.reference
-- column: supplier_invoice_lines.invoice_id
-- column: supplier_invoice_lines.product_id
-- column: supplier_invoice_lines.quantity
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `free_entry_type` TINYINT NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `free_supplier_id` BIGINT NULL
-- statement
ALTER TABLE `stock_movements` ADD COLUMN IF NOT EXISTS `reference` VARCHAR(200) NULL
-- statement
ALTER TABLE `stock_movements` ADD INDEX IF NOT EXISTS `ix_stock_movements_free_supplier` (`free_supplier_id`)
-- statement
ALTER TABLE `stock_movements` ADD CONSTRAINT `fk_stock_movements_free_supplier` FOREIGN KEY IF NOT EXISTS (`free_supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
-- statement
CREATE TABLE IF NOT EXISTS `supplier_invoice_lines` (
  `invoice_id` BIGINT NOT NULL,
  `product_id` BIGINT NOT NULL,
  `quantity` INT NOT NULL,
  PRIMARY KEY (`invoice_id`, `product_id`),
  CONSTRAINT `fk_invoice_lines_invoice` FOREIGN KEY (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
