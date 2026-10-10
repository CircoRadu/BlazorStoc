# Teste rămase de efectuat

> **Numerotarea taskurilor.** Numerele „Task N” din acest fișier sunt cele folosite la momentul implementării. Din 25 septembrie 2026 `TODO.md` nu mai păstrează numere pentru taskurile finalizate, ci denumiri succinte; corespondența este: Task 0 = „Stoc exclusiv prin mișcări de intrare și ieșire”; Task 1 = „Intrări și ieșiri pentru un produs existent” (în perioada timpurie a proiectului „Cod produs”); Task 2 = „Proiecte asociate beneficiarilor”; Task 3 = „Navigarea din jurnal către pagina obiectului”; Task 4 = „Identificarea beneficiarului cu CUI duplicat”; Task 5 = „Confirmarea salvărilor care modifică date existente”; Task 6 = „Confirmarea la părăsirea formularului de adăugare produs”; Task 7 = „Confirmarea deconectării”; Task 8 = „Sincronizarea între utilizatori prin evenimente din baza de date”; Task 9 = „Blocarea temporară a editării unui produs”. Taskul activ „Situația de inventar” (fost Task 10) este acum **Task 1**.

Acest fișier ține evidența verificărilor care **nu au putut fi efectuate** până acum, cu motivul pentru care nu s-au putut face și cu pașii exacți prin care se pot efectua. Suita automată (`tests/BlazorStoc.Checks`, 388 de verificări la 25 septembrie 2026) și verificările manuale din `VALIDARE.md` acoperă restul.

## Cum se folosește și cum se actualizează

- Fiecare test are un identificator stabil (de exemplu `A2`). Nu renumerota testele; un test nou primește următorul număr liber din grupa lui.
- Când un test este efectuat, mută-l în secțiunea **Teste efectuate** de la final, cu data, persoana și rezultatul (trecut/eșuat, cu observații). Un test eșuat generează un task în `TODO.md`.
- Când o lucrare nouă lasă ceva neverificat, adaugă aici un test nou (motiv + pași) și trimite din `VALIDARE.md` la identificatorul lui.
- Nu introduce niciodată parole reale în acest fișier. Conturile fictive ale modului demonstrativ sunt afișate pe pagina de autentificare și descrise în `README.md`.

## Motivele pentru care testele nu s-au putut efectua

| Cod | Motiv |
|---|---|
| **M1** | Nu există un server MariaDB în mediul de lucru al agentului și nici acces la unul (nici la backup-ul real). Proiectul nu folosește Docker, iar integrarea NAS/QNAP este în afara etapei curente. |
| **M2** | Testul cere o a doua stație, un al doilea cont autentificat separat sau un utilizator cu drepturi limitate; în panoul Browser al agentului există o singură sesiune autentificată (contul administrator fictiv), iar agentul nu introduce parole. |
| **M3** | Automatizarea panoului Browser nu poate acționa elementul: ferestrele native (calendarul), gesturile tactile, tastatura pe elemente native sau redimensionarea la dispozitive reale. |
| **M4** | Testul cere un alt browser decât cel din panou (Chromium); utilizatorul a confirmat manual numai Chrome și Brave. |
| **M5** | Testul ar modifica ireversibil date sau ar încheia sesiunea autentificată a utilizatorului din panou; se face doar de utilizator sau pe o copie a datelor. |
| **M6** | Testul cere timp îndelungat sau condiții greu de provocat (întreruperea rețelei, zile de funcționare, oprirea unui serviciu). |
| **M7** | Testul cere o aplicatie externa (alt cititor PDF, o imprimanta fizica) care nu este disponibila in mediul de lucru al agentului. |

## Pregătirea mediului

### Mediul demonstrativ (SQLite) — pentru grupele B–G

```powershell
cd BlazorStoc
dotnet build BlazorStoc.csproj -c Release
.\bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082
```

Autentificare cu un cont fictiv de pe pagina de login (`README.md`). Baza locală este `data\blazorstoc-local.db`; pentru teste care modifică date lucrează pe o copie (`App__LocalDatabasePath` către alt fișier) sau șterge datele de test prin fluxurile normale ale aplicației.

### Mediul MariaDB — pentru grupa A

**Actualizat 28.09.2026 (Task 2, ciclul de adaptare la schema reala):** sectiunea veche de mai jos descria pregatirea unei baze cu schema legacy proprie (`database\BlazorStoc_create.sql`, tabele create la prima folosire, `Database__ApplicationUserId`). Aceasta nu se mai aplica: codul Maria* a fost adaptat la instanta locala reala, deja creata si populata prin migrare din SQLite, descrisa integral in `docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md`. Pentru pregatirea mediului foloseste acel document (sectiunile 2-3: conectare, pornire, persistenta) in loc de pasii vechi de mai jos; `Database:ApplicationUserId` nu mai exista in cod (eliminat, vezi TODO.md Task 2 subtask 2.10 si `docs/PROJECT_STATE.md`). Contul aplicatiei (`blazorstoc_dev`) NU are drepturi DDL (`CREATE`,`ALTER`,`INDEX`,`TRIGGER`) — schema si trigger-ele exista deja, create separat; un cont administrativ/de migrare distinct (`blazorstoc_migrator`, creat in acest ciclu) se foloseste numai pentru migrari viitoare de schema, niciodata pentru operarea curenta a aplicatiei. Testele A1-A11 de mai jos raman din perioada schemei legacy (`produs`/`io`/`web_user`/`project` create la prima folosire) si urmeaza sa fie rescrise cand se face verificarea efectiva prin browser (subtask 2.11/2.12); numele de tabele/coloane din ele nu mai corespund codului curent. A12 (mai jos) foloseste deja schema reala si a fost verificat efectiv in acest ciclu.

Pasii vechi (istorici, pastrati fara corectare):

Folosește **o copie** a bazei (restaurată din backup), niciodată baza de producție.

1. Creează baza: rulează `database\BlazorStoc_create.sql`, apoi `database\BlazorStoc_upgrade_0.2.sql` (sau restaurează backup-ul real într-o bază `BlazorStoc` nouă).
2. Creează un cont pentru aplicație cu privilegiile `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, TRIGGER, REFERENCES` pe baza `BlazorStoc` (aplicația creează la prima folosire tabelele `project*`, `web_user*`, `product_lock`, `change_events` și trigger-ele).
3. Alege un identificator existent din tabela `user` pentru `Database__ApplicationUserId` (operatorul înregistrat în mișcări).
4. Pornește aplicația pe alt port decât preview-ul demonstrativ:

```powershell
$env:App__DemoMode = "false"
$env:Database__Host = "127.0.0.1"
$env:Database__User = "<contul aplicației>"
$env:Database__Password = "<parola contului>"          # nu se salvează în fișiere
$env:Database__SslMode = "Disabled"                     # numai pentru un server local de test
$env:Database__ApplicationUserId = "<id din tabela user>"
$env:Authentication__Username = "admin"
$env:Authentication__Password = "<minimum 12 caractere>"
.\bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5083
```

5. Autentifică-te cu `Authentication__Username` / `Authentication__Password` (administratorul de inițializare).

---

## A. MariaDB pe un server real (motiv: M1, partial rezolvat in ciclul Task 2)

Tot codul MariaDB a fost rescris in acest ciclu pentru schema reala (vezi TODO.md Task 2, `docs/PROJECT_STATE.md`); A1-A11 de mai jos descriu comportamentul asteptat pe schema **veche** si urmeaza sa fie reverificate/rescrise cand se face verificarea completa prin browser (subtask 2.11/2.12), inca neefectuata in acest ciclu. A12 este noul test, deja efectuat pe schema reala.

### A1. Pornirea și crearea schemei
- **Verifică**: inițializarea (`MariaArchiveSchema`, tabelele `project`, `project_observation`, `project_observation_file`, `web_user*`, `product_lock`, `change_events`, coloanele adăugate la `io`) rulează fără erori pe o bază existentă.
- **Pași**: pornește aplicația în modul MariaDB (secțiunea de pregătire); deschide pe rând Produse, Beneficiari, Utilizatori, un produs (pagina de mișcări), un proiect, Jurnal; urmărește `preview.stdout.log`/consola.
- **Așteptat**: nicio excepție; `SHOW TABLES` conține tabelele de mai sus; `SHOW COLUMNS FROM io` conține `id_project`, `io_versiune`, `io_created_utc`, `io_updated_utc`, iar `io_numar_bucati` este `INT` (nu `tinyint`).
- **Sursă**: Task 1 (subtask 1.3), Task 2.

### A2. Formatul datelor din backup (`io_data`, `io_tip_actiune`)
- **Verifică**: ipotezele preluate din aplicația veche (`SQLWrapper.cs`, `IO.cs`): `io_data` are formatul `dd-MM-yyyy`, iar `io_tip_actiune = 1` înseamnă intrare (celelalte valori = ieșire).
- **Pași**: în copia bazei, rulează `SELECT io_data, io_tip_actiune, io_numar_bucati, io_descriere FROM io ORDER BY id_io DESC LIMIT 50;`; deschide în aplicație pagina de mișcări a unui produs cu mișcări vechi și compară data, tipul și cantitatea cu ce afișează aplicația.
- **Așteptat**: datele și tipurile coincid cu rândurile din bază; nicio mișcare veche nu apare cu dată invalidă sau cu tip inversat; stocul afișat = suma intrărilor − suma ieșirilor.
- **Notă**: dacă backup-ul folosește alt format, actualizează `StockMovementRules.ParseLegacyDate` și maparea din `MariaStockMovementRepository`.

### A3. Beneficiari: CUI duplicat, inclusiv la cereri concurente
- **Verifică**: mesajul „Există deja un beneficiar cu acest CUI: «nume».” cu numele salvat, la creare și editare, și traducerea erorii 1062 a indexului `UX_beneficiar_cui` când două salvări ajung simultan.
- **Pași**: (1) creează un beneficiar cu CUI `RO12345678`; (2) încearcă să creezi altul cu același CUI și alt nume → verifică mesajul; (3) editează alt beneficiar către acel CUI → același mesaj, formularul rămâne cu valorile; (4) concurență: din două tab-uri completează formularul de creare cu același CUI nou și apasă „Salvează” cât mai simultan (repetă de 5–10 ori sau folosește două scripturi paralele) → o singură salvare reușește, cealaltă primește mesajul cu numele beneficiarului creat.
- **Așteptat**: niciodată două rânduri cu același CUI; mesajul conține numele existent și în cazul concurent (nu un mesaj generic sau o eroare 500).
- **Sursă**: Task 4.

### A4. Mișcări de stoc: atomicitate, corecții, arhivare
- **Verifică**: `MariaStockMovementRepository` — stocul (`produs_cantitate`) se schimbă atomic cu mișcarea; editarea/ștergerea corectează stocul; istoricul (`io_history`) și arhiva (`archive_stock_movements`) se scriu în aceeași tranzacție.
- **Pași**: pe un produs de test: (1) adaugă intrare 10 și ieșire 4 → stoc +6; (2) editează intrarea la 12 cu motiv → stoc +8, apare `[*]` și istoric prin click dreapta; (3) șterge o mișcare (ștergere în doi pași) → stoc corectat, rând în `archive_stock_movements`; (4) în două tab-uri, adaugă simultan mișcări pe același produs (8–10 repetări) și compară stocul din `produs` cu suma din `io`.
- **Așteptat**: `produs_cantitate` = suma mișcărilor; nicio mișcare pierdută; nicio mișcare parțial arhivată.
- **Sursă**: Task 1.

### A5. Proiecte, observații și fișiere
- **Verifică**: `MariaProjectRepository` / `MariaProjectFileStore`: unicitatea denumirii per beneficiar (index `ux_project_beneficiary_name`, mesajul de concurență), versionarea, arhivarea proiectului cu observațiile și fișierele, blocarea ștergerii beneficiarului cu proiecte și a proiectului cu mișcări, încărcarea/descărcarea fișierelor pe disc.
- **Pași**: creează proiect, două observații, încarcă un fișier (fișier mic text și unul de câțiva MB), descarcă-l și compară hash-ul (`Get-FileHash`); încearcă un proiect cu aceeași denumire (cu alte majuscule/diacritice) → respins; leagă o ieșire de stoc de proiect → încearcă să ștergi proiectul → blocat; șterge un proiect fără mișcări → arhivat (rânduri în `archive_projects`, `archive_project_observations`, `archive_project_observation_files`, fișier mutat în arhivă).
- **Așteptat**: fișierul descărcat este identic; regulile de mai sus se aplică; jurnalul conține evenimentele de adăugare/editare/ștergere.
- **Sursă**: Task 2.

### A6. Utilizatori web și autentificare
- **Verifică**: `MariaUserRepository` (`web_user`, `web_role`, `web_user_audit`): creare, editare (rol, activ, parolă nouă), ștergere, regula „cel puțin un administrator activ”, autentificarea cu conturile create.
- **Pași**: creează un utilizator cu drepturi limitate; autentifică-te cu el în altă fereastră privată; încearcă să dezactivezi/ștergi ultimul administrator → respins; schimbă parola și reautentifică-te.
- **Așteptat**: regulile respectate; utilizatorul limitat nu vede meniul Utilizatori și primește refuz pe `/utilizatori` și `/utilizatori/{id}`.
- **Motiv suplimentar**: M2.

### A7. Sincronizarea prin trigger-e pe MariaDB (Task 8)
- **Verifică**: `MariaChangeEventSource` creează `change_events` și trigger-ele `trg_chg_*` pe `produs`, `io`, `web_user`, `project`, `project_observation`, `project_observation_file`; relay-ul citește evenimentele; modificările făcute de o **aplicație externă** (aplicația veche WinForms sau un client SQL) apar în paginile deschise.
- **Pași**: (1) după pornire: `SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA='BlazorStoc' AND TRIGGER_NAME LIKE 'trg_chg_%';` → 18 trigger-e (6 tabele × 3 operații) — dacă tabelele `web_user*`/`project*` nu existau la pornire, apar la cel mult 5 minute după ce sunt create; (2) deschide pagina unui produs în browser; într-un client SQL rulează `UPDATE produs SET produs_denumire = CONCAT(produs_denumire,' [EXT]') WHERE id_produs = <id>;` → pagina se actualizează în ~2–3 s; readu denumirea; (3) `INSERT INTO io ...` (mișcare nouă) pentru același produs → stocul/lista se reîmprospătează; (4) cu contul aplicației **fără** privilegiul `TRIGGER`, pornește aplicația → în consolă apare un singur avertisment „Change events could not be read…”, iar paginile continuă să funcționeze cu sincronizarea periodică.
- **Așteptat**: exact un eveniment per modificare; `change_events` conține numai identificatori; evenimentele mai vechi de 24 h sunt șterse.
- **Sursă**: Task 8.

### A8. Blocarea editării pe MariaDB (Task 9)
- **Verifică**: `MariaProductLockRepository` (tabela `product_lock`): obținere atomică, reînnoire, expirare cu `UTC_TIMESTAMP(6)`, `INSERT IGNORE` la cereri simultane, deblocare forțată cu jurnal în `audit_events`.
- **Pași**: repetă scenariul de la `VALIDARE.md` (Task 9) cu două sesiuni: banner + „Editează” dezactivat, eliberare live, deblocare forțată cu motiv, heartbeat (`SELECT renewed_utc, expires_utc, UTC_TIMESTAMP(6) FROM product_lock;` la 30 s), închiderea tabului în timpul editării → lease expirat după ~90 s; cereri simultane de lock din două tab-uri apăsând „Editează” cât mai simultan.
- **Așteptat**: un singur editor activ; timpul rămas afișat corect chiar dacă ceasul serverului aplicației diferă de al bazei.

---

