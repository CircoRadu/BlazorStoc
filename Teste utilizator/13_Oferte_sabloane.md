# Oferte - sabloane de devize-oferta (/oferte/sabloane)

Modul nou, separat: "Oferte" (meniu principal si card pe pagina principala). Un sablon spune unde sunt datele intr-un deviz-oferta Excel (.xlsx), ca sa poata fi preluat ulterior (taskul urmator: asistentul de preluare a ofertei). Nu exista nomenclator de unitati de masura: in stoc se tin doar produse pe bucati; liniile cu alta unitate (metri, ore) sunt marcate "in afara stocului". Fisierele .xls (format vechi) nu se pot citi: se salveaza ca .xlsx din Excel.

## Modificari de testat

### 07.10.2026 - Sabloane de oferte

**Ce s-a adaugat:** (1) un cititor propriu de .xlsx (foi, texte comune/bogate/inline, numere, celule imbinate - valoarea ramane in celula stanga-sus), fara biblioteca noua; (2) sabloane de oferta (`offer_templates`, migrarea 24): antetul ofertei se citeste dupa etichete (Oferta Nr, titlul sub numar, Categoria, Beneficiar), tabelul se gaseste dupa etichetele coloanelor din antetul lui (repetat in fiecare sectiune), coloanele se aleg dupa litera (Nr., Tip produs, Denumire, Unitate, Cantitate; preturile si TVA nu se citesc), sectiunile (Echipamente, Manopera, Cheltuieli) se importa sau nu, randurile "Total..." / "Fara TVA" se ignora; (3) pagina cu lista de sabloane, editor cu previzualizarea foii (litere de coloana), propunere automata din fisierul de proba, "Verifica pe fisier"; (4) alegerea automata a sablonului dupa etichetele ofertei (folosita de taskul urmator); (5) jurnal exact: Creare / Modificare (cu ce s-a schimbat) / Activare / Dezactivare / Stergere sablon oferta.

**Ce face acum:** Un devizul-oferta .xlsx se citeste pe baza unui sablon propus automat si editabil (coloane, sectiuni, randuri ignorate).

**Flux:** Oferte -> „+ Sablon nou” -> tragi un deviz .xlsx -> campurile se propun singure -> corectezi (coloane, etichete, sectiuni) -> „Verifica pe fisier” -> „Salveaza sablonul”. Pentru o oferta cu alt aspect faci alt sablon.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Meniul Oferte (sau cardul de pe pagina principala) | Pagina „Sabloane de oferte”, lista goala cu butonul „+ Sablon nou” | |
| 2 | „+ Sablon nou” si tragi un deviz-oferta .xlsx (de ex. RaportOferta.xlsx) | Apare previzualizarea foii cu literele coloanelor (A, B, C…) si numerele randurilor; coloanele alese sunt colorate; mesaj „Campurile au fost propuse din fisier” | |
| 3 | Verifica campurile propuse | Coloane: Nr.=A, Tip produs=B, Denumire=D, Unitate=F, Cantitate=G (cu etichetele lor din antet); antet: Oferta Nr (la dreapta), Titlu (sub „Oferta Nr”), Categoria, Beneficiar; sectiuni: Echipamente = Da, Manopera = Nu, Cheltuieli = Nu | |
| 4 | „Verifica pe fisier” | Numarul ofertei, titlul, categoria, beneficiarul citite corect; pe sectiuni: numarul de linii, cate „pe bucati” si cate „in afara stocului” (cablurile in metri, orele) | |
| 5 | Pune „Se importa” = Nu la Echipamente si verifica din nou | Echipamente apare „nu se importa” | |
| 6 | Lasa campul „Denumirea sablonului” gol sau sterge coloana „Unitate”, apoi salveaza | Mesaje clare („Completeaza denumirea…”, „Alege coloana pentru Unitate de masura”) | |
| 7 | Salveaza cu o denumire | Mesaj „Sablonul a fost creat”; apare in lista cu foaia, coloanele si sectiunile importate | |
| 8 | Alt sablon cu aceeasi denumire (altfel scrisa: majuscule, diacritice) | „Exista deja un sablon cu aceasta denumire” | |
| 9 | Incarca un fisier .xls (format vechi) sau un fisier care nu e Excel | Mesaj ca nu se poate citi, cu indicatia sa fie salvat ca .xlsx | |
| 10 | „Modifica” un sablon, schimba o sectiune si salveaza | In /jurnal: „Modificare sablon oferta” cu „Sectiuni importate: … -> …” (doar ce s-a schimbat) | |
| 11 | Doua taburi, modifica in primul, salveaza in al doilea acelasi sablon | „Sablonul a fost modificat intre timp…” si lista se reincarca | |
| 12 | Comutatorul „Activ” | Sablonul inactiv se estompeaza; jurnal „Dezactivare / Activare sablon oferta” | |
| 13 | Cu un cont „Utilizator” | Poate crea/modifica/activa, dar nu vede „Sterge”; administratorul sterge (dialog de confirmare, jurnal „Stergere sablon oferta”) | |
| 14 | Incearca si celelalte fisiere de proba (CA, IT, Mixt, TVCI) cu acelasi sablon | Se citesc toate (Mixt are si sectiunea Cheltuieli: apare in verificare ca „nu se importa” sau „necunoscuta” daca nu e in sablon) | |
| 15 | Ecran ingust | Previzualizarea se deruleaza in interiorul ei; tabelele de campuri nu ies din pagina | |

Verificat automat pe cele 5 oferte reale .xlsx de proba (D:\_BlazTest): un singur sablon propus din prima le citeste pe toate (liniile pe bucati, cablurile/orele marcate in afara stocului); fisierul `RaportOferta Efr.xls` este in format vechi si nu se poate citi.

## Fluxul de baza

1. Un sablon pe fiecare aspect de oferta -> la preluarea ofertei (taskul urmator) se alege singur dupa etichete.
2. Un aspect nou de oferta = sablon nou (nu se modifica cele existente).
