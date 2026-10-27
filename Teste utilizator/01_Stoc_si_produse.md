# Stoc, produse si categorii (/produse, /categorii)

Fluxul: meniul Stoc -> lista de produse (cautare, categorii in stanga) -> adaugi/editezi un produs -> imagine, cod, categorie -> sterge cu motiv. Categoriile si subcategoriile se gestioneaza din Administrare -> Categorii.

## Modificari de testat

### 27.10.2026 - Parametri: stergerea unui parametru care are produse

**Ce s-a adaugat:** iconita de stergere a unui parametru nu mai e blocata cand subcategoria are produse; fereastra de confirmare arata produsele afectate (cod actual / cod nou / stoc).

**Ce face acum:** produsele care aveau valoare pentru parametru o pierd, iar codul lor (nume) se rescrie fara ea, pastrandu-si stocul. Daca doua coduri ar deveni identice sau un cod exista deja la alt produs, stergerea e refuzata si numele care se bat sunt listate; nimic nu se schimba. Jurnalul are „Stergere parametru obligatoriu subcategorie” (cu numarul de produse actualizate) si cate o „Redenumire produs (valoare parametru)” pe produs. Daca parametrul era membru al unei familii cu doua membri, familia se desface.

**Pasi:** 1) intr-o subcategorie cu produse, sterge un parametru cu valori pe produse: vezi tabelul cu codurile; 2) confirma cu motiv - produsele apar cu codul nou, stocul neschimbat; 3) incearca sa stergi un parametru care distinge doua produse (ex. Marime L / M cu restul la fel) - e refuzat cu lista numelor care s-ar potrivi; 4) un produs ramas fara parametrii ceruti apare cu insigna „Parametri obligatorii necompletati” daca mai lipseste ceva.

### 27.10.2026 - Parametri: schimbarea tipului la editare

**Ce s-a adaugat:** in editarea unui parametru (Categorii -> subcategorie -> Parametri obligatorii), campul „Tip parametru” (Numar / Text liber / Interval).

**Ce face acum:** tipul se schimba daca toate valorile existente ale parametrului se potrivesc cu tipul nou: orice valoare se potriveste cu Text; „2.8” cu Numar; „2.8-12” cu Interval. Valorile care nu se potrivesc sunt listate in mesaj si se corecteaza/sterg intai. Unitatea se schimba odata cu tipul (Text nu are unitate), dar nu mai poate fi modificata cat timp exista produse cu parametrul (face parte din codul produsului). Jurnalul arata „Tip parametru” vechi -> nou. Desfacerea unei familii lasa parametrii ca parametri de sine statatori (fiecare obligatoriu separat), comportament neschimbat.

**Pasi:** 1) creeaza un parametru Text cu valori „2.8” si „4”, editeaza-l si alege Numar - se salveaza; 2) cu o valoare „alb” schimbarea in Numar e refuzata cu lista valorilor; 3) un parametru Numar cu unitate si produse nu se poate face Text (unitatea ar disparea din coduri); 4) desfa o familie: membrii raman in lista ca parametri separati.

### 27.10.2026 - Catalog produse: iconita de editare pe rand

**Ce s-a adaugat:** pe fiecare rand din tabelul de produse (Stoc), iconita creion „Editeaza produsul …”, langa „Iesire rapida” si „Detalii”; apare doar cu dreptul de editare produse.

**Ce face acum:** deschide direct editorul produsului (acelasi ca din pagina produsului), cu blocarea de editare; un produs aflat in editare de altcineva ramane blocat cu mesajul obisnuit.

**Pasi:** 1) Stoc: iconita creion pe un rand deschide editorul; 2) salveaza/inchide - revii la lista; 3) cu un cont fara drept de editare iconita nu apare.

