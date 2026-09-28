# Predare pentru Claude: baza MariaDB permanenta locala

Data: 28.09.2026. Pregatire executata la cererea utilizatorului, fara preluarea unui ciclu de dezvoltare si fara schimbarea configuratiei aplicatiei.

## 1. Rezultatul efectiv

A fost creata o instanta MariaDB locala separata, cu baza `BlazorStoc`, folosind structura ACTUALA a bazei SQLite de dezvoltare. Datele au fost copiate si comparate integral. Nu s-a importat schema MariaDB legacy din `database/BlazorStoc_create.sql`.

- MariaDB Server **11.4.13**, distributie oficiala Windows x64, verificata SHA-256.
- **27 de tabele InnoDB**, **229 de inregistrari**, **24 de chei externe**, **6 constrangeri CHECK**, **18 triggere**.
- Identificatorii, hashurile parolelor, datele calendaristice, textul, stocurile si istoricul au fost pastrate.
- Cele **14 contoare de identificatori** au fost transpuse in AUTO_INCREMENT fara reutilizarea identificatorilor deja consumati in SQLite.
- Copia SQLite corespunde momentului **28.09.2026 13:58:00**, ora Europe/Bucharest. Verificarea finala a noii baze a fost executata la **28.09.2026 14:05:45**.
- A fost copiat si verificat fisierul extern referit de arhiva, precum si cheia Data Protection existenta. Directoarele de imagini si atasamente active erau goale.
- Baza MariaDB ramane pornita la predare si isi pastreaza datele dupa oprire/pornire.

Aplicatia si preview-urile existente folosesc in continuare SQLite. Nu exista sincronizare automata intre cele doua baze. Modificarile facute ulterior in SQLite nu vor aparea in MariaDB; inaintea comutarii aplicatiei trebuie stabilit explicit momentul de oprire a scrierilor in SQLite si, daca au aparut diferente, o migrare finala verificata. Nu rerula importul peste MariaDB fara analiza datelor existente.

## 2. Date de conectare

| Parametru | Valoare |
|---|---|
| Motor | MariaDB 11.4.13 |
| Gazda | `127.0.0.1` |
| Port | `3307` |
| Baza | `BlazorStoc` |
| Utilizator pentru dezvoltare | `blazorstoc_dev` |
| Parola | Campul `Password` din fisierul privat indicat mai jos |
| Transport | TCP local; TLS obligatoriu pentru contul de dezvoltare |
| Setare MySqlConnector | `SslMode=Required` |
| Charset | `utf8mb4` |
| Collation baza/tabele | `utf8mb4_nopad_bin` |
| Fus orar server | UTC (`+00:00`) |
| Motor tabele | InnoDB |

Fisierul privat pentru contul aplicatiei:

`C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB\application-connection.private.json`

Contine numai parametrii conexiunii si parola contului de dezvoltare. Nu copia parola in documentatie, mesaje, argumentele unui proces sau Git. Citeste fisierul local cand ai nevoie de conectare si introdu secretul in mecanismul local de configurare ales la implementare.

Contul `blazorstoc_dev` are numai SELECT, INSERT, UPDATE si DELETE in aceasta baza. Exista pentru conexiuni locale, fara drepturi globale, creare/stergere de baze, modificare de schema sau administrare de conturi. Este potrivit pentru citire, verificari si operatiile uzuale viitoare, dupa adaptarea codului. Initializarile DDL existente in unele repository-uri vor fi refuzate; separarea lor de operatiile curente ramane necesara.

Pentru administrarea instantei locale, exista fisierele protejate `admin.private.cnf` si `connection.private.json` in acelasi director. Acestea contin identitatea administrativa; foloseste-le numai pentru operatii administrative autorizate asupra acestei instante. Nu utiliza contul root ca utilizator normal al aplicatiei. Directorul este protejat prin ACL pentru utilizatorul Windows Alex, SYSTEM si administratorii sistemului. O sesiune izolata care nu poate citi fisierul necesita acces la acel fisier, nu publicarea parolei.

