# Notificari si Setari (/notificari, /setari)

Fluxul: Setari (administrator: ANAF, notificari, harta, Facturi) -> sabloane de notificari -> /notificari arata alertele active, rezolvate si ordonate dupa urgenta; meniul are un contor.

## Modificari de testat

### 10.11.2026 - Setari: Baza de date (fila Spatiu, sectiuni restranse), Preluare ANAF, detaliul propunerilor si erorilor in popup
**Ce s-a adaugat:** (1) Setari -> Baza de date: fila noua "Spatiu" (cat ocupa aplicatia pe disc, defalcat pe baza de date, imagini, fisiere adaugate, backup-uri, fisierele aplicatiei, altele; spatiul liber al partitiei; "Recalculeaza"); sectiunile din Stare si Conexiune sunt restranse implicit si lipite fara goluri; (2) Setari -> Preluare date ANAF: cele 5 sectiuni sunt restranse implicit; (3) Setari -> Dezvoltare -> Erori / Propuneri: detaliul se deschide intr-un popup (vizualizare cu butoanele Editeaza si Exporta; editare cu Salveaza / Renunta), iar pe fiecare rand sunt iconitele vizualizare, editare, export (ZIP) si stergere; (4) tabelele de erori si propuneri se actualizeaza singure cand se adauga, se schimba sau se sterge o intrare.
**Ce face acum:** totalul "Ocupat de aplicatie" din fila Spatiu este suma categoriilor si coincide cu cifra de pe partitie; iconita de editare deschide popup-ul direct in editare, iconita de export descarca ZIP-ul randului fara sa deschida nimic; o editare cu modificari nesalvate cere confirmare la inchidere.
Pasi: Setari -> Baza de date -> Spatiu -> "Recalculeaza" si compara totalul cu cifra de pe partitie; Stare si Conexiune: sectiunile sunt inchise, lipite una de alta; Setari -> Dezvoltare -> Propuneri: ochi (popup vizualizare), creion (popup editare, schimba starea si salveaza), export (se descarca PROP-xxxx.zip), la fel pe Erori; deschide un tabel si adauga din meniu o propunere/o eroare noua: tabelul se actualizeaza singur in cateva secunde.

### 10.11.2026 - Ferestre (popup): mutare, redimensionare, marcarea zonelor pe imagini
**Ce s-a adaugat:** orice fereastra se muta tragand de antet si se redimensioneaza din coltul dreapta-jos (dublu-clic pe antet = centrare, pe mÃ¢ner = dimensiunea implicita; oprite sub 720 px latime); distanta dintre marginea de sus si butoanele barei este de 3 px; in formularele "Raporteaza o eroare" si "Propune o modificare" un clic pe o imagine o deschide intr-o fereastra de marcare plutitoare: tragi dreptunghiuri numerotate 1..N (cel mult 10), fiecare se poate doar sterge (butonul x din afara coltului dreapta-sus), numerele se refac singure, imaginea trimisa are zonele desenate pe ea.
**Ce face acum:** descrierea din popup ramane accesibila cat timp marchezi imaginea; la trimitere se salveaza imaginea marcata (JPEG).
Pasi: deschide "Propune o modificare" -> clic pe imaginea paginii -> deseneaza 3 zone, sterge-o pe cea din mijloc (cea de-a treia devine 2) -> inchide fereastra de marcare, completeaza titlul si descrierea, trimite -> in "Propunerile mele" deschide propunerea si verifica zonele pe imagine; trage popup-ul de antet si redimensioneaza-l din colt.

### 05.11.2026 - Setari -> Preluare Elcomtur: preluare automata a backup-ului Elcomtur
**Ce s-a adaugat:** tab nou "Preluare Elcomtur" in Setari (langa "Preluare ActivityApp", doar administrator): comutator On/Off "Preluare automata", directorul cu backup-urile Elcomtur (cale de retea \\server\partajare\folder cu cont si parola, sau director local), masca numelui de fisier (ex. *elcomtur*), firma de preluat (aleasa din firmele backup-ului, cu numele: "firma1 - Electric Standard Prest SRL"), pragul de alerta in zile, butoanele Salveaza, Testeaza conexiunea, Citeste firmele din backup, Verifica acum si Preia ultimul backup acum. In fiecare zi la 04:00 se cauta in director cel mai recent fisier care corespunde mastii; daca e mai nou decat ultima preluare se copiaza pe server, se verifica integritatea tuturor fisierelor din arhiva si se preiau balantele. Pe server raman ultimele 2 backup-uri preluate. Operatiile sunt jurnalizate (Modificare setari preluare automata Elcomtur, Testare director backup Elcomtur, Verificare backup Elcomtur, Preluare automata backup Elcomtur, Esec preluare automata Elcomtur) si au notificari: "Elcomtur: backup preluat" (ramane 3 zile), "Elcomtur: preluare esuata" (cu cauza, se inchide la prima verificare reusita) si "Elcomtur: backup lipsa" (cand de pragul de zile nu a aparut un backup nou; se inchide la urmatoarea preluare, si cea manuala).
**Ce face acum:** o singura incercare pe zi (la 04:00 sau la prima pornire a serverului dupa aceea); "Verifica acum" face aceeasi verificare oricand (preia numai un fisier mai nou decat ultima preluare), iar "Preia ultimul backup acum" preia cel mai recent fisier chiar daca nu e mai nou. Un fisier modificat acum mai putin de 5 minute nu se preia (programul Elcomtur il scrie inca). Un fisier corupt nu se preia: balantele raman cele vechi si apare notificarea de esec.
Pasi: Setari -> Preluare Elcomtur -> completeaza directorul, contul si parola (pentru NAS), masca -> Testeaza conexiunea -> Citeste firmele din backup si alege firma -> pune Preluare automata pe Activ -> Salveaza -> Verifica acum: backup-ul se preia, apare "Elcomtur: backup preluat" in Notificari; Verifica acum a doua oara: "Nu exista un backup mai nou"; schimba masca in una fara potrivire -> Verifica acum: mesaj de eroare + notificarea de esec; revino la masca buna -> notificarea de esec se inchide singura. Pentru alerta de lipsa: pune pragul la 1 zi si asteapta sa treaca o zi fara backup nou (sau ruleaza pe o instanta de test cu data schimbata).

### 05.11.2026 - Backup NAS: fiecare backup in subdirector propriu
**Ce s-a adaugat:** copierea pe NAS pune fiecare pachet de backup intr-un subdirector al partajarii, numit ca pachetul fara extensie (ex. \\nas\backup\Pachet-X\Pachet-X.zip cu Pachet-X.zip.sha256 alaturi).
**Ce face acum:** la urmatorul backup (sau "Copiaza pachetele lipsa") apare un subdirector nou pe NAS pentru fiecare pachet. Pachetele copiate inainte de aceasta schimbare raman in radacina partajarii, nu se copiaza a doua oara si nu se muta; lista pachetelor de pe NAS si aducerea unui pachet de pe NAS le gasesc si pe ele, si pe cele din subdirectoare.
Pasi: Setari -> Backup NAS -> fa un backup manual -> pe partajare apare subdirectorul pachetului cu .zip si .sha256 in el; repeta copierea: nu se mai copiaza nimic; din lista de pe NAS adu un pachet vechi (din radacina) si unul nou (din subdirector).

### 24.10.2026 - Tema intunecata, iconite pe tile-urile Setarilor, bara verde pe meniuri si taburi

**Ce s-a adaugat:** comutator de tema (luna/soare) in bara de sus, langa „Deconectare"; iconite pe tile-urile din Setari; bara verde pe orice element de meniu, sub-meniu si tab cand e selectat sau la hover (la taburi pe marginea de sus).

**Ce face acum:** tema intunecata are o paleta calma pentru seara; alegerea se tine doar in browserul curent (nu pe server, nu pentru alti utilizatori). Implicit ramane tema luminoasa.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Apasa luna din bara de sus | Aplicatia trece pe tema intunecata, fara alb puternic; butonul devine soare | |
| 2 | Reincarca pagina si deschide alta pagina | Tema ramane intunecata; intr-un alt browser sau la alt utilizator ramane luminoasa | |
| 2b | Cu tema intunecata activa, navigheaza prin meniu (Produse, Oferte, Setari, Jurnal) si deschide ferestre (Ajutor, Adauga produs, Deconectare) | Tema ramane intunecata pe toate paginile si in toate ferestrele; nimic alb puternic | |
| 3 | Apasa soarele | Revine tema luminoasa | |
| 4 | Deschide Setari | Fiecare tile are o iconita | |
| 5 | Treci cu mausul peste meniu, un sub-meniu (ex. Produse -> categorie) si un tab | Bara verde in stanga elementului; la tab, sus | |

### 23.10.2026 - Changelog pentru pachetele de actualizare

**Ce s-a adaugat:** fiecare pachet de actualizare are un changelog (`Assets/Data/changelog.txt`, o sectiune pe versiune; `tools\release.ps1` refuza o versiune fara sectiune si foloseste sectiunea ca note ale pachetului). In Setari -> Actualizare -> Istoric, fiecare rand are iconita de ochi „Vezi changelog" care deschide o fereastra cu ce a adus versiunea.

