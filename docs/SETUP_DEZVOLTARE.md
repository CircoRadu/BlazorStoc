# Instalarea mediului de dezvoltare BlazorStoc pe un calculator nou

Scop: pe orice calculator Windows, dupa `git clone`, un agent Claude (sau utilizatorul) poate instala si configura tot ce este necesar ca sa continue dezvoltarea: .NET, MariaDB local, conturile bazei, schema, fisierele de configurare private si preview-ul. Documentul este scris pentru Claude: citeste-l integral inainte de a instala ceva si ruleaza pasii in ordine.

Data redactarii: 02.10.2026. Scripturile de mai jos au fost verificate pe o instanta MariaDB de proba, creata de la zero (vezi sectiunea 8 pentru ce a fost si ce nu a fost verificat).

## 1. Ce trebuie sa existe pe calculator

| Componenta | Versiune | Cum se instaleaza | Observatii |
|---|---|---|---|
| Windows | 10 sau 11, x64 | - | Proiectul foloseste biblioteci native Windows (`OpenCvSharp4.runtime.win`); nu este pregatit pentru Linux/macOS |
| Git | orice versiune recenta | `winget install Git.Git` | |
| .NET SDK | **9.x** (testat cu 9.0.318) | `winget install Microsoft.DotNet.SDK.9` | `TargetFramework` este `net9.0`; nu exista `global.json` |
| Microsoft Visual C++ Redistributable x64 | 2015-2022 | `winget install Microsoft.VCRedist.2015+.x64` | Necesar bibliotecilor native (Tesseract, OpenCV, PDFium) pentru OCR si PDF; instaleaza-l daca apare o eroare de DLL lipsa |
| MariaDB Server | **11.4.13** (zip Windows x64) | descarcat automat de `tools\setup-dev-environment.ps1` | SHA-256 verificat: `D62986D433EEEBFDE218560B276103831604A61E929E87F1A17F5AEBD80257E2`; NU ca serviciu Windows, proces pornit de script |
| Claude Code | - | instalarea oficiala | Optional, pentru a continua cu agentul |

**Nu sunt necesare:** Docker (permis pentru teste, dar neutilizat), SQLite (eliminat), `gh` (push-ul merge cu Git Credential Manager), IIS, NAS/QNAP (publicarea ramane in afara fazei curente), Node.js.

Pachete NuGet (se restaureaza singure la `dotnet build`): MySqlConnector, PdfPig, PDFsharp, PDFtoImage, OpenCvSharp4 (+ runtime win), Microsoft.ML.OnnxRuntime, Tesseract. Datele Tesseract (`eng`, `ron`), modelul ONNX de cifre si fonturile PT Sans sunt in repository (`Assets/`).

## 2. Pasii, pe scurt

```powershell
git clone https://github.com/CircoRadu/BlazorStoc.git
cd BlazorStoc
.\tools\setup-dev-environment.ps1      # MariaDB + conturi + schema + fisiere private (5-10 minute)
.\tools\start-preview.ps1              # publica si porneste preview-ul pe http://127.0.0.1:5087/
```

Autentificare in preview: utilizatorul `admin`, parola din campul `Authentication.Password` al fisierului `local-secrets\application-connection.private.json` (generata de script; nu o copia in chat, in documentatie sau in Git).

Repository-ul este privat; la primul `git clone`/`git push` se deschide Git Credential Manager pentru autentificarea in browser (o face utilizatorul).

## 3. Ce face `tools\setup-dev-environment.ps1`

Parametri: `-MariaRoot` (implicit `C:\Dev\BlazorStoc-MariaDB`), `-Port` (implicit 3307), `-SecretsDir` (implicit `local-secrets` din repository), `-MariaBinDir` (foloseste o distributie MariaDB deja existenta in loc sa descarce), `-SkipTestDatabase`.

1. Verifica .NET 9 SDK si Git (daca lipsesc, se opreste cu comanda `winget` de rulat).
2. Descarca distributia MariaDB 11.4.13 de pe `archive.mariadb.org`, verifica SHA-256 si o dezarhiveaza in `-MariaRoot`. Nu continua daca hashul nu se potriveste.
3. Initializeaza directorul de date (`mariadb-install-db`), scrie `my.ini` (loopback `127.0.0.1`, port 3307, `utf8mb4`, UTC `+00:00`, `STRICT_ALL_TABLES`, `skip-name-resolve`) si `admin.private.cnf` cu parola root generata. Toate sunt in afara repository-ului.
4. Porneste serverul (`tools\dev-mariadb.ps1`).
5. Creeaza bazele `BlazorStoc` si `blazorstoc_test` (`utf8mb4_nopad_bin`) si conturile de mai jos, toate `@127.0.0.1` cu `REQUIRE SSL` si parole generate aleatoriu:

