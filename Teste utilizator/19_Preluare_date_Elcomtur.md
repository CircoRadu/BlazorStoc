# Preluare date Elcomtur (Administrare -> Preluare date Elcomtur, fisa beneficiarului, Notificari)

Modul nou `Elcomtur` (branch `Elcomtur`). Balanta unui beneficiar persoana juridica = facturat minus incasat din backup-ul programului Elcomtur (fisier `.elc`, firma1).

## Modificari de testat - preluarea balantelor (04.11.2026)

**Ce s-a adaugat:** pagina `Administrare -> Preluare Elcomtur` (permisiunea `elcomtur.import`), sectiunea pliabila "Balanta Elcomtur" in fisa beneficiarului (permisiunea `elcomtur.view`) si tabul "Balante restante" din Notificari.
**Ce face acum:** din backup-ul `.elc` se calculeaza balanta fiecarui client persoana juridica care are un beneficiar cu acelasi CUI; balanta, data backup-ului si facturile neachitate se salveaza pe beneficiar. Nu se creeaza beneficiari noi si nu se genereaza notificari.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Meniu Administrare | Apare "Preluare Elcomtur" doar cu permisiunea de preluare | |
| 2 | Pagina -> alege `softimel_4.elc` (343 MB) | Se vede procentul incarcarii; apoi se simuleaza singura pentru firma1 | |
| 3 | Raportul simularii | Firma, data backup-ului (30.10.2026), clienti, persoane juridice, potriviti cu beneficiari, cu plati restante + total, clienti cu sold pozitiv fara beneficiar (lista paginata) | |
| 4 | "Aplica preluarea" | Mesaj de succes; "Ultima preluare" arata data backup-ului si numarul de beneficiari; in Jurnal apare "Preluare date Elcomtur" | |
| 5 | Un fisier care nu e arhiva Elcomtur (de exemplu un PDF redenumit .elc) | Mesaj clar, nu se salveaza nimic | |
| 6 | Fisa unui beneficiar cu sold pozitiv | Sectiune cu antet rosiatic: "Balanta Elcomtur la <data>", plati restante (suma), "N facturi cu termen depasit", "M facturi in termen" | |
| 7 | Deschide sectiunea | Doua tabele: facturi cu termen depasit (cu zile de intarziere) si facturi in termen; factura platita partial apare cu restul si "(platita partial)"; sub ele, diferenta fata de sold daca exista | |
| 8 | Beneficiar cu balanta 0 sau negativa | Antet verde: "Balanta: 0,00 RON - Nu sunt plati restante", fara tabele | |
| 9 | Beneficiar fara client in Elcomtur | Nu apare sectiunea | |
| 10 | Notificari -> "Balante restante" | Tabel cu beneficiarii cu sold pozitiv, data balantei in antet, total; facturi depasite / in termen pe fiecare | |
| 11 | Clic pe antetul "BALANTA" | Ordonare descrescatoare/crescatoare dupa sold (implicit descrescator) | |
| 12 | Iconita de vizualizare de pe un rand | Deschide fisa beneficiarului cu sectiunea Balanta Elcomtur deschisa si adusa in vedere | |
| 13 | Spitalul Zam (CUI 4373967) dupa preluare | Balanta 24.118,05 la 30.10.2026 (la 30.09.2026 formula da 118.077,60) | |
| 14 | Opreste modulul Elcomtur (Setari -> Module) | Meniul si tabul dispar; sectiunea din beneficiar dispare; pagina spune "modul indisponibil" | |
| 15 | Fisa unui beneficiar fara client Elcomtur (05.11.2026) | Sectiunea "Balanta Elcomtur" apare, cu antetul "Nu exista balanta importata pentru acest beneficiar" | |
| 16 | Opreste apoi porneste modulul Elcomtur din Setari -> Module, fara sa navighezi | Intrarea "Preluare Elcomtur" din meniu dispare si reapare imediat (la fel la alte module) | |

## Modificari de testat - ora verificarii zilnice (06.11.2026, versiunea 0.5.44)

**Ce s-a adaugat:** Setari -> Preluare Elcomtur: lista "Ora verificarii zilnice" (00:00 - 23:00, implicit 04:00).
**Ce face acum:** verificarea automata porneste de la ora aleasa (ora serverului), o singura data pe zi; textele din panou arata ora aleasa; schimbarea apare in Jurnal ("Ora verificarii automate: 04:00 -> 09:00").

| Pas | Ce faci | Rezultat asteptat | Rezultat |
|---|---|---|---|
| 1 | Deschide Setari -> Preluare Elcomtur | Lista "Ora verificarii zilnice" arata 04:00 | |
| 2 | Alege 09:00 si salveaza | "Configurarea a fost salvata"; textul de sub butoane spune "la 09:00" | |
| 3 | Jurnal activitate | Intrare "Configurare preluare automata Elcomtur" cu "Ora verificarii automate: 04:00 -> 09:00" | |
| 4 | Alege o ora cu 1-2 minute dupa ora serverului, salveaza si asteapta | La acea ora apare "Ultima verificare" noua; nu se repeta in aceeasi zi | |
