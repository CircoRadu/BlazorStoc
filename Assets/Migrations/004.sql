-- version: 4
-- description: Notificari de expirare: sabloane si notificari (starea preluat/amanat este globala)
-- column: notification_templates.id
-- column: notification_templates.source_key
-- column: notification_templates.subject
-- column: notification_templates.body
-- column: notification_templates.threshold_days
-- column: notification_templates.is_active
-- column: notification_templates.version
-- column: expiry_notifications.id
-- column: expiry_notifications.template_id
-- column: expiry_notifications.source_key
-- column: expiry_notifications.object_id
-- column: expiry_notifications.expiry_date
-- column: expiry_notifications.created_utc
-- column: expiry_notifications.acknowledged_by
-- column: expiry_notifications.acknowledged_utc
-- column: expiry_notifications.snooze_until
-- column: expiry_notifications.snooze_days
-- column: expiry_notifications.version
-- statement
CREATE TABLE IF NOT EXISTS `notification_templates` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `source_key` VARCHAR(60) NOT NULL,
  `subject` VARCHAR(200) NOT NULL,
  `body` VARCHAR(2000) NOT NULL,
  `threshold_days` INT NOT NULL,
  `is_active` TINYINT NOT NULL DEFAULT 1,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `expiry_notifications` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `template_id` BIGINT NOT NULL,
  `source_key` VARCHAR(60) NOT NULL,
  `object_id` BIGINT NOT NULL,
  `expiry_date` DATE NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `acknowledged_by` VARCHAR(100) NULL,
  `acknowledged_utc` VARCHAR(40) NULL,
  `snooze_until` DATE NULL,
  `snooze_days` INT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_expiry_notifications_0` (`template_id`, `object_id`, `expiry_date`),
  CONSTRAINT `fk_expiry_notifications_0` FOREIGN KEY (`template_id`) REFERENCES `notification_templates` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
