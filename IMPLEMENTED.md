# Taskuri finalizate

Fisier separat de TODO.md (impartit la 28.09.2026, la cererea utilizatorului). Contine taskurile finalizate, in ordine cronologica: cel mai vechi primul, cel mai nou ultimul. Se completeaza doar prin append (taskurile noi finalizate se adauga la sfarsit; intrarile existente nu se modifica decat pentru corectii explicite).


## Finalizat înainte de 24.09.2026 13:44 — Precompletarea categoriei la adăugarea produsului

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

- [x] Precompletează categoria în formularul „Adaugă produs” cu categoria selectată în meniul produselor.
- [x] Precompletează și subcategoria atunci când pagina a fost deschisă pentru o subcategorie.
- [x] Lasă ambele câmpuri goale când formularul este deschis din „Toate produsele”.
- [x] Permite modificarea valorilor precompletate înainte de salvare.
- [x] Păstrează neschimbate editarea produselor și restul fluxului de salvare.

### Criterii de acceptare

- Dintr-o categorie selectată, formularul pornește cu acea categorie și cu subcategoria goală.
- Dintr-o subcategorie selectată, formularul pornește cu ambele valori completate.
- Din „Toate produsele”, formularul pornește fără categorie și subcategorie.

## Finalizat înainte de 24.09.2026 13:44 — Eliminarea individuală a filtrelor din jurnal

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

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

## Finalizat înainte de 24.09.2026 13:44 — Stil roșu pentru acțiunile de anulare și închidere

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

- [x] Identifică toate butoanele existente cu acțiunea sau eticheta „Anulează” și „Închide”.
- [x] Aplică acestor butoane stilul vizual roșu folosit pentru acțiunile de renunțare.
- [x] Actualizează formularele, panourile, popup-urile și dialogurile existente în mod consecvent.
- [x] Păstrează neschimbată funcționalitatea actuală a fiecărui buton.
- [x] Păstrează stările hover, focus și disabled clare și accesibile.

### Criterii de acceptare

- Toate butoanele „Anulează” și „Închide” din interfața existentă sunt roșii.
- Niciun alt buton nu își schimbă rolul sau comportamentul.
- Contrastul textului și indicatorul de focus rămân ușor de observat.

## Finalizat înainte de 24.09.2026 13:44 — Eliminarea diacriticelor la salvare

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

- [x] Elimină diacriticele din valorile text înainte de salvare.
- [x] Păstrează forma literelor introdusă de utilizator după eliminarea diacriticelor.
- [x] Aplică regula produselor, categoriilor, subcategoriilor, beneficiarilor și utilizatorilor.
- [x] Nu modifică parolele.

## Finalizat înainte de 24.09.2026 13:44 — Validarea unicității fără diferență între litere mari și mici

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

- [x] Folosește o cheie unitară pentru toate verificările de unicitate.
- [x] Ignoră diferențele dintre majuscule și minuscule la validare.
- [x] Ignoră diacriticele la compararea valorilor unice.
- [x] Păstrează în baza de date majusculele și minusculele introduse de utilizator.

## Finalizat înainte de 24.09.2026 13:44 — Persistență locală pentru modul demonstrativ și de testare

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

### Decizie tehnică

Înlocuiește stocarea volatilă din memorie și fișierele JSON separate cu o bază de date locală **SQLite**, păstrată într-un singur fișier pe server. SQLite oferă persistență, tranzacții, constrângeri de unicitate și acces concurent controlat fără Docker sau un serviciu de baze de date separat. Imaginile produselor rămân fișiere pe server; baza de date păstrează numai metadatele și calea relativă.

- [x] Creează baza de date locală SQLite în directorul persistent al aplicației, separat de fișierele publice, și permite configurarea căii acesteia.
- [x] Definește schema și versiunea bazei pentru produse, categorii, subcategorii, beneficiari, utilizatori, metadatele imaginilor, mișcările de stoc și evenimentele jurnalului.
- [x] Înlocuiește repository-urile demonstrative bazate pe memorie cu implementări SQLite care păstrează contractele asincrone și anularea operațiilor.
- [x] Mută evenimentele noi din `audit-events.jsonl` în tabela de audit SQLite, folosind același contract de eveniment stabilit în jurnalul de activitate.
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

## Finalizat înainte de 24.09.2026 13:44 — Jurnalizarea completă a acțiunilor

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

Toate subtaskurile jurnalizării sunt finalizate și verificate integrat.

### Subtaskuri finalizate

### Verificare integrată

- [x] Verifică toate tipurile de obiecte și toate operațiile: adăugare, editare, ștergere, conectare și deconectare.
- [x] Verifică respingerea editării și ștergerii fără motivare și păstrarea datelor formularului după o eroare.
- [x] Verifică valorile inițiale și finale din `Details`, motivarea din `Motif`, ținta actualizată și absența datelor sensibile.
- [x] Verifică linkurile către obiectele create, afișarea orei locale, filtrele, hoverul operației, paginarea și sincronizarea automată.

### Filtrare, paginare și sincronizare

- [x] Păstrează filtrele existente și aplică paginarea după filtrarea evenimentelor.
- [x] Adaugă selectorul numărului de evenimente pe pagină cu opțiunile 10, 20, 50 și „Toate”.
- [x] Afișează pagina curentă, numărul total de pagini și comenzile pentru pagina anterioară și următoare.
- [x] Revino la prima pagină când se schimbă un filtru sau numărul de elemente afișate.
- [x] Ascunde navigarea între pagini când este selectată opțiunea „Toate”.
- [x] Păstrează selecția validă și tabelul coerent la sincronizarea automată a jurnalului.

### Prezentarea jurnalului

Folosește contractul și conținutul deja stabilizate pentru jurnal.

- [x] În coloana „Detalii” afișează `Descriere modificare: <Details>` cu valoarea în italic, apoi pe un rând nou `Motiv: <Motif>` cu valoarea în italic.
- [x] Afișează „Editare” în tabel și în selectorul operațiilor.
- [x] Convertește timestampul UTC la fusul orar local al utilizatorului numai pentru afișarea datei și orei.
- [x] Păstrează valorile din coloana „Operație” interactive pentru filtrare și adaugă la hover un indiciu vizual clar, suplimentar schimbării cursorului, precum subliniere și evidențiere de fundal sau contur.

### Contractul evenimentului și compatibilitatea datelor

- [x] Extinde modelele de citire și scriere ale evenimentului și structura JSON din `audit-events.jsonl` cu proprietatea `Motif`.
- [x] Definește un contract unic care păstrează identificatorul evenimentului, timestampul UTC, operatorul, rolul, operația, tipul și identificatorul obiectului, `Target`, `Details` și `Motif`.
- [x] Înlocuiește valoarea operației „Modificare” cu „Editare” în întregul flux de audit.
- [x] Păstrează citirea evenimentelor existente care nu au `Motif` și tratează consecvent înregistrările istorice cu operația „Modificare”.
- [x] Păstrează toate timestampurile persistente în UTC.

### Serviciul comun de jurnalizare și regulile de integritate

Stabilește regulile comune folosite de toate operațiile auditate.

- [x] Centralizează scrierea evenimentelor pentru adăugare, editare, ștergere, conectare și deconectare.
- [x] Scrie exact un eveniment numai după finalizarea cu succes a operației; nu înregistra drept reușite operațiile anulate, respinse de validare sau eșuate.
- [x] Pentru o editare, construiește `Details` numai din câmpurile schimbate, indicând pentru fiecare valoarea inițială și valoarea finală.
- [x] Pentru o ștergere, păstrează în `Details` datele necesare identificării obiectului după eliminarea acestuia.
- [x] Pentru o editare, construiește `Target` din obiectul recitit sau rezultat după salvare, astfel încât să reflecte forma existentă în baza de date.
- [x] Nu include parole, hash-uri sau alte date sensibile în `Details` ori `Motif`.

### Componenta comună pentru motivarea editărilor și ștergerilor

Reutilizează contractul și serviciul comun de jurnalizare în toate formularele.

- [x] Creează un câmp reutilizabil „Motivare modificare” pentru toate paginile care permit editarea manuală sau ștergerea unui obiect, fără limitare la produse.
- [x] Validează obligatoriu motivarea și nu permite confirmarea dacă lipsește ori conține numai spații.
- [x] Transmite motivarea către serviciul de jurnalizare și salveaz-o în `Motif`.
- [x] Păstrează motivarea introdusă dacă salvarea eșuează și formularul rămâne deschis.

### Acoperirea tuturor operațiilor

Folosește contractul, serviciul comun și componenta comună pentru motivare.

- [x] Integrează serviciul comun în toate adăugările, editările și ștergerile, indiferent dacă obiectul este produs, beneficiar, utilizator, categorie, subcategorie sau altă entitate a sistemului.
- [x] Jurnalizează conectările reușite și deconectările confirmate.
- [x] Verifică faptul că fiecare operație reușită produce un singur eveniment, fără dubluri generate de interfață și repository.

### Navigarea din coloana „Țintă”

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

## Finalizat înainte de 24.09.2026 13:44 — Arhivarea obiectelor șterse și pregătirea restaurării

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

### Decizie tehnică

Folosește **tabele de arhivă în aceeași bază de date live**, nu o bază de date separată. Pentru compatibilitate între SQLite și MariaDB, tabelele folosesc prefixul `archive_` și reproduc câmpurile relevante ale entităților live.

Această variantă permite mutarea obiectului, a relațiilor sale și a evenimentului de audit în aceeași tranzacție. Păstrează cheile externe, backupul și restaurarea coerente și evită riscul ca o bază separată să fie indisponibilă sau nesincronizată în momentul ștergerii. Separarea logică rămâne clară prin tabele, repository-uri și permisiuni dedicate arhivei.

Fișierele asociate sunt mutate logic într-un director de arhivă separat, aflat pe același volum cu structura live. Fișierul este copiat mai întâi în zona de arhivă și verificat prin dimensiune și hash; tranzacția bazei mută apoi metadatele și obiectul, iar fișierul live este eliminat numai după confirmarea tranzacției. Astfel, o eroare poate lăsa temporar două copii, dar nu poate pierde singura copie disponibilă.

### Contract comun de arhivare

- [x] Definește un serviciu comun de arhivare folosit de toate entitățile care au sau vor primi opțiune de ștergere.
- [x] Păstrează identificatorul original, toate valorile obiectului, versiunea, relațiile necesare, timestampul UTC, operatorul și motivul ștergerii.
- [x] Adaugă un identificator unic al operației de arhivare și leagă evenimentul din jurnal de acesta.
- [x] Nu arhiva parole în clar; pentru conturile ce trebuie restaurate, păstrează hash-ul parolei numai în câmpul protejat al arhivei, cu acces restricționat, și nu îl include niciodată în jurnal sau interfață.
- [x] Nu permite repository-urilor să execute ștergeri fizice directe în fluxurile normale ale aplicației.

### Structura tabelelor de arhivă

- [x] Creează tabele `archive_*` pentru produse, beneficiari, utilizatori și relațiile dependente care pot fi șterse.
- [x] Aplică obligatoriu aceeași structură de arhivare tuturor entităților implementate ulterior care permit ștergerea, inclusiv proiecte și alte tipuri noi de obiecte.
- [x] Pentru fiecare entitate nouă, adaugă în aceeași modificare tabelele `archive_*`, relațiile arhivate, metadatele fișierelor asociate și integrarea cu serviciul comun de arhivare.
- [x] Nu considera completă implementarea unei entități noi cu opțiune de ștergere dacă nu include și schema, fluxul și verificările de arhivare corespunzătoare.
- [x] Păstrează separat identificatorul arhivei și identificatorul original din tabela live.
- [x] Adaugă constrângeri și indexuri pentru identificatorul original, tipul obiectului, data ștergerii și operator.
- [x] Versionează schema de arhivă împreună cu schema bazei live.

### Mutarea tranzacțională și jurnalizarea

- [x] În aceeași tranzacție, copiază obiectul și relațiile sale în tabelele `archive_*`, elimină înregistrările live și scrie evenimentul de audit.
- [x] Anulează întreaga tranzacție dacă arhivarea, eliminarea din zona live sau jurnalizarea eșuează.
- [x] Verifică versiunea obiectului înainte de arhivare pentru a preveni ștergerea unei versiuni modificate concurent.
- [x] Păstrează obiectele arhivate în afara listelor și validărilor curente ale datelor live.
- [x] Păstrează suficiente date pentru implementarea ulterioară a funcției de restaurare, fără a implementa încă interfața de undelete.

### Arhivarea fișierelor asociate

- [x] Creează o structură de directoare de arhivă paralelă cu structura fișierelor live și separată de fișierele publice.
- [x] Copiază fișierul în arhivă înainte de confirmarea tranzacției și verifică dimensiunea și hash-ul copiei.
- [x] Mută metadatele fișierului în tabela de arhivă împreună cu obiectul părinte.
- [x] Elimină fișierul live numai după confirmarea tranzacției bazei de date.
- [x] Curăță copia pregătită dacă tranzacția este anulată și înregistrează pentru reîncercare eliminarea unui fișier live rămas după confirmare.
- [x] Folosește căi relative controlate și blochează traversarea în afara directoarelor live și de arhivă.

### Verificare și recuperare

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

## Finalizat înainte de 24.09.2026 13:44 — Flux în doi pași pentru confirmarea ștergerii

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

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

## Finalizat înainte de 24.09.2026 13:44 — Păstrarea categoriilor și subcategoriilor goale

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

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

## Finalizat înainte de 24.09.2026 13:44 — Administrarea categoriilor și subcategoriilor

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

Depinde de „Proiecte asociate beneficiarilor”, deoarece administrarea trebuie să opereze pe categorii și subcategorii persistente, independente de produsele asociate.

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

## Finalizat înainte de 24.09.2026 13:44 — Crearea categoriilor exclusiv din pagina de administrare

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

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

## Finalizat înainte de 24.09.2026 13:44 — Jurnalizarea conectărilor și deconectărilor

**Data și ora implementării:** înainte de 24.09.2026 13:44 (ora exactă nu a fost înregistrată; lucrare anterioară introducerii depozitului Git).

Elementele finalizate ale taskului „Confirmarea deconectării și jurnalizarea sesiunilor”; fluxul popup de confirmare a fost finalizat ulterior în „Confirmarea deconectării”.

- [x] Jurnalizează fiecare conectare reușită și fiecare deconectare efectuată de utilizator.
- [x] Înregistrează în jurnal utilizatorul, tipul operației și timestampul UTC.

## Finalizat la 24.09.2026 14:08 — Collapse unitar pentru elementele cu structură subordonată

**Data și ora implementării:** 24.09.2026 14:08 (ora locală).

Implementat la 24 septembrie 2026. Toate elementele actuale și viitoare care afișează elemente subordonate folosesc aceeași interacțiune de extindere și restrângere, după modelul funcțional al secțiunii „Administrare” din meniul lateral.

### Componentă și contract vizual comun

