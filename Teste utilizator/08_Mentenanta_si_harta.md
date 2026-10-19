# Mentenanta, contracte, interventii si harta (/mentenanta, /mentenanta/harta)

Fluxul: Mentenanta -> puncte de lucru (adresa, poze, coordonate) -> contracte de mentenanta si acoperire -> registru de interventii -> harta cu pinuri.

## Modificari de testat

### 15.10.2026 - Mentenanta ca modul (oprire fara efect asupra restului aplicatiei)

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** Mentenanta (contracte, interventii, harta punctelor de lucru, scadentele si expirarile din Notificari) este un modul al aplicatiei, ca Analizele de risc si Vehiculele. Daca se opreste (din configurare sau dupa erori repetate), restul aplicatiei merge. Punctele de lucru si pozele lor raman la fisa beneficiarului.

**Ce face acum:** cu modulul oprit, /mentenanta, harta si fereastra hartii arata "Modul indisponibil"; intrarea Mentenanta dispare din meniu si de pe pagina principala; fisa beneficiarului se deschide cu punctele de lucru si analizele de risc, fara sectiunile Contracte de mentenanta si Interventii (apare o nota); Setari -> Harta arata nota de modul oprit. Notificarile de mentenanta deja create raman in lista.

Pasi:

1. Cu modulul pornit (normal): Mentenanta, harta, fisa unui beneficiar cu contracte si interventii, Setari -> Harta (motor si overlay) se deschid ca pana acum.
2. Porneste aplicatia cu `Modules__mentenanta__Enabled=false`: /mentenanta arata "Modul indisponibil", meniul si pagina principala nu mai au Mentenanta, Setari -> Module arata Mentenanta "Oprit (oprit din configurare)".
3. Tot cu modulul oprit: fisa unui beneficiar arata Puncte de lucru, Analize de risc si Proiecte, dar nu Contracte de mentenanta si Interventii; Setari -> Harta arata nota; Notificari se deschide fara erori.
4. Un beneficiar sau un punct de lucru acoperit de un contract nu poate fi sters nici cu modulul oprit (mesajul obisnuit).

### 10.10.2026 - Detaliile punctului de pe harta, in fereastra popup

**Ce s-a adaugat:** detaliile unui punct (beneficiar, contract, termen, ciclicitate, scadenta, ultima interventie, butoanele) se deschid intr-o fereastra popup, nu in panoul din dreapta; harta ocupa toata latimea.

**Ce face acum:** apasarea unui pin deschide popup-ul centrat; se inchide cu ×, cu clic pe fundal sau cu Escape; „Alte puncte la aceeasi locatie" schimba continutul popup-ului. Insignele (On, In curand etc.) au padding mai mare (regula 6 px / 12 px, aplicata la toate insignele aplicatiei).

1. La un punct cu alte puncte la aceeasi locatie, apasa unul din lista: popup-ul trece la acel punct.
2. Ecran ingust: popup-ul incape fara depasire orizontala.

## Verificari ramase (fara modificari recente; detalii in docs/TESTE_RAMASE.md, sectiunile N7-N27)

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Punct de lucru nou, incarcare poze reale, tragere cu mouse-ul | Galeria functioneaza | |
| 2 | Contract: mutarea unui punct intre contracte active; stergerea unui contract | Jurnal exact; fara blocaje | |
| 3 | Interventii: poze reale, stergerea unui punct cu interventii (rol limitat) | Mesaj corect, fara pierderi | |
| 4 | Harta: multe puncte, clusterizare, fereastra separata pe doua monitoare | Pinurile si panoul functioneaza; dale incarcate | |
| 5 | Cheie harta: schimba furnizorul pe o harta deschisa | Se reincarca fara eroare | |
| 6 | Ecran ingust: sectiunea de contracte, interventii, harta, tipuri de pinuri | Fara depasiri orizontale | |

## Fluxul de baza

1. Punct nou -> contract activ -> interventie -> apare pe harta si in notificari.
