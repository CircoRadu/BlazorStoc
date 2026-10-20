# Propunere: Setari -> Baza de date (conexiune, stare, verificare, repornire/oprire)

Data: 20.10.2026. Document de analiza si propunere, fara cod implementat. Starea codului este cea de la `docs/PROJECT_STATE.md` (10.10.2026).

## 1. Cerinta (asa cum am inteles-o)

O pagina noua in Setari, accesibila doar administratorului, cu:

1. configurarea accesului la baza de date: host, port, utilizator, parola (si portul pe care ruleaza aplicatia);
2. informatii despre starea bazei de date si "de unde ruleaza" (server, versiune, masina, aplicatie);
3. verificare de sanatate si integritate a bazei;
4. repornirea "pipeline-ului" de acces la date;
5. repornirea si oprirea aplicatiei;
6. acces numai pentru administrator, fara posibilitatea de a acorda accesul din Utilizatori.

## 2. Ce exista deja (se refoloseste)

| Element | Unde | Folosire |
|---|---|---|
| Conexiunea se citeste de fiecare data din `IConfiguration` (`Database:Host/Port/Name/User/Password/SslMode`) | `Services/DatabaseConnections.cs` (`Create`, `CreateMigrator`) | Schimbarea se poate aplica **fara repornire**: MySqlConnector tine cate un pool pe sir de conexiune, deci un sir nou inseamna un pool nou |
| Parola vine din fisier privat JSON, nu din `appsettings.json` | `Program.cs` (`application-connection.private.json`, `Database:PrivateConfigPath`) | Pagina scrie intr-un fisier de suprascriere, nu in fisierul din repository |
| Administratorul "configurat" (`Authentication:Username/Password`) se autentifica **fara SQL** | `Pages/Account/Login.cshtml.cs` | Acces la pagina chiar daca baza de date este cazuta - exact cand e nevoie de ea |
| Verificare de sanatate: `/health` (SELECT 1), `/health/live` | `Program.cs` | Punct de plecare pentru testul de conexiune |
| Raport de migrari si coloane lipsa | `MariaSchemaMigrator.MigrateAsync/FindMissingColumnsAsync` | Parte din verificarea de sanatate |
| Blocare de intretinere pentru toate sesiunile + pagina de asteptare | `OperationLock.cs`, `MaintenanceGate.cs`, `wwwroot/maintenance-watch.js` | Reutilizat la reconfigurare/repornire (utilizatorii vad "intretinere", nu erori) |
| Parole protejate cu Data Protection (modelul NAS) | `NasBackup.cs` (`MariaNasBackupStore`) | Acelasi model pentru parola salvata din pagina |
| Jurnal cu actiuni dedicate, filtru, `IsCreateOrEdit` | `Services/AuditTrail.cs`, pagina Audit | Reguli din `CLAUDE.md` (jurnalizare detaliata) |
| Tab-uri Setari, vizibile doar administratorului | `Components/Pages/Settings.razor` (`Tabs`, `isAdmin`) | Tab nou "Baza de date" |

## 3. Constatari care schimba proiectarea

1. **Setarile conexiunii nu pot sta in baza de date** (problema oului si a gainii: aplicatia are nevoie de ele ca sa ajunga la baza). Se pastreaza intr-un fisier local, in afara repository-ului (de ex. `%LOCALAPPDATA%\BlazorStoc-MariaDB\database-connection.override.json`), incarcat ULTIMUL in configuratie, cu `reloadOnChange`. Parola se salveaza protejata (Data Protection, ca la NAS), cu un furnizor de configurare care o decripteaza la citire. Daca cheile lipsesc sau fisierul e corupt, aplicatia ignora suprascrierea si porneste cu fisierul de baza (nu se blocheaza la pornire).
2. **"Administrator" nu inseamna doar rolul.** In Utilizatori (`UserEditor`) orice cont din baza poate primi rolul "Administrator". Cerinta "fara permisiuni acordate in utilizatori" se rezolva cu o politica separata: numai contul **administratorului configurat** (cel din fisierul privat, autentificat fara SQL). La login se adauga un claim propriu (`config_admin`) doar pe ramura "Administrator configurat"; pagina si serviciile cer acest claim, nu rolul. In Utilizatori nu apare nicio optiune pentru aceasta pagina (se scrie explicit in pagina). Consecinta: daca parola administratorului configurat se pierde, nimeni altcineva nu ajunge aici - se reface din fisierul privat de pe server.
3. **Contul aplicatiei (`blazorstoc_dev`) are doar SELECT/INSERT/UPDATE/DELETE.** Din cauza asta:
   - citirea starii merge (`SELECT VERSION()`, `@@port`, `@@datadir`, `SHOW GLOBAL STATUS`, `information_schema.TABLES`);
   - `CHECK TABLE` ar trebui sa mearga cu SELECT+INSERT (de confirmat pe instanta reala; nu am putut rula MariaDB aici, mediul curent e Linux, proiectul e pentru Windows);
   - NU merg `SHOW ENGINE INNODB STATUS` (PROCESS), `SHUTDOWN`, `REPAIR`/`OPTIMIZE`, crearea de conturi;
   - un utilizator isi poate schimba **propria** parola (`ALTER USER CURRENT_USER() IDENTIFIED BY ...`) fara drepturi suplimentare.
4. **Aplicatia nu are supervizor.** Azi porneste cu `start-preview.ps1` (`Start-Process`), nu ca serviciu Windows. Oprirea este usoara (`IHostApplicationLifetime.StopApplication()`), **repornirea nu**: procesul care se opreste nu se poate reporni singur. Comentariul din `/health` ("for the supervisor") arata ca un supervizor era avut in vedere, dar nu exista.
5. **Portul aplicatiei se citeste la pornire** (Kestrel): schimbarea lui cere repornire, spre deosebire de datele conexiunii la baza. Scripturile de preview il dau explicit (`-Port 5087` / variabila de mediu), deci suprascrierea din pagina trebuie sa stie cand este ignorata si sa spuna asta.
6. Nu exista numarare de sesiuni Blazor deschise (nu exista `CircuitHandler`); pentru "cati utilizatori sunt conectati" trebuie adaugat unul.

## 4. Propunere functionala

Tab nou "Baza de date" in Setari, vizibil numai administratorului configurat, cu patru sub-taburi (modelul `SubtabBar` existent).

### 4.1. Stare (implicit)

- **Baza de date**: stare (Conectat / Indisponibil / In intretinere), latenta (ms), versiune server, host:port, nume baza, utilizator (fara parola), `SslMode` si cifrul negociat, "ruleaza local sau pe alta masina" (host rezolvat = loopback sau nu), numele masinii serverului (`@@hostname`), sistem de operare (`@@version_compile_os`), pornit de la (uptime), conexiuni active, marimea bazei, numar de tabele/triggere, calea datelor (`@@datadir`), ora serverului.
- **Schema**: migrari aplicate / asteptate, coloane lipsa, contul de migrare configurat (da/nu, fara valori), contul de backup si de restaurare configurate (da/nu).
- **Aplicatia**: masina, sistem, versiune .NET, proces (PID, pornit la, uptime), adrese pe care asculta, portul configurat vs efectiv, mediul, radacina fisierelor, sesiuni deschise (prin `CircuitHandler`), memorie, ultimul backup (varsta, rezultat), stare lacat de intretinere.
- Buton "Actualizeaza" (`act-reload`) si reimprospatare automata la 10 s doar cat tab-ul este deschis.

### 4.2. Conexiune

Campuri: host, port baza de date, nume baza, utilizator, parola (niciodata afisata; "neschimbata" pana se tasteaza una noua), `SslMode` (Required implicit; sub Required refuzat pentru alt host decat loopback), portul aplicatiei (vezi 4.5).

Fluxul de salvare (siguranta inainte de comoditate):

1. Pasul de confirmare a identitatii: se cere din nou parola administratorului configurat (sesiunea tine 8 ore).
2. **Test** cu valorile noi, fara a le salva: conectare, `SELECT 1`, versiune, existenta tabelelor cheie, drepturile contului (SELECT/INSERT/UPDATE/DELETE pe schema), schema la zi. Salvarea este permisa numai dupa un test reusit.
3. Se ia lacatul de intretinere (`OperationLock`, operatia "Reconfigurare conexiune baza de date"): sesiunile deschise vad pagina de asteptare.
4. Se scrie fisierul de suprascriere (atomic: fisier temporar + inlocuire), se pastreaza vechea valoare ca "ultima configuratie functionala".
5. `MySqlConnection.ClearAllPools()`, retest. Daca esueaza: **revenire automata** la ultima configuratie functionala, mesaj clar, nimic pierdut.
6. Se elibereaza lacatul; jurnal.
- Optional ("Schimba parola contului in baza de date"): `ALTER USER CURRENT_USER() IDENTIFIED BY ...` cu parola noua, apoi test cu ea; secventa este scrisa astfel incat un esec in orice pas sa lase parola veche functionala. Fara aceasta optiune, pagina schimba doar ce foloseste aplicatia ca sa se conecteze, nu parola din server (se scrie explicit in pagina).
- Contul de migrare/backup/restaurare: raman in fisierele lor private; folosesc acelasi host/port, deci se muta odata cu ele. Parolele lor nu se editeaza din pagina in prima versiune.