- [x] Creează o componentă reutilizabilă pentru elementele extensibile, bazată pe comportamentul nativ accesibil al browserului sau pe un mecanism Blazor echivalent stabil.
- [x] Definește un contract comun pentru antet, conținut subordonat, starea extinsă/restrânsă și identificatorii necesari accesibilității.
- [x] Păstrează stilul nivelului în care componenta este folosită, fără a transforma toate elementele în carduri sau meniuri identice vizual.
- [x] Folosește un indicator discret și unitar al stării, integrat în antet; nu afișa indicatorul într-un buton sau badge separat.
- [x] Aplică stări unitare pentru normal, hover, activ, focus și disabled, folosind nuanțele existente ale aplicației.
- [x] Întreaga zonă principală a antetului poate fi apăsată pentru extindere sau restrângere.
- [x] Butoanele și linkurile de acțiune aflate în același antet rămân independente și nu schimbă accidental starea collapse.

### Comportament și accesibilitate

- [x] Permite operarea completă cu mouse, touch și tastatură, inclusiv Enter și Space.
- [x] Expune corect starea prin `aria-expanded`, relația prin `aria-controls` și etichete accesibile pentru conținutul subordonat.
- [x] La restrângere, elimină conținutul ascuns din ordinea de focus.
- [x] Păstrează starea deschisă a ramurii care conține pagina sau selecția activă.
- [x] Pentru listele fără selecție activă, folosește starea inițială definită de ecran; pagina de administrare a categoriilor continuă să pornească cu toate categoriile restrânse.
- [x] Păstrează starea utilizatorului pe durata aceleiași pagini și nu o resetează la actualizări de date care nu schimbă structura relevantă.
- [x] Asigură comportament corect pentru niveluri imbricate, fără ca extinderea unui părinte să modifice automat frații săi.

### Aplicare în interfața actuală și viitoare

- [x] Migrează secțiunea „Administrare” din meniul lateral la componenta comună fără schimbarea aspectului actual.
- [x] Aplică mecanismul categoriilor cu subcategorii din meniul produselor.
- [x] Aplică mecanismul categoriilor din pagina de administrare a categoriilor, păstrând acțiunile și tabelul subcategoriilor existente.
- [x] Aplică mecanismul oricărei alte liste sau secțiuni existente care afișează copii, detalii ori acțiuni subordonate.
- [x] Folosește obligatoriu aceeași componentă pentru ierarhiile viitoare beneficiar–proiect și proiect–observații din „Proiecte asociate beneficiarilor”, dacă acestea sunt prezentate extensibil în aceeași pagină.
- [x] Documentează componenta drept regulă pentru toate entitățile adăugate ulterior care au elemente subordonate.
- [x] Evită implementări locale paralele de collapse și elimină stilurile sau logica redundantă după migrare.

### Verificări

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

## Finalizat la 24.09.2026 14:50 — Cod produs în locul denumirii produsului

**Data și ora implementării:** 24.09.2026 14:50 (ora locală).

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

## Finalizat la 24.09.2026 16:03 — Stoc exclusiv prin mișcări de intrare și ieșire

**Data și ora implementării:** 24.09.2026 16:03 (ora locală).

Implementat la 24 septembrie 2026 de Claude. Stocul unui produs nu se mai introduce manual. Orice produs nou este creat cu stoc 0, iar orice modificare a stocului se face numai prin mișcări de intrare și ieșire (intrări și ieșiri). Această lucrare elimină din interfață și din persistență căile care setează direct cantitatea și pregătește terenul pentru pagina de mișcări.

Decizii stabilite cu utilizatorul (24 septembrie 2026):

- Formularul de creare a produsului nu mai are câmpul „Stoc inițial”; fiecare produs este creat cu stoc 0.
- Editorul de produs nu permite editarea manuală a cantității.
- Produsele existente își păstrează stocul curent; nu se generează mișcări și nu se modifică date.
- Până la livrarea lucrării „Intrări și ieșiri pentru un produs existent”, stocul nu poate fi modificat din aplicație; cele două taskuri se livrează în cicluri consecutive.

### Crearea produsului fără stoc inițial

- [x] Elimină din formularul de creare câmpul „Stoc inițial (bucăți)” și mesajele lui de validare.
- [x] Creează produsul cu stoc 0 în toate implementările (`DemoProductRepository`, `SqliteProductRepository`, `MariaProductRepository`); serverul nu acceptă și nu folosește o cantitate primită de la client la creare.
- [x] Elimină din `ProductInput` cantitatea ca valoare introdusă de utilizator și regula `Quantity < 0` (validarea „stoc inițial negativ”); adaptează construirea produsului la stoc 0.
- [x] Jurnalul creării produsului nu mai prezintă „Cantitate” ca valoare introdusă de utilizator; identificarea produsului rămâne prin cod, categorie și subcategorie.
- [x] Verifică toate căile de creare (formular, seed-ul modului demonstrativ, teste) și adaptează-le astfel încât niciuna să nu seteze un stoc diferit de zero pentru produse noi.

### Editarea produsului fără cantitate editabilă

- [x] Elimină câmpul editabil „Cantitate (bucăți)” din editorul de produs; afișează stocul curent numai pentru consultare (text, nu câmp de introducere), cu mențiunea că se modifică prin intrări și ieșiri.
- [x] Actualizarea produsului (SQLite, MariaDB, modul demonstrativ) nu mai scrie cantitatea (`quantity` / `produs_cantitate`) și nu acceptă o cantitate primită de la client; stocul existent rămâne neschimbat la salvare.
- [x] `ProductRules.CheckCurrent` compară acum și `Quantity`; exclude stocul din comparație (și din verificarea de versiune unde este cazul), astfel încât o modificare a stocului, prezentă sau viitoare, să nu invalideze un formular de editare deschis în paralel.
- [x] Jurnalul editării produsului nu mai conține modificări de cantitate (`ProductInput`: `new("Cantitate", …)`).
- [x] Păstrează regula de ștergere existentă: un produs se șterge numai cu stoc zero și fără mișcări asociate; snapshot-ul arhivei produsului păstrează cantitatea ca dată istorică.

### Verificări

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

## Finalizat la 24.09.2026 16:24 — Intrări și ieșiri pentru un produs existent

**Data și ora implementării:** 24.09.2026 16:24 (ora locală).

Implementat la 24 septembrie 2026 de Claude. Pagina de mișcări de stoc a unui produs: se deschide prin selectarea produsului din tabelul catalogului, permite înregistrarea intrărilor și ieșirilor, afișează istoricul lor și stocul curent. Depinde de taskul finalizat „Stoc exclusiv prin mișcări de intrare și ieșire” (stoc 0 la creare, fără editarea manuală a cantității) și este prerequisit pentru „Proiecte asociate beneficiarilor” (pagina „Echipamente”) („Echipamente” citește mișcările asociate proiectului) și înlocuiește aplicația veche `IO.cs` (WinForms, ecranul „Intrări/Ieșiri”).

Decizii stabilite cu utilizatorul (24 septembrie 2026):

- Această lucrare include adăugarea, listarea, editarea, ștergerea și istoricul modificărilor.
- „Beneficiar” și „Proiect” sunt două căsuțe independente, vizibile numai la ieșire; proiectul poate fi activat numai după beneficiar, este opțional și este limitat la proiectele beneficiarului ales.
- Stocul este calculat din mișcări și poate deveni negativ; o ieșire care depășește stocul nu este blocată și nu cere avertisment.
- Descrierea este obligatorie la intrare și la ieșire; data mișcării este aleasă din calendar și nu poate depăși ziua curentă. **Înlocuit ulterior (25 septembrie 2026, la cererea utilizatorului):** inițial putea fi oricând, inclusiv în viitor; acum calendarul este numai pentru selectare (fără tastare), ultima zi afișată este azi, iar serverul respinge date viitoare.
- Stocul se modifică exclusiv prin mișcările acestui task: taskul finalizat „Stoc exclusiv prin mișcări” a eliminat „Stoc inițial” din crearea produsului (orice produs nou are stoc 0) și editarea manuală a cantității din editorul de produs.

### Acces și pagina produsului

- [x] Selectarea unui produs din tabelul catalogului (codul produsului și acțiunea „Detalii”) deschide pagina produsului, cu identificatorul numeric în rută (`/produse/{id}/miscari`); identificatorul nu se afișează utilizatorului.
- [x] Pagina afișează în panoul „Produs”: categoria, subcategoria, „Cod produs” (nu „Denumire”), imaginea produsului cu mărire la click și butonul „Editare produs”, care reutilizează editorul și regulile existente.
- [x] Panoul de detalii actual al catalogului este înlocuit de această pagină; acțiunile de editare și ștergere ale produsului rămân disponibile utilizatorilor autorizați, cu fluxurile și motivările existente.
- [x] Afișează stare clară (fără excepție necontrolată) dacă produsul nu există sau a fost șters între timp și oferă revenirea la catalog, către catalogul filtrat pe categoria și subcategoria produsului. Textul de căutare și pagina curentă a catalogului nu se restaurează (rămâne pentru restaurarea contextului de navigare din „Proiecte asociate beneficiarilor” (contextul de navigare)).
- [x] Respectă aspectul din ecranul de referință (panou „Produs”, panou de operare, tabel de mișcări) cu stilurile aplicației și fără depășire orizontală sub 900 px.
- [x] Autorizare: aceleași roluri care pot opera produse (`EnsureProductOperatorAsync`), verificate și pe server.

### Model de date și reguli de domeniu

- [x] Definește tipul mișcării (intrare/ieșire) și un model de mișcare cu: produs, tip, cantitate (întreg pozitiv), data mișcării (doar data, fără oră, independentă de `created_utc`), descriere, beneficiar și proiect opționale, operator, versiune și timestamp UTC furnizat de server.
- [x] Creează `StockMovementRules` (în stilul `ProjectRules`/`ProductInput`): cantitate întreagă ≥ 1 și limită superioară fixată într-o constantă (propunere: 100.000; se confirmă la implementare), dată validă, descriere obligatorie (după normalizarea existentă `TextNormalization.ForStorage`, inclusiv eliminarea diacriticelor) cu lungime maximă, mesaje clare în română.
- [x] Beneficiarul și proiectul sunt permise numai la ieșire; la intrare serverul le respinge sau le ignoră explicit, nu le salvează. Proiectul trebuie să aparțină beneficiarului ales, altfel cererea este respinsă.
- [x] Regula stocului: stoc = total intrări − total ieșiri, valoare care poate fi negativă; nu se adaugă nicio validare de blocare sau avertisment pentru stoc insuficient.
- [x] Data se acceptă între 1990 și azi (regulă modificată la 25 septembrie 2026: nu poate fi în viitor); nu se folosește ora curentă pentru ordonare, ci data mișcării, apoi identificatorul.
- [x] Verifică regulile prin teste de domeniu (fără interfață), incluzând valori limită și texte doar cu spații.

### Persistență și stoc atomic

- [x] SQLite (modul demonstrativ și local): extinde `stock_movements` (existent, cu `product_id`, `beneficiary_id`, `project_id`, `quantity`, `created_utc`) cu tip, data mișcării, descriere, operator, versiune și `updated_utc`, printr-o migrare a schemei gestionată în proiect (crește versiunea schemei, fără fișier SQL de upgrade separat); adaugă indexuri pentru produs, dată, beneficiar și proiect.
- [x] MariaDB: mapează pe tabelele existente `io` (`io_tip_actiune`, `io_numar_bucati`, `io_descriere`, `io_data`) și `io_history`; adaugă `id_project` (contractul mișcare–proiect amânat în „Proiecte asociate beneficiarilor”) și extinde coloana `io_numar_bucati` (în prezent `tinyint`) dacă limita cantității o cere, prin inițializarea gestionată de aplicație. Verifică formatul actual al `io_data` din backup și păstrează-l; nu se introduc date, parole sau scripturi separate. Implementat, dar netestat pe un server MariaDB real. Formatul `io_data` (`dd-MM-yyyy`) și semnificația `io_tip_actiune` (1 = intrare) au fost preluate din codul aplicației vechi (`SQLWrapper.cs`, `IO.cs`), nu verificate pe datele din backup.
- [x] Repository-uri complet asincrone (`CancellationToken`) pentru SQLite, MariaDB și modul demonstrativ: listare (filtru tip, sortare după dată, paginare), consultare, creare, editare și ștergere/arhivare. Implementate: `SqliteStockMovementRepository` (modul demonstrativ al aplicației și persistența locală) și `MariaStockMovementRepository`. Nu există implementare în memorie: `DemoProductRepository` (folosit doar în teste) nu are mișcări.
- [x] Inserarea mișcării și actualizarea stocului produsului (`quantity`/`produs_cantitate`) se fac în aceeași tranzacție, cu actualizare atomică `quantity = quantity + delta`, astfel încât două cereri simultane să nu piardă o modificare.
- [x] Actualizarea stocului nu incrementează versiunea produsului (altfel o editare concurentă a produsului ar fi respinsă la fiecare mișcare) și nu este suprascrisă de editorul de produs (vezi „Stoc exclusiv prin mișcări de intrare și ieșire”).
- [x] Ștergerea unui produs cu mișcări rămâne blocată (regula `ProductRules.CheckDelete` existentă), iar ștergerea beneficiarilor și proiectelor cu mișcări asociate rămâne blocată în SQLite și MariaDB (înlocuiește amânarea documentată în `PROJECT_STATE.md` pentru MariaDB).
- [x] Modul SQLite: pentru produsele existente fără mișcări și cu stoc diferit de zero, migrarea creează o mișcare „Stoc inițial” (intrare pentru stoc pozitiv, ieșire pentru stoc negativ), astfel încât stocul să fie egal cu suma mișcărilor. MariaDB nu primește mișcări generate automat.

### Formular de adăugare

- [x] Panoul de operare conține: câmp pentru data mișcării (implicit data de azi, modificabilă, cu selector de calendar), comutatorul „Intrare”/„Ieșire” (Intrare selectată implicit, evidențiere verde/roșie conform ecranului de referință), „Descriere” (câmp multi-linie, obligatoriu), „Cantitate” și butonul „Adaugă”.
- [x] La „Ieșire” apar căsuța „Beneficiar” cu lista beneficiarilor (căutare în listă) și, numai după bifarea beneficiarului, căsuța „Proiect” cu proiectele acelui beneficiar; debifarea beneficiarului șterge și proiectul; schimbarea beneficiarului resetează proiectul.
- [x] La „Intrare”, câmpurile Beneficiar și Proiect nu sunt vizibile, iar valorile selectate anterior nu se trimit.
- [x] Dacă „Beneficiar” este bifat fără selecție sau „Proiect” este bifat fără selecție, salvarea este respinsă cu mesaj clar și datele formularului rămân.
- [x] Dacă nici beneficiarul, nici proiectul nu sunt activate, se completează numai cantitatea, data și descrierea.
- [x] Validările sunt afișate în formular (cantitate, dată, descriere obligatorie) și repetate pe server.
- [x] După salvare reușită: mesaj inline de confirmare, golirea descrierii și a cantității, păstrarea datei și a tipului, actualizarea listei și a stocului fără reîncărcarea paginii. Confirmarea uniformă a salvărilor rămâne în „Confirmarea salvărilor care modifică date existente”.
- [x] Butonul „Adaugă” este dezactivat pe durata salvării pentru a preveni dubla trimitere; un eșec de persistență afișează eroarea și păstrează datele introduse.

### Tabelul de mișcări și stocul curent

