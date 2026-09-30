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

## Task 1. Registru de interventii (propunere, sectiunile 3.4, 4)

Interventii de mentenanta si la cerere, cele trei variante de scadenta, poze, pagina `/mentenanta`.

De legat de contracte (deja implementate): la `service_contract_points.next_due` se scrie scadenta aleasa la inregistrare (contractul si punctul se blocheaza cu `FOR UPDATE` sub blocarea beneficiarului, ca in `MariaServiceContractRepository`); stergerea unui contract sau scoaterea unui punct din contract cu interventii se refuza cu mesaj clar (azi cheile straine ale interventiilor nu exista, deci verificarea se adauga odata cu ele); pagina `/mentenanta` primeste tabul "Scadente" cu comutatorul "Arata si contractele Off" (gri, fara stare de scadenta).

## Task 2. Surse de notificare pentru mentenanta (propunere, sectiunile 6.2, 6.3)

`mentenanta.scadenta` si `contract.expirare`, cu marcajele `<beneficiar>`, `<numar contract>`, `<data expirare>` etc.

Pragul vizual al starii "In curand" (azi `ServiceDueRules.DefaultThresholdDays` = 30 in `Services/ServiceContracts.cs`) trece la pragul sablonului activ al sursei `mentenanta.scadenta`.

## Task 3. Harta de mentenanta (optional, `docs/PROPUNERE_HARTA_MENTENANTA.md`)

Depinde de contracte (implementate) si de Task 1 (registru); coordonatele si pozele punctelor de lucru exista deja.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

