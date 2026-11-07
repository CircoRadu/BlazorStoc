# Propunere: acte aditionale la contractele de mentenanta ale beneficiarilor

Data analizei: 7 noiembrie 2026. Stare: DOAR PROPUNERE, nimic implementat. Fisier scris fara diacritice (regula .md).

Cerinta: "Pentru contracte de mentenanta beneficiari adauga posibilitate de adaugare act aditional contract."

## 1. Ce exista azi (din cod)

- Contractul (`service_contracts`, `ServiceContract`): numar + data (ex. `26/23.09.2025`), ciclicitate globala `cycle_months` (1-12), `valid_until` (data expirarii, optionala), On/Off, observatii, `version` (concurenta optimista). Acoperirea: `service_contract_points` (punct de lucru, ciclicitate proprie optionala, `next_due`). Un punct apartine cel mult unui contract activ.
- Operatii existente: creare, editare (numar, valabilitate, ciclicitate, puncte), activare/dezactivare (cu replanificarea scadentelor), stergere arhivata (doar fara interventii). Jurnal cu actiuni proprii (`CreateServiceContract`, `EditServiceContract`, `EditServiceContractExpiry`, `AddContractPoint`, `RemoveContractPoint`, `EditMaintenanceCycle` etc.).
- Fisiere: contractul are deja `EntityFilesPanel` (`EntityFileOwners.Contract`, max. 20 de fisiere), deci un act aditional scanat se poate incarca azi ca fisier oarecare, fara structura (fara numar, data, efect).
- Notificari: sursa de expirare a contractului citeste `valid_until` al contractelor active, deci prelungirea valabilitatii prin act aditional se reflecta automat in notificari.
- Lipsa: nu exista istoric al schimbarilor contractului (o schimbare de `valid_until`, ciclicitate sau puncte se vede doar in jurnal), nici notiunea de "act aditional" (numar, data, ce a modificat).
- Model apropiat deja implementat: reinnoirile analizelor de risc (`risk_analysis_renewals`: reinnoire cu motiv, anulare "undo", jurnal). Se reia tiparul.

## 2. Ce este un act aditional (ce trebuie sa poata face)

Un act aditional modifica un contract existent, la o data, cu numar propriu (ex. "AA nr. 2 / 15.03.2026"). In practica de mentenanta modificarile tipice sunt:
1. prelungirea (sau scurtarea) valabilitatii contractului;
2. schimbarea ciclicitatii contractului sau a unui punct;
3. adaugarea sau scoaterea de puncte de lucru acoperite;
4. alte clauze (pret, program, obligatii) care nu schimba datele din aplicatie, dar trebuie pastrate ca document.

## 3. Propunere

### 3.1 Concept: act aditional = inregistrare + efect aplicat

Un act aditional este un rand propriu, legat de contract, cu documentul scanat. Cand modifica date pe care aplicatia le tine (punctele 1-3), efectul se **aplica o singura data, atomic** din acelasi formular, iar valorile **dinainte** se pastreaza in act (instantaneu "inainte / dupa"). Pentru punctul 4 actul este doar document + descriere (nu schimba nimic in contract).

De ce nu doar "incarca un fisier": fara efect aplicat, valabilitatea si punctele ar fi modificate manual separat, deci pot diverge de actul real, iar istoricul nu s-ar vedea. De ce nu doar "editeaza contractul": se pierde documentul si trasabilitatea (de ce s-a schimbat data).

### 3.2 Model de date (migrare noua, nr. 74 sau urmatoarea libera)

`service_contract_addenda`:

| Coloana | Tip | Observatie |
|---|---|---|
| `id`, `version` | BIGINT | concurenta optimista, ca la restul |
| `contract_id` | BIGINT | FK `RESTRICT` |
| `number` | VARCHAR(30) | numarul actului (ex. `2`); unic pe contract impreuna cu data |
| `addendum_date` | DATE | data semnarii actului |
| `effective_date` | DATE NULL | de cand se aplica (implicit = data actului) |
| `kinds` | VARCHAR(200) | tipurile de efect: `valabilitate`, `ciclicitate`, `puncte`, `altele` (lista) |
| `summary` | VARCHAR(1000) | ce prevede actul (obligatoriu cand tipul este `altele`) |
| `prev_valid_until`, `new_valid_until` | DATE NULL | pentru `valabilitate` |
| `prev_cycle_months`, `new_cycle_months` | TINYINT NULL | pentru `ciclicitate` (contract) |
| `points_change` | TEXT (JSON) | puncte adaugate/scoase/ciclicitate individuala schimbata, cu valorile dinainte |
| `status` | VARCHAR(12) | `Aplicat`, `Anulat` |
| `created_by`, `created_utc`, `cancelled_by`, `cancelled_utc`, `cancel_reason` | text | trasabilitate |

