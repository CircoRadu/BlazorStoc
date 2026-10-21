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

**Cu fereastra (installer grafic).** Dublu clic pe `BlazorStoc-Actualizare-<versiune>.exe` (sau `--gui`) deschide o fereastra in romana, dupa ce Windows cere o data drepturi de administrator. Pe un server fara instalare: pagina 1 (folderul instalarii, implicit `C:\BlazorStoc`, si „Setari avansate”: portul aplicatiei, portul bazei de date, baza de date instalata de installer sau existenta in alta parte), pagina 2 (contul de administrator: utilizator, nume afisat, parola si parola repetata; sub a doua parola scrie, pe masura ce tastezi, `Parolele coincid` / `Parolele nu coincid inca`, iar „Continua” este activ numai cand totul este valid), pagina 3 (rezumat) si progresul cu jurnalul (butoane „Copiaza jurnalul” si, la final, „Deschide aplicatia” pe portul ales). Pe un server care are deja o instalare fereastra propune „Actualizeaza” (sau „Repara”, cand versiunea este aceeasi). Fereastra construieste aceeasi linie de comanda ca varianta in consola si porneste acelasi installer; optiunile date dupa `--gui` (de exemplu `--install-root`, `--port`, `--no-service` pentru o proba) completeaza fereastra sau se transmit mai departe. Fereastra nu poate confirma un semnatar necunoscut la o actualizare (acolo se foloseste consola cu `--trust`). Fisierul ajunge la circa 210 MB (lansatorul cu Windows Forms, necomprimat de trimming, are 46 MB).

**Varianta in consola:**

Cerinte: Windows x64; **nimic altceva de instalat**: installerul aduce in el si instaleaza singur ce lipseste (runtime-ul .NET 9, runtime-ul ASP.NET Core 9 si runtime-ul Visual C++ x64, vezi mai jos); contul Windows sub care va rula aplicatia, **cu drept de administrator** pe acest pas (instalarea inregistreaza sarcinile programate pe contul care ruleaza `--init`). Serverul de baze de date NU mai este o cerinta prealabila: installerul il face el (MariaDB 11.4.13, serviciu Windows, conturi, schema).

Autentifica-te ca acel cont si deschide o consola **ca administrator**, in folderul cu installerul copiat pe server. Nu trebuie `--init`: pe un server fara nicio instalare (nimic inregistrat in registru) installerul isi da seama singur ca este prima instalare si o face in `C:\BlazorStoc` (sau in folderul gol dat cu `--install-root`); un folder care contine alte lucruri nu este transformat in instalare din greseala (acolo se da `--init` explicit). Dublu clic pe fisier face la fel, dupa ce spune ce va instala si asteapta Enter:

```powershell
.\BlazorStoc-Actualizare-0.5.1.exe
# sau, cu folder si porturi dorite:   .\BlazorStoc-Actualizare-0.5.1.exe --install-root D:\BlazorStoc --trust "..." --port 5087
# --init explicit este pentru optiunile de mai jos (--init --no-install etc.) sau pentru un folder care nu este gol
# la consola, installerul cere intai contul de administrator al aplicatiei (utilizator, parola, parola repetata); fara consola: --admin-user <nume> + BLAZORSTOC_ADMIN_PASSWORD, sau --no-admin
# optional: --port 5087 --db-port 3307 --maria-root <folderul instantei bazei> --maria-bin <bin al unui MariaDB deja despachetat>
#           --maria-zip <arhiva MariaDB descarcata in prealabil, fara Internet> --env "App__DataRoot=D:\BlazorData;..." --no-task
#           --no-database (baza este in alta parte) --no-service (baza ca proces ascuns, doar pentru probe) --no-supervisor --no-install (doar pregatirea)
```

O singura comanda face, in ordine:

