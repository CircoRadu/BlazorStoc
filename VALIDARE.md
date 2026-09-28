# Verificări — versiunea 0.2

> **Numerotarea taskurilor.** Numerele „Task N” din acest fișier sunt cele folosite la momentul implementării. Din 25 septembrie 2026 `TODO.md` nu mai păstrează numere pentru taskurile finalizate, ci denumiri succinte; corespondența este: Task 0 = „Stoc exclusiv prin mișcări de intrare și ieșire”; Task 1 = „Intrări și ieșiri pentru un produs existent” (în perioada timpurie a proiectului „Cod produs”); Task 2 = „Proiecte asociate beneficiarilor”; Task 3 = „Navigarea din jurnal către pagina obiectului”; Task 4 = „Identificarea beneficiarului cu CUI duplicat”; Task 5 = „Confirmarea salvărilor care modifică date existente”; Task 6 = „Confirmarea la părăsirea formularului de adăugare produs”; Task 7 = „Confirmarea deconectării”; Task 8 = „Sincronizarea între utilizatori prin evenimente din baza de date”; Task 9 = „Blocarea temporară a editării unui produs”. Taskul activ „Situația de inventar” (fost Task 10) este acum **Task 1**.

> Verificările care nu au putut fi efectuate sunt urmărite, cu pașii și motivul, în `docs/TESTE_RAMASE.md`.

## Preluare inventar (Task 1) - 28 septembrie 2026

- Pagina `/inventar/preluare` are butonul "Preia inventar" (incarcare fisier PDF), acceptand exclusiv formatul generat de aplicatie la `/inventar` (structura de tabel Cod produs/Valoare stoc/Valoare reala), maximum 20 MB si 50 de pagini.
- Pipeline OCR local, fara Docker si fara serviciu extern: `PDFtoImage` (PDFium+SkiaSharp) rasterizeaza fiecare pagina la 300 dpi; `InventoryPdfLayout.ComputeColumns` da geometria coloanelor (aceeasi formula ca `InventoryPdfWriter`, refolosita, nu duplicata); `InventoryPickupOcrService.CalibrateColumns` recalibreaza pozitia efectiva a coloanelor pe fiecare pagina fata de grid-ul chiar scanat (un scan real nu reproduce exact geometria PDF-ului - printarea "fit to printable area" a scanerului a scazut latimea utila cu cca. 4% pe formularul de proba); randurile tabelului sunt gasite prin scanarea liniilor orizontale, cu toleranta pentru liniile verticale (±6 px). "Cod produs" (text tiparit) e citit cu **Tesseract** (motor nou per celula - motorul refolosit intre celule a produs text corupt la testare); "Valoare reala" (scris de mana) e segmentata pe cifre cu **OpenCvSharp** (componente conexe) si fiecare cifra e clasificata cu modelul **MNIST din ONNX Model Zoo** (licenta MIT, incorporat ca resursa), prin **ONNX Runtime**.
- 19 verificari automate noi au trecut (578 in total, `tests/BlazorStoc.Checks`): 9 ruleaza pipeline-ul OCR real pe o scanare autentica furnizata de utilizator (`tests/BlazorStoc.Checks/Fixtures/inventar-proba.pdf` - situatia de inventar tiparita, completata de mana si scanata cu un Kyocera SKM_C3320i) si verifica valorile citite corect (105, 24, 54, 6, 0, 123) si ca niciun rand nu e eliminat tacut cand OCR e nesigur; restul (10) testeaza potrivirea cu catalogul (exacta, aproximativa la o greseala OCR, negasita separat), excluderea randurilor fara diferenta, si aplicarea miscarilor (Intrare la surplus, Iesire cu `ExitDestination.StockCorrection` existent la lipsa, esec partial raportat fara sa opreasca restul).
- **Rezultat pe formularul real**: 8 din 9 produse cu valoare scrisa au fost citite si asociate corect produsului din catalog; "Set chei combinate" (lasat necompletat) nu a generat nicio modificare; "Polizor unghiular" a fost citit gresit ("8" scris intr-o singura bucla, clasificat "5" cu incredere), fara sa fie semnalat nesigur — limitare cunoscuta a modelului generic, documentata in `docs/TESTE_RAMASE.md` (H2). Randul ramane afisat, cu valoarea editabila inainte de bifare, ca plasa de siguranta.
- Lista de produse cu diferente e grupata pe categorie/subcategorie, cu checkbox NEbifat implicit; randurile nesigure apar in aceeasi lista, marcate "de verificat"; produsele negasite in catalog apar intr-o sectiune separata, informativa. Butonul "Trimite modificari in stoc" deschide un popup cu modificarile (cod produs, modificare stoc) grupate pe categorie/subcategorie; fiecare modificare confirmata devine o miscare de stoc obisnuita (fara tip nou), jurnalizata automat de acelasi mecanism de audit ca restul miscarilor (`stock_movements`/`io`).
- Build Debug si Release: 0 avertismente, 0 erori.