Conexiunea cu `SslMode=Required` a fost verificata cu biblioteca MySqlConnector din buildul proiectului; negocierea a folosit `TLS_AES_256_GCM_SHA384`. Aceasta confirma criptarea, nu o configuratie completa de verificare CA/hostname pentru productie. Serverul asculta numai pe loopback; accesul din LAN nu a fost configurat.

### Pasi pentru conectare dintr-un client SQL

1. Verifica faptul ca instanta locala este pornita, conform sectiunii 3.
2. Deschide o conexiune noua de tip MariaDB/MySQL prin TCP, catre `127.0.0.1`, port `3307`.
3. Introdu utilizatorul `blazorstoc_dev` si parola din `application-connection.private.json`.
4. Activeaza TLS/SSL obligatoriu. Nu dezactiva SSL pentru a evita o eroare de configurare; contul refuza conexiunile necriptate.
5. Selecteaza baza `BlazorStoc` si verifica prezenta tabelelor `products`, `stock_movements`, `web_users` si `audit_events`.
6. Confirma ca serverul raporteaza versiunea 11.4.13 si portul 3307 inaintea oricarei modificari. Nu folosi o conexiune salvata pentru alta baza/server.

Nu este necesar cod nou de conectare pentru inspectarea bazei. Acest document livreaza instructiuni, nu implementarea integrarii in aplicatie.

## 3. Amplasare, pornire si persistenta

Radacina instalarii:

`C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB`

| Continut | Cale relativa la radacina |
|---|---|
| Executabile MariaDB | `mariadb-11.4.13-winx64\bin` |
| Date permanente ale serverului | `data` |
| Configuratie server | `my.ini` |
| Jurnal server | `server-error.log` |
| Comenzi operationale locale | `local-mariadb.ps1` |
| Snapshotul SQLite folosit la import | `migration\source.sqlite` |
| Export SQL dupa migrare | `migration\BlazorStoc-after-migration.sql` |
| Raport verificare completa | `migration\verification.json` |
| Schema sursa inventariata | `migration\source-schema.json` |
| DDL MariaDB rezultat | `migration\schema-mariadb.sql` |
| Definitii triggere MariaDB | `migration\triggers-mariadb.sql` |
| Contoare SQLite originale | `migration\sqlite-sequences.json` |

Instalarea si datele sunt in afara repository-ului si a directorului sincronizat al proiectului. Nu sterge acest director ca pe un artefact de build.

Serverul este configurat sa porneasca ascuns la autentificarea lui Alex in Windows, prin scurtatura:

`C:\Users\Alex\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\BlazorStoc MariaDB local.lnk`

Nu este un serviciu Windows de sistem si nu porneste inainte de autentificarea utilizatorului. Nu au fost necesare drepturi de administrator pentru instalarea serviciului. Persistenta datelor si restartul procesului au fost verificate; autentificarea dupa o repornire completa a Windows nu a fost testata in aceasta sesiune.

Pentru pornire, stare sau oprire controlata, foloseste utilitarul operational deja instalat in directorul local, cu parametrul `-Action start`, `-Action status` sau `-Action stop`. De exemplu, in PowerShell:

```powershell
& "$env:LOCALAPPDATA\BlazorStoc-MariaDB\local-mariadb.ps1" -Action status
```

Schimba doar valoarea parametrului pentru actiunea necesara. Utilitarul nu initializeaza din nou baza si nu contine parole; citeste optiunile administrative locale protejate. Acesta este un instrument operational al instalarii, nu o componenta adaugata codului aplicatiei. Nu opri serverul in timpul unei operatii de date.

## 4. Cum s-a facut transferul

