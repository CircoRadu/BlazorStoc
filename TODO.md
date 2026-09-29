# TODO — funcționalități viitoare

## Regula de actualizare a TODO

Din 28.09.2026, taskurile finalizate nu mai stau in acest fisier: ele se muta in `IMPLEMENTED.md` (fisier separat, in acelasi director), care se completeaza doar prin append (se adauga la sfarsit, nu se rescrie ce exista deja).

- Acest fisier („TODO.md”) contine numai ce mai trebuie implementat: taskurile active și, în cadrul lor, doar subtaskurile nefinalizate.
- Taskurile active sunt numerotate consecutiv, începând de la 1, în ordinea priorității; numărul cel mai mic indică prioritatea cea mai mare.
- Când se introduce un task nou înaintea unui task existent, acesta și toate taskurile active următoare (neimplementate) se renumerotează; subtaskurile își schimbă numerele odată cu taskul.
- Când un task este finalizat, el se scoate din acest fisier, iar toate taskurile rămase neimplementate se renumerotează de la 1.
- Taskul finalizat se **adauga la sfarsitul `IMPLEMENTED.md`** (fisier in ordine cronologica: cel mai vechi primul, cel mai nou ultimul — doar append, nu se modifica intrarile anterioare), cu **data și ora implementării** (ora locală) și cu **detalierea** lucrării: ce s-a implementat, fișierele principale, deciziile, verificările efectuate și ce a rămas neverificat.
- Taskul finalizat **nu păstrează denumirea „Task N”**: primește o denumire care explică succint scopul lui, în forma „Finalizat la zz.ll.aaaa oo:mm — denumire”. Trimiterile către el din alte texte folosesc această denumire, nu un număr; nici subtaskurile lui nu mai păstrează numerele.
- Verificările care nu s-au putut efectua se trec, cu motivul și pașii, în `docs/TESTE_RAMASE.md`.

# Taskuri active

## Ordinea de implementare optimizata

Toate taskurile anterioare sunt finalizate si arhivate in `IMPLEMENTED.md`. Taskul activ (1) se implementeaza intr-un ciclu condus de Claude (agent principal, fara predare catre Codex).

## Task 1 - Combobox cu autocompletare pentru beneficiar si proiect la iesire, cu adaugare fara pierderea formularului

Inlocuieste, in formularul de iesire (`Components/Shared/ExitDestinationPicker.razor`), campul de cautare text si dropdown-ul separat pentru beneficiar cu un singur control de tip combobox (camp de text + lista derulanta) care restrange optiunile pe masura ce utilizatorul tasteaza; foloseste acelasi mecanism si pentru selectarea proiectului beneficiarului ales. Adauga langa fiecare combobox un buton "Adauga beneficiar" / "Adauga proiect" care deschide pagina de adaugare corespunzatoare fara sa piarda datele introduse in formularul de iesire; dupa salvarea cu succes a beneficiarului/proiectului nou, aplicatia revine in formularul de iesire care a initiat cererea, cu campurile anterioare pastrate si cu beneficiarul/proiectul nou selectat.

### Subtask 1.1 - Componenta combobox cu autocompletare

- [ ] Creeaza o componenta comuna reutilizabila (de exemplu `Components/Shared/SearchableSelect.razor`) care combina campul de cautare si lista de optiuni intr-un singur control accesibil (tastatura, cititor de ecran), cu restrangerea optiunilor pe masura tastarii (ca filtrul actual din `ExitDestinationPicker.razor`, dar intr-un singur control, nu doua separate).
- [ ] Inlocuieste in `ExitDestinationPicker.razor` campul `input type="search"` si `select`-ul pentru beneficiar cu noua componenta.
- [ ] Foloseste aceeasi componenta si pentru selectarea proiectului (`select`-ul de proiect), pastrand comportamentul actual (proiectul se goleste la schimbarea beneficiarului, lista de proiecte vine din beneficiarul ales).
- [ ] Pastreaza validarile si mesajele actuale (proiect obligatoriu daca optiunea e bifata etc.), fara sa schimbe contractul `StockMovementInput`.

### Subtask 1.2 - Buton "Adauga beneficiar" fara pierderea formularului

- [ ] Adauga langa combobox-ul de beneficiar un buton "Adauga beneficiar" care deschide pagina de adaugare beneficiar, vizibil numai cand destinatia iesirii este Beneficiar.
- [ ] Inainte de navigare, pastreaza starea completa a formularului de iesire (produs, tip miscare, data, cantitate, descriere, destinatie, sursa, beneficiar/proiect partial completate) intr-un mecanism care supravietuieste navigarii (de exemplu un serviciu scoped similar cu `ListNavigationContext`/`UnsavedChanges`, sau stocare in `sessionStorage` prin interop, cu o cheie legata de produs si de sesiune).
- [ ] Dupa salvarea cu succes a beneficiarului nou (aceleasi validari ca la adaugarea oricarui beneficiar), aplicatia revine automat in pagina de miscari a produsului cu formularul de iesire redeschis si valorile anterioare restaurate, iar beneficiarul nou creat este selectat automat in combobox.
- [ ] Anularea adaugarii beneficiarului (fara salvare) revine de asemenea in formularul de iesire, cu valorile pastrate nemodificate.
- [ ] Daca formularul de iesire nu mai poate fi restaurat (de exemplu produsul a fost sters intre timp), utilizatorul primeste un mesaj clar si este dus in pagina produsului sau a catalogului, fara eroare nespecificata.