### A9. Vehicule pe MariaDB
- **Verifică**: `MariaVehicleRepository` — crearea tabelei `vehicul` la prima accesare, unicitatea numărului (indexul `UX_vehicul_numar`), traducerea erorii 1062 la creare concurentă, arhivarea (`archive_vehicles`) în aceeași tranzacție cu evenimentul din `audit_events`.
- **Pași**: (1) pornește în modul MariaDB și deschide Administrare → Vehicule (`SHOW TABLES` conține `vehicul`); (2) adaugă `HD-01-FDG`, apoi încearcă din nou `hd01fdg` → mesajul cu numărul și descrierea existentă; (3) din două tab-uri creează simultan același număr nou (5–10 repetări) → o singură salvare reușește, cealaltă primește mesajul cu vehiculul existent, nu o eroare 500; (4) editează cu motivare și șterge în doi pași → rând în `archive_vehicles` și în `archive_operations`, eveniment „Ștergere” în `audit_events` cu același `archive_operation_id`; (5) verifică în Jurnal filtrul „Vehicule”.
- **Așteptat**: niciodată două rânduri cu același număr; ștergerea nu lasă un vehicul parțial arhivat.
- **Sursă**: administrarea vehiculelor.

### A11. Restituire si mutare din pagina vehiculului pe MariaDB
- **Verifica**: `MariaStockMovementRepository.TransferFromVehicleAsync` (blocarea produselor in ordinea identificatorilor, inserarea in `io` cu `io_destinatie` 5 sau 2, `id_vehicul_sursa`), `GetVehicleEquipmentAsync` si `MariaVehicleRepository.GetAsync`.
- **Pasi**: (1) pe o baza cu doua vehicule si produse mutate in primul; (2) deschide `/vehicule/{id}/echipamente`; (3) restituie partial un reper, muta un altul in a doua masina, apoi "Muta tot" si "Restituie tot"; (4) din doua tab-uri, "Muta tot" simultan din aceeasi masina; (5) verifica in `io` randurile create si ca `produs_cantitate` nu s-a schimbat.
- **Asteptat**: una singura dintre mutarile simultane reuseste; totalul (`produs_cantitate`) neschimbat; nicio masina cu cantitate negativa; nu apar blocaje (deadlock) intre operatii.
- **Sursa**: pagina vehiculului.

### A16. Eroare tranzitorie "Miscarile nu au putut fi incarcate" pe preview-ul MariaDB (Task 2)
- **Verifica**: `ProductMovements.razor` (`LoadPageAsync`, catch generic la linia care afiseaza "Miscarile nu au putut fi incarcate. Verifica serverul si conexiunea la baza de date.") pentru produsul de test "Diblu nylon 8 x 40 test 22" (id 12, ramas in baza reala de livrare din verificarile 2.12).
- **Motiv**: M6 — observata o singura data de utilizator (28.09.2026, pe `http://127.0.0.1:5085/`), nereprodusa la reincercare dupa repornirea preview-ului si reautentificare; jurnalul serverului (pornit ulterior cu iesire redirectionata in fisier) nu contine nicio intrare noua de `Logger.LogError("Movement list loading failed...")` la reincercare, deci exceptia originala nu a putut fi capturata.
- **Pasi**: daca reapare, verifica imediat (inainte de orice repornire) fisierul de iesire al preview-ului MariaDB pentru mesajul exact `Movement list loading failed ({ErrorType})` si tipul exceptiei; nu reporni serverul inainte de a citi jurnalul, ca sa nu pierzi contextul (o repornire delogheaza toate sesiunile si poate masca eroarea, ca in acest caz).
- **Asteptat**: pagina de miscari se incarca normal pentru orice produs existent, inclusiv cele create de verificarile automate.
- **Sursa**: observatie directa a utilizatorului, 28.09.2026, in timpul verificarii subtask 2.12.

### A14. Autentificare si doua sesiuni concurente pe preview-ul MariaDB (Task 2, subtask 2.12)
- **Verifica**: autentificarea efectiva pe preview-ul MariaDB (`http://127.0.0.1:5085/`, pornit in acest ciclu, conectat la baza reala de livrare) si comportamentul cu doi utilizatori autentificati simultan.
- **Motiv suplimentar**: M2 (agentul nu introduce niciodata parole, nici macar cele demonstrative).
- **Pasi**: autentifica-te cu `administrator.demo`/`utilizator.demo` (parolele demonstrative din `README.md`) sau cu contul bootstrap din `local-secrets\maria-preview-bootstrap.private.json`; deschide o a doua sesiune (alt browser/fereastra privata) cu celalalt cont; verifica fluxurile CRUD obisnuite (produse, beneficiari, miscari, vehicule, proiecte, jurnal) contra datelor reale migrate.
- **Asteptat**: autentificare reusita, date reale (229 de randuri initiale) vizibile si corecte, ambele sesiuni functionale simultan.
- **Sursa**: Task 2, subtask 2.12.

### A10. Destinații și mașini pe MariaDB
- **Verifică**: coloanele adăugate la `io` (`io_destinatie`, `id_vehicul`, `id_vehicul_sursa`), tabela `vehicul` creată de `MariaStockMovementRepository`, interogările cantității din mașini (`VehicleQuantitiesAsync`, subinterogare cu alias), coloanele noi din `archive_stock_movements` (`MariaArchiveSchema`, versiunea 6, `ALTER` pe o arhivă existentă) și blocarea ștergerii unui vehicul cu mișcări (`VehicleHasMovementsAsync`).
- **Pași**: (1) pornește în modul MariaDB pe o bază cu mișcări vechi; (2) deschide un produs și verifică `SHOW COLUMNS FROM io` și `SHOW TABLES` (`vehicul`); (3) adaugă un vehicul și o intrare de 10 buc., apoi o ieșire spre autovehicul de 4 buc. → totalul (`produs_cantitate`) rămâne 10, „6 în depozit, 4 în vehicule”; (4) ieșire din mașină de 3 buc. → total 7, mașina 1; peste 1 buc. → mesajul cu cantitatea disponibilă; (5) din două tab-uri, ieșiri simultane din aceeași mașină → una singură reușește; (6) șterge o mișcare, apoi încearcă ștergerea vehiculului cu mișcări → refuz; (7) `SELECT * FROM archive_stock_movements` conține destinația și vehiculele.
- **Așteptat**: totalul = suma efectelor; nicio mașină cu cantitate negativă; mișcările vechi (fără destinație) rămân neschimbate; aplicația veche nu este afectată de coloanele noi (o ieșire spre mașină apare în ea ca ieșire obișnuită — de ținut minte la folosirea în paralel).
- **Sursă**: ieșire spre vehicul, vânzare generică și corecție de stoc.

### A19. Verificare in browser a butonului "Preia inventar" cu noul backup (Task 2, subtask 2.1)
- **Verifica**: fluxul complet in `/inventar/preluare` — incarcarea unui PDF scanat, indicatorul de progres al backupului (`backupProgress`, `Components/Pages/InventoryPickup.razor`) si confirmarea preluarii numai dupa backup reusit, prin panoul Browser al agentului.
- **Motiv**: M3 — campul de incarcare fisier (`<InputFile>`) deschide un dialog nativ de sistem de operare pentru alegerea fisierului; automatizarea panoului Browser nu poate seta valoarea unui `<input type="file">` din JavaScript (blocat explicit de browser) si nu poate interactiona cu dialogul nativ Windows de deschidere fisier.
- **Pasi**: din panoul Browser (sau manual de catre utilizator), autentifica-te, deschide `/inventar/preluare`, apasa "Preia inventar", alege `tests/BlazorStoc.Checks/Fixtures/inventar-proba.pdf` (sau alt formular scanat), selecteaza cateva produse cu diferente, apasa "Trimite modificari in stoc" → "Confirma"; urmareste mesajele de progres ("Se blocheaza...", "Se exporta...", "Se verifica...", "Se salveaza...") si confirma ca modificarile de stoc apar abia dupa finalizarea backupului.
- **Asteptat**: comportamentul descris in criteriile de acceptare ale subtask-ului 2.1; pachetul de backup apare in directorul configurat dupa confirmare.
- **Sursa**: Task 2, subtask 2.1 (acest ciclu, 29.09.2026); mecanismul a fost verificat direct (fara panoul Browser) prin `tests/BlazorStoc.Checks` (vezi `docs/PROJECT_STATE.md`).

### A21. Drag and drop real (din managerul de fisiere al sistemului de operare) la preluarea inventarului
- **Verifica**: `wwwroot/inventory-pickup-dropzone.js` - o tragere reala a unui fisier din Explorer/Finder peste zona de continut din `/inventar/preluare` declanseaza acelasi flux ca butonul "Preia inventar".
- **Motiv**: M3 - automatizarea panoului Browser al agentului poate simula evenimentele native `dragover`/`drop` cu un obiect `DataTransfer`/`File` construit prin JavaScript (verificat asa in acest ciclu: evidentiere vizuala corecta, fisierul atribuit campului ascuns, `ProcessFileAsync` declansat si validarea de continut executata), dar nu poate initia o tragere reala pornita din afara paginii (dialogul/managerul de fisiere al sistemului de operare), la fel ca la A19.
- **Pasi**: din panoul Browser (sau manual de catre utilizator), autentifica-te, deschide `/inventar/preluare`, deschide un Explorer Windows separat cu `tests/BlazorStoc.Checks/Fixtures/inventar-proba.pdf`, trage fisierul peste zona de continut a paginii (nu doar peste buton); confirma evidentierea vizuala in timpul tragerii si ca formularul se citeste identic cu incarcarea prin buton.
- **Asteptat**: comportamentul e identic cu apasarea butonului "Preia inventar" si alegerea aceluiasi fisier din dialogul nativ.
- **Sursa**: cerere directa a utilizatorului (acest ciclu, 29.09.2026); mecanismul JS a fost verificat prin simularea evenimentelor native in browser (vezi `VALIDARE.md`).

### A18. Backup real prin mariadb-dump la preluarea inventarului (Task 2, subtask 2.1/2.2)
- **Verifică**: `MariaDatabaseBackupService` (`Services/DatabaseBackup.cs`) — rularea reală a `mariadb-dump.exe`, verificarea canonică (`CanonicalRowHasher`) înainte/după export pe baza vie, salvarea pachetului `.zip` + `manifest.json` + fișierul `.sha256`, declanșarea din `Components/Pages/InventoryPickup.razor` (`ConfirmSendAsync`) cu blocarea confirmării preluării până la un backup verificat.
- **Motiv**: M1 — mediul agentului (această sesiune) nu are instanța locală MariaDB (`%LOCALAPPDATA%\BlazorStoc-MariaDB\mariadb-11.4.13-winx64\bin\mariadb-dump.exe`) și nici `application-connection.private.json`; directorul exista doar cu `admin.private.cnf`/`connection.private.json` și cheia Data Protection, fără server/binare. Calea de demo (SQLite, `SqliteDatabaseBackupService`) a fost verificată integral prin execuție reală (vezi `docs/PROJECT_STATE.md`), inclusiv end-to-end prin `tests/BlazorStoc.Checks`.
- **Pași**: pe mașina cu instanța MariaDB reală (`docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md`), pornește aplicația cu `App:DemoMode=false`; deschide `/inventar/preluare`, încarcă un formular scanat cu diferențe, confirmă trimiterea; verifică: (1) fișierul `.zip` apare în `Database:MariaBackupFilesPath` (implicit `...\assets\database-backups`) cu numele conform modelului; (2) conține `dump.sql` și `manifest.json` cu numărul de tabele/rânduri corect; (3) există fișierul `<nume>.zip.sha256`; (4) preluarea nu se confirmă dacă `mariadb-dump.exe` lipsește sau baza e ocupată de o altă operație (mesajul de lacăt); (5) spațiul insuficient pe disc oprește operația înainte de a scrie vreun fișier.
- **Așteptat**: comportamentul descris în TODO.md, „Decizie tehnica” (Task 2) și criteriile de acceptare ale subtask-urilor 2.1/2.2.
- **Sursă**: Task 2, subtask 2.1/2.2 (acest ciclu, 29.09.2026).
- **Rezultat partial 29.09.2026 (instanta C:\Dev\BlazorStoc-MariaDB pornita, port 3307)**: rulat `MariaDatabaseBackupService` real, printr-un harness in afara repository-ului, contra bazei `BlazorStoc` cu `blazorstoc_dev` si `Database:MariaDumpExecutablePath=C:\Dev\...\mariadb-dump.exe` (calea implicita cauta in `%LOCALAPPDATA%\BlazorStoc-MariaDB`, unde instanta nu mai este). Gasit si corectat: fisierul de credentiale scria `ssl-mode=REQUIRED`, optiune pe care `mariadb-dump`/`mariadb` nu o cunosc (`unknown variable`, cod 7); acum scrie `ssl=1` (`MariaClientSsl` in `Services/DatabaseBackup.cs`, folosit si la restaurare). **Blocat**: dupa aceasta corectie exportul esueaza cu `Access denied; you need ... TRIGGER privilege` la `SHOW CREATE TRIGGER`, deoarece `blazorstoc_dev` nu are privilegiul `TRIGGER` pe `BlazorStoc`; exportul fara `--triggers` merge, dar un backup fara cele 18 triggere de evenimente nu e restaurabil corect, deci nu am relaxat parametrul. Pasi de deblocare (decizie de securitate a administratorului bazei): `GRANT TRIGGER ON BlazorStoc.* TO 'blazorstoc_dev'@'127.0.0.1';` (sau alt cont dedicat de backup), apoi reluat pasii (1)-(5) de mai sus.

