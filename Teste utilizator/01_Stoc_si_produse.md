# Stoc, produse si categorii (/produse, /categorii)

Fluxul: meniul Stoc -> lista de produse (cautare, categorii in stanga) -> adaugi/editezi un produs -> imagine, cod, categorie -> sterge cu motiv. Categoriile si subcategoriile se gestioneaza din Administrare -> Categorii.

## Modificari de testat

### 07.10.2026 - Tabul „De regularizat” si reimprospatare fara licarire

**Ce s-a adaugat:** pe pagina Produse, tab „De regularizat” (produsele cu stoc negativ dupa iesiri peste stoc, cu cauza, vechime, filtre si actiunea Regularizeaza; eticheta tabului arata numarul) si butonul „Ieșire multiplă” (vezi 02). Reimprospatarea automata a paginii nu mai arata spinner si nu mai licareste. Pasii: 02_Intrari_si_iesiri_produs.md (pasii 5-13 din sectiunea cauzei).

**Ce face acum:** Produsele cu stoc negativ apar intr-o lista „De regularizat" cu cauza si vechime, iar paginile se actualizeaza singure fara sa palpaie.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Produse, tabul „De regularizat” | Lista descrisa in 02; revenirea la „Catalog” pastreaza cautarea | |
| 2 | Lasa pagina Produse deschisa 1-2 minute | Fara licarire la reimprospatarea automata | |

### 06.10.2026 - Categorii: anulare dupa mutarea unei subcategorii

**Ce s-a adaugat:** La editarea unei subcategorii, categoria se alege dintr-un grup de optiuni afisat direct in formular, iar mutarea poate fi anulata.

**Ce face acum:** Mutarea unei subcategorii in alta categorie se face dintr-un grup de optiuni vizibil si se poate anula dupa mutare.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Administrare -> Categorii: editeaza o subcategorie si muta-o in alta categorie (categoria se alege dintr-un grup de optiuni, nu dintr-o lista care se deschide) | Optiunile sunt afisate direct in formular | |
| 2 | Apasa "Anuleaza", apoi "Paraseste editarea fara salvare" | Avertizarea apare deasupra; dupa "Paraseste" formularul se inchide | |

### 05.10.2026 - Formularul de produs

**Ce s-a adaugat:** Cautarea imaginii pe internet dupa cod, direct din formularul de produs (si la editare).

**Ce face acum:** Din formular cauti o imagine dupa cod, o alegi din popup si ea se pune in formular.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Produs nou: "Cauta imaginea pe internet" dupa cod | Popup cu rezultate; alegerea unei imagini o pune in formular (si la editare) | |
| 2 | Creeaza o categorie noua in popup si revino la formular | Categoria apare in lista | |
| 3 | Anuleaza formularul dupa ce ai ales o imagine | Imaginea nu ramane salvata | |

### 07.10.2026 - Stoc in bucati

**Ce s-a adaugat:** Stocul se tine doar in bucati: nu mai exista unitate de masura sau metraje.

**Ce face acum:** Formularul de produs si listele arata cantitati in „buc."; liniile cu metri sau ore din oferte raman in afara stocului.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Formularul de produs si lista | Nu exista unitate de masura; cantitatile sunt in "buc." | |

## Fluxul de baza

1. Adauga produs (nume, cod, categorie/subcategorie) -> apare in lista.
2. Cauta dupa nume si dupa cod.
3. Editeaza cu motiv; Istoric.
4. Stergere: cere motiv; produsul cu miscari nu se pierde (arhivare).
5. Doua taburi pe acelasi produs: blocarea editarii apare ca banner (vezi 11_Utilizatori_si_acces.md).

## Ecran ingust

- Lista de produse si formularul pe latime de telefon, fara scroll orizontal.
