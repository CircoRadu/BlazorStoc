-- version: 86
-- description: Dezvoltator: indicator pe conturile de utilizator (Propuneri si Erori vizibile doar dezvoltatorilor)
-- note: nobody is a developer after this migration; an administrator marks the accounts in Utilizatori (Setari -> Utilizatori), the flag decides who sees the tile Dezvoltare of Setari and the tabs Bugs and Propuneri of Notificari
-- column: web_users.is_developer
-- statement
ALTER TABLE `web_users` ADD COLUMN IF NOT EXISTS `is_developer` TINYINT(1) NOT NULL DEFAULT 0
