# Mentenanta, contracte, interventii si harta (/mentenanta, /mentenanta/harta)

Fluxul: Mentenanta -> puncte de lucru (adresa, poze, coordonate) -> contracte de mentenanta si acoperire -> registru de interventii -> harta cu pinuri.

## Modificari de testat

### 10.10.2026 - Detaliile punctului de pe harta, in fereastra popup

**Ce s-a adaugat:** detaliile unui punct (beneficiar, contract, termen, ciclicitate, scadenta, ultima interventie, butoanele) se deschid intr-o fereastra popup, nu in panoul din dreapta; harta ocupa toata latimea.

**Ce face acum:** apasarea unui pin deschide popup-ul centrat; se inchide cu ×, cu clic pe fundal sau cu Escape; „Alte puncte la aceeasi locatie" schimba continutul popup-ului. Insignele (On, In curand etc.) au padding mai mare (regula 6 px / 12 px, aplicata la toate insignele aplicatiei).

1. Deschide Harta mentenantei si apasa un pin: apare popup-ul cu detaliile; harta ramane in spate, pe toata latimea.
2. Inchide-l cu ×, cu clic pe fundal, apoi cu Escape.
3. La un punct cu alte puncte la aceeasi locatie, apasa unul din lista: popup-ul trece la acel punct.
4. „Fisa beneficiarului" si „+ Inregistreaza interventie" duc la paginile corecte.
5. Ecran ingust: popup-ul incape fara depasire orizontala.

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
