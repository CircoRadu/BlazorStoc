# Verificări — versiunea 0.2

## Imagini produse — 17 septembrie 2026

- Formularul produsului acceptă o imagine din fișier sau clipboard și refuză salvarea unui produs nou fără imagine.
- Sunt acceptate JPG, PNG, WebP și GIF de maximum 5 MB; formatul este identificat din conținut, iar SVG este refuzat.
- Catalogul afișează un thumbnail clicabil, panoul produsului include imaginea, iar vizualizarea mărită se deschide într-o fereastră dedicată.
- În modul MariaDB, imaginile noi sunt stocate în `data/product-images`, în afara bazei și a directorului public, și sunt livrate printr-o rută care cere autentificare.
- 71 de verificări automate au trecut. Build Release reușit fără avertismente și fără erori.
- Verificarea în browser a confirmat obligativitatea imaginii, încărcarea unui PNG real, salvarea produsului, thumbnail-ul, imaginea din detalii și vizualizarea mărită.

## Secțiunea Beneficiari — 17 septembrie 2026

- A fost adăugat CRUD-ul asincron pentru beneficiari, cu nume, CUI unic, căutare și sincronizare automată la 15 secunde.
- Ambele roluri pot vedea secțiunea și pot adăuga, edita sau șterge beneficiari; meniurile Utilizatori și Jurnal rămân ascunse contului limitat.
- Modificările beneficiarilor sunt jurnalizate cu operator, rol, operație, țintă și timestamp.
- 67 de verificări automate au trecut, inclusiv normalizarea și unicitatea CUI-ului, validarea, concurența la editare, ștergerea și auditul.
- Verificarea în browser a confirmat că `utilizator.demo` vede meniul Beneficiari și poate adăuga un beneficiar; totalul și indicatorii s-au actualizat imediat.
- Build Release reușit fără avertismente și fără erori. Nu a fost generat niciun fișier SQL de upgrade.

## Actualizare utilizatori 0.3 — 16 septembrie 2026

- Au fost adăugate CRUD-ul utilizatorilor, autentificarea din `web_user`, rolurile Administrator/Utilizator și autorizarea scrierilor de produse.
- 58 de verificări automate au trecut: catalogul și CRUD-ul, autentificarea ambelor roluri demonstrative, refuzul parolei greșite, autentificarea unui cont nou, starea distinctă pentru contul inactiv, protecțiile administratorului, dreptul utilizatorului standard de a opera produse și jurnalizarea fără parole.
- Verificarea vizuală în browser a trecut în modul demonstrativ: validarea formularului gol, adăugarea unui utilizator limitat, actualizarea numelui și promovarea la Administrator, actualizarea indicatorilor și afișarea dialogului de ștergere.
- Build Release final reușit fără avertismente și fără erori; toate cele 58 de verificări automate au fost rerulate cu succes.
- Autentificarea este obligatorie și în preview. Verificarea în browser a confirmat că rolul Utilizator vede și poate opera Produse fără meniul Utilizatori, iar Administratorul vede ambele secțiuni și poate deschide administrarea conturilor.
- Verificarea în browser a confirmat mesajul explicit pentru cont inactiv și dashboard-ul administrativ cu timestamp UTC, actor, rol, operație, țintă și detalii. Dezactivarea și reactivarea contului demonstrativ au produs două evenimente, fără parolă în jurnal.
- Butonul manual „Actualizează” a fost eliminat de pe pagina Utilizatori. Lista se sincronizează automat la 15 secunde; verificarea în browser a confirmat că formularul de adăugare rămâne deschis și intact peste un ciclu de sincronizare.
- Nu se livrează fișier SQL pentru 0.3 și nu s-a modificat sau accesat nicio bază de date. Integrarea schemei utilizatorilor este amânată pentru o fază separată.
- Proiectul rulează direct cu .NET; fișierele și pașii Docker au fost eliminați din faza curentă.

## Actualizare asincronă — 16 septembrie 2026