### A20. Restaurare reala prin RENAME TABLE (Task 3, subtask 3.4, Pasul 4)
- **Verifica**: `MariaDatabaseRestoreService`/`MariaSchemaSwap` (`Services/DatabaseRestore.cs`) - Pasii 1-3 (verificare hash, structura din manifest, continut cu `CanonicalRowHasher`) contra unei baze MariaDB reale, si Pasul 4 (creare schema `_bak`, import cu `mariadb.exe`, verificare, comutare atomica `RENAME TABLE`, schema `_old` pastrata).
- **Motiv**: M1 - acelasi motiv ca A18 (fara instanta locala MariaDB in acest mediu). In plus, Pasul 4 necesita un cont MariaDB dedicat cu drepturi `CREATE`/`DROP SCHEMA` si `RENAME TABLE` peste schema vie, `_bak` si `_old` (configurat prin `Database:MigratorUser`/`Database:MigratorPassword`), care nu exista inca - contul `blazorstoc_migrator` creat la Subtask 2.9 are drepturi DDL numai pe schema `BlazorStoc`. Fara acest cont, `RestoreAsync` se opreste explicit la Pasul 4 cu mesajul `RestoreRules.MigratorNotConfiguredMessage`. Calea de demo (SQLite, `SqliteDatabaseRestoreService`) a fost verificata integral prin executie reala (vezi `docs/PROJECT_STATE.md`), inclusiv end-to-end prin `tests/BlazorStoc.Checks` si prin browser (restaurare efectiva a bazei demonstrative).
- **Pasi**: (1) provizioneaza contul dedicat descris mai sus (sau extinde `blazorstoc_migrator` cu drepturi cross-schema, daca se accepta acea alternativa) si seteaza `Database:MigratorUser`/`Database:MigratorPassword` in configuratia privata; (2) pe masina cu instanta MariaDB reala, cu `App:DemoMode=false`, genereaza un pachet de backup (preluare inventar), modifica un rand in baza vie, apoi foloseste "Restaureaza baza de date" din `/inventar/restaurare`; (3) verifica in ordine: pachetul pre-restaurare aparut inaintea oricarei modificari; un pachet cu `.sha256` alterat manual e refuzat fara sa creeze schema `_bak`; un pachet cu un tabel lipsa fata de `MariaArchiveSchema.RequiredTables` e refuzat cu mesajul de structura; restaurarea aceluiasi pachet imediat dupa ce a fost creat e refuzata ca fiind identica; o restaurare valida schimba efectiv randul modificat inapoi, schema `<nume_baza>_old` ramane pe server dupa succes, iar schema `_bak` nu ramane niciodata (stearsa la esec, redenumita in schema vie la succes).
- **Rezultat 29.09.2026 (instanta MariaDB temporara, port 3390, copie a bazei de dezvoltare, aceleasi drepturi de cont ca in `create-restore-account.sql`)**: `MariaDatabaseRestoreService` real, de la capat: backup, modificare, restaurare (5 pasi), pachet pre-restaurare, `_old` pastrat (si inlocuit la a doua rulare), `_bak` sters, 18 triggere recreate si functionale, valoarea modificata revenita, lacat eliberat; un tabel adaugat de o migrare dupa backup (`beneficiary_work_points`, cu cheie externa) ajunge in `_old` cu cheia externa consistenta. Bug gasit si reparat: triggerele recreate apartin contului de restaurare (definer) si cer `SELECT` pe tabele, altfel scrierile aplicatiei esuau dupa restaurare (`SELECT command denied ... for column 'id'`), deci contul are acum si `SELECT` pe `BlazorStoc`. **Ramas neverificat**: rularea pe instanta de dezvoltare reala (port 3307), cu `REQUIRE SSL` si hostul real al conturilor, si fluxul din pagina `/inventar/restaurare` in browser. Pe instanta reala trebuie rulat: ``GRANT SELECT ON `BlazorStoc`.* TO 'blazorstoc_restore'@'127.0.0.1';``
- **Asteptat**: comportamentul descris in TODO.md, Task 3, subtask 3.4 si in criteriile de acceptare ale Task 3.
- **Sursa**: Task 3, subtask 3.4 (acest ciclu, 29.09.2026).
- **Rezultat 30.09.2026 (instanta reala C:\Dev\BlazorStoc-MariaDB, port 3307, preview 5083, browser)**: restaurare reusita din pagina `/inventar/restaurare` ("Restaurarea a fost finalizata cu succes"). Verificat in baza: schema `blazorstoc_old` creata (28 tabele, starea dinaintea restaurarii: produsul 4 = 40, 25 miscari), `blazorstoc_bak` inexistenta, 18 triggere in `blazorstoc` (0 in `_old`, ca la un RENAME TABLE), pachet pre-restaurare nou in director, eveniment de audit "Restaurare" cu pachetul folosit si cel pre-restaurare. Starea restaurata (produsul 4 = 30, 23 miscari, ultima miscare a produsului id 27) este exact cea de la momentul backupului: backupul se face INAINTEA aplicarii preluarii, deci nu contine corectia de +20 (id 28) si nici iesirea de 10 (id 29). Pentru a reveni la stocul de dupa corectie trebuie ales un backup facut dupa ea. Refuzuri verificate in browser pe MariaDB reala (30.09.2026, pachete de test create din cel bun si sterse dupa): `.sha256` alterat -> "Pachetul de siguranta este corupt sau deteriorat (hash necorespunzator); restaurarea a fost oprita."; manifest fara `beneficiary_work_points` -> "Pachetul nu poate fi folosit: structura bazei de date difera de baza curenta."; in ambele cazuri baza vie a ramas neschimbata (stoc 30, 23 miscari, 18 triggere, fara `_bak`, `_old` neatinsa), iar fiecare incercare a generat totusi un pachet pre-restaurare (Pas 0, prin design). Ramas neverificat pe MariaDB reala: refuzul unui pachet identic cu baza vie (baza vie contine deja evenimente de audit scrise dupa orice pachet, deci nu se poate simula; acoperit de teste in modul SQLite); `GRANT SELECT ... TO blazorstoc_restore` a fost dat de utilizator manual (30.09.2026) pe instanta reala.
- **Rezultat A18 30.09.2026 (instanta reala)**: backup real prin `mariadb-dump` verificat: pachet `.zip` cu `dump.sql` (28 tabele, 18 triggere) si `manifest.json` (28 tabele dupa corectia `beneficiary_work_points` in `MariaArchiveSchema.RequiredTables`), hash SHA-256 al arhivei egal cu `.sha256`, preluarea nu se confirma cand `mariadb-dump.exe` lipseste. Spatiu insuficient (30.09.2026, in browser pe 5083, instanta reala): simulat cu un fisier `.zip` rar (sparse) de 400 GB in directorul de backup (cerinta 4 x 400 GB > 1,35 TB liberi) -> mesajul "Spatiu insuficient pe disc pentru copia de siguranta. Elibereaza spatiul si reincearca.", preluarea neconfirmata, niciun pachet nou, niciun fisier temporar (nici credentiale in %TEMP%), stocul neschimbat; fisierul de test a fost sters. Lacat activ (acelasi mod): fisier `operation.lock.json` simulat cu bataie de inima in viitor -> mesajul "O alta operatie de backup sau restaurare este deja in curs. Reincearca mai tarziu.", stocul si pachetele neschimbate; lacatul simulat a fost sters. Banner de intretinere (30.09.2026, browser, lacat simulat activ): apare in antet la reincarcarea/navigarea paginii. Lacat orfan (fisier cu bataia de inima expirata de 10 min, limita 120 s): la urmatoarea preluare a fost eliberat automat, cu eveniment de audit `sistem` / "Deblocare" ("operator anterior: test.orfan"), avertisment in jurnalul aplicatiei, apoi backup reusit (pachet nou, 28 tabele) si stoc aplicat (Polizor 7 -> 9, +1 miscare); fisierul de lacat a disparut. Ramas neverificat: lacat orfan lasat de o oprire reala a aplicatiei in mijlocul unui backup (F2). Notificarea live a taburilor deschise si blocarea scrierilor altor sesiuni sunt implementate si verificate (30.09.2026, vezi `IMPLEMENTED.md`: "Blocarea sesiunilor si notificare live"); in browser, cu lacat simulat, taburile Produse/Beneficiari au trecut pe pagina de asteptare si au revenit singure, Preluare inventar a ramas pe loc; un backup real si doua restaurari reale au reusit sub blocare. Calea implicita a executabilelor (`%LOCALAPPDATA%\BlazorStoc-MariaDB`) nu mai exista: preview-ul se porneste cu `Database__MariaDumpExecutablePath` si `Database__MariaClientExecutablePath` catre `C:\Dev\BlazorStoc-MariaDB\mariadb-11.4.13-winx64\bin`.

## B. Mai mulți utilizatori și mai multe calculatoare (motiv: M2, M6)

### B1. Două calculatoare, două conturi distincte
- **Verifică**: sincronizarea (Task 8) și blocările (Task 9) între utilizatori reali, nu între două tab-uri ale aceluiași cont.
- **Pași**: pornește aplicația pe un calculator accesibil în rețea (`--urls http://0.0.0.0:5082`, cu regula de firewall necesară); pe un al doilea calculator autentifică-te cu alt cont; (1) A editează un produs, B vede „🔒 Produsul este editat de <contul A> din <ora>” și „Editează” dezactivat; A anulează → B primește imediat „eliberat”; (2) A salvează o modificare a numelui/descrierii → B vede catalogul actualizat fără acțiune; (3) B (fără formular deschis) și A (cu formular deschis pe același produs): notificarea „Datele au fost modificate…” apare la A fără să-i schimbe formularul; (4) A (administrator) forțează deblocarea unui produs editat de B → B vede „Blocarea editării a fost pierdută” și la salvare primește verificarea de versiune dacă între timp s-a modificat produsul.
- **Așteptat**: comportamentul din `VALIDARE.md`, cu numele reale ale conturilor.

### B2. Utilizator cu drepturi limitate față de administrator
- **Pași**: cu un cont limitat: (1) meniul Utilizatori lipsește; `/utilizatori`, `/utilizatori/1` și linkul din jurnal → refuz; `/jurnal` este accesibil doar administratorului; (2) pagina unui produs editat de altcineva: butonul „Deblochează (administrator)” **nu** apare; (3) încercarea directă a operației (de exemplu prin `?edit=<id>` pe un produs blocat) → mesajul de blocare, fără formular; (4) paginile beneficiar/proiect/observație funcționează conform regulilor existente.
- **Așteptat**: nicio pagină nu expune date sau acțiuni de administrator.

### B3. Recuperarea după întreruperea conexiunii (SignalR / rețea)
- **Verifică**: „O întrerupere temporară SignalR este recuperată prin sincronizarea periodică” (Task 8) și comportamentul blocării când clientul dispare.
- **Pași**: cu pagina catalogului deschisă pe al doilea calculator, deconectează cablul/Wi-Fi 30–60 s (sau oprește temporar rețeaua), modifică un produs din primul calculator, reconectează → pagina afișează modificarea în cel mult 60 s (sau după reconectarea Blazor); repetă cu formular de editare deschis pe produsul blocat: la reconectare rapidă (<90 s) editarea continuă cu lock-ul reînnoit; la deconectare >90 s lock-ul expiră și editorul primește „Blocarea editării a fost pierdută”.
- **Așteptat**: fără date pierdute sau suprascrise; verificarea de versiune blochează o salvare peste o modificare străină.

---

## C. Interfață în browser neexersată (motiv: M5, M3)

### C1. Confirmarea salvării în fiecare editor (Task 5)
- **Verifică**: dialogul „Salvezi modificările …?” în editoarele care nu au fost exersate (verificat în browser numai la beneficiar).
- **Pași**: pentru **produs** (schimbă descrierea și înlocuiește imaginea), **proiect**, **observație**, **utilizator** (schimbă rolul și parola nouă — parola trebuie să apară doar ca „va fi schimbată”), **categorie**, **subcategorie** (mută în altă categorie) și **mișcare de stoc** (dialogul de editare + al doilea popup peste el): deschide editorul, modifică, apasă „Salvează” → verifică tabelul Câmp / Valoare actuală / Valoare nouă; „Anulează” (și Escape) → nu se salvează nimic, formularul rămâne; „Confirmă salvarea” → se salvează.
- **Așteptat**: numai câmpurile schimbate în tabel; motivarea afișată; crearea unui produs/beneficiar/proiect nou nu afișează popup.
- **De ce nu s-a făcut**: fiecare confirmare modifică date; agentul a confirmat efectiv doar la un beneficiar (readus la valoarea inițială).

### C2. Salvarea și ștergerea efectivă din pagina produsului (revenire în pagina de origine)
- **Pași**: din pagina unui produs de test → „Editează” → modifică → „Salvează” → „Confirmă salvarea” → trebuie să revii în **pagina produsului**; apoi creează un produs de test, deschide-i pagina → „Șterge produs” → confirmă în doi pași → revii în **catalog** (produsul nu mai există).
- **Așteptat**: cele două redirecționări de mai sus; produsul șters apare în arhivă.

### C3. Formular „Adaugă produs”: imaginea păstrată după anulare (Task 6)
- **Pași**: deschide „Adaugă produs”, alege o imagine (sau lipește din clipboard), completează câmpurile, apasă o categorie din meniu → „Continuă adăugarea” → verifică previzualizarea imaginii și câmpurile.
- **Așteptat**: imaginea și valorile rămân; „Părăsește adăugarea” închide formularul fără a salva.
- **De ce nu s-a făcut**: încărcarea unui fișier din dialogul nativ de fișiere nu poate fi acționată din panoul Browser.

### C4. Liste și pagini cu volum mare
- **Pași**: creează 11–25 de proiecte pentru un beneficiar → verifică paginarea din pagina beneficiarului (`?pagina=2`, filtrul `?q=`) și revenirea cu Înapoi; creează >10 mișcări pe un produs → filtrul Intrări/Ieșiri, sortarea după dată și paginarea (10/20/50/Toate); ieșire cu beneficiar **și proiect** din formularul de mișcare.
- **Așteptat**: starea listei în adresă și restaurată prin Înapoi; „Total produse în stoc” corect.
- **Notă**: aceste comportamente sunt acoperite de verificările automate ale repository-urilor, dar nu au fost exersate în browser.

### C5. Pagina observației și a proiectului când altă sesiune le șterge
- **Pași**: deschide pagina unei observații în tab A; în tab B șterge proiectul (ștergere în doi pași); urmărește tab A.
- **Așteptat**: tab A afișează „Proiectul a fost șters de alt utilizator” / „Observația nu mai există”, fără excepții; un formular deschis nu este șters, iar salvarea este respinsă.

### C6. Jurnalul: poziția de derulare și paginare prin adresă (Task 3)
- **Pași**: în `/jurnal` cu >50 de evenimente: `?pe-pagina=10&pagina=3&q=...`, derulează, apasă un link „Țintă”, apoi Înapoi.
- **Așteptat**: filtrele și pagina se restaurează, iar poziția de derulare revine o singură dată.

### C7. Editarea unui beneficiar către un CUI existent, în browser (Task 4)
- **Pași**: „Editează” la un beneficiar → schimbă CUI-ul într-unul existent → motivare → „Salvează” → „Confirmă salvarea”.
- **Așteptat**: mesajul „Există deja un beneficiar cu acest CUI: «nume».”; formularul rămâne cu valorile; beneficiarul editat fără schimbarea CUI-ului nu este raportat ca duplicat.

### C8. Respingerea datei viitoare de către server
- **Verifică**: mesajul `StockMovementRules.FutureDateMessage` afișat în interfață (browserul blochează singur data viitoare, deci serverul nu este atins).
- **Pași**: cu instrumentele de dezvoltare ale browserului elimină atributul `max` de pe câmpul ascuns `input[type=date]` al formularului de mișcare, alege o dată viitoare și apasă „Adaugă”.
- **Așteptat**: „Data mișcării nu poate fi în viitor…”; nimic salvat.
- **De ce nu s-a făcut**: regula este verificată automat pe domeniu și pe SQLite; testul din browser cere manipularea DOM-ului.

### C9. Redirecționarea `inapoi` (securitate)
- **Pași**: deschide `/produse?edit=1&inapoi=https://exemplu.invalid` și `/produse?edit=1&inapoi=//exemplu.invalid`, apoi „Anulează”.
- **Așteptat**: se revine în catalog (adresa externă este ignorată).

### C10. Avertizarea la părăsirea editării: cazuri neexersate în browser (avertizare la părăsirea unei pagini de editare)
- **Pași**: (1) modifică un câmp al unui formular și închide sau reîncarcă tabul → trebuie să apară dialogul nativ al browserului; (2) modifică dialogul „Editează mișcarea” și apasă „Anulează”; (3) în pagina proiectului deschide editorul proiectului și pe cel al observației, modifică-le pe rând și apasă un link din meniu; (4) apasă „Înainte” al browserului după o revenire; (5) mută mai mulți pași în istoric (meniul lung al butonului Înapoi) cu un formular modificat; (6) folosește numai tastatura (Tab, Enter, Escape) pe popup și verifică citirea de către un cititor de ecran.
- **Așteptat**: dialogul nativ la (1); popup „Editarea nu a fost finalizată” la (2)–(5); la (5) mutarea se repetă corect după „Părăsește” sau este anulată la „Înapoi la editare”; Escape = „Înapoi la editare”.
- **De ce nu s-a făcut**: dialogul nativ al tabului și tastatura/cititorul de ecran nu pot fi acționate din panoul Browser; restul nu a fost exersat în ciclu.

### C11. Mesaje românești: cazuri neexersate (mesaje exclusiv în limba română)
- **Pași**: (1) cu serverul oprit, așteaptă până apare starea finală a dialogului de reconectare și verifică textele „Reconectarea a eșuat…” și butonul „Reîncearcă”; (2) deschide calendarul „Data mișcării” și selectorul de fișiere (imagine produs, fișier observație) într-un browser cu interfața în engleză și notează limba ferestrelor native; (3) trimite formularul de autentificare gol.
- **Așteptat**: (1) și (3) în română; (2) ferestrele native folosesc limba browserului (nu pot fi schimbate din aplicație).
- **De ce nu s-a făcut**: ferestrele native nu pot fi acționate din panoul Browser; starea finală a reconectării a fost verificată doar prin funcția de traducere.

---

## D. Tastatură și accesibilitate (motiv: M3)

### D1. Calendarul „Data mișcării” cu tastatura
- **Pași**: cu Tab ajunge pe câmp; Enter, Spațiu, F4 și Alt+Săgeată jos deschid calendarul; în calendar Săgeți + Enter aleg ziua; Escape îl închide; verifică că nu se poate tasta în câmp.
- **Așteptat**: valoarea se schimbă doar din calendar; zilele viitoare sunt dezactivate.
- **De ce nu s-a făcut**: calendarul este o fereastră nativă; panoul Browser nu îi poate trimite click-uri (s-a verificat doar cu tastatura în popup, Săgeată stânga + Enter). Utilizatorul a confirmat manual în Chrome și Brave.

### D2. Escape, focus și Tab în dialoguri
- **Pași**: pentru dialogurile „Salvezi modificările?”, „Părăsești adăugarea produsului?”, „Închizi sesiunea?”, „Deblochezi produsul?” și ștergere: la deschidere focusul este pe dialog/pe butonul sigur; Tab rămâne în dialog (nativ pentru `<dialog>`; verifică și celelalte); Escape = anulare/continuare; după închidere, focusul revine într-un loc logic.
- **Așteptat**: fără capcane de focus, fără acțiuni distructive la Escape.

