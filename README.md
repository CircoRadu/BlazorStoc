# BlazorStoc 0.3

## Colaborare Codex–Claude

Dezvoltarea folosește cicluri strict secvențiale Codex–Claude: agentul activ modifică proiectul, verifică, documentează, face commit și se oprește înainte ca celălalt agent să preia lucrul. Protocolul complet este în [`docs/SEQUENTIAL_COLLABORATION.md`](docs/SEQUENTIAL_COLLABORATION.md), iar starea curentă este în [`docs/PROJECT_STATE.md`](docs/PROJECT_STATE.md).

> Din 25 septembrie 2026 dezvoltarea este condusa de Claude, fara predare catre Codex (`mode: claude_only` in `.collaboration/state.json`); textul de mai sus descrie protocolul anterior.

Migrarea ESP_Stoc: CRUD produse, beneficiari și utilizatori web, două niveluri de acces, catalog, căutare, filtre, paginare și detalii. Interfața este în română, exclusiv pentru browser pe calculator (lățime recomandată minimum 1100 px). Versiunea mobilă nu face parte din această etapă.

Blazor Web App / Interactive Server, .NET 9, MySqlConnector 2.6.2. Aplicația rulează direct cu .NET, fără Docker. Conexiunea interactivă necesită WebSockets sau fallback SignalR și conectivitate continuă.

Faza curentă acoperă dezvoltarea și verificarea locală. Integrarea, configurarea și publicarea pe NAS/QNAP sunt amânate și nu fac parte din instrucțiunile de mai jos.

## Ce este inclus

- Fără mod demonstrativ și fără SQLite (eliminate la 30.09.2026): aplicația lucrează numai pe MariaDB.
- CRUD MariaDB pentru produse, cu categorii/subcategorii create din formular, tranzacții, jurnal și verificarea modificărilor simultane.
- Imagine obligatorie la crearea produsului, selectabilă din fișier sau din clipboard, cu thumbnail în catalog și vizualizare mărită.
- Autentificare web cu un cont configurabil, cookie de sesiune, protecție antiforgery și limitarea cererilor de login.
- Autentificare cu utilizatori din baza de date și parole PBKDF2. Parolele nu sunt stocate sau afișate în clar și nu folosesc valorile Base64 din aplicația veche.
- Rol `Administrator`: CRUD produse, beneficiari și utilizatori. Rol `Utilizator`: CRUD produse și beneficiari, fără acces la meniul sau pagina de administrare a conturilor.
- Registru Beneficiari cu nume și CUI unic, căutare, validare, actualizare automată la 15 secunde și protecție împotriva suprascrierilor concurente.
- Protecții pentru păstrarea unui administrator activ, blocarea ștergerii/dezactivării propriului cont și verificarea modificărilor concurente.
- Jurnal persistent pentru toate adăugările, modificările și ștergerile de produse, beneficiari sau utilizatori, cu timestamp UTC, operator, rol și detalii fără parole.
- Meniul **Setari** (`/setari`), numai administratorilor, organizat pe taburi. Primul tab, **Preluare date ANAF**, este configuratorul interogarii serviciului public ANAF dupa CUI: conexiune (URL, metoda, timeout, interval, antete), cerere (sablon JSON cu `{{cui}}` si `{{data_interogare}}`, previzualizare), mapare a raspunsului (cai, tip, obligatoriu, politici la lipsa si la actualizare), testare pe configuratia nesalvata si versiuni (ciorna, activare doar dupa test reusit, istoric, revenire la activarea precedenta). Configuratia se pastreaza in `data/anaf-configuration.json`; testele nu modifica date de firme.
- Formularul de iesire din pagina de miscari a produsului alege beneficiarul si proiectul prin cate un combobox cu autocompletare (cautare fara diferenta de majuscule/diacritice; pentru beneficiari si dupa CUI si telefon). Butoanele **+ Adauga beneficiar** / **+ Adauga proiect** deschid formularul de adaugare si, la salvare sau anulare, aplicatia revine in formularul de iesire cu valorile pastrate si cu beneficiarul/proiectul nou selectat. Formularul se pastreaza cat timp sesiunea ramane deschisa (cel mult 30 de minute).
- Beneficiari: formularul are radiobutton **Persoana fizica** (Nume complet, Adresa, Telefon - obligatorii; identitate = numele complet) / **Persoana juridica** (CUI, Denumire, Adresa, Nr. Registrul Comertului, Telefon, Cod postal, Cod CAEN; obligatorii CUI, Denumire, Adresa, Telefon; identitate = CUI). Butonul **Preia date din ANAF** foloseste configuratia activa din Setari si completeaza campurile firmei; o bifa verde arata datele preluate din ANAF, un semn de atentionare datele introduse manual. Toate campurile sunt normalizate (spatii, diacritice, telefon numai cifre).
- Dashboard **Jurnal activitate**, disponibil numai administratorilor, cu filtrare și sincronizare automată la 15 secunde.
- Flux asincron complet pentru operațiile SQL, autentificare și evenimentele Blazor care declanșează încărcări sau scrieri.
- Conexiuni deschise și închise asincron după fiecare operație, pool de maximum 10 conexiuni și fără reîncercări automate.
- Încărcarea produselor și a grupurilor categorie/subcategorie rulează în paralel, cu anulare la închiderea componentei.
- Configurare locală prin `appsettings.json` și variabile de mediu .NET.