Plus `archive_service_contract_addenda` (stergere arhivata, ca la contracte) si inscrierea ambelor tabele in `MigratedTables` (backup/restaurare). Fisierele actului: `EntityFileOwners.Addendum = "addendum"` in `entity_files` (mecanismul exista; apar si in backup cu celelalte active).

### 3.3 Interfata

- In fisa beneficiarului, la fiecare contract (`ServiceContractsSection`): sectiune colapsabila **inchisa implicit** "Acte aditionale (N)" cu insigna `count-badge`, tabel paginat (`Paged`): Nr, Data, Efect de la, Ce modifica (rezumat), Stare, Fisiere, Operatii (iconite: vizualizare, editare, anulare, stergere; un rand are ori iconite, ori butoane). Butonul "+ Adauga act aditional" in antetul sectiunii.
- Fereastra "Act aditional" (popup, cu `HelpButton` si descrierea fluxului in sectiuni colapsabile inchise implicit, ca la raportarea erorilor):
  1. Numar, data, efect de la.
  2. Comutatoare On/Off pentru ce modifica: Valabilitate, Ciclicitate, Puncte acoperite, Alte clauze. Fiecare comutator deschide zona lui: data noua de expirare (cu valoarea actuala alaturi), ciclicitatea noua, lista de puncte (adauga/scoate, cu aceeasi alegere "muta din alt contract" ca la contract), text pentru "Alte clauze".
  3. Fisierul scanat (`EntityFilesPanel`; la un act nou, fisierul se ataseaza dupa salvare, ca la contract).
  4. Rezumat "Inainte -> Dupa" afisat inainte de salvare; "Salveaza si aplica".
- Cand se schimba ciclicitatea sau se adauga puncte, scadentele (`next_due`) nu se recalculeaza pe tacute: fereastra propune o data pentru fiecare punct afectat (aceeasi fereastra de replanificare ca la activarea contractului), pe care utilizatorul o poate modifica.
- In antetul contractului: "Valabil pana la ..." arata valoarea efectiva, cu nota "prelungit prin AA nr. 2 / 15.03.2026" cand ultima valabilitate vine dintr-un act.
- Editarea directa a valabilitatii/ciclicitatii contractului ramane (corectii), cu un text de avertizare: "Pentru o modificare semnata, foloseste Adauga act aditional".

### 3.4 Reguli

- Contractul trebuie sa fie On; un contract Off primeste acte numai ca document (tipul `altele`), fara efecte, ca sa nu produca scadente.
- `new_valid_until` nu poate fi anterioara datei contractului si nici datei actului; un act care scurteaza valabilitatea sub data de azi cere confirmare explicita.
- Numar + data unice pe contract. Un act nu se poate modifica dupa ce exista un act mai nou care a aplicat efecte pe aceleasi date (se anuleaza intai cel mai nou).
- **Anulare** ("undo", ca la reinnoirile analizelor de risc): permisa doar pentru ultimul act aplicat si doar daca valorile curente ale contractului sunt cele puse de act (altfel mesaj: contractul s-a schimbat intre timp); restaureaza valorile dinainte, actul trece in starea `Anulat` (ramane in istoric cu motiv). Stergerea este permisa numai pentru actele `Anulat` sau cele fara efect, arhivat, cu motiv.
- Concurenta: `version` pe contract si pe act; aplicarea intr-o tranzactie.

### 3.5 Jurnal (actiuni specifice, regula permanenta)

