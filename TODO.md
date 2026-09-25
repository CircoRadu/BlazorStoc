# TODO — funcționalități viitoare

## Regula de prioritizare

- Taskurile active sunt ordonate crescător după prioritate; numărul cel mai mic indică prioritatea cea mai mare.
- Când se introduce un task nou pe o poziție existentă, taskul aflat pe acea poziție și toate taskurile active următoare sunt mutate cu o poziție mai jos și renumerotate.
- Prima parte a fișierului conține numai ce mai trebuie implementat: taskurile active și, în cadrul lor, doar subtaskurile nefinalizate, renumerotate.
- Tot ce este terminat (taskuri, subtaskuri, elemente bifate) se mută în arhiva „Taskuri finalizate” de la finalul documentului, imediat ce este finalizat.

# Taskuri active

## Ordinea de implementare optimizată

Taskurile 0–9 și mecanismul collapse sunt finalizate și arhivate mai jos. Singurul task activ este Task 10 (situația de inventar), care se bazează pe lucrări finalizate și poate fi implementat independent, în cicluri strict secvențiale Codex–Claude; agenții nu lucrează niciodată simultan.

## Task 10 — Situația de inventar (pagina „Inventar” și fișierul PDF)

Adaugă în meniul principal secțiunea „Inventar” și o pagină din care utilizatorul alege categoriile și subcategoriile pentru care se generează o situație de inventar în format PDF, folosită la numărarea fizică a stocului. Pagina reutilizează structura vizuală și mecanismul collapse din „Categorii și subcategorii”, dar este numai pentru consultare și selectare. Depinde de structura catalogului, de „Cod produs”, de stocul calculat din mișcări (Taskurile 0–1) și de jurnalul de activitate (finalizate); nu depinde de alte taskuri active.

### Decizii stabilite cu utilizatorul (25 septembrie 2026)

- „Valoare reală” rămâne **goală** (se completează manual la numărare).
- Produsele cu **stoc negativ rămân** în situație și când opțiunea „stoc 0” este bifată; textul lor din tabelul generat este scris cu **roșu**.
- „Generat la” folosește momentul (timestamp-ul) în care se generează formularul, convertit la **ora locală**, în forma `dd/mm/aaaa` și ora (`oo:mm`).
- Fontul este **gratuit** (licență liberă de redistribuire, cu diacritice românești). Numele categoriilor și ale subcategoriilor sunt **bold, mărime 14**; restul textului este normal, **mărime 12**.
- Generarea situației se **jurnalizează**, cu utilizatorul care a generat-o.
- Meniul principal: sub „Inventar” există intrarea **„Generare situație inventar”** (duce la pagina descrisă aici) și intrarea **„Preluare inventar”** (funcționalitate care se implementează ulterior; aici există doar intrarea).

### Subtask 10.1 — Pagina, ruta, meniul și dashboard-ul

- [ ] Creează pagina la ruta stabilă `/inventar`, cu titlul „Inventar” (`<h1>` și titlul paginii), accesibilă utilizatorilor autentificați care pot consulta produsele (aceleași drepturi ca pentru catalog); nu cere rol de administrator.
- [ ] Adaugă în meniul principal (bara laterală, secțiunea „Spațiu de lucru”, între „Produse și stocuri” și „Administrare”) elementul extensibil „Inventar” (aceeași componentă `CollapsibleSection` ca „Administrare”, cu pictogramă proprie), cu două intrări: **„Generare situație inventar”** → `/inventar` și **„Preluare inventar”** → `/inventar/preluare`. Elementul „Inventar” este evidențiat și rămâne extins când pagina activă este oricare dintre cele două rute.
- [ ] „Preluare inventar” este doar o intrare de meniu: ruta `/inventar/preluare` afișează o pagină scurtă „Preluare inventar” cu mesajul că funcționalitatea va fi implementată ulterior (fără formulare sau operații); același drept de acces ca la „Generare situație inventar”. Implementarea reală este în afara acestui task.
- [ ] Adaugă „Inventar” în dashboard (pagina principală), ca element de același tip cu cele existente, cu descriere scurtă (de exemplu „Generează situația de inventar pentru numărarea stocului.”), cu link către `/inventar`.
- [ ] Pagina afișează stări clare: încărcare, catalog gol („Nu există categorii sau produse pentru inventar”) și eroare de încărcare cu „Reîncearcă”; nu afișează date simulate.

### Subtask 10.2 — Afișarea categoriilor și subcategoriilor

- [ ] Afișează categoriile și subcategoriile în același stil ca în „Categorii și subcategorii” (carduri extensibile cu `CollapsibleSection`, denumirea categoriei, numărul de subcategorii și de produse asociate, lista subcategoriilor cu numărul de produse), **fără** butoanele „Adaugă categorie”, „Adaugă subcategorie”, „Editează categoria” și „Editează / Mută”.
- [ ] Categoriile pornesc restrânse, ca în pagina de administrare; starea extinsă/restrânsă a unui card nu se pierde la reîmprospătări ale datelor și nu modifică selecția.
- [ ] Ordinea categoriilor și a subcategoriilor este aceeași ca în pagina de administrare (alfabetică, după regulile de comparație existente). Categoriile sau subcategoriile fără produse sunt afișate și selectabile, dar sunt omise din PDF (subtaskul 10.5).

### Subtask 10.3 — Selectarea

- [ ] Fiecare categorie și fiecare subcategorie are o casetă de selectare (`<input type="checkbox">` cu etichetă accesibilă, de exemplu „Selectează categoria Scule electrice”); selectarea unei subcategorii marchează pentru inventar toate produsele ei, iar selectarea unei categorii marchează toate produsele categoriei.
- [ ] Selectarea sau deselectarea unei categorii se propagă asupra tuturor subcategoriilor ei. Când doar o parte din subcategoriile unei categorii sunt selectate, caseta categoriei este în stare **nedeterminată** (`indeterminate`, expusă corect accesibilității); când toate sunt selectate devine bifată, iar când niciuna nu este selectată devine debifată. Apăsarea pe o casetă nedeterminată selectează toate subcategoriile.
- [ ] Deasupra listei de categorii există caseta „Selectează toate categoriile”, cu aceeași logică (bifată, nedeterminată, debifată) calculată din toate categoriile; apăsarea ei selectează sau deselectează toate categoriile și subcategoriile.
- [ ] Sub caseta „Selectează toate categoriile” există caseta „Elimină din situația de inventar produsele cu stoc 0”. Când este bifată, produsele cu stoc **exact 0** nu apar în PDF; produsele cu stoc negativ **rămân** (decizie stabilită). Starea implicită este debifată.
- [ ] Caseta din antetul cardului unei categorii și apăsarea antetului sunt acțiuni independente: bifarea nu extinde/restrânge cardul, iar extinderea nu schimbă selecția (regula din Subtask 0.1 pentru acțiunile din antet).
- [ ] Toate casetele se pot folosi cu mouse, touch și tastatură (Tab, Spațiu); starea selecției este afișată și ca text pentru cititoare de ecran (de exemplu „3 din 5 subcategorii selectate”). Selecția și opțiunea „stoc 0” se păstrează pe durata sesiunii paginii, nu în baza de date.
- [ ] Logica selecției (categorie ↔ subcategorii ↔ „toate”) este o clasă independentă de interfață, acoperită de teste (Subtask 10.8).

### Subtask 10.4 — Butonul „Generează situația de inventar”

- [ ] În partea de sus a paginii, în locul butonului „Adaugă categorie” din pagina de administrare, există butonul „Generează situația de inventar”.
- [ ] Butonul este dezactivat, cu explicație („Selectează cel puțin o categorie sau subcategorie”), cât timp nu este selectat nimic; pe durata generării este dezactivat și afișează „Se generează…” pentru a preveni dubla trimitere.
- [ ] Apăsarea generează fișierul PDF și îl oferă utilizatorului pentru descărcare (nume propus `Inventar_aaaa-ll-zz_oomm.pdf`, ora locală). Fișierul **nu** se păstrează pe server.
- [ ] Selecția trimisă la server este validată pe server: numai identificatori valizi de categorii/subcategorii existente, fără date de produse primite de la client; o selecție care nu mai corespunde catalogului (structură modificată între timp) este semnalată clar, iar PDF-ul nu se generează parțial. Cererea este autorizată și protejată împotriva CSRF, ca celelalte operații.
- [ ] Dacă după aplicarea opțiunii „stoc 0” nu rămâne niciun produs, utilizatorul primește un mesaj („Nu există produse de inventariat pentru selecția făcută”), nu se generează un PDF gol și nu se scrie nimic în jurnal.

### Subtask 10.5 — Conținutul și aspectul fișierului PDF

- [ ] Prima pagină are titlul „Inventar” și, sub el, „Generat la: zz/ll/aaaa oo:mm”, calculat din momentul generării (UTC) convertit la ora locală (aceeași convenție de timp local ca în restul aplicației, adică ora serverului).
- [ ] Pentru fiecare categorie selectată (integral sau parțial): o etichetă cu numele categoriei; sub ea, pentru fiecare subcategorie selectată a categoriei: o etichetă cu numele subcategoriei și un tabel cu antetul **Cod produs | Valoare stoc | Valoare reală**. Secțiunile și tabelele sunt generate numai din opțiunile selectate: categoriile fără nicio subcategorie selectată nu apar, subcategoriile fără produse rămase (inclusiv după eliminarea stocului 0) sunt omise împreună cu tabelul lor, iar o categorie fără nicio subcategorie rămasă este omisă.
- [ ] „Cod produs” conține codul produsului (fără identificatorul tehnic `#<id>`); „Valoare stoc” conține cantitatea din stocul calculat din mișcări la momentul generării (număr întreg, valorile negative afișate ca atare); „Valoare reală” este **goală**, cu spațiu suficient pentru scrierea de mână.
- [ ] Un produs cu **stoc negativ** are textul rândului scris cu **roșu** (codul și valoarea stocului); celelalte rânduri sunt negre. Culoarea nu este singurul indicator: valoarea negativă se recunoaște și după semnul minus.
- [ ] Fonturi: numele categoriilor și ale subcategoriilor **bold, mărime 14**; tot restul textului (titlul, „Generat la”, antetele și celulele tabelelor, subsolul) **normal, mărime 12**.
- [ ] Ordinea: categoriile și subcategoriile ca în pagină (alfabetic); produsele fiecărui tabel sunt ordonate după codul produsului (comparație insensibilă la majuscule), cu ordine stabilă la coduri egale.
- [ ] Aspect: format A4, orientare portret; antetul fiecărui tabel se repetă pe paginile următoare când tabelul continuă; o etichetă de categorie sau de subcategorie nu rămâne singură la finalul unei pagini (fără tabel sub ea); coduri lungi se împart pe mai multe rânduri fără să depășească marginile; număr de pagină („Pagina x din y”) în subsol; coloanele „Valoare stoc” și „Valoare reală” au lățime suficientă pentru cel puțin 6 cifre.
- [ ] Datele sunt citite într-un singur instantaneu coerent al catalogului la momentul generării, astfel încât stocul dintr-o secțiune să nu provină din momente diferite; generarea este complet asincronă și nu blochează alte sesiuni.

### Subtask 10.6 — Bibliotecă PDF și font

- [ ] Alege o bibliotecă de generare PDF fără Docker, fără servicii externe și cu licență compatibilă cu folosirea internă (propunere de evaluat: PDFsharp/MigraDoc, licență MIT; QuestPDF are condiții de licență de verificat înainte de adoptare). Decizia și motivul se notează în `docs/PROJECT_STATE.md`.
- [ ] Fontul este **gratuit**, cu licență care permite redistribuirea și încorporarea în PDF (de exemplu o familie sub SIL Open Font License, ca Noto Sans sau Open Sans, în variantele normal și bold), conține diacriticele românești (ă, â, î, ș, ț — atenție la variantele cu virgulă, nu cu sedilă) și este inclus în aplicație; nu depinde de fonturile instalate pe server și nu are nevoie de internet la generare. Licența fontului se păstrează în proiect.
- [ ] Generatorul este un serviciu separat (de exemplu `IInventoryReportBuilder` → model `InventoryReport` → `IInventoryPdfWriter`), astfel încât construirea datelor (selecție, filtrare, ordonare, marcarea stocului negativ) să poată fi testată fără PDF.

### Subtask 10.7 — Jurnalizarea generării

- [ ] Fiecare generare reușită scrie un eveniment în jurnalul de activitate cu **utilizatorul** care a generat situația (și rolul lui), tipul de obiect „Inventar”, acțiunea nouă „Generare” (adăugată și în filtrele de tip și de operație ale paginii Jurnal) și momentul UTC.
- [ ] Detaliile evenimentului conțin doar rezumatul cererii: numărul de categorii și de subcategorii selectate, dacă a fost bifată eliminarea stocului 0, numărul de produse din situație (din care cu stoc negativ) și numele fișierului; **nu** conțin lista produselor sau valorile stocului. Evenimentul nu are identificator de obiect și nu primește link către o pagină.
- [ ] Nu se jurnalizează cererile respinse, cele fără produse sau cele eșuate; evenimentul se scrie după ce PDF-ul a fost construit cu succes, astfel încât un eșec de jurnalizare să nu producă un fișier nejurnalizat (comportamentul la eroarea de jurnalizare se stabilește la implementare și se documentează).
- [ ] Funcționează în modul demonstrativ (SQLite) și în modul MariaDB, prin infrastructura comună de audit; nu se adaugă fișiere SQL de upgrade separate.

### Subtask 10.8 — Verificări

