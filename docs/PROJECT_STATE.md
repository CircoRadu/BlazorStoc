# Starea curentă a proiectului

Actualizat de: **Claude**
Data: **24 septembrie 2026**
Stare ciclu: **Task 1 („Intrări și ieșiri pentru un produs existent”) și Task 0 finalizate; Task 2 finalizat aproape integral; pregătit pentru predarea către Codex după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari, utilizatori și, nou, proiecte asociate beneficiarilor (cu observații și fișiere), autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse, componenta comună `CollapsibleSection` identificarea produselor prin „Cod produs”, stocul modificabil exclusiv prin mișcări de intrare/ieșire (pagina `/produse/{id}/miscari`) și istoricul mișcărilor.

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude. Următorul ciclu îi este predat lui Codex.

## Ultimele modificări funcționale (ciclul Claude — Task 1)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 1”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`, ca în ciclurile anterioare.

- **Pagina** `Components/Pages/ProductMovements.razor` (`/produse/{id}/miscari`): panou „Produs” (categorie, subcategorie, cod, imagine cu mărire, Editare/Șterge produs), formular de mișcare (dată, Intrare/Ieșire, beneficiar/proiect doar la ieșire prin `Components/Shared/RelationPicker.razor`, descriere, cantitate), tabel cu filtru/sortare/paginare, „Total produse în stoc”, dialoguri de editare, ștergere (`DeleteConfirmationDialog`) și istoric (click dreapta sau buton). `StockMovementRedirect.razor` (`/miscari/{id}`) rezolvă evenimentele din jurnal către pagina produsului. Catalogul (`Home.razor`): codul produsului și „↗” navighează către pagină, panoul de detalii a dispărut, iar `?sterge=<id>` deschide dialogul de ștergere existent.
- **Domeniu**: `Services/StockMovements.cs` (`StockMovement`, `StockMovementInput`, `StockMovementRules`, `IStockMovementRepository`, `ProjectStockMovement`). Cantitate 1–100.000, dată 1990–2100 (fără altă limită; poate fi în viitor), descriere obligatorie (max. 500), beneficiar/proiect numai la ieșire, proiect al beneficiarului ales. Stocul nu se blochează la ieșire peste stoc.
- **Persistență SQLite** (`Services/SqliteStockMovementRepository.cs`, schema versiunea 6): `stock_movements` primește `kind`, `movement_date`, `description`, `operator`, `version`, `updated_utc` (migrare prin `EnsureColumn` pentru baze existente), plus `stock_movement_history` și `archive_stock_movements`. Stocul produsului se actualizează atomic (`quantity = quantity + delta`) în aceeași tranzacție serializabilă cu mișcarea, istoricul și auditul, fără a incrementa `products.version`. Migrare unică (marker `stock_movements_baseline`): produsele cu stoc și fără mișcări primesc o mișcare „Stoc initial”.
- **Persistență MariaDB** (`Services/MariaStockMovementRepository.cs`, arhivă schema versiunea 4): mapare pe `io`/`io_history` (`io_tip_actiune` 1 = intrare, `id_beneficiar` 0 = niciunul, `io_data` = `dd-MM-yyyy`), coloane adăugate la prima folosire: `id_project`, `io_versiune`, `io_created_utc`, `io_updated_utc`; `io_numar_bucati` (tinyint) este lărgit la INT. Operatorul mișcării este utilizatorul bazei (`Database:ApplicationUserId`), nu contul web. Ștergerea unui proiect verifică acum `io.id_project`. **Netestat pe un server real.**
- **Arhivare și audit**: entitate nouă `MiscareStoc` (`ArchiveSchemaRegistry`, `ArchiveRequests.StockMovement`, mapări SQLite/MariaDB); istoricul modificărilor se arhivează ca relații `IstoricMiscareStoc`. Audit: adăugare/editare/ștergere cu ținta = codul produsului, `EntityId` = id-ul mișcării, motiv la editare/ștergere; `AuditNavigation.EditUrl` → `/miscari/{id}`.
- **`IProductRepository.GetProductAsync(id)`** adăugat (demo, SQLite, MariaDB).
- **Contract pentru alte taskuri**: `IStockMovementRepository.GetForProjectAsync(projectId)` returnează ieșirile proiectului (id, produs, cod produs, cantitate, dată, operator) — Task 2 / Subtask 2.4 poate citi „Echipamente” din el. Pentru Task 8 contractul de modificare este evenimentul de audit `MiscareStoc`; nu există un canal separat.

## Ultimele modificări funcționale (ciclul Claude — Task 0)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 0”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`, ca în ciclul anterior.

