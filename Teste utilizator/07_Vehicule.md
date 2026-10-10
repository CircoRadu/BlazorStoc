# Vehicule (/vehicule, /vehicule/{id}, /vehicule/{id}/echipamente)

Fluxul: Administrare -> Vehicule -> pagina vehiculului (taburi: date, expirari ITP/asigurare/rovinieta, echipamente din masina). Produsele ajung in masina prin iesire spre vehicul si se intorc prin restituire sau mutare.

## Modificari de testat

### 08.10.2026 - Nivel tinta pe vehicul si „Completeaza la nivel"

**Ce s-a adaugat:** pe pagina „Materiale si echipamente" a unei masini, sectiunea „Nivel tinta": pentru fiecare produs se seteaza cate bucati ar trebui sa aiba masina, plus butonul „Completeaza la nivel" (migrarea 28, tabelul `vehicle_target_levels`).

**Ce face acum:** tabelul arata nivelul tinta, cate bucati are masina si cate lipsesc. „Completeaza la nivel" deschide „Iesire multipla" cu destinatia masina si cate o linie pentru fiecare produs care lipseste (cantitatea lipsa), descrierea „Completare la nivel"; tu verifici si salvezi (aceleasi reguli ca la orice iesire: peste stoc, rezervari, bon PDF). Nivelul nu schimba stocul singur.

1. Schimba nivelul aceluiasi produs: valoarea se inlocuieste (nu apare un al doilea rand).
2. Apasa „Completeaza la nivel": se deschide iesirea multipla cu masina si liniile pregatite; salveaza-o, apoi revino pe pagina masinii: „Lipseste" este 0 si butonul dispare.

**Detalii:**

- Setare: se alege produsul si nivelul dorit; la acelasi produs valoarea se inlocuieste.
- Tabel: arata nivelul tinta, cate bucati are masina si cate lipsesc, cu × pentru eliminare.
- Completare: „Completeaza la nivel" apare doar cand lipseste ceva si deschide „Iesire multipla" cu masina si cate o linie pentru fiecare produs lipsa; utilizatorul verifica si salveaza (aceleasi reguli ca la orice iesire), iar stocul nu se schimba inainte de salvare.
- Jurnal: „Setare nivel tinta vehicul" si „Eliminare nivel tinta vehicul", cu valoarea veche si noua.
- Schema: migrarea 28 (`vehicle_target_levels`), aplicata pe `BlazorStoc`; pe `blazorstoc_test` tabela a fost creata direct (contul migrator nu are drepturi acolo).
- Cod: `Services/VehicleTargets.cs`, `Services/MariaVehicleTargetRepository.cs`, `Components/Pages/VehicleEquipmentPage.razor`, `ExitOperation.razor` (parametrul `?vehicul=`); test in `MariaExtendedChecks` („Vehicle target levels").
- Jurnalul si tabelul sunt incluse in arhivare (`MariaArchiveSchema.MigratedTables`).

### 07.10.2026 - Operatii si reimprospatare

**Ce s-a adaugat:** transferurile si restituirile de pe pagina vehiculului fac parte acum dintr-o operatie (id comun, vizibil in jurnal), pot fi stornate cu motiv din pagina produsului (refuzat daca masina ar ramane negativa) si au bon; pagina vehiculului se reimprospateaza fara licarire. Pasii: 02_Intrari_si_iesiri_produs.md.

**Ce face acum:** Operatiile pe vehicul (transfer, restituire, folosire) se reimprospateaza singure, fara licarire.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Mutare/restituire din pagina vehiculului, apoi /jurnal | Evenimentul contine „Operație: #id” | |
| 2 | Pagina vehiculului deschisa 1-2 minute | Fara licarire | |

### 07.10.2026 - Stoc din masina

**Ce s-a adaugat:** Stocul tinut intr-o masina se vede pe pagina vehiculului.

**Ce face acum:** O iesire spre vehicul muta produsul in masina (stocul total nu se schimba), iar pagina vehiculului il listeaza cu cantitatea.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Iesire spre vehicul a unui produs (4 buc.) | Pagina vehiculului listeaza produsul cu 4 | |
| 2 | Iesire din masina spre beneficiar peste cantitate (de ex. 7) | Se inregistreaza, marcata "peste stoc"; masina nu mai listeaza produsul (cantitate negativa nu se arata) | |
| 3 | Restituire / mutare in alta masina mai mult decat are | Refuzat cu mesaj | |
| 4 | Dupa ce o masina e negativa, muta alt produs din ea | Nu e blocat de depasirea veche | |

### Verificari anterioare

**Ce s-a adaugat:** Verificari anterioare ale paginii vehiculelor.

**Ce face acum:** Taburile vehiculului si datele de expirare functioneaza, iar notificarile de expirare apar la timp.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Taburile vehiculului si datele de expirare | Notificarile de expirare apar la timp (vezi 09) | |
| 2 | Numar de inmatriculare duplicat | Mesaj de eroare | |
| 3 | Stergere vehicul cu miscari | Refuzata/arhivata cu motiv | |

## Fluxul de baza

1. Vehicul nou -> expirari -> salvare.
2. Intrare in depozit -> iesire spre vehicul -> pagina vehiculului -> restituire.