### 27.10.2026 - Categorii: fereastra "Parametri si etichete" cu trei sectiuni
**Ce s-a adaugat:** fereastra unica "Parametri si etichete" a subcategoriei, cu trei sectiuni colapsabile, inchise la deschidere: Etichete, Familii de parametri, Parametri.
**Ce face acum:** fiecare sectiune are un tabel cu elementele definite (iconite de editare si stergere pe rand) si un buton "Adauga ..." care deschide formularul; valorile unui parametru se vad sub rand, cu iconita "Valorile parametrului". In pagina Categorii a ramas o singura iconita pe subcategorie.
1. Categorii -> iconita "Parametri si etichete ale subcategoriei X": cele trei sectiuni sunt inchise.
2. Deschide fiecare sectiune, apasa "Adauga etichetă / familie / parametru", completeaza si salveaza: elementul apare in tabel.
3. La un parametru apasa iconita "Valorile parametrului": se vad valorile, cu adaugare, editare, stergere.

### 27.10.2026 - Etichete pe subcategorie (On/Off, filtre in catalog)
**Ce s-a adaugat:** etichete definite pe subcategorie, puse On/Off pe fiecare produs; coloana ETICHETE in Categorii; iconita "Etichetele subcategoriei".
**Ce face acum:** o eticheta este optionala, nu intra in codul produsului si nu blocheaza stocul. In formularul produsului apar comutatoare On/Off, iar in catalog etichetele active apar ca insigne (coloana PARAMETRI/ETICHETE); un clic pe o eticheta filtreaza tabelul, mai multe etichete active cer ca produsul sa le aiba pe toate. Adaugarea o pot face operatorii de produse, redenumirea si stergerea administratorul (stergerea pastreaza produsele, doar le scoate eticheta).
1. Categorii: iconita "Etichetele subcategoriei X" -> "Etichete noua" -> adauga "PoE"; apare in coloana ETICHETE.
2. Editeaza un produs complet al subcategoriei, pune "PoE" pe On, salveaza (confirmarea arata "Etichete: — -> PoE").
3. In catalog eticheta apare pe produs; clic pe ea filtreaza, "Elimina etichetele" scoate filtrul.
4. Jurnal: "Adaugare/Redenumire/Stergere eticheta subcategorie", "Modificare etichete produs".

### 27.10.2026 - Parametru cu valoare fixa (ex. Fisheye) in familie
**Ce s-a adaugat:** tip nou de parametru, "Valoare fixa", pentru o subcategorie cu parametri.
**Ce face acum:** parametrul are o singura valoare, egala cu denumirea lui, si nu primeste alte valori. Lucreaza numai intr-o familie de parametri: la produs, alegerea lui in lista familiei da familiei valoarea (codul devine "Model - Fisheye"), fara nimic de completat.
1. Categorii -> subcategorie -> Parametri: adauga parametrul "Fisheye" cu tipul "Valoare fixa"; adauga "Varifocala" (Interval) si creeaza familia "Lentila" cu cele doua.
2. Editeaza un produs din subcategorie, alege la "Lentila" tipul "Fisheye": nu apar campuri de valoare, iar codul rezultat se termina cu "- Fisheye".
3. Un parametru fix nu are "Valoare noua", iar tipul lui nu se schimba; redenumirea si scoaterea din familie sunt refuzate cat timp produse il folosesc.

### 27.10.2026 - Ruta paginii in bara de sus si meniul ramas deschis la produs

**Ce s-a adaugat:** in bara de sus, in locul textului „Inventar / BlazorStoc”, ruta paginii: Meniu principal › sectiune › subsectiune › pagina, pe toate paginile (ex. Meniu principal › Produse › Stoc › Camere › Analogice › Produsul; Meniu principal › Administrare › Beneficiari › Fisa beneficiarului). Fiecare pas, in afara de ultimul, este link. Pe pagina unui produs, linkul „←” din antet are acum numele subcategoriei.

