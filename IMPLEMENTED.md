# Taskuri finalizate

Fisier separat de TODO.md (impartit la 28.09.2026, la cererea utilizatorului). Contine taskurile finalizate, in ordine cronologica: cel mai vechi primul, cel mai nou ultimul. Se completeaza doar prin append (taskurile noi finalizate se adauga la sfarsit; intrarile existente nu se modifica decat pentru corectii explicite).

> Intrarile mai vechi sunt in `docs/arhiva/IMPLEMENTED_pana_la_06.10.2026.md` (cauta dupa titlu cu rg).

## Finalizat la 05.10.2026 10:55 - Preluare factura: corectii si facilitati in wizard, motiv generat automat la editari, tabel Secpral/Emproium

- **Preluare factura, pasul 2:** campurile de cantitate, pret, valoare, valoare cu TVA, TVA si reducere care nu se citesc ca numar au fundal rosu; sub campul denumirii apare un singur mesaj cu iconita „!" pe rand („Atentie: unele valori din acest rand nu sunt numere (campurile cu fundal rosu). Corectati-le."). Campurile numerice accepta doar cifre si o virgula (`wwwroot/numeric-field.js`, plus aceeasi regula pe server in `EditCell`); un punct tastat devine virgula. Denumirea are un camp care creste in inaltime (`NameRows`), UM e coloana cea mai scurta, campurile sunt aliniate sus. Factura preluata se poate deschide intr-o fereastra detasabila (`Components/Pages/InvoiceViewer.razor`, `/produse/preluare-factura/factura/{id}`, doar pentru proprietarul sesiunii).
- **Starea nu se pierde intre pasi:** randurile din pasul 2 sunt refolosite la intoarcerea din pasul 1 sau 3 (`PickRow.SourceKey`: corectii, produs ales sau pregatit, descrieri); la fiecare intrare in pasul 2 catalogul se reciteste si randurile se potrivesc din nou. Pasul 3 nu mai are butonul „Pasul urmator".
- **Formularul de produs:** categoriile se reciteste la deschidere (corectie: un produs pregatit dupa crearea unei categorii in popup nu vedea categoria); cautarea imaginii pe internet dupa cod (popup cu rezultatele Google Images, la produs nou si la editare).
- **Categorii si subcategorii:** formularul „Adauga subcategorie" se deschide in categorie, sub ultima subcategorie, cu categoria deschisa si pagina derulata la camp.
- **Motiv generat automat la editare** (`ChangeReasonField` cu `AutoText`, `Services/ChangeReasonSummary.cs`): alegere prin radiobox intre sumarul modificarilor (o modificare pe linie, „Camp: vechi → nou", se reface la fiecare randare, o modificare adusa la valoarea initiala dispare) si un text scris; la produs, beneficiar, utilizator, vehicul, proiect, observatie de proiect, categorie/subcategorie si editarea unei miscari de stoc. Fara modificari, motivul automat da „Nu ai facut nicio modificare.". Dialogurile de stergere si deblocare raman cu campul clasic.
- **Eroarea la salvare in pasul 3:** declansatoarele din baza locala apartineau contului `blazorstoc_restore`, care nu avea SELECT pe `BlazorStoc`; s-a dat `GRANT SELECT` (cum cer deja `local-secrets/create-restore-account.sql` si `tools/setup-dev-environment.ps1`). Jurnalul aplicatiei noteaza acum si codul erorii de baza.
- **Motorul de facturi (OCR):** (1) un antet de coloana care incepe cu o fraza din dictionar nu mai este penalizat pentru lungime („Denumirea proauseior sau a serviciilor" citit prost de OCR ramane denumire); factura Secpral isi gaseste tabelul; (2) numarul curent se citeste si cu zgomot de OCR (`ParseRunningNumber`: „„1", „|2|", primul „1" citit ca „]", „|", „l"); (3) linia dintre doua randuri fara linii desenate se pune la jumatatea spatiului dintre ultima linie de text a randului de sus si prima linie a randului care incepe la numarul urmator (`Boundary` in `RowBands`); prima linie ramane cea de jos a capului de tabel. Emproium isi separa acum randurile 1-5 dupa numarul curent (inainte 1 si 2 erau lipite).
- **Unelte si teste:** `tests/BlazorStoc.InvoiceCorpus` primeste `--header` (liniile OCR, liniile de text si fiecare candidat de antet cu scorul); proiectul principal expune internele catre el (`InternalsVisibleTo`). Teste bUnit noi (pachetul `bunit`): `ComponentChecks`, `PickupWizardChecks`, `ProductGroupsChecks`, `ReasonSummaryChecks`; se pot rula singure cu `COMPONENT_CHECKS_ONLY=1`. Suita completa: 761 de verificari trec cu `INVOICE_CORPUS_DIR` pe un director gol; cu facturile reale din `D:\_BlazTest\Facturi furnizori` verificarea pe Emproium (valori OCR ilizibile pe randul 1, „SBT") ramane picata.
- **Neverificat in browser:** formularele cu motiv automat (in afara de o pagina de proba pentru aspect), editarea unei miscari de stoc (fara test automat), fereastra cu factura, filtrul de la tastatura in aplicatia reala (verificat pe o pagina de proba), popup-ul de cautare imagini, mesajele din pasul 2 pe o factura reala.

## Motor geometric pentru tabelul facturii, laborator OCR, recitirea numerelor (05.10.2026, ramura `experiment/ocr-geometrie`)

- **Doua motoare, unul implicit:** `Services/Invoices/InvoiceEngines.cs` (`InvoiceEngineOptions`: A = valorile intra in scor, B = geometric: grila tabelului intai, valorile nu intra in scor, sensul coloanelor dedus din numere). B este motorul aplicatiei (`InvoiceEngines.Default`). `InvoiceGridDetector.cs`: liniile de date (cel putin doua cifre aliniate), antetul (liniile de etichete de deasupra primului rand, fara randul de numere de coloane), coloanele din bordurile desenate, din linii gasite pe scanare sau din golurile dintre cuvinte, fara dictionar de etichete si fara pozitii presupuse.
- **Linii desenate:** `DrawnRules` (linii vectoriale din PDF-urile cu text), `FindRuledLines`/`FindFaintLines` (scanari, linii punctate sau palide), `JoinTilted` (fragmente ale unei linii usor inclinate), `RemoveBarcodeLikeRules`, `TableBottomFromRules`.
- **Laborator OCR** (`Invoices:Lab = true`, panou in `/produse/preluare-factura`): comparare cu referinte JSON (`<nume>.reference.json`, doar geometrie: antet, coloane, linii intre randuri; scor pe linii 0,5, coloane 0,3, numar de randuri 0,2, fara valori citite), „Salveaza ca referinta", magazie de sabloane pe fisiere (`FileInvoiceTemplateStore`). `tests/BlazorStoc.InvoiceCorpus`: `--compare`, `--engine A|B|both`, `--references`, `--write-reference`, `--images`, `--reread`, `--pickup`, `--overlay <dir>`; un fisier `exclude.txt` din directorul mostrelor (un nume pe linie, `#` comentariu) scoate fisiere din verificarile pe mostre reale si din unealta de corpus.
- **Citirea paginii:** o pagina cu strat de text care nu explica cerneala paginii (24 de cuvinte tiparite din browser in jurul unei imagini de factura: Facturis.ro) se citeste prin OCR (`UncoveredInk`: 0-18% cerneala neacoperita la paginile cu text real, 96-98% la cele cu imagine; prag 70%).
- **Randuri si antete:** rand de numere de coloane citit prost de OCR („o 1 = a i. 5 6"); casete cu linii desenate fara cifre (continuarea denumirii randului de sus, nota de sub tabel, antet repetat in interior); antet cu doua linii (etichete si, dedesubt, numarul coloanei): o pagina care repeta doar randul de numere continua tabelul si coloanele se asaza dupa numere (`FindNumberHeader`, `ColumnsFromNumbers`); in pasul 1 randul de numere nu se mai lipeste de primul rand si prima linie de separare sta sub el (`SeparatorsFromRows`, `ReadBySeparators`, `ColumnsOfFollowingPage`); coloanele din pasul 1 pastreaza lungimea celor din analiza (`TableGeometryCore`).
- **Recitirea numerelor pe celule** (`InvoicePdfReader.Rereading.cs`, `IInvoiceNumberRereader`, apelata in `InvoiceAnalysisService.AnalyzeAsync`): dupa ce tabelul e gasit, celulele numerice care nu sunt numar se decupeaza din pagina (fara liniile tabelului, doar linia de text proprie, coloanele paginii curente), se citesc cu Tesseract refolosit si doar cu caracterele unui numar; in randurile care nu se potrivesc aritmetic se citesc din nou cantitatea, pretul si valoarea si se alege combinatia la care cantitate x pret = valoare. Emporium: toate cele 5 randuri corecte; Telesystem 682100: 10 cantitati lipsa citite; Secpral: 523.38; Facturis: 250.00, 80.65, 32,00.
- **Raport:** `docs/RAPORT_CODEX_DETECTIE.md` (comparatie cu detectorul de celule inchise al aplicatiei `OcrTabel`, masurat pe 19 pagini din corpus; ce s-a preluat si ce nu).
- **Teste:** suita completa 821 de verificari trec cu `INVOICE_CORPUS_DIR` pe un director gol (`GridChecks.cs`, `OcrLabChecks.cs` noi; teste noi in `InvoiceChecks.cs`, `PickupWizardChecks.cs`, `InvoiceFixtures.cs`: pagina-imagine cu text real, scanari recitite, antet cu doua linii pe doua pagini, coloanele din pasul 1). Cu mostrele reale din `D:\_BlazTest\Facturi furnizori` verificarea pe scanarea Emproium ramane picata: tabelul si suma randurilor sunt corecte (2328.64), dar CUI-ul furnizorului si „Total fara TVA" din antet nu sunt recunoscute dupa OCR.
- **Limite cunoscute:** scrisul de mana (fisele SKM) nu e citit de Tesseract; blocurile suprapuse cu acelasi antet (SKM) nu sunt unite intr-un tabel; coloane neutilizate duplicate pe overlay la unele facturi (GS, Mondoplast, Telesystem 682100: antetul gasit pe grila nu se potriveste dupa text cu cel din sablon); `model-factura-proforma.pdf`: pasul 1 citeste 2 randuri din 3; Facturis.ro -2 (captura foarte blurata); antetul bilingv (`factura-fara-tva-usd+ron-engleza.pdf`, exclus din teste cu `exclude.txt`).
- **Neverificat in browser:** panoul de laborator si pasul 1 cu factura fictiva dupa ultimele modificari, recitirea numerelor pe o factura incarcata prin interfata.

- **Un singur motor de recunoastere a tabelului (05.10.2026):** motorul A (valorile citite intra in scor, tabelul cautat dupa etichete) a fost eliminat; ramane motorul geometric (fostul B): grila tabelului intai, valorile nu intra in scor, sensul coloanelor necunoscute dedus din cantitate x pret = valoare. Dispar `InvoiceEngines`/`InvoiceEngineOptions` si parametrul `options` din `InvoiceAnalyzer.Analyze`, `InvoiceTableReader.Detect/ReadRows`, `InvoiceTemplateEngine.Apply`, `InvoicePickupReader.Read` si `RereadNumbersAsync`; dispare scorul pe valori si reincercarea coloanelor pe ordine. Acelasi motor citeste atat sablonarea (Setari -> Facturi) cat si preluarea facturii. Laboratorul OCR (`Invoices:Lab`) pastreaza referintele si masurarea citirii, fara selector de motor; unealta de corpus nu mai are `--engine`. Sabloanele existente (SC TELESYSTEM SRL, SC General Security SRL) au fost sterse din baza de dezvoltare, fiind facute cu motorul vechi; se refac cu motorul nou. Teste: 819 verificari trec (fara facturile reale).

## Finalizat la 06.10.2026 10:20 - Categorii: anularea dupa mutarea unei subcategorii inchide formularul, categoria se alege direct in formular

- **Defect:** la editarea unei subcategorii mutate in alta categorie, „Anuleaza" deschidea avertizarea de modificari nesalvate, dar „Paraseste editarea fara salvare" nu inchidea formularul: dialogul este randat de `UnsavedChangesHost` (alta componenta), iar `ProductGroups.DiscardEditAsync` schimba starea fara sa ceara re-randarea paginii.
- **Corectie:** `DiscardEditAsync` apeleaza `InvokeAsync(StateHasChanged)` (`Components/Pages/ProductGroups.razor`). La cererea utilizatorului, categoria subcategoriei nu se mai alege dintr-o lista care se deschide: este un grup de optiuni (radiobox) afisat in formular, cu stil `category-choice` in `wwwroot/app.css`.
- **Teste:** `ProductGroupsChecks.cs`: testul vechi (cu `<select>`) adaptat si un test nou pentru scenariul „muta, anuleaza, paraseste"; reproducerea fara corectie confirmata.
- **Cunoscut, neschimbat:** `ProductMovements.DiscardEditAsync` are acelasi tipar (dialog de editare) si poate avea acelasi defect; nu a fost verificat.

## Finalizat la 06.10.2026 10:20 - Corectii la motorul de facturi: total fara TVA din subsol, C.U.I. citit ca C.U.|, moneda dupa sume, sabloane doar pe acelasi furnizor, coloane initial folosite

- **Emproium (scanare):** (1) in `InvoiceValues.Normalize` o bara verticala lipita de o litera cu punct pe o parte si de alta litera cu punct pe cealalta parte este litera I (OCR: „C.U.|." = „C.U.I.", „C.|.F." = „C.I.F."); (2) `InvoiceAnalyzer.PromoteTableTotal`: un „Total" singur de sub tabel devine „Total fara TVA" daca suma lui egaleaza suma randurilor sau daca sta sub coloana de valoare (doar cand nu s-a gasit un total fara TVA dupa eticheta); (3) `InvoiceAnalyzer.AssignLeadingTaxCode`: fara CUI de furnizor gasit, primul CUI fara parte, in ordinea de citire, este al furnizorului (facturi fara antet „Furnizor" sau cu antetul citit gresit de OCR; fisier „General 06.08.2026").
- **Moneda dupa sume:** `InvoiceValues.WithoutCurrency`; `InvoiceTemplateEngine.ReadField` citeste un camp numeric fara moneda („301.41 RON" -> „301.41"), iar analiza face la fel pentru totaluri (`InvoiceFieldFinder`).
- **Verificarea cu sabloane incrucisate** (`InvoiceChecks.RealSamplesAsync`) aplica un sablon pe fisierul din care a fost facut si pe fisiere cu acelasi CUI de furnizor si aceeasi sursa (nu mai amesteca furnizori cu alt aspect: Emproium si General).
- **Coloane initial folosite:** `InvoiceTemplateDraft.FromAnalysis` porneste toate coloanele gasite pe pagina ca folosite la import, si cele cu antet nerecunoscut („Taxa verde" la `905550.pdf` devine coloana cu numele ei); utilizatorul sterge sau dezactiveaza ce nu are nevoie.
- **Teste:** `InvoiceChecks.cs` (normalizare, coloane, serviciu); suita completa 837 de verificari trec, inclusiv cele 5 facturi reale si verificarea cu sabloane incrucisate.

## Finalizat la 06.10.2026 10:20 - Preluare factura: avertizare fara sablon, creare sablon in fereastra si aplicare, sabloane pentru utilizatori obisnuiti

- **Flux:** dupa citirea facturii, pagina cauta sablonul furnizorului (CUI sau aspect) si il foloseste; daca nu exista apare o avertizare cu furnizorul citit din factura (`no-template-warning` in `InvoicePickup.razor`), nu blocanta (citirea automata ramane): „Creeaza sablon pentru acest furnizor" deschide `InvoiceTemplateWorkbench` intr-o fereastra mare peste pagina, pe sesiunea de analiza deja deschisa (parametrul `ExistingSession`: factura nu se incarca a doua oara, sesiunea apartine paginii), sau lista „foloseste un sablon existent". La salvare (`SavedRecord`) fereastra se inchide, lista de sabloane se reincarca, sablonul nou este ales si factura este recitita cu el (`ApplyTemplate`). „Inchide fara sa creezi sablonul" lasa citirea automata.
- **Acces:** orice utilizator care poate gestiona produse poate crea, modifica, redenumi, activa si dezactiva sabloane (`InvoiceTemplateService`: `EnsureProductOperatorAsync`); stergerea ramane a administratorului. `/setari` se deschide si pentru rolul „Utilizator", care vede numai tabul Facturi si nu are butonul de stergere in lista de sabloane; meniul Setari apare si pentru el. Celelalte taburi (ANAF, notificari, harta) raman numai pentru administrator (ascunse in pagina).
- **Teste:** `PickupWizardChecks.TemplateFlowAsync` (avertizare, fereastra pe fisierul deja citit, salvare si aplicare, inchidere fara salvare, tab-urile din Setari pentru utilizator obisnuit), teste de serviciu in `InvoiceChecks.cs` (operator creeaza/modifica, doar administratorul sterge, fara drept la produse nu creeaza). Verificat in browser pe `905550.pdf` (preview 5087): avertizare, fereastra, salvare, sablon detectat 100%, incarcare repetata cu detectare automata, lista din Setari. S-a creat un sablon „SC TELESYSTEM SRL" (CUI 22460883) in baza folosita de preview-ul 5087 (`blazorstoc_test`), nu in baza exportata in `database/dev-data`.

## Finalizat la 06.10.2026 10:20 - Comutatoare On/Off in locul casetelor de bifat si selectie pe randuri (fost Task 2 din TODO)

- **Regula (permanenta, in `CLAUDE.md`):** nicio casuta de bifat; orice bifare este `Components/Shared/ToggleSwitch.razor` (aspectul „Activ / Inactiv" din lista de sabloane: `Value`/`ValueChanged` sau `@bind-Value`, `Compact` fara textul starii, `Mixed` pentru selectie partiala, `OnText`/`OffText`). Inlocuite in: lista de sabloane, editorul de sabloane (campuri, coloane, „Folosit la import", „Arata toate campurile"), preluare factura, Inventar si Preluare inventar (selectii), contracte si mentenanta, ANAF, utilizatori, sabloane de notificari, selectorul de proiect. Starea partiala nu mai foloseste JS (`wwwroot/checkbox-indeterminate.js` sters).
- **Alternative alese (fara intrebari ramase):** vizibilitatea straturilor de pe pagina de factura (camp antet, cap de tabel, coloane) = butoane cu `aria-pressed`; selectia pe randuri de tabel (Inventar subcategorii, Preluare inventar, Preluare factura) = `Components/Shared/RowSelect.razor` (marcaj rotund cu bifa) + clic oriunde pe rand (`tr.selectable-row`) + „Selecteaza tot" in antet; filtrele „Arata si contractele Off" raman comutatoare.
- **Neschimbate:** comutatoarele mari `.toggle-switch` deja existente (arata parola, coordonate, activ la pinuri de harta, curatare cheie, notificari - purge).
- **Teste:** suita completa 837 de verificari; `PickupWizardChecks` (selectia pe randuri). Verificat in browser: Preluare factura si Inventar (comutator, selectie pe categorii).

## Finalizat la 06.10.2026 12:30 - Furnizori (Administrare), facturi de furnizor si intrari in stoc legate de factura

Cerut de utilizator la 06.10.2026 (verificarea fluxului, apoi deciziile: facturile se implementeaza, intrarile sunt legate de factura sau libere, CUI negasit in ANAF se accepta manual cu avertizare, furnizori externi UE acceptati, stergere doar de administrator si doar fara facturi/miscari legate).
- **Registru de furnizori:** `Services/Suppliers.cs` (reguli, validari, surse de date), `MariaSupplierRepository.cs`, pagini `Components/Pages/Suppliers.razor` (`/furnizori`: tabel cu cautare, filtru „doar cei de verificat", iconite editeaza/sterge), `SupplierEditor.razor`, `SupplierDetail.razor` (`/furnizori/{id}`: date, sursa, facturile furnizorului); intrare in meniul Administrare si card in pagina principala. Cheia furnizorului este **CUI-ul** (cifrele, fara RO si zerouri; la alt stat UE codul de TVA cu prefix de tara), nu perechea nume + CUI; numele este doar o data. Doar denumirea si CUI-ul sunt obligatorii. Cifra de control a CUI-ului se verifica (prinde greseli de tastare si de OCR). Utilizatorul obisnuit adauga si editeaza; stergerea este numai a administratorului (arhivare cu motiv) si doar daca furnizorul nu are facturi, intrari prin facturi sau sabloane de factura (dupa CUI); pictograma de stergere este dezactivata cu explicatie. Tara si CUI nu se mai schimba cat furnizorul este folosit.
- **Sursa datelor (5 stari):** preluat din ANAF (nemodificat), preluat din ANAF si editat manual (modificarea oricarui camp preluat), introdus manual - ANAF indisponibil, introdus manual - CUI negasit in ANAF, introdus manual (furnizori UE, niciodata interogati). Un furnizor nou din Romania este verificat in ANAF la salvare: daca ANAF raspunde, formularul se completeaza si se afiseaza inainte de salvare; daca nu raspunde sau nu cunoaste CUI-ul, se salveaza ca manual cu motivul. „Preia din nou din ANAF" la editare face reverificarea (jurnal propriu). `AnafService.LookupCompanyAsync` intoarce acum `AnafLookupOutcome` (Found/NotFound/InvalidCui/Unavailable); „CUI in lista notFound" se deosebeste de o pana a serviciului; interogarea din formular are termen de 12 s.
- **Facturi:** tabelul `supplier_invoices` (furnizor, numar, data emiterii; unic pe furnizor + numar, fara spatii/litere mari/mici) si `stock_movements.invoice_id` (nul = intrare libera); migrarea 13 (si `archive_suppliers`, `archive_stock_movements.invoice_id`); `Services/SupplierInvoices.cs`, `MariaSupplierInvoiceRepository.cs`. Miscarile afiseaza factura si furnizorul (pagina de miscari a produsului) si jurnalul lor le numeste.
- **Preluare factura (wizard):** numarul facturii, CUI-ul furnizorului si data emiterii se verifica in **pasul 2**, in campuri editabile marcate „neverificat"; fiecare se confirma separat cu un comutator, iar editarea unui camp il readuce la „neverificat"; fara cele trei confirmari nu se trece la pasul 3 (care arata doar rezumatul). Furnizorul se gaseste dupa CUI; daca nu e in registru se poate adauga din fereastra (formular precompletat, ANAF interogat imediat; un CUI cu cifra de control gresita este semnalat si nu se trimite la ANAF). La finalizare factura se inregistreaza o singura data si toate intrarile i se leaga.
- **Preluare partiala si erori OCR:** o factura deja preluata (acelasi furnizor si numar, in orice scriere) nu se mai refuza: preluarea continua pe ea, se listeaza ce s-a preluat, produsele deja preluate sunt marcate si se sar daca nu se alege „se preia din nou". Daca numarul nu exista dar exista unul asemanator (confuzii OCR O/0, I/1, S/5 ..., sau o cifra in plus/minus) la acelasi furnizor si aceeasi data, se ofera factura existenta („Da, este factura ..."); pentru scanari se avertizeaza sa se verifice numarul.
- **Jurnal (actiuni exacte):** „Adaugare furnizor", „Modificare furnizor" (valoare veche si noua, sursa), „Reverificare furnizor ANAF", „Inregistrare factura furnizor", plus „Stergere" arhivata; tipuri `Furnizor` si `FacturaFurnizor`, in filtrele jurnalului; evenimentele furnizorului duc la `/furnizori/{id}`.
- **Teste:** `tests/BlazorStoc.Checks/SupplierChecks.cs` (reguli, formular cu ANAF in toate cele 4 feluri, wizardul: confirmari pe camp, preluare partiala, OCR, furnizor nou) si sectiunea MariaDB „Suppliers and invoices" din `MariaExtendedChecks.cs` (cheie unica, concurenta, surse, versiuni, jurnal, facturi, intrari legate, blocari la stergere, sabloane, arhiva).
- **Neverificat / ramas:** verificarea in browser cu ANAF real (disponibil / indisponibil / CUI necunoscut), selectarea furnizorului din registru in formularul de sabloane de facturi (acolo furnizorul se scrie tot ca text), reverificarea ANAF in lot a furnizorilor „de verificat", legarea intrarilor libere existente de o factura dupa fapt; vezi `docs/TESTE_RAMASE.md`.

## Finalizat la 06.10.2026 12:20 - Beneficiari: cheia unica a CUI-ului fara prefixul RO
- `BeneficiaryRules.IdentityKey` (`Services/Beneficiaries.cs`) foloseste pentru persoana juridica cifrele CUI-ului (`SupplierRules.CuiDigits`: fara RO, spatii sau zerouri la inceput), ca la furnizori: `RO123`, `ro 123` si `123` sunt acelasi beneficiar si dau mesajul de CUI duplicat. Persoana fizica isi pastreaza cheia `PF:` + nume.
- Randurile existente se rescriu la pornirea aplicatiei (`MariaSchemaMigrator.NormalizeBeneficiaryKeysAsync`, `UPDATE IGNORE`, idempotent, cu contul aplicatiei: contul de migrare nu are drept de UPDATE, deci nu a putut fi o migrare numerotata). O pereche care ar intra in conflict isi pastreaza cheia veche si ramane de curatat manual.
- Test: `SupplierChecks.Rules` („Beneficiaries: RO123 and 123 are the same key"). Neverificat pe MariaDB: crearea unui beneficiar cu CUI existent scris altfel, in browser.

## Finalizat la 06.10.2026 11:30 - Mișcări de stoc: avertizarea de modificări nesalvate peste dialogul de editare, dialogul se închide la părăsire; reguli de economie de tokeni

- **Defecte (verificate in browser pe `/produse/1/miscari`):** (1) avertizarea „Editarea nu a fost finalizata" se desena sub dialogul de editare al miscarii (ambele `delete-overlay`, `z-index: 20`, avertizarea fiind mai sus in pagina) si nu putea fi apasata; (2) dupa „Paraseste editarea fara salvare" dialogul de editare ramanea deschis (acelasi tipar ca la categorii: `Discard` apelat din alta componenta fara re-randare).
- **Corectie:** `UnsavedChangesDialog` are clasa `unsaved-overlay` (`z-index: 70` in `wwwroot/app.css`); `ProductMovements.DiscardEditAsync` si `DiscardAddAsync` apeleaza `InvokeAsync(StateHasChanged)`. Fara test automat (dependente multe; suprapunerea nu se vede in bUnit); verificat in browser inainte si dupa.
- **Task nou in TODO:** „Robustete la erori si la caderea aplicatiei pe server" (supervizor, jurnal in fisier, ErrorBoundary, /health, sarcini fara asteptare).
- **Reguli:** `CLAUDE.md` primeste „Economie de tokeni" (iesire filtrata, teste tintite, citiri pe intervale, un singur build, verificari in browser cu text, sesiune noua cand contextul nu mai e necesar).

## Finalizat la 06.10.2026 12:00 - Valoarea unui camp citit la dreapta etichetei se opreste la bara verticala

- **Defect:** in descrierea intrarii, „Moneda" aducea si „Pagina 1 din 1" (rand de factura „Moneda: RON | Pagina 1 din 1"): `InvoiceTemplateEngine.WordsRightOf` lua tot restul randului pana la un spatiu mare.
- **Corectie:** citirea se opreste la un cuvant „|" (bordura/linia citita de OCR ca token) - `Services/Invoices/InvoiceTemplates.cs`; sabloanele deja salvate dau valoarea corecta fara refacere (citirea se face la fiecare aplicare). Test in `InvoiceChecks.cs`; suita completa 899 de verificari trec.
- **Limita:** daca OCR nu citeste bara, valoarea tot preia restul randului (se ingusteaza campul pe pagina).

## Finalizat la 06.10.2026 15:24 - Legatura sablon-furnizor, recunoasterea furnizorului si imagini ale regiunilor la pasul 2

- **Furnizor legat de sablon:** `supplier_id` pe sabloane (migrarea 14, fara UPDATE pentru migrator; legarea sabloanelor existente se face cu contul aplicatiei), redenumirea furnizorului se propaga in sablon si in descrierea intrarii; popup „+ Adauga furnizorul" in salvarea sablonului (CUI-ul din campuri devine cel al furnizorului); „Leaga de furnizor" in lista de sabloane; actiuni de jurnal exacte (`RenameInvoiceTemplateSupplier`, `LinkInvoiceTemplateSupplier`, `AddSupplierAlias`, `RemoveSupplierAlias`).
- **Recunoastere unificata:** `SupplierRecognizer` (CUI, nume fara forma juridica, alias, potrivire aproximativa tolerata la OCR) cu metoda si incredere; alias-uri (tabel `supplier_aliases`, migrarea 15), jurnal de recunoastere (`supplier_recognitions`, migrarea 16, coloana „Sablon schimbat" migrarea 17, export CSV), alias dintr-un clic, alegere automata a sablonului, cache de registru pe circuit. Tabelele noi sunt in lista de backup/restore.
- **Motorul de citire:** „Seria X nr. Y" nu mai confunda seria cu numarul; datele cumparatorului nu se mai citesc (nici in sablon: au iesit meaningurile `buyer.*`, grupul „Beneficiar", si liniile din blocul cumparatorului).
- **Pasul 2 al preluarii:** CUI-ul este verificat de la sine cand furnizorul este in registru sau tocmai a fost adaugat; numarul si data raman de verificat, cu imagine decupata din PDF a regiunii din care s-au citit (`InvoiceRegionPicture`); randurile cu valori necitite ca numar arata imaginea randului, cu celulele incadrate.
- **Mediu local:** pasul 3 pica cu `ColumnAccessDenied` cand contul `blazorstoc_restore` (definer al declansatoarelor) nu avea SELECT; s-a dat din nou `GRANT SELECT` (ca in `create-restore-account.sql`). `tools/clear-suppliers-dev.sql` goleste furnizorii, facturile si miscarile legate (rulat de utilizator, doar baza de dezvoltare).
- **Verificari:** suita completa 915 de verificari trec, 0 esecuri; neverificat in browser: imaginea randului din tabel.

## Finalizat la 06.10.2026 - Dezvoltare doar pe Release

- `BlazorStoc.csproj`: `Configurations` = Release si configuratia implicita (sau Debug) devine Release; un `dotnet build` fara parametri construieste Release. Un `-c Debug` scris explicit nu poate fi blocat din proiect, deci regula ramane: nu se mai genereaza build Debug in aceasta faza (folderele Debug generate au fost sterse).

## Finalizat la 07.10.2026 08:54 - Pagina Facturi (consultare) in Administrare si pe pagina principala

- **Ce s-a implementat:** pagina `/facturi` (`Components/Pages/Invoices.razor`), vizibila tuturor utilizatorilor autentificati, cu intrare in meniul Administrare (intre Furnizori si Vehicule) si card pe pagina principala. Lista tuturor facturilor preluate (numar, furnizor cu link, data emiterii, intrari in stoc, preluata de, la data), casete de rezumat, filtre (text, furnizor, interval de date, doar fara intrari) si, la deschiderea unui rand, intrarile din stoc ale facturii.
- **Fisiere:** `Services/SupplierInvoices.cs` (`GetAllAsync` in `ISupplierInvoiceRepository`, `SupplierInvoiceSearch.Filter`), `Services/MariaSupplierInvoiceRepository.cs`, `MainLayout.razor`, `Dashboard.razor`, depozitul din memorie al testelor.
- **Decizii (utilizator, 07.10.2026):** etapa 1 doar consultare; ulterior mutarea pe alt furnizor si, la ștergere, blocarea facturilor cu intrari in stoc; pagina vizibila tuturor.
- **Verificari:** build Release reusit; testul filtrului trece, fara esecuri in suita; neverificat in browser (aspect, deschiderea intrarilor).

## Finalizat la 07.10.2026 - Facturi: corectare, mutare pe alt furnizor si stergere (administrator)

- **Ce s-a implementat:** in `/facturi`, administratorul poate corecta numarul, data emiterii si furnizorul unei facturi (panou cu motivare, text automat din modificari) si poate sterge o factura fara intrari in stoc (dialog cu motiv). O factura cu intrari in stoc nu se sterge (butonul spune de ce). Ceilalti utilizatori raman pe consultare.
- **Fisiere:** `Services/SupplierInvoices.cs` (`UpdateAsync`, `DeleteAsync`, `SameAs`, `Changes`, mesaje), `Services/MariaSupplierInvoiceRepository.cs` (tranzactie serializabila, verificare ca factura nu s-a schimbat intre timp, duplicat dupa `NumberKey`, numararea intrarilor sub blocare), `Components/Pages/Invoices.razor`, `AuditTrail.cs`, `AuditFilters.cs`.
- **Jurnal:** „Modificare numar factura”, „Modificare data factura”, „Mutare factura la alt furnizor” (cate un eveniment pentru fiecare fel de schimbare, cu valoarea veche si noua si motivul) si „Stergere” cu identificarea facturii si motivul; actiunile sunt in filtrul paginii Audit si in `IsCreateOrEdit`.
- **Verificari:** build Release reusit; teste noi (corectare, copie veche, numar duplicat, motiv lipsa, actiuni de jurnal, stergere blocata/permisa) trec, fara esecuri; neverificat in browser si pe MariaDB reala (SQL-ul din `UpdateAsync`/`DeleteAsync` nu are test de integrare).

## Finalizat la 07.10.2026 - Facturi: produsele preluate se vad intr-o fereastra, cu link catre pagina produsului

- Pagina `/facturi`: numarul facturii este link; la apasare se deschide o fereastra (stilul `delete-overlay`) cu produsele preluate din factura (produs, cantitate, data intrarii). Numele produsului duce la pagina de intrari a produsului (`/produse/{id}/miscari`). Randul expandabil si butonul sageata au fost scoase.
- Neverificat in browser (aspectul ferestrei).

## Finalizat la 07.10.2026 09:32 - Facturi: numarul facturii ca buton (popup cu produsele)

- Numarul facturii din /facturi este buton stilizat ca link (button.link-like in app.css), nu <a href="#">: linkul cu # era interceptat ca navigare si popup-ul nu se deschidea.
- Verificat de utilizator in preview: popup-ul cu produsele preluate se deschide.

## Finalizat la 07.10.2026 10:20 - Intrari libere, furnizor/factura pe pagina de miscari, atasare la factura, factura asteptata

- Pagina produsului: coloana FURNIZOR / FACTURA, filtre Cu factura/Libere/furnizor; formular de provenienta (intrare libera cu motiv, factura existenta, factura noua manual, Preia din PDF); migrarea 18 (free_entry_type, free_supplier_id, reference, supplier_invoice_lines); avertisment cu motiv la produs repetat pe factura si la depasirea cantitatii facturate (serviciu, jurnal 'Intrare dublata pe factura (confirmat)'); atasare/detasare intrari de factura (administrator, jurnal + istoric); fila 'Intrari fara factura' pe /facturi cu asociere in lot; 'Adauga produs manual' pe fereastra facturii; potrivire la preluarea facturii (leaga intrarea libera existenta); notificare 'Factura asteptata' pe grup furnizor+data cu sablon in Setari (prag implicit 7 zile inainte de termenul de 14 zile); contor pe pagina principala.

## Finalizat la 07.10.2026 10:29 - Drepturi migrator pe blazorstoc_test si verificare intrari libere

- Contului blazorstoc_migrator i s-au acordat din nou drepturile DDL pe blazorstoc_test; migrarea 18 aplicata; sectiunea MariaDB 'Free entries' trece (951 PASS, 0 erori).

## Finalizat la 07.10.2026 11:46 - Regularizare stoc negativ, ieșiri peste stoc, scenarii de utilizare intrari/iesiri

- Operatia RegularizeNegativeStockAsync (corectie la cantitatea reala din depozit sau 0), panou comun NegativeStockPanel in formularul produsului, preluarea facturii si dialogul de produs manual; banda de regularizare pe pagina produsului; ieșirea peste stoc permisa, marcata calculat (peste stoc), filtru si nota in formular, acțiuni de jurnal exacte; taburi in selectorul de proveniență. Teste: sectiunea MariaDB Usage scenarios (15 scenarii), verificari de componenta pentru panou si selector; gasit si corectat ValueExpression lipsa in panou.

## Finalizat la 07.10.2026 12:55 - Iesiri: jurnal pe destinatie, referinta, filtre, iesire dublata, folosire din vehicul peste cantitate

- Jurnal exact pe destinatie: Iesire spre beneficiar / spre vehicul, Vanzare generica, Corectie stoc, Iesire dublata (confirmat); cand iesirea e peste stoc, detaliile contin Peste stoc: N (ExitOverStock ramane doar pentru intrarile vechi); actiunile sunt si in filtrul Audit (si cele lipsite pana acum pentru intrari libere).
- Referinta (aviz, bon, numar predare) permisa si la iesire: camp in formular si editare, afisata in tabel, in istoric si in jurnal; fisiere: Services/StockMovements.cs, MariaStockMovementRepository.cs, ProductMovements.razor.
- Filtre pe iesiri: destinatie, beneficiar, vehicul (optiunile vin din iesirile produsului); StockMovementQuery si StockMovementPage extinse.
- Iesire dublata: aceeasi iesire (produs, cantitate, destinatie, beneficiar/proiect/vehicule, ziua) cere motiv (DuplicateExitWarningException, panou in formular).
- Folosirea dintr-un vehicul peste cantitatea din masina (beneficiar, vanzare, corectie) se inregistreaza si se marcheaza peste stoc (OverStockExits tine soldul pe vehicul); transferurile si restituirile raman verificate; verificarea de negativ pe vehicul compara cu starea anterioara (EnsureVehicleStocksNotWorseAsync), deci un vehicul deja negativ nu blocheaza alte mutari.
- Verificari: sectiunea MariaDB Exit flow (9 verificari), Usage scenarios si suita in memorie (926) trec; nu s-a verificat vizual in browser.

## Finalizat la 07.10.2026 13:06 - Director Teste utilizator: ghiduri de testare pe componenta

- Director "Teste utilizator" cu 11 fisiere pe componenta (index 00_CUM_SE_FOLOSESTE.md): pasi, rezultat asteptat si coloana Rezultat pentru modificarile recente (intrari/iesiri, facturi, furnizori, preluare factura, jurnal etc.) si fluxul de baza.

## Finalizat la 07.10.2026 13:27 - Lista De regularizat, cauza iesirii peste stoc, notificare, contor, inventar, reimprospatare fara licarire

- Cauza optionala a iesirii peste stoc (enum OverStockCause: Intrare neoperata / Stoc gresit in baza; migrarea 19, coloana stock_movements.over_stock_cause): camp in formularul de iesire si de editare, afisata in tabel, Istoric si jurnal; pastrata doar daca iesirea chiar depaseste stocul (altfel se sterge). Fisiere: Services/StockMovements.cs, MariaStockMovementRepository.cs, MariaSchemaMigrations.cs, Components/Pages/ProductMovements.razor.
- Lista De regularizat: tab nou pe /produse (Components/Shared/ToRegularizeTab.razor): produse cu stoc negativ dupa iesiri peste stoc, cu cauza, data primei iesiri neacoperite si vechimea; filtre pe cauza si vechime; actiunea Regularizeaza reutilizeaza NegativeStockPanel si RegularizeNegativeStockAsync. Calcul intr-o singura trecere (StockMovementRules.UnresolvedOverStock, MariaStockMovementRepository.ReadToRegularizeAsync, GetToRegularizeAsync in IStockMovementRepository).
- Contor pe pagina principala; sursa de notificari OverStockSource (Services/OverStockNotifications.cs, cheie stoc.iesiri-peste-stoc, termen = prima iesire + 14 zile, prag implicit 7 zile, se inchide singura); inventarul listeaza primele produsele cu stoc negativ.
- Reimprospatare automata fara licarire: ProductMovements.RefreshSilentlyAsync (fara spinner, redesenare doar daca s-au schimbat datele) si UserDetail (fara spinner); celelalte pagini aveau deja reimprospatare silentioasa.
- Decizii (utilizator): 4 verificari automate (cauza, lista, notificare, regularizare), suita completa doar la commit. Neverificat: aspectul in browser, preluarea inventarului pentru produse negative.

## Finalizat la 07.10.2026 13:36 - Operatia de iesire si iesirea multipla (cosul de iesiri)

- Operatia de iesire: orice iesire apartine unei operatii (coloana stock_movements.operation_id, migrarea 20; operatia are id-ul primei iesiri, o iesire simpla este o operatie cu o linie, miscarile vechi raman fara). Jurnalul arata Operatie: #id pe fiecare iesire.
- Iesire multipla: pagina /iesiri/multipla (Components/Pages/ExitOperation.razor; meniu Produse si buton pe pagina Produse): destinatie, data si referinta comune, mai multe produse cu cantitate, sursa (depozit sau masina) si descriere; ExitDestinationPicker primeste ShowSource=false. Previzualizare (PreviewExitOperationAsync: aceleasi verificari intr-o tranzactie anulata) cu rezumat al depasirilor de stoc (cauza optionala) si al iesirilor repetate (motiv), apoi CreateExitOperationAsync: tot sau nimic, produsele blocate in ordinea id, mesajele cu numarul liniei.
- Refactorizare: CreateAsync foloseste InsertMovementAsync/RecordCreatedAsync (aceeasi logica pentru o miscare si pentru fiecare linie a operatiei).
- Unealta de teste: grupul CHECKS_ONLY=maria (run-checks.ps1 -Mode maria -Section ...) ruleaza numai sectiunile MariaDB, nu toata suita in memorie.
- Verificari: 4 verificari noi (grupare + jurnal, tot sau nimic, previzualizare fara salvare, linie repetata cu motiv si linie peste stoc); sectiunile Exit flow, Usage scenarios, Free entries trec. Neverificat in browser.

## Finalizat la 07.10.2026 13:46 - Bon de consum, storno de operatie, retur legat de iesire si consum net

- Bon de consum / aviz de predare: PDF al unei operatii de iesire (Services/ConsumptionNotePdfWriter.cs, acelasi font PT Sans ca la inventar), servit la /api/iesiri/{operatie}/bon.pdf (autentificat); buton Bon pe randul iesirii si link dupa salvarea iesirii multiple. Contine destinatia, beneficiarul cu CUI, proiectul, referinta, produsele cu cantitate si sursa, casete de semnatura, marcajul OPERATIE STORNATA cand e cazul.
- Storno de operatie: VoidExitOperationAsync (migrarea 21: voided_utc, void_reason, voided_by): toate iesirile operatiei se marcheaza stornate cu motiv, stocul se reface, cantitatile din masini se calculeaza fara randurile stornate (toate interogarile relevante exclud voided_utc), refuz daca operatia are retururi sau daca o masina ar ramane negativa; iesirile stornate raman ca urma (taiate in tabel, fara editare/stergere), istoric pe fiecare rand, jurnal Stornare operatie de iesire. Butonul StorneazÄƒ pe pagina produsului.
- Retur legat de iesire: intrarea libera Primit de la beneficiar poate fi legata de o iesire spre beneficiar (return_of_movement_id): partial permis, nu depaseste ce a ramas, jurnal Retur de la beneficiar; lista de iesiri returnabile in EntryOriginPicker. Consum net (iesiri minus retururi) pe pagina proiectului si a beneficiarului (Components/Shared/NetConsumptionSection.razor; GetNetConsumptionAsync).
- Iesirile din transferurile de pe pagina vehiculului primesc si ele o operatie (id-ul primei linii).
- Verificari: 4 verificari noi (storno, retur partial, consum net, bonul generat citit cu PdfPig) in sectiunea Storno, return, net consumption, consumption note; sectiunile Exit operation, Exit flow, Usage scenarios, Free entries, To regularize si testele de componente trec. Neverificat in browser (aspect bon, butoane, dialog storno, sectiunea Consum net).

## Finalizat la 07.10.2026 13:55 - Nomenclator: Tipuri de sisteme

- Nomenclator Tipuri de sisteme: pagina /nomenclator (Components/Pages/SystemTypes.razor, doar administrator; intrare in Administrare si card pe pagina principala): adaugare, redenumire, ordine (sus/jos), Activ/Inactiv (fara stergere), denumiri alternative (CCTV = TVCI). Nu exista nomenclator de unitati de masura (decizie utilizator).
- Fisiere: Services/SystemTypes.cs (reguli: cheie fara litere mari/mici, diacritice si separatori, valabila pentru denumiri si denumiri alternative), Services/MariaSystemTypeRepository.cs (versiune pe fiecare tip, tranzactii serializabile, scriere doar pentru administrator, citire pentru operatori; FindAsync dupa nume sau denumire alternativa), migrarea 22 (system_types, system_type_aliases).
- Jurnal exact: Adaugare tip de sistem, Modificare denumire, Activare, Dezactivare, Schimbare ordine, Adaugare/Stergere denumire alternativa; entitatea TipSistem in filtrele jurnalului, cu link catre /nomenclator.
- Verificari: 4 verificari noi (unicitate, denumiri alternative, dezactivare/ordine/versiune veche, jurnal), testele de componente trec. Neverificat in browser.

## Finalizat la 07.10.2026 14:00 - Componente pe proiect (tipuri de sisteme, stari, arhivare)

- Componente pe proiect: un proiect are componente = tipuri de sisteme din Nomenclator, alese la crearea proiectului (comutatoare in ProjectEditor, adaugate dupa salvare) si gestionate pe pagina proiectului in sectiunea Componente (Components/Shared/ProjectComponentsSection.razor): adaugare din tipurile active nefolosite, stare simpla (Ofertata, In executie, Predata, Inchisa), scoatere = arhivare cu motiv (nu stergere), reactivare, comutator pentru arhivate.
- Fisiere: Services/ProjectComponents.cs, Services/MariaProjectComponentRepository.cs (versiune pe componenta, tranzactii serializabile, drepturi ca la proiecte), migrarea 23 (project_components, FK cascada la stergerea proiectului, RESTRICT spre tipul de sistem, unic pe proiect + tip). Serviciile se rezolva la cerere (IServiceProvider) ca sa nu se strice paginile in teste.
- Jurnal exact, inregistrat la proiect: Adaugare componenta proiect, Modificare stare componenta proiect, Arhivare componenta proiect (cu motiv), Reactivare componenta proiect; filtre si link catre proiect.
- Neimplementat inca (taskurile urmatoare): oferta, necesarul/predatul/deficitul pe componenta, eliberarea rezervarilor la inchidere/scoatere. Cunoscut: componentele alese la creare se adauga dupa salvarea proiectului; la esec proiectul ramane si se adauga ulterior.
- Verificari: 4 verificari noi (adaugare cu refuz tip inactiv/repetat, stare + versiune veche, arhivare cu motiv, reactivare, jurnal); testele de componente trec. Neverificat in browser.

## Finalizat la 07.10.2026 14:17 - Oferte: sabloane de devize-oferta xlsx

- Modul Oferte, sabloane de devize-oferta (.xlsx): pagina /oferte/sabloane (Components/Pages/OfferTemplates.razor; intrare Oferte in meniu si card pe pagina principala). Lista de sabloane, editor cu previzualizarea foii (litere de coloana, coloanele alese colorate), propunere automata din fisierul de proba, Verifica pe fisier, activ/inactiv, stergere (administrator).
- Cititor xlsx propriu, fara biblioteca (Services/Offers/XlsxReader.cs): foi, texte comune/bogate/inline, numere, celule imbinate (valoarea in celula stanga-sus); .xls vechi nu se citeste (mesaj). Sablon (Services/Offers/OfferTemplates.cs): campurile de antet dupa eticheta (la dreapta sau sub ea), tabelul dupa etichetele coloanelor din antet (repetat pe sectiune), coloane alese dupa litera (Nr., Tip produs, Denumire, Unitate, Cantitate; preturile si TVA nu se citesc), sectiuni importate/neimportate dupa nume, randuri ignorate (Total, Fara TVA; potrivire pe cuvinte, Totalizator nu e ignorat), unitate folosita doar ca sa se recunoasca liniile pe bucati (restul: in afara stocului), alegerea automata a sablonului dupa etichete.
- Persistenta: Services/Offers/MariaOfferTemplateRepository.cs, migrarea 24 (offer_templates, definitia ca JSON, versiune pe sablon, nume unic fara litere mari/mici si diacritice); jurnal exact: Creare, Modificare (cu ce s-a schimbat), Activare, Dezactivare, Stergere sablon oferta; entitatea SablonOferta in filtre, cu link.
- Verificat pe cele 5 oferte reale .xlsx de proba din D:\_BlazTest (OFFER_SAMPLES_DIR, nu sunt in repository): un sablon propus din prima le citeste pe toate. RaportOferta Efr.xls este in format vechi (neacceptat, cum cere taskul: initial doar xlsx).
- Verificari: 4 verificari in memorie (cititor, aplicare sablon, sectiune necunoscuta/coloane propuse, alegere dupa etichete; fixtura generata in teste) + 2 pe MariaDB (salvare/refuz/jurnal, modificare/versiune veche/dezactivare); testele de componente trec. Neverificat in browser (editorul).

## Finalizat la 07.10.2026 14:29 - Oferte: preluarea devizului-oferta (asistent, beneficiar, proiect, componenta, linii, revizii)

- Preluarea devizului-oferta: asistent in 5 pasi /oferte/preluare (Components/Pages/OfferPickup.razor, meniu Oferte): fisier + sablon ales dupa etichete, beneficiar, proiect + componenta, linii, rezumat. Beneficiarul se recunoaste dupa denumiri alternative din oferte anterioare sau se propune dupa asemanare (OfferBeneficiaryMatcher: forme juridice ignorate, la persoane fizice se arata CUI-ul), cu cautare in toti beneficiarii si â€žBeneficiar nouâ€ in fereastra cu formularul existent (BeneficiaryEditor primeste InitialName). Denumirea din oferta se poate retine ca denumire alternativa.
- Proiect existent sau nou (titlul ofertei), componenta din categoria ofertei prin Nomenclator (tip de sistem sau denumire alternativa); componenta se adauga la proiect sau se reactiveaza. Liniile in bucati: potrivire cu catalogul pe tot textul denumirii (OfferProductMatcher: codurile de model cantaresc triplu, numele intreg continut = 100%), legaturi retinute din oferte anterioare (offer_line_matches), produs nou prin formularul existent de produs, â€žde achizitionatâ€ implicit; liniile cu alta unitate sunt marcate â€žin afara stoculuiâ€ (stocul tine doar bucati).
- Revizii: acelasi numar de oferta = revizia urmatoare, cu diferentele afisate (adaugate, scoase, cantitate schimbata; OfferDiffRules); iesirile existente ale proiectului nu se leaga automat. Persistenta: Services/Offers/MariaOfferRepository.cs (oferta si liniile intr-o tranzactie), migrarea 25 (offers, offer_lines, offer_line_matches, beneficiary_aliases). Jurnal la proiect: Preluare oferta, Revizie oferta (cu diferentele), Legare linie oferta de produs (cate una pe linie), Adaugare denumire alternativa beneficiar.
- Verificari: 1 verificare in memorie (potrivire produse, beneficiari, diferente) + 3 pe MariaDB (preluare cu proiect/componenta/linii/jurnal, revizie cu diferente si potriviri retinute, refuzuri); testele de componente trec. Neverificat in browser (asistentul), iar proiectul/componenta se creeaza chiar la â€žPreia ofertaâ€, imediat inaintea ofertei.

## Finalizat la 07.10.2026 14:43 - Situatia proiectului pe componente

- Pagina /proiecte/{id}/situatie: pe fiecare componenta si linie din ultima revizie a ofertelor, necesar, predat (iesiri minus retururi, repartizat pe liniile care cer produsul), din stoc (stoc negativ = 0), deficit si stare (Acoperit/Partial/Lipsa/In afara stocului); produsele predate care nu sunt in oferta apar la "In afara ofertei" la nivel de proiect (iesirile nu sunt inca legate de componente); lista de achizitie grupata pe ultimul furnizor al produsului (factura sau intrare libera), fara pret.,Export PDF /api/proiecte/{id}/situatie.pdf si CSV /api/proiecte/{id}/situatie.csv; link in ProjectPage; coloana "Predat X din Y" in ProjectComponentsSection. Fisiere: Services/ProjectSituation.cs (reguli pure, CSV), MariaProjectSituationReader.cs, ProjectSituationPdfWriter.cs, Components/Pages/ProjectSituationPage.razor, Program.cs. Fara migrare noua, fara eveniment de jurnal (citire).,Verificari: -Group offers (3 verificari noi pure) si -Mode maria -Section "Offer import" (o verificare noua cu iesire, retur, stoc) trecute; neverificat in browser.

## Finalizat la 07.10.2026 14:55 - Legarea iesirilor de componente si regula de scoatere a componentei

- Migrarea 26: stock_movements.project_component_id, outside_offer, component_settled. O iesire spre beneficiar cu proiect se leaga de componenta: aleasa de utilizator (selectorul Componenta din formularul de iesire si din Iesire multipla, ExitComponentChoice.razor), automat daca produsul e in oferta unei singure componente, "in afara ofertei" daca proiectul are oferte dar produsul nu e in niciuna, refuzata cu mesaj daca e in mai multe si nu s-a ales. Alegerea explicita acopera si produsul echivalent.,Scoaterea componentei cu iesiri nelamurite (ramas la beneficiar > 0) e blocata (ProjectComponentHasExitsException); dialogul din ProjectComponentsSection cere pe fiecare produs: retur in depozit (intrare legata de iesire), ramas la beneficiar, mutat pe alta componenta sau consumat; patru actiuni noi de jurnal (AuditActions.ComponentExit*, in filtru). Situatia proiectului foloseste legatura pentru predat/in afara ofertei pe componenta. Intrarile NU se leaga de oferta (ramane nefacut).,Verificari: -Mode maria -Section "Offer import" (11 trecute, 3 noi: legare automata/in afara ofertei, produs in mai multe componente + blocare, lamurire cu cele 4 optiuni), "Storno" si "omponent" trecute; migrarea 26 aplicata pe blazorstoc_test; neverificat in browser.

## Finalizat la 07.10.2026 15:13 - Rezervari pe proiect si avertismente la iesire

- Migrarea 27: project_reservations (proiect, componenta optionala, produs, cantitate); rezervarea nu schimba stocul, scade stocul liber = stoc - rezervari (niciodata sub zero). Services/Reservations.cs (reguli Free/Touched, ReservationChoice, ReservationWarningException), MariaReservationRepository.cs (Reserve cu capToFree = "Rezerva cat se poate", Reduce cu motiv), 6 actiuni noi de jurnal (Rezervare stoc, Scadere rezervare, Eliberare rezervare, Rezervare consumata prin iesire, Scadere rezervare la iesire, Rezervare eliberata la scoaterea componentei).,Iesirea din depozit spre beneficiar/vanzare consuma intai rezervarea proiectului ei; restul se compara cu stocul liber si, daca ar lua bucati rezervate de alte proiecte, arunca avertisment (nu blocheaza): continua fara sa atinga rezervarile, scade rezervarea unui proiect (cantitate + motiv) sau renunta; Iesirea multipla arata avertismentele in rezumatul de previzualizare, pe linie (ReservationWarningChoice.razor). Scoaterea componentei elibereaza rezervarile ei.,Situatia proiectului: coloanele Din care rezervat si Intrat pt. oferta, butoane Rezerva cat se poate, rezervare manuala, lista rezervarilor cu scadere; pagina produsului: Rezervat/stoc liber (ProductReservationsInfo), propuneri de rezervare dupa intrare (ReserveSuggestions), intrarea poate fi legata de oferta (EntryComponentChoice, project_component_id pe intrare).,Verificari: -Mode maria -Section "Reservations" (9 trecute: rezervare/stoc liber, consum la iesire, avertisment si scadere, eliberare la componenta, doua iesiri simultane), -Group offers (1 verificare noua pura), "Offer import" si "Storno" trecute; migrarea 27 aplicata pe blazorstoc_test; neverificat in browser. Nefacut: legarea intrarii de oferta si propunerile in fluxul de preluare a facturii (ramas ca task in TODO).

## Finalizat la 07.10.2026 15:17 - Rezervari si legarea de oferta la preluarea facturii

- Selectorul "Pentru oferta" (EntryComponentChoice, refactorizat cu ComponentId/ComponentIdChanged) in pasul 3 al preluarii facturii (InvoicePickup, camp ComponentId pe rand, trimis ca ProjectComponentId la intrare) si in InvoiceManualEntryDialog; dupa finalizarea preluarii, propunerile de rezervare (ReserveSuggestions) pentru fiecare produs preluat. Produsele noi din factura nu au selectorul (nu pot fi in oferta).,Verificari: -Mode maria -Section "Offer import" (13 trecute, o verificare noua: intrare legata de componenta, componenta scoasa refuzata, aparitia in situatie ca Intrat pt. oferta, propuneri nevide); neverificat in browser.

## Finalizat la 08.10.2026 08:45 - Completari stoc: iesire rapida, Unde sunt bucatile, completare vehicul la nivel, stoc minim, export consum, notificari de stoc

- Iesire rapida: butonul de pe fiecare rand din lista de produse deschide formularul de iesire multipla intr-un dialog (ExitOperation cu parametrii Embedded, InitialProductId, OnClosed; dialogul in Home.razor).
- Unde sunt bucatile: cardul ProductWhere pe pagina produsului (depozit, fiecare masina, predat pe beneficiar/proiect = iesiri minus retururi); IProductPlacementReader / MariaProductPlacementReader.
- Completare vehicul la nivel: tabelul vehicle_target_levels (migrarea 28), IVehicleTargetRepository / MariaVehicleTargetRepository, sectiunea Nivel tinta pe VehicleEquipmentPage; Completeaza la nivel deschide iesirea multipla cu masina si liniile lipsa (parametrul ?vehicul=); jurnal Setare/Eliminare nivel tinta vehicul.
- Stoc minim: tabelul product_min_stock (migrarea 29), ProductMinStockField pe cardul produsului, notificarea Stoc sub minim (MinStockSource; expira la data ultimei miscari sau a setarii minimului, prag implicit 0 zile; stocul comparat este cel total); jurnal Setare/Eliminare stoc minim.
- Notificari noi: Rezervare fara miscare (StaleReservationSource, 30 de zile fara modificare) si Deficit la un proiect cu termen apropiat (ProjectDeficitSource: termen setat pe pagina situatiei proiectului, tabelul project_deadlines, deficit = lista de achizitie nevida; prag implicit 14 zile); jurnal Setare/Eliminare termen proiect. Sabloanele se creeaza ca la celelalte surse (Setari -> Notificari).
- Export consum: pagina /consum (meniul Produse), filtre beneficiar, proiect, perioada, tabel si CSV la /api/consum.csv (MariaConsumptionReader, ConsumptionExportRules.ToCsv).
- Verificari: build Release; -Mode components 185 trecute; -Mode maria 1518 trecute (sectiuni noi: Vehicle target levels, Stock alerts; verificare noua in Reservations); migrarile 28 si 29 aplicate pe BlazorStoc, tabelele create direct pe blazorstoc_test.

## Finalizat la 08.10.2026 08:57 - Aliniere estetica in formulare si liste derulante fara contur gros

- wwwroot/app.css: comutatoarele din .editor-grid/.anaf-grid stau pe linia casetelor (min-height 39px, align-self end); randurile .filters au casetele pe aceeasi linie (align-items flex-end, ex. filtrele din Facturi); select:focus fara contur gros, doar bordura verde subtire.
- Reguli noi in CLAUDE.md (elemente alaturate in formulare; liste derulante fara contur gros).
- Verificari: parcurgere automata cu script in browser a formularelor si filtrelor din 11 pagini (editoare deschise), fara nealiniere ramasa; ANAF/Notificari/Utilizator confirmate vizual de utilizator.

## Finalizat la 08.10.2026 09:02 - Campuri de editare active fara contur gros

- wwwroot/app.css (reguli comune la sfarsit): input/select/textarea la focus fara outline, cu bordura verde 1px si umbra 1px; .search-box si campul interior evidentiate pe container (:focus-within); .clipboard-zone/.file-drop/.mark-field la fel; butoane si linkuri cu inel de 2px la :focus-visible; forced-colors pastreaza conturul sistemului.
- Regula in CLAUDE.md (inlocuieste regula doar pentru select).
- Verificat in browser pe Jurnal: campul de cautare nu mai are inel exterior (computed style); celelalte pagini de verificat vizual.

## Finalizat la 08.10.2026 09:04 - Pagina principala: intrari Mentenanta si Notificari cu semnalizare

- Dashboard.razor: carduri Mentenanta (/mentenanta) si Notificari (/notificari) in ordinea din meniu; cardul Notificari are data-notification-nav si triunghi ⚠, rosu cand IExpiryNotificationService.AlertCountAsync > 0 (aceeasi sursa ca meniul).
- wwwroot/notification-watch.js: apply() actualizeaza toate elementele data-notification-nav (meniu + card) la sondajul din 60 s; app.css: stil rosu pentru cardul .nav-alert.
- Decizie: Mentenanta nu semnaleaza separat (notificarile de mentenanta sunt deja in Notificari).
- Verificari: Release publicat; in browser, cu raspuns simulat count=3, meniul si cardul devin rosii fara reincarcare; fara alerte nu apare semnul.

## Finalizat la 08.10.2026 09:05 - Sabloane de facturi: scanari slabe si linii desenate (inchis, deja implementat)

- Verificat in cod la 08.10.2026: taskul era depasit de motorul geometric din 05.10.2026.
- 5.1 contrast: InvoicePdfReader (calea OCR) intinde contrastul cand InkContrast < FaintInkContrast (varianta "contrast stretched" si "stretched, without ruled lines", aleasa doar daca citeste mai bine); test cu scanare palida reala in Program.cs (~linia 1716).
- 5.2 linii desenate: InvoicePdfReader.DrawnRules (PdfPig, curse vectoriale si dreptunghiuri subtiri) si FindFaintLines (scanari) alimenteaza grila tabelului (InvoiceGridDetector, GridChecks.cs).
- Nu s-a scris cod nou; scanarea reala palida suplimentara ramane la N28 din docs/TESTE_RAMASE.md.

## Finalizat la 08.10.2026 09:09 - Robustete la erori: jurnal in fisier, /health, ErrorBoundary, sarcini fara asteptare

- Services/Robustness.cs: RollingFileLoggerProvider (logs/blazorstoc-yyyyMMdd.log, avertismente si erori cu stack trace, retentie 14 zile configurabila), handlere AppDomain.UnhandledException si TaskScheduler.UnobservedTaskException, Task.FireAndForget(logger, descriere); AddFileLogging in Program.cs; logs/ in .gitignore.
- Program.cs: /health anonim (SELECT 1; 200 sau 503 fara detalii), /health/live ramane.
- MainLayout.razor: ErrorBoundary in jurul @Body cu buton Reincarca pagina, refacut la schimbarea paginii. FireAndForget aplicat in paginile cu sondaje periodice, Home (bataia de inima), layout si UnsavedChangesHost; nu exista async void in cod.
- Supervizor: doar reteta (Task Scheduler/NSSM, systemd) in docs/SUPERVIZOR_SI_JURNAL.md; nu s-a instalat niciunul (locul instalarii neclarificat).
- Verificari: tests RobustnessChecks (4 PASS), /health 200 in preview, pagina Produse se afiseaza normal.

## Finalizat la 08.10.2026 09:24 - Robustete la erori: completari dupa verificarea in browser (08.10.2026)

- Verificat: cu MariaDB oprita /health da 503 si /health/live 200; aplicatia pornea gresit cu baza oprita (cadea in MariaArchiveSchema.InitializeAsync) - corectat in Program.cs (MySqlException la pornire se logheaza, aplicatia porneste; lipsa tabelelor ramane fatala); la repornirea bazei /health revine 200 fara repornirea aplicatiei.
- Verificat: ErrorBoundary-ul din layout NU vedea erorile paginilor (layout static, fiecare pagina este insula interactiva proprie); inlocuit cu Components/Shared/PageBoundary.razor, pus in jurul continutului celor 40 de pagini. O eroare intr-o componenta copil a paginii arata "Eroare in aceasta pagina" fara sa inchida circuitul (verificat); o eroare in codul paginii insesi (handler/randare proprie) inchide in continuare circuitul (limitare Blazor Server, bannerul "Conexiunea intrerupta" ramane).
- Verificat: jurnalul in fisier nu scria (ClearProviders din Program.cs il sterge) - AddFileLogging mutat dupa; acum scrie avertismentele/erorile (confirmat cu baza oprita).
- Verificat: supervizor simulat (script de repornire) - proces omorat, repornit in ~5 s, /health 200. Blocarea de produs luata, server omorat: blocarea expira singura la 90 s si produsul se poate edita din nou.
- Teste: RobustnessChecks 7 PASS (jurnal, retentie, FireAndForget, fiecare pagina are PageBoundary, fara "_ =" la sondajele periodice); suita componente 189 PASS.
- Tinta de instalare: container (docs/SUPERVIZOR_SI_JURNAL.md, restart policy + healthcheck).

## Finalizat la 08.10.2026 10:15 - Backup pe NAS: configurare in Setari, copiere dupa backup, backup programat

- Services/NasBackup.cs: MariaNasBackupStore (setari, parola criptata cu Data Protection), NasBackupCopier (test conexiune, copiere sub nume temporar, verificare marime si SHA256, redenumire; nimic nu se sterge pe NAS), NetworkShareConnection (WNetAddConnection2 pe Windows), NasCopyingBackupService (decorator: copiaza dupa orice backup reusit, nu si pre-restaurare), SystemAccess, BackupScheduler (backup zilnic la ora aleasa, retry la 15 min).
- Migrarea 30 backup_nas_settings (si in MariaArchiveSchema.MigratedTables); BackupKind.Scheduled si Manual (pagina Restaurare); actiuni de jurnal Modificare configurare backup NAS / Testare conexiune NAS / Copiere backup pe NAS.
- UI: Components/Shared/BackupNasSettings.razor, tab Backup NAS in Setari (administrator). CSS: .catalog-body (margine interioara in carduri), regula in CLAUDE.md; corectat si in Suppliers (recunoasterea furnizorului), OfferTemplates (editor) si ConsumptionExportPage.
- Verificari: NasBackupChecks 7 PASS (reguli de cale, copiere fisier, decorator), sectiune Maria 8 PASS (parola criptata in DB, doar administrator, jurnal fara parola). Testul pe NAS-ul real cu EspStoc nu s-a facut (parola nu se introduce de agent).

## Finalizat la 08.10.2026 11:01 - Backup NAS: corectii dupa testul pe NAS-ul real (08.10.2026)

- Confirmat de utilizator: Fa backup acum face pachetul si il copiaza pe NAS (EspStoc, \\192.168.100.50\BackupStocDepozit); backup-ul programat a urcat si pachetele vechi lipsa.
- Corectat: conectarea la share cu LogonUser NEW_CREDENTIALS + impersonare (nu mai da eroarea 1219); tools/start-preview.ps1 trimite caile mariadb-dump/mariadb (fara ele orice backup esua); Fa backup acum copiaza numai pachetul nou (butonul Copiaza pachetele lipsa urca restul); mesajul unui backup esuat spune cauza reala (BackupRules.FailureMessage); pagina Setari ramane pe loc cat ruleaza backup-ul (data-operation-driver in maintenance-watch.js) - inainte era mutata pe pagina de asteptare si operatia se anula.
- De verificat la instalarea pe server: Database:MariaDumpExecutablePath si MariaClientExecutablePath setate la calea reala; backup programat zilnic pe ceas real; cazurile de eroare (parola gresita, NAS oprit).

## Finalizat la 08.10.2026 11:15 - Notificari pentru backup lipsa si copie pe NAS lipsa

- Services/BackupAlerts.cs: sursele de expirare BackupMissingSource (Sistem / Backup lipsa: expira la data ultimului pachet + 2 zile; fara backup, ceasul porneste de la prima utilizare) si NasCopyMissingSource (Sistem / Copie pe NAS lipsa: numai cand copierea este activa; ultima incercare esuata = restanta de la data ei, reusita = + 2 zile), MariaBackupAlertReader (ultimul .zip din directorul de backup, setarile NAS), BackupAlertRules; chei in ExpirySourceKeys, inregistrare in Program.cs.
- Se inchid singure la un backup nou / o copiere reusita (motorul inchide notificarea cand data se muta). Pragul implicit 0 zile; administratorul creeaza cate un sablon pentru fiecare in Setari -> Notificari, ca la celelalte surse.
- Verificari: 3 teste de reguli si surse (NasBackupChecks), 1 pe baza de test; suita componente 203 PASS.

## Finalizat la 08.10.2026 11:23 - Notificari backup: vechime configurabila, model needitabil, numai cu backup NAS activ

- Migrarea 31 (backup_nas_settings: max_age_days, last_error_utc, last_error). Setari -> Backup NAS: camp „Notificare backup lipsa dupa (zile)” 1-30, jurnalizat (Vechime maxima backup), plus modelul needitabil al notificarii (subiect, text, data ultimului backup, data si cauza ultimei erori).
- Sursele Backup lipsa si Copie pe NAS lipsa dau instante numai cand Copiere pe NAS este Activ. Decoratorul NasCopyingBackupService retine cauza si ora oricarui backup esuat (INasBackupCopier.RecordBackupFailureAsync); marcajele noi <ultima eroare backup>, <vechime maxima>, <cauza copiere>.
- Verificari: teste de reguli/sursa/model (NasBackupChecks) si pe baza de test (setare, limite 1-30, cauza pastrata) trec; pagina Setari nu a fost vazuta in browser dupa modificare.

## Finalizat la 08.10.2026 11:49 - Notificari: tab pe categorie si sabloane de pornire

- Sabloane notificari: tab pentru fiecare categorie; migrarea 32 (notification_template_seeds); DefaultNotificationTemplates creeaza o data, la pornire, un sablon activ din textul implicit al fiecarui eveniment fara sablon; drepturi noi pe tabela pentru utilizatorul aplicatiei la instalare

## Finalizat la 08.10.2026 12:08 - Backup unificat in Setari: pagina Backup, tabel server+NAS, pastrare, NAS redus

- Setari -> Backup: programare, vechime notificare, stergere automata locala (ultimele 4 pachete pastrate, cu NAS numai pachete copiate), tabel server+NAS cu restaurare inclusiv din NAS; migrarea 33 (backup_settings); pagina Restaurare scoasa; copierea pe NAS la preluarea de inventar in fundal; Backup lipsa nu mai depinde de NAS; nimic nu se sterge pe NAS din aplicatie

## Finalizat la 08.10.2026 12:11 - Iconite pe randurile tabelelor (regula + aplicare) si numar in paranteze pe taburile de sabloane

- Clasa comuna row-icon edit/delete (SVG din CSS) pentru editare/stergere/scoatere pe randurile tabelelor, in 15 fisiere (28 butoane); regula in CLAUDE.md; taburile de categorii arata Categorie (n)

## Finalizat la 08.10.2026 12:52 - Backup: stergere la salvare, verificare ora pe internet, setari NTP

- Stergerea automata a pachetelor vechi ruleaza si pe loc la salvarea unui prag nou, dupa confirmarea listei (BackupSettingsPanel, BackupRetentionService); se aplica tuturor tipurilor de pachete, inclusiv prerestaurare.
- Ceasul serverului se compara cu ora de pe internet (TrustedClock: NTP, doua servere trebuie sa fie de acord, apoi antet Date HTTPS). Stergerea asteapta cand ceasul difera cu peste 5 min sau ora nu se poate verifica.
- Backup: ceas decalat peste 15 min = data pachetului de pe internet (manifest TimeSource Internet); ora imposibil de verificat = pachet Unverified, exclus din stergerea automata si din ultimele 4, sters doar manual de administrator. Backup-ul zilnic urmeaza ora de pe internet (verificare la 10 min).
- Setari -> Backup: sectiunea Verificare ora (3 servere NTP precompletate, lista de 6, server propriu, Verifica acum); migrarea 34 (ntp_check_enabled, ntp_servers, clock_issue_utc, clock_skew_minutes); sablon nou Ceas server decalat (Sistem).
- Verificari: build Release, grup nas 24 PASS, sectiunea maria backup 19 PASS, componente 216 PASS

## Finalizat la 08.10.2026 12:59 - Setari Backup: categorie principala cu sub-taburi si zilele saptamanii

- Setari -> Backup este un tab principal cu sub-taburile Backup si restaurare, Backup NAS, Verificare ora (BackupSettingsPanel cu Section; legatura veche tab=backup-nas ramane valabila; notificarile trimit la sub-tabul potrivit).
- Backup-ul programat se poate face doar in anumite zile ale saptamanii (migrarea 35, schedule_days, masca Luni=1..Duminica=64); notificarea Backup lipsa asteapta cel putin intervalul maxim dintre doua backup-uri programate.
- Fiecare sub-tab salveaza doar campurile lui (restul se iau din valorile salvate).
- Verificari: build Release, grup nas 25 PASS, sectiunea maria 21 PASS, componente 217 PASS

## Finalizat la 08.10.2026 14:01 - Pagina produsului: taburi, filtre, aspect; ANAF 404; butoane colorate; padding

- Pagina de miscari: Intrare/Iesire sunt taburi (verde/rosu/alb), trecerea goleste formularul; filtrele radio inlocuite cu camp de text (StockMovementQuery.Text, cautare in descriere, referinta, factura, furnizor, beneficiar, proiect, vehicul) si etichete de coloana care deschid panouri de alegeri, cu etichete pentru filtrele active.
- Aspect: cardurile paginii in doua coloane (produs stanga, restul dreapta) fara spatiu in plus; total stoc cu padding si culori vizibile; padding implicit pentru orice element pus direct intr-un .catalog; filtre cu select simplu nu mai strivesc cautarea; summary de card pliabil cu padding.
- Butoane: clasele act-confirm/reload/nav/edit/close/export pentru toate butoanele refresh albe (84 de butoane), regula in CLAUDE.md.
- Cantitate reala la stoc negativ: validare la fiecare tasta (negativ/zecimal refuzat), butonul Regularizeaza se activeaza imediat.
- ANAF: HTTP 404 cu corpul notFound inseamna CUI inexistent, nu adresa gresita (AnafService.SendAsync).
- SystemTypes: buton Adauga langa denumirea alternativa.
- Verificari: build Release, suita 971 in memorie, componente 221, sectiunile Maria Suppliers si Stock movements trecute; verificat in browserul din aplicatie: taburi, filtre, spatii.

## Finalizat la 08.10.2026 14:46 - ANAF: reguli pe camp, dialog de diferente, versiuni cu slider si stergere; filtre ca in jurnal; liste de alegere; cautare

- ANAF: politici pe camp aplicate in formulare (AnafApplyRules, dialog AnafDifferencesDialog), coloana Folosit in, politica Goleste eliminata, implicite pe camp; HTTP 404 cu corp notFound = CUI inexistent.
- Versiuni configuratie ANAF: coloana Creata prin, slider Activa (ActivateVersionAsync), stergere cu motiv (DeleteVersionAsync), actiuni de jurnal noi; id-urile versiunilor nu se refolosesc.
- Pagina produsului: filtre ca in Jurnal activitate (camp de cautare, liste, etichete Filtre active, valori filtrabile in tabel, stare in adresa, filtru pe zi).
- Liste de alegere (beneficiar, produs, furnizor) se deschid peste card (overflow vizibil); fonturi uniformizate (campuri 14 px); fieldset fara chenar; cutie de cautare cu lupa SVG centrata; insigne count-badge; spatiu dupa randuri de butoane; sliderul pentru stergerea notificarilor rezolvate; tooltip pe butoane cu o glifa.
- Verificari: build Release, suita 979 in memorie, componente 229, jurnal 151; verificat in browserul din aplicatie: selectii cu click real, spatii, chenar.

## Finalizat la 09.10.2026 08:07 - Sablon de import factura de tip XML

- InvoiceXmlMapping/InvoiceXmlReader/InvoiceXmlRules (Services/Invoices/InvoiceXml.cs): cai de elemente (nume locale, @atribut, //Nume, alternative cu |), UBL standard implicit, UM UN/ECE -> buc, data dd.mm.yyyy; DTD interzis, limita 5 MB.
- Sablonul XML se salveaza ca restul sabloanelor (invoice_templates, source_kind xml, campul Xml din definitie; fara migrare), cu jurnalul existent; exclus din propunerile pentru PDF.
- Setari -> Facturi -> Sabloane salvate: buton Sablon XML nou, fereastra InvoiceXmlTemplateEditor (cai, UBL standard, proba pe fisier); creionul unui sablon XML deschide aceeasi fereastra.
- Preluare factura accepta .xml: IInvoiceAnalysisService.OpenXmlAsync, sablonul furnizorului ales dupa CUI, altfel UBL standard cu avertisment; restul asistentului neschimbat (fara pagina/liniile de demarcare).
- Verificari: build Release, 19 verificari noi (InvoiceXmlChecks: UBL complet, linii lipsa, XML invalid/DTD/prea mare, mapare proprie, JSON, wizard cu si fara sablon). Neverificat in browser; netestat: duplicat de factura XML (logica existenta din asistent).

## Finalizat la 09.10.2026 13:36 - Sabloane XML si preluare facturi XML/PDF, categorii, liste de alegere

- Sablon XML: editor pe sectiuni ca cel PDF (Legaturi, Previzualizare, Descriere intrare, Salvare), legare vizuala a elementelor cu arborele fisierului (InvoiceXmlLinker, InvoiceXmlTree), antet de fisier, zona de proba ascunsa dupa incarcare; schimbarea unei valori legate nu trece la urmatorul element si refuza o valoare legata de alt element; nume propus "<furnizor> - xml/pdf <data>" (si la PDF); butonul Adauga furnizorul doar daca furnizorul nu e ales (popup-ul era gol: lipsea using-ul SupplierEditor).
- Unitati UN/ECE Rec 20 (Services/Invoices/InvoiceUnitCodes.cs, 2162 coduri, echivalente scurte: buc, set, kg...); fisier ZIP e-Factura; numere XML afisate fara zerouri inutile (10.000000 -> 10, preturi cu minimum 2 zecimale).
- Descriere intrare in stoc la XML (InvoiceXmlDescription): etichete = elementele legate, aceleasi operatii ca la PDF; spatii automate inainte/dupa etichete si operatii (nu in operatii), trim la capete in previzualizare si salvare (mark-field.js); fontul campurilor cu etichete egalat cu fundalul (cursorul nu mai pleca de la text).
- Potrivirea produsului: cod din denumire, perechi cod furnizor - produs (tabela supplier_product_codes, migrarea 36, jurnal propriu, backup), nume propus la produs nou; refactorizarea preluarii in componente (PickupXmlTemplateBar, PickupPdfTemplateBar, PickupProductCell, InvoicePickup.*.cs).
- Preluare factura: mesajele de eroare/motivele apar si sub butoane si doar dupa o incercare (butoane blocked, fara erori la pagina proaspata); la XML nu mai exista confirmari Verificat; reparat spatiul gol si insignele duble din Datele facturii, decupaje fara randuri vecine.
- PDF: trepte de potrivire a sablonului (sub 30% propune sablon nou, 30-60% avertizeaza), recunoasterea PDF-ului exportat din e-Factura (InvoiceEFacturaPdf) cu mesaj despre XML/ZIP, Nr. inregistrare = CUI in acel PDF.
- Categorii: nume unice impreuna (categorii + subcategorii), stergerea categoriilor/subcategoriilor goale cu motiv (DeleteCategoryAsync/DeleteSubcategoryAsync, actiuni de jurnal Stergere categorie/subcategorie goala), iconite pe antetul categoriei si pe randuri, categoria subcategoriei aleasa cu SearchableSelect doar la mutare.
- Liste de alegere: SearchableSelect alege la mousedown (clicul real nu mergea in Brave); filtre Furnizor/Beneficiar la Miscari, Furnizor la Intrari libere si Furnizor din editorul XML convertite in liste cu cautare.
- ANAF: butonul Salveaza si activeaza configuratia testata; beneficiar: telefonul complet care se termina cu partea locala din ANAF nu mai face datele manuale, insigna listeaza campurile diferite.
- Verificari: build Release, suita completa 1038 trecute, sectiunea Maria Categories (5) si Supplier product codes (6) trecute; verificat in browser: popup furnizor, alegere din liste, ANAF beneficiar; restul neverificat in browser.
