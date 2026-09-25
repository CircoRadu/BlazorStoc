# Teste rămase de efectuat

> **Numerotarea taskurilor.** Numerele „Task N” din acest fișier sunt cele folosite la momentul implementării. Din 25 septembrie 2026 `TODO.md` nu mai păstrează numere pentru taskurile finalizate, ci denumiri succinte; corespondența este: Task 0 = „Stoc exclusiv prin mișcări de intrare și ieșire”; Task 1 = „Intrări și ieșiri pentru un produs existent” (în perioada timpurie a proiectului „Cod produs”); Task 2 = „Proiecte asociate beneficiarilor”; Task 3 = „Navigarea din jurnal către pagina obiectului”; Task 4 = „Identificarea beneficiarului cu CUI duplicat”; Task 5 = „Confirmarea salvărilor care modifică date existente”; Task 6 = „Confirmarea la părăsirea formularului de adăugare produs”; Task 7 = „Confirmarea deconectării”; Task 8 = „Sincronizarea între utilizatori prin evenimente din baza de date”; Task 9 = „Blocarea temporară a editării unui produs”. Taskul activ „Situația de inventar” (fost Task 10) este acum **Task 1**.

Acest fișier ține evidența verificărilor care **nu au putut fi efectuate** până acum, cu motivul pentru care nu s-au putut face și cu pașii exacți prin care se pot efectua. Suita automată (`tests/BlazorStoc.Checks`, 388 de verificări la 25 septembrie 2026) și verificările manuale din `VALIDARE.md` acoperă restul.

## Cum se folosește și cum se actualizează

- Fiecare test are un identificator stabil (de exemplu `A2`). Nu renumerota testele; un test nou primește următorul număr liber din grupa lui.
- Când un test este efectuat, mută-l în secțiunea **Teste efectuate** de la final, cu data, persoana și rezultatul (trecut/eșuat, cu observații). Un test eșuat generează un task în `TODO.md`.
- Când o lucrare nouă lasă ceva neverificat, adaugă aici un test nou (motiv + pași) și trimite din `VALIDARE.md` la identificatorul lui.
- Nu introduce niciodată parole reale în acest fișier. Conturile fictive ale modului demonstrativ sunt afișate pe pagina de autentificare și descrise în `README.md`.

## Motivele pentru care testele nu s-au putut efectua

| Cod | Motiv |
|---|---|
| **M1** | Nu există un server MariaDB în mediul de lucru al agentului și nici acces la unul (nici la backup-ul real). Proiectul nu folosește Docker, iar integrarea NAS/QNAP este în afara etapei curente. |
| **M2** | Testul cere o a doua stație, un al doilea cont autentificat separat sau un utilizator cu drepturi limitate; în panoul Browser al agentului există o singură sesiune autentificată (contul administrator fictiv), iar agentul nu introduce parole. |
| **M3** | Automatizarea panoului Browser nu poate acționa elementul: ferestrele native (calendarul), gesturile tactile, tastatura pe elemente native sau redimensionarea la dispozitive reale. |
| **M4** | Testul cere un alt browser decât cel din panou (Chromium); utilizatorul a confirmat manual numai Chrome și Brave. |
| **M5** | Testul ar modifica ireversibil date sau ar încheia sesiunea autentificată a utilizatorului din panou; se face doar de utilizator sau pe o copie a datelor. |
| **M6** | Testul cere timp îndelungat sau condiții greu de provocat (întreruperea rețelei, zile de funcționare, oprirea unui serviciu). |

## Pregătirea mediului

### Mediul demonstrativ (SQLite) — pentru grupele B–G

```powershell
cd BlazorStoc
dotnet build BlazorStoc.csproj -c Release
.\bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082
```

Autentificare cu un cont fictiv de pe pagina de login (`README.md`). Baza locală este `data\blazorstoc-local.db`; pentru teste care modifică date lucrează pe o copie (`App__LocalDatabasePath` către alt fișier) sau șterge datele de test prin fluxurile normale ale aplicației.

