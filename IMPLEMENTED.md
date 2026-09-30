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

## Finalizat la 29.09.2026 09:07 — Corectarea detectiei liniilor de tabel pe o scanare reala usor inclinata

**Data si ora implementarii:** 29.09.2026 09:07 (ora locala).

La testarea ferestrei de comparare de mai sus, utilizatorul a incercat preluarea unei scanari reale a formularului de inventar si a primit eroarea "Nu a fost gasit niciun tabel recunoscut in fisier", desi fisierul (`D:\_BlazTest\SKM_C3320i 26092908470.pdf`) contine vizibil toate cele 8 tabele completate de mana. Investigat prin instrumentare directa (harness de unica folosinta, in afara proiectului, cu `INVENTORY_OCR_DEBUG=1`), nu prin presupunere.

- [x] **Cauza reala confirmata:** `InventoryPickupOcrService.FindHorizontalLines` cauta un rand de pixeli unde cel putin 65% din latimea tabelului e cerneala, pentru a detecta o linie orizontala de tabel. Pe aceasta scanare, cel mai "plin" rand atingea doar 58%. Masurand direct imaginea: sub jumatate de grad de inclinare a colii (normal la o scanare fizica, oricat de atenta) imprastie cerneala unei linii orizontale drepte din original pe aproximativ 10 randuri de imagine dupa scanare, niciunul singur atingand pragul. Rezultatul: zero linii detectate pe intreaga pagina, deci zero tabele, desi geometria (`approxLeft`/`approxRight`, calculata din `InventoryPdfLayout.ComputeColumns`) era corecta.
- [x] **Corectie:** o dilatare verticala mica (nucleu 1x7, `LineDetectionDilationHeight`) aplicata pe o copie a imaginii binare, doar pentru acest test de detectie a liniilor (`InventoryPickupOcrService.FindHorizontalLines`); restul procesarii (detectia dividerilor verticali, segmentarea cifrelor scrise de mana) foloseste in continuare imaginea binara originala, nemodificata. Pragul de 65% ramane neschimbat, deci riscul de fals-pozitiv pe alt continut (text, zgomot) nu creste - doar toleranta la o inclinare mica a colii creste.
- [x] Mutat si linia de log de depanare (`INVENTORY_OCR_DEBUG`) inaintea intoarcerii timpurii cand se gasesc mai putin de 2 linii, ca sa se vada mereu geometria calculata, nu doar cand detectia reuseste - asta a facut posibila gasirea rapida a cauzei reale.
- [x] Fisierul real furnizat de utilizator a fost adaugat ca fixtura noua (`tests/BlazorStoc.Checks/Fixtures/inventar-proba-inclinata.pdf`), cu doua verificari noi in `tests/BlazorStoc.Checks`: toate cele 10 produse din scanare sunt gasite (erau 0 inainte de corectie), iar cel putin unul dintre ele isi citeste corect valoarea scrisa de mana.

**Fisiere principale:** `Services/InventoryPickupOcr.cs`, `tests/BlazorStoc.Checks/Program.cs`, `tests/BlazorStoc.Checks/Fixtures/inventar-proba-inclinata.pdf` (noua), `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`.

**Decizii:** corectie minima si tintita (dilatare doar pentru testul de linii, prag neschimbat) in loc de o solutie generala de indreptare/deskew a imaginii intregi, care ar fi fost o schimbare mult mai ampla pentru un risc deja documentat ca limitat (`docs/TESTE_RAMASE.md`, fost H1).

**Verificari efectuate:** cauza reala confirmata prin masurare directa a imaginii (nu presupusa); build Release 0 avertismente/erori; `tests/BlazorStoc.Checks` 611/611 `PASS` (609 anterioare + 2 noi, fara regresii, inclusiv fixtura originala `inventar-proba.pdf` neschimbata ca rezultat); pipeline-ul complet rulat direct (in afara suitei, cu acelasi serviciu) pe fisierul real al utilizatorului: toate cele 10 produse citite, cu valorile corecte pentru toate exceptand doua cazuri deja incadrate la limitarea cunoscuta a modelului de cifre (vezi mai jos).

**Neverificat/ramane deschis:** pe aceeasi scanare, doua valori scrise de mana au fost citite gresit de modelul de cifre generic (limitare deja cunoscuta si documentata, nu introdusa de aceasta corectie): "Manusi de lucru" (scris "54", citit "84", semnalat corect `Uncertain=true`) si "Nivela cu bula 60 cm" (scris "3", citit "5", **fara** semnalare de incertitudine - caz nou, adaugat la `docs/TESTE_RAMASE.md` H2). Aspectul general al altor scanere/rezolutii ramane deschis (H1, restul).

## Finalizat la 29.09.2026 09:15 — Indreptarea (deskew) reala a paginii pentru scanari cu inclinare vizibila

**Data si ora implementarii:** 29.09.2026 09:15 (ora locala).

Corectia anterioara (dilatare verticala) acoperea doar sub un grad de inclinare. Utilizatorul a furnizat o a doua scanare reala, rotita vizibil (cateva grade), care era in continuare respinsa cu "Nu a fost gasit niciun tabel recunoscut".

- [x] Adaugata indreptarea efectiva a paginii inainte de orice alta geometrie: `InventoryPickupOcrService.FindSkewDegrees` estimeaza unghiul de inclinare printr-o metoda standard (profilul de proiectie orizontala - varianta e maxima la unghiul corect, pentru ca liniile de tabel/textul dau varfuri ascutite doar cand pagina e dreapta), cautat intre -8 si +8 grade, in doi pasi (grosier din grad in grad, apoi rafinat din zecime in zecime), pe o copie miniaturizata (25%) pentru viteza.
- [x] Pagina e rotita (`Rotate`, `Cv2.WarpAffine`, fundal alb) cu unghiul gasit inainte de binarizare, detectia liniilor de tabel si calibrarea coloanelor - restul algoritmului (deja existent) ramane neschimbat, opereaza doar pe o pagina deja dreapta.
- [x] **Descoperire importanta in acest ciclu:** rotirea intregii pagini, chiar la un unghi mic (~0,3 grade, cazul deja rezolvat de dilatare), a inrautatit acuratetea cifrelor scrise de mana (interpolarea rotatiei "inmoaie" traseul subtire al cernelii, suficient sa strice segmentarea cifrelor pe conectivitate) - confirmat direct, comparand rezultatele cu/fara rotire pe acelasi fisier deja corectat. De aceea rotirea se aplica **numai** peste un prag (`MinCorrectedSkewDegrees = 0.6` grade); sub acest prag ramane activa doar dilatarea din corectia anterioara, care nu are acest efect secundar.
- [x] Fisierul real, mai vizibil rotit, furnizat de utilizator a fost adaugat ca fixtura noua (`tests/BlazorStoc.Checks/Fixtures/inventar-proba-rotita.pdf`), cu doua verificari noi.

**Fisiere principale:** `Services/InventoryPickupOcr.cs`, `tests/BlazorStoc.Checks/Program.cs`, `tests/BlazorStoc.Checks/Fixtures/inventar-proba-rotita.pdf` (noua), `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`.

**Decizii:** prag minim de rotire (0.6 grade) in loc de a roti mereu, tocmai din cauza efectului secundar gasit asupra acuratetei scrisului de mana pe unghiuri mici, deja acoperite suficient de dilatare; cautare in doi pasi (grosier apoi rafinat) pe imagine miniaturizata, pentru viteza (sub 2 secunde per fisier, nu s-a incercat o metoda mai rapida gen transformata Hough, care ar fi introdus o dependenta noua fara sa fie necesara).

**Verificari efectuate:** cauza si efectul secundar confirmate prin executie reala comparativa (cu/fara rotire, pe ambele fisiere reale ale utilizatorului), nu presupuse; build Release 0 avertismente/erori; `tests/BlazorStoc.Checks` 613/613 `PASS` (611 anterioare + 2 noi, fara regresii pe niciuna dintre cele 3 fixturi reale). Pipeline complet rulat direct pe ambele fisiere reale ale utilizatorului: toate cele 10 produse citite din fiecare.

**Neverificat/ramane deschis:** un unghi de inclinare mai mare de 8 grade, sau o pagina rotita in alt fel decat o simpla inclinare de alimentare (de exemplu intoarsa la 90/180 grade), nu sunt acoperite de aceasta cautare. Aspectul general al altor scanere/rezolutii ramane deschis (`docs/TESTE_RAMASE.md`, H1). Limitarile modelului de cifre (H2) sunt neschimbate de aceasta corectie.

## Finalizat la 29.09.2026 09:20 — Fixtura de test cu inclinare diferita pe fiecare pagina a unui PDF

**Data si ora implementarii:** 29.09.2026 09:20 (ora locala).

La cererea utilizatorului: corectia de deskew de mai sus trebuie sa functioneze independent pe fiecare pagina a unui document (o pagina poate fi mai inclinata decat alta). Codul deja facea asta corect (`ScanAsync` apeleaza `ScanPageAsync` separat pentru fiecare pagina, iar `FindSkewDegrees`/`Rotate` ruleaza in interiorul ei), dar nu exista o verificare automata care sa blocheze o eventuala regresie viitoare (de exemplu, cineva mutand estimarea unghiului o singura data la nivelul intregului document, din greseala).

- [x] Construita o fixtura noua cu doua pagini reale diferite (`tests/BlazorStoc.Checks/Fixtures/inventar-proba-multipagina.pdf`): pagina 1 = scanarea usor inclinata (~0,3 grade), pagina 2 = scanarea vizibil rotita (~2,7 grade), combinate intr-un singur PDF cu `PdfSharp.Pdf.PdfDocument.AddPage` (import de pagina, fara alta modificare a continutului).
- [x] Verificare noua in `tests/BlazorStoc.Checks`: documentul are 2 pagini, fiecare pagina isi citeste toate cele 10 produse ale ei, independent de inclinarea celeilalte pagini.

**Fisiere principale:** `tests/BlazorStoc.Checks/Fixtures/inventar-proba-multipagina.pdf` (nou), `tests/BlazorStoc.Checks/Program.cs`, `tests/BlazorStoc.Checks/BlazorStoc.Checks.csproj`.

**Decizii:** fixtura construita din cele doua scanari reale deja existente (nu un fisier sintetic nou), ca sa testeze exact aceleasi doua unghiuri reale confirmate anterior, fara sa introduca o a treia variabila necunoscuta.

**Verificari efectuate:** `tests/BlazorStoc.Checks` 616/616 `PASS` (613 anterioare + 3 noi: numarul de pagini, page 1 completa, page 2 completa); pipeline rulat si direct (harness separat), confirmand unghiurile estimate separat per pagina (0,30 si respectiv 2,70 grade) si toate cele 20 de randuri citite.

**Neverificat/ramane deschis:** neschimbat fata de corectiile de deskew de mai sus (unghiuri peste 8 grade, alte orientari, alte scanere/rezolutii, acuratetea modelului de cifre).

## Finalizat la 29.09.2026 09:39 — Populare catalog demonstrativ cu produse fictive (minimum 4 per subcategorie)

**Data si ora implementarii:** 29.09.2026 09:39 (ora locala).

La cererea utilizatorului: catalogul demonstrativ (`data/blazorstoc-local.db`, folosit de preview-ul de pe 5082) avea prea putine produse pentru teste realiste (11 produse, cate 1-2 per subcategorie). Populat cu 22 de produse fictive noi, astfel incat toate cele 8 subcategorii existente sa aiba cel putin 4 produse fiecare (33 de produse in total).

- [x] Produsele noi au fost create prin fluxul real al aplicatiei (`SqliteProductRepository.CreateAsync`, acelasi serviciu folosit de pagina de adaugare produs), nu prin INSERT direct in baza - normalizarea numelui, unicitatea globala si jurnalizarea (categorie/subcategorie/produs) au fost aplicate identic cu o adaugare facuta din interfata.
- [x] Fiecare produs nou a primit un stoc initial real printr-o miscare de intrare (`SqliteStockMovementRepository.CreateAsync`, descriere "Stoc initial (date demonstrative, produs fictiv)"), respectand regula existenta ca stocul se schimba doar prin miscari, niciodata direct.
- [x] Toate numele produselor noi contin sufixul " test" pentru a fi usor de distins vizual si de sters ulterior in bloc, daca se doreste, fara sa se confunde cu date reale introduse ulterior de utilizator.
- [x] Baza de date a fost copiata inainte de operatie (`data/manual-backups/blazorstoc-local.pre-seed.db`), iar preview-ul a fost oprit pe durata scrierii, ca sa nu existe scrieri concurente.

**Fisiere principale:** `data/blazorstoc-local.db` (date, nu cod sursa); script de unica folosinta in directorul scratchpad al sesiunii (nu a fost pastrat in proiect).

**Decizii:** produsele noi respecta exact categoriile/subcategoriile existente (nu s-au creat categorii noi); denumiri si descrieri plauzibile pentru fiecare subcategorie (fixare, protectie cap/maini, masurare distante/nivelare, strangere, gaurire, taiere), cu cantitati variate (inclusiv un produs cu stoc zero, ca sa pastreze diversitatea deja prezenta in datele existente).

**Verificari efectuate:** rulare reala a scriptului impotriva bazei folosite de preview; verificare directa (interogare) ca toate cele 8 subcategorii au acum exact 4 produse; verificat vizual in browser, pagina `/produse` (33 de produse, 5 categorii, cantitatile si descrierile corecte pentru produsele noi).

**Neverificat/ramane deschis:** nimic specific acestui task; produsele fictive nu au fost folosite inca pentru a genera o situatie de inventar multi-pagina reala (util pentru testarea ulterioara a Task 4, marirea inaltimii randurilor din PDF).


## Finalizat la 29.09.2026 13:40 — Meniul Setari (administrator) cu tab Preluare date ANAF (configurator complet)

Cerere directa a utilizatorului (fara task in TODO): intrarea "Setari" in meniul principal, vizibila numai administratorului, si pagina `/setari` pe taburi, cu primul tab "Preluare date ANAF" in care s-a implementat Prototip-ANAF: conexiune, cerere cu sablon si previzualizare, mapare extensibila, testare live, ciorna/activare/istoric/revenire. Fisiere: `Components/Pages/Settings.razor`, `Components/Shared/AnafConfigurator.razor`, `Components/Layout/MainLayout.razor`, `Services/AnafLookup.cs`, `Program.cs`, `wwwroot/app.css`, `tests/BlazorStoc.Checks/Program.cs`. Decizii: persistenta intr-un fisier JSON (`data/anaf-configuration.json`) in locul bazei; activare doar dupa test reusit al aceleiasi configuratii (amprenta in memorie); administrator obligatoriu si in pagina, si in serviciu; activarile se jurnalizeaza. Verificari: build fara avertismente, 10 verificari ANAF PASS, `/setari` anonim redirectionat la login. Neverificat: browser ca administrator, apel real ANAF, revenirea end-to-end.

## Finalizat la 29.09.2026 13:45 — Beneficiar persoana fizica / persoana juridica cu preluare din ANAF

Cerere directa a utilizatorului (fara task in TODO). Formularul de beneficiar are radiobutton Persoana fizica / Persoana juridica; PF: Nume complet, Adresa, Telefon (toate obligatorii, identitate = nume complet); PJ: CUI, Denumire, Adresa, Nr. Reg. Com., Telefon, Cod postal, Cod CAEN (obligatorii CUI, Denumire, Adresa, Telefon; identitate = CUI), buton "Preia date din ANAF" si indicator bifa verde / avertisment pentru date preluate din ANAF vs. introduse manual; toate campurile normalizate. Fisiere: `Services/Beneficiaries.cs`, `SqliteBeneficiaryRepository.cs`, `MariaBeneficiaryRepository.cs`, `SqliteLocalStore.cs`, `AnafLookup.cs` (`LookupCompanyAsync`), `Components/Pages/BeneficiaryEditor.razor`, `Beneficiaries.razor`, `BeneficiaryDetail.razor`, `wwwroot/app.css`, teste. Decizii: preluarea foloseste configuratia activa si rolul de operator de beneficiari (nu doar administrator); `anaf_verified` se recalculeaza la salvare din compararea cu datele returnate de ANAF; cheie de unicitate `PF:<nume>` pentru PF. Verificari: build fara avertismente, suita de verificari fara FAIL, browser (formular PJ/PF, preluare reala RO9178894, salvare). Neverificat: MariaDB real (tabelul necesita coloanele noi), editarea unui beneficiar existent in browser.

