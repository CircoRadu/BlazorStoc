-- version: 65
-- description: Campurile tichetelor ActivityApp: ce s-a citit pentru fiecare tichet (fields_read), campurile suplimentare (extras) si unde au intrat in observatii (notes_items)
-- note: fields_read lists what was read from ActivityApp for the ticket (number, title, problem, report, executor and the extra fields): a ticket that lacks something the settings now ask for is read again. The tickets read before this version hold all five.
-- note: extras holds the extra fields as JSON (key of the field -> text). notes_items lists the pieces of information that went into the notes of the intervention when it was taken; a refresh rewrites the notes only when they still are what the import wrote.
-- column: activity_import_tickets.fields_read
-- column: activity_import_tickets.extras
-- column: service_intervention_source.fields_read
-- column: service_intervention_source.extras
-- column: service_intervention_source.notes_items
-- statement
ALTER TABLE `activity_import_tickets` ADD COLUMN IF NOT EXISTS `fields_read` VARCHAR(1000) NOT NULL DEFAULT 'executor,number,problem,report,title'
-- statement
ALTER TABLE `activity_import_tickets` ADD COLUMN IF NOT EXISTS `extras` LONGTEXT NULL
-- statement
ALTER TABLE `service_intervention_source` ADD COLUMN IF NOT EXISTS `fields_read` VARCHAR(1000) NOT NULL DEFAULT 'executor,number,problem,report,title'
-- statement
ALTER TABLE `service_intervention_source` ADD COLUMN IF NOT EXISTS `extras` LONGTEXT NULL
-- statement
ALTER TABLE `service_intervention_source` ADD COLUMN IF NOT EXISTS `notes_items` VARCHAR(1000) NOT NULL DEFAULT 'problem,resolution,executor'
