# Propunere: modul "Propune o modificare" (modificari si functionalitati noi), pornind de la Rapoartele de erori

Data: 9 noiembrie 2026. Stare: propunere, neimplementata. Punct de plecare: modulul `Modules/BugReports` (versiunea 1.1.1) cu tot ce il inconjoara.

## 1. Ce inseamna "tot ce implica" un raport de eroare (inventarul de reutilizat)

| Parte | Cum exista la rapoartele de erori | Reutilizare pentru propuneri |
|---|---|---|
| Intrare | element in meniul lateral, deasupra lui Ajutor; buton "Raporteaza o eroare" in bara de deasupra oricarui popup | element propriu in meniu; buton in aceeasi bara |
| Formular | popup nativ `BugReportDialog`: titlu, gravitate, descriere, imagini (captura automata a paginii, Ctrl+V, fisiere), detalii tehnice, ciorna pastrata | acelasi mecanism de popup, captura si imagini; campuri diferite (sectiunea 3) |
| Imagini | captura din DOM (`wwwroot/bug-report.js`), lipire, fisiere, marire cu overlay + | extras in cod comun |
| "Rapoartele mele" | fila in popup: numar, titlu, gravitate, stare, nota administratorului | idem, cu fir de raspunsuri |
| Administrare | Setari -> Rapoarte erori: file Nerezolvate / Rezolvate, filtre, tabel paginat, stare + nota, stergere, export ZIP | Setari -> Propuneri, aceeasi structura |
| Date | `bug_reports` + `bug_report_files` (imaginile in baza, LONGBLOB), migrarea 073, tabele in lista de backup | tabele proprii (sectiunea 4) |
| Acces | `setari-bug-raport.view/edit/export`, doar administrator; formularul pentru orice utilizator autentificat | `setari-propuneri.view/edit/export` |
| Jurnal | actiuni distincte: creata, stare schimbata, stearsa, exportata; entitate de filtrare | actiuni proprii |
| Modul | `BugReportsModule`, circuit breaker (`Guarded...`), `ModuleGate`; oprit = dispar intrarea si butoanele | modul separat `Propuneri` |
| Alte | help, teste MariaDB, teste de componente, `Teste utilizator/`, harta codului | idem |
| Notificari | **nu sunt implementate** la rapoartele de erori (propunerea originala le preciza) | de hotarat odata pentru ambele (sectiunea 7) |

## 2. Decizia de structura: modul separat, cu parti comune extrase

Optiuni:
- **A. Un camp "Tip" in modulul existent** (Eroare / Modificare / Functionalitate noua). Mai putin cod, dar amesteca stari, camp de gravitate si administrare diferite, iar oprirea modulului ar opri ambele.
- **B. Modul nou `Propuneri` (recomandat)**, cu cele doua zone comune extrase intr-un strat partajat, ca sa nu existe cod copiat:
  1. **scriptul de popup / captura / imagini / marire** (`bug-report.js` devine `feedback.js`, comun);
  2. **componentele de interfata**: `ImageLightbox` (exista deja, comun), lista de imagini cu miniaturi, `ReportImagePicker` (extras din `BugReportDialog`), export ZIP (`ReportZipExport`), numerotarea (`PROP-0001`, `BUG-0001`).
  Modulul de erori ramane neschimbat ca functionalitate; fiecare se opreste independent (regula: modul oprit = toate elementele lui se ascund).

## 3. Formularul "Propune o modificare" (popup)

Camp-uri completate automat (afisate colapsat, ca la erori): pagina curenta (ruta si titlul), utilizator si tip de utilizator, versiunea aplicatiei, data si ora, tema. Fara erorile din browser (nu e o eroare). Atasarea paginii (captura) este implicita, dar se poate scoate.

