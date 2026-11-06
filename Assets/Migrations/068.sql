-- version: 68
-- description: Structura tichetelor ActivityApp fata de care s-a verificat corespondenta campurilor: activity_import_settings.ticket_structure_snapshot, alert_ticket_structure_since, ticket_structure_diff
-- note: ticket_structure_snapshot is the list of the columns of Tickete and EvenimenteAgent (with the kind of their data) the correspondence was last checked against; at every application of the ticket import the structure found is compared with it. A difference starts alert_ticket_structure_since (the notification asks to check the correspondence) and keeps what changed in ticket_structure_diff; the administrator's confirmation (or the same structure again) clears both.
-- column: activity_import_settings.ticket_structure_snapshot
-- column: activity_import_settings.alert_ticket_structure_since
-- column: activity_import_settings.ticket_structure_diff
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `ticket_structure_snapshot` LONGTEXT NULL
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `alert_ticket_structure_since` DATETIME NULL
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `ticket_structure_diff` LONGTEXT NULL
