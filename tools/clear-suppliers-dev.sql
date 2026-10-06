-- Sterge din baza locala de dezvoltare toti furnizorii, facturile lor si miscarile de stoc legate de facturi.
-- Se ruleaza de utilizator, cu contul admin, DUPA o copie a bazei (tools/export-dev-data.ps1). Nu se poate anula.
-- Stocul produselor nu este recalculat de acest script: daca miscarile sterse erau intrari, cantitatile se corecteaza separat.
USE `BlazorStoc`;

SELECT (SELECT COUNT(*) FROM suppliers) AS furnizori, (SELECT COUNT(*) FROM supplier_invoices) AS facturi,
       (SELECT COUNT(*) FROM stock_movements WHERE invoice_id IS NOT NULL) AS miscari_legate,
       (SELECT COUNT(*) FROM invoice_templates WHERE supplier_id IS NOT NULL) AS sabloane_legate;

START TRANSACTION;
DELETE FROM stock_movements WHERE invoice_id IS NOT NULL;          -- miscarile legate de facturi
UPDATE invoice_templates SET supplier_id = NULL;                   -- sabloanele raman, dar fara furnizor
DELETE FROM supplier_invoices;                                     -- sterge si supplier_recognitions (CASCADE)
DELETE FROM suppliers;                                             -- sterge si supplier_aliases (CASCADE)
-- Verifica numerele de mai sus, apoi COMMIT; altfel ROLLBACK;
COMMIT;
