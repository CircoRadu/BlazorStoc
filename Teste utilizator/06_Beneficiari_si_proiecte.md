# Beneficiari, proiecte si observatii (/beneficiari, /proiecte/{id})

Fluxul: Administrare -> Beneficiari (PF sau PJ cu preluare ANAF) -> pagina beneficiarului -> proiecte -> observatii, echipamente si fisiere. Iesirile din stoc spre beneficiar/proiect se aleg din combobox.

## Modificari de testat

### 08.10.2026 - Termen pe proiect

**Ce s-a adaugat:** pe pagina „Situatia proiectului" campul „Termen proiect" cu butonul „Setează termenul" (migrarea 29, tabelul `project_deadlines`).

**Ce face acum:** termenul nu poate fi in trecut; se inlocuieste daca il schimbi; cu × se elimina. Cu un sablon de notificare activ pentru „Deficit la un proiect cu termen apropiat" (vezi 09), un proiect cu termen si cu lista de achizitie nevida primeste notificare cand termenul se apropie.

1. Deschide situatia unui proiect cu oferta, alege o data din viitor si apasa „Setează termenul".
2. Incearca o data din trecut: mesajul „Termenul nu poate fi înainte de data de azi.".
3. Schimba data, apoi elimina termenul cu ×. In Jurnal: „Setare termen proiect" / „Eliminare termen proiect", cu valoarea veche si noua.

**Detalii:**

- Termen: se seteaza pe pagina „Situatia proiectului", nu poate fi in trecut; fara el nu exista notificarea de deficit.
- Jurnal: „Setare termen proiect" si „Eliminare termen proiect"; tabela `project_deadlines` este inclusa in arhivare.
- Deficitul este lista de achizitie a situatiei proiectului (ce nu este acoperit de stoc).
- Cod: `Components/Shared/ProjectDeadlineField.razor`, `Services/MariaStockAlerts.cs`.

### 07.10.2026 - Rezervari pe proiect

**Ce s-a adaugat:** (1) **Rezervari**: pagina Situatia proiectului are coloana "Din care rezervat", butoanele "Rezerva cat se poate" (pe linie si pe componenta), formularul de rezervare manuala (produs, componenta, cantitate) si lista "Rezervarile proiectului" cu "Scade / elibereaza" (cantitate + motiv scurt). Rezervarea **nu schimba stocul**; scade **stocul liber** (stoc - rezervari, niciodata sub zero; la stoc negativ nu e nimic liber). (2) **La iesire nu se blocheaza nimic**: iesirea pentru proiect consuma intai rezervarea proiectului; restul se compara cu stocul liber, iar daca ar lua bucati rezervate de alte proiecte apare un avertisment cu proiectele care tin rezervari si alegerea: continua fara sa ating rezervarile, sau scade rezervarea unui proiect (cantitate + motiv), sau renunti; la Iesire multipla, avertismentele apar in rezumatul de dinainte de salvare, pe fiecare linie. (3) Pe pagina produsului apare "Rezervat: X buc. (proiecte) · stoc liber". (4) Dupa o **intrare** pe produs apar propunerile "Rezerva N buc. pentru proiectul X" (proiecte al caror necesar e acoperit de stocul liber, dar nerezervat), iar intrarea poate fi legata de o oferta (selectorul "Pentru oferta", cu propunere automata cand produsul e intr-o singura componenta); in Situatie apare coloana "Intrat pt. oferta". (5) La scoaterea unei componente, rezervarile ei se elibereaza automat.

**Ce face acum:** Poti rezerva bucati pentru un proiect fara sa schimbi stocul: scade stocul liber, iesirea proiectului o consuma prima, iesirea altui proiect care ar lua din ea avertizeaza (fara blocare) si lasa sa continui sau sa scazi o rezervare cu motiv.

1. Pe un produs cu stoc (de ex. 10 buc.) deschide Situatia unui proiect cu oferta si apasa "Rezerva cat se poate" pe linia produsului: "Din care rezervat" creste, stocul produsului ramane neschimbat, iar pe pagina produsului apare "Rezervat ... stoc liber".
2. Incearca o rezervare manuala mai mare decat stocul liber: e refuzata cu mesajul de maximum. "Scade / elibereaza" cere motiv.
3. Fa o iesire pentru proiectul care a rezervat: nu apare niciun avertisment, rezervarea scade cu cantitatea iesita.
4. Fa o iesire (alt beneficiar sau vanzare) care ia din bucatile rezervate de alt proiect: apare avertismentul cu proiectele; alege "Continua" (rezervarile raman) sau "Scade rezervarea" (alegi proiectul, cantitatea si motivul). Repeta cu Iesire multipla.
5. Scoate o componenta care are rezervari: rezervarile ei dispar. In Jurnal apar actiunile exacte (Rezervare stoc, Scadere rezervare, Eliberare rezervare, Rezervare consumata prin iesire, Scadere rezervare la iesire, Rezervare eliberata la scoaterea componentei).
6. La o intrare pe produs, daca un proiect are nevoie de produs si stocul liber il acopera, apare propunerea de rezervare; nimic nu se rezerva fara clic.
7. Nu sunt inca legate de rezervari: intrarile din preluarea facturii (task separat in TODO).

