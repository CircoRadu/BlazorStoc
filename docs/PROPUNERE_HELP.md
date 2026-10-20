# Propunere: help contextual pentru fiecare fereastra (pagina, tab, popup)

Data analizei: 19 octombrie 2026. Stare: DOAR PROPUNERE, nimic implementat, nimic comis. Fisier scris fara diacritice (regula .md); continutul help-ului din aplicatie va avea diacritice (vezi sectiunea 3).

Cerinta utilizatorului (cuvant cu cuvant): "sectiune help pentru fiecare fereastra care se deschide (inclusiv popup). iconita help in fiecare fereastra. iconita deschide sectiunea aferenta ferestrei in popup. in fereastra de help este descris fiecare element al ferestrei. fereastra are sectiuni colapsable cu iconita + in care se explica fluxul de lucru din fereastra (ex. + Cum preiau o factura etc.)."

## 1. Inventarul ferestrelor existente (din cod)

### 1.1 Pagini rutate (`@page`, in `Components/Pages` si `Modules/*`)

40 de sabloane de ruta, ~36 de componente. Aplicatia nu are un registru de pagini: `Routes.razor` + atributul `[Module]` (doar pentru module) sunt singurele puncte comune.

| Modul / zona | Pagini (ruta) | Taburi / sub-sectiuni in pagina |
|---|---|---|
| Nucleu | `/` Dashboard (module-tile) | - |
| Produse / stoc | `/produse` (Home), `/produse/{id}` si `/produse/{id}/miscari` (ProductMovements), `/categorii`, `/categorii/fereastra` (popout), `/iesiri/multipla` (ExitOperation) | `/produse`: Catalog, De regularizat. Miscari: formular intrare/iesire cu `EntryOriginPicker` (taburi provenienta) |
| Beneficiari / proiecte | `/beneficiari`, `/beneficiari/{id}`, `/proiecte/{id}` | sectiuni: Puncte de lucru, Proiecte; Observatii generale, Continutul proiectului |
| Utilizatori / sistem | `/utilizatori`, `/utilizatori/{id}`, `/nomenclator`, `/setari`, `/Error`, `/intretinere` (asteptare backup) | `/setari`: tab-uri Facturi (sub-taburi Analiza, Sabloane salvate, Editare), ANAF, Notificari, Harta, Backup (sub-taburi Principal, NAS, Ora), Tipuri de utilizatori |
| Inventar (`Modules/Inventory`) | `/inventar`, `/inventar/preluare` | - |
| Furnizori (`Modules/Suppliers`) | `/furnizori`, `/furnizori/{id}`, `/facturi` | `/facturi`: Facturi, Intrari fara factura |
| Preluare factura (`Modules/Invoices`) | `/produse/preluare-factura`, `/produse/preluare-factura/factura/{sessionId}` | wizard cu pasi, taburi de pagini ale facturii |
| Oferte (`Modules/Offers`) | `/oferte` si `/oferte/preluare` (aceeasi componenta), `/oferte/sabloane` | taburi "Pasii preluarii" |
| Vehicule (`Modules/Vehicles`) | `/vehicule`, `/vehicule/{id}`, `/vehicule/{id}/echipamente` | VehicleTabs: Informatii autovehicul, Echipamente |
| Mentenanta (`Modules/Maintenance`) | `/mentenanta`, `/mentenanta/harta`, `/mentenanta/harta/fereastra` (popout) | taburi: Scadente, Scadente pe puncte de lucru |
| Notificari | `/notificari` | Active, Rezolvate |
| Jurnal | `/jurnal`, `/jurnal/complet` | filtre |
| Rapoarte proiect | `/consum`, `/proiecte/{id}/situatie`, `/proiecte/{id}/echipamente` | - |
| Observatii proiect | `/proiecte/{pid}/observatii/{id}`; `/observatii/{id}` este redirect | - |
| Analize de risc | `/analize-risc` | sectiune pe pagina proiectului/beneficiarului |
| In afara Blazor | `Pages/Account`: Login, Logout, AccessDenied (Razor Pages `.cshtml`) | - |

Fara help propriu (redirect/eroare): `StockMovementRedirect` (`/miscari/{id}`), `ProjectObservationRedirect`, `Error`, `Maintenance` (asteptare backup), `Logout`.
Ferestre de sine statatoare (popout, `PopoutLayout`, se deschid in fereastra de browser): `ProductGroupsWindow`, `MaintenanceMapWindow` - nu au antet comun, deci trebuie sa isi primeasca singure butonul.

