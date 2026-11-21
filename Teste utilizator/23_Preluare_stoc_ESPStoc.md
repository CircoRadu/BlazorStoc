# Preluare stoc ESPStoc (Setari -> Preluare stoc ESPStoc)

Doar administratorul. Modulul `import-espstoc` (0.1.0), documentat in `docs/PROPUNERE_IMPORT_ESPSTOC.md`.

## Modificari de testat

### 11.11.2026 - modulul nou (branch claude/import-espstoc)

**Ce s-a adaugat:** tabul Setari -> Preluare stoc ESPStoc: analiza unui export al bazei vechi ESPStoc (.sql sau .zip), lista pe categorii colapsabile cu insigne, alegerea parametrilor pentru produsele din subcategorii cu parametri, si aplicarea ca punct zero al gestiunii (sterge miscarile, facturile preluate, ofertele, rezervarile, copiile de siguranta si jurnalul; preia stocul din export).

**Ce face acum:**

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Preluare stoc ESPStoc | Sectiunea "1. Exportul bazei vechi" cu alegere de fisier si cale pe server | |
| 2 | Scrie calea `D:\Radu\ESP_Stoc\ESP_Stoc_backup_11 11  2026.zip` si apasa "Analizeaza fisierul de pe server" | Rezumat: 342 produse cu stoc in export (1527 bucati); cateva cu probleme (produse noi in subcategorii cu parametri, produse existente fara parametri) si 23-25 cu parametri propusi | |
| 3 | Deschide o categorie (clic pe rand) | Insigne "N din M", "buc.", "cu probleme", "de verificat"; tabel paginat al produselor | |
| 4 | Filtrul "Doar problemele"; la un produs apasa iconita de parametri | Se deschide editorul: model + valoarea fiecarui parametru (sau "valoare noua"); dupa salvare produsul iese din probleme | |
| 5 | Filtrul "Doar cele de verificat" | Produsele cu parametri propusi din codul vechi ("-2.8mm", "(4TB)", "Fisheye"); poti schimba propunerea sau omite produsul (comutatorul "Preia") | |
| 5b | Apasa insignele din rezumat sau de pe categorii (cu probleme, de verificat, inghetate, omise); pentru produsele fara parametri apasa "Preia inghetate produsele fara parametri" sau, in editorul unui produs, "Preia inghetat, fara parametri" | Insigna deschide direct lista produselor ei ("Filtru activ ... Arata toate produsele"); produsele inghetate se preiau fara parametri, primesc doar cantitatea si raman blocate (nicio miscare) pana le completezi in editorul de produs | |
| 6 | "Arata ce se sterge" | Tabel cu cate randuri se sterg (miscari, facturi, oferte, rezervari), stocul care se pune 0, evenimentele de jurnal si copiile de siguranta de pe server si de pe NAS | |
| 7 | Scrie PUNCT ZERO si apasa "Aplica punctul zero" | Se face pachetul de siguranta, apoi pagina trece scurt prin "Backup sau restaurare in curs" (intretinere) si revine; la final cardul "Punctul zero a fost stabilit" | |
| 8 | Verifica Produse, Jurnal activitate, Setari -> Backup | Produsele au stocul din export (1527 bucati), miscarile sunt doar intrarile "Punct zero", jurnalul contine doar evenimentele punctului zero, a ramas un singur pachet de backup (punctul de pornire) | |
| 9 | Deschide din nou tabul | Nu mai permite alt import ("punct zero deja stabilit") | |

**Atentie:** pasul 7 este ireversibil in aplicatie (se sterg si copiile de siguranta). Foloseste doar baza de test. Baza de test se reface din exportul BStoc cu scripturile agentului.
