# Raport: algoritmul de detectie al lui Codex (OcrTabel) fata de motorul de facturi

Data: 05.10.2026. Surse comparate: `OcrTabel/TableOcr.cs` (descris in `ChatGPT_OCR_app/CONTEXT_PENTRU_CLAUDE.md`) si `Services/Invoices` (motorul geometric B, `InvoicePdfReader`, `InvoiceGridDetector`).

Nu am modificat nimic in `OcrTabel` si nici in codul BlazorStoc (am adaugat doar acest fisier). Detectorul lui Codex a fost rulat dintr-o copie din scratchpad, cu un mod suplimentar care face doar partea de grila (aceiasi pasi si parametri ca `AnalyzeImage`, fara OCR), pe 19 pagini din `D:\_BlazTest`. Suprapunerile desenate sunt in scratchpad (`ocr-codex/out`). Compararea cu motorul B s-a facut cu `tests/BlazorStoc.InvoiceCorpus`.

## 1. Pe scurt

- Cele doua abordari sunt complementare, nu concurente. Codex porneste de la linii si gaseste celule inchise. Motorul B porneste de la text si gaseste tabelul si cand liniile lipsesc sau sunt rupte.
- Pe tabele cu linii complete detectorul lui Codex este foarte bun. Telesystem, Altex, GS Incendiu si clasic-in-ron dau grila exacta, cu antet, rand de numere de coloane, randuri si totaluri.
- Un caz il castiga clar: **Facturis.ro-1 si Facturis.ro 3**. Codex gaseste tabelul (7x8 si 12x8 celule), motorul meu (A si B) nu gaseste niciunul. Cauza la mine nu e investigata.
- Pe facturile cu probleme la mine (Emporium, scanari cu linii rupte) Codex este mai slab sau da zero: Emporium 0 tabele, GS 5x5 fragmentar, Secpral corpul intr-o singura celula.
- Merita preluate trei lucruri, in ordinea de mai jos: (1) celulele inchise ca a doua sursa de grila, (2) recitirea pe celule dupa ce geometria e stabila, (3) celulele ca ciorna pentru referintele din laborator.

## 2. Ce face Codex

1. Randeaza pagina la cel mult 300 DPI, gri.
2. Inclinare: `HoughLinesP` pe imagine redusa la 35%, mediana unghiurilor liniilor aproape orizontale (pana la 8 grade), sub 0,15 grade se ignora.
3. Liniile: `AdaptiveThreshold` (gaussian, bloc 31, C 15), apoi deschidere morfologica cu nuclee scurte: orizontal `max(25, latime/35)` (aprox. 70 px), vertical `max(40, inaltime/35)` (aprox. 100 px). Mastile orizontala si verticala se reunesc, se inchid cu 3x3 si se dilata.
4. Celulele sunt **gaurile grilei**: `FindContours` cu `CComp`, contururile care au parinte. Celulele cu acelasi parinte formeaza un tabel (minim 4 celule, minim 2 randuri si 2 coloane).
5. Marginile celulelor se grupeaza cu toleranta de 8 px. Rezulta indicii de rand si coloana si intinderea celulelor unite (`rowSpan`, `columnSpan`).
6. OCR: pagina intreaga fara linii, apoi fiecare celula separat (`SingleBlock`, pe imaginea originala indreptata). Se pastreaza citirea cu scorul mediu mai mare.
7. Nu are text extras din PDF, nu are sens pe coloane, nu are continuare pe pagina urmatoare, orientarea se alege manual.

## 3. Rezultate pe facturile tale (doar grila, fara OCR)

| Factura | Codex (celule inchise) | Motorul B |
|---|---|---|
| Telesystem 908159 / 682100 / 875289 / 905550 | 14 / 14 / 13 / 9 randuri x 8 coloane, curat (cu antet, numere de coloane, totaluri) | 10 randuri la 908159 |
| Altex ATX-016659091 | 4 x 11 (antet, numere, 2 randuri), plus subsolul ca al doilea tabel | 11 celule, 2 randuri |
| GS Incendiu 1364312 | 7 x 9, plus subsol 4 x 5 | 9 coloane (la ultima verificare, nu rulat acum) |
| factura-clasic-in-ron | 6 x 9 | nerulat in aceasta comparatie |
| **Facturis.ro-1** | **7 x 8, corect (antet, numere, 2 randuri, corpul gol, totaluri)** | **niciun tabel** |
| **Facturis.ro 3** | **12 x 8** | **niciun tabel** |
| Facturis.ro -2 | 0 (nu are linii verticale) | niciun tabel |
| GS 385502 (scan) | 5 x 5 fragmentar plus doua bucati de subsol, banda coloanelor Disc si Pret dupa discount lipseste | 9 coloane, 4 randuri |
| Secpral 6489 (scan) | 2 x 7: antetul si **tot corpul intr-o celula** (nu sunt linii intre randuri) | 7 coloane, randul citit |
| Emporium 640 | 0 tabele (linii punctate, fara chenar) | 7 coloane, 5 randuri |
| PC Garage, dipol, factura-fara-tva-usd, model-proforma | 0 (fara linii verticale) | PC Garage 6 randuri |
| SKM_C3320i (2 fisiere) | 8 tabele mici 2x3 sau 3x3 fiecare, nu e tabelul facturii | tabele de 5 si 18 randuri |

