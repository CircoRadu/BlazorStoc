# Preluare factura si sabloane de facturi (/produse/preluare-factura, /setari -> Facturi)

Fluxul: Stoc -> Preluare factura -> incarci PDF/scanare -> pas 1 (tabelul si coloanele) -> pas 2 (verifici numar, CUI, data, produsele) -> pas 3 (rezumat si intrarea in stoc). Sabloanele se creeaza per furnizor din Setari sau direct din avertizarea fara sablon.

## Modificari de testat

### 10.10.2026 - Preluare factura si fereastra de sabloane, reorganizate in cod

**Ce s-a schimbat:** nimic vizibil; codul paginii `/produse/preluare-factura` si al ferestrei de sabloane (Setari -> Facturi) a fost impartit pe fisiere, iar citirea randurilor din PDF a fost desfacuta in pasi. Verificarea este ca nimic nu s-a schimbat.

**Ce faci:**
1. Deschide `/produse/preluare-factura`, incarca un PDF cu sablon cunoscut si treci prin pasii 1, 2 si 3 pana la intrarea in stoc pe un produs TEST.
2. Incarca o factura fara sablon si deschide fereastra de sabloane: marcheaza regiunile, salveaza sablonul, redeschide-l din Setari -> Facturi -> Sabloane salvate.
3. Incearca si un fisier XML (e-Factura), daca ai unul.

**Ce trebuie sa vezi:** aceleasi randuri citite ca inainte pe aceleasi facturi (numar, CUI, data, produse), aceleasi avertizari; nicio eroare. Rezultat: ...

### 10.10.2026 - Preluare factura: produse deja preluate si selectorul de randuri la pasul 2

**Ce s-a adaugat:** la pasul 1, avertizarea „deja preluat" (aceeasi ca la pasii 2 si 3) pe randurile ale caror produse au mai fost preluate de pe aceeasi factura; la pasul 2, selectorul rotund de preluare a randurilor (cu „Preia" pentru toate), aceeasi alegere ca la pasul 1.

**Ce face acum:** dupa citirea facturii se afla furnizorul si numarul ei; daca factura a mai fost preluata, apare avertizarea deasupra tabelului, iar randurile cu produse deja preluate au insigna „Deja preluat: produs, N buc.", o bara galbena pe margine si pornesc cu selectorul oprit. La pasul 2 se vad toate randurile; cele oprite sunt estompate si nu intra in pasul 3. Un rand pornit manual ramane pornit.

1. Preia o factura (de exemplu din 09.10.2026) si finalizeaza intrarea in stoc. Preia aceeasi factura a doua oara: la pasul 1 apare „Factura ... a mai fost preluata", iar randurile deja preluate au insigna si selectorul oprit.
2. Porneste manual selectorul unui rand cu insigna; muta o linie de demarcare: randul ramane pornit. „Selecteaza tot" le porneste pe toate.
3. Trece la pasul 2: vezi toate randurile cu selector in stanga; opreste un rand: devine estompat. „Pasul urmator" ramane blocat daca niciun rand preluat nu are produs ales (mesajul trimite la selectorul din stanga).
4. Revino la pasul 1: alegerea de la pasul 2 se vede si aici. La pasul 3 intra doar randurile preluate.
5. La o factura noua (necunoscuta) nu apare nicio avertizare si toate randurile pornesc selectate.

### 09.10.2026 - Preluare factura: alegerea variantei (parametri obligatori, etapa 2)