### 07.10.2026 - Iesiri legate de componente si regula de scoatere a componentei

**Ce s-a adaugat:** (1) o **iesire spre un beneficiar, cu proiect**, se leaga de o **componenta** a proiectului: automat daca produsul apare in oferta unei singure componente, "in afara ofertei" daca proiectul are oferte dar produsul nu apare in niciuna, iar daca apare in mai multe componente trebuie aleasa (selectorul "Componenta" din formularul de iesire si din Iesire multipla; se poate alege oricand alta componenta sau "In afara ofertei", de exemplu pentru un produs echivalent). (2) **Scoaterea unei componente** cu iesiri nelamurite este blocata: apare un dialog cu fiecare produs ramas la beneficiar si o actiune: **Retur in depozit** (se creeaza returul legat de iesire), **Ramas la beneficiar** (predat definitiv), **Mutat pe alta componenta** sau **Consumat**; fiecare actiune are propriul eveniment in Jurnal. (3) Situatia proiectului foloseste acum legatura cu componenta pentru "predat" si "in afara ofertei" pe componenta.

**Ce face acum:** Iesirile se leaga de componente, iar o componenta cu iesiri nelamurite nu se scoate pana nu alegi pe fiecare produs: retur in depozit, ramas la beneficiar, mutat pe alta componenta sau consumat.

1. Preia o oferta cu doua componente care au un produs comun (sau foloseste un proiect existent). Fa o iesire spre beneficiar + proiect pentru un produs aflat doar intr-o componenta: nu apare nicio intrebare, iar in Situatia proiectului "predat" creste pe acea componenta.
2. Fa o iesire pentru un produs care nu e in nicio oferta a proiectului: selectorul arata "Automat: in afara ofertei"; dupa salvare produsul apare la "In afara ofertei".
3. Fa o iesire pentru un produs aflat in mai multe componente fara sa alegi: salvarea e refuzata cu mesajul de alegere; alege componenta si salveaza.
4. Incearca sa scoti o componenta cu iesiri legate: apare tabelul cu produsele; scoaterea nu se face pana nu alegi o actiune pentru fiecare (la "Mutat" alegi si componenta tinta).
5. Alege "Retur in depozit": stocul produsului creste, apare intrarea de retur legata de iesire; "Ramas la beneficiar"/"Consumat" nu schimba stocul. In Jurnal (proiectul) apare cate un eveniment cu actiunea exacta.
6. O componenta fara iesiri se scoate ca pana acum (doar cu motiv).

### 07.10.2026 - Situatia proiectului pe componente

**Ce s-a adaugat:** pagina **Situatia proiectului** (`/proiecte/{id}/situatie`): pe fiecare componenta si linie din ultima revizie a ofertei, necesar / predat (iesiri minus retururi) / din stoc / deficit si stare (Acoperit, Partial, Lipsa, In afara stocului), produsele predate care nu sunt in oferta, **lista de achizitie** grupata pe ultimul furnizor al produsului, export PDF si CSV; in sectiunea Componente a proiectului, coloana "Predat X din Y". Preturile nu se iau in calcul.

**Ce face acum:** Vezi pe fiecare componenta ce necesita oferta, ce s-a predat (net de retururi), ce da stocul si ce lipseste, plus lista de achizitie pe furnizor si exporturile PDF/CSV, fara preturi.

Pagina `/proiecte/{id}/situatie` (link in pagina proiectului: "Situatia proiectului", si butonul din sectiunea Componente).