Concluzii din tabel:
- Unde chenarul e complet, celulele inchise dau fara nicio euristica exact ce vrem: antetul, coloanele, randurile.
- Codex nu alege tabelul facturii dintre grilele paginii (SKM da opt, GS trei). Alegerea trebuie facuta de noi, dupa continut.
- O linie lipsa sau o bordura rupta imparte gresit grila (GS) sau lipeste randurile (Secpral). Acolo geometria din text a motorului B este mai buna.

## 4. Ce ar putea imbunatati motorul meu

### 4.1 Celulele inchise ca a doua sursa de grila (prioritate mare)

Ce rezolva: Facturis.ro-1 si 3 (de ce le pierd eu nu am investigat; textul lor este mic si lipit, o ipoteza este ca OCR-ul il citeste slab, dar nu am verificat). Daca exista un chenar inchis, tabelul se localizeaza dupa celule, apoi se cauta antetul in interiorul lui, fara sa depinda de OCR-ul intregii pagini.

Cum se integreaza: ca al doilea candidat in `Detect`, lang motorul B, ales tot dupa scor geometric (fara valori). Cand cele doua candidate se potrivesc (aceleasi coloane in toleranta), increderea creste; cand difera, castiga cel ce acopera mai multe randuri de date.

Avertizari: trebuie filtrat dupa continut (SKM da 8 grile care nu sunt tabelul facturii) si pentru grile cu mult spatiu gol (corpul din Facturis e un rand de celule gigant, golit). Pentru liniile rupte (GS) nu inlocuieste B.

Efort: mediu. OpenCV exista deja. Mare parte din liniile gasite le am deja in `RulesOf`; gaurile grilei se pot calcula si din acestea, nu neaparat din nou din imagine.

### 4.2 Recitirea pe celule dupa ce geometria e stabila (prioritate mare, a doua)

Aceasta e partea care tinteste erorile ramase la mine (de exemplu „oy" in loc de „1", „Ni", „CANTIITATEA."). Ideea lui Codex: celula decupata din imaginea originala, `SingleBlock`, comparata cu cuvintele citite pe pagina in acea celula.

Ce trebuie schimbat fata de varianta lui:
- `ReadWords` creeaza un motor Tesseract pentru fiecare celula; la 100 de celule e lent. Motorul se refoloseste (la mine exista `tesseractGate`).
- Scorul mediu din citirea pe celula nu e comparabil cu cel de pagina; o celula mica da scoruri mari si pentru text gresit. Criteriul mai bun: la coloanele numerice se prefera citirea care e numar valid (`PSM SingleLine` cu lista de caractere permise `0-9 . , -`), apoi se verifica relatia cantitate x pret = valoare.
- Ordinea ramane cea ceruta de tine: intai geometria, apoi textul. Aceasta tehnica intra la treapta 3, nu inainte.

### 4.3 Celulele ca ciorna de referinta pentru laborator (prioritate medie, efort mic)

Referintele din `references` sunt acum ciorne scrise din motorul A si trebuie corectate manual. Pentru facturile cu chenar complet, celulele lui Codex sunt un punct de plecare mult mai bun (Telesystem, Altex, Facturis, GS Incendiu, clasic). Acolo unde B si grila lui difera, diferenta este exact lista de verificat de catre tine.

### 4.4 Estimarea inclinarii din linii (prioritate mica)

Codex ia mediana unghiurilor liniilor lungi orizontale (`HoughLinesP`, precizie de ordinul 0,1 grade). Eu folosesc profilul de proiectie (pas de 0,1 grad). Cand exista linii, estimarea din ele e de obicei mai precisa si ar reduce nevoia de `JoinTilted`. Nu am comparat inca cele doua metode pe aceleasi scanari, deci nu stiu cat castig. De masurat inainte de adoptare (SKM_C3320i 570 are 2,67 grade dupa estimarea lui Codex).

### 4.5 Parametrii liniilor (prioritate mica)