- [ ] Teste automate pentru logica selecției: selectare/deselectare categorie ↔ subcategorii, stare nedeterminată, „toate categoriile”, apăsare pe caseta nedeterminată, categorii fără subcategorii.
- [ ] Teste automate pentru construirea situației: doar selecția făcută, eliminarea stocului 0 (stocul negativ rămâne și este marcat pentru roșu), omiterea secțiunilor goale, ordinea categoriilor/subcategoriilor/produselor, stoc calculat din mișcări (inclusiv un produs cu mișcări), respingerea identificatorilor inexistenți, selecție goală, formatul „zz/ll/aaaa oo:mm” al momentului generării (conversie din UTC la ora locală, inclusiv schimbarea orei de vară).
- [ ] Test de generare PDF: fișierul începe cu `%PDF`, are cel puțin o pagină, conține textele „Inventar”, „Generat la:”, numele categoriei și subcategoriei, antetul „Cod produs / Valoare stoc / Valoare reală” și un cod cu diacritice (extragere de text din PDF în test); numele de categorie/subcategorie folosesc fontul bold de 14, restul fontul normal de 12 (verificat din structura documentului generat); un catalog mare (de exemplu 300 de produse) produce mai multe pagini cu antet repetat; un rând cu stoc negativ este scris cu roșu.
- [ ] Test pentru jurnalizare: o generare reușită scrie exact un eveniment cu utilizatorul, rezumatul cererii și numele fișierului, fără date de produse; o cerere respinsă sau fără produse nu scrie nimic.
- [ ] Verificare în browser (mod demonstrativ): meniul „Inventar” extins cu cele două intrări și evidențierea corectă; selectarea unei categorii, a unei subcategorii, a tuturor, starea nedeterminată, opțiunea „stoc 0”, dezactivarea butonului fără selecție; descărcarea fișierului și deschiderea lui într-un cititor PDF (conținutul comparat cu pagina și cu stocul din „Produse și stocuri”, inclusiv un produs cu stoc negativ în roșu); evenimentul apare în Jurnal cu utilizatorul; „Preluare inventar” afișează pagina de „va fi implementată ulterior”; la 375 și 768 px fără depășire orizontală.
- [ ] Actualizează `README.md`, `VALIDARE.md`, `docs/PROJECT_STATE.md` și, pentru ce nu se poate verifica (de exemplu deschiderea în alte cititoare PDF sau tipărirea), `docs/TESTE_RAMASE.md`.

### Criterii de acceptare

- În meniul principal există „Inventar” cu intrările „Generare situație inventar” (→ `/inventar`) și „Preluare inventar” (pagină de rezervă, funcționalitate ulterioară); „Inventar” apare și în dashboard.
- Pagina „Inventar” arată categoriile și subcategoriile ca în „Categorii și subcategorii”, fără butoane de adăugare sau editare.
- Fiecare categorie și subcategorie are o casetă de selectare; categoria se propagă la subcategorii, iar starea parțială este nedeterminată; „Selectează toate categoriile” selectează sau deselectează tot.
- „Elimină din situația de inventar produsele cu stoc 0” exclude din PDF produsele cu stoc zero; produsele cu stoc negativ rămân și sunt scrise cu roșu.
- „Generează situația de inventar” este dezactivat fără selecție și generează un PDF pentru selecția făcută.
- PDF-ul are pe prima pagină „Inventar” și „Generat la: zz/ll/aaaa oo:mm” (ora locală a momentului generării), apoi, pentru fiecare categorie selectată, eticheta categoriei, eticheta fiecărei subcategorii selectate și un tabel „Cod produs | Valoare stoc | Valoare reală”, numai cu produsele rezultate din opțiunile alese; „Valoare reală” este goală.
- Numele categoriilor și subcategoriilor sunt bold de 14; restul textului este normal de 12; fontul este gratuit și include diacriticele românești, care se văd corect.
- Tabelele continuă pe pagini cu antet repetat; nu rămân etichete singure la sfârșit de pagină.
- Stocul din PDF coincide cu stocul afișat în „Produse și stocuri” la momentul generării.
- Fiecare generare reușită este jurnalizată cu utilizatorul care a făcut-o; cererile respinse nu sunt jurnalizate.
- Funcționalitatea este asincronă, fără Docker, fără fișiere SQL de upgrade separate și nu modifică date de inventar (numai citire, în afara evenimentului de jurnal).

### Detalii de confirmat la implementare

- „Textul din tabel scris cu roșu” pentru stocul negativ: se aplică **întregului rând** (cod și valoare), așa cum este descris mai sus, sau numai valorii stocului?
- Titlul „Inventar” și textul „Generat la” urmează regula generală (normal, 12), fiindcă nu s-a specificat altfel; se poate mări la cerere.
- „Generat la”: `zz/ll/aaaa oo:mm` (fără secunde), cu separatorul `/`.
- Alegerea concretă a bibliotecii PDF (MIT vs. licența QuestPDF) și a familiei de font liberă.
- „Preluare inventar”: pagină de rezervă cu mesaj (propunerea de aici) sau intrare dezactivată în meniu, fără rută.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

# Taskuri finalizate

## Finalizat — Task 9: Blocarea temporară a editării unui produs

Implementat la 25 septembrie 2026 de Claude. `Services/ProductLocks.cs` (`IProductLockRepository`, `SqliteProductLockRepository`, `MariaProductLockRepository`, `ChangeNotifyingProductLockRepository`, `ProductLockRules`), `Components/Pages/Home.razor` (editorul, heartbeat, insigne), `Components/Pages/ProductMovements.razor` (pagina produsului în consultare), `Components/Shared/ForceUnlockDialog.razor`, `wwwroot/leave-guard.js` (`blazorStocPing`), tabelul `product_locks` (SQLite, schema 8) și `product_lock` (MariaDB, creat la prima folosire).

- [x] Lock de tip lease creat la intrarea în modul de editare (butonul „Editează”, inclusiv `?edit=`), nu la consultarea produsului.
- [x] Lock-ul este identificat prin produs, utilizator, sesiune (circuitul), momentul obținerii, ultima reînnoire și momentul expirării.
- [x] Un singur editor activ per produs, prin operații atomice ale bazei de date: reînnoire de către aceeași sesiune, preluarea unui lock expirat și inserare cu cheie primară (`INSERT OR IGNORE`/`INSERT IGNORE`); 24 de cereri simultane au dat exact un câștigător.
- [x] Heartbeat la 30 s cât timp formularul este deschis (`RenewAsync`), doar dacă browserul răspunde (`blazorStocPing`): un tab închis nu mai reînnoiește, deși serverul păstrează circuitul câteva minute. Un lock eliberat de administrator sau preluat de altă sesiune nu este reluat în tăcere; editorul afișează „Blocarea editării a fost pierdută…”, iar salvarea rămâne protejată de verificarea versiunii.
- [x] Eliberare la salvare, anulare, închiderea formularului, navigare sau eliminarea paginii (`ReleaseAsync`, numai de sesiunea care îl deține).
- [x] Expirare automată după 90 s de la ultima reînnoire (1–2 minute) dacă browserul, conexiunea sau aplicația se închid; verificat prin închiderea unui tab în timpul editării.
- [x] Al doilea utilizator vede produsul în consultare (pagina produsului): „Produsul este editat de <utilizator> din <ora>. Îl poți consulta, dar nu îl poți edita până când este eliberat.”, butonul „Editează” dezactivat; în catalog, produsul are insigna „🔒 În editare de <utilizator>”. Ștergerea unui produs editat de altcineva este refuzată cu explicație.
- [x] Notificare automată a celui care așteaptă: eliberarea sau preluarea este anunțată imediat prin feed-ul din Task 8 (evenimentul `BlocareProdus`, vizibil și clienților SignalR), iar expirarea silențioasă este detectată la cel mult 10 s (pagina produsului) sau 15 s (catalog). La eliberare pagina afișează „Produsul a fost eliberat și poate fi editat acum.”
- [x] Deblocare forțată de administrator (buton „Deblochează (administrator)” în pagina produsului, dialog `ForceUnlockDialog`), cu motiv obligatoriu și jurnalizare: acțiunea „Deblocare” (nouă în jurnal și în filtrul de operații), țintă = codul produsului, detalii = editorul anterior și ora, motiv = motivul dat, actor = administratorul. Nu este permisă altor roluri; fără lock nu se scrie nimic în jurnal.
- [x] Timpul UTC al expirărilor este cel al bazei de date (`strftime('now')` în SQLite, `UTC_TIMESTAMP(6)` în MariaDB); timpul rămas vine din baza de date, nu din ceasul aplicației.
- [x] Nicio tranzacție SQL sau blocare de rând nu rămâne deschisă cât timp formularul este activ: fiecare operație este o singură instrucțiune atomică pe o conexiune scurtă.
- [x] Verificarea versiunii produsului rămâne protecția finală, inclusiv după expirarea sau preluarea lock-ului.
- Neverificat: MariaDB pe un server real; două calculatoare diferite (testat cu două tab-uri, cu același cont); un al doilea utilizator cu cont distinct în browser.
- Corectură legată: scripturile proprii sunt acum încărcate prin `@Assets[...]` (nume cu amprentă), deoarece browserul păstra o versiune veche a `leave-guard.js`.

## Finalizat — Task 8: Sincronizare între utilizatori

Implementat la 25 septembrie 2026 de Claude. `Services/ChangeEvents.cs` (trigger-e, surse SQLite/MariaDB, `ChangeEventRelay`), `Services/ChangeFeed.cs` (`ILocalChangeLedger`), `Services/LiveRefresh.cs`, `Services/ChangesHub.cs`, `Components/Shared/LiveChangeNotice.razor`, `Program.cs`, paginile `Home`, `ProductMovements`, `Users`, `UserDetail`; paginile beneficiarului, proiectului și observației (Task 2) folosesc același feed.

- [x] Mecanism de înregistrare în baza de date: tabelul `change_events` (identificator, tip, operație, identificatori de entitate/proiect/observație/beneficiar, moment UTC) și trigger-e `AFTER INSERT/UPDATE/DELETE` pentru produse, utilizatori, proiecte, observații și fișierele lor. Mișcările de stoc sunt raportate drept modificare a produsului lor, deci și pagina produsului se reîmprospătează. SQLite: create la inițializarea schemei locale (versiunea 7), după migrări și seed, fără evenimente de pornire. MariaDB (`io`, `produs`, `web_user`, `project*`): create de aplicație la prima folosire și reverificate la 5 minute (tabelele unor module sunt create târziu), cu `CREATE TRIGGER` numai dacă lipsesc.
- [x] Trigger-ele scriu numai identificatori într-un tabel dedicat; detectează și modificările aplicațiilor externe. Nu se stochează denumiri, texte, nume de fișiere sau parole (verificat prin test).
- [x] Serviciu asincron `ChangeEventRelay`: citește la 1 s evenimentele noi, pornește de la ultimul eveniment existent (fără reluarea istoricului), reține fiecare eveniment 1,5 s (perioadă de grație), îl publică o singură dată în ordine (cursor care avansează numai înainte) și șterge evenimentele procesate mai vechi de 24 h. Un eveniment identic deja publicat direct de sesiunea care a făcut modificarea (`ILocalChangeLedger`, 30 s) nu este anunțat a doua oară; erorile de citire sunt jurnalizate și reluate.
- [x] Publicare prin SignalR: hub autorizat `/hubs/changes` și `SignalRChangeBroadcaster`, care trimite fiecare eveniment (doar identificatori) mesajului `changed`. Paginile aplicației, care rulează în același proces, se abonează direct la feed.
- [x] Reîncărcare numai a datelor afectate: `LiveRefresh` reîmprospătează catalogul (`Home`), pagina produsului (`ProductMovements`), lista și pagina utilizatorului la evenimentele care le privesc, după 300 ms (rafalele se unesc); evenimentele propriei sesiuni sunt ignorate.
- [x] Sincronizarea periodică rămâne rezervă: 60 s pentru catalog, produs și utilizator (15 s existent pentru lista de utilizatori și beneficiari).
- [x] Verificarea versiunii înregistrării rămâne neschimbată, ca protecție finală la salvare.
- [x] Un formular sau dialog deschis nu este întrerupt: pagina afișează „Datele au fost modificate de altă sesiune sau aplicație… [Reîncarcă datele]”, iar reîncărcarea este la alegerea utilizatorului (sau la sincronizarea periodică, după închiderea formularului).
- [x] Testat cu două sesiuni: catalogul deschis într-un al doilea tab a arătat stocul 13 și apoi 12 după adăugarea și ștergerea unei mișcări în primul tab; o modificare SQL directă (aplicație externă) a actualizat pagina produsului; un dialog de editare deschis nu s-a schimbat, iar notificarea și „Reîncarcă datele” au funcționat; un client SignalR real conectat la `/hubs/changes` a primit evenimentul extern.
- Neverificat: MariaDB pe un server real (trigger-ele MariaDB sunt generate și testate ca text, nu executate); două calculatoare diferite; contul MariaDB al aplicației trebuie să aibă privilegiile CREATE și TRIGGER, altfel se jurnalizează un avertisment și rămâne doar sincronizarea periodică.

## Finalizat — Task 7: Confirmarea deconectării

Implementat la 25 septembrie 2026 de Claude. `Components/Layout/MainLayout.razor` (buton și dialog), `wwwroot/logout-dialog.js`, `Pages/Account/Logout.cshtml.cs`, stiluri `.logout-dialog` în `wwwroot/app.css`. Jurnalizarea sesiunilor era finalizată anterior (vezi „Task 7 (parțial)” mai jos).

- [x] Acțiunea „Deconectare” din bara de sus deschide un popup de confirmare (element nativ `<dialog>` modal, cu `aria-labelledby`/`aria-describedby`, focus pe „Anulează deconectarea”, Escape = anulare) în locul paginii de deconectare; `GET /Account/Logout` redirecționează acum la `/`.
- [x] Popup-ul are butonul roșu „Deconectare” și butonul verde „Anulează deconectarea”.
- [x] Deconectarea se execută numai după confirmare: butonul roșu trimite formularul `POST /Account/Logout` cu token antiforgery (`<AntiforgeryToken />`), iar `LogoutModel` încheie sesiunea, înregistrează deconectarea în jurnal și redirecționează la autentificare.
- [x] La anulare (buton sau Escape) popup-ul se închide și nu se modifică nimic altceva: pagina, formularele deschise și textul introdus rămân (verificat cu un editor deschis și text tastat).
- [x] Conectările reușite și deconectările confirmate rămân vizibile în jurnalul de activitate (finalizat anterior).
- Confirmarea efectivă a deconectării (butonul roșu) a fost verificată de utilizator: duce la pagina de autentificare.

