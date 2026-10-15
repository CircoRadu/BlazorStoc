# Inventar (/inventar, /inventar/preluare; restaurarea a trecut in Setari -> Backup)

Fluxul: Generare situatie inventar (PDF cu categoriile alese) -> tiparesti, completezi de mana -> Preluare inventar (scanare/PDF) -> verifici cifrele citite -> se genereaza miscari de corectie. "Restaureaza stoc" (administrator) readuce stocul la o stare anterioara.

## Modificari de testat

### 15.10.2026 - Inventarul ca modul (oprire fara efect asupra restului aplicatiei)

**Ce s-a adaugat:** Inventarul (generarea situatiei de inventar si preluarea foii numarate) este un modul al aplicatiei, ca Analizele de risc, Vehiculele, Mentenanta si Oferte. Daca se opreste (din configurare sau dupa erori repetate), restul aplicatiei merge.

**Ce face acum:** cu modulul oprit, /inventar si /inventar/preluare arata "Modul indisponibil"; sectiunea Inventar din meniu si cele doua carduri de pe pagina principala dispar. Un refuz obisnuit (nimic selectat, produs lipsa) da mesajul de pana acum si nu opreste modulul. Notele de consum si situatia proiectului se genereaza la fel ca inainte (folosesc acelasi font PDF).

Pasi:

1. Cu modulul pornit (normal): Inventar -> Generare situatie si Preluare inventar se deschid ca pana acum; genereaza un PDF si verifica diacriticele.
2. Porneste aplicatia cu `Modules__inventar__Enabled=false`: /inventar si /inventar/preluare arata "Modul indisponibil", meniul si pagina principala nu mai au Inventar, Setari -> Module arata Inventar "Oprit (oprit din configurare)".
3. Tot cu modulul oprit: genereaza o nota de consum (Export consum) si situatia unui proiect in PDF: se genereaza normal, cu diacritice.

### 15.10.2026 - Citirea inventarului scanat ruleaza intr-un proces separat

**Ce s-a adaugat:** recunoasterea (OCR) fisierului scanat de la Preluare inventar ruleaza acum intr-un proces worker separat, nu in aplicatia web.

**Ce face acum:** incarcarea unui inventar scanat da aceleasi randuri ca inainte. Daca procesul de recunoastere se opreste sau depaseste timpul (5 minute), pagina arata "Citirea fisierului a esuat: procesul de recunoastere s-a oprit..." iar aplicatia continua sa mearga; dupa 3 esecuri la rand pagina spune "Recunoasterea documentelor este indisponibila momentan" pentru 30 de secunde, apoi merge din nou fara repornire.

Pasi:

1. Pe Inventar -> Preluare inventar, incarca un inventar scanat: se citesc randurile ca inainte (cod citit, valoare reala).
2. Incarca un fisier care nu e PDF: apare mesajul obisnuit al paginii (nu mesajul de proces oprit).
3. In `/health/modules` apare modulul `ocr` cu starea `healthy`.


### 07.10.2026 - Produsele negative primele

**Ce s-a adaugat:** in situatia de inventar, in fiecare subcategorie, produsele cu stoc negativ apar primele (marcate), ca sa fie numarate si regularizate intai; legatura cu tabul „De regularizat” de pe pagina Produse (vezi 01 si 02).

**Ce face acum:** Inventarul listeaza intai produsele cu stoc negativ, ca sa fie numarate si regularizate primele.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Genereaza situatia pentru o subcategorie cu un produs cu stoc negativ | Produsul negativ este primul, marcat; celelalte alfabetic | |
| 2 | La preluarea numaratorii reale pentru un produs negativ (neverificat inca) | Stocul se aduce la cantitatea numarata prin corectia existenta; produsul iese din "De regularizat" | |

### 06.10.2026 - Selectie pe randuri

**Ce s-a adaugat:** selectia pe randuri (marcaj rotund, clic pe rand, „Selecteaza tot”) in locul casetelor de bifat, la Inventar si Preluare inventar.

**Ce face acum:** Randurile din inventar se selecteaza cu marcajul rotund sau cu clic pe rand, iar „Selecteaza tot" din antet bifeaza toate.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 2 | Preluare inventar: selectie pe randuri | La fel | |

### Verificari ramase din etapele anterioare (vezi docs/TESTE_RAMASE.md grupa H)

**Ce s-a adaugat:** Verificari ramase din etapele anterioare ale inventarului (scaner, rezolutie, scris de mana).

**Ce face acum:** Preluarea inventarului citeste cifrele corect sau le marcheaza de verificat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Alt scaner/rezolutie, alt scris de mana | Cifrele citite corect sau marcate de verificat | |
| 2 | Tiparire fizica a situatiei si deschidere in alt cititor PDF | Aspect corect | |
| 3 | Miscarile generate: iesirile sunt "peste stoc" cand e cazul, nu blocheaza | Jurnal cu actiuni exacte | |

## Fluxul de baza

1. Genereaza situatia pentru o categorie -> PDF.
2. Completeaza -> scaneaza -> Preluare inventar.
3. Corecteaza cifrele -> confirma -> stocul se ajusteaza, jurnalul arata corectiile.