- [x] Afișează coloanele Data (format `zz-LL-aaaa`), Intrare/Ieșire (intrarea în verde, ieșirea în roșu, cu marcajul `[*]` pentru mișcările modificate), Număr bucăți, Descriere, Beneficiar.
- [x] Coloana „Beneficiar” afișează beneficiarul și proiectul din care face parte ieșirea, atunci când există; rămâne goală pentru intrări și pentru ieșirile fără ele. Beneficiarul și proiectul sunt linkuri către paginile lor (`/beneficiari/{id}`, `/proiecte/{id}`), consecvent cu restul aplicației.
- [x] Filtrul „Intrări/Ieșiri” (toate, doar intrări, doar ieșiri) și sortarea după dată cu indicator (implicit crescător, ca în aplicația veche; ordine stabilă după identificator la date egale).
- [x] Paginare în același stil cu jurnalul (10, 20, 50, „Toate”), aplicată după filtrare, fără încărcarea inutilă a tuturor mișcărilor.
- [x] Afișează „Total produse în stoc: N” calculat din mișcări, cu valoare negativă vizibilă ca atare, și actualizează valoarea după fiecare adăugare, editare sau ștergere.
- [x] Stare goală clară („Nu există intrări/ieșiri pentru acest produs”) și stare de eroare cu buton „Reîncearcă”.
- [x] Bara de jos afișează: „Intrările/ieșirile cu [*] au suferit modificări. Click dreapta pentru a vedea istoricul modificărilor”, doar dacă există astfel de mișcări.

### Editarea și ștergerea mișcărilor

- [x] Selectarea unui rând permite editarea datei, cantității, descrierii și, la ieșire, a beneficiarului și proiectului; tipul mișcării nu se schimbă (se șterge și se adaugă una nouă).
- [x] Editarea și ștergerea cer „Motivare modificare” obligatorie (componenta `ChangeReasonField` și `ChangeReasonRules` existente) și verifică versiunea mișcării pentru a preveni suprascrierea unei modificări concurente.
- [x] Ștergerea folosește fluxul în doi pași (`DeleteConfirmationDialog`), afișează cantitatea și corecția de stoc rezultată și arhivează mișcarea prin serviciul comun (`ArchiveSchemaRegistry`: entitate nouă „MiscareStoc”, tabel `archive_stock_movements`, versionarea schemei de arhivă, fără ștergere fizică directă).
- [x] Editarea și ștergerea recalculează stocul produsului (corecția = diferența dintre efectul nou și cel vechi), în aceeași tranzacție cu mișcarea, cu istoricul și cu evenimentul de audit.
- [x] O eroare într-o etapă anulează întreaga operație, fără stoc modificat parțial și fără mișcare parțial arhivată. Garantat prin tranzacția unică (rollback la orice excepție); nu există un test cu eroare injectată în fiecare etapă.
- [x] Interfața afișează un rezumat înaintea confirmării (cantitate veche/nouă, corecție de stoc), ca în aplicația veche.

### Istoricul modificărilor

- [x] Fiecare editare scrie o înregistrare de istoric (SQLite: tabel dedicat; MariaDB: `io_history`) cu operator, data și ora, valorile înainte/după, corecția de stoc și motivarea.
- [x] Mișcările cu istoric primesc marcajul `[*]`; ștergerea păstrează istoricul în arhivă împreună cu mișcarea.
- [x] Click dreapta pe un rând cu `[*]` deschide istoricul modificărilor mișcării; același dialog este accesibil și fără mouse (buton „Istoric” pe rândul selectat, tastatură) și pe ecrane tactile.
- [x] Dialogul de istoric afișează cronologic modificările, cu timestamp în ora locală, și se închide cu Escape sau buton.

### Audit, jurnal și contract pentru alte taskuri

- [x] Adaugă `AuditEntities.StockMovement` („MiscareStoc”) și jurnalizează adăugarea, editarea și ștergerea cu `Details` (valori înainte/după, tip, cantitate, dată, beneficiar, proiect), `Motif` la editare/ștergere, ținta „cod produs” și identificatorul mișcării în `EntityId`.
- [x] `AuditNavigation.EditUrl` leagă evenimentele mișcărilor de pagina produsului (`/produse/{id}/miscari`); evenimentele mișcărilor arhivate rămân cu țintă text. Implementat prin `/miscari/{id}`, care rezolvă mișcarea și redirecționează către `/produse/{idProdus}/miscari` (evenimentul stochează id-ul mișcării, nu al produsului); după ștergere linkul afișează „Mișcarea nu mai există”.
- [x] pagina „Echipamente” („Proiecte asociate beneficiarilor”) devine deblocat: contractul de citire a mișcărilor după proiect (`project_id`) este expus prin repository, cu produs (cod), cantitate, data, operator și referința mișcării.
- [x] Expune un contract de evenimente de modificare pentru mișcări, ce va fi consumat de „Sincronizarea între utilizatori prin evenimente din baza de date”, fără a implementa sincronizarea acum. Contractul curent este evenimentul de audit `MiscareStoc` (acțiune, `EntityId`, țintă, detalii); nu a fost creat un canal separat de notificare, iar sincronizarea rămâne pentru „Sincronizarea între utilizatori prin evenimente din baza de date”.

### Verificări

- [x] Teste de domeniu pentru regulile din 1.2 (cantitate, dată, descriere, beneficiar/proiect doar la ieșire, proiect aparținând beneficiarului).
- [x] Teste de persistență pe SQLite (și MariaDB unde mediul permite) pentru stoc atomic: două adăugări simultane păstrează suma corectă; editare și ștergere recalculează stocul; stocul poate deveni negativ fără eroare. Acoperit numai pe SQLite; MariaDB nu a fost testat pe un server real.
- [x] Teste pentru arhivarea mișcării șterse, erori injectate în fiecare etapă, editare/ștergere concurentă a aceleiași mișcări (una singură reușește) și istoricul modificărilor. Acoperite: arhivarea împreună cu istoricul, ștergere repetată/veche respinsă. Neacoperite: erori injectate în fiecare etapă și cereri de editare/ștergere strict simultane (verificarea de versiune sub tranzacție serializabilă le respinge, dar nu au test dedicat).
- [x] Verificat în browser, cu preview-ul actualizat la `http://127.0.0.1:5082/`: deschiderea din catalog (codul produsului) și din `/miscari/{id}`, adăugare intrare, ieșire cu beneficiar, respingerea „Beneficiar” bifat fără selecție, editare cu motivare (rezumatul corecției de stoc), marcajul `[*]`, istoric prin click dreapta, ștergere în doi pași cu corecția de stoc, jurnalul (adăugare/editare/ștergere), linkul din jurnal, `/produse?sterge=<id>`. Nu au fost exersate manual în browser: ieșirea cu proiect (demo-ul nu are proiecte pentru beneficiarul ales), filtrul Intrări/Ieșiri, sortarea și paginarea (acoperite de verificările automate ale repository-ului).
- [x] Verifică că un produs nou (stoc 0, conform „Stoc exclusiv prin mișcări de intrare și ieșire”) primește stoc numai prin prima intrare și că o ieșire îl poate duce sub zero.
- [x] Verificat la 375 și 768 px (fără depășire orizontală a paginii; tabelul derulează intern la 375 px) și pe desktop; build Release fără avertismente și suita `tests/BlazorStoc.Checks` (285 de verificări) trecute; `VALIDARE.md` și `docs/PROJECT_STATE.md` actualizate.

### Criterii de acceptare

- Selectarea unui produs din catalog deschide pagina sa de intrări/ieșiri, cu datele produsului, formularul de operare, tabelul mișcărilor și „Total produse în stoc”.
- La intrare, Beneficiar și Proiect nu sunt vizibile; la ieșire pot fi activate independent (proiectul numai după beneficiar), iar fără ele se culeg doar cantitatea, data și descrierea.
- Descrierea este obligatorie la intrare și la ieșire; data mișcării este aleasă din calendar (fără tastare) și nu poate fi în viitor (regulă modificată la 25 septembrie 2026).
- Coloana „Beneficiar” afișează beneficiarul și proiectul mișcării când există.
- O ieșire poate duce stocul sub zero; stocul negativ este afișat corect, fără blocare sau avertisment.
- Stocul se actualizează atomic împreună cu mișcarea; două operații simultane nu pierd nicio modificare.
- Editarea și ștergerea cer motivare, corectează stocul, sunt jurnalizate, arhivate (ștergerea) și marcate cu `[*]` cu istoric accesibil prin click dreapta.
- Stocul unui produs se schimbă numai prin intrări și ieșiri; un produs nou pornește de la stoc 0 („Stoc exclusiv prin mișcări de intrare și ieșire”).
- Mișcările de ieșire asociate unui proiect pot fi citite de „Proiecte asociate beneficiarilor” (pagina „Echipamente”) prin `project_id`.
- Funcționalitatea rămâne asincronă, fără Docker, fără integrare NAS/QNAP și fără fișiere SQL de upgrade separate.

## Finalizat la 25.09.2026 08:40 — Proiecte asociate beneficiarilor

**Data și ora implementării:** 25.09.2026 08:40 (ora locală); modelul de date la 24.09.2026 14:57, persistența, paginile și fișierele la 24.09.2026 15:32.

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

### Finalizat — Contractul de evenimente pentru sincronizare

Implementat la 25 septembrie 2026 de Claude. `Services/ChangeFeed.cs`, `Components/Pages/BeneficiaryDetail.razor`, `ProjectPage.razor`, `ProjectObservationPage.razor`, `Program.cs`.

- [x] Contract de evenimente: `ChangeEvent` și `IChangeFeed`, publicate după commit de decoratorii `ChangeNotifyingProjectRepository` și `ChangeNotifyingProjectFileStore`, identic pentru SQLite și MariaDB. Evenimentele conțin doar identificatori (tip, acțiune, id, proiect, observație, beneficiar, sesiunea de origine, moment UTC), niciodată denumiri, texte sau conținut de fișiere.
- [x] Lista proiectelor din pagina beneficiarului și paginile proiectului și observației se abonează la feed și se reîmprospătează pe loc pentru modificările altor sesiuni (evenimentele propriei sesiuni sunt ignorate). Formularele și dialogurile deschise nu sunt înlocuite: primesc doar o notificare, iar verificarea versiunii rămâne protecția finală.
- [x] Încărcarea unui fișier într-o observație scrie acum evenimentul de audit „Adăugare” (SQLite: în aceeași tranzacție; MariaDB: după inserare). Evenimentul lipsea, deși taskul îl dădea drept finalizat; conținutul fișierului nu intră în jurnal.
- „Sincronizarea între utilizatori prin evenimente din baza de date” conectează feed-ul la surse externe (trigger-e, SignalR); paginile nu mai necesită modificări.

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
- [x] Permite editarea beneficiarului asociat, a denumirii și a observațiilor generale, cu motivare obligatorie și verificarea versiunii; după mutare, proiectul apare numai în pagina noului beneficiar. **Înlocuit ulterior („Confirmarea salvărilor care modifică date existente”):** beneficiarul proiectului nu se mai poate schimba; regula este aplicată și pe server.
- [x] Respinge salvarea dacă beneficiarul selectat nu mai există sau dacă denumirea este deja folosită de alt proiect al aceluiași beneficiar.
- [x] Aplică stilul unitar al linkurilor și butoanelor și păstrează datele formularului după o validare respinsă.

### Finalizat — Pagina proiectului și legătura cu inventarul

Implementat la 24 septembrie 2026 de Claude. `Components/Pages/ProjectPage.razor`, `Components/Pages/ProjectEquipment.razor`. Citirea efectivă a mișcărilor de stoc rămâne în pagina „Echipamente”, deoarece modulul de intrări/ieșiri nu există încă.

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
- [x] Adaugă proiectele în registrul comun al rutelor jurnalului (`AuditNavigation.EditUrl`), folosind identificatorul stabil și pagina de consultare `/proiecte/{id}`. Observațiile rămân în restaurarea contextului de navigare, deoarece necesită un identificator compus (proiect + observație) în registrul de rute.
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

## Finalizat la 25.09.2026 08:58 — Navigarea din jurnal către pagina obiectului

**Data și ora implementării:** 25.09.2026 08:58 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/AuditTrail.cs` (`AuditNavigation.TargetUrl`, `RemovalTimes`, `ProductNavigation`, `UserNavigation`), `Services/ListNavigationContext.cs` (`AuditListState`), `Components/Pages/Audit.razor`, `Components/Pages/ProductMovements.razor`, `Components/Pages/BeneficiaryDetail.razor`, `Components/Pages/UserDetail.razor`, `Components/Pages/Users.razor`.

- [x] Fiecare tip de obiect are o rută stabilă de consultare, construită numai din tipul entității și identificatorul din `EntityId`: `/produse/{id}` (pagina produsului cu datele și mișcările de stoc; `/produse/{id}/miscari` rămâne valabilă), `/beneficiari/{id}`, `/utilizatori/{id}` (nouă, numai administratori), `/proiecte/{id}`, `/observatii/{id}` și `/miscari/{id}`.
- [x] Linkul din coloana „Țintă” deschide pagina obiectului, nu formularul de editare; rutele nu mai conțin `?edit=`.
- [x] Pagina beneficiarului și pagina utilizatorului au buton separat „Editează” (beneficiar: utilizatorii autentificați; utilizator: administratorii); pagina produsului îl afișează utilizatorilor autorizați. Deschiderea paginii nu pornește editarea; nu există încă lock de editare („Sincronizarea între utilizatori prin evenimente din baza de date”).
- [x] Autorizarea rămâne cea existentă: `/utilizatori/{id}` cere rolul Administrator (atât rută, cât și verificare în pagină), produsele și beneficiarii cer autentificare.
- [x] Registrul extensibil este dicționarul `AuditNavigation.Routes`, un singur loc pentru toate tipurile; tipurile fără intrare (categorii, subcategorii, fișiere de observație) și evenimentele fără identificator (conectări, deconectări) rămân text.
- [x] Obiectele cu o ștergere înregistrată după eveniment (mutate în arhivă) nu primesc link (`AuditNavigation.RemovalTimes`); evenimentele de ștergere rămân text, iar evenimentele ulterioare ale unui identificator reutilizat își păstrează linkul. Paginile obiectelor inexistente afișează „nu mai există”.
- [x] Filtrele, dimensiunea paginii și pagina jurnalului sunt în adresă (`/jurnal?q=…&tip=…&operatie=…&operator=…&data=…&pe-pagina=…&pagina=…`, doar valorile diferite de implicit, înlocuită pe loc), citite doar la deschiderea paginii; Înapoi din browser le restaurează, împreună cu poziția de derulare (`data-restore-scroll`). Valorile invalide din adresă revin la implicit.
- [x] Verificări automate în `BlazorStoc.Checks` pentru rutele tuturor tipurilor, textul țintei ignorat, identificatori invalizi, obiecte fără pagină, obiecte eliminate și starea jurnalului din adresă.

### Neacoperit intenționat

- Declanșatoarele `?edit=<id>` ale listelor (`/produse`, `/beneficiari`, `/utilizatori`) au rămas pentru butonul explicit „Editează” al produsului și pentru linkuri existente; jurnalul nu le mai folosește.
- Pentru observațiile arhivate odată cu proiectul lor nu s-a verificat dacă fiecare are propriul eveniment de ștergere; dacă nu, linkul din jurnal rămâne, iar pagina afișează „Observația nu mai există”, nu o pagină invalidă.

## Finalizat la 25.09.2026 09:03 — Identificarea beneficiarului cu CUI duplicat

**Data și ora implementării:** 25.09.2026 09:03 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `BeneficiaryRules.DuplicateCuiMessage` (`Services/Beneficiaries.cs`), `DemoBeneficiaryRepository`, `SqliteBeneficiaryRepository`, `MariaBeneficiaryRepository`; `BeneficiaryEditor.razor` nemodificat.

- [x] La adăugarea sau editarea unui beneficiar, dacă CUI-ul există deja, mesajul de eroare include numele beneficiarului care folosește acel CUI: „Există deja un beneficiar cu acest CUI: «nume».”, același text în modul demonstrativ, SQLite și MariaDB.
- [x] Mesajul folosește numele așa cum este salvat în baza de date, nu cel introdus în formular.
- [x] Formularul rămâne deschis cu valorile introduse după respingerea salvării (comportament existent al `BeneficiaryEditor`, verificat în browser).
- [x] MariaDB: eroarea 1062 a indexului `UX_beneficiar_cui`, produsă de o salvare concurentă, este tradusă după rollback în același mesaj, cu numele existent.
- [x] Beneficiarul editat este exclus din verificare: salvarea fără schimbarea CUI-ului nu îl raportează ca duplicat.
- Neverificat pe un server MariaDB real; editarea în browser nu a fost exersată manual (acoperită de verificările automate).

## Finalizat la 25.09.2026 09:14 — Confirmarea salvărilor care modifică date existente

**Data și ora implementării:** 25.09.2026 09:14 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Components/Shared/SaveConfirmationDialog.razor`, `SaveSummary` (`Services/SaveConfirmation.cs`), `BeneficiaryEditor`, `ProductEditor`, `ProjectEditor`, `ProjectObservationEditor`, `UserEditor`, `ProductGroups` (categorii și subcategorii) și dialogul de editare din `ProductMovements`.