### 1.2 Formulare/editori (componente `*Editor`)

Se deschid ori inline (card deasupra listei), ori in popup, dupa pagina: `ProductEditor` (Home inline; popup in InvoicePickup si OfferPickup), `SupplierEditor` (inline la Furnizori; popup in 4 locuri din fluxul de facturi), `BeneficiaryEditor` (Beneficiari inline; popup in OfferPickup), `ProjectEditor`, `WorkPointEditor`, `UserEditor`, `VehicleEditor`, `ServiceContractEditor`, `ServiceInterventionEditor`, `RiskAnalysisEditor`, `ProjectObservationEditor`. Fiecare e o "fereastra" care se deschide, deci primeste topic propriu; topic-ul se afiseaza identic indiferent de locul de unde e deschis (un singur text, nu 4 copii).

### 1.3 Dialoguri / popup-uri

~42 de locuri cu `class="delete-dialog"` sau `class="invoice-modal"`, fiecare cu marcajul scris de mana in pagina (`@if (stare is not null) { <div class="delete-overlay|invoice-modal-backdrop"> ... }`). Doar `DeleteConfirmationDialog`, `SaveConfirmationDialog` si `UnsavedChangesDialog` sunt componente reutilizabile. Straturi z-index existente: backdrop 1000, `.front` 2000, `unsaved-overlay` 3000 (help-ul trebuie sa fie peste, de ex. 4000).

Dialoguri cu continut (necesita help propriu, ~22 distincte):
- Produs: Categorii si subcategorii (ProductEditor), Editare miscare, Istoricul modificarilor, Regularizare stoc (ToRegularizeTab), Deblocare produs (ForceUnlock), Modificare valoare parametru, Iesire rapida (Home).
- Inventar: Situatia de inventar (previzualizare PDF), Confirma modificarile de stoc (preluare inventar).
- Facturi: Factura detaliu (/facturi), Adauga produs la factura (InvoiceManualEntryDialog), Legare intrare-factura (EntryInvoiceLinkDialog), Salveaza ca sablon nou, Editare camp / Editare coloana (workbench sablon), Legare sablon (InvoiceTemplatesList).
- Altele: Notificare (detaliu, 2 variante), Punct pe harta (MaintenanceMapView), Diferente ANAF, Modifica expirare vehicul, Transfer vehicul, Componente proiect (ExitComponentChoice/EntryComponentChoice/ReservationWarningChoice).

Dialoguri de confirmare simple (~12): Inchizi sesiunea, Stergi X, Restaurezi baza de date, Stornezi operatia, Scoti componenta, Inlocuiesti lista, Modificari nesalvate, Salvezi?, Sterge pachet backup, Sterge sablon. Propunere: NU primesc topic, ci sunt declarate explicit "exceptate" (textul lor se explica singur); lista de exceptari e vizibila in test (sectiunea 4).

Total tinta: ~36 pagini + ~12 taburi/sub-taburi cu continut diferit + ~11 editori + ~22 dialoguri = ~80 topicuri (unele foarte scurte).

## 2. Arhitectura propusa

### 2.1 Principii
1. Un topic = o fereastra (pagina sau tab cu continut propriu, editor, dialog). Cheie stabila `modul.fereastra[.tab]`, ex.: `produse.catalog`, `produse.miscari`, `setari.backup.nas`, `facturi.dialog.legare-intrare`.
2. Continutul sta in fisiere de date, nu in razor. Codul contine doar cheia.
3. Reutilizeaza ce exista: `CollapsibleSection` (details/summary, `IndicatorClass` pentru "+"/"-"), clasele de popup existente, `[Module]`/`ModuleGate`, politicile `perm:*`.
4. Zero modificari de comportament in ferestrele existente in afara de adaugarea unui buton in antet.