### Subtask 1.3 - Acelasi mecanism pentru proiect

- [ ] Adauga langa combobox-ul de proiect un buton "Adauga proiect", vizibil numai cand un beneficiar este ales si optiunea proiect este activa.
- [ ] Butonul deschide pagina de adaugare proiect a beneficiarului ales, cu aceeasi pastrare si restaurare a formularului de iesire ca la subtaskul 1.2.
- [ ] Dupa salvarea cu succes a proiectului nou, formularul de iesire este restaurat cu beneficiarul deja ales si cu noul proiect selectat automat.

### Subtask 1.4 - Verificari

- [ ] Teste automate pentru restrangerea optiunilor in combobox (cautare insensibila la majuscule si diacritice, ca filtrul actual).
- [ ] Teste automate pentru pastrarea si restaurarea starii formularului de iesire in jurul navigarii catre adaugare beneficiar/proiect si inapoi (valori identice inainte si dupa, inclusiv cand adaugarea e anulata).
- [ ] Verificare in browser: adaugarea unui beneficiar nou din formularul de iesire fara sa se piarda cantitatea/data/descrierea introduse, selectarea automata a beneficiarului nou, acelasi flux pentru proiect, comportarea la anulare, tastatura si cititor de ecran pentru noul combobox.
- [ ] Actualizeaza `README.md`, `VALIDARE.md` si `docs/PROJECT_STATE.md`.

### Criterii de acceptare

- Campul de cautare si lista de beneficiari din formularul de iesire sunt un singur control combobox cu autocompletare.
- Acelasi tip de control este folosit pentru alegerea proiectului beneficiarului ales.
- Un buton "Adauga beneficiar" (si, cand e cazul, "Adauga proiect") permite adaugarea unui beneficiar/proiect lipsa fara sa piarda datele introduse in formularul de iesire.
- Dupa salvarea cu succes, aplicatia revine in formularul de iesire cu valorile anterioare si cu beneficiarul/proiectul nou selectat automat.

### Detalii de stabilit la implementare

- Mecanismul concret de pastrare a starii formularului de iesire in timpul navigarii (serviciu scoped, sessionStorage sau alta solutie), tinand cont de faptul ca Blazor Server poate reporni circuitul intre navigari.
- Locul exact al butonului "Adauga proiect" (langa combobox sau langa caseta "Proiect") si textul exact al mesajelor noi.

## Task 2 - Copie de siguranta a bazei de date la preluarea situatiei de inventar

Depinde de integrarea aplicatiei cu baza MariaDB reala (finalizata — vezi "Integrare MariaDB reala si comutarea definitiva de dezvoltare" in `IMPLEMENTED.md`), deoarece backupul opereaza pe schema si contul MariaDB rezultate din acel task.

### Decizie tehnica - mecanism de backup/restaurare (comuna pentru Task 2 si Task 3)

- **Format fisier:** arhiva (`.zip`) continand un export SQL logic complet al bazei (generat cu utilitarul `mariadb-dump`/`mysqldump` din aceeasi distributie MariaDB locala, cu `--single-transaction`, `--routines`, `--triggers`, pentru consistenta InnoDB fara blocare exclusiva la nivel de server), un fisier `manifest.json` (data/ora, operator, rol, numarul de tabele, numarul de randuri per tabela, versiunea schemei, hash SHA-256 al dumpului) si hashul SHA-256 al arhivei insasi. Se prefera dumpul logic (nu o copie binara a directorului de date), pentru ca este portabil, verificabil rand cu rand si independent de versiunea exacta de fisiere InnoDB.
- **Motivul folosirii `mariadb-dump`:** e deja prezent langa serverul local (acelasi pachet ca `mariadbd.exe`), e testat pe cazuri complexe (chei externe, triggere, CHECK-uri), si evita reinventarea unui exportator SQL propriu predispus la erori de escapare/tip de date.
- **Blocarea sesiunilor:** contul aplicatiei nu are drepturi de administrare (nu poate opri conexiuni MariaDB straine, vezi subtask 2.9 din taskul finalizat de integrare MariaDB), deci blocarea se face la nivelul aplicatiei, nu al serverului: un lacat (lock) partajat, persistent in baza de date (de exemplu o inregistrare dedicata cu operatie curenta, operator, identificator de proces si marca de timp/heartbeat), verificat de fiecare circuit Blazor si de fiecare operatie de scriere.
  - Cand lacatul e activ: sesiunile existente primesc o notificare si sunt redirectionate catre un mesaj de intretinere ("Backup/restaurare in curs, reveniti mai tarziu"); sesiunile noi vad acelasi mesaj si nu pot initia operatii.
  - Lacatul are un timp de expirare (heartbeat reimprospatat periodic in timpul operatiei); daca aplicatia se opreste neasteptat cu lacatul activ, la urmatoarea pornire lacatul este recunoscut ca orfan (heartbeat expirat) si eliberat automat, cu inregistrare in jurnal, nu lasat blocat definitiv.
  - Eliberarea lacatului se face intotdeauna intr-un bloc final (`finally`), inclusiv la eroare.
