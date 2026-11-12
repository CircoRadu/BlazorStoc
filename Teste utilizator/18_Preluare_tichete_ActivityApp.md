# Preluare tichete ActivityApp (Setari -> Preluare ActivityApp)

Fluxul: administratorul introduce contul doar-citire al bazei ActivityApp, testeaza conexiunea (se verifica si structura bazei), citeste lista tipurilor de tichete si alege cele care se leaga de service/mentenanta. Etapa 1: nu se preia nimic inca (beneficiari, puncte de lucru si tichete urmeaza). Ferestrei nu i s-a scris help (decizie: doar la cerere).

Pregatire: ai nevoie de un cont SQL Server cu drept doar de citire pe baza ActivityApp (nu `sa`) si de adresa serverului. Migrarea 044 se aplica la pornirea aplicatiei.

## Modificari de testat


### 05.11.2026 - Tichete discardate: vizualizare tichet; motiv concret la notificarea de validare
**Ce s-a adaugat:** (1) pe fiecare rand din "Tichete discardate" o iconita de vizualizare (langa cea de reactivare); (2) motivul de rezolvare al notificarii "Validează mentenanta" nu mai e generic.
**Ce face acum:** (1) iconita deschide un popup doar pentru citire cu numarul si titlul tichetului, cine l-a discardat si cand, executantul, problema, rezolvarea si raportul original; Inchide sau Escape il inchid. (2) cand coada de validare se goleste, notificarea rezolvata scrie, de ex.: "Coada de validare s-a golit: de la 30.10.2026 14:36 s-au decis 922 evenimente (900 preluate ca mentenanta, 20 preluate ca service la cerere, 2 discardate) de ion; ultima decizie la 05.11.2026 10:12." Daca nu e nicio decizie inregistrata, spune ca evenimentele au fost scoase din coada altfel (reactivare sau resetarea preluarii). Notificarile deja rezolvate pastreaza motivul vechi.
Pasi: Notificari -> Tichete de validat -> Arata tichetele discardate -> iconita de vizualizare pe un rand; decide cateva tichete din coada pana se goleste si deschide notificarea din tabul Rezolvate: motivul numara deciziile.

### 29.10.2026 - Preluare automata a persoanelor fizice si a partenerilor fara tichete, coada unica, comutatoare (0.5.31)

**Ce s-a adaugat:** sincronizarea preia si persoanele fizice si partenerii fara tichete; ce are probleme merge intr-o singura coada (Beneficiari -> De verificat), cu o singura notificare pentru administratori; doua comutatoare in Setari.

**Ce face acum:**
1. Setari -> Preluare ActivityApp -> sectiunea 3: doua comutatoare, ambele pornite implicit: "Preia si beneficiarii fara tichete" si "Import automat al beneficiarilor curati"; "Salveaza" le pastreaza (Jurnal: "Preluare beneficiari fara tichete", "Import automat beneficiari").
2. "Rulează acum" (sectiunea 5) sau sincronizarea programata: partenerii noi din ActivityApp devin beneficiari; firmele (CUI cu cifre) se verifica in ANAF, persoanele fizice (fara CUI, cod personal sau nume in campul CUI) devin persoane fizice dupa nume, fara ANAF. Mesajul sincronizarii spune cati beneficiari noi si cati sunt "de verificat". Persoanele ignorate de versiunile vechi se preiau acum.
3. Fara adresa, ANAF fara raspuns sau CUI necunoscut, CUI sau nume repetat la mai multi parteneri, date refuzate de BlazorStoc: partenerul intra in Beneficiari -> De verificat cu motivul (la repetari si date refuzate, cu detaliul), iar tichetele lui asteapta. Nu mai apare in lista de probleme din Setari.
4. Notificarea (Setari -> Notificari) este una singura: "N beneficiari din ActivityApp de verificat", cu motivele si numarul tichetelor care asteapta; dispare cand coada e goala.
5. Comutatorul "Import automat" oprit: fiecare partener nou intra in coada cu motivul "asteapta acceptarea manuala" (nu se creeaza beneficiar si nici punct de lucru); "Accepta" il creeaza atunci, cu punctele lui, si il scoate din coada; "Discard" ca inainte. "Reincearca ANAF" nu se aplica acestor randuri.
6. Comutatorul "Preia si beneficiarii fara tichete" oprit: se preiau numai clientii tichetelor citite (la fel, automat sau in coada, dupa comutatorul de import automat).

### 23.10.2026 - Fundatia modulului de preluare tichete

**Ce s-a adaugat:** fila Setari -> Preluare ActivityApp, vizibila numai administratorului (permisiunile `setari-import-activityapp.*` nu se acorda altor tipuri de utilizatori). Conexiune (server, port, baza, cont, parola criptata), data de la care se preiau tichetele (implicit 1 ianuarie anul curent), fereastra orara, marimea paginii, optiunea de completare a campurilor goale; butonul „Testeaza conexiunea" (verifica accesul si structura ActivityApp fata de contractul modulului); lista tipurilor de tichete din ActivityApp cu numarul de tichete rezolvate/deschise/sterse, alegerea tipurilor si destinatia fiecaruia (la cerere / de mentenanta). Jurnal: „Modificare setari preluare ActivityApp", „Testare conexiune ActivityApp", „Modificare tipuri tichete preluate ActivityApp".

**Ce face acum:** citeste din ActivityApp numai cu SELECT, pe conexiune scurta, fara blocaje; daca serverul nu raspunde, spune asta fara sa afecteze aplicatia. Contul „sa" este refuzat. Daca contul are drepturi de scriere, testul avertizeaza. Daca structura ActivityApp s-a schimbat (dupa un update), testul spune exact tabela si coloana.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Autentifica-te ca administrator, deschide Setari | Exista fila „Preluare ActivityApp" | OK - Setari -> Preluare ActivityApp se deschide ca administrator (24.10.2026) |
| 2 | Autentifica-te cu un utilizator obisnuit | Fila nu apare in Setari | |
| 3 | Completeaza serverul, contul (nu sa) si parola, apasa Salveaza | „Setarile au fost salvate"; parola nu se mai afiseaza (apare „salvata - lasa gol...") | |
| 4 | Incearca contul `sa` | Mesaj: foloseste un cont dedicat doar pentru citire | |
| 5 | Apasa „Testeaza conexiunea" | „Conexiunea functioneaza (versiune SQL Server). Structura ActivityApp: corespunde." | |
| 6 | Pune o parola gresita temporar si testeaza | Mesaj de autentificare refuzata, fara parola in text | |
| 7 | Opreste serverul ActivityApp sau pune o adresa gresita, testeaza | Mesaj ca serverul nu poate fi contactat; restul aplicatiei merge normal | |
| 8 | Apasa „Citeste tipurile din ActivityApp" | Tabel cu tipuri (cod, nume, categorie, rezolvate/deschise/sterse) | |
| 9 | Bifeaza tipurile Service, Mentenanta, Interventie la cerere; alege destinatia; Salveaza alegerea | „Alegerea tipurilor a fost salvata"; la recitire raman bifate | |
| 10 | Debifeaza un tip si salveaza | Tipul ramane nebifat dupa recitire | |
| 11 | Jurnal activitate | Apar cele trei actiuni exacte; parola nu apare nicaieri | |
| 12 | In SQL Server, redenumeste un tip bifat (doar pe o copie de proba) si citeste din nou tipurile | Tipul apare „schimbat in sursa" si nu mai e bifat pana il bifezi si salvezi | |