- **Stocul nu mai poate fi introdus manual.** `ProductInput.Quantity` a fost eliminat. `CreateAsync` creează produsul cu stoc 0 în toate repository-urile (`DemoProductRepository.Crud.cs`, `SqliteProductRepository.cs`, `MariaProductRepository.Crud.cs`); `UpdateAsync` nu mai scrie `quantity` / `produs_cantitate` și returnează produsul cu stocul curent din bază.
- **Editorul de produs** (`Components/Pages/ProductEditor.razor`): câmpul „Stoc inițial”/„Cantitate” a fost înlocuit cu o notă: la creare „Produsul nou este creat cu stoc 0…”, la editare „Stoc curent: N buc.” (doar afișare).
- **Concurență**: `ProductRules.CheckCurrent` compară produsul fără `Quantity`, deci o mișcare de stoc viitoare (Task 1) nu invalidează o editare deschisă. Consecință: ștergerea verifică acum stocul **curent** din bază (`ProductRules.CheckDelete(current, …)` în SQLite și demo; MariaDB o făcea deja), nu instantaneul formularului.
- **Audit**: `ProductCode.AuditIdentification` și `AuditChanges` nu mai includ „Cantitate”. Instantaneul de arhivă al produsului păstrează cantitatea ca dată istorică. Jurnalul din MariaDB (`log`, JSON înainte/după) serializează în continuare întregul produs, inclusiv cantitatea.
- **Neschimbat intenționat**: produsele existente își păstrează stocul; catalogul, filtrele de stoc și `SqliteLocalStore` (reconcilierea unică a stării vechi, care citește „Cantitate” din audit istoric) nu au fost modificate. Nu s-au generat mișcări și nu s-au modificat date.
- **Preview**: nu rula niciun proces pe 5082; a fost pornit din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082` (cont `Alex`, deci Codex sau Claude îl pot opri fără probleme de acces).
- **Documentație**: `README.md`, `VALIDARE.md`, `TODO.md` (Task 0 mutat în „Taskuri finalizate”; Task 1 = „Intrări și ieșiri pentru un produs existent”, dependențele actualizate).

## Ultimele modificări funcționale (ciclul Claude — Task 2, restul subtaskurilor)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează ce a mai rămas din task 2”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`.

- **Persistență**: `Services/SqliteProjectRepository.cs`, `Services/SqliteProjectFileStore.cs`, `Services/MariaProjectRepository.cs`, `Services/MariaProjectFileStore.cs`. Schema SQLite (versiunea 5) adaugă `projects`, `project_observations`, `project_observation_files`, `archive_projects`, `archive_project_observations`, `archive_project_observation_files` și `stock_movements.project_id` (migrare pentru baze existente). Schema MariaDB (arhivă versiunea 3) adaugă tabelele echivalente de arhivă; tabelele live `project`/`project_observation`/`project_observation_file` sunt create de aplicație la prima folosire.
- **Arhivare generalizată**: `ArchivePersistence.InsertAsync` acceptă acum o listă de fișiere per operație de arhivă (nu doar unul singur), necesar pentru arhivarea în cascadă a unui proiect cu mai multe observații/fișiere într-o singură operație. Toate apelurile existente (produs, beneficiar, utilizator) au fost adaptate.
- **Interfață**: `Components/Pages/BeneficiaryDetail.razor` (`/beneficiari/{id}`), `ProjectEditor.razor`, `ProjectPage.razor` (`/proiecte/{id}`), `ProjectEquipment.razor` (`/proiecte/{id}/echipamente`, stare goală), `ProjectObservationEditor.razor`, `ProjectObservationPage.razor` (`/proiecte/{projectId}/observatii/{id}`, cu încărcare/descărcare/eliminare fișiere). `Beneficiaries.razor`: numele beneficiarului este acum link către pagina de detaliu.
- **Endpoint fișiere**: `/media/project-files/{fileId}`, autorizat, cu verificare de conținut (semnătură binară, nu doar extensie) și limite de dimensiune/număr.
- **Audit și jurnal**: `AuditEntities.Project`/`ProjectObservation`/`ProjectObservationFile`; `AuditNavigation.EditUrl` leagă evenimentele de proiect de pagina `/proiecte/{id}`.
- **Corecție**: `DeleteConfirmationRules.DefaultReason` genera mereu forma masculină „…nu va mai fi folosit”, greșită pentru subiecte feminine ca „Observația”. Corectat cu acord de gen simplu (subiect terminat în „a” → „folosită”); depistat prin testarea manuală în browser a fluxului de ștergere a observației.
- **Fișiere renumite** pentru a evita coliziunea de nume cu entitățile de domeniu (Blazor generează o clasă cu numele fișierului): `Project.razor` → `ProjectPage.razor`, `ProjectObservation.razor` → `ProjectObservationPage.razor`.

## Decizii și limitări

