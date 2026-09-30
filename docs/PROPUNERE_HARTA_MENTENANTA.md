# Propunere tehnica: harta interventiilor de mentenanta

**Stare: IMPLEMENTATA la 30.09.2026** (vezi `IMPLEMENTED.md`), cu evaluarea de la sfarsitul documentului: un singur provider din configuratie (fara fallback automat), harta ca vedere doar-citire peste lista de scadente.

Document de evaluare pentru Claude. Descrie o propunere de implementare, nu o decizie aprobata si nici o implementare functionala. Registrul de contracte si regulile de recurenta sunt presupuse existente.

## Obiectiv si limite

Adaugarea unei pagini de harta care afiseaza punctele de lucru ale beneficiarilor si interventiile de mentenanta asociate contractelor. Harta consuma datele registrului; nu devine sursa de adevar pentru contracte, scadente sau istoricul lucrarilor.

Proiectul are deja beneficiari si puncte de lucru. Propunerea este sa se foloseasca ID-ul punctului de lucru si coordonatele geografice asociate. Punctele fara coordonate nu se geocodeaza implicit la fiecare incarcare; se afiseaza intr-o lista de exceptii pentru completare/corectare.

## Componente

- **Leaflet** gestioneaza vizualizarea, straturile, marker-ele, gruparea marker-elor si selectia locatiei.
- **Providerul de tile-uri** furnizeaza fundalul cartografic separat de Leaflet si de overlay.
- **Blazor** gestioneaza pagina, filtrele, autorizarea, citirea datelor si actiunile asupra interventiilor.
- **JavaScript interop** initializeaza Leaflet si actualizeaza straturile/marker-ele. Regulile de business raman in C#.

## Date pentru afisare

Un serviciu de interogare al registrului furnizeaza un DTO de afisare cu:

- ID punct de lucru, ID beneficiar, denumire si adresa;
- latitudine si longitudine;
- ID/numar contract si datele de inceput si expirare;
- stare contract: activ, aproape de expirare, expirat sau fara termen definit;
- ID interventie, tip lucrare, scadenta si stare;
- responsabil, daca registrul contine aceasta informatie.

DTO-ul este numai pentru afisare. Schimbarile de stare se fac prin serviciile existente ale registrului, cu autorizarea si jurnalizarea lor. Numele beneficiarilor, numerele contractelor si detaliile interventiilor nu se trimit catre providerul cartografic.

Mai multe interventii la aceeasi locatie se grupeaza intr-un singur marker, iar panoul de detalii listeaza interventiile potrivite filtrelor.

## Starea expirarii contractului

Pragul pentru „aproape de expirare” este configurabil, propunere initiala: 30 de zile. Calculul se face folosind data locala a aplicatiei:

- **Expirat**: data expirarii este anterioara datei curente.
- **Aproape de expirare**: data expirarii este intre azi si azi + prag, inclusiv.
- **Activ**: data expirarii este dupa prag.
- **Fara termen definit**: contractul nu are data de expirare; nu se presupune implicit ca este activ.

O interventie viitoare asociata unui contract expirat ramane vizibila, insa se marcheaza separat ca neacoperita de un contract activ. Daca este si restanta, ambele stari se afiseaza.

## Marker-e si panoul cu detalii

Propunere de prioritate vizuala:

1. Contract expirat: rosu inchis.
2. Contract aproape de expirare: portocaliu.
3. Contract activ, interventie restanta/scadenta: rosu.
4. Contract activ, interventie apropiata: galben.
5. Contract activ fara interventie apropiata: verde.
6. Coordonate invalide/lipsa: fara marker; locatie in lista de exceptii.

Culoarea este doar un rezumat. Panoul lateral (drawer pe ecrane mici) afiseaza explicit beneficiar, punct de lucru, adresa, contract, data expirarii, mesajul „Expirat” sau „Expira in N zile”, interventiile relevante si linkuri catre fisele existente.

## Filtre si interactiune

Filtre propuse: intervalul scadentelor, starea contractului, starea interventiei, beneficiar/punct de lucru, tipul lucrarii si responsabilul daca exista. Filtrele actualizeaza marker-ele fara recrearea instantei Leaflet.

Incadrarea automata a tuturor rezultatelor se face la prima incarcare sau la apasarea explicita a butonului „Incadreaza rezultatele”, nu la fiecare schimbare a filtrelor. Marker-ele apropiate se grupeaza la zoom mic; selectarea grupului mareste zona sau afiseaza lista punctelor.

## Provider si fallback

Providerul se configureaza independent de codul marker-elor, ca strat cu URL template, atribuire si zoom minim/maxim. Se pot configura un provider primar si unul de rezerva. Orice cheie API se tine in configuratie privata, nu in cod sursa sau loguri.