- [x] Fiecare salvare care modifică date existente (beneficiar, produs, proiect, observație, utilizator, categorie, subcategorie, mișcare de stoc) deschide un popup de confirmare înaintea execuției.
- [x] Popup-ul (`role="dialog"`, `aria-modal`, focus pe dialog, Escape = refuz) prezintă un tabel „Câmp / Valoare actuală / Valoare nouă” doar pentru câmpurile care se schimbă, plus motivarea; valorile goale apar ca „(gol)”, textele foarte lungi sunt scurtate, iar parola nu apare niciodată (doar „va fi schimbată”). Pentru mișcări apare și corecția de stoc, pentru produs și imaginea înlocuită.
- [x] Validarea (`Validated`, motivare obligatorie, câmpuri invalide) rulează înaintea popup-ului; salvarea se execută numai după „Confirmă salvarea”.
- [x] La „Anulează” (sau Escape) nu se salvează nimic, iar formularul rămâne deschis cu valorile curente.
- [x] Crearea nu afișează popup: produsul nou, ca și beneficiarul, proiectul, observația, utilizatorul, categoria, subcategoria și mișcarea nouă se salvează direct. Adăugarea unei mișcări este creare, nu editare, deci nu cere confirmare (decizie a implementării; poate fi extinsă).
- [x] Regulă de server cerută de utilizator (formularul de editare a proiectului nu mai are „Beneficiar”): `ProjectRules.CheckBeneficiaryUnchanged` respinge orice `UpdateAsync` care schimbă beneficiarul unui proiect (`BeneficiaryLockedMessage`), în SQLite și MariaDB, înaintea oricărei scrieri, fără eveniment de audit sau de sincronizare. Mutarea proiectului dintre beneficiari („Proiecte asociate beneficiarilor”) nu mai este posibilă.
- Neverificat manual în browser: dialogul pentru produs, utilizator, proiect, observație, categorie și mișcare (același component, acoperit de compilare); MariaDB neverificat pe server real.

## Finalizat la 25.09.2026 09:32 — Confirmarea la părăsirea formularului de adăugare produs

**Data și ora implementării:** 25.09.2026 09:32 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Components/Pages/Home.razor`, `wwwroot/leave-guard.js`, `ProductMenuSelection` (`Services/LeaveConfirmation.cs`), `Components/Shared/SaveConfirmationDialog.razor` (etichete și mesaj configurabile).

- [x] Cât timp formularul „Adaugă produs” este deschis, alegerea altei categorii, subcategorii sau a „Toate produsele” din meniul produselor este interceptată (clic, Enter sau Spațiu): `leave-guard.js` oprește navigarea înainte să se producă, inclusiv navigarea prin reîncărcare a antetelor de categorie, deci meniul rămâne la poziția anterioară.
- [x] Popup accesibil (`role="dialog"`, `aria-modal`, focus pe dialog, Escape = continuare): „Părăsești adăugarea produsului?” cu mesajul că formularul va fi închis și datele nesalvate, inclusiv imaginea selectată, se pierd; acțiunile explicite „Părăsește adăugarea” (roșu) și „Continuă adăugarea” (verde), reutilizând `SaveConfirmationDialog` din „Confirmarea salvărilor care modifică date existente”.
- [x] Confirmarea închide formularul și execută exact navigarea solicitată; anularea lasă formularul și meniul neschimbate, cu toate valorile și imaginea introduse (editorul nu este atins).
- [x] Popup-ul nu apare dacă formularul de adăugare nu este deschis (protecția se activează numai cât timp `editing && editingProduct is null`), nici la editarea unui produs existent, nici la alegerea selecției curente (`ProductMenuSelection.IsSameSelection`).
- Neverificat manual: comportamentul pe ecran tactil; ieșirea din pagină prin alte linkuri (Meniu principal, Administrare) nu este protejată, conform cerinței (numai meniul produselor).

## Finalizat la 25.09.2026 09:34 — Revenirea în pagina de origine după editarea sau ștergerea produsului

**Data și ora implementării:** 25.09.2026 09:34 (ora locală).

Cerere directă a utilizatorului, 25 septembrie 2026, implementată de Claude. `ReturnNavigation` (`Services/ReturnNavigation.cs`), `Components/Pages/ProductMovements.razor`, `Components/Pages/Home.razor`.

- [x] Acțiunile „Editează” și „Șterge produs” din pagina produsului (`/produse/{id}/miscari`) trimit adresa paginii de origine în `/produse?edit=<id>&inapoi=<adresă>` / `?sterge=<id>&inapoi=<adresă>`; adresa este acceptată numai dacă este o cale locală (fără gazdă, `//` sau `\`), deci nu poate servi ca redirecționare externă.
- [x] Părăsirea editării (Închide, Anulează) și anularea ștergerii readuc utilizatorul în pagina produsului; salvarea confirmată a editării revine tot acolo.
- [x] După o ștergere efectuată, produsul nu mai există, deci utilizatorul ajunge în catalog (comportamentul existent). Linkurile fără `inapoi` (de exemplu din jurnal sau linkuri vechi) păstrează comportamentul anterior: revenirea în catalog.

## Finalizat la 25.09.2026 09:37 — Confirmarea deconectării

**Data și ora implementării:** 25.09.2026 09:37 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Components/Layout/MainLayout.razor` (buton și dialog), `wwwroot/logout-dialog.js`, `Pages/Account/Logout.cshtml.cs`, stiluri `.logout-dialog` în `wwwroot/app.css`. Jurnalizarea sesiunilor era finalizată anterior (vezi „Jurnalizarea conectărilor și deconectărilor” mai jos).

- [x] Acțiunea „Deconectare” din bara de sus deschide un popup de confirmare (element nativ `<dialog>` modal, cu `aria-labelledby`/`aria-describedby`, focus pe „Anulează deconectarea”, Escape = anulare) în locul paginii de deconectare; `GET /Account/Logout` redirecționează acum la `/`.
- [x] Popup-ul are butonul roșu „Deconectare” și butonul verde „Anulează deconectarea”.
- [x] Deconectarea se execută numai după confirmare: butonul roșu trimite formularul `POST /Account/Logout` cu token antiforgery (`<AntiforgeryToken />`), iar `LogoutModel` încheie sesiunea, înregistrează deconectarea în jurnal și redirecționează la autentificare.
- [x] La anulare (buton sau Escape) popup-ul se închide și nu se modifică nimic altceva: pagina, formularele deschise și textul introdus rămân (verificat cu un editor deschis și text tastat).
- [x] Conectările reușite și deconectările confirmate rămân vizibile în jurnalul de activitate (finalizat anterior).
- Confirmarea efectivă a deconectării (butonul roșu) a fost verificată de utilizator: duce la pagina de autentificare.

## Finalizat la 25.09.2026 09:51 — Sincronizarea între utilizatori prin evenimente din baza de date

**Data și ora implementării:** 25.09.2026 09:51 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/ChangeEvents.cs` (trigger-e, surse SQLite/MariaDB, `ChangeEventRelay`), `Services/ChangeFeed.cs` (`ILocalChangeLedger`), `Services/LiveRefresh.cs`, `Services/ChangesHub.cs`, `Components/Shared/LiveChangeNotice.razor`, `Program.cs`, paginile `Home`, `ProductMovements`, `Users`, `UserDetail`; paginile beneficiarului, proiectului și observației („Proiecte asociate beneficiarilor”) folosesc același feed.

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

## Finalizat la 25.09.2026 10:09 — Blocarea temporară a editării unui produs

**Data și ora implementării:** 25.09.2026 10:09 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/ProductLocks.cs` (`IProductLockRepository`, `SqliteProductLockRepository`, `MariaProductLockRepository`, `ChangeNotifyingProductLockRepository`, `ProductLockRules`), `Components/Pages/Home.razor` (editorul, heartbeat, insigne), `Components/Pages/ProductMovements.razor` (pagina produsului în consultare), `Components/Shared/ForceUnlockDialog.razor`, `wwwroot/leave-guard.js` (`blazorStocPing`), tabelul `product_locks` (SQLite, schema 8) și `product_lock` (MariaDB, creat la prima folosire).

- [x] Lock de tip lease creat la intrarea în modul de editare (butonul „Editează”, inclusiv `?edit=`), nu la consultarea produsului.
- [x] Lock-ul este identificat prin produs, utilizator, sesiune (circuitul), momentul obținerii, ultima reînnoire și momentul expirării.
- [x] Un singur editor activ per produs, prin operații atomice ale bazei de date: reînnoire de către aceeași sesiune, preluarea unui lock expirat și inserare cu cheie primară (`INSERT OR IGNORE`/`INSERT IGNORE`); 24 de cereri simultane au dat exact un câștigător.
- [x] Heartbeat la 30 s cât timp formularul este deschis (`RenewAsync`), doar dacă browserul răspunde (`blazorStocPing`): un tab închis nu mai reînnoiește, deși serverul păstrează circuitul câteva minute. Un lock eliberat de administrator sau preluat de altă sesiune nu este reluat în tăcere; editorul afișează „Blocarea editării a fost pierdută…”, iar salvarea rămâne protejată de verificarea versiunii.
- [x] Eliberare la salvare, anulare, închiderea formularului, navigare sau eliminarea paginii (`ReleaseAsync`, numai de sesiunea care îl deține).
- [x] Expirare automată după 90 s de la ultima reînnoire (1–2 minute) dacă browserul, conexiunea sau aplicația se închid; verificat prin închiderea unui tab în timpul editării.
- [x] Al doilea utilizator vede produsul în consultare (pagina produsului): „Produsul este editat de <utilizator> din <ora>. Îl poți consulta, dar nu îl poți edita până când este eliberat.”, butonul „Editează” dezactivat; în catalog, produsul are insigna „🔒 În editare de <utilizator>”. Ștergerea unui produs editat de altcineva este refuzată cu explicație.
- [x] Notificare automată a celui care așteaptă: eliberarea sau preluarea este anunțată imediat prin feed-ul din „Sincronizarea între utilizatori prin evenimente din baza de date” (evenimentul `BlocareProdus`, vizibil și clienților SignalR), iar expirarea silențioasă este detectată la cel mult 10 s (pagina produsului) sau 15 s (catalog). La eliberare pagina afișează „Produsul a fost eliberat și poate fi editat acum.”
- [x] Deblocare forțată de administrator (buton „Deblochează (administrator)” în pagina produsului, dialog `ForceUnlockDialog`), cu motiv obligatoriu și jurnalizare: acțiunea „Deblocare” (nouă în jurnal și în filtrul de operații), țintă = codul produsului, detalii = editorul anterior și ora, motiv = motivul dat, actor = administratorul. Nu este permisă altor roluri; fără lock nu se scrie nimic în jurnal.
- [x] Timpul UTC al expirărilor este cel al bazei de date (`strftime('now')` în SQLite, `UTC_TIMESTAMP(6)` în MariaDB); timpul rămas vine din baza de date, nu din ceasul aplicației.
- [x] Nicio tranzacție SQL sau blocare de rând nu rămâne deschisă cât timp formularul este activ: fiecare operație este o singură instrucțiune atomică pe o conexiune scurtă.
- [x] Verificarea versiunii produsului rămâne protecția finală, inclusiv după expirarea sau preluarea lock-ului.
- Neverificat: MariaDB pe un server real; două calculatoare diferite (testat cu două tab-uri, cu același cont); un al doilea utilizator cu cont distinct în browser.
- Corectură legată: scripturile proprii sunt acum încărcate prin `@Assets[...]` (nume cu amprentă), deoarece browserul păstra o versiune veche a `leave-guard.js`.

## Finalizat la 25.09.2026 14:08 — Avertizare la părăsirea unei pagini de editare cu modificări nesalvate

**Data și ora implementării:** 25.09.2026 14:08 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/UnsavedChanges.cs` (`UnsavedChanges`, `EditTracker`, `FormSnapshot`), `Components/Shared/UnsavedChangesTracker.razor`, `UnsavedChangesHost.razor`, `UnsavedChangesDialog.razor`, `wwwroot/leave-guard.js` (rescris), editorii `ProductEditor`, `BeneficiaryEditor`, `ProjectEditor`, `ProjectObservationEditor`, `UserEditor`, formularele din `ProductGroups` și `ProductMovements` (adăugarea și dialogul de editare a mișcării), paginile cu editori (`UnsavedChangesHost`), `Program.cs`.

