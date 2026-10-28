# Starea curenta a proiectului

Actualizat: 27.10.2026, Europe/Bucharest (Claude, agent unic, mod `claude_only`). Istoricul vechi: `docs/arhiva/` (vezi `docs/arhiva/INDEX.md`). Cand ceva de aici se invecheste, se inlocuieste, nu se aduna.

## Aplicatia

- Blazor Interactive Server, .NET 9, MariaDB, doar Release. Fara SQLite sau mod demo in aplicatie; fara Docker in aplicatia rulata (permis pentru dezvoltare/testare); copierea backup-urilor pe NAS este optionala, din Setari -> Backup -> Backup NAS.
- Module: produse/stoc, categorii, beneficiari/proiecte, vehicule, mentenanta (contracte, interventii, harta), notificari, jurnal, furnizori si facturi, preluare factura PDF/OCR cu sabloane, preluare inventar.
- Motorul de facturi este geometric (`Services/Invoices`); utilizatorii obisnuiti creeaza/modifica sabloane, stergerea ramane administratorului.
- Harta codului: `docs/HARTA_COD.md`. Reguli permanente: `CLAUDE.md`.

## Ultimul lucru facut (27.10.2026): versiunile 0.5.24 - 0.5.27 (parametri, familii, etichete, popup produs)
- Catalog: coloana PARAMETRI cu etichete de filtrare, filtru "Doar incomplete", iconita de editare pe rand; editarea unui produs se face intr-un popup (`Components/Pages/Home.razor`, `ProductEditor.razor`).
- Parametri obligatori: tip Valoare fixa (`ParameterKind.Fixed`, doar in familie), schimbarea tipului la editare, stergerea unui parametru cu produse (codurile se rescriu), produsele incomplete sunt blocate in toate operatiile de stoc (storno permis, banner pe pagina produsului).
- Etichete pe subcategorie (migrarea 53, `Services/ProductTags.cs`, tabelele `subcategory_tags`/`product_tags`): On/Off pe produs, filtre in catalog, jurnal propriu; fereastra "Parametri si etichete" are trei sectiuni colapsabile (`SubcategoryParametersPanel.razor`).
- Ruta paginii in bara de sus, meniul ramane deschis la subcategoria produsului; numarul de randuri ales la un tabel paginat se pastreaza in browser (`AuditPager.razor`, localStorage pe id-ul tabelului).
- Modulul Jurnal 1.2.0, Mentenanta 1.3.0. Cont de test pentru migrari: `local-secrets/test-migrator.private.json` (cont `claude_test_migrator`, DDL numai pe `blazorstoc_test`), folosit prin `MARIA_TEST_MIGRATOR_CONFIG_PATH`.
- Verificat: suita in memorie 2064 PASS, MariaDB 3115 PASS, 0 esecuri; pachetul 0.5.27 se construieste cu `tools\release.ps1`. Captura help `categorii-parametri` este scoasa si de refacut la cerere (`docs/HELP_DE_REGENERAT.md`).

## Ultimul lucru facut (24.10.2026): versiunea 0.5.18 (corectii tema dark)
- `wwwroot/theme.js` pastreaza tema peste navigarea intre pagini; `wwwroot/theme-dark.css` are mapare pe puncte a luminozitatii (generata cu un script din `:root`, apoi reguli ajustate manual dupa marcajul `/* --- hand-tuned --- */`), reguli pentru butoane, campuri, imaginea generica si placile hartii.
- Suita completa: 2047 PASS in memorie, 3024 PASS pe MariaDB; pachetul 0.5.18 se construieste cu `tools\release.ps1`.

## Ultimul lucru facut (24.10.2026): versiunea 0.5.17 (tema dark, ajutor unificat, Setari -> Jurnal activitate, versiuni pe module)
- Tema dark: tokenuri de culoare in `app.css`, paleta in `wwwroot/theme-dark.css` (generata din `:root` cu luminozitatea inversata, apoi ajustata), comutator `wwwroot/theme.js` in bara de sus, alegerea in localStorage (cheia `blazorstoc-theme`). Help si capturi numai pentru light.
- Ajutor unificat: `Services/Help/HelpContents.cs` (sectiuni dupa prefixul cheii topicului), endpoint `/ajutor/cuprins`, intrarea „Ajutor" in meniu si placa pe pagina principala; cautarea apare in orice fereastra de ajutor.
- Setari -> Jurnal activitate (`Modules/Journal/JournalSettings*.cs`, migrarea 51 `journal_settings`, cheia `setari-jurnal`): evenimentele recente afisate implicit (50-50 000, implicit 500); jurnalul nu se sterge niciodata.
- Versiuni pe module: `Modules/module-versions.json` + `tools/module-version.ps1` (-Bump, -Init, -Refresh, -Status) + grupul de teste `moduleversions`.
- Help: capturile au fost regenerate (`--all`, tema light) dupa intrarea „Ajutor" din meniu; vezi `docs/HELP_DE_REGENERAT.md` pentru ce ramane.
- Pachetul de update 0.5.17 se construieste cu `tools\release.ps1`; instalarea pe serverul real ramane pasul utilizatorului (Teste utilizator/09).

## Ultimul lucru facut (23.10.2026): versiunile 0.5.7 - 0.5.11 (Preluare tichete ActivityApp, Setari ca panou)

- Modulul **Preluare tichete ActivityApp** (`Modules/ActivityImport/`, Setari -> Preluare ActivityApp; vezi `docs/PROPUNERE_IMPORT_TICHETE.md` si `docs/HARTA_COD.md`): citire read-only din SQL Server, tipuri de tichete alese, corespondenta campurilor, cuvinte cheie de mentenanta (Nomenclator), sincronizare periodica sau la ore fixe + "Importa acum", beneficiari verificati in ANAF (`ICompanyDataLookup` din nucleu), coada beneficiarilor de verificat (Beneficiari -> De verificat), citire incrementala cu marcaj ("Reciteste tot"), tichete in asteptare, verificare umana a mentenantei (mentenanta s-a facut / la cerere / discard, reactivare). Migrarile 044-050.
- Setari este un panou cu tile-uri; pagina principala are acelasi aspect; meniul lateral are bara verde la activ/hover; Istoricul de actualizari are paginare; changelog pe versiune (`Assets/Data/changelog.txt`).
- Telefonul beneficiarului este optional. Persoanele fizice (fara CUI, cod personal, nume in campul CUI) nu se preiau din ActivityApp.
- Verificat (23.10.2026): suita in memorie 2039 PASS, MariaDB 3009 PASS, 0 esecuri; pachetul de update 0.5.11 (schema 50) facut si verificat. Neverificat pe un ActivityApp real (doar pe date din export si pe un SQL Server simulat); help-ul nu este actualizat (lista in `docs/HELP_DE_REGENERAT.md`, regula din 23.10.2026).

## Ultimul lucru facut (23.10.2026): versiunea 0.5.6

- Pagina de autentificare: comutatorul "Pastreaza-ma autentificat" (cookie de 30 de zile; implicit oprit, sesiune de 8 ore); Jurnalul spune cand o conectare a fost pastrata.
- Verificat: suita in memorie 2034 PASS, MariaDB 2868 PASS, 0 esecuri.