Fallback-ul nu se declanseaza la eroarea unei singure dale. Se urmareste esecul mai multor dale vizibile intr-un interval scurt; dupa depasirea unui prag configurabil, se comuta o data la providerul de rezerva si se afiseaza discret sursa activa. Se foloseste un cooldown; nu se alterneaza automat continuu. Revenirea la primar se face la reincarcarea paginii sau prin reincercare controlata.

OpenStreetMap public si OpenFreeMap pot avea infrastructuri distincte, dar folosesc date OSM; fallback-ul intre ele nu acopera problemele comune ale datelor si nici caderea internetului. Niciunul nu trebuie tratat ca avand SLA fara verificarea termenilor actuali. Conditiile de utilizare si atribuirea fiecarui provider trebuie verificate pentru utilizarea interna concreta. Nu se preincarca tile-uri publice OSM si nu se implementeaza descarcare offline prin serviciul public OSM.

## Confidentialitate si siguranta

- Browserul cere doar tile-urile corespunzatoare zonei si zoom-ului vizibil.
- Nu se includ datele beneficiarilor in URL-ul tile-urilor si nu se trimit la furnizor.
- Atribuirea providerului activ ramane vizibila pe harta.
- API-ul punctelor respecta autorizarea existenta.
- Continutul popup-urilor se reda in siguranta, fara a injecta HTML construit din date nevalidate.
- Nu se adauga functii de preincarcare/offline pentru tile-urile OSM publice.

## Separare propusa in proiect

- `Components/Pages/MaintenanceMap.razor`: pagina, filtre, lista de locatii fara coordonate si panoul cu detalii.
- `wwwroot/maintenance-map.js`: initializare Leaflet, tile layers, cluster, selectare si callback catre Blazor.
- Serviciu/DTO in `Services/`: proiectia datelor din registrul existent catre datele hartii.
- Configuratie `Map`: provider primar/rezerva, atribuiri, zoom si pragul pentru fallback; fara secrete in configuratia versionata.
- CSS local: inaltime responsive a hartii, legenda, drawer si stare vizuala a pinurilor.

Modulul JS se initializeaza o data pe durata componentei. Schimbarile de date actualizeaza LayerGroup-ul, nu recreeaza harta. La distrugerea componentei se elimina listener-ele si instanta Leaflet.

## Criterii de acceptare pentru o implementare ulterioara

1. Punctele cu coordonate valide apar fara pregatire manuala a hartilor regionale.
2. Exista un singur marker per locatie, cu interventiile relevante in detalii.
3. Contractele expirate si cele in prag sunt distincte vizual si textual.
4. Starea contractului nu se confunda cu starea interventiei.
5. Filtrele actualizeaza marker-ele fara reinitializarea hartii.
6. Punctele fara coordonate sunt raportate si pot fi deschise pentru corectare.
7. Fallback-ul este limitat, vizibil si nu oscileaza la erori izolate.
8. Atribuirea corecta este vizibila pentru providerul activ.
9. Nu se transmit date personale/contractuale furnizorului de tile-uri.
10. Overlay-ul functioneaza cu orice provider compatibil configurat.

## Puncte pe care Claude sa le evalueze

- Daca registrul existent expune deja datele necesare fara modificari de schema.
- Cum sunt stocate/validate coordonatele punctelor de lucru si cum se corecteaza lipsurile.
- Daca pragul de expirare trebuie global, per contract sau configurabil din Setari.
- Compatibilitatea providerilor candidati cu utilizarea interna, inclusiv termenii, limitele, atribuirea si fallback-ul automat.
- Daca harta trebuie sa afiseze toate interventiile viitoare sau doar un interval implicit.
- Cum se integreaza pagina in navigatia si rolurile existente.
- Costurile operationale/tehnice ale clusterizarii, geocodarii si eventualului provider propriu.

## Evaluare (Claude, 30.09.2026), in contextul `PROPUNERE_CONTRACTE_MENTENANTA.md`

Verdict: directia (harta ca vedere doar-citire peste registru, Leaflet + interop, provider configurabil, DTO de afisare) ramane buna, iar registrul presupus exista acum ca model. Documentul are insa opt nepotriviri cu modelul de contracte stabilit si cateva lucruri de simplificat. Nimic nu blocheaza; harta se face dupa taskurile "Puncte de lucru extinse", "Contracte si acoperire" si "Registru de interventii" ale modelului de contracte.

### A. Ce nu se potriveste cu modelul de contracte

