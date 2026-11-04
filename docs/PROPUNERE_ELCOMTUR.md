# Propunere: preluare date Elcomtur (balante de plata pe beneficiar)

Data: 04.11.2026. Branch: `Elcomtur`. Stare: propunere, nimic implementat.

## 1. Ce contine fisierul `softimel_4.elc`

- Este o arhiva ZIP de 343 MB (9690 de intrari) cu backup-ul programului Elcomtur (Visual FoxPro, tabele `.dbf`), din 30.10.2026. Nu e baza SQL; se citeste direct, fara program extern.
- Folderele de firma: `firma1` = Electric Standard Prest SRL (CUI RO9178894), `firma3` = KS Digital Energy SRL, `firma2` goala. Datele de clienti sunt in `firma1`.
- Tabelele necesare (in `firma1/`), cu campurile folosite:
  - `clienti.dbf` (1898 clienti): `COD`, `DEN` (nume), `CODFIS` (CUI, cu sau fara RO; gol la 878, in mare parte persoane fizice), `PJ_PF` (1 = 1037, 2 = 861).
  - `vinzari.dbf` (12239 facturi emise): `COD_CLI`, `NR`, `DATA`, `TOT_FACT`, `REST_PL` (rest de plata), `PLATIT`, `TERMEN` (zile).
  - `casa.dbf` (35697 inregistrari): incasarile de la clienti sunt `FEL = 1`, `CONT = 4111`, `CLIFUR` = codul clientului, `INCASARI`.
  - (`dincasa.dbf` leaga incasarile de facturi; nu e necesar pentru sold.)
