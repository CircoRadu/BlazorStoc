-- Executati separat, numai pe BlazorStoc, inainte de pornirea versiunii 0.2.
-- Compatibil MariaDB 5.5.68. Nu importa date si nu modifica stocesp.
-- Opriti aplicatia pe durata migrarii; ALTER TABLE nu este tranzactional.
-- Se poate relua: coloana de versiune este adaugata numai daca lipseste.
USE `BlazorStoc`;
SET @ddl = IF(EXISTS(SELECT 1 FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA='BlazorStoc' AND TABLE_NAME='produs' AND COLUMN_NAME='produs_versiune'),
  'SELECT 1', 'ALTER TABLE `BlazorStoc`.`produs` ADD COLUMN `produs_versiune` bigint NOT NULL DEFAULT 0');
PREPARE migration FROM @ddl;
EXECUTE migration;
DEALLOCATE PREPARE migration;

-- Doar campurile editate de catalog sunt convertite pentru diacritice.
ALTER TABLE `BlazorStoc`.`categorie`
  MODIFY `categorie_nume` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0';
ALTER TABLE `BlazorStoc`.`subcategorie`
  MODIFY `subcategorie_nume` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0';
ALTER TABLE `BlazorStoc`.`produs`
  MODIFY `produs_denumire` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0',
  MODIFY `produs_descriere` varchar(1000) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '<descriere lipsa>';

-- Identitate tehnica pentru jurnalul contului web unic; fara parola de autentificare legacy.
INSERT INTO `BlazorStoc`.`user` (`username`, `parola`)
SELECT 'blazorstoc-web', NULL FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `BlazorStoc`.`user` WHERE username='blazorstoc-web');
-- Copiati ID-ul rezultat in DB_APP_USER_ID; nu este ID-ul contului SQL.
SELECT id_user AS DB_APP_USER_ID FROM `BlazorStoc`.`user` WHERE username='blazorstoc-web' ORDER BY id_user;