- Toate operațiile MariaDB folosesc API-urile asincrone pentru deschidere, citire, scriere, commit, rollback și eliberarea resurselor.
- Adăugarea și editarea întorc acum task-uri asincrone inclusiv când validarea eșuează; apelantul primește uniform erorile prin `await`.
- Produsele și grupurile categorie/subcategorie se încarcă în paralel; evenimentele Blazor care pornesc încărcări sau scrieri returnează `Task` și propagă anularea componentei.
- Gazda SQL implicită este `127.0.0.1`. Integrarea și publicarea pe NAS/QNAP sunt în afara fazei curente și nu au fost executate.

## Verificări anterioare — 15 septembrie 2026

- Build și publish Release reușite cu SDK .NET 9.0.317. O încercare intermediară de rebuild a fost blocată de instanța demo activă; după oprirea ei, verificările și publicarea au reușit.
- 35 verificări automate trecute: 10 pentru catalog/căutare/anulare și 25 pentru CRUD, validare, izolare demo, versiuni concurente, ștergere, identitate obligatorie și blocarea scrierilor spre baza veche înainte de conectare.
- 8 verificări HTTP auth/CSRF/logout trecute pe instanța locală `127.0.0.1:5083`, cu destinația SQL fictivă `127.0.0.1:1`. Instanța de test a fost oprită după verificare.
- Browser desktop, `127.0.0.1:5082`, mod demo: formular gol respins cu mesaje în română; adăugare cu diacritice și categorie/subcategorie noi; produs afișat în catalog și detalii; editare; schimbarea cantității respinsă fără motiv și acceptată cu motiv; ștergere blocată cu stoc nenul; ștergere reușită după corecție la zero; numărul de produse și indicatorii actualizați. Aspectul catalogului a fost verificat vizual.
- Codul MariaDB folosește parametri SQL, tranzacții SERIALIZABLE, blocare de rânduri, versiuni de produs și jurnal în aceeași tranzacție. Acestea au fost revizuite în surse, **nu validate prin integrare cu un server SQL**.
- Scriptul `database/BlazorStoc_upgrade_0.2.sql`, conversia câmpurilor la utf8mb4, granturile contului de scriere, rollback-ul la eroare de jurnal, concurența dintre conexiuni și protecția relațiilor trebuie testate pe o bază MariaDB locală de test înainte de folosire reală. Nu s-a importat backupul.
- Scriptul inițial de creare și arhiva 0.1 rămân istorice; pentru 0.2 se aplică atât scriptul de creare (numai dacă baza nu există), cât și cel de actualizare. Nicio migrare automată la pornire.

## Verificări istorice — versiunea 0.1

- Build și publish Release: reușite pe Windows cu SDK .NET 9.0.317.
- 10 verificări automate pentru căutare, câmpuri, filtre combinate, stoc negativ/zero, catalog gol și anulare: reușite.
- 8 verificări HTTP pe o instanță locală izolată: redirecționare la login, CSS accesibil, antiforgery obligatoriu, parolă incorectă, login corect, pagină autentificată, logout și acces refuzat după logout.
- Browser desktop: catalog, căutare, panou de detalii, pagina a doua, filtrul de stoc negativ și revenirea la pagina întâi verificate.
- Mod MariaDB cu adresă locală indisponibilă: eroare distinctă, fără înlocuire cu date demonstrative. Nicio conexiune către NAS în teste.
- Compatibilitatea interogării cu schema furnizată a fost verificată prin citirea definițiilor. Nu s-a importat backupul și nu s-a executat un test de integrare pe MariaDB 5.5.68.
- Adaptările și testarea pentru mobil sunt în afara scopului, conform cerinței utilizatorului.

Pentru repetarea testelor HTTP, pornește separat o instanță locală de test, în modul MariaDB, cu DB host 127.0.0.1, port 1, Database__Password=local-test-only și Authentication__Password=local-test-password-123. Nu folosi aceste valori pentru instalare. Apoi rulează:

```sh
dotnet run --project tests/BlazorStoc.Checks -c Release -- --http http://127.0.0.1:5081
```

Portul trebuie să coincidă cu portul instanței de test. Verificările HTTP refuză destinații care nu sunt localhost.

# Verificare Task 0 — collapse unitar

