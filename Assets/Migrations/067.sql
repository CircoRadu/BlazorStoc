-- version: 67
-- description: Numele cu care au intrat informatiile tichetului in observatiile unei interventii: service_intervention_source.notes_labels
-- note: JSON (key of the piece of information -> the name written in front of its text in the notes). A refresh needs it to recognise the notes the import wrote when the administrator has renamed a piece since; empty for the interventions taken before names could be chosen (the usual names are then assumed).
-- column: service_intervention_source.notes_labels
-- statement
ALTER TABLE `service_intervention_source` ADD COLUMN IF NOT EXISTS `notes_labels` LONGTEXT NULL
