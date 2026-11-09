-- version: 79
-- description: Componentele acoperite de o interventie de mentenanta
-- note: service_intervention_components keeps which components (nomenclator Componente mentenanta) a maintenance intervention covered; only the units that cover those components have their due dates moved. An intervention recorded without components (older ones, an automatic take) moved every unit of the point.
-- column: service_intervention_components.intervention_id
-- column: service_intervention_components.component_id
-- statement
CREATE TABLE IF NOT EXISTS `service_intervention_components` (
  `intervention_id` BIGINT NOT NULL,
  `component_id` BIGINT NOT NULL,
  PRIMARY KEY (`intervention_id`, `component_id`),
  KEY `ix_service_intervention_components_component` (`component_id`),
  CONSTRAINT `fk_service_intervention_components_intervention` FOREIGN KEY (`intervention_id`) REFERENCES `service_interventions` (`id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `fk_service_intervention_components_component` FOREIGN KEY (`component_id`) REFERENCES `maintenance_components` (`id`) ON DELETE RESTRICT ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