## Finalizat la 29.09.2026 14:30 — Migrari de schema MariaDB cu contul dedicat de migrare

Cerere directa a utilizatorului: schimbarile de structura MariaDB sa se poata aplica fara sa dam drepturi DDL contului aplicatiei. S-a folosit contul existent `blazorstoc_migrator` (Subtask 2.9). `Services/MariaSchemaMigrations.cs` (migrari idempotente + `MariaSchemaMigrator`), `DatabaseConnections.CreateMigrator`, incarcarea credentialelor din `migration-account.private.json` si aplicarea la pornire / prin `--migrate-schema` in `Program.cs`. Migrarea 1 = coloanele beneficiarilor. Decizii: fara tabel de versiuni (contul nu scrie date; `ADD COLUMN IF NOT EXISTS`); fara cont de migrare doar se logheaza lipsurile; `database/mariadb/schema-mariadb.sql` se tine in pas cu migrarile. Verificari: build fara avertismente, 2 verificari noi PASS, esec curat fara server. Neverificat: rularea pe o instanta MariaDB reala (lipseste din mediu).

## Finalizat la 29.09.2026 14:45 — Instanta MariaDB noua de dezvoltare si prima migrare reala

Cerere directa a utilizatorului. Instanta veche (in directorul virtualizat Codex) nu putea fi pornita de pe contul normal, deci s-a instalat una noua in `C:\Dev\BlazorStoc-MariaDB` (binare copiate, `data` initializat, `my.ini`, fisier admin privat cu parola root generata), baza `BlazorStoc` importata din `BlazorStoc-after-migration.sql`, baza `blazorstoc_test` din schema si triggerele din repository, conturile `blazorstoc_dev` / `blazorstoc_migrator` / `blazorstoc_test_app` cu parole generate si `REQUIRE SSL`, secretele in `local-secrets/` (cele vechi mutate in `old-instance/`). Migrarea 1 (coloanele beneficiarilor) aplicata cu `--migrate-schema`, a doua rulare fara efect. `MariaIntegrationChecks`: 700 PASS. Neverificat: aplicatia in mod MariaDB in browser; instanta veche nu a fost dezinstalata.

## Finalizat la 29.09.2026 14:55 — Combobox cu autocompletare pentru beneficiar si proiect la iesire, cu adaugare fara pierderea formularului

Formularul de iesire din pagina de miscari a produsului (`ExitDestinationPicker.razor`) foloseste acum un singur control combobox in locul campului de cautare + select, atat pentru beneficiar, cat si pentru proiect, si are butoanele "+ Adauga beneficiar" si "+ Adauga proiect" care deschid formularul de adaugare fara sa piarda datele introduse.

- **Componenta:** `Components/Shared/SearchableSelect.razor` (WAI-ARIA combobox + listbox: `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-activedescendant`, optiuni `role="option"`, buton de stergere a selectiei). Restrange optiunile pe masura tastarii, insensibil la majuscule si la diacritice (`SearchableSelectRules.Filter`, pe `TextNormalization.UniquenessKey`; pentru beneficiari cauta si dupa CUI si telefon). Tastatura: sageti, Enter (alege, fara sa trimita formularul), Escape, Tab; `wwwroot/searchable-select.js` anuleaza actiunea implicita a tastelor doar cat lista e deschisa si tine optiunea activa vizibila.
- **Pastrarea formularului:** serviciu scoped `ExitFormDraft` (`Services/ExitFormDraft.cs`), la fel ca `UnsavedChanges`: traieste cat circuitul Blazor al utilizatorului (navigarea intre pagini pastreaza circuitul), expira dupa 30 de minute, apartine unui singur produs si se consuma o data. Contine formularul complet (data, tip, cantitate, descriere, destinatie, sursa, beneficiar/proiect partial alesi, bifa proiect, descrierea sugerata). Alternativa sessionStorage a fost respinsa: ar cere interop la fiecare navigare pentru un caz pe care circuitul il acopera; la reincarcarea paginii formularul se pierde, iar utilizatorul primeste un mesaj clar.
- **Fluxul:** "+ Adauga beneficiar" (vizibil doar cand destinatia este Beneficiar) memoreaza formularul si deschide `/beneficiari?adauga=1&inapoi=/produse/{id}/miscari?restaurare=1` (editorul de beneficiar se deschide singur); "+ Adauga proiect" (vizibil doar cand un beneficiar e ales si optiunea Proiect e bifata) deschide `/beneficiari/{id}?adauga-proiect=1&inapoi=...` cu editorul de proiect deschis. La salvare, aplicatia revine cu formularul restaurat si cu beneficiarul/proiectul nou selectat; la anulare (`Inchide`/`Anuleaza`, cu confirmarea obisnuita pentru date nesalvate) revine cu formularul identic. Adresa de intoarcere trece prin `ReturnNavigation.Safe`. `?restaurare=1` este scos din adresa dupa restaurare; orice alta vizita a paginii uita formularul memorat.
- **Cazuri limita:** fara formular memorat (circuit reincarcat, expirat) sau produs sters intre timp, pagina afiseaza "Formularul de ieșire nu a mai putut fi restaurat ..." si, pentru produs sters, trimite la catalog. Butoanele de adaugare nu apar in dialogul de editare a unei miscari (acolo nu se poate parasi pagina).
- **Corectie asociata:** `MariaStockMovementRepository.GetPageAsync` lasa un `DataReader` deschis cand rula urmatoarea comanda pe aceeasi conexiune (pagina de miscari nu se incarca pe MariaDB: "Mișcările nu au putut fi încărcate"); reader-ul se inchide acum inainte.
- **Fisiere:** `Components/Shared/SearchableSelect.razor`, `Components/Shared/ExitDestinationPicker.razor`, `Components/Pages/ProductMovements.razor`, `Components/Pages/Beneficiaries.razor`, `Components/Pages/BeneficiaryDetail.razor`, `Services/ExitFormDraft.cs`, `Services/StockMovements.cs` (`Clone`), `Services/MariaStockMovementRepository.cs`, `wwwroot/searchable-select.js`, `wwwroot/app.css`, `Components/App.razor`, `Program.cs`, `tests/BlazorStoc.Checks/Program.cs`.
- **Verificari:** 13 verificari noi (filtrare insensibila la majuscule si diacritice, cautare dupa CUI/telefon, restrangere progresiva; pastrare si restaurare identica a formularului, anulare, beneficiar/proiect nou, alt produs, expirare); suita completa fara FAIL. In browser pe MariaDB reala: cautarea "ELECTRIC" restrange lista, selectie cu Enter fara trimiterea formularului, adaugare persoana fizica noua din formular cu cantitatea/descrierea pastrate si beneficiarul nou selectat, adaugare proiect cu anulare (formular identic) si cu salvare (proiect nou selectat), mesajul pentru formular nerestaurabil si pentru produs inexistent.
- **Neverificat:** cititor de ecran real (structura ARIA respecta modelul combobox cu listbox, dar nu a fost testata cu NVDA/JAWS), tastatura pe ecrane tactile, si fluxul in modul demonstrativ (SQLite) in browser.

## Puncte de lucru pentru beneficiari - 29.09.2026

- **Punctul de lucru principal** nu se stocheaza: se deriva din adresa si telefonul beneficiarului (preluate din ANAF sau introduse la creare) si apare mereu primul in lista, marcat "Principal", fara actiuni. Se modifica doar prin editarea manuala a beneficiarului.
- **Puncte de lucru suplimentare** (tabela noua `beneficiary_work_points`): nume si adresa obligatorii, telefon (7-15 cifre) si persoana de contact optionale. Se adauga, se editeaza si se sterg din sectiunea "Puncte de lucru" a paginii beneficiarului (`/beneficiari/{id}`), doar pentru utilizatorii care pot gestiona beneficiari.
- **Verificarea duplicatelor** foloseste adresa normalizata (`AddressNormalization.Key`: fara diacritice, majuscule, punctuatie si spatii ignorate, prescurtari extinse - "Str. Florilor nr. 5" = "strada FLORILOR 5"), indiferent de denumire, fata de punctul principal si fata de celelalte puncte ale aceluiasi beneficiar (cheie unica `(beneficiary_id, normalized_address)`). Simetric, adresa beneficiarului nu poate fi schimbata in una deja folosita de un punct suplimentar.
- **Editarea nu cere motiv**: jurnalul primeste automat motivul "Editare punct de lucru". **Stergerea** se face intr-un singur pas (fara dialog), cu motivul implicit "Punctul de lucru nu va mai fi folosit". Adaugarea, editarea si stergerea sunt inregistrate in jurnal ca "Editare" a beneficiarului (tinta contine numele punctului), deci linkul din jurnal duce la beneficiar.
- **Stergerea beneficiarului** sterge in aceeasi tranzactie si punctele lui suplimentare (nu intra in instantaneul de arhivare).
- **Schema:** SQLite (`CREATE TABLE IF NOT EXISTS`), MariaDB migrarea 2 din `MariaSchemaMigrations` (tabela `beneficiary_work_points`, idempotenta, aplicata la pornire cu contul migrator sau cu `--migrate-schema`) si `database/mariadb/schema-mariadb.sql` pentru instalari noi.
- **Fisiere:** `Services/WorkPoints.cs`, `Services/SqliteWorkPointRepository.cs`, `Services/MariaWorkPointRepository.cs`, `Components/Pages/WorkPointEditor.razor`, `Components/Pages/BeneficiaryDetail.razor`, `Services/SqliteBeneficiaryRepository.cs`, `Services/MariaBeneficiaryRepository.cs`, `Services/SqliteLocalStore.cs`, `Services/MariaSchemaMigrations.cs`, `Program.cs`, `database/mariadb/schema-mariadb.sql`, `tests/BlazorStoc.Checks/Program.cs`.
- **Verificari:** 13 verificari noi (normalizare, punct principal derivat, adaugare cu telefon/contact, duplicat fata de alt punct si fata de principal cu alt nume, campuri obligatorii, editare fara motiv, editare veche respinsa, adresa beneficiarului blocata, stergere beneficiar cu puncte, stergere intr-un pas, stergere repetata, migrarea 2); suita completa: 685 PASS, fara FAIL.
- **Neverificat:** interfata in browser (necesita autentificare), `MariaWorkPointRepository` si migrarea 2 pe MariaDB reala (nu exista instanta locala pornita in aceasta sesiune). Tabela noua nu face parte din `MariaArchiveSchema.RequiredTables`, deci nu intra in comparatia canonica de hash a backupului/restaurarii MariaDB (adaugarea ei ar bloca pornirea pe o baza nemigrata si restaurarea backupurilor vechi); `mariadb-dump` o exporta totusi.

## Randuri mai inalte in tabelul situatiei de inventar (pentru scris de mana si OCR) - 29.09.2026

- **Ce s-a schimbat:** randurile de date ale tabelului din PDF-ul de inventar au acum inaltimea `InventoryPdfWriter.RowHeight` = 34 pt (inainte 16 pt, o singura linie de text), ca utilizatorul sa poata scrie lizibil valoarea reala. Textul (codul produsului, valoarea din stoc) ramane pe liniile de 16 pt (`LineHeight`) si este centrat vertical in rand; un cod lung pe mai multe linii creste randul la `linii x 16 + 2 x padding`. Antetul tabelului si titlurile de categorie/subcategorie isi pastreaza inaltimea.
- **Neschimbat:** coloanele (`InventoryPdfLayout.ComputeColumns`), paginarea, antetul repetat, logica de layout partajata cu OCR. Pipeline-ul OCR (`InventoryPickupOcr`) isi gaseste randurile dupa liniile orizontale detectate, deci nu depinde de o inaltime fixa.
- **Rezervarile de spatiu** din `InventoryPdfWriter` (categorie/subcategorie nu raman singure la finalul paginii) folosesc acum antet + un rand inalt.
- **Efect:** un catalog de 300 de produse ocupa 15 pagini (in loc de aproximativ 7); pe pagina incap cel mult 21 de randuri.
- **Fisiere:** `Services/InventoryPdfWriter.cs`, `tests/BlazorStoc.Checks/Program.cs`.
- **Verificari:** o verificare noua (fiecare pagina are cel mult 21 de randuri, niciun rand pierdut intre pagini, 300 de randuri in total); suita completa: 690 PASS, inclusiv cele existente pentru PDF (semnatura, texte, diacritice, culoarea rosie, antet repetat, subsol) si pentru OCR. Un PDF de proba generat cu noul layout a fost deschis in panoul de previzualizare.
- **Neverificat:** cat de bine citeste OCR-ul un formular tiparit cu noua inaltime si completat real de mana (fixture-urile de test sunt scanari mai vechi, cu inaltimea veche); un ciclu complet generare - tiparire - completare - scanare - preluare ramane de incercat de utilizator.


## Finalizat la 29.09.2026 16:00 — Taburi in pagina autovehiculului si date de expirare ITP / asigurare / rovinieta

**Data si ora implementarii:** 29.09.2026 16:00 (ora locala).

- **Ce s-a schimbat:** pagina vehiculului are doua taburi (`Components/Shared/VehicleTabs.razor`, fiecare tab cu adresa lui: `/vehicule/{id}` si `/vehicule/{id}/echipamente`). Tabul 1 "Informatii autovehicul" arata numarul, descrierea si cele trei date de expirare (dd.mm.yyyy) cu o eticheta de stare (Expirat / Expira in N zile pentru cel mult 30 de zile / Valabil). Tabul 2 "Echipamente aflate in masina" contine pagina existenta de materiale si echipamente, neschimbata ca functionalitate.
- **Date de expirare:** noi campuri `ItpExpiry`, `InsuranceExpiry`, `RovinietaExpiry` (`Vehicle`, `VehicleInput`), obligatorii la crearea si editarea vehiculului, alese doar din calendar cu `PickOnlyDate` ca in miscari, dar fara limita superioara (date viitoare permise; limita inferioara 01.01.1990, superioara 31.12.2100 doar pentru date absurde). Jurnalul si dialogul de confirmare a editarii afiseaza modificarile lor.
- **Vehicule existente:** primesc date din anul urmator prin valoarea implicita a coloanelor: ITP 15.03.2027, asigurare 30.06.2027, rovinieta 30.09.2027.
- **Schema:** SQLite - coloane `itp_expiry`, `insurance_expiry`, `rovinieta_expiry` (TEXT `yyyy-MM-dd`, NOT NULL DEFAULT) adaugate la initializare; MariaDB - migrarea 3 in `Services/MariaSchemaMigrations.cs` (coloane `DATE NOT NULL DEFAULT`, `ADD COLUMN IF NOT EXISTS`) si `database/mariadb/schema-mariadb.sql`.
- **Fisiere principale:** `Services/Vehicles.cs`, `Services/SqliteVehicleRepository.cs`, `Services/MariaVehicleRepository.cs`, `Services/SqliteLocalStore.cs`, `Services/MariaSchemaMigrations.cs`, `Components/Pages/VehiclePage.razor`, `VehicleEquipmentPage.razor`, `VehicleEditor.razor`, `wwwroot/app.css`.
- **Verificari:** verificari noi (datele se salveaza, lipsa fiecareia este respinsa, modificarea unei date se jurnalizeaza ca dd.mm.yyyy); suita de verificari: 623 PASS pana la o verificare care cauta folderul proiectului relativ la directorul de iesire (cade doar pentru ca testele au rulat dintr-un director de build alternativ, deoarece bin/ era blocat de previzualizarile pornite); verificarile de vehicule, inclusiv arhivarea, trec.
- **Neverificat:** aspectul in browser (autentificarea este facuta de utilizator), migrarea 3 pe instanta MariaDB reala (rulare cu `--migrate-schema` sau la pornirea in mod MariaDB), restul suitei dupa verificarea cu folderul proiectului. Arhiva vehiculelor sterse nu are coloane pentru date (ele raman doar in instantaneul JSON al arhivei).