Componenta comună `CollapsibleSection` trebuie verificată după orice modificare a meniului lateral sau a unei ierarhii din interfață:

1. Pe pagina principală, secțiunea „Administrare” pornește închisă și răspunde identic la click, Enter și Space; `aria-expanded` alternează între `false` și `true`.
2. Pe o rută de administrare, secțiunea „Administrare” pornește deschisă și păstrează vizibilă ramura activă.
3. În meniul produselor, deschiderea unei categorii aplică filtrul categoriei și afișează subcategoriile; restrângerea categoriei active revine la toate produsele.
4. În administrarea categoriilor, toate categoriile pornesc închise. Deschiderea uneia nu modifică starea celorlalte.
5. Butoanele „Adaugă subcategorie” și „Editează categoria” nu deschid și nu închid secțiunea părinte.
6. Conținutul unei secțiuni închise nu intră în ordinea de focus, iar antetul expune `aria-controls` către regiunea subordonată.
7. La lățimi de 800 px și la dimensiunea desktop implicită, pagina nu produce depășire orizontală, iar acțiunile rămân accesibile.
8. Pentru ierarhii imbricate viitoare, fiecare nivel primește un `Id` unic și o instanță proprie `CollapsibleSection`; starea unui nivel nu trebuie să modifice frații.

Validarea din 24 septembrie 2026 a acoperit manual toate cele trei utilizări existente, inclusiv mouse, Enter, Space, ARIA, filtrarea catalogului, independența acțiunilor și viewportul de 800 px. Buildul Release și suita `BlazorStoc.Checks` au trecut integral.

## Cod produs

Verificări pentru câmpul „Cod produs” (fostul „Denumire”):

1. Formularele „Adaugă produs” și „Editează produsul” afișează eticheta „Cod produs *” și nota despre codul producătorului; nu există buton de generare a codului.
2. Salvarea fără cod afișează „Completează codul produsului.”; un cod de peste 100 de caractere este respins.
3. Un cod care diferă de unul existent doar prin spații, majuscule/minuscule sau diacritice este respins cu mesajul „Codul produsului «…» există deja în catalog…”, iar formularul rămâne completat.
4. Catalogul, panoul de detalii, titlul formularului de editare, dialogul de ștergere și jurnalul nu afișează identificatorul intern în forma `#<număr>`; evenimentele vechi de produs din jurnal sunt afișate fără prefixul `#<număr> · `.
5. Linkurile din jurnal către produse continuă să folosească identificatorul stabil (`/produse?edit=<id>`).

Suita automată acoperă unicitatea normalizată în modul demonstrativ și SQLite, inclusiv două sesiuni SQLite concurente care încearcă același cod (una singură reușește), mesajele de validare, ținta de audit și de arhivă fără `#<id>` și afișarea evenimentelor vechi. Pentru MariaDB, unicitatea folosește aceeași comparație normalizată sub tranzacție `Serializable` cu `FOR UPDATE`; nu a fost testată pe un server MariaDB în acest ciclu.

## Intrări și ieșiri pentru un produs (Task 1)

Verificări pentru pagina `/produse/<id>/miscari`:

1. Codul produsului și „↗” din catalog deschid pagina; panoul de detalii din catalog nu mai există. `/miscari/<id>` redirecționează către pagina produsului mișcării; `/produse?sterge=<id>` deschide dialogul de ștergere al produsului.
2. Formularul: data implicită azi, comutatorul Intrare/Ieșire, „Beneficiar” și „Proiect” doar la Ieșire (proiectul se poate bifa numai după alegerea beneficiarului), descriere și cantitate obligatorii. „Beneficiar”/„Proiect” bifate fără selecție sunt respinse cu mesaj, iar formularul rămâne completat. După salvare se golesc descrierea și cantitatea; data, tipul și beneficiarul rămân.
3. Tabelul: Data (`zz-LL-aaaa`), Intrare/Ieșire cu `[*]` pentru mișcările modificate, Număr bucăți, Descriere, Beneficiar (beneficiar și proiect ca linkuri); filtru, sortare după dată, paginare 10/20/50/Toate; „Total produse în stoc” actualizat după fiecare operație, inclusiv negativ.
4. Editare: motiv obligatoriu, tipul nu se schimbă, rezumat „Cantitate veche/nouă · Corecție stoc”, respingerea unei editări fără modificări sau pe o versiune veche. Istoric: click dreapta pe un rând cu `[*]` (sau butonul „Istoric”) afișează operatorul, ora locală, valorile, corecția și motivul; Escape închide dialogul.
5. Ștergere: dialogul în doi pași afișează corecția de stoc; mișcarea și istoricul ei sunt mutate în `archive_stock_movements`/`archive_relations` (`IstoricMiscareStoc`), stocul se corectează, iar un produs sau proiect cu mișcări nu poate fi șters.
6. Jurnalul: evenimente `MiscareStoc` (Adăugare/Editare/Ștergere) cu ținta „cod produs”, detalii, motiv și legătura către arhivă la ștergere; linkurile duc la `/miscari/<id>`.
7. Migrarea SQLite (schema 6): coloanele noi se adaugă tabelului `stock_movements` existent, iar produsele cu stoc și fără mișcări primesc o singură dată o mișcare „Stoc initial”.

Suita automată (`tests/BlazorStoc.Checks`, 285 de verificări) acoperă regulile de domeniu, stocul atomic (8 adăugări simultane), corecțiile la editare/ștergere, istoricul, arhivarea, auditul, ordonarea după dată, filtrarea, paginarea, `GetForProjectAsync`, blocarea ștergerii produsului/proiectului și migrarea. Verificat în browser (desktop, 375 și 768 px): vezi `TODO.md`, „Intrări și ieșiri pentru un produs existent”. Neverificat: MariaDB pe un server real; ieșirea cu proiect, filtrul, sortarea și paginarea în browser.

## Stoc exclusiv prin mișcări (Task 0)

Verificări pentru eliminarea stocului introdus manual:

1. Formularul „Adaugă produs” nu mai conține „Stoc inițial”; afișează nota „Produsul nou este creat cu stoc 0. Stocul se modifică numai prin intrări și ieșiri.”
2. Formularul „Editează produsul” afișează „Stoc curent: N buc.” ca text, fără câmp de introducere (`#product-quantity` nu mai există).
3. Repository-urile (demo, SQLite, MariaDB) creează produsul cu stoc 0 și nu scriu `quantity` / `produs_cantitate` la editare; `ProductInput` nu mai are proprietatea `Quantity`, deci serverul nu poate primi o cantitate de la client.
4. `ProductRules.CheckCurrent` ignoră stocul (o modificare de stoc nu invalidează o editare deschisă), dar respinge în continuare orice altă diferență; regula de ștergere folosește stocul curent din bază, nu instantaneul formularului.
5. Jurnalul creării și editării produsului nu mai conține „Cantitate”. Reconcilierea unică a stării vechi din `audit-events.jsonl` (`SqliteLocalStore`) citește în continuare „Cantitate” din evenimentele istorice.

Suita automată (`tests/BlazorStoc.Checks`, 234 de verificări) acoperă: produs nou cu stoc 0, editare care păstrează stocul, stoc negativ vechi păstrat, diferență de stoc în instantaneu (SQLite și demo), ștergere blocată de stocul curent, `CheckCurrent` și absența cantității din detaliile jurnalului. Verificare în browser: formularele de creare și editare (fără câmp de cantitate). Neverificat: MariaDB pe un server real.

## Proiecte — modelul de date (Subtask finalizat din Task 2)

Regulile de domeniu din `Services/Projects.cs` sunt acoperite integral de `BlazorStoc.Checks` (29 de verificări):

