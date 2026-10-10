# Starea curenta a proiectului

Actualizat: 10.10.2026, Europe/Bucharest (Claude, agent unic, mod `claude_only`). Istoricul vechi: `docs/arhiva/` (vezi `docs/arhiva/INDEX.md`). Cand ceva de aici se invecheste, se inlocuieste, nu se aduna.

## Aplicatia

- Blazor Interactive Server, .NET 9, MariaDB, doar Release. Fara SQLite sau mod demo in aplicatie; fara Docker in aplicatia rulata (permis pentru dezvoltare/testare); copierea backup-urilor pe NAS este optionala, din Setari -> Backup -> Backup NAS.
- Module: produse/stoc, categorii, beneficiari/proiecte, vehicule, mentenanta (contracte, interventii, harta), notificari, jurnal, furnizori si facturi, preluare factura PDF/OCR cu sabloane, preluare inventar.
- Motorul de facturi este geometric (`Services/Invoices`); utilizatorii obisnuiti creeaza/modifica sabloane, stergerea ramane administratorului.
- Harta codului: `docs/HARTA_COD.md`. Reguli permanente: `CLAUDE.md`.

## Ultimul lucru facut (10.10.2026, catalog si preluare factura)

- Ordinea categoriilor prin drag and drop (administrator; migrarea 38 `categories.sort_order`, `ReorderCategoriesAsync`, `wwwroot/category-order.js`), aceeasi ordine in meniul lateral; jurnal "Reordonare categorii".
- Preluare factura: avertizarea "deja preluat" si deselectarea implicita a randurilor deja preluate la pasul 1 (`RefreshTakenPreviewAsync`), selector de randuri si la pasul 2 (`excluded` comun, `PickRow.RowIndex`).
- Popup pentru detaliile punctului de pe harta; tipul parametrului cu comutatoare; padding minim 6/12 px la insigne; baza locala curatata de categoriile in afara de TVCI si Control Acces.
- Suita in memorie 1065 trecute; suita MariaDB nerulata, migrarea 38 aplicata doar pe `BlazorStoc` (nu pe `blazorstoc_test`). De verificat in browser: `Teste utilizator/` 01, 04, 08.

## Ultimul lucru facut (09.10.2026, parametri obligatori)

- Parametri obligatori pe subcategorie (migrarea 37, `Services/ProductParameters.cs`, `MariaProductParameterRepository`, panoul `SubcategoryParametersPanel` pe /categorii): produsul se salveaza "<model> - <valoare> - ...", produsele vechi ale subcategoriei sunt blocate la miscari pana li se aleg valorile; valorile noi le adauga orice utilizator, editarea unei valori folosite doar administratorul (previzualizare, redenumire atomica, refuz la conflict).
- Preluare factura: variantele unui model se aleg in pasul 2 (`InvoiceVariants`, `PickupProductCell`); caractere nesigure din OCR marcate (`InvoiceAmbiguity`) in tabel si in formularul de produs; corectat crash-ul la numere uriase din scan (`InvoiceTableReader.AddsUp`). Camp "Scrisa de mine" ascuns pana la selectare.
- Suita MariaDB 1703 trecute, grupuri componente/pickup/xml/pdf trecute. De verificat in browser: `Teste utilizator/` 01 si 04.

## Ultimul lucru facut (09.10.2026)

- Facturi XML (UBL / e-Factura): `Services/Invoices/InvoiceXml.cs` (mapare de cai, cititor, reguli), sablon salvat in `invoice_templates` cu `source_kind` xml (campul `Xml` din definitie, fara migrare), fereastra `InvoiceXmlTemplateEditor` in Setari -> Facturi -> Sabloane salvate, ramura XML in `InvoicePickup` (`OpenXmlAsync`, sablon dupa CUI, altfel UBL standard). Teste: `InvoiceXmlChecks` (19). Neverificat in browser (`Teste utilizator/04`).

- Sabloane XML (editor pe sectiuni, legare vizuala `InvoiceXmlLinker`/`InvoiceXmlTree`, descriere intrare `InvoiceXmlDescription`, unitati `InvoiceUnitCodes`, ZIP e-Factura), perechi cod furnizor - produs (`SupplierProductCodes`, migrarea 36), potrivire sablon PDF pe trepte 30/60% si recunoasterea PDF-ului e-Factura (`InvoiceEFacturaPdf`), preluarea refactorizata in componente `Pickup*` si `InvoicePickup.*.cs`.
- Categorii: nume unice impreuna, stergere de categorii/subcategorii goale (`DeleteCategoryAsync`/`DeleteSubcategoryAsync`), iconite; `SearchableSelect` alege la mousedown; telefonul scurt din ANAF (`BeneficiaryRules.PhoneCoveredBy`).
- Suita completa 1038 trecute. De verificat in browser: vezi `docs/TESTE_RAMASE.md` si `Teste utilizator/` 01, 04, 06.

## Ultimul lucru facut (08.10.2026, a treia parte)