- Esantioane facute in alta sesiune: `D:\_BlazTest\Balante\` (`electric_clienti_balanta.csv`: cod, nume, CUI, rest de plata, nr facturi).

## 2. Cum se calculeaza balanta (DECIS de utilizator, 04.11.2026)

Se foloseste logica din sesiunea "Extragere date clienti din fisiere" (scenariul Spitalul Zam la sfarsitul lui septembrie): **balanta clientului = suma facturilor emise (`vinzari.TOT_FACT`) minus suma incasarilor (`casa.INCASARI`, `FEL = 1`, `CONT = 4111`, `CLIFUR` = codul clientului)**, pe tot istoricul pana la data backup-ului.

- Verificat din nou pe arhiva: Zam la 30.09.2026 = 3.595.888,35 - 3.477.810,75 = **118.077,60** (exact valoarea din program). La data backup-ului (30.10.2026) Zam iese 24.118,05.
- Se aplica la toti clientii din arhiva care se potrivesc cu un beneficiar persoana juridica din baza de stocuri (nu doar la Zam).
- Balanta se salveaza cu data backup-ului; pozitiva = plati restante, 0 sau negativa = "Nu sunt plati restante".
- Rezultate pe tot fisierul: 1118 clienti cu sold de cel putin 1 RON (857 de cel putin 100 RON), din care o parte vin din istoric vechi (2011). Pot fi ascunse sub un prag ales de tine (de exemplu sub 1 RON se trateaza ca 0); implicit propun 0,01 RON (rotunjire), fara alt prag.
- Scadenta (`DATA + TERMEN`) nu intra in balanta; se poate adauga ulterior ca a doua coloana.

## 3. Ce se construieste

**Modul nou `Elcomtur`** (`Modules/Elcomtur`, versiune proprie, circuit breaker, intrare in meniu; oprit => paginile lui spun "modul indisponibil", iar sectiunea din beneficiar dispare prin `ModuleGate`).

1. **Pagina `Administrare -> Preluare date Elcomtur`** (permisiune noua `elcomtur.import`):
   - incarcare fisier `.elc` (vezi riscul de dimensiune mai jos), alegerea firmei din arhiva (implicit `firma1`);
   - **simulare** (nu scrie nimic): cati clienti persoane juridice s-au gasit in baza de beneficiari, cati cu sold pozitiv, suma totala, clientii care nu se potrivesc;
   - **aplicare**: salveaza balantele; jurnal "Preluare date Elcomtur" cu numarul de beneficiari actualizati si data backup-ului.
2. **Potrivirea clientilor cu beneficiarii**: dupa CUI (cifrele, fara RO, fara zerouri in fata), doar persoane juridice cu CUI valid. Clientii fara CUI sau fara potrivire se listeaza in simulare ca "nepotriviti" si nu se preiau. Nu se creeaza beneficiari noi.
3. **Fisa beneficiarului**: sectiune pliabila "Balanta Elcomtur" ca celelalte; antet verde si "Nu sunt plati restante" cand balanta e 0 sau negativa; antet rosiatic si valoarea platilor restante cand e pozitiva; data backup-ului afisata in sectiune (datele sunt cele din ultima preluare, nu live).
4. **Notificari**: tab nou "Balante restante" cu tabel (beneficiar cu link, balanta, data datelor, paginat, sortat descrescator). Nu se genereaza notificari: tabul este doar o lista.
5. Stocare: tabel `elcomtur_balances` (beneficiar, balanta, nr facturi, data backup, preluat la/de) + jurnal de preluari; migrare noua; la o preluare noua balantele se inlocuiesc (beneficiarii care nu mai apar primesc 0).
6. Documentare ca la celelalte module: topic `.help` pentru pagina noua, fisier in `Teste utilizator/`, teste (parser DBF pe fisiere mici generate, calculul balantei, potrivirea dupa CUI, pagina, sectiunea, tabul).

## 4. Riscuri si intrebari

- **Dimensiune**: 343 MB. Componenta obisnuita de incarcare din Blazor nu e potrivita; folosesc incarcarea HTTP directa (ca la pachetele de actualizare), cu salvare intr-un fisier temporar si citirea doar a 3 intrari din ZIP (`clienti.dbf`, `vinzari.dbf`, `casa.dbf`, circa 40 MB), fara extragerea restului. Se sterge fisierul temporar dupa preluare.
- **Confidentialitate**: arhiva contine si fisiere cu parole/acces de la distanta (de exemplu programe de control la distanta); nu se citeste nimic in afara celor 3 tabele si nu se pastreaza arhiva.
- **"Furnizori" din cerere**: la tabul din Notificari am inteles *beneficiari* (clientii cu sold pozitiv), nu furnizori. De confirmat.
- **Firma**: se preiau datele unei singure firme (`firma1`); daca vrei si KS Digital Energy (`firma3`), balantele s-ar tine pe firma (decizie suplimentara).
- **Prospetime**: balantele sunt cele din backup; nu exista sincronizare automata (fisierul se incarca manual cand vrei date noi).
- Codificarea textelor din `.dbf` este Windows-1250 (de verificat pe nume cu diacritice).

## 5. Etape propuse

1. Parser DBF + calculul balantei + potrivirea dupa CUI, cu teste (fara interfata).
2. Migrare, stocare, pagina de preluare cu simulare si aplicare.
3. Sectiunea din fisa beneficiarului.
4. Tabul din Notificari.
5. Help, teste utilizator, versiuni de modul, verificare in browser pe instanta de test.

## 6. Completari cerute la 04.11.2026

**Tabul din Notificari:** "Balante restante", pentru beneficiari (nu furnizori), doar `firma1`, doar persoane juridice (`clienti.PJ_PF = 1`: 1037 clienti, 1019 cu CUI; `PJ_PF = 2` sunt persoane fizice, fara CUI), cu balanta pozitiva. Fara notificari generate.

**Facturi cu scadenta depasita in sectiunea din beneficiar** (inclusiv cele platite partial):
- Se pastreaza pe beneficiar facturile cu rest de plata (`vinzari.REST_PL > 0`: 422 in tot fisierul): numar (`NR`), data, scadenta (`DATA + TERMEN` zile; termene uzuale 0, 10, 15, 30, 60), total, platit, rest.
- In sectiune se afiseaza cele cu scadenta depasita la data zilei (scadenta mai mica decat azi), cu zilele de intarziere si restul; o factura platita partial apare cu restul ei. Tabel paginat, sub balanta, in aceeasi sectiune pliabila; ordonat dupa scadenta.
- Pe backup, la 04.11.2026: 374 facturi scadente (108 platite partial), 320 ale persoanelor juridice, la 121 de clienti.
- Tabel nou `elcomtur_invoices` (beneficiar, numar, data, scadenta, total, platit, rest), inlocuit la fiecare preluare.

**Atentie la diferenta intre cele doua cifre:** balanta (facturat - incasat, decisa) si suma resturilor pe facturi nu coincid mereu, pentru ca `REST_PL` nu tine cont de incasarile alocate pe facturi vechi sau de compensari. Exemple: Zam are balanta 24.118,05 si resturi pe facturi 27.346,94; pe tot fisierul suma resturilor scadente este 2.207.774,63, iar PDF-ul "facturi neincasate" al programului arata 1.778.601,41. Sectiunea va spune ca lista este a facturilor marcate neachitate in Elcomtur, iar balanta este cifra de referinta. Alternativa: lista se deduce din balanta (cele mai noi facturi care insumeaza balanta, cea mai veche platita partial) si atunci cele doua cifre coincid mereu, dar facturile listate nu mai corespund cu marcajul din Elcomtur.

**Intrebare de decis:** lista de facturi dupa marcajul `REST_PL` din Elcomtur (propus) sau dedusa din balanta?

## 7. Doua tabele in beneficiar si legatura cu soldul (04.11.2026)

Fisa beneficiarului: antet cu **balanta** (rosiatic cand soldul e pozitiv), apoi doua tabele: **facturi cu termen depasit** si **facturi inca in termen** (restul marcat in Elcomtur, `REST_PL > 0`; scadenta = `DATA + TERMEN`).

Suma celor doua tabele NU este mereu soldul. Pe backup, la persoanele juridice cu sold sau rest pozitiv (486): coincid la 70; la 379 soldul e mai mare decat suma resturilor (363 au sold pozitiv fara nicio factura deschisa, in total 239.330,79); la 37 soldul e mai mic. Zam (30.10.2026): sold 24.118,05, suma facturilor 27.346,94 (diferenta -3.228,89).

Propunere: sub cele doua tabele un rand **"Diferenta fata de sold (incasari nealocate pe facturi / regularizari)"** = sold - suma resturilor, afisat doar cand nu e 0; astfel tabelele + randul dau intotdeauna soldul. Alternativa (nerecomandata): lista dedusa din sold, cele mai noi facturi pana la sold; suma coincide mereu, dar apar facturi pe care Elcomtur le considera platite, iar totalul scadent la 04.11 iese 2.447.802,25 (cu REST_PL: 2.207.774,63; PDF-ul programului: 1.778.601,41, pe date cu 5 zile mai noi).

## 8. Numar de facturi in antet si in Notificari (04.11.2026)

- **Antetul sectiunii "Balanta Elcomtur"** (sold pozitiv): valoarea balantei si "N facturi cu termen depasit" + "M facturi in termen" (numarate pe facturile cu rest > 0, la data zilei: scadenta mai mica decat azi = depasita; numerele se schimba zilnic fara reimport). Sold 0 sau negativ: antet verde, "Nu sunt plati restante", fara numere. Cu sold pozitiv si fara facturi deschise: 0 si 0, plus randul de diferenta.
- **Tabul "Balante restante" din Notificari**: coloane Beneficiar (link), Balanta, Facturi depasite, Facturi in termen, Date la (data backup-ului), Operatii; paginat, descrescator dupa balanta. Iconita `row-icon view` ("Vezi balanta beneficiarului X") deschide `/beneficiari/{id}?balanta=1`: pagina deschide sectiunea Balanta Elcomtur (pliabila, inchisa implicit) si deruleaza pana la ea.

## 9. Implementare (04.11.2026, branch Elcomtur) - corectii fata de analiza

- **Regula incasarilor corectata:** se numara toate incasarile de la clienti din `casa.dbf` (`FEL = 1`, contul incepe cu `4111`: 4111, 4111.2, 4111.3), nu doar contul 4111. Cu regula completa balanta coincide cu restul marcat pe facturi la 96% dintre clienti (1804 din 1871); cu 4111 singur la doar 44%. Spitalul Zam nu se schimba (are doar 4111): 118.077,60 la 30.09.2026, 24.118,05 la 30.10.2026. **Cifrele din sectiunile 2, 6 si 7 (1118 clienti pozitivi, "70 din 486 coincid", totaluri 2.557.240,93) erau calculate cu regula veche si nu mai sunt valabile**; randul "Diferenta fata de sold" ramane, dar apare doar la clientii la care balanta nu se potriveste cu facturile.
- **Firme:** doar folderele `firma<N>` din arhiva (nu `baze` si `trimit`); implicit `firma1`.
- **Implementat:** modul `Elcomtur` 1.0.0 (cititor DBF, calcul, potrivire dupa CUI, tabele `elcomtur_balances` / `elcomtur_invoices` - migrarea 62, incarcare HTTP directa `/api/elcomtur/incarcare`, pagina `/preluare-elcomtur`, sectiunea din beneficiar, tabul "Balante restante" cu sortare, jurnal "Preluare date Elcomtur", permisiuni `elcomtur.view` si `elcomtur.import`).
- **Help:** nu s-a scris (decizia utilizatorului); pagina e in `Pending` din `HelpChecks` si in `docs/HELP_DE_REGENERAT.md`.
- **Verificat:** teste (componente, MariaDB pe baza de test, citire pe fisierul real `softimel_4.elc`: Zam 24.118,05 la 30.10.2026), instanta separata pe baza de dezvoltare (sectiunea rosie/verde, tabul, sortarea, pagina, incarcarea fisierului de 343 MB prin HTTP). Neverificat in browser: alegerea fisierului din fereastra de dialog si simularea/aplicarea prin interfata pe fisierul real (functioneaza pe teste).