### Mediul MariaDB — pentru grupa A

Folosește **o copie** a bazei (restaurată din backup), niciodată baza de producție.

1. Creează baza: rulează `database\BlazorStoc_create.sql`, apoi `database\BlazorStoc_upgrade_0.2.sql` (sau restaurează backup-ul real într-o bază `BlazorStoc` nouă).
2. Creează un cont pentru aplicație cu privilegiile `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, INDEX, TRIGGER, REFERENCES` pe baza `BlazorStoc` (aplicația creează la prima folosire tabelele `project*`, `web_user*`, `product_lock`, `change_events` și trigger-ele).
3. Alege un identificator existent din tabela `user` pentru `Database__ApplicationUserId` (operatorul înregistrat în mișcări).
4. Pornește aplicația pe alt port decât preview-ul demonstrativ:

```powershell
$env:App__DemoMode = "false"
$env:Database__Host = "127.0.0.1"
$env:Database__User = "<contul aplicației>"
$env:Database__Password = "<parola contului>"          # nu se salvează în fișiere
$env:Database__SslMode = "Disabled"                     # numai pentru un server local de test
$env:Database__ApplicationUserId = "<id din tabela user>"
$env:Authentication__Username = "admin"
$env:Authentication__Password = "<minimum 12 caractere>"
.\bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5083
```

5. Autentifică-te cu `Authentication__Username` / `Authentication__Password` (administratorul de inițializare).

---

## A. MariaDB pe un server real (motiv: M1)

Tot codul MariaDB este scris și compilat, iar regulile comune sunt acoperite pe SQLite, dar nicio instrucțiune MariaDB nu a rulat pe un server real.

### A1. Pornirea și crearea schemei
- **Verifică**: inițializarea (`MariaArchiveSchema`, tabelele `project`, `project_observation`, `project_observation_file`, `web_user*`, `product_lock`, `change_events`, coloanele adăugate la `io`) rulează fără erori pe o bază existentă.
- **Pași**: pornește aplicația în modul MariaDB (secțiunea de pregătire); deschide pe rând Produse, Beneficiari, Utilizatori, un produs (pagina de mișcări), un proiect, Jurnal; urmărește `preview.stdout.log`/consola.
- **Așteptat**: nicio excepție; `SHOW TABLES` conține tabelele de mai sus; `SHOW COLUMNS FROM io` conține `id_project`, `io_versiune`, `io_created_utc`, `io_updated_utc`, iar `io_numar_bucati` este `INT` (nu `tinyint`).
- **Sursă**: Task 1 (subtask 1.3), Task 2.

### A2. Formatul datelor din backup (`io_data`, `io_tip_actiune`)
- **Verifică**: ipotezele preluate din aplicația veche (`SQLWrapper.cs`, `IO.cs`): `io_data` are formatul `dd-MM-yyyy`, iar `io_tip_actiune = 1` înseamnă intrare (celelalte valori = ieșire).
- **Pași**: în copia bazei, rulează `SELECT io_data, io_tip_actiune, io_numar_bucati, io_descriere FROM io ORDER BY id_io DESC LIMIT 50;`; deschide în aplicație pagina de mișcări a unui produs cu mișcări vechi și compară data, tipul și cantitatea cu ce afișează aplicația.
- **Așteptat**: datele și tipurile coincid cu rândurile din bază; nicio mișcare veche nu apare cu dată invalidă sau cu tip inversat; stocul afișat = suma intrărilor − suma ieșirilor.
- **Notă**: dacă backup-ul folosește alt format, actualizează `StockMovementRules.ParseLegacyDate` și maparea din `MariaStockMovementRepository`.