**Ce face acum:** pe pagina unui produs, meniul din stanga ramane cel de categorii, cu categoria deschisa si subcategoria produsului evidentiata; „Meniu principal” si linkurile din ruta duc inapoi la subcategorie, la stoc sau la pagina principala.

**Pasi:** 1) deschide un produs din tabel - meniul ramane deschis la subcategoria lui, ruta arata categoria si subcategoria; 2) clic pe subcategorie in ruta sau pe „←” - te intorci la lista subcategoriei; 3) parcurge cateva pagini (Beneficiari, o fisa de beneficiar, Setari, Jurnal) si verifica ruta; 4) pe pagina principala ruta arata doar „Meniu principal”.

### 27.10.2026 - Catalog produse: filtru „Doar incomplete” si parametri lipsa

**Ce s-a adaugat:** langa „Orice stoc”, selectorul „Toate produsele / Doar incomplete (N)” (apare doar cand in categoria/subcategoria curenta sunt produse incomplete); in coloana PARAMETRI, parametrii (sau familiile) necompletate apar ca etichete punctate rosii „Nume: lipsește”.

**Ce face acum:** „Doar incomplete” arata numai produsele blocate pentru ca le lipsesc parametri, combinabil cu subcategoria si etichetele. Clic pe o eticheta „lipsește” deschide editarea produsului (daca ai dreptul de editare). Adresa `/produse?categorie=...&subcategorie=...&incomplete=1` porneste direct filtrul. „Reseteaza filtrele” il opreste.

**Pasi:** 1) adauga un parametru nou unei subcategorii cu produse; 2) in Stoc, alege subcategoria: apare „Doar incomplete (N)”; 3) alege-l: raman produsele cu insigna ⚠; 4) in PARAMETRI vezi „… : lipsește”; clic -> editare; completeaza si salveaza - produsul dispare din lista; 5) cand nu mai sunt incomplete selectorul dispare.

### 27.10.2026 - Catalog produse: coloana PARAMETRI si etichete de filtrare

**Ce s-a adaugat:** in tabelul de produse (Stoc), coloana „PARAMETRI” dupa „SUBCATEGORIE”, afisata cand cel putin un produs din lista are parametri (subcategoria are parametri obligatorii definiti). Fiecare valoare e o eticheta (ex. „Rezolutie: 4 MP”).

**Ce face acum:** clic pe o eticheta filtreaza lista dupa acea valoare; pot fi active mai multe etichete (produsul trebuie sa le aiba pe toate). Deasupra tabelului apare „Filtrat dupa:” cu etichetele active (clic pe una o scoate) si „Elimina etichetele”. Etichetele se sterg cand schimbi categoria/subcategoria sau apesi „Reseteaza filtrele”.

**Pasi:** 1) Stoc -> alege o subcategorie cu parametri; 2) verifica coloana PARAMETRI; 3) clic pe o eticheta - raman doar produsele cu acea valoare, eticheta devine verde; 4) clic pe a doua eticheta (alt parametru) - lista se restrange; 5) „Elimina etichetele” readuce lista; 6) o subcategorie fara parametri nu arata coloana.

### 26.10.2026 - Parametri obligatori: tip Interval si familii de parametri

**Ce s-a adaugat:** in Categorii -> Parametri obligatori (popup): la „Tip” a aparut „Interval” (valorile au un numar de inceput si unul de sfarsit, ex. 2,8 - 12 mm) si sectiunea „Familii de parametri” (o familie are cel putin 2 parametri; un produs are exact unul dintre ei activ). In formularul de produs, familia apare ca o alegere „Tipul” (membrul activ) urmata de lista de valori a membrului ales.