1. A fost identificata sursa `BlazorStoc\data\blazorstoc-local.db`; verificarile SQLite de integritate si chei externe au trecut.
2. S-a descarcat distributia oficiala MariaDB 11.4.13 Windows x64 si s-a verificat hashul fata de manifestul distributiei.
3. S-a initializat o instanta noua, separata, pe portul local liber 3307, cu parole generate si acces limitat la calculatorul local.
4. S-a rezervat temporar accesul de scriere SQLite cu o tranzactie `BEGIN IMMEDIATE`, fara modificari de date. O conexiune separata a creat snapshotul prin API-ul SQLite Backup; in aceeasi fereastra s-au copiat fisierele si verificat hashurile lor. La final, rezervarea a fost eliberata prin rollback.
5. S-au inventariat tabelele, coloanele, cheile, indecsii, CHECK-urile, triggerele si contoarele bazei reale, inclusiv modificarile deja aplicate dupa schema initiala.
6. S-au creat tabelele MariaDB in ordinea dependentelor, cu cheile externe active. S-au importat toate randurile in tranzactie, prin parametri, fara normalizarea sau rescrierea textului, parolelor ori datelor calendaristice.
7. S-au pastrat contoarele de identificatori si s-au creat triggerele DUPA import, pentru a nu genera evenimente suplimentare din simpla copiere a datelor.
8. S-au comparat toate randurile tuturor tabelelor folosind reprezentari canonice cu tipuri si SHA-256, s-au verificat relatiile si s-a executat CHECK TABLE pentru fiecare tabela.
9. S-au verificat conectarea .NET cu TLS, o actualizare tranzactionala, evenimentul generat de trigger si anularea prin rollback. Contoarele avansate de test au fost readuse la valorile necesare pastrarii secventelor sursei.
10. S-a oprit si repornit controlat instanta, apoi s-au reverificat integral datele, fisierele si contoarele. A fost salvat si un export SQL al bazei migrate.

Nu au fost deconectate sau oprite preview-urile SQLite si nu au fost modificate randuri din sursa. Aceasta a fost o migrare punctuala controlata, nu implementarea functiei de backup/restaurare din aplicatie.

## 5. Structura si transformarile tehnice

Schema pastreaza numele folosite de SQLite: `products`, `categories`, `subcategories`, `beneficiaries`, `stock_movements`, `web_users`, `projects` etc. Nu exista tabele legacy `produs`, `io`, `web_user` sau `web_role` create artificial pentru a masca incompatibilitatile codului vechi.

| Element SQLite | Reprezentare in baza noua |
|---|---|
| INTEGER | BIGINT semnat, pentru domeniul numeric SQLite |
| INTEGER PRIMARY KEY AUTOINCREMENT | BIGINT PRIMARY KEY AUTO_INCREMENT, cu urmatorul ID pastrat |
| TEXT neindexat | LONGTEXT, cu valorile si NULL pastrate |
| TEXT indexat/cheie | VARCHAR cu limita explicita, verificata fata de toate valorile existente |
| Comparare binara a textului | `utf8mb4_nopad_bin`, pentru a nu echivala automat majuscule/accente/spatii finale |
| Date calendaristice stocate ca text | Pastrate ca text exact, fara conversie automata in DATETIME |
| `sqlite_sequence` | Starea transpusa in AUTO_INCREMENT si pastrata in raport; nu se creeaza tabela interna SQLite in MariaDB |
| Triggere SQLite | Sintaxa MariaDB `FOR EACH ROW`, cu timestamp UTC textual echivalent |

Limitele VARCHAR principale: identificatori text si `archive_id` 64; `normalized_name` 512; `normalized_username`, `normalized_cui`, `normalized_plate` si `actor_username` 191; tipuri de entitati/relatii 64; identificatori originali text 128; date indexate 40. DDL-ul local descrie exact fiecare coloana. Aceste limite sunt o diferenta fata de TEXT nelimitat din SQLite si trebuie respectate de validarile viitoare. Nu s-a trunchiat nicio valoare la transfer.

Instanta Windows foloseste `lower_case_table_names=1`; numele bazei poate aparea `blazorstoc` in metadate. Conectarea cu `BlazorStoc` a fost verificata. Pentru live se vor verifica diferentele de capitalizare ale numelor SQL inainte de alegerea configuratiei serverului.

`app_metadata.schema_version` a fost copiat ca parte a datelor sursei. El descrie versiunea SQLite existenta si nu dovedeste ca exista deja un sistem de migrari MariaDB. Nu schimba valoarea doar pentru a evita o validare fara a defini migrarile reale.

## 6. Continutul copiat si verificarile

