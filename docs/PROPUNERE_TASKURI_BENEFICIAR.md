# Propunere: taskuri pe beneficiar si dashboard de taskuri al utilizatorului

Data: 09.11.2026. Stare: PROPUNERE cu deciziile utilizatorului incorporate (sectiunea 5), nimic implementat. Scrisa dupa citirea codului existent (vezi "Ce exista deja").

Rezumat decizii (09.11.2026): camp titlu; un singur destinatar; utilizatorul isi sterge fisierele proprii; preluarea nu schimba statusul; fondul rosu la creare si la mutare; administratorul poate fi destinatar; drepturile de gestionare = chei in matricea de permisiuni; termen limita optional cu notificari intr-un tab propriu al utilizatorului in pagina Notificari (sectiunea 3.8).

## 1. Cerinta (rezumat)

- Pe fisa beneficiarului, administratorul adauga, modifica si sterge taskuri destinate unui utilizator.
- Task = data crearii (data curenta, nu se editeaza), descriere, utilizator destinatar, status (In viitor / In lucru / Finalizat), fisiere atasate (adaugare si stergere).
- Utilizatorul isi vede taskurile intr-un dashboard accesat din meniul numelui sau din bara de sus. Poate schimba statusul si adauga fisiere; NU sterge fisierele puse de administrator.
- Dashboard: tabel paginat, doua taburi: Active (orice task nefinalizat) si Finalizate.
- Un task nou apare pe fond rosu si cere "preluare".
- Totul se jurnalizeaza.

## 2. Ce exista deja si se refoloseste

- Meniul utilizatorului: `Components/Shared/AccountMenu.razor` (numele din bara de sus; intrari: Autentificatorul meu, Schimba parola, Deconectare). Intrarea noua "Taskurile mele" se pune aici, cu insigna rosie = numar taskuri nepreluate.
- Fisa beneficiarului: `Components/Pages/BeneficiaryDetail.razor` are sectiuni pliabile (`catalog-collapse`), unele in `ModuleGate`. Sectiunea "Taskuri" se adauga la fel.
- Fisiere: `Services/EntityFiles.cs` + `MariaEntityFileStore` (tabela `entity_files`, regulile `EntityFileRules`: max 20 pe obiect, tip detectat din octeti, nume sigur, duplicate refuzate). Backup-ul/restaurarea deja duc aceste fisiere (`BackupAssets`).
- Jurnal: `AuditActions` / `AuditEntities` / `AuditDetails` / `AuditNavigation` (reguli: actiune exacta pe operatie, valoare veche si noua, filtru in pagina Jurnal, `IsCreateOrEdit`, test).
- Acces: `IAccessControl` (`EnsureAdministratorAsync`, `HasAsync`), matricea `Permissions`. Utilizatorul curent: `GetUsernameAsync`.
- Actualizare live intre sesiuni: `ChangeFeed` / `ChangesHub` / `LiveRefresh` (dashboardul utilizatorului se reimprospateaza cand administratorul adauga un task; administratorul vede schimbarile de status).
- Tipare de UI: `Paged`/`AuditPager`, iconite de rand `row-icon`, `DeleteConfirmationDialog`, `SearchableSelect`, `count-badge`, popup cu `dialog-head` + `HelpButton`.
- Arhitectura pe module: `Modules/<Nume>/` cu `IModule`, `Guarded*`, versiune in `module-versions.json`.

## 3. Propunere

### 3.1 Modul nou `Modules/Tasks/` (id `taskuri`)

Cod propriu, circuit breaker, versiune (`tools\module-version.ps1 -Module Tasks -Init`), randul in `docs/HARTA_COD.md`. Sectiunea de pe beneficiar, linkul din meniu si pagina dashboard sunt in `ModuleGate` / `[Module]`, deci cu modulul oprit dispar fara sa strice restul aplicatiei.

### 3.2 Date (migrare noua; numarul = primul liber la implementare, de verificat in `MariaSchemaMigrations`)

Tabela `beneficiary_tasks`:

| Coloana | Rol |
|---|---|
| id | cheie |
| beneficiary_id | FK beneficiar |
| assigned_user_id | FK `web_users` (destinatarul) |
| title | titlu scurt (obligatoriu, max 150 caractere) |
| description | text (obligatoriu) |
| due_date | termen limita (DATE), optional; NULL = fara termen |
| status | 1 = In viitor, 2 = In lucru, 3 = Finalizat |
| created_utc / created_by | data crearii (setata de server, nu din formular) si administratorul |
| accepted_utc / accepted_by | preluarea; NULL = nepreluat = fond rosu |
| finished_utc | setat la trecerea in Finalizat, golit la redeschidere |
| updated_utc, row_version | verificare de concurenta (doi editori) |

Fisierele: se scriu in `entity_files` cu `owner_kind = 'task'` (acelasi folder pe disc, acelasi backup/restaurare, aceleasi verificari). Un `MariaTaskFileStore` propriu in modul face verificarile de acces ale taskurilor. Magazia generala `MariaEntityFileStore` nu cunoaste tipul `task`, deci fisierele de task NU apar in tabelul "Fisiere beneficiar" (altfel le-ar vedea oricine are dreptul sa vada beneficiarul).

### 3.3 Reguli de acces (verificate pe server, nu doar in interfata)

- Administratorul (sau cine are cheile `taskuri.add/edit/delete`, vezi decizia 7): adauga, modifica, sterge taskuri, schimba destinatarul, sterge orice fisier.
- Utilizatorul: vede numai taskurile in care `assigned_user_id` este el. Poate: schimba statusul, prelua, adauga fisiere, sterge fisierele incarcate de el. Nu poate: modifica descrierea/destinatarul, sterge taskul, sterge fisierele administratorului. Taskul altui utilizator raspunde "negasit" (fara sa-i confirme existenta).
- Descarcarea fisierelor (`/media/task-files/{id}`) aplica aceeasi regula; 503 cat modulul e oprit, ca la fisierele de observatii.
- Dashboardul nu cere cheie in matrice (ca meniul contului): orice utilizator autentificat isi vede propriile taskuri.

### 3.4 Interfata administrator (fisa beneficiarului)

- Sectiune pliabila "Taskuri" cu insigna de numar, tabel paginat (`Paged`): data creare, titlu, utilizator, termen (rosu cand e depasit si taskul nu e finalizat), status, preluat (da/nu), numar fisiere. Pe rand doar iconite: editare, stergere (regula "iconite sau butoane").
- Buton "Adauga task" -> popup: data crearii (afisata, needitabila, `dd.MM.yyyy`), titlu, utilizator (`SearchableSelect`), status (implicit In viitor), termen limita (data, optional, cu comutator "Are termen"; `PickOnlyDate`), descriere, zona de fisiere (lista cu stergere + adaugare). La adaugare fisierele alese se tin in popup si se incarca imediat dupa salvarea taskului.
- Stergere cu `DeleteConfirmationDialog`; sterge taskul si fisierele lui (de pe disc) in aceeasi operatie.
- Un utilizator dezactivat apare marcat; administratorul poate muta taskul altuia.

### 3.5 Dashboard utilizator

- Ruta `/taskurile-mele`; intrare "Taskurile mele" in `AccountMenu` cu insigna rosie (nepreluate); aceeasi insigna mica pe numele din bara de sus, ca sa se vada fara sa deschizi meniul.
- Doua taburi cu `count-badge`: Active (status 1 sau 2) si Finalizate (status 3), fiecare tabel paginat cu alegerea numarului pe pagina.
- Coloane: beneficiar (link doar daca utilizatorul are dreptul sa vada beneficiarii), data creare, titlu, termen (rosu = depasit, galben = in perioada de avertizare), status (lista derulanta), fisiere, actiuni (iconite: preluare, deschidere).
- Task nepreluat: randul pe fond rosu (token de culoare nou, cu varianta in `theme-dark.css`) si iconita "Preia". Preluarea seteaza `accepted_utc/by`, scoate rosul si se jurnalizeaza. O schimbare de status sau un fisier adaugat de utilizator inseamna implicit preluare.
- Popup de detalii: descriere (citire), schimbare status, fisiere (descarcare, adaugare, stergere doar ale lui; fisierele administratorului au stergerea dezactivata cu `title` explicativ).
- Reimprospatare live prin `ChangeFeed` cand administratorul adauga/modifica/sterge un task.