### A3. Beneficiari: CUI duplicat, inclusiv la cereri concurente
- **Verifică**: mesajul „Există deja un beneficiar cu acest CUI: «nume».” cu numele salvat, la creare și editare, și traducerea erorii 1062 a indexului `UX_beneficiar_cui` când două salvări ajung simultan.
- **Pași**: (1) creează un beneficiar cu CUI `RO12345678`; (2) încearcă să creezi altul cu același CUI și alt nume → verifică mesajul; (3) editează alt beneficiar către acel CUI → același mesaj, formularul rămâne cu valorile; (4) concurență: din două tab-uri completează formularul de creare cu același CUI nou și apasă „Salvează” cât mai simultan (repetă de 5–10 ori sau folosește două scripturi paralele) → o singură salvare reușește, cealaltă primește mesajul cu numele beneficiarului creat.
- **Așteptat**: niciodată două rânduri cu același CUI; mesajul conține numele existent și în cazul concurent (nu un mesaj generic sau o eroare 500).
- **Sursă**: Task 4.

### A4. Mișcări de stoc: atomicitate, corecții, arhivare
- **Verifică**: `MariaStockMovementRepository` — stocul (`produs_cantitate`) se schimbă atomic cu mișcarea; editarea/ștergerea corectează stocul; istoricul (`io_history`) și arhiva (`archive_stock_movements`) se scriu în aceeași tranzacție.
- **Pași**: pe un produs de test: (1) adaugă intrare 10 și ieșire 4 → stoc +6; (2) editează intrarea la 12 cu motiv → stoc +8, apare `[*]` și istoric prin click dreapta; (3) șterge o mișcare (ștergere în doi pași) → stoc corectat, rând în `archive_stock_movements`; (4) în două tab-uri, adaugă simultan mișcări pe același produs (8–10 repetări) și compară stocul din `produs` cu suma din `io`.
- **Așteptat**: `produs_cantitate` = suma mișcărilor; nicio mișcare pierdută; nicio mișcare parțial arhivată.
- **Sursă**: Task 1.

### A5. Proiecte, observații și fișiere
- **Verifică**: `MariaProjectRepository` / `MariaProjectFileStore`: unicitatea denumirii per beneficiar (index `ux_project_beneficiary_name`, mesajul de concurență), versionarea, arhivarea proiectului cu observațiile și fișierele, blocarea ștergerii beneficiarului cu proiecte și a proiectului cu mișcări, încărcarea/descărcarea fișierelor pe disc.
- **Pași**: creează proiect, două observații, încarcă un fișier (fișier mic text și unul de câțiva MB), descarcă-l și compară hash-ul (`Get-FileHash`); încearcă un proiect cu aceeași denumire (cu alte majuscule/diacritice) → respins; leagă o ieșire de stoc de proiect → încearcă să ștergi proiectul → blocat; șterge un proiect fără mișcări → arhivat (rânduri în `archive_projects`, `archive_project_observations`, `archive_project_observation_files`, fișier mutat în arhivă).
- **Așteptat**: fișierul descărcat este identic; regulile de mai sus se aplică; jurnalul conține evenimentele de adăugare/editare/ștergere.
- **Sursă**: Task 2.

### A6. Utilizatori web și autentificare
- **Verifică**: `MariaUserRepository` (`web_user`, `web_role`, `web_user_audit`): creare, editare (rol, activ, parolă nouă), ștergere, regula „cel puțin un administrator activ”, autentificarea cu conturile create.
- **Pași**: creează un utilizator cu drepturi limitate; autentifică-te cu el în altă fereastră privată; încearcă să dezactivezi/ștergi ultimul administrator → respins; schimbă parola și reautentifică-te.
- **Așteptat**: regulile respectate; utilizatorul limitat nu vede meniul Utilizatori și primește refuz pe `/utilizatori` și `/utilizatori/{id}`.
- **Motiv suplimentar**: M2.

