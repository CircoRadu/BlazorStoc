# Propunere tehnica: harti Google cu contor de consum si trecere automata pe harta de rezerva

BlazorStoc - propunere pentru discutie - 06.11.2026 - stare: **nimic implementat**

> **Avertisment.** Preturile, pragurile gratuite, regulile de facturare si conditiile de utilizare Google din acest document sunt scrise din cunostinte generale si **nu au fost verificate** pe paginile Google la data redactarii. Sumele din tabele sunt ipoteze de calcul, nu oferte. Inainte de decizie se verifica pagina de preturi Google Maps Platform, conditiile Map Tiles API si consola Google Cloud.

## 1. Rezumat

- Firma are un cont Google si o cheie API proprie pentru **Map Tiles API** (dale de harta care se pot folosi cu Leaflet, biblioteca hartii din aplicatie).
- **Toate dalele trec prin serverul aplicatiei**, nu direct din browser. Serverul numara cererile, ascunde cheia de browser si poate opri consumul.
- Administratorul seteaza in Setari -> Harta o **limita lunara de cereri** (si optional zilnica) si un prag de avertizare. La limita, motorul de harti trece **automat pe Leaflet cu OpenStreetMap** (harta de rezerva), iar trecerea se vede si se jurnalizeaza.
- Un card **"Consum harti"** arata consumul in aplicatie, cu cost estimat din preturi introduse de administrator.
- Securitatea e gandita pentru accesul **prin VPN WireGuard** si **HTTPS prin nginx**: cheia nu ajunge niciodata in browser, punctul de acces la dale e autentificat si limitat, iar nginx nu pastreaza dalele.
- Costul maxim posibil e plafonat in **trei straturi**: limita din aplicatie, cota zilnica din Google Cloud si bugetul cu alerte (plus, optional, un proiect Google dedicat).

## 2. Situatia actuala si obiective

**Acum.** Harta din Mentenanta foloseste Leaflet cu un singur motor configurabil (adresa de dale cu `{z}/{x}/{y}` si, optional, `{key}`), plus un strat satelit optional. Browserul cere dalele **direct** de la furnizor, deci serverul nu poate numara cererile, iar cheia apare in cererile browserului.

**Obiective.**
1. Cheie API pe firma, folosita doar de aceasta aplicatie.
2. Consum vizibil in aplicatie.
3. Prag configurabil; la prag, trecere automata pe Leaflet/OpenStreetMap, reversibila.
4. Costul maxim cunoscut dinainte si plafonat.
5. Functionare sigura in scenariul VPN WireGuard + HTTPS nginx.

## 3. Arhitectura propusa

```
Utilizator (telefon / PC)
   |  tunel WireGuard (UDP, o cheie pentru fiecare utilizator)
   v
nginx  (TLS 1.2/1.3, https://harta.firma.local)
   |-- /            --->  aplicatia BlazorStoc (HTTP, doar pe 127.0.0.1)
   |-- /harta/dale/ --->  aplicatia: verifica sesiunea, numara, aplica limita
                              |  sesiune Google + cheie (criptata in aplicatie)
                              v
                          tile.googleapis.com (HTTPS)          [motor principal]
                          sau tile.openstreetmap.org (HTTPS)   [harta de rezerva]
```

| Componenta | Rol |
|---|---|
| Motor principal "Google Map Tiles" | Creeaza sesiunea Google, reinnoieste sesiunea singur, aduce dala |
| Punct de acces la dale `/harta/dale/{z}/{x}/{y}` | Numai pentru utilizatori autentificati; valideaza parametrii; numara; aplica limita; nu pastreaza dalele |
| Contor de consum | Cereri pe zi (si pe utilizator), erori, octeti; scris in baza de date |
| Limite si stare | Limita lunara/zilnica, prag de avertizare, stare: Google / Avertizare / Rezerva |
| Harta de rezerva | Motorul de acum (OpenStreetMap), prin acelasi punct de acces |
| Card "Consum harti" | Consum, bara de progres, cost estimat, istoric pe zile |