## Ultimul lucru facut (22.10.2026): versiunea 0.5.5

- Jurnal: Conectare/Deconectare cu adresa IP; operatie noua "Conectare esuata" (nume tastat, motiv, IP, fara parola). Pagina de login arata versiunea reala. Formular produs: lipire imagine doar cu Ctrl + V (butonul de clipboard scos), meniul din stanga cu scroll. Regula noua: teste tintite, suita completa numai inainte de commit.
- Verificat: suita in memorie 2033 PASS, MariaDB 2867 PASS, 0 esecuri. Nerezolvat: nimic cunoscut.

## Ultimul lucru facut (20.10.2026): backup si restaurare cu imaginile si fisierele

- Orice backup contine acum si cele patru foldere de imagini/fisiere (product-images, project-files, service-photos, archive-files), nu si cheile Data Protection; restaurarea le readuce odata cu baza (fisiere pregatite, foldere schimbate, apoi baza; rollback al folderelor daca baza nu se schimba); un pachet vechi restaureaza numai baza. Detalii: `docs/HARTA_COD.md`, `docs/OPERARE_ACTUALIZARE.md`, `Teste utilizator/09`. Verificat: grupul `backupassets` 26 PASS, sectiunea MariaDB "Backup worker" si rulare reala (backup, restaurare, pachet stricat, pachet vechi) pe baza de test.

## Ultimul lucru facut (20.10.2026): actualizarea aplicatiei, etapele 1-5

- Plan si decizii: `docs/PROPUNERE_ACTUALIZARE.md`. Etapa 1: `AppVersion` (0.5.0), registrul `schema_migrations` (migrarea 43, `MariaSchemaLedger`, `MariaSchemaMigrator.Plan`, checksum/drift, `--migrate-schema` cod 3), configurarile ANAF/harta si jurnalul in fisier in radacina datelor (`AppDataRoot.DataFile`). Etapa 2: `tools/release.ps1` + `tools/BlazorStoc.Updater` (cheie ECDSA a masinii, pachet `.bsu` semnat, verificare); publish-ul nu mai poarta `local-secrets/` si `data/`.
- Verificat: suita 1813 PASS, MariaDB 2597 + 29 PASS, migrarea reala pe baza BlazorStoc, un release real 0.5.0 (302 fisiere, 100 MB). Cheia de semnare a acestei masini: `%LOCALAPPDATA%/BlazorStoc-release`.
- Etapa 3 (Updater care instaleaza: `install`, `rollback`, `status`, `trust`, `init`, rollback automat, supervizor pe `current.txt`) facuta si verificata cu procese reale; suita 1851 PASS, MariaDB 2683 PASS, supervizor 36 PASS.
- Etapa 4 (Setari -> Actualizare: incarcare pachet, verificare de Updater, confirmare cu motiv, anunt, jurnal, revenire automata) facuta si verificata pe un server de proba real; grupul `package` 122 PASS, `help` 61 PASS. Pe server: `BlazorStoc.Updater init` ca administrator, pe contul aplicatiei, inregistreaza sarcina programata prin care aplicatia porneste Updater-ul.
- Etapa 5 facuta si verificata: `release.ps1` produce acum si `BlazorStoc-Actualizare-<versiune>.exe` (lansator + pachet + Updater, `--repair`, `--rollback`, `--status`, `--check`, `--init`); documentatia de operare in `docs/OPERARE_ACTUALIZARE.md`; grupul `package` 169 PASS plus rulare reala a fisierului (vezi `docs/PROPUNERE_ACTUALIZARE.md`, etapa 5). Pe serverul real raman pasii utilizatorului din `Teste utilizator/09`.
- Ramas din actualizare: nimic in cod; doar pasii utilizatorului pe serverul real (Teste utilizator/09) si, optional, semnatura Authenticode a installerului.

## Ultimul lucru facut (20.10.2026): Setari -> Baza de date

- Implementat si verificat (vezi `IMPLEMENTED.md`, `docs/PROPUNERE_SETARI_BAZA_DE_DATE.md` sectiunile 15-18): acces pe rol, stare, conexiune, conturile bazei, sesiuni, serverul bazei ca serviciu Windows `BlazorStocMariaDB` (pornire/oprire/repornire/port), portul aplicatiei cu repornire sub supervizor.
- Mediu: datele sunt in `C:\ProgramData\BlazorStoc`; MariaDB-ul de dezvoltare ruleaza ca serviciu (`tools\install-mariadb-service.ps1`); supervizorul previewului se inregistreaza FARA `-WithMariaDb` si respecta portul salvat (`Hosting__DefaultPort`, cod de iesire 75 = repornire).
- Ramas: 1.1 Verificare (sanatate/integritate), 1.2 aplicatia ca serviciu Windows + monitor de sanatate si pagina "Baza indisponibila" (vezi `TODO.md`).

## Ultimul lucru facut (10.10.2026, tipuri de utilizatori)

- Tipuri de utilizatori cu permisiuni (propunere si decizii: `docs/PROPUNERE_TIPURI_UTILIZATOR.md`): migrarea 40 (`user_types`, `user_type_permissions`, `web_users.user_type_id`), `Services/Permissions.cs` (catalog modul.actiune), `UserPermissions.cs` (permisiunile utilizatorului din tip, cache 30 s, politici `perm:<cheie>`), `UserTypes.cs`, `UserTypeSeeder` (tipurile de sistem Administrator/Utilizator si legarea conturilor, la pornire cu contul aplicatiei: migratorul nu are drepturi pe date).
- `IAccessControl` are `HasAsync/EnsureAsync/EnsureAnyAsync/EnsureModuleAsync`; fiecare repository verifica cheile modulului lui (operatiile admin-only fara cheie in catalog raman `EnsureAdministrator`: miscari stornate, blocari produs, parametri categorii, stergeri facturi/furnizori/sabloane oferte). Paginile au `[Authorize(Policy="perm:...")]`, endpoint-urile `/api` si `/media` politici pe cheie, meniul `AuthorizeView Policy`, butoanele add/edit/delete pe paginile principale dupa permisiuni. `Account/AccessDenied` si `AccessDeniedNotice` jurnalizeaza "Acces refuzat".
- UI: Setari -> "Tipuri de utilizatori" (`UserTypesPanel`); selectorul de tip in `UserEditor`; rolul din `web_users` ramane Administrator/Utilizator, derivat din tip.
- Rafinari ulterioare (10.10.2026): modulul "Stoc" (stoc.view / intrare / iesire / modificare / stornare / minim / rezervare) separat de "Produse" (adaugare / editare / stergere produs / export); cheile vechi produse.view, produse.iesiri, inventar-preluare.edit sunt migrate/sterse de `UserTypeSeeder`; fiecare operatie din registre cere cheia ei (adaugare / editare / stergere), citirea registrelor cere orice permisiune; parametrii categoriilor cer categorii.edit; pagina principala are carduri pe permisiuni (inclusiv Preluare factura, Iesire multipla, Export consum, Preluare inventar).
- `CurrentUserAccess` ia utilizatorul din cererea HTTP cand nu exista circuit Blazor (`IHttpContextAccessor`): corectat 500 la bonul de consum PDF, exporturile CSV/PDF si imaginile servite direct.
- Aranjarea subcategoriilor prin tragere (migrarea 41, `subcategories.sort_order`, `ReorderSubcategoriesAsync`, jurnal "Reordonare subcategorii", `wwwroot/category-order.js`); dialogurile de mutare/editare au butoane radio unitare; grupurile din matricea de permisiuni sunt pliabile cu insigne acordate/refuzate.
- Verificat la final: in memorie 1094, MariaDB 1852, componente 341 trecute.
- Verificat: suita in memorie 1094 trecute, MariaDB 1819 trecute; migrarea 40 aplicata pe `blazorstoc_test` si pe BlazorStoc. De verificat in browser: `Teste utilizator/16_Tipuri_de_utilizatori.md` (UI nevazut inca). Fara butoane dupa permisiuni inca: Oferte, Mentenanta, sectiunile Analize de risc, Notificari, fila Facturi din Setari (serverul le protejeaza oricum).

