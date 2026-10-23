-- version: 48
-- description: Preluare ActivityApp, corespondenta campurilor: coloana din ActivityApp din care se citeste fiecare text al tichetului
-- note: field_map is a JSON object {"Tabel.Coloana": "ColoanaAleasa"}; a field that is not in it uses the column of the contract (the default). Only text columns of the same table
-- note: can be chosen; a chosen column that no longer exists in ActivityApp is reported in the structure report and the default is used.
-- column: activity_import_settings.field_map
-- statement
ALTER TABLE `activity_import_settings` ADD COLUMN IF NOT EXISTS `field_map` TEXT NULL