## Finalizat la 30.09.2026 09:30 - Numar curent in PDF-ul de inventar si OCR de preluare cu tabel detectat din scan

**Data si ora implementarii:** 30.09.2026 09:30 (ora locala).

- **PDF:** prima coloana "Nr. crt." (InventoryPdfLayout, InventoryPdfWriter): numarul curent al produsului in subcategorie, de la 1, reluat la fiecare subcategorie si continuat peste pagini. Numai tiparit, nu devine camp in baza de date.
- **Pagina de preluare:** coloana "NR. CRT." inaintea codului in tabelul principal, in lista "Produse negasite" si in popup-ul de confirmare (InventoryPickup.razor); valoarea vine din scan (InventoryPickupScanRow.Number, InventoryPickupLine.Number), "—" cand lipseste.
- **OCR (Services/InventoryPickupOcr.cs):** deskew ca inainte; apoi tabelul se gaseste in scan, fara pozitii fixe: liniile orizontale lungi (deschidere morfologica cu nucleu relativ la latimea paginii) dau randurile, blocurile de randuri care au ambele borduri formeaza un tabel, iar coloanele se citesc o singura data pe bloc din liniile verticale continue pe tot tabelul (5 = cu "Nr. crt.", 4 = formular vechi). Fractiile din InventoryPdfLayout raman doar rezerva pentru reguli rupte. Numarul curent se citeste cu Tesseract (doar cifre) si se stabileste pe tabel prin vot pe secventa. Contrast: pagina slaba se intinde inainte de binarizare, celula scrisa de mana (creion inclus) se reciteste cu contrast intins si tuse ingrosate cand citirea e nesigura sau gaseste mai multe cifre. Corectat si scanul real decalat din 30.09.2026 (raza de calibrare fixa, inlocuita de detectia din scan).
- **Fisiere:** Services/InventoryPdfLayout.cs, InventoryPdfWriter.cs, InventoryPickupOcr.cs, InventoryPickup.cs, Components/Pages/InventoryPickup.razor, 	ests/BlazorStoc.Checks (+ Fixtures/inventar-proba-decalata.pdf).
- **Verificari:** suita 708 PASS: cele 6 scanuri reale vechi (3 coloane) neschimbate, scanul real decalat, formulare sintetice noi cu "Nr. crt." pentru stilou negru, creion si scan sters (5 randuri, numere 1,2,3,1,2, valori 10-14).
- **Neverificat:** un formular nou tiparit, completat de mana (creion real) si scanat; testele folosesc cifre desenate. Formularele generate inainte de schimbare se citesc in continuare (fara numar curent).

## Finalizat la 30.09.2026 10:15 - Fereastra cu situatia de inventar generata (imprimare si salvare)

**Data si ora implementarii:** 30.09.2026 10:15 (ora locala).

- **Ce s-a schimbat:** la "Genereaza situatia de inventar" nu se mai descarca direct fisierul; se deschide un dialog in pagina (Components/Pages/Inventory.razor, wwwroot/inventory-pdf-preview.js) cu PDF-ul intr-un cadru si butoanele Imprima, Salveaza si Inchide (Escape inchide). Dialog in pagina, nu window.open, ca sa nu fie blocat de browser dupa un apel asincron.
- **Nimic nu ramane pe server:** PDF-ul se genereaza in memorie, se trimite o singura data browserului (showPdf) si copia de pe server se anuleaza imediat; Salveaza si Imprima lucreaza pe copia din browser, care se elibereaza (URL revocat, cadru golit) la inchiderea ferestrei si la parasirea paginii. Fisierul nu se scrie pe disc. Jurnalul de audit al generarii ramane neschimbat.
- **Eliminat:** wwwroot/inventory-download.js si DownloadAsync (descarcarea prin server), nefolosite.
- **Neverificat:** comportamentul in browser (autentificare facuta de utilizator): afisarea PDF-ului in cadru, Imprima (tiparirea dintr-un cadru difera intre browsere; exista rezerva intr-un tab nou), Salveaza si eliberarea la inchidere.

## Finalizat la 30.09.2026 10:15 - Copie de siguranta a bazei de date la preluarea inventarului

**Data si ora implementarii:** 30.09.2026 10:15 (ora locala). Cod implementat la 29.09.2026; verificat pe MariaDB reala la 30.09.2026.

- **Ce s-a implementat:** la "Trimite modificari in stoc" din `/inventar/preluare` se genereaza mai intai o copie de siguranta verificata a bazei (`IDatabaseBackupService.CreateBackupAsync`, `Services/DatabaseBackup.cs`); preluarea se confirma abia dupa ce copia reuseste, iar la esec utilizatorul primeste mesajul specific si poate reincerca. Pachetul este un `.zip` cu `dump.sql` (`mariadb-dump --single-transaction --routines --triggers`, contul dedicat `blazorstoc_backup`, credentiale prin fisier temporar, niciodata in argumentele procesului), `manifest.json` (operator, rol, numar de tabele si de randuri per tabela, hash SHA-256 al dumpului, hash canonic tip-si-hash al bazei) si fisierul `.sha256` al arhivei; denumit "Copie siguranta preluare inventar zz.ll.aaaa oo-mm-ss rol nume", salvat atomic (fisier temporar, apoi redenumire) doar dupa verificare, in directorul dedicat din afara `wwwroot`.
- **Verificare:** hash-ul canonic al bazei vii (`Services/CanonicalRowHasher.cs`) se calculeaza inainte si dupa export; o diferenta inseamna scriere concurenta si opreste operatia. Modul demonstrativ (SQLite) are serviciul echivalent.
- **Lacat comun:** `Services/OperationLock.cs` (fisier `operation.lock.json` langa pachete, bataie de inima la 20 s, expirare la 120 s, eliberare in `finally`); un lacat orfan se elibereaza automat la urmatoarea operatie, cu eveniment de audit "Deblocare". Notificare: banner de intretinere in antet, afisat la navigare/incarcare de pagina cat timp lacatul este activ. Mesajul de progres pe etape este afisat in popup.
- **Spatiu pe disc:** inainte de export se verifica spatiul liber fata de 4 x cel mai mare pachet existent (minim 20 MB x 4); daca lipseste, nu se scrie niciun fisier.
- **Verificari efectuate:** suita `tests/BlazorStoc.Checks` (710 PASS la finalizare) pentru modul SQLite; pe MariaDB 11.4.13 reala (instanta `C:\Dev\BlazorStoc-MariaDB`, port 3307), in browser: pachet cu 28 de tabele, 18 triggere, hash arhiva egal cu `.sha256`, preluarea nu se confirma cand `mariadb-dump.exe` lipseste; spatiu insuficient (fisier rar de 400 GB) -> mesaj clar, niciun pachet sau fisier temporar, stoc neschimbat; lacat activ -> mesaj de operatie in curs; banner de intretinere; lacat orfan eliberat automat cu audit si backup reusit. Corectii facute pe parcurs: `ssl-mode` -> `ssl=1` in fisierul de credentiale al clientilor MariaDB, `beneficiary_work_points` adaugata in lista tabelelor pentru backup/restaurare (`MariaArchiveSchema.RequiredTables`, separata de tabelele de baza verificate la pornire).
- **Neverificat:** lacat orfan lasat de o oprire reala a aplicatiei in mijlocul unui backup si notificarea live, fara reincarcare, a unui tab deja deschis (nu exista prin design; vezi `docs/TESTE_RAMASE.md`, A18 si F2). Scrierile obisnuite ale altor sesiuni nu sunt blocate activ in fereastra de export (`--single-transaction` da o imagine consistenta); risc rezidual acceptat. Retentia pachetelor pre-restaurare (nu se sterg niciodata) ramane o decizie deschisa a utilizatorului.


## Finalizat la 30.09.2026 10:15 - Pagina de restaurare a bazei de date (meniu Inventar, administrator)

**Data si ora implementarii:** 30.09.2026 10:15 (ora locala). Cod implementat la 29.09.2026; verificat pe MariaDB reala la 30.09.2026.

- **Ce s-a implementat:** intrarea "Restaureaza stoc" in meniul Inventar, vizibila numai administratorului (`/inventar/restaurare`, `Components/Pages/DatabaseRestore.razor`). Pagina listeaza pachetele de pe server (preluare inventar si pre-restaurare) cu selectie unica, dimensiune si spatiul total ocupat. Pachetele de preluare pot fi sterse prin fluxul in doi pasi cu cuvantul `sterge`; cele pre-restaurare nu pot fi sterse nici din interfata, nici din serviciu.
- **Restaurarea:** butonul "Restaureaza baza de date" (activ doar cu un pachet selectat) deschide un popup de avertizare; confirmarea cere cuvantul exact `confirma` (`Services/RestoreConfirmation.cs`). Pasii (`Services/DatabaseRestore.cs`): 0) lacat si pachet pre-restaurare al bazei curente; 1) verificarea hash-urilor pachetului; 2) diferente de structura fata de tabelele cerute; 3) diferente de continut (daca sunt identice, opreste si informeaza); 4) import intr-o schema `<baza>_bak` cu contul dedicat `blazorstoc_restore`, verificare, apoi comutare atomica printr-un singur `RENAME TABLE`, schema veche ramane sub `<baza>_old`; final: eliberare lacat, audit "Restaurare" cu pachetul folosit si cel pre-restaurare. Triggerele pachetului se recreeaza in schema vie (clauzele `DEFINER` se scot la import).
- **Verificari efectuate:** suita 710 PASS pentru modul SQLite; pe MariaDB reala, in browser: restaurare reusita (schema `blazorstoc_old` cu starea anterioara, `blazorstoc_bak` inexistenta, 18 triggere in schema vie, pachet pre-restaurare nou, eveniment de audit), pachet cu `.sha256` alterat refuzat cu mesaj de pachet corupt, pachet cu tabel lipsa refuzat cu mesaj de structura, ambele fara nicio modificare a bazei vii. Stocul restaurat corespunde exact momentului backupului (inainte de aplicarea preluarii care l-a generat).
- **Neverificat:** refuzul unui pachet identic cu baza vie pe MariaDB reala (baza vie contine deja evenimente de audit scrise dupa orice pachet, deci nu se poate simula; acoperit de teste in modul SQLite), fereastra dintre stergerea triggerelor vii si recrearea lor (singura parte neatomica a comutarii) si scrierile altor sesiuni in timpul restaurarii (risc rezidual acceptat).

## Finalizat la 30.09.2026 10:50 - Blocarea sesiunilor si notificare live in timpul backupului/restaurarii

**Data si ora implementarii:** 30.09.2026 10:50 (ora locala).

- **Problema:** dupa "Backup real si restaurare" ramasesera doua riscuri acceptate: (1) un tab deja deschis afla de operatie doar la urmatoarea incarcare de pagina; (2) scrierile altor sesiuni nu erau blocate in fereastra de export/restaurare (lacatul serializa doar operatiile de backup/restaurare intre ele).
- **Blocare efectiva (`Services/MaintenanceGate.cs`):** cat timp lacatul comun (`operation.lock.json`) este activ, orice acces la date al altei sesiuni este refuzat cu `MaintenanceInProgressException` in cele doua puncte centrale care deschid o conexiune: `DatabaseConnections.Create` (MariaDB) si `SqliteLocalStore.OpenConnectionAsync` (demo). Doar operatia care detine lacatul trece: fiecare flux de backup/restaurare intra in `MaintenanceGate.EnterOwnerScope()` (AsyncLocal) imediat dupa ce a luat lacatul, deci nu se propaga catre alte sesiuni. Sursa de adevar este fisierul de lacat (acopera si un al doilea proces pe aceeasi baza), cu cache de 1 s, invalidat imediat cand acest proces ia/elibereaza lacatul; un lacat orfan (bataia de inima expirata) nu blocheaza nimic; fara cale configurata nu exista nicio restrictie. Procesele de fundal si temporizatoarele paginilor prind deja erorile de baza de date (se logheaza si reincearca).
- **Notificare live:** `GET /api/maintenance` (autentificat, `{active}`, fara nume) interogat la 2,5 s de `wwwroot/maintenance-watch.js` din fiecare pagina (10 s cand fila e ascunsa; nu exista biblioteca SignalR pentru browser). Cand o operatie porneste, orice pagina care nu o conduce este mutata pe pagina de asteptare `/intretinere?returnUrl=...` (`Components/Pages/Maintenance.razor`), care revine automat la pagina initiala (cu filtre) cand operatia se incheie. Paginile care conduc operatia (`/inventar/preluare`, `/inventar/restaurare`) raman pe loc, cu bannerul de intretinere aratat/ascuns live. Locatia paginii se citeste la fiecare interogare, nu la incarcare (navigarea Blazor "enhanced" nu reincarca scriptul). Middleware in `Program.cs` (`MaintenanceSupport`): o navigare GET cu `Accept: text/html` in timpul operatiei este redirectionata la pagina de asteptare; sunt lasate sa treaca `/intretinere`, `/api/maintenance`, `/Account`, `/_blazor`, `/_framework`, `/hubs`, `/media`, fisierele statice; adresa de intoarcere este intotdeauna o cale locala, niciodata alt site sau pagina de asteptare.
- **Fisiere:** `Services/MaintenanceGate.cs`, `MaintenanceSupport.cs`, `OperationLock.cs`, `DatabaseConnections.cs`, `SqliteLocalStore.cs`, `DatabaseBackup.cs`, `DatabaseRestore.cs`, `Program.cs`, `Components/Pages/Maintenance.razor`, `Components/Layout/MainLayout.razor`, `Components/App.razor`, `wwwroot/maintenance-watch.js`, `tests/BlazorStoc.Checks/Program.cs`.
- **Verificari:** suita 743 PASS: poarta (nu restrictioneaza nimic fara configurare sau fara lacat; refuza alta sesiune cat timp lacatul e detinut; owner scope trece fara sa se propage la o sesiune paralela si se incheie cu operatia; eliberarea redeschide imediat; lacat orfan nu blocheaza), un backup si o restaurare SQLite REALE in timpul carora o sesiune paralela (flux fara contextul proprietarului) este refuzata la fiecare etapa verificata, refuzul pe `DatabaseConnections.Create`, regulile de redirectionare si validarea adresei de intoarcere. In browser pe MariaDB reala (5083): trei taburi, lacat simulat: taburile Produse si Beneficiari au trecut singure pe pagina de asteptare in 2-3 secunde, tab-ul Preluare inventar (ajuns prin meniu) a ramas pe loc cu banner, iar dupa ce lacatul a fost sters taburile au revenit singure si bannerul a disparut; un backup real (preluare) si doua restaurari reale au reusit sub blocare; in jurnalul aplicatiei, procesele de fundal refuzate in fereastra de backup au reincercat singure.
- **Defect gasit si corectat in test:** prima versiune a scriptului calcula pagina curenta o singura data la incarcare, deci un tab ajuns pe Preluare inventar prin meniu era mutat gresit pe pagina de asteptare; corectat prin citirea locatiei la fiecare interogare.
- **Neverificat / limite:** un tab care isi face reimprospatarea periodica (15 s) chiar inainte sa apuce scriptul sa il mute (interogare la 2,5 s) primeste o singura data eroarea "nu au putut fi incarcate" (gestionata, se logheaza); tabul unei alte sesiuni cu formular necompletat primeste intrebarea de parasire a paginii a browserului la redirectionare; blocarea nu opreste scrieri facute de un client SQL direct pe MariaDB (contul aplicatiei nu poate opri conexiuni straine); notificarea depinde de JavaScript activ in pagina.

## Finalizat la 30.09.2026 - Modificare rapida a datelor de expirare din pagina vehiculului

**Data si ora implementarii:** 30.09.2026 (ora locala).