1. Denumirile proiectelor și observațiilor sunt curățate prin `TextNormalization.ForObjectNameOrCode` (spații exterioare, spații consecutive, diacritice); textele libere prin `ForStorage`.
2. Unicitatea denumirii proiectului este verificată numai în cadrul aceluiași beneficiar, fără diferențe de majuscule, diacritice sau spațiere; aceeași denumire este acceptată la alt beneficiar, iar proiectul editat își poate păstra denumirea.
3. Crearea pornește de la versiunea 0, editarea incrementează versiunea și păstrează `CreatedAtUtc`; timestampurile care nu sunt UTC sunt respinse; o versiune învechită sau un proiect/observație eliminat(ă) este respins(ă).
4. Editările proiectelor și observațiilor cer motivare; denumirile goale sau mai lungi de 200 de caractere sunt respinse; observația cere autor.
5. Metadatele fișierelor păstrează numai numele fișierului din calea trimisă, generează un nume intern unic (GUID + extensie validată), resping fișierele goale și hash-urile care nu sunt SHA-256.
6. `BeneficiaryRules.CheckNoLiveProjects` respinge ștergerea unui beneficiar cu proiecte live. Aplicarea regulii în repository-uri s-a făcut ulterior, odată cu tabelele proiectelor (vezi secțiunea următoare).

## Proiecte — persistență, pagini, fișiere, audit și arhivare (Task 2 finalizat)

Implementat de Claude la 24 septembrie 2026, peste modelul de domeniu de mai sus.

### Persistență

- SQLite: tabelele `projects`, `project_observations`, `project_observation_files`, plus `archive_projects`, `archive_project_observations`, `archive_project_observation_files` (schema versiunea 5). Index unic `(beneficiary_id, normalized_name)` pe `projects`. `stock_movements.project_id` pregătește contractul pentru viitoarele mișcări de stoc.
- MariaDB: tabelele `project`, `project_observation`, `project_observation_file` sunt create de aplicație la prima folosire (nu există în `BlazorStoc_create.sql`, fiind o entitate nouă); arhiva corespunzătoare este creată de `MariaArchiveSchema` (versiunea 3). Nu a fost testată pe un server MariaDB real în acest ciclu. Coloana `io.id_project` (legătura mișcare de stoc → proiect) este amânată până la implementarea efectivă a modulului de intrări/ieșiri; până atunci, MariaDB nu blochează ștergerea unui proiect pe baza mișcărilor de stoc (SQLite o face).
- Fișierele observațiilor sunt scrise pe disc (`data/project-files` în SQLite, configurabil prin `App:ProjectFilesPath`), niciodată ca BLOB; metadatele includ hash SHA-256, dimensiune, tip media verificat prin semnătura conținutului (nu doar extensia sau tipul declarat de browser) și autor.

### Interfață

- `/beneficiari/{id}`: pagina de detaliu a beneficiarului, cu numele, CUI-ul, lista proiectelor (căutare + paginare) și butonul „Adaugă proiect” (beneficiar preselectat, needitabil).
- `/proiecte/{id}`: pagina proiectului, cu beneficiarul (link), observațiile generale, tabelul de navigare cu „Echipamente” mereu primul, apoi observațiile ordonate descrescător după data creării.
- `/proiecte/{id}/echipamente`: stare goală explicită, fără date simulate, până la implementarea mișcărilor de stoc.
- `/proiecte/{projectId}/observatii/{observationId}`: denumire, conținut, autor, timestampuri locale, listă de fișiere cu link de descărcare (`/media/project-files/{fileId}`, endpoint autorizat), încărcare multiplă și eliminare individuală cu motiv și confirmare.
- Ștergerea proiectului și a observației folosește `DeleteConfirmationDialog` existent (motiv + cuvântul `sterge`); ștergerea unui fișier folosește același dialog cu motiv obligatoriu.
- Beneficiarul din tabelul „Beneficiari” este acum link către pagina sa de detaliu.

### Audit și arhivare

- Evenimente noi: `AuditEntities.Project` („Proiect”), `ProjectObservation` („Observatie”), `ProjectObservationFile` („FisierObservatie”), cu `Details`/`Motif` fără conținut de fișier.
- „Țintă” pentru proiecte este link către `/proiecte/{id}` la adăugare/editare (`AuditNavigation.EditUrl`); pentru observații rămâne text (identificator compus proiect+observație, neînregistrat încă în registrul de rute — vezi TODO).
- Ștergerea unui proiect arhivează, în aceeași operație de arhivă, proiectul, observațiile și fișierele rămase (ca relații + copii fizice), numai dacă nu există mișcări de stoc asociate (verificat în SQLite).