Stocul unui produs se modifică numai prin intrări și ieșiri (pagina „Intrări/ieșiri” a produsului); produsele noi au stoc 0 și cantitatea nu se introduce manual. Include gestiunea beneficiarilor. Nu include încă importul datelor, imaginile, export Excel, rapoarte sau conturile individuale din aplicația veche. Nu citește parolele Base64 existente.

## Beneficiari

Administratorii și utilizatorii cu acces limitat pot lista, căuta, adăuga, edita și șterge beneficiari. Fiecare beneficiar are momentan nume și CUI; CUI-ul acceptă 2–10 cifre, cu prefixul opțional `RO`, și trebuie să fie unic. Un beneficiar legat de mișcări de stoc nu poate fi șters.

În modul MariaDB, prima accesare a secțiunii completează direct tabela existentă `beneficiar` cu coloanele necesare pentru CUI și versiunea de concurență, numai în baza configurată exact cu numele `BlazorStoc`. Nu este generat un fișier SQL de upgrade.

## Vehicule

Nota (pagina vehiculului): numarul de inmatriculare din lista duce la pagina `/vehicule/{id}`, care are o singura intrare, "Materiale si echipamente" (`/vehicule/{id}/echipamente`): tabel cu codul produsului si cantitatea din masina, cu "Restituie in depozit" si "Muta in alta masina" pentru fiecare reper (cantitate partiala permisa) si "Restituie tot in depozit" / "Muta tot in alta masina" pentru toate reperele. Pagina nu are observatii si nu primeste fisiere. Fiecare operatie este o miscare de stoc jurnalizata; stocul total nu se schimba.

Meniul **Administrare → Vehicule** (`/vehicule`) administrează autovehiculele firmei, pentru toți utilizatorii autentificați. Fiecare vehicul are un **număr de înmatriculare** obligatoriu și unic și o **descriere** scurtă obligatorie (cel mult 100 de caractere, de exemplu „Dacia Dokker alba”). Numărul respectă masca `AA-OOO-AAA`: 1–2 litere, 2–3 cifre (două sau trei, deci `01`, nu `1`), 3 litere — `HD-01-FDG`, `HD-233-VDG`, `B-123-ABC`. Se poate tasta cu litere mici, cu spații sau fără cratime; se salvează cu majuscule și cratime. Editarea cere motivare și confirmarea salvării, ștergerea se face în doi pași (motiv, apoi `sterge`) și arhivează vehiculul; adăugările, editările și ștergerile apar în Jurnal (tip „Vehicul”). Un vehicul folosit de o mișcare de stoc (destinație sau sursă) nu poate fi șters; lista arată numărul mișcărilor fiecărui vehicul.

În modul MariaDB tabela `vehicul` este creată la prima accesare a secțiunii, numai în baza `BlazorStoc`; nu este generat un fișier SQL de upgrade.