| Cont | Drepturi | Folosit pentru |
|---|---|---|
| `blazorstoc_dev` | SELECT, INSERT, UPDATE, DELETE pe `BlazorStoc` | aplicatia (fara DDL) |
| `blazorstoc_migrator` | CREATE, DROP, REFERENCES, INDEX, ALTER, CREATE VIEW, TRIGGER pe `BlazorStoc` si `blazorstoc_test` | migrarile de schema (`--migrate-schema`, la pornire) |
| `blazorstoc_backup` | SELECT, SHOW VIEW, TRIGGER, LOCK TABLES pe `BlazorStoc` | `mariadb-dump` din backupul din aplicatie |
| `blazorstoc_restore` | ALL pe `BlazorStoc_bak` si `BlazorStoc_old`; SELECT, ALTER, DROP, CREATE, INSERT, TRIGGER pe `BlazorStoc` | restaurarea bazei din aplicatie |
| `blazorstoc_test_app` | SELECT, INSERT, UPDATE, DELETE pe `blazorstoc_test` | verificarile de integrare |

6. Scrie fisierele private (nu intra in Git: `local-secrets/` si `*.private.*` sunt in `.gitignore`):
   - `application-connection.private.json`: conexiunea aplicatiei, utilizatorul `admin` si parola lui, caile catre `mariadb.exe`/`mariadb-dump.exe` (`Database:MariaClientExecutablePath`, `Database:MariaDumpExecutablePath`), radacina fisierelor (`Database:MariaAssetsRoot`, `App:DataProtectionPath`);
   - `migration-account.private.json`, `backup-account.private.json`, `restore-account.private.json`, `test-database.private.json`.
7. Incarca schema de baza (`database\mariadb\schema-mariadb.sql`, 28 de tabele) si triggerele (`triggers-mariadb.sql`), apoi ruleaza `dotnet run ... --migrate-schema`, care aplica migrarile 1-10 din `Services/MariaSchemaMigrations.cs`. Rezultatul corect: **41 de tabele si 18 triggere** in `BlazorStoc` si in `blazorstoc_test` (aceleasi cifre ca pe calculatorul de dezvoltare initial).

Scriptul nu suprascrie fisiere private existente si nu afiseaza parole. Daca exista doar o parte din fisierele private, se opreste cu un mesaj; pentru a reface totul de la zero, sterge directorul `-MariaRoot` si fisierele `*.private.json` generate, apoi ruleaza-l din nou. O nepotrivire intre calea implicita din cod (`%LOCALAPPDATA%\BlazorStoc-MariaDB`) si calea aleasa la instalare este acoperita de cheile din `application-connection.private.json`.

## 4. Lucrul de zi cu zi

| Ce | Comanda |
|---|---|
| Porneste / opreste / verifica MariaDB | `.\tools\dev-mariadb.ps1 -Action start` (sau `stop`, `status`) |
| Publica si reporneste preview-ul | `.\tools\start-preview.ps1` (`-Port 5090` pentru alt port, `-NoPublish` pentru repornire fara republicare) |
| Aplica migrarile de schema | `dotnet run --project BlazorStoc.csproj -c Release -- --migrate-schema` (cu `Database__PrivateConfigPath` setat) sau doar porneste preview-ul: migrarile se aplica la pornire |
| Teste fara baza | `dotnet run --project tests/BlazorStoc.Checks -c Release` |
| Teste cu integrare MariaDB | `$env:RUN_MARIA_INTEGRATION_CHECKS='1'; $env:MARIA_TEST_CONFIG_PATH="$PWD\local-secrets\test-database.private.json"; dotnet run --project tests/BlazorStoc.Checks -c Release` |
| Doar verificarile de facturi | `$env:INVOICE_CHECKS_ONLY='1'; dotnet run --project tests/BlazorStoc.Checks` |
| Raport pe facturi reale | `dotnet run --project tests/BlazorStoc.InvoiceCorpus -- <pdf\|director> [--words] [--transfer]` |

