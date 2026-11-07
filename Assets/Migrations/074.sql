-- version: 74
-- description: Module oprite manual: rolul utilizatorului care a oprit modulul
-- note: module_state.stopped_role keeps the role (Administrator / Utilizator) of the person who stopped the module, shown with the user name in the "Modul oprit" notice. Rows saved before this migration have an empty role (the notice then shows the user name only).
-- column: module_state.stopped_role
-- statement
ALTER TABLE `module_state` ADD COLUMN IF NOT EXISTS `stopped_role` VARCHAR(60) NOT NULL DEFAULT ''
