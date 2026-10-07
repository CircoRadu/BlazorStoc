# Jurnalul colaborării Codex–Claude

Acest fișier este append-only. Intrările noi sunt adăugate automat de `tools/agent-cycle.ps1 finish`.


> Intrarile mai vechi sunt in `docs/arhiva/AGENT_CHANGELOG_pana_la_06.10.2026.md` (istoric Codex-Claude).

## 2026-09-29T06:08:42.0000000Z — claude

- **Task:** Corectarea detectiei liniilor de tabel pe o scanare reala usor inclinata (gasit in timpul verificarii ferestrei de comparare PDF de mai sus)
- **Rezumat:** Utilizatorul a incercat preluarea unei scanari reale a formularului de inventar si a primit eroarea "Nu a fost gasit niciun tabel recunoscut", desi fisierul contine vizibil toate cele 8 tabele completate. Investigat prin instrumentare directa (harness de unica folosinta, in afara proiectului, `INVENTORY_OCR_DEBUG=1`): `InventoryPickupOcrService.FindHorizontalLines` cerea 65% cerneala pe un singur rand de pixeli; pe aceasta scanare cel mai plin rand atingea doar 58%, pentru ca sub jumatate de grad de inclinare a colii imprastie cerneala unei linii drepte pe aproximativ 10 randuri, niciunul singur atingand pragul - deci zero linii, zero tabele detectate pe toata pagina. Corectat cu o dilatare verticala mica (nucleu 1x7, `LineDetectionDilationHeight`) aplicata doar pentru acest test, pe o copie a imaginii binare; pragul de 65% ramane neschimbat. Mutata si linia de log de depanare inaintea intoarcerii timpurii, ca sa arate geometria calculata mereu, nu doar la succes. Fisierul real al utilizatorului adaugat ca fixtura noua (`tests/BlazorStoc.Checks/Fixtures/inventar-proba-inclinata.pdf`) cu doua verificari noi. Protocolul „commit doar la cerere” (intrarea anterioara) se aplica si aici: modificarile raman necomise pana la cererea explicita a utilizatorului.
- **Fișiere modificate:**
  - `Services/InventoryPickupOcr.cs`
  - `tests/BlazorStoc.Checks/Program.cs`
  - `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`
  - `tests/BlazorStoc.Checks/Fixtures/inventar-proba-inclinata.pdf` (nou)
  - `IMPLEMENTED.md`
  - `VALIDARE.md`
  - `docs/PROJECT_STATE.md`
  - `docs/TESTE_RAMASE.md`
  - `docs/AGENT_CHANGELOG.md`
- **Validare:** cauza reala confirmata prin masurare directa a imaginii; build Release 0 avertismente/erori; `tests/BlazorStoc.Checks` 611/611 `PASS` (609 anterioare + 2 noi, fara regresii); pipeline complet rulat pe fisierul real: toate cele 10 produse citite, doua valori ramase gresite de modelul de cifre (limitare deja cunoscuta, vezi mai jos).
- **Neverificat:** aspectul general "alte scanere/rezolutii" (H1, restul); modelul de cifre a citit gresit doua valori scrise de mana pe aceasta scanare (una semnalata corect ca nesigura, una nu - caz nou adaugat la H2, limitare deja cunoscuta, nu o regresie a acestei corectii).
- **Commit:** neefectuat inca (protocol nou: commit doar la cererea explicita a utilizatorului)
- **Predat catre:** claude

## 2026-09-29T06:15:16.0000000Z — claude