## 4. Securitate aplicatie (VPN WireGuard + HTTPS nginx)

### 4.1 Ipoteze de topologie

- Utilizatorii se conecteaza prin **WireGuard** (adresa VPN, de exemplu `10.8.0.0/24`), apoi deschid aplicatia la `https://harta.firma.local`.
- **nginx** termina TLS si transmite catre aplicatie pe `127.0.0.1`. Aplicatia nu e expusa direct nicaieri.
- Aplicatia are activat "HTTPS prin proxy" (Setari -> Baza de date -> Conexiune), cu adresa nginx in lista proxy-urilor de incredere.
- Serverul are iesire catre internet doar spre Google (si OpenStreetMap, pentru rezerva).

### 4.2 Model de amenintari

| Amenintare | Impact | Masuri |
|---|---|---|
| Cheia Google ajunge in browser sau in loguri | Folosire de catre altcineva, costuri | Cheia ramane pe server, criptata (Data Protection); browserul vede doar adresa aplicatiei. Jurnalul si logurile nu contin cheia; clientul HTTP al aplicatiei nu logheaza adresele cu `key=` (nivel de log ridicat sau redactare) |
| Utilizator autentificat consuma masiv dale (script, cont compromis) | Consum si cost | Limita pe utilizator si globala; plafonul lunar din aplicatie; cota zilnica din Google; contor pe utilizator pentru depistare |
| Configuratie WireGuard furata (telefon pierdut) | Acces la aplicatie | Un peer pe utilizator, revocabil; autentificare cu utilizator, parola si cod 2FA (vezi 4.7); sesiune de maximum 8 ore |
| Adresa dalei folosita ca punct de atac (SSRF) | Cereri catre alte tinte | Clientul nu trimite adresa; `z`, `x`, `y` sunt numere validate pe domeniu; gazda amonte e fixa; redirecturile nu se urmeaza |
| Pastrarea dalelor in cache (nginx, aplicatie) | Incalcarea conditiilor Google | Fara cache persistent al dalelor; se transmit doar antetele de cache primite, ca `private` |
| Scurgerea token-ului de sesiune Google | Folosire pe contul firmei, limitata in timp | Token-ul ramane in memorie pe server, nu ajunge in browser sau in jurnal |
| nginx configurat gresit (fara limitare, expus pe LAN) | Abuz sau expunere | Sablon nginx din anexa; asculta numai pe adresa WireGuard; limitare de cereri; verificare in lista de la 4.9 |
| Blocarea punctului de acces la dale (DoS) | Harta nu se incarca | Limita de cereri pe peer in nginx si pe utilizator in aplicatie; timeout-uri scurte spre Google |
| Google indisponibil sau lent | Harta goala | Dupa erori repetate, trecere temporara pe harta de rezerva; indicator pe harta |
| Adresa gresita a clientului in spatele nginx | Decizii intern/extern gresite | `X-Forwarded-For` se **suprascrie** in nginx cu `$remote_addr`, nu se adauga; aplicatia crede doar proxy-ul listat |

### 4.3 Cheia Google

- Salvata **criptat** in baza de date, ca celelalte chei si parole ale aplicatiei; nu se afiseaza dupa salvare, nu apare in jurnal.
- Restrictii in Google: **numai Map Tiles API**; restrictie de aplicatie pe **adresa IP publica a serverului**, pentru ca serverul face cererile (daca IP-ul public se schimba, doar restrictia pe API).
- O **cheie separata** pentru aceasta aplicatie, intr-un proiect dedicat (vezi 6.5).
- Procedura de rotatie: se creeaza cheia noua, se pune in aplicatie, se verifica harta, se sterge cheia veche din Google. Fiecare pas se jurnalizeaza fara valoarea cheii.
- Backup: baza de date contine cheia criptata; restaurarea pe alt server cere si cheile Data Protection din `keys/`.

### 4.4 Punctul de acces la dale