### D3. Cititor de ecran
- **Pași**: cu NVDA/Narrator parcurge dialogurile de mai sus, bannerele de blocare și notificările „Datele au fost modificate” (`role="status"`).
- **Așteptat**: titlul și descrierea dialogului sunt citite la deschidere; notificările sunt anunțate fără a fura focusul.
- **De ce nu s-a făcut**: nu există un cititor de ecran în mediul agentului.

---

## E. Alte browsere și dispozitive (motiv: M4, M3)

### E1. Firefox, Safari și Edge
- **Pași**: în fiecare browser: autentificare, catalog, adăugare mișcare (calendarul cu `showPicker`), dialogurile `<dialog>` (deconectare), popup-urile de confirmare, meniul produselor cu formularul „Adaugă produs” deschis.
- **Așteptat**: același comportament ca în Chrome/Brave. Atenție la: `HTMLInputElement.showPicker` (Safari mai vechi), `<dialog>` modal, `autofocus` în dialog.

### E2. Telefon / tabletă (tactil) și lățimi mici
- **Pași**: pe un dispozitiv real (sau emulare Chrome DevTools 375×812 și 768×1024): dialogurile (`SaveConfirmationDialog`, `ForceUnlockDialog`, deconectare) să nu depășească ecranul și să poată fi derulate; calendarul se deschide la atingere; bannerul de blocare și butoanele se rup pe rânduri; meniul produselor și popup-ul de părăsire funcționează la atingere.
- **Așteptat**: fără depășire orizontală a paginii; acțiunile rămân accesibile.

---

## F. Funcționare îndelungată și scenarii rare (motiv: M6)

### F1. Retenția evenimentelor de sincronizare
- **Pași**: lasă aplicația pornită >1 h cu modificări periodice; `SELECT COUNT(*), MIN(created_utc) FROM change_events;` (SQLite: `data\blazorstoc-local.db`).
- **Așteptat**: după 24 h de la creare evenimentele procesate sunt șterse; tabelul nu crește nelimitat.

### F2. Repornirea aplicației cu blocări active
- **Pași**: începe o editare (lock activ), oprește procesul aplicației brusc (`Stop-Process`), repornește-l; verifică `product_locks` / `product_lock`.
- **Așteptat**: rândul rămâne, dar nu mai blochează după `expires_utc` (max. 90 s); un alt utilizator poate prelua editarea; nu apar erori la pornire.

### F3. Ștergerea în cascadă și evenimentele derivate
- **Pași**: șterge un proiect cu observații și fișiere, cu o a doua sesiune deschisă pe pagina proiectului și alta pe pagina beneficiarului; urmărește notificările.
- **Așteptat**: fără notificări false pentru sesiunea care a făcut ștergerea; celelalte sesiuni se actualizează o singură dată (evenimentele derivate ale observațiilor/fișierelor pot apărea fără origine — acceptat, verifică să nu producă erori).

## G. Fisierul PDF de inventar in alte medii (motiv: M7)

### G1. Deschiderea situatiei de inventar in alte cititoare PDF
- **Pasi**: genereaza situatia de inventar din `/inventar` si deschide fisierul descarcat in cititoare PDF diferite (Adobe Acrobat Reader, vizualizatorul din alt browser, aplicatia mobila de PDF).
- **Asteptat**: continutul, diacriticele, culoarea rosie a stocului negativ si bordurile tabelului arata identic cu verificarea facuta din agent (`VALIDARE.md`).

### G2. Tiparirea fizica a situatiei de inventar
- **Pasi**: tipareste situatia de inventar generata (o pagina si un caz cu mai multe pagini) pe o imprimanta fizica, format A4.
- **Asteptat**: paginarea, antetul repetat pe pagini si spatiul din coloana "Valoare reala" raman utilizabile pe hartie, la marimea reala.
- **Partial confirmat 28.09.2026**: utilizatorul a tiparit, completat de mana si scanat o situatie de inventar reala (o pagina, scaner Kyocera SKM_C3320i); vezi H1-H2 mai jos si "Teste efectuate". Neverificat: un caz cu mai multe pagini pe hartie fizica.

## H. Preluare inventar - variabilitatea scanarii si a scrisului de mana (motiv: M4, M6, M7)

### H1. Alte scanere si rezolutii decat cea de proba
- **Pasi**: scaneaza aceeasi situatie de inventar completata cu alt scaner/multifunctionala, la alta rezolutie (de exemplu 150 sau 600 dpi).
- **Asteptat**: recalibrarea coloanelor per pagina (`InventoryPickupOcrService.CalibrateColumns`) gaseste corect marginile tabelului si dividerii; randurile care nu pot fi calibrate sunt semnalate, nu produc valori gresite tacute.
- **Actualizat 29.09.2026**: partea despre inclinarea colii (usoara si vizibil de cateva grade) a fost mutata la "Teste efectuate" - au fost gasite si corectate doua bug-uri reale succesive (vezi acolo), nu doar verificate. Ramane deschis doar aspectul altor scanere/rezolutii propriu-zise (nu si inclinarea).

### H2. Acuratetea modelului de cifre pe un esantion mai mare de scris de mana
- **Pasi**: aduna mai multe formulare reale completate de utilizatori diferiti si compara valorile recunoscute cu cele scrise.
- **Asteptat/cunoscut**: modelul MNIST preinstruit (generic, neantrenat pe formularele acestei aplicatii) poate confunda ocazional cifre stilizate cu incredere mare (fals pozitiv), nu doar cu incertitudine semnalata — confirmat pe formularul de proba (cifra "8" scrisa intr-o singura bucla, citita "5" cu `Uncertain=false`). Editarea valorii recunoscute inainte de bifare este singura plasa de siguranta curenta; recalibrarea/reantrenarea modelului pe mostre reale ramane o imbunatatire ulterioara neblocanta (`TODO.md`, Task 1).
- **Alta aparitie confirmata 29.09.2026**: pe scanarea reala inclinata furnizata de utilizator (vezi "Teste efectuate"), "Nivela cu bula 60 cm" scrisa de mana cu valoarea "3" a fost citita "5", tot cu `Uncertain=false`. Acelasi tip de limitare cunoscuta (model generic, nu doar incertitudine gresit calculata), pe alta cifra, alt formular - nu o regresie noua introdusa de corectia liniilor inclinate de mai jos.

### H3. Alte browsere si dispozitive pentru incarcarea fisierului
- **Pasi**: incarca un formular scanat din alt browser decat cel confirmat (motiv M4) si de pe un dispozitiv tactil (motiv M3).
- **Asteptat**: `InputFile` si fluxul de incarcare functioneaza identic; fara verificare inca.

### H4. Miscarile de stoc generate pe MariaDB
- **Pasi**: aplica modificari de stoc prin "Preluare inventar" cu modul MariaDB activ (motiv M1).
- **Asteptat**: `ExitDestination.StockCorrection` existent si o miscare de tip Intrare se salveaza identic ca in modul demonstrativ SQLite.

---

### H5. Formular nou cu "Nr. crt.", completat de mana (creion inclus) si scanat (30.09.2026)
- **Verifica**: OCR-ul reproiectat (`Services/InventoryPickupOcr.cs`): tabel si coloane detectate din scan, numar curent citit, contrast intins pentru scan slab si creion.
- **Motiv**: M4/M6 - testele automate folosesc formulare sintetice cu cifre desenate cu fonturi Hershey (stilou negru, "creion" gri subtire, scan sters), nu scris real; singurul scan real disponibil este cel vechi, cu 3 coloane.
- **Pasi**: genereaza o situatie de inventar noua (pagina Inventar), tipareste-o, completeaza "Valoare reala" o data cu pix si o data cu creion (inclusiv cifre subtiri, ex. 1 si 7), scaneaza (si o copie mai deschisa) si incarca fisierele in `/inventar/preluare`. Cu `INVENTORY_OCR_DEBUG=1` pornit, jurnalul arata tabelele gasite, liniile verticale si contrastul.
- **Asteptat**: coloana "NR. CRT." din pagina arata numerele tiparite (reluate la 1 pentru fiecare subcategorie), valorile citite corect sau marcate "de verificat", niciun rand pierdut.
- **Rezultat partial 30.09.2026**: un formular nou, tiparit, completat de mana si scanat (Konica Minolta, inclinare -1,5 grade, contrast slab 51-102) a fost incarcat in `/inventar/preluare` pe 5083: 3 randuri in 2 tabele, numere 1,2 si 1, valori 2, 10, 7, fara probleme; "10" a fost recuperat de recitirea cu contrast intins (citirea directa dadea 1). Scanul este fixture (`Fixtures/inventar-proba-nr-crt.pdf`, verificat in suita). Ramas neverificat: alt scaner/rezolutii, un formular cu multe pagini si subcategorii continuate peste pagina, creion foarte deschis.

## Teste efectuate

| Data | Test | Efectuat de | Rezultat |
|---|---|---|---|
| 25.09.2026 | Butonul roșu „Deconectare” din popup (Task 7) | utilizatorul | Trecut: duce la pagina de autentificare |
| 25.09.2026 | Calendarul „Data mișcării” (citire, doar din calendar, fără date viitoare) | utilizatorul | Trecut în Chrome și Brave |
| 25.09.2026 | Blocarea editării: banner, eliberare live, deblocare forțată cu jurnal, heartbeat, pierdere, expirare după închiderea tabului (SQLite, două sesiuni ale aceluiași cont) | agentul (Claude) | Trecut; detalii în `VALIDARE.md` |
| 28.09.2026 | Preluare inventar: OCR pe un formular real (tiparit, completat de mana, scanat de utilizator cu un Kyocera SKM_C3320i) | agentul (Claude), pe fisierul furnizat de utilizator | Trecut partial: 8 din 9 produse cu valoare scrisa citite corect (cod si valoare), 1 produs ("Set chei combinate", lasat necompletat) tratat corect ca neinventariat; un produs ("Polizor unghiular") a fost citit cu o valoare gresita (cifra stilizata "8" confundata cu "5") fara sa fie semnalat nesigur — vezi H2. Randul ramane afisat, cu valoarea editabila. Detalii in `VALIDARE.md`. |
| 25.09.2026 | Sincronizare: modificare SQL externă, dialog deschis cu notificare, catalog live în două tab-uri, client SignalR real | agentul (Claude) | Trecut; detalii în `VALIDARE.md` |
| 28.09.2026 | A12 (fost): conectare reala la instanta MariaDB locala (`blazorstoc_dev`, TLS) - toate cele 27 de tabele si 229 de randuri, 18 triggere | agentul (Claude) | Trecut: conectare TLS confirmata (`TLS_AES_256_GCM_SHA384`), date identice cu raportul din documentul de predare |
| 28.09.2026 | Task 2 (Subtask 2.11), verificari reale pe MariaDB izolat (`blazorstoc_test`): produse/categorii/subcategorii (CRUD, concurenta optimista, audit+evenimente), beneficiari (CRUD, CUI duplicat), vehicule (CRUD, numar duplicat), blocari de produs, proiecte (creare+observatie) | agentul (Claude) | Trecut integral prin executie reala; detalii si problemele gasite/corectate in `docs/PROJECT_STATE.md` |
| 28.09.2026 | Task 2 (Subtask 2.12), preview MariaDB (baza de livrare reala): pornire, rute neautentificate, `/health/live`, pagina 404, persistenta la restart | agentul (Claude) | Trecut; autentificarea efectiva ramane A14 (agentul nu introduce parole) |
| 28.09.2026 | A13: re-confirmarea prin executie a miscarilor de stoc (intrare, iesire spre vehicul cu defalcarea stocului, garda de stoc negativ la restituire din vehicul) si a utilizatorilor (creare, autentificare, nume duplicat) pe MariaDB de test (`blazorstoc_test`) | agentul (Claude) | Trecut prin executie reala, dupa corectarea unei a treia probleme (de data asta in scriptul de verificare: garda de stoc negativ testa scenariul gresit - iesire catre vehicul in loc de restituire din vehicul). Comanda de rulare nu a mai fost blocata de clasificatorul de siguranta. In aceeasi rulare a fost gasita o problema noua, separata, la stergerea proiectelor - vezi A15 (fost) mai jos. |
| 28.09.2026 | A17 (fost): conectarea cu contul root la instanta locala MariaDB (`127.0.0.1:3307`) folosind `admin.private.cnf` proaspat copiat in `C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB` | agentul (Claude) | Trecut. Testul initial cu clientul `mysql.exe` din MySQL Workbench 8.0 CE returna acces refuzat, rezultat ambiguu (client MySQL 8.0 vs. server MariaDB). Retestat cu un mic program C# separat (`MySqlConnector` 2.6.2, aceeasi biblioteca client folosita deja de aplicatie, rulat o singura data in directorul scratchpad, nu in proiect) care citeste `admin.private.cnf` fara sa afiseze parola: conectare reusita, `SELECT 1` = 1, `server version = 11.4.13-MariaDB`. Confirma ca parola de root din fisierele sincronizate este cea corecta; eroarea anterioara venea din clientul `mysql.exe` (MySQL 8.0), nu din parola. |
| 28.09.2026 | A15 (fost): stergerea unui proiect (si observatiei lui) pe MariaDB de test (`blazorstoc_test`) - investigata pana la cauza reala si corectata | agentul (Claude) | Trecut dupa corectare. Cauza reala (confirmata prin instrumentare temporara a exceptiei, nu presupusa): nu era un rollback in pasul de arhivare, ci `ProjectRules.CheckCurrent` respingand fals stergerea ca "modificata sau stearsa intre timp". `Project`/`ProjectObservation` sunt record-uri C# ale caror `CreatedAtUtc`/`UpdatedAtUtc` intra in egalitate; `MariaTimeText.Format` scrie aceste coloane text cu precizie de milisecunda (ca sa se potriveasca cu formatul folosit deja de cele 18 triggere), dar `MariaProjectRepository` construia obiectul intors catre apelant direct din `DateTime.UtcNow`, cu precizie mai mare (sub-milisecunda). Cand obiectul era re-citit din baza in aceeasi operatie de Update/Delete (`GetLockedAsync`/`GetObservationLockedAsync`), valoarea re-parsata nu mai era egala bit-cu-bit cu cea din memorie, ori de cate ori componenta sub-milisecunda a lui `DateTime.UtcNow` nu era exact zero - deci interminent, exact cum a fost observat. Exceptia reala (`ProjectOperationException`) era apoi mascata de o exceptie de FK in curatarea proprie a scriptului de test (stergerea beneficiarului, blocata de proiectul ramas viu cand stergerea esua), ceea ce a facut investigatia initiala sa banuiasca gresit pasul de arhivare. Corectat cu un helper nou `MariaTimeText.Now()` (rotunjeste `DateTime.UtcNow` prin acelasi `Format`/`Parse` folosit la scriere/citire) folosit in cele 4 locuri din `Services/MariaProjectRepository.cs` unde se calcula `nowUtc` pentru Create/Update de proiect si observatie. Verificat prin executie reala pe `blazorstoc_test` (5 rulari consecutive dupa corectare, toate `PASS: The deleted project is gone from blazorstoc_test`, `archive_projects` cu exact 1 rand, nicio ramasita in `projects`/`project_observations`) si prin suita implicita de 578 de verificari (fara regresii). Niciun alt tip de entitate Maria (produs, beneficiar, vehicul, utilizator) nu are `CreatedAtUtc`/`UpdatedAtUtc` in record-ul sau, deci nu era afectat de acelasi bug. Detalii in `docs/PROJECT_STATE.md`. |
| 29.09.2026 | Fereastra separata pentru PDF-ul de preluare inventar (`wwwroot/inventory-pickup-pdf-viewer.js`): incarcare reala a unui fisier prin dialogul nativ al sistemului de operare, in Brave | utilizatorul | Trecut: fereastra se deschide dupa ce utilizatorul permite explicit ferestrele pop-up pentru site (comportament asteptat al browserului, documentat in pagina prin mesajul afisat cand fereastra e blocata si butonul "Redeschide formularul PDF"). |
| 29.09.2026 | H1 (partial, doar inclinarea colii): scanare reala furnizata de utilizator a aceluiasi formular, cu sub un grad de inclinare, respinsa initial cu "Nu a fost gasit niciun tabel recunoscut" | agentul (Claude), pe fisierul furnizat de utilizator (`D:\_BlazTest\SKM_C3320i 26092908470.pdf`, acum si `tests/BlazorStoc.Checks/Fixtures/inventar-proba-inclinata.pdf`) | Bug real gasit si corectat, nu doar o limitare acceptata. Instrumentat direct (nu presupus): `InventoryPickupOcrService.FindHorizontalLines` cerea ca un singur rand de pixeli sa atinga 65% cerneala pe latimea tabelului; pe aceasta scanare, cel mai bun rand atingea doar 58%, pentru ca sub jumatate de grad de inclinare a colii imprastie cerneala liniei pe aproximativ 10 randuri de imagine, niciunul atingand singur pragul (confirmat prin masurarea directa a randurilor din imagine, nu ghicit). Corectat cu o dilatare verticala mica (nucleu 1x7) aplicata doar inaintea acestui test, inainte de calculul procentului de cerneala - pragul de 65% ramane neschimbat, deci nu creste riscul de fals-pozitiv pe alt continut. Dupa corectie: toate cele 10 produse din scanare sunt gasite si citite (era 0 inainte). Verificat prin executie reala (harness separat, in afara proiectului) si printr-un test nou in `tests/BlazorStoc.Checks` (611/611 `PASS`, inclusiv cele doua verificari noi pe acest fisier). Ramane deschis doar aspectul general al altor scanere/rezolutii (H1, restul). |
| 29.09.2026 | H1 (inclinare vizibila, cateva grade): o a doua scanare reala furnizata de utilizator, de data asta rotita vizibil (nu doar sub un grad), tot respinsa cu "Nu a fost gasit niciun tabel recunoscut" dupa corectia cu dilatare de mai sus | agentul (Claude), pe fisierul furnizat de utilizator (`D:\_BlazTest\SKM_C3320i 26092908570.pdf`, acum si `tests/BlazorStoc.Checks/Fixtures/inventar-proba-rotita.pdf`) | Bug real, aceeasi cauza de fond, dincolo de ce putea acoperi dilatarea (utila doar pentru sub un grad). Corectat cu o indreptare (deskew) reala a paginii inainte de orice alta geometrie: `InventoryPickupOcrService.FindSkewDegrees` cauta unghiul (intre -8 si +8 grade, cautare in doi pasi - intai din grad in grad, apoi rafinat din zecime in zecime) care maximizeaza varianta profilului de proiectie orizontala (metoda standard de detectie a inclinarii textului/tabelelor: la unghiul corect, liniile de tabel si textul dau un profil cu varfuri ascutite; la orice alt unghi, cerneala se imprastie si varfurile se tociesc), calculat pe o copie miniaturizata (25%) pentru viteza (sub 2 secunde per fisier). Pagina intreaga e rotita (`Rotate`, interpolare liniara, fundal alb) cu unghiul gasit inainte de binarizare, detectia liniilor si calibrarea coloanelor, care raman neschimbate in rest. **Observatie importanta gasita in acest ciclu**: rotirea intregii pagini, chiar la un unghi mic (~0,3 grade), a inrautatit acuratetea cifrelor scrise de mana pe fisierul deja corectat mai sus (interpolarea "inmoaie" usor traseul subtire al cernelii, suficient sa incurce segmentarea cifrelor) - de aceea rotirea se aplica **numai** peste un prag (`MinCorrectedSkewDegrees = 0,6 grade`); sub acest prag ramane activa doar dilatarea din corectia anterioara, care nu are acest efect secundar. Verificat prin executie reala (harness separat) si printr-un test nou in `tests/BlazorStoc.Checks` (613/613 `PASS`, inclusiv 4 verificari pe cele doua fisiere reale problematice - niciuna regresata). Ramane deschis doar aspectul general al altor scanere/rezolutii (H1, restul); un unghi mai mare de 8 grade sau o pagina rotita cu mai mult de o simpla inclinare de alimentare nu sunt acoperite. |