- **Progres pentru utilizator:** operatia ruleaza ca proces de fundal urmarit printr-un identificator de job; pagina afiseaza etapele curente (blocare sesiuni, export, verificare, salvare / dezarhivare, verificare structura, verificare continut, import, comutare) si un indicator vizual, actualizat prin re-randarea componentei Blazor (server-side), nu blocand interfata.
- **Verificarea integritatii:** dupa export, se recalculeaza hashul arhivei si continutul e comparat cu baza vie folosind aceeasi metoda de comparare canonica tip-si-hash folosita la migrarea initiala (nu diff text brut pe fisierul SQL, care poate diferi ca ordine fara a diferi ca date).
- **Stocare fisiere:** intr-un director dedicat, in afara `wwwroot` si a oricarei cai servite direct ca fisier static, similar cu directoarele de arhiva din Task finalizat "Arhivarea obiectelor sterse"; descarcarea se face printr-un endpoint HTTP autentificat, dedicat, restrictionat la rolul administrator, nu prin circuitul SignalR.
- **Securitate continut:** dumpul SQL contine hashuri de parole si date de audit; directorul de backup primeste acelasi nivel de protectie ca zonele sensibile deja folosite in proiect (acces restrans, fara publicare, fara includere in raspunsuri catre utilizatori neautorizati).
- **Jurnalizare:** crearea, listarea incercarilor de stergere, stergerea efectiva si restaurarea sunt evenimente de audit, cu operator, rol, timestamp si denumirea pachetului, fara date sensibile in `Details`/`Motif`, conform serviciului comun de jurnalizare deja existent.
- **Actualizare 29.09.2026 (subtask 2.1/2.2 implementate):** lacatul comun a fost implementat ca fisier local (`Services/OperationLock.cs`), nu ca inregistrare in baza de date — contul aplicatiei nu are drepturi DDL pentru a adauga o tabela noua (confirmat explicit in acest ciclu, `MariaArchiveSchema.RequiredTables`), iar adaugarea uneia ar necesita o migrare separata cu contul `blazorstoc_migrator`. Comparatia canonica tip-si-hash a fost scrisa acum ca serviciu reutilizabil (`Services/CanonicalRowHasher.cs`), pe baza `information_schema`, nu exista inca in `Services/` inainte de acest ciclu. Notificarea/redirectionarea sesiunilor active si endpoint-ul de descarcare dedicat raman de construit la Task 3.

### Subtask 2.1 - Declansarea backupului la preluarea inventarului (implementat 29.09.2026)

- [x] Identifica punctul din pagina de preluare inventar unde utilizatorul apasa butonul de preluare si insereaza declansarea backupului ca parte a aceleiasi actiuni: preluarea se confirma utilizatorului abia dupa ce backupul este generat si verificat cu succes (varianta cu siguranta maxima, aliniata cu cerinta ca fisierul sa fie disponibil pentru restaurare imediat ce preluarea e considerata reusita). Implementat in `Components/Pages/InventoryPickup.razor` (`ConfirmSendAsync`): `IDatabaseBackupService.CreateBackupAsync` ruleaza inaintea `PickupApplier.ApplyAsync`.
- [x] Daca backupul esueaza (verificare nereusita, spatiu insuficient, eroare de export), preluarea inventarului nu este confirmata ca reusita, iar utilizatorul primeste mesajul de eroare specific backupului, cu posibilitatea de a reincerca.
- [x] Foloseste lacatul comun descris mai sus pentru a bloca alte sesiuni pe durata snapshotului. Implementat ca lacat pe fisier (`Services/OperationLock.cs`, `FileOperationLockService`), nu ca inregistrare in baza de date — contul aplicatiei (`blazorstoc_dev`) nu are drepturi DDL pentru a adauga o tabela noua (`MariaArchiveSchema.RequiredTables`), asa cum a fost confirmat explicit in acest ciclu; adaugarea unei tabele dedicate ramane o migrare separata, cu contul `blazorstoc_migrator`, daca se doreste ulterior varianta stocata in baza de date. Blocarea acopera doar operatiile de backup/restaurare intre ele, nu scrierile obisnuite ale aplicatiei in timpul ferestrei de export (`--single-transaction` da o imagine MVCC consistenta; blocarea completa a sesiunilor ramane in sarcina Task 3, care introduce notificarea sesiunilor active).
- [x] Afiseaza utilizatorului progresul operatiei (etape + indicator vizual), fara sa blocheze restul aplicatiei mai mult decat este necesar.
- [x] Declansarea backupului foloseste acelasi mecanism si inainte de momentul restaurarii unei copii mai vechi de catre administrator (vezi Task 3, subtask 3.4, Pas 0 - blocare si snapshot curent), nu doar la preluarea inventarului. `IDatabaseBackupService.CreateBackupAsync(BackupKind, ...)` accepta deja `BackupKind.PreRestore`; Task 3 va apela acelasi serviciu.