- Cere **sesiune autentificata** si permisiunea modulului Harti; fara ele, raspuns 401/403.
- Validare: `z` intre zoom-ul minim si maxim configurat; `x` si `y` intre 0 si `2^z - 1`; altfel 400.
- Limita de cereri **pe utilizator** (de exemplu 600 de dale pe minut) si **globala**; peste limita, raspuns 429. Valorile se pot configura.
- Cereri catre Google cu timeout scurt (5 s), dimensiune maxima a raspunsului si verificarea tipului de continut (`image/*`).
- Raspuns cu `Cache-Control: private`, cu durata primita de la Google, ca browserul sa-si poata pastra dalele conform antetelor. Asta reduce numarul de cereri, fara pastrare pe server.
- Antete: `X-Content-Type-Options: nosniff`. Nu se trimit catre browser antetele Google.
- Nicio dala nu se jurnalizeaza individual; se pastreaza doar contoare pe zi si pe utilizator.

### 4.5 nginx

- Asculta **numai** pe adresa serverului din reteaua WireGuard (de exemplu `10.8.0.1:443`), nu pe `0.0.0.0`.
- TLS 1.2/1.3, HSTS, certificat al unei autoritati interne sau obtinut prin DNS-01 pentru un nume intern (fara expunere publica).
- Ruta separata `/harta/dale/` cu `limit_req` si **fara** `proxy_cache`.
- Pentru Blazor Server: HTTP/1.1, antetele `Upgrade`/`Connection`, `proxy_read_timeout` generos pe ruta principala (WebSocket).
- Sablon complet in anexa A.

### 4.6 WireGuard

- Retea dedicata, de exemplu `10.8.0.0/24`; **un peer pe utilizator** (nu o configuratie partajata), `AllowedIPs = <adresa>/32` pe server.
- Pe clienti: `AllowedIPs` doar spre adresa aplicatiei (sau spre reteaua interna necesara), **fara** tunel complet catre internet; `PersistentKeepalive = 25` pentru telefoane.
- Firewall pe server: din interfata WireGuard se permite doar TCP 443 catre nginx; portul aplicatiei asculta doar pe loopback.
- Revocare: se sterge peer-ul; evidenta perechilor (nume, data, telefon) intr-un loc controlat de administrator.
- Optional `PresharedKey` pe fiecare peer, ca strat suplimentar.

### 4.7 Legatura cu mecanismele de acces ale aplicatiei

- **Adresa clientului.** In spatele nginx, aplicatia vede adresa din `X-Forwarded-For`, adica adresa VPN a utilizatorului (`10.8.0.x`).
- **Atentie la modul 2FA "Doar din exterior".** Lista implicita a retelei interne include `10.0.0.0/8`, deci clientii VPN ar fi tratati ca **interni** si ar intra fara cod. Solutia recomandata: modul **"Pentru toti"**, sau o lista a retelei interne care exclude subreteaua WireGuard, ca VPN-ul sa cada la "exterior".
- **Conturile de administrator.** Modificarile autentificatorului lor se fac numai din browserul de pe server, direct pe `127.0.0.1` (fara nginx). Aplicatia permite asta chiar cu "HTTPS prin proxy", pentru ca loopback-ul nu e blocat.
- **Sesiune.** Cookie fara persistenta, maximum 8 ore, `Secure` prin HTTPS.
- **Parole.** Politica de parola si schimbarea la prima autentificare se aplica la fel.

### 4.8 Harta de rezerva si browserul

- Daca browserul ar cere dale direct de la OpenStreetMap, un telefon cu tunel "split" trebuie sa aiba internet propriu si reguli CSP care permit acea gazda. De aceea propun ca **si harta de rezerva sa treaca prin acelasi punct de acces** (cu `User-Agent` care identifica aplicatia, conform politicii OpenStreetMap), asa incat CSP ramane `img-src 'self' data: blob:` si browserul nu contacteaza terti.
- Consumul OpenStreetMap se numara separat; nu are cost, dar politica lor cere utilizare rezonabila.