`start-preview.ps1` pastreaza reteta obligatorie: publicare in `artifacts\notif-build`, pornire din directorul proiectului cu `ASPNETCORE_WEBROOT` catre `wwwroot`-ul publicat (altfel foaia de stil se serveste cu 0 octeti), verificarea foii de stil cu `Accept-Encoding`. Jurnalele sunt in `preview-<port>.stdout.log` / `.stderr.log`. Dupa fiecare task ramane un preview pornit (regula din `CLAUDE.md`).

Pentru a controla baza direct, foloseste clientul cu fisierul root (parola nu apare in linia de comanda):

```powershell
& "C:\Dev\BlazorStoc-MariaDB\mariadb-11.4.13-winx64\bin\mariadb.exe" --defaults-extra-file=C:\Dev\BlazorStoc-MariaDB\admin.private.cnf BlazorStoc
```

## 5. Ce NU vine odata cu `git clone` (se aduce separat, daca e nevoie)

| Element | Unde era pe calculatorul initial | Observatii |
|---|---|---|
| Datele bazei | MariaDB `C:\Dev\BlazorStoc-MariaDB\data` | Baza noua porneste goala (doar schema). Pentru datele reale vezi sectiunea 6 |
| Parolele conturilor | `local-secrets\*.private.json`, `C:\Dev\BlazorStoc-MariaDB\admin.private.cnf` | Pe calculatorul nou se genereaza altele; nu le copia in Git sau in chat |
| Fisierele de pe disc | `%LOCALAPPDATA%\BlazorStoc-MariaDB\assets` (imagini produse, atasamente, fisiere arhivate, fotografii, chei Data Protection) | **Nu sunt incluse in backupul bazei** (risc acceptat in documentele de propunere). Se copiaza manual, impreuna cu `data\` din proiect, daca vrei aceleasi fisiere |
| Configuratii locale | `data\anaf-configuration.json`, `data\map-configuration.json`, `data\audit-events.jsonl` | Ignorate de Git; aplicatia le recreeaza cu valori implicite |
| Cheile Data Protection | `Database:MariaAssetsRoot\data-protection-keys` | Fara ele, sesiunile existente si datele protejate nu se pot citi; pe un calculator nou nu conteaza (utilizatorii se autentifica din nou) |
| Memoria agentului Claude | `C:\Users\<utilizator>\.claude\projects\...\memory` | Este per calculator si per cale de proiect; regulile permanente sunt in `CLAUDE.md`, `AGENTS.md` si `TODO.md` |

## 6. Mutarea datelor reale pe calculatorul nou (optional)

Metoda recomandata, in ordine:

1. Pe calculatorul vechi, fa un backup complet al bazei (functia de backup din aplicatie sau `mariadb-dump`, vezi mai jos) si copiaza fisierul pe un mediu sigur; contine hashuri de parole si date de clienti, deci nu il pune in Git.
2. Pe cel nou, dupa `setup-dev-environment.ps1`, incarca exportul peste baza `BlazorStoc` (are deja schema; exportul sterge si recreeaza tabelele).

```powershell
# calculator vechi (--result-file evita codificarea UTF-16 a redirectarii din Windows PowerShell 5.1)
mariadb-dump --defaults-extra-file=C:\Dev\BlazorStoc-MariaDB\admin.private.cnf --single-transaction --routines --triggers --result-file=BlazorStoc-export.sql BlazorStoc
# calculator nou
mariadb --defaults-extra-file=C:\Dev\BlazorStoc-MariaDB\admin.private.cnf --default-character-set=utf8mb4 BlazorStoc -e "source BlazorStoc-export.sql"
```

**Neverificat:** acest transfer nu a fost executat pe doua calculatoare. Dupa import, verifica numarul de tabele (41), de triggere (18) si de randuri (`audit_events`, `products`, `web_users`), porneste preview-ul si autentifica-te cu un cont existent din date (nu cu `admin` generat de script). Daca importul esueaza din cauza `DEFINER` pe triggere, restaureaza prin fluxul din aplicatie (`Inventar -> Restaureaza stoc`), care curata clauzele `DEFINER`, sau cere indrumare utilizatorului.

## 7. Depanare

- **MariaDB nu porneste dupa o oprire necurata a calculatorului** (`Aria recovery failed`, `Unknown storage engine 'Aria'` in `server-error.log`): s-a intamplat la 02.10.2026. Remediere folosita: opreste procesul `mariadbd`, copiaza intreg `data` intr-un director de siguranta (de exemplu `data-backup-<data>`), muta `aria_log*` din `data` intr-un alt director, ruleaza `aria_chk -r` pe fiecare fisier `*.MAI` (fara extensie), apoi porneste din nou cu `tools\dev-mariadb.ps1 -Action start`. Tabelele aplicatiei sunt InnoDB si nu sunt afectate; Aria tine tabelele de sistem `mysql.*`.
- **Preview fara CSS** (foaie de stil de 0 octeti): lipseste `ASPNETCORE_WEBROOT`; porneste preview-ul cu `tools\start-preview.ps1`, nu direct din `bin\`.
- **`fatal: detected dubious ownership`** la comenzi Git: directorul apartine altui cont Windows. Foloseste `git -c safe.directory=<cale> ...` sau `git config --global --add safe.directory <cale>` (modifica configuratia globala; cere acordul utilizatorului). Dupa un `git clone` facut de contul curent nu apare.
- **Portul 5087 sau 3307 ocupat:** `Get-NetTCPConnection -State Listen -LocalPort <port>`; `start-preview.ps1` opreste singur procesul de pe portul preview-ului (acelasi cont Windows); pentru MariaDB alege alt `-Port` la instalare.
- **`PowerShell 5.1`:** scripturile din `tools\` ruleaza in Windows PowerShell 5.1 (cel din Windows); nu sunt necesare PowerShell 7.
- **Eroare SSL la conectare:** conturile `blazorstoc_*` cer TLS; nu dezactiva `SslMode=Required` si nu scoate `REQUIRE SSL`.

## 8. Ce a fost verificat si ce nu (02.10.2026)

Verificat, pe o instanta de proba (alt port, alt director, secrete separate, stearsa dupa test), folosind distributia MariaDB 11.4.13 deja prezenta pe calculatorul de dezvoltare:

- `setup-dev-environment.ps1` de la zero si repetat (a doua rulare: pasi sariti corect, fara modificari);
- schema rezultata: 41 de tabele si 18 triggere in `BlazorStoc` si in `blazorstoc_test`, identic cu baza reala; cele 10 migrari aplicate de la zero;
- `start-preview.ps1` (fara publicare), foaia de stil servita nenula, autentificare reusita cu contul `admin` generat;
- suita `tests/BlazorStoc.Checks` cu `RUN_MARIA_INTEGRATION_CHECKS=1` pe baza de test creata de script: iesire 0, toate sectiunile MariaDB trecute.

**Neverificat** (nu s-a putut executa fara a descarca fisiere sau fara un al doilea calculator): descarcarea efectiva a arhivei MariaDB (adresa exista, 95.024.126 octeti, dar descarcarea si verificarea hashului in script nu au rulat), instalarea `winget` a prerequisitelor pe un Windows curat, necesitatea exacta a VC++ Redistributable, pasul `-Port` diferit de 3307 pe un calculator real, transferul datelor (sectiunea 6) si publicarea (`dotnet publish` fara `--no-restore`) pe o masina fara pachete NuGet in cache. La prima instalare reala, noteaza orice abatere in `docs/TESTE_RAMASE.md`.

## 9. Reguli de lucru (rezumat din `CLAUDE.md`)

- Citeste `CLAUDE.md`, `TODO.md`, `docs/PROJECT_STATE.md` inainte de orice modificare. Claude este agentul unic (`mode: claude_only`).
- Commit si push **numai la cererea explicita a utilizatorului**; nu commita secrete (`local-secrets/`, `*.private.*`, `keys/`, `data/` sunt ignorate).
- Fisierele `.md` se scriu fara diacritice; textele din aplicatie sunt in romana, cu diacritice, date in forma `dd.mm.yyyy`.
- Operatiile noi care modifica date se jurnalizeaza cu actiuni exacte (`AuditActions`, filtrul Audit, test).
- Taskurile finalizate se muta din `TODO.md` la sfarsitul `IMPLEMENTED.md`; verificarile nefacute se trec in `docs/TESTE_RAMASE.md`.
- La finalul fiecarui task ramane un preview pornit cu implementarea.