**Ce face acum:** un parametru de tip Interval are doua campuri numerice (De la / Pana la) pentru o valoare noua; valoarea se salveaza ca „2.8-12” si apare in codul produsului ca „2.8-12 mm”; inceputul trebuie sa fie mai mic decat sfarsitul. O familie (ex. „Lentila”: Fixa + Varifocala) se creeaza doar de administrator; nu se poate forma daca un produs are deja valori pentru doi dintre membri (se listeaza produsele de corectat). Un produs are valoare la exact un membru: nici unul sau doi sunt refuzati; codul contine valoarea membrului activ la locul primului membru al familiei. Desfacerea familiei face fiecare parametru obligatoriu separat (produsele cu un singur membru devin incomplete). Stergerea unui parametru care lasa un singur membru desface familia.

Pasi:

1. Categorii -> la o subcategorie fara produse, iconita „Parametri obligatori”: adauga parametrul „Fixa” (Numar, mm) cu valorile 2,8 si 4; adauga „Varifocala” (Interval, mm): la „Interval nou” scrie De la 2,8 si Pana la 12 -> apare „2.8-12 mm”; incearca De la 12, Pana la 2,8 -> mesaj ca inceputul trebuie sa fie mai mic. Adauga si „Culoare” (Text) cu „alb”.
2. La „Familii de parametri”: bifeaza doar „Fixa” -> „Adauga familia” ramane inactiv; bifeaza si „Varifocala”, denumeste „Lentila”, „Adauga familia”. Cardul familiei arata cei doi parametri, iar parametrii arata „Familie: Lentila”.
3. Produse -> Adauga produs in subcategorie: model „CAM-T”; la „Lentila” alege tipul „Varifocala”, apoi valoarea 2.8-12, la Culoare „alb”: „Codul produsului rezultat” = „CAM-T - 2.8-12 mm - alb”. Schimba tipul in „Fixa”: valoarea varifocala dispare si produsul devine incomplet pana alegi 2.8 mm; salveaza.
4. Dupa ce exista produse: creeaza o alta familie cu doi parametri la care un produs are deja ambele valori -> refuzat, cu numele produsului. Desfa familia „Lentila” (iconita cos, cu motiv): produsele cu un singur membru devin incomplete si nu primesc miscari de stoc pana nu se completeaza sau nu refaci familia.
5. Jurnal: „Adaugare familie de parametri”, „Modificare familie de parametri”, „Desfacere familie de parametri” cu motivul si numarul produselor devenite incomplete.

### 22.10.2026 - Imagine produs: lipire cu Ctrl + V fara buton

**Ce s-a adaugat:** in formularul de produs, textul „sau lipeste imaginea copiata cu Ctrl + V" sta langa zona „Alege imaginea"; butonul „Preia imaginea din clipboard" si caseta de lipire au fost scoase.

**Ce face acum:** Ctrl + V lipeste imaginea copiata oriunde in formular (fara sa selectezi o zona); butonul nu mai exista.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Deschide „Adauga produs"; copiaza o imagine (clic dreapta → Copiaza imaginea) si apasa Ctrl + V cu cursorul oriunde in formular (ex. in campul Descriere) | Imaginea apare in previzualizare; textul din camp nu se modifica | |
| 2 | Priveste randul „Alege imaginea" | Langa zona de alegere apare „sau lipeste imaginea copiata cu Ctrl + V"; nu exista buton de clipboard | |

### 10.10.2026 - Ordinea subcategoriilor (drag and drop)