### 4.3. Verificare (sanatate si integritate)

Rulare pe pasi, cu progres, anulabila, un singur rulaj o data, rezultat pe linii (OK / Atentie / Eroare) si "Copiaza raportul":

1. Conectivitate si latenta (3 sondaje).
2. Schema la zi (migrari, coloane lipsa).
3. Structura: tabelele si triggerele asteptate (lista din `MariaArchiveSchema`) exista; nimic in plus neasteptat.
4. `CHECK TABLE` pe fiecare tabel (nivel "rapid" sau "mediu", ales; progres pe tabel).
5. Chei externe: cautare de randuri orfane generata din `information_schema.KEY_COLUMN_USAGE` (restaurarea importa cu verificarea cheilor oprita, deci merita).
6. Reguli de coerenta ale aplicatiei, ca lista extensibila (ex. stoc negativ nelamurit - exista deja `NegativeStockPanel`; miscari fara produs).
7. Backup: varsta ultimului pachet, hash verificat, spatiu liber in directorul de backup; ceasul serverului vs referinta (`TrustedClock`).
8. Lacat de operatie orfan.

Pagina nu ofera "Repara" (contul nu are drepturi si repararea fara copie este riscanta): la eroare recomanda restaurarea din pachetul de backup (pagina existenta Backup si restaurare). Rezultatul ultimei rulari se pastreaza (fisier JSON langa suprascriere; tabel doar daca se cere istoric) si se jurnalizeaza ca sumar.

### 4.4. Repornire pipeline (interpretarea mea - de confirmat)

"Pipeline" = lantul de acces la date. "Repornire" in doua trepte:

- **Repornire usoara** (fara oprirea procesului): lacat de intretinere -> inchiderea pool-urilor -> reincarcarea configuratiei -> retest -> reluarea verificarii de schema/migrari -> golirea cache-urilor (ex. `CachedSupplierRepository`) -> repornirea serviciilor de fundal care tin stare (`BackupScheduler`, `ChangeEventRelay`) acolo unde nu citesc singure configuratia -> eliberare lacat. Folosita dupa o cadere a bazei, ca sa nu fie nevoie de repornirea aplicatiei.
- **Repornire completa** = 4.5.

### 4.5. Aplicatie: port, oprire, repornire

- **Portul aplicatiei**: camp cu validare (1024-65535, liber, interfata 127.0.0.1 implicit; 0.0.0.0 doar cu avertizare explicita). Se salveaza in aceeasi suprascriere si se aplica la urmatoarea pornire; pagina arata "port configurat" vs "port efectiv" si spune daca scriptul de pornire il impune. Dupa repornire, o pagina de la adresa veche arata noua adresa. Daca noul port nu se poate lega la pornire, aplicatia revine la ultimul port functional si logheaza motivul (altfel ar ramane oprita).
- **Oprire**: confirmare cu tastarea cuvantului "OPRESTE" si afisarea numarului de sesiuni deschise; refuzata cat timp ruleaza backup/restaurare (lacat activ). Dupa oprire nu mai exista nicio cale din browser de repornire - pagina o spune inainte.
- **Repornire**: aceleasi garantii, apoi `StopApplication()` si relansare. Doua variante de relansare:
  - **A (recomandata pe termen scurt)**: aplicatia lanseaza un proces ajutator desprins care asteapta inchiderea PID-ului, apoi porneste aceeasi comanda cu aceleasi variabile de mediu si verifica `/health/live` pe portul nou; daca nu raspunde in 30 s, relanseaza cu configuratia anterioara. Merge fara serviciu, dar e mai fragil (depinde de contul Windows si de modul de pornire).
  - **B (recomandata pe termen lung)**: rulare ca serviciu Windows (`UseWindowsService`) sau sub un supervizor (Task Scheduler/NSSM) cu repornire automata la iesire; aplicatia iese cu un cod dedicat. Mai robust, dar schimba modul de instalare/publicare (CLAUDE.md spune ca publicarea in containere/QNAP e in afara fazei curente; serviciul Windows local nu e interzis).
- Oprirea/pornirea **serverului MariaDB** nu e inclusa: contul aplicatiei nu are `SHUTDOWN` si nu poate porni un proces cazut. Posibil ulterior, numai pentru instanta locala, printr-o comanda configurata explicit (analog `tools\dev-mariadb.ps1`).

## 5. Securitate

- Politica `ConfiguredAdministrator` (claim propriu) in `Settings.razor`, in serviciile noi (`EnsureConfiguredAdministratorAsync` in `IAccessControl`, la fel ca `EnsureAdministratorAsync`) si pe orice endpoint nou; verificarea se face pe server la fiecare actiune, nu doar prin ascunderea tab-ului.
- Parola: niciodata trimisa inapoi in interfata, in jurnal, in log sau in detalii de audit; stocata protejat; campul se goleste dupa salvare.
- Reautentificare (parola administratorului configurat) pentru salvarea conexiunii, schimbarea parolei, oprire si repornire; limitare de rata ca la login.
- `AllowLoadLocalInfile=false` ramane; `SslMode` sub `Required` refuzat pentru alt host decat loopback.
- Fisierul de suprascriere sta in afara repository-ului (regula curenta spune ca `local-secrets/` se comite; acesta NU trebuie comis) si e protejat prin drepturile directorului utilizatorului Windows.

## 6. Jurnalizare (regula permanenta din `CLAUDE.md`)

Actiuni noi in `AuditActions`, in filtrul paginii Audit si in `IsCreateOrEdit`: "Modificare conexiune baza de date" (valori vechi/noi pentru host, port, baza, utilizator, SslMode; la parola doar "parola schimbata"), "Modificare port aplicatie", "Schimbare parola baza de date", "Verificare integritate baza de date" (sumar), "Repornire conexiuni baza de date", "Repornire aplicatie", "Oprire aplicatie", "Revenire la configuratia anterioara". Daca baza e indisponibila, intrarea de jurnal se scrie in fisierul de log si se reia cand revine baza.

## 7. Interfata (reguli din `CLAUDE.md`)

Tab in `Settings.razor`; butoane cu `act-confirm/act-reload/act-close/act-nav`, oprire cu `danger`; `ToggleSwitch` (nu casete bifate); `catalog-body` pentru continutul din carduri; date `dd.MM.yyyy HH:mm`; `UnsavedChangesTracker` pe formularul de conexiune; insigne `count-badge` pentru numarul de tabele/sesiuni; spatiere dupa randuri de butoane; text fara diacritice doar in `.md`, in aplicatie cu diacritice.

## 8. Teste

- Reguli pure (validare host/port/SslMode, constructia sirului de conexiune, mascarea parolei, alegerea portului efectiv) in `tests/BlazorStoc.Checks`.
- Politica de acces: contul configurat intra; un cont din baza cu rol Administrator nu intra; utilizatorul obisnuit nu intra (pagina si serviciile).
- Salvare cu test esuat nu modifica nimic; salvare reusita aplica; esec dupa aplicare revine automat.
- Jurnal: exista intrarile cu valori vechi/noi si nu contin parola.
- Pe `blazorstoc_test` (MariaDB reala): starea, `CHECK TABLE`, cautarea de orfani (inclusiv un orfan introdus intentionat).
- Componente: tab vizibil/ascuns, confirmari, stari de eroare. Pasi pentru `Teste utilizator/09_Notificari_si_setari.md` (sau fisier nou).

## 9. Etape propuse

1. **Fundatie + Stare + Conexiune**: politica `ConfiguredAdministrator`, fisier de suprascriere cu parola protejata, pagina Stare, test si salvare cu revenire automata, jurnal, `CircuitHandler`. (Cea mai utila etapa si cu cel mai mic risc.)
2. **Verificare**: sanatate si integritate, raport, jurnal.
3. **Aplicatie**: port, oprire, repornire (varianta A), repornire usoara a pipeline-ului.
4. **Optionale**: schimbarea parolei in server, verificare programata cu notificare, supervizor/serviciu Windows (varianta B), pornirea/oprirea MariaDB local.

## 10. Decizii de la utilizator

1. "Portul" = portul bazei de date, portul aplicatiei sau ambele? (Propunerea le acopera pe ambele.)
2. "Repornire pipeline" = repornirea usoara de la 4.4 (conexiuni, cache, servicii de fundal) este ce se doreste?
3. Repornirea aplicatiei: varianta A (proces ajutator) acum, serviciu Windows mai tarziu - sau direct serviciu?
4. Acces numai pentru contul administratorului configurat (nu si pentru conturi din baza cu rol Administrator) - confirmat?
5. Se doreste si schimbarea efectiva a parolei contului in server (4.2, optional), nu doar a celei folosite de aplicatie?
6. Pornirea/oprirea serverului MariaDB local ramane in afara primei versiuni?

## 11. Decizii confirmate de utilizator (20.10.2026) - prevaleaza asupra sectiunilor de mai sus

