-- version: 17
-- description: Furnizori: jurnalul de recunoastere retine si schimbarea sablonului propus
-- note: Whether the user chose another template than the one proposed automatically for the invoice.
-- column: supplier_recognitions.template_changed
-- statement
ALTER TABLE `supplier_recognitions` ADD COLUMN IF NOT EXISTS `template_changed` TINYINT(1) NOT NULL DEFAULT 0