- Nivelul de autorizare pentru proiecte/observații/fișiere reutilizează `EnsureBeneficiaryOperatorAsync`, consecvent cu regula existentă pentru beneficiari.
- Registrul de rute al jurnalului (`AuditNavigation.EditUrl`) leagă doar proiectele de pagina lor; observațiile rămân cu țintă text (necesită un identificator compus proiect+observație, netratat încă generic).
- Restaurarea paginii/filtrului/poziției la revenirea din pagina proiectului nu este implementată.
- Pagina „Echipamente” este doar stare goală; citirea efectivă a mișcărilor de stoc așteaptă modulul de intrări/ieșiri.
- MariaDB: tabelele proiectelor nu au fost testate pe un server real. Coloana `io.id_project` (contractul mișcare–proiect) este amânată până la implementarea modulului de stoc; până atunci, ștergerea unui proiect pe MariaDB nu verifică mișcări de stoc asociate (SQLite o face, prin `stock_movements.project_id`).
- Sincronizarea cu Task 8 (evenimente de modificare, păstrarea formularelor deschise) este explicit amânată, conform TODO.
- Fișierele acceptate: imagini, PDF, Office, text/CSV, verificate prin semnătura conținutului unde există una binară fiabilă; tipurile fără semnătură (Office/CSV/text) cad pe verificarea tipului declarat, dacă acesta e deja pe lista acceptată.

## Validare

- Build Release: 0 avertismente, 0 erori (proiect principal și `BlazorStoc.Checks`).
- Task 1: `BlazorStoc.Checks` — 285 de verificări trecute (234 dinainte; 51 noi: domeniu, stoc atomic cu 8 adăugări simultane, editare/ștergere/istoric, arhivare, audit, ordonare/filtrare/paginare, `GetForProject`, migrarea SQLite, baseline). Browser (5082, desktop, 375 și 768 px): adăugare, ieșire cu beneficiar, editare cu motiv, `[*]`, istoric prin click dreapta, ștergere în doi pași, jurnal, `/miscari/<id>`, `/produse?sterge=<id>`. Neexersate manual: ieșire cu proiect, filtru/sortare/paginare în browser. MariaDB neverificat pe un server real. Preview-ul a fost oprit și repornit de mine (cont `Alex`) pentru fiecare build; datele demo au primit mișcări (baseline „Stoc initial” pentru produsele cu stoc; o mișcare de test a fost adăugată și ștearsă în arhivă).
- `BlazorStoc.Checks`: 234 de verificări trecute (229 dinainte de Task 0; +5 net: produs nou cu stoc 0, editare care păstrează stocul, diferență de stoc în instantaneu, `CheckCurrent`, comportamentul SQLite; eliminată verificarea „stoc inițial negativ”).
- Task 0, browser (`http://127.0.0.1:5082`, sesiune existentă): formularul de creare fără „Stoc inițial”, editorul cu „Stoc curent: 120 buc.” fără câmp de introducere. Salvarea editării și jurnalul nu au fost exersate manual (nu s-au modificat datele demo); sunt acoperite de verificările automate. MariaDB neverificat pe un server real.
- Task 2 (istoric): 176 de verificări dinaintea lui + 29 pentru modelul de domeniu al proiectelor + 24 pentru persistență/pagini/arhivare/corecția de gramatică.
- Browser, `http://127.0.0.1:5082` (admin demo, sesiune existentă): creare proiect din pagina beneficiarului, pagina proiectului cu „Echipamente” primul, adăugare observație cu navigare automată, ștergere observație și ștergere proiect (ambele cu dialogul de confirmare în doi pași), verificare jurnal (evenimente „Proiect”/„Observatie” cu `Details`/`Motif` corecte, fără diacritice în motiv, conform regulii de stocare). Datele de test au fost curățate (proiectul creat pentru verificare a fost șters).
- Preview-ul a fost repornit din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082` de către Claude, care a putut opri de această dată procesul anterior (proprietar același cont local, nu contul sandbox Codex menționat în ciclurile trecute).

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse, beneficiari și proiecte, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Codex**. Dacă utilizatorul nu stabilește altă prioritate, următorul element recomandat este Task 2 / **Subtask 2.4 — Pagina „Echipamente” citește mișcările de stoc** din `TODO.md` (deblocat: `IStockMovementRepository.GetForProjectAsync` există). Alternativ: Task 2 / **Subtask 2.3 — Restaurarea contextului de navigare și registrul jurnalului** din `TODO.md` (cele mai mici bucăți rămase din Task 2), sau Task 4 („Identificarea beneficiarului cu CUI duplicat”), care nu mai depinde de nimic neterminat.

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice, inclusiv secțiunile „Cod produs” și „Proiecte”.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
