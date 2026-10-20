# Propunere: tipuri de utilizatori cu privilegii (Setari, doar administrator)

## 1. Situatia actuala (analizata in cod)

- Doua roluri fixe in `Services/AccessControl.cs`: `Administrator` si `Utilizator` (`AccessRoles.All`). Rolul e o coloana text pe utilizator (`WebUsers.cs`, `MariaUserRepository`), pus ca rol in cookie de autentificare (8h, fara sliding).
- Autorizarea e grosiera:
  - `FallbackPolicy` = orice utilizator autentificat vede tot.
  - Doar Audit, AuditFull, SystemTypes, Users, UserDetail sunt `[Authorize(Roles = Administrator)]`; Settings = Administrator + Utilizator.
  - Meniul (`MainLayout.razor`) ascunde doar: Utilizatori, Nomenclator, Jurnal activitate (`AuthorizeView Roles`).
  - `IAccessControl` are doar `IsAdministrator`, `CanManageProducts`, `CanManageBeneficiaries` (ultimele doua = orice autentificat).
- Toate celelalte module (Produse, Inventar, Beneficiari, Furnizori, Facturi, Vehicule, Oferte, Mentenanta, Analize de risc, Notificari) sunt deschise oricarui utilizator logat, cu toate operatiile (adaugare/editare/stergere/export).
- Endpoint-urile (`/api/consum.csv`, `/api/proiecte/.../situatie.*`, `/media/*`, bon PDF) cer doar autentificare, deci ascunderea din meniu nu ajunge: trebuie impusa si server-side.
- Garda existenta utila: nu se poate dezactiva/retrograda ultimul administrator activ (`MariaUserRepository`). Se pastreaza.

## 2. Modelul propus

**Tip de utilizator (profil de acces)** = nume + descriere + lista de permisiuni. Administratorul le defineste in Setari; fiecare utilizator are exact un tip.

- `Administrator` ramane tip de sistem, **nemodificabil si nesters**, cu toate permisiunile (inclusiv gestiunea tipurilor si utilizatorilor, Jurnal, Nomenclator, Backup/Restore). Evita blocarea din greseala.
- `Utilizator` (existent) devine tip implicit editabil, initial cu setul curent de drepturi (deci nicio schimbare de comportament la migrare).
- Tipuri noi create liber (ex: Magaziner, Tehnician mentenanta, Ofertant, Consultant/doar citire).

### Permisiuni pe doua niveluri

1. **Acces la modul** (vede in meniu si poate deschide pagina): Produse/Stoc, Preluare factura, Iesire multipla, Export consum, Inventar, Categorii, Beneficiari, Furnizori, Facturi, Vehicule, Oferte, Mentenanta, Analize de risc, Notificari, Setari (sectiuni), plus module admin: Utilizatori, Nomenclator, Jurnal.
2. **Actiuni in modul** (matrice modul x actiune), actiuni standard:
   - `Vizualizare` (implicit daca modulul e acordat)
   - `Adaugare`
   - `Editare`
   - `Stergere`
   - `Export` (CSV/PDF/bon)
   - `Operatii speciale` per modul, unde exista: ex. Produse: iesiri/ajustari stoc; Inventar: finalizare; Oferte: sabloane; Mentenanta: activare contracte; Facturi: preluare/validare.

Reguli de consistenta: `Adaugare/Editare/Stergere/Export` implica `Vizualizare`; fara acces la modul nu exista actiuni; dependente intre module (ex. Preluare factura cere Furnizori/Produse in citire) se afiseaza ca avertisment la salvare, nu se impun in tacere.

### Chei de permisiune
Sir stabil `modul.actiune`, ex. `produse.view`, `produse.edit`, `furnizori.delete`, `oferte.export`, `setari.sectiune.facturi`. Lista se defineste intr-un singur loc (catalog in cod), iar UI-ul de configurare se genereaza din catalog, ca un modul nou sa apara automat in matrice.

## 3. Date (MariaDB)

- `tip_utilizator(id, nume UNIQUE, descriere, este_sistem, creat_la)`
- `tip_utilizator_permisiune(id_tip, cheie)` (PK compus; prezenta randului = permis)
- `user.id_tip` (FK, NOT NULL). Migrare: `Administrator` -> tipul sistem Administrator, `Utilizator` -> tipul `Utilizator` cu permisiunile curente; coloana text veche `Role` ramane doar pentru compatibilitate pana la eliminare.
- Migrare aplicata direct pe DB local dev (fara aprobare, conform regulii existente).