### 2.2 Componente comune (noi, in `Components/Shared`)
- `HelpButton.razor` - parametri: `Topic` (cheie), optional `Class`. Randeaza un buton cu iconita "?" (cerc cu semn de intrebare, clasa noua `help-icon`, 32 px, pictograma SVG din CSS exact ca la `row-icon`; `aria-label` si `title` = "Ajutor: <titlul ferestrei>"). La clic deschide `HelpDialog` prin serviciul `HelpHost`.
- `HelpDialog.razor` - popup mare (latime ~ `min(900px, 96vw)`, `max-height: 92vh`, defilare interna), peste orice alt strat (z-index 4000), Esc inchide, focus-trap, buton "Inchide" (`refresh act-close`). Se randeaza o singura data in `MainLayout` si `PopoutLayout`, deci se poate deschide si deasupra unui dialog deja deschis (dialogul de dedesubt ramane neatins, ca sa nu se piarda date nesalvate).
- `HelpHost` (serviciu scoped per circuit): `Open(topicKey)`, `Close()`; tine topicul curent. Un singur loc care incarca si cache-aza topicurile.
- `HelpCatalog` (serviciu singleton): citeste resursele incorporate o data la pornire, valideaza structura, expune `Get(key)`, `Keys`.
- Atribut `[HelpTopic("produse.catalog")]` pe pagina rutata (analog cu `[Module]`), ca sa existe legatura pagina -> topic verificabila prin reflexie (sectiunea 4).

### 2.3 Unde sta butonul
- **Pagini rutate:** o singura iconita in antetul comun (`header.topbar`, `MainLayout`) langa "Deconectare"; citeste `[HelpTopic]` de pe pagina curenta (din `RouteData`), deci toate paginile o primesc fara a fi editate una cate una. Pe pagini cu taburi, pagina publica tabul activ printr-un `HelpContext` (cascading/serviciu): `HelpContext.Set("setari.backup.nas")`; la schimbarea tabului topicul se schimba, iar daca tabul nu are topic propriu se foloseste cel al paginii.
- **Ferestre popout:** `PopoutLayout` primeste acelasi `HelpButton` in coltul de sus (nu au `topbar`).
- **Dialoguri/popup-uri/editori:** `<HelpButton Topic="..."/>` in antetul dialogului (dreapta titlului). Pentru a nu repeta marcajul in cele ~22 de locuri se introduce componenta `AppDialog` (titlu, `HelpTopic`, continut, actiuni) care inlocuieste treptat `<div class="delete-overlay|invoice-modal-backdrop">...`; migrarea se face la atingerea dialogului, nu in bloc. Pana la migrare, butonul se pune manual in antet.
- Nu se pune iconita pe randurile tabelelor (regula "iconite pe randuri" se refera la operatii, nu la help); iconita de help sta numai in antet de fereastra.

### 2.4 Continutul
Locatie: `Assets/Help/<modul>/<cheie>.json`, incluse ca `EmbeddedResource` cu `LogicalName="BlazorStoc.Help.%(Filename)%(Extension)"` (acelasi tipar ca `Assets/Data/**` si `Assets/Migrations`). Avantaje: nu depinde de fisierele de pe disc la publicare, se valideaza la build/test, e versionat in repo, fara pachet nou (`System.Text.Json`).
De ce JSON si nu `.md`: textul se afiseaza in aplicatie, deci are diacritice, iar regula `.md` fara diacritice ar fi incalcata; extensia `.json` e in afara regulii. Alternativa (daca scrierea de text lung in JSON incomoda): un format text simplu `.help` cu sectiuni `## Element: ...` si `## Flux: ...`, parsat de `HelpCatalog` - decizie la intrebarile deschise.

Structura unui topic:
```json
{
  "id": "produse.catalog",
  "title": "Catalog produse",
  "module": "produse",
  "summary": "Ce este fereastra si la ce serveste, 1-3 propozitii.",
  "areas": [
    { "name": "Antet si filtre", "elements": [
      { "id": "cautare", "name": "Cautare", "kind": "camp",
        "text": "Cauta in cod, denumire si descriere...", "anchor": "catalog-search" },
      { "id": "sort", "name": "Sortare", "kind": "lista", "text": "..." }
    ]},
    { "name": "Tabelul de produse", "elements": [
      { "id": "col-stoc", "name": "Cantitate", "kind": "coloana", "text": "..." },
      { "id": "act-edit", "name": "Editare", "kind": "iconita", "icon": "edit",
        "text": "Deschide editorul produsului.", "permission": "stoc.edit" }
    ]}
  ],
  "flows": [
    { "id": "preluare-factura", "title": "Cum preiau o factura",
      "steps": ["Deschide Preluare factura...", "..."],
      "notes": "Atentie: ...",
      "related": ["facturi.preluare"], "permission": "stoc.edit" }
  ],
  "related": ["produse.miscari"]
}
```
- `kind` (camp, buton, iconita, coloana, filtru, tab, insigna, comutator, lista, zona) controleaza prezentarea (pentru `iconita` se randeaza chiar iconita reala `row-icon <icon>` neinteractiva, deci utilizatorul recunoaste pictograma).
- `anchor` (optional) = valoarea unui atribut `data-help="..."` pus pe elementul real din razor; permite (a) test de elemente invechite, (b) buton optional "Arata pe pagina" care evidentiaza elementul (JS mic, `outline` temporar - se respecta regula fara contur gros: evidentiere cu bordura/umbra de 1 px sau fundal, nu `outline`).
- Text cu marcaj minim: `**bold**`, liste cu `-`, referinte `[[topic:cheie]]` (link intre topicuri, deschide alt topic in acelasi popup). Randat de un mic randator propriu (fara Markdig), HTML-encodat (nu se accepta HTML in continut).

