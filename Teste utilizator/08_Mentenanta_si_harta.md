# Mentenanta, contracte, interventii si harta (/mentenanta, /mentenanta/harta)

Fluxul: Mentenanta -> puncte de lucru (adresa, poze, coordonate) -> contracte de mentenanta si acoperire -> registru de interventii -> harta cu pinuri.

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
