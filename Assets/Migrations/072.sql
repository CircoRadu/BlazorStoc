-- version: 72
-- description: Schimbarea parolei la prima autentificare: web_users.must_change_password
-- note: The column is added with the default 1, so EVERY account that exists when the migration runs has to change its password at its next sign-in; the default is then set to 0 (the repository sets the flag itself for a new account and when an administrator sets the password of another account). The flag is cleared when the user chooses a new password.
-- column: web_users.must_change_password
-- statement
ALTER TABLE `web_users` ADD COLUMN IF NOT EXISTS `must_change_password` TINYINT(1) NOT NULL DEFAULT 1
-- statement
ALTER TABLE `web_users` ALTER COLUMN `must_change_password` SET DEFAULT 0