### 2.5 Aspectul ferestrei de help
- Antet: titlu, insigne `count-badge` cu numarul de elemente si de fluxuri, buton "Inchide" (`refresh act-close`). Optional camp de cautare in topic (faza 3).
- Sub antet, `summary`.
- Sectiunea "Fluxuri de lucru": fiecare flux este un `CollapsibleSection` cu indicatorul "+" (inchis) / "-" (deschis) (clasa `IndicatorClass="help-plus"` in CSS, nu glife text; titlul "Cum preiau o factura"), continut: pasi numerotati, nota, link-uri catre topicuri legate. Toate pornesc inchise; daca `HelpHost.Open` primeste si un `flow`, se deschide acela (ex. din butonul "Cum se face?" dintr-un ecran gol).
- Sectiunea "Elementele ferestrei": grupate pe `areas`, fiecare zona colapsabila (deschisa implicit), elemente ca lista cu eticheta in stanga/pictograma si explicatia in dreapta. Un `CollapsibleSection` pe zona are sens doar cand sunt multe elemente; sub ~6 elemente zona ramane deschisa si fara antet colapsabil.
- Fara casete de bifat; daca apare un filtru ("Arata doar ce pot face eu") este `ToggleSwitch`.

### 2.6 Continut pe drepturi / module (valoarea implicita propusa)
- `permission` pe element sau flux = cheia din `Permissions.cs` (aceleasi `perm:*` din meniu). `HelpDialog` ascunde elementele/fluxurile pentru care utilizatorul nu are dreptul (nu vede butoane pe care nu le are).
- Topicul unui modul scos din functiune (`ModuleGate`/`ModuleRegistry`) nu e accesibil; daca o pagina inlocuita arata `ModuleUnavailable`, aceasta isi are propriul text scurt (fara topic).
- Rol/utilizator nu schimba structura fisierului: filtrarea se face la randare, deci exista un singur text per fereastra.

## 3. Respectarea regulilor din CLAUDE.md

| Regula | Cum este respectata |
|---|---|
| Iconite row-icon | Help-ul nu este operatie pe rand: nu se pune pe randuri. Iconita are clasa noua `help-icon` in `wwwroot/app.css` (acelasi mecanism SVG-mask ca `row-icon`, 32 px, `aria-label` + `title` cu numele ferestrei). In help se randeaza pictogramele `row-icon <nume>` existente neinteractiv (clasa suplimentara `static`, fara click). Daca se adauga o operatie noua pe randuri, ea primeste intrare in help (testul din sectiunea 4 o cere). |
| ToggleSwitch, fara checkbox | Niciun `input type=checkbox`; orice filtru din help = `ToggleSwitch`. Sectiunile "+" sunt `<details>/<summary>` (`CollapsibleSection`), nu casute. |
| Butoane `act-*` | Singurul buton de text: "Inchide" = `refresh act-close`; un eventual "Deschide fluxul" = `refresh act-nav`. Ambele 40 px. |
| Spatiere | Continutul popupului in `catalog-body` (padding 24 px); 8 px intre nume element si insigna; 10 px sub campul de cautare; 18/28 px dupa rand de butoane. Verificare vizuala inainte de predare. |
| Diacritice | Textele din aplicatie (continut JSON, etichete componente) au diacritice. Fisierele `.md` (acest document, "Teste utilizator/", documentatia) fara diacritice. |
| Data dd.mm.yyyy | Exemple din help folosesc forma `25.09.2026`; nu se afiseaza formate interne. |
| Etichete `<...>` / MarkedTextField | Daca un text de help descrie etichete `<...>`, se scriu ca text (HTML-encodat); nu e camp editabil. |
| Jurnalizare | Help-ul e read-only (embedded), deci fara eveniment de audit. Daca ulterior se alege editarea din aplicatie, apare actiune dedicata "Modificare text help" in `AuditActions`, filtru Audit si test. |
| Tokeni / context | Continutul se scrie pe module, un fisier pe fereastra; HARTA_COD.md primeste un rand "Help". |
| Fara dependente noi | Fara pachete NuGet; randator propriu pentru marcajul minim. |