## Ultimul lucru facut (10.10.2026, analize de risc)

- Analize de risc pe punct de lucru: migrarea 39, `Services/RiskAnalyses.cs`, `MariaRiskAnalysisRepository`, sursa de notificari `analiza-risc.expirare` (`RiskAnalysisNotificationSources`, prag 60 zile), pagina `/analize-risc`, sectiunea de pe fisa beneficiarului (`RiskAnalysesSection`, `RiskAnalysisEditor`, `RiskAnalysisRenewalForm`). Propunere si decizii: `docs/PROPUNERE_ANALIZE_RISC.md`.
- Verificat: MariaDB sectiunea „Risk analyses” 45 trecute (migrarea 39 aplicata manual pe `blazorstoc_test`; pe baza BlazorStoc se aplica la pornirea aplicatiei), grupurile structure+ui 337 trecute. Suita completa nerulata. De verificat in browser: `Teste utilizator/15_Analize_de_risc.md` (UI nevazut inca; previzualizarea nu a fost republicata).

## Ultimul lucru facut (10.10.2026, refactorizare fara schimbari de functionalitate)

- Acces la baza de date comun in `Services/MariaDb.cs` (conexiune, comanda, tranzactie de scriere Serializable cu retry la deadlock); toate repository-urile si serviciile Maria* il folosesc. `Program.cs` are ~230 de randuri: inregistrarile DI pe module in `Startup/ServiceRegistration.cs`, fisierele private de conturi in `Startup/PrivateConfiguration.cs`.
- Date scoase din cod: codurile de unitate in `Assets/Data/*.tsv` (`EmbeddedData`), migrarile 1-38 in `Assets/Migrations/NNN.sql` (`MariaSchemaMigrations`, acelasi `All`); verificat identic cu varianta veche (38 migrari, 83 instructiuni, 393 coloane). Dublurile `Demo*` au trecut in `tests/BlazorStoc.Checks/Demo/`.
- Pagini mari impartite in clase partiale (`ProductMovements`, `InvoicePickup`, `InvoiceTemplateWorkbench`); metode lungi desfacute (`InvoiceRowScan`, `InventoryPickupOcr.ScanPageAsync`, `GetPageAsync`/`InsertMovementAsync`, `StockMovements.Validated`); fisierele de teste impartite pe subiecte (`FullRun.*.cs`, `MariaExtendedChecks.*.cs`).
- Schimbari de comportament voite: retry la deadlock si pentru Project, ProjectComponent, Offer, OfferTemplate, SystemType; Project verifica `IsAllowedDatabase`; anularea se verifica inainte de deschiderea conexiunii.
- Teste noi `StructureChecks` (grupul `structure`, 23): rutele (`@page` din .razor = rutele compilate; prinde un `@` pierdut), `SettleNumbers`, `OverStockOf`. Verificat pe 30 de PDF-uri reale (raport identic) si pe fixturile OCR (identic).
- Suita in memorie 1089 trecute, MariaDB 1744 trecute, 0 esecuri; migrarea 38 aplicata acum si pe `blazorstoc_test`. Paginile impartite verificate in browser de agent (miscari produs, preluare factura PDF/ZIP, sabloane, categorii, vehicule, harta, nomenclator, inventar); raman de verificat de utilizator pasul 3 al preluarii facturii si salvarea unui sablon (`Teste utilizator/` 02 si 04).
## Ultimul lucru facut (28.10.2026, catalog: arhivare, definitii pe categorie, relocare, tragere pe niveluri)

- Ramura `claude/arhivare-parametri-categorii` (necomisa): etapele 0-8 gata. Migrarea 056; servicii `ParameterArchive`, `DefinitionRelocation`/`MariaDefinitionRelocation`, `DefinitionOwner`; `SubcategoryTree` cu `PreviewMoveAsync`/`MoveAsync(choices)` si `PreviewPromotionAsync`/`PromoteAsync`; ferestre `ParameterArchive/Restore/UsageDialog`, `TakeFromChildDialog`, `BranchMoveDialog`.
- Reguli: definitiile folosite se arhiveaza, nu se sterg; categoria are definitii proprii (cheia negativa = categorie); mutarea copiaza ce se mostenea; frunza devine categorie numai dupa redenumirea subcategoriei si verificari.
- Versiune 0.5.29, Journal 1.4.0. Help/capturi de refacut: `docs/HELP_DE_REGENERAT.md`. Teste de urmarit de utilizator: `Teste utilizator/01_Stoc_si_produse.md`.
- Nefacut: retrogradarea categorie -> subcategorie, scurtaturi Ctrl+sageti; pista paralela (asistent de curatare produse si export pachet) incepe cu migrarea 57.

## Ultimul lucru facut (10.10.2026, catalog si preluare factura)

- Ordinea categoriilor prin drag and drop (administrator; migrarea 38 `categories.sort_order`, `ReorderCategoriesAsync`, `wwwroot/category-order.js`), aceeasi ordine in meniul lateral; jurnal "Reordonare categorii".
- Preluare factura: avertizarea "deja preluat" si deselectarea implicita a randurilor deja preluate la pasul 1 (`RefreshTakenPreviewAsync`), selector de randuri si la pasul 2 (`excluded` comun, `PickRow.RowIndex`).
- Popup pentru detaliile punctului de pe harta; tipul parametrului cu comutatoare; padding minim 6/12 px la insigne; baza locala curatata de categoriile in afara de TVCI si Control Acces.
- De verificat in browser: `Teste utilizator/` 01, 04, 08.

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

- Suita in memorie: 1089 verificari trecute; suita MariaDB `blazorstoc_test`: 1744 trecute, 0 esecuri (10.10.2026). Migrarile 1-38 aplicate pe `blazorstoc_test` si pe `BlazorStoc`.
- Neverificat in browser: tot ce e in `Teste utilizator/` marcat 07.10.2026 (utilizatorul nu a testat inca nimic).

## Preview

- `tools\start-preview.ps1` publica si porneste pe `http://127.0.0.1:5087/`, pe MariaDB locala (port 3307, `local-secrets/application-connection.private.json`). Se republica doar la „review”/„preview”.

