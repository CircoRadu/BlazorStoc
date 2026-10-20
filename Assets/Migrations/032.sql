-- version: 32
-- description: Sabloane de notificare implicite: evidenta evenimentelor tratate
-- note: The events that already got their starting template: a template the administrator deletes is not made again.
-- column: notification_template_seeds.source_key
-- column: notification_template_seeds.seeded_utc
-- statement
CREATE TABLE IF NOT EXISTS `notification_template_seeds` (
  `source_key` VARCHAR(60) NOT NULL,
  `seeded_utc` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`source_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
