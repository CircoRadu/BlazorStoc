-- version: 94
-- description: Personalizarea paginii principale per utilizator (intrarile de meniu afisate ca tile-uri)
-- note: user_dashboard_entries holds, per user, the menu entries (key from the global menu catalog, for example 'produse.stoc') that the user chose to show as tiles on the main page, in the order chosen. A user without rows sees the standard main page; the rows are loaded at sign-in time and removed together with the user. The table is new: it starts empty.
-- column: user_dashboard_entries.user_id
-- column: user_dashboard_entries.entry_key
-- column: user_dashboard_entries.position
-- statement
CREATE TABLE IF NOT EXISTS `user_dashboard_entries` (
  `user_id` BIGINT NOT NULL,
  `entry_key` VARCHAR(80) NOT NULL,
  `position` INT NOT NULL DEFAULT 0,
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`user_id`, `entry_key`),
  CONSTRAINT `fk_user_dashboard_entries_user` FOREIGN KEY (`user_id`) REFERENCES `web_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
