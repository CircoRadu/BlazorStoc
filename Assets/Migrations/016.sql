-- version: 16
-- description: Furnizori: jurnal de recunoastere a furnizorului per factura preluata
-- note: What the recognition of the supplier proposed for an invoice and what the user chose (to see where the reading goes wrong); one row per invoice, removed with it.
-- column: supplier_recognitions.id
-- column: supplier_recognitions.invoice_id
-- column: supplier_recognitions.method
-- column: supplier_recognitions.confidence
-- column: supplier_recognitions.read_name
-- column: supplier_recognitions.read_cui
-- column: supplier_recognitions.recognized_supplier_id
-- column: supplier_recognitions.chosen_supplier_id
-- column: supplier_recognitions.corrected
-- column: supplier_recognitions.created_utc
-- statement
CREATE TABLE IF NOT EXISTS `supplier_recognitions` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `invoice_id` BIGINT NOT NULL,
  `method` VARCHAR(10) NOT NULL,
  `confidence` VARCHAR(10) NOT NULL,
  `read_name` VARCHAR(200) NOT NULL DEFAULT '',
  `read_cui` VARCHAR(30) NOT NULL DEFAULT '',
  `recognized_supplier_id` BIGINT NULL,
  `chosen_supplier_id` BIGINT NULL,
  `corrected` TINYINT(1) NOT NULL DEFAULT 0,
  `created_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_supplier_recognitions_invoice` (`invoice_id`),
  CONSTRAINT `fk_supplier_recognitions_invoice` FOREIGN KEY (`invoice_id`) REFERENCES `supplier_invoices` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