### 4.9 Jurnal, monitorizare si lista de verificare

Operatii jurnalizate cu denumire exacta: modificarea motorului Google, inlocuirea sau stergerea cheii (fara valoare), modificarea limitelor, trecerea automata la harta de rezerva, reluarea hartii Google, fortarea manuala a rezervei.

Lista de verificare la punerea in functiune:
1. Aplicatia asculta numai pe `127.0.0.1`; nu se ajunge la ea de pe LAN fara nginx.
2. nginx asculta numai pe adresa WireGuard; certificatul e valid pentru numele folosit.
3. "HTTPS prin proxy" activ; clientul VPN apare in Jurnal cu adresa `10.8.0.x`, nu cu adresa nginx.
4. Cheia Google restrictionata la Map Tiles API si la IP-ul serverului; cheia nu apare in logurile aplicatiei si nu apare in cererile browserului.
5. Limita din aplicatie si cota din Google sunt setate; bugetul cu alerte e activ.
6. Testul de trecere pe rezerva a reusit (limita mica, temporara).
7. Modul 2FA ales conform 4.7.

## 5. Partea financiara si costurile posibile

### 5.1 Cum se factureaza (ipoteze, de verificat)

- Map Tiles API se factureaza **pe cerere de dala** (cu praguri lunare gratuite, care s-au schimbat in timp). **Facturarea trebuie activata** in proiect chiar daca consumul ramane in zona gratuita.
- O deschidere de harta cere in jur de **20-40 de dale**; mutarea hartii si schimbarea zoom-ului cer altele. Browserul pastreaza o parte din dale, ceea ce reduce numarul.
- Costul lunar estimat = `max(0, cereri - prag_gratuit) x pret_la_1000 / 1000`.

### 5.2 Scenarii de consum (ipoteze de lucru)

Ipoteze: 22 de zile lucratoare pe luna; 30 de dale la o deschidere de harta (40 in scenariul mare).

| Scenariu | Utilizatori | Deschideri/zi | Cereri pe luna |
|---|---|---|---|
| S1 mic | 5 | 10 | 33.000 |
| S2 mediu | 20 | 20 | 264.000 |
| S3 mare | 50 | 40 | 1.760.000 |
| Abuz: un script, 10 dale/secunda, o luna | 1 | - | 25.920.000 |

### 5.3 Costul in functie de pretul la 1000 de cereri (USD, ipotetic)

Pretul real **nu e verificat**; coloanele arata sensibilitatea. "Prag gratuit" este tot ipotetic.

| Scenariu | Prag gratuit | 0,5 USD/1000 | 2 USD/1000 | 7 USD/1000 |
|---|---|---|---|---|
| S1 | 0 | 17 | 66 | 231 |
| S1 | 100.000 | 0 | 0 | 0 |
| S2 | 0 | 132 | 528 | 1.848 |
| S2 | 100.000 | 82 | 328 | 1.148 |
| S3 | 0 | 880 | 3.520 | 12.320 |
| S3 | 100.000 | 830 | 3.320 | 11.620 |
| Abuz, fara plafon | 0 | 12.960 | 51.840 | 181.440 |
| Abuz, fara plafon | 100.000 | 12.910 | 51.640 | 180.740 |

Concluzie: un consum normal e mic, dar **un abuz sau o greseala fara plafon poate costa mii sau zeci de mii de dolari**. De aceea plafonul pe mai multe straturi e esential.

### 5.4 Expunerea maxima cu limita din aplicatie

Cost maxim posibil lunar daca limita lunara din aplicatie este L (USD, ipotetic):

| Limita lunara L | Prag gratuit | 0,5 USD/1000 | 2 USD/1000 | 7 USD/1000 |
|---|---|---|---|---|
| 100.000 | 0 | 50 | 200 | 700 |
| 100.000 | 100.000 | 0 | 0 | 0 |
| 250.000 | 0 | 125 | 500 | 1.750 |
| 250.000 | 100.000 | 75 | 300 | 1.050 |
| 500.000 | 0 | 250 | 1.000 | 3.500 |
| 500.000 | 100.000 | 200 | 800 | 2.800 |
| 1.000.000 | 0 | 500 | 2.000 | 7.000 |
| 1.000.000 | 100.000 | 450 | 1.800 | 6.300 |

