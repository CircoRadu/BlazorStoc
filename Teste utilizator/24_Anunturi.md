# Anunturi (Setari -> Anunturi, fereastra dupa autentificare)

Modulul "Anunturi" (versiunea 1.0.0, 21.11.2026). Tile-ul exista numai pentru Administrator marcat "Dezvoltator" (cheia setari-anunturi).

## Modificari de testat (21.11.2026)

**Ce s-a adaugat:** pagina Setari -> Anunturi (editor cu formatare si poze, previzualizare, draft, publicare, republicare, arhivare, stergere definitiva, tabel paginat si ordonabil), butoanele "Creare anunt" si "Incarca anunt" (ZIP), fereastra de anunturi dupa autentificare cu comutatorul "Nu mai afisa anunturile la pornire" si butonul "Am citit".
**Ce face acum:** un anunt general se arata o singura data pe sesiune si se marcheaza vazut la inchidere; unul obligatoriu revine la fiecare autentificare pana la "Am citit"; republicarea il trimite din nou tuturor celor vizati.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Setari -> tile "Anunturi" | Se vad butoanele "Creare anunt", "Incarca anunt" si tabelul (gol la inceput) | |
| 2 | "Creare anunt": scrie titlu, text cu aldin, titlu mare, lista, culoare, link; lipeste o poza (Ctrl+V) si adauga alta cu butonul "Poza" | Formatarea se vede in editor, pozele apar in text | |
| 3 | "Previzualizeaza" | Se deschide aceeasi fereastra ca la utilizatori, "1 din 1", fara butoane de salvare | |
| 4 | "Salveaza ca draft" | Mesaj; anuntul apare in tabel cu starea Draft; un utilizator obisnuit nu il vede | |
| 5 | Alege audienta "Utilizatori selectati", cauta un utilizator, porneste comutatorul lui, bifeaza "Obligatoriu", pune "Valabil pana" in viitor (dd.mm.yyyy), "Publica" | Starea devine Activ; tabelul arata tipul, audienta ("1 selectati") si data | |
| 6 | Autentifica-te ca utilizatorul ales | Dupa logare apare fereastra cu anuntul, "Am citit" activ; inchisa cu "Inchide" revine la urmatoarea logare | |
| 7 | Apasa "Am citit" | Anuntul nu mai apare pentru acel utilizator | |
| 8 | Anunt general: ca utilizator, cand se vede un anunt general, porneste "Nu mai afisa anunturile la pornire" (la un anunt obligatoriu comutatorul nu apare) si inchide; logheaza-te din nou | Anunturile generale nu mai apar (preferinta ramane salvata); cele obligatorii apar in continuare | |
| 9 | In tabel iconita de modificare pe anuntul obligatoriu, schimba textul, "Republica" | Notificare de republicare; tabelul arata "publicat de 2 ori"; utilizatorul il primeste din nou | |
| 10 | Pune "Valabil pana" in trecut direct in baza sau asteapta data | Anuntul apare ca "Arhivat (expirat)" si nu se mai afiseaza | |
| 11 | Iconita "Descarca ca ZIP" pe un rand, apoi "Incarca anunt" cu acelasi ZIP | Se creeaza un draft nou cu aceeasi continut si poze; utilizatorii inexistenti sau pozele lipsa apar ca avertismente | |
| 12 | Comutatorul din coloana Activ pe Off la un anunt activ, apoi iconita de stergere | Dezactivarea il scoate din afisare; stergerea cere confirmare si il elimina definitiv | |
| 12a | Iconita de republicare fara modificari pe un anunt activ (dezactivata la draft) | Cei vizati il primesc din nou, tabelul arata "publicat de N ori" | |
| 12b | Coloana "Activ" a tabelului: comutatorul Activ/Inactiv | On pe un draft sau un anunt arhivat il activeaza (starea devine Activ); Off il arhiveaza; cu data "Valabil pana" trecuta, activarea refuza cu mesaj | |
| 12c | Anunt obligatoriu: fereastra nu are Inchide, X sau Urmatorul si nu se inchide cu Escape; se inchide doar cu "Am citit". Anunt general: are Inchide si Urmatorul, fara "Am citit". In tabel, la un obligatoriu, iconita ochi din coloana "Vazut / citit" | Fereastra cu cine a citit arata utilizatorul, numele, data si ora; la anunturile generale coloana arata doar numarul | |
| 12d | Meniul numelui (bara de sus) -> "Vizibilitate anunturi generale" cu comutator On/Off; in fereastra de anunt porneste "Nu mai afisa anunturile la pornire" si inchide | Meniul arata comutatorul pe Off; il pornesti si anunturile generale nevazute revin la urmatoarea logare | |
&: filtreaza dupa "Anunturi" | Fiecare operatie are actiunea ei: creat, publicat, modificat, arhivat, republicat, sters, confirmare citire | |
