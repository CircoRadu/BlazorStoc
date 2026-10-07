# Vehicule (/vehicule, /vehicule/{id}, /vehicule/{id}/echipamente)

Fluxul: Administrare -> Vehicule -> pagina vehiculului (taburi: date, expirari ITP/asigurare/rovinieta, echipamente din masina). Produsele ajung in masina prin iesire spre vehicul si se intorc prin restituire sau mutare.

## Modificari de testat

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