## 4. Garantarea acoperirii (teste)

Grup nou `help` in `tests/BlazorStoc.Checks` (rulat de `tools\run-checks.ps1 -Group help`, in plus in suita completa inainte de commit).

1. **Pagini (reflexie, strict):** pentru fiecare tip cu `RouteAttribute` din assembly: are `[HelpTopic(cheie)]` sau este pe lista explicita `HelpExempt` (redirecturi, Error, asteptare backup) cu motivul scris; cheia exista in `HelpCatalog`. O pagina noua fara help esueaza la build-ul testelor.
2. **Orfani:** fiecare topic din `Assets/Help` este referit de o pagina, un tab, un `HelpButton`/`AppDialog` sau este exceptat; fiecare `related` si `[[topic:...]]` indica un topic existent.
3. **Dialoguri (scanare de sursa):** testul parcurge `*.razor` si cauta marcajul de dialog (`delete-overlay`, `invoice-modal-backdrop`, `delete-dialog`); fiecare aparitie trebuie sa contina `<HelpButton`/`<AppDialog ... HelpTopic=` ori marcajul `data-help-exempt="motiv"` (confirmarile simple). O baza de numar (baseline) se pastreaza in test pana la migrarea completa spre `AppDialog`; dupa migrare se interzice marcajul brut in afara `AppDialog`.
4. **Taburi:** fiecare `role="tab"` din razor are un topic de tab (`id` din cheie) sau mentiune in topicul paginii (verificare prin `HelpContext.Set` sau lista de taburi din topic).
5. **Elemente invechite (help -> cod):** orice `anchor` din JSON trebuie sa existe ca `data-help="..."` in fisierul razor al ferestrei (pagina, componenta, dialog); un element redenumit/scos din ecran fara actualizarea help-ului esueaza.
6. **Elemente lipsa (cod -> help, euristic):** pentru fiecare fereastra: toate numele de iconite `row-icon <nume>` folosite in razor apar ca element `kind=iconita` in topic; toate antetele `<th>` fixe apar ca `kind=coloana` (cu lista de exceptari pentru antete dinamice). Nu se pretinde acoperirea oricarui `<input>`: aceasta ramane responsabilitatea autorului + revizuire.
7. **Calitate continut:** fiecare topic are `summary`; fiecare flux are minim 2 pasi; fara HTML in text; fara date in alt format decat `dd.mm.yyyy`; fara `input type=checkbox` mentionat ca instrument; diacritice prezente (test care semnaleaza text romanesc fara nicio diacritica pe lungime > 80 caractere).
8. **Permisiuni:** `permission` din JSON este o cheie reala din `Permissions.cs`.
9. **Prospetime la commit:** script `tools\check-help.ps1` (rulat in pasul "documentatia la commit/push") listeaza fisierele `.razor` modificate in `git status` a caror topic de help nu a fost modificat; nu blocheaza, avertizeaza (verificarea manuala a textului ramane umana).

Legatura cu "Teste utilizator/": fiecare fisier de componenta primeste o sectiune fixa "Help" (cu data) cu pasii: deschide iconita de help in fiecare fereastra a componentei, verifica ca titlul e cel corect, ca fiecare element vizibil e descris, ca fluxurile "+" se deschid/inchid, ca fluxul descris se poate parcurge efectiv (utilizatorul urmeaza pasii din help si confirma ca duc la rezultat). Adaugarea unei ferestre/elemente noi in orice modificare cere: (a) topic nou/actualizat, (b) pas nou in "Teste utilizator/". Regula se adauga in CLAUDE.md (ca cea pentru jurnal/etichete): "orice fereastra sau element nou are intrare in help; testul `help` o impune".

