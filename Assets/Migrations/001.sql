-- version: 1
-- description: Beneficiari: persoana fizica / juridica, adresa, telefon, date ANAF
-- column: beneficiaries.kind
-- column: beneficiaries.address
-- column: beneficiaries.phone
-- column: beneficiaries.registry_number
-- column: beneficiaries.postal_code
-- column: beneficiaries.caen_code
-- column: beneficiaries.anaf_verified
-- statement
ALTER TABLE `beneficiaries`
    ADD COLUMN IF NOT EXISTS `kind` VARCHAR(2) NOT NULL DEFAULT 'PJ' AFTER `normalized_cui`,
    ADD COLUMN IF NOT EXISTS `address` VARCHAR(300) NOT NULL DEFAULT '' AFTER `kind`,
    ADD COLUMN IF NOT EXISTS `phone` VARCHAR(20) NOT NULL DEFAULT '' AFTER `address`,
    ADD COLUMN IF NOT EXISTS `registry_number` VARCHAR(40) NOT NULL DEFAULT '' AFTER `phone`,
    ADD COLUMN IF NOT EXISTS `postal_code` VARCHAR(10) NOT NULL DEFAULT '' AFTER `registry_number`,
    ADD COLUMN IF NOT EXISTS `caen_code` VARCHAR(4) NOT NULL DEFAULT '' AFTER `postal_code`,
    ADD COLUMN IF NOT EXISTS `anaf_verified` TINYINT NOT NULL DEFAULT 0 AFTER `caen_code`
