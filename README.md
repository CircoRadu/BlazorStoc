# BlazorStoc 0.3

## Colaborare Codex–Claude

Dezvoltarea folosește cicluri strict secvențiale Codex–Claude: agentul activ modifică proiectul, verifică, documentează, face commit și se oprește înainte ca celălalt agent să preia lucrul. Protocolul complet este în [`docs/SEQUENTIAL_COLLABORATION.md`](docs/SEQUENTIAL_COLLABORATION.md), iar starea curentă este în [`docs/PROJECT_STATE.md`](docs/PROJECT_STATE.md).

Migrarea ESP_Stoc: CRUD produse, beneficiari și utilizatori web, două niveluri de acces, catalog, căutare, filtre, paginare și detalii. Interfața este în română, exclusiv pentru browser pe calculator (lățime recomandată minimum 1100 px). Versiunea mobilă nu face parte din această etapă.

Blazor Web App / Interactive Server, .NET 9, MySqlConnector 2.6.2. Aplicația rulează direct cu .NET, fără Docker. Conexiunea interactivă necesită WebSockets sau fallback SignalR și conectivitate continuă.

Faza curentă acoperă dezvoltarea și verificarea locală. Integrarea, configurarea și publicarea pe NAS/QNAP sunt amânate și nu fac parte din instrucțiunile de mai jos.

## Ce este inclus

- Mod demonstrativ cu exemple fictive, fără acces SQL, activat implicit.
- CRUD MariaDB pentru produse, cu categorii/subcategorii create din formular, tranzacții, jurnal și verificarea modificărilor simultane.
- Imagine obligatorie la crearea produsului, selectabilă din fișier sau din clipboard, cu thumbnail în catalog și vizualizare mărită.
- Autentificare web cu un cont configurabil, cookie de sesiune, protecție antiforgery și limitarea cererilor de login.
- Autentificare cu utilizatori din baza de date și parole PBKDF2. Parolele nu sunt stocate sau afișate în clar și nu folosesc valorile Base64 din aplicația veche.
- Rol `Administrator`: CRUD produse, beneficiari și utilizatori. Rol `Utilizator`: CRUD produse și beneficiari, fără acces la meniul sau pagina de administrare a conturilor.
- Registru Beneficiari cu nume și CUI unic, căutare, validare, actualizare automată la 15 secunde și protecție împotriva suprascrierilor concurente.
- Protecții pentru păstrarea unui administrator activ, blocarea ștergerii/dezactivării propriului cont și verificarea modificărilor concurente.
- Jurnal persistent pentru toate adăugările, modificările și ștergerile de produse, beneficiari sau utilizatori, cu timestamp UTC, operator, rol și detalii fără parole.
- Dashboard **Jurnal activitate**, disponibil numai administratorilor, cu filtrare și sincronizare automată la 15 secunde.
- Flux asincron complet pentru operațiile SQL, autentificare și evenimentele Blazor care declanșează încărcări sau scrieri.
- Conexiuni deschise și închise asincron după fiecare operație, pool de maximum 10 conexiuni și fără reîncercări automate.
- Încărcarea produselor și a grupurilor categorie/subcategorie rulează în paralel, cu anulare la închiderea componentei.
- Configurare locală prin `appsettings.json` și variabile de mediu .NET.

Stocul unui produs se modifică numai prin intrări și ieșiri (pagina „Intrări/ieșiri” a produsului); produsele noi au stoc 0 și cantitatea nu se introduce manual. Include gestiunea beneficiarilor. Nu include încă importul datelor, imaginile, export Excel, rapoarte sau conturile individuale din aplicația veche. Nu citește parolele Base64 existente.

## Beneficiari

Administratorii și utilizatorii cu acces limitat pot lista, căuta, adăuga, edita și șterge beneficiari. Fiecare beneficiar are momentan nume și CUI; CUI-ul acceptă 2–10 cifre, cu prefixul opțional `RO`, și trebuie să fie unic. Un beneficiar legat de mișcări de stoc nu poate fi șters.

În modul MariaDB, prima accesare a secțiunii completează direct tabela existentă `beneficiar` cu coloanele necesare pentru CUI și versiunea de concurență, numai în baza configurată exact cu numele `BlazorStoc`. Nu este generat un fișier SQL de upgrade.

## Administrarea utilizatorilor

Administratorul configurat prin `Authentication__Username` și `Authentication__Password` rămâne contul de inițializare. După autentificare, pagina **Utilizatori** permite:

- adăugarea conturilor cu parolă de minimum 12 caractere;
- alegerea nivelului `Administrator` sau `Utilizator`;
- editarea numelui, numelui afișat, rolului și stării active;
- schimbarea opțională a parolei;
- dezactivarea temporară sau ștergerea definitivă a unui cont.