## Meniul Setari / tab Preluare date ANAF (29.09.2026)

- Ca administrator, in browser: meniul contine "Setari"; cu utilizator simplu meniul nu apare, iar `/setari` este refuzat. Tabul ANAF arata cele 5 sectiuni.
- Test real cu `RO9178894` (asteptat: ELECTRIC STANDARD PREST SRL, 11 mapari fara erori) si `28996610`, apoi salvare ciorna, activare, a doua activare si "Revino la activarea precedenta" (nu s-a putut verifica: agentul nu are sesiune de administrator in preview).

## Beneficiari PF/PJ cu preluare ANAF (29.09.2026)

- MariaDB: FACUT 29.09.2026 pe noua instanta `C:\Dev\BlazorStoc-MariaDB` (migrarea 1 aplicata, idempotenta confirmata, `MariaIntegrationChecks` 700 PASS). Aplicatia pornita in mod MariaDB verificata in browser la 29.09.2026 (formularul de beneficiar PJ/PF, ANAF, editare).
- In browser, neverificate: editarea unui beneficiar existent (motiv + confirmare cu campurile noi), indicatorul care trece pe "manual" dupa modificarea unui camp preluat, mesajul pentru firma inactiva fiscal.

## Combobox beneficiar/proiect la iesire (29.09.2026)

- Cititor de ecran real (NVDA/JAWS): structura respecta modelul ARIA combobox + listbox (`role`, `aria-expanded`, `aria-controls`, `aria-activedescendant`, `aria-selected`), dar anuntul optiunilor nu a fost ascultat; de verificat manual pe formularul de iesire (Beneficiar si Proiect).
- Fluxul "+ Adauga beneficiar/proiect" in modul demonstrativ (SQLite, port 5082) si pe ecran tactil: codul este comun cu modul MariaDB verificat, dar nu a fost rulat in browser.
- Restaurarea dupa reincarcarea paginii (F5) pe pagina de adaugare beneficiar: formularul memorat traieste doar cat circuitul Blazor; de confirmat mesajul afisat la intoarcere in acel caz.

## Taburi vehicul si date de expirare (29.09.2026)

**Toate verificate la 30.09.2026 (sectiune inchisa):**
- Aspect si flux in browser (5083, MariaDB reala, verificat de utilizator): pagina `/vehicule/{id}` cu tabul Informatii (etichetele Expirat / Expira in N zile / Valabil), tabul Echipamente, formularul cu cele trei calendare la adaugare si editare.
- MariaDB reala: migrarea 3 este aplicata (coloanele `itp_expiry`, `insurance_expiry`, `rovinieta_expiry`, `DATE NOT NULL`, cu valorile implicite 15.03.2027 / 30.06.2027 / 30.09.2027), iar vehiculele existente (HD-03-ESP, HD-04-ESP) le au; coloanele au supravietuit restaurarilor.
- Suita completa din `tests/BlazorStoc.Checks`: 743 PASS, 0 esecuri.
- Ramane neconfirmat doar ca arhiva vehiculelor sterse nu are coloane pentru date (raman doar in instantaneul JSON al arhivei), decizie deja consemnata in `IMPLEMENTED.md`.

## Eliminarea SQLite (30.09.2026)

- **REZOLVAT 30.09.2026 12:30:** datele din `blazorstoc_test` au fost golite (la cererea utilizatorului) si `MariaIntegrationChecks` au rulat complet: exit 0, 435 PASS in total (394 fara baza + verificarile de integrare), fara FAIL/BUG-BLOCKED. Istoric:
- **Actualizare 30.09.2026 12:15:** schema `blazorstoc_test` a fost adusa la zi (root a acordat contului `blazorstoc_migrator` drepturi DDL pe `blazorstoc\_test`; `--migrate-schema` a aplicat migrarile 1-3). Rularea urmatoare a trecut de beneficiari (406 PASS) si s-a oprit la `Exista deja un beneficiar cu acest CUI` din cauza unui rest de date lasat de rularea intrerupta anterioara; ramane de golit datele din `blazorstoc_test` (nu si schema) si de reluat suita.
- **Neefectuat - MariaIntegrationChecks (motivul initial):** rularea cu `RUN_MARIA_INTEGRATION_CHECKS=1` si `MARIA_TEST_CONFIG_PATH=local-secrets	est-database.private.json` s-a oprit la beneficiari cu eroarea `Table 'blazorstoc_test.beneficiary_work_points' doesn't exist`: schema bazei izolate `blazorstoc_test` (27 de tabele) este mai veche decat migrarile curente. Pasi: aducerea schemei la zi (Task 2, 2.1, cere cont administrativ sau de migrare cu drepturi pe `blazorstoc_test`), apoi rularea comenzii de mai sus si verificarea ca toate sectiunile trec.
- **Neefectuat - verificare vizuala:** pagina de login fara conturile demo si bannerele scoase din Home, Beneficiari, Utilizatori, Editor produs; autentificarea nu a fost facuta de agent (se face de utilizator pe `http://127.0.0.1:5087/`).
- **Acoperire scoasa:** verificarile SQLite pentru concurenta a doua sesiuni, arhivare la stergere, jurnal, stocuri pe vehicule, blocari, evenimente de schimbare si lacatul de intretinere pe backup/restaurare reale nu mai exista pana la refacerea lor pe MariaDB (Task 2, 2.2).
- **Acoperire refacuta (30.09.2026 13:00):** concurenta, arhivarea, jurnalul, miscarile/stocurile pe vehicule, blocarile si evenimentele de schimbare sunt din nou verificate, pe MariaDB, in `MariaExtendedChecks` (517 PASS in 4 rulari). Ramase neverificate automat: lacatul de intretinere cu backup/restaurare reale si comutarea de scheme la restaurare (necesita instanta cu conturile de backup/restaurare pe schema `BlazorStoc`); se verifica manual din pagina `/inventar/restaurare` cand utilizatorul o cere.

## Notificari de expirare (30.09.2026)

- Neverificat vizual: aspectul pe ecran ingust (telefon) al tabelului si al popup-ului, si comportamentul pentru un utilizator cu rol limitat (preluarea si amanarea sunt permise oricarui utilizator autentificat, sabloanele doar administratorului; testat automat, nu in browser).
- Neverificat: reavertizarea reala la sfarsitul unei amanari (testata automat cu ceas simulat, nu asteptand zilele), si actualizarea triunghiului dintr-o a doua fereastra dupa preluarea din prima (interogare la 60 s, neasteptata in browser).
- Verificare manuala recomandata pe baza reala: creeaza un sablon pentru un eveniment cu expirare apropiata (Setari -> Notificari), deschide `/notificari`, preia si amana o notificare, apoi verifica intrarile din Jurnal activitate.

## Notificari depasite, rezolvate si ordonate (30.09.2026)

### N1. Aspectul paginii `/notificari` pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: deschide `/notificari` pe un telefon sau in emulare 375 px cu cel putin o notificare depasita si una nerezolvata; verifica cele doua tabele (derulare orizontala), filele "Active"/"Rezolvate", dialogul si campul de amanare.
- **Asteptat**: tabelele se deruleaza orizontal, fondul rosu ramane lizibil, butoanele dialogului nu se suprapun.

### N2. Utilizator cu rol limitat
- **Motiv**: M2 (o singura sesiune autentificata in panoul Browser). **Pasi**: autentifica-te cu un cont cu rolul Utilizator, deschide `/notificari`, preia, amana si marcheaza ca rezolvata o notificare; incearca Setari -> Notificari.
- **Asteptat**: cele trei operatii merg pentru orice utilizator autentificat, iar sabloanele raman rezervate administratorului (acoperit automat, neconfirmat in browser).

### N3. Rezolvarea automata pe date reale
- **Pasi**: pe baza reala, dupa ce ai reinnoit un ITP (modifici data pe pagina vehiculului), deschide `/notificari` -> "Rezolvate" si citeste motivul; sterge un vehicul cu notificare activa si verifica motivul "Vehiculul a fost sters.".
- **Asteptat**: motivul numeste data veche si cea noua; notificarea nu dispare. (Automat: acoperit cu sursa de test si cu ITP-ul unui vehicul, nu cu stergerea unui vehicul real.)

## Setari notificari: curatarea rezolvatelor (30.09.2026)

### N4. Aspectul subtabului "Setari notificari" pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: deschide Setari -> Notificari -> "Setari notificari" pe un telefon sau in emulare 375 px; bifeaza comutatorul, schimba perioada si apasa "Salveaza setarile" cu cel putin o notificare rezolvata veche.
- **Asteptat**: comutatorul si campul de luni se aseaza pe verticala, iar popup-ul de confirmare se vede complet, cu butoanele nesuprapuse.

### N5. Rularea zilnica pe ceas real
- **Motiv**: M6 (curatarea zilnica ruleaza la prima evaluare din ziua noua; testata cu ceas simulat, nu asteptand miezul noptii). **Pasi**: cu comutatorul activ pe baza reala si o notificare rezolvata veche (rezolvata manual pentru un obiect a carui data s-a schimbat), asteapta prima evaluare a zilei urmatoare (deschide orice pagina dupa miezul noptii) si verifica in Jurnal intrarea "Curatare notificari rezolvate" cu actorul "sistem".
- **Asteptat**: o singura intrare pe zi, doar cand s-a sters ceva; notificarile nerezolvate si cele rezolvate manual pentru evenimente curente raman neatinse.

### N6. Restaurarea dintr-un backup facut inainte de migrarea 6
- **Motiv**: M5 (restaurarea inlocuieste baza; necesita si contul `blazorstoc_restore`, neconfigurat pe aceasta masina). **Pasi**: fa un backup nou (contine `notification_settings`), apoi restaureaza-l; incearca si cu un backup mai vechi.
- **Asteptat**: un backup nou se restaureaza; unul mai vechi este refuzat cu mesajul de structura diferita (manifestul nu contine tabelul nou). Backupurile existente din 29.09.2026 erau oricum anterioare tabelelor notificarilor.

## Puncte de lucru extinse (30.09.2026)

### N7. Aspectul editorului si al galeriei de poze pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: deschide un beneficiar pe telefon sau in emulare 375 px, editeaza un punct de lucru cu 3-4 poze si o descriere lunga.
- **Asteptat**: campurile se aseaza pe verticala, galeria are 2 coloane, butoanele "Sterge" nu se suprapun, tabelul punctelor se deruleaza orizontal.

### N8. Incarcarea de fotografii reale si utilizator cu rol limitat
- **Motiv**: M2 (o singura sesiune autentificata; nu se folosesc fisiere reale mari din panou). **Pasi**: cu un cont cu rol Utilizator, incarca de pe telefon 3-4 fotografii de camera (2-8 MB fiecare) intr-un punct de lucru; incearca si o imagine de peste 10 MB; sterge una.
- **Asteptat**: fotografiile mari se incarca si se vad; cea de peste 10 MB este refuzata cu mesaj, celelalte raman; un cont fara dreptul de operator de beneficiari nu vede butoanele de modificare (acoperit automat, neconfirmat in browser).

### N9. Beneficiari fara adresa si restaurarea dintr-un backup anterior
- **Pasi (a)**: pe baza reala, 2 beneficiari nu au adresa (punctul lor principal are adresa goala); completeaza-o din "Editeaza" beneficiar si verifica ca punctul principal o preia. **Pasi (b)**: fa un backup nou (contine tabelele `service_photos`, `archive_work_points`, `archive_service_photos`), apoi restaureaza-l pe o copie; un backup mai vechi trebuie refuzat cu mesajul de structura diferita.
- **Motiv**: (b) M5 (restaurarea inlocuieste baza; cere si contul `blazorstoc_restore`, neconfigurat pe aceasta masina). **Nota**: pozele de pe disc nu sunt in backup (risc acceptat, `docs/PROPUNERE_CONTRACTE_MENTENANTA.md`, sectiunea 12).

## Contracte de mentenanta si acoperire (30.09.2026)

### N10. Aspectul sectiunii de contracte si al formularului pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: deschide un beneficiar cu 3-4 puncte de lucru pe telefon sau in emulare 375 px; adauga un contract cu toate punctele, editeaza-l, expandeaza lista de puncte, deschide panoul de activare.
- **Asteptat**: tabelul contractelor si cel al punctelor se deruleaza orizontal, calendarele se deschid, butoanele nu se suprapun, textul "Muta aici" si mesajele de stare raman lizibile.

