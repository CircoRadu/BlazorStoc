# Starea curentă a proiectului

Actualizat de: **Claude**
Data: **25 septembrie 2026**
Stare ciclu: **Taskurile 0–9 finalizate; nu mai există taskuri active; pregătit pentru predarea către Codex după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari, utilizatori și, nou, proiecte asociate beneficiarilor (cu observații și fișiere), autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse, componenta comună `CollapsibleSection` identificarea produselor prin „Cod produs”, stocul modificabil exclusiv prin mișcări de intrare/ieșire (pagina `/produse/{id}/miscari`) și istoricul mișcărilor.

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude. Următorul ciclu îi este predat lui Codex.

## Ultimele modificări funcționale (ciclul Claude — Task 9)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 9”), deși `nextAgent` era `codex`; ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`. Este ultimul task activ din `TODO.md`.

- **Lock lease** (`Services/ProductLocks.cs`): un rând per produs (`product_locks`, SQLite schema 8; `product_lock`, MariaDB creat la prima folosire) cu utilizator, sesiune (`ChangeOrigin.Id`), obținere, reînnoire, expirare (90 s). Operații atomice și scurte, cu ceasul bazei de date: `AcquireAsync` (reînnoire de aceeași sesiune → preluare de lock expirat → inserare cu cheie primară), `RenewAsync` (heartbeat strict), `ReleaseAsync` (numai proprietarul), `ForceReleaseAsync` (administrator, motiv obligatoriu, jurnal `Deblocare`), `GetAsync`/`GetActiveAsync` (numai lease-uri valide, cu timpul rămas din baza de date). `ChangeNotifyingProductLockRepository` publică evenimentul `BlocareProdus` (feed-ul din Task 8, deci și SignalR) la obținere, eliberare și deblocare forțată.
- **Editare** (`Home.razor`): lock la intrarea în editare (inclusiv `?edit=`; dacă este ocupat, mesaj și revenire în pagina de origine), heartbeat la 30 s cu `blazorStocPing` (nu reînnoiește dacă browserul nu răspunde), eliberare la salvare/anulare/eliminarea paginii, notificare „Blocarea editării a fost pierdută” fără reluare tăcută. Ștergerea unui produs editat de altcineva este refuzată.
- **Consultare** (`ProductMovements.razor`): banner „🔒 Produsul este editat de … din …”, „Editează” dezactivat, buton „Deblochează (administrator)” cu `ForceUnlockDialog`, mesaj „eliberat și poate fi editat acum” la eliberare; reîmprospătare la evenimentul produsului și la 10 s (catalogul: insigna „În editare de …”, 15 s).
- **Jurnal**: acțiune nouă `AuditActions.Unlock` („Deblocare”), în filtrul de operații al jurnalului; `AuditRecorder.RecordUnlockAsync`.
- **Scripturi**: scripturile proprii se încarcă prin `@Assets[...]` (amprentă), fiindcă un `leave-guard.js` vechi din cache nu avea `blazorStocPing`.
- **Verificarea versiunii** produsului rămâne protecția finală. Autentificarea și regulile de acces nu au fost modificate.
- **Validare**: build Release 0 avertismente; `BlazorStoc.Checks` 388 (27 noi); browser pe 5082 cu două sesiuni (banner, eliberare live, deblocare forțată cu jurnal, heartbeat, pierdere fără reluare, expirare după închiderea tab-ului). Neverificat: MariaDB real, un al doilea cont autentificat separat.

## Ultimele modificări funcționale (ciclul Claude — Task 8)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 8”), deși `nextAgent` era `codex`; ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`. Utilizatorul a confirmat că butonul de deconectare din Task 7 funcționează.