Adaugare act aditional contract; Modificare act aditional contract; Anulare act aditional contract; Stergere act aditional contract; Adaugare / stergere fisier act aditional (reutilizeaza actiunile de fisiere atasate cu obiectul "act aditional"). Detaliile contin valorile veche si noua (valabilitate, ciclicitate, puncte), iar motivul se genereaza cand nu il cere interfata.

### 3.6 Permisiuni

Fara permisiune noua in prima varianta: adaugarea/editarea reia `mentenanta.add` / `mentenanta.edit`, stergerea si anularea `mentenanta.delete` (aceleasi drepturi ca la contract). Daca se doreste separat, se poate adauga actiunea `mentenanta.act-aditional` (cu cerinta `mentenanta.edit`).

### 3.7 Notificari

Fara sursa noua: sursa existenta de expirare a contractului citeste `valid_until`, deci o prelungire prin act scoate contractul din lista de expirari. Optional: nota in notificarea de expirare "ultimul act aditional: AA nr. 2 / 15.03.2026".

## 4. Alte aspecte

- **Export/rapoarte:** lista contractelor poate arata coloana "Ultimul act aditional"; fara export nou.
- **Backup:** tabelele noi intra in `MigratedTables`; fisierele actelor in activele din pachet (nu necesita lucru in plus).
- **Compatibilitate:** contractele existente nu au acte; nimic de migrat. Fisierele deja atasate contractelor (scanari de acte) raman la contract; utilizatorul le poate muta manual intr-un act nou (optional, nu automat).
- **Cod:** modulul `Maintenance` (versiune minor + nota in `Modules/module-versions.json`), `ServiceContractAddenda.cs` (modele, reguli, interfata, repository Maria, `Guarded`/`Unavailable`), `ServiceContractAddendumEditor.razor`, completari in `ServiceContractsSection.razor`, `AuditActions`, `MariaArchiveSchema`.
- **Help si teste:** topic `.help` nou pentru fereastra (descriere la cerere, conform regulii), pasi in `Teste utilizator/08_Mentenanta_si_harta.md`, teste Maria tintite (aplicare, anulare, concurenta, refuzuri, jurnal) si de reguli.

## 5. Etape propuse

1. Model, migrare, repository, reguli, jurnal (+ teste Maria).
2. Fereastra actului si sectiunea "Acte aditionale" din contract (fara fisiere).
3. Fisierele actului (`EntityFileOwners.Addendum`) si previzualizarea.
4. Replanificarea scadentelor si anularea (undo), afisarea valorii efective in antetul contractului.
5. Help minim, teste de utilizator, versiunea modulului.

## 5b. Revizie 07.11.2026 (dupa raspunsurile utilizatorului): fara scanari si componentele contractului

Confirmat de utilizator: actul aplica efectul; cele patru tipuri de efect sunt suficiente; NU se scaneaza acte (fara fisiere atasate actului); aceleasi drepturi ca la contract; scadentele se propun, utilizatorul le poate modifica.

### 5b.1 Cerinta noua: acte aditionale cu componente proprii

Cerinta (reformulata): contractul are **componente** (ex. TVCI, antiefractie, control acces, incendiu), alese dintr-un **nomenclator "Componente contract mentenanta"**. Un act aditional poate avea componente proprii (ex. contract pentru TVCI, iar prin act se adauga antiefractia). Reguli cerute:
1. Daca actul face referinta la aceleasi componente ca contractul initial, **ciclicitatea o da actul**.
2. Daca prin act se renunta la componente ale contractului initial, **raman in vigoare doar cele date de act**.
3. La adaugarea actului se vede **ce componente (categorii) acopera**; componentele contractului initial se **preiau cu comutator On/Off** (On = ramane in vigoare; Off = se renunta la ea).

### 5b.2 Ce exista azi (analiza)

- **Nu exista** nomenclatorul "Componente contract mentenanta" si contractul **nu are componente**: acopera doar puncte de lucru (`service_contract_points`), cu o ciclicitate a contractului si, optional, una individuala pe punct. Nomenclatorul are doua taburi: "Tipuri de sisteme" (antiefractie, TVCI, control acces, incendiu, retelistica; folosit la proiecte, `system_types`/`project_components`) si "Cuvinte cheie mentenanta" (ActivityApp).
- Deci regula 1 (ciclicitatea data de act pentru aceleasi componente) cere ca **ciclicitatea sa fie pe componenta**, nu doar pe contract/punct. Asta este o schimbare de model a contractului, nu doar a actului.
- Scadentele urmaresc azi **punctul de lucru** (`next_due`); registrul de interventii si notificarile se leaga de punct + contract, nu de componenta.

