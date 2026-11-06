-- version: 70
-- description: Autentificatorul asociat de administrator: stare suspendata (cont inactiv), ultima folosire (data si IP), cine l-a asociat si cine l-a confirmat: web_user_totp.suspended_utc, last_used_utc, last_used_ip, enrolled_by, confirmed_by
-- note: A suspended authenticator (suspended_utc set when the account is made inactive) cannot be used, not even with a recovery code; the administrator resumes or revokes it after the account is active again.
-- column: web_user_totp.suspended_utc
-- column: web_user_totp.last_used_utc
-- column: web_user_totp.last_used_ip
-- column: web_user_totp.enrolled_by
-- column: web_user_totp.confirmed_by
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `suspended_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `last_used_utc` VARCHAR(40) NULL
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `last_used_ip` VARCHAR(64) NULL
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `enrolled_by` VARCHAR(100) NULL
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `confirmed_by` VARCHAR(100) NULL