## Probleme deschise

- Help contextual (branch `feature/help`, `docs/PROPUNERE_HELP.md` cu deciziile din 19.10.2026): etapa 1 gata, necomisa (infrastructura `Services/Help`, `/ajutor/*`, `wwwroot/help.js`, `HelpButton`, 6 topicuri `Assets/Help/*.help`, grupul de teste `help`). Toate ferestrele au topic (89 de fisiere in `Assets/Help`, lista `Pending` din `HelpChecks.cs` este goala); confirmarile simple sunt exceptate si migrarea dialogurilor la `HelpButton`; harta de cod si regula in CLAUDE.md sunt actualizate.

- Propunere de studiu, neimplementata: selectarea liniilor ofertei la pasul 4 si stergerea unei oferte din proiect (`docs/PROPUNERE_OFERTE_SELECTIE_STERGERE.md`); asteapta deciziile din sectiunea 5.

- Verificari ramase: `docs/TESTE_RAMASE.md` (cazurile N45, N47 si cele de pe pagina Facturi); drepturile contului migrator pe baza de teste.
- Taskuri active: niciunul in `TODO.md`.

## Urmatorul pas

Il stabileste utilizatorul.

## Actualizare 12.10.2026

- Drepturi: dependente intre permisiuni (`Permissions.Requires`), module rezervate administratorului (ANAF, Backup); fluxurile nu mai inlocuiesc drepturile de domeniu (furnizori.add, facturi.add).
- Baza locala golita de produse/facturi/oferte/jurnal si reincarcata cu 370 de produse din ESP_Stoc prin `tests/BlazorStoc.EspImport` (analiza si fisierele in `artifacts/esp-import`, necomise).
- Verificari ramase: pasii din `Teste utilizator/16_...` (25-29) si `11_...`.

## Actualizare 15.10.2026 - module (branch refactor/module)

- Pilot de modularizare: contractul `IModule`, registrul cu circuit breaker (`Modules/`), modulul Analize de risc mutat in `Modules/RiskAnalyses/`, meniu din registru, `ModuleGate`/`ModuleUnavailable`, `/health/modules`, oprire din configurare (`Modules:<id>:Enabled=false`). Schema, URL-urile, permisiunile si jurnalul nu s-au schimbat.
- Verificat: suita in memorie 1154 PASS, grupul `modules` (nou, 58), MariaDB 'Risk analyses' 45 PASS; ecrane verificate in browser cu modulul pornit (5090) si oprit din configurare (5091): meniu, pagina principala, fisa beneficiarului, /analize-risc, /health/modules, iar notificari/vehicule/produse merg. Corectate in timpul verificarii: stand-in pentru repository cand modulul e oprit, `<Shared.X />` din Routes.razor (era eticheta HTML necunoscuta; inclusiv `AccessDeniedNotice`), test nou care interzice acest tip de eticheta. Esec preexistent, identic pe commitul de baza: MariaDB 'User types' ("A user moved to the Administrator type has the role and every key").
- Defectiune reala simulata pe baza de test (5092): o analiza cu date 0000-00-00 (InvalidCastException la citire) a dus modulul la degraded dupa prima eroare, la disabled dupa a treia (pagina /analize-risc: Modul indisponibil, meniul si tile-ul dispar, fisa beneficiarului, notificari, produse si pagina principala merg); dupa 30 s modulul e degraded, o eroare il scoate din nou imediat, iar dupa stergerea inregistrarii revine la healthy si sectiunea reapare. Inregistrarea de proba a fost stearsa (risk_analyses: 0 randuri).
- Verificate in aplicatie (baza de test, 5092): defecte in sursa de notificari (3 evaluari esuate -> modulul disabled, notificarea existenta ramane nerezolvata, /notificari merge); baza de date oprita (/health 503, modulul ramane healthy dupa 5 incarcari esuate, paginile isi arata propria eroare, dupa repornirea bazei revin fara repornirea aplicatiei). Datele de proba sterse (risk_analyses 0 randuri, notificarea de proba stearsa).
- Urmatorii pasi propusi: worker OCR si supervizor; restul modulelor treptat. Nimic comis.

## Actualizare 15.10.2026 - worker OCR

- OCR-ul (facturi PDF, recitirea numerelor, inventar scanat) ruleaza intr-un proces worker separat, prin modulul `ocr` (`Modules/Ocr/`), cu circuit breaker, limite de timp/memorie/paralelism si `Ocr:Mode=InProcess` ca drum inapoi. Paginile si interfetele nu s-au schimbat.
- Verificat: suita in memorie 1190 PASS (36 noi in grupul `ocrworker`: rezultate identice cu motorul in proces pe PDF cu text, factura scanata, recitirea numerelor si inventarul real; fisier refuzat, worker mort, blocat, anulat, rezultat invalid, limite, inregistrari); in aplicatie (5090, administrator.demo): inventar si facturi (text si scanata) citite prin worker, 3 workeri omorati in timpul lucrului -> mesaj clar, ocr disabled, restul aplicatiei merge, dupa 30 s revine la healthy fara repornire.
- Descoperit la verificare: vechile inregistrari din ServiceRegistration suprascriau modulul (testele izolate nu vedeau); corectat si pazit de test. Limitat: memoria nativa (OpenCV/Tesseract) nu e plafonata de DOTNET_GCHeapHardLimit; un Job Object Windows ar fi urmatorul pas.

## Actualizare 15.10.2026 - supervizor

- `tools/supervisor.ps1`: tine aplicatia pornita (repornire cu backoff, repornire cand `/health/live` nu mai raspunde, nu cand doar baza e oprita, renuntare dupa 10 caderi in 10 minute, `-WithMariaDb` reporneste baza locala), cu stare, jurnal si oprire curata; `install` (sarcina programata la autentificare) este scris dar NU rulat (configuratie persistenta, asteapta acordul utilizatorului).
- Verificat: `tools/test-supervisor.ps1` 23 PASS pe o aplicatie falsa (cadere, blocare, baza oprita, iesire imediata, program care nu poate porni, oprire in asteptare, script de baza care lasa un proces); pe aplicatia reala (5090): omorata -> repornita in 3-4 s, inghetata (proces suspendat) -> detectata si inlocuita in 24 s, MariaDB oprita -> repornita in 6 s fara repornirea aplicatiei, oprire curata in 1,5 s (aplicatie, supervizor si port inchise, baza ramasa pornita). Suita in memorie 1198 PASS.
- Defecte gasite la verificare si corectate: aplicatia cadea la pornire cu `MaintenanceInProgressException` cand un blocaj de backup ramas de la un proces omorat nu expirase (Program.cs, pazit de test); scrierea starii/jurnalului supervizorului putea arunca si opri supervizorul (acum cu reincercari); `Start-Process -Wait` bloca bucla supervizorului la repornirea bazei (acum asteapta doar scriptul, max 90 s); testele limitei de login nu erau legate in `StructureChecks.Run` (legate acum, 8 verificari noi rulate cu adevarat).
- De stiut: un backup programat porneste la pornirea aplicatiei si poate fi intrerupt de o repornire (blocajul expira singur dupa 2 minute); memoria nativa a workerului OCR nu e plafonata.