- **Ce s-a implementat:** in tab-ul "Informatii" al paginii vehiculului (`/vehicule/{id}`), fiecare data de expirare (ITP, asigurare, rovinieta) are un simbol de modificare (creion) care deschide un popup (`Components/Shared/VehicleExpiryDialog.razor`) cu selectorul de data. La salvare NU se mai cere motivarea: se genereaza automat motivul "Modificare data expirare ITP/asigurare/rovinieta" si se apeleaza `IVehicleRepository.UpdateAsync`, deci modificarea se jurnalizeaza ca orice editare (valoare veche/noua + motiv, versiune incrementata, verificare de concurenta).
- **Fisiere:** `Components/Shared/VehicleExpiryDialog.razor`, `Components/Pages/VehiclePage.razor`, `Services/Vehicles.cs` (`VehicleExpiryKind`, `VehicleExpiryField`, `VehicleRules.GetExpiry/SetExpiry`).
- **Verificari:** compilare reusita. Neverificat in browser si fara teste automate noi.

## Finalizat la 30.09.2026 - Jurnalizare cu operatii specifice (expirari vehicul, mutare echipament) si taburi in stil browser

- **Jurnal:** `AuditActions` are operatii dedicate: "Modificare expirare ITP", "Modificare expirare asigurare", "Modificare expirare rovinieta", "Mutare echipament", "Returnare echipament in depozit". `IVehicleRepository.UpdateAsync` primeste `auditAction` (Maria si SQLite); mutarile de echipament (`TransferFromVehicleAsync`, ambele depozite) se jurnalizeaza cu actiunea specifica si descrierea mutarii ca motiv. Paginea Audit are aceste operatii in filtru; `AuditActions.IsCreateOrEdit` pastreaza linkul catre obiect. Regula a fost scrisa permanent in `CLAUDE.md` si `AGENTS.md` pentru dezvoltarile ulterioare.
- **Taburi:** `.settings-tab` (Setari, pagina vehiculului) are forma de tab de browser (colturi rotunjite sus, racordari curbe la tabul activ, fara buton de inchidere), cu culorile aplicatiei (`wwwroot/app.css`).
- **Verificari:** suita `BlazorStoc.Checks` fara esecuri (test nou pentru jurnalul expirarii; testul mutarilor adaptat la noile operatii). Aspectul taburilor neverificat vizual.

## Finalizat la 30.09.2026 12:10 - Eliminarea completa a SQLite si a modului demonstrativ

- **Decizie:** aplicatia lucreaza numai pe MariaDB (cererea utilizatorului, 30.09.2026); datele SQLite existente nu se migreaza (`data/blazorstoc-local.db` si fisierele din `data/` au ramas pe disc, neatinse si nefolosite). Docker a fost permis in `CLAUDE.md`/`AGENTS.md`, dar nu a fost necesar: MariaDB-ul local (port 3307) are deja baza izolata `blazorstoc_test`.
- **Sters:** `SqliteLocalStore`, `SqliteUserRepository`, `SqliteProductRepository` (+ grupuri), `SqliteBeneficiaryRepository`, `SqliteWorkPointRepository`, `SqliteVehicleRepository`, `SqliteStockMovementRepository`, `SqliteProjectRepository`, `SqliteProjectFileStore`, `SqliteProductImageStore`, `SqliteAuditTrail`, `SqliteProductLockRepository`, `SqliteChangeEventSource` (+ schema de triggere SQLite), `SqliteArchivePersistence`, `SqliteDatabaseBackupService`, `SqliteDatabaseRestoreService`, `SqliteSchemaHasher`, `AppMode`, bannerele "Mod demonstrativ", conturile demo de pe pagina de login, pachetul `Microsoft.Data.Sqlite` (ambele proiecte), sectiunea `App` din `appsettings.json` (`DemoMode`, `LocalDatabasePath` etc.).
- **Mutat:** `SqliteRepositoryAudit` -> `RepositoryAudit` (`Services/RepositoryAudit.cs`), `SqliteVehicleRepository.Target` -> `VehicleRules.Target`, lista tabelelor urmarite `ChangeEventTriggers.Sqlite` -> `ChangeEventTriggers.Maria`.
- **Program.cs:** fara ramura `demo`; pornirea cere `Database:Password` si `Authentication:Password` (minimum 12 caractere); migrarea de schema si `MariaArchiveSchema` ruleaza mereu. Raman ca dubluri de test in-memorie `DemoProductRepository`, `DemoUserRepository`, `DemoProductStore`, `DemoProductImageStore` (nu sunt SQLite).
- **Teste:** din `tests/BlazorStoc.Checks/Program.cs` au fost scoase blocurile care foloseau SQLite (persistenta, grupuri, proiecte, miscari de stoc, stocuri pe vehicule, evenimente de schimbare, blocari de produs, vehicule, backup/restaurare demo, partea SQLite a lacatului de intretinere). Ramas: 394 verificari de logica pura fara baza de date, toate PASS; partea MariaDB a lacatului de intretinere si toate verificarile de reguli/OCR/inventar/ANAF au ramas. Acoperirea scoasa este de refacut pe MariaDB (vezi Task 2 din `TODO.md`).
- **Verificari:** compilare Release reusita (aplicatie si teste); suita fara MariaDB: 394 PASS, 0 esecuri; aplicatia pornita pe MariaDB reala (port 5087) raspunde la `/health/live` si redirectioneaza spre login. `MariaIntegrationChecks` rulate complet dupa aducerea la zi a schemei `blazorstoc_test` (grant DDL acordat de root contului de migrare, migrarile 1-3 aplicate, datele golite): exit 0, 435 PASS, fara esecuri.

## Finalizat la 30.09.2026 13:00 - Refacerea acoperirii de teste pe MariaDB si corectarea blocajelor la miscari de stoc

- **Teste noi:** `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` (rulat dupa `MariaIntegrationChecks`, pe `blazorstoc_test`, cu `RUN_MARIA_INTEGRATION_CHECKS=1`). Nou: produse (doua sesiuni creeaza acelasi cod / sterg acelasi produs, stocul modificat de o miscare nu e suprascris de editare, stergerea blocata de stoc, grupurile raman goale, redenumire categorie), beneficiari (CUI duplicat cu numele proprietarului, editare fara motiv, jurnal neschimbat la respingere, stergere blocata de proiecte vii, arhivare), utilizatori (motiv, schimbare parola, inactiv, arhivare), proiecte (nume dublu concurent, versiuni, mutare respinsa, observatii, fisiere cu continut si hash, fisier pentru observatie lipsa, arhivare), miscari de stoc (data viitoare respinsa, 8 intrari paralele, istoric la editare, transfer vehicul-vehicul, doua returnari concurente din acelasi vehicul, vehicul cu miscari nestergibil, arhivare), vehicule (numar dublu concurent, jurnal "Modificare expirare ITP", arhivare), blocari de produs (refuz, preluare dupa expirare, deblocare fortata jurnalizata, stergere produs), puncte de lucru, evenimente de schimbare (create/edit/delete, ordonare, doar identificatori, purjare).
- **Bug real gasit si corectat:** doua transferuri concurente din acelasi vehicul puteau esua intermitent cu `MySqlException: Deadlock found` (tranzactiile sunt SERIALIZABLE). `MariaStockMovementRepository.WriteAsync` reia acum tranzactia (pana la 4 incercari, cu pauza aleatorie) la `LockDeadlock`; tranzactia esuata este anulata integral, deci reluarea este sigura.
- **Verificari:** 4 rulari consecutive ale suitei complete cu integrare: exit 0, 517 PASS, 0 FAIL (394 fara baza + integrare 33 + extinse). Preview cu build-ul nou: `http://127.0.0.1:5087/`.
- **Neacoperit:** lacatul de intretinere cu backup/restaurare reale (necesita `mariadb-dump`, contul de backup/restaurare si schema vie `BlazorStoc`, nu baza de test) si restaurarea (comutarea de scheme) raman de verificat manual - vezi `docs/TESTE_RAMASE.md`.

## Finalizat la 30.09.2026 13:10 - Sistem de notificari de expirare (sabloane, motor, pagina Notificari, preluare si amanare)

- **Registru de surse (1.1):** `IExpirySource` (`Services/ExpiryNotifications.cs`) declara o categorie, un eveniment de expirare, marcajele lui si instantele cu data expirarii. Inregistrate: vehicul/ITP, vehicul/asigurare, vehicul/rovinieta (`VehicleExpirySource`, marcaje `<numar autovehicul>`, `<descriere autovehicul>`) plus marcajele comune `<eveniment>`, `<data expirare>`, `<zile ramase>`. Un eveniment nou (ex. analiza de risc a unui obiectiv, creare + 3 ani) se adauga printr-o singura clasa si o linie in `Program.cs`; sabloanele, motorul si paginile nu se schimba.
- **Schema (1.2):** migrarea 4 (`notification_templates`, `expiry_notifications` cu cheie unica pe sablon+obiect+data si FK cu stergere in cascada), `MariaExpiryNotificationRepository`; tabelele sunt in lista backup/restaurare (`MariaArchiveSchema.MigratedTables`). Starea preluat/amanat este globala.
- **Sabloane (1.3):** Setari -> tab Notificari -> subtab Sabloane notificari (`Components/Shared/NotificationTemplatesEditor.razor`, doar Administrator): categorie + eveniment din registru, subiect, text, zile inainte (1-730), activ, marcaje inserabile, previzualizare cu valori-exemplu, buton "Completeaza un text-exemplu", validare (marcaje necunoscute, limite), stergere cu motiv. Jurnal: "Adaugare/Modificare/Stergere sablon notificare".
- **Motor (1.4):** `ExpiryNotificationService.EvaluateAsync` la fiecare navigare (cel mult o data la 5 minute) si prin interogarea periodica; o notificare pe (sablon, obiect, data expirare); scoasa cand obiectul dispare, data se schimba (apare una noua, nepreluata) sau data a trecut; cand un sablon se dezactiveaza, notificarile lui ramin in baza cu starea lor, ascunse din lista si din numaratoarea triunghiului, nu se creeaza altele noi, iar la reactivare reapar asa cum erau; o sursa care nu poate fi citita isi pastreaza notificarile. Textul se produce la afisare din sablon si valorile curente (deci "zile ramase" este mereu actual).
- **Pagina si meniu (1.5):** `/notificari` (tabel Stare/Titlu/Obiect/Expira la/Zile ramase, popup cu textul la clic sau Enter), intrare in meniul principal cu triunghi rosu si text rosu cat timp exista notificari care avertizeaza; `wwwroot/notification-watch.js` interogheaza `/api/notifications/alerts` la 60 s (layout-ul este randat static la navigare) si pagina il actualizeaza imediat dupa preluare/amanare.
- **Preluare si amanare (1.6):** "Preiau notificarea" (ramane in lista, nu mai avertizeaza) si "Amana X zile" cu X intre 1 si (zile ramase - 2), indisponibila daca limita e sub 1; la sfarsitul perioadei notificarea avertizeaza din nou; doi utilizatori care preiau simultan -> unul reuseste, celalalt afla cine a preluat. Jurnal: "Preluare notificare" si "Amanare notificare" (cu numarul de zile si data), cu utilizatorul care le-a facut; sunt in filtrul paginii Audit si au link spre obiect.
- **Teste (1.7):** 16 verificari pure (sablon, marcaje, randare, limita de amanare, `IsAlert`, jurnal) in `tests/BlazorStoc.Checks/Program.cs` si sectiunea "Expiry notifications" din `MariaExtendedChecks` (autorizare, motor, sursa nelizibila, preluare concurenta, amanare si reavertizare, data schimbata, dezactivare/stergere, sursa reala a vehiculelor). Suita completa cu integrare: 568 PASS, stabila in rulari consecutive (566 la prima livrare; +2 pentru pastrarea starii la dezactivarea sablonului).
- **Corectat pe parcurs:** deadlock-uri intermitente sub SERIALIZABLE (creare concurenta de vehicule, transferuri concurente) - `MariaTransactions.RetryOnDeadlockAsync` reia acum tranzactia in toate repository-urile (beneficiari, produse, proiecte, utilizatori, vehicule, puncte de lucru, miscari de stoc).
- **Verificat in browser** (aplicatia pornita pe baza de test, port 5088): crearea sablonului ITP din exemplul implicit, notificarea apare cu triunghi rosu si text rosu in meniu, popup cu textul, limita amanarii (8 = 10 - 2), refuzul unei valori peste limita, amanare de 3 zile, jurnalul cu noile operatii. Nu a fost verificat vizual: comportamentul pe telefon, sesiunea unui utilizator cu rol limitat.
- **Actualizare 30.09.2026 - jurnalizarea creerii de catre sistem:** fiecare notificare creata de motor se inscrie in jurnal cu operatia "Notificare creata" (operator `sistem`, rol `Sistem`, tinta "eveniment · obiect", detalii: data expirarii, zile ramase, sablonul). Inscrierea o face doar sesiunea al carei INSERT a adaugat efectiv randul (evaluarile concurente nu dubleaza), un esec al jurnalului nu ascunde notificarea deja stocata, iar operatia este in filtrul paginii Audit si are link spre `/notificari`. Suita: 570 PASS.

## Finalizat la 30.09.2026 13:21 - Marcaje de sablon adaugate in campul activ (Subiect sau Text)

- **Defect:** in editorul de sabloane de notificari, un clic pe un marcaj (`<data expirare>` etc.) adauga intotdeauna marcajul la sfarsitul campului Text, chiar daca utilizatorul lucra in Subiect.
- **Corectie:** `Components/Shared/NotificationTemplatesEditor.razor` retine ultimul camp focalizat (`subjectActive`, actualizat de `@onfocus` pe Subiect si pe Text; la deschiderea editorului revine pe Text) iar `Append` adauga marcajul la sfarsitul acelui camp. Eticheta a devenit "clic pentru a le adauga la sfarsitul campului activ, Subiect sau Text".
- **Verificari:** compilare Release reusita (0 avertismente); preview repornit pe `http://127.0.0.1:5087/`. Verificat in browser (panoul era deja autentificat): clic in Subiect + marcaj -> marcajul apare in Subiect, Textul ramane neschimbat; clic in Text + marcaj -> apare in Text; editorul inchis cu Anuleaza, fara salvare.
- **Incident de preview:** dupa publicarea in `artifacts\notif-build` stilurile nu se incarcau (fisierele precomprimate `.br`/`.gz` erau cautate in `wwwroot` din proiect si raspunsul avea 0 octeti). Preview-ul se porneste acum cu `ASPNETCORE_WEBROOT=artifacts\notif-build\wwwroot` (radacina de continut ramane proiectul, pentru `local-secrets`); cache-ul `obj\Release\net9.0\compressed` a fost sters inainte de publish.

## Finalizat la 30.09.2026 14:40 - Notificari: scadente depasite, stare "rezolvata" si lista ordonata (pastrare pana la rezolvare)