**Ce face acum:** fereastra arata versiunea (sau versiunile prin care a trecut actualizarea, cea mai noua sus) cu data si cate o linie pe schimbare; iconita e dezactivata pentru o versiune fara changelog.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Actualizare, tabelul Istoric | Coloana „Ce contine update" cu iconita de ochi pe fiecare rand | |
| 2 | Apasa iconita de pe un rand reusit (ex. 0.5.0 → 0.5.1) | Fereastra „Changelog · versiunea 0.5.1" cu data si schimbarile; Esc sau „Inchide" o inchide | |
| 3 | Rand pentru o versiune care nu e in changelog (ex. instalarea initiala 0.4.9) | Iconita dezactivata, cu explicatia in titlu | |
| 4 | Dupa urmatorul release: incarca pachetul si citeste „Ce aduce" in rezultatul verificarii | Textul este changelog-ul versiunii pachetului | |

### 20.10.2026 - Backup si restaurare cu imaginile si fisierele

**Verificat de agent 20.10.2026** pe baza de test, cu workerul real de backup: backup cu fisiere de proba (inclusiv un nume cu diacritice si un folder adanc), restaurare care readuce baza si fisierele exact (fisierul sters revine, cel adaugat dispare, cel modificat se schimba la loc), pachetul de siguranta de dinainte contine fisierele inlocuite, un pachet cu un fisier stricat este refuzat fara sa schimbe nimic, un pachet vechi (fara fisiere) restaureaza doar baza si lasa imaginile, baza identica dar cu fisiere diferite se restaureaza. Ramane pentru tine: pasii de mai jos in aplicatie, cu imaginile tale.

**Ce s-a adaugat:** orice backup (la cerere, programat, la preluarea inventarului, pre-restaurare, pre-actualizare) contine acum, pe langa baza de date, imaginile si fisierele aplicatiei: pozele produselor, fisierele proiectelor, pozele de la interventii si arhiva fisierelor sterse. Nu intra cheile care protejeaza parolele salvate, pachetele de backup si starea de rulare. Restaurarea readuce baza de date SI fisierele din pachet.

**Ce face acum:**
- Pachetul este mai mare (creste cu imaginile) si backup-ul dureaza mai mult; verificarea de spatiu pe disc tine cont de imagini. Copierea pe NAS copiaza pachetul intreg.
- La restaurare, fisierele din pachet le inlocuiesc pe cele de acum: ce lipseste din pachet dispare, ce lipseste de pe server revine. Inainte se face pachetul „pre-restaurare”, care contine si fisierele de acum; restaurat el, ajungi la starea de dinainte.
- Un pachet vechi (facut inainte de aceasta schimbare) restaureaza numai baza de date; mesajul de dupa restaurare spune ca imaginile au ramas neschimbate.
- Un pachet cu imagini sau fisiere deteriorate este refuzat inainte sa se schimbe ceva.

**Pasi:**
1. Setari -> Backup si restaurare -> „Fa backup acum”. Dimensiunea pachetului din tabel trebuie sa creasca fata de unul vechi cu cat ocupa imaginile (vezi dimensiunea folderului `assets` din folderul de date).
2. Schimba ceva: sterge o poza de produs, adauga alta, modifica un produs.
3. Alege pachetul facut la pasul 1, „Restaureaza baza de date”, scrie cuvantul cerut si confirma. Mesajul de reusita spune cate imagini si fisiere au fost restaurate.
4. Verifica: poza stearsa a revenit, cea adaugata a disparut, produsul are valorile din pachet.
5. Optional: restaureaza pachetul „pre-restaurare” de la pasul 3: revii la starea de dupa pasul 2.
6. Pe un server nou: restaurarea unui pachet de pe NAS (doar pe NAS) aduce si imaginile; nu mai e nevoie de copierea separata a folderelor.

### 20.10.2026 - Installerul de actualizare (BlazorStoc-Actualizare-<versiune>.exe)

**Verificat de agent 20.10.2026** cu fisierul real facut de `release.ps1` (lansator real, dotnet, supervizor si sarcini programate reale, cont neelevat, instalare protejata, baza de test): `--init` (folderele, protectia, sarcina Updater-ului, registrul), prima instalare cu amprenta data prin `--trust` (inclusiv dupa o incercare intrerupta), actualizare cu aplicatia pornita (backup inainte de actualizare, Updater-ul din instalare inlocuit), `--rollback --restore-db` cu restaurarea reala a bazei dintr-un backup (o prima incercare, fara contul de restaurare, a lasat aplicatia oprita si a spus asta), `--repair` cand versiunea instalata era stricata si aplicatia oprita (a scos la iveala un defect, corectat: repararea aceleiasi versiuni era refuzata), `--status`, `--check`, pachet cu cheie necunoscuta (refuzat, nimic schimbat), repornire elevata de la o consola neelevata, sarcina programata inregistrata de `--init` care ruleaza Updater-ul inlocuit, iar Jurnalul a primit fiecare rezultat (Actualizare aplicatie (installer), Actualizare refuzata, Actualizare esuata, Revenire la versiunea anterioara). Ce ramane pentru tine: pasii de mai jos pe serverul tau real.

**Ce s-a adaugat:** `tools\release.ps1` produce, pe langa pachetul `.bsu`, fisierul `BlazorStoc-Actualizare-<versiune>.exe` (circa 114 MB; pachetul, Updater-ul acelei versiuni si un lansator) si tipareste amprenta cheii si SHA-256 al fisierului. Documentatia de operare completa este in `docs\OPERARE_ACTUALIZARE.md`.

**Ce face acum:** pe server, ca administrator, installerul face prima pregatire a instalarii (`--init`), actualizeaza (implicit), repara (`--repair`), revine la versiunea anterioara (`--rollback`, optional cu restaurarea bazei dintr-un backup de pe server) si arata starea (`--status`). Merge si cand aplicatia nu mai porneste. Dupa o actualizare reusita inlocuieste si Updater-ul de pe server cu cel din installer. Rezultatul apare in Jurnal (Actualizare aplicatie (installer), Actualizare reusita...).

**Pasi (server nou):**
1. Pe calculatorul de dezvoltare: `.\tools\release.ps1`. Noteaza amprenta si SHA-256 tiparite; copiaza `BlazorStoc-Actualizare-<versiune>.exe` pe server si compara `Get-FileHash <fisier>` cu SHA-256 tiparit.
2. Pe server, autentificat cu contul sub care va rula aplicatia, consola **ca administrator**: `.\BlazorStoc-Actualizare-<v>.exe --init --install-root D:\BlazorStoc --port 5087` (fara `--migration-account-file` daca contul de migrare e deja in folderul de date; vezi documentatia). Verifica mesajele: "Protected: ...", "Registered as the installation of this server", "Scheduled task BlazorStocUpdater-... registered".
3. Ruleaza comanda afisata la sfarsit pentru supervizor (`supervisor.ps1 -Action install ...`).
4. `.\BlazorStoc-Actualizare-<v>.exe --reason "prima instalare"`. Cand cere amprenta, tasteaza primele 8 caractere ale celei tiparite de `release.ps1` (sau da `--trust "<amprenta completa>"`). Asteapta "Done: BlazorStoc <v> is installed and answering."
5. Deschide aplicatia: versiunea din Setari -> Actualizare si in subsolul meniului este cea noua.

**Pasi (actualizare cu installerul):** fara `--init`, `.\BlazorStoc-Actualizare-<v>.exe --reason "..."` (Enter la intrebare). Aplicatia se opreste cateva minute si revine pe versiunea noua; in Setari -> Actualizare apare randul din istoric cu "installer", iar in Jurnal "Actualizare aplicatie (installer)" si "Actualizare reusita" la urmatoarea pornire.

**Pasi (aplicatie moarta):** `.\BlazorStoc-Actualizare-<v>.exe --status`, apoi `--repair` sau `--rollback`. Pentru restaurarea bazei dintr-un backup: copiaza pachetul de pe NAS in folderul de backup-uri al serverului (daca e doar pe NAS) si da `--rollback --restore-db "<nume pachet>"`.

### 21.10.2026 - Acces din retea si dezinstalare

**Verificat de agent 21.10.2026** (instalare reala intr-un folder de proba, consola ridicata): `/health` 200 si pe adresa din retea a calculatorului (192.168.100.20), aplicatia asculta pe 0.0.0.0, regula de firewall `BlazorStoc (port 5095)` (intrare, permite, doar subreteaua locala, toate profilurile); `--uninstall --remove-data --backup-to`: pachetul de restaurare a fost facut si salvat (dump.sql, assets.json, manifest.json), apoi s-au sters folderul instalarii, folderul de date, sarcina, regula de firewall, intrarea din registru, iar porturile s-au inchis; serviciul bazei (serviciu de proba `BlazorStocMariaDBTest`): oprit si sters, folderul pastrat cand se pastreaza datele (si refolosit de o instalare noua), sters cand se sterg datele. Fereastra de dezinstalare condusa automat (capturi, casete active/inactive). Dezinstalarea unui server real pe care l-ai folosit de la inceput (serviciul `BlazorStocMariaDB` real) nu a fost facuta de agent.

**Ce s-a adaugat:** aplicatia este accesibila implicit din reteaua locala (nu doar de pe server); installerul are `--uninstall` (si butonul „Dezinstaleaza…” in fereastra): doar aplicatia (baza de date si fisierele raman) sau stergere totala cu pierderea datelor, cu pachet de restaurare optional salvat inainte.

