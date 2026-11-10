# Raportare erori (meniu "Raporteaza o eroare" si Setari -> Rapoarte erori)

## Modificari de testat

### 10.11.2026 - Insigne pe taburile Setari -> Dezvoltare

**Ce s-a adaugat:** insigna cu numarul de obiecte nerezolvate pe tile-ul Dezvoltare si pe sub-taburile Erori si Propuneri.
**Ce face acum:** Erori arata cate rapoarte nu sunt Rezolvate sau Respinse, Propuneri cate nu sunt inchise (Implementata, Amanata, Respinsa), tile-ul suma lor; insigna dispare la 0 si se actualizeaza singura cand se adauga sau se schimba un raport / o propunere.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Setari | Pe tile-ul Dezvoltare apare insigna cu suma rapoartelor nerezolvate si a propunerilor active | |
| 2 | Setari -> Dezvoltare | Sub-taburile Erori si Propuneri au fiecare insigna cu numarul lor | |
| 3 | Marcheaza un raport Rezolvat | Insigna de pe Erori scade cu 1, fara reincarcarea paginii | |
| 4 | Din alt cont adauga o propunere | Insigna de pe Propuneri creste singura | |
| 5 | Cand nu mai e nimic nerezolvat | Insignele dispar | |

### 07.11.2026 - Modulul de raportare a erorilor

**Ce s-a adaugat:** intrarea "Raporteaza o eroare" in meniul principal, deasupra lui "Ajutor" (vizibila oricarui utilizator autentificat, pe paginile cu meniul principal); fereastra de raportare cu sectiuni explicative inchise implicit, captura automata a paginii, lipire de imagini cu Ctrl+V, alegere de fisiere, comutator pentru detaliile tehnice; fila "Rapoartele mele"; fila "Rapoarte erori" din Setari (administrator) cu filtre, stare + nota, stergere si export ZIP.

**Ce face acum:** apasarea intrarii fotografiaza pagina de sub fereastra si o atasaza raportului; raportul ramane in aplicatie, cu numar (BUG-0001...); administratorul il vede in Setari, ii schimba starea, il exporta ca ZIP (raport.json, raport.txt, imagini; index.csv la export multiplu) sau il sterge. Nimic nu se trimite in afara aplicatiei.

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Intra pe orice pagina (ex. Beneficiari) si apasa "Raporteaza o eroare" din meniu (deasupra lui "Ajutor") | Se deschide fereastra; prima imagine din lista este captura paginii pe care erai | |
| 2 | Deschide pe rand cele cinci sectiuni explicative de sus | Sunt inchise la deschidere; fiecare se deschide la clic si descrie fluxul, ce se ataseaza, imaginile, stari, confidentialitatea | |
| 3 | Fa o captura cu Win+Shift+S si apasa Ctrl+V in fereastra | Apare a doua imagine "Imagine lipita"; coșul de lângă ea o scoate | |
| 4 | Lasa titlul gol si apasa "Trimite raportul" | Mesaj de eroare: scrie un titlu | |
| 5 | Completeaza titlul, gravitatea, descrierea si trimite | Mesajul "Raportul BUG-000N a fost trimis" | |
| 6 | Apasa "Raporteaza o eroare" din nou, deschide "Rapoartele mele" | Raportul apare cu starea Nou | |
| 7 | Scrie ceva, apasa "Inchide (pastreaza ciorna)", redeschide | Textul scris a ramas | |
| 8 | Setari -> Rapoarte erori: filtreaza dupa stare/autor, deschide raportul (pictograma ochi) | Se vad descrierea, imaginile, detaliile tehnice (sectiune inchisa) | |
| 9 | Schimba starea in "In analiza", scrie o nota, salveaza; intoarce-te la "Rapoartele mele" ca autor | Starea si nota apar la autor | |
| 10 | "Exporta raportul (ZIP)" si "Exporta lista filtrata (ZIP)" | Se descarca fisiere ZIP cu folder pe raport; lista filtrata are si index.csv | |
| 11 | Sterge raportul (coș, confirmare) | Raportul dispare; Jurnal activitate arata crearea, schimbarea starii, exportul si stergerea cu actiunile lor | |
| 12 | Pe o pagina cu harta (Mentenanta -> Harta) fa un raport | Captura poate fi incompleta in zona hartii; lipeste o captura proprie cu Ctrl+V | |
