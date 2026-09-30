# TODO — funcționalități viitoare

## Regula de actualizare a TODO

Din 28.09.2026, taskurile finalizate nu mai stau in acest fisier: ele se muta in `IMPLEMENTED.md` (fisier separat, in acelasi director), care se completeaza doar prin append (se adauga la sfarsit, nu se rescrie ce exista deja).

- Acest fisier („TODO.md”) contine numai ce mai trebuie implementat: taskurile active și, în cadrul lor, doar subtaskurile nefinalizate.
- Taskurile active sunt numerotate consecutiv, începând de la 1, în ordinea priorității; numărul cel mai mic indică prioritatea cea mai mare.
- Când se introduce un task nou înaintea unui task existent, acesta și toate taskurile active următoare (neimplementate) se renumerotează; subtaskurile își schimbă numerele odată cu taskul.
- Când un task este finalizat, el se scoate din acest fisier, iar toate taskurile rămase neimplementate se renumerotează de la 1.
- Taskul finalizat se **adauga la sfarsitul `IMPLEMENTED.md`** (fisier in ordine cronologica: cel mai vechi primul, cel mai nou ultimul — doar append, nu se modifica intrarile anterioare), cu **data și ora implementării** (ora locală) și cu **detalierea** lucrării: ce s-a implementat, fișierele principale, deciziile, verificările efectuate și ce a rămas neverificat.
- Taskul finalizat **nu păstrează denumirea „Task N”**: primește o denumire care explică succint scopul lui, în forma „Finalizat la zz.ll.aaaa oo:mm — denumire”. Trimiterile către el din alte texte folosesc această denumire, nu un număr; nici subtaskurile lui nu mai păstrează numerele.
- Verificările care nu s-au putut efectua se trec, cu motivul și pașii, în `docs/TESTE_RAMASE.md`.

# Taskuri active

Planul de mai jos vine din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (deciziile confirmate de utilizator la 30.09.2026) si din evaluarea de la sfarsitul `docs/PROPUNERE_HARTA_MENTENANTA.md`. Detaliile fiecarui task (schema, reguli, jurnal, teste) sunt in documentul de propunere; aici sunt numai subtaskurile de urmarit. Verificarile ramase din taskurile anterioare sunt in `docs/TESTE_RAMASE.md`.