Codex foloseste nuclee mai scurte pentru linii (aprox. 70 px orizontal, 100 px vertical la 300 DPI) fata de `max(60, latime/16)` (aprox. 155 px) la mine, si inchide colturile cu 3x3 plus dilatare. Pe cele 19 pagini nucleele scurte nu au dat tabele false mari, dar nu am comparat efectul asupra liniilor mele. De incercat doar ca experiment in laborator.

## 5. Ce nu merita preluat

- Orientarea manuala: motorul meu incearca singur cele patru orientari.
- OCR obligatoriu pe orice PDF: la mine PDF-urile cu text folosesc stratul de text (si liniile vectoriale), mult mai exact decat raster plus OCR.
- Tabelul fara linii (Emporium, Secpral, PC Garage): Codex nu are strategie; motorul B ramane singura cale.
- Scorul pe celula ca singur criteriu de alegere intre doua citiri (vezi 4.2).
- Toate grilele paginii ca rezultat: la facturi trebuie ales tabelul liniilor.

## 6. Ordinea propusa

1. Investigat de ce Facturis.ro-1 si 3 nu sunt gasite de motorul B (verific cu `--words` ce vede OCR-ul in zona tabelului).
2. Adaugat celulele inchise ca al doilea candidat de grila, cu alegere dupa continut si scor geometric (4.1).
3. Ciorne de referinta din celule pentru facturile cu chenar complet (4.3).
4. Dupa ce geometria e stabila: recitire pe celule cu motorul refolosit si reguli pe coloane numerice (4.2).
5. Experiment in laborator cu inclinarea din linii si cu nucleele scurte (4.4 si 4.5).

## 7. Ce s-a implementat dupa raport (05.10.2026)

Investigatia Facturis a schimbat prioritatile: cauza nu era lipsa celulelor inchise, ci o decizie gresita la citire.

1. **Pagina cu strat de text care nu e factura (Facturis.ro-1 si 3).** PDF-ul are 24 de cuvinte in stratul de text (antetul si subsolul tiparite din browser), iar factura e imagine. Pragul `MinTextWords = 8` o lua drept pagina cu text si nu facea OCR. Acum se masoara cat din cerneala paginii nu e acoperita de cuvintele extrase (`UncoveredInk`): paginile cu text real au 0-18% (proforma cu grafica mare 45%), cele cu imagine 96-98%; peste 70% se citeste prin OCR. Facturis.ro-1 si 3 au tabel, cu 2 si 6 randuri. Facturis.ro -2 ramane ilizibila (captura foarte blurata, fara linii verticale).
2. **Rand de numere de coloane citit prost de OCR** ("o 1 = a i. 5 6") este recunoscut ca atare.
3. **Benzi cu linii desenate fara cifre:** o banda cu text la marginea de sus (descriere sub produs) continua randul de deasupra, o nota in spatiul gol de sub ultimul rand nu e rand, un antet repetat in interiorul tabelului (fisele SKM) nu e nici una, nici alta.
4. **Recitirea numerelor pe celule (idee preluata de la Codex, adaptata).** Dupa ce tabelul e gasit, celulele numerice care nu sunt numar se decupeaza din imaginea paginii (fara liniile tabelului, doar linia de text proprie), se citesc cu motorul Tesseract refolosit si doar cu caracterele unui numar permise. In randurile care nu se potrivesc aritmetic se citesc din nou cantitatea, pretul si valoarea si se alege combinatia la care cantitate x pret = valoare, cu cele mai putine celule schimbate. Se foloseste coloana de pe pagina curenta (din antetul repetat), nu cea de pe prima pagina. Raman neatinse celulele care sunt deja numere.
   - Emporium: cantitatile 1 (in loc de "oy" si "4"), pretul 942.12, TVA 197.85, pretul 38.33 fara bara bordurii; toate cele 5 randuri sunt corecte.
   - Telesystem 682100: cele 10 cantitati lipsa de pe pagina 2 (12, 4, 2, 1, 1, 2, 5, 5, 2, 3) sunt citite; randuri marcate 10 -> 0.
   - Secpral: valoarea 523.38 (in loc de "§23.38"). Facturis: 250.00, 80.65, 32,00.
   - Ramase gresite: valori din imagini blurate (Facturis 3 randul 3 si 6, Facturis-1 "58 32").

Neimplementat, din decizie: celulele inchise ca a doua sursa de grila. Dupa remedierea de la punctul 1 motorul geometric gaseste toate tabelele cu linii din corpus; ramane de reconsiderat daca apar facturi cu chenar complet pe care motorul B nu le gaseste. Inclinarea din linii si nucleele scurte nu au fost incercate.

Verificari: suita completa 812 PASS, 0 FAIL (facturile reale sarite); pe corpus numarul de coloane si de randuri nu s-a schimbat nicaieri (doar celulele din cele 6 fisiere de mai sus).
