# Intrari si iesiri pe produs (/produse/{id}/miscari)

Fluxul: deschizi un produs -> pagina de miscari -> adaugi intrare sau iesire -> vezi tabelul, filtrele, istoricul si jurnalul. Stocul se tine doar in bucati (fara metraje de cablu). Principiu: o iesire peste stoc se poate inregistra oricand, fara motiv; stocul negativ este o eroare de operare si se corecteaza prin regularizare.

## Modificari de testat

### 10.10.2026 - Pagina de miscari a produsului, reorganizata in cod

**Ce s-a schimbat:** nimic vizibil; codul paginii `/produse/{id}/miscari` a fost impartit pe fisiere. Verificarea este ca totul se comporta ca inainte.

**Ce faci:**
1. Adauga o iesire (tabul Iesire) pe un produs TEST si sterge-o.
2. Foloseste cautarea, listele de provenienta si de destinatie si filtrul pe zi, apoi schimba pagina tabelului.

**Ce trebuie sa vezi:** aceleasi ecrane si mesaje ca inainte; nicio eroare in pagina. Rezultat: ...

### 07.10.2026 - Rezervari la iesire si intrare legata de oferta

**Ce s-a adaugat:** la iesire, avertisment (fara blocare) cand ar lua bucati rezervate de alte proiecte, cu alegerea continua / scade rezervarea; la intrare, selectorul "Pentru oferta" si propunerile de rezervare dupa salvare; pe pagina produsului, "Rezervat ... stoc liber". Pasii sunt in 06_Beneficiari_si_proiecte.md.


**Ce face acum:** La iesire apare avertisment (fara blocare) cand ar lua bucati rezervate de alte proiecte, la intrare poti lega oferta, iar pe pagina produsului vezi „Rezervat / stoc liber".
### 07.10.2026 - Componenta la iesirea spre un proiect

**Ce s-a adaugat:** la o iesire spre beneficiar cu proiect (formularul de iesire de pe produs si Iesire multipla, pe fiecare linie) apare selectorul **Componenta** (doar daca proiectul are componente). Implicit "Automat" (componenta cu produsul in oferta; "in afara ofertei" daca nu e in nicio oferta); trebuie aleasa daca produsul e in mai multe componente. Pasii de test sunt in 06_Beneficiari_si_proiecte.md.


**Ce face acum:** O iesire spre un proiect se leaga de o componenta a proiectului: automat cand produsul e intr-o singura componenta, „in afara ofertei" cand nu e in nicio oferta, la alegere cand e in mai multe.
### 07.10.2026 - Bon de consum, storno de operatie, retur legat de iesire, consum net

**Ce s-a adaugat:** (1) **Bon de consum / aviz de predare** (PDF) pentru fiecare operatie de iesire: buton „Bon” pe randul iesirii (pagina produsului) si link dupa salvarea unei iesiri multiple; contine data, destinatia, beneficiarul (CUI), proiectul, referinta, produsele cu cantitati si sursa, si casete de semnatura „Predat de / Primit de”. (2) **Storno de operatie**: butonul „Stornează” pe randul iesirii anuleaza toate iesirile operatiei, cu motiv; iesirile raman in pagina marcate „stornat” (taiate), stocul si cantitatile din masini se refac, jurnalul are „Stornare operatie de iesire”; iesirile stornate nu se mai pot edita sau sterge. (3) **Retur legat de iesire**: la intrare libera cu motivul „Primit de la beneficiar / vehicul” apare lista „Ieșirea returnată (opțional)”; returul poate fi partial, nu poate depasi ce a ramas de returnat, iar jurnalul are „Retur de la beneficiar”. (4) **Consum net** (iesiri minus retururi) pe pagina proiectului (Echipamente) si pe pagina beneficiarului. Schema: migrarea 21 (storno + legatura returului). Componente noi: `ConsumptionNotePdfWriter`, `NetConsumptionSection`.

**Ce face acum:** Fiecare operatie de iesire are bon PDF; o operatie gresita se stornează (stocul revine, iesirile raman ca urma); un retur legat de iesire scade consumul net al proiectului si al beneficiarului.

