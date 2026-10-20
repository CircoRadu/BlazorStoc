-- version: 14
-- description: Sabloane de facturi: legatura cu furnizorul prin supplier_id
-- note: A template is tied to its supplier by id (the name shown is the supplier's own, so a rename in the register reaches the template); the
-- note: tax id column stays for matching. Templates of a supplier already in the register are linked here; a supplier with templates cannot be deleted (RESTRICT).
-- column: invoice_templates.supplier_id
-- statement
ALTER TABLE `invoice_templates` ADD COLUMN IF NOT EXISTS `supplier_id` BIGINT NULL
-- statement
ALTER TABLE `invoice_templates` ADD INDEX IF NOT EXISTS `ix_invoice_templates_supplier` (`supplier_id`)
-- statement
ALTER TABLE `invoice_templates` ADD CONSTRAINT `fk_invoice_templates_supplier` FOREIGN KEY IF NOT EXISTS (`supplier_id`) REFERENCES `suppliers` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
