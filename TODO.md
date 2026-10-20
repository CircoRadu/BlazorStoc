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

## Task 1: Setari -> Baza de date

Propunere, decizii si stare: `docs/PROPUNERE_SETARI_BAZA_DE_DATE.md` (sectiunile 11-14; toate deciziile sunt confirmate, fara intrebari deschise). Subtaskuri in ordinea implementarii:

- 1.1 Etapa 1: finalizare (acces pe rol Administrator + protectia ultimului administrator, rulare teste `dbsettings` si suita, sectiune MariaDB, documentatie, `tools/start-preview.ps1`, verificare in browser).
- 1.2 Sub-tab "Ajutor" pentru baza de date.
- 1.3 Etapa Verificare (sanatate si integritate).
- 1.4 Etapa Aplicatie (3a): serviciu Windows, oprire/repornire, repornire usoara a pipeline-ului; 3b: monitor de sanatate si starea "Baza de date indisponibila".
- 1.5 Conturi de baza de date: creare cont si schimbare parola cu credentiale administrative introduse pe loc.
- 1.6 Controlul serverului MariaDB (3c): doar cand ruleaza pe acelasi calculator ca serviciu Windows (repornire/oprire/pornire server + aplicatie); remote = doar aplicatia. Proiectare in `docs/PROPUNERE_SETARI_BAZA_DE_DATE.md`, sectiunea 13.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