### N11. Mutarea unui punct intre contracte active si stergerea unui contract din interfata
- **Motiv**: M4 (fluxul e acoperit de teste de integrare, dar nu a fost parcurs in browser). **Pasi**: creeaza doua contracte pe acelasi beneficiar; in al doilea bifeaza "Muta aici" pentru un punct al primului; sterge apoi un contract din dialogul "Stergi contractul?" (motiv implicit sau propriu, cuvantul de confirmare).
- **Asteptat**: punctul apare in al doilea contract cu scadenta pastrata, primul contract il pierde; stergerea arhiveaza contractul (Jurnal: "Stergere", tipul "Contracte mentenanta (stergeri)"), iar mutarea apare ca "Mutare punct de lucru in alt contract".

### N12. Rol limitat si restaurare dintr-un backup anterior migrarii 8
- **Motiv**: M2 / M5 (o singura sesiune autentificata; restaurarea cere contul `blazorstoc_restore`, neconfigurat pe aceasta masina). **Pasi**: cu un cont fara dreptul de operator de beneficiari, deschide un beneficiar cu contracte; fa un backup nou (contine `service_contracts`, `service_contract_points`, `archive_service_contracts`) si restaureaza-l pe o copie; un backup mai vechi trebuie refuzat.
- **Asteptat**: fara drept, butoanele de modificare lipsesc (acoperit automat, neconfirmat in browser); backup-ul vechi este refuzat cu mesajul de structura diferita.

## Registru de interventii si campuri de fisiere (30.09.2026)

### N13. Aspectul sectiunii de interventii, al formularului si al paginii /mentenanta pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: pe telefon sau in emulare 375 px deschide un beneficiar cu interventii; inregistreaza una de mentenanta cu varianta "Stabilita de mine", deschide `/mentenanta` (ambele taburi, filtrele de perioada).
- **Asteptat**: tabelele se deruleaza orizontal, cele trei variante de scadenta si calendarul raman lizibile, filtrele nu se suprapun, zona de fisiere ocupa latimea disponibila.

### N14. Fotografii reale la interventii si tragerea cu mouse-ul
- **Motiv**: M4 (testat cu imagini generate si cu evenimente de drag and drop simulate). **Pasi**: pe o interventie deschisa la editare trage cu mouse-ul 2-3 fotografii reale de telefon (cateva MB) direct din Explorer pe zona "Alege fotografii sau trage fisierele aici"; repeta pe editorul de produs (imagine), pe observatia de proiect si pe punctul de lucru.
- **Asteptat**: zona se coloreaza la survol, fisierele se incarca, un fisier de alt tip este ignorat, un fisier lasat langa zona nu se deschide in browser.

### N15. Rol limitat si stergerea unui punct cu interventii din interfata
- **Motiv**: M2 (o singura sesiune autentificata) si M4. **Pasi**: cu un cont fara dreptul de operator de beneficiari deschide un beneficiar cu interventii; apoi, cu administrator, incearca stergerea unui punct de lucru care are interventii si a unui contract cu interventii.
- **Asteptat**: fara drept, butoanele de inregistrare/editare/stergere lipsesc (acoperit automat, neconfirmat in browser); stergerea punctului si a contractului este refuzata cu mesajul despre interventii.

### N16. Restaurare dintr-un backup anterior migrarii 9
- **Motiv**: M5 (restaurarea cere contul `blazorstoc_restore`, neconfigurat pe aceasta masina). **Pasi**: cu un backup facut inainte de migrarea 9 incearca restaurarea; apoi fa un backup nou (contine `service_interventions` si `archive_service_interventions`) si restaureaza-l.
- **Asteptat**: backup-ul vechi este refuzat cu mesajul de structura diferita; cel nou se restaureaza cu interventiile si pozele lor.

## Surse de notificare pentru mentenanta (30.09.2026)

### N17. Formularul de sabloane si lista de notificari pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: pe telefon sau in emulare 375 px deschide Setari -> Notificari -> Sabloane, alege categoria "Mentenanta", completeaza text-exemplu si salveaza; deschide `/notificari` cu mai multe notificari de mentenanta.
- **Asteptat**: lista de marcaje se rupe pe randuri fara sa iasa din ecran, textul de previzualizare ramane lizibil, tabelele de notificari se deruleaza orizontal.

### N18. Evaluarea la interval real cu doua sesiuni
- **Motiv**: M2 (o singura sesiune autentificata; evaluarea ruleaza la 5 minute). **Pasi**: cu un sablon `mentenanta.scadenta` activ, inregistreaza o interventie de mentenanta dintr-o sesiune, apoi asteapta cel mult 5 minute cu `/notificari` deschis in alta sesiune (un utilizator fara drept de operator de beneficiari) si reincarca.
- **Asteptat**: notificarea veche a punctului apare la "Rezolvate" cu motivul "Scadenta interventiei de mentenanta s-a modificat de la ... la ... (ultima interventie de mentenanta: ...)" (jurnal "Rezolvare automata notificare", actor `sistem`); una noua apare doar cand noua scadenta intra in prag.

## Harta de mentenanta (30.09.2026)

### N19. Aspectul hartii si al panoului pe ecran ingust
- **Motiv**: M3 (redimensionarea la dispozitive reale nu este acoperita de panoul Browser). **Pasi**: pe telefon sau in emulare 375 px deschide `/mentenanta/harta`, filtreaza, apasa un marker si un cluster.
- **Asteptat**: harta are latimea ecranului (420 px inaltime), panoul de detalii apare sub ea, filtrele se rup pe randuri, legenda nu iese din ecran, marker-ele se pot atinge.

### N20. Furnizorul de dale indisponibil
- **Motiv**: M4 (nu s-a simulat caderea furnizorului). **Pasi**: seteaza `Map:TileUrl` catre o adresa https inexistenta (sau opreste conexiunea la internet), deschide harta, apoi restabileste conexiunea si apasa "Reincearca".
- **Asteptat**: dupa cel putin 6 dale esuate apare mesajul "Fundalul hartii nu poate fi incarcat", punctele raman afisate si utilizabile; "Reincearca" reincarca dalele, iar mesajul dispare cand o dala se incarca.

### N21. Multe puncte, clusterizare si termenii serverului public OSM
- **Motiv**: M4 (volum real indisponibil) si M6 (termenii unui serviciu extern se verifica de proprietar). **Pasi**: cu 50+ puncte cu coordonate (unele la aceleasi coordonate) deschide harta la zoom mic, apasa un cluster, apoi un grup de puncte de la aceeasi adresa; verifica termenii de utilizare ai furnizorului configurat (OpenStreetMap public: utilizare interna limitata, atribuire, cerere de Referer valid).
- **Asteptat**: clusterele se desfac la apasare, punctele de la aceeasi adresa se desfac in evantai, panoul listeaza celelalte puncte de la aceeasi locatie; termenii permit utilizarea interna preconizata (altfel se schimba `Map:TileUrl` cu un alt furnizor).

### N22. Fereastra separata a hartii pe doua monitoare
- **Motiv**: M4 (un singur monitor si panou de browser in verificare; panoul ascuns nu raporteaza vizibilitatea). **Pasi**: din `/mentenanta/harta` apasa „Fereastra separata", muta fereastra pe al doilea monitor, foloseste aplicatia in fereastra principala (inregistreaza o interventie, modifica un contract sau un punct), apoi apasa un link din panoul ferestrei (fisa beneficiarului) si inchide fereastra principala inainte de un al doilea clic.
- **Asteptat**: in cel mult 30 s harta din fereastra separata reflecta modificarea (antetul „Actualizat la ...", marker-ul pulseaza 8 s), linkurile se deschid in fereastra principala (in tab nou daca a fost inchisa), un al doilea clic pe buton reutilizeaza aceeasi fereastra, iar cu fereastra minimizata nu se fac interogari (se vede in jurnalul serverului) pana la revenire.

### N23. Actualizarea hartii: esec, sesiune expirata si trecerea in ziua urmatoare
- **Motiv**: M4 (nu s-a simulat). **Pasi**: opreste scurt baza de date sau serverul in timp ce harta este deschisa; lasa o harta deschisa peste miezul noptii; lasa fereastra separata deschisa dupa expirarea cookie-ului de autentificare.
- **Asteptat**: la esec harta ramane neschimbata si antetul spune „Actualizarea automata a esuat, se reincearca"; dupa ziua noua culorile se recalculeaza; dupa expirarea sesiunii (sau retragerea accesului) apare mesajul de acces refuzat cu indemnul de a te autentifica din nou.

### N24. Jurnalul complet pe un jurnal foarte mare
- **Motiv**: M4 (volum real indisponibil; baza de test are ~9.400 de evenimente). **Pasi**: populeaza `audit_events` cu 200.000 de randuri (in baza de test), deschide `/jurnal/complet?tot=1`, cauta un text rar si un text frecvent, filtreaza pe operator si tip, mergi la ultima pagina si exporta.
- **Asteptat**: fiecare interogare raspunde in mai putin de ~1-2 s; daca cautarea de text sau numaratoarea depasesc acest prag, se adauga indecsi (prefix pe `entity_type`, `action`, `actor_username`) printr-o migrare cu contul de migrare, sau `FULLTEXT` pentru text.

### N25. Descarcarea fisierului CSV si aspectul jurnalelor pe ecran ingust
- **Motiv**: M4 (panoul de browser nu salveaza fisiere). **Pasi**: exporta din `/jurnal/complet`, deschide fisierul in Excel (setari regionale romanesti); deschide ambele jurnale pe telefon.
- **Asteptat**: diacriticele se citesc corect (UTF-8 cu BOM), coloanele se separa cu `;`, o valoare care incepe cu `=` apare cu apostrof si nu se calculeaza; paginile raman utilizabile pe ecran ingust.

### N26. Schimbarea furnizorului de harta pe o harta deschisa, cu cheie
- **Motiv**: M4 (panoul de verificare ascuns nu ruleaza reincarcarea automata; nu exista cheie de furnizor de test). **Pasi**: deschide harta (si fereastra separata), in Setari → Harta → Motor harta schimba adresa dalelor intr-un furnizor cu `{key}` si cheie, „Testeaza furnizorul", salveaza; asteapta 30 s fara sa reincarci harta.
- **Asteptat**: fundalul se schimba pe harta deschisa (atribuirea si limitele de zoom se actualizeaza, pozitia si punctul selectat raman), cheia nu apare nicaieri in pagina de setari, in jurnal sau in sursa paginii setarilor; cu o cheie gresita apare mesajul de dale indisponibile.

### N27. Tipuri de pinuri pe ecran ingust si culorile predefinite
- **Motiv**: M4. **Pasi**: deschide Setari → Harta → Overlay si harta pe telefon; compara culorile simbolurilor pe pinii predefiniti.
- **Asteptat**: lista si formularul raman utilizabile pe ecran ingust; simbolurile sunt lizibile pe toate culorile (alb sau inchis, dupa contrast); daca aspectul dinainte (simboluri albe pe gri si portocaliu) este preferat, se schimba culorile predefinite in `MapPinRules.Defaults`.

## Sabloane de facturi (30.09.2026)

### N28. Facturi scanate reale (scaner sau telefon)
- **Motiv**: M4 (nu exista scanari reale de facturi; testele folosesc pagini generate si rasterizate, cu inclinare si rotire simulate). **Pasi**: in Setari → Facturi incarca 3-4 facturi scanate reale (de pe scanerul Konica Minolta, poze de pe telefon, o factura cu stampila peste text, una cu contrast slab); verifica „Citit prin recunoasterea imaginii (OCR)", tabelul, valorile si campurile; ruleaza si `dotnet run --project tests/BlazorStoc.InvoiceCorpus -- <director>`.
- **Asteptat**: tabelul si cel putin campurile principale (numar, data, CUI, total) se gasesc; valorile incerte se corecteaza in sablon; o factura care esueaza se salveaza (anonimizata) ca fixtura si devine test inainte de corectarea motorului.

### N29. Desenarea cu mouse-ul pe pagina
- **Motiv**: M4 (verificat in browser numai cu evenimente de pointer sintetice). **Pasi**: apasa „Adauga camp desenand pe pagina", trage un dreptunghi peste o valoare, apoi „Adauga coloana desenand"; incearca si pe ecran tactil.
- **Asteptat**: dreptunghiul apare in timpul tragerii, campul/coloana se adauga si valoarea citita apare in panou; pe ecran tactil pagina nu deruleaza cat timp modul de desenare este pornit.

### N30. Banca de lucru pe ecran ingust
- **Motiv**: M4. **Pasi**: deschide Setari → Facturi → Analiza pe telefon sau in emulare 375 px, incarca o factura.
- **Asteptat**: pagina si panoul se aseaza una sub alta, lista si formularele raman utilizabile, tabelul de previzualizare deruleaza pe orizontala.

### N31. Facturi reale de alte tipuri
- **Motiv**: M1 (corpusul real are trei facturi e-Factura). **Pasi**: adauga in corpus facturi din alte programe de facturare (nu e-Factura), in valuta, cu storno (cantitati negative), cu TVA pe mai multe cote, cu antet care se repeta, cu tabel fara linii; ruleaza raportul de corpus si `--transfer`.
- **Asteptat**: fiecare produce un sablon utilizabil cu cel mult corectii minore; ce nu se citeste corect devine test in `InvoiceChecks.cs` si se corecteaza in dictionar sau in motor.

### N32. Durata pentru fisiere scanate mari
- **Motiv**: M4 (un OCR de 30 de pagini dureaza cateva minute si nu exista indicator de progres). **Pasi**: incarca un PDF scanat de 20-30 de pagini.
- **Asteptat**: analiza se termina fara sa se piarda sesiunea; daca durata este prea mare se adauga indicator de progres sau o limita mai mica de pagini pentru OCR.

### N33. Restaurare dintr-un backup anterior migrarii 10 si ciocniri intre administratori
- **Motiv**: M2 (un singur administrator; restaurarea pe baza reala nu se ruleaza in verificari). **Pasi**: restaureaza un backup facut inainte de migrarea 10 si reporneste aplicatia (tabelele de sabloane se recreeaza prin migrare); in doua ferestre editeaza acelasi sablon si salveaza pe rand ca versiune noua.
- **Asteptat**: dupa restaurare Setari → Facturi functioneaza (lista poate fi goala); a doua salvare primeste „Sablonul a fost modificat sau sters intre timp".

## Preluare factura si editare de sabloane (02.10.2026)

### N34. Liniile de demarcare din Preluare factura, trase cu mouse-ul
- **Motiv**: M1 (tragerea reala cu mouse-ul nu se ruleaza in verificari automate; logica de citire intre linii este testata). **Pasi**: Produse → Preluare factura, incarca o factura (cu linii trase, una fara linii, una scanata); trage o linie in sus/jos, trage capetele ei, sterge-o (buton, × si Delete), adauga una cu „Adauga linie de demarcare" si clic pe pagina; schimba pagina la o factura pe mai multe pagini.
- **Asteptat**: liniile urmeaza mouse-ul, randurile din tabel se citesc din nou dupa fiecare modificare, ce este sub ultima linie nu apare, o singura linie nu da niciun rand; pe o scanare liniile cad pe pagina indreptata.

### N35. Campul cu etichete colorate (cursor, culori, simbol pentru spatiu)
- **Motiv**: M1 (aspectul si pozitia cursorului depind de browser si de rezolutie; verificat doar la compilare). **Pasi**: in Setari → Facturi → descrierea intrarii in stoc a produsului si la Notificari → Sabloane (Subiect si Text): apasa etichete, scrie intre ele, selecteaza text; incearca pe un ecran 4K si in Brave, Chrome, Edge.
- **Asteptat**: eticheta se insereaza la cursor si cursorul ramane dupa ea, etichetele sunt albastre (cele necunoscute rosii), fiecare spatiu din afara etichetelor are simbolul magenta vizibil, iar textul colorat si cel tastat se suprapun exact (fara deplasare, si cu text lung si defilare).

### N36. Sub-tab-ul „Editare sablon" si avertizarea la parasire
- **Motiv**: M1 (fluxul cu mai multe tab-uri si dialogul de avertizare nu au fost rulate in browser). **Pasi**: ✎ pe un sablon; modifica un element; apasa pe alt sub-tab, pe alt tab din Setari, pe o intrare din meniu, pe Inapoi, pe ✎ al altui sablon, pe „Inchide editarea"; repeta fara modificari; salveaza cu „Salveaza" si cu „Salveaza ca sablon nou" (cu aceeasi denumire, apoi cu alta).
- **Asteptat**: tasta Delete sterge elementul activ al sablonului (si Ctrl+Z anuleaza) chiar si cand panourile Generare si Editare sunt amandoua pe pagina; cu modificari apare „editare neterminata" si, la „Parasesc", sub-tab-ul dispare; fara modificari iesirea e imediata; sub-tab-ul apare numai cat editarea e activa; „Salveaza ca sablon nou" refuza aceeasi denumire; la deschidere sablonul apare exact ca salvat peste PDF-ul model, fara citire pana la „Analizeaza fisierul".

### N37. Comutatorul Activ/Inactiv din lista de sabloane
- **Motiv**: M1 (aspectul in tabel si propunerea sabloanelor depind de browser si de date reale). **Pasi**: Setari → Facturi → Sabloane salvate: comuta un sablon, reincarca pagina, incarca o factura a aceluiasi furnizor in Generare sablon si in Preluare factura; verifica jurnalul.
- **Asteptat**: bara mica aliniata cu textul „Activ" (verde) / „Inactiv" (gri); un sablon inactiv nu este propus; jurnalul are „Activare/Dezactivare sablon factura".

### N38. Migrarea 12, restaurare si factura reala Emproium
- **Motiv**: M2/M3 (nu s-a rulat pe o baza cu sabloane existente, iar verificarea cu facturile reale esueaza la una din ele). **Pasi**: aplica migrarea 12 pe o baza cu sabloane salvate si restaureaza un backup anterior; ruleaza `tests/BlazorStoc.Checks` cu `INVOICE_CORPUS_DIR=D:\_BlazTest\Facturi furnizori` si analizeaza `Emproium 09.11.2026 - 640.pdf` cu `tests/BlazorStoc.InvoiceCorpus`.
- **Asteptat**: toate sabloanele existente raman active; pentru factura Emproium (scanata, cu „Discount +tva" si „TIMBRU VERDE") suma randurilor se potriveste cu totalul fara TVA sau esecul se transforma intr-un test cu o fixtura generata si se corecteaza motorul.

## N39 - Etichete din denumirile proprii ale sablonului (02.10.2026)

De verificat in browser (dupa Ctrl+F5): (1) coloana „Taxa verde" cu sensul Cota TVA apare in „Coloane folosite din tabel" ca `<Taxa verde>`; (2) la analiza unui PDF, etichetele campurilor din antet sunt textele din factura; (3) stergerea unei coloane/unui camp de pe overlay scoate butonul etichetei si marcajele ei din descriere; (4) un sablon salvat inainte se deschide cu descrierea adusa la denumirile coloanelor.

## N40 - Operatii aritmetice noi si redenumirea etichetelor (02.10.2026)

De verificat in browser: (1) butoanele SCADERE{}, INMULTIRE{}, IMPARTIRE{} se insereaza la cursor cu cursorul intre acolade, iar etichetele apasate dupa prima primesc automat simbolul (+ - * /) intre ele; (2) simbolurile apar in culoarea operatiei; (3) IMPARTIRE{} cu 3 etichete blocheaza salvarea; (4) schimbarea „Denumirii din antet" a unei coloane (sau „Etichetei" unui camp) redenumeste marcajul in descriere.