Lista utilizatorilor se sincronizează automat la fiecare 15 secunde. Sincronizarea este suspendată cât timp este deschis un formular de editare sau dialogul de ștergere, pentru a nu întrerupe operația în curs.

Autentificarea este obligatorie inclusiv în modul demonstrativ. Pentru preview se folosesc conturile fictive afișate pe pagina de autentificare: `administrator.demo` cu parola `admin-demo-123` și `utilizator.demo` cu parola `utilizator-demo-123`.

Un cont inactiv nu poate deschide o sesiune. Dacă parola introdusă este corectă, pagina de autentificare explică explicit că acel cont este inactiv; parolele greșite primesc în continuare mesajul generic.

## Imaginile produselor

Imaginile noi nu sunt salvate ca BLOB în baza de date. În modul MariaDB, fișierul este păstrat implicit în `data/product-images`, în afara directorului public, folosind ID-ul produsului drept cheie. Browserul îl primește prin ruta autentificată `/media/products/{id}`. Sunt acceptate JPG, PNG, WebP și GIF, verificate după conținut, cu limita de 5 MB. SVG nu este acceptat.

Catalogul afișează imaginile ca thumbnail-uri, iar un click deschide varianta mare. Aceeași imagine apare în panoul de detalii. Produsele vechi fără imagine primesc temporar un substitut vizual și trebuie completate la următoarea editare.

Pentru instalarea finală recomand păstrarea fișierelor într-un volum dedicat, cu nume generate de aplicație, iar în baza de date doar metadate: ID produs, cheie relativă, tip MIME, dimensiune și hash SHA-256. Backupul trebuie să includă atât baza, cât și directorul imaginilor. Migrarea BLOB-urilor existente către fișiere va fi o operație separată și verificabilă; nu este generat acum un fișier SQL de upgrade.

## Jurnalul de activitate

Meniul **Jurnal activitate** este afișat numai administratorilor. Fiecare operație CRUD reușită asupra produselor, beneficiarilor sau utilizatorilor înregistrează:

- data și ora UTC;
- utilizatorul și rolul care au efectuat operația;
- tipul entității și operația;
- identificatorul și denumirea țintei;
- un rezumat al schimbării, inclusiv motivul unei corecții de stoc.

Coloana „Țintă” deschide pagina de consultare a obiectului (`/produse/{id}`, `/beneficiari/{id}`, `/utilizatori/{id}`, `/proiecte/{id}` etc.), nu formularul de editare; editarea pornește numai din butonul „Editează” al paginii. Obiectele șterse (arhivate), tipurile fără pagină și evenimentele fără identificator sunt afișate ca text. Filtrele, dimensiunea paginii și pagina jurnalului sunt păstrate în adresă (`/jurnal?q=…&tip=…&pagina=…`), astfel că Înapoi din browser le restaurează.

Parolele și hash-urile nu sunt incluse în jurnal. În faza locală, dashboard-ul citește jurnalul persistent din `data/audit-events.jsonl`; directorul este exclus din sursele distribuite. În modul MariaDB, jurnalizarea tranzacțională existentă în baza de date rămâne activă, iar jurnalul aplicației furnizează vizualizarea unificată.

Sistemul refuză eliminarea ultimului administrator activ. Un administrator nu își poate șterge, dezactiva, redenumi sau retrograda propriul cont în sesiunea curentă. În modul real, implementarea este pregătită să auditeze modificările fără parole sau hash-uri.

În faza curentă se livrează și se verifică numai implementarea din proiect și modul demo. Fișierele SQL pentru tabelele utilizatorilor și orice actualizare a bazei sunt amânate pentru o fază separată.

## Pornire locală

### Utilizarea catalogului

