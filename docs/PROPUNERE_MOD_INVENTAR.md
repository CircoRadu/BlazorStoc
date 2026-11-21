# Propunere: mod INVENTAR al aplicatiei (11.11.2026)

Stare: propunere completa, cu toate deciziile utilizatorului din 11.11.2026 aplicate (sectiunea 5); neimplementata. Fara diacritice (regula fisierelor .md).

## 1. Ce se cere

Un comutator global "Mod inventar", pornit/oprit de administrator. Cat timp e activ:
- nu se mai pot face intrari, iesiri, retururi, transferuri, stornari, regularizari, preluari de facturi (stocul este inghetat);
- modulele care nu misca produse raman disponibile (beneficiari, proiecte, vehicule, mentenanta, taskuri, notificari, jurnal, backup, setari, utilizatori, ActivityApp, catalogul de produse).

Scopul: cifrele de stoc ramase constante cat se numara marfa; corectiile rezultate din numarare se aplica pe un stoc care nu s-a miscat intre timp.

Atentie la nume: exista deja modulul `inventar` (`Modules/Inventory/`: generare foi de inventar PDF si preluare numaratoare scanata). El ramane; noul concept este "modul de lucru al aplicatiei", numit in interfata "Mod inventar" / "Inventariere in curs". Modulul genereaza listele si aplica corectiile, modul inventar ingheata stocul.

## 2. Ce am verificat in cod

1. Cantitatea unui produs se schimba intr-un singur loc: `MariaStockMovementRepository.ApplyStockAsync` (`UPDATE products SET quantity=quantity+@delta`, `Services/MariaStockMovementRepository.cs`). `MariaProductRepository` creeaza produse cu cantitatea 0 si nu atinge cantitatea la editare (`MariaProductRepository.Crud.cs`). Deci un blocaj pus in depozitul de miscari acopera tot stocul, indiferent de pagina care il cheama.
2. Toate scrierile de stoc trec prin `IStockMovementRepository`: `CreateAsync`, `UpdateAsync`, `DeleteAsync`, `CreateExitOperationAsync`, `VoidExitOperationAsync`, `TransferFromVehicleAsync`, `RegularizeNegativeStockAsync` (+ `AttachToInvoiceAsync`/`DetachFromInvoiceAsync`, care nu schimba cantitatea).
3. Preluarea facturii (`Modules/Invoices/InvoicePickup`) creeaza intrari prin acelasi depozit si inregistreaza factura (`supplier_invoices`, `Modules/Suppliers/MariaSupplierInvoiceRepository`); iesirile din proiecte (`ExitOperation`, `ProjectComponentsSection`) si transferurile din masini (`Modules/Vehicles`, `VehicleTransferDialog`) la fel.
4. Preluarea inventarului (`Modules/Inventory/InventoryPickup.cs`, `InventoryPickupApplier`) aplica corectiile ca miscari "Corectie stoc (inventar) dd.mm.yyyy" prin `movements.CreateAsync`; deci trebuie sa poata scrie CHIAR cand stocul e inghetat.
5. Precedent in cod: `MaintenanceGate` (`Services/MaintenanceGate.cs`) blocheaza scrierile cat tine un backup/restaurare, cu `EnterOwnerScope` pentru operatia proprie, cache 1 s si fisier de lacat; `ModuleControl` + `module_state` (migrarea 42) sunt modelul pentru o pornire/oprire de catre administrator cu motiv obligatoriu, jurnal si permisiune rezervata. Propunerea reia aceste doua modele.
6. Rezervarile (`IReservationRepository`, `Services/Reservations.cs`), legarea intrarilor de factura (`AttachToInvoiceAsync`) si incarcarea ofertelor (`IOfferRepository`, `OfferPickup`) nu schimba cantitatea, dar decizia utilizatorului este sa fie blocate in mod inventar, deci primesc acelasi gate. Legarea iesirilor de componente (`MariaProjectComponentRepository`: `component_settled`, `project_component_id`) ramane permisa (nu e rezervare, nu schimba stocul si nu apare in lista de blocat).

## 3. Proiectare

### 3.1 Starea (nucleu, nu modul)
Starea sta in nucleu (`Services/InventoryMode.cs`), NU in modulul `inventar`: daca modulul se opreste (circuit breaker, oprire manuala) inghetarea trebuie sa ramana valabila.