## Inventar

Meniul **Inventar** (sectiunea "Spatiu de lucru", intre "Produse si stocuri" si "Administrare") are doua intrari: **Generare situatie inventar** (`/inventar`) si **Preluare inventar** (`/inventar/preluare`). "Inventar" apare si in dashboard.

Pagina `/inventar` arata categoriile si subcategoriile ca in "Categorii si subcategorii" (carduri extensibile, restranse implicit), fara butoanele de adaugare/editare. Fiecare categorie si subcategorie are o caseta de selectare; selectarea unei categorii propaga la subcategoriile ei, iar starea partiala este `indeterminate`. "Selecteaza toate categoriile" selecteaza sau deselecteaza tot. "Elimina din situatia de inventar produsele cu stoc 0" exclude din PDF produsele cu stoc exact 0; produsele cu stoc negativ raman si sunt scrise cu rosu pe intregul rand.

Butonul "Genereaza situatia de inventar" este dezactivat fara nicio selectie si genereaza un fisier PDF (`Inventar_aaaa-ll-zz_oomm.pdf`) descarcat direct din browser, fara sa fie pastrat pe server. PDF-ul are pe prima pagina "Inventar" si "Generat la: zz.ll.aaaa oo:mm" (ora serverului la momentul generarii), apoi pentru fiecare categorie si subcategorie selectata un tabel "Cod produs | Valoare stoc | Valoare reala" (coloana "Valoare reala" ramane goala, pentru completare manuala). Valoarea stocului este cea "in depozit" (stocul total minus cantitatea aflata in vehicule). Numele categoriilor si subcategoriilor sunt bold 14; restul textului este normal 12. Antetul tabelului se repeta pe paginile urmatoare cand tabelul continua.

Fiecare generare reusita este jurnalizata (tip "Inventar", actiunea "Generare") cu utilizatorul, numarul de categorii/subcategorii selectate, optiunea stoc 0, numarul de produse (din care cu stoc negativ) si numele fisierului; nu contine lista produselor sau valorile stocului si nu are link catre o pagina. Cererile respinse sau fara produse nu sunt jurnalizate.

Pagina `/inventar/preluare` permite reintroducerea in stoc a numaratorii fizice: butonul "Preia inventar" incarca un fisier PDF cu situatia de inventar de mai sus, tiparita si completata de mana la "Valoare reala", apoi scanata (sau fisierul poate fi tras direct din sistemul de operare peste zona de continut a paginii, drag and drop). Se accepta exclusiv acest format (alt fisier este respins cu mesaj clar), maximum 20 MB si 50 de pagini. Aplicatia citeste fisierul prin OCR local (fara Docker, fara serviciu extern): textul tiparit ("Cod produs") cu Tesseract, iar cifrele scrise de mana ("Valoare reala") prin segmentarea lor cu OpenCvSharp si clasificarea fiecarei cifre izolate cu un model ONNX de cifre scrise de mana (MNIST, incorporat in aplicatie). Pozitia coloanelor tabelului se recalibreaza pe fiecare pagina fata de grid-ul efectiv scanat, nu doar fata de geometria PDF-ului generat.

Produsele cu diferenta de stoc (valoare reala citita fata de stocul curent in depozit) apar intr-o lista grupata pe categorie si subcategorie, cu checkbox NEbifat implicit pentru fiecare; randurile pe care OCR nu le-a putut citi cu certitudine apar in aceeasi lista, marcate cu "de verificat", iar valoarea recunoscuta este editabila pentru corectie inainte de bifare. Produsele al caror cod citit nu (mai) exista in catalog apar informativ, separat, fara checkbox. Butonul "Trimite modificari in stoc" (activ doar cu cel putin un produs bifat) deschide un popup cu modificarile de operat (cod produs, modificare stoc), grupate pe categorie si subcategorie; confirmarea aplica fiecare modificare ca o miscare de stoc obisnuita (Intrare pentru plus, Iesire cu destinatia existenta "Corectie stoc" pentru minus, fara tip nou de miscare), jurnalizata ca restul miscarilor. O eroare la o singura modificare nu opreste trimiterea celorlalte; rezultatul arata explicit ce s-a aplicat si ce nu.