## Situatia de inventar (Task 1) - 28 septembrie 2026

- Pagina `/inventar` afiseaza categoriile si subcategoriile ca in "Categorii si subcategorii" (carduri restranse implicit), cu caseta de selectare pentru fiecare categorie/subcategorie, propagare categorie -> subcategorii, stare `indeterminate` (verificata vizual: caseta "Selecteaza toate categoriile" devine indeterminata cand doar o parte din categorii sunt selectate) si optiunea "Elimina din situatia de inventar produsele cu stoc 0".
- Meniul principal are elementul extensibil "Inventar" (intre "Produse si stocuri" si "Administrare") cu intrarile "Generare situatie inventar" (`/inventar`) si "Preluare inventar" (`/inventar/preluare`, pagina de rezerva); "Inventar" apare si in dashboard.
- Butonul "Genereaza situatia de inventar" este dezactivat fara selectie si genereaza un PDF descarcat direct din browser (fara sa fie pastrat pe server), folosind PDFsharp 6.2.1 si fontul PT Sans (SIL OFL) incorporat in aplicatie.
- 36 de verificari automate noi au trecut (559 in total): logica selectiei (categorie/subcategorie/toate, indeterminate, categorie fara subcategorii), constructia situatiei (doar selectia facuta, eliminarea stocului 0 cu stocul negativ pastrat, omiterea sectiunilor goale, ordinea, stocul calculat prin `WarehouseStock` cu excluderea cantitatii din vehicule, respingerea unei selectii inexistente sau goale), generarea PDF-ului (semnatura `%PDF`, textele asteptate extrase din PDF prin CMap-ul `ToUnicode` al fontului incorporat, un cod cu diacritice, fontul bold 14 pentru categorii/subcategorii si normal 12 pentru rest, culoarea rosie a randului cu stoc negativ, un catalog de 300 de produse cu antet repetat pe mai multe pagini si subsolul "Pagina x din y") si jurnalizarea (un singur eveniment "Generare"/"Inventar" cu rezumatul cererii, fara date de produse si fara link).
- Verificare in browser (mod demonstrativ, cont administrator.demo): selectarea unei categorii intregi propaga la subcategorii, "Selecteaza toate categoriile" devine indeterminata corect, PDF-ul generat contine tabelul asteptat cu bordurile complete ale celulelor (deschis si verificat direct din fisierul descarcat), iar evenimentul "Generare" apare in Jurnal cu rezumatul corect si fara link pe "Tinta". Pagina "Preluare inventar" afiseaza mesajul de rezerva. Verificat si la 768 si 375 px: fara derulare orizontala a paginii (aplicatia ramane, ca in restul proiectului, gandita pentru minimum 1100 px).
- Build Release: 0 avertismente, 0 erori. Neverificat: deschiderea fisierului PDF in alte cititoare decat cel folosit la verificare (`docs/TESTE_RAMASE.md`, G1) si tiparirea fizica (G2).

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
3. Tabelul: Data (`zz-LL-aaaa`), Intrare/Ieșire cu `[*]` pentru mișcările modificate, Număr bucăți, Descriere, Beneficiar/Proiect (beneficiar și proiect ca linkuri); filtru, sortare după dată, paginare 10/20/50/Toate; „Total produse în stoc” actualizat după fiecare operație, inclusiv negativ.
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