**Pasi:**
1. Dupa instalare, de pe alt calculator din retea deschide `http://<adresa serverului>:<port>/` (nu https) si autentifica-te.
2. Pe server, dublu clic pe installer -> „Dezinstaleaza…” -> „Sterge doar aplicatia”. Rezultat: aplicatia dispare (sarcina, regula de firewall, serviciul bazei), dar `<install-root>\mariadb` si folderul de date raman; rulat din nou installerul, instaleaza peste ele (conturile private se refac, datele raman).
3. „Dezinstaleaza…” -> „Stergere totala”, cu „Creeaza un pachet de restaurare” bifat si un folder in afara instalarii: dupa confirmari apare fisierul `.zip` in acel folder si tot ce tine de aplicatie dispare. Fara pachet, fereastra cere o a doua confirmare.
4. Instaleaza din nou, copiaza pachetul in `<folder de date>\assets\database-backups` si restaureaza-l din pagina de backup a aplicatiei: datele revin.

### 21.10.2026 - Installer cu fereastra (GUI)

**Verificat de agent 21.10.2026:** fereastra a fost condusa automat (UI Automation) pe fisierul real: toate paginile cu capturi, verificarea live a parolelor, butoane active/inactive, si o instalare COMPLETA reala printr-o fereastra elevata, intr-un folder de proba (porturi 5093/3417, fara serviciu Windows si fara sarcina Updater-ului): dependente verificate, baza MariaDB, supervizor, versiunea 0.5.0 (migrarile 1-43, /health 200), contul `boss.admin` facut, autentificare reala cu parola tastata in fereastra reusita si cu una gresita refuzata; apoi sarcina, procesele si folderul de proba au fost sterse. Proba a scos la iveala un defect corectat: supervizorul pornit de sarcina programata nu primea variabilele din `--env` (de exemplu `App__DataRoot`), deci aplicatia nu-si gasea fisierele de configurare; acum `init` le scrie in `scripts\environment.json`, citit de supervizor.

**Ce s-a adaugat:** dublu clic pe installer deschide o fereastra in romana (pagini: folder si setari avansate, cont de administrator cu verificarea live a parolelor, rezumat, progres cu jurnal). **Ce face acum:** acelasi lucru ca varianta din consola, fara a tasta comenzi.

**Pasi:**
1. Pe un server curat, dublu clic pe `BlazorStoc-Actualizare-<v>.exe`; accepta fereastra Windows de administrator.
2. Pagina 1: lasa folderul sau alege altul (gol); „Setari avansate” arata porturile 5087 si 3307 si optiunea bazei de date existente. „Continua”.
3. Pagina 2: scrie utilizatorul, parola, apoi parola repetata gresit (apare `Parolele nu coincid inca`, „Continua” inactiv), apoi corect (apare `Parolele coincid`, „Continua” activ).
4. Pagina 3: citeste rezumatul, „Instaleaza”. Urmareste jurnalul; la final „Instalarea s-a incheiat”, apoi „Deschide aplicatia” si autentifica-te cu utilizatorul si parola alese.
5. Pe un server care are instalarea: dublu clic propune „Actualizeaza” cu versiunea veche si cea noua.

### 21.10.2026 - Installerul aduce si instaleaza dependentele (.NET, Visual C++)

**Ce s-a adaugat:** installerul (circa 177 MB) contine installerele Microsoft pentru runtime-ul .NET 9, ASP.NET Core 9 si Visual C++ x64. **Ce face acum:** pe un server unde lipsesc, le instaleaza singur, silentios si fara repornire, dupa ce verifica semnatura Microsoft; ce exista deja nu se reinstaleaza.

**Pasi:** pe un server curat (fara .NET) ruleaza installerul din consola administrator. Rezultat asteptat: mesaje `Installing dotnet-runtime-9.0.x-win-x64.exe ...`, `aspnetcore-runtime ...`, `vc_redist.x64.exe ...`, `The prerequisites are installed.`, apoi instalarea merge mai departe; `dotnet --list-runtimes` arata Microsoft.NETCore.App 9 si Microsoft.AspNetCore.App 9. Pe un server care le are deja nu apare niciun mesaj de instalare.

### 21.10.2026 - Installerul instaleaza tot si alege porturile libere

