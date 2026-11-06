-- version: 61
-- description: Coordonatele GPS ale evenimentului de rezolvare al unui tichet ActivityApp: activity_import_tickets.gps
-- note: The GPS location attached to the event is kept with the ticket as read (text, as ActivityApp holds it); when the ticket is taken it completes the coordinates of the work point if it has none.
-- column: activity_import_tickets.gps
-- statement
ALTER TABLE `activity_import_tickets` ADD COLUMN IF NOT EXISTS `gps` VARCHAR(100) NOT NULL DEFAULT ''