## Revenirea în pagina de origine după editarea/ștergerea produsului (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 339 verificări; 2 noi): `ReturnNavigation.Safe` acceptă numai căi locale (respinge gazdă externă, `//`, `\`, caractere de control, valori goale) și linkurile de editare/ștergere poartă adresa de întoarcere.
- **Browser** (`http://127.0.0.1:5082`): din `/produse/1/miscari`, „Editează” duce la `/produse?edit=1&inapoi=%2Fproduse%2F1%2Fmiscari`, iar „Anulează” readuce în pagina produsului; „Șterge produs” deschide dialogul de ștergere, iar „Anulează” readuce în pagina produsului. Nu s-a salvat și nu s-a șters nimic.
- **Neverificat manual**: salvarea confirmată a editării (aceeași cale de navigare, acoperită de cod) și ștergerea efectivă (duce în catalog, nemodificat).

## Task 7 — Confirmarea deconectării (25 septembrie 2026)

- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator): cu editorul beneficiarului deschis și textul „text de pastrat” tastat în motivare, „Deconectare” din bara de sus deschide popup-ul „Închizi sesiunea?” cu butonul roșu „Deconectare” și butonul verde „Anulează deconectarea”; „Anulează deconectarea” închide popup-ul, iar editorul și textul tastat rămân. Formularul popup-ului are `method="post"`, `action="/Account/Logout"` și tokenul antiforgery.
- **Neverificat manual**: butonul roșu (ar fi încheiat sesiunea utilizatorului din panoul Browser), Escape, tastatura. `GET /Account/Logout` redirecționează la `/`. `BlazorStoc.Checks`: 339 verificări trecute (neschimbate; UI-ul nu are teste automate).

## Task 8 — Sincronizare între utilizatori (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 361 verificări; 22 noi): trigger-ele SQLite înregistrează fiecare inserare/editare/ștergere pentru produse, mișcări (ca modificare a produsului), utilizatori, proiecte, observații și fișiere, cu identificatorii de proiect/observație/beneficiar; evenimentele nu conțin parole, nume, texte sau nume de fișiere; migrările și seed-ul nu produc evenimente; ștergerea evenimentelor procesate respectă identificatorul și retenția; relay-ul pornește după ultimul eveniment, respectă perioada de grație, publică o singură dată în ordine, elimină copia trigger a unei modificări deja publicate de sesiune și își reia cursorul după o eroare de citire; `LiveRefresh` unește rafalele, ignoră propria sesiune și alte entități, arată notificare când un formular este deschis și are rezervă periodică; textul trigger-elor MariaDB folosește coloanele corecte și `UTC_TIMESTAMP(6)`.
- **Browser** (`http://127.0.0.1:5082`, sesiune autentificată de utilizator, două tab-uri): (1) pagina produsului s-a actualizat singură după o modificare SQL directă (numele produsului), fără acțiune în browser; (2) cu dialogul de editare al unei mișcări deschis, o a doua modificare externă nu a schimbat dialogul, iar notificarea „Datele au fost modificate de altă sesiune sau aplicație” și butonul „Reîncarcă datele” au funcționat; (3) catalogul deschis într-un al doilea tab a arătat 13 buc. după ce în primul tab s-a adăugat o intrare și 12 buc. după ștergerea ei (datele de test au fost arhivate normal, numele produsului readus); (4) `POST /hubs/changes/negotiate` cu sesiunea autentificată → 200 (WebSockets, ServerSentEvents, LongPolling), anonim → 302 la autentificare; (5) un client SignalR real (`@microsoft/signalr`) conectat la `/hubs/changes` a primit mesajul `changed` (`Produs`/`Editare`/id, doar identificatori) după o modificare externă.
- **Verificat de utilizator** (Task 7): butonul roșu „Deconectare” duce la pagina de autentificare.
- **Neverificat**: trigger-ele MariaDB pe un server real (privilegiile CREATE/TRIGGER ale contului), două calculatoare diferite, sincronizarea utilizatorilor și a proiectelor prin browser (aceeași cale, acoperită de teste).

## Task 9 — Blocarea temporară a editării unui produs (25 septembrie 2026)

