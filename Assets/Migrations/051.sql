-- version: 51
-- description: Setari -> Jurnal activitate: cate evenimente recente arata implicit pagina Jurnal activitate
-- note: journal_settings has one row (id 1). recent_window is the number of the most recent events the page Jurnal activitate works on (at least 50, at most 50000; 500 until the administrator changes it). Nothing is ever deleted from the journal: the complete journal keeps showing every event.
-- column: journal_settings.id
-- column: journal_settings.recent_window
-- column: journal_settings.updated_by
-- column: journal_settings.updated_utc
-- statement
CREATE TABLE IF NOT EXISTS `journal_settings` (
  `id` INT NOT NULL,
  `recent_window` INT NOT NULL DEFAULT 500,
  `updated_by` VARCHAR(100) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