## 5. Etape de implementare si efort

Estimarile sunt in sesiuni de lucru (o sesiune = un task incheiat cu verificari, ca in protocolul curent).

| Etapa | Continut | Efort |
|---|---|---|
| 0. Decizii | raspunsurile la sectiunea 6 (minim: format continut, drepturi, cine scrie) | 0,2 sesiune |
| 1. Infrastructura | `HelpCatalog`, `HelpHost`, `HelpContext`, `HelpButton`, `HelpDialog`, atribut `[HelpTopic]`, CSS (`help-icon`, `help-plus`, strat 4000), buton in `topbar` si `PopoutLayout`, randator marcaj, incarcare resurse; 2-3 topicuri pilot (Catalog produse, Preluare factura, un dialog); test grup `help` partea 1-2, 7-8 | 2 sesiuni |
| 2. Dialoguri | `AppDialog` + migrare dialoguri cu continut (~22), cu helpul lor; test scanare (3) | 2 sesiuni |
| 3. Continut pe module (cate un modul = o sesiune): Produse/stoc (inclusiv miscari si categorii), Facturi + preluare + sabloane + Setari->Facturi, Furnizori, Inventar + backup, Beneficiari/proiecte/observatii/rapoarte, Vehicule, Mentenanta + harta, Oferte, Notificari/Jurnal/Utilizatori/Tipuri/Nomenclator/Setari-ANAF, Analize de risc | 8-10 sesiuni (cel mai mult text; ~80 topicuri, 4-6 flux-uri pe fereastra mare) |
| 4. Acoperire fina | `data-help` pe elemente, testele 4-6, `check-help.ps1`, regula in CLAUDE.md, sectiunea "Help" in `Teste utilizator/`, HARTA_COD | 1-2 sesiuni |
| 5. Optional | cautare globala in help, "Arata pe pagina", help din ecrane goale (flux deschis direct), tiparire/export PDF al helpului, editare in aplicatie | 2-4 sesiuni, dupa decizie |

Total fara optionale: ~13-17 sesiuni; livrabil util dupa etapele 1+2+primele module din 3 (se poate publica incremental: ferestrele fara topic nu afiseaza iconita sau afiseaza "Ajutor in curs de scriere" - vezi intrebarea 5).

Riscuri: (1) volumul de text este costul real, nu codul; (2) help-ul se invecheste - de aici testele 5, 6 si `check-help.ps1`; (3) markup duplicat al dialogurilor - `AppDialog` reduce riscul, dar migrarea atinge multe fisiere (de facut gradual, cu testele existente); (4) straturile z-index si focus cand help-ul se deschide peste un dialog cu formular nesalvat - help-ul nu inchide si nu reseteaza dialogul de dedesubt (de verificat in browser); (5) `PopoutLayout` este fereastra separata de browser: help-ul se deschide in acea fereastra, nu in cea principala.

## 6. Intrebari de produs deschise

1. **Format de scriere a continutului:** JSON structurat (recomandat: validabil, fara parser nou) sau text simplu `.help` mai comod la scris? Cine il scrie si il revizuieste (Claude propune, utilizatorul valideaza fiecare modul)?
2. **Pe roluri / permisiuni:** se ascund elementele/fluxurile la care utilizatorul nu are drept (propus), sau se arata toate cu eticheta "necesita dreptul X"?
3. **Cautare:** cautare in tot helpul (faza 5) sau doar in topicul deschis (propus pentru inceput: doar topicul curent)?
4. **Limba:** doar romana (propus, ca restul aplicatiei) sau structura pregatita pentru mai multe limbi (cheile ar trebui sa fie independente de text)?
5. **Ferestre fara topic inca:** ascundem iconita pana exista continut, sau o aratam cu "Ajutor in curs de scriere"? (Testul din sectiunea 4 nu lasa ferestre nou-adaugate fara topic, dar la rollout incremental trebuie o regula.)
6. **Dialoguri de confirmare:** acceptam ca nu au help (propus; se explica singure), sau vrei help pe toate, inclusiv "Inchizi sesiunea?"?
7. **Mod de editare:** help-ul se schimba doar prin rebuild (propus; simplu, versionat) sau doreste administratorul sa il poata ajusta din aplicatie (necesita stocare in baza + audit + drept dedicat)?
8. **Pagina de login si ecranele din afara Blazor:** includem un help scurt si acolo?
9. **Cand sa se afiseze primul ajutor:** doar la clic pe iconita (propus), sau si un "tur" la prima deschidere a unei ferestre noi / notificare "Ce e nou" dupa o versiune?
10. **Ordinea modulelor la scrierea continutului:** propunere - Produse/stoc, Facturi si preluare, Inventar, apoi restul; confirmi sau alta prioritate?

