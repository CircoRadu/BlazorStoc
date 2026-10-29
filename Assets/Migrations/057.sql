-- version: 57
-- description: Preluare ActivityApp: preluarea beneficiarilor (si a celor fara tichete) si importul automat sau punerea in coada pentru acceptare manuala
-- note: take_partners = the synchronization also takes the partners that have no ticket (persons and companies); off = only the clients of the tickets read are taken.
-- note: auto_accept_partners = a clean partner is made a beneficiary at once; off = every new partner waits in the queue of the beneficiaries to verify (reason awaiting_acceptance) until it is accepted by hand, and its tickets wait with it.
-- column: activity_import_settings.take_partners
-- column: activity_import_settings.auto_accept_partners
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `take_partners` TINYINT(1) NOT NULL DEFAULT 1, ADD COLUMN IF NOT EXISTS `auto_accept_partners` TINYINT(1) NOT NULL DEFAULT 1