| Tabela | Randuri |
|---|---:|
| app_metadata | 6 |
| archive_beneficiaries | 0 |
| archive_files | 1 |
| archive_operations | 9 |
| archive_products | 0 |
| archive_project_observation_files | 1 |
| archive_project_observations | 1 |
| archive_projects | 3 |
| archive_relations | 3 |
| archive_stock_movements | 4 |
| archive_vehicles | 0 |
| archive_web_users | 0 |
| audit_events | 133 |
| beneficiaries | 3 |
| categories | 5 |
| change_events | 11 |
| product_images | 0 |
| product_locks | 0 |
| products | 11 |
| project_observation_files | 0 |
| project_observations | 1 |
| projects | 1 |
| stock_movement_history | 3 |
| stock_movements | 21 |
| subcategories | 8 |
| vehicles | 2 |
| web_users | 2 |
| **Total** | **229** |

Rezultate: toate randurile identice fata de snapshot; zero relatii invalide; toate tabelele verificate; toate fisierele copiate corespund hashurilor. La controlul final al sursei nu existau diferente de continut intre SQLite live si snapshot; aceasta constatare nu se extinde asupra modificarilor viitoare.

Snapshot SQLite SHA-256:

`9ac9aba674e6e7dfa832ed65e66b917c8bc37d5078bd4bde7910c515ca4ef8f9`

Export SQL dupa migrare SHA-256:

`8C5B927864EDC46C5B2577411725F0DFEE0EDCE911C1A28D2B5661B6F0C5485B`

Exportul contine date sensibile, inclusiv hashuri de parole si istoricul aplicatiei; ramane in directorul local protejat. Existenta exportului nu reprezinta un test complet de restaurare a acestui fisier intr-o a doua baza. Functia de restaurare din aplicatie ramane neimplementata.

## 7. Fisiere asociate si chei

Toate caile de mai jos sunt sub radacina locala `C:\Users\Alex\AppData\Local\BlazorStoc-MariaDB`:

| Utilizare viitoare | Director |
|---|---|
| Imagini produse | `assets\product-images` |
| Atasamente proiecte | `assets\project-files` |
| Fisiere arhivate | `assets\archive-files` |
| Copia cheilor Data Protection | `assets\data-protection-keys` |

S-a copiat un fisier arhivat de 2.294.044 octeti si un fisier de cheie Data Protection. Cheile sunt sensibile si nu se publica. Copia lor conserva materialul necesar pentru datele protejate existente; decriptarea efectiva si compatibilitatea identitatii Windows/application name trebuie verificate la integrare. Nu presupune portabilitatea automata a cheilor pe alta gazda.

Nu configura noua baza sa foloseasca din greseala directoarele SQLite active. La comutare, foloseste coerent baza MariaDB si directoarele copiate pentru ea.

## 8. Punctul de pornire pentru Claude: adaptari ulterioare

**Nu este suficient sa setezi acum `App:DemoMode=false`.** Codul MariaDB existent foloseste in mai multe locuri alta schema decat schema de dezvoltare transferata. Aplicatia nu a fost conectata la noua baza in aceasta sesiune si nu se declara functionala integral pe ea.

Pasi recomandati, fara cod de implementare in aceasta predare:

1. Citeste acest document, raportul de verificare si DDL-ul local. Confirma conectarea cu contul de dezvoltare si verifica datele fara a le modifica.
2. Revizuieste `Program.cs`, `Services/DatabaseConnections.cs` si `appsettings.json`. Pregateste in ciclul autorizat configuratia MariaDB locala si selectia serviciilor; nu introduce secrete in surse.
3. Foloseste ca referinta semantica repository-urile `Sqlite*`, deoarece acestea corespund structurii transferate. Adapteaza implementarile MariaDB la numele/coloanele actuale, nu recrea automat schema legacy peste datele migrate.
4. Produse/categorii/beneficiari: codul MariaDB foloseste `produs`, `categorie`, `subcategorie`, `beneficiar`, in timp ce baza noua foloseste `products`, `categories`, `subcategories`, `beneficiaries`.
5. Miscari: adapteaza `MariaStockMovementRepository` de la `io` si istoricul legacy la `stock_movements` si `stock_movement_history`, pastrand identificatorii, sursa/destinatia, vehiculele si proiectele existente.
6. Autentificare: `MariaUserRepository` asteapta `web_user` si `web_role`; baza noua are `web_users`, cu rolul in coloana `role`. Hashurile existente au fost pastrate. Nu recrea conturile si nu reseta parolele pentru a evita adaptarea interogarilor.
7. Audit/arhivare: verifica `MariaAuditTrail`, `MariaArchiveSchema`, `ArchivePersistence` si tipurile de date. Campurile UTC din baza migrata sunt text; citirile `GetDateTime` si scrierile de DATETIME necesita adaptare explicita. Nu rula automat schemele Maria legacy peste aceste tabele.
8. Proiecte, vehicule, imagini si atasamente: verifica numele tabelelor, ID-urile si citirea cailor relative. Configureaza directoarele locale din sectiunea 7.
9. Blocari si sincronizare: `ProductLocks` foloseste in implementarea MariaDB `product_lock`/`produs`; baza noua are `product_locks`/`products`. `ChangeEvents` trebuie sa foloseasca structura actuala si cele 18 triggere deja instalate, fara a crea triggere suplimentare pentru tabele legacy inexistente. Verifica parsarea timpilor textuali.
10. Separa initializarea si migrarile de operatiile normale. Contul aplicatiei nu are DDL; nu ii acorda drepturi globale doar ca sa treaca initializarile vechi. Defineste migrarile si contul operational pentru ele in ciclul de implementare.
11. `Database:ApplicationUserId` si tabela legacy `user` nu au fost create artificial: schema transferata stocheaza operatorii/auditul dupa regulile SQLite actuale. Adapteaza dependentele MariaDB legacy de aceasta identitate tehnica sau defineste explicit o migrare, fara ID-uri fictive.
12. Adauga si executa verificari de integrare pe baze MariaDB locale izolate, apoi valideaza fluxurile prin browser. Abia dupa aceea comuta preview-ul si confirma persistenta la restart, autentificarea si doua sesiuni concurente.
13. La comutarea definitiva de dezvoltare, opreste scrierile SQLite si gestioneaza explicit eventualele date aparute dupa snapshot. Nu lasa doua baze independente sa para aceeasi sursa de adevar.

Nu s-a generat pentru Claude cod nou de conectare, repository-uri sau componente ale aplicatiei. Instrumentele temporare de migrare/verificare nu fac parte din livrarea surselor proiectului. Implementarea ulterioara a backupului/restaurarii se ghideaza dupa [CLAUDE_BACKUP_RESTAURARE.md](CLAUDE_BACKUP_RESTAURARE.md), tinand cont de baza reala descrisa aici.

## 9. Limite si stare la predare

- Conexiune, import, egalitate a datelor, TLS, tranzactie/trigger/rollback si restart MariaDB: verificate.
- Integrarea completa a aplicatiei, login prin pagina web pe MariaDB si toate fluxurile CRUD pe noua baza: neimplementate/nevalidate, ramase pentru Claude.
- Restaurarea exportului SQL intr-o alta baza si mecanismul de backup din UI: neimplementate/nevalidate.
- Pornire automata: configurata pentru autentificarea utilizatorului Alex; nu s-a repornit Windows pentru verificare.
- Nu s-au configurat NAS, containere, acces din retea sau productie.
- Modificarile preexistente in `Services/Products.cs`, `Services/SqliteLocalStore.cs` si `tests/BlazorStoc.Checks/Program.cs` au ramas neatinse. Nu s-au facut commituri si nu s-a modificat `.collaboration/state.json`.

## 10. Provenienta instalarii

Arhiva: `mariadb-11.4.13-winx64.zip`, din distributia oficiala arhivata MariaDB. SHA-256 verificat:

`D62986D433EEEBFDE218560B276103831604A61E929E87F1A17F5AEBD80257E2`

Referinte: [distributia oficiala](https://archive.mariadb.org/mariadb-11.4.13/winx64-packages/), [instalarea ZIP pe Windows](https://mariadb.com/docs/server/server-management/install-and-upgrade-mariadb/installing-mariadb/binary-packages/installing-mariadb-windows-zip-packages).