**Ce s-a adaugat:** aceeasi aranjare prin tragere, acum si pentru subcategoriile fiecarei categorii (migrarea 41, `subcategories.sort_order`; jurnal „Reordonare subcategorii").

**Ce face acum:** pe pagina Categorii, administratorul vede un maner pe fiecare rand de subcategorie si il trage (sau foloseste sagetile sus/jos pe maner) in cadrul aceleiasi categorii; ordinea se salveaza imediat si este ordinea din meniul lateral. O subcategorie noua se adauga la sfarsitul categoriei; cele nearanjate raman alfabetice. Doar administratorul poate aranja.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | /categorii, categorie cu cel putin 3 subcategorii: trage maner-ul unui rand peste alt rand | Linie verde deasupra/dedesubtul randului tinta; la lasare randul se muta | |
| 2 | Mesajul de dupa mutare | „Subcategoria ... a fost mutata pe pozitia N din ..." | |
| 3 | Meniul lateral „Categorii produse", aceeasi categorie | Subcategoriile in noua ordine | |
| 4 | Reincarca pagina | Ordinea ramane | |
| 5 | Adauga o subcategorie noua in categorie | Apare ultima | |
| 6 | Pe maner: sageata sus / jos de la tastatura | Randul se muta o pozitie | |
| 7 | Incearca sa tragi un rand in alta categorie | Nu se intampla nimic (doar in aceeasi categorie) | |
| 8 | /jurnal, actiunea „Reordonare subcategorii" | Un eveniment cu ordinea veche si cea noua | |
| 9 | Cont care nu este administrator | Fara manere pe subcategorii | |

### 10.10.2026 - Ordinea categoriilor (drag and drop) si tipul parametrului cu comutatoare

**Ce s-a adaugat:** pe pagina Categorii si subcategorii, un maner (⋮⋮) la inceputul fiecarui card de categorie, vizibil numai administratorului (migrarea 38: coloana `categories.sort_order`); actiunea de jurnal „Reordonare categorii"; la „Parametru nou", alegerea tipului (Text liber / Numar) cu doua comutatoare On/Off intr-un chenar „Tip".

**Ce face acum:** administratorul trage cardurile de categorie (sau foloseste sagetile sus/jos pe maner); ordinea se salveaza imediat si este ordinea din meniul lateral „Categorii produse". Categoriile nearanjate raman alfabetic, iar o categorie noua se adauga la sfarsit. Utilizatorul fara drept de administrator nu vede manerul. La „Parametru nou", pornirea unui comutator il opreste pe celalalt.

1. Ca administrator, deschide „Categorii": fiecare card are in stanga un maner cu puncte. Trage o categorie peste alta: o linie verde arata unde ajunge; la eliberare apare mesajul cu pozitia noua.
2. Conecteaza-te ca utilizator fara drept de administrator: nu exista manere, iar ordinea aranjata se vede in meniu.

### 09.10.2026 - Parametri obligatori pe subcategorie (etapa 1)

**Ce s-a adaugat:** pe pagina Categorii si subcategorii, butonul „Parametri" pe fiecare subcategorie (migrarea 37: tabelele `subcategory_parameters`, `parameter_values`, `product_parameter_values` si coloana `products.base_model`); in formularul de produs, model + o valoare pentru fiecare parametru; marcajul „Parametri obligatorii necompletati · blocat" in lista de produse.

**Ce face acum:** pentru o subcategorie cu parametri (de exemplu „Lentila", numar, unitate mm), un produs se salveaza ca „<model> - <valoare> - <valoare>" (ordinea parametrilor) si nu poate fi salvat fara model si fara valoarea fiecarui parametru. Produsele care existau deja in subcategorie raman blocate (nicio intrare/iesire/rezervare pe ele) pana li se aleg valorile prin editare. Orice utilizator adauga parametri si valori noi si sterge valorile nefolosite; doar administratorul editeaza parametri si valori. O valoare folosita se modifica numai de administrator, cu previzualizare a produselor redenumite; daca un cod nou ar exista deja, modificarea se refuza.

1. Deschide „Categorii", la o subcategorie apasa „Parametri": panoul se deschide sub formulare.
2. La parametrul Lentila (Numar, mm) adauga valoarea 4: apare ca 4 mm.
3. In „Produse" cauta un produs deja existent in subcategorie: are marcajul „Parametri obligatorii necompletati · blocat". Incearca o intrare sau o iesire pe el: se refuza si mesajul numeste parametrii lipsa.
4. Editeaza produsul: campul „Model" contine vechiul cod; alege valoarea lentilei: sub formular se vede „Codul produsului rezultat: <model> - 2.8 mm". Salveaza (motivarea automata arata si „Parametri"). Marcajul dispare, intrarea/iesirea merg.
5. Adauga un produs nou in aceeasi subcategorie: cere model si valoarea; fara valoare apare „Subcategoria ... cere modelul si valoarea fiecarui parametru". Aceeasi combinatie model + valoare a doua oara: „Codul produsului ... exista deja".
6. In formularul de produs, la lentila scrie o valoare noua („6") si apasa „Adauga valoarea": apare in lista si este aleasa.
7. Pe panou, cu iconita cos de la o valoare folosita de produse: este inactiva, cu explicatia „Folosita de N produse". La o valoare nefolosita: stergere cu motivare.
8. Ca administrator, creionul de la o valoare folosita: scrie „2.9" si „Verifica modificarea": tabelul arata codurile vechi si noi; „Continua", motivare, „Aplica modificarea": produsele sunt redenumite cu stocul pastrat. Daca exista deja un produs cu unul dintre codurile noi, modificarea e refuzata si nu se schimba nimic.
9. Ca utilizator fara drept de administrator: panoul nu arata iconitele de editare a parametrilor si valorilor.
10. In Jurnal: Stergere valoare parametru, Modificare valoare parametru si, pentru fiecare produs, Redenumire produs (valoare parametru).

**Detalii:** preluarea de pe factura (alegerea variantei) este in `04_Preluare_factura_si_sabloane.md`.

### 08.10.2026 - Stoc minim pe produs si export consum

**Ce s-a adaugat:** pe cardul „Unde sunt bucatile" al produsului, campul „Stoc minim (buc.)" (migrarea 29, tabelul `product_min_stock`); pagina „Export consum" (`/consum`, meniul Produse > Export consum).

**Ce face acum:** daca stocul produsului scade sub minim, apare avertismentul „Stoc sub minim: lipsesc N buc." si, cu un sablon de notificare activ (vezi 09), o notificare „Stoc sub minim". Pagina de consum arata, pe beneficiar/proiect/produs, cat s-a iesit, cat s-a returnat si consumul net, pentru filtrele alese, si le descarca ca CSV.

1. Pe pagina unui produs scrie un stoc minim mai mare decat stocul si apasa „Setează": apare avertismentul cu cate bucati lipsesc; cu × minimul se elimina. In Jurnal: „Setare stoc minim" / „Eliminare stoc minim".
2. Fa o intrare care aduce stocul la minim sau peste: avertismentul dispare.
3. Deschide „Export consum": alege un beneficiar (apoi un proiect), o perioada si apasa „Arata consumul": tabelul arata iesit / returnat / net.
4. Apasa „Descarca CSV": fisierul `consum.csv` se deschide in Excel cu aceleasi randuri (separator `;`).
5. Foloseste o perioada fara iesiri: apare „Nu exista consum pentru filtrele alese".

**Detalii:**

- Stoc minim: se seteaza si se elimina pe cardul „Unde sunt bucatile"; sub minim apare avertisment si, cu sablon activ, notificarea „Stoc sub minim".
- Export consum: pagina `/consum` (meniul Produse), filtre beneficiar, proiect, perioada; arata iesit, returnat, consum net si descarca CSV (`/api/consum.csv`).
- Jurnal: „Setare stoc minim" si „Eliminare stoc minim"; tabela `product_min_stock` este inclusa in arhivare.
- Schema: migrarea 29 aplicata pe `BlazorStoc`; pe `blazorstoc_test` tabelele au fost create direct (contul migrator nu are drepturi acolo).
- Stocul comparat cu minimul este stocul total (depozit + masini).
- Cod: `Components/Shared/ProductMinStockField.razor`, `Components/Pages/ConsumptionExportPage.razor`, `Services/ConsumptionExport.cs`, `Services/StockAlerts.cs`, `Services/MariaStockAlerts.cs`; teste in `MariaExtendedChecks` („Stock alerts").

### 08.10.2026 - Cardul „Unde sunt bucatile" pe pagina produsului

**Ce s-a adaugat:** pe pagina unui produs (intrari/iesiri), sub rezervari, cardul „Unde sunt bucatile" (vizibil utilizatorilor care gestioneaza produsele).

**Ce face acum:** arata cate bucati sunt in depozit, cate in fiecare masina si cate au fost predate fiecarui beneficiar/proiect (iesirile catre beneficiar minus returnarile; stornarile nu se numara). Daca toate bucatile sunt in depozit, spune asta.

1. Deschide un produs care a avut iesiri catre un beneficiar cu proiect: cardul arata „Beneficiar · Proiect: N buc. predate".
2. Fa o iesire catre o masina: apare linia „Masina XX-NN-ABC: N buc.", iar depozitul scade.
3. Returneaza o parte dintr-o iesire catre beneficiar: cantitatea predata scade; la returnare totala linia dispare.
4. Storneaza o iesire: nu mai apare in card.

**Detalii:**

- Cod: `Components/Shared/ProductWhere.razor`, `Services/ProductPlacement.cs`, `Services/MariaProductPlacementReader.cs`; test in `MariaExtendedChecks` (sectiunea Reservations).

### 08.10.2026 - Iesire rapida din lista de produse

**Ce s-a adaugat:** pe fiecare rand al listei de produse (pentru utilizatorii care gestioneaza produsele) un buton ⇥ „Iesire rapida", care deschide formularul de iesire intr-un dialog, fara parasirea listei.

**Ce face acum:** formularul este cel din „Iesire multipla" (aceleasi reguli: peste stoc, rezervari, iesire repetata, bon PDF), cu produsul randului deja ales; se pot adauga si alte produse. Dupa „Inchide" lista se reimprospateaza cu stocul nou.

1. In `/produse` apasa ⇥ pe un produs cu stoc: se deschide dialogul cu produsul ales si stocul din depozit afisat.
2. Alege destinatia, cantitatea, apoi „Verifica si salveaza": apare mesajul operatiei si linkul bonului PDF.
3. Apasa „Inchide": stocul din lista este actualizat.
4. Cu un utilizator fara drept de gestiune butonul ⇥ nu apare.

**Detalii:**

- Buton nou: pe fiecare rand din `/produse` apare ⇥ „Iesire rapida", doar pentru cei care gestioneaza produsele.
- Dialog: se deschide formularul din „Iesire multipla" fara sa parasesti lista, cu produsul randului deja ales; regulile sunt aceleasi (peste stoc, rezervari, iesire repetata, bon PDF).
- Dupa „Inchide": lista se reincarca cu stocul nou.
- Cod: `ExitOperation.razor` are parametrii `Embedded`, `InitialProductId`, `OnClosed`; dialogul este in `Home.razor`; clasa `.quick-exit-modal` in `wwwroot/app.css`.

### 07.10.2026 - Tabul „De regularizat” si reimprospatare fara licarire

**Ce s-a adaugat:** pe pagina Produse, tab „De regularizat” (produsele cu stoc negativ dupa iesiri peste stoc, cu cauza, vechime, filtre si actiunea Regularizeaza; eticheta tabului arata numarul) si butonul „Ieșire multiplă” (vezi 02). Reimprospatarea automata a paginii nu mai arata spinner si nu mai licareste. Pasii: 02_Intrari_si_iesiri_produs.md (pasii 5-13 din sectiunea cauzei).

**Ce face acum:** Produsele cu stoc negativ apar intr-o lista „De regularizat" cu cauza si vechime, iar paginile se actualizeaza singure fara sa palpaie.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Produse, tabul „De regularizat” | Lista descrisa in 02; revenirea la „Catalog” pastreaza cautarea | |
| 2 | Lasa pagina Produse deschisa 1-2 minute | Fara licarire la reimprospatarea automata | |

### 06.10.2026 - Categorii: anulare dupa mutarea unei subcategorii

**Ce s-a adaugat:** La editarea unei subcategorii, categoria se alege dintr-un grup de optiuni afisat direct in formular, iar mutarea poate fi anulata.

**Ce face acum:** Mutarea unei subcategorii in alta categorie se face dintr-un grup de optiuni vizibil si se poate anula dupa mutare.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Administrare -> Categorii: editeaza o subcategorie si muta-o in alta categorie (categoria se alege dintr-un grup de optiuni, nu dintr-o lista care se deschide) | Optiunile sunt afisate direct in formular | |
| 2 | Apasa "Anuleaza", apoi "Paraseste editarea fara salvare" | Avertizarea apare deasupra; dupa "Paraseste" formularul se inchide | |

### 05.10.2026 - Formularul de produs

**Ce s-a adaugat:** Cautarea imaginii pe internet dupa cod, direct din formularul de produs (si la editare).

**Ce face acum:** Din formular cauti o imagine dupa cod, o alegi din popup si ea se pune in formular.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Produs nou: "Cauta imaginea pe internet" dupa cod | Popup cu rezultate; alegerea unei imagini o pune in formular (si la editare) | |
| 2 | Creeaza o categorie noua in popup si revino la formular | Categoria apare in lista | |
| 3 | Anuleaza formularul dupa ce ai ales o imagine | Imaginea nu ramane salvata | |

### 07.10.2026 - Stoc in bucati

**Ce s-a adaugat:** Stocul se tine doar in bucati: nu mai exista unitate de masura sau metraje.

**Ce face acum:** Formularul de produs si listele arata cantitati in „buc."; liniile cu metri sau ore din oferte raman in afara stocului.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Formularul de produs si lista | Nu exista unitate de masura; cantitatile sunt in "buc." | |

### 09.10.2026 - Categorii si subcategorii: nume unice, stergere, iconite

**Ce s-a adaugat:** Numele categoriilor si subcategoriilor sunt unice impreuna; se pot sterge categorii si subcategorii goale; antetul categoriei are iconite; categoria unei subcategorii se alege (cu cautare) doar la mutare; listele de alegere (furnizor, beneficiar) se aleg cu clic si se pot filtra scriind.

**Ce face acum:** Pagina Categorii: la fiecare categorie iconitele plus (adauga subcategorie), creion, cos (inactiv cu explicatie in tooltip daca nu e goala); la subcategorie creion si cos (doar daca nu are produse). La adaugarea unei subcategorii nu mai exista alegerea categoriei. Jurnalul are actiunile „Stergere categorie goala" si „Stergere subcategorie goala".

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Adauga o subcategorie cu numele unei categorii (si invers) | Mesaj ca numele e deja folosit | |
| 2 | Sterge o subcategorie goala, apoi categoria goala (motiv + cuvant de confirmare) | Dispar; jurnalul le consemneaza cu actiuni proprii | |
| 3 | Treci cu mouse-ul pe cosul unei categorii cu subcategorii | Cos inactiv, tooltip cu explicatia | |
| 4 | Editeaza o subcategorie: campul Categorie | Camp cu alegere si scriere pentru filtrare | |
| 5 | Export consum si filtrele Furnizor/Beneficiar din Miscari: alege cu clic | Alegerea se pastreaza (si in Brave) | |

## Fluxul de baza

1. Adauga produs (nume, cod, categorie/subcategorie) -> apare in lista.
2. Cauta dupa nume si dupa cod.
3. Editeaza cu motiv; Istoric.
4. Stergere: cere motiv; produsul cu miscari nu se pierde (arhivare).
5. Doua taburi pe acelasi produs: blocarea editarii apare ca banner (vezi 11_Utilizatori_si_acces.md).

## Ecran ingust

- Lista de produse si formularul pe latime de telefon, fara scroll orizontal.
