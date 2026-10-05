/*M!999999\- enable the sandbox mode */ 

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;

LOCK TABLES `app_metadata` WRITE;
/*!40000 ALTER TABLE `app_metadata` DISABLE KEYS */;
REPLACE INTO `app_metadata` (`key`, `value`) VALUES ('diacritics_normalized','2026-09-28T09:30:13.7557522Z'),
('legacy_audit_imported','2026-09-21T10:18:24.4097598Z'),
('legacy_product_state_reconciled','2026-09-21T10:19:42.9029360Z'),
('schema_version','10'),
('seed_version','1'),
('stock_movements_baseline','2026-09-24T13:18:21.9716678Z');
/*!40000 ALTER TABLE `app_metadata` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_beneficiaries` WRITE;
/*!40000 ALTER TABLE `archive_beneficiaries` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_beneficiaries` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_files` WRITE;
/*!40000 ALTER TABLE `archive_files` DISABLE KEYS */;
REPLACE INTO `archive_files` (`id`, `archive_id`, `relation_type`, `original_relation_id`, `live_relative_path`, `archive_relative_path`, `content_type`, `file_name`, `byte_length`, `content_hash`, `archived_utc`) VALUES (1,'f4255dbc-0eec-48f0-ac34-2da7222b9eac','FisierObservatie','1','b3a431e0fbb443cc9c909f1bbc9afe0f.jpg','FisierObservatie/1/f4255dbc-0eec-48f0-ac34-2da7222b9eac/Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg','image/jpeg','Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg',2294044,'7CFF1C1E3721A528DEDEA5F32165B4B17E12DC4DE8136CF598385D9C71A6352D','2026-09-24T13:30:25.1583204Z');
/*!40000 ALTER TABLE `archive_files` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_operations` WRITE;
/*!40000 ALTER TABLE `archive_operations` DISABLE KEYS */;
REPLACE INTO `archive_operations` (`id`, `entity_type`, `original_id`, `original_version`, `deleted_utc`, `actor_username`, `actor_role`, `motif`, `target`, `details`, `data_json`, `protected_data_json`) VALUES ('2b7dbdab-278a-40e2-b28a-d6797ddeac75','MiscareStoc','11',1,'2026-09-24T13:20:13.5402431Z','administrator.demo','Administrator','Miscarea nu va mai fi folosita','Diblu nylon 8 × 40 test 22','Cod produs: Diblu nylon 8 × 40 test 22; Tip: Ieșire; Cantitate: 7; Data: 24-09-2026; Descriere: Montaj la client - test; Beneficiar: Construct Demo SRL','{\"id\":11,\"productId\":12,\"kind\":0,\"quantity\":7,\"date\":\"2026-09-24\",\"description\":\"Montaj la client - test\",\"beneficiaryId\":1,\"beneficiaryName\":\"Construct Demo SRL\",\"projectId\":null,\"projectName\":null,\"operator\":\"administrator.demo\",\"version\":1,\"createdUtc\":\"2026-09-24T13:19:38.9151956Z\",\"updatedUtc\":\"2026-09-24T13:19:49.2022473Z\",\"modified\":true}',NULL),
('474461ab-d495-44cc-a13d-4a253be2418e','Proiect','1',0,'2026-09-24T12:30:35.8361653Z','administrator.demo','Administrator','Proiectul nu va mai fi folosit','Renovare hala productie','Denumire: Renovare hala productie; Beneficiar: Construct Demo SRL; Observații: Lucrari in doua etape','{\"id\":1,\"beneficiaryId\":1,\"name\":\"Renovare hala productie\",\"observations\":\"Lucrari in doua etape\",\"version\":0,\"createdAtUtc\":\"2026-09-24T12:28:27.0574186Z\",\"updatedAtUtc\":\"2026-09-24T12:28:27.0574186Z\"}',NULL),
('9bbaeabd-aaa1-49ff-ba46-946a0f17aefa','Observatie','1',0,'2026-09-24T12:30:20.0430141Z','administrator.demo','Administrator','Observatia nu va mai fi folosita','Verificare santier','Denumire: Verificare santier; Proiect: Renovare hala productie; Autor: administrator.demo','{\"id\":1,\"projectId\":1,\"name\":\"Verificare santier\",\"content\":\"\",\"author\":\"administrator.demo\",\"version\":0,\"createdAtUtc\":\"2026-09-24T12:28:58.3024678Z\",\"updatedAtUtc\":\"2026-09-24T12:28:58.3024678Z\"}',NULL),
('c9513c3b-98c4-4f3a-8bb1-4f613e511e28','MiscareStoc','15',0,'2026-09-25T06:19:04.9379333Z','administrator.demo','Administrator','Miscarea nu va mai fi folosita','Mașină de găurit cu acumulator','Cod produs: Mașină de găurit cu acumulator; Tip: Intrare; Cantitate: 1; Data: 31-12-2026; Descriere: Test d12312026ata viitoare','{\"id\":15,\"productId\":1,\"kind\":1,\"quantity\":1,\"date\":\"2026-12-31\",\"description\":\"Test d12312026ata viitoare\",\"beneficiaryId\":null,\"beneficiaryName\":null,\"projectId\":null,\"projectName\":null,\"operator\":\"administrator.demo\",\"version\":0,\"createdUtc\":\"2026-09-25T06:18:13.3758684Z\",\"updatedUtc\":\"2026-09-25T06:18:13.3758684Z\",\"modified\":false}',NULL),
('d55c2518-4b43-4971-bd6f-98cd22001f16','MiscareStoc','13',0,'2026-09-25T05:37:02.4784171Z','administrator.demo','Administrator','Miscarea nu va mai fi folosita','Mașină de găurit cu acumulator','Cod produs: Mașină de găurit cu acumulator; Tip: Ieșire; Cantitate: 2; Data: 25-09-2026; Descriere: Montaj hala test; Beneficiar: Construct Demo SRL; Proiect: Hala nord test B','{\"id\":13,\"productId\":1,\"kind\":0,\"quantity\":2,\"date\":\"2026-09-25\",\"description\":\"Montaj hala test\",\"beneficiaryId\":1,\"beneficiaryName\":\"Construct Demo SRL\",\"projectId\":4,\"projectName\":\"Hala nord test B\",\"operator\":\"administrator.demo\",\"version\":0,\"createdUtc\":\"2026-09-25T05:34:51.5331743Z\",\"updatedUtc\":\"2026-09-25T05:34:51.5331743Z\",\"modified\":false}',NULL),
('d5c533a5-c629-4b1b-97cd-3e48f6c535ed','Proiect','3',0,'2026-09-24T13:37:14.5638531Z','administrator.demo','Administrator','Proiectul nu va mai fi folosit','instalare efractie','Denumire: instalare efractie; Beneficiar: Atelier Tehnic SRL 22; Observații:','{\"id\":3,\"beneficiaryId\":2,\"name\":\"instalare efractie\",\"observations\":\"\",\"version\":0,\"createdAtUtc\":\"2026-09-24T13:37:01.6573449Z\",\"updatedAtUtc\":\"2026-09-24T13:37:01.6573449Z\"}',NULL),
('ea82438f-389d-4519-8c7a-c31b12815000','Proiect','4',2,'2026-09-25T05:37:25.3989522Z','administrator.demo','Administrator','Proiectul nu va mai fi folosit','Hala nord test B','Denumire: Hala nord test B; Beneficiar: Construct Demo SRL; Observații: modificat din tab B','{\"id\":4,\"beneficiaryId\":1,\"name\":\"Hala nord test B\",\"observations\":\"modificat din tab B\",\"version\":2,\"createdAtUtc\":\"2026-09-25T05:33:42.8842763Z\",\"updatedAtUtc\":\"2026-09-25T05:36:37.7869726Z\"}',NULL),
('f4255dbc-0eec-48f0-ac34-2da7222b9eac','FisierObservatie','1',0,'2026-09-24T13:30:25.1583204Z','administrator.demo','Administrator','Fisierul nu va mai fi folosit','Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg','Nume fișier: Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg; Autor: administrator.demo','{\"id\":1,\"observationId\":2,\"originalName\":\"Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg\",\"storedName\":\"b3a431e0fbb443cc9c909f1bbc9afe0f.jpg\",\"contentType\":\"image/jpeg\",\"sizeBytes\":2294044,\"sha256\":\"7cff1c1e3721a528dedea5f32165b4b17e12dc4de8136cf598385d9c71a6352d\",\"author\":\"administrator.demo\",\"uploadedAtUtc\":\"2026-09-24T13:28:43.4523563Z\"}',NULL),
('fa786b41-f72a-4525-a4aa-603a7d235158','MiscareStoc','17',0,'2026-09-25T06:50:16.8685205Z','administrator.demo','Administrator','Miscarea nu va mai fi folosita','Mașină de găurit cu acumulator','Cod produs: Mașină de găurit cu acumulator; Tip: Intrare; Cantitate: 1; Data: 25-09-2026; Descriere: Test sincronizare','{\"id\":17,\"productId\":1,\"kind\":1,\"quantity\":1,\"date\":\"2026-09-25\",\"description\":\"Test sincronizare\",\"beneficiaryId\":null,\"beneficiaryName\":null,\"projectId\":null,\"projectName\":null,\"operator\":\"administrator.demo\",\"version\":0,\"createdUtc\":\"2026-09-25T06:50:02.0120254Z\",\"updatedUtc\":\"2026-09-25T06:50:02.0120254Z\",\"modified\":false}',NULL);
/*!40000 ALTER TABLE `archive_operations` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_products` WRITE;
/*!40000 ALTER TABLE `archive_products` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_products` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_project_observation_files` WRITE;
/*!40000 ALTER TABLE `archive_project_observation_files` DISABLE KEYS */;
REPLACE INTO `archive_project_observation_files` (`archive_id`, `original_id`, `observation_id`, `original_name`, `content_type`, `byte_length`, `sha256`, `author`, `uploaded_utc`) VALUES ('f4255dbc-0eec-48f0-ac34-2da7222b9eac',1,2,'Beach_Nature_Ultra_HD_Wallpaper_for_4K_UHD_Widescr.jpg','image/jpeg',2294044,'7cff1c1e3721a528dedea5f32165b4b17e12dc4de8136cf598385d9c71a6352d','administrator.demo','2026-09-24T13:28:43.4523563Z');
/*!40000 ALTER TABLE `archive_project_observation_files` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_project_observations` WRITE;
/*!40000 ALTER TABLE `archive_project_observations` DISABLE KEYS */;
REPLACE INTO `archive_project_observations` (`archive_id`, `original_id`, `project_id`, `name`, `content`, `author`, `version`) VALUES ('9bbaeabd-aaa1-49ff-ba46-946a0f17aefa',1,1,'Verificare santier','','administrator.demo',0);
/*!40000 ALTER TABLE `archive_project_observations` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_projects` WRITE;
/*!40000 ALTER TABLE `archive_projects` DISABLE KEYS */;
REPLACE INTO `archive_projects` (`archive_id`, `original_id`, `beneficiary_id`, `name`, `observations`, `version`) VALUES ('474461ab-d495-44cc-a13d-4a253be2418e',1,1,'Renovare hala productie','Lucrari in doua etape',0),
('d5c533a5-c629-4b1b-97cd-3e48f6c535ed',3,2,'instalare efractie','',0),
('ea82438f-389d-4519-8c7a-c31b12815000',4,1,'Hala nord test B','modificat din tab B',2);
/*!40000 ALTER TABLE `archive_projects` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_relations` WRITE;
/*!40000 ALTER TABLE `archive_relations` DISABLE KEYS */;
REPLACE INTO `archive_relations` (`id`, `archive_id`, `relation_type`, `original_relation_id`, `data_json`) VALUES (1,'2b7dbdab-278a-40e2-b28a-d6797ddeac75','IstoricMiscareStoc','1','{\"id\":1,\"movementId\":11,\"actor\":\"administrator.demo\",\"timestampUtc\":\"2026-09-24T13:19:49.2022473Z\",\"changes\":\"Cantitate: 5 \\u2192 7; Corec\\u021Bie stoc: -2\",\"stockCorrection\":-2,\"reason\":\"Cantitate gresita la introducere\"}'),
(2,'ea82438f-389d-4519-8c7a-c31b12815000','Observatie','4','{\"id\":4,\"projectId\":4,\"name\":\"Observatie din al doilea tab\",\"content\":\"\",\"author\":\"administrator.demo\",\"version\":0,\"createdAtUtc\":\"2026-09-25T05:36:01.8141441Z\",\"updatedAtUtc\":\"2026-09-25T05:36:01.8141441Z\"}'),
(3,'ea82438f-389d-4519-8c7a-c31b12815000','Observatie','3','{\"id\":3,\"projectId\":4,\"name\":\"Observatie jurnal test\",\"content\":\"\",\"author\":\"administrator.demo\",\"version\":0,\"createdAtUtc\":\"2026-09-25T05:35:14.1585573Z\",\"updatedAtUtc\":\"2026-09-25T05:35:14.1585573Z\"}');
/*!40000 ALTER TABLE `archive_relations` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_service_contracts` WRITE;
/*!40000 ALTER TABLE `archive_service_contracts` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_service_contracts` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_service_interventions` WRITE;
/*!40000 ALTER TABLE `archive_service_interventions` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_service_interventions` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_service_photos` WRITE;
/*!40000 ALTER TABLE `archive_service_photos` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_service_photos` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_stock_movements` WRITE;
/*!40000 ALTER TABLE `archive_stock_movements` DISABLE KEYS */;
REPLACE INTO `archive_stock_movements` (`archive_id`, `original_id`, `product_id`, `kind`, `quantity`, `movement_date`, `description`, `beneficiary_id`, `project_id`, `operator`, `version`, `destination`, `vehicle_id`, `source_vehicle_id`) VALUES ('2b7dbdab-278a-40e2-b28a-d6797ddeac75',11,12,0,7,'2026-09-24','Montaj la client - test',1,NULL,'administrator.demo',1,NULL,NULL,NULL),
('c9513c3b-98c4-4f3a-8bb1-4f613e511e28',15,1,1,1,'2026-12-31','Test d12312026ata viitoare',NULL,NULL,'administrator.demo',0,NULL,NULL,NULL),
('d55c2518-4b43-4971-bd6f-98cd22001f16',13,1,0,2,'2026-09-25','Montaj hala test',1,4,'administrator.demo',0,NULL,NULL,NULL),
('fa786b41-f72a-4525-a4aa-603a7d235158',17,1,1,1,'2026-09-25','Test sincronizare',NULL,NULL,'administrator.demo',0,NULL,NULL,NULL);
/*!40000 ALTER TABLE `archive_stock_movements` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_vehicles` WRITE;
/*!40000 ALTER TABLE `archive_vehicles` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_vehicles` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_web_users` WRITE;
/*!40000 ALTER TABLE `archive_web_users` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_web_users` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `archive_work_points` WRITE;
/*!40000 ALTER TABLE `archive_work_points` DISABLE KEYS */;
/*!40000 ALTER TABLE `archive_work_points` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `beneficiaries` WRITE;
/*!40000 ALTER TABLE `beneficiaries` DISABLE KEYS */;
REPLACE INTO `beneficiaries` (`id`, `name`, `normalized_name`, `cui`, `normalized_cui`, `kind`, `address`, `phone`, `registry_number`, `postal_code`, `caen_code`, `anaf_verified`, `version`) VALUES (1,'Construct Demo SRL','CONSTRUCT DEMO SRL','RO10000001','RO10000001','PJ','Strada Demo 1, Bucuresti','0721000001','','','',0,1),
(2,'Atelier Tehnic SRL 22','ATELIER TEHNIC SRL 22','RO10000002','RO10000002','PJ','','','','','',0,3),
(3,'Servicii Industriale SA','SERVICII INDUSTRIALE SA','10000003','10000003','PJ','','','','','',0,0),
(4,'ELECTRIC STANDARD PREST SRL','ELECTRIC STANDARD PREST SRL','RO9178894','RO9178894','PJ','JUD. HUNEDOARA, ORS. SIMERIA, STR. LIBERTATII, NR.39','0254261787','J1997000089209','335900','7112',1,0),
(5,'Ion Paun','ION PAUN','','PF:ION PAUN','PF','Strada Pacii 5, Deva','0744123456','','','',0,0),
(6,'TELESYSTEM SRL','TELESYSTEM SRL','RO22460883','RO22460883','PJ','JUD. BACAU, MUN. BACAU, CAL. MARASESTI, NR.110, SC.C, ET.1, AP.7','0334418118','J2007001650049','600073','8009',1,0),
(7,'Maria Popescu','MARIA POPESCU','','PF:MARIA POPESCU','PF','Strada Florilor 3, Deva','0733111222','','','',0,0);
/*!40000 ALTER TABLE `beneficiaries` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `beneficiary_work_points` WRITE;
/*!40000 ALTER TABLE `beneficiary_work_points` DISABLE KEYS */;
REPLACE INTO `beneficiary_work_points` (`id`, `beneficiary_id`, `name`, `address`, `normalized_address`, `phone`, `contact_person`, `version`, `description`, `is_primary`, `latitude`, `longitude`, `primary_beneficiary_id`) VALUES (1,1,'Punct de lucru principal','Strada Demo 1, Bucuresti','STRADA DEMO 1 BUCURESTI','0721000001','',0,'',1,NULL,NULL,1),
(2,2,'Punct de lucru principal','','','','',0,'',1,NULL,NULL,2),
(3,3,'Punct de lucru principal','','','','',0,'',1,NULL,NULL,3),
(4,4,'Punct de lucru principal','JUD. HUNEDOARA, ORS. SIMERIA, STR. LIBERTATII, NR.39','JUDETUL HUNEDOARA ORASUL SIMERIA STRADA LIBERTATII 39','0254261787','',0,'',1,NULL,NULL,4),
(5,5,'Punct de lucru principal','Strada Pacii 5, Deva','STRADA PACII 5 DEVA','0744123456','',0,'',1,NULL,NULL,5),
(6,6,'Punct de lucru principal','JUD. BACAU, MUN. BACAU, CAL. MARASESTI, NR.110, SC.C, ET.1, AP.7','JUDETUL BACAU MUNICIPIUL BACAU CAL MARASESTI 110 SCARA C ETAJ 1 APARTAMENT 7','0334418118','',0,'',1,NULL,NULL,6),
(7,7,'Punct de lucru principal','Strada Florilor 3, Deva','STRADA FLORILOR 3 DEVA','0733111222','',0,'',1,NULL,NULL,7);
/*!40000 ALTER TABLE `beneficiary_work_points` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `categories` WRITE;
/*!40000 ALTER TABLE `categories` DISABLE KEYS */;
REPLACE INTO `categories` (`id`, `name`, `normalized_name`) VALUES (1,'Scule electrice','SCULE ELECTRICE'),
(2,'Echipamente de protectie personala','ECHIPAMENTE DE PROTECTIE PERSONALA'),
(3,'Consumabile generice','CONSUMABILE GENERICE'),
(4,'Masurare','MASURARE'),
(5,'Scule de mana','SCULE DE MANA'),
(6,'TVCI','TVCI'),
(7,'Retelistica','RETELISTICA');
/*!40000 ALTER TABLE `categories` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `expiry_notifications` WRITE;
/*!40000 ALTER TABLE `expiry_notifications` DISABLE KEYS */;
REPLACE INTO `expiry_notifications` (`id`, `template_id`, `source_key`, `object_id`, `expiry_date`, `created_utc`, `acknowledged_by`, `acknowledged_utc`, `snooze_until`, `snooze_days`, `version`, `resolved_by`, `resolved_utc`, `resolved_reason`, `resolved_auto`, `object_label`, `snapshot_values`, `snapshot_subject`, `snapshot_body`, `snapshot_source`) VALUES (1,1,'vehicul.itp',1,'2026-10-15','2026-09-30T10:21:03.873Z','administrator.demo','2026-09-30T10:40:51.531Z',NULL,NULL,1,NULL,NULL,NULL,0,NULL,NULL,NULL,NULL,NULL);
/*!40000 ALTER TABLE `expiry_notifications` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `invoice_template_models` WRITE;
/*!40000 ALTER TABLE `invoice_template_models` DISABLE KEYS */;
/*!40000 ALTER TABLE `invoice_template_models` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `invoice_template_versions` WRITE;
/*!40000 ALTER TABLE `invoice_template_versions` DISABLE KEYS */;
/*!40000 ALTER TABLE `invoice_template_versions` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `invoice_templates` WRITE;
/*!40000 ALTER TABLE `invoice_templates` DISABLE KEYS */;
/*!40000 ALTER TABLE `invoice_templates` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `notification_settings` WRITE;
/*!40000 ALTER TABLE `notification_settings` DISABLE KEYS */;
/*!40000 ALTER TABLE `notification_settings` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `notification_templates` WRITE;
/*!40000 ALTER TABLE `notification_templates` DISABLE KEYS */;
REPLACE INTO `notification_templates` (`id`, `source_key`, `subject`, `body`, `threshold_days`, `is_active`, `version`, `active_source_key`) VALUES (1,'vehicul.itp','Expirare <eveniment> – <numar autovehicul> la data de <data expirare>','Atenție: <eveniment> pentru autovehiculul <numar autovehicul> (<descriere autovehicul>) expiră la data de <data expirare>, peste <zile ramase> zile. Vă rugăm să luați măsurile necesare înainte de această dată.',15,1,1,'vehicul.itp'),
(2,'vehicul.asigurare','Expirare <eveniment> – <numar autovehicul> la data <data expirare>','Atenție: <eveniment> pentru autovehiculul <numar autovehicul> (<descriere autovehicul>) expiră la data de <data expirare>, peste <zile ramase> zile. Vă rugăm să luați măsurile necesare înainte de această dată.',30,1,0,'vehicul.asigurare'),
(3,'vehicul.rovinieta','Expirare <eveniment> – <numar autovehicul> la data <data expirare>','Atenție: <eveniment> pentru autovehiculul <numar autovehicul> (<descriere autovehicul>) expiră la data de <data expirare>, peste <zile ramase> zile. Vă rugăm să luați măsurile necesare înainte de această dată.',30,1,0,'vehicul.rovinieta');
/*!40000 ALTER TABLE `notification_templates` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `product_images` WRITE;
/*!40000 ALTER TABLE `product_images` DISABLE KEYS */;
/*!40000 ALTER TABLE `product_images` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `product_locks` WRITE;
/*!40000 ALTER TABLE `product_locks` DISABLE KEYS */;
/*!40000 ALTER TABLE `product_locks` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `products` WRITE;
/*!40000 ALTER TABLE `products` DISABLE KEYS */;
REPLACE INTO `products` (`id`, `category_id`, `subcategory_id`, `name`, `normalized_name`, `description`, `quantity`, `version`) VALUES (1,1,1,'Masina de gaurit cu acumulator','MASINA DE GAURIT CU ACUMULATOR','18 V · mandrina de 13 mm · set cu doua acumulatoare',10,0),
(2,1,2,'Polizor unghiular','POLIZOR UNGHIULAR','Disc 125 mm · putere 900 W',9,0),
(3,2,3,'Casca de protectie alba XXL','CASCA DE PROTECTIE ALBA XXL','Reglaj cu rotita · utilizare pe santier',34,1),
(4,3,4,'Surub autoforant 4,8 × 25 test','SURUB AUTOFORANT 4,8 × 25 TEST','Cutie pentru montaj profile metalice',150,4),
(5,4,5,'Telemetru laser','TELEMETRU LASER','Domeniu 0,2–50 m · husa inclusa',0,0),
(6,5,6,'Set chei combinate','SET CHEI COMBINATE','12 piese · dimensiuni 8–19 mm',7,0),
(7,2,7,'Manusi de lucru','MANUSI DE LUCRU','Marimea 10 · acoperire nitril',52,0),
(8,3,2,'Disc diamantat 230 mm','DISC DIAMANTAT 230 MM','Pentru beton si zidarie',-2,0),
(9,4,8,'Nivela cu bula 60 cm','NIVELA CU BULA 60 CM','Corp aluminiu · trei fiole',5,0),
(10,1,1,'Ciocan rotopercutor SDS Plus','CIOCAN ROTOPERCUTOR SDS PLUS','Putere 800 W · energie de impact 2,7 J',2,0),
(12,5,6,'Diblu nylon 8 × 40 test 22','DIBLU NYLON 8 × 40 TEST 22','Pentru fixari in zidarie si polistiren si BCA',148,8),
(20,7,15,'DS-3E0109P-E-M','DS-3E0109P-E-M','Switch 8 porturi PoE, 1 port uplink- HIKVISION',5,1),
(21,6,13,'DS-2CD1343G2-LIU-2.8mm','DS-2CD1343G2-LIU-2.8MM','Camera IP, 4MP, lentila 2.8mm, IR 30m, WL 30m, Mic - HIKVISION',1,1),
(22,6,13,'DS-2CD1B43G2-LIU-2.8mm','DS-2CD1B43G2-LIU-2.8MM','Camera IP 4MP, lentila 2.8mm, IR 60m, WL 60m, Mic. - HIKVISION',7,1),
(23,7,15,'DS-3E1318P-EI-M','DS-3E1318P-EI-M','Switch 16 porturi PoE 100Mbps, 1 port Gigabit combo, 1 Gigabit RJ45, SMART Management',1,2),
(24,7,15,'DS-3E0105P-E-M','DS-3E0105P-E-M','DS-3E0105P-E-M Switch 4 porturi PoE, 1 port uplink- HIKVISION',1,1),
(25,6,12,'DS-7116HQHI-K1','DS-7116HQHI-K1','',1,1);
/*!40000 ALTER TABLE `products` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `project_observation_files` WRITE;
/*!40000 ALTER TABLE `project_observation_files` DISABLE KEYS */;
/*!40000 ALTER TABLE `project_observation_files` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `project_observations` WRITE;
/*!40000 ALTER TABLE `project_observations` DISABLE KEYS */;
REPLACE INTO `project_observations` (`id`, `project_id`, `name`, `content`, `author`, `version`, `created_utc`, `updated_utc`) VALUES (2,2,'Parole sistem TVCI','parole aici','administrator.demo',0,'2026-09-24T13:28:09.6540346Z','2026-09-24T13:28:09.6540346Z');
/*!40000 ALTER TABLE `project_observations` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `projects` WRITE;
/*!40000 ALTER TABLE `projects` DISABLE KEYS */;
REPLACE INTO `projects` (`id`, `beneficiary_id`, `name`, `normalized_name`, `observations`, `version`, `created_utc`, `updated_utc`) VALUES (2,2,'Instalare TVCI Barcea','INSTALARE TVCI BARCEA','',0,'2026-09-24T13:27:12.7227815Z','2026-09-24T13:27:12.7227815Z'),
(5,7,'Renovare casa Deva','RENOVARE CASA DEVA','',0,'2026-09-29T11:53:32.418Z','2026-09-29T11:53:32.418Z');
/*!40000 ALTER TABLE `projects` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `service_contract_points` WRITE;
/*!40000 ALTER TABLE `service_contract_points` DISABLE KEYS */;
/*!40000 ALTER TABLE `service_contract_points` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `service_contracts` WRITE;
/*!40000 ALTER TABLE `service_contracts` DISABLE KEYS */;
/*!40000 ALTER TABLE `service_contracts` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `service_interventions` WRITE;
/*!40000 ALTER TABLE `service_interventions` DISABLE KEYS */;
/*!40000 ALTER TABLE `service_interventions` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `service_photos` WRITE;
/*!40000 ALTER TABLE `service_photos` DISABLE KEYS */;
/*!40000 ALTER TABLE `service_photos` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `stock_movement_history` WRITE;
/*!40000 ALTER TABLE `stock_movement_history` DISABLE KEYS */;
REPLACE INTO `stock_movement_history` (`id`, `movement_id`, `actor`, `timestamp_utc`, `changes`, `stock_correction`, `reason`) VALUES (2,12,'administrator.demo','2026-09-24T13:38:00.4101820Z','Cantitate: 10 → 9; Corecție stoc: +1',1,'modificare cantitate'),
(3,12,'administrator.demo','2026-09-25T07:56:42.9258232Z','Cantitate: 9 → 7; Corecție stoc: +2',2,'corectare cantitate'),
(4,12,'utilizator.demo','2026-09-25T12:50:39.4463441Z','Cantitate: 7 → 5; Destinație: — → Beneficiar; Corecție stoc: +2',2,'test');
/*!40000 ALTER TABLE `stock_movement_history` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `stock_movements` WRITE;
/*!40000 ALTER TABLE `stock_movements` DISABLE KEYS */;
REPLACE INTO `stock_movements` (`id`, `product_id`, `beneficiary_id`, `quantity`, `created_utc`, `project_id`, `kind`, `movement_date`, `description`, `operator`, `version`, `updated_utc`, `destination`, `vehicle_id`, `source_vehicle_id`) VALUES (1,1,NULL,12,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(2,2,NULL,8,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(3,3,NULL,34,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(4,4,NULL,96,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(5,6,NULL,7,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(6,7,NULL,52,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(7,8,NULL,2,'2026-09-24T13:18:21.9716678Z',NULL,0,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(8,9,NULL,5,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(9,10,NULL,3,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(10,12,NULL,120,'2026-09-24T13:18:21.9716678Z',NULL,1,'2026-09-24','Stoc initial','sistem',0,'2026-09-24T13:18:21.9716678Z',NULL,NULL,NULL),
(12,12,2,5,'2026-09-24T13:27:36.6943105Z',2,0,'2026-09-24','Iesire 10','administrator.demo',3,'2026-09-25T12:50:39.4463441Z',1,NULL,NULL),
(14,12,NULL,12,'2026-09-25T06:15:25.6668561Z',NULL,1,'2026-09-25','pret 1','administrator.demo',0,'2026-09-25T06:15:25.6668561Z',NULL,NULL,NULL),
(16,12,NULL,1,'2026-09-25T06:20:14.4642658Z',NULL,1,'2026-09-29','sdas','administrator.demo',0,'2026-09-25T06:20:14.4642658Z',NULL,NULL,NULL),
(18,12,NULL,10,'2026-09-25T12:47:33.1665477Z',NULL,1,'2026-09-25','treea','utilizator.demo',0,'2026-09-25T12:47:33.1665477Z',NULL,NULL,NULL),
(19,12,NULL,20,'2026-09-25T12:48:37.1257764Z',NULL,0,'2026-09-25','Completare stoc masina HD-03-ESP 25.09.2026','utilizator.demo',0,'2026-09-25T12:48:37.1257764Z',2,1,NULL),
(20,12,2,15,'2026-09-25T12:50:10.3654829Z',NULL,0,'2026-09-25','proiect','utilizator.demo',0,'2026-09-25T12:50:10.3654829Z',1,NULL,1),
(21,12,NULL,3,'2026-09-25T12:55:37.7129591Z',NULL,0,'2026-09-25','Mutare din masina HD-03-ESP in masina HD-04-ESP 25.09.2026','utilizator.demo',0,'2026-09-25T12:55:37.7129591Z',2,2,1),
(22,12,NULL,2,'2026-09-28T06:44:50.3861828Z',NULL,0,'2026-09-28','Mutare din masina HD-03-ESP in masina HD-04-ESP 28.09.2026','administrator.demo',0,'2026-09-28T06:44:50.3861828Z',2,2,1),
(23,12,NULL,20,'2026-09-28T06:45:26.1189266Z',NULL,0,'2026-09-28','Completare stoc masina HD-03-ESP 28.09.2026','administrator.demo',0,'2026-09-28T06:45:26.1189266Z',2,1,NULL),
(24,12,NULL,25,'2026-09-28T09:08:02.4473066Z',NULL,1,'2026-09-28','Corectie stoc (inventar) 28.09.2026','administrator.demo',0,'2026-09-28T09:08:02.4473066Z',NULL,NULL,NULL),
(25,10,NULL,3,'2026-09-28T09:08:02.4584197Z',NULL,0,'2026-09-28','Corectie stoc (inventar) 28.09.2026','administrator.demo',0,'2026-09-28T09:08:02.4584197Z',4,NULL,NULL),
(26,4,NULL,46,'2026-09-30T05:43:07.306Z',NULL,0,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T05:43:07.306Z',4,NULL,NULL),
(27,4,NULL,20,'2026-09-30T05:46:08.120Z',NULL,0,'2026-09-30','vanzare test','administrator.demo',0,'2026-09-30T05:46:08.120Z',3,NULL,NULL),
(28,10,NULL,2,'2026-09-30T06:31:04.365Z',NULL,1,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T06:31:04.365Z',NULL,NULL,NULL),
(29,1,NULL,2,'2026-09-30T06:31:04.381Z',NULL,0,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T06:31:04.381Z',4,NULL,NULL),
(30,2,NULL,1,'2026-09-30T06:31:04.385Z',NULL,0,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T06:31:04.385Z',4,NULL,NULL),
(31,2,NULL,2,'2026-09-30T07:02:45.640Z',NULL,1,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T07:02:45.640Z',NULL,NULL,NULL),
(32,4,NULL,120,'2026-09-30T07:43:03.450Z',NULL,1,'2026-09-30','Corectie stoc (inventar) 30.09.2026','administrator.demo',0,'2026-09-30T07:43:03.450Z',NULL,NULL,NULL),
(33,12,NULL,10,'2026-09-30T07:56:37.156Z',NULL,0,'2026-09-30','Mutare din masina HD-03-ESP in masina HD-04-ESP 30.09.2026','administrator.demo',0,'2026-09-30T07:56:37.156Z',2,2,1),
(34,20,NULL,5,'2026-10-05T06:13:47.446Z',NULL,1,'2026-10-05','168.2 factura nr  111094321 din data 22.09.2026 SC TELESYSTEM SRL','administrator.demo',0,'2026-10-05T06:13:47.446Z',NULL,NULL,NULL),
(35,21,NULL,1,'2026-10-05T06:13:47.472Z',NULL,1,'2026-10-05','243.8 factura nr  111094321 din data 22.09.2026 SC TELESYSTEM SRL','administrator.demo',0,'2026-10-05T06:13:47.472Z',NULL,NULL,NULL),
(36,22,NULL,7,'2026-10-05T06:13:47.486Z',NULL,1,'2026-10-05','335.6 factura nr  111094321 din data 22.09.2026 SC TELESYSTEM SRL','administrator.demo',0,'2026-10-05T06:13:47.486Z',NULL,NULL,NULL),
(37,23,NULL,1,'2026-10-05T06:13:47.498Z',NULL,1,'2026-10-05','539.3 factura nr  111094321 din data 22.09.2026 SC TELESYSTEM SRL','administrator.demo',0,'2026-10-05T06:13:47.498Z',NULL,NULL,NULL),
(38,24,NULL,1,'2026-10-05T06:13:47.509Z',NULL,1,'2026-10-05','105.2 factura nr  111094321 din data 22.09.2026 SC TELESYSTEM SRL','administrator.demo',0,'2026-10-05T06:13:47.509Z',NULL,NULL,NULL),
(39,25,NULL,1,'2026-10-05T06:27:33.764Z',NULL,1,'2026-10-05','481.2700 lei factura 385502 din data 08.lun.2026 SC GENERAL SECURITY S.R.L','administrator.demo',0,'2026-10-05T06:27:33.764Z',NULL,NULL,NULL);
/*!40000 ALTER TABLE `stock_movements` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `subcategories` WRITE;
/*!40000 ALTER TABLE `subcategories` DISABLE KEYS */;
REPLACE INTO `subcategories` (`id`, `category_id`, `name`, `normalized_name`) VALUES (1,1,'Gaurire','GAURIRE'),
(2,1,'Taiere','TAIERE'),
(3,2,'Protectie cap','PROTECTIE CAP'),
(4,3,'Elemente fixare','ELEMENTE FIXARE'),
(5,4,'Distante','DISTANTE'),
(6,5,'Strangere','STRANGERE'),
(7,2,'Protectie maini','PROTECTIE MAINI'),
(8,4,'Nivelare','NIVELARE'),
(9,6,'Generic','GENERIC'),
(10,7,'Generice','GENERICE'),
(11,6,'NVR','NVR'),
(12,6,'DVR','DVR'),
(13,6,'Camere IP','CAMERE IP'),
(14,6,'Camere analogice','CAMERE ANALOGICE'),
(15,7,'Switch','SWITCH');
/*!40000 ALTER TABLE `subcategories` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `vehicles` WRITE;
/*!40000 ALTER TABLE `vehicles` DISABLE KEYS */;
REPLACE INTO `vehicles` (`id`, `plate_number`, `normalized_plate`, `description`, `version`, `itp_expiry`, `insurance_expiry`, `rovinieta_expiry`) VALUES (1,'HD-03-ESP','HD-03-ESP','Dacia docker alba',3,'2026-10-15','2027-06-30','2027-09-30'),
(2,'HD-04-ESP','HD-04-ESP','papuc',0,'2027-03-15','2027-06-30','2027-09-30');
/*!40000 ALTER TABLE `vehicles` ENABLE KEYS */;
UNLOCK TABLES;

LOCK TABLES `web_users` WRITE;
/*!40000 ALTER TABLE `web_users` DISABLE KEYS */;
REPLACE INTO `web_users` (`id`, `username`, `normalized_username`, `display_name`, `password_hash`, `role`, `is_active`, `version`) VALUES (1,'administrator.demo','ADMINISTRATOR.DEMO','Administrator demonstratie','AQAAAAIAAYagAAAAEIeR3dzbxxPEXnPWJ2h6E1QnJg7VWSxKvvZFsk1/hylP26VxVBIILKj/Fr3b6PgsNA==','Administrator',1,0),
(2,'utilizator.demo','UTILIZATOR.DEMO','Utilizator demonstratie','AQAAAAIAAYagAAAAEG6GfKQW+dSJCuHG4T16Zl9QvtaQHTwtCCh2KIHJ9kP9C37eKheuYMQLSONzkTzCpg==','Utilizator',1,0);
/*!40000 ALTER TABLE `web_users` ENABLE KEYS */;
UNLOCK TABLES;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;