- [x] Serviciu scoped `UnsavedChanges` în care se înregistrează fiecare editor deschis (`UnsavedChangesTracker`); avertizarea se declanșează dacă **oricare** editor al paginii este modificat (proiectul și observația pe aceeași pagină). Un editor este „modificat” când valorile diferă de cele de la deschidere (instantaneu păstrat ca hash, prin reflecție pe modelul de intrare; motivarea nu contează, imaginea aleasă și parola introdusă contează); revenirea la valoarea inițială înseamnă „nemodificat”; după o salvare reușită valorile salvate devin starea nemodificată (`Rebase`).
- [x] Verificarea este făcută pe server, în momentul acțiunii: valoarea unui câmp ajunge pe server la pierderea focusului, iar mesajul acesta precede întotdeauna apăsarea care urmează.
- [x] Interceptarea: script (`leave-guard.js`) pentru clicurile pe linkuri interne, antetele meniului produselor și „Deconectare”; pentru Înapoi/Înainte ale browserului (routerul aplicației nu le raportează către `NavigationLock`) scriptul etichetează intrările din istoric, anulează mutarea și o repetă după răspuns; `NavigationLock` rămâne pentru navigările din cod și pentru confirmarea nativă la închiderea sau reîncărcarea tabului. Înlocuiește protecția veche din meniul produselor și dialogul „Părăsești adăugarea produsului?”.
- [x] Popup cu titlul „Editarea nu a fost finalizată” (editare) sau „Adăugarea nu a fost finalizată” (formular de adăugare), cu „Înapoi la editare”/„Înapoi la adăugare” (verde, primește focusul, Escape are același efect; focusul revine la elementul de unde a pornit acțiunea) și „Părăsește editarea fără salvarea modificărilor”/„Părăsește adăugarea fără salvare” (roșu), care închide editorul (eliberând lock-ul produsului) și execută acțiunea inițială (navigare, mutare în istoric, deschiderea dialogului de deconectare). Textul diferă între adăugare și editare (cerere ulterioară a utilizatorului).
- [x] „Anulează”/„Închide” de pe un formular modificat afișează același popup (numai pentru acel formular); pe un formular nemodificat închid direct. Selectarea aceleiași categorii în meniul produselor nu întreabă.
- [x] Teste automate: 21 de verificări noi (nemodificat, modificat, revenire, „Înapoi”, „Părăsește” doar pentru formularele modificate, `Rebase`, dispariția trackerului, motivarea ignorată, imagine și parolă, textul de adăugare/editare).
- [x] Verificat în browser (modul demonstrativ, contul administrator, `http://127.0.0.1:5082`): formular nemodificat părăsit fără întrebare; formular modificat + link din meniu, antet de categorie, „Închide”/„Anulează”, „Deconectare”, Înapoi al browserului și mișcare de stoc (formularul de adăugare); „Înapoi la editare” păstrează valorile; „Părăsește” navighează (cu reîncărcare pentru antetul de categorie), deschide dialogul de deconectare sau revine la pagina anterioară; lock-ul produsului editat este eliberat; textele diferă între adăugare (beneficiar) și editare (produs).
- Neverificat (trecut în `docs/TESTE_RAMASE.md`): dialogul nativ la închiderea/reîncărcarea tabului cu un formular modificat, dialogul de editare a mișcării și formularele din pagina proiectului și a observației în browser, tastatura și cititorul de ecran, Înainte al browserului, mutări de mai mulți pași în istoric.

## Finalizat la 25.09.2026 14:13 — Datele calendaristice afișate ca dd.mm.yyyy

**Data și ora implementării:** 25.09.2026 14:13 (ora locală).

Cerere a utilizatorului: regulă de dezvoltare și corectarea interfeței. Regula este scrisă în `CLAUDE.md`, `AGENTS.md`, în „Observații pentru etapa de implementare” și în `README.md`. `Services/StockMovements.cs` (`DisplayDate` = `dd.MM.yyyy`, `LegacyDate` = `dd-MM-yyyy` numai pentru coloana `io_data`, `NormalizeDisplayDates`), `Services/MariaStockMovementRepository.cs`, `Services/ProductLocks.cs`, `Components/Shared/PickOnlyDate.razor` (text și indiciu `zz.ll.aaaa`), `Components/Pages/ProductMovements.razor`, `Components/Pages/Audit.razor`.

- [x] Câmpul „Data mișcării”, tabelul intrărilor/ieșirilor, titlurile și dialogurile mișcărilor, rezumatele de salvare, istoricul unei mișcări și textele din jurnal arată `dd.mm.yyyy` (cu oră: `dd.mm.yyyy hh:mm`); paginile care aveau deja `dd.MM.yyyy` (jurnal, proiecte, observații) au rămas neschimbate.
- [x] Scrierea în MariaDB rămâne compatibilă cu aplicația existentă: `io_data` păstrează textul `dd-MM-yyyy`; SQLite păstrează `yyyy-MM-dd`.
- [x] Textele din jurnal și din istoricul mișcărilor scrise înainte de regulă (`dd-MM-yyyy`) sunt afișate `dd.MM.yyyy` (doar date calendaristice reale; restul textului rămâne neschimbat).
- [x] Testat: `BlazorStoc.Checks` 410 (formatul, formatul legacy și normalizarea textelor vechi); browser pe 5082: câmpul de dată, tabelul mișcărilor și jurnalul fără date în alt format.
- Neverificat: calendarul nativ al browserului (fereastra de alegere) folosește formatul limbii browserului, care nu poate fi controlat din aplicație; documentul PDF al inventarului urmează aceeași regulă (`zz.ll.aaaa`, trecută în taskul activ).

## Finalizat la 25.09.2026 14:15 — Alinierea antetului „Data” din tabelul de intrări/ieșiri

**Data și ora implementării:** 25.09.2026 14:15 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `wwwroot/app.css` (regula `.movement-table .sort-header`).

- [x] Cauza: butonul de sortare al antetului „DATA” folosea stilul comun `.sort-header` (`justify-content:flex-end`, lățime 100%), gândit pentru coloana numerică din catalog, deci textul antetului era împins spre marginea dreaptă a coloanei, nealiniat cu datele. Pentru tabelul mișcărilor butonul începe acum unde încep datele (`justify-content:flex-start; width:auto`); catalogul nu este afectat.
- [x] Măsurat în browser (5082): la desktop, 375 și 768 px începutul textului „DATA ▲” coincide cu începutul datelor (323 / 71 / 71 px); antetele „INTRARE/IEȘIRE” și „DESCRIERE” încep la aceeași margine stângă cu celulele, „NUMĂR BUCĂȚI” are marginea dreaptă comună cu numerele; nicio depășire orizontală a paginii.
- Neverificat: temă închisă (aplicația are o singură temă); antetul „Beneficiar/Proiect” este tratat în taskul lui.

## Finalizat la 25.09.2026 14:23 — Mesaje exclusiv în limba română

**Data și ora implementării:** 25.09.2026 14:23 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `wwwroot/romanian-ui.js` (nou), `Components/App.razor`, `Pages/Account/Login.cshtml`, `Components/Routes.razor`, `Components/Pages/ProductMovements.razor`, `Program.cs` (cultura `ro-RO`, pagini de stare, mesaj la limitarea cererilor), `tests/BlazorStoc.Checks/Program.cs`.

- [x] Cauza mesajului în engleză de la o intrare cu valoare negativă: validarea nativă a browserului („Value must be greater than or equal to 1.”, în limba interfeței browserului, nu în `lang="ro"`). `romanian-ui.js` înlocuiește, prin `setCustomValidity` în evenimentul `invalid`, toate mesajele native (câmp lipsă, valoare prea mică/mare, număr invalid, lungime, format, dată cu `dd.mm.yyyy`) cu mesaje românești; mesajul personalizat se șterge la prima modificare a câmpului. Se încarcă și în pagina de autentificare. Rezultat în browser: −5 și 0 → „Valoarea trebuie să fie cel puțin 1.”, prea mare → „Valoarea poate fi cel mult 100000.”; câmp gol → mesajul serverului „Introdu o cantitate întreagă mai mare decât zero.”.
- [x] Dialogul implicit de reconectare al Blazor (texte scrise în `blazor.web.js`, în engleză) este tradus prin observarea shadow DOM-ului lui: „Reconectare la server…”, „Reconectarea a eșuat… se reîncearcă în N secunde”, „Reconectarea a eșuat. Reîncearcă sau reîncarcă pagina.”, „Reîncearcă”. Verificat cu serverul oprit.
- [x] Erorile HTTP fără pagină proprie (404, 403, 401, altele) afișează pagini românești (`UseStatusCodePages`), `Routes.razor` are `NotFound` în română, iar limitarea cererilor de autentificare (429) răspunde „Prea multe încercări. Așteaptă un minut și încearcă din nou.”. `ParsingErrorMessage` în română la câmpurile de cantitate.
- [x] Cultura aplicației este `ro-RO` pentru toate cererile și circuitele (`UseRequestLocalization`); formatele de dată rămân `dd.mm.yyyy`, iar formatele interne folosesc explicit cultura invariantă.
- [x] Inventar al textelor vizibile: validările `DataAnnotations`, excepțiile de operare, mesajele de repository, popup-urile, dialogurile și stările paginilor erau deja în română (verificat prin căutare în `Services`, `Components`, `Pages`); mesajele tehnice din jurnalul serverului rămân în engleză.
- [x] Verificare automată: `BlazorStoc.Checks` (413) scanează literalele care devin mesaje vizibile (`ErrorMessage`, `*Exception("…")`, câmpurile `error`/`notice`/…, constantele `…Message`, `errors.Add`) și eșuează dacă găsesc cuvinte englezești frecvente.
- Neverificat: mesajele native ale ferestrei calendarului și ale selectorului de fișiere (limba browserului, nu pot fi schimbate din aplicație); starea finală „Reconectarea a eșuat…” cu butonul „Reîncearcă” (testată doar funcția de traducere); lista `english` din scanare acoperă doar cuvinte frecvente.

## Finalizat la 25.09.2026 14:29 — Eticheta „Beneficiar/Proiect” în tabelul de intrări/ieșiri

**Data și ora implementării:** 25.09.2026 14:29 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Components/Pages/ProductMovements.razor` (antetul coloanei), `README.md`, `VALIDARE.md`.

- [x] Antetul coloanei din tabelul de intrări/ieșiri al unui produs (în stilul existent al antetelor, majuscule) este acum „BENEFICIAR/PROIECT”, în loc de „BENEFICIAR”. Conținutul coloanei și linkurile către beneficiar și proiect sunt neschimbate.
- [x] Locurile care numeau coloana au fost actualizate: `README.md` („Coloana „Beneficiar/Proiect””) și `VALIDARE.md` (descrierea tabelului). Nu există alte texte, etichete accesibile, export sau teste care să folosească vechea etichetă pentru această coloană; etichetele „Beneficiar” din formularul de ieșire (`RelationPicker`), din formularul proiectului, din pagina Beneficiari și din jurnal nu au fost modificate.
- [x] Verificat în browser (instanță de probă pe 127.0.0.1:5083, modul demonstrativ, pagina `/produse/1`): antetele sunt „DATA, INTRARE/IEȘIRE, NUMĂR BUCĂȚI, DESCRIERE, BENEFICIAR/PROIECT, Acțiuni”, fiecare pe un singur rând (înălțime 40 px). La 768 px pagina nu are depășire orizontală (753 px pe 768); la 375 px pagina are 375 px, iar tabelul (644 px) defilează în propriul container, ca înainte.
- [x] Build Release fără avertismente; `BlazorStoc.Checks`: 413 verificări trecute, 0 eșuate.
- Neverificat: preview-ul de pe 5082 (pornit de un alt proces) nu a putut fi repornit din sesiune și poate arăta încă antetul vechi până la repornire.

## Finalizat la 25.09.2026 14:42 — Administrarea vehiculelor (secțiunea „Vehicule”)

**Data și ora implementării:** 25.09.2026 14:42 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/Vehicles.cs`, `Services/SqliteVehicleRepository.cs`, `Services/MariaVehicleRepository.cs`, `Components/Pages/Vehicles.razor`, `Components/Pages/VehicleEditor.razor`, `Components/Layout/MainLayout.razor`, `Components/Pages/Audit.razor`, `Services/Archiving.cs`, `Services/ArchivePersistence.cs`, `Services/SqliteLocalStore.cs`, `Services/MariaArchiveSchema.cs`, `Services/AuditTrail.cs`, `Program.cs`, `tests/BlazorStoc.Checks/Program.cs`.

- [x] Meniul „Administrare” are intrarea „Vehicule” → `/vehicule` (evidențiată, meniul rămâne extins); pagina „Vehicule” are aceeași structură ca „Categorii și subcategorii”/„Beneficiari” (titlu, „+ Adaugă vehicul”, căutare după număr sau descriere, tabel „Număr de înmatriculare / Descriere” cu editare și ștergere), stări de încărcare, listă goală („Nu există vehicule”), căutare fără rezultat și eroare cu „Reîncearcă”, sincronizare la 15 secunde. Acces: utilizatori autentificați (aceleași drepturi ca pentru catalog), verificat și pe server.
- [x] Număr de înmatriculare obligatoriu, cu masca `AA-OOO-AAA` extinsă la cererea utilizatorului: **1–2 litere, 2–3 cifre, 3 litere** (`HD-01-FDG`, `HD-233-VDG`, `B-123-ABC`, `B-12-ABC`); `HD-1-FDG` (o cifră) este respins. Se acceptă litere mici, spații și lipsa cratimelor; forma salvată este cu majuscule și cratime (`VehiclePlate.TryNormalize`), iar formularul completează numărul la părăsirea câmpului. Descriere obligatorie, cel mult 100 de caractere, fără diacritice la salvare. Numărul este unic (cheie normalizată); un duplicat este respins cu numărul și descrierea vehiculului existent.
- [x] Adăugare directă; editare cu „Motivare modificare”, confirmare a salvării (numai câmpurile modificate), verificarea versiunii și avertizare la părăsirea formularului; ștergere în doi pași (motiv, apoi `sterge`) cu arhivare în `archive_vehicles` în aceeași tranzacție cu evenimentul de jurnal.
- [x] Jurnal: tip nou „Vehicul” (filtru „Vehicule” în pagina Jurnal), evenimente de adăugare/editare/ștergere cu utilizatorul, `Details` cu valorile inițiale și finale, ținta „#id · număr” cu link către `/vehicule?edit={id}` (nu după ștergere).
- [x] Stocare: SQLite (`vehicles`, `archive_vehicles`, schema locală versiunea 9) și MariaDB (tabela `vehicul`, creată la prima folosire, și `archive_vehicles`, versiunea schemei de arhivă 5), fără fișiere SQL de upgrade separate; complet asincron.
- [x] Teste automate (`BlazorStoc.Checks`, 464 trecute, 51 noi): masca (valide/invalide, normalizare), creare, unicitate, editare cu motiv, editare veche respinsă, jurnal (un eveniment, `Details`, `Motif`, link), creare concurentă a aceluiași număr (o singură reușită), ștergere cu arhivare și eveniment în aceeași operație, reînregistrarea unui număr arhivat, persistență după repornire, căutare.
- [x] Verificat în browser (instanță de probă pe 5083, bază SQLite separată, sesiune autentificată): meniul, starea goală, `HD-1-FDG` respins cu mesaj, `b123abc` completat ca `B-123-ABC`, adăugare, editare cu dialogul de confirmare, jurnalul (filtru și link), linkul `/vehicule?edit=1`, ștergerea în doi pași; la 375 și 768 px fără depășire orizontală a paginii.
- **Mutat în taskul „Ieșire spre vehicul, vânzare generică și corecție de stoc”** (mișcările nu au încă vehicul): coloana cu numărul mișcărilor asociate și blocarea ștergerii unui vehicul cu mișcări; regula `VehicleRules.CheckDelete` există și este testată.
- Neverificat: MariaDB pe un server real (tabela `vehicul`, eroarea 1062 la creare concurentă); tastatura; ecran tactil.

