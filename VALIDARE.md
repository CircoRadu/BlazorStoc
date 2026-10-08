# Verificări — versiunea 0.2

> **Numerotarea taskurilor.** Numerele „Task N” din acest fișier sunt cele folosite la momentul implementării. Din 25 septembrie 2026 `TODO.md` nu mai păstrează numere pentru taskurile finalizate, ci denumiri succinte; corespondența este: Task 0 = „Stoc exclusiv prin mișcări de intrare și ieșire”; Task 1 = „Intrări și ieșiri pentru un produs existent” (în perioada timpurie a proiectului „Cod produs”); Task 2 = „Proiecte asociate beneficiarilor”; Task 3 = „Navigarea din jurnal către pagina obiectului”; Task 4 = „Identificarea beneficiarului cu CUI duplicat”; Task 5 = „Confirmarea salvărilor care modifică date existente”; Task 6 = „Confirmarea la părăsirea formularului de adăugare produs”; Task 7 = „Confirmarea deconectării”; Task 8 = „Sincronizarea între utilizatori prin evenimente din baza de date”; Task 9 = „Blocarea temporară a editării unui produs”. Taskul activ „Situația de inventar” (fost Task 10) este acum **Task 1**.

> Verificările care nu au putut fi efectuate sunt urmărite, cu pașii și motivul, în `docs/TESTE_RAMASE.md`.

> Intrarile mai vechi sunt in `docs/arhiva/VALIDARE_pana_la_30.09.2026.md` (verificari istorice).

## ANAF, pagina produsului si uniformizarea interfetei (08.10.2026)

- Verificat automat: build Release; suita completa (979 in memorie, 1588 MariaDB, 229 componente); decizia regulilor ANAF pe camp, versiunile configuratiei, filtrul pe text si pe zi al miscarilor, validarea cantitatii reale, dialogul de diferente; in browserul din aplicatie: selectii cu click real in liste, taburile Intrare/Iesire, filtrele din tabel, inaltimea butoanelor vecine pe toate paginile si subsectiunile din Setari.
- Neverificat: dialogul de diferente ANAF pe date reale ANAF, ferestrele de dialog deschise (padding, butoane), culorile pe un ecran ingust; vezi `Teste utilizator/` si `docs/TESTE_RAMASE.md`.

## Backup unificat, verificare ora, iconite (08.10.2026)

- Verificat automat: build Release; suita completa (vezi `docs/PROJECT_STATE.md`); verificari tintite pentru regulile de stergere (ultimele 4, ora neverificata exclusa), decizia de ceas (decalat, bun, fara internet, servere in dezacord), validarea serverelor NTP, zilele saptamanii si notificarea de ceas.
- Neverificat: ecranele din Setari -> Backup si Notificari in browser, comunicarea reala cu serverele NTP, stergerea reala de fisiere, restaurarea dintr-un pachet doar pe NAS; vezi `Teste utilizator/09_Notificari_si_setari.md` si `docs/TESTE_RAMASE.md`.

## Setari notificari: curatarea rezolvatelor (30.09.2026)

- `tests/BlazorStoc.Checks`: 636 PASS (601 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test` (`RUN_MARIA_INTEGRATION_CHECKS=1`); sectiunea noua "Notification settings: clean-up of old resolved notifications" acopera valorile implicite, accesul doar pentru administrator, validarea perioadei, ce se sterge si ce se pastreaza, activarea cu stergere imediata, scurtarea perioadei, oprirea, rularea zilnica o data pe zi (ceas simulat) si jurnalul.
- Migrarea 6 aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087) si pe `blazorstoc_test` (`--migrate-schema`).
- Browser (baza de test, port 5088): subtabul "Setari notificari", popup cu "3 notificari rezolvate mai vechi de 30.09.2025", Anuleaza (nimic nu se schimba), Confirma si sterge (mesaj, stare "activa ... ultima curatare"), scurtare 12 -> 1 luna cu popup pentru inca o notificare, oprire fara popup, intrarile din Jurnal. Pagina de pe baza reala (5087) se incarca cu setarile implicite; nicio setare nu a fost modificata pe baza reala.
- Neverificat: ecran ingust, rulare zilnica pe ceas real, restaurare dintr-un backup anterior migrarii; vezi `docs/TESTE_RAMASE.md` (N4-N6).

## Puncte de lucru extinse (30.09.2026)

- `tests/BlazorStoc.Checks`: 680 PASS (647 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test` (`RUN_MARIA_INTEGRATION_CHECKS=1`); sectiunea "Work points" a fost rescrisa (punct principal real si unic, urmarea beneficiarului, descriere, coordonate, jurnal, poze, stergeri arhivate, backfill).
- Migrarea 7 aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087; backfill: 7 randuri principale create, 2 beneficiari fara adresa) si pe `blazorstoc_test` (`--migrate-schema`).
- Browser (baza de test, port 5088): tabelul cu coloanele noi, punct nou cu coordonate lipite, incarcare de poze (un `.txt` refuzat), stergere de poza, editarea punctului principal, stergerea punctului cu poza lui (arhivat), intrarile din Jurnal. Nu s-a modificat nimic manual pe baza reala in afara migrarii si a backfill-ului de la pornire.
- Neverificat: ecran ingust, rol limitat, fisiere reale de 10 MB, restaurare dintr-un backup anterior migrarii; vezi `docs/TESTE_RAMASE.md` (N7-N9).