- **Trigger-e și tabel** (`Services/ChangeEvents.cs`): `change_events` + trigger-e `AFTER INSERT/UPDATE/DELETE` generate din `ChangeEventTriggers` pentru produse, mișcări (raportate ca editare a produsului), utilizatori, proiecte, observații și fișiere. SQLite: create la finalul `SqliteLocalStore.InitializeAsync` (schema versiunea 7). MariaDB: `MariaChangeEventSource.EnsureAsync` creează tabelul și trigger-ele lipsă (`produs`, `io`, `web_user`, `project`, `project_observation`, `project_observation_file`), reverificat la 5 minute; **netestat pe un server MariaDB real**, contul aplicației are nevoie de CREATE/TRIGGER.
- **Relay** (`ChangeEventRelay`, `BackgroundService`): polling la 1 s (`Sync:PollMilliseconds`), grație 1,5 s (`Sync:GraceMilliseconds`) numărată de la prima citire a evenimentului, cursor numai înainte, pornire după ultimul eveniment, retenție 24 h, avertisment jurnalizat cel mult o dată pe minut la erori. `InProcessChangeFeed` implementează `ILocalChangeLedger`: o modificare publicată direct de o sesiune (cu origine) consumă copia ei din trigger (30 s), deci nu apare notificare la propria sesiune. Modificările în cascadă sau externe se anunță fără origine.
- **SignalR**: `ChangesHub` (`[Authorize]`, `/hubs/changes`) și `SignalRChangeBroadcaster`; mesajul `changed` are numai identificatori. Paginile Blazor rulează în același proces și se abonează direct la feed.
- **Pagini**: `LiveRefresh` (debounce 300 ms, notificare când un formular este deschis, rezervă periodică 60 s) + `LiveChangeNotice.razor` în `Home` (catalog), `ProductMovements`, `Users` (păstrează sincronizarea de 15 s) și `UserDetail`; paginile beneficiar/proiect/observație folosesc deja feed-ul (Task 2) și primesc acum și evenimentele externe.
- **Ce nu s-a schimbat**: verificarea versiunii la salvare, autentificarea și regulile de acces; schema nu are fișier SQL separat (trigger-ele sunt gestionate de aplicație).
- **Validare**: build Release 0 avertismente; `BlazorStoc.Checks` 361 (22 noi, inclusiv un bug prins de teste: stilurile `DateTimeStyles` ale cititorului SQLite și marcarea sosirii tuturor evenimentelor unui lot); browser pe 5082 cu două tab-uri, modificări SQL externe, dialog deschis, client SignalR real. Datele de test au fost readuse (mișcarea arhivată, numele produsului readus). Neverificat: MariaDB real, două calculatoare.

## Ultimele modificări funcționale (ciclul Claude — Task 7)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 7”), deși `nextAgent` era `codex`; ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`.

- `MainLayout.razor`: „Deconectare” este acum un buton (`data-logout-open`) care deschide un `<dialog>` modal („Închizi sesiunea?”) cu butonul roșu „Deconectare” (submit `POST /Account/Logout`, cu `<AntiforgeryToken />`) și butonul verde „Anulează deconectarea” (`data-logout-cancel`, focus inițial). `wwwroot/logout-dialog.js` (încărcat din `App.razor`) deschide/închide dialogul; Escape îl închide nativ.
- `Pages/Account/Logout.cshtml.cs`: `OnGet` redirecționează la `/` (pagina veche de confirmare nu mai este afișată); `OnPostAsync` neschimbat (încheie sesiunea, jurnalizează deconectarea, redirecționează la autentificare).
- Autentificarea și regulile de acces nu au fost modificate. Validare: build Release 0 avertismente; `BlazorStoc.Checks` 339; browser pe 5082 (popup, anulare cu starea paginii păstrată, token antiforgery prezent). Neverificat manual: confirmarea efectivă a deconectării.

## Revenirea în pagina de origine după editarea/ștergerea produsului

- La cererea utilizatorului: „Editează” și „Șterge produs” din pagina produsului trimit `&inapoi=<adresa paginii>` către `/produse`; `Home.razor` (`ReturnQuery`, `OriginOrCatalog`, `ClearDeleteQuery(returnToOrigin)`) readuce utilizatorul acolo la închiderea/anularea editării, la salvarea unei editări și la anularea ștergerii. După o ștergere efectivă se merge în catalog (produsul nu mai există). `ReturnNavigation.Safe` (`Services/ReturnNavigation.cs`, testată) acceptă numai căi locale.
- Validare: build Release 0 avertismente; `BlazorStoc.Checks` 339; browser pe 5082 (editare și ștergere anulate din pagina produsului). Neverificat manual: salvarea și ștergerea efectivă.

## Ultimele modificări funcționale (ciclul Claude — Task 6)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 6”), deși `nextAgent` era `codex`; ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`.

- **Interceptare în JavaScript**: antetele de categorie din meniul produselor navighează prin `window.location.assign` (reîncărcare completă), deci un hook Blazor `LocationChanging` nu le-ar prinde. `wwwroot/leave-guard.js` (încărcat din `App.razor`) interceptează în faza de captură clicurile pe linkurile `.sidebar a[href^="/produse"]` și pe `summary`-urile categoriilor, oprește evenimentul și apelează `Home.RequestLeave(url, reload)` prin `DotNetObjectReference`. Se activează numai cât timp `Home` are formularul „Adaugă produs” deschis (`OnAfterRenderAsync` sincronizează `blazorStocLeaveGuard.enable/disable`).
- **Popup**: `SaveConfirmationDialog` a primit parametri (`Message`, `ConfirmLabel`, `CancelLabel`, `ConfirmClass`, `CancelClass`); pentru părăsire: „Părăsește adăugarea” (roșu) / „Continuă adăugarea” (verde). Confirmarea închide formularul și apelează `NavigateTo(url, forceLoad: reload)`; anularea nu atinge editorul, deci valorile și imaginea rămân.
- **`ProductMenuSelection.IsSameSelection`** (`Services/LeaveConfirmation.cs`, testată): nu se afișează popup pentru selecția curentă.
- **Validare**: build Release 0 avertismente; `BlazorStoc.Checks` 337 (335 + 2); browser pe 5082 (popup, continuare, părăsire, lipsa popup-ului fără formular, subcategorie + Escape). Neverificat manual: ecran tactil, imagine selectată păstrată după anulare.

