# Furnizori si facturi de furnizor (/furnizori, /facturi)

Fluxul: Administrare -> Furnizori (registru dupa CUI) -> facturile se inregistreaza la preluarea facturii sau manual din pagina produsului -> /facturi le arata, le corecteaza (administrator) si leaga intrarile libere.

## Modificari de testat

### 07.10.2026 - Preluarea facturii: intrare legata de oferta si propuneri de rezervare

**Ce s-a adaugat:** in pasul 3 al preluarii facturii (PDF) si in dialogul "Adauga produs la factura", fiecare produs din catalog care apare in oferta unui proiect are selectorul **Pentru oferta** (propunere automata cand e intr-o singura componenta; se poate lasa nelegat). Dupa finalizarea preluarii apar propunerile **"Rezerva N buc. pentru proiectul X"** pentru produsele facturii al caror necesar e acoperit de stocul liber, dar nerezervat; nimic nu se rezerva fara clic. Pasi: 1) preia o factura cu un produs aflat intr-o oferta de proiect; in pasul 3 alege/lasa propunerea la "Pentru oferta"; 2) finalizeaza: in Situatia proiectului, linia are "Intrat pt. oferta"; 3) daca stocul liber acopera necesarul, apare propunerea de rezervare dupa finalizare. (Produsele noi din factura nu au inca oferta, deci nu au selectorul.)


**Ce face acum:** La preluarea facturii, intrarea poate fi legata de componenta ofertei pe care o aproviziona (si apare in Situatie ca „Intrat pt. oferta"), iar dupa finalizare vezi cine poate rezerva din stocul liber.
### 06.10.2026 - Furnizori

**Ce s-a adaugat:** registrul de furnizori (Administrare -> Furnizori, cheia este CUI-ul), cu preluare din ANAF si 5 surse de date (ANAF, ANAF editat, manual ANAF indisponibil, manual CUI negasit, manual UE), verificarea cifrei de control a CUI-ului, stergere doar de administrator si doar fara facturi/intrari; beneficiarii folosesc aceeasi cheie CUI fara prefixul RO.

**Ce face acum:** Furnizorii au lista proprie (cautare, filtru „de verificat"), facturile lor se preiau cu verificarea furnizorului in pasul 2, iar un furnizor cu facturi nu se poate sterge.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 2 | Furnizor nou cu CUI real, ANAF activ | Formularul se completeaza; sursa "Date preluate din ANAF"; modifici adresa -> "Preluate din ANAF, editate manual" | |
| 3 | ANAF oprit sau dezactivat din Setari | Se salveaza ca "Introdus manual (ANAF indisponibil)" | |
| 4 | CUI valid dar necunoscut ANAF | "CUI negasit", se accepta manual cu avertizare | |
| 5 | Furnizor UE (de ex. DE + 9 cifre) | Se accepta fara interogare ANAF | |
| 6 | CUI cu cifra de control gresita | Mesaj de eroare | |
| 7 | Sterge un furnizor cu facturi, apoi unul fara | Primul: iconita dezactivata cu explicatie; al doilea: se sterge cu motiv (doar administrator) | |
| 8 | Cont "Utilizator": adauga si editeaza furnizor | Merge; stergerea nu e disponibila | |
| 9 | Beneficiari: creeaza cu CUI RO12345678, apoi 12345678 si "ro 12345678" | Mesaj de CUI duplicat | |

### 07.10.2026 - Pagina Facturi

**Ce s-a adaugat:** pagina /facturi (Administrare si pagina principala): registru de facturi preluate cu casete de rezumat si filtre, fereastra cu produsele unei facturi (numarul facturii e buton), corectarea numarului/datei/furnizorului si stergerea unei facturi fara intrari (administrator, cu motiv), jurnal cu actiuni exacte.

**Ce face acum:** Vezi si filtrezi toate facturile de furnizor (text, furnizor, date, doar cele fara intrari) si deschizi intrarile fiecareia; administratorul le poate corecta sau sterge.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Deschide /facturi din meniu Administrare si de pe pagina principala | Casete de rezumat, tabel (numar, furnizor cu link, data, intrari, preluata de) | |
| 2 | Filtre: text, furnizor, interval de date, "doar fara intrari" | Rezultate corecte | |
| 4 | Administrator: corecteaza numarul, data, furnizorul (cu motiv) | Jurnal: "Modificare numar factura", "Modificare data factura", "Mutare factura la alt furnizor" | |
| 5 | Numar deja existent la acelasi furnizor | Mesaj de duplicat | |
| 6 | Sterge o factura fara intrari; incearca la una cu intrari | Prima dispare (si din pagina furnizorului); a doua are butonul dezactivat cu explicatie | |
| 7 | Cont limitat | Nu apar iconitele de editare si stergere | |
| 8 | Fila "Intrari fara factura", "Adauga produs manual" pe fereastra facturii | Functioneaza; intrarea apare in stoc legata de factura | |
| 9 | Latime de telefon | Fara depasiri orizontale | |

### 07.10.2026 - Factura asteptata

**Ce s-a adaugat:** sursa de notificari „Factura asteptata” pe grup furnizor + data (termen 14 zile, prag implicit 7), sablon in Setari, contor pe pagina principala; se inchide singura cand intrarile primesc factura.

**Ce face acum:** O intrare „factura asteptata" ramane in urmarire si o notificare te anunta daca factura furnizorului intarzie; se inchide cand intrarea se leaga de factura.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Fa intrari libere de la acelasi furnizor si aceeasi data si asteapta (sau muta data in setari) pragul de 7 zile | Apare notificarea "Factura asteptata"; contor pe pagina principala | |

## Fluxul de baza

1. Furnizor nou -> factura prin preluare -> intrari legate de factura.
2. Corectare numar pe /facturi -> jurnal.
3. Furnizor -> pagina lui -> lista facturilor.