0. **Dependentele.** Fisierul contine installerele Microsoft pentru `dotnet-runtime-9.0.x-win-x64.exe`, `aspnetcore-runtime-9.0.x-win-x64.exe` (aceeasi versiune 9.0.x ca pe calculatorul de build) si `vc_redist.x64.exe` (necesar bazei de date MariaDB si bibliotecilor native ale aplicatiei). Daca pe server lipseste `dotnet` sau unul dintre cele doua runtime-uri 9, sau runtime-ul Visual C++ nu este inregistrat, installerul verifica intai semnatura Authenticode a fiecarui fisier (valida, semnatar Microsoft; altfel NU il ruleaza, cod de iesire 18) si il instaleaza silentios (`/install /quiet /norestart`, fara repornirea calculatorului; coduri acceptate 0, 1638, 1641, 3010). Ce exista deja nu se reinstaleaza. `--check` nu instaleaza nimic. Calea completa a lui `dotnet` se scrie in `updater\settings.json`, iar supervizorul cauta `dotnet` si in `Program Files\dotnet`, ca sa nu depinda de un PATH al sesiunii vechi. `release.ps1` descarca fisierele o singura data in `artifacts\prerequisites` (verifica semnatura Microsoft) si le pune in installer, care ajunge la circa 177 MB; `-SkipPrerequisites` le lasa deoparte.
1. **Porturile.** Portul initial al aplicatiei (`--port`, implicit 5087) si al bazei de date (`--db-port`, implicit 3307) se verifica pe server in momentul instalarii (ascultatori TCP pe orice adresa). Un port liber se pastreaza; unul ocupat se inlocuieste cu urmatorul liber (cele doua porturi nu coincid niciodata). Ce s-a ales se afiseaza ("Application port: 5087 is taken on this server, 5089 is free ...") si se scrie in `updater\settings.json` (`port`, `databasePort`), in `my.ini` al bazei, in `application-connection.private.json` (Database.Port) si in sarcina supervizorului. O a doua rulare a `--init` pastreaza porturile deja scrise (aplicatia care asculta pe ele nu este motiv de mutare).
2. Folderele instalarii, Updater-ul si scripturile, protectia radacinii, sarcina programata `BlazorStocUpdater-<hash>` si `HKLM\SOFTWARE\BlazorStoc` (valoarea `InstallRoot`), ca in versiunile anterioare.
3. **Serverul de baze de date** (`scripts\provision-database.ps1`): distributia MariaDB (`--maria-bin`, sau `<maria-root>\mariadb-11.4.13-winx64`, sau `--maria-zip`, sau descarcata de la archive.mariadb.org cu SHA-256 fixat; o arhiva cu alt hash este refuzata), instanta in `<install-root>\mariadb` (sau `--maria-root`), serviciul Windows `BlazorStocMariaDB` (cont virtual `NT SERVICE\BlazorStocMariaDB`, porneste cu calculatorul, repornire dupa esec; contul aplicatiei poate doar sa-l porneasca/opreasca), baza `BlazorStoc`, cele patru conturi din `Assets\Data\database-accounts.json` cu **parole generate**, schema de baza si triggerele. Fisierele private (parolele nu se afiseaza): `application-connection.private.json`, `backup-account.private.json`, `restore-account.private.json` in folderul de date al aplicatiei (`App__DataRoot` sau `C:\ProgramData\BlazorStoc`), `migration-account.private.json` in `<install-root>\private` (doar Updater-ul). **Contul de administrator al aplicatiei.** Inainte sa faca ceva, installerul cere (la consola) numele de utilizator, parola si parola repetata; la repetare, pe masura ce tastezi, linia spune daca parolele coincid (`[the passwords match]` / `[the passwords do not match yet]`), iar parolele apar ca `*`. Regulile sunt ale aplicatiei: nume de 3-100 caractere (litere, cifre, `.`, `-`, `_`), parola de cel putin 8 caractere. Dupa instalarea primei versiuni (schema si migrarile sunt gata) installerul face utilizatorul, cu rolul **Administrator**, in utilizatorii aplicatiei (tabelul `web_users`, aceeasi parola hash ca la pagina Utilizatori), iar jurnalul inregistreaza "Creare cont administrator la instalare" (de sistem, motiv "Instalarea aplicatiei"). Te autentifici cu acel utilizator si acea parola. Fara consola (instalare automata): `--admin-user <nume> [--admin-name <nume afisat>]` si parola in variabila de mediu `BLAZORSTOC_ADMIN_PASSWORD` (nu pe linia de comanda); `--no-admin` nu face niciun cont. Un utilizator cu acelasi nume care exista deja se pastreaza (parola tastata nu se aplica). Parola nu se scrie in niciun fisier; ajunge la aplicatie doar prin intrarea standard.

**Ultima solutie.** Instalarea mai face un administrator "din configurare": utilizator `admin`, parola generata, in `application-connection.private.json` (Authentication.Password), neafisata. O citesti in consola de administrator cu `(Get-Content "<folder de date>\application-connection.private.json" -Raw | ConvertFrom-Json).Authentication.Password` (installerul tipareste comanda). Nu este un utilizator din baza de date: aplicatia nu are pagina pentru schimbarea parolei lui (se schimba doar editand fisierul si repornind aplicatia).
4. **Supervizorul** aplicatiei (`supervisor.ps1 -Action install -Port <portul ales>`).
5. **Prima versiune** (ca la o actualizare: semnatura, migrarile, pornirea, /health). **Cheia de semnare a pachetului din fisier este de incredere de la prima instalare** (nu tastezi nicio amprenta): fisierul este ce ai adus si ai comparat (SHA-256 tiparit de `release.ps1`, vezi `Get-FileHash`), iar installerul spune ce amprenta a acceptat. Un pachet ulterior semnat cu alta cheie ramane refuzat pana il confirmi (`--trust` sau amprenta tastata). La `--init --no-install` nu se acorda increderea: ea se acorda la instalarea versiunii.