### 3.6 Jurnal (actiuni exacte, adaugate in `AuditActions`, filtrul Jurnalului, `IsCreateOrEdit`, `AuditNavigation`)

- Adaugare task, Modificare task (titlu si/sau descriere, cu valoare veche -> noua), Modificare termen task (data veche -> noua, inclusiv setat/scos), Mutare task la alt utilizator, Stergere task (toate campurile in detalii).
- Schimbare status task (de catre utilizator sau administrator; veche -> noua), Preluare task.
- Adaugare fisier task, Stergere fisier task (nume, dimensiune, cine a incarcat).
- Entitate noua `AuditEntities.Task` cu identificare "#id · beneficiar «X» · utilizator". Linkul din jurnal duce pe fisa beneficiarului, sectiunea Taskuri.

### 3.8 Notificari de termen, tab propriu in pagina Notificari

Analiza motorului existent (`Modules/Notifications`, `Services/ExpiryNotifications.cs`): notificarile de azi sunt GLOBALE (o notificare = sursa + obiect + data; preluarea de catre oricine o preia pentru toti; contorul din triunghiul meniului numara toate). Nu au proprietar-utilizator, deci o sursa `IExpirySource` pentru taskuri ar arata termenele fiecarui utilizator tuturor celorlalti. Nu se modifica motorul global.

Propunere: tab nou **"Taskuri"** in pagina Notificari, pe tiparul filelor aduse de alte module (`ElcomturBalancesTab`, `MaintenanceIncompleteTab`, cu insigna `count-badge` si `needs-attention`), vizibil oricarui utilizator care are taskuri si continand DOAR taskurile lui:

- Calculat la citire din `beneficiary_tasks` (fara randuri de notificare stocate, deci nu exista stare de sincronizat): 
  - "Nepreluat" (task nou sau mutat, inca fara preluare);
  - "Termen depasit" (termen < azi, status diferit de Finalizat);
  - "Termen in curand" (termen in urmatoarele N zile).
- Dispare singur cand taskul devine Finalizat, termenul se muta dincolo de perioada sau taskul se sterge/muta la altcineva. Nu are amanare in aceasta etapa; actiunea este deschiderea taskului (link catre dashboard-ul de taskuri / popup de detalii).
- N (zile de avertizare) = setare globala in Setari -> Notificari, implicit 3, modificabila de administrator si jurnalizata ("Modificare zile avertizare taskuri"). Alternativa: termen de avertizare pe fiecare task.
- Contorul triunghiului din meniu: la numarul global se adauga alertele de task ale utilizatorului curent printr-un mic contract din modulul Taskuri (`ITaskAlerts`, trecut prin circuit breaker; cu modulul oprit contribuie 0). Insigna de pe nume si cea din "Taskurile mele" arata numai taskurile nepreluate.
- Nu se jurnalizeaza afisarea notificarilor (nu modifica date); se jurnalizeaza schimbarile de termen si de setare.

### 3.9 Pagina "Adauga task utilizator" (meniul Administrare)

