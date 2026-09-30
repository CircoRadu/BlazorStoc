# Propunere: puncte de lucru extinse, contracte de mentenanta si registru de interventii

Document de analiza din 30.09.2026. Este o propunere, nu o implementare; deciziile din sectiunea 11 au fost confirmate de utilizator la 30.09.2026 si sunt incorporate mai jos; ipotezele de proiectare H1-H6 de la sfarsitul sectiunii 11 sunt, de asemenea, confirmate. Porneste de la cerinta utilizatorului (puncte de lucru cu descriere si poze, contract de mentenanta On/Off cu ciclicitate, registru de interventii, sablon de notificare) si de la codul existent (`Services/WorkPoints.cs`, `Services/ExpiryNotifications.cs`, `Services/MariaSchemaMigrations.cs`, `Components/Pages/BeneficiaryDetail.razor`).

## 1. Rezumat

Formula recomandata are patru piese, fiecare cu un singur rol:

1. **Punct de lucru** = randul real din `beneficiary_work_points`, inclusiv cel principal (azi acesta este doar derivat din adresa beneficiarului si nu are id). Primeste `descriere` si poze.
2. **Contract de mentenanta** = o inregistrare proprie (numar + data, stare On/Off, ciclicitate globala). Un beneficiar poate avea mai multe contracte; un punct de lucru apartine **cel mult unui contract activ** (decizie confirmata; contractele Off nu conteaza), regula garantata de un index unic. Contractul are si o **data de expirare** optionala, cu notificare proprie.
3. **Acoperire** = legatura contract - punct de lucru. Aici sta **scadenta curenta** (`next_due`), singurul camp care spune "cand urmeaza urmatoarea interventie", plus o ciclicitate individuala optionala.
4. **Interventie** = randul din registru, de doua feluri: **de mentenanta** (sub contract, intra in ciclu) si **la cerere** (nu intra in ciclu). Cea de mentenanta are beneficiar, punct de lucru, contract, data efectuarii, observatii si scadenta pe care a inchis-o; la inregistrare, operatorul alege urmatoarea scadenta dintre trei variante (din data efectuarii, din data planificata sau stabilita de el). Cea la cerere are data, punct de lucru, observatii si poze, fara contract si fara efect asupra scadentei.

Notificarile nu au tabel nou pentru surse: acoperirea (scadenta interventiei) si contractul (expirarea lui) devin doua **surse** in registrul `IExpirySource` existent. Motorul primeste completari generale, valabile pentru toate sursele: cel mult o notificare nerezolvata pe eveniment, ramane in lista dupa scadenta pana este rezolvata (depasitele in varf, pe fond rosu discret), starea "rezolvata" cu motiv si lista ei, si o curatare optionala a rezolvatelor vechi (vezi sectiunea 6).

## 2. Ce exista deja si ce lipseste

| Cerinta | Stare in cod |
|---|---|
| Punct de lucru cu denumire, adresa, telefon | Exista (`beneficiary_work_points`, plus `contact_person`) |
| Punctul principal | Derivat din beneficiar, **fara id** (`WorkPointRules.Primary`, `Id = 0`) |
| Descriere text | Lipseste |
| Poze pentru punctul de lucru | Lipsesc (exista mecanismul pentru produse si observatii de proiect, `Services/ProductImages.cs`, `MariaProjectFileStore`) |
| Contract de mentenanta, acoperire, ciclicitate | Lipsesc |
| Registru de interventii (mentenanta si la cerere, cu poze) | Lipseste |
| Notificari pe data | Exista (`IExpirySource`, sabloane, pagina `/notificari`), dar numai **pana la** expirare |

Atentie la denumiri: in cod, "Maintenance"/"Intretinere" inseamna deja **pagina de asteptare la backup/restaurare** (`Maintenance.razor`, `MaintenanceGate`, `MaintenanceSupport`, ruta `/intretinere`). Pentru domeniul nou se recomanda in cod prefixul `Service` (`ServiceContract`, `ServiceIntervention`), iar in interfata "Mentenanta". Ruta noua: `/mentenanta`.

## 3. Modelul de date

Toate modificarile de schema intra ca migrari noi (cate una sau mai multe pe task, incepand cu 5) in `MariaSchemaMigrations` si in `database/mariadb/schema-mariadb.sql`, idempotente, cu coloanele asteptate listate.

### 3.1 Punctele de lucru

- Coloane noi in `beneficiary_work_points`: `description VARCHAR(2000) NOT NULL DEFAULT ''`, `is_primary TINYINT NOT NULL DEFAULT 0`.
- **Punctul principal devine rand real** (`is_primary = 1`), cate unul pe beneficiar:
  - se creeaza in aceeasi tranzactie cu beneficiarul; adresa si telefonul lui se sincronizeaza la editarea beneficiarului (regula existenta "se modifica din editarea beneficiarului" ramane);
  - denumirea, descrierea si pozele lui se editeaza ca la celelalte; adresa si telefonul raman doar-citire in dialogul lui;
  - nu poate fi sters separat.
  - De ce: contractul, interventia, pozele si notificarile au nevoie de un id stabil. Alternativa "NULL inseamna principal" rupe cheile unice si nu are unde sa tina poze si descriere.