`--init --no-install` face doar pasii 1-4 (pregatirea); versiunea se instaleaza apoi cu `.\BlazorStoc-Actualizare-0.5.1.exe --trust "..."`. Rulat fara drept de administrator, `--init` spune ca NU a protejat folderele si nu a inregistrat sarcina, iar scriptul bazei refuza sa faca serviciul: se ruleaza din nou ca administrator (ce exista se pastreaza: nicio parola nu se schimba, nicio data nu se atinge). Daca serverul de baze de date nu se poate face, installerul se opreste inainte de prima versiune si spune cauza (`<maria-root>\server-error.log`).

Dupa instalare: `.\BlazorStoc-Actualizare-0.5.1.exe --status`, apoi deschide aplicatia pe portul scris in `updater\settings.json`.

### Acces din retea si dezinstalare

**Acces din retea.** Aplicatia instalata de installer asculta pe toate adresele (`0.0.0.0`), nu doar pe `127.0.0.1`, iar installerul creeaza regula de firewall `BlazorStoc (port <n>)` (intrare TCP, toate profilurile de retea, doar de la **subreteaua locala**; `--no-firewall` o omite). Adresa este `http://<adresa serverului>:<port>/` (portul din `updater\settings.json`); aplicatia nu are HTTPS, deci nu se deschide portul dincolo de reteaua locala fara un reverse proxy. Daca din Setari -> Baza de date -> Conexiune se alege alt port, regula de firewall trebuie schimbata (`scripts\firewall.ps1 -Action add -Port <n>`). Un administrator poate cere explicit ascultarea doar locala in aceeasi pagina (alegerea salvata de el are prioritate).

**Dezinstalare.** `BlazorStoc-Actualizare-<v>.exe --uninstall` (sau, in fereastra, butonul „Dezinstaleaza…” pe un server care are instalarea) are doua cai:
- **Doar aplicatia (implicit):** se opresc si se scot aplicatia, supervizorul, sarcinile programate, regula de firewall si serviciul bazei de date, se sterg folderele instalarii si intrarea din registru; **raman** baza de date (folderul instantei MariaDB, `<install-root>\mariadb`) si fisierele aplicatiei (poze, arhive, chei, conturile private din folderul de date). O instalare noua le refoloseste: serviciul se face din nou peste datele existente.
- **Stergere totala, cu pierderea tuturor datelor:** `--uninstall --remove-data` sterge si baza de date si fisierele aplicatiei (`assets`, conturile private, jurnalele; alte fisiere din folderul de date nu se ating). La consola trebuie tastat cuvantul `STERGE`; cu `--yes` se sare peste intrebare. **Pachet de restaurare:** `--backup-to <folder>` face inainte de stergere copia de siguranta a bazei de date cu fisierele aplicatiei (acelasi pachet ca „Copie de siguranta” din aplicatie) si o salveaza in folderul ales, in afara instalarii; daca pachetul nu se poate face sau copia nu este completa, **nu se sterge nimic**. La consola, fara `--backup-to`, installerul intreaba folderul (Enter = fara pachet); in fereastra este caseta „Creeaza un pachet de restaurare inainte de stergere”. Pentru restaurare: instaleaza din nou BlazorStoc, copiaza pachetul in folderul de copii de siguranta al noii instalari (`<folder de date>\assets\database-backups`) si il restaurezi din pagina de backup a aplicatiei. Cheile de criptare ale aplicatiei nu sunt in pachet.

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

Coduri de iesire: 0 reusit; 2 pachet invalid; 3 semnatar neacceptat; 4 refuzat (nimic schimbat); 5 versiunea noua nu a pornit, s-a revenit la cea veche; 6 esuat (aplicatia poate fi oprita: `--rollback` sau `--repair`); 10 fara drepturi de administrator; 11 fisierul nu contine pachet; 12 continut deteriorat (copiaza din nou fisierul); 13 nu e gasita o instalare (`--install-root`, sau `--init` pe un server nou); 14 lipseste dotnet / runtime-ul .NET 9 (si nu este in fisier); 15 anulat; 16 argumente gresite; 17 fara cont de administrator pentru aplicatie (`--admin-user` / `--no-admin`); 18 o dependenta nu s-a putut instala sau nu este semnata de Microsoft.

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