- ANAF: reguli pe camp aplicate in formulare (`AnafApplyRules`, dialogul „Date diferite in ANAF”), coloana „Folosit in”, versiunile configuratiei cu slider de activare si stergere (jurnal exact), HTTP 404 cu corp `notFound` = CUI inexistent (nu adresa gresita).
- Pagina produsului: taburi Intrare/Iesire (golesc formularul la schimbare), filtre ca in Jurnal (cautare, liste, etichete „Filtre active”, valori filtrabile in tabel, stare in adresa, filtru pe zi), carduri in doua coloane, validare cantitate reala la stoc negativ.
- Interfata: butoane colorate dupa actiune si de aceeasi inalime (40 / 32 px), insigne `count-badge`, spatiu dupa randuri de butoane, liste de alegere deschise peste card, fonturi uniforme, cutie de cautare cu lupa SVG, fieldset fara chenar, tooltip pe butoane cu o glifa, camp drag and drop la preluarea inventarului (regulile sunt in `CLAUDE.md`).

## Ultimul lucru facut (08.10.2026, a doua parte)

- Setari -> Backup este un tab principal cu sub-taburile „Backup si restaurare” (generare, notificare, stergere pachete vechi, tabel cu pachetele de pe server si NAS), „Backup NAS” si „Verificare ora”. Pagina de restaurare separata a disparut.
- Backup programat doar in zilele alese (migrarea 35); stergere automata a pachetelor vechi o data pe zi si pe loc la salvarea unui prag nou (cu confirmarea listei); ultimele 4 pachete se pastreaza; pe NAS nu se sterge nimic din aplicatie.
- Ora serverului se compara cu internetul (NTP alese in Setari, apoi antet HTTPS): stergerea asteapta cand ceasul difera cu peste 5 min; backup-ul cu ceas decalat peste 15 min ia ora de pe internet; ora imposibil de verificat marcheaza pachetul „ora neverificata” (exclus din stergerea automata, sters doar manual de administrator). Notificare noua „Ceas server decalat” (migrarea 34).
- Notificari: un tab pe categorie cu numarul de sabloane, sabloane implicite pentru toate evenimentele (migrarea 32); iconite pe randurile tabelelor (regula in `CLAUDE.md`).

## Ultimul lucru facut (08.10.2026, prima parte)

- Completari stoc: iesire rapida din lista, cardul „Unde sunt bucatile", nivel tinta pe vehicul + „Completeaza la nivel" (migrarea 28), stoc minim, termen pe proiect (migrarea 29), notificarile „Stoc sub minim" / „Rezervare fara miscare" / „Deficit la proiect cu termen", pagina `/consum` (export CSV). Suita MariaDB: 1518 trecute, componente: 185.

## Anterior (07.10.2026)

- Stoc doar pe bucati; operatia de iesire / iesirea multipla, bon PDF, storno, retur, consum net; lista „De regularizat" (cauza iesirii peste stoc).
- Nomenclator „Tipuri de sisteme" (`/nomenclator`), componente pe proiect, sabloane de oferte xlsx (`/oferte/sabloane`), preluarea ofertelor (`/oferte/preluare`, `/oferte`, revizii cu diferente).
- Situatia proiectului pe componente (`/proiecte/{id}/situatie`, export PDF/CSV, lista de achizitie), iesiri legate de componente (migrarea 26) cu lamurire la scoaterea componentei.
- Rezervari pe proiect (migrarea 27): stoc liber = stoc - rezervari, consum la iesire, avertisment fara blocare, propuneri dupa intrare, intrari legate de oferta (si la preluarea facturii).
- Backup/restore: tabelele migrarilor 22-27 au fost adaugate in lista tabelelor (`MariaArchiveSchema.MigratedTables`).
- Pagina Facturi `/facturi`; protocol cu context redus (`PROTOCOL_CLAUDE_OPTIMIZAT.md`, `tools/run-checks.ps1`, `tools/log-task.ps1`).

## Validari

- Suita in memorie: 979 verificari trecute; suita MariaDB `blazorstoc_test`: 1588 trecute, 0 esecuri; UI pe componente: 229 (08.10.2026, inainte de commit). Migrarile 1-35 aplicate pe `blazorstoc_test`; pe `BlazorStoc` migrarile 34-35 se aplica la repornirea aplicatiei.
- Neverificat in browser: tot ce e in `Teste utilizator/` marcat 07.10.2026 (utilizatorul nu a testat inca nimic).

## Preview

- `tools\start-preview.ps1` publica si porneste pe `http://127.0.0.1:5087/`, pe MariaDB locala (port 3307, `local-secrets/application-connection.private.json`). Se republica doar la „review”/„preview”.

## Probleme deschise

- Propunere de studiu, neimplementata: selectarea liniilor ofertei la pasul 4 si stergerea unei oferte din proiect (`docs/PROPUNERE_OFERTE_SELECTIE_STERGERE.md`); asteapta deciziile din sectiunea 5.

- Verificari ramase: `docs/TESTE_RAMASE.md` (cazurile N45, N47 si cele de pe pagina Facturi); drepturile contului migrator pe baza de teste.
- Taskuri active: niciunul in `TODO.md`.

## Urmatorul pas

Il stabileste utilizatorul.
