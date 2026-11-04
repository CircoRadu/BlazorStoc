-- version: 62
-- description: Balantele de plata din Elcomtur pe beneficiar (elcomtur_balances) si facturile lor neachitate (elcomtur_invoices)
-- note: Administrare -> Preluare date Elcomtur: the balance (invoiced minus collected) of each client of the Elcomtur program that is a beneficiary (matched by CUI), with the invoices still marked unpaid there.
-- note: Derived data, replaced as a whole by every import (data_date = the date of the backup it came from); deleting a beneficiary removes its rows.
-- column: elcomtur_balances.beneficiary_id
-- column: elcomtur_balances.balance
-- column: elcomtur_balances.data_date
-- column: elcomtur_balances.firm
-- column: elcomtur_balances.imported_utc
-- column: elcomtur_balances.imported_by
-- column: elcomtur_invoices.id
-- column: elcomtur_invoices.beneficiary_id
-- column: elcomtur_invoices.number
-- column: elcomtur_invoices.invoice_date
-- column: elcomtur_invoices.due_date
-- column: elcomtur_invoices.total
-- column: elcomtur_invoices.paid
-- column: elcomtur_invoices.rest
-- statement
CREATE TABLE IF NOT EXISTS `elcomtur_balances` (
  `beneficiary_id` BIGINT NOT NULL,
  `balance` DECIMAL(14,2) NOT NULL,
  `data_date` DATE NOT NULL,
  `firm` VARCHAR(50) NOT NULL,
  `imported_utc` VARCHAR(40) NOT NULL,
  `imported_by` VARCHAR(100) NOT NULL,
  PRIMARY KEY (`beneficiary_id`),
  CONSTRAINT `fk_elcomtur_balances_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `elcomtur_invoices` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `beneficiary_id` BIGINT NOT NULL,
  `number` VARCHAR(30) NOT NULL,
  `invoice_date` DATE NOT NULL,
  `due_date` DATE NOT NULL,
  `total` DECIMAL(14,2) NOT NULL,
  `paid` DECIMAL(14,2) NOT NULL,
  `rest` DECIMAL(14,2) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_elcomtur_invoices_0` (`beneficiary_id`, `due_date`),
  CONSTRAINT `fk_elcomtur_invoices_0` FOREIGN KEY (`beneficiary_id`) REFERENCES `beneficiaries` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