1. **Coordonatele nu exista.** In tot codul nu exista `latitude`/`longitude`; afirmatia "coordonatele geografice asociate" nu este adevarata azi. Se adauga coloane nulabile `latitude`/`longitude` (DECIMAL(9,6)) pe `beneficiary_work_points`, **odata cu taskul "Puncte de lucru extinse"** (decizie confirmata), cu un comutator "Coordonate" in dialogul punctului; un punct de lucru se poate introduce si fara coordonate. Selectorul "alege pe harta" vine odata cu harta. Validare: -90..90 si -180..180, ambele sau niciuna; introducere in dialogul punctului de lucru (camp "lat, long" care accepta lipirea din alta harta, plus "alege pe harta" cand exista Leaflet). Fara geocodare automata in prima etapa (ar trimite adrese ale beneficiarilor unui serviciu extern, contrar propriei reguli de confidentialitate din document). Jurnal: "Modificare coordonate punct de lucru". Dependenta tare: punctul principal era doar derivat (`Id = 0`) si n-ar fi putut avea coordonate; devine rand real prin taskul "Puncte de lucru extinse", deci harta vine dupa el.
2. **"Interventie" are alt sens aici.** Harta vorbeste de interventii viitoare cu scadenta si stare; in model, viitorul nu este un rand: este `next_due` pe acoperire (una singura pentru fiecare punct de lucru, fiindca un punct e in cel mult un contract activ). Randurile din registru sunt interventii **efectuate**. Deci un marker = un punct de lucru cu scadenta lui, iar "mai multe interventii la aceeasi locatie" apare doar cand mai multe puncte de lucru au aceleasi coordonate (aceeasi cladire) sau cand panoul arata istoricul.
3. **Starea contractului are doua axe.** Modelul separa On/Off (comutatorul obligatiilor) de `valid_until` (data de expirare, optionala). Un contract expirat dar On continua sa produca scadente; unul fara termen si On este activ. Regula din document "fara termen definit: nu se presupune ca este activ" se inlocuieste cu: activ = On; `valid_until` da doar chipul "Expirat" / "Expira in N zile". Contractele Off nu se afiseaza implicit (optiune de filtru, in gri).
4. **Doua stari independente nu incap intr-o singura culoare.** Criteriul 4 al documentului ("starea contractului nu se confunda cu starea interventiei") este incalcat de lista de prioritati, in care rosul inseamna si contract expirat, si interventie restanta. Propunere: **culoarea de umplere = starea scadentei** (depasita / in curand / la zi), **insigna sau inel = starea contractului** (expirat / aproape de expirare), plus semn de forma sau pictograma, ca rosu-verde sa nu fie singurul indiciu. Nuantele se iau din aceleasi variabile CSS ca fondul rosu discret al paginii `/notificari`, pentru un singur limbaj vizual.
5. **Pragul "aproape de expirare" nu trebuie sa fie o setare noua.** Raspuns la intrebarea din document: se deriva din sabloanele de notificare (pragul singurului sablon activ al sursei `contract.expirare`, respectiv `mentenanta.scadenta`; implicit 30 zile), aceeasi valoare ca la chipurile din `/mentenanta`. O a doua setare ar produce doua adevaruri.
6. **"Tip lucrare" si "responsabil" nu exista.** Interventiile au doar felul (mentenanta / la cerere), iar `recorded_by` este utilizatorul care a introdus datele, nu tehnicianul. Se elimina din DTO si din filtre; daca se vrea "executant", este un camp optional nou pe interventie, de decis separat.
7. **"Contract expirat: interventie neacoperita" nu se mai aplica.** Expirarea nu opreste acoperirea, doar comutarea Off o face. Cazul devine insigna "Contract expirat" pe un punct inca urmarit.
8. **Interventiile la cerere** nu intra in culoare si nu au scadenta; pot aparea cel mult in istoricul din panoul lateral.

### B. Ce se schimba in proiectare