Campuri completate de utilizator:
- **Tip**: Modificare a unei functii existente / Functionalitate noua / Altceva (selector).
- **Zona aplicatiei**: implicit pagina curenta; se poate schimba din lista paginilor / modulelor (utila cand propunerea nu e despre pagina de unde se scrie) sau "Toata aplicatia".
- **Titlu scurt** (obligatoriu).
- **Ce vrei sa poti face / ce sa se schimbe** (obligatoriu) si **De ce, ce problema rezolva** (optional, dar incurajat: ajuta prioritizarea).
- **Importanta pentru tine**: Mare / Medie / Mica (inlocuieste "gravitatea"; este parerea autorului, nu hotaraste ordinea).
- **Imagini / schite**: captura automata, Ctrl+V, fisiere (aceleasi limite ca la erori), cu marire; optional alte tipuri de fisier (document de specificatie) pe modelul fisierelor de la taskuri (orice tip, descarcare).
- Ciorna pastrata la inchidere si avertizarea la renuntare (mecanismul existent).

Confirmare dupa trimitere: numar `PROP-0042`.

## 4. Date

Migrare noua (nu se modifica migrarile existente), tabele in `MigratedTables` (backup):
- `change_requests`: id, `number`, `kind` (modificare / functionalitate / altceva), `area_route`, `area_title`, `title`, `description`, `benefit`, `wanted_importance`, `state`, `user_name`, `user_role`, `app_version`, `created_utc`, `updated_by`, `updated_utc`, `decided_utc`, `implemented_version` (text, optional), `linked_task_id` (optional, vezi 6), `version` (concurenta).
- `change_request_notes`: istoric de raspunsuri (autor, rol, text, data), in loc de o singura nota - propunerile cer dialog, nu doar un verdict.
- `change_request_files`: ca la erori (imaginile in baza), sau, daca se doreste orice tip de fisier, `entity_files` cu `owner_kind='proposal'` (ca la taskuri). Recomandare: `entity_files`, ca sa existe un singur mecanism de fisiere in loc de al treilea.
- (doar daca se alege varianta cu sustineri, sectiunea 5) `change_request_votes` (propunere, utilizator).

## 5. Stari, vizibilitate si sustinere

Stari: **Noua**, **In analiza**, **Acceptata**, **Planificata**, **Implementata** (cu "in versiunea X"), **Amanata**, **Respinsa** (nota spune de ce). Fila "Active" = Noua, In analiza, Acceptata, Planificata; fila "Inchise" = Implementata, Amanata, Respinsa.

Vizibilitate - **decizie de luat** (afecteaza cat de mult cod si ce date vad utilizatorii):
- **Varianta 1 (ca la erori)**: propunerea o vad autorul si administratorii. Simplu, fara date partajate.
- **Varianta 2 (recomandat daca vrei prioritizare)**: lista tuturor propunerilor este vizibila oricarui utilizator (titlu, stare, autor, zona), iar ceilalti pot "Sustine" o propunere (un clic, reversibil). Administratorul vede numarul de sustineri si ordoneaza dupa el. Evita propuneri duplicate (cautare inainte de a trimite: la scrierea titlului se arata propuneri asemanatoare).

## 6. Administrare (Setari -> Propuneri)

Aceeasi structura ca la rapoartele de erori: file Active / Inchise, filtre (stare, tip, importanta, zona, autor, perioada, dupa sustineri in varianta 2), tabel paginat cu selector de randuri/pagina, ordonare pe Creat / Actualizat / Sustineri, popup de vizualizare cu imaginile marite, schimbarea starii cu nota, fir de raspunsuri, stergere cu motiv, export ZIP (propunerea si lista filtrata, cu `index.csv`). Permisiuni: `setari-propuneri.view / edit / export`, doar administrator.

Integrare cu modulul Taskuri (optional, de hotarat): butonul **"Creeaza task din propunere"** deschide formularul de task cu titlul si descrierea completate si leaga propunerea de task (`linked_task_id`); la finalizarea taskului administratorul poate trece propunerea in "Implementata".

