# Actualizarea aplicatiei pe server: documentatie de operare

Pentru cine administreaza serverul Windows pe care ruleaza BlazorStoc. Proiectarea si deciziile sunt in `docs/PROPUNERE_ACTUALIZARE.md`; aici sunt pasii. Comenzile sunt PowerShell sau consola Windows.

## 1. Ce exista

| Piesa | Unde | Ce face |
|---|---|---|
| Pachet `BlazorStoc-<versiune>.bsu` | `artifacts\release\` pe calculatorul de dezvoltare | aplicatia publicata, manifest cu sume de control, **semnatura** cheii acelui calculator |
| Installer `BlazorStoc-Actualizare-<versiune>.exe` | langa pachet | un singur fisier de copiat pe server: pachetul, Updater-ul acelei versiuni si un lansator. Merge si cand aplicatia nu mai porneste |
| Updater | `<radacina>\updater\` pe server | singurul loc cu logica de actualizare: verifica, opreste, face backup, schimba versiunea, migreaza, porneste, asteapta `/health`, revine singur la versiunea veche daca cea noua nu raspunde |
| Setari -> Actualizare | in aplicatie (doar administrator) | incarci pachetul `.bsu`, vezi ce aduce, confirmi cu motiv; utilizatorii sunt anuntati 60 s |

Pe server, radacina instalarii (ex. `D:\BlazorStoc`) arata asa:

```
releases\<versiune>\app\   codul fiecarei versiuni (neschimbat dupa instalare; se pastreaza ultimele 3)
current.txt, previous.txt   versiunea care ruleaza si cea dinainte
updater\                    Updater, settings.json, trusted-keys.json (doar Administrators si SYSTEM)
scripts\                    supervisor.ps1, data-root.ps1 (le ruleaza contul aplicatiei)
private\                    contul de migrare (doar Administrators si SYSTEM)
updates\staged, applied\    pachetele incarcate din aplicatie / cele aplicate (ultimele 3)
state\, logs\               starea si istoricul actualizarilor, jurnalele (logs\updater-<data>.log)
```

Datele (baza, fisiere, chei Data Protection, setari NAS, backup-uri) sunt in **folderul de date** al aplicatiei (implicit `C:\ProgramData\BlazorStoc`), nu in radacina instalarii: o actualizare sau o revenire nu le atinge.

## 2. Pe calculatorul de dezvoltare: facerea unei versiuni

```powershell
.\tools\release.ps1 -NotesFile note.txt          # suita completa, publish, pachet semnat, installer
.\tools\release.ps1 -MinFromVersion 0.5.0        # nu se poate instala peste versiuni mai vechi decat 0.5.0
```

- Versiunea este `<Version>` din `BlazorStoc.csproj` (sau `-Version x.y.z`). `note.txt` (ce aduce versiunea) apare administratorului inainte de confirmare.
- La prima rulare pe un calculator se face **cheia de semnare** (protejata cu DPAPI pe utilizatorul curent, in `%LOCALAPPDATA%\BlazorStoc-release`). Nu se inlocuieste: serverele au amprenta ei. Pierderea cheii inseamna o cheie noua, care se confirma pe server (sectiunea 7).
- La final scriptul afiseaza **amprenta cheii** si **SHA-256 al installerului**. Amprenta o compari pe server cand cheia e noua; SHA-256 il compari cu `Get-FileHash` dupa copiere.
- Scriptul verifica pachetul si installerul (`--check`) inainte sa termine.

## 3. Server nou: prima instalare

Cerinte: Windows x64; .NET 9 runtime (Microsoft.NETCore.App 9.x); MariaDB cu schema si conturile `app`, `migrator`, `backup`, `restore` (Setari -> Baza de date, sau `docs/SETUP_DEZVOLTARE.md`); contul Windows sub care va rula aplicatia, **cu drept de administrator** pe acest pas (Updater-ul inregistreaza sarcina prin care aplicatia il cheama, pe contul care ruleaza `init`).

Autentifica-te ca acel cont si deschide o consola **ca administrator**, in folderul cu installerul copiat pe server:

```powershell
# 1. Pregatirea instalarii (nu instaleaza nicio versiune)
.\BlazorStoc-Actualizare-0.5.1.exe --init --install-root D:\BlazorStoc --port 5087 --migration-account-file C:\...\migration-account.private.json
# optional: --maria-root <folder MariaDB> --env "App__DataRoot=D:\BlazorData;..." --no-task

