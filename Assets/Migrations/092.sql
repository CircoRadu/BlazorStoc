-- version: 92
-- description: Elcomtur: istoricul totalului platilor restante, o valoare la fiecare preluare
-- note: Notificari -> Balante restante -> "Arata evolutie balanta": each import of an Elcomtur backup notes the total of the positive balances (the value of the "Total plati restante") with the date of the backup; a new import of the same date and firm replaces its line. Derived from the imports, never edited by hand.
-- column: elcomtur_balance_history.id
-- column: elcomtur_balance_history.data_date
-- column: elcomtur_balance_history.firm
-- column: elcomtur_balance_history.total
-- column: elcomtur_balance_history.owed_count
-- column: elcomtur_balance_history.imported_utc
-- column: elcomtur_balance_history.imported_by
-- statement
CREATE TABLE IF NOT EXISTS `elcomtur_balance_history` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `data_date` DATE NOT NULL,
  `firm` VARCHAR(50) NOT NULL,
  `total` DECIMAL(14,2) NOT NULL,
  `owed_count` INT NOT NULL,
  `imported_utc` VARCHAR(40) NOT NULL,
  `imported_by` VARCHAR(100) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_elcomtur_balance_history_date` (`data_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