- Migrare noua (urmatorul numar liber; 88 la data scrierii, de verificat) + `database/mariadb/schema-mariadb.sql`: tabelul `inventory_mode` cu un singur rand: `active`, `started_utc`, `started_by`, `started_role`, `reason`, `ended_utc`, `ended_by`, `end_note`, plus `session_id` (numarul inventarierii).
- `IInventoryMode` (`GetAsync`, `StartAsync(reason)`, `StopAsync(note)`), `InventoryModeGate` static ca `MaintenanceGate` (cache 1-2 s, invalidat la pornire/oprire in acest proces; sursa de adevar = baza, deci merge si cu doua procese).
- Pornire/oprire: numai Administrator (permisiune noua `setari-inventar.toggle`, rezervata ca `setari-module.toggle`), motiv obligatoriu dupa `ChangeReasonRules`, jurnalizare cu actiuni exacte: `Pornire mod inventar`, `Oprire mod inventar` (`AuditActions`, filtrul din Jurnal, `IsCreateOrEdit`, test pe jurnal).
- Unde se vede si se comanda: tab nou Setari -> Inventariere (nucleu, doar administrator) cu starea, de cand, cine, motiv, butonul Porneste/Opreste (confirmare cu motiv) si legatura spre /inventar. Topic `.help` pentru fereastra (regula help-ului).

### 3.2 Aplicarea blocajului (defensiva pe doua straturi)

A. Depozitul (garantia):
- fiecare metoda de scriere de stoc din `MariaStockMovementRepository` cheama `InventoryModeGate.ThrowIfFrozen()` la inceput (mesaj clar, exceptie `StockMovementOperationException` care este deja "raspuns, nu defectiune" pentru circuit breaker);
- in plus, `ApplyStockAsync` (singurul loc care schimba cantitatea) citeste `inventory_mode` in aceeasi tranzactie cu `LOCK IN SHARE MODE`; pornirea modului face `UPDATE` pe randul respectiv (ia lacat exclusiv), deci asteapta operatiile aflate in curs, iar dupa ea nicio scriere nu mai trece: fara cursa intre "am verificat" si "am scris".
- `InventoryModeGate.EnterCorrectionScope()` (ca `EnterOwnerScope`): il intra numai `InventoryPickupApplier` si `RegularizeNegativeStockAsync`. Ambele le face operatorul care preia inventarul, deci in mod inventar cer permisiunea `inventar-preluare.add` (nu administrator). Sub acest scop trece numai miscarea de tip corectie (`Corectie stoc (inventar)` / `Regularizare stoc negativ`); orice alta miscare ramane refuzata chiar si in scop. Regularizarea din `ToRegularizeTab`/`NegativeStockPanel` ramane vizibila in mod inventar pentru cine are permisiunea, iar in afara modului isi pastreaza regulile de azi.
- La fel `InventoryModeGate.ThrowIfFrozen()` in `AttachToInvoiceAsync`/`DetachFromInvoiceAsync`, in scrierile `IReservationRepository` (rezervare, anulare/eliberare) si in scrierile de incarcare a ofertelor din `IOfferRepository` (`OfferPickup`); sabloanele de oferte (`/oferte/sabloane`) raman editabile (nu incarca nicio oferta).
- Preluarea inventarului ar fi permisa DOAR cat modul inventar e activ (corectiile se aplica pe stoc garantat neschimbat de la pornire); in afara modului pagina spune "Porneste modul inventar".

B. Interfata (comoditate, nu garantie):
- banner pe toate paginile, pentru toti utilizatorii: "Mod inventar activ din dd.mm.yyyy hh:mm - intrarile si iesirile sunt oprite" (insula interactiva ca `ModuleAlertBanner`, se actualizeaza singur);
- componenta `InventoryModeGate` (ca `ModuleGate`) in jurul formularelor de miscare: raspunde cu nota si dezactiveaza butoanele, cu `title` cu motivul; puncte: `ProductMovements` (formular intrare/iesire), `ExitOperation` (Iesire multipla), `ProjectComponentsSection` (iesiri), `EntryOriginPicker`, `InvoiceManualEntryDialog`, `EntryInvoiceLinkDialog`/`FreeEntriesTab` (legarea de factura), `VehicleTransferDialog`, `ReserveSuggestions`/butoanele de rezervare din `ProjectComponentsSection`, `OfferPickup` (/oferte/preluare, oprit de la inceput ca `InvoicePickup`), tile-urile Intrare/Iesire de pe `Dashboard`, intrarile de meniu "Iesire multipla" si "Preluare factura" din `MainLayout`;
- `InvoicePickup` se opreste de la inceput (nu dupa ce utilizatorul a incarcat si analizat PDF-ul): pagina arata nota si dezactiveaza incarcarea; `InvoiceViewer` (doar vizualizare) ramane. Modulele nu se refera intre ele (regula `ModuleChecks`): `Invoices`, `Vehicles`, `Suppliers`, `Offers` citesc starea prin `IInventoryMode` din nucleu, ca `IAuditTrail`.
- ce a fost pregatit inainte de pornire nu se pierde: cosul de iesiri si facturile analizate raman in pagina; actiunea de salvare e refuzata cu mesajul modului, iar utilizatorul o reia dupa oprire.