**Ce s-a adaugat:** in pasul 2 al preluarii (PDF, OCR, XML), un rand al carui cod este modelul unor produse cu parametri obligatori (de exemplu „DS-2CD1043" pentru „DS-2CD1043 - 2.8 mm" si „DS-2CD1043 - 4 mm") arata variantele ca optiuni.

**Ce face acum:** varianta se alege din lista; se propune (si apare deja bifata) cea scrisa in denumirea randului (de exemplu „4mm"), altfel cea legata de factura anterioara a furnizorului; daca nu se poate deduce nimic, nu e bifata nicio varianta. Se poate alege „Niciuna dintre variante" (randul nu se preia) sau „+ Varianta noua a modelului": se deschide formularul de produs cu modelul si subcategoria completate, unde alegi valorile; produsul se adauga in catalog la finalizare. Un produs ales sau gasit dupa cod care are parametri necompletati arata avertismentul „Parametri obligatorii necompletati" cu legatura „Editeaza produsul" (fereastra noua); intrarea pe el este refuzata la finalizare, iar celelalte randuri se salveaza.

1. Ai in catalog produsele „<model> - 2.8 mm" si „<model> - 4 mm" (vezi 01). Preia o factura cu un rand cu codul <model> si „4 mm" in denumire: la rand apar cele doua variante, cea de 4 mm bifata.
2. Schimba varianta; treci la pasul 3 si finalizeaza: intrarea se face pe varianta aleasa. A doua factura a aceluiasi furnizor cu acelasi cod propune varianta legata, dar o denumire care spune alta varianta o inlocuieste in propunere.
3. Pe un rand fara varianta in denumire nu e bifata nicio varianta: alege una sau „Niciuna".
4. Apasa „+ Varianta noua": formularul are modelul si subcategoria; fara valoarea fiecarui parametru nu se poate pregati. Dupa „Pregateste produsul", randul arata „Produs nou pregatit" cu codul compus; la finalizare produsul apare in catalog cu valorile si intrarea se inregistreaza.
5. Alege un produs vechi (fara valori) al subcategoriei: apare avertismentul si, la finalizare, mesajul de refuz sub rand.

### 09.10.2026 - Facturi XML (e-Factura UBL)

**Ce s-a adaugat:** Preluarea unei facturi din fisier XML (de exemplu e-Factura UBL), fara OCR, pe baza unui sablon XML (cai de elemente) definit in Setari -> Facturi -> Sabloane salvate.

**Ce face acum:** In Preluare factura poti alege un fisier .xml; se citesc numarul, data, furnizorul (CUI), liniile (cod, denumire, cantitate, UM, pret). Sablonul furnizorului se alege singur dupa CUI-ul din fisier; fara sablon se folosesc caile UBL standard, cu un avertisment. Restul asistentului (potrivire produse, confirmari, intrari legate de factura, jurnal, duplicat) este ca la PDF.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Facturi -> Sabloane salvate -> „Sablon XML nou"; alege furnizorul, „Completeaza cu caile UBL standard", incarca un XML de proba | Se vad numarul, data, furnizorul si primele 10 linii citite; denumirea si furnizorul sunt obligatorii | |
| 2 | Salveaza; sablonul apare in lista cu sursa „Fisier XML"; deschide detaliile | Caile salvate; creionul deschide aceeasi fereastra (nu editorul PDF) | |
| 3 | Produse -> Preluare factura -> alege o factura .xml a furnizorului | „Sablon XML detectat: ...", tabelul cu liniile, fara imaginea paginii | |
| 4 | Aceeasi factura XML fara sablon al furnizorului | Mesaj ca s-au folosit caile UBL standard; liniile se citesc | |
| 5 | Un fisier XML invalid sau fara linii | Mesaj clar in romana; nimic preluat | |
| 6 | Continua pana la pasul 3 si salveaza; apoi repeta cu aceeasi factura | Intrarile apar legate de factura; a doua oara avertisment de duplicat | |

### 06.10.2026 - Verificarea in pasul 2, furnizor, sabloane

**Ce s-a adaugat:** Verificarea furnizorului si a sablonului in pasul 2 al preluarii facturii (avertizare cu furnizorul citit).

**Ce face acum:** La preluarea unei facturi vezi furnizorul citit si sablonul propus; fara sablon citirea automata ramane si esti avertizat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Incarca o factura (de ex. 905550.pdf) fara sablon pentru furnizor | Avertizare cu furnizorul citit; citirea automata ramane | |
| 2 | "Creeaza sablon pentru acest furnizor" | Se deschide fereastra mare pe aceeasi factura (nu se reincarca); la salvare se inchide, sablonul se alege si factura se reciteste | |
| 3 | Pasul 2: numar, CUI si data sunt campuri editabile "neverificat"; fiecare se confirma cu comutator | Fara cele 3 confirmari nu se trece la pasul 3; editarea unui camp il readuce la "neverificat" | |
| 4 | Furnizor negasit in registru | Se poate adauga din fereastra (formular precompletat, ANAF interogat) | |
| 5 | Camp cu valoare necitita ca numar (cantitate, pret) | Fundal rosu + mesajul cu "!" sub denumire; nu se pot tasta litere; punctul devine virgula | |
| 6 | Randuri cu valori necitite | Se vede imaginea randului cu celulele incadrate; numarul si data au imaginea regiunii din PDF | |
| 7 | Intoarcere pas 3 -> 2 -> 1 | Corectiile, produsele pregatite si descrierile raman | |
| 8 | "Afiseaza factura intr-o fereastra noua" | Fereastra detasabila cu paginile | |
| 9 | Aceeasi factura a doua oara | Preluare partiala: se listeaza ce s-a preluat, produsele existente sunt marcate si se sar, daca nu alegi "se preia din nou" | |
| 10 | Numar asemanator (O/0, I/1) la acelasi furnizor si data | Se ofera factura existenta ("Da, este factura ...") | |
| 11 | Stoc negativ la un produs preluat | Panoul de stoc negativ propune regularizarea | |

### 06.10.2026 - Sabloane pentru utilizatori obisnuiti

**Ce s-a adaugat:** Utilizatorii obisnuiti au acces la tabul Facturi din Setari (generare si sabloane salvate).

**Ce face acum:** Un cont „Utilizator" vede in Setari doar tabul Facturi, poate genera sabloane, dar nu le poate sterge si nu deschide alte taburi.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Cont "Utilizator": meniul Setari | Apare numai tabul Facturi; adresele ?tab=anaf etc. nu deschid alte taburi | |
| 2 | Creeaza, modifica, redenumeste, activeaza/dezactiveaza un sablon | Merge; nu exista iconita de stergere | |
| 3 | Jurnal | Numeste utilizatorul | |

### 05-06.10.2026 - Editare sablon

**Ce s-a adaugat:** Editarea unui sablon cu etichete in camp, cursor si culori, fara re-analiza automata.

**Ce face acum:** Sablonul se afiseaza exact cum a fost salvat; etichetele se insereaza la cursor (necunoscutele in rosu), iar analiza porneste numai la cerere.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Editare sablon: etichete in camp (cursor, culori, simbol pentru spatiu) | Eticheta se insereaza la cursor; necunoscutele in rosu | |
| 2 | Parasire cu modificari nesalvate | Avertizare | |
| 3 | Comutatoare On/Off pentru coloane si campuri; coloane gasite = initial folosite | Corect | |
| 4 | Facturile reale furnizate de tine (scanari, alte furnizori) | Tabelul si totalurile citite corect; la nepotriviri spune ce | |

### 09.10.2026 - Sablon XML cu legare vizuala, unitati, ZIP, descriere intrare, perechi cod furnizor

**Ce s-a adaugat:** Editorul de sablon XML imparte pagina in sectiuni ca editorul PDF (Legaturi cu fisierul XML, Previzualizare, Sablon descriere intrare in stoc, Salvare); elementele sablonului se leaga vizual de valorile din fisierul de proba; unitatile UN/ECE (XPP etc.) se arata in romaneste; fisierul poate fi si ZIP e-Factura; potrivirea produsului cauta si codul din denumire si retine perechea cod furnizor - produs.

**Ce face acum:** Pe un XML de proba apeseaza „Alege" la un element si apoi valoarea din arborele fisierului; la „Schimba" nu se trece la elementul urmator, iar o valoare deja legata de alt element e refuzata. Numele sablonului porneste ca „<furnizor> - xml <data>" (la PDF „... - pdf <data>"). Descrierea intrarii in stoc are etichete colorate cu spatii puse automat inainte si dupa ele (nu in operatii). La preluarea unui XML nu mai exista confirmari de verificat; cantitatea „10.000000" se arata „10". Un PDF exportat din e-Factura arata mesajul despre XML/ZIP; potrivirea slaba a sablonului (sub 30%) propune un sablon nou, iar „Nr. inregistrare" al vanzatorului din acel PDF este CUI.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Facturi -> Generare sablon: incarca un XML | Sectiunile Legaturi/Previzualizare/Descriere/Salvare; butonul „+ Adauga furnizorul" doar daca furnizorul nu e ales, iar fereastra arata formularul | |
| 2 | Alege un element, apoi o valoare din arbore; apoi „Schimba" pe un element legat | Dupa „Schimba" nu se trece la urmatorul; o valoare legata de alt element da avertisment | |
| 3 | Descriere: apasa etichete si operatii | Spatiu inainte si dupa fiecare, fara spatii in operatii; previzualizarea fara spatii la capete; cursorul dupa eticheta | |
| 4 | Preluare factura cu un XML (si un ZIP e-Factura) | Fara comutatoare de verificare la numar/CUI/data; cantitatile fara zerouri; descrierea dupa sablon | |
| 5 | Preluare PDF e-Factura (de ex. telesystem) | Mesajul despre XML/ZIP; „Nr. inregistrare" apare ca CUI; la potrivire sub 30% avertizare cu „Creeaza sablon" | |
| 6 | Produs nou dintr-un XML: cod GS in denumire; la urmatoarea factura acelasi cod furnizor | Numele propus e codul din denumire; a doua oara produsul e legat din factura anterioara | |

## Fluxul de baza

1. Factura noua -> sablon -> pasul 2 cu confirmari -> pasul 3 -> intrari in stoc legate de factura.
2. Aceeasi factura din nou -> preluare partiala.
3. Verifica pe /facturi si pe pagina produsului ca intrarile exista.