- **Suita automată** (`BlazorStoc.Checks`, 388 verificări; 27 noi): lock cu produs/utilizator/sesiune/obținere/expirare (timp UTC al bazei de date, 90 s); al doilea utilizator refuzat și informat cine deține lock-ul și de când; reînnoire fără schimbarea deținătorului; eliberare numai de sesiunea proprietară; disponibil imediat după eliberare; lease expirat care nu mai blochează, preluat de următorul editor; heartbeat strict (`RenewAsync`) care reînnoiește un lock expirat nepreluat, nu reia un lock eliberat forțat și nu poate fi folosit de o sesiune care nu îl deține; 24 de cereri simultane → exact un câștigător; produs inexistent nu poate fi blocat; deblocare forțată doar de administrator, cu motiv obligatoriu, cu intrare în jurnal (actor, produs, editor anterior, motiv) și fără intrare când nu există lock; notificări publicate o dată la obținere, la eliberare și la deblocare forțată, nu la reînnoire sau la cereri refuzate; ștergerea produsului elimină lock-ul; mesajul de consultare.
- **Browser** (`http://127.0.0.1:5082`, cont administrator, două tab-uri = două sesiuni): (1) editarea pornită într-un tab afișează în celălalt „🔒 Produsul este editat de administrator.demo din 10:04…” și „Deblochează (administrator)”, iar „Editează” este dezactivat; (2) anularea editării eliberează lock-ul, iar celălalt tab arată imediat „Produsul a fost eliberat și poate fi editat acum.”; (3) deblocarea forțată: motiv gol → „Completează câmpul «Motivare modificare»”; cu motiv → „Blocarea a fost eliberată și înregistrată în jurnal.”, iar în `audit_events` apare `Deblocare` / actor / țintă = codul produsului / motiv; (4) heartbeat: `renewed_utc` avansează la 30 s și `expires_utc` la +90 s; după ștergerea rândului lock-ului, editorul afișează „Blocarea editării a fost pierdută…” și nu îl reia; (5) după închiderea tab-ului în timpul editării, lease-ul a încetat la `expires_utc` (90 s de la ultima reînnoire), iar pagina produsului nu mai afișa blocarea; rândul a fost șters ulterior la eliminarea circuitului.
- **Defecte găsite prin verificare și corectate**: heartbeat-ul reia tăcut un lock eliberat forțat (acum `RenewAsync`); un circuit rămas activ după închiderea browserului ar fi reînnoit lock-ul (acum reînnoire numai dacă browserul răspunde); o versiune veche a `leave-guard.js` din cache (scripturile proprii au acum amprentă prin `@Assets`).
- **Neverificat**: MariaDB pe un server real; un al doilea cont de utilizator autentificat separat (testat cu același cont în două sesiuni); două calculatoare.

## Corecție — subcategoria aleasă din „Toate produsele” rămânea pe categorie (25 septembrie 2026)

- **Defect** (raportat de utilizator): din „Toate produsele”, alegerea subcategoriei „Protecție mâini” afișa și produsele din „Protecție cap”. Cauza: linkul de subcategorie schimbă adresa, meniul redeschide programatic categoria părinte, iar `collapsible.js` trata orice eveniment `toggle` de încredere ca pe o acțiune a utilizatorului și naviga la adresa categoriei (`window.location.assign`), pierzând subcategoria.
- **Corecție**: `wwwroot/collapsible.js` navighează numai când `toggle` urmează unei apăsări reale pe antetul secțiunii (clic, Enter sau Spațiu, în ultima secundă; un clic anulat, de exemplu de protecția din Task 6, nu contează). Deschiderile făcute de aplicație nu mai navighează.
- **Browser** (`http://127.0.0.1:5082`): din `/produse`, linkul „Protecție mâini” duce la `?categorie=…&subcategorie=Protecție mâini` și rămâne acolo, cu 1 produs și categoria extinsă în meniu (înainte se ajungea la `?categorie=…`, cu 2 produse); clic real pe antetul unei categorii → pagina categoriei; al doilea clic → `/produse` (toate produsele).
- **Neverificat**: Enter/Spațiu pe antet (același cod, prin evenimentul `click`), ecran tactil.