## Contracte de mentenanta si acoperire (30.09.2026)

- `tests/BlazorStoc.Checks`: 743 PASS (680 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test` (`RUN_MARIA_INTEGRATION_CHECKS=1`); sectiune noua "Maintenance contracts" (acoperire, un contract activ per punct, On/Off, mutari, doua sesiuni simultane, arhivare) si verificari pure pentru numarul/data contractului, validare, stari de scadenta, actiuni de jurnal, arhivare si migrarea 8.
- Migrarea 8 aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087) si pe `blazorstoc_test` (`--migrate-schema`). Nimic nu a fost creat manual pe baza reala.
- Browser (baza de test, port 5088, beneficiarul 241): contract nou cu doua puncte, expandare, Dezactiveaza / "Arata si contractele Off", Activeaza cu panoul de reprogramare, coloana "Mentenanta" din tabelul punctelor, refuzul stergerii unui punct acoperit.
- Neverificat: ecran ingust, rol limitat, mutare si stergere de contract din interfata (acoperite in teste), restaurare dintr-un backup anterior migrarii 8; vezi `docs/TESTE_RAMASE.md` (N10-N12).

## Registru de interventii si campuri de fisiere cu drag and drop (30.09.2026)

- `tests/BlazorStoc.Checks`: 815 PASS (743 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test`; sectiune noua "Maintenance interventions" (variantele E/P/O, interventie mai veche, corectari, poze, registru, doua sesiuni simultane, arhivare, ocrotirile contract/punct/beneficiar) si verificari pure pentru variantele de scadenta, regula ultimei interventii, validare, actiuni de jurnal, arhivare si migrarea 9.
- Migrarea 9 aplicata pe baza reala `BlazorStoc` (la pornirea preview-ului 5087) si pe `blazorstoc_test` (`--migrate-schema`). Nimic nu a fost creat manual pe baza reala.
- Browser (baza de test, port 5088, beneficiarul 241): inregistrare cu alegerea scadentei, poza, interventie veche, interventie la cerere, stergere cu revenirea scadentei, pagina `/mentenanta`, lasare de fisier pe zona de drag and drop.
- Neverificat: ecran ingust, fotografii reale, rol limitat, stergere de punct cu interventii din interfata, restaurare dintr-un backup anterior migrarii 9, tragere reala cu mouse-ul; vezi `docs/TESTE_RAMASE.md` (N13-N16).

## Surse de notificare pentru mentenanta (30.09.2026)

- `tests/BlazorStoc.Checks`: 835 PASS (815 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test`; sectiune noua "Maintenance notifications" (notificare pe punct si pe contract, inchidere automata la interventie / prelungire / contract Off, redeschidere cand data revine, prag din sablon) si verificari pure pentru surse, marcaje, texte si motive.
- Nu s-a modificat schema. Pe baza reala nu s-a creat niciun sablon.
- Browser (baza de test, port 5088): sablon nou pe categoria "Mentenanta" (text-exemplu, marcaje), notificarile aparute pe `/notificari`, starea "In curand" din `/mentenanta` dupa pragul sablonului.
- Neverificat: ecran ingust, rularea la interval real cu mai multe sesiuni; vezi `docs/TESTE_RAMASE.md` (N17-N18).

## Harta de mentenanta (30.09.2026)

- `tests/BlazorStoc.Checks`: 845 PASS (835 inainte), 3 rulari consecutive fara esec, cu integrare MariaDB pe `blazorstoc_test`; verificari pure pentru termenul contractului, umplerea si insigna marker-ului, marker-e si puncte fara coordonate, configuratia furnizorului de dale, si o verificare de integrare pentru coordonatele si ultima interventie din lista de scadente.
- Nu s-a modificat schema. Leaflet si markercluster au fost descarcate cu acordul utilizatorului in `wwwroot/lib/leaflet`.
- Browser (baza de test, port 5088): harta cu dale reale, marker-e si insigne, panou de detalii, filtru, link catre formularul de interventie cu punctul preselectat.
- Neverificat: ecran ingust, furnizor de dale indisponibil, multe puncte (clusterizare), termenii serverului public OSM; vezi `docs/TESTE_RAMASE.md` (N19-N21).

## Sabloane de facturi in Setari (30.09.2026)

- `tests/BlazorStoc.Checks`: 1000 PASS, cu integrare MariaDB pe `blazorstoc_test` (`RUN_MARIA_INTEGRATION_CHECKS=1`); 77 verificari noi de facturi (valori, vocabular, facturi generate in mai multe layout-uri, scanari cu OCR, sabloane aplicate pe alte facturi, cele 3 facturi reale din `INVOICE_CORPUS_DIR`, sesiuni, serviciu si jurnal) si sectiunea MariaDB „Invoice templates" (16 verificari). Rapid, fara restul suitei: `INVOICE_CHECKS_ONLY=1 dotnet run --project tests/BlazorStoc.Checks`.
- Migrarea 10 aplicata pe `blazorstoc_test` (`--migrate-schema`) si, la pornirea preview-ului 5087, pe baza reala `BlazorStoc`. Nimic nu a fost creat manual pe baza reala.
- Browser (baza de test, port 5088): incarcarea unei facturi, suprapunerea, editarea, desenarea unui camp, salvarea, propunerea si aplicarea unui sablon pe factura altui furnizor, versiune noua, stergere cu motiv, jurnal.
- Neverificat: scanari reale, desenare reala cu mouse-ul, ecran ingust, facturi reale de alte tipuri; vezi `docs/TESTE_RAMASE.md` (N28-N33).

## Sabloane de facturi la preluare, acces pentru utilizatori obisnuiti, comutatoare On/Off (06.10.2026)

- `tests/BlazorStoc.Checks`: 837 PASS (fara integrare MariaDB), cu cele 5 facturi reale din `INVOICE_CORPUS_DIR`; teste noi: normalizarea „C.U.|.", toate coloanele initial folosite, drepturile serviciului de sabloane (operator vs administrator vs fara acces), fluxul de preluare fara sablon (avertizare, fereastra, salvare si aplicare, inchidere fara salvare), tab-urile din Setari pentru utilizator obisnuit, selectia pe randuri, categorii (mutare, anulare, parasire).
- Browser (preview 5087, administrator): preluare `905550.pdf` pana la sablon creat si aplicat, detectare automata la incarcarea repetata, lista din Setari, Inventar (comutatoare si selectie).
- Neverificat: contul de utilizator obisnuit in browser, comutatoarele din celelalte pagini pe telefon; vezi `docs/TESTE_RAMASE.md` (N46).

## Facturi (pagina), protocol cu context redus si unelte de test (07.10.2026)

- Pagina `/facturi`: lista, filtre, popup cu produsele preluate (link la intrarile produsului), corectare numar/data/furnizor si stergere fara intrari (administrator). Jurnal: „Modificare numar factura”, „Modificare data factura”, „Mutare factura la alt furnizor”, „Stergere”.
- Verificari: build Release reusit; suita in memorie 913 trecute, 0 esecuri (`tools\run-checks.ps1`); pe `blazorstoc_test` sectiunea „Suppliers and invoices” trece integral (corectare, copie veche, duplicat, motiv, drepturi, stergere blocata/permisa, jurnal); popup-ul de produse verificat de utilizator in preview.
- Unelte: `CHECKS_ONLY=<grup>` (suppliers, pickup, groups, reasons, components, invoices, ui), `tools\run-checks.ps1`, `tools\log-task.ps1`; istoricul vechi mutat in `docs/arhiva/`.
- Neverificat: pagina Facturi pe ecran ingust si cu rol „Utilizator”; vezi `docs/TESTE_RAMASE.md`.
- 07.10.2026 (inainte de commit): suita in memorie 935 trecute, suita MariaDB `blazorstoc_test` 1505 trecute, 0 esecuri; migrarile 1-27 aplicate. Neverificat in browser: paginile si fluxurile din `Teste utilizator/` marcate 07.10.2026.

