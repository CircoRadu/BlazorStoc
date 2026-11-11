# Propuneri de modificari (meniul "Propune o modificare", Setari -> Dezvoltare -> Propuneri, Notificari -> Propuneri)

Modulul "Propuneri" este separat de "Raportare erori". Textul propunerilor si raspunsurile sunt vizibile tuturor utilizatorilor; imaginile doar autorului si administratorilor.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Meniu -> "Propune o modificare" (sub "Raporteaza o eroare") | Se deschide fereastra "Propune o modificare" cu captura paginii ca prima imagine si "Zona aplicatiei" completata cu pagina curenta | |
| 2 | Scrie un titlu de cel putin 4 litere care seamana cu o propunere existenta | Apare lista "Propuneri asemanatoare deja facute" | |
| 3 | Completeaza tip, importanta, descriere, "De ce"; apasa "Trimite propunerea" | Mesaj cu numarul PROP-0042; propunerea apare in "Propunerile mele" cu starea Noua | |
| 4 | Fila "Toate propunerile": cauta dupa un cuvant, comuta "Doar cele deschise" | Lista se filtreaza; propunerile inchise apar doar cand comutatorul e oprit | |
| 5 | Deschide o propunere (ochi), scrie un raspuns si trimite-l | Raspunsul apare in fir; doar autorul si administratorul au caseta de raspuns | |
| 6 | Ca administrator: Setari -> Dezvoltare -> Propuneri; fila Active; deschide propunerea, alege "Implementata", scrie versiunea 0.5.99 si un raspuns, Salveaza | Propunerea trece in fila Inchise cu versiunea; raspunsul apare in fir; jurnalul are "Propunere: stare schimbata" si "Propunere: raspuns adaugat" | |
| 7 | Ca autor: Notificari -> fila "Propuneri" | Fila e rosie; randul spune "Stare: Implementata"; deschiderea propunerii face avertizarea sa dispara | |
| 8 | Ca administrator: Notificari -> fila "Propuneri" | O propunere noua apare cu motivul "Propunere noua" pana ii schimbi starea | |
| 9 | Orice popup deschis: bara de deasupra are "Raporteaza o eroare", "Propune" si cercul rosu de inchidere | "Propune" deschide formularul peste popup; captura include popup-ul | |
| 10 | Setari -> Module: opreste modulul "Propuneri" | Dispar intrarea din meniu, butonul "Propune" din bara, fila din Notificari si sub-fila "Propuneri" din Setari | |
| 11 | Setari -> Dezvoltare -> Propuneri: exporta o propunere si lista filtrata | ZIP cu propunere.json, propunere.txt, imagini (si index.csv pentru lista) | |

## Modificari de testat - marcare zone la editarea unei propuneri (11.11.2026, versiunea 0.5.72)

**Ce s-a adaugat:** la editarea unei propuneri (Setari -> Dezvoltare -> Propuneri -> Editeaza) imaginile se pot marca cu zone, ca la raportul de eroare, cu zoom in fereastra de marcare.
**Ce face acum:** clic pe o imagine in modul editare deschide marcarea; zonele se salveaza ca imagine noua marcata-...jpg, originalul ramane; Salveaza adauga imaginea la propunere.

1. Deschide o propunere cu imagine, Editeaza, clic pe imagine, deseneaza doua zone, inchide marcarea, Salveaza: propunerea are acum si imaginea marcata.