- **Backfill**: `normalized_address` se calculeaza in C# (`AddressNormalization.Key`), nu in SQL, iar contul de migrare nu are drepturi pe date. Deci migrarea DDL adauga doar coloanele; crearea randurilor principale pentru beneficiarii existenti se face **in cod, cu contul aplicatiei, idempotent, la pornire** (creeaza doar ce lipseste).
- Un punct de lucru **acoperit de un contract sau cu interventii nu se sterge** (FK `RESTRICT`); mesaj: "scoate-l mai intai din contract". Istoricul ramane citibil prin snapshot-ul din interventie (3.4).
- Poze (decizie confirmata: **pe disc**, ca la produse si proiecte): un singur tabel `service_photos` pentru punctele de lucru **si** pentru interventii (`id`, `work_point_id` NULL, `intervention_id` NULL, exact unul completat, verificat cu CHECK; `relative_path`, `original_name`, `content_type`, `byte_length`, `sha256`, `caption`, `uploaded_by`, `uploaded_utc`). Fisierele sunt pe disc sub radacina de active (ca `project-files`), servite printr-un endpoint autorizat `/media/service-photos/{id}` (ca `/media/project-files/{id}`). Reguli: doar imagini (detectia prin octeti, ca `ProductImageRules.DetectContentType`), limita de dimensiune (propus 10 MB) si de numar (propus 20 per punct sau per interventie), duplicat blocat dupa `sha256`. Miniaturi optionale (OpenCvSharp exista deja ca dependenta). Un singur magazin de fisiere serveste ambele utilizari.
- **Coordonate** (decizie confirmata): coloane nulabile `latitude` / `longitude` (DECIMAL(9,6)) pe `beneficiary_work_points`, introduse odata cu restul punctului de lucru (taskul "Puncte de lucru extinse"), nu la harta. In dialogul punctului exista un **comutator "Coordonate"**: cand este pornit apar campurile (sau un singur camp "lat, long" care accepta lipirea din alta harta), cand este oprit punctul nu are coordonate (valorile se sterg la salvare). **Un punct de lucru se poate introduce si fara coordonate.** Validare: -90..90 si -180..180, ambele sau niciuna. Punctul principal isi are propriile coordonate, editabile chiar daca adresa lui vine din beneficiar. Fara geocodare automata (ar trimite adresele beneficiarilor unui serviciu extern). Jurnal: "Modificare coordonate punct de lucru" (valoarea veche si noua). Selectorul "alege pe harta" vine odata cu harta.

### 3.2 Contractul

`service_contracts`:

| Coloana | Tip | Observatie |
|---|---|---|
| `id`, `version` | BIGINT | ca in celelalte tabele (concurenta optimista) |
| `beneficiary_id` | BIGINT | FK `RESTRICT` |
| `contract_number` | VARCHAR(30) | ex. `26` |
| `contract_date` | DATE | ex. `23.09.2025` |
| `cycle_months` | TINYINT | 1-12, ciclicitatea globala a contractului |
| `valid_until` | DATE NULL | data de expirare a contractului (decizie confirmata); NULL = fara termen; nu poate fi anterioara `contract_date` |
| `is_active` | TINYINT | On/Off |
| `notes` | VARCHAR(1000) | optional |

Cheie unica `(beneficiary_id, contract_number, contract_date)`.

**Numarul se tine structurat, nu ca text liber.** In interfata este un singur camp care accepta `26/23.09.2025`, `26 / 23.09.2025` si `26 din 23.09.2025`; se parseaza in `numar` + `data` (data reala, validata) si se afiseaza canonic `26/23.09.2025`. Astfel data este sortabila si verificabila, iar duplicatele se prind.

**De ce mai multe contracte pe beneficiar:** faptul ca exista starea Off (in loc de stergere) presupune ca un contract incheiat ramane in istoric, iar unul nou (alt numar/alta data) se adauga. Un singur contract pe beneficiar ar pierde legatura dintre interventii si contractul sub care s-au facut.

**Regula confirmata: mai multe contracte pe beneficiar, dar un obiectiv (punct de lucru) nu poate fi legat decat de un singur contract, iar regula se refera numai la contractele active.** Un punct poate deci sta in mai multe contracte Off (istoric) si in cel mult unul activ. MariaDB nu are index unic partial, asa ca regula se garanteaza prin coloana `active_work_point_id` din tabelul de acoperire (3.3), unica, plina numai cat timp contractul este activ. O incalcare nu poate corupe datele: ea esueaza ca eroare de unicitate, tradusa in mesajul "Punctul X este deja in contractul activ Y".

**Contractul expirat.** `valid_until` controleaza numai chipul "Expirat" si notificarea de expirare (6.2); starea On/Off ramane singurul comutator al obligatiilor de mentenanta. Un contract expirat si inca On continua sa produca scadente de interventii pana este trecut pe Off (sau pana i se prelungeste data); notificarea rosie de expirare depasita este tocmai avertismentul pentru asta.

### 3.3 Acoperirea (puncte de lucru in contract)

`service_contract_points`:

| Coloana | Observatie |
|---|---|
| `id`, `version` | `id` este `ObjectId` pentru notificari |
| `contract_id`, `work_point_id` | FK `RESTRICT`; unic pe pereche |
| `active_work_point_id` | BIGINT NULL, **unic**: egal cu `work_point_id` cat timp contractul este activ, altfel NULL (indicele unic accepta mai multe NULL). Il seteaza serviciul in aceeasi tranzactie cu activarea/dezactivarea contractului si cu adaugarea/scoaterea punctului; garanteaza "un punct - un singur contract activ" |
| `cycle_months` | NULL = mosteneste de la contract; valoare = ciclicitate individuala |
| `next_due` | DATE NOT NULL, scadenta curenta |

Doua comportamente diferite, intentionat:

- **Ciclicitatea se mosteneste** (`NULL` = ia valoarea contractului). Schimbarea ciclicitatii contractului se aplica tuturor punctelor care nu au valoare proprie, adica exact "declarata global".
- **Data primei interventii se copiaza** in `next_due` la crearea acoperirii (din data globala sau din cea individuala aleasa in formular) si apoi este proprie fiecarui punct. Este o valoare initiala, nu o regula continua; butonul "Aplica aceeasi data tuturor" o scrie in toate randurile selectate.

### 3.4 Interventia (registrul)

Registrul are **doua feluri de interventii** (decizie confirmata), in acelasi tabel si in aceeasi lista, deosebite prin `kind`:

- **De mentenanta** (`M`): se face pe un punct acoperit de un contract activ si **intra in ciclu** (muta `next_due`).
- **La cerere** (`C`): se face pe orice punct de lucru al beneficiarului (si fara contract, si cu contract Off), are data, punctul de lucru, observatii optionale si poze, si **nu intervine in ciclul de mentenanta**: nu muta `next_due`, nu cere contract, nu genereaza notificari.

Felul se alege la adaugarea interventiei si nu se mai schimba dupa aceea (o greseala se corecteaza prin stergere cu motiv si reintroducere, ca sa nu se recalculeze ciclul pe ascuns).

`service_interventions`:

| Coloana | Observatie |
|---|---|
| `id`, `version` | |
| `kind` | `M` mentenanta / `C` la cerere |
| `beneficiary_id` | denormalizat, pentru filtrarea registrului |
| `work_point_id` | FK `RESTRICT` |
| `contract_id` | FK `RESTRICT`; obligatoriu la `M`, NULL la `C` |
| `work_point_name`, `work_point_address`, `contract_label` | **snapshot** la momentul inregistrarii: numele si adresa pot fi editate ulterior, registrul ramane fidel; acopera cerinta "interventia dispune de denumire si adresa"; `contract_label` este NULL la `C` |
| `performed_on` | DATE, nu in viitor |
| `planned_due` | DATE NULL; la `M`, scadenta pe care a inchis-o (snapshot al `next_due`); NULL la `C` |
| `next_due_basis` | `E` din data efectuarii / `P` din data planificata / `O` stabilita de operator; NULL la `C` si la interventiile care nu muta scadenta |
| `notes` | VARCHAR(2000), "observatii" (optionale) |
| `recorded_by`, `recorded_utc` | automat, din utilizatorul autentificat |

Pozele interventiei sunt in `service_photos` (3.1), pentru ambele feluri.

`planned_due` da gratuit: intarzierea (`performed_on - planned_due`), rapoarte de respectare a ciclicitatii si **anularea exacta** a ultimei interventii de mentenanta (revenirea `next_due` la valoarea dinainte). `next_due_basis` lasa in registru urma alegerii operatorului.

Stergerea unei interventii cere motiv si urmeaza modelul de arhivare existent (`ArchiveService`). Un punct de lucru cu interventii de orice fel nu se sterge.

## 4. Formula datelor

### 4.1 Reguli

```
ciclicitate_efectiva = acoperire.cycle_months ?? contract.cycle_months

la crearea acoperirii:           next_due = data primei interventii (globala sau individuala)

la inregistrarea unei interventii DE MENTENANTA cu data D pe acoperire:
                                 interventie.planned_due = next_due
                                 operatorul alege urmatoarea scadenta dintre:
                                   E  din data efectuarii   D.AddMonths(ciclicitate_efectiva)
                                   P  din data planificata  planned_due.AddMonths(ciclicitate_efectiva)
                                   O  stabilita de operator  o data introdusa de el
                                 next_due = data aleasa; interventie.next_due_basis = E / P / O

interventie LA CERERE:           next_due nu se modifica
```

- "Data primei interventii" este **scadenta initiala** (prima vizita planificata), nu data de la care se numara ciclul (decizie confirmata). Primul ciclu se numara din data la care s-a facut efectiv prima vizita, cu varianta aleasa la inregistrare.
- **Alegerea se face la introducerea efectuarii** (decizie confirmata): dialogul afiseaza cele trei variante, fiecare cu data calculata alaturi, iar cea din data efectuarii este preselectata (cum a fost formulat initial: "data de referinta se modifica"). Varianta din data planificata pastreaza calendarul initial: o vizita facuta cu intarziere nu muta seria. Varianta stabilita de operator acopera reprogramarile la cererea clientului. Scadenta este stocata, deci toate trei sunt doar modalitati de a o completa.
- Scadenta aleasa trebuie sa fie **dupa** data efectuarii. Daca varianta P ar cadea inaintea sau in ziua efectuarii (vizita facuta cu mai mult de un ciclu intarziere), ea se afiseaza dezactivata, cu explicatia.
- Variantele E si P se calculeaza din valori reale, nu inlantuit din scadente calculate, deci nu se acumuleaza abateri. `DateOnly.AddMonths` limiteaza la sfarsitul lunii (30.11.2025 + 3 luni = 28.02.2026), fara efect cumulat.
- Numai cea mai recenta interventie de mentenanta a punctului determina `next_due`: una introdusa cu data mai veche nu il muta si nu afiseaza alegerea (nu are ce sa aleaga).
- Corectarea datei ultimei interventii de mentenanta redeschide alegerea; stergerea ei readuce `next_due` la `planned_due` al interventiei sterse.
- Interventiile la cerere nu apar in aceste calcule si nu modifica starea din 4.3.

### 4.2 Exemplu

Contract `26/23.09.2025`, ciclicitate 3 luni, doua puncte: "Sediu" (prima interventie 15.10.2025) si "Depozit" (20.10.2025).

| Eveniment | `next_due` Sediu dupa eveniment |
|---|---|
| Creare contract | 15.10.2025 |
| Interventie de mentenanta efectuata 18.10.2025, varianta E | 18.01.2026 |
| Interventie la cerere 12.03.2026 | nemodificat (18.01.2026) |
| Interventie de mentenanta efectuata 05.02.2026 (planificata 18.01.2026, intarziere 18 zile), varianta E | 05.05.2026 |
| Aceeasi interventie, varianta P | 18.04.2026 |
| Aceeasi interventie, varianta O (operatorul alege 02.06.2026) | 02.06.2026 |
| Anulare interventie din 05.02.2026 | 18.01.2026 |

### 4.3 Starea afisata (derivata, nestocata)

| Stare | Conditie |
|---|---|
| Contract Off | contractul nu este activ; punctul nu apare implicit in scadente (optiunea "Arata si contractele Off" il afiseaza in gri, fara stare de scadenta) si nu genereaza notificari |
| Depasita | `next_due < azi` |
| In curand | `0 <= zile pana la next_due <= prag` |
| La zi | altfel |

Pragul vizual = pragul singurului sablon activ al sursei (implicit 30 zile cand nu exista niciunul). Toate datele se compara cu ziua locala a aplicatiei si se afiseaza `dd.mm.yyyy`.

## 5. Contractul On/Off

- Off nu sterge nimic: acoperirea, `next_due` si istoricul raman. Punctele contractului nu mai apar la scadente si nu mai produc notificari (notificarile lor nerezolvate se rezolva automat la urmatoarea evaluare, cu motivul "contract dezactivat (On → Off)", si raman vizibile in fila "Rezolvate" a notificarilor).
- **Reactivarea deschide un dialog de reprogramare**: lista punctelor cu `next_due` curent (cele din trecut evidentiate) si un camp de data editabil per punct. Fara acest pas, un contract redeschis dupa luni ar declansa deodata notificari "depasite" pentru toate punctele.
- Un punct de lucru poate fi in cel mult un contract **activ** (3.2). Consecinte: (a) la adaugarea unui punct intr-un contract activ, formularul arata punctele deja acoperite de alt contract activ, cu optiunea "Muta aici" (randul de acoperire trece la contractul nou, cu `next_due`, ciclicitatea individuala si identitatea notificarii pastrate; jurnalizat "Mutare punct de lucru in alt contract"); (b) la **reactivarea** unui contract, daca unele puncte sunt intre timp in alt contract activ, activarea se opreste cu lista punctelor in conflict si propunerea de a le scoate din contractul reactivat; (c) un contract Off isi pastreaza punctele ca istoric, chiar daca ele au intrat intre timp in alt contract.
- Stergerea unui contract e permisa doar daca nu are interventii; altfel se trece pe Off.