- **O singura interogare pentru scadente**, folosita de tabul "Scadente" din `/mentenanta`, de sursele de notificare si de harta (un serviciu care returneaza cate un rand pe acoperire activa: beneficiar, punct, adresa, coordonate, contract, `valid_until`, `next_due`, ultima interventie efectuata, starile derivate). Regulile de stare exista astfel intr-un singur loc; DTO-ul hartii este o proiectie a lui.
- **Fara API HTTP nou.** Aplicatia este Blazor Server: componenta apeleaza serviciul (deja autorizat) si trimite datele catre JS prin interop. Nu apare un endpoint public de securizat, spre deosebire de "API-ul punctelor" din document.
- **Actiuni din panou**: link catre `/beneficiari/{id}` si, cum exista deja tiparul `?adauga-proiect=1`, un parametru `?adauga-interventie={punctDeLucru}` care deschide direct dialogul de interventie; operatia trece prin serviciile registrului, cu jurnal.
- **Filtre**: raman intervalul scadentelor, starea scadentei, starea contractului, beneficiar/punct; se adauga "Contract Off" (implicit ascuns). Nu mai este nevoie de un interval implicit al "interventiilor viitoare": exista o singura scadenta pe punct si se afiseaza toate cele active.
- **Leaflet se serveste local** (`wwwroot/lib`), nu de pe CDN. In cod nu exista astazi `Content-Security-Policy` sau `Referrer-Policy` care sa blocheze dale externe; daca se adauga ulterior, providerul de dale intra in `img-src`, iar `Referrer-Policy: no-referrer` ar incalca politica OSM publica (cere Referer valid).

### C. Ce as simplifica

- **Fallback intre doi provideri** (numarare de dale esuate, prag, cooldown, comutare): prea mult pentru o unealta interna cu cel mult cateva sute de puncte si putini utilizatori. Prima etapa: un singur provider din configuratie (URL template, atribuire, zoom minim/maxim), mesaj vizibil la esec si buton de reincercare. Fallback-ul se adauga doar daca apare o problema reala. Configurarea ramane independenta de codul marker-elor, cum cere documentul, deci adaugarea ulterioara nu costa refacere.
- **Clusterizarea** ramane (ieftina si utila cand mai multe puncte au aceeasi adresa).
- Termenii OSM/OpenFreeMap **nu au fost reverificati** in aceasta evaluare (nu depind de modelul de contracte); se verifica la implementare, cum cere si documentul.

### D. Raspunsuri la "Puncte pe care Claude sa le evalueze"

1. **Registrul expune datele fara schimbari de schema?** Aproape: totul vine din modelul de contracte; singura schimbare de schema in plus sunt coordonatele (A.1).
2. **Coordonate: stocare, validare, corectare?** Coloane nulabile pe punctul de lucru, validare de interval, editare in dialogul punctului; lista de exceptii trimite la acelasi dialog (A.1).
3. **Prag: global, per contract sau din Setari?** Derivat din sabloanele de notificare (A.5).
4. **Provideri si fallback?** Un provider in prima etapa; fallback amanat (C).
5. **Toate interventiile viitoare sau un interval?** O singura scadenta pe punct; se afiseaza toate cele active, cu filtre (B).
6. **Navigatie si roluri?** Intrare in meniul "Mentenanta", langa `/mentenanta`, cu aceleasi drepturi de acces ca acea pagina.
7. **Costuri (clusterizare, geocodare, provider propriu)?** Clusterizarea este neglijabila la aceasta scara; geocodarea automata se evita (confidentialitate si dependenta de un serviciu extern); provider propriu nu se justifica.

### E. Criterii de acceptare revizuite

Criteriile 3, 4 si 6 se citesc astfel: (3) contractele expirate si cele in prag se deosebesc prin insigna, nu prin culoarea de umplere; (4) culoarea de umplere arata numai scadenta, insigna numai contractul; (6) punctele fara coordonate apar in lista de exceptii, cu legatura catre dialogul de editare a punctului. Criteriile 1, 2, 5, 7-10 raman, cu 7 valabil doar daca se implementeaza fallback-ul.

### F. Locul in plan

Harta este ultimul task (al saptelea in planul din `PROPUNERE_CONTRACTE_MENTENANTA.md`, sectiunea 10): depinde de "Puncte de lucru extinse" (coordonate optionale pe punctul principal real), de "Contracte si acoperire" si "Registru de interventii" (scadentele) si foloseste pragurile sabloanelor de la "Surse de notificare pentru mentenanta" (fara acestea, implicit 30 zile). Nu blocheaza si nu este blocata de taskul 1 (notificari depasite si rezolvate), in afara de folosirea acelorasi culori.

### G. Confirmari ale utilizatorului (30.09.2026)

- **Propunerea A.4 acceptata:** culoarea de umplere a marker-ului arata starea scadentei, insigna sau inelul arata starea contractului.
- **Contract expirat si On** continua sa produca scadente si trece prin mecanismul de notificari (sursa `contract.expirare`); pe harta apare ca insigna "Expirat", nu ca oprire a urmaririi punctului.
- **Contractele Off se ascund implicit, dar pot fi facute vizibile** (comutator "Arata si contractele Off", in gri), la fel ca in lista de contracte si in tabul "Scadente".
- **Coordonatele** se introduc odata cu punctul de lucru, prin comutator; punctele fara coordonate raman valide si apar in lista de exceptii a hartii.