### Subtask 2.2 - Generarea si validarea arhivei (implementat 29.09.2026)

- [x] Genereaza dumpul SQL cu `mariadb-dump` (transactional, consistent) intr-un fisier temporar, apoi `manifest.json` cu metadatele operatiei. Implementat in `Services/DatabaseBackup.cs` (`MariaDatabaseBackupService`); credentialele trec printr-un fisier `--defaults-extra-file` temporar, niciodata in argumentele procesului.
- [x] Calculeaza hashurile SHA-256 (dump si arhiva finala) si compara continutul dumpului cu baza vie folosind comparatia canonica tip-si-hash existenta din procesul de migrare. Comparatia canonica tip-si-hash a fost scrisa acum ca serviciu reutilizabil (`Services/CanonicalRowHasher.cs`) — nu exista inca in `Services/` inainte de acest ciclu (era doar un instrument scratch nedocumentat la migrare); verificarea compara un hash canonic al bazei vii calculat inainte si dupa export (confirma ca baza nu s-a schimbat in fereastra de export), nu o comparare directa cu textul dumpului SQL (ar necesita reimport intr-o schema separata, care cere drepturi DDL/CREATE SCHEMA neexistente pentru contul curent — ramane pentru Task 3, care introduce un cont dedicat cu aceste drepturi).
- [x] Salveaza arhiva definitiv (redenumire atomica din fisier temporar) numai dupa ce verificarea trece; daca verificarea esueaza, sterge fisierele temporare, elibereaza lacatul si informeaza utilizatorul clar despre esec, fara sa lase o arhiva partiala/nesigura pe disc.
- [x] Verificat integral prin executie reala (nu doar teste unitare) pentru modul demonstrativ (SQLite): `tests/BlazorStoc.Checks` ruleaza `SqliteDatabaseBackupService` contra unei baze SQLite reale, seed, verifica pachetul `.zip`, `manifest.json`, hash-ul `.sha256` si eliberarea lacatului. Modul MariaDB (`mariadb-dump` real) nu a putut fi rulat in acest ciclu — vezi `docs/TESTE_RAMASE.md`, A18 (mediul agentului nu are instanta locala MariaDB).

### Subtask 2.3 - Denumirea si pastrarea pachetului (implementat 29.09.2026, ca parte a 2.1/2.2)

- [x] Denumeste fisierul dupa modelul "Copie siguranta preluare inventar data ora user_level user_name", cu data/ora in formatul de afisare al proiectului (`dd.MM.yyyy HH:mm`) transpus intr-un nume de fisier valid (fara caractere interzise de sistemul de fisiere, fara diacritice), si cu rolul/numele utilizatorului care a declansat preluarea. Implementat in `Services/DatabaseBackup.cs` (`BackupNaming.BuildFileName`), cu o coliziune de nume (doua backup-uri in aceeasi secunda) rezolvata printr-un sufix numeric, ca la Windows Explorer.
- [x] Pastreaza pachetul pe server, in directorul dedicat, disponibil ulterior in pagina de restaurare stoc (Task 3). Director dedicat: `data/database-backups` (demo) / `Database:MariaBackupFilesPath` sub `MariaAssetPaths` (real), in afara `wwwroot`. Disponibilitatea efectiva in pagina de restaurare ramane de construit la Task 3 (care doar listeaza acest director).

### Criterii de acceptare

- Fiecare preluare de inventar produce o copie de siguranta verificata, denumita conform modelului cerut, disponibila administratorului in pagina de restaurare. *(Copia verificata si denumita conform modelului: implementat. Pagina de restaurare: Task 3, neinceput.)*
- Nicio alta sesiune nu poate scrie in baza de date in timpul generarii snapshotului. *(Partial: lacatul comun previne o a doua operatie de backup/restaurare simultana; blocarea tuturor scrierilor aplicatiei in timpul ferestrei de export ramane la Task 3 — vezi nota de la subtask 2.1.)*
- O verificare esuata nu lasa pe server o arhiva nesigura si informeaza clar utilizatorul. *(Implementat.)*
- Utilizatorul vede progresul operatiei de backup. *(Implementat.)*

### Subtask 2.4 - Spatiu pe disc (implementat 29.09.2026, ca parte a 2.1/2.2)

- [x] Inainte de a incepe exportul, verifica spatiul liber disponibil in directorul de backup fata de o estimare a dimensiunii bazei (de exemplu dimensiunea ultimului export reusit, plus o marja); daca spatiul e insuficient, opreste operatia inainte de a scrie orice fisier si informeaza utilizatorul cu un mesaj clar. Implementat in `Services/DatabaseBackup.cs` (`BackupDiskSpace`): estimeaza cerinta din cel mai mare pachet `.zip` existent (sau o valoare implicita pentru primul backup), cu o marja x4 pentru fisierul temporar de export si arhiva finala coexistente.

### Decizii acceptate (fara alte optiuni de stabilit)