Planul din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` este implementat integral (vezi `IMPLEMENTED.md`); verificarile ramase sunt in `docs/TESTE_RAMASE.md`.

## Task 1 - Comutatoare glisante (slidere) in locul casutelor de bifat

Cerut de utilizator la 30.09.2026 (exemplul: „Sablon activ" din formularul de sabloane de notificari, o casuta albastra mare, nelegata vizual de restul formularului). Casutele de bifat care exprima o **stare pornit/oprit** se inlocuiesc cu comutatorul glisant deja folosit la „Arata parola" si „Sterge cheia salvata" (`.toggle-switch` in `wwwroot/app.css`, `input type="checkbox" role="switch"`, accesibil de la tastatura, cu `prefers-reduced-motion`).

- **Se inlocuiesc (stari, nu selectii):** „Sablon activ" (`NotificationTemplatesEditor`), „Integrare activata", „Antet activ", „Taiere spatii", „Elimina RO", „Obligatoriu" (`AnafConfigurator`), „Cont activ" (`UserEditor`), „Arata si contractele Off" (`MaintenanceMapView`, `ServiceContractsSection`, `ServiceMaintenance`), „Sterge contractele in conflict" (`ServiceContractActivationPanel`), „Foloseste proiect" (`ExitDestinationPicker`), „Exclude stocul zero" (`Inventory`). Activ/inactiv din formularul tipurilor de pinuri foloseste deja comutatorul.
- **Raman casute de bifat (selectii multiple in liste):** „Selecteaza toate"/categorie/subcategorie (`Inventory`), „Selecteaza toate"/linie (`InventoryPickup`), „Muta"/„Inclus" pe randurile tabelului de contract (`ServiceContractEditor`) - un slider pe fiecare rand ar incarca tabelul si nu ar arata ca este o selectie.
- **Implementare:** un singur component `ToggleSwitch.razor` (parametri `Value`/`ValueChanged`, `Label`, `Id`, `Disabled`, `AriaLabel`) in locul marcajului repetat; stilul ramane in `app.css`; se elimina regulile vechi `.check-label`/`.anaf-check` cand nu mai sunt folosite. Starea (bifat/nebifat) si comportamentul (`@bind:after`, dezactivare in timpul salvarii) raman neschimbate; verificare in browser a fiecarei pagini atinse si a navigarii de la tastatura (Spatiu comuta, focusul se vede).

## Task 2 - Aliniere estetica a elementelor alaturate in formulare

Cerut de utilizator la 30.09.2026 (exemplul: in „Integrare ANAF", comutatorul „Integrare activata" sta alaturi de campul „Denumire integrare", dar nu este pe aceeasi linie cu caseta campului si pare pierdut; la fel casuta „Sablon activ" langa „Eveniment de expirare"). Situatii de genul acesta nu trebuie sa existe: fie elementul se muta intr-un loc firesc, fie se aliniaza astfel incat sa fie estetic.

- **Regula:** in orice grila de formular (`.editor-grid`, `.anaf-grid` si echivalentele) un element care sta pe acelasi rand cu un camp cu eticheta deasupra (comutator, buton, nota) se aliniaza cu **caseta campului** (nu cu eticheta) sau se muta pe propriul rand, pe toata latimea, sub campurile pe care le comanda; campurile de pe acelasi rand au aceeasi linie de baza pentru casete, chiar daca etichetele sau notele lor au inaltimi diferite (etichete pe doua randuri, note de ajutor).
- **Audit:** parcurgerea tuturor formularelor si dialogurilor (utilizator, beneficiar, punct de lucru, contract, interventie, vehicul, produs, proiect, sabloane de notificari, setari notificari, ANAF, harta, filtre ale listelor si jurnalelor) la latime mare, medie si pe telefon, cu captura inainte/dupa; corectia se face in CSS comun (de exemplu `align-items: start` pe grila, clasa `.field-inline` pentru comutatoare care stau pe linia casetei, `min-height` rezervat pentru note) si nu pagina cu pagina, ca sa nu apara alte cazuri.
- **Verificare:** lista paginilor cu capturi inainte/dupa la 3 latimi in `docs/TESTE_RAMASE.md`; un test automat de aspect nu este practic, asa ca regula se adauga in `CLAUDE.md` („elementele alaturate se aliniaza estetic; un comutator/buton nu sta „plutind" langa un camp") pentru dezvoltarile viitoare.

## Task 3 - Fara contur gros in jurul campurilor de editare active; evidentiere mai discreta

Cerut de utilizator la 30.09.2026 (exemplul: campul de cautare din jurnal are, cand este activ, un contur verde gros, cu distanta fata de caseta, care se suprapune peste caseta de cautare si „strica estetica"). Marginea aceea nu trebuie sa existe cand campul este activ.

- **Cauza:** regula globala din `wwwroot/app.css`: `input:focus-visible, select:focus-visible, textarea:focus-visible { outline: 3px solid #76bfb0; outline-offset: 3px }` (la fel `.clipboard-zone:focus`). La campurile din interiorul unui container cu chenar propriu (`.search-box`, `.pick-date`, campuri cu pictograma) conturul se deseneaza in afara containerului si dubleaza chenarul.
- **Audit:** toate campurile de editare: text, cautare (liste, jurnale, harta, produse), numar, parola, data (`PickOnlyDate`), selectii (`select`, `SearchableSelect`), zona de text, campurile din dialoguri si din filtre, zonele de incarcare de fisiere. Se verifica fiecare la focus cu tastatura si cu mausul.
- **Alternativa propusa (evidentiere fara contur exterior):** campul activ isi schimba doar **chenarul propriu** (1 px, culoarea de accent `--green`) si primeste o **umbra interioara foarte fina** (`box-shadow: 0 0 0 1px var(--green)`, fara distanta fata de caseta); pentru containerele cu pictograma (`.search-box`) evidentierea trece pe container (`:focus-within`), iar campul interior isi pierde orice contur. Evidentierea ramane vizibila pentru accesibilitate (contrast de cel putin 3:1 fata de fundal, cerinta WCAG pentru indicatorul de focus), dar nu mai apare un inel gros separat. Butoanele si linkurile pastreaza un inel discret (2 px, distanta 1 px) doar la navigarea cu tastatura (`:focus-visible`), nu la clic.
- **Implementare:** reguli comune in `app.css` (nu pagina cu pagina): `:where(input, select, textarea):focus { outline: none }`, stilul de accent pe chenar, `:focus-within` pe containere; `prefers-reduced-motion`/`forced-colors` respectate (in modul cu contrast ridicat se pastreaza conturul sistemului). Verificare vizuala inainte/dupa pe formularele si listele principale, la latime mare si pe telefon; nota in `docs/TESTE_RAMASE.md` cu lista paginilor verificate si regula in `CLAUDE.md` pentru dezvoltarile viitoare.

## Task 4 - Pagina principala: intrari pentru Mentenanta si Notificari, cu semnalizarea notificarilor

Cerut de utilizator la 30.09.2026. Pagina principala (`Components/Pages/Dashboard.razor`, sectiunea „Sectiunile aplicatiei") are intrari pentru Produse, Categorii, Inventar, Beneficiari, Vehicule, Utilizatori si Jurnal, dar **nu** pentru Mentenanta (`/mentenanta`) si Notificari (`/notificari`), care exista in meniul lateral.

- **Intrari noi:** „Mentenanta" (scadente, registru de interventii, contracte si harta; descriere scurta in stilul celorlalte) si „Notificari" (notificari de expirare si de mentenanta), cu pictograma din meniul lateral (⚒, ✉), vizibile tuturor utilizatorilor autentificati, in aceeasi ordine ca in meniu.
- **Semnalizarea se extinde:** logica prin care intrarea „Notificari" din meniul lateral devine rosie cu semnul de exclamare/triunghi (`nav-alert`, `nav-triangle` in `MainLayout.razor` si `wwwroot/app.css`; numarul notificarilor care avertizeaza vine de la `IExpiryNotificationService.AlertCountAsync`; `wwwroot/notification-watch.js` o tine la zi la 60 s prin `/api/notifications/alerts`) se aplica si intrarii „Notificari" din pagina principala: titlul si pictograma rosii plus semnul ⚠ (cu `aria-label="Notificari noi"`). Acelasi mecanism si aceeasi sursa de date, nu o logica separata: `notification-watch.js` actualizeaza toate elementele marcate cu `data-notification-nav`/`data-notification-triangle`, deci noua intrare primeste aceleasi atribute.
- **Mentenanta:** intrarea poate semnala si ea, cu acelasi stil, cand exista scadente depasite sau contracte expirate (numar dat de lista de scadente), daca utilizatorul o vrea; de confirmat, implicit doar Notificarile (care includ deja notificarile de mentenanta) primesc semnul, ca sa nu existe doua semnale pentru aceeasi cauza.
- **Verificari:** pagina principala pentru administrator si pentru utilizator limitat, cu si fara notificari care avertizeaza (semnul apare si dispare fara reincarcare), stil identic cu meniul lateral, navigare de la tastatura, ecran ingust; test pentru marcajele `data-notification-*` pe ambele intrari; actualizarea `docs/TESTE_RAMASE.md`.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