- 15.10.2026 (la cererea utilizatorului): sarcina programata BlazorStocSupervisor-5087 instalata (	ools\supervisor.ps1 -Action install -Port 5087 -WithMariaDb): la autentificarea utilizatorului porneste supervizorul pe portul 5087, din artifacts\notif-build (build-ul din 12.10.2026, fara module: se republica cu tools\start-preview.ps1 sau dotnet publish -o artifacts\notif-build), repornire la esec de 3 ori la 1 minut. Pornita doar la urmatoarea autentificare sau cu Start-ScheduledTask; dezinstalare: -Action uninstall -Port 5087.

- Test de cadere pe 5087 (15.10.2026): aplicatia omorata -> repornita in 3,2 s, sesiunea utilizatorului ramane valabila; supervizorul omorat -> aplicatia ramane orfana si sarcina programata NU il reporneste (RestartCount nu reactioneaza la un proces omorat). Corectat in script: un supervizor care porneste opreste intai aplicatia ramasa de la rularea lui anterioara (recunoscuta dupa pid si ora de pornire din fisierul de stare), iar -Action install adauga un al doilea declansator, repetat la 5 minute (ignorat cat supervizorul traieste). Declansatorul periodic NU e inca instalat (sarcina existenta are doar declansatorul la autentificare; necesita -Action install din nou, cu acordul utilizatorului). Preluarea s-a verificat pe instanta reala: sarcina pornita manual a oprit aplicatia orfana si a ridicat una curata in 2,8 s. tools/test-supervisor.ps1: 28 PASS.

## Actualizare 15.10.2026 - lacune de vizibilitate (jurnal, ecran de stare, login, reconectare)
- Jurnal de activitate: modulele oprite/revenite si opririle neasteptate ale aplicatiei (marcaj de rulare, cu cauza de la supervizor) se scriu in Jurnal de catre `sistem`; scrierea se reincearca cat baza nu raspunde. Entitati noi `Modul`, `Aplicatie`; actiuni noi "Oprire modul", "Revenire modul", "Reincercare modul", "Oprire neasteptata aplicatie" (si in filtrul paginii Jurnal).
- Setari -> Module (doar administrator, permisiunea `setari-module`): starea fiecarui modul, de cand, cauza, "Reincearca" imediat; banner pentru administrator deasupra paginilor cand un modul e oprit.
- Login: baza oprita/backup in curs are mesaj propriu, nu "Utilizator sau parola incorecta".
- Reconectare: mesaj mai clar in dialogul Blazor si in #blazor-error-ui; reincarcarea automata a paginii o face Blazor (verificat in browser: aplicatia oprita 70 s, pagina deschisa se reincarca singura la revenire). Un script propriu de recuperare s-a dovedit inutil si a fost scos.
- Verificat: grupul `runtracking` (56) trece; supervizorul scrie nota de iesire si sterge marcajul la oprirea ceruta (teste noi in tools/test-supervisor.ps1). Verificare in browser: vezi TESTE_RAMASE.
- Module: butonul "Detalii" din bannerul de module oprite deschide fila Module si cand adresa era deja aplicata (parametrul `reaplica`, `SettingsNavigation.ModulesUrl`); verificat in browser cu o defectiune reala (circuit breaker, Reincearca, intrarile din Jurnal), cu un modul oprit din configurare, cu login la baza oprita si cu cauza oprii in Jurnal.

## Actualizare 15.10.2026 - modulul Vehicule
- Vehiculele sunt al doilea modul (`Modules/Vehicles/`, id `vehicule`): repository-urile (vehicule si niveluri tinta) trec prin circuit breaker, paginile sunt marcate `[Module]`, sursele de notificari (ITP, asigurare, rovinieta) sunt ale modulului; cu modulul oprit (configurare `Modules:vehicule:Enabled=false` sau erori repetate) paginile de stoc raman functionale (lista de vehicule goala, nota in selectorul de destinatie).
- Verificat: suita in memorie 1273 PASS (15 verificari noi in `VehicleModuleChecks`), MariaDB sectiunile Vehicles (31) si Vehicle target levels (4); in browser, modul oprit din configurare (pagina Vehicule, Iesire multipla, miscarile unui produs, Notificari) si modul pornit (lista, fisa, echipamente).

## Actualizare 15.10.2026 - modulul Mentenanta
- Mentenanta (contracte, interventii, harta si sursele de notificari) este al treilea modul (`Modules/Maintenance/`, id `mentenanta`), ordonat dupa Vehicule si inaintea Analizelor de risc. Punctele de lucru si pozele lor au ramas la fisa beneficiarului (le folosesc fisa, analizele de risc si contractele); asta face ca Analizele de risc sa citeasca punctele de lucru din nucleu, nu din modul.
- Cu modulul oprit: paginile Mentenanta si harta arata "Modul indisponibil", meniul si tile-ul dispar, fisa beneficiarului se deschide fara sectiunile de contracte/interventii, Setari -> Harta arata nota de modul oprit.
- Verificat: suita in memorie 1273 + verificarile noi din `MaintenanceModuleChecks` (grupul modules: 93), MariaDB sectiunile Maintenance contracts (47), interventions (58), notifications (13), Expiry notifications (58), Risk analyses (45); in browser pe un build separat cu modulul oprit si pornit.

## Actualizare 15.10.2026 - modulul Oferte, banner reimprospatat, defectiuni reale
- Oferte (preluarea devizelor xlsx si sabloanele) este al patrulea modul (`Modules/Offers/`, id `oferte`). Componentele proiectelor, rezervarile si iesirile au ramas in nucleul de stoc; situatia proiectului depinde de oferte si, cu modulul oprit, spune asta explicit.
- Defectiuni reale (rand cu date 0000-00-00 in baza de test) provocate pentru Vehicule si Mentenanta: fiecare a iesit din functiune dupa erori repetate, restul aplicatiei a mers, Jurnalul are Oprire modul si Revenire modul, modulele au revenit dupa restaurarea datelor. Pentru Oferte circuit breaker-ul e acoperit doar de teste.
- Banner-ul de module oprite se actualizeaza singur (eveniment + ceas de 10 s). Verificat in browser: Vehicule a disparut din banner dupa pauza de 30 s fara navigare.
- Cont de test pentru agent (`claude.test`) in `local-secrets/test-login.private.json`, comis in repo-ul privat; creat doar in baza de test.
- Verificat: suita in memorie 1312 PASS; MariaDB: Offer import (13), Offer templates (5), Project components (10), Reservations (10); in browser cu Oferte oprit din configurare si pornit.

## Actualizare 15.10.2026 - modulul Inventar
- Inventarul (generarea situatiei si preluarea foii numarate) este al cincilea modul (`Modules/Inventory/`, id `inventar`). Motorul OCR (`InventoryPickupOcr`) ramane in nucleu (il foloseste si modulul Ocr si citirea facturilor), la fel `InventoryPdfLayout`; fontul PDF comun a trecut in `PdfFonts`.
- Cu modulul oprit: /inventar si /inventar/preluare arata "Modul indisponibil", sectiunea de meniu si tile-urile dispar; restul aplicatiei nu e afectat. Verificat in browser oprit din configurare si pornit; suita in memorie 1327 PASS.

