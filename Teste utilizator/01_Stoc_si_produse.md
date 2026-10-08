# Stoc, produse si categorii (/produse, /categorii)

Fluxul: meniul Stoc -> lista de produse (cautare, categorii in stanga) -> adaugi/editezi un produs -> imagine, cod, categorie -> sterge cu motiv. Categoriile si subcategoriile se gestioneaza din Administrare -> Categorii.

## Modificari de testat

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

## Fluxul de baza

1. Adauga produs (nume, cod, categorie/subcategorie) -> apare in lista.
2. Cauta dupa nume si dupa cod.
3. Editeaza cu motiv; Istoric.
4. Stergere: cere motiv; produsul cu miscari nu se pierde (arhivare).
5. Doua taburi pe acelasi produs: blocarea editarii apare ca banner (vezi 11_Utilizatori_si_acces.md).

## Ecran ingust

- Lista de produse si formularul pe latime de telefon, fara scroll orizontal.
