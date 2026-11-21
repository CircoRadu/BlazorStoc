-- version: 85
-- description: Taskuri: ordinea proprie a fiecarui utilizator si stadiul / notele de lucru
-- note: the migrator has no data rights, so the tasks that existed get their first order from the application (MariaUserTaskRepository.BackfillOrderAsync); sort_order = position of an active task in the list of its user (1, 2, 3...; 0 for a finished task); progress_note = the stage / working notes written by the user or the administrator, also on finished tasks
-- column: beneficiary_tasks.sort_order
-- column: beneficiary_tasks.progress_note
-- statement
ALTER TABLE `beneficiary_tasks` ADD COLUMN IF NOT EXISTS `sort_order` INT NOT NULL DEFAULT 0
-- statement
ALTER TABLE `beneficiary_tasks` ADD COLUMN IF NOT EXISTS `progress_note` TEXT NULL