### 23.10.2026 - Preluarea beneficiarilor si a punctelor de lucru din ActivityApp (etapa 2)

**Ce s-a adaugat:** in Setari -> Preluare ActivityApp, sectiunea „3. Beneficiari si puncte de lucru": butoanele „Simuleaza importul" si „Aplica importul" (cu confirmare), tabelul cu cifre (de creat / de legat de existente / deja legate / probleme / ignorate), lista problemelor si a elementelor ignorate, lista „Probleme deschise" cu „Marcheaza rezolvata". Migrarea 045. Jurnal: „Simulare import ActivityApp", „Import beneficiari ActivityApp", „Rezolvare problema import ActivityApp".

**Ce face acum:** simularea citeste ActivityApp si baza noastra si nu scrie nimic. Aplicarea creeaza beneficiarii si punctele de lucru prin regulile obisnuite (apar in Jurnal ca „Adaugare beneficiar" / „Adaugare punct de lucru", pe numele administratorului). Un partener cu acelasi CUI (sau aceeasi persoana fizica dupa nume) cu unul existent doar se leaga, fara a-i schimba datele. Nu se preiau (apar ca probleme): CUI invalid sau repetat in ActivityApp, fara adresa, fara telefon valid (BlazorStoc le cere), denumire lipsa. Punctul cu adresa partenerului se leaga de punctul principal; celelalte devin puncte suplimentare (nume = obiectivul, altfel localitate si strada; „sistat"/inactiv ramane ca nota in descriere). A doua aplicare nu mai creeaza nimic. Optiunea „completeaza campurile goale" nu are efect inca: adresa si telefonul sunt obligatorii la noi, deci nu exista campuri goale de completat.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Cu conexiunea salvata si testata, apasa „Simuleaza importul" | Tabel cu cifre si lista problemelor; nu apare niciun beneficiar nou la Beneficiari | |
| 2 | Verifica problemele din lista pe 3-4 parteneri (CUI gresit, fara telefon) in ActivityApp | Motivul e corect pentru fiecare | |
| 3 | Apasa „Aplica importul", apoi „Renunta" | Nu se creeaza nimic | |
| 4 | Apasa „Aplica importul", apoi „Da, aplica" (preluarea trebuie sa fie Activa) | Mesaj cu numarul creat; la Beneficiari apar partenerii noi, cu adresa „strada, localitate, judet" | |
| 5 | Deschide un beneficiar creat | Are punctul principal si, daca ActivityApp avea, puncte suplimentare cu coordonate | |
| 6 | Deschide un beneficiar care exista deja la noi cu acelasi CUI | Datele lui nu s-au schimbat; are in plus punctele suplimentare din ActivityApp | |
| 7 | Apasa din nou Simuleaza, apoi Aplica | Nimic de creat; „deja legate" = cifrele de la pasul 4 | |
| 8 | Completeaza in ActivityApp un telefon lipsa, simuleaza si aplica | Partenerul se creeaza, iar problema lui dispare din lista | |
| 9 | La „Probleme deschise" apasa „Marcheaza rezolvata" | Problema iese din lista; in Jurnal apare „Rezolvare problema import ActivityApp" | |
| 10 | Jurnal activitate | „Simulare import ActivityApp" si „Import beneficiari ActivityApp" cu cifrele | |

### 23.10.2026 - Preluarea tichetelor rezolvate din ActivityApp (etapa 3)

**Ce s-a adaugat:** in Setari -> Preluare ActivityApp, sectiunea „4. Tichete rezolvate": „Simuleaza tichetele" si „Aplica tichetele" (cu confirmare), tabel cu cifre (total / de preluat / deja preluate / probleme / ignorate) si lista problemelor si a tichetelor ignorate. Migrarea 046 (`service_intervention_source`). Jurnal: „Simulare import ActivityApp" (tichete), „Import tichete ActivityApp".

**Ce face acum:** citeste din ActivityApp numai tichetele rezolvate (cu eveniment de rezolvare), nesterse, ale tipurilor alese la sectiunea 2, deschise de la data aleasa (implicit 1 ianuarie anul curent). Fiecare devine o interventie „la cerere" in Mentenanta, la beneficiarul clientului (preluat la sectiunea 3) si la punctul de lucru al tichetului, altfel la cel principal; data = sfarsitul evenimentului. Observatiile contin „Preluat din ActivityApp, tichet ...: titlu", „Problema", „Rezolvare" (partea adaugata la inchidere; daca raportul a fost rescris, tot raportul; daca nu s-a adaugat nimic, nu apare) si „Executant". Problema, rezolvarea, raportul original si executantul se pastreaza separat, intregi, in baza (nu se vad inca in interfata). Tichetele cu clientul nepreluat, orfane sau cu tipul pe destinatia „de mentenanta" se ignora (destinatia „de mentenanta" nu e disponibila inca, ar muta scadentele). Tichetele cu data in viitor sau al caror beneficiar a disparut sunt probleme. A doua aplicare nu mai preia nimic. Interventia apare in Jurnal ca „Inregistrare interventie la cerere" pe numele administratorului. Textele din observatii nu au diacritice (regula aplicatiei).

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Cu beneficiarii preluati si tipurile alese, apasa „Simuleaza tichetele" | Cifre si lista ignoratelor/problemelor; nu apare nicio interventie noua | |
| 2 | Compara numarul „de preluat" cu lista din ActivityApp (tipurile alese, de la data setata, doar inchise) | Cifrele corespund (cele deschise nu intra) | |
| 3 | Apasa „Aplica tichetele", apoi „Renunta" | Nu se preia nimic | |
| 4 | Apasa „Aplica tichetele", apoi „Da, aplica" | Mesaj cu numarul preluat; la fisa unui beneficiar, in Mentenanta, apar interventiile „la cerere" cu data lucrarii | |
| 5 | Deschide o interventie preluata | Observatiile au titlul, Problema, Rezolvare si Executant, fara a repeta problema la rezolvare | |
| 6 | Verifica un tichet la care operatorul a rescris raportul | Rezolvarea este tot raportul | |
| 7 | Verifica un tichet inchis fara text adaugat | Nu are linia „Rezolvare" | |
| 8 | Verifica ca scadentele contractelor nu s-au schimbat | Neschimbate | |
| 9 | Apasa din nou Simuleaza, apoi Aplica | „Deja preluate" = cifra de la pasul 4; nu se dubleaza nimic | |
| 10 | Al doilea tip, cu destinatia „de mentenanta" | Tichetele lui apar ca ignorate, cu explicatia | |
| 11 | Jurnal activitate | „Import tichete ActivityApp" cu cifrele | |

### 23.10.2026 - Sincronizarea periodica, notificarile si elementele sterse in ActivityApp (etapa 4)

**Ce s-a adaugat:** in Setari -> Preluare ActivityApp, sectiunea „5. Sincronizare periodica": comutator Activa/Oprita, intervalul (5-1440 minute), butonul „Ruleaza acum", ultima sincronizare cu rezultatul si cand urmeaza. Migrarea 047. In lista „Probleme deschise" apar si „Partener / Punct de lucru / Tichet sters in ActivityApp". In Notificari apare sursa „Sistem: Preluare ActivityApp cu probleme" (sablonul de pornire se creeaza singur). Jurnal: „Sincronizare ActivityApp".

**Ce face acum:** cat timp preluarea este Activa si sincronizarea Activa, aplicatia verifica singura ActivityApp in fereastra orara din sectiunea 1, la intervalul ales: preia beneficiarii, punctele de lucru si tichetele noi (ca la „Aplica" manual, dar fara utilizator: in Jurnal apare „sistem"), apoi cauta ce s-a sters in ActivityApp dupa preluare. Ce s-a sters acolo doar se semnaleaza (problema deschisa, pana o marchezi rezolvata); la noi nu se sterge si nu se schimba nimic. Daca ActivityApp pare sa fi pierdut dintr-o data toate randurile preluate (25 sau mai multe de un fel), nu se marcheaza nimic (e o indoiala despre sursa), iar rezultatul spune asta. Dupa un esec urmatoarea incercare asteapta de doua ori mai mult (cel mult 4 ore). Sincronizarea apare in Jurnal cand a facut ceva, cand ai cerut-o tu sau cand incepe sa esueze (nu pentru fiecare ora fara noutati). Notificarile (cate una pe situatie care dureaza, pe ziua in care a inceput): sincronizarea esueaza de 3 ori la rand; structura ActivityApp s-a schimbat; probleme de preluare deschise; tipuri de tichete noi sau cu alt nume in ActivityApp. Dispar singure cand situatia s-a rezolvat.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | In sectiunea 5 pune sincronizarea Activa, interval 5 minute, Salveaza | Setarea se salveaza; „Nicio sincronizare inca" | |
| 2 | Apasa „Ruleaza acum" | Mesaj cu cate au fost preluate; apare ultima sincronizare si urmatoarea | |
| 3 | Dupa cateva minute, in ferestra orara, deschide Setari | Ultima sincronizare s-a facut singura | |
| 4 | Jurnal activitate | „Sincronizare ActivityApp"; creatiile automate apar pe „sistem" | |
| 5 | Creeaza in ActivityApp un tichet nou si inchide-l; asteapta sincronizarea | Intervine in Mentenanta fara sa apesi nimic | |
| 6 | Sterge in ActivityApp (pe o copie) un partener deja preluat; asteapta sincronizarea | Apare „Partener sters in ActivityApp" la probleme; beneficiarul de la noi ramane neschimbat | |
| 7 | Marcheaza acea problema rezolvata | Nu mai reapare la sincronizarile urmatoare | |
| 8 | Opreste serverul ActivityApp; asteapta 3 sincronizari | Dupa a treia apare notificarea „Preluare ActivityApp: sincronizarea esueaza"; intervalul dintre incercari creste | |
| 9 | Porneste serverul ActivityApp | Urmatoarea sincronizare reuseste; notificarea se inchide singura | |
| 10 | Setari -> Notificari -> Sistem | Sursa „Preluare ActivityApp cu probleme" are sablonul ei | |
### 23.10.2026 - Corespondenta campurilor (etapa 5)

**Ce s-a adaugat:** in Setari -> Preluare ActivityApp, sectiunea „6. Corespondenta campurilor": tabel cu textele tichetului (titlu, problema, raportul evenimentului, executant, numar), coloana implicita din ActivityApp si o lista cu coloanele de tip text ale aceluiasi tabel; butoanele „Citeste coloanele din ActivityApp", „Salveaza corespondenta" si „Revino la coloanele implicite". Migrarea 048 (coloana `field_map`). Actiune noua in Jurnal: „Modificare corespondenta campuri ActivityApp".

**Ce face acum:** fiecare text al tichetului se citeste din coloana aleasa in loc de cea implicita (doar pentru tichetele care urmeaza sa fie preluate; cele deja preluate raman cum sunt). Se pot alege doar coloane cu text din acelasi tabel. Daca o coloana aleasa dispare din ActivityApp, simularea, aplicarea si sincronizarea se opresc cu mesaj (nu se citeste alt text in loc). Revenirea la coloana implicita sterge alegerea.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Sectiunea 6 -> „Citeste coloanele din ActivityApp" | Apar 5 randuri, fiecare cu coloana implicita selectata si cu coloanele text ale tabelului | |
| 2 | Alege pentru „Problema" o alta coloana text, apoi „Salveaza corespondenta" | Mesaj „Corespondenta campurilor a fost salvata"; la reincarcare alegerea ramane | |
| 3 | Jurnal activitate | „Modificare corespondenta campuri ActivityApp" cu valoarea veche si noua | |
| 4 | Sectiunea 4 -> Simuleaza tichetele (pe un tichet nepreluat) | Textul „Problema" din observatiile propuse vine din coloana aleasa | |
| 5 | Salveaza din nou aceeasi alegere | Nu apare o a doua intrare in Jurnal | |
| 6 | „Revino la coloanele implicite", Salveaza | Alegerea se sterge; simularea foloseste din nou coloanele obisnuite | |
| 7 | (pe o copie a bazei) Alege o coloana, apoi redenumeste-o in ActivityApp si apasa Simuleaza tichetele | Mesaj „Corespondenta campurilor: coloana aleasa ... lipseste"; nu se scrie nimic | |

### 23.10.2026 - Cuvinte cheie de mentenanta, evenimente de validat, programarea sincronizarii (etapa 6)

**Ce s-a adaugat:** (1) Nomenclator -> tabul „Cuvinte cheie mentenanta” (lista cuvintelor: adaugare, modificare, activ/inactiv, stergere, „Incearca un text”); Nomenclatorul are acum doua taburi (Tipuri de sisteme si Cuvinte cheie mentenanta). (2) In Setari -> Preluare ActivityApp: sectiunea „7. Evenimente de validat” (lista cu „Este mentenanta” / „Nu este mentenanta”) si sectiunea 5 cu programare la interval (ore si minute) sau la ore fixe, butonul „Importa acum”. (3) Notificarea „Eveniment ActivityApp de validat ca mentenanta” (Setari -> Notificari -> Sistem, sablon propriu). Migrarea 049 (cuvinte, propuneri, mod si ore de sincronizare). Actiuni noi in Jurnal: „Adaugare/Modificare/Stergere cuvant cheie mentenanta ActivityApp”, „Validare mentenanta ActivityApp”.

**Ce face acum:** cuvintele din Nomenclator se aplica tuturor tipurilor de tichete preluate, mai putin cele cu destinatia „de mentenanta”. Se cauta in ce s-a facut (textul de la inchiderea evenimentului, nu in problema), fara sa conteze literele mari/mici, punctuatia sau diacriticele. Cuvant gasit exact + punctul de lucru sub contract de mentenanta activ: tichetul se inregistreaza ca interventie de mentenanta si muta urmatoarea scadenta (data efectuarii + ciclicitatea; o vizita mai veche decat ultima mentenanta nu o muta inapoi); fara contract ramane la cerere, cu o nota. Cuvant scris incomplet sau gresit (litera lipsa/in plus/schimbata, spatiu in cuvant, cuvant taiat, cifre sau semne in loc de litere, ex. „m3ntenan@a”) si punct sub contract: tichetul NU se preia, apare notificarea si propunerea in sectiunea 7; dupa decizie se preia la urmatoarea sincronizare ca mentenanta sau la cerere si nu se mai propune. Programarea: la interval (ore + minute, in fereastra orara) sau la ore fixe (o ora ratata cat aplicatia era oprita se recupereaza o data); „Importa acum” face o sincronizare imediat.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Nomenclator -> tabul „Cuvinte cheie mentenanta”, adauga „mentenanta” | Apare in lista, activ; acelasi cuvant cu alte litere, cu semne sau fara diacritice este refuzat | |
| 2 | In „Incearca un text” scrie „am facut MENTENANTA anuala”, apoi „am facut m3ntenan@a”, apoi „am schimbat routerul” | „Cuvant gasit”, apoi „Seamana cu ... (cere validare)”, apoi „Niciun cuvant cheie” | |
| 3 | Pe un beneficiar preluat cu contract de mentenanta activ pe punctul principal, inchide in ActivityApp un tichet cu textul „curatat filtre, mentenanta” si apasa „Importa acum” | Apare interventie de MENTENANTA in registru, iar scadenta urmatoare a punctului este data efectuarii + ciclicitatea | |
| 4 | La un punct fara contract, tichet cu acelasi text | Interventie „la cerere”, cu nota „punctul nu are un contract de mentenanta activ” | |
| 5 | Inchide din aplicatia mobila (sau simuleaza) un tichet cu „mentenata” / „m3ntenanta” la un punct cu contract; Importa acum | Tichetul nu se preia; sectiunea 7 il listeaza (ce scrie, ce cuvant seamana); apare notificarea in Notificari | |
| 6 | La el apasa „Este mentenanta”, apoi „Importa acum” | Se preia ca mentenanta si muta scadenta; notificarea dispare; Jurnal: „Validare mentenanta ActivityApp” | |
| 7 | La alt eveniment apasa „Nu este mentenanta”, apoi „Importa acum” | Se preia ca interventie la cerere; nu mai apare propus | |
| 8 | Sectiunea 5: alege „La ore fixe”, scrie „08:00, 13:30”, Salveaza | La fiecare ora fixa se face o sincronizare; „Urmatoarea programata” arata urmatoarea ora | |
| 9 | Sectiunea 5: „La interval regulat”, 1 ora 30 minute | Intervalul se salveaza (minim 5 minute in total) | |
| 10 | Un tip cu destinatia „de mentenanta” si un tichet cu cuvant cheie | Se ignora ca pana acum | |

## Modificari de testat - completare din ANAF si telefon optional (23.10.2026)

**Ce s-a adaugat:** la „Simuleaza importul”/„Aplica importul” (sectiunea 3), firmele cu CUI care nu au adresa in ActivityApp (si cele fara telefon valid) sunt cautate in ANAF; adresa si telefonul gasite completeaza beneficiarul. Persoanele fizice nu se cauta. Telefonul beneficiarului este acum optional (si in formularul Beneficiar).
**Ce face acum:** o firma fara adresa si cu CUI cunoscut de ANAF se preia cu adresa din ANAF (mesajul spune „Completat din ANAF”); un CUI necunoscut sau ANAF oprit ramane problema, cu motivul „(ANAF: ...)”; la ANAF oprit se face o singura cerere, restul nu se mai cer; un telefon incomplet („260681”) nu se ia; firma fara telefon se preia fara telefon. Pe backup-ul real din 10.10.2026: din 357 de parteneri, 171 se creau, 55 fara telefon valid (ANAF a dat adresa pentru toate, telefon utilizabil pentru 8) – acum se preiau si cele 47 fara telefon; raman 131 cu CUI invalid in ActivityApp.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Setari -> Preluare ActivityApp -> sectiunea 3, „Simuleaza importul” cu parteneri firma fara telefon | Nu mai sunt probleme de telefon; beneficiarii se vor crea | |
| 2 | Un partener firma fara adresa in ActivityApp, CUI valid | Raportul spune ca adresa se completeaza din ANAF; la „Aplica importul” beneficiarul are adresa din ANAF | |
| 3 | Un partener cu CUI necunoscut ANAF si fara adresa | Ramane problema, cu „(ANAF: CUI-ul nu este inregistrat ...)” | |
| 4 | Oprește integrarea ANAF (Setari -> Preluare date ANAF), simuleaza | Firmele fara adresa raman probleme cu „(ANAF: ...)”; importul nu se blocheaza | |
| 5 | Beneficiari -> Adauga: lasa telefonul gol | Se salveaza fara telefon; un numar gresit este refuzat | |

## Modificari de testat - fluxul pe tichete: citire incrementala, beneficiari de verificat, verificare umana a mentenantei (23.10.2026, versiunea 0.5.11)

**Ce s-a adaugat:** sincronizarea (programata sau „Importa acum”) citeste doar tichetele inchise noi sau modificate; clientii lor se preiau la nevoie si se verifica in ANAF; beneficiarii care cer o verificare apar in Beneficiari -> De verificat, iar tichetele lor asteapta; orice cuvant cheie de mentenanta la un punct cu contract introduce tichetul imediat ca interventie la cerere si il propune operatorului; operatorul poate si sa il discardeze.
**Ce face acum:**
- Citirea: se retine data celei mai recente modificari vazute la ultima sincronizare reusita; urmatoarea citeste de la o zi inainte. „Reciteste tot” (sectiunea 5) reia citirea completa; nu se dubleaza nimic.
- Beneficiarii: un client nou al unui tichet se preia ca beneficiar daca are CUI valid (verificat in ANAF = „Date ANAF: Da”). Daca ANAF nu raspunde, nu cunoaste CUI-ul sau CUI-ul are cifre dar forma gresita, beneficiarul se preia „de verificat” (Beneficiari -> De verificat) si tichetele lui asteapta. Un CUI cu litere (in afara de RO) sau fara CUI = persoana fizica: nu se preia, nici tichetele lui.
- Mentenanta: cuvant cheie (exact sau aproximativ) la un punct cu contract activ -> interventie la cerere imediata + notificare; „Mentenanta s-a facut” o transforma in mentenanta si muta scadenta; „Nu este mentenanta” o lasa la cerere; „Discard” o sterge, iar tichetul nu se mai preia (Tichete discardate -> reactiveaza).

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Inchide in ActivityApp un tichet al unui client nou (firma cu CUI valid), apoi „Importa acum” | Se creeaza beneficiarul (cu „Date ANAF: Da”) si interventia la cerere; rezumatul spune „Beneficiari: 1 noi” | |
| 2 | Opreste integrarea ANAF (Setari -> Preluare date ANAF) si repeta cu alt client nou | Beneficiarul apare in Beneficiari -> De verificat („ANAF nu a putut raspunde”); tichetul lui nu apare ca interventie (asteapta); notificare „Beneficiar din ActivityApp de verificat” | |
| 3 | Porneste ANAF si la sincronizarea urmatoare asteapta | Beneficiarul devine verificat, iese din lista, tichetul se preia | |
| 4 | In De verificat: „Accepta beneficiarul” pe un beneficiar | Iese din lista; tichetele lui se preiau la urmatoarea sincronizare | |
| 5 | In De verificat: „Discard” (confirma) pe un partener | Iese din lista; tichetele lui nu se mai preiau; Jurnal „Discard partener ActivityApp” | |
| 6 | Tichet cu cuvantul cheie exact la un punct cu contract | Interventie la cerere imediata; in sectiunea 7 apare propunerea; scadenta nu s-a mutat inca | |
| 7 | „Mentenanta s-a facut” pe el | Interventia devine mentenanta, scadenta urmatoare = data + ciclicitatea contractului | |
| 8 | Tichet cu „mentenanta nu s-a putut face” la un punct cu contract: „Discard” (confirma) | Interventia dispare; tichetul apare in „Tichete discardate”; sincronizarea urmatoare nu il mai preia | |
| 9 | „Reactiveaza” tichetul discardat | Se preia din nou la urmatoarea sincronizare si se propune din nou | |
| 10 | Sectiunea 5 -> „Reciteste tot”, apoi „Importa acum” | Se citesc toate tichetele; nu apar duplicate | |
| 11 | Sectiunea 3 -> „Simuleaza importul” pe un ActivityApp cu firme si persoane | Doua sectiuni pliabile: „Cu probleme” (pliata implicit) si „Selectati pentru preluare” (deschisa; firmele „De creat” / „De creat, de verificat”, cele legate de existente, la urma persoanele ignorate) | |
| 12 | Alege si salveaza tipuri de tichete (sectiunea „Tipuri de tichete”), iesi din pagina si reintra | Tipurile salvate apar imediat, bifate, in grupul „folosite”, fara a citi din nou ActivityApp | |
| 13 | Sectiunea 4 -> „Simuleaza tichetele” cu tichete de tipuri alese, unele cu un cuvant cheie in ce s-a facut, altele fara | Doar cele cu cuvant cheie (exact sau aproximativ) se preiau; celelalte apar ca ignorate („nu mentioneaza niciun cuvant cheie”) | |
| 14 | Sectiunea 3 -> simulare cu multi parteneri | Trei sectiuni: Cu probleme (pliata), Selectati pentru preluare (deschisa, toti candidatii, paginata cu „Randuri pe pagina”), Ignorati (pliata, paginata); mesajele operatiei apar si sub textul sectiunii 3 | |
| 15 | In „Ignorati”: „Ca persoana fizica” sau „Ca persoana juridica” pe un partener | Mesaj „neverificat, preluat manual”; partenerul trece in „Selectati pentru preluare”; dupa „Aplica importul” beneficiarul exista, cu „Date ANAF: Nu”; Jurnal „Includere manuala partener ActivityApp”; tichetele lui se preiau dupa „Reciteste tot” | |
| 16 | Sectiunea 4 -> „Simuleaza tichetele” | Patru sectiuni: Cu probleme, In asteptarea clientului, Ignorate (pliate) si Selectate pentru preluare (deschisa), fiecare paginata cu „Randuri pe pagina” | |
| 17 | Orice lista de date (Beneficiari, Setari -> Utilizatori, Module, Backup, Produse etc.) cu peste 10 randuri | Sub tabel apare „Randuri pe pagina” (10/25/50/100/Toate) si butoanele de pagina; schimbarea numarului de randuri reia de la pagina 1 | OK - Produse: Randuri pe pagina 10/25/50/100/Toate, 370 rezultate, 25 randuri dupa alegere; Beneficiari (9 randuri) fara pager, cum e prevazut (24.10.2026) |

## Modificari de testat - corespondenta campurilor la deschidere (25.10.2026, versiunea 0.5.19)

**Ce s-a adaugat:** la deschiderea panoului, sectiunea „6. Corespondenta campurilor” se incarca din ce este salvat.
**Ce face acum:** dupa salvare, la o noua intrare pe pagina tabelul arata imediat coloana aleasa pentru fiecare camp (fara conexiune la ActivityApp); „Citeste coloanele din ActivityApp” aduce lista coloanelor disponibile.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Citeste coloanele, alege o coloana diferita, „Salveaza corespondenta” | Mesaj de salvare | |
| 2 | Paraseste pagina si intra din nou in Setari -> Preluare ActivityApp | Tabelul de la sectiunea 6 este afisat imediat, cu coloana salvata | |
| 3 | „Citeste coloanele din ActivityApp” | Apar listele de coloane; alegerea ramane | |

## Modificari de testat - coada de validare a tichetelor (30.10.2026, versiunile 0.5.34-0.5.36)

**Ce s-a adaugat:** tichetele rezolvate din ActivityApp nu se mai inregistreaza singure: intra in coada „Tichete de validat" (Notificari / Mentenanta). Intra tichetele de un tip de mentenanta si cele din categoriile alese care contin un cuvant cheie din nomenclator; un tichet de service fara cuvant cheie nu se preia. Mentenanta se poate inregistra si la un punct fara contract (fara mutarea scadentei). Operatiile sunt in ferestre (popup), iar tabelul are paginare.
**Ce face acum:** pe fiecare rand: ochi (vizualizare), creion (modifici si preiei), bifa (preiei ca la propunere), cos (discard). Scriptul `tools\reset-activity-tickets.ps1` goleste coada si reia citirea.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Ruleaza importul de tichete | Tichetele cu cuvant cheie sau de tip mentenanta apar in coada; cele de service fara cuvant cheie nu | |
| 2 | Ochi pe un tichet | Popup cu datele tichetului | |
| 3 | Creion: schimba categoria/punctul/data si preia | Intrare in registrul de interventii cu categoria aleasa | |
| 4 | Bifa pe un tichet de mentenanta la un punct fara contract | Mentenanta fara contract, scadenta neschimbata | |
| 5 | Cos pe un tichet | Dupa confirmare tichetul nu se mai preia | |
| 6 | Cu peste 10 tichete in coada | Apare selectorul de randuri pe pagina | |
| 7 | `reset-activity-tickets.ps1` fara `-Apply` | Doar numara; cu `-Apply` goleste coada si reseteaza punctul de citire | |

## Modificari de testat - executanti, GPS, stare Finalizat, taburi in Notificari (04.11.2026, ActivityImport 0.8.0)

**Ce s-a adaugat:** executantul vine din agentii atasati evenimentului; coordonatele GPS ale evenimentului completeaza punctul de lucru; se preiau doar evenimentele in starea Finalizat (culoarea Label = 2); coada de validare si problemele importului sunt in Notificari (taburi), nu in Setari.
**Ce face acum:** vezi pasii; un tichet din coada se actualizeaza cand se schimba raportul sau executantii in ActivityApp; migrarea 61 adauga coloana `gps`.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Tichet finalizat cu doi agenti atasati | Executant = cei doi agenti (nu "ProcesatDe") | |
| 2 | Tichet cu eveniment Alocat / In lucru | Nu intra in coada; reapare dupa Finalizat | |
| 3 | Tichet cu eveniment programat in viitor | Nu mai e problema; daca apare, mesajul explica ce se intampla | |
| 4 | Preia un tichet cu GPS "45,8827287, 22,9065762" la un punct fara coordonate | Punctul primeste 45.882729, 22.906576 (rotunjit la 6 zecimale, cat memoreaza baza); jurnalul "Preluare tichet" arata completarea | |
| 5 | Acelasi, GPS "0, 0" sau punct cu coordonate | Coordonatele punctului raman neschimbate | |
| 6 | Notificari -> "Tichete de validat" | Coada + sectiunea "Tichete discardate" (reactivare) | |
| 7 | Notificari -> "Probleme import" | Lista cu "Marchează rezolvata"; Setari -> Import ActivityApp nu o mai are | |
| 8 | Tichet in coada al carui raport s-a completat dupa prima citire | Dupa urmatorul import apare raportul complet | |

## Modificari de testat - tipurile de tichete se pastreaza si link-ul alertei (04.11.2026, ActivityImport 0.9.0)

**Ce s-a adaugat:** lista tipurilor citite din ActivityApp se pastreaza (migrarea 63); notificarea "Probleme de preluare" duce la tabul "Probleme import" din Notificari.
**Ce face acum:** la deschiderea Setari -> Preluare ActivityApp, "Tipuri nefolosite" contine si tipurile necitite alese; "Deschide obiectul" pe notificare merge in Notificari.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Setari -> Preluare ActivityApp -> "Citeste tipurile din ActivityApp", apoi iesi si reintra in pagina | Tipurile nefolosite apar fara sa mai apesi "Citeste" | |
| 2 | Notificari -> notificarea "Probleme de preluare" -> Deschide obiectul | Se deschide Notificari, tabul "Probleme import" | |

## Modificari de testat - informatiile din tichet (06.11.2026, versiunea 0.5.44)

**Ce s-a adaugat:** Setari -> Preluare ActivityApp, sectiunea 6 "Informatiile din tichet": comutator Pornit/Oprit pe problema, rezolvare, raport original si executant (titlul si numarul sunt mereu preluate), coloana sursa, destinatia (in observatii / camp separat / ambele) si campuri suplimentare (orice coloana text din Tickete sau EvenimenteAgent, cu nume ales).
**Ce face acum:** ce e Oprit nu se mai citeste, nu se mai scrie si nu apare in ferestrele tichetului (validare, discardate, interventie). Cand pornesti o informatie sau adaugi un camp, tichetele deja preluate care nu il au se citesc din nou din ActivityApp si se actualizeaza (copia pastrata, inregistrarea sursa si observatiile); observatiile modificate de un operator raman neatinse.

| Pas | Ce faci | Rezultat asteptat | Rezultat |
|---|---|---|---|
| 1 | Deschide sectiunea 6 | 6 randuri; titlul si numarul au "Mereu", celelalte au comutator | |
| 2 | Opreste "Executantul", salveaza | Dupa salvare un tichet nou nu mai are executant in observatii; fereastra tichetului nu mai arata Executant | |
| 3 | Alege pentru "Raportul original" destinatia "Observatii si camp separat", salveaza | Tichetele noi au "Raport original: ..." in observatii | |
| 4 | "Citeste coloanele din ActivityApp", adauga un camp suplimentar (alege coloana, scrie un nume), salveaza | Mesaj cu numarul de tichete actualizate, de inregistrari sursa rescrise si de observatii actualizate | |
| 5 | Deschide o interventie preluata inainte | In fereastra tichetului apare campul suplimentar cu numele ales; observatiile au linia noua daca nu fusesera modificate | |
| 6 | Modifica manual observatiile unei interventii preluate, apoi adauga alt camp suplimentar | Observatiile modificate manual raman neatinse; mesajul le numara ("modificate de operator") | |
| 7 | Scoate campul suplimentar cu iconita de stergere, salveaza | Campul dispare din ferestre; datele ramase pe tichete nu se sterg | |
| 8 | Journal activitate | Intrari "Modificare corespondenta campuri" si "Actualizare tichete ActivityApp preluate" cu valorile vechi/noi si numerele | |

## Modificari de testat - tichet exemplu, nume editabile, structura tichetelor (06.11.2026, versiunea 0.5.45)

**Ce s-a adaugat:** in Setari -> Preluare date ActivityApp, sectiunea 6: butonul "Deschide un tichet exemplu", numele editabile ale informatiilor, bannerul si notificarea cand structura tichetelor din ActivityApp se schimba.
**Ce face acum:** fereastra arata cel mai recent tichet finalizat cu toate coloanele si valorile lui, ce primeste fiecare informatie si observatiile rezultate; "Tichet anterior" incarca tichetul dinaintea celui afisat. Numele din coloana "Informatie" se pot schimba. La preluare se compara structura tichetelor cu cea verificata; o schimbare da notificarea "Structura tichetelor" pana apesi "Corespondenta verificata".

| Pas | Ce faci | Rezultat asteptat | Rezultat |
|---|---|---|---|
| 1 | Sectiunea 6 -> "Deschide un tichet exemplu" | Se deschide cel mai recent tichet finalizat: tabelul "Ce primeste fiecare informatie", observatiile si doua tabele (Tickete, EvenimenteAgent) cu toate coloanele si valorile | |
| 2 | Apasa "Tichet anterior" de cateva ori, apoi "Tichet mai nou" | Se incarca tichetul dinaintea celui afisat; "Tichet mai nou" revine fara sa citeasca din nou; "Tichet anterior" se dezactiveaza la cel mai vechi | |
| 3 | La o coloana cu text alege "Folosește pentru" -> o informatie | Coloana devine sursa informatiei; previzualizarea se schimba pe loc; salveaza corespondenta ca sa ramana | |
| 4 | La alta coloana alege "Camp suplimentar nou" | In tabelul de campuri suplimentare apare un rand cu numele coloanei (il poti redenumi) | |
| 5 | Schimba numele "Problema" in "Defect", salveaza | In ferestrele tichetului si in observatiile noi apare "Defect"; observatiile vechi raman neatinse | |
| 6 | Journal activitate | "Vizualizare tichet exemplu ActivityApp" pentru fiecare tichet deschis, cu numarul tichetului (fara continut) | |
| 7 | In ActivityApp se adauga/scoate o coloana din Tickete sau EvenimenteAgent, apoi ruleaza o sincronizare | Notificarea "Structura tichetelor" cu lista modificarilor; in sectiunea 6 apare bannerul cu aceeasi lista | |
| 8 | Verifica corespondenta (cu tichetul exemplu), apasa "Corespondenta verificata" | Bannerul si notificarea dispar; Jurnal: "Confirmare structura tichete ActivityApp" | |

## Modificari de testat - preluarea tichetelor separata de Mentenanta si tichet exemplu usor (10.11.2026, ActivityImport 0.14.1, Maintenance 1.11.1, migrarea 84)

**Ce s-a adaugat:** decizia pe un tichet (Mentenanta / La cerere) se pastreaza ca rezultat (tabelul activity_ticket_outcomes) si tichetul iese din coada indiferent daca modulul Mentenanta ruleaza; un worker al Mentenantei inregistreaza interventiile; in Tichet exemplu coloanele au butonul "Alege..." in loc de o lista pe fiecare rand.
**Ce face acum:** cu Mentenanta oprita, tichetele se preiau si se valideaza in continuare, iar scadentele nu se muta; la pornirea Mentenantei interventiile se inregistreaza in ordinea datelor si scadentele urmeaza ultimele tichete; fereastra Tichet exemplu se deruleaza usor si de pe alt calculator.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Setari -> Module: opreste Mentenanta. Mentenanta -> Validare tichete (sau Notificari): valideaza un tichet ca Mentenanta | Tichetul iese din coada; mesajul "N tichete decise asteapta inregistrarea in Mentenanta"; scadenta contractului NU se muta; Jurnal "Preluare tichet ActivityApp ca mentenanta" cu "Inregistrare in Mentenanta: in asteptare" | |
| 2 | Ruleaza o sincronizare ActivityApp | Tichetul validat nu mai este propus din nou | |
| 3 | Porneste Mentenanta si asteapta cel mult un minut | Mesajul dispare; intervenia apare la beneficiar, scadenta contractului se muta cu ciclul fata de data efectuarii; tichetul apare legat de interventie | |
| 4 | Valideaza mai multe tichete cu Mentenanta oprita, date diferite, apoi porneste modulul | Scadenta ramane cea data de cel mai recent tichet | |
| 5 | Cu Mentenanta pornita valideaza un tichet | Interventia apare imediat, ca inainte | |
| 6 | Setari -> Preluare ActivityApp -> Tichet exemplu | Fiecare coloana cu text are butonul "Alege..."; la clic apare lista doar pe acel rand; dupa alegere revine butonul; derularea e fluida si de pe alt calculator | |


## Modificari de testat - corespondenta tichet cu mutare, camp nou, sumar si suspendarea importului (10.11.2026, ActivityImport 0.15.0)

**Ce s-a adaugat:** in Tichet exemplu alegerea se face cu "Tichet - nume" / "Eveniment - nume"; o informatie are o singura coloana si alegerea unei coloane deja asignate o muta; "Camp suplimentar nou" are fereastra proprie (nume repetabil, fara limita de campuri); sumarul din finalul ferestrei arata coloana sursa; importul se suspenda cand structura tichetelor se schimba.
**Ce face acum:** corespondenta se modifica direct din fereastra cu mesaj despre ce s-a mutat; dupa o schimbare in ActivityApp (coloana noua, dispareuta sau cu alt tip) preluarea tichetelor se opreste, apare notificarea cu lista schimbarilor si se reia dupa "Corespondenta verificata".

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Setari -> Preluare ActivityApp -> Deschide un tichet exemplu; la o coloana apasa Alege... | Lista: "Tichet - Titlul tichetului", ..., campurile suplimentare existente, "+ Camp suplimentar nou..."; pentru EvenimenteAgent apare "Eveniment - ..." | |
| 2 | Alege pentru "Numarul tichetului" coloana pe care o citeste titlul | Mesaj: titlul a trecut pe coloana numarului; sumarul de la final arata noile coloane sursa | |
| 3 | La o coloana alege "+ Camp suplimentar nou...", scrie un nume deja folosit | Fereastra cere nume si destinatie; campul se adauga; apar doua campuri cu acelasi nume | |
| 4 | Adauga peste 10 campuri suplimentare | Nu exista limita; lista are paginare | |
| 5 | Alege un camp suplimentar existent pentru alta coloana | Campul se muta pe coloana aleasa, cu mesaj | |
| 6 | Deruleaza la sfarsitul ferestrei | Sumar: informatie, citita din, valoare, merge in; apoi observatiile intervenției | |
| 7 | In ActivityApp (test) adauga o coloana in Tickete, apoi ruleaza sincronizarea | Preluarea tichetelor nu mai preia nimic; Notificari: "Structura tichetelor..." cu coloanele noi; Jurnal: "Suspendare preluare ActivityApp: structura schimbata" o singura data | |
| 8 | Setari -> Preluare ActivityApp -> "Corespondenta verificata", apoi sincronizare | Preluarea se reia, tichetele ramase se preiau in ordinea datelor, notificarea dispare | |

## Modificari de testat - sablonarea preluarii tichetului (11.11.2026, versiunea 0.5.72, BUG-0017)

**Ce s-a adaugat:** fereastra "Tichet exemplu" (Setari -> Preluare ActivityApp, sectiunea 6) are listele pliabile Tichet / Eveniment / Client cu toate coloanele, fiecare cu comutatorul "Folosit" (oprit implicit), lista "Folosite" cu descriere, rol, destinatie si detasare, coloane de orice tip (numar, data, da/nu) ca campuri libere si CUI-ul clientului mereu preluat.
**Ce face acum:** o coloana pornita intra in lista "Folosite"; observatiile tichetului arata "descriere: valoare"; sumarul si observatiile se actualizeaza imediat; titlul, numarul si CUI-ul clientului nu pot fi detasate.

1. Deschide Setari -> Preluare ActivityApp -> sectiunea 6 -> "Deschide tichetul exemplu". Verifica cele trei liste pliabile (Tichet, Eveniment, Client) si ca toate comutatoarele "Folosit" sunt oprite, in afara de titlu, numar si CUI.
2. Porneste "Folosit" la o coloana numerica (ex. IDClient) si la una de data (ex. StartTime): apar in "Folosite" si in observatii ca "IDClient: ...", "StartTime: 02.11.2026 12:30:00".
3. Schimba descrierea in lista "Folosite" (ex. "ID client"): observatiile arata "ID client: valoare".
4. La o coloana text alege alt rol din lista "Rol" (ex. Problema): informatia se muta pe coloana, cu mesaj; "Detaseaza" (cosul) scoate coloana din folosite.
5. Inchide fereastra, "Salveaza corespondenta", reincarca pagina: campurile libere si rolurile s-au pastrat.

## Modificari de testat - titlul si numarul tichetului din orice coloana (11.11.2026, BUG-0019)



**Ce s-a adaugat:** in fereastra "Tichet exemplu", pe randurile "Titlul tichetului" si "Numarul tichetului" din lista "Folosite", coloana din care se citesc se alege dintr-o lista; rolurile "Titlul/Numarul tichetului" apar si in lista "Rol" a coloanelor text.

**Ce face acum:** titlul si numarul se pot citi din orice coloana text a tichetului (nu doar Subject si NumeTicket).



| Pas | Ce faci | Ce trebuie sa vezi | OK |

|---|---|---|---|

| 1 | "Tichet exemplu" -> lista "Folosite", randul "Numarul tichetului" -> alege alta coloana in "Citit din" | Mesajul "Tichet – NumeTicket → ...", valoarea din coloana noua apare in sumar si in observatii; randul ramane fara cos (nedetasabil) | |

| 2 | La "Titlul tichetului" alege coloana folosita acum de numar | Numarul trece pe coloana lasata libera de titlu, cu mesaj | |

| 3 | La o coloana text, in "Rol", alege "Numarul tichetului" | Coloana devine numarul; coloana veche se elibereaza | |

| 4 | Salveaza corespondenta si reincarca pagina | Alegerile pentru titlu si numar s-au pastrat | |

## Modificari de testat - numarul tichetului obligatoriu si fereastra flotanta (12.11.2026, ActivityImport 0.18.0, BUG-0023)

**Ce s-a adaugat:** in fereastra "Tichet exemplu", blocul "Coloane obligatorii" (titlul si numarul tichetului se aleg din orice coloana a tichetului: text, numar sau data), numarul tichetului fara valoare implicita si obligatoriu pentru salvare, fereastra flotanta "Cum arata tichetul preluat".

**Ce face acum:** alegi tu coloana numarului; corespondenta nu se salveaza fara ea. Sumarul si observatiile intervenitiei stau intr-o fereastra separata, care se actualizeaza la fiecare schimbare. Titlul, numarul si CUI-ul nu se mai aleg din lista "Folosite".

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Setari -> Preluare ActivityApp -> "Deschide un tichet exemplu" | Sus apare "Coloane obligatorii" cu Numarul, Titlul si CUI client; in dreapta, fereastra "Cum arata tichetul preluat" | |
| 2 | Daca numarul nu a fost ales: priveste randul "Numarul tichetului" si butonul "Salveaza corespondenta" | "- alege coloana -" si mesajul rosu; butonul este dezactivat si are explicatie | |
| 3 | Alege pentru numar o coloana numerica (de exemplu ID) | Valoarea apare in coloana "Valoare", iar in fereastra flotanta numarul tichetului se schimba pe loc | |
| 4 | Alege pentru titlu alta coloana | Titlul si observatiile din fereastra flotanta se actualizeaza imediat | |
| 5 | Muta fereastra flotanta (trage de bara ei), redimensioneaz-o, apasa "Ascunde", apoi "Arata fereastra ..." din bloc | Fereastra se muta, se redimensioneaza, dispare si reapare | |
| 6 | Inchide fereastra, apasa "Salveaza corespondenta", reincarca pagina | Alegerea numarului si a titlului s-a pastrat, iar butonul de salvare este activ | |