## 4. Aplicare in cod

- La login: claim `role` = numele tipului (compatibil cu `Roles="Administrator"` existent) + claim-uri `perm` cu cheile permise. Alternativ (recomandat): claim doar cu `id_tip` si permisiunile citite dintr-un serviciu cu cache, ca modificarea unui tip sa se aplice **imediat**, nu abia dupa re-login (cookie-ul dureaza 8h).
- Politici dinamice: `IAuthorizationPolicyProvider` care creeaza politica la cerere din cheie (`[Authorize(Policy = "produse.edit")]`), plus `<AuthorizeView Policy="...">` in meniu si butoane.
- `IAccessControl` extins: `HasAsync(string key)` si `EnsureAsync(string key)`; `CanManageProducts/Beneficiaries` raman ca wrapper peste chei noi.
- **Obligatoriu server-side**: verificare in servicii/repository-uri la fiecare operatie de scriere si in toate endpoint-urile minimal API (`/api/*`, `/media/*`), nu doar ascundere in UI. Pagina fara drept -> redirect la pagina "Acces refuzat" cu mesaj clar; actiune fara drept -> `AccessDeniedException` (deja exista) afisata prietenos.
- Protectii: ultimul administrator nu poate fi mutat pe alt tip; tipul Administrator nu se editeaza/sterge; un tip cu utilizatori asignati nu se sterge (se cere reasignare); administratorul nu isi poate scoate singur accesul la gestiunea tipurilor.

## 5. Interfata (Setari > Tipuri de utilizatori, vizibil doar administratorului)

- Lista tipurilor (nume, descriere, nr. utilizatori, badge "sistem").
- Editor tip: nume, descriere, apoi matrice cu module pe randuri si actiuni pe coloane, **ToggleSwitch On/Off** (fara checkbox-uri, conform regulii), grupare pe sectiuni ca in meniu, comutator "acces la modul" care activeaza/dezactiveaza randul, butoane "Acorda tot / Revoca tot" pe modul, buton "Copiaza din alt tip".
- Previzualizare "Meniul vazut de acest tip".
- In `UserEditor` campul "Nivel de acces" devine selector pe tipurile definite; in `Users`/`UserDetail` badge cu numele tipului.
- Operatiile pe rand in tabele: doar icoane sau doar butoane (regula existenta), toggle-uri permise.

## 6. Audit (actiuni specifice, conform regulii)

Evenimente separate in jurnal: `Tip utilizator creat`, `Tip utilizator redenumit`, `Permisiune acordata (cheie)` / `Permisiune revocata (cheie)` cu valoare veche/noua, `Tip utilizator sters`, `Utilizator mutat din tipul X in tipul Y`, plus `Acces refuzat` (utilizator, cheie, ruta) pentru incercari blocate. Rolul din `Archiving`, `AuditTrail`, `DatabaseBackup/Restore`, `RepositoryAudit` (acum `Administrator`/`Utilizator`) se inlocuieste cu numele tipului.

## 7. Etape recomandate

1. Catalog de permisiuni + tabele + migrare (comportament identic cu azi).
2. `IAccessControl.Has/Ensure` + provider de politici + claim-uri; inlocuire `Roles=` existente.
3. Protejare server-side: servicii de scriere si endpoint-uri.
4. UI Setari (lista + matrice) si integrare in editorul de utilizatori.
5. Meniu si butoane conditionate de permisiuni; pagina "Acces refuzat".
6. Audit pe actiunile noi; teste tintite (3-5 cai critice: ultimul admin, tip sistem, actiune fara drept, aplicare imediata, migrare); fisier "Teste utilizator".

## 8. Decizii luate (10.10.2026)

- Un singur tip per utilizator.
- Granularitate: modul x (vizualizare/adaugare/editare/stergere/export) + operatii speciale.
- Modificarile de drepturi se aplica imediat (claim minim + cache de permisiuni).
- Sectiunile din Setari au permisiuni separate (ex. `setari.facturi`, `setari.backup`).
- Datele pe inregistrare (domeniu de date) raman in afara scopului.