### Verificare manuală (browser, admin demo, `http://127.0.0.1:5082`)

1. Beneficiar → link „Construct Demo SRL” → pagina de detaliu → „Adaugă proiect” → formular cu beneficiarul blocat → salvare → proiectul apare în listă cu data ultimei modificări.
2. Pagina proiectului → tabelul de navigare afișează „Echipamente” primul → link funcțional către starea goală explicită.
3. „Adaugă observație” → salvare → navigare automată la pagina observației.
4. Ștergerea observației: dialogul de confirmare afișează corect „Observația nu va mai fi folosită” (acord de gen corectat în `DeleteConfirmationRules.DefaultReason`, care anterior genera doar forma masculină „folosit”); după confirmare cu `sterge`, observația dispare din proiect.
5. Ștergerea proiectului: dialogul afișează „Proiectul nu va mai fi folosit”; după confirmare, revine la pagina beneficiarului, iar proiectul nu mai apare în listă.
6. Jurnalul de activitate afișează evenimentele „Adăugare”/„Ștergere” pentru „Proiect” și „Observatie”, cu `Details` (valori inițiale/finale), `Motif` (fără diacritice, conform regulii generale de stocare) și fără conținut de fișier.

Suita automată (`BlazorStoc.Checks`, 229 verificări) acoperă, pe lângă modelul de domeniu: persistența proiectelor/observațiilor/fișierelor după repornire, unicitatea per beneficiar (inclusiv mesajul de duplicat cu numele beneficiarului), reutilizarea denumirii la alt beneficiar, două sesiuni SQLite concurente care încearcă același proiect pentru același beneficiar (una singură reușește), concurența optimistă la editare, ștergerea individuală a unui fișier (arhivare + indisponibilitate ulterioară), blocarea ștergerii beneficiarului cu proiect live, ștergerea proiectului cu arhivarea observației/fișierului rămas, evenimentele de audit pentru proiect/observație/fișier și rândurile scrise în `archive_projects`/`archive_project_observation_files`.

## Proiecte — context de navigare, „Echipamente”, ruta observației și evenimente de modificare (Task 2, subtaskurile 2.3–2.5)

Verificat la 25 septembrie 2026.

- **Suita automată** (`BlazorStoc.Checks`, 312 verificări; 27 noi): un eveniment pentru fiecare operație asupra proiectelor, observațiilor și fișierelor (cu proiect, observație, beneficiar, sesiune de origine și moment UTC), niciun eveniment pentru operații respinse, notificarea ambilor beneficiari la mutarea unui proiect, evenimente fără nume, texte sau conținut de fișiere, izolarea abonaților care eșuează, dezabonarea, filtrarea evenimentelor pe pagini, auditul încărcării unui fișier (fără conținut) și respingerea unui fișier pentru o observație inexistentă fără fișier rămas pe disc, ruta `/observatii/{id}` din jurnal, memorarea filtrului și a paginii listei de proiecte.
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator, două tab-uri):
  1. Filtrul din lista de proiecte apare în adresă (`?q=hala`) fără pierderea caracterelor tastate.
  2. Linkul „înapoi” din pagina proiectului duce la `/beneficiari/{id}?q=hala`, cu filtrul restaurat.
  3. Butonul Înapoi al browserului restaurează filtrul și poziția de derulare (salvată la clic pe proiect), o singură dată.
  4. „Echipamente” fără ieșiri afișează starea goală; după o ieșire cu proiect afișează codul produsului, cantitatea, data, operatorul și „Mișcarea nr. N” (linkuri către produs și mișcare).
  5. Linkul observației din jurnal (`/observatii/3`) deschide pagina din proiect; `/observatii/99999` afișează „Observația nu mai există”.
  6. O observație adăugată în alt tab apare live în lista proiectului, fără a schimba formularul de editare deschis (textul local a rămas).
  7. O editare a proiectului din alt tab afișează notificarea „…modificat de alt utilizator…” peste formularul deschis; lista proiectelor din pagina beneficiarului se actualizează live.
  8. Datele de test au fost șterse prin fluxul normal (mișcarea arhivată, stocul revenit la 12, proiectul și observațiile arhivate).
