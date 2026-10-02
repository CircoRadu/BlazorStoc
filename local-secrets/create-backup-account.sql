-- Ruleaza cu contul root al instantei MariaDB locale (C:\Dev\BlazorStoc-MariaDB, port 3307).
-- 1) Alege o parola generata si inlocuieste <PAROLA> (nu o trimite in chat).
-- 2) Hostul trebuie sa fie acelasi cu al contului blazorstoc_dev: verifica cu
--    SELECT user, host FROM mysql.user WHERE user LIKE 'blazorstoc%';
CREATE USER IF NOT EXISTS 'blazorstoc_backup'@'127.0.0.1' IDENTIFIED BY '<PAROLA>' REQUIRE SSL;
GRANT SELECT, SHOW VIEW, TRIGGER, LOCK TABLES ON `BlazorStoc`.* TO 'blazorstoc_backup'@'127.0.0.1';
FLUSH PRIVILEGES;
-- Apoi creeaza fisierul  BlazorStoc\local-secrets\backup-account.private.json :
-- { "Database": { "User": "blazorstoc_backup", "Password": "<PAROLA>" } }