# 2. Supervizorul aplicatiei (comanda e afisata de pasul 1)
powershell -ExecutionPolicy Bypass -File D:\BlazorStoc\scripts\supervisor.ps1 -Action install -Port 5087 -InstallRoot D:\BlazorStoc

# 3. Prima versiune; cheia de semnare noua se confirma cu amprenta tiparita de release.ps1
.\BlazorStoc-Actualizare-0.5.1.exe --trust "5de2344e 386e97fc ..." --reason "prima instalare"
```

`--init` face folderele, copiaza Updater-ul si scripturile, protejeaza radacina (utilizatorii obisnuiti pot scrie doar `updates\staged`, `logs\` si `state\supervisor`), inregistreaza sarcina programata `BlazorStocUpdater-<hash>` si scrie radacina in `HKLM\SOFTWARE\BlazorStoc` (valoarea `InstallRoot`), ca installerul sa gaseasca singur instalarea de aici incolo. Rulat fara drept de administrator, `init` spune ca NU a protejat folderele si nu a inregistrat sarcina: se ruleaza din nou ca administrator.

Pasul 3 cere, daca nu dai `--trust`, sa tastezi primele 8 caractere ale amprentei (compara-o cu cea de pe calculatorul de dezvoltare). Dupa confirmare cheia ramane de incredere. Dupa instalare: `.\BlazorStoc-Actualizare-0.5.1.exe --status`, apoi deschide aplicatia.

## 4. Actualizare obisnuita

**Din aplicatie** (cea mai simpla): Setari -> Actualizare, alegi fisierul `.bsu`, citesti tabelul (versiune veche -> noua, semnatar, schema, note, "Poate fi instalat"), scrii motivul, "Aplica actualizarea". Utilizatorii vad 60 s un anunt (poti anula), apoi aplicatia se opreste cateva minute si se reincarca singura. Detalii: `Teste utilizator\09_Notificari_si_setari.md`.

**Cu installerul** (din consola administrator pe server, aplicatia pornita sau nu):

```powershell
.\BlazorStoc-Actualizare-0.5.2.exe --reason "corectie rapoarte"
```

Te intreaba o data ("Enter = continua"); `--yes` sare peste intrebare. Daca nu esti administrator, Windows cere drepturile si porneste o fereastra noua.

In ambele cazuri se intampla aceleasi lucruri, in ordine: semnatura si sumele de control; versiunea (nu mai veche, nu aceeasi), platforma, locul pe disc; despachetare langa versiunea care ruleaza (nimic atins pana aici, deci o refuzare nu costa timp de oprire); oprire; **backup al bazei** (tipul "inainte de actualizare", nu se sterge automat); schimbarea lui `current.txt`; migrarile cu contul `migrator`; pornire; asteptare `/health` cu versiunea noua (120 s). Daca versiunea noua nu raspunde: revine singur la cea veche si, daca migrarile pornisera, la baza dinainte. Dupa un rezultat bun installerul inlocuieste si Updater-ul din `updater\` cu cel din installer.

Rezultatul intra in **Jurnalul** aplicatiei la urmatoarea pornire ("Actualizare aplicatie (installer)", "Actualizare reusita/esuata/refuzata", "Revenire la versiunea anterioara"), chiar daca actualizarea s-a facut cand aplicatia era oprita. Daca s-a revenit singur, administratorul vede o banda rosie pana apasa "Am luat la cunostinta".

## 5. Aplicatia nu mai merge

Mai intai: `.\BlazorStoc-Actualizare-<v>.exe --status` (versiunile de pe disc, ultima actualizare, ce raspunde `/health`) si `D:\BlazorStoc\logs\updater-<data>.log`.

| Situatie | Comanda |
|---|---|
| Versiunea curenta e stricata (fisiere sterse/corupte) | `--repair` (reinstaleaza versiunea din installer peste cea care ruleaza) |
| Versiunea noua merge prost, se vrea cea de dinainte | `--rollback` |
| ...si baza trebuie adusa la un backup | `--rollback --restore-db <nume pachet backup>` |
| Se vrea o versiune mai veche decat cea care ruleaza | installerul acelei versiuni cu `--allow-downgrade` |

Un installer pentru versiunea X ruleaza si peste o instalare stricata: nu depinde de continutul lui `current`. Daca baza nu raspunde, actualizarea se refuza inainte sa opreasca aplicatia.

### Restaurarea bazei din backup-uri (server si NAS)

- Backup-urile se fac din aplicatie (Setari -> Backup) in folderul de date; copierea pe NAS se activeaza in Setari -> Backup NAS. Din aplicatie, Setari -> Backup arata pachetele de pe server si de pe NAS; unul care e doar pe NAS este adus pe server (verificat prin suma de control) si apoi restaurat. Restaurarea face intai un pachet de siguranta "pre-restaurare" al bazei de acum.
- Cu aplicatia moarta, `--restore-db` primeste **numele unui pachet aflat pe server**, in folderul de backup-uri. Un pachet care e doar pe NAS se copiaza intai de mana in acel folder.
- Un pachet facut in alt mediu (de exemplu dezvoltare) se poate restaura pe server (verificat 20.10.2026 pe baza de test): tabelele bazei live sunt inlocuite cu cele din pachet, adica **utilizatorii, produsele, stocul, setarile si jurnalul din acel mediu**; conturile de acum (inclusiv ale administratorului live) dispar. Conditii si urmari:
  - Pachetul trebuie facut de **aceeasi versiune a aplicatiei** (acelasi set de tabele). Unul mai vechi este refuzat cu "structura bazei de date difera de baza curente", fara sa schimbe nimic: aduci mediul sursa la versiunea instalata pe server si faci un backup nou.
  - Setarile NAS vin cu pachetul (cale, cont, **copiere si program activate**), iar parola lor este criptata cu cheile Data Protection ale mediului sursa. Pe un server cu alte chei, NAS-ul apare ca neconfigurat pana reintroduci parola in Setari -> Backup NAS; cu aceleasi chei, serverul se conecteaza la acelasi NAS si acelasi folder ca dezvoltarea. Reconfigureaza NAS-ul serverului (folder propriu) imediat dupa restaurare.
  - Imaginile si fisierele aplicatiei (pozele produselor, fisierele proiectelor, pozele de interventii, arhiva) sunt in pachet din 20.10.2026 si se restaureaza odata cu baza; un pachet mai vechi restaureaza doar baza (vezi mai jos). Cheile Data Protection nu sunt in pachet.
  - Restaurarea face intai un pachet de siguranta "pre-restaurare" al bazei de dinainte; restaurandu-l pe acela revii exact la starea de dinainte (verificat).
  - Nu amesteca folderul de NAS al dezvoltarii cu cel al serverului.
- O restaurare aduce baza la momentul pachetului: tot ce s-a scris dupa, inclusiv randurile din Jurnal ale unei actualizari facute dupa acel backup, dispare (Updater-ul nu le mai scrie a doua oara, fiindca le-a scris o data). Istoricul complet ramane in `state\update-history.jsonl` si in `logs\updater-<data>.log`.
- Triggerele bazei apartin, dupa o restaurare, contului de restaurare (`DEFINER`): nu sterge acel cont dupa ce ai restaurat o data, altfel scrierile in tabelele cu triggere esueaza cu „The user specified as a definer does not exist" (il recreezi cu aceleasi drepturi sau refaci triggerele cu un alt `DEFINER`). Constatat la curatarea unui cont de proba.
- `--rollback --restore-db` cere contul de restaurare configurat (Setari -> Baza de date; fisierul `restore-account.private.json` sta langa configurarea aplicatiei). Fara el restaurarea esueaza, Updater-ul lasa aplicatia **oprita** pe versiunea curenta (baza nu a fost atinsa) si spune asta: configureaza contul si repeta, sau porneste aplicatia cu `scripts\supervisor.ps1 -Action start`.

### Imaginile si fisierele in backup si la migrare (dezvoltare -> server)

Baza tine doar numele fisierelor (`relative_path` in `product_images`, la fel pentru fisierele proiectelor si pozele de interventii); fisierele sunt in `<folderul de date>\assets\` (implicit `C:\ProgramData\BlazorStoc\assets`): `product-images`, `project-files`, `service-photos`, `archive-files`. **Din 20.10.2026 orice backup contine si aceste foldere** (in pachet: `assets/<folder>/...`, lista cu sumele de control in `assets.json`, doar numarul si semnatura lor in `manifest.json`), iar restaurarea le readuce odata cu baza: fisierele se pregatesc langa foldere, folderele se schimba, apoi baza; daca baza nu se poate schimba, folderele vechi revin. Nu intra cheile Data Protection (`assets\data-protection-keys`), pachetele de backup si `run-state`. Un pachet mai vechi, facut inainte de aceasta schimbare, restaureaza numai baza si lasa imaginile de acum. Consecinte: pachetele sunt mai mari (timp, loc pe disc si pe NAS), iar un backup refuza sa se faca daca un fisier se schimba in timpul copierii.

Pentru pachetele vechi, sau ca sa duci imaginile fara sa restaurezi baza, ele se pot copia si separat, cu aceleasi nume si aceeasi structura:

```powershell
# 1. pe calculatorul de dezvoltare (aplicatia poate ramane pornita, se doar citeste)
robocopy C:\ProgramData\BlazorStoc\assets D:\transfer\assets /E /XD database-backups run-state data-protection-keys

