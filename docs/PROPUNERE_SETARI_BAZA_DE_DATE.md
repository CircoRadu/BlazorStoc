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
