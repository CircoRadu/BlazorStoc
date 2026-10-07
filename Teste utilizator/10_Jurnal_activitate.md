# Jurnal de activitate (/jurnal, /jurnal/complet) - doar administrator

Regula: jurnalul numeste exact operatia (nu "Creare"/"Editare" generic). Fiecare actiune noua este si in filtrul paginii.

## Modificari de testat

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