1. Alege un proiect cu o oferta preluata (`/oferte/preluare`) care are produse legate de catalog. Deschide "Situatia proiectului": apare un tabel pe componenta cu necesar, predat, din stoc, deficit si stare (Acoperit / Partial / Lipsa / In afara stocului pentru metri si ore).
2. Fa o iesire pentru acel proiect (un produs din oferta), apoi un retur partial: la Actualizeaza, "Predat" creste cu iesirea minus returul, iar antetul componentei arata "predat X din Y" (doar bucati).
3. Un produs cu stoc negativ se socoteste cu stoc zero. Un produs predat proiectului dar care nu e in oferta apare in "In afara ofertei" (la nivel de proiect, pana cand iesirile se leaga de componente).
4. "Lista de achizitie" grupeaza deficitul pe ultimul furnizor din care s-a facut intrarea produsului; fara intrare cunoscuta, la "Furnizor necunoscut".
5. Butoanele "Export PDF" si "Export CSV" dau aceleasi date (CSV se deschide in Excel cu separator ;). In sectiunea Componente a proiectului, coloana "Predat" arata "X din Y" sau "fara oferta".
6. Rezervarile nu exista inca (taskul urmator); preturile nu apar nicaieri.

### 07.10.2026 - Componente pe proiect

**Ce s-a adaugat:** un proiect are **componente** = tipuri de sisteme din Nomenclator (vezi 12_Nomenclator.md). Se aleg la crearea proiectului (comutatoare in formular) si se gestioneaza pe pagina proiectului, sectiunea **Componente** (`ProjectComponentsSection`): adaugare dintr-o lista cu tipurile active inca nefolosite, stare simpla (Ofertata / In executie / Predata / Inchisa), scoatere = arhivare cu motiv (nu stergere), reactivare, comutator „Arata si arhivate”. Tabel nou `project_components` (migrarea 23). Jurnal exact: „Adăugare componentă proiect”, „Modificare stare componentă proiect”, „Arhivare componentă proiect”, „Reactivare componentă proiect” (inregistrate la proiect). Oferta, necesarul, predatul si deficitul se completeaza in taskurile urmatoare (oferte).

**Ce face acum:** Un proiect are componente (tipuri de sisteme) cu stare Ofertata/In executie/Predata/Inchisa; scoaterea unei componente o arhiveaza cu motiv si se poate reactiva.

**Flux:** creezi proiectul si bifezi sistemele -> pe pagina proiectului adaugi/scoti sisteme pe parcurs -> schimbi starea pe masura ce avanseaza -> un sistem renuntat se scoate cu motiv (poate fi reactivat).

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Pregatire: in Nomenclator exista cateva tipuri active (si unul inactiv) | - | |
| 2 | Beneficiar -> „Adaugă proiect” | In formular apare „Componente (sisteme)” cu cate un comutator pe tip activ (tipul inactiv lipseste) | |
| 3 | Bifeaza 2 sisteme si salveaza proiectul | Pe pagina proiectului sectiunea „Componente 2”, ambele „Ofertată” | |
| 4 | Creeaza un alt proiect fara nicio componenta | Sectiunea arata „Proiectul nu are componente” si lista de adaugare | |
| 5 | Adauga un sistem din lista „Adaugă un sistem” | Apare in tabel „Ofertată”, dispare din lista de adaugare; mesaj „Componenta a fost adăugată.” | |
| 6 | Schimba starea unei componente (Ofertată -> În execuție -> Predată -> Închisă) | Mesaj „Starea a fost modificată.”; la reincarcarea paginii starea ramane | |
| 7 | „Scoate” o componenta: fara motiv, apoi cu motiv | Fara motiv: eroare in dialog; cu motiv: dispare din lista activa, mesaj „…scoasă (arhivată)” | |
| 8 | Comutatorul „Arată și arhivate” | Componenta apare estompata cu „arhivată: <motiv>”, fara selector de stare, cu buton „Reactivează” | |
| 9 | „Reactivează” | Revine in lista activa cu starea pe care o avea | |
| 10 | Tipul scos reapare in lista de adaugare? | Nu (e deja in proiect, arhivat); adaugarea lui direct este refuzata cu „reactivează-o” | |
| 11 | Dezactiveaza un tip in Nomenclator care e folosit de un proiect | Ramane pe proiect; nu mai apare la alegeri noi | |
| 12 | Doua taburi pe acelasi proiect: schimba starea in primul, apoi in al doilea pe aceeasi componenta | Al doilea primeste „Componenta a fost modificată între timp…” si se reincarca | |
| 13 | /jurnal | Cele 4 actiuni exacte apar cu proiectul, componenta, schimbarea (stare veche -> noua) si motivul la arhivare | |
| 14 | Stergerea unui proiect fara miscari | Se sterge cu tot cu componente (nu raman randuri orfane) | |
| 15 | Ecran ingust | Tabelul si dialogul de scoatere nu ies din pagina | |

Cunoscut: daca adaugarea componentelor alese la creare esueaza (de ex. tipul a fost dezactivat intre timp), proiectul se salveaza oricum si componentele se adauga ulterior din pagina proiectului.

### 06.10.2026 - Cheia CUI