**Verificat de agent 21.10.2026:** testele automate ale installerului (porturi ocupate mutate pe urmatoarele libere, porturi pastrate la a doua rulare, `--init` cu prima versiune intr-o singura comanda, oprire cand baza nu se poate face, `--no-database`) si o rulare reala a `init` pe acest calculator (5087 si 5088 erau ocupate: a ales 5089; MariaDB real din `C:\Dev` ca proces ascuns pe un port liber, 4 conturi, 28 de tabele, migrarile 1-43 aplicate cu contul de migrare, a doua rulare a pastrat porturile si conturile). Serviciul Windows verificat tot de agent 21.10.2026, cu un serviciu de proba `BlazorStocMariaDBTest` (consola ridicata prin UAC, port 3417, sters la final; serviciul `BlazorStocMariaDB` al calculatorului nu a fost atins): creat de `provision-database.ps1`, pornit, tip Automatic, cont `NT SERVICE\BlazorStocMariaDBTest`, repornire dupa esec configurata; din consola NEridicata contul care a rulat scriptul a oprit si a pornit serviciul, dar nu l-a putut sterge; fisierele private au drept de citire doar pentru acel cont, SYSTEM si Administrators, cu portul ales scris in ele. Ramas: pornirea dupa repornirea PC-ului.md`.

**Ce s-a adaugat:** `--init` nu mai pregateste doar folderele. Alege porturile libere pentru aplicatie (implicit 5087) si pentru baza de date (implicit 3307) si le scrie in `updater\settings.json`; face serverul MariaDB (descarcat cu SHA-256 verificat, sau `--maria-zip` / `--maria-bin`) ca serviciu Windows `BlazorStocMariaDB`, conturile cu parole generate, schema, fisierele private; inregistreaza supervizorul si instaleaza prima versiune. Optiuni noi: `--db-port`, `--maria-bin`, `--maria-zip`, `--no-database`, `--no-service`, `--no-supervisor`, `--no-install`. Pe un server fara instalare nu mai este nevoie de `--init`: o rulare simpla (sau dublu clic) face tot, in `C:\BlazorStoc`.

**Ce face acum:** pe un server nou, o singura comanda ca administrator instaleaza tot; porturile ocupate sunt inlocuite cu urmatoarele libere si afisate in consola.

**Pasi (server nou):**
1. `.\BlazorStoc-Actualizare-<v>.exe` intr-o consola administrator (nu tastezi amprenta: la prima instalare cheia pachetului din fisier devine de incredere si installerul spune "First installation: the signer ... is trusted from now on"), pe un server fara nicio instalare (fara `--init`: installerul isi da seama singur si instaleaza in `C:\BlazorStoc`; cu `--install-root D:\BlazorStoc` pentru alt folder gol). Rezultat asteptat: mesaje "Application port: ..." si "Database port: ...", "[database] ready: MariaDB answers on 127.0.0.1:<port>", "Done: BlazorStoc <v> is installed and answering".
2. Daca 5087 sau 3307 sunt ocupate (de exemplu porneste inainte `python -m http.server 5087`), mesajul spune portul liber ales; acelasi port este in `updater\settings.json`, in `Database.Port` din `application-connection.private.json` si in sarcina `BlazorStocSupervisor-<port>`.
3. `Get-Service BlazorStocMariaDB` este Running, tip Automatic; dupa repornirea calculatorului baza si aplicatia pornesc singure.
4. La inceput installerul cere contul de administrator al aplicatiei: **User name**, **Password** (apar `*`), **Repeat the password**. Tasteaza parola repetata gresit si observa ca linia arata `[the passwords do not match yet]`, apoi corecteaz-o (Backspace) si apare `[the passwords match]`; cu parole diferite la Enter te intreaba din nou. Dupa instalarea versiunii vezi `Administrator account "<utilizator>" made in the users of the application`.
5. Deschide aplicatia si autentifica-te cu acel utilizator si acea parola. Trebuie sa ai toate meniurile (Setari, Utilizatori etc.); in Utilizatori contul apare cu rolul Administrator, iar in Jurnal este "Creare cont administrator la instalare" (actor: sistem). Contul `admin` din configurare (parola generata, neafisata; comanda care o citeste o tipareste scriptul bazei) ramane doar ultima solutie.
6. Rulat din nou pe acelasi server cu acelasi nume de utilizator, installerul spune ca utilizatorul exista deja si ca parola tastata nu s-a aplicat.

### 20.10.2026 - Setari -> Actualizare: actualizarea aplicatiei dintr-un pachet

**Verificat de agent 20.10.2026** pe un server de proba (supervizor si sarcini programate reale, baza de test): incarcare, verificare, anulare, actualizare, refuz, revenire automata, pachet cu cheie necunoscuta, sarcina lipsa, installer manual. Ce ramane pentru tine: pasii de mai jos pe serverul tau real.

**Ce s-a adaugat:** un tab nou, „Actualizare", in Setari (doar administrator). Aici alegi un pachet de actualizare (fisier .bsu, facut cu `tools\release.ps1` pe calculatorul de dezvoltare), Updater-ul il verifica, confirmi cu un motiv, utilizatorii sunt anuntati si aplicatia se actualizeaza singura. Un banner nou apare deasupra paginilor. In Jurnal apar actiuni noi (Actualizare aplicatie, Actualizare reusita, Actualizare esuata, Actualizare refuzata, Anulare actualizare aplicatie, Revenire la versiunea anterioara, Actualizare aplicatie (installer), Confirmare esec actualizare).

**Ce face acum:** pachetul se incarca pe server, se verifica semnatura, cine a semnat (o cheie necunoscuta serverului nu se accepta din aplicatie), versiunea, platforma, locul pe disc si schema bazei. Dupa confirmare toti utilizatorii vad un anunt cu numaratoare (60 s), apoi aplicatia se opreste, se face backup al bazei, se trece pe versiunea noua, se aplica migrarile si se porneste. Daca versiunea noua nu raspunde in doua minute, se revine singur la cea veche (si la baza de dinainte, daca migrarile pornisera) si administratorul vede o banda rosie pana confirma.

**Pasi (pe serverul real, dupa ce ai instalat prin Updater: `BlazorStoc.Updater init` rulat ca administrator, sub contul care ruleaza aplicatia):**
1. Pe calculatorul de dezvoltare: `tools\release.ps1 -NotesFile note.txt` (note.txt = ce aduce versiunea). Copiaza fisierul .bsu pe calculatorul de pe care deschizi aplicatia.
2. Autentifica-te ca administrator, Setari -> Actualizare: sunt aratate versiunea, versiunea anterioara si istoricul. Daca aplicatia nu ruleaza dintr-o instalare facuta de Updater, tabul spune asta si nu ofera incarcare.
3. Alege fisierul .bsu: apare tabelul cu versiunea veche -> noua, semnatarul, schema bazei, notele si „Poate fi instalat". Un pachet mai vechi, stricat sau semnat cu o cheie necunoscuta apare ca „Nu poate fi instalat", cu motivul.
4. Scrie un motiv si apasa „Aplica actualizarea". Verifica: apare numaratoarea si, pe orice alta pagina (si in alta sesiune), bannerul „Aplicatia se actualizeaza la versiunea X". Cat numara poti apasa „Anuleaza actualizarea": nu se opreste nimic.
5. Lasa sa treaca: aplicatia se opreste cateva minute, apoi pagina se reincarca singura. Verifica versiunea noua in tab si in subsolul meniului, randul nou din istoric si in Jurnal „Actualizare aplicatie" (cu motivul) si „Actualizare reusita".
6. Optional: incarca un pachet a carui versiune noua nu porneste: dupa doua minute aplicatia revine la versiunea veche, apare banda rosie, „Am luat la cunostinta" o scoate si in Jurnal apar „Actualizare esuata" si „Confirmare esec actualizare".
7. Un utilizator care nu e administrator nu vede tabul Actualizare si nu vede bannerul de eroare, dar vede bannerul de anunt cat numara.

### 20.10.2026 - Setari -> Backup: pachetele facute inainte de o actualizare a aplicatiei

**Ce s-a adaugat:** un tip nou de pachet, „Pre-actualizare", pe care il face instrumentul de actualizare inainte sa schimbe versiunea aplicatiei sau schema bazei. Cardul de rezumat „Pre-restaurare" se numeste acum „Pre-restaurare si pre-actualizare" si le numara pe amandoua.

**Ce face acum:** un pachet pre-actualizare apare in tabel cu insigna „Pre-actualizare", nu se poate sterge din aplicatie (ca cele pre-restaurare) si poate fi ales la „Restaureaza baza de date". Pana nu se face o actualizare reala nu exista niciun astfel de pachet; verificarea de acum este doar ca pagina arata corect.

**Pasi:**
1. Deschide Setari -> Backup si restaurare: al cincilea card de rezumat se numeste „Pre-restaurare si pre-actualizare".
2. Cand va exista un pachet pre-actualizare (dupa o actualizare facuta cu instrumentul): verifica insigna din tabel, ca nu are iconita de stergere si ca numele incepe cu „Copie siguranta baza de date inainte actualizare aplicatie".

### 20.10.2026 - Versiunea aplicatiei in Setari -> Module si in subsolul meniului

**Ce s-a adaugat:** o linie deasupra tabelului din Setari -> Module cu versiunea aplicatiei (`0.5.0`) si versiunea schemei bazei de date (numarul ultimei migrari, `43`); subsolul meniului din stanga arata aceeasi versiune in loc de „0.3". `/health` raspunde acum si cu versiunea.

**Ce face acum:** versiunea vine din proiect (nu mai e scrisa de mana); schema se citeste din registrul `schema_migrations`, in care sunt inscrise toate migrarile bazei.

**Pasi:**
1. Autentifica-te ca administrator, deschide Setari -> Module: deasupra tabelului apare „Versiunea aplicatiei 0.5.0 · versiunea schemei bazei de date 43".
2. Verifica ca subsolul meniului spune „Versiunea 0.5.0".
3. Deschide `/health` in browser: raspunsul contine `"version":"0.5.0"`.

### 20.10.2026 - Setari -> Baza de date (server, port server, port aplicatie, conturi, sesiuni)

**Verificat de agent 20.10.2026** (instanta de test pe baza demo si serviciul real `BlazorStocMariaDB`: repornire server, port 3307 -> 3308 -> 3307, port ocupat de alt program, port aplicatie 5089 -> 5090 -> 5089 sub supervizor; vezi IMPLEMENTED.md).

**Ce s-a adaugat:** Setari -> Baza de date -> Conexiune are patru carduri: „Serverul bazei de date" (serviciul Windows, portul pe care asculta, Reporneste / Opreste / Porneste / Schimba portul serverului), „Conexiunea la baza de date" (portul din ea este doar cel la care se conecteaza aplicatia), „Aplicatia" (port si interfata, „Salveaza si reporneste aplicatia") si „Conturile bazei de date". Starea are iconita „Vezi sesiunile deschise" cu fereastra de detalii si inchidere de catre administrator.

**Ce face acum:** portul serverului se schimba de pe pagina (copie `my.ini`, repornire pe portul nou, revenire daca nu porneste), iar aplicatia se conecteaza singura la portul nou; portul aplicatiei se schimba cu repornire reala si pagina se deschide singura pe adresa noua. Un port tinut de alt program este numit, cu un port liber propus. Toate operatiile cer parola contului tau de administrator in fereastra lor.

Pasi:

1. Instaleaza serviciul bazei (o singura data, PowerShell normal): `.\tools\install-mariadb-service.ps1` si accepta fereastra UAC. Deschide Setari -> Baza de date -> Conexiune: cardul „Serverul bazei de date" arata serviciul „pornit" si portul 3307.
2. „Reporneste serverul": scrii parola ta de administrator; apare „Serverul bazei de date a fost repornit si raspunde." Jurnal: „Repornire server baza de date".
3. „Opreste serverul": butonul ferestrei ramane inchis pana scrii OPRESTE BAZA. Dupa oprire pagina arata „oprit"; „Porneste serverul" il readuce. Ramai conectat cat e oprit (conturile din baza nu se pot autentifica fara baza).
4. „Schimba portul serverului": alege portul propus, parola; serverul se reporneste pe el, iar „Port la care se conecteaza aplicatia" se schimba singur. Ruleaza `.\tools\sync-dev-port.ps1 -Port N` ca sa actualizezi si fisierele de dezvoltare. Revino la 3307 la fel.
5. Port ocupat: opreste serverul, ocupa portul lui cu alt program; cardul numeste programul (nume, PID), propune un port liber si „Porneste serverul" e inchis; „Schimba portul serverului" il porneste direct pe portul nou.
6. Cardul „Aplicatia": scrie un port liber (de exemplu 5090), „Salveaza si reporneste aplicatia", parola; pagina se deschide singura pe noua adresa (supervizorul previewului trebuie sa ruleze). Gol = revine la portul implicit.
7. Pe un server aflat pe alt calculator cardul serverului nu are butoane si spune ce se face acolo.

### 19.10.2026 - Oprire si pornire manuala a unui modul (Setari -> Module)

**Verificat de agent 19.10.2026** (instanta de test pe baza de test: oprire cu motiv, repornirea aplicatiei, pornire, modul oprit din configurare, pauza programarii backup; vezi IMPLEMENTED.md).

**Ce s-a adaugat:** in Setari -> Module, coloana "Pornit" cu un comutator pe fiecare modul. Oprirea cere un motiv (obligatoriu) si arata ce nu va mai merge; pornirea se face dintr-un clic. Starea se pastreaza in baza (tabelul `module_state`, migrarea 42), deci un modul oprit ramane oprit si dupa repornirea aplicatiei, pana il pornesti tu. Permisiune noua `setari-module.toggle`, rezervata administratorului. Jurnalul de activitate nu poate fi oprit (comutatorul lui este dezactivat), iar un modul care nu a pornit sau e oprit din configurare are comutatorul dezactivat (cere repornirea aplicatiei).

**Ce face acum:** modulul oprit manual iese imediat din meniu, pe pagina principala placa lui ramane la locul ei, gri, fara link si cu textul "Oprit temporar de administrator", iar paginile lui spun "Modul oprit temporar". Operatiile deja pornite se termina; apelurile noi sunt refuzate. Un modul oprit manual nu mai este numarat de protectia la erori si nu se mai reia singur dupa 30 de secunde. Programarea backup-ului asteapta cat modulul Backup este oprit. Cand il pornesti, modulul incepe de la zero (fara erori numarate). Administratorul vede in tabel cine l-a oprit, cand si de ce, iar bannerul de deasupra paginilor il include.

Pasi:

1. Intra ca administrator in Setari -> Module. Comutatorul Jurnalului este dezactivat (titlu: "nu poate fi oprit").
2. Opreste un modul (de exemplu Analize de risc): se deschide confirmarea; apasa "Opreste modulul" fara motiv: apare mesajul de camp obligatoriu si nimic nu se opreste. Apasa "Renunta": comutatorul revine pe On.
3. Opreste-l din nou cu motiv: randul arata "Oprit", "oprit de administrator (utilizatorul tau): motivul", iar sus apare bannerul. Intrarea din meniu dispare.
4. Deschide pagina principala: placa modulului este gri, fara link, cu "Oprit temporar de administrator". Deschide direct adresa paginii modulului: apare "Modul oprit temporar".
4b. (07.11.2026) Pe pagina modulului oprit, bannerul spune acum cine l-a oprit: "Aceasta parte a aplicatiei a fost oprita de <utilizator> (Administrator)" (numele contului si rolul lui). Motivul opririi nu se afiseaza utilizatorilor obisnuiti. Bannerul rosu de sus (vazut de administrator) arata "<modul> (oprit de administrator: <utilizator> (Administrator))", iar randul din Setari -> Module are acelasi text. Un modul oprit inainte de aceasta versiune arata doar numele contului, fara rol.
   **Ce s-a adaugat:** coloana module_state.stopped_role (migrarea 74) si textul cu utilizatorul si rolul in bannerul "Modul oprit temporar".
   **Ce face acum:** bannerul nu mai este generic, numeste utilizatorul si rolul celui care a oprit modulul.
5. In Jurnal, filtreaza dupa actiunea "Oprire manuala modul": intrarea este pe numele tau, cu starea veche/noua si motivul.
6. Opreste aplicatia si porneste-o din nou (`tools\supervisor.ps1`): modulul ramane oprit. Porneste-l din comutator: intrarea "Pornire manuala modul" arata cine l-a oprit, de cand, cat a durat oprirea si motivul.
7. Cu un utilizator obisnuit: nu vede fila Module, nici bannerul; placa gri o vede doar daca are permisiunea modulului.

### 16.10.2026 - Backup, restaurare si copiere NAS intr-un proces separat

**Ce s-a adaugat:** backup-ul (la cerere, programat, la preluarea de inventar), restaurarea si copierea pe NAS ruleaza intr-un proces propriu al aplicatiei; modulul "Copii de siguranta si restaurare" apare in Setari -> Module.

**Ce face acum:** pentru tine nimic nu se schimba in fluxuri (aceleasi butoane, mesaje si etape). Diferenta: daca procesul de backup se blocheaza sau pica, aplicatia ramane in picioare, lacatul de "backup in curs" se elibereaza imediat, iar dupa 3 caderi la rand modulul iese 30 s din functiune (mesaj "Copiile de siguranta si restaurarea sunt indisponibile momentan"; tabelul de pachete din Setari -> Backup este inlocuit de nota "modul indisponibil", setarile de program raman).

Pasi:

1. Setari -> Backup -> "Fa backup acum": mesajul cu numele pachetului apare dupa ~15 s; pachetul este in tabel; in Jurnal "Generare CopieSiguranta" cu utilizatorul tau.
2. Cat dureaza backup-ul, intr-un alt browser (alta sesiune) navigheaza intr-o pagina: esti dus la pagina de asteptare; dupa backup totul merge. Modulele (Notificari etc.) nu apar oprite dupa backup.
3. Setari -> Backup -> Backup NAS (daca NAS-ul este configurat): "Testeaza conexiunea", "Fa backup acum" cu copierea Activ: pachetul apare si pe partajare, fara fisiere .partial.
4. Restaurare dintr-un pachet vechi (pe o baza de proba): etapele apar in fereastra, apare o copie "inainte de restaurare", baza se schimba, Jurnalul are "Restaurare".
5. Backup programat: bifeaza ziua, pune ora peste 2 minute, Salveaza; la ora aleasa apare un pachet "Programat" facut de utilizatorul "sistem".
6. Setari -> Module: "Copii de siguranta si restaurare" este sanatos.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Backup -> "Fa backup acum" | Pachet nou, jurnal cu actiunea si utilizatorul tau | |
| 2 | Alta sesiune, in timpul backup-ului | Pagina de asteptare, apoi normal; niciun modul oprit | |
| 3 | Restaurare dintr-un pachet (baza de proba) | Etape, copie inainte de restaurare, jurnal "Restaurare" | |
| 4 | Backup programat peste 2 minute | Pachet "Programat" de la "sistem" | |

### 16.10.2026 - Notificarile ca modul (oprire fara efect asupra restului aplicatiei)

**Ce s-a adaugat:** motorul de notificari (pagina Notificari, evaluarea sabloanelor, preluare / amanare / rezolvare, curatarea celor vechi si filele Sabloane si Setari din Setari -> Notificari) este un modul al aplicatiei. Daca se opreste (din configurare sau dupa erori repetate), restul aplicatiei merge.

**Ce face acum:** cu modulul oprit, /notificari arata "Modul indisponibil", intrarea Notificari din meniu (cu triunghiul de avertizare) si cardul de pe pagina principala dispar, iar Setari -> Notificari arata nota de modul oprit. Paginile care coloreaza scadentele (fisa beneficiarului, Mentenanta, harta, Analizele de risc) folosesc zilele implicite in loc de pragul din sablon, fara erori. Un modul oprit dupa erori repetate se reincearca singur dupa 30 de secunde, la prima pagina deschisa, sau imediat cu "Reincearca" din Setari -> Module. Notificarile deja create raman in lista cand modulul revine.

Pasi:

1. Cu modulul pornit (normal): Notificari, Setari -> Notificari (sabloane si setari) si triunghiul din meniu merg ca pana acum.
2. Porneste aplicatia cu `Modules__notificari__Enabled=false`: /notificari arata "Modul indisponibil", meniul si pagina principala nu mai au Notificari, Setari -> Notificari arata nota, fisa unui beneficiar si Mentenanta se deschid normal, Setari -> Module arata Notificari "Oprit (oprit din configurare)".
3. Tot cu modulul oprit: Jurnalul are "Oprire modul" pentru Notificari, iar notificarile existente nu s-au schimbat in baza de date; la repornirea cu modulul pornit apar neschimbate.

### 17.10.2026 - NAS prin worker si pagina de asteptare la restaurare

**Verificat de agent 19.10.2026** (NAS prin worker, backup cu pagina de asteptare si restaurare completa pe 5087, cu acordul utilizatorului; vezi `docs/TESTE_RAMASE.md`).

**Ce s-a adaugat:** restaurarea completa prin worker a fost rulata cu succes pe baza locala (comutarea schemelor, copia dinaintea restaurarii, schema `_old`); pagina de asteptare nu se desena in timpul unui backup/restaurare (500 cu corp gol, corectat: redirectul vine inaintea autorizarii, iar permisiunile folosesc ultimele valori cunoscute cat tine lacatul). Ramase de verificat: copierea pe un NAS real prin worker si pagina de asteptare in browser.

**Ce face acum:** in timpul unui backup sau al unei restaurari, o a doua sesiune care deschide o pagina vede „Backup sau restaurare in curs / Te rugam sa astepti" si revine singura la pagina ceruta; backup-ul cu NAS Activ copiaza pachetul pe partajare prin worker.

Pregatire: aplicatia de pe 5087 ruleaza sub supervizor (directorul real `%LOCALAPPDATA%`), unde parola NAS nu este salvata; o introduce utilizatorul, nu agentul.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Backup -> Backup NAS: cale `\\192.168.100.50\BackupStocDepozit`, cont `EspStoc`, parola, Copiere „Activ", Salveaza | „Configurarea a fost salvata"; dupa reincarcare campul parola cere din nou parola doar la schimbare | |
| 2 | „Testeaza conexiunea" | „Conexiunea functioneaza" (fara eroare „nu este salvata nici una") | |
| 3 | Setari -> Backup si restaurare -> „Fa backup acum" | Mesaj cu numele pachetului si „Copierea pe NAS s-a incercat automat"; dupa „Reimprospateaza NAS" randul e „Server + NAS" | |
| 4 | Pe partajare `\\192.168.100.50\BackupStocDepozit` | Pachetul `.zip` si `.sha256` exista, fara fisiere `.partial`; Jurnal: „Copiere backup pe NAS" fara parola in detalii | |
| 5 | In timpul pasului 3 (15-25 s), intr-o a doua fila deschide /produse | Pagina „Backup sau restaurare in curs" cu „Te rugam sa astepti"; dupa terminare revine singura la /produse | |
| 6 | Repeta pasul 5 in timpul unei restaurari dintr-un pachet proaspat (Setari -> Backup, selectezi, „Restaureaza baza de date", scrii confirma) | Aceeasi pagina de asteptare, apoi revenire; Jurnal „Restaurare"; nicio eroare 500 | |
| 7 | Opreste NAS-ul sau strica parola si fa un backup | Backup-ul local reuseste, mesaj in cuvinte despre NAS; apare notificarea „Copie pe NAS lipsa" | |

### 15.10.2026 - Setari -> Module (stare module, doar administrator)

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** fila "Module" in Setari (doar pentru administrator): lista modulelor cu versiune, stare (Functioneaza / Cu erori / Oprit), de cand, cauza (erori repetate cu tipul erorii, nu a pornit, oprit din configurare) si numarul de erori la rand; iconita "Reincearca" pentru un modul oprit dupa erori repetate (scrisa in Jurnal). Deasupra tuturor paginilor, administratorul vede un banner cu modulele oprite, cu link spre aceasta fila.

**Ce face acum:** un modul oprit se vede imediat de catre administrator, nu doar prin `/health/modules` sau in log. "Reincearca" lasa urmatoarea utilizare sa treaca fara a astepta cele 30 de secunde. Un modul care nu a pornit sau e oprit din configurare cere repornirea aplicatiei si nu are iconita.

Pasi:

1. Intra ca administrator in Setari -> Module: toate modulele apar "Functioneaza".
2. Porneste aplicatia cu `Modules__analize-risc__Enabled=false`: fila arata modulul "Oprit (oprit din configurare)" si apare bannerul de deasupra paginilor; un utilizator obisnuit nu vede nici fila, nici bannerul.
3. La un modul oprit dupa erori repetate, apasa iconita Reincearca: apare mesajul "va fi reincercat la urmatoarea utilizare" si, in Jurnal, "Reincercare modul" pe numele tau.
4. Cu bannerul afisat, trece pe alta fila din Setari (de exemplu Notificari) si apasa "Detalii" din banner: se deschide fila Module (si cand adresa ramasese aceeasi, ?tab=module).

### 08.10.2026 - Iconite pe randurile tabelelor si numar ca insigna pe taburi

**Ce s-a adaugat:** regula „Iconite pe randurile tabelelor" (CLAUDE.md): editarea si stergerea/scoaterea de pe un rand sunt butoane doar cu iconita (creion verde, cos de gunoi rosu), peste tot (sabloane notificari, hartă, facturi, oferte, furnizori, beneficiari, vehicule, utilizatori, miscari, tipuri de sisteme, componente proiect, pachete backup). Taburile de categorii din sabloanele de notificari arata numarul in paranteze, ex. „Stoc (4)".

**Ce face acum:** fiecare iconita are explicatie la trecerea mouse-ului si nume pentru cititorul de ecran; iconita dezactivata e estompata. Actiunile cu text specific (Stornează, Istoric, Bon, Rezervă, Mută, Reactivează) raman cu text.

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 1 | Setari -> Notificari -> Sabloane notificari | Taburile arata numarul de sabloane ca insigna mica langa nume („Autovehicule 3", „Stoc 4”), la fel ca numarul din titlul unui card; la fel pe taburile Notificari (Active/Rezolvate) si Mentenanta (Scadente/Registru) | |
| 2 | Un tabel cu randuri: Furnizori, Beneficiari, Utilizatori, Vehicule, Facturi, Sabloane oferte | La fiecare rand: creion si cos, nu cuvinte sau ✎/× | |
| 3 | Trece mouse-ul peste o iconita | Apare „Editeaza <obiect>" / „Sterge <obiect>" | |
| 4 | Un rand cu stergere interzisa (furnizor folosit, factura cu miscari) | Cosul este estompat, motivul apare la trecerea mouse-ului | |

### 08.10.2026 - Setari -> Backup (flux unificat) si Backup NAS redus la NAS

**Ce s-a adaugat:** tab-ul „Backup" (administrator) cu: backup la preluarea de inventar (Activ, obligatoriu), backup zilnic automat (ora), notificare backup lipsa dupa N zile, stergere automata a pachetelor vechi de pe server (prag in zile; ultimele 4 pachete se pastreaza oricand), modelul needitabil „Backup lipsa", butonul „Fa backup acum" si tabelul „Backupuri disponibile" (server si NAS, coloana „Locatie", „Reimprospateaza NAS", „Copiaza pe NAS" pe un rand, „Restaureaza baza de date"). Pagina „Restaureaza stoc" din meniul Inventar a fost scoasa. Tab-ul „Backup NAS" are acum numai: copiere, cale, cont, parola, „Testeaza conexiunea", „Copiaza pe NAS pachetele lipsa" si modelul „Copie pe NAS lipsa". Jurnal: „Modificare setari backup", „Stergere automata pachet backup vechi", „Aducere pachet backup de pe NAS".

**Ce face acum:** orice backup facut pe server se copiaza automat pe NAS cat timp copierea e Activ; la preluarea de inventar copierea se face in fundal (preluarea nu mai asteapta NAS-ul). Din aplicatie nu se sterge niciodata nimic de pe NAS. Notificarea „Backup lipsa" urmareste backup-ul local indiferent de NAS; „Copie pe NAS lipsa" numai cat NAS-ul e Activ. Un pachet doar pe NAS se restaureaza dupa ce este adus pe server (verificat cu suma de control) si ramane apoi si pe server.

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 1 | Setari -> Backup (tab principal cu sub-taburile: Backup si restaurare, Backup NAS, Verificare ora; se deschide pe primul) | Sectiuni: Generare backup, Notificare, Stergere pachete vechi, model notificare, apoi tabelul; „Backup la preluarea de inventar" este Activ si nu se poate opri | |
| 2 | Schimba ora, vechimea (ex. 5), pragul de stergere (ex. 14), Salveaza | „Setarile de backup au fost salvate"; in Jurnal „Modificare setari backup" cu valorile vechi si noi; valori in afara limitelor sunt refuzate | |
| 3 | Tabelul: „Fa backup acum" | Pachet nou „La cerere" in tabel; cu NAS Activ apare „Server + NAS" dupa reimprospatare | |
| 4 | Sterge de mana de pe NAS un pachet, „Reimprospateaza NAS" | Randul devine „Doar pe server", cu butonul „Copiaza pe NAS" | |
| 5 | Sterge de pe server un pachet de „Preluare inventar" | Se sterge numai de pe server; daca a fost pe NAS, randul ramane „Doar pe NAS" | |
| 6 | Selecteaza un rand „Doar pe NAS", „Restaureaza baza de date" | Pachetul e adus pe server, apoi restaurarea merge ca de obicei (cu copie de siguranta inainte) | |
| 7 | Opreste NAS-ul/strica parola, deschide tabelul | Tabelul arata pachetele de pe server si o nota cu cauza (fara eroare bruta) | |
| 8 | Prag de stergere mic (7 zile) cu pachete vechi pe server (si copii de siguranta dinaintea restaurarii), Salveaza | Apare lista pachetelor care se sterg (toate tipurile; cu NAS Activ numai cele cu copie pe NAS; ultimele 4 raman); „Renunta" nu salveaza nimic; „Salveaza si sterge" salveaza si sterge pe loc, tabelul se reimprospateaza; Jurnal „Stergere automata pachet backup vechi". Fara pachete de sters, salvarea nu cere confirmare. Ulterior stergerea ruleaza o data pe zi | |
| 7b | Setari -> Backup -> Backup si restaurare: sub „Backup programat automat” alege zilele (ex. doar Luni si Joi), Salveaza | Backup-ul programat se face numai in zilele pornite, la ora aleasa; notificarea „Backup lipsa” asteapta cel putin intervalul dintre doua backup-uri (ex. 4 zile + 1); fara nicio zi pornita salvarea e refuzata; Jurnal „Modificare setari backup” cu zilele vechi si noi | |
| 8b | Muta ceasul serverului cu 1 luna inainte (sau taie internetul serverului), apoi salveaza un prag de stergere nou | Nu se sterge nimic: nota „Ceasul serverului difera ... min de ora de pe internet” sau „Ora serverului nu a putut fi verificata”; stergerea automata reincearca din ora in ora; dupa corectarea ceasului ruleaza normal (toleranta 5 min) | |
| 8c | Setari -> Backup -> Verificare ora: 3 servere NTP precompletate; alege altele din lista (6 servere) sau „Altul (server propriu)”; „Verifica acum” | Pentru fiecare server apare ora raspunsa si diferenta fata de ceasul serverului, sau „nu raspunde (verifica firewall-ul: UDP 123)”; un URL (https://...), spatii sau peste 3 servere sunt refuzate la salvare; Jurnal „Modificare setari backup” cu serverele vechi si noi | |
| 8d | Cu ceasul serverului mutat cu peste 15 min, fa „Fa backup acum” | Pachetul are data de pe internet (in nume si in coloana Creat, cu mentiunea „ora de pe internet”); backup-ul zilnic se declanseaza dupa ora reala; apare notificarea „Ceas server decalat” (Notificari -> Sistem, sablon implicit) care dispare dupa corectarea ceasului | |
| 8e | Taie internetul serverului, fa „Fa backup acum” | Backup-ul reuseste; pachetul apare cu „ora neverificata”, nu este sters de stergerea automata si nici numarat la „ultimele 4”; un administrator il poate sterge manual (iconita de stergere) | |
| 9 | Setari -> Notificari -> Sistem | Sabloanele „Backup lipsa" si „Copie pe NAS lipsa" exista deja | |

### 08.10.2026 - Backup pe NAS (Setari -> Backup -> Backup NAS)

**Ce s-a adaugat:** tab-ul „Backup NAS" (numai administrator): cale de retea, cont, parola (salvata criptat, nu se mai afiseaza), copiere pe NAS, backup zilnic programat, butoanele „Testeaza conexiunea", „Copiaza acum pachetele lipsa", „Fa backup acum"; tipuri noi de pachete „Programat" si „La cerere" in Restaurare; actiunile de jurnal „Modificare configurare backup NAS", „Testare conexiune NAS", „Copiere backup pe NAS".

**Ce face acum:** fiecare backup facut de aplicatie (inventar, programat, la cerere) se copiaza dupa aceea pe NAS (se scrie sub nume temporar, se verifica marimea si suma de control, apoi se redenumeste; nimic nu se sterge pe NAS). Pe NAS ramane si fisierul `.sha256`.

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 1 | Setari -> Backup -> Backup NAS: cale `\\192.168.100.50\BackupStocDepozit`, cont `EspStoc`, parola, Copiere „Activ", Salveaza | „Configurarea a fost salvata"; la reintrare parola apare doar ca „salvata" | |
| 2 | „Testeaza conexiunea" | „Conexiunea functioneaza: se poate crea si redenumi" (stergerea poate fi refuzata de NAS: este spus) | |
| 3 | „Fa backup acum" | Mesaj cu numele pachetului; pachetul apare pe NAS (.zip si .sha256), fara fisiere .partial | |
| 4 | Parola gresita, apoi cale inexistenta, apoi NAS oprit | Mesaje in cuvinte (cont/parola gresite, partajare negasita), nu cod de eroare brut | |
| 5 | Backup zilnic: Activ, ora peste 2 minute, Salveaza; asteapta | La ora setata apare un pachet „Programat" (Restaurare) si pe NAS; a doua zi/o singura data pe zi | |
| 6 | „Copiaza acum pachetele lipsa" dupa ce NAS-ul a fost oprit la un backup | Se copiaza pachetele care lipsesc, cele existente sunt sarite | |
| 7 | Jurnal activitate | Actiunile NAS apar, fara parola in detalii | |
| 8 | Tab-ul pe ecran ingust si pe telefon | Campurile au margine fata de card, fara depasiri | |

### 08.10.2026 - Sabloane de notificare pe categorii, cu sabloane de pornire

**Ce s-a adaugat:** in Setari -> Notificari -> Sabloane notificari, un tab pentru fiecare categorie (Autovehicule, Mentenanta, Proiecte, Sistem, Stoc), cu numarul de sabloane. La pornirea aplicatiei, fiecare eveniment fara sablon primeste unul activ, cu textul lui implicit (cel din "Completeaza un text-exemplu"); cele 3 sabloane existente (ITP, asigurare, rovinieta) raman neschimbate.

**Ce face acum:** tabul arata doar sabloanele categoriei; "+ Adauga sablon" propune categoria tabului. Un sablon sters nu se mai reface. Pragul sabloanelor noi este cel implicit al evenimentului, minimum 1 zi.

| Pas | Ce faci | Ce trebuie sa vezi |
|---|---|---|
| 1 | Deschide Setari -> Notificari -> Sabloane notificari | Taburile de categorii; fiecare cu cel putin un sablon activ |
| 2 | Tabul "Stoc" -> Editeaza un sablon, schimba textul, salveaza | Modificarea apare in lista tabului |
| 3 | Sterge un sablon, reporneste aplicatia | Sablonul sters nu reapare |
| 4 | Deschide Jurnal, filtreaza "Adaugare sablon notificare" | Sabloanele de pornire apar create de "sistem" |

### 08.10.2026 - Trei notificari noi de stoc

**Ce s-a adaugat:** trei surse noi de notificari, in Setari > Notificari (categoriile „Stoc" si „Proiecte"): „Stoc sub minim", „Rezervare fara miscare" si „Deficit la un proiect cu termen apropiat".

**Ce face acum:** dupa ce creezi un sablon pentru fiecare sursa (subiect si text propuse automat), notificarile apar in pagina Notificari: stoc sub minim (prag implicit 0 zile), rezervare nemodificata de peste 30 de zile, proiect cu termen si lista de achizitie nevida (prag implicit 14 zile inainte de termen). Se inchid singure cand cauza dispare.

1. In Setari > Notificari adauga un sablon pentru „Stoc sub minim": textul propus foloseste `<produs>`, `<stoc>`, `<stoc minim>`.
2. Seteaza un minim peste stocul unui produs (vezi 01): in pagina Notificari apare notificarea, cu link catre produs.
3. Aduce stocul la minim printr-o intrare: notificarea se inchide.
4. Adauga sabloanele pentru „Rezervare fara miscare" si „Deficit la un proiect cu termen apropiat"; seteaza un termen apropiat unui proiect cu produse de achizitionat (vezi 06): apare notificarea cu link catre situatia proiectului.

**Detalii:**

- Stoc sub minim: expira la data ultimei miscari a stocului sau a setarii minimului; prag implicit 0 zile; se inchide cand stocul revine la minim.
- Rezervare fara miscare: o rezervare nemodificata de 30 de zile; se inchide cand este consumata, eliberata sau modificata.
- Deficit la proiect: expira la termenul proiectului, prag implicit 14 zile; se inchide cand lista de achizitie devine goala.
- Sabloanele se creeaza manual in Setari > Notificari, ca la celelalte surse.
- Cod: `Services/StockAlerts.cs` (surse), `Services/ExpiryNotifications.cs` (cheile `stoc.sub-minim`, `stoc.rezervare-fara-miscare`, `proiect.deficit-termen`).

### 07.10.2026 - Ieșiri peste stoc nerezolvate

**Ce s-a adaugat:** sursa de notificari "Ieșiri peste stoc nerezolvate" (cheie `stoc.iesiri-peste-stoc`): o notificare pe produs cu stoc negativ dupa iesiri peste stoc; termen = data primei iesiri neacoperite + 14 zile; prag implicit 7 zile; se inchide singura cand stocul nu mai e negativ. Marcajele din sablon: produs, data prima iesire, numar iesiri, cauza.

**Ce face acum:** Notificarile si contorul de pe pagina principala te anunta cand exista iesiri peste stoc nerezolvate.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> Notificari: sablon nou pentru sursa "Ieșiri peste stoc nerezolvate" | Marcajele <produs>, <data prima iesire>, <numar iesiri>, <cauza> sunt disponibile (inserate la cursor) | |
| 2 | Un produs negativ cu prima iesire peste stoc mai veche de 7 zile | Apare pe /notificari cu link catre pagina de miscari a produsului | |
| 3 | Regularizeaza produsul | Notificarea se inchide singura (apare la rezolvate) | |

### 07.10.2026 - Factura asteptata

**Ce s-a adaugat:** Sablonul de notificare „Factura asteptata" cu prag in zile.

**Ce face acum:** Notificarea apare cand o intrare asteapta factura peste prag; sablonul se poate modifica din Setari.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Setari -> notificari: sablonul "Factura asteptata" (prag 7 zile) | Se poate modifica; comutatoarele On/Off functioneaza | |
| 2 | Intrari libere vechi de la un furnizor | Notificarea apare pe /notificari si contorul de pe pagina principala | |
| 3 | Atasezi intrarile la o factura | Notificarea se rezolva singura | |

### 06.10.2026 - Comutatoare On/Off

**Ce s-a adaugat:** Orice bifare din Setari si notificari este comutatorul On/Off, nu o caseta de bifat.

**Ce face acum:** ANAF, sabloanele de notificari si contractele folosesc comutatoare On/Off; nu mai exista casete de bifat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | ANAF, sabloane de notificari, contracte ("Arata si contractele Off") | Nu exista casete de bifat, doar comutatoare | |
| 2 | Cont "Utilizator" in Setari | Numai tabul Facturi | |

### Verificari anterioare (docs/TESTE_RAMASE.md N1-N6, N17-N18)

**Ce s-a adaugat:** Verificari anterioare ale notificarilor si setarilor.

**Ce face acum:** Pagina /notificari si Setari notificari functioneaza si pe ecran ingust, fara depasiri.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | /notificari si Setari notificari pe ecran ingust | Fara depasiri | |
| 2 | Curatarea rezolvatelor | Dispar dupa interval | |
| 3 | Rulare zilnica pe ceas real | Notificari noi la ora setata | |

## Fluxul de baza

1. Data de expirare apropiata -> notificare -> rezolvare -> dispare automat.

## Modificari de testat - incarcarea pachetului de actualizare (25.10.2026, versiunea 0.5.20)

**Ce s-a adaugat:** pachetul .bsu se incarca printr-o cerere HTTP directa, cu procent.
**Ce face acum:** dupa alegerea fisierului apare „Se incarca pachetul: N%”, apoi „Pachetul este incarcat; Updater-ul il verifica”; raportul apare ca inainte. Pe telefon, prin VPN, incarcarea dureaza cat viteza de upload.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Setari -> Actualizare, alege un .bsu de pe telefon | Procentul creste pana la 100%, apoi apare raportul pachetului | |
| 2 | Alege un fisier care nu este .bsu | Mesaj de eroare clar, fara blocare | |
| 3 | Fara dreptul de aplicare a actualizarilor | Mesaj ca nu ai dreptul | |

## Modificari de testat - notificari agregate, taburi de validare si paginare (30.10.2026, versiunile 0.5.33-0.5.36)

**Ce s-a adaugat:** o singura notificare agregata pentru tichetele de validat si una pentru beneficiarii de verificat; taburile „Tichete de validat" si „Beneficiari de validat" (rosii cand sunt elemente de rezolvat, vizibile pe baza permisiunilor `mentenanta.validare` / `beneficiari.validare`); paginare la tabelele de notificari.
**Ce face acum:** bannerele de sus arata cate elemente asteapta si se actualizeaza dupa rezolvare; tabelele (active, rezolvate) au selector de rand pe pagina.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Notificari cu tichete in coada | Tab rosu cu numarul si banner „Sunt X tichete de validat" | |
| 2 | Valideaza un tichet | Numarul si textul bannerului scad | |
| 3 | Operator cu permisiunea de validare | Vede tabul si poate valida | |
| 4 | Lista cu peste 10 notificari | Selector rand pe pagina sub tabel | |

## Modificari de testat - taburi ActivityApp si module oprite (04.11.2026, Notifications 1.2.0)

**Ce s-a adaugat:** tab nou "Probleme import" in Notificari; "Tichete de validat" arata si tichetele discardate. Panourile de backup si de jurnal din Setari, cautarea ANAF din formularele de beneficiar/furnizor si legarea intrarii de factura au mesaje clare cand modulul lor e oprit.
**Ce face acum:** vezi pasii.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Notificari -> "Probleme import" | Se vad problemele deschise | |
| 2 | Opreste modulul Preluare ActivityApp (Setari -> Module), deschide taburile | "Secțiunea nu este disponibila momentan (modul indisponibil)" | |
| 3 | Opreste ANAF, adauga beneficiar si apasa preluarea din ANAF | Mesaj "Modulul ANAF este oprit..."; formularul ramane utilizabil manual | |
| 4 | Opreste Backup, deschide Setari -> Backup | Mesaj de modul indisponibil | |
| 5 | Porneste la loc toate modulele | Totul revine | |

## Modificari de testat - tabul Balante restante rosu cu numar (04.11.2026, Elcomtur 1.1.0)

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Dupa o preluare Elcomtur cu beneficiari cu sold pozitiv, deschide Notificari | Tabul "Balante restante" este rosu si arata numarul de beneficiari restanti | |
| 2 | Fara beneficiari restanti | Tabul nu e rosu si arata 0 | |

## Modificari de testat - HTTPS prin proxy (06.11.2026, branch claude/https)

**Ce s-a adaugat:** card nou "HTTPS prin proxy" in Setari -> Baza de date -> Conexiune (sub cardul Aplicatia): comutator Pornit/Oprit (implicit Oprit) si lista IP-urilor sau retelelor proxy-ului (nginx). Antete de securitate (nosniff, X-Frame-Options, frame-ancestors) pe toate raspunsurile.
**Ce face acum:** cu comutatorul Oprit aplicatia merge pe HTTP exact ca pana acum. Pornit, aplicatia accepta antetele X-Forwarded-For si X-Forwarded-Proto doar de la IP-urile listate, cookie-urile devin Secure (autentificarea directa pe HTTP la portul aplicatiei nu mai merge) si trimite HSTS pe raspunsurile https. Setarea se aplica la urmatoarea pornire.

| Pas | Ce faci | Rezultat asteptat | Rezultat |
|---|---|---|---|
| 1 | Setari -> Baza de date, deruleaza la cardul "HTTPS prin proxy" | Comutator Oprit, caseta de IP goala | |
| 2 | Porneste comutatorul si apasa Salveaza fara IP | Eroare: scrie cel putin un IP | |
| 3 | Scrie `abc` si Salveaza | Eroare: nu este o adresa IP valida | |
| 4 | Scrie IP-ul proxy-ului (cum il vede VM-ul), Salveaza, parola de administrator | Mesaj ca se aplica la urmatoarea pornire; in Jurnal apare "Modificare HTTPS prin proxy" cu valorile vechi si noi | |
| 5 | Apasa "Reporneste aplicația" din cardul Aplicatia | Aplicatia revine; deschide adresa https a proxy-ului (nginx) | |
| 6 | Autentifica-te prin adresa https | Login reuseste, sesiunea se pastreaza, paginile se incarca fara reconectari | |
| 7 | Deschide direct `http://ip-vm:port` din alt calculator | Autentificarea nu reuseste (cookie Secure): accesul se face doar prin proxy | |
| 8 | Opreste comutatorul, salveaza, reporneste | Aplicatia functioneaza din nou pe HTTP direct | |