## Actualizare 15.10.2026 - modulul ANAF
- Interogarea ANAF (date firme dupa CUI) si configuratorul ei sunt al saselea modul (`Modules/Anaf/`, id `anaf`). Circuit breaker-ul numara raspunsurile "indisponibil" ale serviciului extern; cu circuitul deschis cautarea raspunde imediat, fara sa astepte timeout-ul. Editoarele de beneficiari si furnizori nu s-au schimbat (folosesc fluxul existent pentru ANAF indisponibil).
- Verificat: suita in memorie 1340 PASS, MariaDB Suppliers and invoices (65); in browser cu modulul oprit (Setari -> ANAF cu nota, editorul de furnizori spune ca modulul e oprit si propune introducerea manuala) si pornit (Setari -> ANAF).

## Actualizare 16.10.2026 - modulul Preluare factura, corectie banner
- Preluarea facturilor (pagina, sesiunile de analiza, sabloanele si fila Setari -> Facturi) este al saptelea modul (`Modules/Invoices/`, id `preluare-factura`). Motorul de citire si analiza (~4.700 de linii) a ramas in `Services/Invoices` fiindca il foloseste si modulul Ocr; modulul are ~1.600 de linii de servicii plus interfata. Registrul facturilor de furnizor si intrarile ramasera in nucleu.
- Regula noua `IBelongsToModule`: caderea workerului OCR nu mai scoate din functiune si modulul de facturi (o numara doar modulul Ocr).
- Corectie: bannerul de module oprite nu se reimprospata niciodata in aplicatia reala (layout static); acum e insula interactiva si dispare in sub 0,5 s dupa "Reincearca". Gasita prin verificare in browser; testul bUnit trecea fiindcat randeaza interactiv.
- Verificat: suita in memorie 1365 PASS (grupurile xml si pdf incluse), MariaDB 2117 PASS cu aceeasi sectiune care pica de la inceput (User types); in browser pe baza de test cu modulul oprit din configurare si cu o defectiune reala (definitie de sablon cu JSON invalid): iesire din functiune dupa 3 erori (JsonException), OCR neatins, furnizorii si intrarile merg, revenire dupa restaurare si Reincearca, Jurnal complet.
- Preluare factura: descrierea intrarii se propune automat la pasul 3 si pentru PDF cand sablonul nu are descriere (`InvoiceProductDescription.PlainEntry`, folosit si de fluxul XML); verificat in browser pe baza de test.

## Actualizare 16.10.2026 - modulul Notificari
- Motorul de notificari (serviciul, repository-ul, sabloanele implicite, pagina si fila din Setari) este al optulea modul (`Modules/Notifications/`, id `notificari`). Contractul surselor si interfetele raman in nucleu, deci sursele modulelor (vehicule, mentenanta, analize de risc) si ale domeniilor (stoc, facturi, backup) nu s-au schimbat.
- Cu modulul oprit sau defect, restul aplicatiei merge: pragurile primesc zilele implicite, nu apare triunghiul de avertizare, endpointul de alerte raspunde 0. Verificat in browser cu o data corupta intr-o notificare: modulul a iesit din functiune din simpla navigare (layout-ul ii cere evaluarea), nicio pagina nu a cazut, a revenit singur dupa restaurare.
- Verificat: suita in memorie 1387 PASS, MariaDB 2142 PASS cu aceeasi sectiune care pica de la inceput (User types).
## Actualizare 16.10.2026 - serviciile de fundal nu mai opresc aplicatia
- Verificare de izolare: toate serviciile de fundal prind deja erorile in bucla lor, dar implicit .NET opreste host-ul cand un `BackgroundService` arunca din `ExecuteAsync` (si doar citirea marcajului de rulare cu `UnauthorizedAccessException` ar fi scapat). Acum `AddHostResilience` seteaza `Ignore` (serviciul care cade se opreste singur, host-ul continua) si `RunMarker.ReadPrevious` prinde si `UnauthorizedAccessException`. Test cu control: fara setare host-ul se opreste, cu setare ramane pornit.

## Actualizare 16.10.2026 - backup, restaurare si copiere NAS intr-un proces separat
- Al zecelea modul, `backup` (`Modules/Backup/`): backup-ul, restaurarea si copierea pe NAS ruleaza intr-un proces `BlazorStoc.dll --backup-worker` (cerere pe stdin, raspuns pe stdout, ca la workerul OCR dar fara folder de job, ca sa nu ajunga parola NAS pe disc). Workerul ruleaza aceleasi clase motor, isi ia singur lacatul comun si actioneaza ca utilizatorul trimis de aplicatie; programul zilnic merge tot prin el (`IBackupOperations`).
- Ce castiga: o cadere, o blocare (partajare NAS care nu raspunde) sau lipsa de memorie la export / impachetare / hash / copiere costa operatia respectiva, nu aplicatia; limite de timp, anulare cu rugaminte de oprire si apoi omorarea arborelui de procese, lacat eliberat si fisiere temporare sterse imediat dupa un worker oprit (inclusiv fisierul cu parola contului de dump). `Backup:Mode=InProcess` ramane calea inapoi.
- Corectie gasita la verificare: un apel catre baza facut de alta sesiune in timpul unui backup (`MaintenanceInProgressException`) conta ca defectiune si scotea modulele (Notificari) din functiune; acum e asteptare normala.
- Verificat: suita in memorie 1437 PASS (47 verificari noi, grupul `backupworker`), MariaDB 2216 PASS cu aceeasi sectiune care pica de la inceput (User types), supervizor 30 PASS, sectiunea MariaDB `Backup worker` PASS (backup real, lacat luat de worker, anulare, restaurare pana la comutare - contul de restaurare nu se da workerului de test -, NAS oprit); in browser pe baza de test: backup din Setari, jurnal cu utilizatorul, pagini redirectionate la pagina de asteptare cat tine lacatul, defectiune reala (limita de timp de 1 s: 3 backupuri oprite scot modulul, restul aplicatiei merge, lacatul eliberat) si backup programat de la 02:00 mutat pe baza de test.
- Observatie: un backup dureaza ~14 s pe baza de test (hash canonic inainte si dupa, dump, verificare ceas), la fel in worker si in aplicatie.
- Ramas: restaurarea completa (comutarea schemelor) cu contul de restaurare prin worker si copierea pe un NAS real prin worker nu au fost rulate in aceasta verificare (vezi `docs/TESTE_RAMASE.md`).

## Actualizare 16.10.2026 - modulul Furnizori
- Furnizorii si registrul facturilor de furnizor sunt al unsprezecelea modul (`Modules/Suppliers/`, id `furnizori`): paginile /furnizori, /furnizori/{id}, /facturi, cele patru servicii (furnizori, facturi, coduri de produs, jurnal de recunoastere), meniul si tile-urile. `SupplierEditor` a ramas in nucleu (il folosesc si paginile de preluare factura).
- Cu modulul oprit sau defect: stocul merge si arata factura / furnizorul intrarilor (SQL direct), originea unei intrari ofera doar intrarea libera, preluarea facturii spune ca furnizorii sunt indisponibili, paginile modulului arata "Modul indisponibil".
- Verificat: suita in memorie 1453 PASS (16 verificari noi), MariaDB 2232 PASS cu aceeasi sectiune care pica de la inceput (User types), supervizor 30 PASS; in browser pe baza de test: modul oprit din configurare, defectiune reala (data de verificare corupta la un furnizor: 3 incarcari esuate scot modulul, stocul merge, dupa 30 s si cu data refacuta revine), datele restaurate.