- **Neverificat manual**: paginarea listei peste 10 proiecte, reîmprospătarea paginii observației la ștergerea proiectului de altă sesiune, MariaDB pe un server real.

## Task 3 — Navigarea din jurnal către pagina obiectului (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 319 verificări; 7 noi): rutele produselor, beneficiarilor, utilizatorilor și proiectelor din tip și identificator, fără `?edit=`; textul țintei ignorat; identificatori invalizi sau lipsă; conectări/deconectări și tipuri fără pagină ca text; obiecte cu ștergere ulterioară fără link (evenimentele ulterioare și alte obiecte păstrează linkul); starea jurnalului în adresă (doar valorile diferite de implicit, escape, valori invalide → implicit).
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator):
  1. Căutarea „casca” în jurnal apare în adresă (`/jurnal?q=casca`) fără pierderea caracterelor tastate; linkul țintei este `/produse/3`.
  2. Linkul deschide pagina produsului în consultare (fără editor); Înapoi din browser restaurează căutarea și rezultatul filtrat.
  3. `/utilizatori` are linkuri către `/utilizatori/{id}`; pagina afișează datele contului, fără editor deschis; „Editează” deschide editorul, „Anulează” îl închide; `/utilizatori/99999` afișează „Utilizatorul nu mai există”.
  4. `/beneficiari/2` are „Editează” (editorul nu se deschide singur; se deschide și se închide la cerere, fără salvare).
- **Neverificat manual**: paginarea și dimensiunea paginii prin adresă în browser, poziția de derulare a jurnalului, autorizarea utilizatorului limitat pe `/utilizatori/{id}` (rolul este verificat prin atributul de rută și în pagină, ca la `/utilizatori`), MariaDB pe un server real.

## Task 4 — Identificarea beneficiarului cu CUI duplicat (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 324 verificări; 5 noi): mesajul exact `BeneficiaryRules.DuplicateCuiMessage` cu numele salvat (nu cel tastat) la creare în modul demonstrativ și SQLite; editarea către un CUI existent (numele proprietarului, valorile formularului păstrate, obiectul neschimbat); editarea fără schimbarea CUI-ului nu îl raportează pe beneficiarul curent ca duplicat; mesajul fără proprietar cunoscut.
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator, modul demonstrativ): „Adaugă beneficiar” cu numele „Firma Test Duplicat” și CUI `10000003` → „Există deja un beneficiar cu acest CUI: «Servicii Industriale SA».”; formularul rămâne deschis cu ambele valori; nu s-au salvat date.
- **Neverificat manual**: editarea în browser, MariaDB pe un server real (inclusiv traducerea erorii 1062 la cereri concurente).

## Formularul de editare a proiectului — fără câmpul „Beneficiar” (25 septembrie 2026)

- Proiectul rămâne legat de beneficiarul de la creare: formularul de editare (`ProjectEditor`) nu mai are câmpul „Beneficiar” și nu mai încarcă lista beneficiarilor; formularul de creare din pagina beneficiarului păstrează beneficiarul fix, afișat dezactivat.
- Browser (`http://127.0.0.1:5082`, sesiune autentificată de utilizator): `/proiecte/{id}` → „Editează” arată numai Denumire, Observații și Motivare; nu s-au salvat date. `BlazorStoc.Checks`: 324 verificări trecute.
- **Neschimbat**: repository-urile și `ProjectRules` acceptă în continuare un `BeneficiaryId` diferit la `UpdateAsync` (mutarea proiectului), dar nicio pagină nu o mai declanșează.