## Data mișcării — calendar numai pentru selectare, fără date viitoare

- La cererea utilizatorului: „Data mișcării” se alege numai din calendar (fără tastare), ultima zi este azi, iar serverul respinge date viitoare. Un ciclu anterior a înțeles greșit cerința și a ridicat limita la 2100; a fost anulat.
- `StockMovementRules.Today`, `FutureDateMessage` și parametrul opțional `today` la `Validated` (`Services/StockMovements.cs`); `Components/Shared/PickOnlyDate.razor` (text `readonly` + `input type=date` nativ ascuns pentru calendar; ambele câmpuri de dată din `ProductMovements.razor`) și `wwwroot/date-pick-only.js` (deschide calendarul la clic/Enter/Spațiu/F4).
- Mișcările deja existente cu dată viitoare (dacă există) nu pot fi editate fără a schimba data, deoarece regula se aplică și la editare. Testele cu 2099 au fost mutate pe ziua curentă; 335 de verificări trec.

## Ultimele modificări funcționale (ciclul Claude — Task 5)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 5”), deși `nextAgent` era `codex`; ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`.

- **Componenta** `Components/Shared/SaveConfirmationDialog.razor` (+ `SaveSummary` în `Services/SaveConfirmation.cs`, stiluri `.save-confirmation-dialog`/`.save-summary` în `wwwroot/app.css`): popup modal accesibil cu tabelul Câmp / Valoare actuală / Valoare nouă (doar câmpurile schimbate), notă opțională (corecția de stoc), motivarea și „Confirmă salvarea” / „Anulează”; Escape = anulare, focus pe dialog.
- **Editoare care confirmă** (doar editarea unui obiect existent): `BeneficiaryEditor`, `ProductEditor` (inclusiv imaginea înlocuită), `ProjectEditor`, `ProjectObservationEditor`, `UserEditor` (parola apare doar ca „va fi schimbată”), `ProductGroups` (categorie/subcategorie: `RequestSaveCategoryAsync`/`RequestSaveSubcategoryAsync`) și dialogul de editare din `ProductMovements` (`SaveEditAsync` → `ConfirmEditAsync`). Modelul: `SaveAsync` validează (`Validated`, motivarea) → construiește rezumatul → afișează popup-ul; `PersistAsync` rulează numai după confirmare; refuzul închide doar popup-ul.
- **Creările nu cer confirmare** (produs nou, beneficiar, proiect, observație, utilizator, categorie, subcategorie, mișcare nouă). Adăugarea unei mișcări schimbă stocul, dar este o creare; nu s-a cerut confirmare pentru ea.
- **Regula de server pentru proiecte** (cerută de utilizator după eliminarea câmpului din formular): `ProjectRules.CheckBeneficiaryUnchanged` / `BeneficiaryLockedMessage`, aplicată în `ProjectRules.Edited` și în `SqliteProjectRepository`/`MariaProjectRepository.UpdateAsync` înaintea oricărei scrieri. Testele de mutare (domeniu, feed de modificări) au fost înlocuite cu teste de respingere. Logica de notificare a beneficiarului vechi din `ChangeFeed` a rămas (inofensivă).
- **Validare**: build Release 0 avertismente; `BlazorStoc.Checks` 329 (327 + 2 pentru `SaveSummary`, după 324 la Task 4); browser pe 5082 (popup, anulare, confirmare la beneficiar; numele readus). Neverificat manual: celelalte editoare, Escape în browser, MariaDB pe server real.

## Modificare la cererea utilizatorului — editarea proiectului fără „Beneficiar”

- `Components/Pages/ProjectEditor.razor`: formularul de editare nu mai are câmpul „Beneficiar” (proiectul rămâne legat de beneficiarul de la creare); lista beneficiarilor nu se mai încarcă. La creare, din pagina beneficiarului, beneficiarul fix rămâne afișat dezactivat.
- Blocarea la nivel de server a fost adăugată ulterior, în ciclul Task 5 (`ProjectRules.CheckBeneficiaryUnchanged`).
- Validare: build Release 0 avertismente, `BlazorStoc.Checks` 324 trecute, verificat în browser pe 5082 (formularul de editare al proiectului „Instalare TVCI Barcea”).

## Ultimele modificări funcționale (ciclul Claude — Task 4)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 4”), deși `nextAgent` era `codex`; working tree-ul era curat. Ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json`, apoi `start` normal.