### A7. Sincronizarea prin trigger-e pe MariaDB (Task 8)
- **Verifică**: `MariaChangeEventSource` creează `change_events` și trigger-ele `trg_chg_*` pe `produs`, `io`, `web_user`, `project`, `project_observation`, `project_observation_file`; relay-ul citește evenimentele; modificările făcute de o **aplicație externă** (aplicația veche WinForms sau un client SQL) apar în paginile deschise.
- **Pași**: (1) după pornire: `SELECT TRIGGER_NAME FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA='BlazorStoc' AND TRIGGER_NAME LIKE 'trg_chg_%';` → 18 trigger-e (6 tabele × 3 operații) — dacă tabelele `web_user*`/`project*` nu existau la pornire, apar la cel mult 5 minute după ce sunt create; (2) deschide pagina unui produs în browser; într-un client SQL rulează `UPDATE produs SET produs_denumire = CONCAT(produs_denumire,' [EXT]') WHERE id_produs = <id>;` → pagina se actualizează în ~2–3 s; readu denumirea; (3) `INSERT INTO io ...` (mișcare nouă) pentru același produs → stocul/lista se reîmprospătează; (4) cu contul aplicației **fără** privilegiul `TRIGGER`, pornește aplicația → în consolă apare un singur avertisment „Change events could not be read…”, iar paginile continuă să funcționeze cu sincronizarea periodică.
- **Așteptat**: exact un eveniment per modificare; `change_events` conține numai identificatori; evenimentele mai vechi de 24 h sunt șterse.
- **Sursă**: Task 8.

### A8. Blocarea editării pe MariaDB (Task 9)
- **Verifică**: `MariaProductLockRepository` (tabela `product_lock`): obținere atomică, reînnoire, expirare cu `UTC_TIMESTAMP(6)`, `INSERT IGNORE` la cereri simultane, deblocare forțată cu jurnal în `audit_events`.
- **Pași**: repetă scenariul de la `VALIDARE.md` (Task 9) cu două sesiuni: banner + „Editează” dezactivat, eliberare live, deblocare forțată cu motiv, heartbeat (`SELECT renewed_utc, expires_utc, UTC_TIMESTAMP(6) FROM product_lock;` la 30 s), închiderea tabului în timpul editării → lease expirat după ~90 s; cereri simultane de lock din două tab-uri apăsând „Editează” cât mai simultan.
- **Așteptat**: un singur editor activ; timpul rămas afișat corect chiar dacă ceasul serverului aplicației diferă de al bazei.

---

## B. Mai mulți utilizatori și mai multe calculatoare (motiv: M2, M6)

### B1. Două calculatoare, două conturi distincte
- **Verifică**: sincronizarea (Task 8) și blocările (Task 9) între utilizatori reali, nu între două tab-uri ale aceluiași cont.
- **Pași**: pornește aplicația pe un calculator accesibil în rețea (`--urls http://0.0.0.0:5082`, cu regula de firewall necesară); pe un al doilea calculator autentifică-te cu alt cont; (1) A editează un produs, B vede „🔒 Produsul este editat de <contul A> din <ora>” și „Editează” dezactivat; A anulează → B primește imediat „eliberat”; (2) A salvează o modificare a numelui/descrierii → B vede catalogul actualizat fără acțiune; (3) B (fără formular deschis) și A (cu formular deschis pe același produs): notificarea „Datele au fost modificate…” apare la A fără să-i schimbe formularul; (4) A (administrator) forțează deblocarea unui produs editat de B → B vede „Blocarea editării a fost pierdută” și la salvare primește verificarea de versiune dacă între timp s-a modificat produsul.
- **Așteptat**: comportamentul din `VALIDARE.md`, cu numele reale ale conturilor.

