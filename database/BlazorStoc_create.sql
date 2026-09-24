-- BlazorStoc - structura goala bazata pe backupul furnizat.
-- Compatibilitate tinta: MariaDB 5.5.68.
-- Pastreaza tipurile, indicii, relatiile, motoarele si latin1 din backup.
-- Fara date, parole, DROP sau modificari ale bazei stocesp.
-- IF NOT EXISTS nu actualizeaza structura unor tabele deja existente.
-- Importati fara optiunea de ignorare/continuare la erori.

CREATE DATABASE IF NOT EXISTS `BlazorStoc`
  DEFAULT CHARACTER SET latin1 COLLATE latin1_swedish_ci;
USE `BlazorStoc`;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`user` (
  `id_user` int(11) NOT NULL AUTO_INCREMENT,
  `username` char(50) DEFAULT NULL,
  `parola` text,
  PRIMARY KEY (`id_user`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`beneficiar` (
  `id_beneficiar` int(11) NOT NULL AUTO_INCREMENT,
  `id_user` int(11) DEFAULT NULL,
  `beneficiar_denumire` text,
  PRIMARY KEY (`id_beneficiar`) USING BTREE,
  KEY `FK_lucrare_user` (`id_user`) USING BTREE,
  CONSTRAINT `FK_lucrare_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`categorie` (
  `id_categorie` int(11) NOT NULL AUTO_INCREMENT,
  `id_user` int(11) DEFAULT NULL,
  `categorie_nume` varchar(100) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id_categorie`),
  KEY `FK_categorie_user` (`id_user`) USING BTREE,
  CONSTRAINT `FK_categorie_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`subcategorie` (
  `id_subcategorie` int(11) NOT NULL AUTO_INCREMENT,
  `id_categorie` int(11) NOT NULL DEFAULT '0',
  `id_user` int(11) DEFAULT NULL,
  `subcategorie_nume` varchar(100) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id_subcategorie`),
  KEY `FK__categorie` (`id_categorie`) USING BTREE,
  KEY `FK_subcategorie_user` (`id_user`) USING BTREE,
  CONSTRAINT `FK_subcategorie_categorie` FOREIGN KEY (`id_categorie`) REFERENCES `BlazorStoc`.`categorie` (`id_categorie`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_subcategorie_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`produs` (
  `id_produs` int(11) NOT NULL AUTO_INCREMENT,
  `id_categorie` int(11) NOT NULL DEFAULT '0',
  `id_subcategorie` int(11) NOT NULL DEFAULT '0',
  `id_user` int(11) NOT NULL DEFAULT '0',
  `produs_denumire` varchar(100) NOT NULL DEFAULT '0',
  `produs_descriere` varchar(1000) NOT NULL DEFAULT '<descriere lipsa>',
  `produs_cantitate` int(11) DEFAULT '0',
  PRIMARY KEY (`id_produs`) USING BTREE,
  KEY `FK_reper_categorie` (`id_categorie`) USING BTREE,
  KEY `FK_reper_subcategorie` (`id_subcategorie`) USING BTREE,
  KEY `FK_reper_user` (`id_user`) USING BTREE,
  CONSTRAINT `FK_reper_categorie` FOREIGN KEY (`id_categorie`) REFERENCES `BlazorStoc`.`categorie` (`id_categorie`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_reper_subcategorie` FOREIGN KEY (`id_subcategorie`) REFERENCES `BlazorStoc`.`subcategorie` (`id_subcategorie`) ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_reper_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`imagine` (
  `id_imagine` int(11) NOT NULL AUTO_INCREMENT,
  `id_user` int(11) DEFAULT NULL,
  `id_produs` int(11) DEFAULT NULL,
  `imagine_nume_fisier` text NOT NULL,
  `imagine_data` mediumblob NOT NULL,
  PRIMARY KEY (`id_imagine`),
  KEY `FK_imagine_user` (`id_user`) USING BTREE,
  KEY `FK_imagine_produs` (`id_produs`),
  CONSTRAINT `FK_imagine_produs` FOREIGN KEY (`id_produs`) REFERENCES `BlazorStoc`.`produs` (`id_produs`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_imagine_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`io` (
  `id_io` int(11) NOT NULL AUTO_INCREMENT,
  `id_user` int(11) NOT NULL DEFAULT '0',
  `id_produs` int(11) NOT NULL DEFAULT '0',
  `id_beneficiar` int(11) NOT NULL DEFAULT '0',
  `io_tip_actiune` tinyint(1) NOT NULL DEFAULT '0',
  `io_numar_bucati` tinyint(4) NOT NULL DEFAULT '0',
  `io_descriere` text NOT NULL,
  `io_data` varchar(50) NOT NULL DEFAULT '',
  PRIMARY KEY (`id_io`),
  KEY `FK_io_user` (`id_user`),
  KEY `FK_io_beneficiar` (`id_beneficiar`),
  KEY `FK_io_produs` (`id_produs`),
  CONSTRAINT `FK_io_produs` FOREIGN KEY (`id_produs`) REFERENCES `BlazorStoc`.`produs` (`id_produs`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_io_user` FOREIGN KEY (`id_user`) REFERENCES `BlazorStoc`.`user` (`id_user`) ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`io_history` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `id_user` int(11) DEFAULT NULL,
  `id_io` int(11) DEFAULT NULL,
  `denumire_produs` varchar(200) DEFAULT NULL,
  `modificare` text,
  `data` varchar(50) DEFAULT NULL,
  `motiv` text,
  `timestamp` tinytext,
  PRIMARY KEY (`id`),
  KEY `FK__user` (`id_user`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`log` (
  `log_timestamp` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `log_command` text,
  `id_user` int(11) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`produs_bak` (
  `id_produs` int(11) NOT NULL AUTO_INCREMENT,
  `id_categorie` int(11) NOT NULL DEFAULT '0',
  `id_subcategorie` int(11) NOT NULL DEFAULT '0',
  `id_user` int(11) NOT NULL DEFAULT '0',
  `produs_denumire` varchar(100) NOT NULL DEFAULT '0',
  `produs_descriere` varchar(1000) NOT NULL DEFAULT '<descriere lipsa>',
  `produs_cantitate` int(11) DEFAULT '0',
  PRIMARY KEY (`id_produs`) USING BTREE,
  KEY `FK_reper_categorie` (`id_categorie`) USING BTREE,
  KEY `FK_reper_subcategorie` (`id_subcategorie`) USING BTREE,
  KEY `FK_reper_user` (`id_user`) USING BTREE
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE IF NOT EXISTS `BlazorStoc`.`updater` (
  `id` tinyint(1) unsigned zerofill NOT NULL DEFAULT '1',
  `latest_version` tinytext,
  `latest_kit_name` text,
  `latest_kit_data` longblob
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

SHOW TABLES FROM `BlazorStoc`;