- Backupul face parte din aceeasi actiune cu preluarea inventarului si o conditioneaza (subtask 2.1); nu ruleaza asincron/deconectat de rezultatul preluarii.
- Pachetele de tip "preluare inventar" nu au expirare automata si nu sunt sterse de sistem; se pastreaza pe server nelimitat si pot fi sterse numai manual de administrator, prin fluxul dual de confirmare (Task 3, subtask 3.2). Spatiul ocupat este vizibil administratorului in pagina de restaurare (Task 3, subtask 3.1).

## Task 3 - Pagina de restaurare a bazei de date (meniu Inventar, exclusiv administrator)

Depinde de integrarea MariaDB reala (finalizata) si de Task 2 (foloseste pachetele generate la preluarea inventarului si acelasi mecanism de backup/lacat/verificare).

**Progres 29.09.2026:** toate cele patru subtaskuri (3.1-3.4) sunt implementate. 3.1 (meniu si listare) si 3.2 (stergerea pachetelor de tip preluare inventar) au fost verificate intr-un ciclu anterior. In acest ciclu au fost adaugate 3.3 (declansarea restaurarii, cu popup de avertizare si confirmarea prin cuvantul `confirma`) si 3.4 (cei 5 pasi efectivi de restaurare). Modul demonstrativ (SQLite) este verificat integral prin executie reala (teste automate + browser, vezi mai jos); modul MariaDB real ramane neverificat (fara instanta locala in acest mediu) si Pasul 4 (comutarea de scheme) necesita in plus un cont MariaDB dedicat neconfigurat inca - vezi nota de la subtask 3.4 si `docs/TESTE_RAMASE.md`. Un singur punct din criteriile de acceptare ramane doar partial acoperit: notificarea sesiunilor active este afisata la fiecare navigare/incarcare de pagina (nu impinsa instantaneu catre un tab deja deschis si inactiv), iar scrierile obisnuite ale altor sesiuni (formulare de produse, miscari de stoc etc.) nu sunt blocate in mod activ in timpul restaurarii - lacatul comun serializeaza doar operatiile de backup/restaurare intre ele, ca si pana acum (vezi nota de la subtask 2.1).

### Subtask 3.1 - Meniu si listare pachete (implementat 29.09.2026)

- [x] Adauga in meniul Inventar optiunea "Restaureaza stoc", vizibila numai utilizatorilor cu rol administrator. Implementat: `Components/Layout/MainLayout.razor` (`<AuthorizeView Roles="Administrator">`), intrare "Restaurează stoc" catre `/inventar/restaurare`.
- [x] Afiseaza in pagina toate pachetele disponibile pe server: cele generate la preluarea situatiei de inventar (Task 2) si cele generate automat inainte de o restaurare anterioara (subtask 3.3), intr-un tabel cu selectie unica de tip radio button per rand. Implementat: `Components/Pages/DatabaseRestore.razor` + `IDatabaseBackupService.ListPackagesAsync`/`Services/DatabaseBackup.cs` (`BackupPackageStore.ListAsync`, citeste `manifest.json` din fiecare `.zip`).
- [x] Distinge vizual cele doua categorii de pachete (preluare inventar / pre-restaurare), fara sa permita confuzia intre ele la selectare. Coloana "TIP" cu eticheta distincta per categorie.
- [x] Afiseaza dimensiunea fiecarui pachet si spatiul total ocupat de backupuri pe server, pentru ca administratorul sa poata urmari cresterea spatiului folosit (pachetele pre-restaurare nu pot fi sterse, vezi subtask 3.2).

### Subtask 3.2 - Stergerea pachetelor de tip "preluare inventar" (implementat 29.09.2026)

- [x] Permite administratorului sa stearga numai pachetele generate la preluarea inventarului, folosind acelasi flux dual de confirmare deja implementat (pagina de atentionare cu motiv + pagina cu introducerea cuvantului exact `sterge`), reutilizand componentele existente din taskul finalizat "Flux in doi pasi pentru confirmarea stergerii". Reutilizat `Components/Shared/DeleteConfirmationDialog.razor` (extins cu parametrul `ConfirmationNote`, ca textul implicit "va muta obiectul in arhiva" sa nu fie afisat la o stergere definitiva de fisier, care nu are arhivare).
- [x] Nu permite stergerea pachetelor generate automat inainte de o restaurare (butonul/optiunea de stergere nu este disponibila pentru aceasta categorie), atat in interfata cat si la nivelul serviciului (respinge explicit o cerere de stergere pentru aceasta categorie, indiferent de origine). Implementat in UI (`BackupPackage.CanDelete`) si independent in `BackupPackageStore.DeleteAsync` (verifica tipul din manifest, nu se bazeaza pe UI).
- [x] Jurnalizeaza stergerea unui pachet (operator, denumire pachet, motiv). Tip de entitate nou `AuditEntities.DatabaseBackup` ("CopieSiguranta"), actiunea "Ștergere".
- [x] Verificat integral prin executie: `tests/BlazorStoc.Checks` (listare, refuzul stergerii unui pachet de tip pre-restaurare, stergerea reala a unui pachet de tip preluare inventar) si direct in browser (preview demonstrativ): listare, selectie radio, dialog in doi pasi, stergere efectiva de pe disc, eveniment corect in Jurnal activitate.

