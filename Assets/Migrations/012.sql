-- version: 12
-- description: Sabloane de facturi: coloana active (sablon folosit la citirea facturilor)
-- note: Versioning of invoice templates was given up (nothing keeps the earlier versions): a template is replaced when saved. A supplier can
-- note: have several templates and chooses which ones are used when invoices are read: `active` (every existing template stays in use).
-- column: invoice_templates.active
-- statement
ALTER TABLE `invoice_templates` ADD COLUMN IF NOT EXISTS `active` TINYINT(1) NOT NULL DEFAULT 1