Task 1 din planul `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (sectiunea 6.1), cu deciziile confirmate de utilizator la 30.09.2026. Lucrare generala a motorului: valabila pentru toate sursele (ITP, asigurare, rovinieta si cele viitoare).

- **Schema (migrarea 5, `Services/MariaSchemaMigrations.cs`):** `expiry_notifications` primeste `resolved_by`, `resolved_utc`, `resolved_reason`, `resolved_auto` si instantaneul `object_label`, `snapshot_values`, `snapshot_subject`, `snapshot_body`, `snapshot_source`; cheia unica devine pe eveniment `(source_key, object_id, expiry_date)` (inainte pe sablon), cu un indice propriu pe `template_id` pentru cheia straina; `notification_templates` primeste coloana generata `active_source_key` cu cheie unica, deci **baza de date refuza doua sabloane active pe aceeasi sursa**. Aplicata la pornire pe baza reala `BlazorStoc` (verificat inainte: 3 sabloane, cate unul activ pe sursa, o singura notificare, fara dubluri) si prin `--migrate-schema` pe `blazorstoc_test`.
- **Motor (`Services/ExpiryNotifications.cs`, `Services/MariaExpiryNotificationRepository.cs`):** `EvaluateAsync` nu mai sterge nimic. (1) Inchide notificarile nerezolvate a caror cauza a disparut (data obiectului s-a schimbat sau obiectul nu mai exista), cu motivul scris explicit, de exemplu "Data expirarii ITP s-a modificat de la 10.10.2026 la 18.04.2027", si cu textul notificarii pastrat; (2) redeschide o notificare inchisa automat cand data revine la valoarea ei (cea rezolvata de utilizator nu se redeschide); (3) creeaza notificarea cand `zile ramase <= prag`, **fara limita inferioara**, deci si dupa scadenta, si numai daca evenimentul nu are deja una. Repository-ul are acum `CreateNotificationAsync` (INSERT IGNORE), `ResolveAsync`, `ReopenAsync`, `CountOpenAsync` in locul `SynchronizeAsync`. O sursa nelizibila isi pastreaza notificarile, afisate din valorile stocate la creare.
- **Sabloane:** un singur sablon activ pe eveniment (verificare in serviciu cu mesaj "Exista deja un sablon activ pentru acest eveniment («...»). Dezactiveaza-l mai intai." si, in plus, cheia unica din baza); dezactivarea unui sablon **nu mai ascunde** notificarile lui existente (opreste doar crearea altora); dialogul de stergere arata cate notificari nerezolvate se pierd odata cu sablonul. Textul-exemplu implicit nu mai spune "peste N zile" (nepotrivit dupa scadenta): "are data de expirare ... (zile ramase: ...; zile de depasire: ...)".
- **Marcaje si amanare:** `<zile ramase>` nu mai iese negativ (minimum 0), marcaj nou `<zile depasire>`; amanarea unei notificari depasite: 1-30 zile (campul precompletat cu 7), regula "zile ramase - 2" ramane pentru cele nedepasite.
- **Pagina `/notificari` (`Components/Pages/Notifications.razor`, `wwwroot/app.css`):** doua file, "Active (n)" si "Rezolvate (n)". In "Active": tabelul "Scadenta depasita" primul (cea mai veche scadenta prima, fond rosu deschis pastel, coloana "Depasit cu (zile)") si tabelul "Urmatoarele scadente" (cronologic); starea (nepreluata/preluata/amanata) se vede prin eticheta, nu prin pozitie. Dialogul: linia "Termenul a fost depasit cu N zile", "Marcheaza ca rezolvata" cu confirmare, "Deschide obiectul" (link, ex. `/vehicule/{id}`), preluare si amanare ca inainte. "Rezolvate": titlu, obiect, scadenta, rezolvata la, rezolvata de (utilizator sau "sistem (automat)"), motiv, cu dialog de citire; cele mai recente 500. Triunghiul din meniu numara doar notificarile nerezolvate care avertizeaza.
- **Jurnal (`Services/AuditTrail.cs`, filtrul din `Components/Pages/Audit.razor`):** "Rezolvare notificare" (utilizator), "Rezolvare automata notificare" si "Redeschidere automata notificare" (actor `sistem`, cu motivul), toate cu link spre `/notificari`.
- **Teste:** in `tests/BlazorStoc.Checks/Program.cs` verificari pure noi (limita de amanare la depasire, valoarea propusa, marcajele de zile, `IsAlert` pentru rezolvate, operatiile de jurnal, migrarea 5; verificarea "fara DROP" interzice acum doar DROP TABLE/COLUMN/DATABASE, migrarea 5 inlocuind un indice) si `MariaExtendedChecks.NotificationsAsync` rescris: sablon activ unic (serviciu si baza), notificare depasita creata o singura data si listata prima, text fara zile negative, sursa nelizibila, preluare concurenta, amanarea unei notificari depasite (max 30), rezolvare manuala (doi utilizatori, nerecreata la aceeasi data), rezolvare automata cu motiv la schimbarea datei, redeschidere la revenirea datei, obiect disparut, dezactivare fara ascundere, numarul notificarilor la stergerea sablonului, sursa reala a vehiculelor (link, motiv cu data veche si noua). Testul vehiculului sterge acum un sablon ITP ramas din sesiuni manuale pe baza de test (regula sablonului unic l-ar fi refuzat). Suita completa cu integrare: **601 PASS, 0 esecuri**, in 3 rulari consecutive (570 inainte de lucrare).
- **Verificat in browser** (instanta pe baza de test, port 5088, cu date create pentru verificare: un vehicul cu ITP depasit cu 3 zile, altul cu 10 zile): sablon ITP creat din exemplu, al doilea sablon activ refuzat cu mesajul de mai sus, cele doua tabele cu cea depasita prima si pe fond rosu, dialogul cu linia de depasire, campul de amanare precompletat cu 7, confirmarea "Marcheaza ca rezolvata" si trecerea in "Rezolvate", rezolvarea automata dupa o modificare SQL a datei ITP (motivul "Data expirarii ITP s-a modificat de la 10.10.2026 la 18.04.2027" apare in lista). Pagina `/notificari` de pe baza reala (5087) se incarca cu noua schema.
- **Neverificat:** aspectul pe ecran ingust (telefon); comportamentul pentru un utilizator cu rol limitat (preluare/amanare/rezolvare permise oricarui utilizator autentificat, sabloanele doar administratorului; acoperit automat, nu in browser); rezolvarea automata la stergerea unui vehicul real si redeschiderea, in browser (acoperite automat doar cu sursa de test); "Deschide obiectul" (linkul apare, navigarea neverificata). Vezi `docs/TESTE_RAMASE.md`.
- **Preview:** `http://127.0.0.1:5087/` (baza reala, cu noua schema) si `http://127.0.0.1:5088/` (baza de test, cu vehiculele depasite pentru demonstratie), ambele din `artifacts\t1-preview`.

## Finalizat la 30.09.2026 15:25 - Setari notificari: curatarea notificarilor rezolvate vechi (comutator, perioada, curatare zilnica, jurnal)

Task din planul `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (sectiunea 6.4 si H5), cu deciziile confirmate de utilizator la 30.09.2026. Depinde de lucrarea "Notificari: scadente depasite, stare rezolvata si lista ordonata".

- **Schema (migrarea 6, `Services/MariaSchemaMigrations.cs`):** tabel nou `notification_settings` (un singur rand, `id=1`: `purge_enabled`, `purge_months`, `last_purge_utc`, `version`). Randul este scris de aplicatie (contul de migrare nu are drepturi pe date); cat timp lipseste, se aplica valorile implicite (oprit, 12 luni). Tabelul a intrat in `MariaArchiveSchema.MigratedTables`, deci si in backup/restaurare (un test verifica ca orice tabel creat de o migrare este in lista). Migrarea este aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087) si pe `blazorstoc_test` (`--migrate-schema`).
- **Reguli (`Services/NotificationSettings.cs`):** perioada 1-60 luni (implicit 12), masurata de la momentul rezolvarii (in ora locala); data-limita = azi minus perioada; texte "au fost eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy" (si "a fost eliminata ... 1 notificare rezolvata mai veche de ..."), respectiv "vor fi eliminate ..." in confirmare; regula "scadenta zilnica" (comutator activ si nicio rulare in ziua locala curenta).
- **Ce se sterge (`ExpiryNotificationService.PlanPurgeAsync`):** numai notificari rezolvate mai vechi de limita; cele nerezolvate nu se sterg niciodata. O notificare rezolvata **manual** pentru un eveniment inca curent (acelasi obiect, aceeasi data) se pastreaza chiar peste limita, altfel evaluarea ar recrea-o nerezolvata (H5); o notificare rezolvata **automat** se sterge; daca sursa unei notificari manuale nu poate fi citita, ea se pastreaza (nimic nu se sterge "pe ghicite"); o sursa care nu mai este inregistrata nu mai poate recrea notificarea, deci se sterge.
- **Repository (`Services/MariaExpiryNotificationRepository.cs`):** `GetSettingsAsync`, `SaveSettingsAsync` (concurenta optimista cu `version`; prima salvare face INSERT si pierde daca alta sesiune a inserat deja), `DeleteResolvedAsync` (in tranzactie, un DELETE pe rand cu verificarea `version` si a `resolved_utc IS NOT NULL`, deci o notificare redeschisa intre timp nu se sterge).
- **Serviciu (`Services/ExpiryNotifications.cs`):** doar administratorul (`GetSettingsAsync`, `PreviewPurgeAsync`, `SaveSettingsAsync`). Salvarea cu comutatorul activ ruleaza curatarea imediat, deci ceea ce a anuntat confirmarea se si intampla; apoi memoreaza momentul rularii. **Curatarea zilnica** ruleaza la sfarsitul `EvaluateAsync`, cel mult o data pe zi: ziua se "revendica" mai intai printr-o scriere cu verificare de versiune (doua instante nu curata de doua ori), iar la o eroare de stergere revendicarea se restituie ca evaluarea urmatoare sa reincerce; nimic din curatare nu poate opri evaluarea. Momentele rularii folosesc ceasul serviciului (`TimeProvider`), deci sunt testabile.
- **Pagina (`Components/Shared/NotificationSettingsEditor.razor`, `Components/Pages/Settings.razor`):** Setari -> Notificari are acum doua subtaburi, "Sabloane notificari" si "Setari notificari" (navigare cu sageti). Subtabul are comutatorul "Sterge notificarile rezolvate mai vechi de:", perioada in luni, starea actuala (inclusiv ultima curatare), "Salveaza setarile" (activ numai cand ceva s-a schimbat) si "Renunta la modificari". La Off -> On si la scurtarea perioadei cat timp comutatorul este On apare popup-ul "Cu aceasta setare vor fi eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy. Continui?" cu tabelul valorilor vechi si noi si "Confirma si sterge"; daca nu exista nimic de eliminat, setarea se aplica direct; la Off nu apare popup. Dupa salvare, un mesaj spune cate notificari au fost eliminate.
- **Jurnal (`Services/AuditTrail.cs`, filtrele din `Components/Pages/Audit.razor`):** doua operatii noi si un tip nou de inregistrare: "Modificare setari curatare notificari" (tip "Setari notificari", cu valoarea veche si noua a comutatorului si a perioadei, cu link catre Setari) si "Curatare notificari rezolvate" (cu "au fost eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy"; operator: administratorul pentru cea de la activare/scurtare, `sistem` pentru cea zilnica; nu se scrie nimic cand nu s-a sters nimic).
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure noi (migrarea 6, limitele perioadei, data-limita cu luna mai scurta, textele la singular/plural, cand este scadenta rularea zilnica, valorile vechi/noi din jurnal, legaturile din jurnal); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - sectiune noua "Notification settings: clean-up of old resolved notifications" (valori implicite pana la prima salvare, doar administratorul, perioada validata, nimic salvat cand nu s-a schimbat nimic, ce se sterge si ce se pastreaza - manuala curenta, deschisa, recenta, sursa ilizibila, activarea sterge imediat si jurnalizeaza, versiune veche refuzata, scurtarea sterge mai mult, o rulare fara nimic de sters nu scrie in jurnal, oprit nu sterge, rularea zilnica o data pe zi, actor `sistem`, a doua zi din nou). Un test mai vechi ("depasita in varful listei") verifica acum numai notificarile propriului sablon, ca sa nu depinda de datele lasate pe baza de test de sesiunile manuale. Suita completa cu integrare: **636 PASS, 0 esecuri**, in 3 rulari consecutive (601 inainte de lucrare).
- **Verificat in browser** (instanta pe baza de test, port 5088, cu 4 notificari rezolvate vechi create pentru verificare): subtabul se deschide cu valorile implicite si "Salveaza setarile" inactiv; bifat + salvat -> popup cu "3 notificari rezolvate mai vechi de 30.09.2025"; "Anuleaza" nu schimba nimic; "Confirma si sterge" elimina cele 3 (cea de acum o luna ramane), mesajul de succes si starea "activa ... ultima curatare: 30.09.2026"; scurtare 12 -> 1 luna cu popup pentru inca 1 notificare; oprirea nu cere confirmare; jurnalul contine ambele operatii cu detaliile de mai sus. Pagina de pe baza reala (5087) se incarca cu setarile implicite (nu s-a modificat nicio setare pe baza reala).
- **Neverificat:** aspectul subtabului pe ecran ingust; utilizator cu rol limitat (pagina Setari este a administratorului, refuz acoperit automat); rularea zilnica pe ceas real peste miezul noptii (testata numai cu ceas simulat); restaurarea unui backup facut inainte de aceasta migrare (manifestul nu contine tabelul nou; backupurile existente din 29.09.2026 sunt anterioare si tabelelor notificarilor, deci nu s-ar fi restaurat oricum). Vezi `docs/TESTE_RAMASE.md`.
- **Preview:** `http://127.0.0.1:5087/` (baza reala, setari implicite) si `http://127.0.0.1:5088/` (baza de test), ambele din `artifacts\t2-preview`.

## Finalizat la 30.09.2026 15:28 - Puncte de lucru extinse: punct principal real, descriere, coordonate optionale si fotografii