### Subtask 3.3 - Declansarea restaurarii si confirmarea (implementat 29.09.2026)

- [x] Adauga butonul "Restaureaza baza de date", activ numai cand este selectat exact un pachet din tabel. Implementat in `Components/Pages/DatabaseRestore.razor` (dezactivat cand `selectedFileName is null`).
- [x] La apasare, deschide un popup care avertizeaza explicit ca restaurarea aduce baza de date la o versiune anterioara si ca pot fi pierdute date introduse ulterior momentului backupului selectat. Componenta noua `Components/Shared/RestoreConfirmationDialog.razor`.
- [x] Confirmarea finala se face prin introducerea cuvantului exact `confirma` (aceeasi regula de comparare stricta ca la cuvantul `sterge` din fluxul de stergere existent). `Services/RestoreConfirmation.cs` (`RestoreConfirmationRules`), acelasi tipar ca `DeleteConfirmationRules`.
- [x] O confirmare gresita, incompleta sau abandonata nu declanseaza nicio modificare asupra bazei de date. Butonul de confirmare din dialog ramane dezactivat pana la potrivirea exacta a cuvantului; verificat in browser (cuvant gresit `Confirma` -> buton dezactivat, cuvant exact `confirma` -> buton activ) si prin `tests/BlazorStoc.Checks`.

### Subtask 3.4 - Pasii de restaurare (executati numai dupa confirmare) (implementat 29.09.2026)

- [x] **Pas 0 - blocare si snapshot curent.** Activeaza lacatul comun; genereaza imediat un export/arhiva complet al bazei curente, cu hash de verificare, folosind acelasi mecanism ca la Task 2. Acest pachet este denumit dupa modelul "Copie siguranta baza de date inainte restaurare baza de date folosind backup preluare inventar, data ora user_level user_name", marcat ca nesters din categoria pre-restaurare (subtask 3.2) si pastrat pe server indiferent de rezultatul restaurarii. Implementat in `Services/DatabaseRestore.cs` (`SqliteDatabaseRestoreService`/`MariaDatabaseRestoreService`), folosind lacatul deja detinut de operatia de restaurare (nu un al doilea `TryAcquireAsync`, care ar esua - vezi extensia `existingLock` adaugata la `IDatabaseBackupService.CreateBackupAsync` in `Services/DatabaseBackup.cs`). Notificarea sesiunilor active: banner in `Components/Layout/MainLayout.razor`, afisat la fiecare navigare/incarcare de pagina cat timp lacatul e activ (nu o notificare live impinsa catre un tab deja deschis, deoarece `MainLayout` nu are propriul `@rendermode` interactiv - vezi comentariul din cod); nu blocheaza activ scrierile obisnuite ale altor sesiuni in timpul ferestrei de restaurare (ramane un risc rezidual acceptat, ca si la subtask 2.1).
- [x] **Pas 1 - integritatea backupului ales.** Verifica hashul arhivei (fata de fisierul `.sha256`) si al continutului (fata de `manifest.json`); daca nu corespund, opreste procesul, elibereaza lacatul si informeaza utilizatorul ca pachetul este corupt/deteriorat, fara sa continue. Verificat prin executie reala (test cu sidecar `.sha256` alterat manual).
- [x] **Pas 2 - diferente de structura.** Mod demonstrativ (SQLite): compara direct lista de tabele si coloanele (nume + prefix canonic de tip) intre fisierul de backup si baza vie. Mod MariaDB real: compara lista de tabele din manifestul pachetului cu tabelele cerute de schema (`MariaArchiveSchema.RequiredTables`) - comparatia exacta de coloane/chei/indecsi/triggere ar necesita fie parsarea textului brut al dumpului SQL, fie importul lui intr-o schema temporara inaintea acestui pas; a fost aleasa a doua varianta doar pentru Pasul 4 (vezi mai jos), ca sa nu introduca un parser SQL propriu, fragil, doar pentru o verificare preliminara.
- [x] **Pas 3 - diferente de continut.** Compara continutul (randurile) din pachetul de backup cu baza vie curenta, cu aceeasi metoda canonica de comparare folosita la migrare/backup (`CanonicalRowHasher`, reutilizat si pentru SQLite printr-un hasher dedicat `SqliteSchemaHasher`). Daca nu exista nicio diferenta, opreste procesul, elibereaza lacatul si informeaza utilizatorul ca backupul si baza actuala sunt identice, deci restaurarea nu este necesara. Verificat prin executie reala.
- [x] **Pas 4 - import si comutare atomica.** Mod demonstrativ (SQLite): pachetul de backup e deja un fisier de baza complet, asa ca "importul" e o copiere directa peste conexiunea vie prin API-ul de backup SQLite (`SqliteConnection.BackupDatabase`, in sens invers fata de generarea copiei de siguranta) - verificat integral prin executie reala (restaurare efectiva, catalogul de produse revenit la starea dinaintea unei modificari). Mod MariaDB real: codul (`Services/DatabaseRestore.cs`, `MariaSchemaSwap`) creeaza schema noua `<nume_baza>_bak`, importa `dump.sql` cu clientul `mariadb.exe`, verifica importul cu `CanonicalRowHasher`, apoi comuta atomic printr-o singura instructiune `RENAME TABLE` care muta simultan toate tabelele din schema vie in `<nume_baza>_old` si toate tabelele din `<nume_baza>_bak` in numele schemei vii - **neexecutat/neverificat in acest mediu** (fara instanta locala MariaDB, vezi `docs/TESTE_RAMASE.md`); necesita in plus contul dedicat cu drepturi `CREATE`/`DROP SCHEMA` si `RENAME TABLE` peste cele trei scheme (`Database:MigratorUser`/`Database:MigratorPassword`, neconfigurate - contul `blazorstoc_migrator` existent are drepturi DDL numai pe schema `BlazorStoc`, nu si pe `_bak`/`_old`); fara aceasta configurare, Pasul 4 se opreste explicit cu un mesaj clar in loc sa incerce operatii DDL fara drepturi.
- [x] **Pas final - eliberare si confirmare.** Elibereaza lacatul, informeaza utilizatorul ca restaurarea a reusit si inregistreaza evenimentul de audit complet (pachet folosit, pachetul pre-restaurare generat, operator). Actiune noua de jurnal `AuditActions.Restore` ("Restaurare"); verificat in browser (Jurnal activitate arata evenimentul cu tinta si detaliile corecte).
- [x] Schema `<nume_baza>_old` nu este stearsa automat de proces; ramane pe server ca plasa de siguranta suplimentara, pentru curatare manuala ulterioara (cod MariaDB, neexecutat in acest mediu).