## Avertizare la părăsirea unei pagini de editare cu modificări nesalvate (25 septembrie 2026)

- Serviciul `UnsavedChanges` urmărește editorii deschiși îi compară, prin hash, cu valorile de la deschidere; popup-ul „Editarea/Adăugarea nu a fost finalizată” apare la link, meniu, antet de categorie, „Anulează”/„Închide”, „Deconectare” și Înapoi al browserului, numai pentru formulare modificate; „Părăsește” închide editorul (eliberând lock-ul produsului) și execută acțiunea inițială.
- `BlazorStoc.Checks`: 21 de verificări noi (409 în total) și build Release fără avertismente.
- Browser pe 5082 (SQLite, administrator): părăsire fără modificări = fără popup; cu modificări, popup pentru link din meniu, antet de categorie, „Închide”/„Anulează”, „Deconectare”, `history.back()` și formularul de mișcare; „Înapoi la editare” păstrează valorile și focusul; „Părăsește” navighează, revine în istoric sau deschide dialogul de deconectare; `product_locks` gol după părăsire; texte diferite la adăugare (beneficiar) și editare (produs).
- Defect găsit prin verificare: routerul aplicației nu raportează Înapoi către `NavigationLock`, deci intercepția Înapoi/Înainte este făcută în `leave-guard.js` (intrări de istoric etichetate, mutarea anulată și repetată după răspuns).
- Neverificat: vezi `docs/TESTE_RAMASE.md` (C10).

## Datele afișate ca dd.mm.yyyy (25 septembrie 2026)

- Regulă de dezvoltare nouă (`CLAUDE.md`, `AGENTS.md`, `TODO.md`, `README.md`): orice dată afișată este `dd.mm.yyyy`. Corectate: câmpul „Data mișcării” (text și indiciu `zz.ll.aaaa`), tabelul, dialogurile și istoricul mișcărilor, jurnalul (texte vechi normalizate la afișare).
- `io_data` (MariaDB) păstrează `dd-MM-yyyy` prin `StockMovementRules.LegacyDate`; SQLite păstrează `yyyy-MM-dd`.
- `BlazorStoc.Checks` 410 verificări; browser pe 5082: câmpul de dată `25.09.2026`, tabelul `24.09.2026`, jurnalul fără date în alt format.
- Neverificat: calendarul nativ al browserului (limba browserului).

## Antetul „Data” din tabelul de intrări/ieșiri (25.09.2026)

- Butonul de sortare al antetului folosea `.sort-header` cu `justify-content:flex-end`; în tabelul mișcărilor începe acum unde încep datele (`wwwroot/app.css`).
- Browser pe 5082: începutul antetului coincide cu începutul datelor la desktop, 375 și 768 px; celelalte antete sunt aliniate cu conținutul lor; fără depășire orizontală.

## Mesaje exclusiv în limba română (25.09.2026)

- Mesajul în engleză la o intrare cu valoare negativă venea din validarea nativă a browserului; `wwwroot/romanian-ui.js` o înlocuiește cu mesaje românești (și pentru celelalte câmpuri, inclusiv paginile de autentificare). Dialogul de reconectare Blazor, paginile de eroare HTTP, limitarea cererilor și cultura `ro-RO` sunt în română.
- `BlazorStoc.Checks` 413 (verificarea literalelor de mesaj); browser pe 5082: −5, 0, valoare prea mare, câmp gol, 404 și dialogul de reconectare cu serverul oprit.
- Neverificat: ferestrele native (calendar, selector de fișiere).

## Pagina vehiculului, restituire si mutare (25 septembrie 2026)

- Pagina `/vehicule/{id}` (fara observatii si fisiere) si `/vehicule/{id}/echipamente` (tabel COD PRODUS / CANTITATE, restituire in depozit si mutare in alta masina pe reper, cantitate partiala permisa, si pentru toate reperele, atomic). Fiecare operatie este o miscare de stoc jurnalizata; totalul ramane neschimbat.
- `BlazorStoc.Checks` 523 trecute (18 noi); browser pe instanta de proba: pagina cu o singura intrare, restituire partiala, mutare cu alegerea masinii, muta tot / restituie tot, stare goala, tabelul miscarilor (VEHICUL, etichete), editarea unei mutari, stergerea vehiculului blocata, 375/768 px fara depasire orizontala.
- **Neverificat**: MariaDB pe server real (`docs/TESTE_RAMASE.md`, A11).