## 7. Decizii ale utilizatorului (19 octombrie 2026) - prevaleaza asupra sectiunilor de mai sus

1. **Format continut:** text simplu comod de scris (format `.help`, nu JSON), scris de Claude. Pot exista imagini pentru clarificarea unui topic: `Assets/Help/img/*.png`, referite din text (`[img:nume.png | legenda]`), incluse ca resurse/fisiere statice; dimensiune limitata, fara date reale in capturi.
2. **Permisiuni:** fara filtrare pe drepturi; oricine vede help-ul unei ferestre active. Campul `permission` din structura se elimina, testul 8 (sectiunea 4) cade.
3. **Cautare:** cautare in tot help-ul numai cand help-ul se deschide din Dashboard (pagina principala); in orice alta fereastra, help-ul este limitat la fereastra activa (fara camp de cautare).
4. **Limba:** doar romana; nu se pregateste structura multilingva.
5. **Ferestre fara topic:** nu exista ferestre fara topic - fiecare fereastra are descrise elementele si fluxul de operare; iconita apare peste tot, nu se ascunde si nu are mesaj "in curs de scriere". Testul de acoperire ramane strict.
6. **Confirmari:** dialogurile de confirmare simple nu au help (exceptate explicit).
7. **Actualizare:** help-ul se schimba la rebuild, daca e cazul (resurse incorporate); fara editare din aplicatie, fara audit.
8. **Ecrane din afara Blazor:** Login, AccessDenied (Razor Pages) primesc help scurt; iconita in antetul acelor pagini, acelasi popup (JS minimal sau pagina de help servita ca fragment).
9. **Ordinea scrierii continutului = ordinea meniului principal:** Pagina principala (Dashboard) -> Produse (Stoc, Preluare factura, Iesire multipla, Export consum) -> Inventar (Generare situatie, Preluare inventar) -> Administrare (Categorii, Beneficiari, Furnizori, Facturi, Vehicule, Utilizatori, Nomenclator) -> modulele din `Modules.NavItems()` (Oferte, Mentenanta, Analize de risc etc., in ordinea afisata in meniu) -> Notificari -> Jurnal activitate -> Setari -> Login/AccessDenied. Paginile de detaliu si dialogurile se scriu odata cu pagina din care se deschid.

Efect asupra planului: etapa 3 (continut) urmeaza ordinea de mai sus; formatul `.help` cere un parser mic in `HelpCatalog` (sectiuni `# Titlu`, `## Element: nume | tip`, `## Flux: titlu`, pasi numerotati) si o validare la test (sectiune lipsa, imagine inexistenta). Estimarea ramane ~13-17 sesiuni, +0,5 sesiune pentru parser/imagini si Login/AccessDenied.

## 8. Stare la 19 octombrie 2026

Implementat pe branch-ul `feature/help`: infrastructura (`Services/Help`, `/ajutor/*`, `wwwroot/help.js`, `HelpButton`), 89 de topicuri `.help` care acopera toate paginile si toate formularele/dialogurile cu continut (confirmarile simple sunt exceptate), iconita "?" in bara de sus, in ferestrele separate, pe paginile Login/Acces refuzat si in antetul formularelor/dialogurilor. Taburile si subtaburile au topicuri proprii (ex. Setari -> Backup -> Backup NAS). Testele `help` (36 de verificari) impun acoperirea: pagina fara topic, topic orfan, legatura catre topic inexistent, imagine sau pictograma inexistenta, fisier cu fereastra peste pagina fara buton de ajutor. Ramas de facut, la cerere: imagini de clarificare in topicuri (formatul `[img:nume.png | legenda]` si endpoint-ul exista; `Assets/Help/img` este gol), cautare in tot ajutorul din pagina principala (facuta), actualizarea textelor dupa feedback-ul utilizatorului din `Teste utilizator/17_Help.md`.