## Actualizare 17.10.2026 - restaurare completa prin worker
- Restaurarea completa (comutarea schemelor `_bak` / `_old` cu contul `blazorstoc_restore`) rulata prin workerul de backup pe 5087, baza locala BlazorStoc: reusita la a doua incercare; prima s-a oprit inainte de comutare la verificarea copiei de siguranta (alta conexiune a scris in fereastra de export; baza nemodificata). Dupa restaurare: 72 tabele, 370 produse, 18 triggere, schema `BlazorStoc_old` pastrata, aplicatia sanatoasa, Jurnal "Restaurare". Detalii in `docs/TESTE_RAMASE.md`. Previzualizarea 5087 a fost republicata cu build-ul curent (modulele noi incluse).
- 17.10.2026 (dupa restaurare): pagina de asteptare dadea 500 in timpul unui backup/restaurare (redirectul era dupa UseAuthorization, iar politicile perm:* citesc permisiunile din baza). Corectat: redirectul vine inaintea autentificarii (Program.cs) si `MariaUserPermissions` foloseste ultimele chei cunoscute cat tine lacatul. Teste: structure 39 PASS, suita completa in memorie 1455 PASS, 0 esecuri. NU verificat in browser dupa corectie (instanta 5087 are build-ul cu doar prima parte a corectiei; republicare prin supervizor: supervisor.ps1 -Action stop, dotnet publish -o artifacts\notif-build, Start-ScheduledTask BlazorStocSupervisor-5087).
- Mediu: procesele pornite din aplicatia Claude (MSIX) vad un %LOCALAPPDATA% virtualizat; instanta pornita de agent si supervizorul au pachete de backup si setari NAS diferite. Nu se ruleaza start-preview.ps1 pe 5087 cat sarcina programata o detine.
- Ramas: copierea pe NAS real prin worker (parola NAS se introduce de utilizator pe instanta supravegheata) si pagina de asteptare in browser: `Teste utilizator/09_Notificari_si_setari.md` (17.10.2026) si `docs/TESTE_RAMASE.md`.

## Actualizare 19.10.2026 - republicare 5087
- 5087 republicat cu build-ul curent (supervisor stop, dotnet publish -o artifacts\notif-build, Start-ScheduledTask): /health/live 200, CSS incarcat (1104 reguli), pagina de login se afiseaza. Corectia paginii de asteptare (redirect inainte de autentificare) este acum in instanta.
- Ramas: pagina de asteptare in browser in timpul unui backup (cere login administrator facut de utilizator) si copierea pe NAS real prin worker.
- 19.10.2026: pagina de asteptare verificata in browser pe 5087 in timpul unui backup (navigare -> /intretinere, 200, fara erori); ramane doar copierea pe NAS real prin worker. Backupurile de proba (3 pachete "La cerere" din 19.10.2026 08:34-08:37) raman in lista de pachete.
- 19.10.2026: copierea pe NAS real prin workerul de backup verificata pe 5087 (pachet nou pe partajare cu hash identic, "Server + NAS", stare reusita). Nu mai ramane nicio verificare de backup/restaurare/NAS prin worker.

## Actualizare 19.10.2026 - sfarsitul listei "ramas din modulele facute"
- Worker OCR: memoria nativa e plafonata printr-un Windows Job Object (`Services/ProcessJob.cs`, `Ocr:MaxProcessMemoryMb`, implicit 4096 MB, 0 = fara plafon) si workerul moare odata cu aplicatia (kill on close). Vf. in suita: un proces care cere 500 MB sub plafon de 200 MB cade, fara plafon reuseste; joburile OCR reale (PDF cu text, scan, inventar) au varful de 296 MB; grupul `ocrworker` 41 PASS.
- Supervizor: declansatorul periodic de 5 minute a fost instalat (`-Action install` pe 5087, la cererea utilizatorului). Verificat: supervizorul omorat la 08:51:00 a fost repornit de sarcina la 08:51:47, aplicatia orfana inlocuita, /health/live 200.
- Defectiunile reale pentru Oferte, Inventar si ANAF si bannerul cu doua module fusesera deja facute la 15.10.2026 (vezi `docs/TESTE_RAMASE.md`, "defectiuni reale pentru Oferte, Inventar si ANAF"); nota "RAMASE (nefacute)" din ea e depasita.
- Ramas din modularizare: doar pasii din `Teste utilizator/` facuti de utilizator.

## Actualizare 19.10.2026 - modulul Situatia proiectului si export consum
- Al doisprezecelea modul, `situatie-proiect` (`Modules/ProjectReports/`, spatiu de nume `BlazorStoc.Modules.ProjectReports`; numele `ProjectSituation` ar fi intrat in conflict cu recordul): situatia proiectului pe componente (pagina, PDF, CSV, lista de achizitie), echipamentele proiectului si exportul de consum. Proiectul, componentele, rezervarile, iesirile si alertele de stoc au ramas in nucleu; `ProjectDeficitSource` ramane in `StockAlerts.cs` si, cu modulul oprit, devine ilizibila (nu inchide notificari). Nomenclatorul, beneficiarii si proiectele nu s-au mutat: le folosesc Oferte, Analizele de risc si stocul.
- Cu modulul oprit: dispar meniul/tile-ul/randurile/linkul spre paginile lui, paginile arata "Modul indisponibil", endpoint-urile de export raspund 503, sugestiile de rezervare raman goale; stocul, proiectele si componentele merg.
- Verificat: suita in memorie 1476 PASS (21 verificari noi in `ProjectReportsModuleChecks`), grupul `modules` 208 PASS, MariaDB: Stock alerts (8), Offer import (13), Project components (10). In browser pe baza de test (5092): modul pornit, oprit din configurare, si defectiune reala (cantitate 99999999999 pe o iesire, OverflowException la export consum): 3 erori scot modulul, restul aplicatiei merge (produse, proiect, notificari), revenire dupa restaurarea datelor, Jurnal "Oprire modul" / "Revenire modul". Datele de proba restaurate (miscarea 1136 are din nou cantitatea 2).
- 19.10.2026 (ramasul verificat): situatia proiectului cu oferta, PDF/CSV si Echipamente verificate in browser; notificarea de deficit verificata cu modulul pornit si oprit (oprit: nu raspunde, nu inchide si nu modifica nimic; pornit: proceseaza schimbarea de termen). Detalii in docs/TESTE_RAMASE.md.