### Criterii de acceptare

- Pagina "Restaureaza stoc" este vizibila numai administratorului si listeaza corect ambele categorii de pachete, cu selectie unica.
- Pachetele de tip preluare inventar pot fi sterse numai prin fluxul dual de confirmare existent; pachetele de tip pre-restaurare nu pot fi sterse in nicio conditie din interfata sau din serviciu.
- Restaurarea nu porneste fara selectarea exact a unui pachet si fara introducerea corecta a cuvantului `confirma`.
- Inaintea oricarei modificari, se genereaza si pastreaza un pachet pre-restaurare al bazei curente.
- Un pachet cu hash invalid, cu structura incompatibila sau cu continut identic bazei curente nu declanseaza modificari asupra bazei vii, iar utilizatorul primeste mesajul corespunzator fiecarui caz.
- O restaurare validata inlocuieste atomic continutul bazei vii cu cel al pachetului ales, fara stare intermediara vizibila aplicatiei, si pastreaza schema anterioara sub `_old` pentru siguranta suplimentara. *(Implementat si verificat in mod demonstrativ; codul MariaDB exista dar e neverificat - vezi subtask 3.4, Pasul 4.)*
- Nicio sesiune, in afara celei care conduce restaurarea, nu poate scrie in baza de date in timpul procesului; sesiunile noi primesc un mesaj explicit ca o restaurare este in curs. *(Partial: fiecare navigare/incarcare de pagina afiseaza un banner de intretinere cat timp lacatul comun e activ (`Components/Layout/MainLayout.razor`); scrierile obisnuite ale altor sesiuni nu sunt blocate activ in timpul ferestrei de restaurare - risc rezidual acceptat, documentat si la subtask 2.1.)*

### Riscuri identificate si masuri de preventie/remediere

- **Scriere concurenta in timpul snapshotului de la o conexiune din afara aplicatiei** (client SQL direct, nu prin aplicatie): lacatul aplicatiei nu poate opri o asemenea conexiune, pentru ca acest cont nu are drepturi de administrare server (vezi taskul finalizat de integrare MariaDB). Se foloseste totusi `--single-transaction` la export pentru consistenta MVCC, iar riscul rezidual ramane un fapt documentat, nu o garantie absoluta; de discutat cu utilizatorul daca e nevoie de restrictionare suplimentara la nivel de cont MariaDB pentru accesul direct in ferestrele de backup/restaurare.
- **Proces de backup/restaurare intrerupt** (crash aplicatie, repornire server): fisierele se scriu intai cu nume temporar si se redenumesc atomic doar dupa succes; lacatul are heartbeat si expira, fiind recunoscut si eliberat automat ca orfan la urmatoarea initializare, cu inregistrare in jurnal.
- **Spatiu insuficient pe disc** pentru export sau pentru schema `_bak` in paralel cu schema vie (necesita temporar aproape dublul spatiului bazei): se verifica spatiul liber estimat inainte de a incepe orice pas care scrie date, cu oprire timpurie si mesaj clar daca spatiul e insuficient.
- **Comparatie de continut cu rezultat eronat din cauza ordinii randurilor** in dumpul SQL (nu reflecta neaparat diferente reale): se foloseste comparatia canonica tip-si-hash (aceeasi metoda validata la migrare), nu un diff text brut pe fisierul SQL.
- **Doua actiuni de backup/restaurare initiate simultan** (doi administratori, sau preluare inventar + restaurare in acelasi timp): lacatul comun unic per baza de date impiedica a doua operatie sa porneasca; a doua cerere primeste mesajul ca o operatie e deja in curs.
- **Comutarea atomica a schemelor esueaza partial**: folosirea unei singure instructiuni `RENAME TABLE` cu toate tabelele implicate garanteaza ca operatia e completa sau nu are niciun efect; nu se foloseste o secventa de instructiuni separate pentru acest pas.
- **Selectarea gresita a pachetului de restaurare** de catre administrator: avertismentul explicit despre pierderea de date, confirmarea prin cuvantul exact `confirma` si pachetul pre-restaurare generat automat ofera o cale de revenire chiar si dupa o alegere gresita.
- **Cresterea necontrolata a spatiului ocupat de pachetele pre-restaurare**, care nu pot fi sterse niciodata conform cerintei: este un risc pe termen lung, semnalat aici ca decizie de business ramasa deschisa (nu s-a presupus o politica de retentie neceruta explicit); de clarificat cu utilizatorul intr-o etapa ulterioara.
- **Expunerea pachetelor de backup** (contin hashuri de parole si date de audit): stocare in afara `wwwroot`, descarcare doar prin endpoint autentificat/autorizat pentru rol administrator, acelasi nivel de protectie ca alte zone sensibile ale proiectului.
- **Blocarea permanenta a aplicatiei** daca lacatul nu se elibereaza corect dupa o eroare neasteptata: eliberarea lacatului se face intr-un bloc `finally` in jurul intregului proces, plus mecanismul de expirare/heartbeat descris mai sus ca a doua plasa de siguranta.

