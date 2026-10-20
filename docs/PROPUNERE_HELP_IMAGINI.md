# Propunere: capturi de ecran in help, cu chenar pulsatil pe elementul ales

Data: 19 octombrie 2026. Stare: DOAR PROPUNERE, nimic implementat. Fisier fara diacritice (regula .md); textele din aplicatie vor avea diacritice.

Cerinta (ultima formulare a utilizatorului):
- fiecare fereastra al carei help descrie elementele primeste o imagine (captura ferestrei), in sectiunea "Elementele ferestrei" a helpului ei;
- fara badge-uri si numere pe imagine;
- elementele descrise sunt intr-un tabel; la clic pe un rand, in jurul elementului respectiv de pe imagine apare un chenar pulsatil;
- chenarul este vizibil numai cat timp un rand este ales.

## 1. Punct de plecare (din cod)

- Helpul actual: 89 de topicuri `Assets/Help/*.help` cu ~990 de elemente (`- Nume | tip | descriere`), randate de `HelpRenderer` ca lista de definitii (`dl`) in sectiunea "Elementele ferestrei". Exista deja `[img:fisier.png | legenda]`, endpoint-ul `/ajutor/imagine/{nume}` (doar utilizatori autentificati) si resurse incorporate `Assets/Help/img/*` (director gol acum).
- Un topic = o fereastra (pagina, tab, formular sau dialog), deci o imagine per topic se potriveste natural: fiecare tab si fiecare dialog au deja topic propriu.
- Pe Windows exista Node si Edge/Chrome, deci o unealta de captura nu cere instalari de browser noi (Playwright cu `channel: 'msedge'`, sau Chrome DevTools Protocol direct).
- Masurat pe pagina Produse si stocuri (preview 5089): 17 din 20 de elemente descrise se regasesc automat in pagina dupa text (buton, antet de coloana, eticheta, placeholder, `title`/`aria-label`); cele 3 ramase sunt descrieri de grup ("Categorii produse - meniu", "Numarul de rezultate", "Paginile"). Concluzie: ~85% din legaturi pot fi automate, restul se leaga cu un selector scris de mana.

## 2. Principiul: captura si coordonatele vin din aceeasi sursa

O captura facuta de mana se invecheste si coordonatele scrise de mana nu mai nimeresc dupa prima schimbare de aspect. De aceea propun ca **imaginea si pozitiile elementelor sa fie generate impreuna, de o unealta**, dintr-o instanta de test a aplicatiei:

1. unealta deschide pagina (sau dialogul) cu date de demonstratie;
2. face captura (JPEG, ~1280 px latime);
3. pentru fiecare element din topic gaseste in DOM elementul real si ii masoara dreptunghiul (`getBoundingClientRect`);
4. scrie `Assets/Help/img/<cheie>.jpg` si `Assets/Help/img/<cheie>.map.json` cu dreptunghiurile in procente din imagine (deci chenarul ramane exact la orice latime a ferestrei de help).

## 3. Cum se leaga un rand de element

- **Automat (implicit):** unealta cauta elementul dupa numele din rand, in ordinea: text vizibil al unui buton/link, antet de coloana (`th`), eticheta (`label`/`legend`), `aria-label`, `title`, `placeholder`; pentru iconite (`iconita:edit`) cauta `.row-icon.edit` (toate aparitiile, deci chenar pe fiecare iconita de pe randuri); pentru randuri de tipul "Categorie, Subcategorie" incearca fiecare nume separat. Un nume gasit de mai multe ori primeste mai multe chenare (cel mult cateva, ca sa nu se aglomereze).
- **Explicit (cand automatul nu gaseste):** un camp optional la sfarsitul randului din `.help`: `- Paginile | butoane | Descriere | sel: .pagination`. Selectorul poate fi CSS sau `text: "Pagina anterioara"`.
- **Fara chenar:** elementele care nu se pot vedea in captura (mesaje de eroare, avertismente, stari rare) raman descrise in tabel, dar randul nu are actiune de clic si poarta mentiunea mica "nu apare in captura". Unealta le listeaza intr-un raport, ca sa se decida daca se mai face o captura cu alta stare.

## 4. Aspectul si comportamentul in fereastra de help

- In sectiunea "Elementele ferestrei": mai intai imaginea (latime maxima a ferestrei de help, cu `alt` = "Captura ferestrei <titlu>"; clic pe imagine = o deschide mai mare in aceeasi fereastra, chenarul functioneaza si acolo), apoi tabelul.
- Tabelul (`<table class="help-table">`, coloane Element | Tip | Descriere; zonele "### Zona" raman ca randuri de grup colapsabile sau antete de sectiune). Pictograma reala a actiunilor `row-icon` ramane in celula "Element".
- Randurile cu element vizibil in captura sunt butoane implicite (`tabindex="0"`, Enter/Spatiu = clic, `aria-pressed`), cu stil de rand selectabil (hover usor, ca la `tr.selectable-row`).
- **Chenarul:** un singur `div.help-frame` pozitionat absolut peste imagine (`left/top/width/height` in %), contur de 2 px in culoarea de accent, cu animatie `pulse` (box-shadow care creste si scade, ~1,2 s, infinita cat e vizibil). Este ascuns implicit; apare doar la selectarea unui rand; dispare cand: se apasa din nou acelasi rand, se alege alt rand (se muta), se apasa Escape sau se apasa pe imagine in afara chenarului, ori se schimba topicul. Elementele cu mai multe aparitii afiseaza mai multe chenare. Imaginea se deruleaza in vedere daca elementul e in afara portiunii vizibile a ferestrei de help. `prefers-reduced-motion`: chenar fix, fara pulsatie.
- Fara badge-uri, fara numere. Stilul urmeaza regulile existente (fara contur gros pe campuri; chenarul este un element decorativ separat de regula `outline`, deci nu o incalca).

