-- version: 5
-- description: Notificari: stare rezolvata cu motiv si instantaneu, o singura notificare pe eveniment, un singur sablon activ pe eveniment
-- column: expiry_notifications.resolved_by
-- column: expiry_notifications.resolved_utc
-- column: expiry_notifications.resolved_reason
-- column: expiry_notifications.resolved_auto
-- column: expiry_notifications.object_label
-- column: expiry_notifications.snapshot_values
-- column: expiry_notifications.snapshot_subject
-- column: expiry_notifications.snapshot_body
-- column: expiry_notifications.snapshot_source
-- column: notification_templates.active_source_key
-- statement
ALTER TABLE `expiry_notifications`
    ADD COLUMN IF NOT EXISTS `resolved_by` VARCHAR(100) NULL,
    ADD COLUMN IF NOT EXISTS `resolved_utc` VARCHAR(40) NULL,
    ADD COLUMN IF NOT EXISTS `resolved_reason` VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS `resolved_auto` TINYINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `object_label` VARCHAR(300) NULL,
    ADD COLUMN IF NOT EXISTS `snapshot_values` TEXT NULL,
    ADD COLUMN IF NOT EXISTS `snapshot_subject` VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS `snapshot_body` TEXT NULL,
    ADD COLUMN IF NOT EXISTS `snapshot_source` VARCHAR(200) NULL
-- statement
ALTER TABLE `expiry_notifications` ADD INDEX IF NOT EXISTS `ix_expiry_notifications_template` (`template_id`)
-- statement
ALTER TABLE `expiry_notifications` DROP INDEX IF EXISTS `uq_expiry_notifications_0`, ADD UNIQUE INDEX IF NOT EXISTS `uq_expiry_notifications_event` (`source_key`, `object_id`, `expiry_date`)
-- statement
ALTER TABLE `notification_templates` ADD COLUMN IF NOT EXISTS `active_source_key` VARCHAR(60) AS (IF(`is_active` = 1, `source_key`, NULL)) PERSISTENT
-- statement
ALTER TABLE `notification_templates` ADD UNIQUE INDEX IF NOT EXISTS `uq_notification_templates_active` (`active_source_key`)
