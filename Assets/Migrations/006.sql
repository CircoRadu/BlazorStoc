-- version: 6
-- description: Setari notificari: curatarea periodica a notificarilor rezolvate (comutator, perioada in luni, ultima rulare)
-- note: One row (id 1), written by the application (the migrator has no data rights): a missing row means the defaults.
-- column: notification_settings.id
-- column: notification_settings.purge_enabled
-- column: notification_settings.purge_months
-- column: notification_settings.last_purge_utc
-- column: notification_settings.version
-- statement
CREATE TABLE IF NOT EXISTS `notification_settings` (
  `id` TINYINT NOT NULL,
  `purge_enabled` TINYINT NOT NULL DEFAULT 0,
  `purge_months` INT NOT NULL DEFAULT 12,
  `last_purge_utc` VARCHAR(40) NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