# 2. copiezi D:\transfer\assets pe server (partajare, disc USB, arhiva)

# 3. pe server, cu aplicatia oprita (scripts\supervisor.ps1 -Action stop ...), in folderul de date al serverului
robocopy D:\transfer\assets D:\BlazorData\assets /E
icacls D:\BlazorData\assets /grant "<contul aplicatiei>:(OI)(CI)M" /T
# apoi pornesti aplicatia (scripts\supervisor.ps1 -Action start ...)
```

- Fa pasul 3 **dupa** restaurarea bazei din develop: numele fisierelor corespund randurilor din pachet (in verificarea de mai sus: 370 produse, 370 fisiere `product-<id>.png`). Verificare: numarul de fisiere din `product-images` ≥ `SELECT COUNT(*) FROM product_images`, apoi deschide cateva produse.
- Nu copia `data-protection-keys` (cheile care protejeaza parolele salvate; fiecare server are ale lui), nici `database-backups` (pachetele de backup si blocajul de operatii) si nici `run-state`. Fara `/MIR`: fisierele care exista deja pe server nu se sterg (cele fara rand in baza raman orfane, nu stricate).
- `\assets` se afla in folderul de date al serverului: cel din `App__DataRoot` (setat cu `--env` la `--init`) sau `C:\ProgramData\BlazorStoc`. Actualizarile aplicatiei nu ating acest folder.
- Nu am rulat inca aceasta copiere pe un server live; comenzile sunt cele din `tools\export-dev-data.ps1` (robocopy), fara chei si backup-uri.

## 6. Optiuni ale installerului

`--help` le listeaza. Pe scurt: fara argument = actualizare la versiunea din fisier; `--repair`, `--rollback [--restore-db <pachet>]`, `--status`, `--check [--trusted <amprenta>]` (verifica fisierul, fara drepturi de administrator, nu instaleaza nimic), `--init` (prima pregatire a unui server, vezi 3), `--install-root <folder>`, `--reason <text>`, `--trust <amprenta completa>`, `--allow-downgrade`, `--health-timeout <s>`, `--dotnet <cale>`, `--yes`, `--no-pause`.

Coduri de iesire: 0 reusit; 2 pachet invalid; 3 semnatar neacceptat; 4 refuzat (nimic schimbat); 5 versiunea noua nu a pornit, s-a revenit la cea veche; 6 esuat (aplicatia poate fi oprita: `--rollback` sau `--repair`); 10 fara drepturi de administrator; 11 fisierul nu contine pachet; 12 continut deteriorat (copiaza din nou fisierul); 13 nu e gasita o instalare (`--install-root`, sau `--init` pe un server nou); 14 lipseste dotnet / runtime-ul .NET 9; 15 anulat; 16 argumente gresite.

## 7. Cheile de incredere

Serverul accepta doar pachete semnate de o cheie din `updater\trusted-keys.json`. Din aplicatie nu se poate adauga o cheie. Se adauga:
- la installer, cu `--trust <amprenta completa>` sau tastand primele 8 caractere cand o cere, dupa ce ai comparat amprenta cu cea tiparita de `release.ps1` pe calculatorul de build;
- cu `BlazorStoc.Updater trust add <amprenta>` / `trust list` / `trust remove <amprenta>` (consola administrator, din `updater\`).

Cheie compromisa sau pierduta: `trust remove`, apoi pachet semnat cu cheia noua si confirmat la installer.

## 8. Limite si lucruri de stiut

- Installerul **nu are semnatura Authenticode**: Windows poate afisa avertismentul SmartScreen. Protectia reala este semnatura pachetului din interior, verificata de Updater cu lista de chei a serverului, nu cu ce trimite aplicatia sau installerul. Copiaza installerul pe un canal de incredere si compara SHA-256 cu cel tiparit de `release.ps1`.
- Updater-ul din `updater\` se inlocuieste **doar** prin installer. O actualizare facuta din aplicatie nu il schimba; cand o versiune aduce o corectie de Updater, ruleaza o data installerul.
- O singura actualizare la un moment dat (blocaj in `state\`); a doua spune ca alta ruleaza si nu face nimic.
- .NET si MariaDB nu se actualizeaza prin acest mecanism; pachetul declara runtime-ul necesar si se refuza daca serverul are unul mai vechi.
- Sarcina `BlazorStocUpdater-<hash>` (a Updater-ului) si sarcina supervizorului ruleaza pe contul care le-a inregistrat. Mutarea aplicatiei pe alt cont cere rularea `init` ca acel cont.
- Intr-o sesiune de consola neinteractiva (script, sarcina programata) installerul cere `--yes` si `--trust`; fara ele nu ghiceste.

## 9. Depanare

| Simptom | Cauza probabila |
|---|---|
| Setari -> Actualizare spune ca aplicatia nu ruleaza dintr-o instalare facuta de Updater | aplicatia nu a fost pornita de `scripts\supervisor.ps1 -InstallRoot ...`, sau nu s-a rulat `init` |
| "sarcina Updater-ului lipseste" la confirmare | `init` nu s-a rulat ca administrator, sau s-a schimbat contul; ruleaza din nou `--init` (idempotent) ca administrator |
| Installerul spune "No installation is registered" | nu exista valoarea din registru; da `--install-root` |
| "This file carries no package" | ai pornit lansatorul gol (`artifacts\...\launcher`), nu fisierul facut de `release.ps1` |
| Cod 12 | fisierul s-a stricat la copiere; copiaza din nou si compara SHA-256 |
| Cod 14 | instaleaza .NET 9 runtime (`winget install Microsoft.DotNet.Runtime.9`) |
| Dupa actualizare banda rosie | revenirea automata a avut loc: citeste `logs\updater-<data>.log`, apoi "Am luat la cunostinta" |