- **Mesaj unic** `BeneficiaryRules.DuplicateCuiMessage(existingName)` (`Services/Beneficiaries.cs`): „Există deja un beneficiar cu acest CUI: «<nume>».”, cu numele așa cum este salvat în baza de date, nu cel introdus în formular; fără nume cunoscut revine la formularea simplă. Folosit în modul demonstrativ, SQLite și MariaDB, la creare și la editare.
- **Editare**: beneficiarul editat este exclus din verificare (`id <> @id`), deci salvarea fără schimbarea CUI-ului nu îl raportează ca duplicat.
- **MariaDB**: verificarea `EnsureUniqueCuiAsync` citește acum și numele proprietarului; o încălcare concurentă a `UX_beneficiar_cui` (eroare 1062 după verificare) este tradusă, după rollback, în același mesaj cu numele existent (`ConcurrentDuplicateCuiAsync`). **Netestat pe un server MariaDB real.**
- **Formular**: `BeneficiaryEditor` păstra deja formularul deschis cu valorile introduse după o respingere; nu a fost modificat.
- **Verificări**: `BlazorStoc.Checks` 324 (5 noi) pentru mesajul exact și numele salvat (demo și SQLite), editarea către un CUI existent (valorile formularului și obiectul rămân neschimbate), editarea fără schimbarea CUI-ului și mesajul fără proprietar.
- **Preview**: procesul de pe 5082 (cont `Alex`) a fost oprit pentru build și repornit din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082`.

## Ultimele modificări funcționale (ciclul Claude — Task 3)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 3”), deși `nextAgent` era `codex`.

- **Registrul de rute al jurnalului**: `AuditNavigation.EditUrl` a devenit `AuditNavigation.TargetUrl(entry, removals)`, cu dicționarul `Routes` (tip entitate → rută) ca singur loc de extindere: `/produse/{id}` (`ProductNavigation`), `/beneficiari/{id}`, `/utilizatori/{id}` (`UserNavigation`), `/proiecte/{id}`, `/observatii/{id}`, `/miscari/{id}`. Doar evenimentele Adăugare/Editare cu `EntityId` valid primesc link; ruta nu depinde de textul `Target`. `RemovalTimes(events)` reține ultima ștergere per obiect; evenimentele mai vechi decât ștergerea rămân text.
- **Pagini de consultare**: `ProductMovements.razor` răspunde acum și la `/produse/{id}` (linkul „Editare produs” a devenit „Editează”, vizibil utilizatorilor autorizați); `BeneficiaryDetail.razor` are „Editează” (`CanManageBeneficiariesAsync`) care deschide `BeneficiaryEditor` în pagină; `UserDetail.razor` (nouă, `/utilizatori/{id}`, rol Administrator plus `EnsureAdministratorAsync`) arată contul și „Editează”; `Users.razor` leagă numele utilizatorului de pagina lui. Deschiderea unei pagini nu deschide niciun editor; nu există lock de editare (Task 9).
- **Contextul jurnalului**: `AuditListState` (în `ListNavigationContext.cs`) — filtrele (`q`, `tip`, `operatie`, `operator`, `data`), `pe-pagina` și `pagina` sunt în adresa `/jurnal`, înlocuită pe loc (`replace: true`), citite doar la deschiderea paginii; linkurile țintei au `data-restore-scroll` și `Audit.razor` restaurează poziția după formatarea orelor locale.
- **Neschimbat intenționat**: declanșatoarele `?edit=`/`?sterge=` ale listelor rămân pentru acțiunile explicite din pagini; nu s-au modificat autentificarea sau regulile de acces.
- **Preview**: procesul de pe 5082 (pornit anterior) a fost oprit pentru build și repornit din `bin\Release
et9.0\BlazorStoc.exe --urls http://127.0.0.1:5082` din contul `Alex`.

## Ultimele modificări funcționale (ciclul Claude — Task 2, subtaskurile 2.3–2.5)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează ce a mai rămas din task 2”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Ordinea a fost schimbată printr-un commit separat al `.collaboration/state.json` (`nextAgent: claude`), apoi `start` normal.