## Finalizat la 25.09.2026 15:21 — Ieșire spre vehicul, vânzare generică și corecție de stoc

**Data și ora implementării:** 25.09.2026 15:21 (ora locală).

Implementat la 25 septembrie 2026 de Claude. `Services/StockMovements.cs`, `Services/SqliteStockMovementRepository.cs`, `Services/MariaStockMovementRepository.cs`, `Components/Shared/ExitDestinationPicker.razor` (nou, înlocuiește `RelationPicker.razor`), `Components/Pages/ProductMovements.razor`, `Components/Pages/Home.razor`, `Components/Pages/ProductEditor.razor`, `Components/Pages/Vehicles.razor`, `Services/SqliteLocalStore.cs`, `Services/MariaArchiveSchema.cs`, `Services/ArchivePersistence.cs`, `Services/SqliteVehicleRepository.cs`, `Services/MariaVehicleRepository.cs`, `wwwroot/app.css`, `tests/BlazorStoc.Checks/Program.cs`.

- [x] **Destinația ieșirii, obligatorie**: butoane radio „Beneficiar / Autovehicul / Vânzare generică / Corecție stoc” la adăugare și editare; nicio opțiune preselectată, salvarea fără alegere este respinsă pe client și pe server („Alege destinația ieșirii.”). „Beneficiar” păstrează beneficiarul (acum obligatoriu) și proiectul facultativ; „Autovehicul” cere un vehicul (lista din „Vehicule”, cu trimitere la pagina Vehicule dacă nu există); celelalte nu au relații; combinațiile incompatibile sunt respinse cu mesaje românești. Ieșirile vechi rămân neschimbate (fără destinație, efect −cantitate); la editarea uneia fără beneficiar destinația devine obligatorie.
- [x] **Sursa ieșirii**: „Depozit” (preselectat) sau „Mașină” cu lista vehiculelor și cantitatea disponibilă în fiecare; din mașină nu se poate scoate mai mult decât conține pentru produsul respectiv („În mașina HD-01-FDG există numai 4 bucăți din acest produs.”), verificat pe server în aceeași tranzacție; editarea sau ștergerea unei mișcări care ar lăsa o mașină cu cantitate negativă este respinsă. Transferul mașină → mașină nu se face din formular (opțiunea „Mașină” este dezactivată la „Autovehicul”, cu trimitere la pagina vehiculului) — decizie confirmată.
- [x] **Ieșirea spre autovehicul mută produsul**: efectul asupra stocului total este 0 (`StockMovementRules.Effect`); cantitatea din mașini se derivă din mișcări (fără coloană nouă): transferuri spre mașină minus ce s-a folosit din ea. Defalcarea „X în depozit, Y în vehicule” apare numai când există produse în mașini: în „Total produse în stoc” din pagina mișcărilor, în catalog (sub cantitate) și în editorul produsului; `StockMovementRules.StockBreakdown`/`StockSplit`/`WarehouseStock` (acesta din urmă pentru inventar). Stocul din depozit poate deveni negativ; ștergerea produsului rămâne blocată de stocul total (mesaj actualizat).
- [x] **Descrierea precompletată**: „Completare stoc mașină <număr> <zz.ll.aaaa>” pentru autovehicul și „Corecție stoc <zz.ll.aaaa>” pentru corecție, cu data de azi; nu suprascrie textul scris de utilizator, se actualizează la schimbarea vehiculului/opțiunii și dispare când o precompletare neschimbată nu mai e valabilă (`ApplySuggestion`).
- [x] **Tabel, jurnal, arhivă**: coloană „VEHICUL” („în <număr>” pentru destinație, „din <număr>” pentru sursă), etichetă „Vânzare generică”/„Corecție stoc” lângă beneficiar; jurnalul și istoricul „[*]” includ Destinație, Vehicul și Sursă (valori inițiale și finale); `archive_stock_movements` păstrează destinația și vehiculele. Coloana „MIȘCĂRI” în lista „Vehicule” și blocarea ștergerii unui vehicul cu mișcări (SQLite și MariaDB).
- [x] **Stocare**: SQLite — `stock_movements.destination/vehicle_id/source_vehicle_id` (`ALTER` la prima pornire, chei externe RESTRICT către `vehicles`), schema locală versiunea 10; MariaDB — coloanele `io_destinatie`, `id_vehicul`, `id_vehicul_sursa` în `io` (aplicația veche le ignoră) și tabela `vehicul`, arhiva versiunea 6; fără fișiere SQL de upgrade separate.
- [x] **Teste automate** (`BlazorStoc.Checks`, 505 trecute, 41 noi față de administrarea vehiculelor): regulile de validare, efectul pe total, precompletarea, defalcarea, transfer, folosire din mașină, respingerea peste cantitate, editarea/ștergerea care ar face mașina negativă, schimbarea destinației, jurnalul, arhiva, ieșiri vechi, sesiuni concurente, repornirea.
- [x] **Browser** (instanță de probă pe 5084, bază separată, sesiune autentificată): radio-urile și lipsa preselecției, mesajul fără destinație, precompletările cu data de azi, transfer de 4 buc. (totalul rămâne 12, „8 în depozit, 4 în vehicule”), folosire din mașină (mesajul peste cantitate, apoi 3 buc.), editarea unui transfer refuzată de server, catalogul cu defalcare numai pentru produsul cu piese în mașini, lista „Vehicule” cu numărul mișcărilor; la 375 și 768 px fără depășire orizontală a paginii.
- [x] **Abateri față de descriere**: linkul către vehicul din tabelul mișcărilor duce deocamdată la editorul din listă (pagina vehiculului vine în taskul următor); coloana „MIȘCĂRI” arată numărul, fără link, până la pagina vehiculului.
- Neverificat: MariaDB pe un server real (coloanele noi din `io`, interogările cu `vehicul`, arhiva), ecran tactil, tastatură.

## Finalizat la 25.09.2026 15:41 - Pagina vehiculului: materiale si echipamente, restituire si mutare

**Data si ora implementarii:** 25.09.2026 15:41 (ora locala).

Implementat la 25 septembrie 2026 de Claude. `Components/Pages/VehiclePage.razor` (nou), `Components/Pages/VehicleEquipmentPage.razor` (nou), `Components/Shared/VehicleTransferDialog.razor` (nou), `Components/Pages/Vehicles.razor`, `Components/Pages/ProductMovements.razor`, `Services/StockMovements.cs`, `Services/SqliteStockMovementRepository.cs`, `Services/MariaStockMovementRepository.cs`, `Services/Vehicles.cs`, `Services/SqliteVehicleRepository.cs`, `Services/MariaVehicleRepository.cs`, `Services/AuditTrail.cs`, `tests/BlazorStoc.Checks/Program.cs`.

- [x] Pagina `/vehicule/{id}` urmeaza logica paginii proiectului, fara observatii si fara fisiere: link "Vehicule", antet cu numarul si descrierea, butoanele "Editeaza" (acelasi formular, cu motivare si confirmare) si "Sterge vehiculul" (fluxul in doi pasi; blocat cat timp exista miscari), sectiunea "Continutul vehiculului" cu o singura intrare, "Materiale si echipamente" (cu sumarul "N repere, M buc."). Stari de incarcare, vehicul inexistent si eroare cu "Reincearca"; reimprospatare la miscarile altor sesiuni (evenimentele de produs) si periodica la 15 secunde; un formular sau dialog deschis nu este inlocuit.
- [x] Numarul de inmatriculare din lista "Vehicule" este link catre pagina; coloana "MISCARI" este link (cand are miscari); linkurile din tabelul "Intrari/iesiri" si din Jurnal (`VehicleNavigation.PageUrl`) duc acum la pagina vehiculului.
- [x] Pagina `/vehicule/{id}/echipamente`: tabel COD PRODUS / CANTITATE (cod ca link catre produs, doar cantitati mai mari ca 0, ordonate dupa cod), subsol cu numarul de repere si de bucati, stare goala, actiuni pe fiecare reper: "Restituie in depozit" si "Muta in alta masina" (dialog cu cantitatea, implicit toata, editabila intre 1 si cantitatea din masina, si lista celorlalte vehicule; fara alta masina actiunea explica situatia).
- [x] "Restituie tot in depozit" si "Muta tot in alta masina": se aplica tuturor reperelor cu toata cantitatea, dupa o confirmare cu numarul de repere si de bucati; operatia este atomica (`TransferFromVehicleAsync`, una sau nici o miscare), iar cantitatile se verifica in aceeasi tranzactie.
- [x] Fiecare restituire sau mutare este o miscare de stoc a produsului (data de azi, operatorul, descriere "Restituire in depozit din masina X <data>" / "Mutare din masina X in masina Y <data>"), jurnalizata si arhivabila; destinatie noua `ExitDestination.WarehouseReturn` (efect 0 asupra totalului), mutarea foloseste destinatia autovehicul cu sursa masina. Stocul total ramane neschimbat; depozitul si masinile se actualizeaza; cantitatea dintr-o masina nu poate deveni negativa. Mutarile si restituirile se pot edita din pagina produsului (destinatia si sursa raman, cantitatea, data si descrierea se pot schimba) si sterge, cu aceeasi regula.
- [x] Teste automate (`BlazorStoc.Checks`, 523 trecute, 18 noi): restituire si mutare partiala si totala, respingerea peste cantitate, aceeasi masina, masina sau produs inexistente, operatie atomica cu o linie imposibila, masina goala, editarea si stergerea miscarilor, doua sesiuni concurente, jurnalul, ruta paginii.
- [x] Verificat in browser (instanta de proba, baza separata, sesiune autentificata): pagina vehiculului cu o singura intrare, tabelul de echipamente, restituire partiala (1 din 4), mutare de 2 buc. cu alegerea masinii si mesajele fara masina, "Muta tot" si "Restituie tot", starea goala, coloana VEHICUL si etichetele in tabelul miscarilor, editarea unei mutari (dialog fara alegerea destinatiei), stergerea vehiculului blocata; la 375 si 768 px fara depasire orizontala a paginii.
- Neverificat: MariaDB pe un server real (vezi `docs/TESTE_RAMASE.md`, A11); tastatura; ecran tactil.

## Finalizat la 28.09.2026 10:25 - Situatia de inventar (pagina "Inventar" si fisierul PDF)

**Data si ora implementarii:** 28.09.2026 10:25 (ora locala).

Implementat la 28 septembrie 2026 de Claude. `Components/Pages/Inventory.razor` (nou), `Components/Pages/InventoryPickup.razor` (nou), `Components/Layout/MainLayout.razor`, `Components/Pages/Dashboard.razor`, `Components/Pages/Audit.razor`, `Services/Inventory.cs` (nou), `Services/InventorySelectionState.cs` (nou), `Services/InventoryPdfWriter.cs` (nou), `Services/AuditTrail.cs`, `Program.cs`, `Assets/Fonts/PTSans-Regular.ttf`, `Assets/Fonts/PTSans-Bold.ttf`, `Assets/Fonts/OFL.txt` (noi, incorporate ca resurse), `wwwroot/checkbox-indeterminate.js`, `wwwroot/inventory-download.js` (noi), `BlazorStoc.csproj`, `tests/BlazorStoc.Checks/Program.cs`, `tests/BlazorStoc.Checks/PdfTextExtractor.cs` (nou, folosit numai in teste).

- [x] Pagina `/inventar` (meniu "Inventar" intre "Produse si stocuri" si "Administrare", cu intrarile "Generare situatie inventar" si "Preluare inventar"; card nou in dashboard) arata categoriile si subcategoriile ca in "Categorii si subcategorii" (restranse implicit), fara butoane de adaugare/editare, cu stari de incarcare, catalog gol si eroare cu "Reincearca".
- [x] Fiecare categorie si subcategorie are o caseta de selectare; selectarea unei categorii se propaga la subcategoriile ei, cu stare `indeterminate` cand doar o parte sunt selectate (JS `wwwroot/checkbox-indeterminate.js`, fiindca proprietatea `indeterminate` nu are echivalent in atribute HTML); "Selecteaza toate categoriile" foloseste aceeasi logica; "Elimina din situatia de inventar produsele cu stoc 0" exclude produsele cu stoc exact 0 (stocul negativ ramane); logica selectiei este clasa independenta `InventorySelectionState`, testata separat de UI. O categorie fara subcategorii este intotdeauna debifata (nu are ce selecta).
- [x] Butonul "Genereaza situatia de inventar" este dezactivat fara selectie ("Se genereaza..." pe durata generarii) si produce un PDF (`Inventar_aaaa-ll-zz_oomm.pdf`) descarcat direct din browser prin `DotNetStreamReference` si `wwwroot/inventory-download.js` (Blob + link de descarcare), fara sa fie pastrat pe server si fara endpoint HTTP nou de protejat impotriva CSRF (actiunea ruleaza in acelasi circuit Blazor Server autentificat). Selectia este revalidata pe server fata de catalogul curent (`InventoryReportBuilder`/`InventoryRules.Validated`); o selectie neconcordanta sau ramasa fara produse dupa filtrul de stoc 0 este semnalata, fara PDF partial si fara jurnalizare.
- [x] PDF: titlu "Inventar" si "Generat la: zz.ll.aaaa oo:mm" (ora serverului), apoi pentru fiecare categorie/subcategorie selectata un tabel "Cod produs | Valoare stoc | Valoare reala" (grid complet pe toate laturile celulelor, pentru usurinta numararii fizice), cu "Valoare stoc" egala cu valoarea "in depozit" (`StockMovementRules.WarehouseStock`, exclude cantitatea din vehicule). Un rand cu stoc negativ este scris integral cu rosu. Numele categoriilor/subcategoriilor sunt bold 14, restul normal 12. Antetul tabelului se repeta pe paginile urmatoare; o eticheta de categorie/subcategorie nu ramane singura la finalul unei pagini; coduri lungi se impart pe mai multe randuri; subsol "Pagina x din y".
- [x] Biblioteca PDF: **PDFsharp 6.2.1** (licenta MIT); decizie: versiunea noua, cross-platform, nu include inca MigraDoc, asa ca raportul e desenat direct cu `XGraphics` (fara MigraDoc/QuestPDF). Font: **PT Sans** (SIL Open Font License 1.1, variantele statice Regular/Bold, alese pentru ca au diacriticele romanesti corecte - virgula, nu sedila - verificat direct din tabela `cmap` inainte de alegere), incorporat ca resursa in `Assets/Fonts/` printr-un `IFontResolver` propriu.
- [x] Generarea reusita se jurnalizeaza (`AuditEntities.Inventory` = "Inventar", `AuditActions.Generate` = "Generare", adaugate si in filtrele paginii Jurnal) cu rezumatul cererii (categorii/subcategorii selectate, optiunea stoc 0, produse in situatie si cate au stoc negativ, numele fisierului), fara lista produselor, fara identificator de obiect si fara link; scrisa numai dupa construirea cu succes a PDF-ului. Decizie: daca jurnalizarea insasi esueaza dupa aceea, eroarea e doar logata pe server, iar utilizatorul primeste totusi fisierul.
- [x] Teste automate (`BlazorStoc.Checks`, 559 trecute, 36 noi): logica selectiei (propagare, `indeterminate`, "toate", categorie fara subcategorii), construirea situatiei (doar selectia facuta, eliminarea stocului 0, omiterea sectiunilor goale, ordinea, stocul din `WarehouseStock` cu excluderea vehiculelor, selectie invalida sau goala respinsa), generarea PDF-ului (semnatura `%PDF`, text extras printr-un extractor de test propriu care decodeaza CMap-ul `ToUnicode` al fontului incorporat, cod cu diacritice, fontul bold 14/normal 12, randul negativ rosu, 300 de produse pe mai multe pagini cu antet repetat, subsolul "Pagina x din y") si jurnalizarea (un singur eveniment, fara date de produse).
- [x] Verificat in browser (mod demonstrativ, cont administrator.demo): meniul extins cu cele doua intrari, selectarea unei categorii intregi si `indeterminate` pe "Selecteaza toate categoriile", generarea si descarcarea PDF-ului (deschis si confirmat cu bordurile complete ale celulelor), evenimentul din Jurnal cu rezumatul corect, pagina de rezerva "Preluare inventar"; la 375 si 768 px fara depasire orizontala a paginii.
- Neverificat: deschiderea fisierului PDF in alte cititoare decat cel folosit la verificare si tiparirea fizica; MariaDB pe un server real (`GetQuantitiesInVehiclesAsync` in modul MariaDB, deja acoperit generic de taskurile anterioare); ecran tactil.