## Actualizare 19.10.2026 - modulul Jurnal de activitate
- Al treisprezecelea modul, `jurnal` (`Modules/Journal/`): paginile /jurnal si /jurnal/complet, bara de filtre si filtrele. Interfata `IAuditTrail` a fost impartita: `IAuditReader` (citirea, folosita doar de cele doua pagini, prin circuit breaker) si `IAuditTrail : IAuditReader` (inregistrarea, ramasa in nucleu; implementarile existente si testele nu s-au schimbat). Jurnalul complet isi inregistreaza exportul prin `IAuditTrail`.
- Verificat: suita in memorie 1491 PASS (15 verificari noi in `JournalModuleChecks`, grupul `modules` 223), MariaDB sectiunile cu "Journal" 198 PASS. In browser pe baza de test (5092): modul pornit, oprit din configurare si cu defectiune reala (rand cu id care nu e GUID: FormatException la citire): erorile repetate scot modulul, paginile spun "Modul indisponibil", linkul si tile-ul dispar, produsele/notificarile/setarile merg, iar Jurnalul continua sa inregistreze ("Oprire modul" scris cu modulul oprit); dupa stergerea randului modulul revine singur. Randul de proba sters.
- Din nucleu nu se mai poate izola nimic fara sa se strice altceva: stocul, utilizatorii/permisiunile, beneficiarii/proiectele si nomenclatorul sunt folosite de mai multe module.

## Actualizare 19.10.2026 - verificare completa a modulelor, supervizor, permisiuni
- Regula noua (`CLAUDE.md`): la sfarsitul unei schimbari se ruleaza toate verificarile necesare, fara sectiuni "Neverificat"/"Ramas". Aplicata acum pe toate modulele: detalii in `docs/TESTE_RAMASE.md` ("Verificare completa a modulelor").
- Corectate: tile-ul Vehicule fara ModuleGate; cache-ul `UserPermissions` (se retine setul tipului, filtrul pe rolul sesiunii se aplica la citire); `supervisor.ps1`: `stop` deschide o fereastra de oprire de 10 minute respectata de sarcina programata (`-Scheduled`), `-Action start` o inchide; sarcina BlazorStocSupervisor-5087 reinstalata cu `-Scheduled`. Publicarea pe 5087: `supervisor.ps1 -Action stop`, `dotnet publish -o artifacts\notif-build`, `supervisor.ps1 -Action start` (nu `Start-ScheduledTask`).
- Verificat: suita in memorie 1492 PASS, MariaDB 2275 PASS (inclusiv "User types", picata de la modularizare), supervizor 36 PASS, in browser toate pasii modulelor. Singura exceptie spusa explicit: restaurarea completa a bazei reale cu pagina de asteptare (decizia utilizatorului).
- 19.10.2026 (dupa acordul utilizatorului): restaurarea completa a bazei reale pe 5087 cu pagina de asteptare vazuta intr-o a doua sesiune a fost rulata si trece (detalii in docs/TESTE_RAMASE.md). Din verificarile de modularizare si de backup/restaurare/NAS nu mai ramane nimic.

## Actualizare 19.10.2026 - modulul Observatii proiect
- Al paisprezecelea modul, `observatii-proiect` (`Modules/ProjectObservations/`): observatiile unui proiect si fisierele lor (pagina unei observatii, editorul, legatura /observatii/{id} din Jurnal, lista si butonul de pe pagina proiectului, descarcarea unui fisier). `IProjectRepository : IProjectObservationRepository` si `IProjectFileStore : IObservationFiles` (aceeasi schema ca la Jurnal): paginile modulului folosesc interfetele inguste, prin circuit breaker; depozitele ramase intregi nu s-au schimbat. Stergerea unui proiect (arhiveaza proiectul + observatiile + fisierele in aceeasi tranzactie) ramane in nucleu si citeste observatiile direct, deci functioneaza si cu modulul oprit.
- Pagina proiectului ramane in nucleu si isi arata proiectul oricand: cu modulul oprit lista de observatii e inlocuita de o nota, butonul „Adauga observatie” dispare, iar o eroare la incarcarea observatiilor da un rand „Observatiile nu au putut fi incarcate” fara sa strice pagina.
- Verificat: suita in memorie 1509 PASS (17 verificari noi in `ProjectObservationsModuleChecks`, grupul `modules` 241), MariaDB 2292 PASS. In browser pe baza de test (5092): modul pornit (adaugare, fisier PDF atasat, descarcare 200 application/pdf, lista pe proiect, editare cu motivare, stergere cu arhivare), defectiune reala (data corupta in `project_observations.created_utc`: dupa 3 incarcari modulul iese din functiune, pagina proiectului si stocul merg, pagina observatiei spune „Modul indisponibil”, descarcarea da 503, Jurnalul are „Oprire modul”; dupa refacerea datei si 30 s revine singur, „Revenire modul”), oprit din configurare (nota pe proiect, 503, `/observatii/{id}` „Modul indisponibil”) si stergerea unui proiect cu observatie + fisier cu modulul oprit (proiectul, observatia si fisierul in arhiva). Datele de proba sterse.
- Din nucleu nu se mai poate izola nimic fara sa se strice altceva (stocul, utilizatorii/permisiunile, beneficiarii/proiectele, nomenclatorul).

## Actualizare 19.10.2026 - oprire/pornire manuala a modulelor
- Setari -> Module: comutator `Pornit` pe fiecare modul (permisiunea `setari-module.toggle`, doar administrator). Oprirea cere motiv, se aplica imediat fara repornire, se salveaza in `module_state` (migrarea 42) si ramane si dupa repornire (`ModuleStateRestorer`). `jurnal` nu se poate opri; un modul care nu a pornit/e oprit din configurare cere repornirea aplicatiei.
- Jurnal: actiunile `Oprire manuala modul` / `Pornire manuala modul` pe numele administratorului (motiv, stare veche/noua, durata opririi).
- Pagina principala: placa modulului oprit ramane, gri si fara link (`ModuleTile`, orice modul oprit, nu doar manual); pagina modulului spune `Modul oprit temporar`; programarea backup asteapta cat modulul Backup e oprit.
- Verificat: suita completa in memorie, sectiunile MariaDB, browser pe instanta de test (vezi IMPLEMENTED.md).
- Help cu capturi (19.10.2026, necomis, branch `feature/help`): 89 de capturi 1920x1080, una pe topic (9 MB), chenar pulsatil la clic pe un rand din tabel, "Arata pe pagina", marire; toate topicurile au captura (lista `WithoutCapture` din `HelpChecks.cs` este goala; fereastra ANAF se fotografiaza cu CUI-ul real RO9178894, cerut de utilizator, si cere internet). Fisiere demo pentru incarcare: `dotnet run --project tests/BlazorStoc.HelpDemo -- --files artifacts/help-demo-files`. Regenerare: vezi antetul `tools/help-capture.mjs` (baza demo + `--prepare` + `--all`); la schimbarea unei ferestre se reruleaza `--topic cheie`. Teste `-Group help`: 61 PASS.

Actualizare 28.10.2026: butoanele de actiune raman cu text (scriptul de iconite a fost scos), reflow la ingustare, popup categorii/subcategorii, card Descriere pe pagina produsului. Urmeaza pe ramura noua: arhivarea parametrilor/etichetelor, categorii cu parametri, mutare cu pastrare, drag&drop pe niveluri (plan etapele 0-8 discutat 28.10.2026).