- Intrare noua in sectiunea **Administrare** a meniului (`MainLayout`, ca modulele cu `Nav`/`ModuleNavItem`), vizibila cu politica `perm:taskuri.add` (deci administratorului, sau cui primeste cheia) si doar cat modulul Taskuri merge. Ruta propusa: `/taskuri/adauga`.
- Pagina: la inceput, **cautare beneficiar dupa nume** cu `SearchableSelect` (optiunile arata nume + CUI/telefon, ca in `ExitDestinationPicker`). Butonul **"+ Adauga beneficiar"** deschide ca popup mecanismul existent `BeneficiaryEditor` (tiparul din `OfferPickup`: `delete-overlay` + `BeneficiaryEditor InitialName="<textul cautat>"`), cu validarea ANAF, jurnalul si dreptul `beneficiari.add` deja existente; dupa salvare beneficiarul nou este selectat automat. Butonul este dezactivat, cu `title` explicativ, daca utilizatorul nu are dreptul de a adauga beneficiari.
- Dupa alegerea beneficiarului apare acelasi formular de task ca in popup-ul de pe fisa beneficiarului (titlu, utilizator, status, termen, descriere, fisiere), ca o componenta comuna `TaskForm`: o singura implementare, doua locuri de folosire (pagina din meniu si butonul "Adauga task" de pe fisa beneficiarului, unde beneficiarul este deja stabilit).
- Dupa salvare: mesaj de reusita cu link catre sectiunea Taskuri a beneficiarului si formularul se goleste pentru un alt task (beneficiarul ramane ales, cu buton "Schimba beneficiarul"). Parasirea cu date necompletate cere confirmare (`UnsavedChangesTracker`).
- Pagina contine si tabelele de taskuri ale tuturor utilizatorilor (3.10), sub formular. Topic `.help` pentru pagina (`-Group help`), randul in `docs/HELP_DE_REGENERAT.md`.

### 3.10 Tabelele administratorului: taskuri active si finalizate

- Pe aceeasi pagina din Administrare (`/taskuri/adauga`), sub formularul de adaugare (card pliabil, deschis cand nu exista taskuri), doua taburi cu `count-badge`: **Active** (status In viitor sau In lucru) si **Finalizate**. Fiecare tabel este paginat cu `AuditPager` (10/25/50/100/Toate), fara limita fixa.
- Coloane: beneficiar (link spre fisa), titlu, utilizator, creat la, termen (rosu depasit / galben aproape), status, preluat (insigna "Nepreluat" pe fond rosu cat timp utilizatorul nu a preluat), fisiere (numar). Cautare dupa beneficiar/titlu/utilizator si filtre dupa utilizator si dupa termen (depasit / fara termen). Ordonare implicita: active dupa termen apoi data crearii; finalizate dupa data finalizarii (cele mai recente intai).
- **Starea taskului si utilizatorul sunt etichete de filtrare** (decizie 09.11.2026): in tabel, coloanele Status si Utilizator afiseaza valoarea ca eticheta (insigna) pe care se da clic; clicul adauga un filtru. Filtrele active apar deasupra tabelului ca etichete eliminabile (`audit-filter-chip` cu "x", tiparul din Jurnal si Miscari produs: "status: In lucru x", "utilizator: Popescu x"), combinate cu SI intre tipuri diferite; mai multe valori ale aceluiasi tip se combina cu SAU. Un clic pe o eticheta deja activa o scoate; "Sterge filtrele" le goleste. Deasupra tabelului stau si etichetele-scurtatura cu numar pentru fiecare stare (In viitor / In lucru, la Active) si fiecare utilizator care are taskuri, pentru filtrare fara a cauta in tabel. Nu mai exista liste derulante pentru status si utilizator; raman cautarea text si filtrul dupa termen. Filtrele se pastreaza la schimbarea paginii (pagina revine la 1 cand se schimba un filtru) si se golesc la parasirea paginii.
- Aceleasi etichete clicabile se folosesc si in tabelul de pe fisa beneficiarului (3.4) si in dashboard-ul utilizatorului (3.5, unde utilizatorul este el insusi, deci doar eticheta de status filtreaza).
- Iconite pe rand (regula "iconite sau butoane"): la taburile **Active** — editare (deschide popup-ul `TaskForm` cu beneficiarul fixat, inclusiv fisiere, utilizator, termen) si stergere (`DeleteConfirmationDialog`, cu mentiunea ca se sterg si fisierele); la **Finalizate** — doar vizualizare (popup in citire, cu fisierele descarcabile). Cheile cerute: `taskuri.edit` pentru editare, `taskuri.delete` pentru stergere, `taskuri.view` pentru tabele.
- Reimprospatare live: un task preluat sau schimbat de utilizator apare imediat in tabelul administratorului (`ChangeFeed`). Fisa beneficiarului (3.4) arata aceleasi taskuri, filtrate pe beneficiar, cu aceleasi iconite.
- Un task finalizat poate fi redeschis numai de utilizator (schimbarea statusului) sau de administrator din popup-ul de vizualizare (buton "Redeschide", jurnalizat "Schimbare status task").

