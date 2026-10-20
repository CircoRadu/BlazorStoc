# Notificari si Setari (/notificari, /setari)

Fluxul: Setari (administrator: ANAF, notificari, harta, Facturi) -> sabloane de notificari -> /notificari arata alertele active, rezolvate si ordonate dupa urgenta; meniul are un contor.

## Modificari de testat

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
| 3 | Sterge un sablon, repornește aplicatia | Sablonul sters nu reapare |
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
