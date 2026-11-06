-- version: 69
-- description: Autentificare in doi pasi (TOTP) pentru accesul din afara retelei interne: secretul fiecarui utilizator (web_user_totp) si codurile de recuperare (web_user_recovery_codes)
-- note: web_user_totp has one row per account; user_id 0 is the administrator configured in the settings file (no row in web_users). The secret is kept encrypted (Data Protection) and is never shown again, logged or audited.
-- note: confirmed_utc stays NULL until the user proves the authenticator with a first valid code; an unconfirmed secret does not open external access. last_step is the last accepted 30-second step, so a code cannot be used twice.
-- note: web_user_recovery_codes keeps only the hash of every recovery code; used_utc is set when the code is spent.
-- column: web_user_totp.user_id
-- column: web_user_totp.secret_protected
-- column: web_user_totp.created_utc
-- column: web_user_totp.confirmed_utc
-- column: web_user_totp.last_step
-- column: web_user_recovery_codes.id
-- column: web_user_recovery_codes.user_id
-- column: web_user_recovery_codes.code_hash
-- column: web_user_recovery_codes.used_utc
-- statement
CREATE TABLE IF NOT EXISTS `web_user_totp` (
  `user_id` INT NOT NULL,
  `secret_protected` TEXT NOT NULL,
  `created_utc` VARCHAR(40) NOT NULL,
  `confirmed_utc` VARCHAR(40) NULL,
  `last_step` BIGINT NOT NULL DEFAULT 0,
  PRIMARY KEY (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
-- statement
CREATE TABLE IF NOT EXISTS `web_user_recovery_codes` (
  `id` INT NOT NULL AUTO_INCREMENT,
  `user_id` INT NOT NULL,
  `code_hash` VARCHAR(100) NOT NULL,
  `used_utc` VARCHAR(40) NULL,
  PRIMARY KEY (`id`),
  KEY `ix_web_user_recovery_codes_user` (`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