### 3.7 Alte puncte

- Stergerea unui beneficiar sau dezactivarea/stergerea unui utilizator cu taskuri: se urmeaza regula existenta pentru dependente (la implementare se verifica exact; propunerea este blocare cu mesaj clar, nu stergere in cascada tacuta).
- Arhivare: stergerea unui task este definitiva, dar jurnalul pastreaza continutul complet.
- Notificari prin e-mail sau push: in afara acestei etape (notificarile sunt in aplicatie, sectiunea 3.8).

## 4. Etape si verificari

1. Schema + repository + magazie de fisiere + jurnal + reguli de acces. Teste MariaDB (sectiune noua "Tasks"): creare/modificare/stergere, un utilizator nu vede taskul altuia, nu sterge fisierul administratorului, isi sterge propriul fisier, preluare, mutare, concurenta, jurnal pentru fiecare actiune, curatarea fisierelor la stergere.
2. Componenta comuna `TaskForm`, sectiunea de pe beneficiar + popup (adaugare/editare/stergere/fisiere) si pagina "Adauga task utilizator" din Administrare (cautare beneficiar + popup de adaugare beneficiar).
3. Dashboard utilizator, meniu cu insigna, rosu/preluare, live. Teste de componente (taburi, paginare, rand rosu).
4. Verificare in browser cu `claude.dev` (administrator) si un utilizator simplu de test creat in baza de dezvoltare si sters dupa verificare; fisiere reale, tema light si dark, telefon. Versiune de modul, topicuri `.help` minime + `docs/HELP_DE_REGENERAT.md`, `Teste utilizator/`, `docs/HARTA_COD.md`.

## 5. Decizii (09.11.2026)

1. Camp "titlu" scurt in plus fata de descriere: DA.
2. Un singur utilizator destinatar pe task: DA.
3. Utilizatorul isi sterge fisierele incarcate de el (nu pe ale administratorului): DA.
4. Preluarea NU schimba statusul; statusul initial il alege administratorul (implicit In viitor).
5. Fondul rosu apare la creare si la mutarea taskului la alt utilizator (nu la orice editare).
6. Administratorul poate fi si el destinatar (orice cont activ): DA.
7. Drepturile de gestionare: chei in matricea de permisiuni (`taskuri.add/edit/delete`, implicit doar Administrator, acordabile unui tip de utilizator).
8. Termen limita optional (data), cu notificari intr-un tab propriu al utilizatorului in pagina Notificari (3.8).
9. Intrare "Adauga task utilizator" in Administrare, cu cautare beneficiar dupa nume si adaugare beneficiar prin popup-ul existent (3.9).
10. Pe aceeasi pagina, tabele paginate cu taskurile active si finalizate ale tuturor utilizatorilor; editare si stergere pe taskurile active (3.10).

11. Starea taskului si utilizatorul sunt etichete de filtrare (clic pe eticheta = filtru, filtre active ca etichete eliminabile; 3.10).

Ramas de confirmat (au valori implicite): (a) taskurile finalizate au doar vizualizare (fara editare/stergere) — alternativa: aceleasi iconite si pe finalizate; (b) tabelele stau pe aceeasi pagina cu formularul de adaugare — alternativa: intrare separata "Taskuri utilizatori" in Administrare.

Ramas de confirmat (are valoare implicita): perioada de avertizare inainte de termen = setare globala, implicit 3 zile (alternativa: pe fiecare task).