Inainte de a aplica modificarile, aplicatia genereaza si verifica automat o copie de siguranta completa a bazei de date (export + arhiva `.zip` verificata prin hash); popup-ul de confirmare arata etapele in curs ("Se exporta baza de date...", "Se verifica...", "Se salveaza..."). Daca backupul esueaza (spatiu insuficient pe disc, alta operatie de backup/restaurare deja in curs, eroare la export), modificarile de stoc nu sunt aplicate deloc si utilizatorul vede mesajul de eroare, cu posibilitatea de a reincerca. Pachetul de siguranta ramane pe server, disponibil ulterior pentru restaurare.

Meniul Inventar are, numai pentru administrator, optiunea "Restaurează stoc" (`/inventar/restaurare`): lista tuturor pachetelor de siguranta de pe server (tip, denumire, data, operator, dimensiune), cu un rezumat al spatiului ocupat. Pachetele generate la preluarea inventarului pot fi sterse (motiv + cuvantul `sterge`, ca la orice stergere din aplicatie); pachetele generate automat inaintea unei restaurari nu pot fi sterse in nicio conditie. Butonul "Restaureaza baza de date" (activ numai cu un pachet selectat) deschide un popup cu avertizarea explicita ca restaurarea aduce baza la o versiune anterioara; confirmarea finala se face prin introducerea cuvantului exact `confirma`. Restaurarea genereaza intai automat o copie de siguranta a bazei curente, verifica integritatea pachetului ales, compara structura si continutul cu baza vie (o restaurare identica e refuzata ca inutila) si abia apoi inlocuieste efectiv datele. In modul demonstrativ (SQLite) fluxul este complet functional; in modul MariaDB real, ultimul pas (comutarea de scheme) necesita un cont dedicat suplimentar, neconfigurat inca.

Generarea foloseste PDFsharp 6.2.1 (licenta MIT; versiunea curenta nu include inca MigraDoc pentru API-ul cross-platform, asa ca raportul este desenat direct cu `XGraphics`) si fontul PT Sans (SIL Open Font License, `Assets/Fonts`), care contine diacriticele romanesti si este inclus in aplicatie, fara dependenta de fonturile instalate pe server.

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
  - La **Ieșire** trebuie aleasă **destinația**: „Beneficiar” (beneficiarul este obligatoriu, proiectul facultativ), „Autovehicul” (un vehicul din pagina Vehicule), „Vânzare generică” sau „Corecție stoc”. Nicio opțiune nu este preselectată. Pentru autovehicul și pentru corecție, descrierea este precompletată („Completare stoc mașină HD-01-FDG 25.09.2026”, „Corecție stoc 25.09.2026”) și rămâne editabilă. La **Intrare** aceste câmpuri nu apar.
  - La **Ieșire** se alege și **sursa** produsului: „Depozit” (implicit) sau „Mașină”. Din mașină nu se poate scoate mai mult decât conține pentru produsul respectiv; transferul dintr-o mașină în alta nu se face din acest formular.
  - Stocul total este suma intrărilor minus ieșirile care consumă produsul și poate deveni negativ; o ieșire peste stoc din depozit nu este blocată. O ieșire **spre autovehicul nu scade stocul total**: produsul este mutat în mașină. Când există produse în mașini, stocul se afișează defalcat („X în depozit, Y în vehicule”) în pagina mișcărilor, în catalog și în editorul produsului.
  - Coloana „Beneficiar/Proiect” arată beneficiarul și proiectul ieșirii (sau eticheta „Vânzare generică”/„Corecție stoc”), iar coloana „Vehicul” vehiculul destinație („în …”) sau sursă („din …”). Tabelul se filtrează (Intrări/Ieșiri), se sortează după dată și se paginează.
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

Nota 28.09.2026 (Task 2, adaptarea la instanta MariaDB reala): paragraful de mai sus descrie mecanismul vechi, inlocuit. `Database:ApplicationUserId` si tabela `user` au fost eliminate din cod; identitatea operatorului in jurnalul produselor si in miscarile de stoc este acum username-ul text, prin `IAccessControl`/`IAuditTrail`, la fel ca in modul demonstrativ SQLite. Garda "scrierile sunt refuzate daca baza nu se numeste exact BlazorStoc" ramane neschimbata. Detalii in `docs/CLAUDE_CONECTARE_MARIADB_LOCALA.md` si `docs/PROJECT_STATE.md`.