Task din planul `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (sectiunea 3.1), cu deciziile confirmate de utilizator la 30.09.2026 (coordonate cu comutator, poze pe disc). Este baza pe care se sprijina contractele, interventiile si harta.

- **Schema (migrarea 7, `Services/MariaSchemaMigrations.cs`):** `beneficiary_work_points` primeste `description` (2000), `is_primary`, `latitude`/`longitude` (DECIMAL(9,6), NULL) cu `CHECK` "ambele sau niciuna" si o coloana generata `primary_beneficiary_id` cu cheie unica (**cel mult un punct principal pe beneficiar, garantat de baza de date**); tabel nou `service_photos` (un singur tabel pentru punct de lucru si, mai tarziu, interventie: `work_point_id`/`intervention_id`, exact unul completat - `CHECK`; `sha256` unic per proprietar; FK `RESTRICT` spre punct) si tabelele de arhiva `archive_work_points`, `archive_service_photos`. Toate intra in `MariaArchiveSchema.MigratedTables` (backup/restaurare). Aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087) si pe `blazorstoc_test`.
- **Punctul principal ca rand real:** creat in aceeasi tranzactie cu beneficiarul (`MariaBeneficiaryRepository.CreateAsync`); la editarea beneficiarului adresa si telefonul lui se sincronizeaza (versiunea randului creste numai daca s-au schimbat; verificarea "adresa ocupata de un punct suplimentar" ignora acum randul principal); nu se poate sterge separat ("dispare odata cu beneficiarul"). In dialog, adresa si telefonul lui sunt doar-citire, iar repository-ul le ignora oricum din formular; numele, descrierea, persoana de contact, coordonatele si pozele se editeaza ca la celelalte. **Backfill** (`WorkPointBackfill`, `Services/MariaWorkPointRepository.cs`): la pornire, cu contul aplicatiei, idempotent - creeaza randul principal pentru beneficiarii care nu il au (adresa normalizata calculata in C#); un punct suplimentar cu aceeasi adresa ca a beneficiarului este **promovat** la principal, nu dublat; logheaza cati beneficiari nu au adresa. Pe baza reala: 7 beneficiari, 7 randuri principale create, **2 beneficiari fara adresa** (de completat). Pana la backfill, `GetAsync` arata punctul principal derivat din beneficiar (Id 0, doar-citire).
- **Descriere si coordonate (`Services/WorkPoints.cs`):** descriere text liber (max. 2000, cu randuri noi). Comutatorul "Coordonate (optional)": oprit = punct fara coordonate (valoarea din camp se ignora); pornit = un singur camp "latitudine, longitudine" care accepta lipirea din alta harta ("45.7489, 21.2087", "45.7489 21.2087", "45,7489 21,2087", cu ";"), validat -90..90 / -180..180, rotunjit la 6 zecimale (`WorkPointCoordinates`). Fara geocodare automata (adresele nu se trimit nimanui).
- **Fotografii (`Services/ServicePhotos.cs`, `Services/MariaServicePhotoStore.cs`):** fisierele sunt pe disc (`<radacina active>\service-photos`, cheie `Database:MariaServicePhotosPath`), sub nume generat, rand in `service_photos`; doar imagini recunoscute dupa octeti (JPG, PNG, WebP, GIF), cel mult 10 MB, cel mult 20 per punct, duplicat blocat dupa `sha256`, legenda optionala (200); servite prin endpointul autorizat `/media/service-photos/{id}`. Adaugarea numara sub blocarea randului punctului (doua incarcari simultane nu depasesc limita). **Stergerea este arhivata** (ca la fisierele de proiect): randul in `archive_service_photos`, fisierul mutat in directorul de arhiva (copiat si verificat prin dimensiune si hash inainte, sters din zona live dupa commit). Backup-ul bazei nu contine pozele (risc acceptat de utilizator, ca la produse si proiecte).
- **Stergere arhivata a punctelor de lucru si a beneficiarului:** `MariaWorkPointRepository.DeleteAsync` arhiveaza acum punctul (`archive_work_points`, cu pozele lui ca relatii si fisierele mutate in arhiva, o singura tranzactie); stergerea beneficiarului arhiveaza punctele lui (principalul inclus) ca relatii si fisierele pozelor in `archive_files`, cu reverificarea setului de poze in tranzactie. Inainte, punctele suplimentare se stergeau fara arhiva.
- **Jurnal (`Services/AuditTrail.cs`, filtrele din `Components/Pages/Audit.razor`):** operatii numite exact - "Adaugare punct de lucru", "Modificare punct de lucru", "Modificare descriere punct de lucru" (doar descrierea s-a schimbat), "Modificare coordonate punct de lucru" (doar coordonatele: schimbate sau oprite), "Adaugare fotografie punct de lucru" (toate sub tipul Beneficiar, cu link catre pagina beneficiarului); stergerile (punct, fotografie) sunt "Stergere" arhivata, sub tipurile noi "Puncte de lucru (stergeri)" si "Fotografii puncte de lucru (stergeri)". Valoarea veche si noua in detalii; inainte, toate erau "Editare" generic.
- **Interfata (`Components/Pages/WorkPointEditor.razor`, `Components/Pages/BeneficiaryDetail.razor`, `wwwroot/app.css`):** tabelul de pe pagina beneficiarului are coloanele Descriere, Coordonate, Foto (numar) si arata punctul principal ca rand real, fara "Sterge"; dialogul are descrierea, comutatorul si campul de coordonate, iar la editarea unui punct existent galeria de poze (alegere multipla, legenda, miniaturi, "Sterge" in un pas cu motiv fix); un punct nou ramane deschis dupa salvare ca sa i se poata adauga poze imediat.
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure noi (citirea coordonatelor in toate formele, limite, formatare, comutator, numele operatiilor de jurnal, recunoasterea imaginilor, limite si nume de fisier, migrarea 7, registrul de arhivare); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - sectiunea "Work points" rescrisa (punct principal la creare si unic in baza, urmarea adresei si telefonului beneficiarului, punct principal needitabil la adresa/telefon si nesters, descriere si coordonate, adresa unica, `CHECK` coordonate, jurnalul, poze: stocare, citire, duplicat, non-imagine, peste 10 MB, punct inexistent, limita de 20, stergere arhivata, stergerea punctului cu poze, stergerea beneficiarului cu puncte si poze, backfill idempotent si promovare). Suita completa cu integrare: **680 PASS, 0 esecuri**, in 3 rulari consecutive (647 inainte de lucrare, cu testul vechi de puncte de lucru).
- **Corectat pe parcurs:** in MariaDB, un `UPDATE ... SET` isi vede propriile atribuiri anterioare; conditia `version=IF(address<>@address ...)` trebuia pusa inaintea coloanelor modificate (a prins-o testul).
- **Verificat in browser** (instanta pe baza de test, port 5088, cu un beneficiar creat pentru verificare): tabelul cu coloanele noi si punctul principal fara "Sterge"; punct nou cu descriere pe mai multe randuri si coordonate lipite cu virgula zecimala (afisate ca 45.7489, 21.2087), care ramane deschis pentru poze; incarcarea a doua imagini PNG cu legenda si a unui `.txt` (refuzat cu mesajul de format, restul incarcate), miniaturile se incarca prin `/media/service-photos/{id}`; stergerea unei poze; numarul de poze in tabel; editarea punctului principal (adresa si telefonul doar-citire, nume, descriere si coordonate salvate); stergerea punctului cu poza lui (arhivat, fisierul mutat, zona live goala); intrarile din jurnal cu numele exacte ale operatiilor. Pe baza reala (5087): migrarea 7 si backfill-ul rulate la pornire (7 randuri create).
- **Neverificat:** aspectul editorului si al galeriei pe ecran ingust; utilizator cu rol limitat (operatiile cer rol de operator de beneficiari, refuzul e acoperit automat); incarcarea unor fisiere reale mari (10 MB) din browser; restaurarea unui backup facut inainte de migrarea 7 (manifestul nu contine tabelele noi). Vezi `docs/TESTE_RAMASE.md`.
- **Preview:** `http://127.0.0.1:5087/` (baza reala, cu punctele principale create) si `http://127.0.0.1:5088/` (baza de test, beneficiarul "Demo Puncte SRL", id 241), ambele din `artifacts\t3-preview`.

## Finalizat la 30.09.2026 16:05 - Contracte de mentenanta si acoperirea punctelor de lucru

Task din planul `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (sectiunile 3.2, 3.3, 5), cu deciziile confirmate de utilizator la 30.09.2026 (mai multe contracte pe beneficiar, un punct in cel mult un contract activ, On/Off cu reprogramare, valabilitate optionala, contractele Off ascunse implicit).

- **Schema (migrarea 8, `Services/MariaSchemaMigrations.cs`):** `service_contracts` (numar + data structurate, `cycle_months` 1-12 cu `CHECK`, `valid_until` optional cu `CHECK` sa nu preceada data contractului, `is_active`, observatii, `version`; cheie unica `(beneficiary_id, contract_number, contract_date)`; FK `RESTRICT` spre beneficiar), `service_contract_points` (acoperirea: `contract_id`, `work_point_id`, `cycle_months` individual optional, `next_due`, `version`; cheie unica pe pereche; **`active_work_point_id` unic** = cheia "un punct - un singur contract activ", egala cu `work_point_id` cat timp contractul este activ si NULL altfel, cu `CHECK` sa nu poata fi altceva; FK `RESTRICT` spre contract si spre punctul de lucru) si `archive_service_contracts`. Toate intra in `MariaArchiveSchema.MigratedTables` (backup/restaurare). Aplicata pe `blazorstoc_test` (`--migrate-schema`) si pe baza reala `BlazorStoc` (la pornirea preview-ului 5087).
- **Reguli (`Services/ServiceContracts.cs`):** `ServiceContractNumber` citeste `26/23.09.2025`, `26 / 23.09.2025` si `26 din 23.09.2025` (numar cu majuscule, data zz.ll.aaaa validata, 2000-2100) si afiseaza canonic `26/23.09.2025`; `ServiceContractInput` (validare: ciclicitate 1-12, expirare nu inaintea datei contractului, un punct o singura data, prima scadenta obligatorie in afara de punctele mutate, observatii cel mult 1000); `ServiceDueRules` (starea afisata, nestocata: Depasita / In curand / La zi, prag implicit 30 de zile pana cand sursa de notificare a mentenantei ofera pragul sablonului; ciclicitatea efectiva; contract expirat); `ServiceContractRules` (mesaje, tinta si detalii de jurnal, actiunea exacta a unei editari).
- **Serviciu (`Services/MariaServiceContractRepository.cs`):** `GetForBeneficiary` (toate contractele On si Off cu punctele lor), `Create` (contractul nou este On), `Update` (campuri + acoperire: adaugare, scoatere, mutare, schimbare de ciclicitate sau de scadenta, intr-o singura tranzactie; versiunea contractului creste o singura data si numai daca s-a schimbat ceva), `PrepareActivation` / `Activate` (Off -> On cu scadentele alese per punct; punctele intrate intre timp in alt contract activ opresc activarea cu lista lor sau se scot din contractul reactivat), `Deactivate` (nu sterge nimic, elibereaza cheia activa), `Delete` (motiv obligatoriu, arhivat: randul in `archive_service_contracts`, acoperirea ca relatii). Fiecare scriere blocheaza mai intai randul beneficiarului (contractele, punctele si mutarile nu trec niciodata intre beneficiari), deci doua sesiuni pe acelasi beneficiar se serializeaza; cheia unica din baza ramane ultima aparare. **Mutarea ("Muta aici")** actualizeaza randul de acoperire (`contract_id`) si isi pastreaza scadenta, ciclicitatea individuala si identitatea; ambele contracte isi schimba versiunea. Un punct poate sta in mai multe contracte Off (istoric) si in cel mult unul activ.
- **Legaturi cu restul:** un punct de lucru acoperit de un contract (On sau Off) nu se sterge ("scoate-l mai intai din contract", `MariaWorkPointRepository`, cu prevalidare si in tranzactie); un beneficiar cu contracte nu se sterge (`MariaBeneficiaryRepository`); `ArchiveSchemaRegistry` / `ArchiveRequests.ServiceContract` / `ArchivePersistence` pentru arhivare.
- **Jurnal (`Services/AuditTrail.cs`, filtrele din `Components/Pages/Audit.razor`):** operatii numite exact, sub tipul Beneficiar (cu link catre pagina lui): "Adaugare contract mentenanta", "Modificare contract mentenanta", "Modificare expirare contract mentenanta" (doar valabilitatea), "Modificare ciclicitate mentenanta" (doar ciclicitatea contractului sau a unui punct), "Activare contract mentenanta" (cu punctele si scadentele veche -> noua), "Dezactivare contract mentenanta", "Adaugare punct in contract", "Scoatere punct din contract", "Reprogramare interventie mentenanta" (scadenta veche -> noua), "Mutare punct de lucru in alt contract"; stergerea este "Stergere" arhivata, sub tipul nou "Contracte mentenanta (stergeri)". Valorile veche si noua in detalii, date dd.mm.yyyy.
- **Interfata:** sectiunea "Contracte de mentenanta" pe `/beneficiari/{id}` (`Components/Pages/ServiceContractsSection.razor`): tabel cu contract, expirare (chip "Expirat"), stare On/Off, ciclicitate, puncte (expandabile, cu ciclicitatea si scadenta fiecaruia), urmatoarea scadenta cu chip de stare, Editeaza / Dezactiveaza / Activeaza / Sterge; contractele Off sunt ascunse, cu comutatorul "Arata si contractele Off (N)". Formularul (`ServiceContractEditor.razor`): campul unic numar/data, ciclicitate, valabilitate (calendar, "Fara termen"), observatii, punctele cu bife, data primei interventii per punct + "Aplica tuturor punctelor bifate", ciclicitate individuala, "Muta aici" pentru punctele din alt contract activ. Reactivarea deschide panoul de reprogramare (`ServiceContractActivationPanel.razor`): scadentele din trecut evidentiate, data noua per punct sau pentru toate, conflictele cu optiunea de a scoate punctele. Tabelul punctelor de lucru are coloana "Mentenanta" (contractul activ si scadenta cu chip de stare, sau "Contract Off"). Stiluri in `wwwroot/app.css`.
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure (citirea numarului/datei in toate formele si refuzurile, validarea intrarii, starile de scadenta, scadenta cea mai apropiata, actiunea de jurnal a unei editari, actiunile inregistrate, arhivare, migrarea 8); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - sectiunea "Maintenance contracts" (creare cu puncte, unicitate numar/data, punct al altui beneficiar refuzat, un punct in doua contracte active refuzat cu rollback complet, mutarea cu pastrarea randului si a scadentei, cheile si `CHECK`-urile din baza refuzand INSERT/UPDATE direct, editari cu jurnal exact, versiune veche refuzata, editare fara schimbari nesalvata, Off/On cu reprogramare si conflicte, punct in contract Off si in unul activ, doua sesiuni simultane pe acelasi punct si pe aceeasi versiune, punct acoperit si beneficiar cu contracte nesterse, stergere arhivata cu motiv). Suita completa cu integrare: **743 PASS, 0 esecuri**, in 3 rulari consecutive (680 inainte).
- **Verificat in browser** (instanta pe baza de test, port 5088, beneficiarul "Demo Puncte SRL", id 241): contract nou `26 din 23.09.2025` cu doua puncte (prima scadenta si ciclicitate individuala de 6 luni), tabelul cu chipuri "Depasita", expandarea punctelor, Dezactiveaza (contractul dispare din lista, apare cu "Arata si contractele Off (1)"), Activeaza cu panoul de reprogramare (scadenta noua la un punct), coloana "Mentenanta" din tabelul punctelor, refuzul stergerii unui punct acoperit. Contractul demonstrativ ramane in baza de test. Panoul de activare a aratat initial textul literal "activationError" (parametru Razor fara `@`), corectat si republicat.
- **Neverificat:** aspectul formularului si al tabelului pe ecran ingust, mutarea unui punct dintre doua contracte active din interfata (acoperita in teste), stergerea contractului din dialog in browser (acoperita in teste), rolul limitat, pragul "In curand" legat de sablon (vine cu sursa de notificare), restaurarea dintr-un backup facut inainte de migrarea 8; vezi `docs/TESTE_RAMASE.md` (N10-N12).
- **Preview:** `http://127.0.0.1:5087/` (baza reala, migrarea 8 aplicata la pornire) si `http://127.0.0.1:5088/` (baza de test), ambele din `artifacts\t3-preview`.

## Finalizat la 30.09.2026 18:50 - Registru de interventii de mentenanta si la cerere, pagina /mentenanta si campuri de fisiere cu drag and drop

Implementeaza sectiunile 3.4 si 4 din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (deciziile confirmate de utilizator la 30.09.2026). Necomis (commit doar la cerere).

