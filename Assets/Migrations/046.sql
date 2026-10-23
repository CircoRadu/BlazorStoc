-- version: 46
-- description: Preluare ActivityApp, etapa tichete: detaliul sursei fiecarei interventii preluate (problema, rezolvarea, raportul original, executantul)
-- note: One row per intervention created by the ticket import (intervention_id = service_interventions.id). The notes of the intervention carry a readable summary cut to
-- note: 2000 characters; this row keeps the pieces apart and whole: the problem reported at the opening of the ticket, the resolution (what was added to the text when it was closed),
-- note: the original report of the event as written in ActivityApp, who did the work, and the ids in ActivityApp. Nothing here writes to ActivityApp.
-- column: service_intervention_source.intervention_id
-- column: service_intervention_source.source_ticket_id
-- column: service_intervention_source.source_type_id
-- column: service_intervention_source.source_event_id
-- column: service_intervention_source.ticket_number
-- column: service_intervention_source.title
-- column: service_intervention_source.problem
-- column: service_intervention_source.resolution
-- column: service_intervention_source.report
-- column: service_intervention_source.executor
-- column: service_intervention_source.imported_utc
-- statement
CREATE TABLE IF NOT EXISTS `service_intervention_source` (
  `intervention_id` BIGINT NOT NULL,
  `source_ticket_id` BIGINT NOT NULL,
  `source_type_id` BIGINT NOT NULL,
  `source_event_id` BIGINT NOT NULL,
  `ticket_number` VARCHAR(100) NOT NULL DEFAULT '',
  `title` VARCHAR(500) NOT NULL DEFAULT '',
  `problem` LONGTEXT NULL,
  `resolution` LONGTEXT NULL,
  `report` LONGTEXT NULL,
  `executor` VARCHAR(200) NOT NULL DEFAULT '',
  `imported_utc` DATETIME NOT NULL,
  PRIMARY KEY (`intervention_id`),
  UNIQUE KEY `uq_service_intervention_source_ticket` (`source_ticket_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
