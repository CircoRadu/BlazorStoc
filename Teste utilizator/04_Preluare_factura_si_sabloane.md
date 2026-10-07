# Preluare factura si sabloane de facturi (/produse/preluare-factura, /setari -> Facturi)

Fluxul: Stoc -> Preluare factura -> incarci PDF/scanare -> pas 1 (tabelul si coloanele) -> pas 2 (verifici numar, CUI, data, produsele) -> pas 3 (rezumat si intrarea in stoc). Sabloanele se creeaza per furnizor din Setari sau direct din avertizarea fara sablon.

## Modificari de testat

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

## Fluxul de baza

1. Factura noua -> sablon -> pasul 2 cu confirmari -> pasul 3 -> intrari in stoc legate de factura.
2. Aceeasi factura din nou -> preluare partiala.
3. Verifica pe /facturi si pe pagina produsului ca intrarile exista.
