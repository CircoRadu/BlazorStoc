-- version: 84
-- description: Setari de afisare per utilizator (global si pe tabel)
-- note: user_display_settings holds, per user, the display scale chosen by that user: scope 'global' = the rules for the whole application, any other scope = the id of one table (for example 'produse.catalog') whose particular settings override, field by field, the global ones. settings is a small JSON text (percent of the default size for table text, header text, images, icons, page titles, tab text). Removed together with the user.
-- column: user_display_settings.user_id
-- column: user_display_settings.scope
-- column: user_display_settings.settings
-- statement
CREATE TABLE IF NOT EXISTS `user_display_settings` (
  `user_id` INT NOT NULL,
  `scope` VARCHAR(80) NOT NULL,
  `settings` VARCHAR(1000) NOT NULL DEFAULT '',
  `updated_utc` VARCHAR(40) NOT NULL DEFAULT '',
  PRIMARY KEY (`user_id`, `scope`),
  CONSTRAINT `fk_user_display_settings_user` FOREIGN KEY (`user_id`) REFERENCES `web_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