### B2. Utilizator cu drepturi limitate față de administrator
- **Pași**: cu un cont limitat: (1) meniul Utilizatori lipsește; `/utilizatori`, `/utilizatori/1` și linkul din jurnal → refuz; `/jurnal` este accesibil doar administratorului; (2) pagina unui produs editat de altcineva: butonul „Deblochează (administrator)” **nu** apare; (3) încercarea directă a operației (de exemplu prin `?edit=<id>` pe un produs blocat) → mesajul de blocare, fără formular; (4) paginile beneficiar/proiect/observație funcționează conform regulilor existente.
- **Așteptat**: nicio pagină nu expune date sau acțiuni de administrator.

### B3. Recuperarea după întreruperea conexiunii (SignalR / rețea)
- **Verifică**: „O întrerupere temporară SignalR este recuperată prin sincronizarea periodică” (Task 8) și comportamentul blocării când clientul dispare.
- **Pași**: cu pagina catalogului deschisă pe al doilea calculator, deconectează cablul/Wi-Fi 30–60 s (sau oprește temporar rețeaua), modifică un produs din primul calculator, reconectează → pagina afișează modificarea în cel mult 60 s (sau după reconectarea Blazor); repetă cu formular de editare deschis pe produsul blocat: la reconectare rapidă (<90 s) editarea continuă cu lock-ul reînnoit; la deconectare >90 s lock-ul expiră și editorul primește „Blocarea editării a fost pierdută”.
- **Așteptat**: fără date pierdute sau suprascrise; verificarea de versiune blochează o salvare peste o modificare străină.

---

## C. Interfață în browser neexersată (motiv: M5, M3)

### C1. Confirmarea salvării în fiecare editor (Task 5)
- **Verifică**: dialogul „Salvezi modificările …?” în editoarele care nu au fost exersate (verificat în browser numai la beneficiar).
- **Pași**: pentru **produs** (schimbă descrierea și înlocuiește imaginea), **proiect**, **observație**, **utilizator** (schimbă rolul și parola nouă — parola trebuie să apară doar ca „va fi schimbată”), **categorie**, **subcategorie** (mută în altă categorie) și **mișcare de stoc** (dialogul de editare + al doilea popup peste el): deschide editorul, modifică, apasă „Salvează” → verifică tabelul Câmp / Valoare actuală / Valoare nouă; „Anulează” (și Escape) → nu se salvează nimic, formularul rămâne; „Confirmă salvarea” → se salvează.
- **Așteptat**: numai câmpurile schimbate în tabel; motivarea afișată; crearea unui produs/beneficiar/proiect nou nu afișează popup.
- **De ce nu s-a făcut**: fiecare confirmare modifică date; agentul a confirmat efectiv doar la un beneficiar (readus la valoarea inițială).

### C2. Salvarea și ștergerea efectivă din pagina produsului (revenire în pagina de origine)
- **Pași**: din pagina unui produs de test → „Editează” → modifică → „Salvează” → „Confirmă salvarea” → trebuie să revii în **pagina produsului**; apoi creează un produs de test, deschide-i pagina → „Șterge produs” → confirmă în doi pași → revii în **catalog** (produsul nu mai există).
- **Așteptat**: cele două redirecționări de mai sus; produsul șters apare în arhivă.

### C3. Formular „Adaugă produs”: imaginea păstrată după anulare (Task 6)
- **Pași**: deschide „Adaugă produs”, alege o imagine (sau lipește din clipboard), completează câmpurile, apasă o categorie din meniu → „Continuă adăugarea” → verifică previzualizarea imaginii și câmpurile.
- **Așteptat**: imaginea și valorile rămân; „Părăsește adăugarea” închide formularul fără a salva.
- **De ce nu s-a făcut**: încărcarea unui fișier din dialogul nativ de fișiere nu poate fi acționată din panoul Browser.