### 5b.3 Propunere

**Nomenclator.** Tab nou "Componente contract mentenanta" in Nomenclator (acelasi tipar ca "Tipuri de sisteme": denumire, denumiri alternative, activ/inactiv, ordine, fara stergere). Tabela `maintenance_components` (`id`, `name`, `name_key`, `active`, `position`). Pornit cu valorile tipurilor de sisteme existente (antiefractie, TVCI, control acces, incendiu, retelistica) ca sa nu se plece de la zero; apoi cele doua nomenclatoare sunt independente. Alternativa: se refoloseste direct `system_types` (fara nomenclator nou) - mai putin lucru, dar lista de sisteme de proiect si lista de componente de mentenanta raman legate.

**Componentele contractului.** `service_contract_components`: `contract_id`, `component_id`, `cycle_months` (NULL = ciclicitatea contractului), `is_active` (On/Off). Contractul nou/editat primeste lista de componente cu comutator On/Off si ciclicitate proprie. Contractele existente raman fara componente (= "nespecificat", comportament neschimbat) pana le completezi.

**Actul aditional cu componente** (inlocuieste tipul "ciclicitate" de contract din varianta anterioara cu tipul "componente"):
- fereastra actului preia **componentele in vigoare ale contractului** ca lista cu comutator On/Off (On = ramane in vigoare, Off = se renunta) si ciclicitate editabila pe fiecare (implicit cea curenta; ce schimbi tu este ciclicitatea data de act);
- buton "+ Adauga componenta" din nomenclator (cu ciclicitatea ei);
- dupa salvare, componentele in vigoare ale contractului sunt exact cele On + cele adaugate, fiecare cu ciclicitatea din act (regulile 1 si 2); cele cu Off raman in istoricul actului, nu in contract;
- rezumatul "Inainte -> Dupa" arata explicit: "Acopera: TVCI (3 luni -> 6 luni), Control acces (nou, 12 luni); renuntare: Incendiu";
- starea "in vigoare" a contractului se calculeaza dupa ultimul act aplicat; anularea ultimului act readuce componentele dinainte (instantaneu inainte/dupa, ca la puncte).

**Afisare.** Pe randul contractului: "Acopera: TVCI 3 luni · Control acces 12 luni" (insigne); in lista de acte: coloana "Componente" cu ce adauga/scoate/schimba.

**Legatura cu scadentele (decizie necesara, vezi 6.6).** Varianta A (recomandata pentru prima etapa): componentele si ciclicitatile lor descriu **acoperirea si ciclicitatea contractului**, iar scadenta ramane pe punctul de lucru; ciclicitatea efectiva a unui punct fara ciclicitate individuala = cea mai scurta ciclicitate dintre componentele active, iar propunerea de scadente la act foloseste aceasta regula. Varianta B: scadenta pe **(punct, componenta)**, interventia de mentenanta alege componentele efectuate - corecta pentru contracte cu cicluri diferite pe componente, dar schimba registrul de interventii, lista de scadente, harta si notificarile (task separat, mare).

### 5b.4 Ce se schimba in ce am pregatit deja

- Stratul de date al actelor (tabela `service_contract_addenda`, repository, jurnal, anulare, teste) este facut pentru valabilitate/ciclicitate/puncte/alte clauze si merge; tipul "ciclicitate" de contract se inlocuieste cu "componente" dupa confirmarea de mai jos, iar instantaneele "inainte/dupa" primesc si componentele.
- Fereastra actului este schitata (fisier nelegat inca in pagina) si se completeaza cu lista de componente On/Off abia dupa confirmare, ca sa nu fie refacuta.
- Fara fisiere la acte (conform deciziei de azi): nu se foloseste `EntityFilesPanel` pentru act.

## 5c. Revizie 2 (07.11.2026): familii de componente, data de referinta, contracte inghetate, interventii cu componente