- **Task:** Indreptarea (deskew) reala a paginii pentru scanari cu inclinare vizibila (a doua scanare reala a utilizatorului, rotita cu cateva grade, respinsa dupa corectia cu dilatare de mai sus)
- **Rezumat:** Utilizatorul a furnizat o a doua scanare reala, rotita vizibil (nu doar sub un grad ca prima), tot respinsa cu "Nu a fost gasit niciun tabel recunoscut" - dilatarea din corectia anterioara nu acopera un unghi asa de mare. Adaugat `InventoryPickupOcrService.FindSkewDegrees`: estimeaza unghiul de inclinare prin profilul de proiectie orizontala (metoda standard - varianta e maxima la unghiul corect), cautat intre -8 si +8 grade in doi pasi (grosier din grad in grad, apoi rafinat din zecime in zecime), pe o copie miniaturizata (25%) pentru viteza; pagina e rotita (`Rotate`, `Cv2.WarpAffine`) cu unghiul gasit inainte de restul geometriei. Rulat independent pentru fiecare pagina a documentului (utilizatorul a intrebat explicit despre acest aspect - `ScanAsync` apeleaza deja `ScanPageAsync` separat per pagina, iar estimarea/rotirea ruleaza in interiorul ei, deci fiecare pagina isi are propriul unghi, fara nicio schimbare de cod necesara). **Descoperire importanta**: rotirea intregii pagini, chiar la un unghi mic (~0,3 grade, cazul deja rezolvat de dilatare), a inrautatit acuratetea cifrelor scrise de mana (interpolarea rotatiei inmoaie traseul subtire al cernelii) - confirmat comparativ pe acelasi fisier, nu presupus. Corectat cu un prag minim de rotire (`MinCorrectedSkewDegrees = 0.6` grade); sub prag ramane activa doar dilatarea. Fisierul real mai vizibil rotit adaugat ca fixtura noua (`tests/BlazorStoc.Checks/Fixtures/inventar-proba-rotita.pdf`), cu doua verificari noi.
- **Fișiere modificate:**
  - `Services/InventoryPickupOcr.cs`
  - `tests/BlazorStoc.Checks/Program.cs`
  - `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`
  - `tests/BlazorStoc.Checks/Fixtures/inventar-proba-rotita.pdf` (nou)
  - `IMPLEMENTED.md`
  - `VALIDARE.md`
  - `docs/PROJECT_STATE.md`
  - `docs/TESTE_RAMASE.md`
  - `docs/AGENT_CHANGELOG.md`
- **Validare:** cauza si efectul secundar (rotirea strica cifrele la unghiuri mici) confirmate prin executie reala comparativa pe ambele fisiere ale utilizatorului, nu presupuse; build Release 0 avertismente/erori; `tests/BlazorStoc.Checks` 613/613 `PASS` (611 anterioare + 2 noi, fara regresii pe niciuna dintre cele 3 fixturi reale); pipeline complet rulat pe ambele fisiere reale: toate cele 10 produse citite din fiecare.
- **Neverificat:** unghiuri peste 8 grade sau alta orientare (90/180 grade); un document cu mai multe pagini, fiecare cu propria inclinare diferita, nu a fost testat efectiv cu un fisier real (doar rationat din cod - fiecare pagina isi calculeaza propriul unghi independent). Aspectul general "alte scanere/rezolutii" (H1) si acuratetea modelului de cifre (H2) raman deschise.
- **Commit:** neefectuat inca (protocol: commit doar la cererea explicita a utilizatorului)
- **Predat catre:** claude

## 2026-09-29T06:39:48.0000000Z — claude

- **Task:** Fixtura de test multi-pagina pentru deskew (fiecare pagina cu inclinarea ei) si populare catalog demonstrativ cu produse fictive (minimum 4 per subcategorie)
- **Rezumat:** Utilizatorul a intrebat daca deskew-ul (corectia anterioara) ruleaza per pagina - confirmat ca da (`ScanAsync` apeleaza `ScanPageAsync` separat pentru fiecare pagina), fara nicio schimbare de cod necesara, si adaugata o fixtura noua (`inventar-proba-multipagina.pdf`, cele doua scanari reale existente combinate cu `PdfSharp`) cu verificari care blocheaza explicit o eventuala regresie viitoare. Separat, utilizatorul a cerut popularea catalogului demonstrativ (11 produse, 1-2 per subcategorie) cu produse fictive, minimum 4 per subcategorie: adaugate 22 de produse noi (sufix " test") prin fluxul real al aplicatiei (`SqliteProductRepository.CreateAsync`, cu o miscare de intrare pentru stocul initial prin `SqliteStockMovementRepository.CreateAsync`), nu prin scriere directa in baza - normalizare, unicitate si jurnalizare identice cu o adaugare din interfata. Baza de date reala (`data/blazorstoc-local.db`) copiata inainte de scriere; preview-ul oprit pe durata operatiei.
- **Fișiere modificate:**
  - `tests/BlazorStoc.Checks/Fixtures/inventar-proba-multipagina.pdf` (nou)
  - `tests/BlazorStoc.Checks/Program.cs`
  - `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`
  - `data/blazorstoc-local.db` (date, nu cod - 22 produse noi + miscarile de stoc aferente)
  - `IMPLEMENTED.md`
  - `VALIDARE.md`
  - `docs/PROJECT_STATE.md`
  - `docs/AGENT_CHANGELOG.md`
