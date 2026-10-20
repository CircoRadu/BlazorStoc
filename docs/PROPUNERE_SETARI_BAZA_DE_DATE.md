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
- Numele serviciului: detectat (servicii cu `mariadb`/`mysql` in nume, verificate prin `ImagePath` si port), confirmat de administrator o singura data si salvat in fisierul de suprascriere (`Database:ServerServiceName`). Daca nu se gaseste niciun serviciu, linia din 13.1 spune cum se instaleaza (`mariadbd --install`).
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

### 13.9. Intrebari ramase

1. Pe server, MariaDB ruleaza deja ca serviciu Windows? Cum se numeste (de obicei `MariaDB`)?
2. Sub ce cont va rula serviciul aplicatiei (`LocalSystem` sau un cont dedicat)? Pentru cont dedicat, scriptul acorda dreptul de control asupra serviciului MariaDB.
3. Instanta de dezvoltare locala poate fi trecuta pe serviciu (`-InstallService`), ca pagina sa se poata testa si acolo?
4. Pagina de asteptare "Baza de date indisponibila" pentru utilizatorii obisnuiti este acceptata (13.5)?