## 6. Notificari

Sunt doua surse noi in registrul existent (fara tabel nou pentru ele) si doua completari **generale** ale motorului, valabile pentru toate sursele, inclusiv ITP, asigurare si rovinieta.

### 6.1 Completari generale ale motorului (decizii confirmate)

1. **Cel mult un sablon activ pe eveniment si cel mult o notificare nerezolvata pe eveniment** (decizie confirmata). "Eveniment" in formularul de sablon este perechea categorie + eveniment (sursa, de exemplu Autovehicule / ITP). **Nu pot exista doua sabloane active in acelasi timp pentru aceeasi sursa**: crearea sau activarea unui al doilea sablon activ este refuzata cu mesaj clar ("Exista deja un sablon activ pentru acest eveniment: dezactiveaza-l mai intai"); pot exista oricate sabloane dezactivate. La livrare se verifica daca baza contine deja doua sabloane active pe aceeasi sursa; daca da, administratorul alege pe care il pastreaza. Pentru un eveniment `(sursa, obiect, data scadentei)` exista cel mult o notificare nerezolvata: cheia unica devine `(source_key, object_id, expiry_date)` (azi `(template_id, object_id, expiry_date)`). Cu un singur sablon activ pe sursa nu mai apar conflicte de prag, iar pragul se schimba editand acel sablon.
2. **O notificare ramane in lista pana este rezolvata.** Nicio notificare nu mai dispare singura. Azi `EvaluateAsync` cere `instance.Expiry >= today`, iar sincronizarea **sterge** (`DELETE`, `MariaExpiryNotificationRepository`) notificarile care nu mai sunt "dorite": dupa scadenta, la schimbarea datei, la disparitia obiectului. Regula noua, pentru toate sursele (inclusiv ITP, asigurare, rovinieta): sincronizarea doar **creeaza** (cand `zile ramase <= prag`, **fara limita inferioara**, deci si dupa scadenta) si doar **inchide** (marcheaza rezolvate). Singurul loc in care se sterg randuri este curatarea rezolvatelor (6.4). Preluarea si amanarea nu scot notificarea din lista.
3. **Doua tabele in pagina de notificari** (decizie confirmata), in fila "Active": mai intai tabelul **"Scadenta depasita"**, cu notificarile depasite si nerezolvate (cea mai veche scadenta prima), apoi tabelul **"Urmatoarele scadente"**, cu restul notificarilor active in ordine cronologica dupa data scadentei, crescator. Un tabel gol nu se afiseaza (sau arata "Nicio notificare cu scadenta depasita"). Sortarea actuala "cele care avertizeaza primele" dispare: starea (nepreluata / preluata / amanata) se vede prin eticheta si prin randul ingrosat, nu prin pozitie.
4. **Fond rosu discret.** Randul unei notificari cu scadenta depasita si nerezolvata are fundal rosu deschis (o nuanta pastel, cu contrast suficient pentru textul inchis); se aplica indiferent daca notificarea a fost preluata sau amanata. Coloana "Zile ramase" devine "Depasit cu N zile" pentru randurile depasite, iar dialogul arata o linie de stare generata de sistem ("Termenul a fost depasit cu N zile"). In textul sablonului, `<zile ramase>` se randeaza cel putin 0 (nu cifre negative), iar marcajul nou `<zile depasire>` da numarul zilelor de depasire (0 inainte de scadenta).
5. **Stari: "rezolvata" se adauga pe langa "preluata".** *Nepreluata* (avertizeaza), *preluata* (nu mai avertizeaza, "ma ocup de ea"), *amanata* (avertizeaza din nou la sfarsitul perioadei), *rezolvata* (inchisa). O notificare se rezolva:
   - **manual**, cu butonul "Marcheaza ca rezolvata", cand data obiectului nu se schimba (de exemplu o vizita la care se renunta). Jurnal: "Rezolvare notificare";
   - **automat**, de catre sistem, cand cauza ei dispare. **Motivul se scrie explicit, cu parametrul modificat**, ca sa se vada in lista rezolvatelor de ce s-a inchis. Exemple: "Data expirarii ITP s-a modificat de la 15.03.2027 la 15.03.2028"; "Interventie de mentenanta inregistrata la 05.02.2026, scadenta modificata de la 18.01.2026 la 05.05.2026"; "Contractul a fost dezactivat (On → Off)"; "Punctul de lucru a fost scos din contract"; "Vehiculul a fost sters". Jurnal: "Rezolvare automata notificare".
   - Motivul si valorile se determina la evaluare, din diferenta dintre data notificarii si data curenta a obiectului. Cine a facut modificarea si momentul exact sunt in jurnalul operatiei respective; `resolved_utc` este momentul evaluarii (cel mult 5 minute mai tarziu, pentru ca evaluarea ruleaza la interval).
   - Coloane noi in `expiry_notifications`: `resolved_by` (utilizator sau `sistem`), `resolved_utc`, `resolved_reason`, plus **instantaneul** afisabil: `object_label`, `snapshot_subject`, `snapshot_body`, `snapshot_event`. Este necesar deoarece lista se construieste azi live din sursa si ascunde randurile a caror data nu mai corespunde obiectului sau al caror obiect nu mai exista; o notificare rezolvata trebuie sa ramana lizibila oricare ar fi soarta obiectului.
