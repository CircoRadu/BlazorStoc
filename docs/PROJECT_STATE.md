# Starea curentă a proiectului

Actualizat de: **Claude**
Data: **24 septembrie 2026**
Stare ciclu: **Task 2 („Proiecte asociate beneficiarilor”) finalizat aproape integral; pregătit pentru predarea următorului ciclu către Codex după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari, utilizatori și, nou, proiecte asociate beneficiarilor (cu observații și fișiere), autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse, componenta comună `CollapsibleSection` și identificarea produselor prin „Cod produs”.

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude. Următorul ciclu îi este predat lui Codex.

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
- `BlazorStoc.Checks`: 229 verificări trecute (176 de dinaintea Task 2 + 29 pentru modelul de domeniu al proiectelor + 24 noi pentru persistență/pagini/arhivare/corecția de gramatică).
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

Următorul agent este **Codex**. Dacă utilizatorul nu stabilește altă prioritate, următorul element activ este Task 2 / **Subtask 2.3 — Restaurarea contextului de navigare și registrul jurnalului** din `TODO.md` (cele mai mici bucăți rămase din Task 2), sau Task 4 („Identificarea beneficiarului cu CUI duplicat”), care nu mai depinde de nimic neterminat.

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice, inclusiv secțiunile „Cod produs” și „Proiecte”.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