## Finalizat la 28.09.2026 12:12 - Preluare inventar (OCR pe formularul de service scanat)

**Data si ora implementarii:** 28.09.2026 12:12 (ora locala).

Implementat la 28 septembrie 2026 de Claude. `Components/Pages/InventoryPickup.razor` (rescrisa), `Services/InventoryPickupOcr.cs` (nou), `Services/InventoryPickup.cs` (nou), `Services/InventoryPdfLayout.cs` (nou, geometrie de coloane extrasa din `InventoryPdfWriter.cs` si refolosita, fara sa schimbe PDF-ul generat), `Program.cs`, `BlazorStoc.csproj`, `wwwroot/app.css`, `Assets/Models/mnist-12.onnx` si `Assets/Models/LICENSE-onnx-models.txt` (noi, incorporate ca resursa), `Assets/Tessdata/eng.traineddata` si `Assets/Tessdata/LICENSE.txt` (noi, continut copiat la output), `tests/BlazorStoc.Checks/Program.cs`, `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`, `tests/BlazorStoc.Checks/Fixtures/inventar-proba.pdf` (nou, scanare reala furnizata de utilizator, folosita numai in teste).

- [x] Pagina `/inventar/preluare` are butonul "Preia inventar" (incarcare fisier PDF), acceptand exclusiv formatul generat de aplicatie la `/inventar`, maximum 20 MB si 50 de pagini; incarcarea unui nou fisier cand exista selectii nebifate netrimise cere confirmare explicita (popup dedicat) inainte sa inlocuiasca lista.
- [x] OCR local, fara Docker si fara serviciu extern: rasterizare PDF -> imagine cu **PDFtoImage** (PDFium+SkiaSharp) la 300 dpi; gasirea liniilor orizontale ale tabelului prin densitatea de cerneala pe fiecare rand de pixeli, apoi recalibrarea coloanelor (`InventoryPickupOcrService.CalibrateColumns`) fata de marginile efectiv gasite in scanare, nu doar fata de geometria PDF-ului (un scan real "aluneca" fata de coordonatele exacte - confirmat pe formularul de proba, unde tiparirea a redus latimea utila cu cca. 4%); "Cod produs" citit cu **Tesseract** (motor nou per celula: motorul refolosit intre celule a produs text corupt la testare); "Valoare reala" segmentata pe cifre cu **OpenCvSharp** (componente conexe) si fiecare cifra clasificata cu modelul **MNIST din ONNX Model Zoo** (licenta MIT) prin **ONNX Runtime**.
- [x] Randurile necompletate ("neinventariate") nu genereaza nicio modificare si nu apar in lista; randurile pe care OCR nu le-a putut citi cu certitudine (cifre nesegmentate) apar totusi in lista principala, marcate "de verificat", cu valoarea editabila - niciun rand cu cerneala prezenta nu este eliminat tacut.
- [x] Lista de diferente e grupata pe categorie/subcategorie (potrivire cu catalogul exacta, apoi aproximativa prin distanta Levenshtein pentru o mica greseala OCR, fara ambiguitate), cu checkbox NEbifat implicit pentru toate produsele; produsele cu codul citit negasit in catalog apar separat, informativ, fara checkbox; produsele cu valoarea reala identica stocului curent nu genereaza rand (exceptie: randurile nesigure raman vizibile oricum).
- [x] Butonul "Trimite modificari in stoc" (activ doar cu o selectie) deschide un popup cu modificarile (cod produs, modificare stoc) grupate pe categorie/subcategorie; confirmarea aplica fiecare diferenta ca miscare de stoc obisnuita, fara tip nou - Intrare pentru surplus, Iesire cu `ExitDestination.StockCorrection` existent pentru lipsa - jurnalizata automat de acelasi mecanism de audit ca restul miscarilor (fara cod de jurnalizare nou). O eroare la o linie nu opreste aplicarea celorlalte; rezultatul arata explicit reusitele si esecurile.
- [x] Teste automate (`BlazorStoc.Checks`, 578 trecute, 19 noi): 9 ruleaza pipeline-ul OCR real pe o scanare autentica furnizata de utilizator (formular tiparit, completat de mana, scanat cu un Kyocera SKM_C3320i, pastrat ca fixture) si verifica valorile citite corect si ca niciun rand nu e eliminat tacut la incertitudine; restul testeaza potrivirea cu catalogul, excluderea randurilor fara diferenta si aplicarea miscarilor (surplus/lipsa, esec partial raportat), cu repository-uri false, independent de OCR.
- [x] Verificat in browser (preview separat pe portul 5083, acelasi cont si aceeasi baza SQLite ca preview-ul principal): incarcarea fisierului real, lista grupata cu diferentele corecte, badge-ul "de verificat", corectarea unei valori gresite cu recalcularea instanta a diferentei, selectarea a doua produse, popup-ul de confirmare cu gruparea corecta, aplicarea ("2 produse au fost actualizate in stoc", randurile aplicate disparute din lista), evenimentele din Jurnal generate automat cu descrierea si destinatia asteptate, iar stocul din depozit al produsului actualizat corect pe pagina lui. Verificat si la 768 px, fara derulare orizontala a paginii.
- Decizie de proiectare: geometria coloanelor tabelului (`InventoryPdfLayout.ComputeColumns`) a fost extrasa din `InventoryPdfWriter` si refolosita de OCR, ca sa nu poata diverge intre desenare si citire; PDF-ul generat ramane byte-identic (testele existente pentru situatia de inventar au trecut nemodificate).
- Limitare cunoscuta: modelul MNIST preinstruit, generic, poate confunda o cifra scrisa intr-un stil neobisnuit (de exemplu un "8" scris intr-o singura bucla) cu alta cifra, uneori cu incredere falsa (fara sa semnaleze randul nesigur) - confirmat pe formularul de proba. Corectia manuala a valorii, inainte de bifare, ramane singura plasa de siguranta; recalibrarea/reantrenarea modelului pe mostre reale este o imbunatatire ulterioara neblocanta, detaliata in `docs/TESTE_RAMASE.md` (grupa H).
- Neverificat: incarcarea fisierului prin automatizarea browserului (fereastra nativa de selectare, motiv M3 - a facut-o utilizatorul); alte scanere/rezolutii decat cel folosit la proba; MariaDB pe un server real; alte browsere si ecran tactil pentru incarcare; suportul Linux al `OpenCvSharp4`/`Tesseract` pentru o eventuala instalare NAS (in afara fazei curente). Detalii in `docs/TESTE_RAMASE.md` (grupa H).

## Finalizat la 28.09.2026 14:39 - Normalizarea diacriticelor in datele existente

**Data si ora implementarii:** 28.09.2026 14:39 (ora locala).

Implementat la 28 septembrie 2026 de Claude. `Services/Products.cs`, `Services/SqliteLocalStore.cs`, `tests/BlazorStoc.Checks/Program.cs`.

- [x] Regula "eliminarea diacriticelor la salvare" era deja implementata pentru toate campurile relevante (cod produs/denumire, descriere, categorie, subcategorie, beneficiar, CUI, utilizator, vehicul, proiect, observatie, descrierea miscarii de stoc si motivarile), consecvent pe SQLite si MariaDB, prin metodele partajate `*Input.Validated()`/`ChangeReasonRules.Normalize` - verificat prin investigatie punctuala, fara sa fie nevoie de modificari la aceste fluxuri.
- [x] Gaura reala gasita: datele demonstrative initiale (`DemoProductRepository.InitialProducts()`, folosite direct de `SqliteLocalStore.SeedIfNeededAsync` la prima creare a bazei) ocoleau normalizarea, fiind inserate direct in baza cu diacritice din literalii C#. Literalii au fost corectati (diacriticele eliminate; simbolurile care nu sunt diacritice - "×", "·", "–" - raman neschimbate).
- [x] Adaugata o migrare unica in `SqliteLocalStore.cs` (`NormalizeExistingDiacriticsAsync`, marcaj in `app_metadata`) care normalizeaza textul existent din `products` (nume, descriere), `categories`/`subcategories` (nume), `beneficiaries` (nume, CUI), `web_users` (nume afisat), `vehicles` (descriere), `projects` (nume, observatii), `project_observations` (nume, continut) si `stock_movements` (descriere) pentru orice baza care avea deja diacritice dinainte de regula. Coloanele `normalized_*` (cheie de unicitate) nu au fost atinse, fiindca `TextNormalization.UniquenessKey` ignora deja diacriticele. Jurnalul de audit si tabelele `archive_*` raman neschimbate, ca inregistrare istorica.
- [x] Teste automate actualizate (`BlazorStoc.Checks`, 578 trecute): 3 verificari care depindeau de textul cu diacritice al datelor demonstrative au fost adaptate la noile valori normalizate ("mandrina", "Masurare"); restul suitei (inclusiv testele care folosesc diacritice deliberat, pentru randarea fontului PDF sau normalizarea la salvare) au trecut nemodificate.
- [x] Verificat direct pe baza locala persistenta (`data/blazorstoc-local.db`, folosita de preview-ul de pe portul 5083): interogare directa inainte si dupa repornire a confirmat eliminarea completa a diacriticelor din toate coloanele vizate (7 produse, 2 categorii, 6 subcategorii afectate), fara alterarea cantitatilor, versiunilor sau cheilor de unicitate; verificat si vizual in pagina `/produse`.
- Neverificat: o baza MariaDB reala cu date vechi, dinainte de regula (motiv M1); nu s-a adaugat o migrare echivalenta pentru MariaDB, fiindca fiecare scriere prin repository-urile Maria normalizeaza deja la fel ca SQLite si nu exista un mecanism de seed controlat de aplicatie pentru acel motor.

## Finalizat la 28.09.2026 21:32 - Integrare MariaDB reala si comutarea definitiva de dezvoltare

**Data si ora implementarii:** 28.09.2026 21:32 (ora locala), la finalul unui efort pe mai multe cicluri (subtaskurile 2.1-2.13, incepute mai devreme in aceeasi zi).

Baza MariaDB locala permanenta a fost creata si populata printr-o migrare punctuala din SQLite (vezi `docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md`): server MariaDB 11.4.13 pe `127.0.0.1:3307`, baza `BlazorStoc`, 27 de tabele InnoDB, 229 de inregistrari initiale, chei externe, constrangeri CHECK si 18 triggere. Taskul nu a recreat baza si nu a repetat importul; a rescris integral cele 6 repository-uri Maria ale aplicatiei pe schema reala migrata si a comutat definitiv dezvoltarea de la SQLite la MariaDB ca sursa de adevar.

**Ce s-a implementat, pe subtaskuri:**
- [x] **2.1 Confirmare conectare**: conectare TLS confirmata cu contul `blazorstoc_dev` (cifru `TLS_AES_256_GCM_SHA384`); toate cele 27 de tabele, 229 de randuri si 18 triggere verificate identic cu documentul de predare, fara nicio scriere. Parola citita din `local-secrets\application-connection.private.json` (fisier privat, ignorat de Git), niciodata in cod/mesaje/Git.
- [x] **2.2 Configuratia aplicatiei**: `Program.cs`, `Services/DatabaseConnections.cs`, `appsettings.json` revizuite (port `3307`, utilizator `blazorstoc_dev`, `CharacterSet=utf8mb4`); incarcare optionala a configuratiei private prin `Database:PrivateConfigPath`; collation `utf8mb4_nopad_bin` confirmata.
- [x] **2.3 Produse, categorii, subcategorii, beneficiari**: `Services/MariaProductRepository.Crud.cs`, `Services/MariaProductGroups.cs`, `Services/MariaBeneficiaryRepository.cs` rescrise pe tabelele reale. Verificat real: CRUD complet, unicitate, audit+evenimente.
- [x] **2.4 Miscari de stoc**: `Services/MariaStockMovementRepository.cs` rescris integral pe `stock_movements`/`stock_movement_history` (cel mai mare fisier al taskului); identificatori, sursa/destinatie, vehicule, proiecte, blocare `FOR UPDATE` pentru garda de stoc negativ. Verificat real: intrare, iesire spre vehicul (defalcare stoc), garda de stoc negativ la restituire din vehicul.
- [x] **2.5 Autentificare**: `Services/MariaUserRepository.cs` rescris pe `web_users`, acelasi `PasswordHasher<object>` (PBKDF2) ca SQLite. Verificat real: autentificare, creare, nume duplicat respins, stergere.
- [x] **2.6 Audit si arhivare**: `MariaAuditTrail`, `MariaArchiveSchema`, partea Maria din `Services/ArchivePersistence.cs` adaptate; toate coloanele `_utc` (text, niciodata DATETIME nativ) citite/scrise printr-un helper nou comun, `Services/MariaTimeText.cs`, in acelasi format folosit de cele 18 triggere. `MariaArchiveSchema.InitializeAsync` doar verifica (SELECT) ca tabelele exista, fara niciun DDL (contul aplicatiei nu are drepturi de schema).
- [x] **2.7 Proiecte, vehicule, imagini, atasamente**: directoarele locale (imagini, fisiere proiect, arhiva, chei Data Protection) folosesc `MariaAssetPaths` cu radacina configurabila `Database:MariaAssetsRoot`.
- [x] **2.8 Blocari si sincronizare**: `MariaProductLockRepository` pe `product_locks`/`products`; `MariaChangeEventSource` pe aceleasi nume reale de tabele ca SQLite, doar verificare (SELECT), fara sa creeze niciodata triggere.
- [x] **2.9 Initializare, migrari, cont operational**: cont dedicat `blazorstoc_migrator` creat (drepturi DDL limitate strict la schema `BlazorStoc`, fara SELECT/INSERT/UPDATE/DELETE, fara drepturi globale - verificat direct). `blazorstoc_dev` (contul aplicatiei) ramane fara DDL, deci nu poate opri conexiuni straine sau schimba schema.
- [x] **2.10 Identitatea operatorului**: `Database:ApplicationUserId` si dependenta de o tabela `user` eliminate din toate cele 6 repository-uri Maria; identitatea foloseste username text prin `IAccessControl`/`IAuditTrail`, ca in SQLite.
- [x] **2.11 Verificari de integrare**: baza izolata `blazorstoc_test` creata (acelasi DDL si 18 triggere, separata de cele 229 de randuri reale), cu cont dedicat. Verificari reale rulate cu succes: produse/categorii/subcategorii, beneficiari, vehicule, blocari de produs, miscari de stoc, utilizatori, proiecte (creare, observatie, **stergere**). Trei probleme reale gasite si corectate in acest subtask: (1) garda "scrie numai in baza BlazorStoc" compara literalul fix `"BlazorStoc"` in loc de o valoare configurabila, ceea ce facea imposibila testarea pe o baza izolata - corectat cu `MariaDatabaseGuard`/`Database:ExpectedName` (implicit tot `"BlazorStoc"`, deci o instalare reala nemodificata pastreaza comportamentul vechi); (2) scriptul de verificare a miscarilor de stoc testa scenariul gresit pentru garda de stoc negativ (iesire catre vehicul, care nu poate deveni negativa, in loc de restituire din vehicul) - corectat in `tests/BlazorStoc.Checks/MariaIntegrationChecks.cs`; (3) **bug real de aplicatie (A15)**: stergerea unui proiect/observatie pe MariaDB esua intermitent cu "Proiectul/Observatia a fost modificat(a) sau sters(a) intre timp", desi nimic nu se schimbase - vezi detaliile mai jos.
- [x] **2.12 Comutarea preview-ului**: preview pornit pe portul `5085` cu `App:DemoMode=false`, conectat la baza reala de livrare (`blazorstoc_dev`); pornire fara exceptii, `/health/live` 200, pagina 404, persistenta la restart (cheia Data Protection refolosita de pe disc). Autentificare confirmata de utilizator din doua browsere simultan.
- [x] **2.13 Comutarea definitiva de dezvoltare**: momentul opririi scrierilor in SQLite stabilit explicit la 28.09.2026 18:05 (ora Europe/Bucharest); comparatie completa (SHA-256 canonic, pe cheie primara) intre `data\blazorstoc-local.db` si MariaDB pe toate cele 27 de tabele, cu o singura diferenta reala (1 rand nou in `audit_events`, migrat manual, fara risc de coliziune); re-verificat 230/230 randuri identice dupa migrare. MariaDB (`blazorstoc_dev`, baza `BlazorStoc`) declarata sursa de adevar; SQLite ramane artefact istoric. Valoarea implicita `App:DemoMode=true` din `appsettings.json` **nu** a fost schimbata (decizie deliberata, ca sa nu schimbe comportamentul unei porniri obisnuite fara variabile de mediu explicite); parola de autentificare a preview-ului MariaDB a primit acelasi mecanism de fisier privat ca restul configuratiei (`Authentication` in `local-secrets\application-connection.private.json`).