### 3.3 Ce ramane disponibil (nu misca produse)
Catalogul de produse si parametrii (creare produs nou cu cantitatea 0, editare nume/descriere/categorie), grupe, beneficiari, proiecte (fara iesiri si fara rezervari), sabloanele de oferte (fara incarcare de oferte), vehicule (fisa, ITP etc.; fara transferuri de produse), mentenanta, analize de risc, taskuri, notificari, jurnal, backup (vezi decizia d), Setari, utilizatori, ActivityApp, actualizarea aplicatiei, istoricul miscarilor si toate rapoartele (citire), generarea foilor de inventar PDF, export consum.

### 3.4 Interactiuni de urmarit
- Backup: merge normal (pachetul poarta si starea modului). Restaurare (Setari -> Backup, operatie de administrator care inlocuieste TOATA baza cu cea dintr-un pachet vechi, deci anuleaza toate miscarile facute dupa acel pachet): in mod inventar ar putea sterge numaratoarea si corectiile deja aplicate si ar schimba starea modului cu cea din pachet. Decis: restaurarea este refuzata cat modul e activ ("Opreste intai modul inventar"), iar in alt moment ramane ca azi (verificarea in aplicatie inainte de pornirea workerului + in `MariaDatabaseRestoreService`, ca sa tina si pe calea `InProcess`).
- Actualizarea aplicatiei: neatinsa; migrarea schemei creeaza tabelul si porneste cu modul oprit.
- `Modules:<id>:Enabled=false` pentru `inventar` nu opreste inghetarea.
- Notificari: sursa noua optionala "Mod inventar activ de N zile" (uitat pornit); alertele de stoc existente continua.
- Teste vechi care scriu stoc nu sunt afectate: modul este oprit implicit si gate-ul nu e configurat in testele fara el.

## 4. Etape

1. Nucleu: migrare, `IInventoryMode` + `InventoryModeGate`, protectia din `MariaStockMovementRepository` (+ in-tranzactie in `ApplyStockAsync`), `EnterCorrectionScope` in `InventoryPickupApplier`, permisiune, actiuni de jurnal, Setari -> Inventariere. Teste: sectiune MariaDB "Inventory mode" (fiecare metoda de scriere refuzata cu modul pornit, corectia trece, citirile trec, pornirea asteapta o scriere in curs, oprire -> scrierile revin, jurnal, permisiune doar administrator) + verificari pure in `StructureChecks`/grup nou `inventorymode`.
2. Interfata: banner, `InventoryModeGate`, meniu, formulare, `InvoicePickup`; verificare in browser pe instanta de verificare (conturile `claude.verify`, baza de test), inclusiv pagini deschise inainte de pornire.
3. Fluxul inventarului: preluarea doar cat modul e activ, rezumat la oprire (cate corectii s-au aplicat de la pornire, produse cu stoc negativ ramase), eventual raport "stoc la pornire / numarat / corectie".
4. Inchidere: versiuni de modul (`tools\module-version.ps1` pentru `Inventory`, `Invoices`, `Vehicles`, `Suppliers`, `Offers`), changelog, `Teste utilizator/`, `docs/HELP_DE_REGENERAT.md`, `PROJECT_STATE`.

Estimare: etapa 1 si 2 sunt miezul (un depozit central + un set de pagini); etapa 3 poate veni dupa prima folosire reala.

## 5. Decizii de luat (cu varianta propusa)

Decise de utilizator la 11.11.2026:
a) Regularizarea stocului negativ in mod inventar o fac operatorii care preiau inventarul (permisiunea `inventar-preluare.add`), nu doar administratorul.
b) Legarea/dezlegarea intrarilor de o factura: BLOCATA.
c) Rezervarile si incarcarea ofertelor: BLOCATE.
e) Pornirea/oprirea modului: numai administratorul.
f) Raportul de inventariere (stoc la pornire / corectii): etapa 3.
g) Pornire programata si oprire automata: nu.

d) Restaurarea unui backup (Setari -> Backup, administrator) cat modul e activ: REFUZATA ("Opreste intai modul inventar"); decis 11.11.2026.

Toate deciziile sunt luate; propunerea este completa.