- **Schema (migrarea 9, `Services/MariaSchemaMigrations.cs`):** `service_interventions` (fel `M` mentenanta / `C` la cerere, beneficiar, punct de lucru, contract, instantanee pentru numele si adresa punctului si eticheta contractului, data efectuarii, scadenta inchisa `planned_due`, varianta aleasa `next_due_basis` E/P/O, scadenta stabilita `next_due_set`, observatii, autor, versiune), chei straine `RESTRICT` catre beneficiar / punct / contract, `CHECK`-uri (felul, contractul doar la mentenanta, triplul scadenta inchisa/varianta/scadenta stabilita ori toate nule ori toate completate, scadenta stabilita dupa data efectuarii), cheia straina a fotografiilor (`service_photos.intervention_id`) si `archive_service_interventions`. Tabelele sunt in `MariaArchiveSchema.MigratedTables` (backup/restaurare).
- **Reguli si depozit:** `Services/ServiceInterventions.cs` (`ServiceInterventionRules`: cele trei variante `Options`/`ResolveDue` - din data efectuarii, din data planificata (dezactivata cand data planificata + ciclicitatea nu depaseste data efectuarii), stabilita de operator, implicit prima; `LatestMoving`/`Moves`; mesaje; text de jurnal) si `Services/MariaServiceInterventionRepository.cs`. Fiecare scriere blocheaza intai randul beneficiarului, apoi acoperirea (`FOR UPDATE`, Serializable, `RetryOnDeadlockAsync`), concurenta optimista pe `version`, jurnalul dupa commit. O interventie de mentenanta noua muta scadenta punctului (scrie `service_contract_points.next_due`, creste versiunea contractului) doar daca nu exista una cu data efectuarii mai recenta; una mai veche se inregistreaza fara sa mute nimic. La cerere nu atinge niciodata scadentele si merge pe orice punct al beneficiarului. Corectarea: observatiile oricand; data si alegerea doar la ultima interventie de mentenanta care a mutat scadenta (nu inaintea celei precedente, si doar daca scadenta punctului nu a fost schimbata de mana intre timp); la cerere isi poate schimba data. Stergerea: arhivata, cu motiv si cu pozele (fisierele trec in directorul de arhiva); daca este ultima interventie mutatoare, scadenta revine la cea inchisa (doar daca nu a fost schimbata intre timp).
- **Ocrotiri:** un contract cu interventii, un punct de lucru cu interventii si un beneficiar cu interventii nu se pot sterge (mesaj clar, verificare inainte si in tranzactie); scoaterea unui punct din contract ramane permisa (interventiile pastreaza instantaneele). `IServiceContractRepository.GetDueListAsync(includeOff)` pentru tabul "Scadente".
- **Fotografii:** `MariaServicePhotoStore` extins (`GetForIntervention`, `CountsForInterventions`, `AddToIntervention`), aceleasi reguli ca la punctele de lucru (10 MB, 20 per interventie, duplicat refuzat), jurnal "Adaugare fotografie interventie", stergere arhivata.
- **Jurnal (actiuni numite exact, `Services/AuditTrail.cs`):** "Inregistrare interventie mentenanta" (varianta aleasa si scadenta veche -> noua, sau "Nu modifica scadenta"), "Inregistrare interventie la cerere", "Modificare interventie mentenanta", "Modificare interventie la cerere", "Adaugare fotografie interventie" (toate cu link catre beneficiar), iar stergerea este "Stergere" arhivata sub tipul nou "Interventii mentenanta (stergeri)" cu felul in text; filtre noi in `Audit.razor`.
- **Interfata:** `Components/Pages/ServiceInterventionsSection.razor` + `ServiceInterventionEditor.razor` pe `/beneficiari/{id}` (filtru "Fel", tabel cu scadenta inchisa -> stabilita si varianta, foto, Editeaza/Sterge cu dialog de motiv); formularul alege felul, punctul (la mentenanta doar cele sub contract activ), data, observatiile si cele trei variante de scadenta cu calendar pentru varianta "Stabilita de mine"; fisa ramane deschisa dupa inregistrare ca sa se poata adauga poze. Pagina noua `Components/Pages/ServiceMaintenance.razor` (`/mentenanta`, link in meniu inaintea "Notificari"): tab "Scadente" (filtre text/stare, "Arata si contractele Off (N)" cu randuri gri fara stare de scadenta) si tab "Registru interventii" (text, fel, perioada, paginare, legaturi catre beneficiari). Stiluri in `wwwroot/app.css`.
- **Campuri de fisiere cu drag and drop (cerere ulterioara a utilizatorului):** componenta noua `Components/Shared/FileDropZone.razor` + `wwwroot/file-drop.js` (delegat pe document: fisierele lasate pe zona ajung in `InputFile`-ul ascuns printr-un eveniment `change`, deci `OnChange` ruleaza ca la o alegere normala; un camp cu un singur fisier ia doar primul, fisierele care nu respecta `accept` sunt ignorate; o lasare in afara zonelor nu mai deschide fisierul in browser). Inlocuieste campurile de fisiere din editorul de produs (imaginea), din observatia de proiect (fisiere), din editorul punctului de lucru si din cel al interventiei (fotografii); butonul "Alege ..." este stilizat (`.file-drop`, plus `::file-selector-button` pentru orice camp nativ ramas). Preluarea inventarului avea deja zona de drag and drop si un buton stilizat si nu s-a schimbat.
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure (variantele, luna scurta, planificata dezactivata, `ResolveDue`, `LatestMoving`/`Moves`, coduri, validarea intrarii, actiunile de jurnal, arhivare, migrarea 9); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - sectiunea "Maintenance interventions" (E/P/O, exemplul din plan, interventie mai veche care nu muta nimic, refuzuri, jurnal exact, `CHECK`-urile si cheile din baza pe INSERT direct, corectari, poze, registrul cu filtre si paginare, lista de scadente, doua sesiuni simultane pe acelasi punct, stergere cu revenirea scadentei si cu scadenta schimbata de mana, ocrotirile contract/punct/beneficiar, arhivare). Suita completa cu integrare: **815 PASS, 0 esecuri**, in 3 rulari consecutive (743 inainte). Erori gasite si corectate: sintaxa `ADD FOREIGN KEY IF NOT EXISTS` in migrare, precizia `recorded_utc` (rotunjire la milisecunde inainte de comparatia de versiune).
- **Verificat in browser** (baza de test, port 5088, beneficiarul 241): formularul cu cele trei variante (30.12.2026 / 05.02.2027 / alegere), inregistrare cu "Stabilita de mine" (scadenta 01.03.2027), fisa ramasa deschisa, poza incarcata, interventie mai veche ("nu modifica scadenta"), interventie la cerere pe alt punct, stergerea celei mutatoare cu dialogul in doi pasi (scadenta a revenit la 05.11.2026), `/mentenanta` (ambele taburi), lasare de fisier pe zona de drag and drop (un `.txt` ignorat, un `.png` incarcat). Datele demonstrative raman in baza de test (o interventie de mentenanta mai veche, una la cerere cu o poza).
- **Nefacut prin decizie:** inregistrarea pe mai multe puncte deodata dintr-un singur dialog (optionala in plan); se inregistreaza cate un punct.
- **Neverificat:** aspectul pe ecran ingust, fotografii reale, rolul limitat, stergerea unui punct cu interventii din interfata, restaurarea dintr-un backup anterior migrarii 9, tragerea reala a fisierelor cu mouse-ul (testata prin evenimente simulate); vezi `docs/TESTE_RAMASE.md` (N13-N16).
- **Preview:** `http://127.0.0.1:5087/` (baza reala, migrarea 9 aplicata la pornire) si `http://127.0.0.1:5088/` (baza de test), ambele din `artifacts\t3-preview`.

## Finalizat la 30.09.2026 19:45 - Surse de notificare pentru mentenanta (scadenta interventiei si expirarea contractului)

Implementeaza sectiunile 6.2 si 6.3 din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md`. Necomis (commit doar la cerere).

- **Surse (`Services/MaintenanceNotificationSources.cs`):** `MaintenanceDueSource` (`mentenanta.scadenta`, categoria "Mentenanta", evenimentul "Interventie de mentenanta": o instanta pe fiecare punct acoperit de un contract ACTIV, `ObjectId` = id-ul acoperirii, data = `next_due`, eticheta "Beneficiar · Punct de lucru", link `/beneficiari/{id}`) si `ContractExpirySource` (`contract.expirare`, evenimentul "Expirare contract": o instanta pe fiecare contract activ cu `valid_until`, eticheta "Beneficiar · Contract 26/23.09.2025"). Marcaje: `<beneficiar>`, `<punct de lucru>`, `<adresa punct de lucru>`, `<numar contract>`, `<data ultima interventie>` (ultima interventie DE MENTENANTA a punctului; cele la cerere nu se numara; "nicio interventie" cand nu exista) pentru prima sursa; `<beneficiar>`, `<numar contract>`, `<data contract>` pentru a doua; plus cele comune (`<data expirare>`, `<zile ramase>`, `<zile depasire>`, `<eveniment>`). Contractele fara termen sau Off, si punctele din contracte Off, nu produc notificari. Cheile sunt constante in `ExpirySourceKeys` (`MaintenanceDue`, `ContractExpiry`); constructorul accepta o cheie proprie (folosita de teste ca sa nu atinga sabloanele reale).
- **Citirea fara drept de operator:** `IMaintenanceNotificationReader` / `MariaMaintenanceNotificationReader` (doua interogari de citire, `service_interventions` pentru ultima data). Repozitoriile de contracte si interventii cer operator de beneficiari; evaluarea notificarilor ruleaza pentru oricine deschide prima data aplicatia, iar o sursa care esueaza este sarita, deci notificarile ar fi ramas nesincronizate pana la un operator.
- **Motor (`Services/ExpiryNotifications.cs`), completari generale:** `IExpirySource` primeste membri cu implementare implicita: `DateChangedReason(from, to, current)` (mesajul scris la inchiderea automata cand data se schimba; implicit textul de pana acum, deci ITP/asigurare/rovinieta nu se schimba), `DefaultSubject`/`DefaultBody` (textul propus de butonul "Completeaza un text-exemplu" din formularul de sabloane, per sursa). Motivele noi: "Scadenta interventiei de mentenanta s-a modificat de la 18.01.2026 la 05.05.2026 (ultima interventie de mentenanta: 05.02.2026)." si "Data expirarii contractului s-a modificat de la ... la ..."; la disparitia obiectului: "Punctul de lucru nu mai este acoperit de un contract activ (contractul a fost dezactivat sau sters, ori punctul a fost scos din contract)." / "Contractul a fost dezactivat, sters sau nu mai are termen de expirare.". Redeschiderea automata cand data revine (de exemplu dupa stergerea interventiei) merge deja generic.
- **Pragul "In curand":** `IExpiryNotificationService.GetThresholdDaysAsync(sourceKey, defaultDays)` (citit de orice utilizator autentificat) da pragul sablonului activ al sursei, sau 30 daca nu exista. Il folosesc `/mentenanta`, tabelul de puncte din pagina beneficiarului si sectiunea de contracte (`ServiceContractsSection.ThresholdDays`); `ServiceDueRules.DefaultThresholdDays` ramane doar implicit.
- **Interfata:** sursele apar singure in Setari -> Notificari -> Sabloane la categoria "Mentenanta" (formularul, marcajele si previzualizarea sunt cele generice); Program.cs inregistreaza cititorul si cele doua surse.
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure cu un cititor fals (chei, categorie, instantele, textele propuse randate, marcajele acceptate si refuzate, motivele, textul implicit al celorlalte surse); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - sectiunea "Maintenance notifications" (sabloane cu chei proprii, evaluare pornita de un utilizator fara drepturi, notificarea unui punct in perioada si a unui contract care expira, pragul, interventie la cerere care nu inchide nimic, interventie de mentenanta care inchide automat cu data veche/noua si data interventiei, jurnalul "Rezolvare automata notificare", stergerea interventiei care redeschide, prelungirea contractului care inchide si revenirea care redeschide, reprogramarea unui punct in perioada, contractul Off care inchide ambele feluri, pragul dupa editarea sablonului). Suita completa cu integrare: **835 PASS, 0 esecuri**, in 3 rulari consecutive (815 inainte).
- **Verificat in browser** (baza de test, port 5088): categoria "Mentenanta" in formularul de sabloane, cele doua evenimente, text-exemplu si marcajele, sablon nou cu prag 60 -> notificarile "Scadenta depasita" (Depozit, 345 zile) si "Urmatoarele scadente" (Sediu central, 36 zile) pe `/notificari`, iar pe `/mentenanta` scadenta din 05.11.2026 (36 de zile) apare "In curand" (cu pragul implicit de 30 ar fi fost "La zi"). Sablonul demonstrativ ramane in baza de test; pe baza reala nu s-a creat niciun sablon.
- **Neverificat:** aspectul pe ecran ingust, rularea la interval real (5 minute) cu mai multe sesiuni, un sablon pentru `contract.expirare` creat din interfata (acoperit in teste); vezi `docs/TESTE_RAMASE.md` (N17-N18).
- **Preview:** `http://127.0.0.1:5087/` (baza reala) si `http://127.0.0.1:5088/` (baza de test), ambele din `artifacts\t3-preview`.

## Finalizat la 30.09.2026 20:30 - Harta de mentenanta (/mentenanta/harta)

Implementeaza `docs/PROPUNERE_HARTA_MENTENANTA.md` cu evaluarea de la sfarsitul ei si confirmarile utilizatorului din 30.09.2026. Necomis (commit doar la cerere).

- **Descarcare aprobata de utilizator:** Leaflet 1.9.4 (`leaflet.js`, `leaflet.css`, imaginile din `dist/images`) si Leaflet.markercluster 1.5.3 (`leaflet.markercluster.js`, `MarkerCluster.css`, `MarkerCluster.Default.css`), luate din registrul npm prin unpkg.com si puse in `wwwroot/lib/leaflet` (servite local, fara CDN), cu licentele (BSD-2 / MIT) alaturi. Bibliotecile se incarca doar cand se deschide pagina hartii.
- **Date:** fara schimbari de schema si fara API nou. `ServiceDueRow` (lista de scadente, aceeasi interogare ca tabul "Scadente" si ca sursele de notificare) primeste `Latitude`, `Longitude` (ale punctului de lucru) si `LastIntervention` (ultima interventie de mentenanta); `HasCoordinates`. Pagina apeleaza direct serviciul (autorizarea operatorului ca la `/mentenanta`).
- **Reguli (`Services/MaintenanceMap.cs`, `Services/ServiceContracts.cs`):** `MaintenanceMapRules` - umplerea marker-ului arata numai starea scadentei (depasita / in curand / la zi; gri pentru contract Off), insigna numai starea contractului (expirat / expira in curand; nimic la fara termen, valabil sau Off), deci cele doua stari nu se mai amesteca; `Marker` (campuri simple pentru script; punctele fara coordonate nu primesc marker), `SameLocation`, `AddInterventionUrl`. `ServiceDueRules.ExpiryState` / `ExpiryText` ("Expirat de N zile", "Expira in N zile", "Fara termen", "Valabil pana la ..."). Pragurile sunt cele ale sabloanelor active (`mentenanta.scadenta` pentru scadenta, `contract.expirare` pentru insigna), 30 de zile implicit. Un contract expirat dar On ramane urmarit (insigna "E").
- **Furnizorul de dale (`Services/MapOptions.cs`, sectiunea `Map` din `appsettings.json`):** URL-ul sablon, atribuirea, zoom minim/maxim si centrul initial, independente de codul marker-elor; se accepta numai https cu `{z}/{x}/{y}`, altfel revine la OpenStreetMap standard. Fara fallback automat intre provideri (decizie din evaluare, sectiunea C): la esecul a cel putin 6 dale in 10 secunde se afiseaza un mesaj vizibil cu buton "Reincearca", iar punctele raman afisate. Atribuirea furnizorului ramane vizibila pe harta (Leaflet). Browserul cere doar dalele zonei vizibile; nicio data a beneficiarilor nu intra in adresa dalelor. Cheile API, daca un furnizor le cere, se pun in configuratia privata.
- **Interfata:** `Components/Pages/MaintenanceMap.razor` + `wwwroot/maintenance-map.js` (modul incarcat cu `import`, o singura instanta Leaflet pe durata paginii, marker-ele se inlocuiesc fara sa se recreeze harta, `dispose` la parasire, clusterizare). Filtre: text (beneficiar, punct, adresa, contract), starea scadentei, starea contractului, interval de scadente, "Arata si contractele Off (N)". "Incadreaza rezultatele" doar la prima incarcare si la apasare. Legenda, panou lateral cu beneficiar (link), punct, adresa, coordonate, contract On/Off, termen, ciclicitate, scadenta cu chip, ultima interventie, butoane "Fisa beneficiarului" si "+ Inregistreaza interventie" (deschide `/beneficiari/{id}?adauga-interventie={punct}`, cu formularul de interventie deja pe acel punct; un punct fara contract activ primeste "La cerere"), lista celorlalte puncte de la aceleasi coordonate, lista "Puncte fara coordonate" cu legatura catre fisa pentru completare (fara geocodare automata). Continutul panoului si titlurile marker-elor nu sunt HTML construit din date: marker-ul primeste doar clase si un glif fix. Legatura din `/mentenanta` (butonul "Harta").
- **Teste:** `tests/BlazorStoc.Checks/Program.cs` - verificari pure (starea si textul termenului contractului, umplerea si insigna separate, marker si randuri fara coordonate, coordonate/linkuri, puncte la aceeasi locatie, validarea si revenirile configuratiei furnizorului); `tests/BlazorStoc.Checks/MariaExtendedChecks.cs` - lista de scadente cu coordonate si ultima interventie. Suita completa cu integrare: **845 PASS, 0 esecuri**, in 3 rulari consecutive (835 inainte).
- **Verificat in browser** (baza de test, port 5088, beneficiarul 241, doua puncte la Timisoara): dale reale OpenStreetMap, marker-e cu umplere galbena / rosie, insigna "expirat" (contractul demonstrativ a fost expirat pe moment, apoi readus fara termen), panoul de detalii la apasarea marker-ului, filtrul "Depasite" fara reinitializarea hartii, lista fara coordonate (inainte de a pune coordonatele si pe al doilea punct), linkul "Inregistreaza interventie" cu punctul preselectat. Coordonatele celor doua puncte demonstrative raman in baza de test.
- **Neverificat:** aspectul pe ecran ingust, comportamentul cand furnizorul de dale nu raspunde (mesajul si "Reincearca"), clusterizarea cu multe puncte, conditiile de utilizare si limitele serverului public OSM pentru utilizarea interna (de verificat de proprietar; nu se preincarca si nu se descarca dale), un furnizor cu cheie; vezi `docs/TESTE_RAMASE.md` (N19-N21).
- **Preview:** `http://127.0.0.1:5087/` (baza reala) si `http://127.0.0.1:5088/` (baza de test), ambele din `artifacts\t3-preview`.