6. **Lista rezolvatelor.** In `/notificari`, doua file: "Active" si "Rezolvate". Rezolvatele arata: titlu, obiect, scadenta, rezolvata la, rezolvata de (utilizator sau sistem) si motivul; cele mai recente primele. Randul ramane in baza, iar cheia unica pe eveniment impiedica recrearea notificarii pentru aceeasi data; pentru o data noua apare, daca e cazul, o notificare noua. **Exceptie: rezolvarea automata este reversibila.** Daca data obiectului revine la valoarea unei notificari rezolvate automat (de exemplu o data corectata inapoi dupa o greseala de tastare), sincronizarea o **redeschide** (sterge marcajul de rezolvare, o readuce ca nepreluata, cu jurnalul "Redeschidere automata notificare"), altfel evenimentul real ar ramane pentru totdeauna fara notificare. Rezolvarea manuala nu se redeschide.
7. **Amanarea.** Numarul de zile se alege in campul existent, **precompletat cu o valoare propusa**. Pentru o notificare nedepasita ramane regula actuala (1 pana la zile ramase minus 2, precompletat cu valoarea de acum). Pentru una depasita, regula "zile ramase minus 2" ar iesi negativa, deci intervalul este 1-30 zile, iar valoarea propusa este 7.
8. **Sabloane.** *Dezactivarea*: azi notificarile unui sablon dezactivat se ascund si reapar la reactivare (30.09.2026); regula "ramane in lista pana e rezolvata" o inlocuieste: dezactivarea opreste doar crearea de notificari noi. *Stergerea* sterge in cascada notificarile lui; daca are notificari nerezolvate, dialogul arata numarul lor si cere confirmare explicita, ca sa nu se piarda din greseala.
9. **Legatura catre obiect.** Instanta poarta un link (pentru mentenanta `/beneficiari/{id}`, pentru vehicule `/vehicule/{id}`), iar dialogul notificarii afiseaza "Deschide obiectul" (azi pagina nu are legaturi catre obiect).

### 6.2 Sursa "Scadenta interventie de mentenanta"

- Cheia `mentenanta.scadenta`, categoria "Mentenanta", evenimentul "Interventie de mentenanta".
- Instantele: cate una pentru fiecare acoperire dintr-un contract activ; `ObjectId` = id-ul acoperirii; `Expiry` = `next_due`; eticheta "Beneficiar · Punct de lucru".
- Marcaje proprii: `<beneficiar>`, `<punct de lucru>`, `<adresa punct de lucru>`, `<numar contract>`, `<data ultima interventie>` (ultima interventie **de mentenanta**; cele la cerere nu se numara).
- Cheia unica existenta `(template_id, object_id, expiry_date)` se potriveste direct: dupa inregistrarea unei interventii de mentenanta, `next_due` se schimba, notificarea veche se rezolva automat (motiv "data modificata") si apare una noua pentru urmatoarea scadenta.
- Interventiile la cerere nu produc si nu inchid aceste notificari.
- Notificarea este per punct de lucru (fiecare are scadenta proprie). Un beneficiar cu multe puncte poate genera multe notificari; gruparea pe beneficiar in lista poate veni ulterior, fara schimbare de model.

### 6.3 Sursa "Expirare contract" (decizie confirmata)

- Cheia `contract.expirare`, categoria "Mentenanta", evenimentul "Expirare contract".
- Instantele: cate una pentru fiecare contract **activ** care are `valid_until`; `ObjectId` = id-ul contractului; `Expiry` = `valid_until`; eticheta "Beneficiar · Contract 26/23.09.2025". Contractele fara termen si cele Off nu produc notificari; trecerea pe Off rezolva automat notificarea la urmatoarea evaluare (motiv "contract dezactivat").
- **Marcaje ce se pot introduce in sablon** (cerinta expresa): `<beneficiar>` (numele beneficiarului), `<numar contract>` (in forma canonica, ex. `26/23.09.2025`) si data expirarii, prin marcajul comun existent `<data expirare>`; in plus `<data contract>` si `<zile ramase>` / `<zile depasire>`.
- Text-exemplu implicit: "Contractul de mentenanta <numar contract> al beneficiarului <beneficiar> expira la data de <data expirare>."
- Se rezolva automat prin prelungirea `valid_until` (schimbarea datei inchide notificarea veche); manual, prin "Marcheaza ca rezolvata". Jurnal: "Modificare expirare contract mentenanta" (regula: operatie numita exact, ca la "Modificare expirare ITP").

Cele doua surse folosesc aceleasi sabloane, aceeasi pagina si acelasi triunghi din meniu ca restul notificarilor; administratorul le defineste in Setari → Notificari → Sabloane notificari, alegand categoria "Mentenanta" si evenimentul.

### 6.4 Setari notificari: curatarea rezolvatelor (decizie confirmata)

Administratorul (doar el, ca la sabloane) stabileste dupa cat timp se sterg notificarile rezolvate. Butonul "Marcheaza ca rezolvata" din lista o poate folosi oricine poate prelua notificari, cu verificare de versiune ca la preluare. Optiunea sta intr-un subtab nou, **"Setari notificari"**, langa "Sabloane notificari" in Setari → Notificari (`Settings.razor` are deja subtaburi; azi exista unul singur).

- **Comutator On/Off** "Sterge notificarile rezolvate mai vechi de", cu **perioada** alaturi (propus in luni, 1-60, implicit 12). Vechimea se masoara de la `resolved_utc`. Se sterg numai notificari rezolvate; cele nerezolvate nu se sterg niciodata.
- **Off** (implicit): notificarile rezolvate se acumuleaza.
- **Off → On**: apare un popup de confirmare care spune cate notificari se vor elimina si data-limita ("vor fi eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy"); se aplica abia dupa confirmare. Daca nu exista nimic de eliminat, comutatorul se activeaza direct.
- **Cu comutatorul On**, curatarea ruleaza singura, cel mult o data pe zi, odata cu evaluarea notificarilor, si sterge ce a trecut de limita. Scurtarea perioadei cat timp comutatorul este On (de exemplu 12 → 3 luni) sterge deodata mai mult si cere aceeasi confirmare ca la activare.
- **Jurnal**: fiecare curatare cu cel putin un rand sters se inscrie cu mentiunea "au fost eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy" (operator: administratorul pentru cea de la activare sau scurtare, `sistem` pentru cele periodice; nimic nu se scrie cand nu s-a sters nimic). Actiuni: "Curatare notificari rezolvate" si "Modificare setari curatare notificari" (comutator sau perioada, cu valoarea veche si noua).
- **Stocare**: tabel mic `notification_settings` (un singur rand: `purge_enabled`, `purge_months`, `last_purge_utc`, `version`), cu concurenta optimista ca restul. `app_metadata` (cheie-valoare) exista, dar nu are versiune si nu este folosit de cod azi.
- **Ce nu se sterge (protectia impotriva reaparitiei):** o notificare rezolvata **manual** pentru un eveniment inca curent (aceeasi data, obiectul exista) este pastrata chiar peste limita; daca ar fi stearsa, sincronizarea ar recrea imediat notificarea, nerezolvata. Se sterg rezolvatele automat si cele manuale ale caror eveniment nu mai este curent (data s-a schimbat sau obiectul a disparut).

