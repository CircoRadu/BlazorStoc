# Starea curenta a proiectului

Actualizat: 07.10.2026, Europe/Bucharest (Claude, agent unic, mod `claude_only`). Istoricul vechi: `docs/arhiva/` (vezi `docs/arhiva/INDEX.md`). Cand ceva de aici se invecheste, se inlocuieste, nu se aduna.

## Aplicatia

- Blazor Interactive Server, .NET 9, MariaDB, doar Release. Fara SQLite sau mod demo in aplicatie; fara Docker/NAS in aplicatia rulata (Docker permis pentru dezvoltare/testare).
- Module: produse/stoc, categorii, beneficiari/proiecte, vehicule, mentenanta (contracte, interventii, harta), notificari, jurnal, furnizori si facturi, preluare factura PDF/OCR cu sabloane, preluare inventar.
- Motorul de facturi este geometric (`Services/Invoices`); utilizatorii obisnuiti creeaza/modifica sabloane, stergerea ramane administratorului.
- Harta codului: `docs/HARTA_COD.md`. Reguli permanente: `CLAUDE.md`.

## Ultimul lucru facut (07.10.2026)

- Pagina Facturi `/facturi` (Administrare + pagina principala, vizibila tuturor): lista, filtre, numarul facturii deschide o fereastra cu produsele preluate (link la intrarile produsului); administratorul corecteaza numar/data/furnizor si sterge facturile fara intrari (jurnal cu actiuni exacte).
- Protocolul de lucru cu context redus: `PROTOCOL_CLAUDE_OPTIMIZAT.md` (in directorul parinte), cu `tools/run-checks.ps1` si `tools/log-task.ps1`.

## Validari

- Suita in memorie: 913 verificari trecute, 0 esecuri (07.10.2026). MariaDB `blazorstoc_test`: sectiunea „Suppliers and invoices” trece integral (rulata cu `MARIA_ONLY`); suita MariaDB completa nu a fost rerulata la aceasta data.
- Neverificat: pagina Facturi in browser (aspect, fereastra produselor), rol „Utilizator” in browser.

## Preview

- `tools\start-preview.ps1` publica si porneste pe `http://127.0.0.1:5087/`, pe MariaDB locala (port 3307, `local-secrets/application-connection.private.json`). Se republica doar la „review”/„preview”.

## Probleme deschise

- Verificari ramase: `docs/TESTE_RAMASE.md` (cazurile N45, N47 si cele de pe pagina Facturi); drepturile contului migrator pe baza de teste.
- Taskuri active: `TODO.md` (primul: fisierul modelului de sablon, partial implementat altfel - de reevaluat).

## Urmatorul pas

Il stabileste utilizatorul.