**Ce s-a adaugat:** Cheia de unicitate a CUI-ului ignora prefixul RO si spatiile.

**Ce face acum:** Un CUI scris cu sau fara „RO" sau cu spatii este recunoscut ca duplicat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Beneficiar nou cu CUI RO12345678; apoi 12345678 si "ro 12345678" | Mesaj de CUI duplicat | |
| 2 | Persoana fizica cu acelasi nume | Cheia "PF: nume" (doar persoana fizica) | |

### 07.10.2026 - Iesiri spre beneficiar

**Ce s-a adaugat:** Alegerea beneficiarului si a proiectului din combobox la iesire, cu referinta afisata in tabelul de miscari.

**Ce face acum:** La iesirea spre beneficiar alegi beneficiarul si proiectul din combobox, iar referinta (avizul) se vede in tabelul de miscari.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | La iesire spre beneficiar alege beneficiarul si proiectul din combobox | Merge; referinta (aviz) se vede in tabelul de miscari | |
| 2 | Pagina proiectului / echipamente | Iesirile apar la proiect | |

## Fluxul de baza

1. Beneficiar PJ cu preluare ANAF -> salvare.
2. Proiect -> observatie -> fisier.
3. Stergere beneficiar/proiect cu motiv; cu miscari legate se refuza sau se arhiveaza.
4. Doua sesiuni pe aceeasi observatie: blocarea editarii.

### 08.10.2026 - Preluare din ANAF: CUI inexistent

**Ce s-a schimbat:** ANAF raspunde cu HTTP 404 (si corpul obisnuit `notFound`) cand CUI-ul nu exista; aplicatia citea asta ca „adresa serviciului este gresita”. Acum se afiseaza „CUI-ul nu este inregistrat in ANAF. Datele se pot introduce manual.”; un 404 fara corpul ANAF ramane eroare de adresa.

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 1 | Beneficiar nou/editare, persoana juridica, CUI inexistent (ex. RO10000002), „Preia date din ANAF” | Mesajul „CUI-ul nu este inregistrat in ANAF...”, fara mentiune despre adresa serviciului | |
| 2 | Acelasi lucru cu un CUI real | Datele firmei se completeaza | |

### 08.10.2026 - Preluare din ANAF: reguli pe camp si versiunile configuratiei

**Ce s-a schimbat:** (1) Setari -> Preluare date ANAF: coloana „Folosit in” arata ce face fiecare camp (Formular / Avertisment / Doar informativ); „Cand ANAF nu trimite campul” are doua variante (Pastreaza valoarea din formular / Opreste preluarea), „Cand ANAF da alta valoare” are trei (Cere confirmare / Pastreaza valoarea mea / Ia valoarea din ANAF). (2) La „Preia date din ANAF”: campul gol se completeaza, valoarea egala nu schimba nimic, restul dupa regula; „Cere confirmare” deschide dialogul „Date diferite in ANAF” cu alegere pe fiecare camp. Implicit: denumire, adresa, cod postal = confirmare; telefon = pastrez valoarea mea; nr. registrului si CAEN = valoarea din ANAF. La furnizor nou (din factura sau la prima salvare) denumirea din ANAF o inlocuieste pe cea citita/tastata. (3) Versiunile configuratiei: coloana „Creata prin”, slider „Activa” (activeaza o versiune veche) si iconita de stergere (nu pentru versiunea activa; cere motiv; jurnal „Stergere versiune configuratie ANAF” / „Activare versiune configuratie ANAF”).

| Nr | Pas | Rezultat asteptat | Confirmat |
|---|---|---|---|
| 3 | Setari -> Preluare date ANAF: priveste tabelul de mapari | Coloana „Folosit in”; denumirea campurilor folosite nu se poate schimba; la campurile „Doar informativ” politica de actualizare e inactiva | |
| 4 | Beneficiar existent cu adresa modificata manual: „Preia date din ANAF” | Dialogul „Date diferite in ANAF”: valoarea ta, cea din ANAF, slider pe fiecare camp; fara slider pornit, „Aplica alegerea” nu schimba nimic | |
| 5 | Acelasi lucru cu telefonul scris de mana | Telefonul ramane al tau; mesajul spune „S-au pastrat valorile tale pentru: Telefon” | |
| 6 | Setari -> ANAF -> Versiuni: porneste sliderul „Activa” la o versiune veche | Versiunea devine activa (apare o versiune noua cu „Revenire la versiunea #N”), editorul se incarca cu ea | |
| 7 | Sterge o versiune inactiva (iconita cos, motiv) | Dispare din lista; versiunea activa nu are iconita activa; jurnalul o consemneaza | |