## 7. Interfata

Pagina beneficiarului (`/beneficiari/{id}`):

- **Puncte de lucru**: coloane noi "Descriere" (scurta) si "Foto" (numar/miniatura), chip de stare a mentenantei; dialog de editare cu descriere si galerie de poze (incarcare, legenda, stergere). Punctul principal apare ca rand real.
- **Contracte de mentenanta** (sectiune noua): tabel cu numar/data, expirare (cu chipul "Expirat" cand data a trecut), stare On/Off (comutator; **contractele Off sunt ascunse implicit**, iar un comutator "Arata si contractele Off (N)" le face vizibile), ciclicitate, nr. puncte acoperite, urmatoarea scadenta si starea ei. Formular: campul unic pentru numar/data, data de expirare (optionala, selector de data), ciclicitate 1-12, lista punctelor de lucru cu bife (cele libere, plus cele din alt contract activ, marcate si cu optiunea "Muta aici"), o data globala "prima interventie" cu buton "Aplica tuturor" si coloana editabila per punct, ciclicitate individuala optionala (sectiune restransa).
- **Interventii** (sectiune noua, numai cu felul acesta de lucru): butonul "+ Adauga interventie" deschide un dialog cu **selectorul de fel** ("Mentenanta" / "La cerere"), care schimba campurile:
  - *Mentenanta*: punct acoperit de un contract activ, data efectuarii, observatii, si cele trei variante pentru scadenta urmatoare (din data efectuarii / din data planificata / stabilita de operator), fiecare cu data calculata; optional mai multe puncte odata, cu aceeasi data, creand cate un rand per punct (alegerea scadentei se face per punct).
  - *La cerere*: orice punct de lucru al beneficiarului, data, observatii optionale, poze; fara contract si fara scadenta.
  - Sub formular, istoricul interventiilor beneficiarului, cu coloana "Fel", filtru dupa fel si galeria de poze a fiecareia.

Pagina `/notificari` (6.1): file "Active" si "Rezolvate"; in "Active", doua tabele, "Scadenta depasita" primul si "Urmatoarele scadente" dupa el (cronologic), cu fond rosu deschis pentru cele depasite si nerezolvate, coloana "Depasit cu N zile", butonul "Marcheaza ca rezolvata", campul "Amana" cu numarul de zile precompletat (7 la depasire) si legatura catre obiect; in "Rezolvate", motivul rezolvarii cu parametrul modificat. Setari → Notificari primeste subtabul "Setari notificari" (6.4).

Pagina noua `/mentenanta` (intrare in meniu): tab "Scadente" (toate acoperirile active, sortate dupa `next_due`, filtre beneficiar/stare, comutatorul "Arata si contractele Off" care le adauga in gri; interventiile la cerere nu apar aici) si tab "Registru" (toate interventiile, ambele feluri, filtre beneficiar/punct/perioada/fel). Drepturi ca la beneficiari (`CanManageBeneficiariesAsync` pentru modificari, autor = utilizatorul autentificat).

## 8. Jurnal (actiuni numite exact)

Conform regulii permanente, fiecare operatie are actiune proprie in `AuditActions`, in filtrul paginii Audit si in `IsCreateOrEdit`, cu valoare veche/noua in detalii:

- Adaugare contract mentenanta, Modificare contract mentenanta, **Modificare expirare contract mentenanta** (valoarea veche si noua a `valid_until`), Activare contract mentenanta, Dezactivare contract mentenanta, Stergere contract mentenanta;
- Adaugare punct in contract, Scoatere punct din contract, Modificare ciclicitate mentenanta, Reprogramare interventie mentenanta;
- Inregistrare interventie mentenanta (cu varianta aleasa pentru scadenta: din data efectuarii / din data planificata / stabilita de operator, si valorile veche si noua ale scadentei), Modificare interventie mentenanta, Stergere interventie mentenanta;
- Inregistrare interventie la cerere, Modificare interventie la cerere, Stergere interventie la cerere;
- Modificare descriere punct de lucru, Adaugare fotografie punct de lucru, Stergere fotografie punct de lucru, Adaugare fotografie interventie, Stergere fotografie interventie;
- Mutare punct de lucru in alt contract (cand se muta un punct intre doua contracte active);
- Rezolvare notificare (marcarea manuala ca rezolvata, cu starea dinainte si sursa notificarii);
- Redeschidere automata notificare (data obiectului a revenit la valoarea notificarii rezolvate automat);
- Curatare notificari rezolvate ("au fost eliminate din baza de date N notificari rezolvate mai vechi de dd.mm.yyyy"), Modificare setari curatare notificari (comutator sau perioada, cu valoarea veche si noua);
- Modificare coordonate punct de lucru (valoarea veche si noua);
- Rezolvare automata notificare (de catre sistem, cu motivul: data modificata / obiect sters / contract dezactivat).

Observatie: operatiile existente pe puncte de lucru se jurnalizeaza azi cu `RecordEditAsync` (actiunea generica "Editare", entitatea `Beneficiar`); ele ar trebui aduse la aceeasi regula cu ocazia acestei lucrari.

## 9. Impactul asupra codului existent