**Bug A15 - stergerea unui proiect pe MariaDB (gasit in 2.11, corectat in acest ciclu final):** investigat pana la cauza reala prin executie reala pe `blazorstoc_test`, cu instrumentare temporara a exceptiei (capturare si afisare inainte de a fi aruncata mai departe), nu prin presupunere. Nu era, cum se banuia initial, un rollback in pasul de arhivare (`MariaArchivePersistence.InsertAsync`/`InsertAuditAsync`): exceptia reala era `ProjectOperationException` ("Proiectul/Observatia a fost modificat(a) sau sters(a) intre timp"), aruncata de `ProjectRules.CheckCurrent` cand compara obiectul "original" din memorie cu randul re-citit din baza in aceeasi tranzactie de Update/Delete. `Project`/`ProjectObservation` sunt record-uri C#, a caror egalitate implicita include `CreatedAtUtc`/`UpdatedAtUtc`. `MariaTimeText.Format` scrie aceste coloane text cu precizie de milisecunda (ca sa se potriveasca formatul deja folosit de cele 18 triggere), dar `MariaProjectRepository` construia obiectul intors catre apelant direct din `DateTime.UtcNow`, cu precizie mai mare (sub-milisecunda); la re-citire, valoarea reparsata din text nu mai era egala bit-cu-bit cu cea din memorie, intermitent - ori de cate ori componenta sub-milisecunda a lui `DateTime.UtcNow` nu era exact zero. Exceptia reala era apoi mascata de o exceptie de FK aparuta in curatarea proprie a scriptului de test (stergerea beneficiarului de test, blocata de proiectul ramas viu cand stergerea esua), ceea ce a facut investigatia initiala din 2.11 sa banuiasca gresit pasul de arhivare. **Corectat** cu un helper nou, `MariaTimeText.Now()` (in `Services/MariaTimeText.cs`: `DateTime.UtcNow` rotunjit prin acelasi `Format`/`Parse` folosit la scriere/citire), folosit in cele 4 locuri din `Services/MariaProjectRepository.cs` unde se calcula `nowUtc` (`CreateAsync`, `UpdateAsync`, `CreateObservationAsync`, `UpdateObservationAsync`). Niciun alt tip de entitate Maria (produs, beneficiar, vehicul, utilizator) nu are `CreatedAtUtc`/`UpdatedAtUtc` in record-ul sau, deci nu era afectat de acelasi bug.

**Fisiere principale (intreg efortul multi-ciclu):** `Services/MariaProductRepository.Crud.cs`, `Services/MariaProductGroups.cs`, `Services/MariaBeneficiaryRepository.cs`, `Services/MariaStockMovementRepository.cs`, `Services/MariaUserRepository.cs`, `Services/MariaProjectRepository.cs`, `Services/MariaProjectFileStore.cs`, `Services/MariaVehicleRepository.cs`, `Services/ProductLocks.cs` (partea Maria), `Services/ChangeEvents.cs` (partea Maria), `Services/ArchivePersistence.cs` (partea Maria), `Services/MariaArchiveSchema.cs`, `Services/MariaTimeText.cs` (helper comun nou: format/parsare timp text, cai de active, garda de baza de date), `Services/DatabaseConnections.cs`, `Program.cs`, `appsettings.json`, `tests/BlazorStoc.Checks/MariaIntegrationChecks.cs` (verificari de integrare reale, opt-in prin `RUN_MARIA_INTEGRATION_CHECKS=1`).

**Decizii:** cont operational separat (`blazorstoc_migrator`) pentru migrari, niciodata contul aplicatiei; toate coloanele `_utc` raman text (nu DATETIME nativ), pentru compatibilitate cu triggerele deja instalate si cu formatul de sortare `utf8mb4_nopad_bin`; garda "scrie numai in baza BlazorStoc" ramane implicit stricta pentru o instalare reala, configurabila explicit doar pentru medii de test izolate; `App:DemoMode` ramane `true` implicit.

**Verificari efectuate:** conectare TLS reala; CRUD real pe toate entitatile (produse, categorii, subcategorii, beneficiari, vehicule, miscari de stoc, utilizatori, proiecte, observatii - inclusiv stergerea proiectelor, dupa corectarea bugului A15) pe baza izolata `blazorstoc_test`; preview real pe portul `5085` conectat la baza de livrare, cu autentificare din doua browsere simultan; comparatie completa SHA-256 canonica intre SQLite si MariaDB pe toate cele 27 de tabele; suita implicita de 578 de verificari, fara regresii, rulata dupa fiecare corectare (inclusiv dupa fixul A15, 5 rulari consecutive ale sectiunii Projects, toate `PASS`).

**Neverificat/ramane deschis:** fluxurile prin browser care necesita autentificare cu parola reala raman de facut manual de utilizator (agentul nu introduce parole - vezi A14 in `docs/TESTE_RAMASE.md`); eroarea tranzitorie de incarcare a miscarilor observata o singura data pe preview (A16, nereprodusa, separata de acest task); politica de retentie/curatare a preview-urilor vechi ramase pornite pe alte porturi (5082, 5083) nu a fost stabilita in acest task. Detalii complete in `docs/PROJECT_STATE.md` si `docs/TESTE_RAMASE.md`.

## Finalizat la 28.09.2026 22:05 — Sincronizarea fisierelor de acces root MariaDB (Task 0)

**Data si ora implementarii:** 28.09.2026 22:05 (ora locala).

Dupa rotirea parolei de root a instantei locale MariaDB (efectuata la cererea utilizatorului dintr-o sesiune Claude Code care lucra de la distanta si nu avea acces la calea reala de pe disc), fisierele actualizate se aflau doar in `Livrare-DDL-MariaDB` (director partajat, dar netrackuit in git), iar copiile originale de la `C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB` ramasesera cu parola veche, pentru ca acea cale nu era vizibila din sesiunile anterioare.

- [x] Neasteptat: spre deosebire de sesiunile anterioare, calea `C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB` a devenit accesibila si scriptibila direct din sesiunea Claude Code (28.09.2026); nu s-a mai putut stabili sigur de ce (posibil o schimbare de mediu/montare intre sesiuni). Fisierele `admin.private.cnf` si `connection.private.json` din `Livrare-DDL-MariaDB` au fost copiate acolo, identice byte cu byte cu sursa.
- [x] Verificarea conectarii cu contul root a fost initial neconcludenta: singurul client MySQL gasit in mediul agentului (`mysql.exe` din `MySQL Workbench 8.0 CE`) a returnat `Access denied` atat pentru fisierul sursa, cat si pentru copie (rezultat identic pentru ambele, deci nu era o problema de copiere) — ambiguu intre parola gresita si o incompatibilitate de protocol/plugin de autentificare intre acest client MySQL 8.0 si serverul MariaDB. Rezolvat prin scrierea unui mic program C# de unica folosinta (in directorul scratchpad, nu in proiect), care foloseste `MySqlConnector` 2.6.2 - aceeasi biblioteca client folosita deja de aplicatie - citeste `admin.private.cnf` fara sa afiseze parola si incearca o conectare reala: **conectare reusita**, `SELECT 1` = 1, `server version = 11.4.13-MariaDB`. Confirma ca parola de root sincronizata este cea corecta; eroarea anterioara venea din incompatibilitatea clientului `mysql.exe` (MySQL 8.0), nu din parola.

**Fisiere principale:** `C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB\admin.private.cnf`, `...\connection.private.json` (in afara repo-ului, credentiale); program de verificare de unica folosinta in directorul scratchpad al sesiunii (nu a fost pastrat in repo).

**Decizii:** parola root nu a fost niciodata afisata in raspunsuri sau printata in consola; verificarea s-a facut printr-un program care citeste fisierul si raporteaza doar succes/esec.

**Verificari efectuate:** copiere confirmata byte-cu-byte; conectare reala confirmata cu `MySqlConnector` (aceeasi biblioteca folosita de aplicatie pentru conturile `blazorstoc_dev`/`blazorstoc_migrator`). Detalii in `docs/TESTE_RAMASE.md` (A17, mutat la "Teste efectuate").

**Neverificat/ramane deschis:** nimic; ambele verificari din Task 0 original sunt finalizate.

## Finalizat la 29.09.2026 08:52 — Fereastra separata pentru compararea formularului PDF la preluarea inventarului

**Data si ora implementarii:** 29.09.2026 08:52 (ora locala).

La cererea utilizatorului: cand un fisier PDF este incarcat in pagina de preluare inventar (`/inventar/preluare`), se deschide o fereastra noua, separata de pagina de preluare stocuri (nu un modal/overlay in pagina), care afiseaza fisierul PDF incarcat, astfel incat utilizatorul sa poata compara vizual valorile citite de sistem cu formularul fizic scanat. Fereastra este o fereastra reala de browser (`window.open`), nu un element in pagina, deci utilizatorul o poate muta si redimensiona liber, independent de fereastra principala.

- [x] Implementat in `Components/Pages/InventoryPickup.razor` (`ProcessFileAsync`): fisierul incarcat (`IBrowserFile`) este citit intr-un buffer de octeti; imediat dupa citire (inainte de trimiterea catre OCR) se apeleaza noul modul JS pentru deschiderea ferestrei, astfel incat fereastra sa apara rapid, indiferent de rezultatul citirii OCR ulterioare.
- [x] Modulul nou `wwwroot/inventory-pickup-pdf-viewer.js` (`openPdfViewer`) creeaza un `Blob` din octetii primiti printr-un `DotNetStreamReference` si deschide fereastra cu `window.open`, cu un nume fix (`blazorStocPickupPdfViewer`) astfel incat o noua incarcare reutilizeaza aceeasi fereastra (revocand URL-ul `blob:` anterior) in loc sa deschida ferestre multiple; dimensiune si pozitie calculate din ecranul disponibil (aproximativ jumatate din latime, aproape toata inaltimea, aliniata in dreapta ecranului), fara bare de instrumente/adresa (`popup=yes`).
- [x] Adaugat un buton "Redeschide formularul PDF" (vizibil dupa prima incarcare reusita, langa butonul "Preia inventar") pentru cazul in care utilizatorul inchide fereastra din greseala sau vrea sa o repozitioneze fara sa reincarce fisierul.
- [x] Daca fereastra nu poate fi deschisa (blocata de setarile pop-up ale browserului), utilizatorul primeste un mesaj explicit in pagina, cu indicatia sa permita ferestrele pop-up pentru acest site si sa foloseasca butonul de redeschidere; fluxul de OCR/preluare continua neschimbat, indiferent de rezultatul deschiderii ferestrei.
- [x] Modulul JS este eliberat (`DisposeAsync`) la parasirea paginii; fereastra si URL-ul temporar (`blob:`) sunt eliberate automat cand fereastra se inchide (`beforeunload`) sau cand o noua preluare o inlocuieste.

**Fisiere principale:** `Components/Pages/InventoryPickup.razor`, `wwwroot/inventory-pickup-pdf-viewer.js` (nou).

**Decizii:** fereastra este o fereastra reala de browser, nu un overlay/modal in pagina, ca sa fie efectiv mutabila si independenta de pagina de preluare, asa cum a cerut utilizatorul; deschiderea foloseste acelasi tipar (Blob + `DotNetStreamReference`) deja folosit in proiect pentru descarcarea PDF-ului de inventar (`wwwroot/inventory-download.js`), pentru consistenta cu restul codului.

**Verificari efectuate:** build Release fara avertismente/erori; `tests/BlazorStoc.Checks`, 609 verificari, toate `PASS` (fara regresii fata de cele 578 anterioare); modulul JS verificat direct in consola browserului real (import dinamic, apel `openPdfViewer` cu un flux simulat) - se executa fara erori. Incarcarea reala a unui fisier PDF prin `InputFile`, testata de utilizator in Brave pe previzualizarea de la `http://127.0.0.1:5082/inventar/preluare`: fereastra s-a deschis corect dupa ce utilizatorul a permis explicit ferestrele pop-up pentru site (comportament asteptat al browserului, semnalat si in pagina prin mesajul de eroare cand fereastra e blocata).

**Neverificat/ramane deschis:** comportamentul exact de redimensionare/repozitionare si de reutilizare a ferestrei la o a doua incarcare consecutiva nu a fost confirmat explicit de utilizator (doar deschiderea initiala). Detalii in `docs/TESTE_RAMASE.md`.
