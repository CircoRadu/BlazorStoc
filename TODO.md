# TODO — funcționalități viitoare

## Regula de actualizare a TODO

Din 28.09.2026, taskurile finalizate nu mai stau in acest fisier: ele se muta in `IMPLEMENTED.md` (fisier separat, in acelasi director), care se completeaza doar prin append (se adauga la sfarsit, nu se rescrie ce exista deja).

- Acest fisier („TODO.md”) contine numai ce mai trebuie implementat: taskurile active și, în cadrul lor, doar subtaskurile nefinalizate.
- Taskurile active sunt numerotate consecutiv, începând de la 1, în ordinea priorității; numărul cel mai mic indică prioritatea cea mai mare.
- Când se introduce un task nou înaintea unui task existent, acesta și toate taskurile active următoare (neimplementate) se renumerotează; subtaskurile își schimbă numerele odată cu taskul.
- Când un task este finalizat, el se scoate din acest fisier, iar toate taskurile rămase neimplementate se renumerotează de la 1.
- Taskul finalizat se **adauga la sfarsitul `IMPLEMENTED.md`** (fisier in ordine cronologica: cel mai vechi primul, cel mai nou ultimul — doar append, nu se modifica intrarile anterioare), cu **data și ora implementării** (ora locală) și cu **detalierea** lucrării: ce s-a implementat, fișierele principale, deciziile, verificările efectuate și ce a rămas neverificat.
- Taskul finalizat **nu păstrează denumirea „Task N”**: primește o denumire care explică succint scopul lui, în forma „Finalizat la zz.ll.aaaa oo:mm — denumire”. Trimiterile către el din alte texte folosesc această denumire, nu un număr; nici subtaskurile lui nu mai păstrează numerele.
- Verificările care nu s-au putut efectua se trec, cu motivul și pașii, în `docs/TESTE_RAMASE.md`.

# Taskuri active

Planul de mai jos vine din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (deciziile confirmate de utilizator la 30.09.2026) si din evaluarea de la sfarsitul `docs/PROPUNERE_HARTA_MENTENANTA.md`. Detaliile fiecarui task (schema, reguli, jurnal, teste) sunt in documentul de propunere; aici sunt numai subtaskurile de urmarit. Verificarile ramase din taskurile anterioare sunt in `docs/TESTE_RAMASE.md`.

Planul din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` este implementat integral (vezi `IMPLEMENTED.md`); verificarile ramase sunt in `docs/TESTE_RAMASE.md`.

## Task 1 - Sablon de import factura de tip XML

Cerut de utilizator la 07.10.2026. Facturile de furnizor se pot prelua si dintr-un fisier **XML** (de exemplu factura electronica UBL / e-Factura), nu doar din PDF/OCR: fisierul se citeste direct, fara OCR, pe baza unui **sablon XML** care spune ce element/atribut contine numarul facturii, data, furnizorul (CUI), liniile (cod/denumire, cantitate, unitate) si totalurile.

- Sablonul XML se defineste in Setari -> Facturi (cai de elemente, ex. XPath simplificat), se salveaza ca restul sabloanelor si se potriveste furnizorului; pentru UBL standard exista un sablon propus implicit.
- Preluarea foloseste acelasi asistent ca PDF-ul (recunoastere furnizor, potrivire produse, intrari legate de factura, jurnal exact, avertismente de duplicat).
- Fisiere de test generate in teste (fara facturi reale); XML invalid sau cu alt format primeste mesaj clar.
- **Teste:** UBL valid citit complet, XML cu linii lipsa, XML invalid, furnizor necunoscut, factura deja preluata.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