- `MariaSchemaMigrations`, `schema-mariadb.sql`, `MariaArchiveSchema.MigratedTables` (tabelele noi intra in backup/restaurare), `ChangeEventTriggers.Maria` (actualizare live intre sesiuni).
- `WorkPoints.cs`, `MariaWorkPointRepository`, `MariaBeneficiaryRepository` (punct principal in tranzactie; stergerea beneficiarului blocata de contracte/interventii, la fel ca acum de proiecte).
- `ExpiryNotifications.cs` (conditia `Expiry >= today` din `EvaluateAsync`, `ExpiryTemplateRules.MaxSnoozeDays`, randarea marcajelor, `IExpirySource` cu link), `MariaExpiryNotificationRepository` (migrare pentru `resolved_by`/`resolved_utc`/`resolved_reason`; sincronizarea nu mai face `DELETE`, ci marcheaza rezolvate si nu recreeaza notificarile rezolvate; dialogul de stergere sablon numara notificarile nerezolvate), `NotificationTemplatesEditor.razor`, `Notifications.razor` (sectiunea 6). Testele existente ale notificarilor presupun ca o notificare depasita dispare (`Expiry >= today`); ele se adapteaza la regula noua.
- `ExpiryNotificationService.GetViewsAsync` si `AlertCountAsync` filtreaza azi dupa `template.Active`; cu regula "dezactivarea opreste doar crearea" (6.1, punctul 8) filtrul dispare, iar notificarile unui sablon dezactivat raman in lista si in numaratoare.
- `Settings.razor` (subtabul "Setari notificari"), tabel nou `notification_settings`, curatarea periodica in `ExpiryNotificationService`.
- `Program.cs`: inregistrari DI, endpoint pentru poze, intrare de meniu.
- Teste: unitare (parsarea `26/23.09.2025`, `AddMonths`, cele trei variante de scadenta si conditia "dupa data efectuarii", stari, anulare, interventia la cerere care nu muta scadenta) si de integrare pe `blazorstoc_test` cu doua sesiuni concurente (doua interventii de mentenanta simultane pe acelasi punct, acelasi punct adaugat simultan in doua contracte active, respins de indicele unic; reactivarea unui contract cu puncte intre timp ocupate; stergere punct acoperit sau cu interventii), plus notificari: depasita ramane si e rosie, se rezolva automat la schimbarea datei si manual, nu se recreeaza dupa rezolvare, expirarea contractului cu marcajele `<beneficiar>`, `<numar contract>`, `<data expirare>`.

## 10. Etapizare propusa

1. **(IMPLEMENTAT 30.09.2026) Notificari: depasite, rezolvate, ordonate** (general, independent de restul): o notificare nerezolvata pe eveniment, ramane dupa scadenta, ordinea cu depasitele sus, fond rosu discret, starea "rezolvata" cu instantaneu si motiv, file "Active" / "Rezolvate", amanare cu zile precompletate, dezactivarea sablonului care nu mai ascunde, link catre obiect, `<zile depasire>` (sectiunea 6.1). Se poate face primul; schimba imediat si comportarea ITP/asigurare/rovinieta.
2. **(IMPLEMENTAT 30.09.2026) Setari notificari: curatarea rezolvatelor** (sectiunea 6.4): subtab, comutator, popup, curatare periodica, jurnal. Depinde de 1.
3. **(IMPLEMENTAT 30.09.2026) Puncte de lucru extinse**: punct principal real (+ backfill), descriere, **coordonate optionale cu comutator**, poze (`service_photos` si magazinul de fisiere, refolosite la interventii).
4. **(IMPLEMENTAT 30.09.2026) Contracte si acoperire**: schema (inclusiv `valid_until` si `active_work_point_id`), serviciu, formular, On/Off cu reprogramare si verificarea conflictelor, ciclicitate globala/individuala, "Arata si contractele Off".
5. **(IMPLEMENTAT 30.09.2026) Registru de interventii**: ambele feluri (mentenanta si la cerere), inregistrare cu cele trei variante de scadenta, poze la interventii, pagina `/mentenanta`.
6. **(IMPLEMENTAT 30.09.2026) Surse de notificare pentru mentenanta**: `mentenanta.scadenta` si `contract.expirare`, cu marcajele din 6.2 si 6.3.
7. **Harta de mentenanta** (optional, ulterior): vezi `PROPUNERE_HARTA_MENTENANTA.md`, cu evaluarea de la sfarsitul lui; depinde de 3, 4 si 5 (coordonatele sunt introduse deja la 3).

Dependente: 2 depinde de 1; 4 de 3 (id stabil pentru punctul principal); 5 de 4; 6 de 1 si de 5 (sursa de expirare contract ar putea fi livrata chiar dupa 4); 7 de 3, 4 si 5. Taskul 1 nu depinde de nimic.

## 11. Decizii

Toate confirmate de utilizator la 30.09.2026, in doua serii.

**Seria 1**

1. **CONFIRMAT:** "data primei interventii" este scadenta initiala, nu data de la care se numara ciclul. Daca in realitate se introduce data ultimei vizite deja efectuate, se poate inregistra ca interventie istorica (sectiunea 3.4 permite date din trecut).
2. **CONFIRMAT:** mai multe contracte pe beneficiar; un obiectiv (punct de lucru) nu poate fi legat decat de un singur contract, iar regula se refera **numai la contractele active** (un punct poate sta in mai multe contracte Off). Garantat prin `active_work_point_id` unic (3.3).
3. **CONFIRMAT:** scadenta urmatoare se alege la introducerea efectuarii, dintre: data derivata din data efectuarii, data planificata sau o data stabilita de operator (sectiunea 4.1). Preselectata: din data efectuarii.
4. **CONFIRMAT:** se inregistreaza si interventii **la cerere**, care nu intervin in ciclul de mentenanta; la adaugare se alege felul (mentenanta / la cerere). La cerere: data, obiectivul, observatii optionale, poze.
5. **CONFIRMAT:** "rezolvata" se adauga pe langa "preluata"; o notificare ramane in lista pana este rezolvata, pentru **toate** sursele; cele cu scadenta depasita si nerezolvate apar pe fond rosu discret (6.1). Amanarea unei notificari depasite are numarul de zile la alegere, precompletat cu 7 (maximum 30).
6. **CONFIRMAT:** contractul are data de expirare (`valid_until`, optionala), tratata prin mecanismul de notificari; in sablon se pot introduce numele beneficiarului, numarul contractului si data expirarii (6.3). Un contract expirat dar On continua sa produca scadente si trece prin mecanismul de notificare.
7. **CONFIRMAT:** poze pe disc, ca la produse si proiecte. Riscul pentru backup ramane (sectiunea 12).

**Seria 2**