Raspunsuri primite la 5b: nomenclator NOU (6); ciclicitate globala SAU pe familie/componenta discreta (7, 8); "categoriile" = componentele din nomenclatorul de mentenanta (9); ciclicitatea se poate schimba prin act, cu alegerea sursei (contract sau act). Reguli noi mai jos; ele schimba modelul contractului, al scadentelor, al interventiilor si al preluarii tichetelor, deci actul aditional devine ultima etapa, nu prima.

### 5c.1 Regulile cerute, asezate

1. **Nomenclator "Mentenanta"**: tabul de acum "Cuvinte cheie mentenanta" devine grupul **Mentenanta** cu doua sub-taburi: "Cuvinte cheie" (neschimbat) si **"Componente interventii mentenanta"** (nou). Aici se definesc **familii** (ex. "Securitate fizica") si **componente** (ex. efractie, TVCI, control acces, incendiu). O componenta apartine unei singure familii sau este **discreta** (fara familie).
2. **Contractul** are: data de referinta (obligatorie), ciclicitate globala si lista de **unitati**: o unitate este fie o **familie** (cu elementele ei On/Off), fie o **componenta discreta**. Ciclicitatea si data de referinta se pot da **global** (pentru toate unitatile) sau **pe unitate**; **elementele dintr-o familie nu au ciclicitate sau data proprie**, doar familia. O componenta dintr-o familie nu se poate adauga separat in afara familiei (ca sa nu apara o ciclicitate proprie pentru ea); daca trebuie ciclicitate separata, componenta se defineste discreta.
3. **Contract inghetat**: un contract careia ii lipsesc parametri (data de referinta, ciclicitatea unei unitati active, nicio unitate activa, niciun punct de lucru) este **inghetat**: nu produce efecte (fara scadente in lista/harta, fara notificari de scadenta, interventiile pot fi inregistrate dar nu muta scadente) si arata un indicator "Inghetat" cu lista informatiilor lipsa. Se genereaza o **notificare "Contract de mentenanta cu informatii lipsa"** (se inchide singura la completare).
4. **Actul aditional**: preia din contract unitatile in vigoare cu On/Off, permite adaugarea de unitati noi din nomenclator, si alege **sursa ciclicitatii** (pastrata din contract sau data de act; global sau pe unitate) si **sursa datei de referinta** (pastrata din contractul parinte sau data noua; global sau pe unitate). Ce ramane On + ce se adauga = singurele unitati in vigoare dupa act. Ciclicitatea unei familii/componente din act este cea din act cand actul o declara.
5. **Verificarea interventiilor la act**: daca beneficiarul are interventii preluate, la act se verifica ce componente acopera fiecare (se alege/confirma lista de componente a interventiilor care nu o au); scadentele propuse tin cont de ultima interventie care acopera unitatea.
6. **Interventii si tichete**: interventia de mentenanta (manuala si cea preluata din tichet de operator) are **componente selectabile** din nomenclator (ce a acoperit).

### 5c.2 Model de date propus (migrari noi, in etape)

- Nomenclator: `maintenance_families` (id, name, name_key, active, position) si `maintenance_components` (id, family_id NULL, name, name_key, alt. names ca la tipurile de sisteme, active, position).
- `service_contracts`: coloane noi `reference_date DATE NULL` (data de referinta globala), `is_frozen TINYINT`, `missing_info VARCHAR(500)` (recalculate de aplicatie la fiecare modificare, in aceeasi tranzactie, ca sa poata fi folosite in interogarile de scadente); `cycle_months` devine nulabila (gol = neales, pentru regula 3); contractele noi nu mai primesc ciclicitate implicita 3.
- `service_contract_units`: contract_id, family_id sau component_id (exact una), cycle_months NULL (gol = globala), reference_date NULL (gol = globala), is_active; `service_contract_unit_members` (unit_id, component_id, is_active) pentru elementele On/Off ale unei familii.
- Scadente pe **(punct acoperit x unitate)**: `service_contract_unit_dues` (contract_point_id, unit_id, next_due). `service_contract_points.next_due` ramane ca **cea mai apropiata scadenta a punctului** (tinut la zi de aplicatie), ca lista, harta si notificarile sa mearga neschimbat in prima faza.
- `service_intervention_components` (intervention_id, component_id): ce componente a acoperit interventia; unitatile atinse se deduc (componenta -> familia ei sau ea insasi). Muta scadenta doar unitatile atinse.
- `service_contract_addenda` (exista, migrarea 74): se completeaza cu `cycle_source`, `reference_source`, `reference_date`, instantanee de unitati inainte/dupa; tipul de efect "ciclicitate" de contract se inlocuieste cu "componente" (incl. ciclicitati si data de referinta).
- Nicio alta tabela de fisiere pentru acte (fara scanari).