## Ieșire spre vehicul, vânzare generică și corecție de stoc (25 septembrie 2026)

- Formularul de ieșire: destinație obligatorie (Beneficiar / Autovehicul / Vânzare generică / Corecție stoc), sursă Depozit/Mașină, descriere precompletată cu data de azi. O ieșire spre autovehicul nu scade stocul total; din mașină nu se poate scoate mai mult decât conține; editările și ștergerile nu pot lăsa o mașină cu cantitate negativă. Defalcarea „X în depozit, Y în vehicule” apare numai când există produse în mașini.
- `BlazorStoc.Checks` 505 trecute (41 noi): validarea destinațiilor și a sursei, efectul pe total, precompletarea, defalcarea, transfer, folosire din mașină, respingerea peste cantitate, editare/ștergere care ar face mașina negativă, schimbarea destinației, jurnal, arhivă, ieșiri vechi, sesiuni concurente, repornire.
- Browser (instanță de probă pe `http://127.0.0.1:5084`, bază separată, sesiune autentificată): radio-urile fără preselecție; „Alege destinația ieșirii.”; precompletările („Corecție stoc 25.09.2026”, „Completare stoc mașină HD-01-FDG 25.09.2026”); transfer de 4 buc. (total 12 neschimbat, „8 în depozit, 4 în vehicule”); folosire din mașină (mesaj peste cantitate, apoi 3 buc.: total 9, „8 în depozit, 1 în vehicule”); editarea transferului spre 2 buc. refuzată de server; catalogul cu defalcare numai pentru produsul cu piese în mașini; lista „Vehicule” cu numărul mișcărilor; 375 și 768 px fără depășire orizontală.
- **Neverificat**: MariaDB pe un server real (vezi `docs/TESTE_RAMASE.md`, A10); ecran tactil și tastatură.

## Administrarea vehiculelor (25 septembrie 2026)

- Pagina `/vehicule` (Administrare → Vehicule): adăugare, editare cu motivare și confirmare, ștergere în doi pași cu arhivare, căutare, jurnal (tip „Vehicul”, filtru și link către `/vehicule?edit={id}`). Număr de înmatriculare `AA-OOO-AAA` cu 1–2 litere, 2–3 cifre, 3 litere (`HD-01-FDG`, `HD-233-VDG`, `B-123-ABC` valide; `HD-1-FDG` respins), unic; descriere obligatorie.
- `BlazorStoc.Checks` 464 trecute (51 noi): masca și normalizarea, creare, unicitate, editare cu motiv, editare veche, jurnal, creare concurentă, ștergere cu arhivare și eveniment, reînregistrarea unui număr arhivat, persistență după repornire, căutare.
- Browser (instanță de probă, bază SQLite separată, sesiune autentificată): meniul, starea goală, număr invalid respins, `b123abc` completat ca `B-123-ABC`, adăugare, editare cu dialog de confirmare, jurnal, link de editare, ștergere în doi pași; la 375 și 768 px fără depășire orizontală.
- **Neverificat**: MariaDB pe un server real (vezi `docs/TESTE_RAMASE.md`, A9).

## Eticheta „Beneficiar/Proiect” în tabelul mișcărilor (25 septembrie 2026)

- Antetul coloanei din tabelul de intrări/ieșiri este „BENEFICIAR/PROIECT”; conținutul și linkurile coloanei sunt neschimbate.
- BlazorStoc.Checks 413 trecute; browser (instanță de probă pe http://127.0.0.1:5083, modul demonstrativ, /produse/1): antetele „DATA, INTRARE/IEȘIRE, NUMĂR BUCĂȚI, DESCRIERE, BENEFICIAR/PROIECT, Acțiuni” pe un singur rând; la 768 px fără depășire orizontală, la 375 px pagina rămâne pe 375 px, iar tabelul defilează în propriul container.
- Neverificat: preview-ul de pe 5082 până la repornirea lui.