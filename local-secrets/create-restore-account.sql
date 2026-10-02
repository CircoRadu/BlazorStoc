-- Cont dedicat pentru restaurarea bazei de date (Pasul 4: schema temporara _bak, schema de siguranta _old,
-- RENAME TABLE peste schema vie). Ruleaza cu contul root al instantei MariaDB locale (port 3307).
-- 1) Inlocuieste <PAROLA> cu o parola generata (nu o trimite in chat).
-- 2) Hostul trebuie sa fie acelasi cu al celorlalte conturi blazorstoc_* (verifica:
--    SELECT user, host FROM mysql.user WHERE user LIKE 'blazorstoc%';).
-- Nu are drepturi globale, nu are SUPER / SET USER / GRANT OPTION si nu poate citi datele altor scheme.

CREATE USER IF NOT EXISTS 'blazorstoc_restore'@'127.0.0.1' IDENTIFIED BY 'test' REQUIRE SSL;

-- Schema temporara si schema de siguranta: control complet (create/drop schema, import, verificare).
-- Underscore-ul din nume este escapat (\_), altfel ar fi caracter joker in GRANT.
GRANT ALL PRIVILEGES ON `BlazorStoc\_bak`.* TO 'blazorstoc_restore'@'127.0.0.1';
GRANT ALL PRIVILEGES ON `BlazorStoc\_old`.* TO 'blazorstoc_restore'@'127.0.0.1';

-- Schema vie: strict cat cere RENAME TABLE (ALTER + DROP pe tabelele mutate, CREATE + INSERT pe cele aduse)
-- si gestionarea triggerelor (TRIGGER). Fara alte drepturi.
-- SELECT este necesar pentru ca triggerele recreate la restaurare apartin acestui cont (definer) si isi citesc
-- randul (NEW.id / OLD.id) la fiecare scriere a aplicatiei: fara SELECT, scrierile aplicatiei esueaza dupa restaurare.
GRANT SELECT, ALTER, DROP, CREATE, INSERT, TRIGGER ON `BlazorStoc`.* TO 'blazorstoc_restore'@'127.0.0.1';

FLUSH PRIVILEGES;
SHOW GRANTS FOR 'blazorstoc_restore'@'127.0.0.1';

-- Apoi creeaza fisierul  BlazorStoc\local-secrets\restore-account.private.json :
-- { "Database": { "User": "blazorstoc_restore", "Password": "<PAROLA>" } }
