-- version: 77
-- description: Scadente de mentenanta pe punct de lucru si componenta (unitate de contract)
-- note: Each covered work point has one due date per unit that covers it (service_contract_unit_dues); service_contract_points.next_due stays the EARLIEST of them (kept by the application) so the lists, the map and the old readers keep working. service_intervention_unit_dues keeps, for an intervention that moved dates, the date each unit had before and after, so a correction or a deletion gives exactly those dates back.
-- column: service_contract_unit_dues.id
-- column: service_contract_unit_dues.point_id
-- column: service_contract_unit_dues.unit_id
-- column: service_contract_unit_dues.next_due
-- column: service_contract_unit_dues.version
-- column: service_intervention_unit_dues.intervention_id
-- column: service_intervention_unit_dues.unit_id
-- column: service_intervention_unit_dues.planned_due
-- column: service_intervention_unit_dues.next_due_set
-- statement
CREATE TABLE IF NOT EXISTS `service_contract_unit_dues` (
  `id` BIGINT NOT NULL AUTO_INCREMENT,
  `point_id` BIGINT NOT NULL,
  `unit_id` BIGINT NOT NULL,
  `next_due` DATE NOT NULL,
  `version` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_service_contract_unit_dues_pair` (`point_id`, `unit_id`),
  KEY `ix_service_contract_unit_dues_unit` (`unit_id`),
  CONSTRAINT `fk_service_contract_unit_dues_point` FOREIGN KEY (`point_id`) REFERENCES `service_contract_points` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_contract_unit_dues_unit` FOREIGN KEY (`unit_id`) REFERENCES `service_contract_units` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
-- statement
CREATE TABLE IF NOT EXISTS `service_intervention_unit_dues` (
  `intervention_id` BIGINT NOT NULL,
  `unit_id` BIGINT NOT NULL,
  `planned_due` DATE NOT NULL,
  `next_due_set` DATE NOT NULL,
  PRIMARY KEY (`intervention_id`, `unit_id`),
  KEY `ix_service_intervention_unit_dues_unit` (`unit_id`),
  CONSTRAINT `fk_service_intervention_unit_dues_intervention` FOREIGN KEY (`intervention_id`) REFERENCES `service_interventions` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_intervention_unit_dues_unit` FOREIGN KEY (`unit_id`) REFERENCES `service_contract_units` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
