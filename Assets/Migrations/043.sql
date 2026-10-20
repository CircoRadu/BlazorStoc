-- version: 43
-- description: Registrul migrarilor aplicate (versiune, checksum, origine): baza actualizarii aplicatiei
-- note: One row per migration of Assets/Migrations. checksum is the SHA-256 of the migration file (line ends normalised); a row whose checksum differs from the file
-- note: of the running application means an applied migration was edited afterwards (reported as drift). origin: "baseline" = the migration was already part of the
-- note: database when the ledger was started (or came with the first schema), "applied" = applied by the migrator after that. The rows are written with the
-- note: application account (the migrator account has no data rights), see MariaSchemaLedger.
-- column: schema_migrations.version
-- column: schema_migrations.description
-- column: schema_migrations.checksum
-- column: schema_migrations.origin
-- column: schema_migrations.applied_utc
-- column: schema_migrations.applied_by_version
-- statement
CREATE TABLE IF NOT EXISTS `schema_migrations` (
  `version` INT NOT NULL,
  `description` VARCHAR(300) NOT NULL,
  `checksum` CHAR(64) NOT NULL,
  `origin` VARCHAR(20) NOT NULL,
  `applied_utc` DATETIME NOT NULL,
  `applied_by_version` VARCHAR(40) NOT NULL,
  PRIMARY KEY (`version`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_nopad_bin