### C4. Liste și pagini cu volum mare
- **Pași**: creează 11–25 de proiecte pentru un beneficiar → verifică paginarea din pagina beneficiarului (`?pagina=2`, filtrul `?q=`) și revenirea cu Înapoi; creează >10 mișcări pe un produs → filtrul Intrări/Ieșiri, sortarea după dată și paginarea (10/20/50/Toate); ieșire cu beneficiar **și proiect** din formularul de mișcare.
- **Așteptat**: starea listei în adresă și restaurată prin Înapoi; „Total produse în stoc” corect.
- **Notă**: aceste comportamente sunt acoperite de verificările automate ale repository-urilor, dar nu au fost exersate în browser.

### C5. Pagina observației și a proiectului când altă sesiune le șterge
- **Pași**: deschide pagina unei observații în tab A; în tab B șterge proiectul (ștergere în doi pași); urmărește tab A.
- **Așteptat**: tab A afișează „Proiectul a fost șters de alt utilizator” / „Observația nu mai există”, fără excepții; un formular deschis nu este șters, iar salvarea este respinsă.

### C6. Jurnalul: poziția de derulare și paginare prin adresă (Task 3)
- **Pași**: în `/jurnal` cu >50 de evenimente: `?pe-pagina=10&pagina=3&q=...`, derulează, apasă un link „Țintă”, apoi Înapoi.
- **Așteptat**: filtrele și pagina se restaurează, iar poziția de derulare revine o singură dată.

### C7. Editarea unui beneficiar către un CUI existent, în browser (Task 4)
- **Pași**: „Editează” la un beneficiar → schimbă CUI-ul într-unul existent → motivare → „Salvează” → „Confirmă salvarea”.
- **Așteptat**: mesajul „Există deja un beneficiar cu acest CUI: «nume».”; formularul rămâne cu valorile; beneficiarul editat fără schimbarea CUI-ului nu este raportat ca duplicat.

### C8. Respingerea datei viitoare de către server
- **Verifică**: mesajul `StockMovementRules.FutureDateMessage` afișat în interfață (browserul blochează singur data viitoare, deci serverul nu este atins).
- **Pași**: cu instrumentele de dezvoltare ale browserului elimină atributul `max` de pe câmpul ascuns `input[type=date]` al formularului de mișcare, alege o dată viitoare și apasă „Adaugă”.
- **Așteptat**: „Data mișcării nu poate fi în viitor…”; nimic salvat.
- **De ce nu s-a făcut**: regula este verificată automat pe domeniu și pe SQLite; testul din browser cere manipularea DOM-ului.

### C9. Redirecționarea `inapoi` (securitate)
- **Pași**: deschide `/produse?edit=1&inapoi=https://exemplu.invalid` și `/produse?edit=1&inapoi=//exemplu.invalid`, apoi „Anulează”.
- **Așteptat**: se revine în catalog (adresa externă este ignorată).

---

## D. Tastatură și accesibilitate (motiv: M3)

### D1. Calendarul „Data mișcării” cu tastatura
- **Pași**: cu Tab ajunge pe câmp; Enter, Spațiu, F4 și Alt+Săgeată jos deschid calendarul; în calendar Săgeți + Enter aleg ziua; Escape îl închide; verifică că nu se poate tasta în câmp.
- **Așteptat**: valoarea se schimbă doar din calendar; zilele viitoare sunt dezactivate.
- **De ce nu s-a făcut**: calendarul este o fereastră nativă; panoul Browser nu îi poate trimite click-uri (s-a verificat doar cu tastatura în popup, Săgeată stânga + Enter). Utilizatorul a confirmat manual în Chrome și Brave.

### D2. Escape, focus și Tab în dialoguri
- **Pași**: pentru dialogurile „Salvezi modificările?”, „Părăsești adăugarea produsului?”, „Închizi sesiunea?”, „Deblochezi produsul?” și ștergere: la deschidere focusul este pe dialog/pe butonul sigur; Tab rămâne în dialog (nativ pentru `<dialog>`; verifică și celelalte); Escape = anulare/continuare; după închidere, focusul revine într-un loc logic.
- **Așteptat**: fără capcane de focus, fără acțiuni distructive la Escape.