Lucrul pentru NAS/QNAP, Docker și publicarea în containere rămân explicit în afara fazei curente.

## Sincronizare între utilizatori

- Modificările produselor, mișcărilor de stoc, utilizatorilor, proiectelor, observațiilor și fișierelor sunt înregistrate de trigger-e ale bazei de date în tabelul `change_events` (numai identificatori), inclusiv cele făcute de aplicații externe. Un serviciu de fundal le publică la aproximativ o secundă către paginile deschise și către clienții SignalR conectați la `/hubs/changes` (mesajul `changed`).
- Paginile afectate se reîmprospătează singure; dacă un formular sau un dialog este deschis, apare o notificare cu butonul „Reîncarcă datele”, iar verificarea versiunii la salvare rămâne protecția finală. Sincronizarea periodică (60 s, 15 s la lista de utilizatori) rămâne rezervă.
- Configurare opțională: `Sync:PollMilliseconds` (implicit 1000) și `Sync:GraceMilliseconds` (implicit 1500). În MariaDB, contul aplicației are nevoie de privilegiile CREATE și TRIGGER pentru a crea tabelul și trigger-ele la prima folosire.

## Blocarea temporară a editării unui produs

- Când începi editarea unui produs existent, produsul este blocat pentru tine (lease de 90 s, reînnoit la 30 s cât timp formularul este deschis). Ceilalți utilizatori îl pot consulta, văd cine îl editează și de când, iar butonul „Editează” este dezactivat; când se eliberează (salvare, anulare, închiderea formularului) sau expiră (browser închis, conexiune pierdută), sunt anunțați automat.
- Un administrator poate elibera forțat blocarea din pagina produsului, cu motiv obligatoriu; acțiunea „Deblocare” apare în jurnalul de activitate. Verificarea versiunii produsului rămâne protecția finală la salvare.

## Avertizare la părăsirea unei editări nefinalizate

- Dacă ai modificat valori într-un formular de adăugare sau de editare (produs, beneficiar, proiect, observație, utilizator, categorie, subcategorie, mișcare de stoc) și pleci fără să salvezi, aplicația afișează un popup: „Editarea nu a fost finalizată” (sau „Adăugarea nu a fost finalizată”). „Înapoi la editare” (verde) păstrează formularul și valorile; „Părăsește editarea fără salvarea modificărilor” (roșu) renunță la modificări și execută acțiunea aleasă (link, meniu, „Înapoi” al browserului, „Deconectare”, „Anulează”/„Închide”).
- Un formular nemodificat se părăsește fără întrebare. Închiderea sau reîncărcarea tabului folosește dialogul nativ al browserului.
- Mecanismul este comun (`Services/UnsavedChanges.cs`, `UnsavedChangesTracker`/`UnsavedChangesHost`, `wwwroot/leave-guard.js`): un editor nou primește avertizarea prin `<UnsavedChangesTracker …>` în formular și `<UnsavedChangesHost />` în pagină.

## Formatul datelor

- Toate datele calendaristice afișate sunt `dd.mm.yyyy` (cu oră: `dd.mm.yyyy hh:mm`). Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana `io_data` din MariaDB) nu se afișează.

## Limba mesajelor

- Toate mesajele afișate utilizatorului sunt în limba română: validări, erori, dialoguri, texte de stare, dialogul de reconectare, paginile de eroare HTTP și mesajele native ale browserului pentru câmpurile de formular (`wwwroot/romanian-ui.js`). Mesajele tehnice din jurnalul serverului pot rămâne în engleză. Cultura aplicației este `ro-RO`.
- Un mesaj nou se scrie direct în română (`ErrorMessage`, excepții de operare, câmpuri `error`/`notice`); `BlazorStoc.Checks` verifică automat că literalele acestea nu conțin cuvinte englezești frecvente.
