# Starea curenta a proiectului

Actualizat: 07.10.2026, Europe/Bucharest (Claude, agent unic, mod `claude_only`). Istoricul vechi: `docs/arhiva/` (vezi `docs/arhiva/INDEX.md`). Cand ceva de aici se invecheste, se inlocuieste, nu se aduna.

## Aplicatia

- Blazor Interactive Server, .NET 9, MariaDB, doar Release. Fara SQLite sau mod demo in aplicatie; fara Docker/NAS in aplicatia rulata (Docker permis pentru dezvoltare/testare).
- Module: produse/stoc, categorii, beneficiari/proiecte, vehicule, mentenanta (contracte, interventii, harta), notificari, jurnal, furnizori si facturi, preluare factura PDF/OCR cu sabloane, preluare inventar.
- Motorul de facturi este geometric (`Services/Invoices`); utilizatorii obisnuiti creeaza/modifica sabloane, stergerea ramane administratorului.
- Harta codului: `docs/HARTA_COD.md`. Reguli permanente: `CLAUDE.md`.

## Ultimul lucru facut (07.10.2026)

- Stoc doar pe bucati; operatia de iesire / iesirea multipla, bon PDF, storno, retur, consum net; lista „De regularizat" (cauza iesirii peste stoc).
- Nomenclator „Tipuri de sisteme" (`/nomenclator`), componente pe proiect, sabloane de oferte xlsx (`/oferte/sabloane`), preluarea ofertelor (`/oferte/preluare`, `/oferte`, revizii cu diferente).
- Situatia proiectului pe componente (`/proiecte/{id}/situatie`, export PDF/CSV, lista de achizitie), iesiri legate de componente (migrarea 26) cu lamurire la scoaterea componentei.
- Rezervari pe proiect (migrarea 27): stoc liber = stoc - rezervari, consum la iesire, avertisment fara blocare, propuneri dupa intrare, intrari legate de oferta (si la preluarea facturii).
- Backup/restore: tabelele migrarilor 22-27 au fost adaugate in lista tabelelor (`MariaArchiveSchema.MigratedTables`).
- Pagina Facturi `/facturi`; protocol cu context redus (`PROTOCOL_CLAUDE_OPTIMIZAT.md`, `tools/run-checks.ps1`, `tools/log-task.ps1`).

## Validari

- Suita in memorie: 935 verificari trecute; suita MariaDB `blazorstoc_test`: 1505 trecute, 0 esecuri (07.10.2026, inainte de commit). Migrarile 1-27 aplicate pe `blazorstoc_test` si pe `BlazorStoc`.
- Neverificat in browser: tot ce e in `Teste utilizator/` marcat 07.10.2026 (utilizatorul nu a testat inca nimic).

## Preview

- `tools\start-preview.ps1` publica si porneste pe `http://127.0.0.1:5087/`, pe MariaDB locala (port 3307, `local-secrets/application-connection.private.json`). Se republica doar la „review”/„preview”.

## Probleme deschise

- Verificari ramase: `docs/TESTE_RAMASE.md` (cazurile N45, N47 si cele de pe pagina Facturi); drepturile contului migrator pe baza de teste.
- Taskuri active: `TODO.md` (primul: completari - iesire rapida, „Unde sunt bucatile", stoc minim, export consum; ultimul: sablon de import factura XML).

## Urmatorul pas

Il stabileste utilizatorul.
