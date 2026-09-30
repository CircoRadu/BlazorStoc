# Propunere tehnica: harta interventiilor de mentenanta

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
