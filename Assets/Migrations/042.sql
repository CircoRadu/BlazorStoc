-- version: 42
-- description: Module oprite manual de administrator (Setari -> Module): modulul, motivul, cine l-a oprit si cand
-- note: One row per module stopped by hand; the row is deleted when the module is started again. The rows are read at startup (ModuleStateRestorer), so a module
-- note: stopped by the administrator stays out after a restart of the application. The module ids are the ones of IModule.Id (Modules/*Module.cs).
-- column: module_state.module_id
-- column: module_state.reason
-- column: module_state.stopped_by
-- column: module_state.stopped_utc
-- statement
CREATE TABLE IF NOT EXISTS `module_state` (
  `module_id` VARCHAR(60) NOT NULL,
  `reason` VARCHAR(500) NOT NULL DEFAULT '',
  `stopped_by` VARCHAR(100) NOT NULL,
  `stopped_utc` DATETIME NOT NULL,
  PRIMARY KEY (`module_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
