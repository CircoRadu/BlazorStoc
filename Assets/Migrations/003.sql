-- version: 3
-- description: Vehicule: date de expirare ITP, asigurare si rovinieta (vehiculele existente primesc date din anul urmator)
-- column: vehicles.itp_expiry
-- column: vehicles.insurance_expiry
-- column: vehicles.rovinieta_expiry
-- statement
ALTER TABLE `vehicles`
    ADD COLUMN IF NOT EXISTS `itp_expiry` DATE NOT NULL DEFAULT '2027-03-15' AFTER `version`,
    ADD COLUMN IF NOT EXISTS `insurance_expiry` DATE NOT NULL DEFAULT '2027-06-30' AFTER `itp_expiry`,
    ADD COLUMN IF NOT EXISTS `rovinieta_expiry` DATE NOT NULL DEFAULT '2027-09-30' AFTER `insurance_expiry`