## Modificari de testat - changelog pe categorii (11.11.2026, versiunea 0.5.72, PROP-0019)

**Ce s-a adaugat:** versiunile noi din changelog se impart in Functionalitati noi, Bugs si Diverse; migrarea 87 adauga versiunea de rezolvare a rapoartelor de eroare; scriptul tools/changelog-draft.ps1 listeaza PROP/BUG-urile versiunii.
**Ce face acum:** Setari -> Actualizare -> Istoric -> iconita changelog arata categoriile cu titlu pentru versiunea 0.5.72; versiunile vechi raman plate.

1. Deschide Istoric -> "Vezi changelog" pentru 0.5.72: apar cele trei categorii. Pentru 0.5.71 lista ramane simpla.
2. Treci un raport de eroare pe Rezolvat, apoi ruleaza tools/changelog-draft.ps1 -Version 0.5.72: raportul apare sub Bugs (BUG-xxxx).

## Modificari de testat - descarcare pachet backup (11.11.2026, PROP-0020)

**Ce s-a adaugat:** in Setari -> Backup, pe fiecare pachet din tabel, iconita de descarcare (sageata in jos). Un pachet care are alaturi fisierul de suma de control vine intr-o arhiva cu ambele fisiere; unul singur vine asa cum este.
**Ce face acum:** pachetul se descarca de pe server; unul aflat doar pe NAS este adus mai intai pe server (verificat prin suma de control, ca la restaurare) si apoi descarcat. Fiecare descarcare apare in jurnal ca "Descarcare pachet backup".

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Setari -> Backup, iconita de descarcare pe un pachet de pe server | Browserul salveaza arhiva `<nume>-cu-suma-de-control.zip` (pachet + .sha256) sau pachetul simplu | |
| 2 | Pe un pachet doar pe NAS, iconita de descarcare | Mesaj "Se aduce pachetul de pe NAS...", apoi descarcarea; pachetul apare si pe server | |
| 3 | Jurnal activitate | Intrare "Descarcare pachet backup" cu pachetul si daca a fost arhiva | |
| 4 | Cu un cont fara dreptul de a modifica setarile de backup, deschide adresa /api/backup/<nume> | Acces refuzat | |