1. Porturi: ambele (portul bazei de date si portul aplicatiei) - implementat in etapa 1.
2. "Repornire pipeline" = repornirea usoara de la 4.4 (conexiuni, cache-uri, servicii de fundal, fara oprirea procesului): DA.
3. Repornirea/oprirea aplicatiei: **direct serviciu Windows** (varianta B de la 4.5), nu procesul ajutator (varianta A). Implica `UseWindowsService`, scripturi de instalare/dezinstalare a serviciului si a repornirii automate la iesire, actualizarea `docs/SETUP_DEZVOLTARE.md` si a `tools/start-preview.ps1`; oprirea din pagina opreste serviciul, repornirea iese cu un cod pe care serviciul il reporneste.
4. **Acces: rolul Administrator are acces la pagina, fara posibilitatea de a-l pierde.** Inlocuieste politica "doar administratorul configurat" (sectiunea 3, punctul 2). Consecinte de implementat:
   - pagina si serviciile cer rolul Administrator (`IsAdministratorAsync` / `EnsureAdministratorAsync`), nu claim-ul `config_admin`; administratorul configurat din fisierul privat ramane administrator permanent (se autentifica fara SQL, deci ajunge in pagina si cand baza este cazuta);
   - accesul nu se poate pierde: nu se poate sterge, dezactiva sau retrograda ultimul administrator din baza (regula in repository-ul de utilizatori, cu mesaj clar si test), iar contul administratorului configurat nu poate fi modificat din Utilizatori;
   - nota adaugata in `UserEditor.razor` (spune ca pagina e rezervata administratorului configurat) trebuie inlocuita cu textul corect;
   - in codul din etapa 1, `IsConfiguredAdministratorAsync`, `EnsureConfiguredAdministratorAsync`, `AccessRoles.ConfiguredAdministratorClaim` si claim-ul pus la login devin inutile pentru aceasta pagina (de scos sau de redenumit); verificarile din `DatabaseSettingsChecks` pentru "un administrator din baza este refuzat" se inverseaza: administratorul din baza intra, utilizatorul obisnuit nu.
5. **Creare cont de baza de date si schimbare de parola la nevoie** (nu doar parola folosita de aplicatie): din pagina, cu credentiale administrative ale serverului MariaDB (de exemplu `root`) introduse de administrator pe loc, folosite o singura data si nepastrate nicaieri (nici in fisier, nici in jurnal, nici in log). Operatii: crearea contului aplicatiei (`blazorstoc_dev`) cu drepturile din `docs/SETUP_DEZVOLTARE.md`, tabelul de conturi, `REQUIRE SSL`; schimbarea parolei oricarui cont al aplicatiei (aplicatie, migrare, backup, restaurare); apoi actualizarea parolei folosite de aplicatie cu aceeasi secventa sigura (parola veche ramane functionala la orice esec), jurnal fara parole. Trece din etapa 4 (optionala) intr-o etapa proprie, dupa etapa 1.
6. Pornirea/oprirea serverului MariaDB: **decis (20.10.2026)**: daca aplicatia ruleaza pe acelasi server cu baza de date, pagina permite repornirea (si oprirea/pornirea) serverului bazei si a aplicatiei; daca baza ruleaza remote, pagina permite doar oprirea/repornirea aplicatiei. Proiectarea este in sectiunea 13.
7. **Sectiune de ajutor (help) pentru baza de date**: in pagina "Baza de date" un sub-tab "Ajutor" (sau panou de ajutor in fiecare sub-tab) care explica in romana, pe intelesul administratorului: ce este fiecare camp (host, port, utilizator, parola, SSL, port aplicatie, interfata), ce inseamna fiecare linie din Stare, ce verifica fiecare test, cum se face revenirea la configuratia de baza, ce se intampla la schimbare (blocare de intretinere, revenire automata), cum se creeaza contul si se schimba parola, cum se opreste/reporneste aplicatia ca serviciu, ce se face daca baza nu raspunde (pasi de depanare: serverul ruleaza? port? firewall? cont/parola? SSL?), unde sunt fisierele (suprascrierea, jurnalul aplicatiei, copiile de siguranta). Text static fara diacritice in `.md`, cu diacritice in aplicatie; actualizat la fiecare etapa.

### Clarificare la punctul 6 (pornirea/oprirea serverului MariaDB)

Intrebarea: pagina trebuie sa poata controla doar **aplicatia** BlazorStoc (oprire/repornire, deja stabilit) sau si **serverul MariaDB** (procesul `mariadbd`)?

- Contul aplicatiei nu poate opri serverul (nu are privilegiul `SHUTDOWN`) si nu poate porni un server oprit. Deci ar fi nevoie de una din doua cai: (a) credentiale administrative introduse pe loc pentru `SHUTDOWN` (merge doar pentru oprire, nu pentru pornire), sau (b) controlul serviciului Windows al MariaDB (`sc stop/start`), posibil daca MariaDB ruleaza ca serviciu pe acelasi calculator si serviciul aplicatiei are dreptul sa-l controleze.
- Riscuri: oprirea serverului cu aplicatia pornita face toate paginile inutilizabile pana la repornirea lui; un server pornit/oprit gresit poate corupe date daca nu se inchide curat; pe un server aflat pe alt calculator nu se poate face deloc.
- Recomandare: **da, dar numai pentru instanta locala care ruleaza ca serviciu Windows** (cazul probabil, odata ce aplicatia devine si ea serviciu), cu confirmare dubla si refuz cat timp ruleaza backup/restaurare; pentru un server aflat pe alt calculator butoanele nu apar. Daca MariaDB local nu este serviciu, ramane in afara.
- Raspunsul de dat: DA (doar serviciu local) sau NU (doar aplicatia).

## 12. Stare implementare si preluare in sesiune locala (20.10.2026)

Etapa 1 este **partial implementata** (commit `fea14a8` pe branch-ul `ccr-eafcbe49-gslqnq`, marcat WIP):

- Gata si compilat (`dotnet build BlazorStoc.csproj -c Release` trece): `Services/DatabaseConnectionSettings.cs` (reguli, fisier de suprascriere cu parola protejata, sursa de configurare, `AppHosting`, `DatabaseSettingsService`), `Services/DatabaseStatus.cs` (stare baza si aplicatie, `CircuitCounter`), actiuni de jurnal + filtre (`AuditTrail.cs`, `AuditFilters.cs`), `Components/Shared/DatabaseStatusPanel.razor` si `DatabaseConnectionPanel.razor`, tab-ul "Baza de date" in `Settings.razor`, cablare in `Program.cs`, CSS in `wwwroot/app.css`.
- Scris dar **nerulat**: `tests/BlazorStoc.Checks/DatabaseSettingsChecks.cs` (grupul `dbsettings`); `AddDatabaseOverride` trebuie sa returneze `IConfigurationBuilder` (corectat in sursa, proiectul de teste nu s-a mai compilat dupa asta).
- Lipsesc: rularea `dbsettings` si a suitei complete (`tools\run-checks.ps1`), sectiunea MariaDB (stare, test conexiune, salvare cu aceeasi parola), documentatia (`docs/PROJECT_STATE.md`, `IMPLEMENTED.md`, `docs/TESTE_RAMASE.md`, `Teste utilizator/09_Notificari_si_setari.md`, `docs/HARTA_COD.md`), `tools/start-preview.ps1` (sa citeasca portul salvat), verificarea vizuala in browser.
- **De refacut dupa decizia 4**: politica de acces (rolul Administrator, nu claim-ul), protectia ultimului administrator, nota din `UserEditor.razor`, testele de acces.
- Dupa aceea, in ordine: sub-tabul Ajutor (decizia 7), etapa Verificare (sectiunea 4.3), etapa Aplicatie ca serviciu Windows + repornire usoara (deciziile 2 si 3), etapa conturi si parole in server (decizia 5), apoi decizia 6 daca se aproba.
- Sesiunea cloud nu avea MariaDB; SDK-ul .NET 10 din apt a compilat tinta net9.0 (cu `DOTNET_ROLL_FORWARD=Major`). Testele pe MariaDB si previzualizarea se fac local.

## 13. Controlul serverului bazei de date si al aplicatiei (decizia 6, 20.10.2026)

### 13.1. Regula

| Unde ruleaza baza fata de aplicatie | Aplicatia (serviciu Windows) | Serverul MariaDB |
|---|---|---|
| Pe acelasi calculator, ca serviciu Windows gasit si controlabil | Oprire, repornire | Oprire, pornire, repornire |
| Pe acelasi calculator, dar nu ca serviciu (proces pornit manual, Docker, nume de serviciu necunoscut) | Oprire, repornire | Nu; pagina spune de ce si ce se configureaza |
| Pe alt calculator (remote) | Oprire, repornire | Nu; doar stare si verificare. Textul spune ca repornirea se face pe acel calculator |

Butoanele inexistente nu apar dezactivate fara explicatie: apare o linie care spune motivul (regula din `CLAUDE.md`: un buton dezactivat isi explica motivul).

### 13.2. Cum se stabileste "pe acelasi calculator"

Trei semnale, toate trebuie sa fie de acord (nu doar hostul scris in configuratie):
1. hostul configurat este loopback, numele calculatorului sau o adresa a unei interfete locale (`NetworkInterface`);
2. serverul raporteaza `@@hostname` egal cu `Environment.MachineName` (cand baza raspunde);
3. exista un serviciu Windows al MariaDB (vezi mai jos) a carui comanda (`ImagePath`, citita din registru, fara drepturi speciale) porneste `mariadbd` cu `--port`/`my.ini` pe portul configurat.
Cand baza nu raspunde, ramane semnalul 1 (+ ultimul `@@hostname` cunoscut, retinut in fisierul de suprascriere). Un tunel (`127.0.0.1` catre un server din alta parte) este prins de semnalul 2.