Limita din aplicatie opreste doar consumul care trece **prin aplicatie**. Cota din Google (5.5) acopera si restul.

### 5.5 Straturile de control al costului

| Strat | Unde | Ce face | Opreste consumul? |
|---|---|---|---|
| 1. Limita lunara/zilnica si prag | Aplicatie | Avertizeaza la prag, trece pe rezerva la limita | Da, pentru ce trece prin aplicatie |
| 2. Cota pe zi si pe minut pentru Map Tiles API | Google Cloud, Quotas | Google refuza cererile peste cota | Da, indiferent de sursa |
| 3. Buget cu alerte (50%, 90%, 100%) | Google Cloud Billing | Trimite e-mail | **Nu**, doar alerta |
| 4. Oprire automata la depasire | Google Cloud (Pub/Sub si o functie) | Dezactiveaza facturarea proiectului | Da, dar opreste **toate** serviciile cu facturare ale proiectului; de aceea proiect dedicat |
| 5. Proiect Google dedicat | Google Cloud | Costurile Maps nu se amesteca cu alte servicii | - |
| 6. Cardul de plata | Banca | Plafon sau card virtual cu limita | Depinde de banca |
| 7. Verificare lunara | Procedura | Compararea contorului din aplicatie cu raportul Google | - |

Recomandare: straturile **1, 2, 3 si 5** obligatoriu; 4 si 6 la decizia firmei.

### 5.6 Alte costuri

- **Dezvoltare (estimare):** etapa A 4-6 zile-om, etapa B 1 zi, etapa C 2 zile.
- **Operare:** nu apar costuri de infrastructura noi; traficul dalelor trece prin serverul existent (cativa KB per dala).
- **Cloud Monitoring (etapa C):** apelurile de citire a consumului sunt de regula in zona gratuita; de verificat la implementare.

## 6. Configurarea in Google Cloud (pasi)

> Meniurile si denumirile se pot schimba; pasii sunt orientativi.

1. **Cont si organizatie.** Intra cu contul Google al firmei (de preferinta Google Workspace) in Google Cloud Console. Intr-un Workspace, administratorul poate trebui sa-ti permita crearea de proiecte si legarea facturarii.
2. **Proiect dedicat.** Creeaza un proiect nou, de exemplu `blazorstoc-harti`. Da drepturi doar persoanelor necesare (rol **Owner** limitat la 1-2 persoane).
3. **Facturare.** Leaga proiectul la contul de facturare al firmei (card sau factura). Noteaza cine primeste e-mailurile de facturare.
4. **Activeaza API-ul.** In *APIs & Services -> Library* activeaza **Map Tiles API**.
5. **Creeaza cheia.** *APIs & Services -> Credentials -> Create credentials -> API key*. Redenumeste-o `blazorstoc-server`.
6. **Restrictii pe cheie.** *API restrictions*: doar **Map Tiles API**. *Application restrictions*: **IP addresses**, cu IP-ul public al serverului (daca IP-ul nu e fix, lasa doar restrictia pe API si compenseaza cu cote mai stranse).
7. **Cote.** *IAM & Admin -> Quotas* (sau *APIs & Services -> Map Tiles API -> Quotas*): seteaza cererile pe zi si pe minut. Regula: cota zilnica = (limita lunara din aplicatie / 30) x 1,3, ca aplicatia sa ajunga prima la limita.
8. **Buget si alerte.** *Billing -> Budgets & alerts*: buget lunar al proiectului, alerte la 50%, 90% si 100% catre 2-3 adrese de e-mail.
9. **Oprire automata (optional).** Buget cu notificare Pub/Sub si o functie care dezactiveaza facturarea proiectului dedicat. Se testeaza pe un buget mic inainte de productie.
10. **Cont de serviciu (doar etapa C).** Cont de serviciu cu rolul **Monitoring Viewer**, o singura cheie JSON, pastrata criptat in aplicatie.
11. **Conditii de utilizare.** Verifica obligatiile de atribuire (sigla si textul Google pe harta) si interdictiile (pastrarea dalelor, folosirea in afara hartii). Aplicatia va afisa atribuirea.
12. **Test.** Cu cheia noua, de pe server:

```
curl -s -X POST "https://tile.googleapis.com/v1/createSession?key=CHEIA" \
  -H "Content-Type: application/json" \
  -d '{"mapType":"roadmap","language":"ro-RO","region":"RO"}'
curl -s -o dala.png "https://tile.googleapis.com/v1/2dtiles/10/580/354?session=TOKEN&key=CHEIA"
```

## 7. Proiectare tehnica

### 7.1 Setari

Setari -> Harta primeste doua zone: **Motor principal** (Google Map Tiles: cheie, tip harta — drum / satelit, limba, regiune) si **Motor de rezerva** (motorul de acum, nemodificat). Un card nou **Limite si consum**: limita lunara de cereri, limita zilnica optionala, prag de avertizare (%), pret la 1000 de cereri, prag gratuit lunar, moneda.

### 7.2 Date (migrarea 73)

| Tabel | Continut |
|---|---|
| `map_tile_usage` | zi, furnizor, cereri, reusite, erori, octeti (cheie: zi + furnizor) |
| `map_tile_usage_user` | zi, utilizator, cereri (pentru depistarea abuzului) |
| `map_engine_state` | stare (Google / Avertizare / Rezerva), de cand, motiv, cine a schimbat |

Cheia Google se pastreaza criptat, in setarile hartii.

### 7.3 Stari si trecerea pe rezerva

- **Google activ** -> **Avertizare** (consum >= prag%) -> **Rezerva** (consum >= limita, minus o marja pentru cererile deja in zbor).
- Revenire: automat la inceputul lunii, sau manual cu "Reia Google" (permis doar daca limita a fost marita sau consumul e sub ea). Mod manual "Forteaza rezerva".
- Decizia se ia la **fiecare cerere de dala**, cu un contor atomic in memorie, scris in baza de date la cateva secunde; la repornire contorul se reincarca din baza de date.
- Pe o harta deschisa, un raspuns 429 cu antet specific face ca Leaflet sa schimbe stratul pe cel de rezerva, fara reincarcarea paginii.
- Dupa erori repetate de la Google (de exemplu 5 consecutive), trecere temporara pe rezerva, cu reincercare periodica.

### 7.4 Sesiunea Google

Un serviciu creeaza sesiunea la prima cerere, o retine in memorie si o reinnoieste inainte de expirare. La eroare de sesiune se creeaza una noua o singura data pe cerere.

### 7.5 Interfata

Cardul "Consum harti": cereri azi si luna aceasta fata de limita, bara de progres, starea motorului, cost estimat, tabel paginat cu istoricul pe zile si pe utilizator. Pe harta, un indicator discret cand se foloseste rezerva. Atribuirea Google, afisata conform conditiilor.

### 7.6 Jurnal si notificari

Operatiile din 4.9. Etapa B: notificare in aplicatie si e-mail la prag si la trecerea pe rezerva, prin modulul Notificari.

### 7.7 Teste

Un server Google simulat (sesiune, dale, 429, 500) pentru: validarea parametrilor, autentificarea, incrementarea contorului, trecerea la limita, cererile paralele (nu se depaseste limita cu mai mult decat marja), schimbarea lunii cu ceasul de test, reincarcarea dupa repornire, cheia absenta din raspunsuri si din loguri, reinnoirea sesiunii, fallback la erori. Pasii de verificare manuala pentru utilizator in `Teste utilizator/`. Testul cu cheia reala cere ca firma sa activeze facturarea si sa introduca cheia.

## 8. Etape si criterii de acceptare