### Decizii acceptate (fara alte optiuni de stabilit)

- **Cont MariaDB dedicat pentru restaurare:** se defineste un cont operational separat de `blazorstoc_dev`, cu drepturi limitate strict la `CREATE`/`DROP SCHEMA` si `RENAME TABLE` pentru schemele implicate in fluxul de restaurare (schema vie, `_bak`, `_old`), fara alte drepturi globale sau de administrare a serverului. Datele lui de conectare se pastreaza dupa acelasi tipar de fisier privat protejat folosit pentru `admin.private.cnf`/`connection.private.json` (vezi documentul de predare MariaDB), niciodata in cod sau in `appsettings.json` necriptat.
- **Notificarea sesiunilor Blazor active:** se reutilizeaza tiparul deja existent in aplicatie pentru notificarea schimbarilor de baza de date (componenta similara `FrmDatabaseChangedNotification`/mecanismul curent de "database changed"), extins pentru a afisa mesajul de intretinere si a redirectiona sesiunea catre o pagina de asteptare, fara sa introduca un mecanism nou separat.
- **Retentia pachetelor pre-restaurare care nu pot fi sterse:** ramane fara stergere automata, conform cerintei explicite; masura de atenuare acceptata este vizibilitatea spatiului ocupat in pagina de restaurare (subtask 3.1), nu o curatare automata. O eventuala politica de arhivare externa (mutare pe alt suport) ramane o decizie ulterioara, separata de acest task.

## Task 4 - Mareste inaltimea randurilor din tabelul situatiei de inventar pentru OCR

La cererea utilizatorului: in formularul PDF de inventar generat (`Services/InventoryPdfWriter.cs`, constanta `LineHeight`, folosita si de `Services/InventoryPdfLayout.cs` pentru geometria comuna cu pipeline-ul OCR), inaltimea randurilor/celulelor tabelului este prea mica pentru ca utilizatorii sa scrie lizibil valoarea reala de mana, ceea ce reduce fiabilitatea recunoasterii OCR ulterioare (`Services/InventoryPickupOcr.cs`, folosit de fluxul de preluare inventar).

### Subtask 4.1 - Marirea inaltimii randurilor tabelului

- [ ] Mareste `LineHeight` (sau introdu o inaltime dedicata pentru randurile de date ale tabelului, distincta de titlu/antet daca e nevoie) in `Services/InventoryPdfWriter.cs`, pastrand restul geometriei (coloanele din `InventoryPdfLayout.ComputeColumns`, paginarea, antetul repetat) neschimbate.
- [ ] Nu modifica logica de layout partajata cu OCR-ul (`InventoryPdfLayout`) decat daca marirea inaltimii o cere explicit; coloanele (Cod produs / Valoare stoc / Valoare reala) raman aceleasi.

### Subtask 4.2 - Actualizarea testelor existente

- [ ] Testele din `tests/BlazorStoc.Checks` care verifica layout-ul/continutul exact al PDF-ului generat (pozitii, numarul de pagini pentru un catalog mare, grila celulelor) trebuie actualizate sa reflecte noua inaltime; verifica ce teste devin nepotrivite dupa modificare si corecteaza-le sa continue sa verifice ceva relevant (nu doar sa treaca).

### Criterii de acceptare

- Randurile tabelului din PDF-ul de inventar au mai mult spatiu vertical pentru scris de mana, fara sa schimbe coloanele sau restul logicii de layout partajate cu OCR.
- Testele din `tests/BlazorStoc.Checks` trec dupa actualizare, verificand efectiv noua geometrie.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

