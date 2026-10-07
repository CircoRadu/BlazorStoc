# Oferte - preluarea devizului-oferta (/oferte/preluare)

Asistent in 5 pasi, ca la „Preluare factura", dar se recunoaste un **beneficiar** (nu un furnizor). Ia un deviz-oferta Excel (.xlsx), il citeste cu un sablon (vezi 13_Oferte_sabloane.md) si il leaga de beneficiar, proiect si componenta, iar liniile in bucati le potriveste cu produsele din catalog. Stocul se tine doar pe bucati: liniile cu alta unitate (metri, ore) sunt marcate „in afara stocului". Initial doar fisiere .xlsx (un .xls vechi trebuie salvat ca .xlsx).

## Modificari de testat

### 07.10.2026 - Preluarea unei oferte

**Ce s-a adaugat:** pagina `Preluare oferta` (`OfferPickup.razor`, meniu Oferte si card pe pagina principala) cu cei 5 pasi: (1) fisierul + sablonul ales automat dupa etichete, antetul citit (numar, titlu, categorie, beneficiar) editabil si avertizare daca oferta exista deja; (2) beneficiarul: recunoscut dupa denumiri alternative din oferte anterioare sau propus dupa asemanare (forme juridice ignorate; la persoane fizice se arata si CUI-ul), cautare in toti beneficiarii, „+ Beneficiar nou” (formularul existent, intr-o fereastra, precompletat cu numele din oferta) si optiunea de a retine numele din oferta ca denumire alternativa; (3) proiectul (existent sau nou cu titlul ofertei) si componenta (categoria ofertei -> tip de sistem prin Nomenclator, inclusiv denumiri alternative; componenta se adauga la proiect sau se reactiveaza); (4) liniile: potrivire cu catalogul pe tot textul denumirii (codurile de model cantaresc mai mult), legaturi retinute din oferte anterioare, propuneri cu procent, cautare in catalog, „Produs nou” (formularul existent de produs), „de achizitionat” implicit, „in afara stocului” pentru metraje/ore; (5) rezumat cu diferentele fata de revizia anterioara. Tabele noi (migrarea 25): `offers`, `offer_lines`, `offer_line_matches`, `beneficiary_aliases`. Jurnal exact (la proiect): „Preluare oferta”, „Revizie oferta”, „Legare linie oferta de produs” (cate una pe linie legata), „Adaugare denumire alternativa beneficiar”.

**Ce face acum:** O oferta se preia in 5 pasi, cu beneficiar si produse potrivite, proiect si componenta create la nevoie; acelasi numar de oferta nou devine revizie cu diferentele afisate.

**Flux:** fisier .xlsx -> beneficiar -> proiect + componenta -> legarea liniilor de produse -> rezumat -> „Preia oferta”. Acelasi numar de oferta = revizie (1, 2, 3…) cu diferentele (+ adaugate, − scoase, ~ cantitate schimbata). Ieșirile deja facute pe proiect NU se leaga automat de oferta.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Oferte -> Preluare oferta; trage un deviz .xlsx (din cele de proba) | Sablonul se alege singur; apar numarul, titlul, categoria, beneficiarul din oferta si lista de sectiuni (Echipamente se importa; Manopera/Cheltuieli nu) | |
| 2 | Alege manual alt sablon / fisier fara sablon potrivit | Avertizare ca etichetele nu se potrivesc; fara sabloane active: link spre crearea unui sablon | |
| 3 | „Pasul urmator” cu un beneficiar existent in oferta | Pasul 2: beneficiari asemanatori cu procent; un nume identic fara forma juridica (SC…SRL) iese primul si e preselectat | |
| 4 | Persoana fizica printre candidati | Se vede „Persoana fizica · CUI …” | |
| 5 | „+ Beneficiar nou” | Se deschide formularul de adaugare beneficiar cu numele din oferta; dupa salvare beneficiarul nou e ales | |
| 6 | Alege un beneficiar al carui nume difera de cel din oferta; lasa activa „Reține denumirea…” | La pasul 5 rezumatul spune ca se retine denumirea; la o oferta viitoare cu acelasi nume beneficiarul e recunoscut singur („recunoscut dupa o denumire alternativa”) | |
| 7 | Pasul 3: proiect existent al beneficiarului sau „Proiect nou” | Proiect nou: denumirea implicita e titlul ofertei (editabila); componenta propusa din categoria ofertei (ex. CCTV -> TVCI daca „CCTV” e denumire alternativa in Nomenclator) | |
| 8 | Categorie fara corespondent in Nomenclator | Mesaj ca nu corespunde niciunui tip; poti alege manual sau lasa fara componenta | |
| 9 | Pasul 4: priveste liniile | Fiecare linie in bucati are propuneri din catalog cu procent; liniile cu cod de model identic cu un produs sunt preselectate („Propus automat — verifica”); metrajele/orele au „in afara stocului” | |
| 10 | Schimba produsul unei linii (cautare), alege o propunere, lasa alta „de achizitionat” | Se schimba imediat; „De achizitionat” cand nu e ales niciun produs | |
| 11 | „Produs nou” pe o linie | Se deschide formularul de produs cu denumirea (prima linie) si descrierea (restul) precompletate; dupa salvare produsul e ales | |
| 12 | Pasul 5 si „Preia oferta” | Mesaj cu numarul legaturilor/„de achizitionat”/„in afara stocului” si link catre proiect; pe pagina proiectului apare componenta | |
| 13 | Preia aceeasi oferta a doua oara, cu o cantitate schimbata si o linie scoasa | Pasul 1 anunta „va fi revizia 2”; la pasul 5 apar diferentele cu liniile afectate; legaturile confirmate la prima preluare sunt reținute si preselectate | |
| 14 | /jurnal | „Preluare oferta” / „Revizie oferta” (cu diferentele), „Legare linie oferta de produs” pentru fiecare linie legata, „Adaugare denumire alternativa beneficiar” | |
| 15 | Incarca un fisier .xls sau care nu e Excel | Mesaj clar (salveaza ca .xlsx) | |
| 16 | Ecran ingust | Tabelul de linii se deruleaza in interiorul lui; ferestrele nu ies din pagina | |

Cunoscut: proiectul nou si componenta se creeaza chiar la „Preia oferta”, imediat inaintea ofertei (fiecare cu jurnalul lui); produsele noi create din fereastra raman in catalog chiar daca renunti ulterior la preluare.

## Fluxul de baza

1. Oferta noua: fisier -> beneficiar -> proiect/componenta -> linii -> preluare.
2. Oferta revizuita (acelasi numar): aceeasi cale, cu diferentele afisate si potrivirile retinute.
