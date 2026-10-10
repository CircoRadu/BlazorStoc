# Analize de risc (/analize-risc si fisa beneficiarului)

Fluxul: beneficiar -> punct de lucru -> analiza de risc (numar de inregistrare, data inregistrarii, intocmita de, valabilitate in luni) -> reinnoiri (revizii cu acelasi numar) -> notificare de expirare (60 de zile) -> analiza noua cu numar nou care o inlocuieste pe cea veche.

## Modificari de testat

### 10.10.2026 - Sectiunea „Analize de risc"

**Ce s-a adaugat:** analiza de risc legata de un beneficiar si de un punct de lucru, cu numar de inregistrare, data inregistrarii, persoana care a intocmit-o, valabilitate editabila (36 de luni implicit), data ultimei reinnoiri si istoric de reinnoiri; sectiune pe fisa beneficiarului, pagina proprie `/analize-risc` (meniu lateral), coloana „Analiza de risc" in tabelul punctelor de lucru si sursa de notificari „Expirare analiza de risc" (prag implicit 60 de zile, sablon creat singur la pornire).

**Ce face acum:** pentru un punct de lucru exista cel mult o analiza activa; cele vechi raman dezactivate (Off) cu tot istoricul. Expirarea se calculeaza din data ultimei reinnoiri plus valabilitatea; la o analiza noua ultima reinnoire este data inregistrarii. „Reinnoieste" inregistreaza o revizie (acelasi numar), muta expirarea si inchide automat notificarea veche. Jurnalul numeste exact fiecare operatie.

Pregatire: un beneficiar cu cel putin doua puncte de lucru (numele sa inceapa cu „TEST ...").

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Fisa beneficiarului: sectiunea „Analize de risc" (dupa „Interventii") | Tabel gol cu mesaj; sub titlu nota „N puncte de lucru nu au analiza de risc activa" cu numele punctelor ca butoane | |
| 2 | In tabelul „Puncte de lucru", coloana „Analiza de risc" | Chip rosu „Fara analiza de risc inregistrata" pe fiecare punct | |
| 3 | „+ Adauga analiza de risc": punct, numar (ex. ar-14), intocmita de, data inregistrarii, valabilitate 36, Salveaza | Analiza apare On; numarul devine AR-14; „Ultima reinnoire" = data inregistrarii; „Expira" = data + 36 de luni; punctul dispare din nota; chipul din tabelul punctelor arata „Nr. AR-14 · expira ..." | |
| 4 | Alege din nota un punct fara analiza | Formularul se deschide cu punctul precompletat | |
| 5 | Adauga a doua analiza pe un punct care are deja una activa | Apare nota cu analiza activa si comutatorul „Dezactiveaza analiza veche la salvare" (pornit); la salvare cea veche trece pe Off (ascunsa; „Arata si analizele Off") | |
| 6 | La aceeasi adaugare opreste comutatorul si salveaza | Mesaj clar ca punctul are deja o analiza activa; nimic salvat | |
| 7 | Foloseste acelasi numar de inregistrare pe alt punct | Mesaj ca numarul exista deja si ca o revizie se face cu „Reinnoieste" | |
| 8 | „Reinnoieste": data in viitor sau cel mult egala cu ultima reinnoire | Mesaj de eroare, nimic salvat | |
| 9 | „Reinnoieste" cu o data valida si observatii | Ultima reinnoire si expirarea se muta; „Data inregistrarii" si numarul raman; „Istoric (1)" arata reinnoirea si „Inregistrare initiala" | |
| 10 | „Anuleaza reinnoirea" pe cea mai noua | Ultima reinnoire revine la valoarea de dinainte; istoricul scade | |
| 11 | Editeaza analiza: schimba doar valabilitatea (ex. 48) | Expirarea se recalculeaza; in jurnal „Modificare valabilitate analiza de risc" | |
| 12 | Editeaza analiza reinnoita si incearca sa schimbi data inregistrarii | Campul este blocat (explicatie sub el) | |
| 13 | Comutatorul On/Off pe un rand; activeaza una Off cat timp alta e activa pe acelasi punct | Mesaj de refuz; dupa ce cealalta e Off, se activeaza | |
| 14 | Sterge o analiza (cos de gunoi) cu motiv | Dispare din tabel; in jurnal „Stergere" cu motivul; punctul de lucru se poate sterge apoi | |
| 15 | Incearca sa stergi un punct de lucru sau beneficiarul care au analize | Mesaj ca trebuie sterse mai intai analizele | |
| 16 | Meniu lateral „Analize de risc" (/analize-risc) | Tabel cu toate analizele, cu beneficiar (link), cautare si filtru de stare; cele care expira primele sunt sus | |
| 17 | Pe /analize-risc: „+ Adauga analiza de risc" | In formular alegi mai intai beneficiarul, apoi punctul lui de lucru | |
| 18 | Notificari: pagina /notificari sau Setari -> Notificari (sabloane) | Exista sablonul „Expirare analiza de risc" (categoria „Analize de risc", prag 60); pentru o analiza care expira in mai putin de 60 de zile apare o notificare cu numarul, persoana si datele | |
| 19 | Reinnoieste acea analiza si deschide Notificari (peste maximum 5 minute sau la reincarcare) | Notificarea veche apare ca rezolvata automat, cu motivul „Data expirarii analizei de risc s-a modificat de la ... la ... (ultima reinnoire: ...)" | |
| 20 | Jurnal activitate, filtru pe actiunile noi | Adaugare / Modificare / Modificare valabilitate / Reinnoire / Anulare reinnoire / Activare / Dezactivare analiza de risc, cu valorile vechi si noi | |
| 21 | Ecran ingust (telefon) pe sectiune si pe /analize-risc | Fara depasire orizontala; tabelul deruleaza | |
| 22 | Utilizator cu rol limitat | Vede analizele, dar nu are butoane de adaugare, editare, reinnoire sau stergere | |

### 10.10.2026 - Card pe pagina principala si iconite pe randuri

**Ce s-a adaugat:** cardul „Analize de risc" pe pagina principala (cu numarul de analize expirate sau care expira curand); in toata aplicatia randurile de tabel au ori iconite, ori butoane (operatiile au iconite: editare, stergere, reinnoire, istoric, storno, mutare etc.); meniul lateral ramane deschis pe Iesire multipla, Nomenclator, Proiecte si Observatii.

**Ce face acum:** pe orice rand de tabel actiunile sunt iconite cu explicatie la hover; Activ/Inactiv la contracte este comutator.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 23 | Pagina principala | Card „Analize de risc" dupa „Mentenanta"; cu analize aproape de expirare apare „N analize expira" | |
| 24 | Contracte, intervenții, puncte de lucru (fisa beneficiarului), miscari produs, versiuni ANAF, tipuri de sisteme, rezervari proiect | Fara butoane cu text pe randuri, doar iconite (si comutatoare) | |
| 25 | Intra in Produse -> Iesire multipla, apoi Nomenclator, un proiect | Submeniul parintelui ramane deschis, linkul curent evidentiat | |