## Task 5 — Confirmarea salvărilor (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 329 verificări): rezumatul (`SaveSummary`) listează doar câmpurile modificate, arată „(gol)” și scurtează valorile lungi; regula de server care respinge schimbarea beneficiarului unui proiect (domeniu, SQLite cu proiectul și auditul neschimbate, feed fără eveniment).
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator, modul demonstrativ), pagina `/beneficiari/2`: „Editează” + modificarea numelui + motivare → popup „Salvezi modificările beneficiarului?” cu tabelul Câmp / Valoare actuală / Valoare nouă și motivarea; „Anulează” lasă formularul deschis cu valorile introduse și nu salvează; „Confirmă salvarea” salvează („Beneficiarul a fost actualizat.”). Numele a fost readus la valoarea inițială prin același flux (două evenimente „Editare” în jurnal).
- **Neverificat manual**: popup-ul în editoarele de produs, proiect, observație, utilizator, categorie/subcategorie și mișcare (același component); tastatura (Escape) în browser; MariaDB pe un server real.

## Data mișcării — calendar numai pentru selectare, fără date viitoare (25 septembrie 2026)

- Regula cerută de utilizator: câmpul „Data mișcării” este un calendar numai pentru selectare (nu se tastează), ultima zi afișată/selectabilă este azi, iar o intrare sau o ieșire nu poate avea o dată viitoare. (Într-un ciclu anterior, mesajul utilizatorului a fost înțeles greșit ca o problemă și limita a fost ridicată la 2100; modificarea a fost anulată.)
- Server: `StockMovementRules.Validated` respinge orice dată după ziua curentă (`FutureDateMessage`), la creare și la editare, în SQLite și MariaDB (repository-urile apelează aceeași regulă); data minimă rămâne 1 ianuarie 1990.
- Interfață: „Data mișcării” (adăugare și editare) este componenta `Components/Shared/PickOnlyDate.razor`: un câmp text `readonly` (afișat `zz-ll-aaaa`, cu pictogramă de calendar) care nu poate fi tastat, deasupra unui `input type=date` nativ ascuns (`min=1990-01-01`, `max` = azi) care oferă calendarul; `wwwroot/date-pick-only.js` deschide calendarul la clic, Enter, Spațiu, F4 sau Alt+Săgeată jos. (Un `input type=date` cu atributul `readonly` nu poate deschide calendarul, de aceea sunt două câmpuri.)
- **Suita automată** (`BlazorStoc.Checks`, 335 verificări): data de azi și cele din trecut sunt acceptate; data viitoare este respinsă la intrare și ieșire, la creare și la editare; SQLite respinge o mișcare viitoare și lasă stocul neschimbat. Testele care foloseau 2099 au fost mutate pe ziua curentă.
- **Browser** (`http://127.0.0.1:5082`, modul demonstrativ): câmpul apare ca text doar pentru citire (`25-09-2026`); clic pe el deschide calendarul, zilele după azi sunt dezactivate, iar săgeata spre luna următoare este inactivă; alegerea unei zile din calendar (Săgeată stânga + Enter) a schimbat valoarea în `24-09-2026`; tastarea cifrelor nu schimbă nimic; același câmp apare în dialogul de editare a mișcării.
- **Neverificat manual**: selectarea cu tastatura (Enter/Spațiu/F4) în browser; validarea de server în browser (acoperită de teste).

## Task 6 — Confirmare la părăsirea formularului de adăugare (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 337 verificări; 2 noi): `ProductMenuSelection.IsSameSelection` recunoaște aceeași categorie/subcategorie (fără diferențe de majuscule, diacritice codificate, `/` final, parametri străini) și detectează altă categorie, altă subcategorie sau „Toate produsele”.
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator, modul demonstrativ): cu „Adaugă produs” deschis și „COD-TEST-6” introdus, clic pe o categorie → popup „Părăsești adăugarea produsului?”, meniul rămâne pe „Toate produsele”; „Continuă adăugarea” păstrează formularul cu valoarea introdusă; „Părăsește adăugarea” închide formularul și deschide categoria „Scule electrice”; fără formular deschis, clicul pe o altă categorie navighează direct, fără popup; un clic pe o subcategorie (formular deschis) afișează popup-ul, iar Escape păstrează formularul și selecția curentă. Nu s-a salvat niciun produs.
- **Neverificat manual**: imaginea selectată păstrată după anulare (editorul nu este re-randat, dar nu s-a încărcat o imagine în test), ecran tactil.