- **2.4 — „Echipamente”** (`Components/Pages/ProjectEquipment.razor`): citește `IStockMovementRepository.GetForProjectAsync` și afișează codul produsului (link către `/produse/{id}/miscari`), cantitatea, data, operatorul și referința („Mișcarea nr. N”, link `/miscari/{id}`); stare goală și eroare cu „Reîncearcă”.
- **2.3 — Context de navigare**: `Services/ListNavigationContext.cs` (`BeneficiaryProjectListState`, `ListNavigationContext`, scoped) și `wwwroot/navigation-context.js`. Filtrul și pagina listei de proiecte sunt în adresa `/beneficiari/{id}?q=…&pagina=…` (înlocuită cu `replace: true`, deci Înapoi din browser le restaurează), iar linkul „înapoi” din `ProjectPage` și redirecționarea după ștergerea proiectului folosesc ultima stare memorată pe sesiune. Poziția de derulare se salvează în `sessionStorage` la clic pe un link `a[data-restore-scroll]` și se restaurează o singură dată (cel mult 30 de minute). Starea din adresă se citește doar la deschiderea paginii, ca să nu suprascrie textul tastat.
- **2.3 — Registrul jurnalului**: `AuditNavigation.EditUrl` leagă observațiile (adăugare/editare) de `/observatii/{id}`; `ProjectObservationRedirect.razor` rezolvă observația și redirecționează către `/proiecte/{projectId}/observatii/{id}`. `ProjectNavigation` (în `Services/Projects.cs`) centralizează rutele.
- **2.5 — Evenimente de modificare** (`Services/ChangeFeed.cs`): `ChangeEvent` (tip, acțiune, id, proiect/observație/beneficiar, `Origin`, moment UTC), `IChangeFeed`/`InProcessChangeFeed` (singleton, abonați izolați: o eroare într-un abonat nu afectează publicarea), `ChangeOrigin` (scoped, identifică sesiunea) și decoratorii `ChangeNotifyingProjectRepository` / `ChangeNotifyingProjectFileStore`, înregistrați în `Program.cs` peste implementările SQLite/MariaDB. Publicarea se face după commit, nu la operații respinse; mutarea unui proiect notifică ambii beneficiari; ștergerea unui fișier nu cunoaște observația (se adresează după id), deci paginile o tratează ca relevantă. `ProjectChanges` decide ce pagină este afectată.
- **Pagini abonate**: `BeneficiaryDetail`, `ProjectPage`, `ProjectObservationPage` reîmprospătează pe loc pentru modificările altor sesiuni (evenimentele propriei sesiuni sunt ignorate). Un formular sau dialog deschis nu este înlocuit: pagina afișează un `info-banner` („…modificat/șters de alt utilizator…”), iar verificarea versiunii rămâne protecția finală. Task 8 trebuie doar să publice în același feed.
- **Lacună reparată**: încărcarea unui fișier într-o observație nu scria nimic în jurnal, deși TODO-ul o dădea drept finalizată. `SqliteProjectFileStore.SaveAsync` scrie acum evenimentul „Adăugare” (`FisierObservatie`) în aceeași tranzacție cu inserarea; `MariaProjectFileStore` (parametru nou `IAuditTrail`) îl scrie după inserare. Detaliile conțin numele fișierului, observația, tipul, dimensiunea și autorul, nu conținutul.
- **Documentație**: `TODO.md` (Task 2 mutat în „Taskuri finalizate”, dependențele actualizate, notă în Task 3 și Task 8), `VALIDARE.md`, `docs/PROJECT_STATE.md`.

## Ultimele modificări funcționale (ciclul Claude — Task 1)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 1”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`, ca în ciclurile anterioare.

- **Pagina** `Components/Pages/ProductMovements.razor` (`/produse/{id}/miscari`): panou „Produs” (categorie, subcategorie, cod, imagine cu mărire, Editare/Șterge produs), formular de mișcare (dată, Intrare/Ieșire, beneficiar/proiect doar la ieșire prin `Components/Shared/RelationPicker.razor`, descriere, cantitate), tabel cu filtru/sortare/paginare, „Total produse în stoc”, dialoguri de editare, ștergere (`DeleteConfirmationDialog`) și istoric (click dreapta sau buton). `StockMovementRedirect.razor` (`/miscari/{id}`) rezolvă evenimentele din jurnal către pagina produsului. Catalogul (`Home.razor`): codul produsului și „↗” navighează către pagină, panoul de detalii a dispărut, iar `?sterge=<id>` deschide dialogul de ștergere existent.
- **Domeniu**: `Services/StockMovements.cs` (`StockMovement`, `StockMovementInput`, `StockMovementRules`, `IStockMovementRepository`, `ProjectStockMovement`). Cantitate 1–100.000, dată 1990–2100 (fără altă limită; poate fi în viitor), descriere obligatorie (max. 500), beneficiar/proiect numai la ieșire, proiect al beneficiarului ales. Stocul nu se blochează la ieșire peste stoc.
- **Persistență SQLite** (`Services/SqliteStockMovementRepository.cs`, schema versiunea 6): `stock_movements` primește `kind`, `movement_date`, `description`, `operator`, `version`, `updated_utc` (migrare prin `EnsureColumn` pentru baze existente), plus `stock_movement_history` și `archive_stock_movements`. Stocul produsului se actualizează atomic (`quantity = quantity + delta`) în aceeași tranzacție serializabilă cu mișcarea, istoricul și auditul, fără a incrementa `products.version`. Migrare unică (marker `stock_movements_baseline`): produsele cu stoc și fără mișcări primesc o mișcare „Stoc initial”.
- **Persistență MariaDB** (`Services/MariaStockMovementRepository.cs`, arhivă schema versiunea 4): mapare pe `io`/`io_history` (`io_tip_actiune` 1 = intrare, `id_beneficiar` 0 = niciunul, `io_data` = `dd-MM-yyyy`), coloane adăugate la prima folosire: `id_project`, `io_versiune`, `io_created_utc`, `io_updated_utc`; `io_numar_bucati` (tinyint) este lărgit la INT. Operatorul mișcării este utilizatorul bazei (`Database:ApplicationUserId`), nu contul web. Ștergerea unui proiect verifică acum `io.id_project`. **Netestat pe un server real.**
- **Arhivare și audit**: entitate nouă `MiscareStoc` (`ArchiveSchemaRegistry`, `ArchiveRequests.StockMovement`, mapări SQLite/MariaDB); istoricul modificărilor se arhivează ca relații `IstoricMiscareStoc`. Audit: adăugare/editare/ștergere cu ținta = codul produsului, `EntityId` = id-ul mișcării, motiv la editare/ștergere; `AuditNavigation.EditUrl` → `/miscari/{id}`.
- **`IProductRepository.GetProductAsync(id)`** adăugat (demo, SQLite, MariaDB).
- **Contract pentru alte taskuri**: `IStockMovementRepository.GetForProjectAsync(projectId)` returnează ieșirile proiectului (id, produs, cod produs, cantitate, dată, operator) — Task 2 / Subtask 2.4 poate citi „Echipamente” din el. Pentru Task 8 contractul de modificare este evenimentul de audit `MiscareStoc`; nu există un canal separat.

