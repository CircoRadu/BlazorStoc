# Jurnal de activitate (/jurnal, /jurnal/complet) - doar administrator

Regula: jurnalul numeste exact operatia (nu "Creare"/"Editare" generic). Fiecare actiune noua este si in filtrul paginii.

## Modificari de testat

### 24.10.2026 - Setari -> Jurnal activitate: cate evenimente recente arata pagina

**Ce s-a adaugat:** in Setari apare tile-ul „Jurnal activitate" (doar administrator), unde alegi cate dintre cele mai recente evenimente le arata implicit pagina Jurnal activitate (minim 50, maxim 50 000; 500 pana la prima modificare). Nu se sterge nimic din jurnal. Operatie noua in jurnal: „Modificare evenimente afisate in jurnal".

**Ce face acum:** pagina Jurnal activitate (titlul „Ultimele N de evenimente" si nota de sub el) lucreaza pe numarul setat; „Jurnalul complet" nu depinde de el si arata toate evenimentele. Valoarea se aplica in cel mult 30 de secunde.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Deschide Setari -> Jurnal activitate | Un camp cu 500 (valoarea implicita) si, sub el, intervalul 50 - 50 000 si numarul de evenimente din jurnal | |
| 2 | Scrie 49 si apasa Salveaza | Mesaj ca numarul trebuie sa fie intre 50 si 50 000; nimic nu se salveaza | |
| 3 | Scrie 1000 si apasa Salveaza | Mesaj de confirmare | |
| 4 | Deschide pagina Jurnal activitate | Titlul spune „Ultimele 1000 de evenimente"; daca jurnalul are mai multe, nota de sub titlu spune ca pagina lucreaza pe cele mai recente 1000 | |
| 5 | Deschide Jurnal complet | Toate evenimentele raman accesibile, nu doar 1000 | |
| 6 | In Jurnal filtreaza dupa operatia „Modificare evenimente afisate in jurnal" | Apare evenimentul cu detalii vechi -> nou (500 -> 1000) | |
| 7 | Intra cu un cont care nu este administrator | Tile-ul Jurnal activitate nu apare in Setari | |

### 22.10.2026 - Adresa IP la conectari si deconectari, conectari esuate

**Ce s-a adaugat:** evenimentele „Conectare" si „Deconectare" din Jurnal contin acum adresa IP de pe care s-a conectat utilizatorul (data si ora erau deja in jurnal); operatie noua „Conectare esuata" (nume tastat, motiv, IP, fara parola), si in filtrul „Operatie".

**Ce face acum:** la fiecare autentificare reusita si la fiecare deconectare, coloana „Detalii" arata „Sesiune autentificata · IP <adresa>" / „Sesiune incheiata · IP <adresa>". Evenimentele mai vechi raman fara IP.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Autentifica-te, apoi deconecteaza-te; deschide Jurnal activitate | Doua evenimente (Conectare, Deconectare) cu data si ora, iar in „Detalii" adresa IP a calculatorului de pe care ai intrat | |
| 2 | Intra de pe alt calculator din retea | Evenimentul „Conectare" arata IP-ul acelui calculator | |
| 3 | Pe pagina de autentificare scrie un utilizator si o parola gresite, apoi intra corect si deschide Jurnal activitate | Eveniment „Conectare esuata": operatorul este numele tastat, in „Detalii" „Autentificare refuzata: utilizator sau parola incorecte · IP <adresa>"; parola nu apare nicaieri. Filtrul „Operatie" contine „Conectare esuata" | |
| 4 | Incearca un cont inactiv si apoi parola gresita pentru administratorul configurat | Evenimente „Conectare esuata" cu motivele „cont inactiv" si „parola gresita" | |

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