### D3. Cititor de ecran
- **Pași**: cu NVDA/Narrator parcurge dialogurile de mai sus, bannerele de blocare și notificările „Datele au fost modificate” (`role="status"`).
- **Așteptat**: titlul și descrierea dialogului sunt citite la deschidere; notificările sunt anunțate fără a fura focusul.
- **De ce nu s-a făcut**: nu există un cititor de ecran în mediul agentului.

---

## E. Alte browsere și dispozitive (motiv: M4, M3)

### E1. Firefox, Safari și Edge
- **Pași**: în fiecare browser: autentificare, catalog, adăugare mișcare (calendarul cu `showPicker`), dialogurile `<dialog>` (deconectare), popup-urile de confirmare, meniul produselor cu formularul „Adaugă produs” deschis.
- **Așteptat**: același comportament ca în Chrome/Brave. Atenție la: `HTMLInputElement.showPicker` (Safari mai vechi), `<dialog>` modal, `autofocus` în dialog.

### E2. Telefon / tabletă (tactil) și lățimi mici
- **Pași**: pe un dispozitiv real (sau emulare Chrome DevTools 375×812 și 768×1024): dialogurile (`SaveConfirmationDialog`, `ForceUnlockDialog`, deconectare) să nu depășească ecranul și să poată fi derulate; calendarul se deschide la atingere; bannerul de blocare și butoanele se rup pe rânduri; meniul produselor și popup-ul de părăsire funcționează la atingere.
- **Așteptat**: fără depășire orizontală a paginii; acțiunile rămân accesibile.

---

## F. Funcționare îndelungată și scenarii rare (motiv: M6)

### F1. Retenția evenimentelor de sincronizare
- **Pași**: lasă aplicația pornită >1 h cu modificări periodice; `SELECT COUNT(*), MIN(created_utc) FROM change_events;` (SQLite: `data\blazorstoc-local.db`).
- **Așteptat**: după 24 h de la creare evenimentele procesate sunt șterse; tabelul nu crește nelimitat.

### F2. Repornirea aplicației cu blocări active
- **Pași**: începe o editare (lock activ), oprește procesul aplicației brusc (`Stop-Process`), repornește-l; verifică `product_locks` / `product_lock`.
- **Așteptat**: rândul rămâne, dar nu mai blochează după `expires_utc` (max. 90 s); un alt utilizator poate prelua editarea; nu apar erori la pornire.

### F3. Ștergerea în cascadă și evenimentele derivate
- **Pași**: șterge un proiect cu observații și fișiere, cu o a doua sesiune deschisă pe pagina proiectului și alta pe pagina beneficiarului; urmărește notificările.
- **Așteptat**: fără notificări false pentru sesiunea care a făcut ștergerea; celelalte sesiuni se actualizează o singură dată (evenimentele derivate ale observațiilor/fișierelor pot apărea fără origine — acceptat, verifică să nu producă erori).

---

## Teste efectuate

| Data | Test | Efectuat de | Rezultat |
|---|---|---|---|
| 25.09.2026 | Butonul roșu „Deconectare” din popup (Task 7) | utilizatorul | Trecut: duce la pagina de autentificare |
| 25.09.2026 | Calendarul „Data mișcării” (citire, doar din calendar, fără date viitoare) | utilizatorul | Trecut în Chrome și Brave |
| 25.09.2026 | Blocarea editării: banner, eliberare live, deblocare forțată cu jurnal, heartbeat, pierdere, expirare după închiderea tabului (SQLite, două sesiuni ale aceluiași cont) | agentul (Claude) | Trecut; detalii în `VALIDARE.md` |
| 25.09.2026 | Sincronizare: modificare SQL externă, dialog deschis cu notificare, catalog live în două tab-uri, client SignalR real | agentul (Claude) | Trecut; detalii în `VALIDARE.md` |
