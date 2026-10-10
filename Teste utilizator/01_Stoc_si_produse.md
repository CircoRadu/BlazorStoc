# Stoc, produse si categorii (/produse, /categorii)

Fluxul: meniul Stoc -> lista de produse (cautare, categorii in stanga) -> adaugi/editezi un produs -> imagine, cod, categorie -> sterge cu motiv. Categoriile si subcategoriile se gestioneaza din Administrare -> Categorii.

## Modificari de testat

### 10.10.2026 - Ordinea categoriilor (drag and drop) si tipul parametrului cu comutatoare

**Ce s-a adaugat:** pe pagina Categorii si subcategorii, un maner (⋮⋮) la inceputul fiecarui card de categorie, vizibil numai administratorului (migrarea 38: coloana `categories.sort_order`); actiunea de jurnal „Reordonare categorii"; la „Parametru nou", alegerea tipului (Text liber / Numar) cu doua comutatoare On/Off intr-un chenar „Tip".

**Ce face acum:** administratorul trage cardurile de categorie (sau foloseste sagetile sus/jos pe maner); ordinea se salveaza imediat si este ordinea din meniul lateral „Categorii produse". Categoriile nearanjate raman alfabetic, iar o categorie noua se adauga la sfarsit. Utilizatorul fara drept de administrator nu vede manerul. La „Parametru nou", pornirea unui comutator il opreste pe celalalt.

1. Ca administrator, deschide „Categorii": fiecare card are in stanga un maner cu puncte. Trage o categorie peste alta: o linie verde arata unde ajunge; la eliberare apare mesajul cu pozitia noua.
2. Reincarca pagina si deschide meniul lateral „Produse": categoriile sunt in noua ordine, la fel pe pagina Categorii.
3. Pune focusul pe un maner (Tab) si apasa sageata jos / sus: categoria se muta cu o pozitie.
4. Adauga o categorie noua: apare ultima, in pagina si in meniu.
5. Conecteaza-te ca utilizator fara drept de administrator: nu exista manere, iar ordinea aranjata se vede in meniu.
6. In Jurnal apare „Reordonare categorii" cu „Ordinea veche" si „Ordinea noua".
7. La „Parametru nou" ai doua comutatoare in chenarul „Tip": „Text liber" si „Numar"; pornind unul se opreste celalalt, iar la „Numar" apare campul „Unitate de masura".

### 09.10.2026 - Parametri obligatori pe subcategorie (etapa 1)

**Ce s-a adaugat:** pe pagina Categorii si subcategorii, butonul „Parametri" pe fiecare subcategorie (migrarea 37: tabelele `subcategory_parameters`, `parameter_values`, `product_parameter_values` si coloana `products.base_model`); in formularul de produs, model + o valoare pentru fiecare parametru; marcajul „Parametri obligatorii necompletati · blocat" in lista de produse.

**Ce face acum:** pentru o subcategorie cu parametri (de exemplu „Lentila", numar, unitate mm), un produs se salveaza ca „<model> - <valoare> - <valoare>" (ordinea parametrilor) si nu poate fi salvat fara model si fara valoarea fiecarui parametru. Produsele care existau deja in subcategorie raman blocate (nicio intrare/iesire/rezervare pe ele) pana li se aleg valorile prin editare. Orice utilizator adauga parametri si valori noi si sterge valorile nefolosite; doar administratorul editeaza parametri si valori. O valoare folosita se modifica numai de administrator, cu previzualizare a produselor redenumite; daca un cod nou ar exista deja, modificarea se refuza.

1. Deschide „Categorii", la o subcategorie apasa „Parametri": panoul se deschide sub formulare. La o subcategorie nou creata panoul se deschide singur.
2. Adauga parametrul „Lentila", tip „Numar", unitate „mm". Adauga valorile „2,8" si „4": apar ca „2.8 mm" si „4 mm". Scrie „2.80": se refuza (exista deja).
3. In „Produse" cauta un produs deja existent in subcategorie: are marcajul „Parametri obligatorii necompletati · blocat". Incearca o intrare sau o iesire pe el: se refuza si mesajul numeste parametrii lipsa.
4. Editeaza produsul: campul „Model" contine vechiul cod; alege valoarea lentilei: sub formular se vede „Codul produsului rezultat: <model> - 2.8 mm". Salveaza (motivarea automata arata si „Parametri"). Marcajul dispare, intrarea/iesirea merg.
5. Adauga un produs nou in aceeasi subcategorie: cere model si valoarea; fara valoare apare „Subcategoria ... cere modelul si valoarea fiecarui parametru". Aceeasi combinatie model + valoare a doua oara: „Codul produsului ... exista deja".
6. In formularul de produs, la lentila scrie o valoare noua („6") si apasa „Adauga valoarea": apare in lista si este aleasa.
7. Pe panou, cu iconita cos de la o valoare folosita de produse: este inactiva, cu explicatia „Folosita de N produse". La o valoare nefolosita: stergere cu motivare.
8. Ca administrator, creionul de la o valoare folosita: scrie „2.9" si „Verifica modificarea": tabelul arata codurile vechi si noi; „Continua", motivare, „Aplica modificarea": produsele sunt redenumite cu stocul pastrat. Daca exista deja un produs cu unul dintre codurile noi, modificarea e refuzata si nu se schimba nimic.
9. Ca utilizator fara drept de administrator: panoul nu arata iconitele de editare a parametrilor si valorilor.
10. In Jurnal: „Adaugare parametru obligatoriu subcategorie", „Adaugare valoare parametru", „Stergere valoare parametru", „Modificare valoare parametru" si, pentru fiecare produs, „Redenumire produs (valoare parametru)".

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