### 13.3. Mecanismul pentru serverul MariaDB: serviciul Windows

- Se foloseste `ServiceController` (pachetul `System.ServiceProcess.ServiceController`, apelat numai cand `OperatingSystem.IsWindows()`), pentru ca nu cere credentiale ale bazei (spre deosebire de `SHUTDOWN`), opreste curat (InnoDB isi inchide si scrie tot) si permite pornirea unui server oprit.
- Numele serviciului este PERSONALIZAT, nu cel general `MariaDB` (decizia utilizatorului, 20.10.2026: un nume general poate intra in conflict cu alte instalari MariaDB/MySQL de pe calculator). Implicit `BlazorStocMariaDB`, ca pe instanta de dezvoltare; valoarea se pastreaza in `Database:ServerServiceName` (fisierul de suprascriere) si poate fi schimbata la instalare (`-ServiceName`). Serviciul se creeaza cu `mariadbd --install BlazorStocMariaDB --defaults-file=<my.ini>`, deci cu `my.ini`-ul si portul propriu aplicatiei, nu cu cele ale unei instalari generale. Detectia nu mai ia la intamplare serviciile cu `mariadb`/`mysql` in nume: se cauta intai numele configurat, apoi (doar ca sugestie in pagina, nu ca implicit) alte servicii a caror `ImagePath` porneste `mariadbd` cu `--defaults-file`/portul configurat; pagina nu controleaza niciodata un serviciu care nu a fost confirmat de administrator. Daca nu se gaseste niciun serviciu, linia din 13.1 spune cum se instaleaza.
- Instanta de dezvoltare (`tools\dev-mariadb.ps1`) nu este serviciu: scriptul `tools\setup-dev-environment.ps1` primeste optiunea `-InstallService` (serviciu `BlazorStocMariaDB`) ca pagina sa functioneze identic pe dezvoltare si pe server. Un `mariadbd` pornit de aplicatie ca proces copil ar muri odata cu ea si nu se ofera.
- Dreptul: contul sub care ruleaza serviciul aplicatiei trebuie sa poata porni/opri serviciul MariaDB. `LocalSystem` il are; pentru un cont dedicat, scriptul de instalare acorda dreptul (`sc sdset`) si pagina verifica la deschidere (`ServiceController` cere `SERVICE_START|SERVICE_STOP`) si spune clar ce lipseste.
- Aplicatia ca serviciu (decizia 3): `UseWindowsService`, scripturi `tools\install-service.ps1` / `uninstall-service.ps1`, actiuni de recuperare (`sc failure ... restart`, `sc failureflag 1`); repornirea = `StopApplication()` cu cod de iesire diferit de zero, pe care Windows il reporneste; oprirea = `StopApplication()` cu iesire 0 (serviciul ramane oprit). Daca aplicatia nu ruleaza ca serviciu (`dotnet run`, preview), repornirea nu se ofera si oprirea cere confirmare suplimentara.

### 13.4. Secvente (toate sub lacatul de intretinere, refuzate cat timp ruleaza backup sau restaurare)

- **Repornire server baza**: lacat de intretinere (sesiunile deschise vad pagina de asteptare; tab-ul administratorului ramane, are `data-operation-driver`) -> serviciul MariaDB `Stop` (asteapta oprirea completa, timeout 120 s, afisat in pagina) -> `Start` -> asteapta portul si `SELECT 1` -> `ClearAllPools` -> verifica schema si tabelele -> repornire usoara a pipeline-ului (cache-uri, servicii de fundal; `ChangeEventRelay` isi reia singur interogarile dupa erori) -> eliberare lacat -> jurnal. La esec la pornire: starea serviciului si ultimele linii din jurnalul de erori al MariaDB (`@@log_error` retinut cat timp baza raspundea) afisate in pagina; lacatul se elibereaza, aplicatia ramane in starea "Indisponibil".
- **Oprire server baza**: aceleasi garantii, confirmare cu cuvantul "OPRESTE BAZA" si numarul de sesiuni deschise; dupa oprire se elibereaza lacatul, iar aplicatia trece in starea "Baza de date indisponibila" (13.5).
- **Pornire server baza** (cand e oprit): butonul apare in locul celui de oprire.
- **Repornire aplicatie + server**: intai serverul bazei (cu aplicatia vie ca sa conduca operatia si sa verifice), apoi aplicatia se opreste cu cod de repornire. Ordinea inversa nu se ofera (o aplicatie oprita nu mai poate porni baza).
- **Oprire aplicatie + server**: baza se opreste prima, apoi aplicatia cu iesire 0; dupa asta nimic din browser nu mai poate porni nimic (pornirea se face din Windows, `Servicii`); avertizare pe pagina inainte de confirmare.
- Fiecare pas se jurnalizeaza (cand baza e oprita, intrarile se scriu in jurnalul aplicatiei si se reiau in jurnalul din baza la revenire, ca la schimbarea conexiunii).

### 13.5. Starea "Baza de date indisponibila" (necesara pentru oprirea/caderea bazei)

Azi, cand baza cade, paginile arata erori. Pentru oprirea controlata si pentru caderi propun:
- un monitor de sanatate (serviciu de fundal, sondaj la 5 s cu `SELECT 1`) cu starea ultimelor minute, folosit si de pagina Stare;
- cand baza e indisponibila mai mult de ~10 s, navigarile utilizatorilor obisnuiti merg la o pagina de asteptare "Baza de date este indisponibila" (aceeasi tehnica ca pagina de intretinere, cu revenire automata cand baza raspunde), iar administratorii pot ajunge la Setari -> Baza de date;
- **atentie la autentificare**: conturile din baza de date nu se pot autentifica cat baza e oprita (verificarea parolei interogheaza SQL). Pot intra numai administratorul configurat (fara SQL) si sesiunile deja deschise (cookie de 8 ore, fara SQL). Deci pentru a porni baza, administratorul trebuie sa fi ramas conectat sau sa intre cu contul configurat; pagina Conexiune/Stare spune asta explicit, iar textul de ajutor (decizia 7) trebuie sa o repete.

### 13.6. Securitate si reautentificare

- Acces: rolul Administrator (decizia 4). Reautentificarea pentru oprire/pornire/repornire: parola contului cu care e conectat (administratorul configurat: parola din fisierul privat; cont din baza: verificare prin `IUserAuthenticator`; cand baza e oprita, doar contul configurat o poate confirma - alta coincidenta cu 13.5).
- Limitare de rata ca la login; confirmare scrisa pentru oprire; refuz cat timp exista lacat de operatie activ; nimic din pagina nu accepta numele unui serviciu tastat liber (numai din lista detectata), ca sa nu poata fi folosita pentru a controla alte servicii.
- `ImagePath` si numele serviciului se afiseaza (nu sunt secrete); caile si argumentele serviciului nu se modifica de aici.

### 13.7. Teste

- Reguli pure: matricea 13.1 pentru toate combinatiile (local/remote x serviciu gasit/lipsa x aplicatie serviciu/proces), potrivirea celor trei semnale, refuzul pe nume de serviciu necunoscut.
- Un `IServiceControl` cu implementare falsa (pornit/oprit/timeout/refuz de drepturi) pentru secventele din 13.4, inclusiv esec la pornire si eliberarea lacatului.
- Jurnal cu valori fara secrete; pentru `ServiceController` real: verificare manuala pe Windows (pasi in `docs/TESTE_RAMASE.md`), pentru ca mediul cloud nu are Windows.
- Componente: butoane afisate/ascunse dupa matrice, linia cu motivul, confirmarea scrisa.

### 13.8. Etape (inlocuiesc etapa 3 si punctul 1.6 din `TODO.md`)

- 3a. Aplicatie ca serviciu Windows: instalare/dezinstalare, oprire/repornire din pagina, repornire usoara a pipeline-ului.
- 3b. Monitor de sanatate si starea "Baza de date indisponibila" (13.5).
- 3c. Controlul serverului bazei: detectare (13.2), serviciul MariaDB (13.3), secventele (13.4), optiunea `-InstallService` pentru instanta de dezvoltare.

### 13.9. Intrebari (raspunsuri in sectiunea 14)

1. Pe server, MariaDB ruleaza deja ca serviciu Windows? Cum se numeste (nu se mai presupune `MariaDB`: vezi 13.3 si sectiunea 14)?
2. Sub ce cont va rula serviciul aplicatiei (`LocalSystem` sau un cont dedicat)? Pentru cont dedicat, scriptul acorda dreptul de control asupra serviciului MariaDB.
3. Instanta de dezvoltare locala poate fi trecuta pe serviciu (`-InstallService`), ca pagina sa se poata testa si acolo?
4. Pagina de asteptare "Baza de date indisponibila" pentru utilizatorii obisnuiti este acceptata (13.5)?

## 14. Raspunsuri la intrebarile din 13.9 (utilizator, 20.10.2026)

