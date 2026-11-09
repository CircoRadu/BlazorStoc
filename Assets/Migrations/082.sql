-- version: 82
-- description: Nivel de prioritate la taskurile utilizatorilor
-- note: priority: 1 = Scazut, 2 = Mediu, 3 = Ridicat. Every task that existed before this version gets 1 (Scazut) through the column default.
-- column: beneficiary_tasks.priority
-- statement
ALTER TABLE `beneficiary_tasks` ADD COLUMN IF NOT EXISTS `priority` TINYINT NOT NULL DEFAULT 1