## 5. Format si randare (modificari mici)

- `.help`: o linie noua optionala in antet: `Captura: <ruta sau cheie>` (de ex. `Captura: /produse`) si, pentru dialoguri/formulare, `Pasi:` (lista scurta de actiuni de executat inainte de captura: `click "+ Adauga produs"`, `asteapta 500`). Randurile primesc campul optional `sel:`.
- `HelpCatalog`: stie ca un topic are imagine daca exista `<cheie>.jpg` si `<cheie>.map.json`; valideaza ca fiecare rect din harta apartine unui rand al topicului.
- `HelpRenderer`: deseneaza imaginea (cu `data-help-map`) si tabelul; harta se trimite o data in fragment (JSON mic in `data-` sau script `type="application/json"`).
- `wwwroot/help.js`: tratarea clicului pe rand, pozitionarea/afisarea/ascunderea chenarului, derularea.
- `Startup/HelpEndpoints.cs`: endpoint-ul de imagini serveste deja resurse; se adauga tipul `.jpg`/`.webp` (exista) si cache (`Cache-Control: private, max-age`).

## 6. Unealta de captura (`tools/help-capture.mjs` + `tools/help-capture.ps1`)

- Porneste (sau foloseste) o instanta de test pe **baza de test** cu date de demonstratie, se autentifica cu un cont de test, citeste topicurile care au `Captura:`, executa `Pasi:`, face captura, calculeaza dreptunghiurile si scrie fisierele. Argumente: `--topic <cheie>`, `--changed` (doar topicurile ale caror fisiere razor s-au schimbat de la ultima captura), `--report` (doar raportul de elemente negasite).
- Fiecare `.map.json` poarta amprenta fisierelor razor ale topicului; `tools/check-help.ps1` (la commit) avertizeaza cand un razor s-a schimbat fara recapturare.
- Date si captura stabile: dimensiune fixa de fereastra (ex. 1366 x 900), teme implicita, fara animatii (CSS injectat), elementele cu ora/date curente marcate si mascate sau lasate (diferentele la regenerare sunt acceptate, regenerarea se face doar cand se schimba interfata).
- Pagini lungi: captura de pagina intreaga pana la o inaltime maxima (de ex. 2400 px), apoi decupata; elementele din afara decupajului raman fara chenar.

## 7. Teste (grupul `help`)

- fiecare topic cu `Captura:` are `.jpg` si `.map.json`; fisierele din `Assets/Help/img` sunt referite (fara imagini orfane);
- fiecare rect din harta se afla in intervalul 0-100% si apartine unui rand al topicului; fiecare rand cu `sel:` are macar un rect (altfel elementul s-a schimbat si selectorul trebuie revazut);
- fisierul `.map.json` nu depaseste o dimensiune rezonabila, imaginea nu depaseste un buget (ex. 250 KB);
- randarea: tabelul are un rand-buton pentru fiecare element cu rect si nu are pentru celelalte; chenarul exista ascuns (`hidden`) la deschidere (verificat cu bUnit/markup, plus in browser: clic pe rand -> chenar vizibil si pozitionat, al doilea clic -> ascuns).
- `Teste utilizator/17_Help.md` primeste pasii: deschide ajutorul unei pagini, apasa pe cateva randuri, verifica ca chenarul inconjoara elementul corect, ca dispare la al doilea clic si ca nu apare fara selectie.

## 8. Reguli din CLAUDE.md

| Regula | Cum este respectata |
|---|---|
| Fara casete de bifat | Nu exista; randurile selectabile sunt butoane/randuri, nu checkbox-uri. |
| Butoane `act-*`, iconite `row-icon` | Singurul buton nou de text (daca e nevoie) este "Mareste imaginea" cu `refresh act-nav`; iconitele reale se afiseaza in tabel ca pana acum. |
| Spatiere, padding 24 px | Imaginea si tabelul stau in corpul popup-ului (`help-body`, padding 24 px); 12-18 px intre imagine si tabel. |
| Diacritice | Textele din `.help` raman cu diacritice; acest document nu are. |
| Date `dd.mm.yyyy` | Datele din capturi vin din interfata, deja in format corect. |
| Date sensibile | Capturile se fac numai pe baza de test cu date fictive (nume de beneficiari, furnizori, CUI, utilizatori inventate); nu se comit date reale. |
| Teste utilizator, documentatie la commit | Se actualizeaza cum este regula; imaginile intra in commit. |
| Economie de tokeni | Captura se ruleaza o data pe topic si doar pe `--changed`; imaginile nu se citesc in conversatie, se verifica prin raport si prin cateva inspectii in browser. |