**Flux:** iesire (simpla sau multipla) -> bon PDF la predare -> daca marfa se intoarce partial: intrare „Primit de la beneficiar” legata de iesire -> consumul net scade -> daca operatia a fost gresita din start: storno (doar daca nu are retururi).

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Pe randul unei iesiri apasa „Bon” | Se deschide un PDF cu titlul „Bon de consum / aviz de predare”, nr. operatiei, data, beneficiar (CUI), proiect, referinta, tabel cu produse si cantitati, casetele de semnatura, diacriticele corecte | |
| 2 | Bonul unei iesiri multiple (3 produse) | Toate liniile operatiei apar pe bon; descrierile lungi se continua pe randul urmator, fara sa iasa din tabel | |
| 3 | „Stornează” pe o iesire a unei operatii cu 2 linii; fara motiv, apoi cu motiv | Fara motiv: eroare; cu motiv: ambele iesiri apar taiate cu eticheta „stornat”, stocul revine, mesaj „Operatia #… a fost stornata” | |
| 4 | Pe iesirile stornate | Nu mai au Editeaza/Sterge, dar au „Bon” (bonul arata „OPERAȚIE STORNATĂ” cu motivul); la hover pe „stornat” se vede cine si de ce | |
| 5 | Jurnal | „Stornare operatie de iesire” cu produsele si motivul | |
| 6 | Storno pentru iesire spre o masina, dupa ce produsul a fost folosit din masina | Refuzat cu mesajul despre stoc negativ in masina; nimic nu se schimba | |
| 7 | Intrare libera, motiv „Primit de la beneficiar / vehicul” | Apare lista „Ieșirea returnată”, cu data, beneficiar/proiect, referinta si „rămase N din M buc.” | |
| 8 | Alege o iesire de 5 buc. si returneaza 2, apoi 4 | 2 se salveaza (Istoric/jurnal „Retur de la beneficiar”, in tabel „retur al ieșirii #…”); 4 este refuzat cu „depășește cantitatea … (3 buc.)”; 3 se salveaza | |
| 9 | Dupa returnarea integrala | Iesirea nu mai apare in lista de retururi | |
| 10 | Incearca storno pe o operatie care are retururi | Refuzat cu mesajul despre retururi | |
| 11 | Pagina proiectului -> Echipamente si pagina beneficiarului | Sectiunea „Consum net”: iesiri, retururi, consum net pe produs (iesirile stornate nu se numara); nu apare daca nu exista iesiri; un utilizator fara drept nu o vede | |
| 12 | Lista iesirilor proiectului (Echipamente) | Nu include iesirile stornate | |
| 13 | Ecran ingust | Randul cu butoanele Bon/Stornează nu iese din tabel (tabelul se deruleaza) | |

### 07.10.2026 - Ieșire multiplă (operația de ieșire / coșul de ieșiri)

**Ce s-a adaugat:** pagina `Ieșire multiplă` (`/iesiri/multipla`; meniu Produse -> Ieșire multiplă și buton pe pagina Produse): o destinație, o dată și o referință comune pentru mai multe produse, fiecare cu cantitate, sursă (depozit sau mașină) și descriere proprie. Se salvează toate liniile sau niciuna. Orice ieșire (și cea simplă) aparține acum unei operații (`operation_id`, migrarea 20): operația are id-ul primei ieșiri, iar jurnalul arată „Operație: #id” pe fiecare ieșire. Regulile unei ieșiri simple se aplică pe fiecare linie: peste stoc permis și marcat, ieșire repetată cu motiv; avertismentele se strâng într-un rezumat înainte de salvare.

**Ce face acum:** Mai multe produse ies intr-o singura operatie, cu destinatie comuna, iar avertismentele (peste stoc, iesire repetata, rezervari) apar in rezumat inainte de salvare; operatia se salveaza intreaga sau deloc.

**Flux:** alegi data, destinația (beneficiar/proiect/vehicul/vânzare/corecție), referința și descrierea comună -> adaugi produse (căutare, cantitate, sursă) -> „Verifică și salvează” -> dacă nu sunt avertismente se salvează direct, altfel apare rezumatul (depășiri de stoc cu cauză opțională, ieșiri repetate cu motiv) -> „Salvează operația”.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Produse -> „Ieșire multiplă”; alege vânzare generică, referința „Aviz 5”, descriere comună, apoi 3 produse cu stoc suficient | La fiecare produs se vede „Depozit: N buc.”; „Verifică și salvează” salvează direct, mesaj „Operația #… a fost salvată: 3 ieșiri” | |
| 2 | Pe pagina fiecărui produs și în /jurnal | Fiecare ieșire apare cu referința „Aviz 5”, iar jurnalul are „Vânzare generică” cu „Operație: #id” (același id la toate trei) | |
| 3 | O linie cu cantitate mai mare decât stocul | Apare rezumatul galben: „depășește stocul cu N buc.; stocul va fi …”, cu alegerea cauzei (opțional) | |
| 4 | „Salvează operația” din rezumat | Se salvează; pe produs ieșirea e „peste stoc” cu cauza aleasă; produsul apare în „De regularizat” | |
| 5 | Repetă aceeași operație, aceeași zi | Rezumatul spune „ieșire repetată” și cere „Motiv pentru ieșirile repetate”; fără motiv: eroare; cu motiv: se salvează, jurnal „Ieșire dublată (confirmat)” | |
| 6 | Linie cu sursa „Mașina X” (apare în listă doar dacă produsul e în mașină) | Se scoate din mașină; peste cantitatea din mașină se permite, marcat „peste stoc” | |
| 7 | „Modific” din rezumat | Revii la formular fără să se fi salvat nimic | |
| 8 | Elimină o linie (×), adaugă altele; încearcă fără produs sau cantitate | Mesaj „Linia N: alege produsul și o cantitate mai mare ca zero.” | |
| 9 | Destinație „Beneficiar” + proiect | Alegerea proiectului funcționează ca la ieșirea simplă; fără destinație completă se refuză | |
| 10 | Fă o ieșire simplă pe pagina unui produs | Jurnalul arată „Operație: #id” și la ea (operație cu o singură linie) | |
| 11 | Ecran îngust | Tabelul cu linii se derulează orizontal în interiorul lui, fără să iasă din pagină | |

Neverificat încă: legarea de bonul de consum / storno (Task următor).

### 07.10.2026 - Cauza iesirii peste stoc, lista "De regularizat", reimprospatare fara licarire

**Ce s-a adaugat:** (1) camp optional "Cauza ieșirii peste stoc" (Necunoscuta / Intrare neoperata / Stoc gresit in baza) la iesirile care depasesc stocul, in formularul de adaugare si in editare; cauza apare in tabel langa marcajul "peste stoc", in Istoric si in jurnal. (2) Tab nou "De regularizat" pe pagina Produse (`/produse?tab=de-regularizat`), componenta `ToRegularizeTab`: produsele cu stoc negativ dupa iesiri peste stoc, cu cauza, data primei iesiri neacoperite si vechimea, filtre pe cauza si vechime, actiunea "Regularizeaza". (3) Contor "N produse de regularizat" pe pagina principala. (4) Notificare "Ieșiri peste stoc nerezolvate" (sablon in Setari -> Notificari). (5) Situatia de inventar listeaza primele produsele cu stoc negativ. (6) Pagina de miscari se reimprospateaza in fundal, fara spinner si fara redesenare daca nu s-a schimbat nimic.

**Ce face acum:** O iesire peste stoc se inregistreaza oricand, cu cauza optionala; produsul intra in „De regularizat" pana se regularizeaza.

**Flux:** faci o iesire mai mare decat stocul -> alegi (optional) cauza -> produsul apare in "De regularizat" -> numeri stocul real -> "Regularizeaza" -> produsul dispare din lista, notificarea se inchide singura.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Iesire mai mare decat stocul din depozit | Sub formular apare nota de depasire si grupul "Cauza ieșirii peste stoc (opțional)" cu 3 optiuni; fara nota, grupul nu apare | |
| 2 | Alege "Intrare neoperata" si salveaza | In tabel: "peste stoc · Intrare neoperata"; jurnalul are "Cauza peste stoc: Intrare neoperata" | |
| 3 | Editeaza iesirea marcata si schimba cauza in "Stoc gresit in baza" | Istoric: "Cauza peste stoc: Intrare neoperata -> Stoc gresit in baza" | |
| 4 | Depaseste stocul, alege o cauza, apoi scade cantitatea sub stoc si salveaza | Grupul de cauze dispare si cauza nu se pastreaza pe iesire | |
| 5 | Produse -> tab "De regularizat" | Lista: produs, stoc negativ (rosu), nr. iesiri, "de la" data, vechime in zile, cauza; eticheta tabului arata (N) | |
| 6 | Filtre: Intrare neoperata / Stoc gresit / Fara cauza; Vechime "cel putin 7 zile" | Lista se restrange corect; fara rezultate: mesaj "Nimic de regularizat ... pentru filtrele alese" | |
| 7 | "Regularizeaza": introdu cantitatea reala (sau "Trec stocul pe 0") si confirma | Mesaj de succes, produsul dispare din lista, contorul scade; pe pagina produsului stocul = cantitatea reala; jurnal "Regularizare stoc negativ" | |
| 8 | Produs al carui stoc total nu e negativ dar depozitul da (ai piese in masina) | In loc de buton: link "Deschide produsul" cu explicatie | |
| 9 | Pagina principala | Pe cardul "Produse si stocuri" apare "N produse de regularizat" cand exista | |
| 10 | Setari -> Notificari: sablonul "Ieșiri peste stoc nerezolvate" (prag implicit 7 zile) | Se poate crea/edita; pe /notificari apare dupa prag pentru un produs negativ vechi; dupa regularizare se inchide singura | |
| 11 | Inventar -> Generare situatie, subcategorie cu un produs negativ | Produsul negativ e primul din subcategorie, marcat negativ | |
| 12 | Lasa pagina de miscari deschisa 1-2 minute (60 s intre reimprospatari) | Tabelul nu licareste, nu apare spinnerul "Se incarca miscarile"; la o schimbare facuta din alt tab, datele se actualizeaza fara licarire | |
| 13 | Acelasi lucru pe Produse, Furnizori, Beneficiari, Vehicule, pagina unui vehicul/utilizator | Fara licarire la reimprospatarea automata | |

### 07.10.2026 - Iesiri: referinta, jurnal pe destinatie, filtre, iesire dublata, folosire din vehicul

**Ce s-a adaugat:** campul Referinta (aviz, bon, numar predare) la iesiri, afisat in tabel, Istoric si jurnal; actiuni de jurnal exacte pe destinatie (Iesire spre beneficiar/vehicul, Vanzare generica, Corectie stoc); filtre Destinatie/Beneficiar/Vehicul pe iesiri; avertisment cu motiv la iesire repetata in aceeasi zi (Iesire dublata - confirmat); folosirea dintr-o masina peste cantitatea ei se inregistreaza si se marcheaza „peste stoc”, iar mutarile fizice (transfer, restituire) raman verificate.

**Ce face acum:** Iesirile au referinta (aviz, bon) si jurnal cu actiunea exacta pe destinatie, se filtreaza dupa destinatie/beneficiar/vehicul, iar o iesire repetata in aceeasi zi cere motiv.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Pe un produs cu stoc, adauga o iesire spre un beneficiar si completeaza "Referinta (aviz, bon, numar predare)" cu "Aviz 77" | Iesirea apare in tabel cu "ref. Aviz 77" langa descriere | |
| 2 | Editeaza iesirea si schimba referinta in "Aviz 78", cu motiv | Referinta noua in tabel; Istoric arata "Referinta: Aviz 77 -> Aviz 78" | |
| 3 | Fa cate o iesire spre: beneficiar, vehicul, vanzare generica, corectie stoc | Pe /jurnal fiecare are actiunea ei exacta: "Iesire spre beneficiar", "Iesire spre vehicul", "Vanzare generica", "Corectie stoc" (nu "Creare") | |
| 4 | In tabelul de miscari alege filtrul Iesiri | Apar filtrele Destinatie, Beneficiar, Vehicul (ultimele doua doar daca produsul are iesiri spre ele) | |
| 5 | Filtreaza dupa Destinatie = Vanzare generica, apoi dupa un beneficiar, apoi dupa un vehicul | Se vad doar iesirile potrivite; pagina revine la 1; trecerea la Intrari sau Toate reseteaza filtrele | |
| 6 | Adauga de doua ori, in aceeasi zi, aceeasi iesire (aceeasi cantitate, destinatie, beneficiar) | A doua oara apare panoul "Aceeasi iesire a fost deja inregistrata..." cu campul "Motiv pentru iesirea repetata" | |
| 7 | In panou apasa "Adauga totusi" fara motiv, apoi cu motiv | Fara motiv: mesaj de eroare; cu motiv: se adauga; jurnalul arata "Iesire dublata (confirmat)" cu motivul | |
| 8 | In panou apasa "Renunt" | Panoul dispare, nu se adauga nimic | |
| 9 | Aceeasi iesire dar cu alta cantitate sau alt beneficiar | Nu apare avertismentul | |
| 10 | Pune un produs in masina (iesire spre vehicul, 2 buc.), apoi o iesire din acea masina (sursa = vehiculul) de 5 buc., spre beneficiar sau vanzare | Sub formular apare nota "Depaseste cantitatea inregistrata in masina (2 buc.)..."; se poate salva; randul are marcajul "peste stoc"; jurnalul spune "Peste stoc: 3" | |
| 11 | Pagina vehiculului: lista produselor din masina | Produsul cu cantitate negativa nu apare (se listeaza doar cantitati pozitive) | |
| 12 | Din aceeasi masina incearca restituire in depozit sau mutare in alta masina cu mai mult decat are | Se refuza cu mesaj (mutarile fizice raman verificate) | |
| 13 | Filtrul "Peste stoc" | Arata si iesirile din masina peste cantitate | |

### 07.10.2026 - Intrari libere, furnizor/factura, atasare la factura

**Ce s-a adaugat:** selector de provenienta la intrare (intrare libera cu motiv, factura existenta, factura noua manual, Preia din PDF); coloana FURNIZOR / FACTURA si filtre Cu factura/Libere/furnizor; avertisment cu motiv la produs repetat pe aceeasi factura sau peste cantitatea facturata; atasare/detasare a intrarilor la factura (administrator) si fila „Intrari fara factura” pe /facturi cu asociere in lot; notificarea „Factura asteptata”.

**Ce face acum:** O intrare se inregistreaza cu sau fara factura (intrare libera cu motiv, factura existenta sau factura noua), iar cele fara factura raman in urmarire pana se leaga de factura furnizorului.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | "Adauga intrare": selectorul de provenienta are taburi (intrare libera / factura existenta / factura noua manual / Preia din PDF) | Fiecare tab arata campurile lui; intrarea libera cere motiv | |
| 2 | Intrare libera cu motiv si furnizor optional | Coloana FURNIZOR / FACTURA arata "Intrare libera" cu motivul | |
| 3 | Filtre Cu factura / Libere / furnizor | Tabelul se restrange corect | |
| 4 | Adauga pe aceeasi factura acelasi produs a doua oara; apoi o cantitate peste cea facturata | Avertisment cu motiv; jurnal "Intrare dublata pe factura (confirmat)" | |
| 5 | Administrator: ataseaza o intrare libera la o factura, apoi o detaseaza | Jurnal si Istoric arata operatia; utilizatorul limitat nu are butonul | |
| 6 | Pe /facturi, fila "Intrari fara factura": asociere in lot | Intrarile alese se leaga de factura si dispar din fila | |

### 07.10.2026 - Stoc negativ si regularizare

**Ce s-a adaugat:** o iesire peste stoc se poate inregistra oricand (fara blocaj si fara motiv) si se marcheaza „peste stoc”; stocul negativ se corecteaza prin operatia „Regularizeaza” (cantitatea reala din depozit sau 0) din banda de pe pagina produsului sau din panoul comun de stoc negativ (formularul produsului, preluarea facturii); filtrul „Peste stoc”.

**Ce face acum:** Un stoc negativ se vede intr-un panou de regularizare; o iesire peste stoc nu se blocheaza, iar regularizarea aduce stocul la realitate cu jurnal.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Fa o iesire mai mare decat stocul | Se salveaza fara blocaj si fara motiv; randul e marcat "peste stoc"; stocul produsului apare negativ | |
| 2 | Pe pagina produsului apare banda "Regularizare din pagina produsului" | Introdu cantitatea reala din depozit (sau 0) si apasa "Regularizeaza stocul acum" | |
| 3 | Dupa regularizare | Stocul = cantitatea reala; jurnalul are "Regularizare stoc negativ"; banda dispare | |
| 4 | Aceeasi regularizare din formularul produsului si din preluarea facturii (panoul de stoc negativ) | Panoul arata aceleasi optiuni; confirmarea functioneaza | |
| 5 | Filtrul "Peste stoc" | Arata doar iesirile care au depasit stocul din depozit la data lor | |

## Fluxul de baza (verificare de rutina)

1. Intrare 10 buc. -> stoc 10.
2. Iesire spre vehicul 4 -> depozit 6, masina 4.
3. Restituire 1 din masina -> depozit 7, masina 3.
4. Editare cu motiv, Istoric, Sterge cu motiv.
5. Data in viitor este refuzata.
6. Anulare/parasire cu modificari nesalvate: avertizarea apare deasupra dialogului si "Paraseste" inchide ambele.

### 08.10.2026 - Pagina produsului: taburi Intrare/Iesire, filtre in tabel, aspect

**Ce s-a schimbat:** (1) „Intrare” si „Iesire” sunt taburi (selectat: verde pentru Intrare, rosu pentru Iesire, neselectat: alb); la trecerea de la unul la altul formularul se goleste. (2) Filtrele cu butoane radio au disparut: camp de filtrare editabil (descriere, referinta, numar factura, furnizor, beneficiar, proiect, numar vehicul) si etichetele coloanelor INTRARE/IESIRE, FURNIZOR / FACTURA, BENEFICIAR/PROIECT, VEHICUL deschid un panou cu alegeri; filtrele active apar ca etichete cu × si „Reseteaza filtrele”. (3) Cardurile paginii stau in doua coloane (produsul la stanga, restul in coloana lata), fara spatiu mare pana la tabel; totalul de stoc are fundal vizibil si padding; butoanele albe sunt colorate dupa tipul actiunii. (4) Cantitatea reala la stoc negativ: valoarea negativa sau cu zecimale e refuzata pe loc, iar „Regularizeaza” se activeaza imediat ce cantitatea este valida.

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 1 | Deschide un produs | Cardul „Unde sunt bucatile”, „Inregistreaza o miscare” si tabelul sunt in coloana din dreapta, produsul in stanga; spatiu de ~20 px intre carduri | |
| 2 | Scrie o descriere la Intrare, apasa tabul Iesire, apoi inapoi Intrare | Formularul este gol de fiecare data; tabul Intrare e verde, Iesire e rosu cand e ales, alb cand nu | |
| 3 | In tabel scrie in „Filtreaza” (ex. numele unui furnizor, un numar de factura, o placuta) | Dupa o scurta pauza tabelul se restrange; apare eticheta „Text: ...” cu × | |
| 4 | Apasa eticheta coloanei INTRARE/IESIRE, alege „Intrari”; apoi FURNIZOR / FACTURA si alege un furnizor | Panou cu alegeri sub filtre; filtrele active apar ca etichete; „Reseteaza filtrele” le scoate pe toate | |
| 5 | Produs cu stoc negativ: la „Cantitate reala in depozit” scrie -10, apoi 0, apoi 5 | -10: mesaj de eroare si „Regularizeaza” inactiv; 0 si 5: butonul devine activ imediat, fara a iesi din camp | |

## Ecran ingust

- Formularul de iesire (Referinta, panoul de iesire repetata, nota de depasire) pe latime de telefon: fara depasiri orizontale.
- Filtrele noi se aseaza pe mai multe randuri.