## Ultimele modificări funcționale (ciclul Claude — Task 0)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează task 0”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`, ca în ciclul anterior.

- **Stocul nu mai poate fi introdus manual.** `ProductInput.Quantity` a fost eliminat. `CreateAsync` creează produsul cu stoc 0 în toate repository-urile (`DemoProductRepository.Crud.cs`, `SqliteProductRepository.cs`, `MariaProductRepository.Crud.cs`); `UpdateAsync` nu mai scrie `quantity` / `produs_cantitate` și returnează produsul cu stocul curent din bază.
- **Editorul de produs** (`Components/Pages/ProductEditor.razor`): câmpul „Stoc inițial”/„Cantitate” a fost înlocuit cu o notă: la creare „Produsul nou este creat cu stoc 0…”, la editare „Stoc curent: N buc.” (doar afișare).
- **Concurență**: `ProductRules.CheckCurrent` compară produsul fără `Quantity`, deci o mișcare de stoc viitoare (Task 1) nu invalidează o editare deschisă. Consecință: ștergerea verifică acum stocul **curent** din bază (`ProductRules.CheckDelete(current, …)` în SQLite și demo; MariaDB o făcea deja), nu instantaneul formularului.
- **Audit**: `ProductCode.AuditIdentification` și `AuditChanges` nu mai includ „Cantitate”. Instantaneul de arhivă al produsului păstrează cantitatea ca dată istorică. Jurnalul din MariaDB (`log`, JSON înainte/după) serializează în continuare întregul produs, inclusiv cantitatea.
- **Neschimbat intenționat**: produsele existente își păstrează stocul; catalogul, filtrele de stoc și `SqliteLocalStore` (reconcilierea unică a stării vechi, care citește „Cantitate” din audit istoric) nu au fost modificate. Nu s-au generat mișcări și nu s-au modificat date.
- **Preview**: nu rula niciun proces pe 5082; a fost pornit din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082` (cont `Alex`, deci Codex sau Claude îl pot opri fără probleme de acces).
- **Documentație**: `README.md`, `VALIDARE.md`, `TODO.md` (Task 0 mutat în „Taskuri finalizate”; Task 1 = „Intrări și ieșiri pentru un produs existent”, dependențele actualizate).

## Ultimele modificări funcționale (ciclul Claude — Task 2, restul subtaskurilor)

Ciclul a fost pornit la cererea explicită a utilizatorului („implementează ce a mai rămas din task 2”), deși `nextAgent` era `codex`; working tree-ul era curat și nu exista un ciclu Codex deschis. Tranziția `start` a fost aplicată manual în `.collaboration/state.json`.

- **Persistență**: `Services/SqliteProjectRepository.cs`, `Services/SqliteProjectFileStore.cs`, `Services/MariaProjectRepository.cs`, `Services/MariaProjectFileStore.cs`. Schema SQLite (versiunea 5) adaugă `projects`, `project_observations`, `project_observation_files`, `archive_projects`, `archive_project_observations`, `archive_project_observation_files` și `stock_movements.project_id` (migrare pentru baze existente). Schema MariaDB (arhivă versiunea 3) adaugă tabelele echivalente de arhivă; tabelele live `project`/`project_observation`/`project_observation_file` sunt create de aplicație la prima folosire.
- **Arhivare generalizată**: `ArchivePersistence.InsertAsync` acceptă acum o listă de fișiere per operație de arhivă (nu doar unul singur), necesar pentru arhivarea în cascadă a unui proiect cu mai multe observații/fișiere într-o singură operație. Toate apelurile existente (produs, beneficiar, utilizator) au fost adaptate.
- **Interfață**: `Components/Pages/BeneficiaryDetail.razor` (`/beneficiari/{id}`), `ProjectEditor.razor`, `ProjectPage.razor` (`/proiecte/{id}`), `ProjectEquipment.razor` (`/proiecte/{id}/echipamente`, stare goală), `ProjectObservationEditor.razor`, `ProjectObservationPage.razor` (`/proiecte/{projectId}/observatii/{id}`, cu încărcare/descărcare/eliminare fișiere). `Beneficiaries.razor`: numele beneficiarului este acum link către pagina de detaliu.
- **Endpoint fișiere**: `/media/project-files/{fileId}`, autorizat, cu verificare de conținut (semnătură binară, nu doar extensie) și limite de dimensiune/număr.
- **Audit și jurnal**: `AuditEntities.Project`/`ProjectObservation`/`ProjectObservationFile`; `AuditNavigation.EditUrl` leagă evenimentele de proiect de pagina `/proiecte/{id}`.
- **Corecție**: `DeleteConfirmationRules.DefaultReason` genera mereu forma masculină „…nu va mai fi folosit”, greșită pentru subiecte feminine ca „Observația”. Corectat cu acord de gen simplu (subiect terminat în „a” → „folosită”); depistat prin testarea manuală în browser a fluxului de ștergere a observației.
- **Fișiere renumite** pentru a evita coliziunea de nume cu entitățile de domeniu (Blazor generează o clasă cu numele fișierului): `Project.razor` → `ProjectPage.razor`, `ProjectObservation.razor` → `ProjectObservationPage.razor`.

## Decizii și limitări

- Nivelul de autorizare pentru proiecte/observații/fișiere reutilizează `EnsureBeneficiaryOperatorAsync`, consecvent cu regula existentă pentru beneficiari.
- Registrul de rute al jurnalului (`AuditNavigation.Routes`, `TargetUrl`) leagă produsele, beneficiarii, utilizatorii, proiectele, observațiile (`/observatii/{id}`, redirecționare către pagina din proiect) și mișcările de stoc (`/miscari/{id}`); categoriile, subcategoriile și fișierele nu au pagină proprie și rămân text. Un obiect al cărui eveniment de ștergere există în jurnal nu primește link din evenimentele mai vechi.
- Restaurarea contextului listei de proiecte depinde de adresă (filtru, pagină) și de `sessionStorage` (poziția de derulare); la o reîncărcare completă a aplicației, linkul „înapoi” al proiectului revine la lista neafiltrată (starea per sesiune se pierde), dar Înapoi din browser păstrează adresa cu filtrul.
- MariaDB: tabelele proiectelor și ale mișcărilor nu au fost testate pe un server real; ștergerea unui proiect verifică `io.id_project` (Task 1).
- Feed-ul de modificări este în proces (un singur server); notificările între servere diferite sau modificările făcute direct în baza de date vin odată cu Task 8. Ștergerea unui fișier publică un eveniment fără observație (adresare după id), deci paginile de proiect/observație se reîmprospătează la orice ștergere de fișier.
- Încărcarea fișierelor din interfață apelează `IProjectFileStore.SaveAsync` fără verificare de autorizare în magazinul de fișiere (pagina cere autentificare); de verificat la Task 8 dacă se dorește aceeași protecție ca la ștergere.
- Fișierele acceptate: imagini, PDF, Office, text/CSV, verificate prin semnătura conținutului unde există una binară fiabilă; tipurile fără semnătură (Office/CSV/text) cad pe verificarea tipului declarat, dacă acesta e deja pe lista acceptată.

## Validare

- Ciclul anterior (Task 4): build Release 0 avertismente, 0 erori (proiect principal și `BlazorStoc.Checks`); `BlazorStoc.Checks` — 324 verificări trecute (319 + 5 noi). Browser pe 5082 (sesiune autentificată de utilizator, modul demonstrativ): „Adaugă beneficiar” cu CUI 10000003 și nume nou → mesajul „Există deja un beneficiar cu acest CUI: «Servicii Industriale SA».”, formularul rămas deschis cu numele și CUI-ul introduse; nu s-au salvat date. Neverificat manual: editarea în browser, MariaDB pe server real.
- Ciclul anterior (Task 3): build Release 0 avertismente, 0 erori; `BlazorStoc.Checks` — 319 verificări trecute (312 + 7 noi pentru rute, obiecte eliminate și starea jurnalului). Browser pe 5082 (sesiune autentificată de utilizator): filtrul jurnalului în adresă și restaurat prin Înapoi, `/produse/3` fără editor, `/utilizatori/{id}` și `/utilizatori/99999`, „Editează” la beneficiar și utilizator (deschidere și anulare). Neverificat manual: paginarea prin adresă, poziția de derulare în jurnal, utilizatorul limitat, MariaDB pe server real.
- Ciclul curent (Task 2 / 2.3–2.5): build Release 0 avertismente, 0 erori; `BlazorStoc.Checks` — 312 verificări trecute (285 dinainte + 27 noi: evenimente pentru fiecare operație și niciunul pentru operații respinse, identificatori fără date sensibile, abonați care eșuează, filtrarea pe pagini, auditul încărcării fișierelor, rutele observațiilor, starea listei de proiecte). Browser, `http://127.0.0.1:5082`, două tab-uri, sesiune autentificată de utilizator: filtrul în adresă fără pierderea caracterelor tastate, linkul „înapoi” cu filtrul, Înapoi din browser cu filtrul și poziția de derulare (y=120) restaurate o singură dată, „Echipamente” goală și cu o ieșire asociată, ieșire cu proiect din formularul mișcărilor, linkul observației din jurnal (`/observatii/3` → pagina proiectului) și `/observatii/99999` („Observația nu mai există”), reîmprospătarea live a listei de proiecte și a observațiilor din alt tab, notificarea peste un formular de editare deschis (textul local a rămas). Datele de test (proiect, observații, ieșirea de stoc) au fost șterse prin fluxul normal (arhivate); stocul produsului 1 a revenit la 12. Neverificat manual: paginarea listei de proiecte (peste 10) și reîmprospătarea paginii observației la ștergerea proiectului de către altă sesiune (acoperite de verificările de filtrare); MariaDB neverificat pe server real. Preview repornit din contul `Alex`.
- Build Release: 0 avertismente, 0 erori (proiect principal și `BlazorStoc.Checks`).
- Task 1: `BlazorStoc.Checks` — 285 de verificări trecute (234 dinainte; 51 noi: domeniu, stoc atomic cu 8 adăugări simultane, editare/ștergere/istoric, arhivare, audit, ordonare/filtrare/paginare, `GetForProject`, migrarea SQLite, baseline). Browser (5082, desktop, 375 și 768 px): adăugare, ieșire cu beneficiar, editare cu motiv, `[*]`, istoric prin click dreapta, ștergere în doi pași, jurnal, `/miscari/<id>`, `/produse?sterge=<id>`. Neexersate manual: ieșire cu proiect, filtru/sortare/paginare în browser. MariaDB neverificat pe un server real. Preview-ul a fost oprit și repornit de mine (cont `Alex`) pentru fiecare build; datele demo au primit mișcări (baseline „Stoc initial” pentru produsele cu stoc; o mișcare de test a fost adăugată și ștearsă în arhivă).
- `BlazorStoc.Checks`: 234 de verificări trecute (229 dinainte de Task 0; +5 net: produs nou cu stoc 0, editare care păstrează stocul, diferență de stoc în instantaneu, `CheckCurrent`, comportamentul SQLite; eliminată verificarea „stoc inițial negativ”).
- Task 0, browser (`http://127.0.0.1:5082`, sesiune existentă): formularul de creare fără „Stoc inițial”, editorul cu „Stoc curent: 120 buc.” fără câmp de introducere. Salvarea editării și jurnalul nu au fost exersate manual (nu s-au modificat datele demo); sunt acoperite de verificările automate. MariaDB neverificat pe un server real.
- Task 2 (istoric): 176 de verificări dinaintea lui + 29 pentru modelul de domeniu al proiectelor + 24 pentru persistență/pagini/arhivare/corecția de gramatică.
- Browser, `http://127.0.0.1:5082` (admin demo, sesiune existentă): creare proiect din pagina beneficiarului, pagina proiectului cu „Echipamente” primul, adăugare observație cu navigare automată, ștergere observație și ștergere proiect (ambele cu dialogul de confirmare în doi pași), verificare jurnal (evenimente „Proiect”/„Observatie” cu `Details`/`Motif` corecte, fără diacritice în motiv, conform regulii de stocare). Datele de test au fost curățate (proiectul creat pentru verificare a fost șters).
- Preview-ul a fost repornit din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082` de către Claude, care a putut opri de această dată procesul anterior (proprietar același cont local, nu contul sandbox Codex menționat în ciclurile trecute).

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse, beneficiari și proiecte, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Codex**. Taskurile 0–9 sunt complete; `TODO.md` nu mai are taskuri active. Următorul pas îl stabilește utilizatorul (taskuri noi, verificare MariaDB pe un server real, teste cu două calculatoare).

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice, inclusiv secțiunile „Cod produs” și „Proiecte”.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
