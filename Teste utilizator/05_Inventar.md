# Inventar (/inventar, /inventar/preluare; restaurarea a trecut in Setari -> Backup)

Fluxul: Generare situatie inventar (PDF cu categoriile alese) -> tiparesti, completezi de mana -> Preluare inventar (scanare/PDF) -> verifici cifrele citite -> se genereaza miscari de corectie. "Restaureaza stoc" (administrator) readuce stocul la o stare anterioara.

## Modificari de testat

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