## Finalizat — Revenirea în pagina de origine după editarea sau ștergerea produsului

Cerere directă a utilizatorului, 25 septembrie 2026, implementată de Claude. `ReturnNavigation` (`Services/ReturnNavigation.cs`), `Components/Pages/ProductMovements.razor`, `Components/Pages/Home.razor`.

- [x] Acțiunile „Editează” și „Șterge produs” din pagina produsului (`/produse/{id}/miscari`) trimit adresa paginii de origine în `/produse?edit=<id>&inapoi=<adresă>` / `?sterge=<id>&inapoi=<adresă>`; adresa este acceptată numai dacă este o cale locală (fără gazdă, `//` sau `\`), deci nu poate servi ca redirecționare externă.
- [x] Părăsirea editării (Închide, Anulează) și anularea ștergerii readuc utilizatorul în pagina produsului; salvarea confirmată a editării revine tot acolo.
- [x] După o ștergere efectuată, produsul nu mai există, deci utilizatorul ajunge în catalog (comportamentul existent). Linkurile fără `inapoi` (de exemplu din jurnal sau linkuri vechi) păstrează comportamentul anterior: revenirea în catalog.

## Finalizat — Task 6: Confirmare la părăsirea formularului de adăugare

Implementat la 25 septembrie 2026 de Claude. `Components/Pages/Home.razor`, `wwwroot/leave-guard.js`, `ProductMenuSelection` (`Services/LeaveConfirmation.cs`), `Components/Shared/SaveConfirmationDialog.razor` (etichete și mesaj configurabile).

- [x] Cât timp formularul „Adaugă produs” este deschis, alegerea altei categorii, subcategorii sau a „Toate produsele” din meniul produselor este interceptată (clic, Enter sau Spațiu): `leave-guard.js` oprește navigarea înainte să se producă, inclusiv navigarea prin reîncărcare a antetelor de categorie, deci meniul rămâne la poziția anterioară.
- [x] Popup accesibil (`role="dialog"`, `aria-modal`, focus pe dialog, Escape = continuare): „Părăsești adăugarea produsului?” cu mesajul că formularul va fi închis și datele nesalvate, inclusiv imaginea selectată, se pierd; acțiunile explicite „Părăsește adăugarea” (roșu) și „Continuă adăugarea” (verde), reutilizând `SaveConfirmationDialog` din Task 5.
- [x] Confirmarea închide formularul și execută exact navigarea solicitată; anularea lasă formularul și meniul neschimbate, cu toate valorile și imaginea introduse (editorul nu este atins).
- [x] Popup-ul nu apare dacă formularul de adăugare nu este deschis (protecția se activează numai cât timp `editing && editingProduct is null`), nici la editarea unui produs existent, nici la alegerea selecției curente (`ProductMenuSelection.IsSameSelection`).
- Neverificat manual: comportamentul pe ecran tactil; ieșirea din pagină prin alte linkuri (Meniu principal, Administrare) nu este protejată, conform cerinței (numai meniul produselor).

## Finalizat — Task 5: Confirmarea salvărilor

Implementat la 25 septembrie 2026 de Claude. `Components/Shared/SaveConfirmationDialog.razor`, `SaveSummary` (`Services/SaveConfirmation.cs`), `BeneficiaryEditor`, `ProductEditor`, `ProjectEditor`, `ProjectObservationEditor`, `UserEditor`, `ProductGroups` (categorii și subcategorii) și dialogul de editare din `ProductMovements`.

- [x] Fiecare salvare care modifică date existente (beneficiar, produs, proiect, observație, utilizator, categorie, subcategorie, mișcare de stoc) deschide un popup de confirmare înaintea execuției.
- [x] Popup-ul (`role="dialog"`, `aria-modal`, focus pe dialog, Escape = refuz) prezintă un tabel „Câmp / Valoare actuală / Valoare nouă” doar pentru câmpurile care se schimbă, plus motivarea; valorile goale apar ca „(gol)”, textele foarte lungi sunt scurtate, iar parola nu apare niciodată (doar „va fi schimbată”). Pentru mișcări apare și corecția de stoc, pentru produs și imaginea înlocuită.
- [x] Validarea (`Validated`, motivare obligatorie, câmpuri invalide) rulează înaintea popup-ului; salvarea se execută numai după „Confirmă salvarea”.
- [x] La „Anulează” (sau Escape) nu se salvează nimic, iar formularul rămâne deschis cu valorile curente.
- [x] Crearea nu afișează popup: produsul nou, ca și beneficiarul, proiectul, observația, utilizatorul, categoria, subcategoria și mișcarea nouă se salvează direct. Adăugarea unei mișcări este creare, nu editare, deci nu cere confirmare (decizie a implementării; poate fi extinsă).
- [x] Regulă de server cerută de utilizator (formularul de editare a proiectului nu mai are „Beneficiar”): `ProjectRules.CheckBeneficiaryUnchanged` respinge orice `UpdateAsync` care schimbă beneficiarul unui proiect (`BeneficiaryLockedMessage`), în SQLite și MariaDB, înaintea oricărei scrieri, fără eveniment de audit sau de sincronizare. Mutarea proiectului dintre beneficiari (Task 2) nu mai este posibilă.
- Neverificat manual în browser: dialogul pentru produs, utilizator, proiect, observație, categorie și mișcare (același component, acoperit de compilare); MariaDB neverificat pe server real.

## Finalizat — Task 4: Identificarea beneficiarului cu CUI duplicat

Implementat la 25 septembrie 2026 de Claude. `BeneficiaryRules.DuplicateCuiMessage` (`Services/Beneficiaries.cs`), `DemoBeneficiaryRepository`, `SqliteBeneficiaryRepository`, `MariaBeneficiaryRepository`; `BeneficiaryEditor.razor` nemodificat.

- [x] La adăugarea sau editarea unui beneficiar, dacă CUI-ul există deja, mesajul de eroare include numele beneficiarului care folosește acel CUI: „Există deja un beneficiar cu acest CUI: «nume».”, același text în modul demonstrativ, SQLite și MariaDB.
- [x] Mesajul folosește numele așa cum este salvat în baza de date, nu cel introdus în formular.
- [x] Formularul rămâne deschis cu valorile introduse după respingerea salvării (comportament existent al `BeneficiaryEditor`, verificat în browser).
- [x] MariaDB: eroarea 1062 a indexului `UX_beneficiar_cui`, produsă de o salvare concurentă, este tradusă după rollback în același mesaj, cu numele existent.
- [x] Beneficiarul editat este exclus din verificare: salvarea fără schimbarea CUI-ului nu îl raportează ca duplicat.
- Neverificat pe un server MariaDB real; editarea în browser nu a fost exersată manual (acoperită de verificările automate).

## Finalizat — Task 7 (parțial): jurnalizarea sesiunilor

Elementele finalizate ale taskului „Confirmarea deconectării și jurnalizarea sesiunilor”; fluxul popup de confirmare rămâne activ în Task 7.

- [x] Jurnalizează fiecare conectare reușită și fiecare deconectare efectuată de utilizator.
- [x] Înregistrează în jurnal utilizatorul, tipul operației și timestampul UTC.

## Finalizat — Task 3: Navigarea din jurnal către pagina obiectului

Implementat la 25 septembrie 2026 de Claude. `Services/AuditTrail.cs` (`AuditNavigation.TargetUrl`, `RemovalTimes`, `ProductNavigation`, `UserNavigation`), `Services/ListNavigationContext.cs` (`AuditListState`), `Components/Pages/Audit.razor`, `Components/Pages/ProductMovements.razor`, `Components/Pages/BeneficiaryDetail.razor`, `Components/Pages/UserDetail.razor`, `Components/Pages/Users.razor`.

- [x] Fiecare tip de obiect are o rută stabilă de consultare, construită numai din tipul entității și identificatorul din `EntityId`: `/produse/{id}` (pagina produsului cu datele și mișcările de stoc; `/produse/{id}/miscari` rămâne valabilă), `/beneficiari/{id}`, `/utilizatori/{id}` (nouă, numai administratori), `/proiecte/{id}`, `/observatii/{id}` și `/miscari/{id}`.
- [x] Linkul din coloana „Țintă” deschide pagina obiectului, nu formularul de editare; rutele nu mai conțin `?edit=`.
- [x] Pagina beneficiarului și pagina utilizatorului au buton separat „Editează” (beneficiar: utilizatorii autentificați; utilizator: administratorii); pagina produsului îl afișează utilizatorilor autorizați. Deschiderea paginii nu pornește editarea; nu există încă lock de editare (Task 8).
- [x] Autorizarea rămâne cea existentă: `/utilizatori/{id}` cere rolul Administrator (atât rută, cât și verificare în pagină), produsele și beneficiarii cer autentificare.
- [x] Registrul extensibil este dicționarul `AuditNavigation.Routes`, un singur loc pentru toate tipurile; tipurile fără intrare (categorii, subcategorii, fișiere de observație) și evenimentele fără identificator (conectări, deconectări) rămân text.
- [x] Obiectele cu o ștergere înregistrată după eveniment (mutate în arhivă) nu primesc link (`AuditNavigation.RemovalTimes`); evenimentele de ștergere rămân text, iar evenimentele ulterioare ale unui identificator reutilizat își păstrează linkul. Paginile obiectelor inexistente afișează „nu mai există”.
- [x] Filtrele, dimensiunea paginii și pagina jurnalului sunt în adresă (`/jurnal?q=…&tip=…&operatie=…&operator=…&data=…&pe-pagina=…&pagina=…`, doar valorile diferite de implicit, înlocuită pe loc), citite doar la deschiderea paginii; Înapoi din browser le restaurează, împreună cu poziția de derulare (`data-restore-scroll`). Valorile invalide din adresă revin la implicit.
- [x] Verificări automate în `BlazorStoc.Checks` pentru rutele tuturor tipurilor, textul țintei ignorat, identificatori invalizi, obiecte fără pagină, obiecte eliminate și starea jurnalului din adresă.

### Neacoperit intenționat

- Declanșatoarele `?edit=<id>` ale listelor (`/produse`, `/beneficiari`, `/utilizatori`) au rămas pentru butonul explicit „Editează” al produsului și pentru linkuri existente; jurnalul nu le mai folosește.
- Pentru observațiile arhivate odată cu proiectul lor nu s-a verificat dacă fiecare are propriul eveniment de ștergere; dacă nu, linkul din jurnal rămâne, iar pagina afișează „Observația nu mai există”, nu o pagină invalidă.


## Finalizat — Proiecte asociate beneficiarilor

Proiectele reprezintă lucrări realizate pentru beneficiari și devin punctul de legătură dintre beneficiar, observațiile de lucru, fișierele atașate și viitoarele mișcări de ieșire din inventar. Implementarea păstrează separat câmpul general „Observații” al proiectului și lista de observații individuale, fiecare cu propria denumire, descriere și fișiere. Finalizat la 25 septembrie 2026 de Claude.

### Finalizat — Restaurarea contextului de navigare și registrul jurnalului

Implementat la 25 septembrie 2026 de Claude. `Services/ListNavigationContext.cs`, `wwwroot/navigation-context.js`, `Components/Pages/BeneficiaryDetail.razor`, `Components/Pages/ProjectObservationRedirect.razor`, `AuditNavigation.EditUrl`.

- [x] Filtrul și pagina listei de proiecte sunt păstrate în adresa paginii beneficiarului (`?q=…&pagina=…`, înlocuită pe loc, fără intrări noi în istoric) și memorate pe sesiune; linkul „înapoi” din pagina proiectului și redirecționarea după ștergerea proiectului revin la aceeași vedere, iar butonul Înapoi al browserului o restaurează prin adresă.
- [x] Poziția de derulare este salvată în `sessionStorage` la apăsarea unui link de proiect și restaurată o singură dată (cel mult 30 de minute) la revenirea la aceeași adresă; dacă stocarea nu este disponibilă, pagina funcționează fără restaurarea poziției.
- [x] Observațiile sunt în registrul comun al rutelor jurnalului: `AuditNavigation.EditUrl` → `/observatii/{id}` (identificatorul stabil al observației, cel salvat în `EntityId`), o pagină care rezolvă observația și redirecționează către `/proiecte/{projectId}/observatii/{id}`; observațiile șterse afișează „Observația nu mai există”, iar evenimentele de ștergere rămân fără link.

### Finalizat — Pagina „Echipamente” citește mișcările de stoc

Implementat la 25 septembrie 2026 de Claude. `Components/Pages/ProjectEquipment.razor`.

- [x] `/proiecte/{id}/echipamente` citește exclusiv `IStockMovementRepository.GetForProjectAsync` (ieșirile cu `stock_movements.project_id`) și afișează codul produsului (link către pagina produsului), cantitatea, data (`zz-LL-aaaa`), operatorul și referința mișcării („Mișcarea nr. N”, link `/miscari/{id}`).
- [x] Stare goală explicită și stare de eroare cu „Reîncearcă”; nu se afișează date simulate.

### Finalizat — Sincronizare cu Task 8

Implementat la 25 septembrie 2026 de Claude. `Services/ChangeFeed.cs`, `Components/Pages/BeneficiaryDetail.razor`, `ProjectPage.razor`, `ProjectObservationPage.razor`, `Program.cs`.

- [x] Contract de evenimente: `ChangeEvent` și `IChangeFeed`, publicate după commit de decoratorii `ChangeNotifyingProjectRepository` și `ChangeNotifyingProjectFileStore`, identic pentru SQLite și MariaDB. Evenimentele conțin doar identificatori (tip, acțiune, id, proiect, observație, beneficiar, sesiunea de origine, moment UTC), niciodată denumiri, texte sau conținut de fișiere.
- [x] Lista proiectelor din pagina beneficiarului și paginile proiectului și observației se abonează la feed și se reîmprospătează pe loc pentru modificările altor sesiuni (evenimentele propriei sesiuni sunt ignorate). Formularele și dialogurile deschise nu sunt înlocuite: primesc doar o notificare, iar verificarea versiunii rămâne protecția finală.
- [x] Încărcarea unui fișier într-o observație scrie acum evenimentul de audit „Adăugare” (SQLite: în aceeași tranzacție; MariaDB: după inserare). Evenimentul lipsea, deși taskul îl dădea drept finalizat; conținutul fișierului nu intră în jurnal.
- Task 8 conectează feed-ul la surse externe (trigger-e, SignalR); paginile nu mai necesită modificări.

### Finalizat — Verificări ale ultimelor subtaskuri

- [x] 27 de verificări automate noi în `BlazorStoc.Checks` (312 în total): evenimentele de modificare pentru fiecare operație, nicio publicare pentru operații respinse, izolarea abonaților care eșuează, filtrarea evenimentelor pe pagini, auditul încărcării fișierelor, ruta observației în jurnal și memorarea stării listei.
- [x] Verificat în browser (`http://127.0.0.1:5082`, două sesiuni): filtrul în adresă, întoarcerea din proiect cu filtrul și poziția de derulare, pagina „Echipamente” goală și cu o ieșire asociată, linkul observației din jurnal și `/observatii/{id}` inexistent, reîmprospătarea live a listelor și notificarea peste un formular deschis. Datele de test au fost șterse.

### Finalizat — Persistența și repository-urile asincrone

Implementat la 24 septembrie 2026 de Claude. `Services/SqliteProjectRepository.cs`, `Services/SqliteProjectFileStore.cs`, `Services/MariaProjectRepository.cs`, `Services/MariaProjectFileStore.cs`. Modul demonstrativ al aplicației este SQLite (`Program.cs`, `App:DemoMode`), deci structurile SQLite acoperă și modul demonstrativ.

- [x] Creează structurile persistente pentru proiecte, observații și metadatele fișierelor în SQLite și MariaDB, plus structurile echivalente pentru modul demonstrativ.
- [x] Adaugă cheia externă obligatorie dintre proiect și beneficiar și cheia externă dintre observație și proiect.
- [x] Adaugă o constrângere sau un index unic concurent sigur pentru combinația beneficiar și denumire normalizată a proiectului, folosind `ProjectRules.NormalizedName`; traduce încălcarea indexului în `ProjectRules.ConcurrentDuplicateMessage`.
- [x] Aplică `BeneficiaryRules.CheckNoLiveProjects` în ștergerea beneficiarului din modul demonstrativ, SQLite și MariaDB, în aceeași tranzacție cu verificarea mișcărilor de stoc.
- [x] Construiește și editează înregistrările numai prin `ProjectRules.Create`/`Edited`, `CreateObservation`/`EditedObservation` și `ProjectFileRules.Create`, cu timestamp UTC furnizat de server.
- [x] Adaugă indexuri pentru beneficiar, proiect, data creării observației și relația fișier–observație.
- [x] Definește repository-uri complet asincrone pentru listare, consultare, creare, editare și ștergere/arhivare.
- [x] Păstrează operațiile asupra proiectului, observațiilor, fișierelor și auditului în tranzacții coerente, cu operații compensatorii pentru fișiere când este necesar.
- [x] Nu genera un fișier SQL de upgrade separat; actualizează doar schema și inițializarea gestionate în cadrul proiectului.

### Finalizat — Navigarea prin beneficiari și CRUD-ul proiectelor

Implementat la 24 septembrie 2026 de Claude. `Components/Pages/BeneficiaryDetail.razor`, `Components/Pages/ProjectEditor.razor`.

- [x] Nu adăuga o secțiune „Proiecte” în meniul principal și nu crea o listă globală a proiectelor.
- [x] Transformă denumirea beneficiarului din tabelul „Beneficiari” într-un link spre ruta stabilă de consultare `/beneficiari/{id}`.
- [x] Creează pagina de detaliu a beneficiarului și afișează în aceasta numele, CUI-ul și acțiunile permise utilizatorului.
- [x] Afișează lista proiectelor asociate exclusiv în pagina de detaliu a beneficiarului; nicio altă pagină de listare nu agregă proiectele tuturor beneficiarilor.
- [x] Pentru fiecare proiect asociat, afișează denumirea ca link spre pagina proiectului și data ultimei modificări.
- [x] În pagina beneficiarului, permite filtrarea proiectelor după denumire și paginarea listei atunci când numărul lor o impune.
- [x] Adaugă în pagina beneficiarului butonul „Adaugă proiect”; formularul pornește cu beneficiarul curent preselectat și nemodificabil în fluxul de creare.
- [x] Formularul de creare solicită denumirea proiectului și observațiile generale.
- [x] Permite editarea beneficiarului asociat, a denumirii și a observațiilor generale, cu motivare obligatorie și verificarea versiunii; după mutare, proiectul apare numai în pagina noului beneficiar. **Înlocuit ulterior (Task 5):** beneficiarul proiectului nu se mai poate schimba; regula este aplicată și pe server.
- [x] Respinge salvarea dacă beneficiarul selectat nu mai există sau dacă denumirea este deja folosită de alt proiect al aceluiași beneficiar.
- [x] Aplică stilul unitar al linkurilor și butoanelor și păstrează datele formularului după o validare respinsă.

### Finalizat — Pagina proiectului și legătura cu inventarul

Implementat la 24 septembrie 2026 de Claude. `Components/Pages/ProjectPage.razor`, `Components/Pages/ProjectEquipment.razor`. Citirea efectivă a mișcărilor de stoc rămâne în Subtask 2.4, deoarece modulul de intrări/ieșiri nu există încă.

- [x] Creează o rută stabilă de consultare `/proiecte/{id}` care nu deschide automat editarea.
- [x] Afișează denumirea proiectului, beneficiarul cu link spre pagina sa, observațiile generale și datele de creare/actualizare în ora locală.
- [x] Afișează un tabel de navigare în care primul rând este întotdeauna linkul „Echipamente”.
- [x] Linkul „Echipamente” deschide `/proiecte/{id}/echipamente`, pagina materialelor și echipamentelor scoase din inventar pentru proiect.
- [x] Definește de acum contractul prin care o viitoare mișcare de ieșire poate avea un proiect asociat, fără să implementeze anticipat modulul de intrări/ieșiri.
- [x] Până la implementarea mișcărilor de stoc, afișează o stare goală explicită și nu introduce date simulate.
- [x] Nu permite ștergerea/arhivarea unui proiect care are mișcări de stoc asociate; istoricul de inventar trebuie păstrat.

### Finalizat — Observațiile proiectului

Implementat la 24 septembrie 2026 de Claude. `Components/Pages/ProjectObservationEditor.razor`, `Components/Pages/ProjectObservationPage.razor`.

- [x] Adaugă în pagina proiectului butonul „Adaugă observație”.
- [x] Permite adăugarea mai multor observații, fiecare cu denumire obligatorie și câmp text pentru conținut.
- [x] Afișează observațiile în tabelul proiectului după rândul „Echipamente”, ordonate descrescător după timestampul UTC al introducerii și apoi după identificator pentru ordine stabilă.
- [x] Fiecare rând afișează denumirea observației ca link, data și ora locală, autorul și numărul fișierelor asociate.
- [x] Creează ruta stabilă `/proiecte/{projectId}/observatii/{observationId}` și verifică faptul că observația aparține proiectului din rută.
- [x] Pagina observației afișează denumirea, textul, autorul, timestampurile și lista fișierelor asociate.
- [x] Permite editarea denumirii și textului observației cu motivare obligatorie, verificarea versiunii și păstrarea formularului după erori.
- [x] Permite ștergerea/arhivarea unei observații numai prin fluxul comun de confirmare în doi pași.

### Finalizat — Fișierele observațiilor

Implementat la 24 septembrie 2026 de Claude. `ProjectFileRules` din `Services/Projects.cs`, `SqliteProjectFileStore`, `MariaProjectFileStore`, endpoint-ul `/media/project-files/{fileId}`.

- [x] Permite încărcarea unuia sau mai multor fișiere pentru o observație existentă, atât din pagina observației, cât și imediat după crearea acesteia.
- [x] Stochează conținutul fișierelor în structura de fișiere a serverului, nu ca BLOB în baza de date; baza păstrează doar metadatele și calea internă controlată.
- [x] Generează nume interne unice și păstrează separat numele original destinat afișării și descărcării.
- [x] Configurează limite pentru dimensiune, număr de fișiere și tipuri acceptate și validează conținutul fără a avea încredere numai în extensie sau tipul declarat de browser.
- [x] Calculează și păstrează hash-ul și dimensiunea fiecărui fișier pentru verificarea integrității.
- [x] Expune descărcarea numai printr-un endpoint autorizat care verifică accesul la proiect și împiedică traversarea căilor de fișiere.
- [x] Afișează în pagina observației linkuri de descărcare cu numele, tipul și dimensiunea fișierului.
- [x] Permite adăugarea de fișiere noi și eliminarea individuală a celor existente, cu motiv obligatoriu și confirmare.
- [x] La eliminare, mută fișierul și metadatele în arhivă conform contractului existent; nu șterge definitiv singura copie.

### Finalizat — Autorizare, audit și arhivare

Implementat la 24 septembrie 2026 de Claude. Proiectul folosește nivelul de autorizare al beneficiarilor (`EnsureBeneficiaryOperatorAsync`), consecvent cu regula existentă pentru operațiile beneficiarilor.

- [x] Permite administratorilor acces complet și acordă utilizatorilor cu drepturi limitate accesul operațional la proiecte, observații și fișiere, conform regulilor existente pentru produse și beneficiari.
- [x] Verifică autorizarea atât în interfață, cât și în repository-uri și endpointurile de fișiere.
- [x] Jurnalizează adăugarea, editarea și ștergerea/arhivarea proiectelor și observațiilor, precum și adăugarea sau eliminarea fișierelor.
- [x] Pentru editări, păstrează în `Details` numai valorile inițiale și finale, iar motivarea în `Motif`; nu introduce conținutul fișierelor în jurnal.
- [x] Adaugă proiectele în registrul comun al rutelor jurnalului (`AuditNavigation.EditUrl`), folosind identificatorul stabil și pagina de consultare `/proiecte/{id}`. Observațiile rămân în Subtask 2.3, deoarece necesită un identificator compus (proiect + observație) în registrul de rute.
- [x] Extinde registrul și tabelele `archive_*` pentru proiecte, observații și fișiere în aceeași modificare, conform contractului obligatoriu pentru orice entitate nouă care poate fi ștearsă.
- [x] Arhivează proiectul împreună cu observațiile și fișierele sale numai dacă nu există mișcări de stoc asociate și păstrează date suficiente pentru restaurarea ulterioară.

### Finalizat — Verificări

Implementat la 24 septembrie 2026 de Claude, în `BlazorStoc.Checks` (verificări SQLite integrate). MariaDB nu a fost testat pe un server real în acest ciclu, conform practicii curente a proiectului.

- [x] Adaugă verificări automate pentru unicitatea denumirii în cadrul beneficiarului și reutilizarea aceleiași denumiri la beneficiari diferiți.
- [x] Verifică operațiile CRUD, validările, concurența optimistă, autorizarea, auditul și arhivarea în modul demonstrativ (SQLite).
- [x] Verifică încărcarea, descărcarea, integritatea, autorizarea, eliminarea și recuperarea după erori pentru fișiere.
- [x] Verifică ordinea tabelului: „Echipamente” primul, urmat de observații în ordinea stabilită.
- [x] Verifică două sesiuni concurente care încearcă să creeze același proiect pentru același beneficiar.

### Finalizat — Modelul de date și regulile de domeniu

Implementat la 24 septembrie 2026 de Claude în `Services/Projects.cs`. Persistența și aplicarea regulilor în repository-uri sunt finalizate în „Persistența și repository-urile asincrone”, mai jos.

- [x] Definește entitatea `Project` cu identificator stabil, beneficiar obligatoriu, denumire obligatorie, câmp text „Observații”, versiune și timestampuri UTC pentru creare și ultima modificare.
- [x] Definește entitatea `ProjectObservation` cu proiect obligatoriu, denumire obligatorie, conținut text, autor, versiune și timestampuri UTC pentru creare și ultima modificare.
- [x] Definește metadatele fișierelor asociate unei observații: identificator, observație, nume original, nume intern, tip media, dimensiune, hash, autor și timestamp UTC.
- [x] Aplică regula curentă de curățare a denumirilor: elimină spațiile exterioare, reduce spațiile consecutive la unul singur și aplică regulile existente privind diacriticele.
- [x] Validează unicitatea denumirii proiectului numai în cadrul aceluiași beneficiar, fără diferențe între majuscule, minuscule, diacritice sau spațiere echivalentă.
- [x] Permite aceeași denumire de proiect pentru beneficiari diferiți.
- [x] Folosește versiuni pentru concurență optimistă la editarea proiectelor și observațiilor.
- [x] Blochează eliminarea unui beneficiar cât timp acesta are proiecte live asociate (regula `BeneficiaryRules.CheckNoLiveProjects`, aplicată în repository-uri în „Persistența și repository-urile asincrone”).

### Criterii de acceptare

- Un proiect aparține obligatoriu unui beneficiar, iar denumirea sa este unică numai în cadrul acelui beneficiar.
- Proiectele nu au element propriu în meniul principal și nu sunt afișate într-o listă globală.
- Denumirea beneficiarului din tabel este un link, iar lista proiectelor sale este disponibilă numai în pagina de detaliu a beneficiarului.
- Pagina proiectului afișează primul linkul „Echipamente”, apoi observațiile ordonate după data și ora introducerii.
- Un proiect poate conține mai multe observații, iar fiecare observație are pagină proprie și poate fi editată.
- Fișierele sunt stocate pe server, legate de observația corectă și pot fi descărcate sau eliminate numai de utilizatori autorizați.
- Pagina „Echipamente” reflectă exclusiv mișcările de ieșire asociate proiectului.
- Toate modificările sunt validate, versionate, jurnalizate și arhivate conform regulilor comune ale aplicației.

## Finalizat — Intrări și ieșiri pentru un produs existent

Implementat la 24 septembrie 2026 de Claude. Pagina de mișcări de stoc a unui produs: se deschide prin selectarea produsului din tabelul catalogului, permite înregistrarea intrărilor și ieșirilor, afișează istoricul lor și stocul curent. Depinde de taskul finalizat „Stoc exclusiv prin mișcări de intrare și ieșire” (stoc 0 la creare, fără editarea manuală a cantității) și este prerequisit pentru Task 2 / Subtask 2.4 („Echipamente” citește mișcările asociate proiectului) și înlocuiește aplicația veche `IO.cs` (WinForms, ecranul „Intrări/Ieșiri”).

Decizii stabilite cu utilizatorul (24 septembrie 2026):

- Task 1 include adăugarea, listarea, editarea, ștergerea și istoricul modificărilor.
- „Beneficiar” și „Proiect” sunt două căsuțe independente, vizibile numai la ieșire; proiectul poate fi activat numai după beneficiar, este opțional și este limitat la proiectele beneficiarului ales.
- Stocul este calculat din mișcări și poate deveni negativ; o ieșire care depășește stocul nu este blocată și nu cere avertisment.
- Descrierea este obligatorie la intrare și la ieșire; data mișcării este aleasă din calendar și nu poate depăși ziua curentă. **Înlocuit ulterior (25 septembrie 2026, la cererea utilizatorului):** inițial putea fi oricând, inclusiv în viitor; acum calendarul este numai pentru selectare (fără tastare), ultima zi afișată este azi, iar serverul respinge date viitoare.
- Stocul se modifică exclusiv prin mișcările acestui task: taskul finalizat „Stoc exclusiv prin mișcări” a eliminat „Stoc inițial” din crearea produsului (orice produs nou are stoc 0) și editarea manuală a cantității din editorul de produs.

### Subtask 1.1 — Acces și pagina produsului

- [x] Selectarea unui produs din tabelul catalogului (codul produsului și acțiunea „Detalii”) deschide pagina produsului, cu identificatorul numeric în rută (`/produse/{id}/miscari`); identificatorul nu se afișează utilizatorului.
- [x] Pagina afișează în panoul „Produs”: categoria, subcategoria, „Cod produs” (nu „Denumire”), imaginea produsului cu mărire la click și butonul „Editare produs”, care reutilizează editorul și regulile existente.
- [x] Panoul de detalii actual al catalogului este înlocuit de această pagină; acțiunile de editare și ștergere ale produsului rămân disponibile utilizatorilor autorizați, cu fluxurile și motivările existente.
- [x] Afișează stare clară (fără excepție necontrolată) dacă produsul nu există sau a fost șters între timp și oferă revenirea la catalog, către catalogul filtrat pe categoria și subcategoria produsului. Textul de căutare și pagina curentă a catalogului nu se restaurează (rămâne pentru restaurarea contextului de navigare din Task 2 / Subtask 2.3).
- [x] Respectă aspectul din ecranul de referință (panou „Produs”, panou de operare, tabel de mișcări) cu stilurile aplicației și fără depășire orizontală sub 900 px.
- [x] Autorizare: aceleași roluri care pot opera produse (`EnsureProductOperatorAsync`), verificate și pe server.

### Subtask 1.2 — Model de date și reguli de domeniu

- [x] Definește tipul mișcării (intrare/ieșire) și un model de mișcare cu: produs, tip, cantitate (întreg pozitiv), data mișcării (doar data, fără oră, independentă de `created_utc`), descriere, beneficiar și proiect opționale, operator, versiune și timestamp UTC furnizat de server.
- [x] Creează `StockMovementRules` (în stilul `ProjectRules`/`ProductInput`): cantitate întreagă ≥ 1 și limită superioară fixată într-o constantă (propunere: 100.000; se confirmă la implementare), dată validă, descriere obligatorie (după normalizarea existentă `TextNormalization.ForStorage`, inclusiv eliminarea diacriticelor) cu lungime maximă, mesaje clare în română.
- [x] Beneficiarul și proiectul sunt permise numai la ieșire; la intrare serverul le respinge sau le ignoră explicit, nu le salvează. Proiectul trebuie să aparțină beneficiarului ales, altfel cererea este respinsă.
- [x] Regula stocului: stoc = total intrări − total ieșiri, valoare care poate fi negativă; nu se adaugă nicio validare de blocare sau avertisment pentru stoc insuficient.
- [x] Data se acceptă între 1990 și azi (regulă modificată la 25 septembrie 2026: nu poate fi în viitor); nu se folosește ora curentă pentru ordonare, ci data mișcării, apoi identificatorul.
- [x] Verifică regulile prin teste de domeniu (fără interfață), incluzând valori limită și texte doar cu spații.

### Subtask 1.3 — Persistență și stoc atomic

- [x] SQLite (modul demonstrativ și local): extinde `stock_movements` (existent, cu `product_id`, `beneficiary_id`, `project_id`, `quantity`, `created_utc`) cu tip, data mișcării, descriere, operator, versiune și `updated_utc`, printr-o migrare a schemei gestionată în proiect (crește versiunea schemei, fără fișier SQL de upgrade separat); adaugă indexuri pentru produs, dată, beneficiar și proiect.
- [x] MariaDB: mapează pe tabelele existente `io` (`io_tip_actiune`, `io_numar_bucati`, `io_descriere`, `io_data`) și `io_history`; adaugă `id_project` (contractul mișcare–proiect amânat în Task 2) și extinde coloana `io_numar_bucati` (în prezent `tinyint`) dacă limita cantității o cere, prin inițializarea gestionată de aplicație. Verifică formatul actual al `io_data` din backup și păstrează-l; nu se introduc date, parole sau scripturi separate. Implementat, dar netestat pe un server MariaDB real. Formatul `io_data` (`dd-MM-yyyy`) și semnificația `io_tip_actiune` (1 = intrare) au fost preluate din codul aplicației vechi (`SQLWrapper.cs`, `IO.cs`), nu verificate pe datele din backup.
- [x] Repository-uri complet asincrone (`CancellationToken`) pentru SQLite, MariaDB și modul demonstrativ: listare (filtru tip, sortare după dată, paginare), consultare, creare, editare și ștergere/arhivare. Implementate: `SqliteStockMovementRepository` (modul demonstrativ al aplicației și persistența locală) și `MariaStockMovementRepository`. Nu există implementare în memorie: `DemoProductRepository` (folosit doar în teste) nu are mișcări.
- [x] Inserarea mișcării și actualizarea stocului produsului (`quantity`/`produs_cantitate`) se fac în aceeași tranzacție, cu actualizare atomică `quantity = quantity + delta`, astfel încât două cereri simultane să nu piardă o modificare.
- [x] Actualizarea stocului nu incrementează versiunea produsului (altfel o editare concurentă a produsului ar fi respinsă la fiecare mișcare) și nu este suprascrisă de editorul de produs (vezi Task 0).
- [x] Ștergerea unui produs cu mișcări rămâne blocată (regula `ProductRules.CheckDelete` existentă), iar ștergerea beneficiarilor și proiectelor cu mișcări asociate rămâne blocată în SQLite și MariaDB (înlocuiește amânarea documentată în `PROJECT_STATE.md` pentru MariaDB).
- [x] Modul SQLite: pentru produsele existente fără mișcări și cu stoc diferit de zero, migrarea creează o mișcare „Stoc inițial” (intrare pentru stoc pozitiv, ieșire pentru stoc negativ), astfel încât stocul să fie egal cu suma mișcărilor. MariaDB nu primește mișcări generate automat.

### Subtask 1.4 — Formular de adăugare

- [x] Panoul de operare conține: câmp pentru data mișcării (implicit data de azi, modificabilă, cu selector de calendar), comutatorul „Intrare”/„Ieșire” (Intrare selectată implicit, evidențiere verde/roșie conform ecranului de referință), „Descriere” (câmp multi-linie, obligatoriu), „Cantitate” și butonul „Adaugă”.
- [x] La „Ieșire” apar căsuța „Beneficiar” cu lista beneficiarilor (căutare în listă) și, numai după bifarea beneficiarului, căsuța „Proiect” cu proiectele acelui beneficiar; debifarea beneficiarului șterge și proiectul; schimbarea beneficiarului resetează proiectul.
- [x] La „Intrare”, câmpurile Beneficiar și Proiect nu sunt vizibile, iar valorile selectate anterior nu se trimit.
- [x] Dacă „Beneficiar” este bifat fără selecție sau „Proiect” este bifat fără selecție, salvarea este respinsă cu mesaj clar și datele formularului rămân.
- [x] Dacă nici beneficiarul, nici proiectul nu sunt activate, se completează numai cantitatea, data și descrierea.
- [x] Validările sunt afișate în formular (cantitate, dată, descriere obligatorie) și repetate pe server.
- [x] După salvare reușită: mesaj inline de confirmare, golirea descrierii și a cantității, păstrarea datei și a tipului, actualizarea listei și a stocului fără reîncărcarea paginii. Confirmarea uniformă a salvărilor rămâne în Task 5.
- [x] Butonul „Adaugă” este dezactivat pe durata salvării pentru a preveni dubla trimitere; un eșec de persistență afișează eroarea și păstrează datele introduse.

### Subtask 1.5 — Tabelul de mișcări și stocul curent

- [x] Afișează coloanele Data (format `zz-LL-aaaa`), Intrare/Ieșire (intrarea în verde, ieșirea în roșu, cu marcajul `[*]` pentru mișcările modificate), Număr bucăți, Descriere, Beneficiar.
- [x] Coloana „Beneficiar” afișează beneficiarul și proiectul din care face parte ieșirea, atunci când există; rămâne goală pentru intrări și pentru ieșirile fără ele. Beneficiarul și proiectul sunt linkuri către paginile lor (`/beneficiari/{id}`, `/proiecte/{id}`), consecvent cu restul aplicației.
- [x] Filtrul „Intrări/Ieșiri” (toate, doar intrări, doar ieșiri) și sortarea după dată cu indicator (implicit crescător, ca în aplicația veche; ordine stabilă după identificator la date egale).
- [x] Paginare în același stil cu jurnalul (10, 20, 50, „Toate”), aplicată după filtrare, fără încărcarea inutilă a tuturor mișcărilor.
- [x] Afișează „Total produse în stoc: N” calculat din mișcări, cu valoare negativă vizibilă ca atare, și actualizează valoarea după fiecare adăugare, editare sau ștergere.
- [x] Stare goală clară („Nu există intrări/ieșiri pentru acest produs”) și stare de eroare cu buton „Reîncearcă”.
- [x] Bara de jos afișează: „Intrările/ieșirile cu [*] au suferit modificări. Click dreapta pentru a vedea istoricul modificărilor”, doar dacă există astfel de mișcări.

### Subtask 1.6 — Editarea și ștergerea mișcărilor

- [x] Selectarea unui rând permite editarea datei, cantității, descrierii și, la ieșire, a beneficiarului și proiectului; tipul mișcării nu se schimbă (se șterge și se adaugă una nouă).
- [x] Editarea și ștergerea cer „Motivare modificare” obligatorie (componenta `ChangeReasonField` și `ChangeReasonRules` existente) și verifică versiunea mișcării pentru a preveni suprascrierea unei modificări concurente.
- [x] Ștergerea folosește fluxul în doi pași (`DeleteConfirmationDialog`), afișează cantitatea și corecția de stoc rezultată și arhivează mișcarea prin serviciul comun (`ArchiveSchemaRegistry`: entitate nouă „MiscareStoc”, tabel `archive_stock_movements`, versionarea schemei de arhivă, fără ștergere fizică directă).
- [x] Editarea și ștergerea recalculează stocul produsului (corecția = diferența dintre efectul nou și cel vechi), în aceeași tranzacție cu mișcarea, cu istoricul și cu evenimentul de audit.
- [x] O eroare într-o etapă anulează întreaga operație, fără stoc modificat parțial și fără mișcare parțial arhivată. Garantat prin tranzacția unică (rollback la orice excepție); nu există un test cu eroare injectată în fiecare etapă.
- [x] Interfața afișează un rezumat înaintea confirmării (cantitate veche/nouă, corecție de stoc), ca în aplicația veche.

### Subtask 1.7 — Istoricul modificărilor

- [x] Fiecare editare scrie o înregistrare de istoric (SQLite: tabel dedicat; MariaDB: `io_history`) cu operator, data și ora, valorile înainte/după, corecția de stoc și motivarea.
- [x] Mișcările cu istoric primesc marcajul `[*]`; ștergerea păstrează istoricul în arhivă împreună cu mișcarea.
- [x] Click dreapta pe un rând cu `[*]` deschide istoricul modificărilor mișcării; același dialog este accesibil și fără mouse (buton „Istoric” pe rândul selectat, tastatură) și pe ecrane tactile.
- [x] Dialogul de istoric afișează cronologic modificările, cu timestamp în ora locală, și se închide cu Escape sau buton.

### Subtask 1.8 — Audit, jurnal și contract pentru alte taskuri

- [x] Adaugă `AuditEntities.StockMovement` („MiscareStoc”) și jurnalizează adăugarea, editarea și ștergerea cu `Details` (valori înainte/după, tip, cantitate, dată, beneficiar, proiect), `Motif` la editare/ștergere, ținta „cod produs” și identificatorul mișcării în `EntityId`.
- [x] `AuditNavigation.EditUrl` leagă evenimentele mișcărilor de pagina produsului (`/produse/{id}/miscari`); evenimentele mișcărilor arhivate rămân cu țintă text. Implementat prin `/miscari/{id}`, care rezolvă mișcarea și redirecționează către `/produse/{idProdus}/miscari` (evenimentul stochează id-ul mișcării, nu al produsului); după ștergere linkul afișează „Mișcarea nu mai există”.
- [x] Subtask 2.4 (Task 2) devine deblocat: contractul de citire a mișcărilor după proiect (`project_id`) este expus prin repository, cu produs (cod), cantitate, data, operator și referința mișcării.
- [x] Expune un contract de evenimente de modificare pentru mișcări, ce va fi consumat de Task 8, fără a implementa sincronizarea acum. Contractul curent este evenimentul de audit `MiscareStoc` (acțiune, `EntityId`, țintă, detalii); nu a fost creat un canal separat de notificare, iar sincronizarea rămâne pentru Task 8.

### Subtask 1.9 — Verificări

- [x] Teste de domeniu pentru regulile din 1.2 (cantitate, dată, descriere, beneficiar/proiect doar la ieșire, proiect aparținând beneficiarului).
- [x] Teste de persistență pe SQLite (și MariaDB unde mediul permite) pentru stoc atomic: două adăugări simultane păstrează suma corectă; editare și ștergere recalculează stocul; stocul poate deveni negativ fără eroare. Acoperit numai pe SQLite; MariaDB nu a fost testat pe un server real.
- [x] Teste pentru arhivarea mișcării șterse, erori injectate în fiecare etapă, editare/ștergere concurentă a aceleiași mișcări (una singură reușește) și istoricul modificărilor. Acoperite: arhivarea împreună cu istoricul, ștergere repetată/veche respinsă. Neacoperite: erori injectate în fiecare etapă și cereri de editare/ștergere strict simultane (verificarea de versiune sub tranzacție serializabilă le respinge, dar nu au test dedicat).
- [x] Verificat în browser, cu preview-ul actualizat la `http://127.0.0.1:5082/`: deschiderea din catalog (codul produsului) și din `/miscari/{id}`, adăugare intrare, ieșire cu beneficiar, respingerea „Beneficiar” bifat fără selecție, editare cu motivare (rezumatul corecției de stoc), marcajul `[*]`, istoric prin click dreapta, ștergere în doi pași cu corecția de stoc, jurnalul (adăugare/editare/ștergere), linkul din jurnal, `/produse?sterge=<id>`. Nu au fost exersate manual în browser: ieșirea cu proiect (demo-ul nu are proiecte pentru beneficiarul ales), filtrul Intrări/Ieșiri, sortarea și paginarea (acoperite de verificările automate ale repository-ului).
- [x] Verifică că un produs nou (stoc 0, conform Task 0) primește stoc numai prin prima intrare și că o ieșire îl poate duce sub zero.
- [x] Verificat la 375 și 768 px (fără depășire orizontală a paginii; tabelul derulează intern la 375 px) și pe desktop; build Release fără avertismente și suita `tests/BlazorStoc.Checks` (285 de verificări) trecute; `VALIDARE.md` și `docs/PROJECT_STATE.md` actualizate.

### Criterii de acceptare

- Selectarea unui produs din catalog deschide pagina sa de intrări/ieșiri, cu datele produsului, formularul de operare, tabelul mișcărilor și „Total produse în stoc”.
- La intrare, Beneficiar și Proiect nu sunt vizibile; la ieșire pot fi activate independent (proiectul numai după beneficiar), iar fără ele se culeg doar cantitatea, data și descrierea.
- Descrierea este obligatorie la intrare și la ieșire; data mișcării este aleasă din calendar (fără tastare) și nu poate fi în viitor (regulă modificată la 25 septembrie 2026).
- Coloana „Beneficiar” afișează beneficiarul și proiectul mișcării când există.
- O ieșire poate duce stocul sub zero; stocul negativ este afișat corect, fără blocare sau avertisment.
- Stocul se actualizează atomic împreună cu mișcarea; două operații simultane nu pierd nicio modificare.
- Editarea și ștergerea cer motivare, corectează stocul, sunt jurnalizate, arhivate (ștergerea) și marcate cu `[*]` cu istoric accesibil prin click dreapta.
- Stocul unui produs se schimbă numai prin intrări și ieșiri; un produs nou pornește de la stoc 0 (Task 0).
- Mișcările de ieșire asociate unui proiect pot fi citite de Task 2 / Subtask 2.4 prin `project_id`.
- Funcționalitatea rămâne asincronă, fără Docker, fără integrare NAS/QNAP și fără fișiere SQL de upgrade separate.

## Finalizat — Stoc exclusiv prin mișcări de intrare și ieșire

Implementat la 24 septembrie 2026 de Claude. Stocul unui produs nu se mai introduce manual. Orice produs nou este creat cu stoc 0, iar orice modificare a stocului se face numai prin mișcări de intrare și ieșire (Task 1). Task 0 elimină din interfață și din persistență căile care setează direct cantitatea și pregătește terenul pentru pagina de mișcări.

Decizii stabilite cu utilizatorul (24 septembrie 2026):

- Formularul de creare a produsului nu mai are câmpul „Stoc inițial”; fiecare produs este creat cu stoc 0.
- Editorul de produs nu permite editarea manuală a cantității.
- Produsele existente își păstrează stocul curent; nu se generează mișcări și nu se modifică date.
- Până la livrarea Task 1 („Intrări și ieșiri pentru un produs existent”), stocul nu poate fi modificat din aplicație; cele două taskuri se livrează în cicluri consecutive.

### Subtask 0.1 — Crearea produsului fără stoc inițial

- [x] Elimină din formularul de creare câmpul „Stoc inițial (bucăți)” și mesajele lui de validare.
- [x] Creează produsul cu stoc 0 în toate implementările (`DemoProductRepository`, `SqliteProductRepository`, `MariaProductRepository`); serverul nu acceptă și nu folosește o cantitate primită de la client la creare.
- [x] Elimină din `ProductInput` cantitatea ca valoare introdusă de utilizator și regula `Quantity < 0` (validarea „stoc inițial negativ”); adaptează construirea produsului la stoc 0.
- [x] Jurnalul creării produsului nu mai prezintă „Cantitate” ca valoare introdusă de utilizator; identificarea produsului rămâne prin cod, categorie și subcategorie.
- [x] Verifică toate căile de creare (formular, seed-ul modului demonstrativ, teste) și adaptează-le astfel încât niciuna să nu seteze un stoc diferit de zero pentru produse noi.

### Subtask 0.2 — Editarea produsului fără cantitate editabilă

- [x] Elimină câmpul editabil „Cantitate (bucăți)” din editorul de produs; afișează stocul curent numai pentru consultare (text, nu câmp de introducere), cu mențiunea că se modifică prin intrări și ieșiri.
- [x] Actualizarea produsului (SQLite, MariaDB, modul demonstrativ) nu mai scrie cantitatea (`quantity` / `produs_cantitate`) și nu acceptă o cantitate primită de la client; stocul existent rămâne neschimbat la salvare.
- [x] `ProductRules.CheckCurrent` compară acum și `Quantity`; exclude stocul din comparație (și din verificarea de versiune unde este cazul), astfel încât o modificare a stocului, prezentă sau viitoare, să nu invalideze un formular de editare deschis în paralel.
- [x] Jurnalul editării produsului nu mai conține modificări de cantitate (`ProductInput`: `new("Cantitate", …)`).
- [x] Păstrează regula de ștergere existentă: un produs se șterge numai cu stoc zero și fără mișcări asociate; snapshot-ul arhivei produsului păstrează cantitatea ca dată istorică.

### Subtask 0.3 — Verificări

- [x] Teste: crearea unui produs produce stoc 0 indiferent de datele trimise; editarea nu schimbă stocul; două sesiuni (una editează produsul, alta modifică stocul direct în bază) nu produc eroare falsă de concurență din cauza stocului. Verificat pe modul demonstrativ și SQLite; MariaDB nu a fost testat pe un server real.
- [x] Verificat în browser, cu preview-ul actualizat la `http://127.0.0.1:5082/`: formularul de creare fără „Stoc inițial” (cu nota despre stoc 0) și editorul cu stocul afișat numai pentru consultare, fără câmp de cantitate. Salvarea editării și jurnalul creării/editării fără cantitate sunt acoperite de verificările automate, nu de un test manual în browser.
- [x] Verifică produsele existente: stocul afișat în catalog, filtrele de stoc și regula de ștergere rămân neschimbate.
- [x] Rulează build Release și suita `tests/BlazorStoc.Checks`; actualizează `VALIDARE.md` și `docs/PROJECT_STATE.md`.

### Criterii de acceptare

- Formularul de creare nu conține „Stoc inițial”, iar orice produs nou are stoc 0.
- Editorul de produs nu permite modificarea manuală a cantității; stocul este afișat numai pentru consultare.
- Serverul ignoră sau respinge orice cantitate trimisă la creare sau editare; stocul nu poate fi schimbat prin editorul de produs.
- Stocul produselor existente rămâne neschimbat.
- O modificare de stoc nu invalidează o editare de produs deschisă în paralel.
- Jurnalul produsului nu prezintă cantitatea ca valoare introdusă sau modificată de utilizator.
- Funcționalitatea rămâne asincronă, fără Docker, fără integrare NAS/QNAP și fără fișiere SQL de upgrade separate.

## Finalizat — Cod produs

Implementat la 24 septembrie 2026 de Claude. Câmpul existent „Denumire” a devenit „Cod produs”, fără o caracteristică suplimentară în modelul produsului. Proprietatea tehnică `Name` și coloanele `name`/`produs_denumire` au fost păstrate pentru compatibilitatea bazei și a instantaneelor din arhivă; toate etichetele, mesajele și jurnalul folosesc „Cod produs”.

- [x] Redenumește unitar câmpul și eticheta „Denumire” în „Cod produs” în formularele de creare și editare, în catalog, în pagina produsului, în validări și în jurnal.
- [x] Păstrează funcționalitatea actuală a câmpului „Denumire”: obligativitate, limite, salvare, editare, căutare și folosirea sa pentru identificarea produsului.
- [x] Permite introducerea manuală a codului stabilit de producător.
- [x] Nu implementa generarea unui cod intern și nu afișa un buton pentru generarea automată a codului.
- [x] Verifică unicitatea codului la salvare, inclusiv pentru două cereri concurente, și afișează un mesaj clar dacă acel cod există deja.
- [x] Folosește normalizarea numai pentru validarea unicității, fără a diferenția duplicatele prin spații, litere mari/mici sau diacritice; valoarea se salvează conform regulilor generale existente de stocare.
- [x] Elimină unitar din interfață afișarea identificatorului tehnic al produsului în forma `#<număr produs>`, inclusiv din catalog, pagina produsului, formulare, dialoguri și jurnal.
- [x] Păstrează identificatorul numeric intern în baza de date, în rute și în relațiile tehnice, fără să îl expună utilizatorului.
- [x] Afișează „Cod produs” în toate locurile în care este afișată în prezent denumirea produsului.

### Criterii de acceptare

- Un produs nu poate fi salvat fără „Cod produs”.
- Nu mai există în model sau în formulare un câmp „Denumire” separat de „Cod produs”.
- Utilizatorul introduce manual codul produsului, iar interfața nu oferă generarea unui cod intern.
- Codurile introduse sunt unice în întregul catalog conform regulilor existente de comparație.
- Niciun ecran destinat utilizatorului nu afișează identificatorul intern în forma `#<număr produs>`.
- Identificatorul numeric intern continuă să fie folosit pentru persistență, navigare, relații și jurnalizare tehnică.

## Finalizat — Collapse unitar pentru elementele cu structură subordonată

Implementat la 24 septembrie 2026. Toate elementele actuale și viitoare care afișează elemente subordonate folosesc aceeași interacțiune de extindere și restrângere, după modelul funcțional al secțiunii „Administrare” din meniul lateral.

### Subtask 0.1 — Componentă și contract vizual comun

- [x] Creează o componentă reutilizabilă pentru elementele extensibile, bazată pe comportamentul nativ accesibil al browserului sau pe un mecanism Blazor echivalent stabil.
- [x] Definește un contract comun pentru antet, conținut subordonat, starea extinsă/restrânsă și identificatorii necesari accesibilității.
- [x] Păstrează stilul nivelului în care componenta este folosită, fără a transforma toate elementele în carduri sau meniuri identice vizual.
- [x] Folosește un indicator discret și unitar al stării, integrat în antet; nu afișa indicatorul într-un buton sau badge separat.
- [x] Aplică stări unitare pentru normal, hover, activ, focus și disabled, folosind nuanțele existente ale aplicației.
- [x] Întreaga zonă principală a antetului poate fi apăsată pentru extindere sau restrângere.
- [x] Butoanele și linkurile de acțiune aflate în același antet rămân independente și nu schimbă accidental starea collapse.

### Subtask 0.2 — Comportament și accesibilitate

- [x] Permite operarea completă cu mouse, touch și tastatură, inclusiv Enter și Space.
- [x] Expune corect starea prin `aria-expanded`, relația prin `aria-controls` și etichete accesibile pentru conținutul subordonat.
- [x] La restrângere, elimină conținutul ascuns din ordinea de focus.
- [x] Păstrează starea deschisă a ramurii care conține pagina sau selecția activă.
- [x] Pentru listele fără selecție activă, folosește starea inițială definită de ecran; pagina de administrare a categoriilor continuă să pornească cu toate categoriile restrânse.
- [x] Păstrează starea utilizatorului pe durata aceleiași pagini și nu o resetează la actualizări de date care nu schimbă structura relevantă.
- [x] Asigură comportament corect pentru niveluri imbricate, fără ca extinderea unui părinte să modifice automat frații săi.

### Subtask 0.3 — Aplicare în interfața actuală și viitoare

- [x] Migrează secțiunea „Administrare” din meniul lateral la componenta comună fără schimbarea aspectului actual.
- [x] Aplică mecanismul categoriilor cu subcategorii din meniul produselor.
- [x] Aplică mecanismul categoriilor din pagina de administrare a categoriilor, păstrând acțiunile și tabelul subcategoriilor existente.
- [x] Aplică mecanismul oricărei alte liste sau secțiuni existente care afișează copii, detalii ori acțiuni subordonate.
- [x] Folosește obligatoriu aceeași componentă pentru ierarhiile viitoare beneficiar–proiect și proiect–observații din Task 2, dacă acestea sunt prezentate extensibil în aceeași pagină.
- [x] Documentează componenta drept regulă pentru toate entitățile adăugate ulterior care au elemente subordonate.
- [x] Evită implementări locale paralele de collapse și elimină stilurile sau logica redundantă după migrare.

### Subtask 0.4 — Verificări

- [x] Verifică stările inițiale, extinderea și restrângerea pentru fiecare utilizare existentă.
- [x] Verifică faptul că acțiunile din antet nu declanșează collapse și că linkurile subordonate navighează corect.
- [x] Verifică accesibilitatea cu tastatura și atributele ARIA pentru stările deschis și închis.
- [x] Verifică aspectul pe dimensiuni desktop și mobile și previne depășirea sau suprapunerea conținutului.
- [x] Adaugă verificări de regresie pentru ramura activă, actualizarea datelor și structurile imbricate.

### Criterii de acceptare

- Orice element cu elemente subordonate poate fi extins și restrâns prin aceeași interacțiune.
- Stilul rămâne coerent cu zona în care elementul este afișat și oferă feedback vizual clar la hover și focus.
- Starea activă este vizibilă, iar ramura care conține selecția curentă rămâne deschisă.
- Componenta funcționează cu mouse, touch și tastatură și expune corect starea tehnologiilor asistive.
- Adăugarea unei noi ierarhii folosește componenta comună, fără implementarea unei noi logici locale de collapse.

## Finalizat — Crearea categoriilor exclusiv din pagina de administrare

Depinde de pagina de administrare finalizată pentru categorii și subcategorii. Elimină crearea implicită a structurii catalogului din formularul produsului.

- [x] Elimină posibilitatea introducerii libere a unei categorii sau subcategorii în formularul de creare și editare a produsului.
- [x] În formularul produsului, afișează categoria ca listă selectabilă formată exclusiv din categoriile existente.
- [x] După selectarea categoriei, afișează exclusiv subcategoriile existente care îi aparțin.
- [x] Permite mutarea unui produs numai prin selectarea unei alte combinații existente de categorie și subcategorie.
- [x] Nu crea automat o categorie sau subcategorie atunci când se salvează un produs.
- [x] Dacă o categorie sau subcategorie selectată a fost modificată ori eliminată între încărcarea formularului și salvare, respinge salvarea și solicită reîncărcarea listei.
- [x] Adaugă în pagina dedicată categoriilor și subcategoriilor acțiuni explicite pentru crearea unei categorii și pentru crearea unei subcategorii într-o categorie existentă.
- [x] Solicită denumirea și aplică validarea unicității la fiecare creare.
- [x] Jurnalizează separat crearea categoriei și crearea subcategoriei, cu operator, timestamp și identificator stabil.
- [x] Păstrează validarea unicității fără diferențe între majuscule, minuscule sau diacritice.
- [x] Aplică operațiile explicite de creare în SQLite, MariaDB și modul demonstrativ.
- [x] Expune pagina dedicată prin dashboard și prin meniul principal al aplicației.
- [x] Grupează „Categorii”, „Beneficiari” și „Utilizatori” sub secțiunea „Administrare”, păstrând „Utilizatori” vizibil numai administratorilor.

### Criterii de acceptare

- O categorie și o subcategorie nouă pot fi create numai din pagina dedicată administrării lor.
- Formularul produsului nu acceptă text liber pentru categorie sau subcategorie.
- Un produs nou poate fi salvat numai într-o categorie și subcategorie existente.
- Editarea poate muta produsul numai într-o combinație existentă și validă.
- Salvarea produsului nu poate produce implicit evenimente de creare pentru categorie sau subcategorie.
- Categoriile și subcategoriile create din pagina dedicată devin disponibile în formularul produsului după actualizarea datelor.


## Finalizat — Administrarea categoriilor și subcategoriilor

Depinde de Task 2, deoarece administrarea trebuie să opereze pe categorii și subcategorii persistente, independente de produsele asociate.

- [x] Creează o pagină dedicată administrării categoriilor și subcategoriilor.
- [x] Permite editarea numelui unei categorii.
- [x] Permite editarea numelui unei subcategorii.
- [x] Permite mutarea unei subcategorii din categoria curentă într-o altă categorie.
- [x] Actualizează automat meniul produselor și filtrele după redenumire sau mutare.
- [x] Păstrează asocierile produselor la redenumirea unei categorii sau subcategorii.
- [x] Mută împreună cu subcategoria toate produsele asociate atunci când aceasta este mutată în altă categorie.
- [x] Aplică validarea unicității fără diferențe de majuscule, minuscule sau diacritice.
- [x] Jurnalizează redenumirile și mutările cu operator, timestamp și valorile înainte/după.
- [x] Respectă drepturile de acces existente în aplicație.

### Criterii de acceptare

- Redenumirea actualizează meniul și toate produsele asociate fără pierdere de date.
- Mutarea unei subcategorii o afișează numai sub noua categorie și păstrează produsele asociate.
- Nu pot rezulta categorii sau subcategorii duplicate prin redenumire sau mutare.


## Finalizat — Păstrarea categoriilor și subcategoriilor goale

Taskul stabilizează entitățile necesare administrării. SQLite și MariaDB folosesc tabele independente, iar catalogul demonstrativ folosește acum o colecție separată de grupuri.

- [x] Stochează categoriile și subcategoriile ca entități independente de produsele asociate.
- [x] Păstrează categoria și subcategoria după mutarea sau ștergerea ultimului produs asociat.
- [x] Include în meniul produselor toate categoriile și subcategoriile existente, inclusiv cele fără produse.
- [x] Nu șterge automat și nu ascunde o categorie sau subcategorie doar pentru că a rămas goală.
- [x] Păstrează regulile existente de unicitate fără diferențe de majuscule, minuscule sau diacritice.

### Criterii de acceptare

- Mutarea ultimului produs dintr-o subcategorie nu elimină subcategoria din meniu.
- Mutarea ultimului produs dintr-o categorie nu elimină categoria sau subcategoriile sale din meniu.
- O categorie sau subcategorie goală rămâne disponibilă la selectare și la adăugarea unui produs.
- Eliminarea unei categorii sau subcategorii se poate face numai printr-o acțiune explicită de administrare.

## Finalizat — Flux în doi pași pentru confirmarea ștergerii

Depinde de taskul finalizat „Arhivarea obiectelor șterse și pregătirea restaurării” pentru executarea arhivării în locul ștergerii fizice.

- [x] Aplică fluxul tuturor paginilor și dialogurilor care permit ștergerea unui obiect, inclusiv entităților adăugate ulterior.
- [x] Creează componente reutilizabile pentru alegerea motivului și confirmarea prin text, astfel încât dialogurile ulterioare să folosească aceeași infrastructură accesibilă.
- [x] În primul popup, afișează o listă cu două opțiuni radio: „<Denumirea tipului de obiect> nu va mai fi folosit” și „Alt motiv”.
- [x] Adaptează corect denumirea primei opțiuni la obiect, de exemplu „Produsul nu va mai fi folosit” sau „Beneficiarul nu va mai fi folosit”.
- [x] Dacă este selectată prima opțiune, folosește textul complet al opțiunii drept valoare `Motif` în jurnal.
- [x] Dacă este selectată „Alt motiv”, afișează câmpul de motivare și solicită o valoare nevidă înainte de continuare.
- [x] Păstrează în `Motif` exact motivarea introdusă pentru opțiunea „Alt motiv”, după eliminarea spațiilor de la început și sfârșit.
- [x] La continuarea fluxului, deschide un al doilea popup care solicită introducerea cuvântului exact `sterge`.
- [x] Activează confirmarea finală numai când valoarea introdusă, după eliminarea spațiilor exterioare, este exact `sterge`, cu aceeași formă a literelor.
- [x] Execută arhivarea și eliminarea din zona live numai după confirmarea finală.
- [x] Butonul „Anulează” din al doilea popup abandonează complet procesul, închide ambele popup-uri și curăță motivul și textul de confirmare.
- [x] O validare eșuată sau o eroare de salvare păstrează primul popup și valorile introduse pentru o nouă încercare.
- [x] Respectă stilul și regulile de accesibilitate stabilite pentru butoanele și dialogurile aplicației.

### Criterii de acceptare

- Niciun obiect nu poate fi șters fără selectarea unui motiv și confirmarea prin cuvântul `sterge`.
- Prima opțiune generează automat motivul specific tipului de obiect în jurnal.
- „Alt motiv” salvează în jurnal motivarea introdusă de utilizator.
- Confirmarea greșită, incompletă sau cu alte majuscule/minuscule nu permite ștergerea.
- Anularea din al doilea popup abandonează complet ștergerea și nu produce modificări sau evenimente de succes.
- După confirmarea corectă, obiectul dispare din zona live, este păstrat în arhivă și produce exact un eveniment de audit.

## Finalizat — Arhivarea obiectelor șterse și pregătirea restaurării

### Decizie tehnică

Folosește **tabele de arhivă în aceeași bază de date live**, nu o bază de date separată. Pentru compatibilitate între SQLite și MariaDB, tabelele folosesc prefixul `archive_` și reproduc câmpurile relevante ale entităților live.

Această variantă permite mutarea obiectului, a relațiilor sale și a evenimentului de audit în aceeași tranzacție. Păstrează cheile externe, backupul și restaurarea coerente și evită riscul ca o bază separată să fie indisponibilă sau nesincronizată în momentul ștergerii. Separarea logică rămâne clară prin tabele, repository-uri și permisiuni dedicate arhivei.

Fișierele asociate sunt mutate logic într-un director de arhivă separat, aflat pe același volum cu structura live. Fișierul este copiat mai întâi în zona de arhivă și verificat prin dimensiune și hash; tranzacția bazei mută apoi metadatele și obiectul, iar fișierul live este eliminat numai după confirmarea tranzacției. Astfel, o eroare poate lăsa temporar două copii, dar nu poate pierde singura copie disponibilă.

### Subtask 0.1 — Contract comun de arhivare

- [x] Definește un serviciu comun de arhivare folosit de toate entitățile care au sau vor primi opțiune de ștergere.
- [x] Păstrează identificatorul original, toate valorile obiectului, versiunea, relațiile necesare, timestampul UTC, operatorul și motivul ștergerii.
- [x] Adaugă un identificator unic al operației de arhivare și leagă evenimentul din jurnal de acesta.
- [x] Nu arhiva parole în clar; pentru conturile ce trebuie restaurate, păstrează hash-ul parolei numai în câmpul protejat al arhivei, cu acces restricționat, și nu îl include niciodată în jurnal sau interfață.
- [x] Nu permite repository-urilor să execute ștergeri fizice directe în fluxurile normale ale aplicației.

### Subtask 0.2 — Structura tabelelor de arhivă

- [x] Creează tabele `archive_*` pentru produse, beneficiari, utilizatori și relațiile dependente care pot fi șterse.
- [x] Aplică obligatoriu aceeași structură de arhivare tuturor entităților implementate ulterior care permit ștergerea, inclusiv proiecte și alte tipuri noi de obiecte.
- [x] Pentru fiecare entitate nouă, adaugă în aceeași modificare tabelele `archive_*`, relațiile arhivate, metadatele fișierelor asociate și integrarea cu serviciul comun de arhivare.
- [x] Nu considera completă implementarea unei entități noi cu opțiune de ștergere dacă nu include și schema, fluxul și verificările de arhivare corespunzătoare.
- [x] Păstrează separat identificatorul arhivei și identificatorul original din tabela live.
- [x] Adaugă constrângeri și indexuri pentru identificatorul original, tipul obiectului, data ștergerii și operator.
- [x] Versionează schema de arhivă împreună cu schema bazei live.

### Subtask 0.3 — Mutarea tranzacțională și jurnalizarea

- [x] În aceeași tranzacție, copiază obiectul și relațiile sale în tabelele `archive_*`, elimină înregistrările live și scrie evenimentul de audit.
- [x] Anulează întreaga tranzacție dacă arhivarea, eliminarea din zona live sau jurnalizarea eșuează.
- [x] Verifică versiunea obiectului înainte de arhivare pentru a preveni ștergerea unei versiuni modificate concurent.
- [x] Păstrează obiectele arhivate în afara listelor și validărilor curente ale datelor live.
- [x] Păstrează suficiente date pentru implementarea ulterioară a funcției de restaurare, fără a implementa încă interfața de undelete.

### Subtask 0.4 — Arhivarea fișierelor asociate

- [x] Creează o structură de directoare de arhivă paralelă cu structura fișierelor live și separată de fișierele publice.
- [x] Copiază fișierul în arhivă înainte de confirmarea tranzacției și verifică dimensiunea și hash-ul copiei.
- [x] Mută metadatele fișierului în tabela de arhivă împreună cu obiectul părinte.
- [x] Elimină fișierul live numai după confirmarea tranzacției bazei de date.
- [x] Curăță copia pregătită dacă tranzacția este anulată și înregistrează pentru reîncercare eliminarea unui fișier live rămas după confirmare.
- [x] Folosește căi relative controlate și blochează traversarea în afara directoarelor live și de arhivă.

### Subtask 0.5 — Verificare și recuperare

- [x] Testează arhivarea produselor, beneficiarilor, utilizatorilor și fișierelor asociate.
- [x] Testează că o eroare injectată în fiecare etapă nu produce pierdere de date și nu lasă obiectul parțial arhivat.
- [x] Testează două cereri concurente de ștergere pentru același obiect; numai una trebuie să reușească.
- [x] Testează persistența arhivei după repornire și compatibilitatea atât cu SQLite, cât și cu MariaDB.
- [x] Documentează datele și verificările necesare viitoarei operații de restaurare.

### Criterii de acceptare

- O ștergere reușită elimină obiectul din zona live, dar păstrează în arhivă toate datele necesare identificării și restaurării lui.
- Obiectul arhivat și evenimentul de audit sunt confirmate atomic, fără stări intermediare vizibile.
- O eroare nu poate produce pierderea simultană a înregistrării live și a copiei arhivate.
- Fișierele asociate nu mai sunt servite din structura live și rămân disponibile în structura de arhivă.
- Două ștergeri concurente nu pot crea arhive duplicate sau evenimente de succes duplicate.
- Modelul de arhivare se aplică implicit tuturor entităților noi care primesc opțiune de ștergere.


## Finalizat — jurnalizarea completă a acțiunilor

Toate subtaskurile jurnalizării sunt finalizate și verificate integrat.

### Subtaskuri finalizate

### Subtask 1.1 — Verificare integrată

- [x] Verifică toate tipurile de obiecte și toate operațiile: adăugare, editare, ștergere, conectare și deconectare.
- [x] Verifică respingerea editării și ștergerii fără motivare și păstrarea datelor formularului după o eroare.
- [x] Verifică valorile inițiale și finale din `Details`, motivarea din `Motif`, ținta actualizată și absența datelor sensibile.
- [x] Verifică linkurile către obiectele create, afișarea orei locale, filtrele, hoverul operației, paginarea și sincronizarea automată.

### Subtask 1.2 — Filtrare, paginare și sincronizare

- [x] Păstrează filtrele existente și aplică paginarea după filtrarea evenimentelor.
- [x] Adaugă selectorul numărului de evenimente pe pagină cu opțiunile 10, 20, 50 și „Toate”.
- [x] Afișează pagina curentă, numărul total de pagini și comenzile pentru pagina anterioară și următoare.
- [x] Revino la prima pagină când se schimbă un filtru sau numărul de elemente afișate.
- [x] Ascunde navigarea între pagini când este selectată opțiunea „Toate”.
- [x] Păstrează selecția validă și tabelul coerent la sincronizarea automată a jurnalului.

### Subtask 1.3 — Prezentarea jurnalului

Folosește contractul și conținutul deja stabilizate pentru jurnal.

- [x] În coloana „Detalii” afișează `Descriere modificare: <Details>` cu valoarea în italic, apoi pe un rând nou `Motiv: <Motif>` cu valoarea în italic.
- [x] Afișează „Editare” în tabel și în selectorul operațiilor.
- [x] Convertește timestampul UTC la fusul orar local al utilizatorului numai pentru afișarea datei și orei.
- [x] Păstrează valorile din coloana „Operație” interactive pentru filtrare și adaugă la hover un indiciu vizual clar, suplimentar schimbării cursorului, precum subliniere și evidențiere de fundal sau contur.

### Subtask 1.4 — Contractul evenimentului și compatibilitatea datelor

- [x] Extinde modelele de citire și scriere ale evenimentului și structura JSON din `audit-events.jsonl` cu proprietatea `Motif`.
- [x] Definește un contract unic care păstrează identificatorul evenimentului, timestampul UTC, operatorul, rolul, operația, tipul și identificatorul obiectului, `Target`, `Details` și `Motif`.
- [x] Înlocuiește valoarea operației „Modificare” cu „Editare” în întregul flux de audit.
- [x] Păstrează citirea evenimentelor existente care nu au `Motif` și tratează consecvent înregistrările istorice cu operația „Modificare”.
- [x] Păstrează toate timestampurile persistente în UTC.

### Subtask 1.5 — Serviciul comun de jurnalizare și regulile de integritate

Stabilește regulile comune folosite de toate operațiile auditate.

- [x] Centralizează scrierea evenimentelor pentru adăugare, editare, ștergere, conectare și deconectare.
- [x] Scrie exact un eveniment numai după finalizarea cu succes a operației; nu înregistra drept reușite operațiile anulate, respinse de validare sau eșuate.
- [x] Pentru o editare, construiește `Details` numai din câmpurile schimbate, indicând pentru fiecare valoarea inițială și valoarea finală.
- [x] Pentru o ștergere, păstrează în `Details` datele necesare identificării obiectului după eliminarea acestuia.
- [x] Pentru o editare, construiește `Target` din obiectul recitit sau rezultat după salvare, astfel încât să reflecte forma existentă în baza de date.
- [x] Nu include parole, hash-uri sau alte date sensibile în `Details` ori `Motif`.

### Subtask 1.6 — Componenta comună pentru motivarea editărilor și ștergerilor

Reutilizează contractul și serviciul comun de jurnalizare în toate formularele.

- [x] Creează un câmp reutilizabil „Motivare modificare” pentru toate paginile care permit editarea manuală sau ștergerea unui obiect, fără limitare la produse.
- [x] Validează obligatoriu motivarea și nu permite confirmarea dacă lipsește ori conține numai spații.
- [x] Transmite motivarea către serviciul de jurnalizare și salveaz-o în `Motif`.
- [x] Păstrează motivarea introdusă dacă salvarea eșuează și formularul rămâne deschis.

### Subtask 1.7 — Acoperirea tuturor operațiilor

Folosește contractul, serviciul comun și componenta comună pentru motivare.

- [x] Integrează serviciul comun în toate adăugările, editările și ștergerile, indiferent dacă obiectul este produs, beneficiar, utilizator, categorie, subcategorie sau altă entitate a sistemului.
- [x] Jurnalizează conectările reușite și deconectările confirmate.
- [x] Verifică faptul că fiecare operație reușită produce un singur eveniment, fără dubluri generate de interfață și repository.

### Subtask 1.8 — Navigarea din coloana „Țintă”

Folosește identificarea stabilă a obiectelor din evenimentele de audit.

- [x] Pentru o adăugare, afișează „Țintă” ca link către pagina de editare a obiectului creat.
- [x] Pentru o editare, afișează în „Țintă” obiectul în forma existentă în baza de date după salvare.
- [x] Generează linkul pe baza tipului și identificatorului obiectului, fără extragerea identificatorului din textul afișat.
- [x] Pentru un obiect șters sau pentru un tip fără pagină de editare, păstrează „Țintă” ca text fără link invalid.

### Criterii finale de acceptare

- Orice operație reușită din aria definită produce exact un eveniment complet și identificabil, iar o operație nereușită nu produce un eveniment de succes.
- Nicio editare manuală și nicio ștergere nu poate fi executată fără „Motivare modificare”.
- Evenimentele de editare folosesc „Editare”, conțin valorile inițiale și finale în `Details`, motivarea în `Motif` și obiectul actualizat în `Target`.
- Evenimentele de ștergere permit identificarea obiectului eliminat și păstrează motivarea.
- Linkurile din „Țintă”, ora locală, prezentarea detaliilor, filtrarea și paginarea funcționează împreună după sincronizarea automată.

## Finalizat — persistență locală pentru modul demonstrativ și de testare

### Decizie tehnică

Înlocuiește stocarea volatilă din memorie și fișierele JSON separate cu o bază de date locală **SQLite**, păstrată într-un singur fișier pe server. SQLite oferă persistență, tranzacții, constrângeri de unicitate și acces concurent controlat fără Docker sau un serviciu de baze de date separat. Imaginile produselor rămân fișiere pe server; baza de date păstrează numai metadatele și calea relativă.

- [x] Creează baza de date locală SQLite în directorul persistent al aplicației, separat de fișierele publice, și permite configurarea căii acesteia.
- [x] Definește schema și versiunea bazei pentru produse, categorii, subcategorii, beneficiari, utilizatori, metadatele imaginilor, mișcările de stoc și evenimentele jurnalului.
- [x] Înlocuiește repository-urile demonstrative bazate pe memorie cu implementări SQLite care păstrează contractele asincrone și anularea operațiilor.
- [x] Mută evenimentele noi din `audit-events.jsonl` în tabela de audit SQLite, folosind același contract de eveniment stabilit în Task 1.
- [x] Salvează modificarea obiectului și evenimentul aferent de audit în aceeași tranzacție, astfel încât acestea să fie confirmate sau anulate împreună.
- [x] Activează cheile externe, modul WAL și un timp de așteptare controlat pentru accesul simultan din mai multe sesiuni.
- [x] Aplică în baza de date constrângerile de unicitate pe cheile normalizate și verificarea versiunii pentru editările concurente.
- [x] Inserează datele demonstrative inițiale numai la prima creare a unei baze goale; nu reinițializa și nu suprascrie datele la repornirea aplicației.
- [x] Păstrează imaginile produselor în directorul dedicat de pe server și salvează în SQLite calea relativă, tipul media, dimensiunea și identificatorul produsului.
- [x] Implementează o migrare unică a evenimentelor valide din jurnalul JSON existent, fără dubluri; după migrare, arhivează fișierul sursă și nu îl mai folosi pentru scriere.
- [x] Păstrează selectabil modul MariaDB existent pentru instalarea finală, fără ca modul local SQLite să depindă de acesta.
- [x] Adaugă verificări automate pentru creare, editare, ștergere, audit și repornirea aplicației folosind același fișier SQLite.

### Criterii de acceptare

- Produsele, categoriile, subcategoriile, beneficiarii, utilizatorii și jurnalul își păstrează starea după oprirea și repornirea aplicației.
- Un produs editat înainte de repornire apare cu aceeași valoare în catalog și în ținta evenimentului din jurnal după repornire.
- O salvare nu poate produce numai modificarea obiectului sau numai evenimentul de audit; cele două sunt persistate atomic.
- Datele demonstrative sunt create o singură dată și modificările utilizatorului nu sunt suprascrise la pornirile următoare.
- Două sesiuni pot citi și modifica baza locală fără coruperea fișierului, pierderea evenimentelor sau acceptarea duplicatelor.
- Modul local funcționează fără Docker, MariaDB sau un alt serviciu extern.
- Imaginile produselor nu sunt salvate ca BLOB în SQLite și rămân disponibile după repornire.

## Finalizat — validarea unicității fără diferență între litere mari și mici

- [x] Folosește o cheie unitară pentru toate verificările de unicitate.
- [x] Ignoră diferențele dintre majuscule și minuscule la validare.
- [x] Ignoră diacriticele la compararea valorilor unice.
- [x] Păstrează în baza de date majusculele și minusculele introduse de utilizator.

## Finalizat — eliminarea diacriticelor la salvare

- [x] Elimină diacriticele din valorile text înainte de salvare.
- [x] Păstrează forma literelor introdusă de utilizator după eliminarea diacriticelor.
- [x] Aplică regula produselor, categoriilor, subcategoriilor, beneficiarilor și utilizatorilor.
- [x] Nu modifică parolele.

## Finalizat — stil roșu pentru acțiunile de anulare și închidere

- [x] Identifică toate butoanele existente cu acțiunea sau eticheta „Anulează” și „Închide”.
- [x] Aplică acestor butoane stilul vizual roșu folosit pentru acțiunile de renunțare.
- [x] Actualizează formularele, panourile, popup-urile și dialogurile existente în mod consecvent.
- [x] Păstrează neschimbată funcționalitatea actuală a fiecărui buton.
- [x] Păstrează stările hover, focus și disabled clare și accesibile.

### Criterii de acceptare

- Toate butoanele „Anulează” și „Închide” din interfața existentă sunt roșii.
- Niciun alt buton nu își schimbă rolul sau comportamentul.
- Contrastul textului și indicatorul de focus rămân ușor de observat.

## Finalizat — eliminarea individuală a filtrelor din jurnal

- [x] Afișează lângă fiecare filtru activ din istoricul operațiilor o iconiță roșie cu „×”.
- [x] Permite eliminarea individuală a filtrului prin apăsarea iconiței sale.
- [x] După eliminare, recalculează imediat lista istoricului folosind numai filtrele care au rămas active.
- [x] Păstrează opțiunea existentă „Șterge filtrele” pentru eliminarea simultană a tuturor filtrelor.
- [x] Asigură o etichetă accesibilă pentru fiecare buton, de exemplu „Elimină filtrul operator”.

### Criterii de acceptare

- Fiecare filtru activ are propriul buton roșu „×”.
- Apăsarea butonului elimină numai filtrul asociat.
- Numărul și rândurile din istoricul operațiilor sunt actualizate conform filtrelor rămase.
- Eliminarea ultimului filtru readuce istoricul la varianta nefiltrată.

## Finalizat — precompletarea categoriei la adăugarea produsului

- [x] Precompletează categoria în formularul „Adaugă produs” cu categoria selectată în meniul produselor.
- [x] Precompletează și subcategoria atunci când pagina a fost deschisă pentru o subcategorie.
- [x] Lasă ambele câmpuri goale când formularul este deschis din „Toate produsele”.
- [x] Permite modificarea valorilor precompletate înainte de salvare.
- [x] Păstrează neschimbate editarea produselor și restul fluxului de salvare.

### Criterii de acceptare

- Dintr-o categorie selectată, formularul pornește cu acea categorie și cu subcategoria goală.
- Dintr-o subcategorie selectată, formularul pornește cu ambele valori completate.
- Din „Toate produsele”, formularul pornește fără categorie și subcategorie.