1. MariaDB ruleaza ca **serviciu Windows** pe server. Numele serviciului este **personalizat** (utilizator, 20.10.2026: `MariaDB` este prea general si ar putea aparea conflicte): implicit `BlazorStocMariaDB`, configurabil in `Database:ServerServiceName`; nu se mai cauta `MariaDB` (13.3).
2. Serviciul aplicatiei ruleaza sub **LocalSystem** (are dreptul sa porneasca/opreasca serviciul MariaDB; scriptul de instalare nu mai trebuie sa acorde drepturi speciale pentru acest caz).
3. Instanta de dezvoltare locala **poate fi trecuta pe serviciu**: `tools\setup-dev-environment.ps1 -InstallService` (serviciu `BlazorStocMariaDB`), `tools\dev-mariadb.ps1` ramane pentru cine nu o foloseste.
4. Pagina de asteptare "Baza de date indisponibila" pentru utilizatorii obisnuiti este **acceptata** (13.5).

Toate deciziile 1-7 (sectiunea 11) si 13-14 sunt acum confirmate; nu mai exista intrebari deschise. Implementarea continua dupa ordinea din sectiunea 12 (inceput: finalizarea etapei 1 cu accesul pe rol Administrator), apoi Ajutor (decizia 7), Verificare, 3a, 3b, 3c si conturile/parolele din server (decizia 5).

## 15. Conexiune: oprire, repornire, port server, parola contului aplicatiei (cerere 20.10.2026) - propunere si analiza

Cerere: in Setari -> Baza de date -> Conexiune, pe langa host/port/cont (care schimba doar ce foloseste aplicatia), patru operatii asupra serverului: oprirea bazei, repornirea, schimbarea portului pe care asculta serverul si schimbarea parolei contului cu care se conecteaza aplicatia. Operatiile 1-2 sunt deja proiectate (sectiunea 13); 3 si 4 sunt noi aici. Nu e implementat nimic din aceasta sectiune.

### 15.1. Ce exista azi (verificat)

- Serverul de dezvoltare (`C:\Dev\BlazorStoc-MariaDB`) NU este serviciu Windows: este un `mariadbd.exe` ascuns, pornit de `tools\dev-mariadb.ps1` cu `--defaults-file=my.ini`. Portul este linia `port=3307` din `my.ini`; aceeasi instanta gazduieste bazele `BlazorStoc`, `blazorstoc_test` si `blazorstoc_demo`.
- Contul aplicatiei (`blazorstoc_dev`@`127.0.0.1`, `REQUIRE SSL`) are doar `SELECT, INSERT, UPDATE, DELETE`: nu are `SHUTDOWN`, nu poate crea conturi si nu poate modifica `my.ini`. Parola lui sta in `local-secrets\application-connection.private.json` (in repository) si, dupa o salvare din pagina, in fisierul de suprascriere (criptata, in afara repository-ului), care are prioritate.
- Radacina (`admin.private.cnf`, host/port 3307 scrise in el) este folosita doar de scripturile de dezvoltare; aplicatia nu o citeste si nu trebuie sa o citeasca.
- Constatare (de corectat oricum): procesul de lucru al backup-ului (`Modules/Backup/BackupWorker.cs`, modul implicit `Worker`) isi construieste configuratia fara fisierul de suprascriere (`AddPrivateAccountFiles`, nu `AddDatabaseOverride`). Dupa o schimbare de host, port, cont sau parola din pagina, backup-ul si restaurarea ar continua cu valorile vechi. De verificat pe un caz real si de corectat in prima etapa de mai jos.
- Constatare: dupa unirea cu `feature/help`, tab-urile din Setari se decid prin permisiuni citite din baza (`IUserPermissions`). Cand baza este oprita, citirea esueaza si pagina Setari ar putea sa nu se mai deschida, tocmai cand administratorul vrea sa porneasca baza. Tab-ul Baza de date trebuie sa depinda numai de rolul din cookie, iar citirea permisiunilor celorlalte tab-uri sa fie prinsa (try/catch) si sa nu opreasca pagina.

### 15.2. Cele patru operatii: propunere

**A. Oprire / Pornire / Repornire server** (sectiunea 13 neschimbata, completari):
- Mecanism: serviciul Windows cu nume propriu `BlazorStocMariaDB` (`ServiceController`). Pe dezvoltare serviciul se creeaza cu `tools\setup-dev-environment.ps1 -InstallService` (`mariadbd --install BlazorStocMariaDB --defaults-file=<my.ini>`), apoi instanta pornita acum ca proces ascuns se opreste o singura data cu `dev-mariadb.ps1 -Action stop`.
- Drepturi: pe server aplicatia ruleaza ca `LocalSystem` (decizia 14.2) si are dreptul. Pe dezvoltare aplicatia ruleaza prin sarcina programata a utilizatorului `Alex`, care nu poate controla un serviciu: scriptul de instalare acorda utilizatorului dreptul de pornire/oprire doar pe serviciul `BlazorStocMariaDB` (`sc sdset`).
- Vizibilitate (matricea 13.1): butoanele apar doar cand baza este pe acelasi calculator si serviciul este gasit si controlabil; altfel o linie spune motivul.
- Sub lacatul de intretinere, refuzat cat timp ruleaza backup sau restaurare, cu cuvant de confirmare scris (OPRESTE BAZA / REPORNESTE BAZA) si parola contului de administrator.

**B. Schimbarea portului pe care asculta serverul** (nou, doar local + serviciu):
1. Se valideaza portul nou (1024-65535, diferit de cel curent, liber pe acest calculator, nefolosit de aplicatie).
2. Se salveaza o copie a `my.ini` (langa el, cu data) si se modifica numai linia `port=` (scriere atomica; nimic altceva din fisier nu se atinge; serviciul nu poarta `--port` in comanda, ca portul sa vina dintr-un singur loc).
3. Lacat de intretinere; oprire serviciu; pornire serviciu; se asteapta portul nou si `SELECT 1` cu contul aplicatiei pe portul nou.
4. Reusit: se scrie `Database:Port` in fisierul de suprascriere, se reincarca configuratia si se inchid pool-urile (aceeasi secventa ca la salvarea conexiunii), se jurnalizeaza.
5. Esec la orice pas dupa modificarea fisierului: se pune `my.ini` inapoi, se reporneste serverul pe portul vechi, aplicatia ramane pe conexiunea veche, pagina spune ce a esuat. Garantie ca la salvarea conexiunii: portul vechi ramane functional pana la confirmarea celui nou.
- Efecte in afara paginii (de spus clar in pagina inainte de confirmare si de acoperit in ajutor): `tools\dev-mariadb.ps1` si `admin.private.cnf` (radacina) au portul vechi; `local-secrets\test-database.private.json` si `demo-database.private.json` (testele si capturile din help) la fel; `tools\start-preview.ps1` urmeaza deja portul salvat. Propun un script `tools\sync-dev-port.ps1 -Port N` care actualizeaza fisierele de dezvoltare dintr-o singura comanda.
- Nu se ofera pentru server aflat pe alt calculator (pagina doar explica: portul se schimba pe acel calculator, apoi se pune aici in campul Port).

**C. Schimbarea parolei contului aplicatiei** (nou, merge si pe server remote):
- Fara credentiale de radacina: un cont isi poate schimba singur parola in MariaDB (`SET PASSWORD = PASSWORD('...')`, echivalent `ALTER USER CURRENT_USER()`), fara privilegii speciale, deschizand conexiunea cu parola veche. Deci nu cerem si nu pastram nicaieri parola radacinii.
- Secventa in doua faze, ca o cadere la mijloc sa nu blocheze aplicatia: (1) parola noua se scrie criptat in fisierul de suprascriere ca `parola in asteptare`; (2) se schimba parola pe server cu conexiunea veche; (3) se verifica o conexiune noua cu parola noua; (4) parola in asteptare devine parola curenta, se reincarca configuratia, se inchid pool-urile; (5) jurnal. La pornire, daca parola curenta este refuzata si exista una in asteptare, se incearca aceasta (acopera caderea intre pasii 2 si 4). Esec la pasul 3: se revine pe server la parola veche (conexiunea cu parola noua), parola in asteptare se sterge.
- MariaDB nu permite doua parole valide in acelasi timp, de aceea este nevoie de faza de asteptare, nu de o fereastra de suprapunere.
- Politica: minimum 16 caractere, buton „Genereaza parola” (parola nu se afiseaza dupa salvare si nu apare in jurnal, nici in log); confirmare cu parola contului de administrator.
- Se schimba numai contul aplicatiei. Conturile de migrare, backup si restaurare isi au parolele in fisierele private si raman neschimbate aici (aceeasi tehnica s-ar putea aplica ulterior, daca se doreste, cu scrierea fiecarui fisier privat).
- Riscuri de acoperit: (a) fisierul privat din `local-secrets` pastreaza parola veche; daca cheile de criptare ale suprascrierii se pierd, partea de baza a suprascrierii se dezactiveaza si aplicatia incearca parola veche, refuzata de server: recuperarea este `tools\reset-app-password.ps1` (cu radacina) care pune pe server parola din fisierul privat sau una noua; (b) alte procese cu fisierul privat (backup worker, scripturi) trebuie sa citeasca suprascrierea (constatarea din 15.1).

### 15.3. Jurnal si acces