### 5c.3 Reguli de calcul (de confirmat in 6.11)

- **Data de referinta** = data de la care se numara ciclul (ex. inceputul contractului sau ultima mentenanta efectuata inainte de contract). Prima scadenta a unei unitati = referinta + ciclicitatea ei; dupa aceea scadenta se muta de interventiile care acopera unitatea, cu cele trei variante existente (din data efectuarii, din cea planificata, aleasa).
- Daca actul schimba ciclicitatea cu pastrarea referintei: scadenta propusa = (ultima interventie care acopera unitatea, altfel referinta) + ciclicitatea noua. Daca declara data noua de referinta: scadenta propusa = referinta noua + ciclicitatea. Utilizatorul poate modifica orice scadenta propusa.
- Ciclicitatea efectiva a unei unitati: a ei, altfel cea globala a contractului (sau a actului, cand actul o declara global).

### 5c.4 Efecte asupra codului existent

- **Nomenclator** (`SystemTypes.razor`, taburi): sub-taburi noi; componente/familii cu aceleasi reguli ca tipurile (fara stergere, activ/inactiv, ordine).
- **Contract**: editorul contractului primeste data de referinta, ciclicitatea globala (fara implicita), lista de unitati cu On/Off pe elementele familiilor, ciclicitate/data pe unitate; lista de contracte arata "Inghetat" si unitatile; `MariaServiceContractRepository` si regulile de validare.
- **Scadente**: lista `/mentenanta`, harta, sursa de notificari `MaintenanceDueSource` si `GetDueListAsync` trec pe (punct x unitate); contractele inghetate sunt excluse.
- **Interventii**: `ServiceInterventionEditor` si repository-ul primesc componentele; alegerea scadentei urmatoare se face pe unitatile atinse.
- **Preluare tichete** (`ActivityMaintenanceReview`, popupul operatorului): `ReviewTakeInput` primeste componentele; obligatorii cand categoria este Mentenanta si punctul are contract complet; alegerile sunt limitate la unitatile in vigoare ale contractului punctului (la un punct fara contract sau cu contract inghetat se aleg liber din nomenclator, fara mutarea scadentelor). Optional, propunere automata din textul tichetului dupa denumirile componentelor.
- **Notificari**: sursa noua "Contract de mentenanta cu informatii lipsa" (stare, nu data; se inchide singura la completare).
- **Acte aditionale**: stratul de date existent (valabilitate, puncte, alte clauze, anulare, jurnal, teste) ramane; "ciclicitate" de contract se inlocuieste cu "componente"; fereastra se leaga abia dupa etapele 0-3.
- Jurnal cu actiuni specifice noi (adaugare/modificare familie si componenta, schimbare unitati contract, inghetare/dezghetare contract, componente interventie), help, teste de utilizator, versiune de modul.

### 5c.5 Etape propuse

0. **Nomenclator**: sub-taburi "Cuvinte cheie" si "Componente interventii mentenanta" (familii + componente), jurnal, teste.
1. **Contract cu unitati**: data de referinta, ciclicitate globala/pe unitate, familii On/Off, calcul "inghetat" + notificarea cu informatii lipsa; politica pentru contractele existente (6.12).
2. **Scadente pe unitate**: tabela de scadente, lista/harta/notificari, excluderea contractelor inghetate.
3. **Interventii cu componente**: editorul de interventii si popupul operatorului pentru tichete; mutarea scadentelor pe unitatile atinse.
4. **Actul aditional complet**: unitati On/Off, sursa ciclicitatii si a datei de referinta, verificarea interventiilor, propunerea scadentelor, anulare.
5. Help (la cerere), teste de utilizator, versiunile modulelor, documentatie.

