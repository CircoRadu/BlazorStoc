# Notificari si Setari (/notificari, /setari)

Fluxul: Setari (administrator: ANAF, notificari, harta, Facturi) -> sabloane de notificari -> /notificari arata alertele active, rezolvate si ordonate dupa urgenta; meniul are un contor.

## Modificari de testat

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