- **Validare:** `tests/BlazorStoc.Checks` 616/616 `PASS` (613 anterioare + 3 noi); pipeline OCR rulat direct pe fixtura multi-pagina (unghiuri 0,30 si 2,70 grade estimate separat, toate cele 20 de randuri citite); catalogul verificat vizual in browser (`/produse`, 33 de produse, 5 categorii).
- **Neverificat:** produsele fictive nu au fost inca folosite pentru a genera o situatie de inventar multi-pagina reala (util pentru Task 4).
- **Commit:** neefectuat inca (protocol: commit doar la cererea explicita a utilizatorului)
- **Predat catre:** claude

## 2026-09-29T10:30:00.0000000Z — claude

- **Task:** Task 3, subtaskurile 3.3-3.4 (declansarea si pasii de restaurare a bazei de date); tile "Vehicule" pe pagina principala; drag and drop la incarcarea fisierului de preluare inventar
- **Rezumat:** Trei cereri separate ale utilizatorului, in aceeasi sesiune. (1) Task 3.3/3.4: butonul "Restaureaza baza de date" (activ numai cu un pachet selectat), popup nou de confirmare (`RestoreConfirmationDialog`, cuvantul exact `confirma`) si cei 5 pasi de restaurare (`Services/DatabaseRestore.cs`) - blocare+snapshot pre-restaurare, verificare hash, comparare structura, comparare continut (identic = refuzat ca inutil), import+comutare. Mod demonstrativ (SQLite) verificat integral prin executie reala (restaurare efectiva confirmata in browser, catalogul de produse intact). Mod MariaDB real: cod complet scris, dar Pasul 4 (comutare de scheme) se opreste explicit cu mesaj clar in lipsa unui cont dedicat cu drepturi cross-schema, neconfigurat in acest mediu; neverificat, fara instanta locala MariaDB (vezi `docs/TESTE_RAMASE.md`, A20). Adaugat si un banner de intretinere in `MainLayout.razor` (verificare sincrona a lacatului comun la fiecare navigare, nu un timer de fundal - `MainLayout` nu are propriul `@rendermode` interactiv, un `PeriodicTimer` pornit acolo a fost incercat si nu functiona, vezi `docs/PROJECT_STATE.md`). (2) Adaugat un tile "Vehicule" pe pagina principala (`Dashboard.razor`), lipsea din grila de navigare desi pagina `/vehicule` exista deja. (3) Drag and drop la `/inventar/preluare`: fisierul poate fi tras direct peste zona de continut, nu doar ales prin buton (`wwwroot/inventory-pickup-dropzone.js`, script delegat pe `document` care atribuie fisierul campului `<InputFile>` ascuns si declanseaza `change`, refolosind neschimbat fluxul C# existent).
- **Fișiere modificate:**
  - `Services/DatabaseRestore.cs` (nou), `Services/RestoreConfirmation.cs` (nou), `Components/Shared/RestoreConfirmationDialog.razor` (nou)
  - `Services/DatabaseBackup.cs` (parametru `existingLock` pe `CreateBackupAsync`), `Services/AuditTrail.cs` (actiunea `Restore`), `Services/MariaTimeText.cs` (`MariaClientExecutable`)
  - `Components/Pages/DatabaseRestore.razor`, `Components/Layout/MainLayout.razor`, `Program.cs`
  - `Components/Pages/Dashboard.razor` (tile Vehicule)
  - `wwwroot/inventory-pickup-dropzone.js` (nou), `Components/Pages/InventoryPickup.razor`, `Components/App.razor`, `wwwroot/app.css`
  - `tests/BlazorStoc.Checks/Program.cs` (verificari noi pentru restaurare si `RestoreConfirmationRules`)
  - `TODO.md`, `VALIDARE.md`, `README.md`, `docs/PROJECT_STATE.md`, `docs/TESTE_RAMASE.md`, `docs/AGENT_CHANGELOG.md`
- **Validare:** `tests/BlazorStoc.Checks` 630/630 `PASS` (inclusiv un test nou end-to-end pentru restaurarea SQLite); build Debug 0 avertismente/erori; verificat direct in browser (preview demonstrativ, port 5083): restaurare reala reusita cu popup de confirmare, tile Vehicule functional, drag and drop simulat cu evenimente native (`DataTransfer`/`File` prin JavaScript) trecand prin aceeasi validare ca butonul de incarcare.
- **Neverificat:** modul MariaDB real pentru restaurare (Pasii 1-4, fara instanta locala in acest mediu - `docs/TESTE_RAMASE.md` A20); o tragere reala dintr-un manager de fisiere al sistemului de operare pentru drag and drop (doar simulata prin JS - A21); scrierile obisnuite ale altor sesiuni nu sunt blocate activ in timpul restaurarii (doar notificate, risc rezidual acceptat).
- **Commit:** `claude: restaurare baza de date (3.3-3.4), tile Vehicule, drag and drop preluare inventar`
- **Predat catre:** claude

## 2026-09-30T10:09:59.7760809Z â€” claude

- **Task:** Eliminare SQLite, teste MariaDB, notificari de expirare
- **Rezumat:** Eliminarea completa a SQLite si a modului demonstrativ (aplicatia lucreaza numai pe MariaDB); acoperirea de teste refacuta pe MariaDB (MariaExtendedChecks) cu reluarea tranzactiilor la deadlock; sistem de notificari de expirare (registru de surse, sabloane in Setari, motor, pagina Notificari cu triunghi rosu, preluare si amanare, jurnal cu operatiile specifice si Notificare creata); Docker permis in dezvoltare. Pe baza reala migrarea 4 este aplicata.
- **FiÈ™iere modificate:**
  - `.collaboration/state.json`
  - `.gitignore`
  - `AGENTS.md`
  - `appsettings.json`
  - `BlazorStoc.csproj`
  - `CLAUDE.md`
  - `Components/App.razor`
  - `Components/Layout/MainLayout.razor`
  - `Components/Pages/Audit.razor`
  - `Components/Pages/Beneficiaries.razor`
  - `Components/Pages/Home.razor`
  - `Components/Pages/Notifications.razor`
  - `Components/Pages/ProductEditor.razor`
  - `Components/Pages/Settings.razor`
  - `Components/Pages/Users.razor`
  - `Components/Shared/NotificationTemplatesEditor.razor`
  - `docs/AGENT_CHANGELOG.md`
  - `docs/PROJECT_STATE.md`
  - `docs/PROPUNERE_HARTA_MENTENANTA.md`
  - `docs/TESTE_RAMASE.md`
  - `IMPLEMENTED.md`
  - `Pages/Account/Login.cshtml`
  - `Pages/Account/Login.cshtml.cs`
  - `Program.cs`
  - `README.md`
  - `Services/ArchivePersistence.cs`
  - `Services/Archiving.cs`
  - `Services/AuditTrail.cs`
  - `Services/ChangeEvents.cs`
  - `Services/DatabaseBackup.cs`
  - `Services/DatabaseRestore.cs`
  - `Services/ExpiryNotifications.cs`
  - `Services/MaintenanceGate.cs`
  - `Services/MariaArchiveSchema.cs`
  - `Services/MariaBeneficiaryRepository.cs`
  - `Services/MariaExpiryNotificationRepository.cs`
  - `Services/MariaProductRepository.Crud.cs`
  - `Services/MariaProjectRepository.cs`
  - `Services/MariaSchemaMigrations.cs`
  - `Services/MariaStockMovementRepository.cs`
  - `Services/MariaTransactions.cs`
  - `Services/MariaUserRepository.cs`
  - `Services/MariaVehicleRepository.cs`
  - `Services/MariaWorkPointRepository.cs`
  - `Services/ProductLocks.cs`
  - `Services/Products.cs`
  - `Services/RepositoryAudit.cs`
  - `Services/SqliteAuditTrail.cs`
  - `Services/SqliteBeneficiaryRepository.cs`
  - `Services/SqliteLocalStore.cs`
  - `Services/SqliteProductGroups.cs`
  - `Services/SqliteProductImageStore.cs`
  - `Services/SqliteProductRepository.cs`
  - `Services/SqliteProjectFileStore.cs`
  - `Services/SqliteProjectRepository.cs`
  - `Services/SqliteStockMovementRepository.cs`
  - `Services/SqliteUserRepository.cs`
  - `Services/SqliteVehicleRepository.cs`
  - `Services/SqliteWorkPointRepository.cs`
  - `Services/Vehicles.cs`
  - `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`
  - `tests/BlazorStoc.Checks/MariaExtendedChecks.cs`
  - `tests/BlazorStoc.Checks/Program.cs`
  - `TODO.md`
  - `VALIDARE.md`
  - `wwwroot/app.css`
  - `wwwroot/notification-watch.js`
- **Validare:** tests/BlazorStoc.Checks cu RUN_MARIA_INTEGRATION_CHECKS=1 pe blazorstoc_test: 570 PASS, 0 esecuri, rulari consecutive repetate; verificat in browser pe baza de test (sablon, notificare, popup, amanare, jurnal).
- **Commit:** `claude: eliminare SQLite, teste pe MariaDB, notificari de expirare

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`
- **Predat cÄƒtre:** claude

## 2026-10-06T07:20:00.0000000Z - claude

- **Rezumat:** (1) categorii: anularea dupa mutarea unei subcategorii inchide formularul (re-randare la `DiscardEditAsync`), categoria se alege direct in formular; (2) motor facturi: Emproium (C.U.|. = C.U.I., „Total" singur sub coloana de valoare devine total fara TVA, CUI fara parte la facturi fara antet de furnizor), moneda taiata din campurile numerice, verificarea cu sabloane incrucisate doar pe acelasi furnizor, toate coloanele gasite pornesc ca folosite; (3) preluare factura: avertizare fara sablon, creare sablon in fereastra pe fisierul deja citit, aplicare dupa salvare; (4) sabloanele de facturi pot fi create si modificate de orice utilizator cu acces la produse (stergerea ramane a administratorului), Setari deschis si utilizatorilor obisnuiti cu tabul Facturi; (5) fara casete de bifat: `ToggleSwitch` On/Off peste tot, `RowSelect` si clic pe rand pentru selectia pe randuri, butoane cu stare apasata pentru vizibilitatea straturilor.
- **Fisiere:** `Components/Shared/ToggleSwitch.razor`, `Components/Shared/RowSelect.razor` (noi); `Components/Pages/InvoicePickup.razor`, `Inventory.razor`, `InventoryPickup.razor`, `ProductGroups.razor`, `Settings.razor`, `UserEditor.razor`, `ServiceContract*.razor`, `ServiceMaintenance.razor`; `Components/Shared/InvoiceTemplateWorkbench.razor`, `InvoiceTemplatesList.razor`, `AnafConfigurator.razor`, `ExitDestinationPicker.razor`, `MaintenanceMapView.razor`, `NotificationTemplatesEditor.razor`; `Components/Layout/MainLayout.razor`; `Services/Invoices/InvoiceAnalyzer.cs`, `InvoiceFieldFinder.cs`, `InvoiceTemplateStore.cs`, `InvoiceTemplates.cs`, `InvoiceValues.cs`, `InvoiceAnalysisService.cs`; `wwwroot/app.css`; `wwwroot/checkbox-indeterminate.js` (sters); teste `InvoiceChecks.cs`, `PickupWizardChecks.cs`, `ProductGroupsChecks.cs`, `Program.cs`; `CLAUDE.md`, `README.md`, `TODO.md`, `IMPLEMENTED.md`, `VALIDARE.md`, `docs/PROJECT_STATE.md`, `docs/TESTE_RAMASE.md`.
- **Validare:** suita completa 837 de verificari trec cu cele 5 facturi reale (`INVOICE_CORPUS_DIR`); browser pe `905550.pdf` (avertizare, creare, salvare, aplicare, detectare automata, lista din Setari) si pe Inventar.
- **Neverificat:** contul „Utilizator" in browser, `ProductMovements` (acelasi tipar de re-randare), comutatoarele pe telefon; vezi `docs/TESTE_RAMASE.md` N46.
- **Commit:** `claude: sabloane de facturi pentru utilizatori obisnuiti, creare in fereastra la preluare, comutatoare On/Off, corectii motor facturi, categorii`
- **Predat catre:** claude

## 2026-10-06T08:30:00.0000000Z - claude

- **Rezumat:** `ProductMovements`: avertizarea de modificari nesalvate apare peste dialogul de editare (`unsaved-overlay`, z-index 70) si dialogul se inchide la „Paraseste editarea" (re-randare in `DiscardEditAsync`/`DiscardAddAsync`); Task 6 nou in `TODO.md` (robustete la erori); regula „Economie de tokeni" in `CLAUDE.md`.
- **Fisiere:** `Components/Pages/ProductMovements.razor`, `Components/Shared/UnsavedChangesDialog.razor`, `wwwroot/app.css`, `CLAUDE.md`, `TODO.md`, `IMPLEMENTED.md`, `docs/PROJECT_STATE.md`, `docs/TESTE_RAMASE.md`.
- **Validare:** teste de componente trec; verificat in browser (preview 5087).
- **Commit:** `claude: miscari de stoc - avertizarea de modificari nesalvate peste dialog, inchiderea la parasire; task robustete; reguli de economie de tokeni`
- **Predat catre:** claude

## 2026-10-06T09:00:00.0000000Z - claude

- **Rezumat:** valoarea unui camp citit la dreapta etichetei se opreste la bara verticala („Moneda: RON | Pagina 1 din 1" = „RON"); `WordsRightOf` in `Services/Invoices/InvoiceTemplates.cs`, test in `tests/BlazorStoc.Checks/InvoiceChecks.cs`.
- **Validare:** suita completa 899 de verificari trec; neverificat in browser.
- **Commit:** `claude: valoarea campului citit la dreapta etichetei se opreste la bara verticala`
- **Predat catre:** claude

## 2026-10-07T10:40:00.0000000Z - claude

- **Task:** Pagina Facturi (consultare, corectare, stergere, popup cu produse) si protocol de lucru cu context redus
- **Rezumat:** `/facturi` in Administrare si pe pagina principala; `ISupplierInvoiceRepository` cu `GetAllAsync`/`UpdateAsync`/`DeleteAsync`; actiuni noi de jurnal; istoricul vechi mutat in `docs/arhiva/`, `docs/PROJECT_STATE.md` rescris compact, `docs/HARTA_COD.md`, `tools/run-checks.ps1`, `tools/log-task.ps1`, `CHECKS_ONLY` in teste, `agent-cycle.ps1 start` fara cerinta de tree curat.
- **Teste:** suita in memorie 913 trecute; MariaDB `blazorstoc_test`, sectiunea „Suppliers and invoices”, trecuta; popup verificat de utilizator.

## 2026-10-07T15:40:00.0000000Z - claude

- **Task:** Stoc in bucati, iesiri/retur/storno, oferte, componente, situatia proiectului, rezervari
- **Rezumat:** migrarile 19-27 (cauza iesirii, operatia de iesire, storno/retur, nomenclator, componente, sabloane de oferte, oferte, iesiri legate de componente, rezervari); pagini `/iesiri/multipla`, `/nomenclator`, `/oferte/*`, `/proiecte/{id}/situatie`; actiuni noi de jurnal; tabelele migrarilor 22-27 adaugate in lista de backup/restore; directorul `Teste utilizator/` cu „Ce s-a adaugat" / „Ce face acum".
- **Teste:** suita in memorie 935 trecute; suita MariaDB 1505 trecute, 0 esecuri; neverificat in browser (vezi `docs/TESTE_RAMASE.md`).

