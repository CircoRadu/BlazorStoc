# Jurnal de activitate (/jurnal, /jurnal/complet) - doar administrator

Regula: jurnalul numeste exact operatia (nu "Creare"/"Editare" generic). Fiecare actiune noua este si in filtrul paginii.

## Modificari de testat

### 20.10.2026 - Actiuni noi de jurnal pentru actualizarea aplicatiei

**Ce s-a adaugat:** opt actiuni, toate si in filtrul paginii Jurnal, entitatea „Aplicatie": Actualizare aplicatie (la confirmare, pe numele administratorului, cu motiv, versiunile si suma de control a pachetului), Anulare actualizare aplicatie, Actualizare reusita, Actualizare esuata, Actualizare refuzata, Revenire la versiunea anterioara (manuala, cu installerul), Actualizare aplicatie (installer) si Confirmare esec actualizare. Rezultatele le scrie „sistem", din istoricul Updater-ului, la pornirea aplicatiei (o singura data).

**Pasi:** vezi `09_Notificari_si_setari.md` (Setari -> Actualizare); dupa o actualizare filtreaza Jurnalul dupa entitatea „Aplicatie".

### 19.10.2026 - Oprire si pornire manuala a modulelor in Jurnal

**Verificat de agent 19.10.2026** (instanta de test pe baza de test: oprire cu motiv, repornirea aplicatiei, pornire, modul oprit din configurare, pauza programarii backup; vezi IMPLEMENTED.md).

**Ce s-a adaugat:** doua actiuni noi, "Oprire manuala modul" si "Pornire manuala modul", scrise pe numele administratorului (nu al utilizatorului "sistem"), in filtrul de actiuni al paginii Jurnal, entitatea "Module".

**Ce face acum:** la oprire, intrarea arata starea veche si noua ("Functioneaza" -> "Oprit de administrator") si motivul scris de administrator; la pornire, arata cine a oprit modulul, de cand, cat a durat oprirea si motivul opririi (motivul pornirii este generat automat). Oprirea dupa erori repetate ramane "Oprire modul" scrisa de "sistem".

Pasi:

1. Opreste si porneste un modul din Setari -> Module (vezi `09_Notificari_si_setari.md`).
2. In Jurnal filtreaza dupa entitatea "Module" si actiunile "Oprire manuala modul" / "Pornire manuala modul": ambele apar pe numele tau, cu detaliile de mai sus.
3. Verifica ca la o oprire manuala nu apare si o intrare "Oprire modul" de la "sistem".

### 19.10.2026 - Jurnalul ca modul

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** citirea Jurnalului (/jurnal si /jurnal/complet) este acum modulul „Jurnal de activitate” (Setari -> Module); inregistrarea operatiilor in Jurnal ramane in aplicatie.

**Ce face acum:** Jurnalul functioneaza ca inainte; daca citirea lui cade (erori repetate) sau modulul e oprit din configurare, linkul din meniu si tile-ul de pe pagina principala dispar, paginile spun „Modul indisponibil”, iar restul aplicatiei merge si continua sa jurnalizeze operatiile.

1. Deschide Jurnal activitate si Jurnal complet: filtrele, cautarea, paginarea functioneaza ca inainte.
2. In Jurnal complet exporta CSV: fisierul se descarca, iar in Jurnal apare „Export jurnal” cu numarul de evenimente.
3. In Setari -> Module „Jurnal de activitate” este „functioneaza”.


### 15.10.2026 - Module si oprirea aplicatiei in Jurnal

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** Jurnalul de activitate inregistreaza acum, de catre utilizatorul "sistem": "Oprire modul" (un modul scos din functiune dupa erori repetate, sau care nu a pornit / e oprit din configurare), "Revenire modul", "Reincercare modul" (cand administratorul apasa Reincearca, pe numele lui) si "Oprire neasteptata aplicatie" (la pornirea de dupa o cadere: ultima activitate, ora pornirii, procesul si cauza, cand se cunoaste). Entitatile "Module" si "Aplicatie" si cele patru actiuni apar in filtrele paginii.

**Ce face acum:** o cadere a aplicatiei lasa urma in Jurnal la urmatoarea pornire; o oprire ceruta (supervizorul `-Action stop`, oprirea normala) nu lasa nicio intrare. Un modul oprit apare o singura data in Jurnal pe durata iesirii din functiune.

Pasi:

1. Omoara procesul aplicatiei (sau lasa supervizorul sa o reporneasca dupa o cadere), apoi deschide Jurnal activitate: apare "Oprire neasteptata aplicatie" (entitate Aplicatie, utilizator sistem), cu ultima activitate si cauza ("exited with code ...").
2. Opreste aplicatia normal (`tools\supervisor.ps1 -Action stop`): la pornirea urmatoare nu apare nicio intrare noua.
3. Filtreaza dupa entitatea "Module": apar intrarile "Oprire modul" / "Revenire modul" / "Reincercare modul", daca au existat.

### 07.10.2026 - Cauza iesirii peste stoc

**Ce s-a adaugat:** detaliile evenimentelor de iesire contin „Cauza peste stoc” si „Operație: #id”; actiuni noi: Regularizare stoc negativ (din lista), Stornare operatie de iesire, Retur de la beneficiar, actiunile pentru Nomenclator si componentele proiectului (filtrele jurnalului le contin).

**Ce face acum:** Jurnalul arata cauza iesirii peste stoc si poate fi filtrat dupa ea.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Iesire peste stoc cu cauza aleasa | Evenimentul (ex. "Vanzare generica") are in detalii "Peste stoc: N" si "Cauza peste stoc: ..." | |
| 2 | Regularizare din tabul "De regularizat" | Eveniment "Regularizare stoc negativ" cu motivul "Regularizare din lista De regularizat" | |

### 07.10.2026 - Actiuni noi la ieșiri si intrari

**Ce s-a adaugat:** actiuni de jurnal exacte pentru iesiri (spre beneficiar/vehicul, vanzare generica, corectie stoc, iesire dublata) si intrari (intrare libera, atasare/detasare la factura, intrare dublata pe factura, regularizare), toate in filtrul de actiuni.

**Ce face acum:** Jurnalul numeste exact operatia (retur, storno, iesire spre beneficiar, rezervari, componente) si are filtre pentru fiecare actiune noua.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Filtrul de actiuni din jurnal | Contine: Iesire spre beneficiar, Iesire spre vehicul, Vanzare generica, Corectie stoc, Iesire dublata (confirmat), Intrare libera, Atasare/Detasare la factura, Intrare dublata pe factura, Regularizare stoc negativ | |
| 2 | Dupa fiecare operatie din 02_Intrari_si_iesiri_produs.md | Evenimentul are actiunea exacta si link catre miscare | |
| 3 | Iesire peste stoc | Detaliile contin "Peste stoc: N" | |
| 4 | Facturi: modificare numar/data, mutare, stergere | Cate un eveniment pe fel de schimbare, cu valoare veche/noua si motiv | |
| 5 | Furnizor: adaugare, modificare, reverificare ANAF | Evenimente proprii | |

## Fluxul de baza

1. Fa o operatie -> /jurnal -> eveniment cu utilizator, ora (dd.mm.yyyy hh:mm), detalii.
2. Jurnal complet: paginare, derulare, export CSV; ecran ingust.