## 6. Decizii de confirmat (cu recomandare)

1. Actul **aplica efectul** (valabilitate, ciclicitate, puncte) sau este doar document? Recomandare: aplica efectul, cu instantaneu "inainte/dupa" si anulare.
2. Tipurile de efect: valabilitate, ciclicitate, puncte, alte clauze - sunt suficiente sau mai exista (ex. pret, perioada de garantie)? Recomandare: cele patru; pretul se tine in "alte clauze" pana exista un camp pret la contract.
3. Scanarea actului obligatorie la salvare? Recomandare: nu obligatorie, dar cu avertizare "fara document atasat" in tabel.
4. Permisiune separata `mentenanta.act-aditional` sau aceleasi drepturi ca la contract? Recomandare: aceleasi drepturi, in prima varianta.
5. Cand se schimba ciclicitatea sau punctele: replanificare propusa, la alegerea utilizatorului (recomandat) sau recalculare automata?

### 6.6 Decizii noi pentru componente

6. Nomenclatorul "Componente contract mentenanta": tab nou, pornit cu tipurile de sisteme existente, apoi independent (recomandat), sau se refoloseste direct "Tipuri de sisteme"?
7. Scadentele: varianta A (ciclicitatea unui punct = cea mai scurta dintre componentele active; scadenta ramane pe punct; recomandat pentru prima etapa) sau varianta B (scadenta pe punct si componenta; task separat, mare)?
8. Contractul insusi primeste lista de componente cu On/Off si ciclicitate proprie (necesar ca actul sa aiba ce prelua); contractele existente raman fara componente pana le completezi. De acord?
9. "Categorii" din cerinta = componentele din nomenclator (nu categoriile de produse din stoc). Confirmi?
10. Ciclicitatea contractului ramane ca valoare implicita pentru componentele fara ciclicitate proprie si pentru puncte; se modifica doar din editarea contractului, nu prin act. De acord?

### 6.11 Decizii noi (revizia 2)

11. **Semantica datei de referinta**: prima scadenta = referinta + ciclicitatea (referinta este inceputul numaratorii, de ex. ultima mentenanta sau inceputul contractului), iar dupa aceea scadentele se muta de interventii cu variantele existente (recomandat); alternativa: referinta este chiar prima scadenta.
12. **Contractele existente**: toate au azi ciclicitate si scadente, dar nu au unitati sau data de referinta. Strict, cum ai cerut, ar deveni toate inghetate in momentul etapei 1 si ar inceta sa produca scadente si notificari. Recomandare: la migrare fiecare contract existent primeste automat o unitate tehnica "Mentenanta generala (de completat)" cu ciclicitatea lui si data de referinta = data contractului, ramane activ, dar apare cu notificarea "informatii lipsa" pana alegi componentele reale; sau le ingheti pe toate imediat (strict).
13. **Ce inseamna inghetat**: fara scadente in lista/harta si fara notificari de scadenta; interventiile se pot inregistra, dar nu muta scadente; contractul ramane editabil si pastreaza acoperirea punctelor. De acord?
14. **Componenta intr-o familie** nu se poate adauga separat din afara familiei; pentru ciclicitate proprie se defineste ca discreta. De acord?
15. **Nomenclatorul**: tabul "Cuvinte cheie mentenanta" devine grupul "Mentenanta" cu sub-taburile "Cuvinte cheie" si "Componente interventii mentenanta". Asa ai vrut transformarea?
16. **Verificarea interventiilor la act**: se verifica interventiile beneficiarului de la data de referinta incoace; cele fara componente se completeaza in fereastra actului (cu propunere din denumirile componentelor); cele necompletate nu opresc salvarea, dar scadenta propusa pentru unitatile lor porneste de la referinta si apare marcata "neverificat". De acord?
17. **Tichete**: componentele sunt obligatorii la preluarea ca Mentenanta daca punctul are contract complet; la un punct fara contract (sau cu contract inghetat) se aleg liber din nomenclator si nu muta scadente. De acord?
18. **Ordinea de lucru**: etapele 0 -> 4; actul aditional (UI) vine ultimul, dupa ce exista componentele si scadentele pe unitate. De acord sa incep cu etapa 0 (nomenclatorul)?