## N41 - Cap de tabel cu celule, zone de coloana ancorate (02.10.2026)

De verificat in browser pe `905550.pdf` (Generare sablon) si pe un sablon salvat: (1) capul de tabel se selecteaza si se redimensioneaza sus/jos, celulele lui au 8 manere si latimea lor devine latimea coloanei; (2) zona unei coloane are doar manere sus/jos, iar dupa tragere dialogul coloanei arata elementele de deasupra/dedesubt de care e legata; (3) „Adauga celula de antet" fara „Deseneaza zona" blocheaza salvarea cu mesajul din Probleme; (4) stergerea celulei sau a zonei sterge coloana; (5) pe o factura cu rand de numere („0 1 2 ...") randul 0 nu apare ca produs si coloanele incep sub el; (6) factura de mai multe pagini cu antet repetat: zona se opreste la elementul de jos.


## N42 - Linii pe liniile tabelului, pagina 2 si etichete unice (03.10.2026)

De verificat in browser (dupa Ctrl+F5) cu `875289.pdf` si sablonul SC TELESYSTEM SRL: (1) pe pagina 1 cele 10 linii sunt pe liniile tabelului si randurile 1-9 sunt separate; (2) pe pagina 2 apar linii peste tabel (randurile 10-16, fara randul de antet); (3) mutarea/stergerea unei linii de pe pagina 2 reciteste randurile; (4) in sablon, un camp desenat manual si denumit afiseaza denumirea in titlul dialogului si pe pagina; (5) doua campuri cu aceeasi eticheta nu permit inchiderea dialogului, iar „Foloseste ... (2)" le diferentiaza; (6) numarul facturii citit cu sablonul este 111087721; daca in aplicatie apare altceva, sablonul din baza utilizatorului difera de cel verificat. Neverificat: OCR citeste CUI-ul furnizorului ca RO2Z2460883 (cu o litera in plus), deci nu se potriveste cu CUI-ul din sablon; sablonul se propune dupa asemanarea de aspect.

## N43 - Wizard de preluare factura (03.10.2026)

De verificat in browser (dupa Ctrl+F5), cu `875289.pdf`: (1) pasul 1: overlay-ul cu campuri, cap de tabel si coloane colorate ramane in interiorul tabelului pe ambele pagini, iar bifele il arata/ascund; bifele de rand estompeaza randul; (2) pasul 2: un cod existent in catalog da „Potrivire exacta" (de creat un produs cu un cod din factura si reluat), un cod apropiat da alternative cu radiobox; popup-ul „Pregateste un produs nou" nu scrie in baza (catalogul ramane neschimbat pana la pasul 3); „Adauga categorie / subcategorie" deschide popup-ul in primplan si o categorie noua apare in lista dupa „Gata, inapoi la produs"; (3) pasul 3: data nu poate fi in viitor; descrierea fiecarei intrari este cea din sablon si se poate edita; **„Finalizeaza preluarea" nu a fost incercata** (scrie produse si intrari in baza): de verificat pe o baza de proba ca produsele noi apar cu imaginea, intrarile cu data si descrierea aleasa, ca un rand salvat nu se dubleaza la o reincercare dupa o eroare si ca jurnalul are evenimentele de creare produs si de intrare in stoc.

## N44 - Preluare factura (pasul 2), motive automate, categorii, facturi Secpral si Emproium (05.10.2026)

De verificat in browser (dupa Ctrl+F5): (1) pasul 2 pe o factura reala: un camp de cantitate sau pret necitit ca numar are fundal rosu si, sub denumire, mesajul cu iconita „!"; in campurile numerice nu se pot tasta litere, a doua virgula sau alte caractere (punctul devine virgula), nici prin lipire; denumirea lunga se vede intreaga, UM e mai ingusta; (2) „Afiseaza factura intr-o fereastra noua" deschide fereastra detasabila cu paginile; (3) intoarcerea din pasul 3 sau 1 in pasul 2 pastreaza corectiile, produsele pregatite si descrierile editate; (4) formularul de produs: „Cauta imaginea pe internet" deschide popup cu rezultatele, la produs nou si la editare; (5) formularele de editare (produs, beneficiar, utilizator, vehicul, proiect, observatie, categorie, subcategorie, miscare de stoc): radiobox intre motivul generat (o modificare pe linie, se actualizeaza pe masura ce se lucreaza, o modificare pusa inapoi dispare) si cel scris; dialogul de confirmare arata motivul ales; **editarea unei miscari de stoc nu are test automat**; (6) „+ Adauga subcategorie" deschide formularul sub ultima subcategorie a categoriei; (7) pe `Secpral 06.26.2026 - 6489.pdf` sablonarea gaseste tabelul (rand cu „§23.38" in loc de „523.38" de corectat), iar pe `Emproium 09.11.2026 - 640.pdf` randurile 1-5 sunt separate; randul 5 contine si liniile TIMBRU VERDE (numerotarea OCR da de trei ori „5"); (8) salvarea finala din pasul 3 dupa `GRANT SELECT` pentru `blazorstoc_restore` (verificat doar prin insert cu rollback).

## N45 - Motor geometric, laborator OCR, recitirea numerelor (05.10.2026, ramura `experiment/ocr-geometrie`)

Nota 05.10.2026 (trecere pe main): motorul A a fost eliminat, exista un singur motor; sabloanele salvate (Telesystem 22460883, General Security 11160619) au fost sterse din baza de dezvoltare si trebuie refacute din Setari -> Facturi, apoi verificate la preluare. Backup al lor: dump in scratchpad-ul sesiunii (nu e in repository).

De verificat in browser pe `/produse/preluare-factura` (preview 5087 cu `Invoices__Lab=true`, `Invoices__LabReferencesDirectory=D:\_BlazTest\Facturi furnizori\references`): (1) `factura fictiva.pdf`: pasul 1 are un singur rand (randul de numere de coloane „0 1 2 ..." nu se lipeste de primul rand), iar prima linie de separare este sub el; (2) `PC Garage...pdf`: coloanele colorate de pe overlay acopera tot ce e sub antet (denumirea pe toata latimea, nu doar sub eticheta); (3) `Emporium 09.11.2026 - 640.pdf`: cantitati 1, pret 942.12, TVA 197.85, pretul 38.33 de la randul 5 fara bara, toate cele 5 randuri fara avertisment aritmetic; (4) `682100.pdf` pagina 2: cantitatile citite (12, 4, 2, 1, 1, 2, 5, 5, 2, 3); (5) `Facturis.ro-1.pdf` si `Facturis.ro 3.pdf` au tabel (2 si 6 randuri; valori blurate de corectat); (6) panoul de laborator: tabelul de comparare, „Salveaza ca referinta" (referintele existente sunt ciorne scrise din motorul A, de corectat manual); (7) coloane duplicate neutilizate pe overlay la GS 385502, Mondoplast si 682100 (limita cunoscuta, nerezolvata); (8) `model-factura-proforma.pdf`: pasul 1 citeste 2 randuri din 3 (limita cunoscuta, lasata deoparte). **Nu au test automat cu fisiere reale:** recitirea numerelor prin interfata, panoul de laborator pe o factura incarcata. Verificarea pe mostrele reale (`INVOICE_CORPUS_DIR` implicit) ramane picata la scanarea Emproium (CUI si „Total fara TVA" din antet nerecunoscute dupa OCR).

## N46 - Sabloane de facturi la preluare, acces pentru utilizatori obisnuiti, comutatoare On/Off, selectie pe randuri (06.10.2026)

Rezolvat: N38 (factura reala Emproium) - verificarea trece, vezi `IMPLEMENTED.md` (corectii la motorul de facturi, 06.10.2026).

De verificat in browser (dupa Ctrl+F5): (1) cont cu rolul „Utilizator" (nu administrator): meniul Setari apare, `/setari` arata numai tabul Facturi, „Generare sablon factura" si „Sabloane salvate" merg, nu exista iconita de stergere a sablonului, adresele `/setari?tab=anaf` etc. nu deschid alte taburi; (2) acelasi cont: preluare factura fara sablon -> avertizare -> „Creeaza sablon" -> salvare -> aplicare; jurnalul numeste utilizatorul; (3) `ProductMovements`: verificat in browser la 06.10.2026 (avertizarea apare peste dialog, „Paraseste" inchide ambele), ramane de verificat pe ecran ingust; (4) comutatoarele din ANAF, sabloane de notificari, contracte si mentenanta (Arata si contractele Off), utilizatori (Cont activ), selectorul de proiect de la iesiri: aspect, navigare cu Tab si Space, ecran ingust (telefon), contrast cu `prefers-reduced-motion`; (5) selectia pe randuri la Inventar si Preluare inventar (clic pe rand, marcaj rotund, liniuta la selectie partiala, campul „Valoare reala" nu alege randul); (6) fereastra de creare sablon pe telefon si pe ecran ingust (dialog aproape pe tot ecranul, derulare).
Neverificat automat: tabul Setari al administratorului in `bUnit` (ar cere servicii ANAF in test).

## N47 - Furnizori, facturi de furnizor si preluare cu verificare in pasul 2 (06.10.2026)

Motiv: M1/M4 (aspectul pe ecran si ANAF real nu se pot verifica automat; testele folosesc un ANAF simulat). De verificat in browser (dupa Ctrl+F5): (1) Administrare -> Furnizori: lista, cautare, filtrul „Doar cei de verificat", iconita de stergere dezactivata pentru un furnizor cu facturi si pentru un utilizator obisnuit; (2) furnizor nou cu un CUI real, cu ANAF activ: formularul se completeaza, sursa este „Date preluate din ANAF"; modificarea adresei -> „Preluate din ANAF, editate manual"; (3) cu ANAF oprit sau dezactivat din Setari -> furnizor salvat ca „Introdus manual (ANAF indisponibil)"; cu un CUI valid dar necunoscut ANAF -> „CUI negasit"; (4) furnizor UE (de exemplu DE + 9 cifre): fara buton ANAF, sursa „Introdus manual"; (5) Produse -> Preluare factura: in pasul 2 cele trei campuri (numar, CUI, data) sunt „neverificate", fiecare se confirma separat, editarea unuia il readuce la „neverificat", pasul 3 ramane inchis pana la cele trei confirmari; un CUI nou se adauga din fereastra; (6) aceeasi factura preluata a doua oara (si cu un numar citit gresit, de exemplu „I" in loc de „1"): se ofera factura existenta, produsele deja preluate sunt marcate; (7) pagina de miscari a produsului arata factura si furnizorul pe intrarile preluate; (8) jurnalul: „Adaugare furnizor", „Modificare furnizor", „Reverificare furnizor ANAF", „Inregistrare factura furnizor".
Neverificat automat: raspunsul real al ANAF pentru un CUI inexistent (forma listei `notFound`: numere sau obiecte - ambele sunt tratate), verificarea cu un cont „Utilizator" in browser, migrarea 13 pe baza `BlazorStoc` reala (se aplica la pornirea preview-ului cu contul de migrare; pe baza `blazorstoc_test` a fost aplicata cu contul root, pentru ca `blazorstoc_migrator` nu are drepturi pe ea pe acest calculator).

Beneficiari - cheia CUI fara prefix RO (06.10.2026): in browser, creeaza un beneficiar cu CUI `RO12345678`, apoi incearca `12345678` si `ro 12345678` -> mesajul de CUI duplicat. Pe o baza cu beneficiari existenti verifica ca nu raman chei cu prefix RO (`SELECT cui, normalized_cui FROM beneficiaries`); pe dev a fost verificat doar prin SQL.

#### Pagina Facturi (07.10.2026)
- **Ce s-a adaugat:** Pagina `/facturi` (din Administrare si de pe pagina principala): lista tuturor facturilor de furnizor, casete de rezumat, filtre si intrarile fiecarei facturi.
- **Ce face acum:** Poti vedea si filtra toate facturile (text, furnizor, date, doar cele fara intrari) si deschide intrarile unei facturi; arata bine si pe ecran ingust.
- **De verificat:** In browser: casetele de rezumat, filtrele, deschiderea intrarilor, ecranul ingust. Nu s-a rulat in browser.

#### Facturi - corectare si stergere (07.10.2026)
- **Ce s-a adaugat:** Corectarea numarului, a datei si a furnizorului unei facturi si stergerea unei facturi fara intrari, din `/facturi`.
- **Ce face acum:** Administratorul corecteaza o factura (jurnal cu cate un eveniment pe fel de schimbare), numarul duplicat la acelasi furnizor e refuzat, o factura fara intrari se sterge, iar una cu intrari nu (butonul × dezactivat); un utilizator limitat nu are ✎ si ×.
- **De verificat:** Ca administrator si ca utilizator limitat, in browser, pasii de mai sus. SQL-ul pe MariaDB reala a fost rulat ulterior (vezi intrarea urmatoare).

#### Facturi - SQL pe baza reala (07.10.2026)
- **Ce s-a adaugat:** Verificarea pe `blazorstoc_test` a corectarii, stergerii si listarii facturilor (sectiunea „Suppliers and invoices").
- **Ce face acum:** Corectare, copie veche, numar duplicat, motiv lipsa, acces interzis, jurnal exact, stergere blocata cu intrari si permisa fara - toate trec pe baza reala.
- **De verificat:** Ramane doar verificarea vizuala in browser.

#### Intrari libere, furnizor/factura pe pagina de miscari, atasare la factura, factura asteptata (07.10.2026)
- **Ce s-a adaugat:** Selector de provenienta la intrare (intrare libera cu motiv, factura existenta, factura noua), fila „Intrari fara factura", atasarea unei intrari la o factura, intrarea cu factura asteptata (cu notificare).
- **Ce face acum:** O intrare se inregistreaza cu sau fara factura, iar cele fara factura raman in urmarire pana se leaga de factura furnizorului.
- **De verificat:** Verificare vizuala in preview a formularului, filei si dialogurilor. Sectiunea MariaDB „Free entries" a rulat dupa acordarea drepturilor (vezi intrarea urmatoare).

#### Drepturi migrator pe blazorstoc_test si verificare intrari libere (07.10.2026)
- **Ce s-a adaugat:** Contul de migrare are drepturi DDL pe `blazorstoc_test`; migrarea 18 aplicata si sectiunea „Free entries" rulata.
- **Ce face acum:** Testele MariaDB pentru intrari libere pot rula pe baza de test.
- **De verificat:** Verificare vizuala in preview a formularului, filei „Intrari fara factura" si dialogurilor.

#### Regularizare stoc negativ, iesiri peste stoc, scenarii de utilizare intrari/iesiri (07.10.2026)
- **Ce s-a adaugat:** Panou de regularizare a stocului negativ, banda de avertizare, taburile paginii de miscari si scenarii de utilizare.
- **Ce face acum:** O iesire peste stoc se inregistreaza oricand; produsul ramane in „De regularizat" pana se regularizeaza stocul.
- **De verificat:** Verificare vizuala in preview a panoului, benzii si taburilor; scenariile 18-19 (preluare factura cu stoc negativ) doar prin cod, fara test de pagina.

#### Iesiri: jurnal pe destinatie, referinta, filtre, iesire dublata, folosire din vehicul peste cantitate (07.10.2026)
- **Ce s-a adaugat:** Campul Referinta la iesiri, jurnal cu actiunea exacta pe destinatie, filtrele Destinatie/Beneficiar/Vehicul, avertisment pentru iesire dublata, nota de depasire a cantitatii din masina.
- **Ce face acum:** Iesirile au referinta (aviz, bon), se pot filtra, o iesire repetata in aceeasi zi cere motiv, iar jurnalul spune exact tipul iesirii.
- **De verificat:** In previzualizare, pe pagina produsului: campul Referinta, filtrele, panoul de iesire dublata si nota de depasire a cantitatii din masina.

#### Director Teste utilizator: ghiduri de testare pe componenta (07.10.2026)
- **Ce s-a adaugat:** Directorul `Teste utilizator/` cu cate un fisier pe componenta si indexul `00_CUM_SE_FOLOSESTE.md`.
- **Ce face acum:** Pasii de testare ai utilizatorului sunt pe componenta; la fiecare commit/push se actualizeaza (pasi noi cu data, cei confirmati se scot si trec aici) si directorul intra in commit si push.
- **De verificat:** Nimic de testat; regula este in CLAUDE.md.

#### Lista De regularizat, cauza iesirii peste stoc, notificare, contor, inventar, reimprospatare fara licarire (07.10.2026)
- **Ce s-a adaugat:** Tab „De regularizat" pe pagina principala, cauza optionala a iesirii peste stoc, contor pe Dashboard, notificari, inventar care tine cont de stoc negativ, reimprospatare fara licarire.
- **Ce face acum:** Produsele cu stoc negativ se vad intr-o lista cu cauza si vechime; paginile se actualizeaza singure fara sa palpaie.
- **De verificat:** Pasii sunt in Teste utilizator/02, 05, 09, 10; reimprospatarea fara licarire: Teste utilizator/02 pasii 12-13.

#### Operatia de iesire si iesirea multipla (cosul de iesiri) (07.10.2026)
- **Ce s-a adaugat:** Pagina `/iesiri/multipla`: o operatie cu mai multe produse, o destinatie comuna si previzualizare inainte de salvare (se salveaza intreaga sau deloc).
- **Ce face acum:** Mai multe produse ies intr-o singura operatie, cu avertismente (peste stoc, iesire repetata) afisate inainte de salvare.
- **De verificat:** Pasii sunt in Teste utilizator/02 (sectiunea Iesire multipla).

#### Bon de consum, storno de operatie, retur legat de iesire si consum net (07.10.2026)
- **Ce s-a adaugat:** Bon de consum / aviz de predare (PDF), storno de operatie, retur legat de o iesire (partial), consum net pe proiect si beneficiar.
- **Ce face acum:** Fiecare operatie de iesire are bon PDF; o operatie gresita se stornează (stocul revine, iesirile raman ca urma); returul scade consumul net.
- **De verificat:** Pasii sunt in Teste utilizator/02.

#### Nomenclator: Tipuri de sisteme (07.10.2026)
- **Ce s-a adaugat:** Pagina `/nomenclator` cu tipurile de sisteme (adaugare, redenumire, activare, ordine, denumiri alternative).
- **Ce face acum:** Exista o lista proprie de tipuri de sisteme din care se aleg componentele proiectelor.
- **De verificat:** Pasii sunt in Teste utilizator/12_Nomenclator.md.

#### Componente pe proiect (tipuri de sisteme, stari, arhivare) (07.10.2026)
- **Ce s-a adaugat:** Sectiunea Componente pe pagina proiectului si alegerea componentelor la crearea proiectului.
- **Ce face acum:** Un proiect are componente cu stare (Ofertata, In executie, Predata, Inchisa); scoaterea unei componente o arhiveaza cu motiv si poate fi reactivata.
- **De verificat:** Pasii sunt in Teste utilizator/06_Beneficiari_si_proiecte.md.

#### Oferte: sabloane de devize-oferta xlsx (07.10.2026)
- **Ce s-a adaugat:** Pagina `/oferte/sabloane`: sabloane care spun ce coloane si sectiuni se citesc dintr-un fisier .xlsx de oferta.
- **Ce face acum:** Un devizul-oferta .xlsx se citeste pe baza unui sablon propus automat si editabil.
- **De verificat:** Pasii sunt in Teste utilizator/13_Oferte_sabloane.md.

#### Oferte: preluarea devizului-oferta (asistent, beneficiar, proiect, componenta, linii, revizii) (07.10.2026)
- **Ce s-a adaugat:** Asistentul in 5 pasi `/oferte/preluare` si lista `/oferte`: potrivire beneficiar si produse, creare proiect/componenta, revizii cu diferente.
- **Ce face acum:** O oferta se preia cu liniile legate de produsele din catalog; acelasi numar de oferta nou devine revizie cu diferentele afisate.
- **De verificat:** Pasii sunt in Teste utilizator/14_Oferte_preluare.md.

#### Situatia proiectului pe componente (07.10.2026)
- **Ce s-a adaugat:** Pagina `/proiecte/{id}/situatie`: pe componenta si linie de oferta, necesar, predat (net de retururi), din stoc, deficit si stare; lista de achizitie pe furnizor; export PDF si CSV; coloana „Predat X din Y" pe componente.
- **Ce face acum:** Vezi dintr-o privire ce lipseste unui proiect si ce trebuie cumparat, fara preturi.
- **De verificat:** In browser: pagina, exporturile PDF/CSV si coloana Predat din Componente (Teste utilizator/06).

#### Legarea iesirilor de componente si regula de scoatere a componentei (07.10.2026)
- **Ce s-a adaugat:** Selectorul „Componenta" la iesirea spre un proiect (formularul de pe produs si Iesire multipla) si dialogul de lamurire la scoaterea unei componente (migrarea 26).
- **Ce face acum:** Iesirea se leaga automat de componenta (sau „in afara ofertei"), iar o componenta cu iesiri nelamurite nu se scoate pana nu alegi pe fiecare produs: retur in depozit, ramas la beneficiar, mutat sau consumat.
- **De verificat:** In browser: selectorul si dialogul (Teste utilizator/06); migrarea 26 pe baza BlazorStoc se aplica la pornirea previzualizarii.

#### Rezervari pe proiect si avertismente la iesire (07.10.2026)
- **Ce s-a adaugat:** Rezervari pe proiect (migrarea 27): coloana „Din care rezervat" si butoane „Rezerva cat se poate" pe Situatie, rezervare manuala, lista rezervarilor cu scadere, „Rezervat / stoc liber" pe pagina produsului, avertisment la iesire, propuneri dupa intrare.
- **Ce face acum:** O rezervare tine bucati pentru un proiect fara sa schimbe stocul si scade stocul liber; iesirea consuma intai rezervarea proiectului, iar daca ar lua bucati rezervate de alte proiecte avertizeaza (fara sa blocheze) si lasa sa continui sau sa scazi o rezervare cu motiv; scoaterea unei componente elibereaza rezervarile ei.
- **De verificat:** In browser: Situatia proiectului, avertismentul la iesire (pagina produsului si Iesire multipla), propunerile dupa intrare, selectorul „Pentru oferta" (Teste utilizator/06); migrarea 27 pe baza BlazorStoc se aplica la pornirea previzualizarii.

#### Rezervari si legarea de oferta la preluarea facturii (07.10.2026)
- **Ce s-a adaugat:** Selectorul „Pentru oferta" in pasul 3 al preluarii facturii si in dialogul „Adauga produs la factura"; propuneri de rezervare dupa finalizarea preluarii.
- **Ce face acum:** O intrare din factura poate fi legata de componenta ofertei pe care o aproviziona (propunere automata cand produsul e intr-o singura componenta) si apare in Situatie ca „Intrat pt. oferta"; dupa preluare vezi cine poate rezerva din stocul liber.
- **De verificat:** In browser: selectorul si propunerile (Teste utilizator/03).

Completari stoc: iesire rapida, Unde sunt bucatile, completare vehicul la nivel, stoc minim, export consum, notificari de stoc (08.10.2026): Task completari stoc: totul de verificat in browser (vezi Teste utilizator 01, 06, 07, 09); neverificat: sabloanele de notificare pentru cele trei surse noi, crearea unui sablon si aparitia notificarilor in pagina Notificari.

#### Aliniere comutatoare in grilele de formular (08.10.2026)
- **Ce s-a adaugat:** regula CSS comuna `.editor-grid > .row-toggle, .anaf-grid > .row-toggle` (aliniere pe linia casetelor, inaltime 39 px).
- **Ce face acum:** comutatorul de langa un camp (Setari > ANAF „Integrare activata", Setari > Notificari „Sablon activ") sta pe linia casetei campului, nu la inaltimea etichetei.
- **De verificat:** in browser, la latime mare, medie si pe telefon: ANAF, sabloane de notificari, editorul de utilizator; restul formularelor din audit (butoane/note langa campuri) nu au fost parcurse.

Campuri de editare active fara contur gros (08.10.2026): Focus fara contur gros: de verificat vizual cautarea (liste, harta, produse), PickOnlyDate, SearchableSelect, zona de lipire/incarcare, dialoguri, la latime mare si pe telefon.

Pagina principala: intrari Mentenanta si Notificari cu semnalizare (08.10.2026): Pagina principala: de verificat cu notificari reale care avertizeaza, ca utilizator limitat si pe ecran ingust.

#### Robustete la erori: jurnal in fisier, /health, ErrorBoundary (08.10.2026)
- **Ce s-a adaugat:** jurnal rulant in `logs/` cu retentie, handlere pentru exceptii necaptate, ajutorul `FireAndForget`, `ErrorBoundary` in layout, `/health` cu verificarea bazei; reteta de supervizor in `docs/SUPERVIZOR_SI_JURNAL.md`.
- **Ce face acum:** o eroare intr-o pagina nu mai inchide circuitul; erorile ajung in `logs/blazorstoc-*.log`; `/health` da 200/503.
- **De verificat:** (1) pornirea cu MariaDB oprita: `/health` = 503, aplicatia porneste; (2) o eroare fortata intr-o pagina arata bannerul si butonul „Reincarca pagina"; (3) cadere simulata a procesului si repornirea de catre supervizorul ales (nu este instalat niciunul); (4) eliberarea blocarilor de operatie/produs cand circuitul cade (bataia de inima).

Robustete la erori: jurnal in fisier, /health, ErrorBoundary, sarcini fara asteptare (08.10.2026): Robustete: baza oprita (/health 503), eroare fortata in pagina, cadere cu repornire de supervizor, eliberarea blocarilor la caderea circuitului (vezi TESTE_RAMASE).

Robustete la erori: completari dupa verificarea in browser (08.10.2026) (08.10.2026): Robustete: de verificat la instalarea in container (restart policy, healthcheck, volum logs); erorile din codul paginii insesi inchid circuitul (acceptat).

Backup pe NAS: configurare in Setari, copiere dupa backup, backup programat (08.10.2026): Backup NAS: de testat pe NAS-ul real (Teste utilizator/09), inclusiv backup programat si cazurile de eroare; fara notificare la backup lipsa.

Backup NAS: corectii dupa testul pe NAS-ul real (08.10.2026) (08.10.2026): Backup NAS: backup programat pe ceas real si cazurile de eroare pe NAS (Teste utilizator/09 pasii 4-5).

Notificari pentru backup lipsa si copie pe NAS lipsa (08.10.2026): Notificari backup: de creat sabloanele in Setari -> Notificari si de verificat aparitia/inchiderea (de exemplu oprind backup-ul programat 3 zile sau cu un NAS oprit).

Notificari backup: vechime configurabila, model needitabil, numai cu backup NAS activ (08.10.2026): Backup NAS: de verificat vizual modelul de notificare si campul de zile; de creat sabloanele in Setari -> Notificari.

Backup: stergere la salvare, verificare ora pe internet, setari NTP (08.10.2026): De verificat in browser: sectiunea Verificare ora, fereastra de confirmare la stergere, ceas mutat, internet taiat; migrarea 34 pe baza BlazorStoc se aplica la repornirea aplicatiei

Setari Backup: categorie principala cu sub-taburi si zilele saptamanii (08.10.2026): De verificat in browser: sub-taburile Backup, zilele saptamanii; migrarile 34 si 35 pe baza BlazorStoc se aplica la repornirea aplicatiei

Pagina produsului: taburi, filtre, aspect; ANAF 404; butoane colorate; padding (08.10.2026): De verificat de utilizator: paginile cu formulare in browser (padding), culorile butoanelor, filtrele din tabelul de miscari pe date reale

ANAF: reguli pe camp, dialog de diferente, versiuni cu slider si stergere; filtre ca in jurnal; liste de alegere; cautare (08.10.2026): De verificat de utilizator: dialogul ANAF pe date reale, versiunile ANAF, aspectul listelor si al cautarii; migrarile 34-35 pe baza BlazorStoc

Sablon de import factura de tip XML (09.10.2026): Facturi XML: de verificat in browser pasii din Teste utilizator/04 (09.10.2026)

Sabloane XML si preluare facturi XML/PDF, categorii, liste de alegere (09.10.2026): Verificare in browser a editorului XML, descrierii cu spatii automate, avertizarilor de potrivire PDF, ZIP e-Factura, stergerii de categorii (Teste utilizator 01, 04, 06)

Parametri obligatori pe subcategorie, variante la preluarea facturii si caractere nesigure (09.10.2026): Parametri obligatori, variante la preluare si caractere nesigure: de verificat in browser (Teste utilizator 01 si 04)

Ordinea categoriilor, preluare factura cu produse deja preluate, popup harta, padding insigne (10.10.2026): drag and drop al categoriilor, avertizarea deja preluat la pasul 1, popup-ul hartii si padding-ul insignelor: de verificat in browser (Teste utilizator/ 01, 04, 08); suita MariaDB si migrarea 38 pe blazorstoc_test

Refactorizare fara schimbari de functionalitate (10.10.2026): paginile impartite (produs -> miscari, preluare factura, sabloane de facturi) si fluxurile lor: de verificat in browser (Teste utilizator/ 02 si 04); schema-mariadb.sql ramane de intretinut in paralel cu fisierele de migrare