8. **CONFIRMAT:** cel mult o notificare nerezolvata pe eveniment; notificarile cu scadenta depasita sunt mereu in varful listei, celelalte in ordine cronologica (6.1, punctele 1 si 3).
9. **CONFIRMAT:** la schimbarea datei (ITP reinnoit, interventie inregistrata, contract prelungit) notificarea trece automat in lista rezolvatelor, cu mentionarea modificarii care a dus la rezolvare (6.1, punctul 5).
10. **CONFIRMAT:** curatarea notificarilor rezolvate, in subtabul "Setari notificari", cu comutator, popup la activare si jurnal (6.4).
11. **CONFIRMAT:** punctul de lucru primeste coordonate optionale, cu comutator; un punct se poate introduce si fara coordonate (3.1).
12. **CONFIRMAT:** contractele Off se ascund implicit, dar pot fi facute vizibile (lista de contracte de pe pagina beneficiarului, tabul "Scadente", harta).
13. **CONFIRMAT:** propunerea A.4 din evaluarea hartii (culoare de umplere = scadenta, insigna = contractul).

**Ipoteze de proiectare care decurgeau din deciziile de mai sus, toate confirmate de utilizator la 30.09.2026:**

- **H1. CONFIRMAT:** nu pot exista doua sabloane active in acelasi timp pentru acelasi eveniment (6.1, punctul 1).
- **H2. CONFIRMAT:** pagina de notificari are doua tabele, "Scadenta depasita" primul, apoi restul in ordine cronologica (6.1, punctul 3).
- **H3. CONFIRMAT:** dezactivarea unui sablon nu mai ascunde notificarile lui existente, ci opreste doar crearea altora noi; asta modifica comportarea livrata la 30.09.2026 (6.1, punctul 8).
- **H4. CONFIRMAT:** rezolvarea automata este reversibila daca data revine la valoarea notificarii; cea manuala nu (6.1, punctul 6).
- **H5. CONFIRMAT:** curatarea sterge numai rezolvatele; o notificare rezolvata manual pentru un eveniment inca curent se pastreaza, altfel s-ar recrea nerezolvata (6.4). Perioada se exprima in luni (1-60, implicit 12) si se masoara de la rezolvare; scurtarea ei cere aceeasi confirmare ca activarea.
- **H6. CONFIRMAT:** rezolvarea automata se inscrie la urmatoarea evaluare (cel mult 5 minute dupa modificare), cu valorile veche si noua in motiv; autorul modificarii ramane doar in jurnalul operatiei respective (6.1, punctul 5).

## 12. Riscuri si puncte de tratat la implementare

- **Backup**: pachetul de backup contine numai `dump.sql` si `manifest.json`; pozele de pe disc (si azi fisierele proiectelor si imaginile produselor) **nu** sunt incluse. Restaurarea bazei nu readuce pozele. Alegerea "pe disc" este confirmata, deci riscul este acceptat pentru aceasta lucrare; extinderea backup-ului cu directorul de active ramane o lucrare separata, valabila si pentru produse si proiecte.
- **Fisiere orfane**: stergerea unui punct de lucru sau a unei interventii cu poze trebuie sa stearga si fisierele de pe disc (nu doar randurile), pe tiparul existent al stergerii fisierelor de proiect (`ArchiveRequests.ProjectObservationFile`); la fel la stergerea beneficiarului, care sterge azi direct punctele lui de lucru.
- **Val de notificari la prima evaluare dupa schimbarea din 6.1**: obiectele deja depasite (de exemplu un vehicul cu ITP din trecut) nu au azi notificare; primul ciclu de evaluare le creeaza pe toate, deja rosii si nepreluate. Se verifica in baza reala cate sunt inainte de livrare; sunt reale, nu artefacte, dar pot surprinde.
- **Cheia unica pe eveniment** (`source_key, object_id, expiry_date`): indexul nu se poate crea peste date cu dubluri, iar contul de migrare nu are drepturi pe date. Sistemul de notificari exista abia din 30.09.2026, deci volumul este mic; se verifica la livrare si, daca exista dubluri, se elimina o data inainte de migrare.
- **Rezolvarea automata are intarziere** fata de momentul modificarii (evaluarea ruleaza la interval); autorul si ora exacta sunt in jurnalul operatiei. Alternativa, notificarea imediata din fiecare repository, ar cupla vehiculele, interventiile si contractele de serviciul de notificari si nu a fost aleasa.
- **Adrese goale**: beneficiarii vechi pot avea adresa vida (valoarea implicita a migrarii 1); punctul principal creat pentru ei ar avea adresa vida. De numarat la implementare si de completat.
- **Reactivare fara reprogramare** ar genera un val de notificari depasite; de aceea dialogul din sectiunea 5 este parte din model, nu optiune.
- **Indicele `active_work_point_id`** este o coloana derivata, mentinuta de serviciu; o eroare de cod nu strica datele (esueaza unicitatea), dar poate bloca o activare pe nedrept. Se acopera cu teste: activare/dezactivare, mutare, adaugare simultana din doua sesiuni.
- **Mutarea unui punct intre contracte active** se face ca **actualizare a randului de acoperire** (`contract_id`), nu ca scoatere si readaugare: pastreaza astfel `next_due`, ciclicitatea individuala si identitatea notificarii. Jurnal: "Mutare punct de lucru in alt contract".
- **Contract expirat si inca On**: ramane activ (produce scadente, iar interventiile se pot inregistra) pana este oprit sau prelungit; notificarea de expirare depasita, rosie, este avertismentul, nu o oprire automata (3.2).
- **Interventii la cerere in registru**: nu trebuie confundate cu vizitele de mentenanta la raportari; filtrul "Fel" si coloana dedicata sunt obligatorii in lista si in `/mentenanta`.

## 13. Legatura cu `PROPUNERE_HARTA_MENTENANTA.md`

Harta presupune un registru cu contracte, scadente si interventii; modelul de aici il ofera. Datele pe care harta le mai cere si care nu exista in cerinta de fata: coordonatele punctelor de lucru (acum in taskul "Puncte de lucru extinse", cu comutator, 3.1), tipul lucrarii si responsabilul interventiei. Data de expirare a contractului exista acum (`valid_until`, 3.2). Niciunul nu blocheaza modelul propus; toate se adauga aditiv, prin migrari ulterioare. Evaluarea detaliata a hartii fata de acest model este la sfarsitul documentului hartii.