## Modificari de testat - actualizare cu notificare sau imediata (12.11.2026, versiunea 0.5.77)

**Ce s-a adaugat:** in Setari -> Actualizare, dupa verificarea unui pachet, butonul "Aplica actualizarea" se numeste "Actualizeaza cu notificare utilizatori" si are acelasi efect; butonul nou "Actualizare imediata".

**Ce face acum:** "Actualizeaza cu notificare utilizatori" anunta utilizatorii in banner si porneste dupa 60 de secunde (poate fi anulata); "Actualizare imediata" porneste actualizarea acum, fara banner si fara intarziere. Ambele cer motivul si intra in jurnal ("Actualizare aplicatie"; la cea imediata "Porneste in: imediat, fara anunt").

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Incarca un pachet valid si scrie motivul | Apar doua butoane: "Actualizeaza cu notificare utilizatori" si "Actualizare imediata", cu explicatia lor | |
| 2 | Apasa "Actualizeaza cu notificare utilizatori" | Bannerul cu numaratoare apare pentru toti, butonul "Anuleaza actualizarea" merge, la sfarsit aplicatia se actualizeaza | |
| 3 | Pe alt pachet apasa "Actualizare imediata" | Nu apare banner la alti utilizatori, nu exista numaratoare; actualizarea porneste pe loc | |
| 4 | Deschide Jurnal activitate | Intrarea "Actualizare aplicatie" cu motivul; la cea imediata, "Porneste in: imediat, fara anunt" | |

## Modificari de testat: fereastra de anunt al actualizarii (13.11.2026)

**Ce s-a adaugat:** la o actualizare cu anunt, ceilalti utilizatori conectati primesc o fereastra deasupra paginii si a oricarei ferestre deschise.
**Ce face acum:** "Am inteles" si "Inchide" o inchid, actualizarea continua; "Am nevoie de mai mult timp" intrerupe actualizarea pentru toti, iar cel care a pornit-o primeste mesajul "Actualizare intrerupta" cu numele utilizatorului. Cel care porneste actualizarea nu primeste avertizarea.

| Nr | Pas | Rezultat asteptat | OK |
|----|-----|-------------------|----|
| 1 | Cu doi utilizatori conectati, porneste o actualizare cu anunt (alt pachet) | Al doilea utilizator vede fereastra cu numaratoare; primul nu | |
| 2 | La al doilea apasa "Am nevoie de mai mult timp" | Ferestrele se inchid; primul vede "Actualizare intrerupta, utilizatorul X are nevoie de mai mult timp" | |
| 3 | Jurnal activitate | Intrare "Intrerupere actualizare aplicatie" cu numele utilizatorului | |
