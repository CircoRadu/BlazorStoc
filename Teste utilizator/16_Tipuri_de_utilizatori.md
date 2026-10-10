# Tipuri de utilizatori si permisiuni (/setari -> Tipuri de utilizatori, /utilizatori)

Fluxul: administratorul defineste tipuri de utilizatori (nume + permisiuni pe module si actiuni) in Setari -> Tipuri de utilizatori; fiecare utilizator are exact un tip (formularul din /utilizatori); meniul, paginile, butoanele si operatiile (si pe server) respecta drepturile tipului. Modificarile de drepturi se aplica imediat (maximum ~30 de secunde pentru un utilizator deja conectat), fara reconectare.

## Modificari de testat

### 10.10.2026 - Tipuri de utilizatori cu permisiuni

**Ce s-a adaugat:** Setari -> fila „Tipuri de utilizatori" (lista si editorul cu matrice module x actiuni), selectorul „Tip utilizator" in formularul utilizatorului, pagina „Acces refuzat", politici de acces pe fiecare pagina/endpoint, evenimente noi in jurnal (Adaugare tip utilizator, Redenumire tip utilizator, Modificare descriere tip utilizator, Acordare permisiune, Revocare permisiune, Stergere tip utilizator, Schimbare tip utilizator, Acces refuzat).

**Ce face acum:** exista doua tipuri de sistem: „Administrator" (toate drepturile, nu se modifica si nu se sterge) si „Utilizator" (drepturile de pana acum; i se pot schimba permisiunile, nu si numele). Poti crea oricate tipuri noi. Un utilizator vede in meniu numai modulele permise; o pagina fara drept duce la „Acces refuzat" si jurnalizeaza incercarea.

Pregatire: conectat ca administrator; un al doilea cont de test pentru verificarea drepturilor (parola o stabilesti tu).

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> „Tipuri de utilizatori" | Lista cu „Administrator" (sistem, „toate" permisiuni) si „Utilizator" (sistem) | |
| 2 | Deschide „Administrator" (iconita ochi) | Matricea cu toate comutatoarele pornite si dezactivate; fara buton „Salveaza tipul"; nota ca nu poate fi modificat | |
| 3 | „+ Adauga tip": nume „Magaziner", descriere, porneste „Acces" pe Stoc produse, Furnizori; la Stoc produse porneste „Adaugare" | Comutatorul de actiune porneste si accesul modulului (vizualizare implicita); „Meniul vazut de acest tip" listeaza modulele | |
| 4 | Incearca „Copiaza drepturile din" „Utilizator" | Comutatoarele se inlocuiesc cu ale tipului ales, apoi le poti ajusta | |
| 5 | „Acorda tot" / „Revoca tot" | Toate comutatoarele pornesc / se opresc | |
| 6 | Salveaza un tip cu nume deja existent (alte majuscule) | Mesaj ca exista deja un tip cu acest nume | |
| 7 | Salveaza „Magaziner" | Apare in lista cu 0 utilizatori | |
| 8 | /utilizatori -> editeaza contul de test: „Tip utilizator" = Magaziner, motiv, Salveaza | Cont actualizat; coloana „Tip utilizator" arata Magaziner | |
| 9 | Conecteaza-te cu contul de test | Meniul arata numai modulele permise (fara Utilizatori, Nomenclator, Jurnal, Setari etc., daca nu sunt acordate) | |
| 10 | Deschide direct o adresa nepermisa (ex. /utilizatori sau /oferte) | Pagina „Acces refuzat" cu legatura spre pagina principala | |
| 11 | Pe un modul cu doar „Vizualizare" (ex. Furnizori): pagina | Se vede lista, fara „+ Adauga", fara iconite de editare/stergere | |
| 12 | Ca administrator, in alta fereastra: acorda tipului „Editare" pe Furnizori; in fereastra contului de test reincarca pagina (maxim ~30 s) | Apar iconitele de editare, fara reconectare | |
| 13 | Editeaza tipul „Magaziner": revoca o permisiune, motiv, Salveaza (dialog de confirmare cu modificarile) | Tipul se actualizeaza; versiunea creste | |
| 14 | Incearca sa stergi „Magaziner" cat timp are utilizatori | Iconita de stergere dezactivata cu explicatie in titlu | |
| 15 | Muta contul de test pe „Utilizator", apoi sterge „Magaziner" (motiv) | Tipul dispare din lista | |
| 16 | /jurnal, filtru actiune: „Acordare permisiune", „Revocare permisiune", „Schimbare tip utilizator", „Acces refuzat" | Fiecare operatie are evenimentul ei, cu permisiunea numita si cu motiv | |
| 17 | Incearca sa dezactivezi propriul cont de administrator sau sa il muti pe alt tip | Mesaj de refuz (la fel pentru ultimul administrator activ) | |
| 18 | Tip cu doar „Adaugare produs" (modulul Produse) si „Vizualizare stoc": pagina de stoc | Apare „+ Adauga produs"; fara editare/stergere produs; fara iconita de iesire rapida | |
| 19 | Acelasi tip, pagina unui produs: formularul de miscare | Lipseste (fara Intrare/Iesire/Stornare); cu „Iesire din stoc" apare doar tabul Iesire | |
| 20 | Tip cu „Modificare / stergere miscare" fara Intrare/Iesire | Nu poate inregistra o miscare noua, doar edita/sterge existente | |
| 21 | Card „Unde sunt bucatile": fara „Setare stoc minim" | Doar textul „Stoc minim: N buc.", fara casuta si buton | |
| 22 | Pagina principala | Cardurile (inclusiv Preluare factura, Iesire multipla, Export consum, Preluare inventar) apar doar cu dreptul de vizualizare | |
| 23 | Categorii: tip cu doar „Stergere" | Fara „+ Adauga categorie", fara iconite de editare, fara panou de parametri editabil | |
| 24 | Bonul de consum (iconita de pe randul unei iesiri) | Se deschide PDF-ul, si pentru vanzare generica | |