- **Adaugă produs** deschide formularul pentru cod produs, descriere, categorie și subcategorie. Produsul nou este creat cu stoc 0. Categoria și subcategoria se aleg dintre cele create în pagina „Categorii și subcategorii”.
- **Cod produs** este codul stabilit de producător, introdus manual. Este obligatoriu, are cel mult 100 de caractere și este unic în tot catalogul: două coduri care diferă doar prin spații, majuscule/minuscule sau diacritice sunt considerate identice. Aplicația nu generează coduri interne.
- Identificatorul numeric intern al produsului rămâne folosit pentru persistență, rute, relații și jurnalizare tehnică (`EntityId`), dar nu mai este afișat în interfață în forma `#<număr>`.
- Apasă pe codul unui produs (sau pe „↗”) pentru a deschide pagina **Intrări/ieșiri** a produsului (`/produse/<id>/miscari`). De acolo folosești **Editare produs** sau **Șterge produs** (acesta deschide fluxul de ștergere din catalog, `/produse?sterge=<id>`).
- Pagina de intrări/ieșiri arată produsul, formularul de mișcare și tabelul mișcărilor:
  - Data mișcării este aleasă de utilizator (implicit azi), aleasă numai din calendar (câmp doar pentru citire, fără tastare), între 1 ianuarie 1990 și azi; o dată viitoare este respinsă și de server. Descrierea este obligatorie; cantitatea este un număr întreg între 1 și 100.000.
  - La **Ieșire** poți bifa „Beneficiar” și, după alegerea acestuia, „Proiect” (proiectele beneficiarului). Fără ele se completează numai data, cantitatea și descrierea. La **Intrare** aceste câmpuri nu apar.
  - Stocul este suma intrărilor minus suma ieșirilor și poate deveni negativ; o ieșire peste stoc nu este blocată.
  - Coloana „Beneficiar” arată beneficiarul și proiectul ieșirii. Tabelul se filtrează (Intrări/Ieșiri), se sortează după dată și se paginează.
  - **Editează**/**Șterge** cer un motiv; editarea corectează stocul și marchează mișcarea cu `[*]`. Istoricul modificărilor se deschide cu click dreapta pe rând sau cu butonul „Istoric”. Ștergerea (în doi pași) mută mișcarea și istoricul ei în arhivă.
  - Produsele care aveau deja stoc în baza de date locală primesc o singură dată o mișcare „Stoc initial”, ca stocul să fie egal cu suma mișcărilor.
- Formularele de creare și editare nu conțin câmp pentru cantitate: stocul se afișează numai pentru consultare și nu poate fi introdus sau corectat manual. Un stoc existent (inclusiv negativ) rămâne neschimbat la editarea produsului.
- Pagina „Echipamente” a unui proiect (`/proiecte/<id>/echipamente`) listează ieșirile de stoc asociate proiectului: cod produs, cantitate, dată, operator și referința mișcării. Lista de proiecte a beneficiarului își păstrează filtrul și pagina în adresă (`?q=…&pagina=…`), iar listele proiectelor, ale observațiilor și fișierelor se actualizează live pentru modificările altor sesiuni, fără a închide un formular deschis.
- Fiecare editare a produsului cere un motiv. În modul MariaDB, operațiile și valorile înainte/după se salvează ca JSON în `log`, în aceeași tranzacție cu produsul. Nu se creează mișcări `io` fictive.
- Ștergerea cere confirmare și este permisă numai cu stoc zero, fără rânduri asociate în `io` sau `imagine`. Categoriile nu se șterg automat.
- O versiune veche a produsului nu poate suprascrie sau șterge o versiune mai nouă. În caz de conflict, închide formularul, actualizează catalogul și reia editarea.
- Dacă se întrerupe conexiunea în timpul salvării, verifică mai întâi catalogul: operația poate fi deja confirmată de server. Nu există retry automat.
- În modul demo nu se scrie în SQL. Modificările produselor sunt izolate în sesiunea browserului, iar conturile demonstrative sunt partajate între sesiunile locale până la repornirea aplicației, astfel încât autentificarea conturilor nou create să poată fi testată.

### Comandă de pornire

Instalează SDK .NET 9, apoi din acest director:

```powershell
dotnet run --no-launch-profile --urls http://127.0.0.1:5080
```

Deschide http://127.0.0.1:5080. Implicit apar doar exemple fictive. Pentru utilizare pe termen lung este necesară planificarea upgrade-ului la următoarea versiune .NET suportată; ținta 9 a fost aleasă pentru SDK-ul existent pe calculator.

## Baza de date

Integrarea schemei utilizatorilor 0.3 este amânată. Aplicația nu modifică automat schema și nu livrează un fișier SQL de upgrade pentru utilizatori. În această fază, CRUD-ul utilizatorilor se verifică în modul demonstrativ, direct în interfață.

## Validare

Rulează verificările de căutare și CRUD cu `dotnet run --project tests/BlazorStoc.Checks`. Detaliile și testele HTTP sunt în `VALIDARE.md`. Validarea tranzacțiilor, a scriptului de actualizare și a drepturilor SQL trebuie făcută pe o bază locală de test. Scripturile nu se conectează implicit la baza de producție.

Contul tehnic pentru jurnalul produselor rămâne asociat prin `Database__ApplicationUserId` unei identități din tabela legacy `user`. Conturile de autentificare noi sunt exclusiv în `web_user`. Scrierile sunt refuzate dacă baza configurată nu se numește exact `BlazorStoc` sau dacă lipsește identitatea tehnică pentru jurnalul produselor. Arhiva 0.1 de la rădăcina proiectului este istorică; folosește sursele actuale din acest director.

Lucrul pentru NAS/QNAP, Docker și publicarea în containere rămân explicit în afara fazei curente.