| Etapa | Continut | Criteriu de acceptare |
|---|---|---|
| A | Motor Google, punct de acces la dale, sesiune, contor, limite, trecere pe rezerva, card de consum, jurnal, teste | Cu limita mica, harta trece pe rezerva; cheia nu apare in browser sau in loguri; contorul rezista la repornire |
| B | Notificari la prag si la trecere | Notificarea apare o singura data pe eveniment |
| C | Consum real din Cloud Monitoring | Valoarea din aplicatie si cea din Google se pot compara in card |
| D | Documentatie: operare, configurare nginx/WireGuard, topic de ajutor | Procedura se poate urma fara ajutor |

## 9. Riscuri si intrebari deschise

**Riscuri.**
- Conditiile si preturile Google se pot schimba; contorul si limitele sunt configurabile tocmai de aceea.
- Contorul din aplicatie nu vede consumul facut in afara ei cu aceeasi cheie; de aici cheia separata si cota Google.
- Luna de facturare si resetarea cotelor zilnice urmeaza ora Pacificului; contorul din aplicatie foloseste luna calendaristica locala, deci apar diferente mici la granita lunii.
- Serverul trebuie sa aiba iesire catre internet; fara ea nici rezerva nu merge.
- Un singur proxy intre utilizatori si Google creste incarcarea serverului la utilizare mare; dalele sunt mici, dar se masoara in etapa A.

**De decis.**
1. Limita lunara tinta si bugetul maxim acceptat de firma.
2. Pretul si pragul gratuit actuale (verificate pe paginile Google).
3. Harta de drum, satelit sau ambele; afisarea atribuirii Google.
4. Harta de rezerva prin serverul aplicatiei (recomandat) sau direct din browser.
5. IP public fix al serverului (pentru restrictia cheii) sau doar restrictie pe API.
6. Nume intern si certificat pentru nginx; cine administreaza contul Google si alertele de facturare.
7. Modul 2FA pentru clientii VPN: "Pentru toti" (recomandat) sau subreteaua VPN exclusa din reteaua interna.
8. Oprirea automata a facturarii (strat 4): da sau nu.

## Anexa A. Sablon nginx (orientativ)

```
limit_req_zone $binary_remote_addr zone=tiles:10m rate=20r/s;

server {
    listen 10.8.0.1:443 ssl http2;              # adresa serverului din reteaua WireGuard
    server_name harta.firma.local;

    ssl_certificate     /etc/nginx/tls/harta.crt;
    ssl_certificate_key /etc/nginx/tls/harta.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    add_header Strict-Transport-Security "max-age=31536000" always;

    # dalele: limitate, fara pastrare pe server
    location /harta/dale/ {
        limit_req zone=tiles burst=60 nodelay;
        proxy_pass http://127.0.0.1:5087;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;   # se suprascrie, nu se adauga
        proxy_set_header X-Forwarded-Proto https;
        proxy_read_timeout 10s;
        proxy_buffering on;
        # fara proxy_cache: dalele Google nu se pastreaza pe server
    }

    # restul aplicatiei (Blazor Server, WebSocket)
    location / {
        proxy_pass http://127.0.0.1:5087;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto https;
        proxy_read_timeout 100s;
    }
}
```

## Anexa B. WireGuard (orientativ)

```
# server /etc/wireguard/wg0.conf
[Interface]
Address = 10.8.0.1/24
ListenPort = 51820
PrivateKey = <cheia-serverului>

[Peer]                      # un bloc pentru fiecare utilizator
PublicKey = <cheia-publica-a-utilizatorului>
AllowedIPs = 10.8.0.11/32

# client (telefon)
[Interface]
Address = 10.8.0.11/32
PrivateKey = <cheia-clientului>

[Peer]
PublicKey = <cheia-publica-a-serverului>
Endpoint = vpn.firma.ro:51820
AllowedIPs = 10.8.0.1/32     # doar aplicatia, nu tot internetul
PersistentKeepalive = 25
```