## Finalizat la 30.09.2026 19:04 — Jurnal: link la sablon, mesaj ANAF HTTP clar, parola de confirmare la utilizatori

- **Jurnal, link la sablon:** linkul din coloana „Tinta" pentru adaugare/modificare sablon ducea la `/setari` (tab-ul implicit „Preluare date ANAF"). Acum duce la `/setari?tab=notificari&subtab=templates&sablon={id}`: se deschide tab-ul „Notificari", sub-tab-ul „Sabloane notificari", randul sablonului este evidentiat (`.row-focus`) si adus in vedere (`wwwroot/scroll-to.js`). Un sablon sters de atunci afiseaza „Sablonul #N nu mai exista". Intrarile despre setarile de curatare duc la sub-tab-ul „Setari notificari". Fisiere: `Services/AuditTrail.cs` (`SettingsNavigation`), `Components/Pages/Settings.razor` (parametri din adresa; clicurile ulterioare pe tab-uri raman prioritare), `Components/Shared/NotificationTemplatesEditor.razor`, `Components/App.razor`, `wwwroot/app.css`.
- **Mesaj ANAF HTTP:** `AnafRules.HttpErrorMessage(status)` inlocuieste „ANAF a raspuns cu HTTP 404." cu mesaje explicative (400, 401/403, 404, 429, 5xx, altele): 404 spune ca nu e vorba de CUI, ci de adresa serviciului din Setari sau de un serviciu mutat/retras, cere anuntarea administratorului si aminteste completarea manuala. Apare in formularul de beneficiar si in testul din configurator.
- **Formular utilizator:** parola minima scazuta de la 12 la 8 caractere (`WebUserInput.MinimumPasswordLength`); camp nou „Confirma parola" (si „Confirma parola noua" la editare, obligatoriu doar daca se scrie o parola noua), verificat in formular (`PasswordConfirmationError`), nepersistat si nejurnalizat. Pornirea aplicatiei cere in continuare `Authentication:Password` de minimum 12 caractere (parola contului initial din configurare, nemodificata).
- **Verificari:** `tests/BlazorStoc.Checks/Program.cs` — 492 PASS, 0 esecuri (adaugate: parola de 7/8 caractere, confirmare, linkurile sablonului si ale setarilor de notificari, mesajele HTTP ANAF). Suita cu integrare MariaDB nu a fost rulata in acest ciclu.
- **Neverificat in browser:** evidentierea si derularea randului (pagina cere autentificare de administrator); preview: `http://127.0.0.1:5087/`.

## Finalizat la 30.09.2026 19:40 — Harta de mentenanta: fereastra independenta si actualizare automata

- **Fereastra separata:** butonul „Fereastra separata" de pe `/mentenanta/harta` deschide pagina `/mentenanta/harta/fereastra` intr-o fereastra popup cu nume fix (`blazorstoc-harta`, un singur exemplar reutilizat), fara meniu si antet, cu harta pe inaltimea ferestrei, filtrele si panoul de detalii, pentru un al doilea monitor. Este un link obisnuit interceptat de `wwwroot/map-window.js` (`window.open` direct in clic, altfel browserul blocheaza popup-ul; fara script se deschide intr-un tab). In fereastra, linkurile catre aplicatie (fisa beneficiarului, „Inregistreaza interventie", lista) se deschid in fereastra principala care a deschis-o (`window.opener`), iar daca aceasta este inchisa, intr-un tab nou. Aceeasi autentificare si aceleasi drepturi (`[Authorize]`).
- **Componente:** pagina a fost mutata in `Components/Shared/MaintenanceMapView.razor` (parametrul `Popout`), cu doua pagini subtiri (`Pages/MaintenanceMap.razor`, `Pages/MaintenanceMapWindow.razor`) si `Layout/PopoutLayout.razor`. Regulile noi sunt in `MaintenanceMapRules` (`WindowUrl`, `WindowName`, `AutoRefreshSeconds`, `ChangedRows`).
- **Actualizare automata numai cat harta e vizibila:** `wwwroot/maintenance-map.js` (`watch`) cere paginii o reincarcare la 30 s doar daca fereastra/tab-ul nu este ascuns (Page Visibility API) si harta este pe ecran (IntersectionObserver); la revenirea in vizibil (dupa mai mult de 5 s) verifica imediat. Se reincarca numai datele overlay-ului (lista scadentelor si pragurile); dalele hartii nu se reincarca (le cere Leaflet la deplasare/zoom). Se pastreaza pozitia, zoom-ul, filtrele, punctul selectat si panoul; nu se reincadreaza. Marker-ele noi sau schimbate pulseaza 8 s (`.mm-flash`); antetul arata „Actualizat la hh:mm:ss · N puncte noi sau modificate", iar la esec „Actualizarea automata a esuat, se reincearca" (harta ramane neschimbata). Culorile se recalculeaza si la trecerea in ziua urmatoare.
- **Abatere fata de propunere:** in loc de un „numar de versiune" separat, fiecare verificare reciteste lista scadentelor (aceeasi interogare ca la deschidere) si o compara prin valoare cu cea afisata; la 30 s si numai pentru harti vizibile costul este mic, si nu s-a adaugat cod nou in depozitele de contracte.
- **Verificari:** `tests/BlazorStoc.Checks/Program.cs` (`ChangedRows`, adresa si numele ferestrei, ritmul reincarcarii). **Verificat in browser** (baza de test, port 5088): butonul si linkul ferestrei, pagina ferestrei fara meniu, reincarcarea la revenirea in vizibil, detectarea unui punct modificat direct in baza („Actualizat la ... · 1 punct nou sau modificat"; valoarea a fost readusa). **Neverificat:** doua monitoare reale, blocarea popup-ului de catre browser, pulsarea marker-ului (punctele de test sunt grupate intr-un cluster), sesiunea expirata; vezi `docs/TESTE_RAMASE.md` (N22-N23).

## Finalizat la 30.09.2026 19:40 — Jurnal: filtrare, cautare si paginare pe server, ultimele 500 de evenimente si pagina „Jurnal complet"

- **Interogare pe server:** `IAuditTrail` are acum `QueryAsync(AuditQuery, pagina, marime, fereastra)`, `SummaryAsync` si `RemovalTimesAsync`; `GetEventsAsync` ramane (LIMIT 2000) pentru teste si alte utilizari. `MariaAuditTrail` aplica in SQL filtrele (tip, operatie - „Modificare" vechi conta ca „Editare", operator fara deosebire de litere, interval de date ca instante UTC - inclusiv inceputul, exclusiv sfarsitul -, text literal in operator/tinta/detalii/motiv cu `%` si `_` scapate), numara rezultatele si returneaza doar pagina ceruta (`ORDER BY timestamp_utc DESC, id DESC LIMIT/OFFSET`). „Fereastra" (ultimele N evenimente) este un subselect. `FileAuditTrail` raspunde acelorasi interogari in memorie (`AuditQueryRules`, aceleasi reguli). Nu s-au adaugat indecsi noi: `ix_audit_events_timestamp` acopera ordonarea, iar coloanele `entity_type`/`action`/`actor_username` sunt LONGTEXT (indexabile doar cu prefix, prin DDL cu contul de migrare); de reconsiderat daca masuratorile pe un jurnal foarte mare o cer.
- **Pagina „Jurnal activitate" (`/jurnal`):** lucreaza pe ultimele 500 de evenimente (`AuditQueryRules.RecentWindow`), cu filtre, cautare si paginare pe server (10/20/50/Toate = cele 500), cautare cu asteptare de 350 ms dupa ultima tasta, sincronizare la 15 s pastrata, cartonasele de sus calculate de server pe tot jurnalul, un banner care spune ca sunt mai multe evenimente si trimite la pagina completa cu filtrele alese. Linkurile catre obiecte sterse se decid printr-o interogare dedicata (`RemovalTimesAsync`) pentru obiectele paginii, nu prin evenimentele incarcate.
- **Pagina „Jurnal complet" (`/jurnal/complet`, administrator):** tot jurnalul, pagini de 25/50/100 pe server, interval de date implicit „ultima luna" (butoane „Ultima luna", „Ultimele 3 luni", „Tot jurnalul" si doua campuri de data doar-din-calendar), aceleasi filtre, fara sincronizare automata (butonul „Actualizeaza"), starea in adresa (`AuditCompleteState`: `q`, `tip`, `operatie`, `operator`, `de-la`, `pana-la`, `tot=1`, `pe-pagina`, `pagina`). **Export CSV** (separator `;`, valori intre ghilimele, celulele ce ar putea fi citite ca formula sunt prefixate cu apostrof, UTF-8 cu BOM, ore UTC `dd.MM.yyyy HH:mm:ss`), cel mult 50.000 de randuri; exportul se jurnalizeaza INAINTE de descarcare cu actiunea exacta „Export jurnal" (tip „Jurnal", detalii: numarul de evenimente si filtrele folosite) si, daca jurnalizarea esueaza, nu se descarca nimic.
- **Componente comune:** `Shared/AuditFilterBar.razor`, `AuditEventsTable.razor` (ore locale prin `wwwroot/audit-time.js`, refacere a pozitiei de derulare), `AuditPager.razor`; ziua/intervalul ales devine instante UTC in browser (`localRangeUtc`, fusul orar afisat este al browserului). Rute: `/jurnal/complet` (in propunere `/audit/complet`).
- **Verificari:** suita completa cu integrare MariaDB: **879 PASS, 0 esecuri** (492 inainte de aceste doua taskuri fara integrare; 506+ verificari pure acum): reguli de fereastra/filtre/pagini/CSV/adresa, `FileAuditTrail`, iar pe `blazorstoc_test` sectiunea „Journal: server-side filtering..." (pagini, total, ferestre, filtre, text literal, intervale, timpi de stergere, sumar). Un defect gasit la verificarea in browser (stil de parsare invalid la intervalul UTC care facea sa esueze incarcarea paginii) a fost corectat si acoperit de test. **Verificat in browser** (baza de test, 9.431 de evenimente): `/jurnal` (500, banner, cartonase), `/jurnal/complet` (pagina 3 din 378, cautare cu litere mari si diacritice, filtru pe zi din clic pe data, export CSV cu jurnalizarea „Export jurnal" si filtrele in detalii). **Neverificat:** timpii pe un jurnal foarte mare (200.000 de evenimente), descarcarea efectiva a fisierului CSV in browser (s-a verificat crearea si numele fisierului), aspectul pe ecran ingust; vezi `docs/TESTE_RAMASE.md` (N24-N25).

## Finalizat la 30.09.2026 20:37 — Formular utilizator: campuri de parola aliniate, verificare la tastare, „Arata parola"

- **`Components/Pages/UserEditor.razor`:** „Parola" si „Confirma parola" stau pe acelasi rand, aliniate sus; ambele se actualizeaza la fiecare tasta (`InputTextLive`), iar sub „Confirma parola" apare imediat „Parolele coincid." / „Parolele nu coincid." (si la modificarea primei parole dupa ce a doua a fost scrisa); mesajul de la salvarea refuzata ramane pana cand coincid. „Arata parola" este un comutator glisant (`role=switch`, stil `.toggle-switch` in `wwwroot/app.css`) care schimba ambele campuri intre `password` si `text`, la adaugare si la modificare. Nivelul de acces a coborat pe randul urmator.
- **Verificat in browser** (baza de test, 5088): concordanta la tastare (partial / identic / dupa modificarea primei parole), comutarea tipului campurilor, stilul verde al comutatorului cand este activ. **Neverificat:** alinierea pe ecran larg si tranzitia comutatorului (panoul de verificare este ascuns si nu ruleaza tranzitiile).

## Finalizat la 30.09.2026 20:54 — Setari harta: motor harta si overlay (tipuri de pinuri)

- **Setari → Harta (administrator):** tab nou cu doua sub-tab-uri, `Motor harta` si `Overlay (tipuri de pinuri)`. Configuratia se pastreaza intr-un fisier JSON (`data/map-configuration.json`, cale din `Map:ConfigurationPath`, scriere atomica, versiune pentru ciocniri intre administratori), ca la ANAF: fara schimbare de schema. Pana la prima salvare se folosesc valorile din `appsettings.json` (sectiunea `Map`) si tipurile predefinite; un fisier deteriorat revine la valorile implicite. Cod: `Services/MapConfiguration.cs` (`MapConfigurationService`, `MapEngineRules`, `MapPinRules`, `MapConfigStore`), `Components/Shared/MapEngineEditor.razor`, `MapPinTypesEditor.razor`, `wwwroot/map-settings.js`.
- **Motor harta:** adresa dalelor (https, `{z}/{x}/{y}`, optional `{s}`, `{r}`, `{key}`; fara credentiale in adresa), atribuire ca text plus adresa https optionala (marcajul este generat de aplicatie, nu se accepta HTML), zoom minim/maxim/de pornire, centrul hartii, cheie furnizor optionala (nu se mai afiseaza dupa salvare, nu apare in jurnal; „Sterge cheia salvata"). Butonul „Testeaza furnizorul" incarca in browser o dala reala (zoom 3) si arata dala si timpul; dialog de confirmare cu valorile vechi si noi; „Revino la valorile din configuratia aplicatiei". Hartile deschise preiau modificarea in cel mult 30 s (la reincarcarea automata; `configure` in `maintenance-map.js` schimba adresa dalelor, atribuirea si limitele de zoom fara sa recreeze harta).
- **Overlay:** 4 tipuri de umplere (scadenta depasita / in curand / la zi / contract Off) si 2 insigne (contract expirat / expira in curand) predefinite, cu denumire (legenda), culoare, simbol (2 caractere), ordine si, la insigne, activ; nu se sterg si nu isi schimba regula, iar umplerile nu pot fi dezactivate; doua umpleri nu pot avea aceeasi culoare. Pana la 3 **insigne proprii** cu regula „nicio interventie de mentenanta de cel putin N luni (sau niciodata)", pentru contracte pornite (On), cu adaugare, editare, dezactivare si stergere cu motiv. Culoarea simbolului (alb sau inchis) se alege automat dupa contrast. Legenda hartii, pinii (culoare inline validata #rrggbb, simboluri ca text) si panoul se construiesc din aceste tipuri (`MaintenanceMapView`).
- **Jurnal:** actiuni exacte „Modificare furnizor harta", „Resetare furnizor harta", „Adaugare tip pin harta", „Modificare tip pin harta", „Stergere tip pin harta" (tipuri `FurnizorHarta`, `TipPinHarta`), cu valoarea veche si noua, in filtrul paginii Jurnal; legaturile duc la `/setari?tab=harta&subtab=motor` si `...subtab=overlay&pin=ID` (randul evidentiat).
- **Verificari:** `tests/BlazorStoc.Checks/Program.cs`: 535 PASS fara integrare (reguli de adresa, atribuire sigura, cheie, tipuri de pinuri, contrast, insigne proprii, marker, legaturi de jurnal, serviciul cu fisier temporar: salvare, versiuni, refuzuri, jurnalizare, revenire, fisier deteriorat). **Verificat in browser** (baza de test, 5088): tab-urile, lista tipurilor, adaugarea unei insigne proprii, legenda si pinul cu insigna pe harta, testul furnizorului (dala reala in 40 ms), salvarea cu dialog de confirmare, revenirea, stergerea cu motiv si intrarile din jurnal cu linkuri; datele de proba au fost sterse. **Neverificat:** un furnizor cu cheie, aspectul pe ecran ingust, actualizarea dalelor pe o harta deschisa dupa schimbarea furnizorului (panoul de verificare este ascuns), culorile predefinite ale simbolurilor (acum alese dupa contrast: textul este inchis pe galben, gri si portocaliu); vezi `docs/TESTE_RAMASE.md` (N26-N27). Nota: fisierul `data/map-configuration.json` este comun preview-urilor 5087 si 5088 (acelasi director `data`).