## 7. Notificari (decizie comuna cu erorile)

Rapoartele de erori nu trimit inca notificari. Propun, pentru ambele module, in modulul Notificari existent: (a) la un raport sau o propunere noua -> administratorii (se poate opri din Setari -> Notificari), (b) la schimbarea starii sau un raspuns nou -> autorul, cu insigna in meniul contului ca la taskuri. Fara email sau iesiri din aplicatie (regula: rapoartele raman in aplicatie).

## 8. Interfata: intrare si bara popup-ului

- Meniu lateral: "Propune o modificare" imediat sub "Raporteaza o eroare" si deasupra lui Ajutor.
- Bara de deasupra oricarui popup (cea cu "Raporteaza o eroare" si cercul rosu): al doilea buton, mic, "Propune" (cand modulul e activ). Cand un modul e oprit, butonul lui dispare (regula existenta).
- Formularul poate fi deschis din orice popup; captura include popup-ul deschis (ca la erori).
- Cuvintele din interfata: "Propune o modificare", "Propunerile mele", "Propuneri".

## 9. Jurnal si siguranta

- Actiuni distincte: "Propunere creata", "Propunere: stare schimbata", "Propunere: raspuns adaugat", "Propunere: versiune de implementare setata", "Propunere stearsa", "Propunere exportata", "Propunere: sustinere adaugata / retrasa" (varianta 2); entitate de filtrare "Propuneri".
- Aceleasi reguli de confidentialitate ca la erori: nu se colecteaza parole, chei, cookie-uri sau continutul formularelor; captura se poate scoate inainte de trimitere.
- Limite: dimensiunea textului, numarul si marimea imaginilor/fisierelor, numar de propuneri pe utilizator pe zi (optional, implicit fara limita ca la erori).

## 10. Etape de implementare

1. **Extragerea partilor comune** (JS comun, `ReportImagePicker`, export ZIP, numerotare) fara schimbare de comportament la erori; teste existente trec nemodificate.
2. **Modulul `Propuneri`**: migrarea, repository + `Guarded`, formular, "Propunerile mele", jurnal, permisiuni, intrare in meniu, buton in bara popup-ului.
3. **Administrare**: file, filtre, ordonare, stare + fir de raspunsuri, export ZIP.
4. **Optional**: sustineri + cautare de duplicate; integrare cu taskuri; notificari (impreuna cu erorile).
5. Help, `Teste utilizator/`, teste MariaDB si de componente, harta codului, changelog, versiune de modul.

## 11. Decizii cerute

1. Modul separat (B, recomandat) sau camp "Tip" in modulul de erori (A)?
2. Propunerile sunt private (autor + administrator) sau vizibile tuturor, cu "Sustine" (varianta 2)?
3. Integrarea cu Taskurile ("Creeaza task din propunere") - da / nu / mai tarziu?
4. Notificarile pentru ambele module - acum, cu aceasta lucrare, sau separat?
5. Fisierele atasate: doar imagini ca la erori, sau orice tip de fisier ca la taskuri?

## 12. Deciziile luate (09.11.2026) si ce s-a implementat

1. Module separate (erori / propuneri); sectiunea din Setari "Rapoarte erori" devine "Dezvoltare", cu doua file: Erori si Propuneri.
2. Varianta 2: propunerile (text) si raspunsurile sunt vizibile tuturor utilizatorilor; **fara "Sustine"** (retras ulterior); cautarea de propuneri asemanatoare ramane.
3. Fara buton "Creeaza task din propunere".
4. Notificari: fila "Propuneri" in Notificari (propunere noua pentru administrator; stare schimbata / raspuns nou pentru autor). Notificarile pentru rapoartele de erori nu sunt facute (separat).
5. Fisiere: doar imagini, ca la rapoartele de erori.

Implementat in 0.5.61 (vezi `docs/PROJECT_STATE.md`).
