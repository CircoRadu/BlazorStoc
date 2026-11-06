-- version: 71
-- description: Inlocuirea atomica a secretului autentificatorului (schimbarea telefonului): web_user_totp.pending_secret_protected
-- note: The new secret waits in pending_secret_protected while the old one keeps working; it replaces the old one only when the administrator confirms it with a valid code read on the new phone (same dialog). Closing the dialog drops it and nothing changes.
-- column: web_user_totp.pending_secret_protected
-- statement
ALTER TABLE `web_user_totp` ADD COLUMN IF NOT EXISTS `pending_secret_protected` TEXT NULL