- Actiuni exacte noi: „Oprire server baza de date", „Pornire server baza de date", „Repornire server baza de date", „Modificare port server baza de date", „Modificare parola cont aplicatie baza de date" (fara nicio parola; la port: valoarea veche si noua); se adauga in `AuditActions`, in filtrul Jurnalului si in `IsCreateOrEdit` daca e cazul, cu test.
- Acces: rolul Administrator (decizia 11.4), reconfirmare cu parola proprie la fiecare operatie, limitare de rata ca la salvarea conexiunii, nimic din pagina nu accepta un nume de serviciu tastat liber.
- Pagina ramane utilizabila cand baza este oprita (vezi constatarea din 15.1); cand baza este oprita, doar administratorul configurat se autentifica, deci pagina spune ca administratorul trebuie sa ramana conectat.

### 15.4. Interfata (pagina Conexiune)

Trei carduri noi sub cele existente, fiecare cu starea si motivul cand nu se poate: „Serverul bazei de date" (stare serviciu + Opreste / Reporneste / Porneste), „Portul serverului" (portul curent, camp pentru cel nou, „Schimba portul serverului") si „Parola contului aplicatiei" (parola noua, confirmare, „Genereaza", „Schimba parola"). Topicul de help `setari-baza-conexiune` se completeaza, iar captura se reface numai pentru el.

### 15.5. Etape propuse (in aceasta ordine)

1. Parola contului aplicatiei (C) + corectia procesului de backup + tab-ul care nu depinde de baza. Nu cere serviciu Windows, merge si pe server remote, are cel mai mare castig si cel mai mic risc.
2. Serviciul pentru dezvoltare (`-InstallService`, dreptul utilizatorului) si A: oprire/pornire/repornire server (include `IServiceControl`, secventele din 13.4, pagina „Baza indisponibila" din 13.5).
3. Portul serverului (B) peste A, cu `tools\sync-dev-port.ps1`.
Teste: reguli pure (validari, matrice), `IServiceControl` fals pentru secvente si rollback, pe MariaDB reala `blazorstoc_test` pentru schimbarea parolei unui cont de test (cu revenire), verificare manuala pe Windows a serviciului real (pasi in `docs/TESTE_RAMASE.md`).

### 15.6. Decizii cerute

1. Ordinea de la 15.5 si, in special, etapa 1 cu parola contului aplicatiei?
2. Instanta de dezvoltare trece pe serviciul `BlazorStocMariaDB` (deja acceptat la 14.3) si utilizatorul `Alex` primeste dreptul pe acel serviciu?
3. Politica de parola: minimum 16 caractere cu generator?
4. `tools\sync-dev-port.ps1` pentru fisierele de dezvoltare dupa schimbarea portului?
5. Schimbarea parolei si pentru conturile de migrare, backup si restaurare (cu scrierea fisierelor private), sau doar contul aplicatiei?

## 16. Raspunsuri la 15.6 si analiza conturilor predefinite ale bazei (utilizator, 20.10.2026)

### 16.1. Raspunsurile utilizatorului

1. Ordinea din 15.5 si parola contului aplicatiei: DA, dar extinsa la toate conturile bazei (16.3). Serviciile trebuie sa ruleze la nivel de masina, deasupra utilizatorilor, daca e posibil (16.2).
2. Instanta de dezvoltare trece pe serviciu (`BlazorStocMariaDB`): DA.
3. Politica de parola (minimum 16 caractere, generator): DA. Tab-ul Baza de date depinde numai de rol (nu de permisiuni citite din baza; vezi constatarea din 15.1, care ramane de implementat).
4. `tools\sync-dev-port.ps1`: DA.
5. Schimbarea parolelor tuturor conturilor legate de baza, cu resetare de catre administrator si verificarea conectarii conturilor dupa schimbare: DA (16.3).
6. Restaurarea unui backup trebuie sa tina cont de setarile in vigoare ale bazei (16.4).
7. Regula permanenta: orice schimbare de flux sau de interfata are update in help; se regenereaza doar partile (topicurile si capturile) care s-au schimbat. Adaugata in `CLAUDE.md` si in memorie.

### 16.2. Unde ruleaza serviciile (la nivel de masina)

- **Serviciul bazei (`BlazorStocMariaDB`)**: cont virtual `NT SERVICE\BlazorStocMariaDB` (fara parola, fara profil de utilizator, drepturi numai pe folderul datelor), nu `LocalSystem`: mariadbd nu are nevoie de drepturi mai mari. Porneste la pornirea calculatorului, inaintea oricarei autentificari, deci nu depinde de utilizatorul `Alex`. Alternativa mai simpla (`LocalService`) este acceptabila, dar are acces mai larg.
- **Aplicatia (serviciu Windows, decizia 11.3)**: `LocalSystem` (decizia 14.2). Controleaza serviciul bazei fara drepturi acordate unui utilizator; sarcina programata a utilizatorului (`BlazorStocSupervisor-5087`) se inlocuieste cu actiunile de recuperare ale serviciului. Dispare si nevoia ca `Alex` sa primeasca drept pe serviciu (15.2.A).
- **Consecinta importanta**: sub `LocalSystem` si sub un cont virtual, `%LOCALAPPDATA%` nu mai este profilul lui `Alex`, iar valorile implicite de azi (fisierul de suprascriere, cheile de criptare, asset-urile, pachetele de backup, `data\`) ar pointa in alt folder si aplicatia nu si-ar mai gasi parolele si pachetele. Propun un folder de date al masinii, `C:\ProgramData\BlazorStoc\`, citit dintr-un singur setting (`App:DataRoot`), cu drept de scriere numai pentru `SYSTEM` si `Administrators`, si un pas unic de mutare din `%LOCALAPPDATA%\BlazorStoc-MariaDB` (atat vederea reala, cat si cea virtualizata MSIX, vezi memoria despre cele doua `%LOCALAPPDATA%`), cu cheile de criptare mutate impreuna cu fisierul de suprascriere (altfel parolele salvate nu se mai pot decripta). Efect secundar bun: aplicatia pornita din orice sesiune vede aceleasi fisiere (dispare divergenta descoperita la 17.10.2026). De verificat pe masina: ca ProgramData nu este virtualizat pentru procesele pornite din aplicatia desktop.
- Previewul de dezvoltare (supervizorul pe 5087) ramane pana cand dezvoltarea trece pe serviciu; ambele trebuie sa citeasca acelasi `App:DataRoot`.

### 16.3. Conturi predefinite ale bazei, parole implicite si schimbarea lor din setari

**Catalogul de conturi (fix, in cod, nemodificabil din pagina).** O singura clasa `DatabaseAccounts` cu numele, scopul si drepturile cerute ale fiecarui cont; pagina nu poate adauga, redenumi, sterge conturi sau schimba drepturi, doar parolele lor:

| Cont (nume fix propus) | Scop | Drepturi cerute |
|---|---|---|
| `blazorstoc_app` (azi `blazorstoc_dev`) | accesul aplicatiei | SELECT, INSERT, UPDATE, DELETE pe baza aplicatiei |
| `blazorstoc_migrator` | actualizarea schemei la pornire | CREATE, DROP, REFERENCES, INDEX, ALTER, CREATE VIEW, TRIGGER |
| `blazorstoc_backup` | copiile de siguranta | SELECT, SHOW VIEW, TRIGGER, LOCK TABLES |
| `blazorstoc_restore` | restaurarea | drepturi pe bazele `_bak`/`_old` si schimbul de schema |

(Contul `blazorstoc_test_app` ramane in afara schemei: este numai pentru teste si dezvoltare.) Redenumirea `blazorstoc_dev` -> `blazorstoc_app` se face o singura data (`RENAME USER`) de catre scriptul de instalare; alternativa este pastrarea numelui actual. Decizie 16.7.2.

**Cine creeaza conturile.** Aplicatia nu are `CREATE USER`, deci nu le creeaza. Le creeaza instalatorul (azi `tools\setup-dev-environment.ps1`, pe server pasul de instalare al bazei), cu radacina, odata, cu **parole implicite documentate** (cate una pe cont, definite o singura data in cod si in scriptul de instalare, nu secrete). Asa aplicatia porneste la prima rulare fara nicio configurare. Pagina nu poate crea un cont lipsa; o verificare il raporteaza, iar „Reseteaza” (mai jos) il poate recrea cu credentiale de radacina introduse pe loc.

**Schimbarea parolelor, fara radacina.** Fiecare cont isi poate schimba singur parola (`SET PASSWORD` cu conexiunea facuta cu parola curenta). Aplicatia cunoaste parola curenta a fiecarui cont (implicita sau cea salvata criptat), deci se conecteaza ca acel cont si ii pune parola noua, pe aceeasi secventa in doua faze din 15.2.C (parola in asteptare in fisierul de suprascriere, schimbare pe server, verificare cu o conexiune noua, promovare, revenire la esec). Parolele noi se pastreaza criptat in fisierul de suprascriere, pentru toate cele patru conturi (fisierul de suprascriere primeste o sectiune `accounts`; numele de utilizator nu mai sunt editabile, campul „Utilizator” din Conexiune devine doar afisat).

**Pagina (card „Conturile bazei de date”, in Conexiune).** Tabel cu cele patru conturi: scop, stare conectare (OK / Eroare), drepturi (OK / lipsesc / in plus), „parola implicita in uz” (DA / NU), data ultimei schimbari (din jurnal) si butoane: „Verifica conturile”, „Schimba parola” (pe cont, cu generator), „Schimba toate parolele” (secvential, rezultat pe fiecare cont, un esec nu le opreste pe celelalte si nu strica un cont deja schimbat), „Reseteaza conturile”.
- „Verifica conturile”: pentru fiecare cont deschide o conexiune noua cu parola in uz, ruleaza `SELECT 1` si `SHOW GRANTS FOR CURRENT_USER()` si le compara cu catalogul; aceasta este verificarea conectarii dupa orice schimbare de parola (rulata automat dupa fiecare schimbare si la cerere).
- „Parola implicita in uz” se afla incercand conectarea cu parola implicita (nu printr-un indicator salvat, care ar putea minti); se face la deschiderea paginii si la pornire, cu cache scurt.
- „Reseteaza conturile” (resetare de catre administrator): administratorul introduce pe loc credentialele unui cont de administrare a serverului (de exemplu `root`), folosite o singura data si nepastrate nicaieri; aplicatia recreeaza conturile lipsa, repune drepturile din catalog si seteaza parole noi generate (sau cele implicite, la alegere), le scrie criptat, apoi verifica toate conturile. Este calea de recuperare cand parolele s-au pierdut (de exemplu cheile de criptare pierdute). Refuzat cat timp ruleaza backup sau restaurare. Aceasta este si etapa „conturi de baza de date” 1.5 din `TODO.md`.

**Prima pornire si somarea administratorului.** Cat timp macar un cont foloseste parola implicita: (1) un administrator care se autentifica este dus pe Setari -> Baza de date -> Conturi, cu mesaj clar, si celelalte pagini ii raman blocate pana schimba parolele celor care au ramas implicite (nu poate amana; in mediul de dezvoltare, `Environment=Development`, somarea ramane doar ca banner, ca sa nu incurce lucrul); (2) ceilalti utilizatori vad o notificare catre administrator si lucreaza normal; (3) jurnal: „Schimbare parola cont baza de date <cont>” (fara parola). Nota de securitate: parolele implicite sunt publice, deci ele sunt sigure numai pana la prima schimbare; de aceea conturile sunt create doar pentru `127.0.0.1`/`localhost`, cu `REQUIRE SSL`, iar serverul asculta pe loopback (ca azi). Pe un server de retea, scriptul de instalare cere hostul permis si nu lasa instalarea terminata cu parole implicite fara avertisment. Decizie 16.7.3 pentru somare.

**Riscuri si masuri.**
- Cheile de criptare pierdute: toate parolele salvate devin ilizibile si aplicatia ar incerca parolele implicite, refuzate de server dupa schimbare. Masuri: mutarea cheilor in `App:DataRoot` (16.2), includerea folderului de chei in lista lucrurilor de salvat (documentat), „Reseteaza conturile” ca recuperare, mesaj clar in pagina.
- Procese separate (backup worker, scripturi, supervizor): toate citesc aceeasi configuratie efectiva (16.4).
- Schimbarea parolei contului de backup sau restaurare cat ruleaza o operatie: refuzata (lacatul de operatie).
- Parolele din `local-secrets` (in repository) raman cele implicite; valorile reale stau doar in fisierul de suprascriere, in afara repository-ului.

### 16.4. Restaurarea tine cont de setarile in vigoare

Cauza (15.1): procesul de lucru al backup-ului/restaurarii nu citeste fisierul de suprascriere. Propunere: (1) o singura functie de configuratie (`EffectiveConfiguration`, in `Startup`) folosita de aplicatie, de proces si de scripturi, care aplica ordinea: appsettings, mediu, fisiere private, suprascriere; (2) aplicatia trimite proceselor de lucru, in cererea de operatie, amprenta setarilor in vigoare (host, port, baza, conturi; fara parole), iar procesul o compara cu a lui inainte de a incepe: la diferenta refuza si spune ce difera; (3) restaurarea foloseste contul de restaurare, hostul si portul din setarile in vigoare, deci urmeaza si o schimbare de port sau de server; (4) conturile bazei sunt la nivel de server, nu in pachet: o restaurare nu schimba parolele si nu readuce parole vechi; (5) test cu un backup si o restaurare pe `blazorstoc_test` dupa o schimbare de port/parola din pagina.

### 16.5. Etape revizuite

1. Fundatie comuna: `App:DataRoot` si mutarea datelor (16.2), `EffectiveConfiguration` pentru toate procesele (16.4), tab independent de permisiuni din baza.
2. Catalogul de conturi, cardul „Conturile bazei de date”: verificare, schimbare parola (pe cont si toate), resetare cu credentiale de administrare, somarea de la prima pornire; scripturile de instalare (conturi cu parole implicite, redenumirea).
3. Serviciile (baza cu cont virtual, aplicatia ca `LocalSystem`), oprire/pornire/repornire din pagina.
4. Portul serverului si `tools\sync-dev-port.ps1`.
La fiecare etapa se actualizeaza topicurile de help afectate si se regenereaza numai capturile ferestrelor care s-au schimbat (regula 16.1.7).

### 16.6. Teste

Reguli pure (catalog, matricea de drepturi, parola implicita), `IServiceControl` fals, pe MariaDB reala: schimbarea parolei unui cont de test si a unui cont din catalog pe baza `blazorstoc_test` cu revenire, caderea simulata intre faze, verificarea drepturilor in plus/lipsa, resetarea cu radacina din test; verificare manuala pe Windows pentru serviciile reale (pasi in `docs/TESTE_RAMASE.md`).

### 16.7. Decizii cerute

1. Folderul de date al masinii `C:\ProgramData\BlazorStoc\` (cu mutarea unica a datelor si cheilor) este acceptat?
2. Conturi cu nume fixe noi (`blazorstoc_app` in loc de `blazorstoc_dev`, cu `RENAME USER` la instalare) sau pastram numele actuale?
3. Somarea de la prima pornire blocheaza paginile administratorului pana la schimbare (in dezvoltare doar banner), sau doar banner peste tot?
4. Pe server de retea, scriptul de instalare cere parole proprii inca de la instalare (fara parole implicite) ca varianta recomandata?

## 17. Raspunsuri la 16.7 (utilizator, 20.10.2026)

1. Folderul de date al masinii `C:\ProgramData\BlazorStoc\`, cu mutarea unica a datelor si a cheilor de criptare: DA.
2. Conturile au nume fixe noi, bazate pe seed-ul `ESPGestiuneStoc`. Propunere de nume (o singura constanta in catalogul `DatabaseAccounts`, usor de schimbat): `ESPGestiuneStoc_app` (accesul aplicatiei), `ESPGestiuneStoc_migrator`, `ESPGestiuneStoc_backup`, `ESPGestiuneStoc_restore`; scriptul de instalare creeaza conturile cu aceste nume (instalarile existente primesc `RENAME USER` din `blazorstoc_dev`, `blazorstoc_migrator`, `blazorstoc_backup`, `blazorstoc_restore`). Numele fixe nu mai sunt editabile in pagina. Pentru conturile de test ramane `blazorstoc_test_app`.
3. Somarea de la prima pornire BLOCHEAZA: cat timp un cont are parola implicita, administratorul nu poate folosi alte pagini pana nu le schimba (fara exceptie pentru dezvoltare; pagina Conturi si „Iesire” raman accesibile).
4. Pe server de retea (si, in general, la instalare) scriptul cere parole proprii, cu posibilitatea de a le genera; parolele implicite raman doar pentru prima pornire fara configurare.
5. Fisier de configurare exportat pentru administrator, dupa schimbarea parolelor (nou, propunere de detaliu):
   - generat dupa orice schimbare reusita de parole (pe cont, „Schimba toate” sau resetare), dupa ce verificarea conturilor a trecut;
   - continutul: host si port ale serverului, numele bazei, numele serviciului bazei, cele patru conturi cu parolele lor, data si operatorul. Contine secrete: se descarca o singura data din pagina, nu se pastreaza pe server, nu intra in jurnal sau log; jurnalul spune doar ca s-a exportat fisierul;
   - protejat cu o fraza-parola aleasa de administrator in momentul exportului (criptare autentificata), ca sa nu stea in clar in Descarcari; varianta fara fraza ramane doar cu confirmare explicita;
   - folosit si la recuperare: „Importa configurarea” scrie din fisier valorile in fisierul de suprascriere si verifica conturile (acopera cheile de criptare pierdute fara a avea nevoie de radacina).
   - se adauga la etapa 2 din 16.5; topicul de help `setari-baza-conexiune` si captura lui se actualizeaza (regula 16.1.7).

Etapele ramase neschimbate (16.5); urmatorul pas este etapa 1 (folderul de date si mutarea, configuratia efectiva pentru toate procesele, tab independent de permisiunile din baza).

### 17.1. Completare (utilizator, 20.10.2026): parola de root in fisierul pentru administrator

- Fisierul de configurare exportat contine si contul de administrare al serverului (de obicei `root`) cu parola lui.
- Aplicatia nu cunoaste si nu pastreaza parola de root (nu o citeste din `admin.private.cnf`): la export, administratorul o tasteaza pe loc. Aplicatia o foloseste o singura data, pentru a verifica ca merge (conectare cu `SELECT 1` pe serverul din setarile in vigoare) inainte sa o scrie in fisier, ca fisierul sa nu ajunga cu o parola gresita; nu se salveaza nicaieri altundeva, nu intra in jurnal sau log (jurnalul spune doar ca fisierul a fost exportat, cu sau fara contul de administrare).
- Pentru ca fisierul devine cheia intregii baze, fraza-parola de criptare este OBLIGATORIE cand contine parola de root (nu se mai ofera varianta fara fraza); pagina avertizeaza sa fie pastrat intr-un loc sigur (seif, manager de parole), nu pe calculatorul serverului si nu in repository.
- La „Importa configurarea” contul de administrare din fisier poate fi folosit pentru „Reseteaza conturile” (16.3) fara sa fie tastat din nou, dupa ce administratorul introduce fraza-parola.
- Ramane valabil: fisierul nu se pastreaza pe server dupa descarcare.

### 17.2. Corectie (utilizator, 20.10.2026): parola de root intra automat in export

Tastarea parolei de root la export nu este practica (parola lunga si generata, imposibil de tinut minte). Inlocuieste modul din 17.1; restul din 17.1 (fraza-parola obligatorie, fisierul nu ramane pe server, importul) ramane.

- **Aplicatia pastreaza parola de root, criptat.** O genereaza si o pune instalatorul (`tools\setup-dev-environment.ps1` si pasul de instalare pe server), care oricum o stabileste: parola lunga generata, scrisa intr-un depozit de acreditari de administrare, `C:\ProgramData\BlazorStoc\secrets\db-admin.protected`, criptat cu protectia Windows a masinii (DPAPI `LocalMachine`) si cu ACL doar pentru `SYSTEM` si `Administrators`. Aplicatia ruleaza ca `LocalSystem` (16.2), deci il poate citi; un utilizator obisnuit al calculatorului nu.
- **Separare de restul configuratiei.** Parola de root NU intra in configuratia aplicatiei, nu apare in siruri de conexiune si nu este citita de procesele de lucru: o citeste doar un serviciu dedicat (`IDatabaseAdminCredentials`) si numai pentru patru operatii: export, resetarea conturilor, verificarea drepturilor, schimbarea parolei de root. In rest aplicatia lucreaza numai cu cele patru conturi cu drepturi limitate.
- **Control la fiecare folosire:** rolul Administrator, reconfirmare cu parola contului, limitare de rata, refuz cat timp ruleaza backup sau restaurare; fiecare folosire se jurnalizeaza cu actiune exacta („Export configurare baza de date", „Resetare conturi baza de date", „Verificare drepturi conturi baza de date") fara nicio parola; parola nu se afiseaza pe ecran si nu se scrie in log.
- **Schimbarea parolei de root din pagina** (operatie noua, aceeasi tehnica: contul isi schimba singur parola, secventa in doua faze din 15.2.C): depozitul se actualizeaza automat, fisierul exportat anterior devine vechi si pagina spune sa fie refacut.
- **Compromis asumat:** cine are rolul Administrator in aplicatie si parola proprie poate obtine, prin export, accesul de root la baza (pana acum il avea doar cine avea calculatorul). Reducem expunerea: fisierul exportat este mereu criptat cu fraza-parola, parola de root nu sta in claritate pe disc si nici in memorie dupa folosire, iar rolul Administrator ramane protejat de „ultimul administrator” si de limitarea de rata. Daca nu se doreste, exportul fara radacina ramane o optiune de bifat (decizia 17.3).
- **Pe server remote** parola de root nu este a aplicatiei: exportul contine acolo doar cele patru conturi, iar campul de root se completeaza manual (optional) sau ramane gol; resetarea cere credentialele tastate.
- Teste: depozitul nu contine parola in clar; o parola decriptata nu ajunge in jurnal, log sau in configuratia aplicatiei; export si import cu fraza gresita/corecta; schimbarea parolei de root cu revenire; un utilizator fara rolul Administrator este refuzat.

### 17.3. Decizie ramasa

Decis (utilizator, 20.10.2026): exportul include contul de administrare IMPLICIT, cu optiune de debifat „Include contul de administrare" (la debifare fisierul contine doar cele patru conturi). Fraza-parola ramane obligatorie cand contine root.

## 18. Implementat (20.10.2026): serverul bazei, portul serverului si portul aplicatiei (cerere utilizator)

Cerere: portul din „Conexiune" schimba doar unde se conecteaza aplicatia, deci nu avea sens; utilizatorul vrea sa configureze (a) portul pe care asculta baza de date si (b) portul pe care ruleaza aplicatia, ca sa rezolve conflictele cu alte aplicatii. Rezervarea unui port in Windows nu se face (decizie: renuntat).

### 18.1. Pagina Conexiune are acum patru carduri, in aceasta ordine
1. **Serverul bazei de date** (`DatabaseServerPanel`, `Services/DatabaseServer.cs`): serviciul Windows (`Database:ServerServiceName`, implicit `BlazorStocMariaDB`), portul din `my.ini`, portul conexiunii si daca raspunde. Butoane: Reporneste / Opreste (server pornit), Porneste (oprit), Schimba portul serverului, Actualizeaza starea. Fiecare operatie are fereastra ei (parola administratorului; la oprire cuvantul OPRESTE BAZA), ia lacatul de intretinere si se jurnalizeaza cu actiune proprie (Oprire / Pornire / Repornire / Modificare port server baza de date).
2. **Conexiunea la baza de date**: host, port la care se conecteaza, cont, parola, SSL (ca inainte; portul serverului nu se mai schimba de aici).
3. **Aplicatia** (`ApplicationPanel`): portul si interfata; „Salveaza si reporneste aplicatia" salveaza, reporneste si pagina se deschide singura pe adresa noua (`wwwroot/app-restart.js`).
4. **Conturile bazei de date** (neschimbat).

### 18.2. Cand se poate controla serverul (matricea 13.1, implementata)
Doar daca hostul este acest calculator, serviciul exista si portul din `my.ini` al serviciului este portul conexiunii (altfel pagina spune de ce, fara butoane). Nimic nu accepta un nume de serviciu tastat.

### 18.3. Schimbarea portului serverului
Valideaza (1024-65535, diferit de cel curent si de portul aplicatiei, liber), copie `my.ini.bak-<data>`, modifica numai linia `port=` (scriere atomica), opreste si porneste serviciul, asteapta raspunsul cu contul aplicatiei pe portul nou. Reusit: conexiunea aplicatiei (fisierul de suprascriere) si fisierul contului de administrare din folderul de date primesc portul nou. Esec: `my.ini` se pune la loc, serverul porneste pe portul vechi, conexiunea nu se schimba, pagina spune ce a esuat; jurnalizat si esecul. Dupa schimbare: `tools\sync-dev-port.ps1 -Port N` actualizeaza fisierele de dezvoltare (admin.private.cnf al instantei, `local-secrets\*.private.json`).

### 18.4. Portul serverului ocupat de alt program (scenariul „aplicatia porneste, portul bazei e ocupat")
Aplicatia porneste oricum. Cardul numeste programul de pe port (nume si PID, din `netstat`), propune primul port liber, arata „nu raspunde" (ceva raspunde pe port, dar nu serverul) si inchide „Porneste serverul". „Schimba portul serverului" porneste serverul direct pe portul nou, fara oprire. Nu se schimba nimic automat.

### 18.5. Portul aplicatiei
`tools\supervisor.ps1` nu mai impune adresa (`ASPNETCORE_URLS`): da `Hosting__DefaultPort` (portul supervizorului) si `App__Supervisor`; aplicatia asculta pe portul salvat din pagina cand e liber, altfel pe cel propus (nota in pagina). Supervizorul citeste portul salvat (`Get-SavedPort`) pentru verificarile de sanatate si pentru `status` (`ListenPort`). Repornirea: aplicatia iese cu codul 75 dupa oprirea in ordine a gazdei (`ApplicationRestart`); supervizorul o porneste imediat, fara sa o numere ca esec. Fara supervizor (`dotnet run`) repornirea nu se ofera, iar portul se aplica la urmatoarea pornire. Adresele impuse cu `--urls`/`ASPNETCORE_URLS` castiga ca inainte.

### 18.6. Serviciul bazei pe dezvoltare
`tools\install-mariadb-service.ps1` (cere elevare singur): opreste supervizorul previewului si serverul pornit ascuns, instaleaza `BlazorStocMariaDB` cu `my.ini`-ul instantei, cont virtual `NT SERVICE\BlazorStocMariaDB`, pornire automata si repornire dupa cadere, drept de pornire/oprire doar pentru utilizatorul curent. Supervizorul previewului se inregistreaza apoi FARA `-WithMariaDb` (altfel ar porni un al doilea server). `-Uninstall` il scoate.

### 18.7. Verificat
Teste: grupul `dbsettings` (150): my.ini, regulile, secventele cu serviciu fals (oprire, pornire, repornire, schimbare port reusita si cu revenire, port ocupat, acces, parola, lacat), repornirea aplicatiei, `AppHosting` cu port propus, cardurile. Pe calculator, cu serviciul real si pagina: repornire server, port 3307 -> 3308 -> 3307, starea cu portul tinut de alt program (programul numit, port propus), port aplicatie 5089 -> 5090 -> 5089 sub supervizor cu deschiderea automata a adresei noi. Ramase (monitor de sanatate si pagina „Baza indisponibila", 3b; aplicatia ca serviciu Windows, 3a) sunt etape separate.