## 9. Alternativa mai ieftina (complementara)

Un buton "Arata pe pagina" in randurile din tabel: cu helpul deschis peste pagina reala, chenarul pulsatil apare direct pe elementul din pagina (fara imagini, mereu exact, fara intretinere), folosind aceleasi reguli de potrivire a numelui. Nu inlocuieste imaginile (pe care le-ai cerut si care functioneaza si pentru dialoguri care nu sunt deschise), dar poate fi adaugat ulterior cu putin cod in plus, deoarece reutilizeaza potrivirea si chenarul.

## 10. Etape si efort (in sesiuni de lucru)

| Etapa | Continut | Efort |
|---|---|---|
| 1. Randare si interactiune | `Captura:`/`sel:` in parser, tabel in loc de `dl`, imagine + harta, chenar pulsatil, `help.js`, CSS, teste de randare; pilot cu 2-3 topicuri facute de mana | 1-1,5 |
| 2. Unealta de captura | `help-capture.mjs` (potrivire automata, harta, raport), date de demonstratie pe baza de test, autentificare, instanta de test | 1,5-2 |
| 3. Pagini | rulare pe toate topicurile de pagina/tab (~60); lista elementelor negasite; selectori manuali pentru ~15% din ~990 de elemente (~150) | 2 |
| 4. Formulare si dialoguri | `Pasi:` pentru ~40 de formulare/dialoguri, date pentru stari (aviz, eroare) unde merita | 2 |
| 5. Teste si documentatie | grupul `help`, `tools/check-help.ps1`, `Teste utilizator`, CLAUDE.md, HARTA_COD | 0,5 |

Total aproximativ 7-8 sesiuni. Dupa etapa 1+2 se poate publica incremental (topicurile fara imagine raman ca acum, cu lista).

## 11. Riscuri

- Demo data: unele stari (avertismente, erori, ferestre de confirmare) nu apar fara scenarii special pregatite; raman fara chenar.
- Dimensiunea: ~70 de imagini JPEG de ~100-150 KB inseamna ~8-10 MB in plus in aplicatie si in repository (binare); regenerarile repetate umfla istoricul git, deci se regenereaza doar cand se schimba interfata.
- Text mic: o pagina de 1366 px afisata in ~850 px nu se citeste usor; de aceea "Mareste imaginea" si, la dialoguri, decupaj strans pe fereastra.
- Elemente suprapuse sau foarte mici (iconite de 32 px): chenarul are margine de 4-6 px in jurul dreptunghiului ca sa fie vizibil.
- Conturile si autentificarea unei unelte automate: vezi intrebarea 2.

## 12. Intrebari deschise

1. **Date de demonstratie:** pregatesc un set fictiv (beneficiari, furnizori, vehicule, contracte, produse) in baza de test, doar pentru capturi; de acord?
2. **Autentificare pentru unealta:** pot folosi contul de test din `local-secrets/test-login.private.json` (baza de test, instanta locala) ca unealta sa se conecteze singura? Ai spus ca parolele nu se tasteaza de catre mine in browser; o unealta locala pe baza de test este o exceptie pe care o accepti, sau preferi sa te conectezi tu o data si sa salveze sesiunea?
3. **Cadru:** captura de pagina intreaga (pana la o inaltime maxima) sau doar ce incape intr-un ecran? Cu meniul lateral si bara de sus sau doar zona de lucru? (Elementele din meniu/bara apar doar daca le includem.)
4. **Rezolutie si buget:** 1366 px latime si JPEG ~100-150 KB sunt acceptabile?
5. **Mobil:** doar desktop (propus) sau si capturi pentru ecran ingust?
6. **Elementele fara chenar:** raman in tabel, cu mentiunea "nu apare in captura" (propus), sau vrei captura suplimentara pentru starile importante?
7. **"Arata pe pagina"** (sectiunea 9): il adaugam dupa imagini?
8. **Ordinea:** incep cu paginile din meniul principal, ca la continut (propus), sau cu cele mai folosite (Produse, Preluare factura, Iesire multipla)?

## 9. Stare (19.10.2026)

Implementat conform raspunsurilor utilizatorului (date demo fictive, cont de test pe instanta de test, 1920x1080 pagina intreaga, doar desktop, capturi suplimentare pentru stari, iconita "Arata pe pagina" dupa numele elementului). Capturile sunt generate de `tools/help-capture.mjs` pe baza `blazorstoc_demo`; toate topicurile au captura; fereastra ANAF foloseste CUI-ul real al firmei Electric Standard Prest Simeria (cerut de utilizator; apel real catre ANAF) (lista `WithoutCapture` din `HelpChecks.cs`).
