# TODO — funcționalități viitoare

## Regula de actualizare a TODO

Din 28.09.2026, taskurile finalizate nu mai stau in acest fisier: ele se muta in `IMPLEMENTED.md` (fisier separat, in acelasi director), care se completeaza doar prin append (se adauga la sfarsit, nu se rescrie ce exista deja).

- Acest fisier („TODO.md”) contine numai ce mai trebuie implementat: taskurile active și, în cadrul lor, doar subtaskurile nefinalizate.
- Taskurile active sunt numerotate consecutiv, începând de la 1, în ordinea priorității; numărul cel mai mic indică prioritatea cea mai mare.
- Când se introduce un task nou înaintea unui task existent, acesta și toate taskurile active următoare (neimplementate) se renumerotează; subtaskurile își schimbă numerele odată cu taskul.
- Când un task este finalizat, el se scoate din acest fisier, iar toate taskurile rămase neimplementate se renumerotează de la 1.
- Taskul finalizat se **adauga la sfarsitul `IMPLEMENTED.md`** (fisier in ordine cronologica: cel mai vechi primul, cel mai nou ultimul — doar append, nu se modifica intrarile anterioare), cu **data și ora implementării** (ora locală) și cu **detalierea** lucrării: ce s-a implementat, fișierele principale, deciziile, verificările efectuate și ce a rămas neverificat.
- Taskul finalizat **nu păstrează denumirea „Task N”**: primește o denumire care explică succint scopul lui, în forma „Finalizat la zz.ll.aaaa oo:mm — denumire”. Trimiterile către el din alte texte folosesc această denumire, nu un număr; nici subtaskurile lui nu mai păstrează numerele.
- Verificările care nu s-au putut efectua se trec, cu motivul și pașii, în `docs/TESTE_RAMASE.md`.

# Taskuri active

Planul de mai jos vine din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` (deciziile confirmate de utilizator la 30.09.2026) si din evaluarea de la sfarsitul `docs/PROPUNERE_HARTA_MENTENANTA.md`. Detaliile fiecarui task (schema, reguli, jurnal, teste) sunt in documentul de propunere; aici sunt numai subtaskurile de urmarit. Verificarile ramase din taskurile anterioare sunt in `docs/TESTE_RAMASE.md`.

Planul din `docs/PROPUNERE_CONTRACTE_MENTENANTA.md` este implementat integral (vezi `IMPLEMENTED.md`); verificarile ramase sunt in `docs/TESTE_RAMASE.md`.

## Task 1 - Completari: iesire rapida, „Unde sunt bucatile", completare vehicul la nivel, stoc minim, export consum

Acceptat la 07.10.2026 ca etapa optionala, in aceasta ordine de interes: (1) **iesire rapida** din lista de produse (dialog fara parasirea listei, reutilizeaza formularul de iesire); (2) cardul **„Unde sunt bucatile"** pe pagina produsului (depozit, fiecare vehicul, predat pe beneficiar/proiect); (3) **completare vehicul la nivel** (nivel tinta pe produs pentru masina, genereaza o iesire multipla); (4) **stoc minim** cu notificare (dupa ce stocul este corect); (5) **export consum** pe proiect/beneficiar/perioada (CSV); (6) notificari de „rezervare fara miscare" si „deficit la un proiect cu termen apropiat".

## Task 2 - Fisierul facturii folosit la un sablon se pastreaza pe server si se poate consulta si edita din sablon

**Stare la 02.10.2026 (de reevaluat):** o parte din acest task este deja implementata altfel, pentru fisierul unui sablon: factura model se salveaza **in baza de date** (tabelul `invoice_template_models`, migrarea 11, `LONGBLOB`, cu `sha256`; se inlocuieste la salvare, nu se aduna), se deschide la editarea sablonului (✎ din lista de sabloane, sub-tab-ul „Editare sablon", fara analiza pana la „Analizeaza fisierul") si nu mai exista versiuni de sablon (vezi `IMPLEMENTED.md`, intrarile din 02.10.2026). Raman de decis: „Consulta fisierul" fara intrarea in editare, stocarea pe disc in directorul de active in locul bazei, jurnalul pentru atasare/inlocuire/stergere/consultare fisier, includerea in backup si stergerea impreuna cu sablonul; subpunctele de mai jos care vorbesc de „versiune" sunt depasite (nu mai exista versiuni).

Cerut de utilizator la 30.09.2026; **inlocuieste decizia anterioara** („fisierul se arunca dupa ce utilizatorul accepta sablonul", implementata in Setari → Facturi). Acum fisierul PDF din care s-a facut un sablon **ramane salvat pe server**, legat de sablon, poate fi **consultat** din sablon, iar sablonul se poate **edita pe el**, fara sa mai fie nevoie de o noua incarcare.

- **Ce se schimba in comportament:** la salvarea unui sablon (nou sau versiune noua) fisierul analizat se scrie pe disc si se leaga de sablon; lista „Sabloane salvate" primeste „Consulta fisierul" (deschide PDF-ul si, pe pagina, regiunile si coloanele sablonului) si „Editeaza" (incarca sesiunea de analiza direct din fisierul salvat, cu sablonul aplicat, fara alegerea unui fisier). „Editeaza cu un fisier" ramane pentru a folosi o alta factura ca model. Textele din pagina („Fisierul se pastreaza doar in memorie... se sterge cand salvezi") se corecteaza.
- **Stocare:** fisiere pe disc in directorul de active al bazei (ca fotografiile si fisierele proiectelor: `MariaProjectFileStore`, `MariaServicePhotoStore`), nume generat, cu tabel de legatura (`invoice_template_files`: sablon, versiune, cale relativa, nume original, dimensiune, `sha256`, incarcat de/la) creat printr-o migrare noua; acelasi fisier folosit de mai multe sabloane nu se dubleaza (dupa `sha256`). Limite ca la analiza (20 MB, 30 de pagini). Fisierul se serveste numai administratorilor, prin `/media/invoice-templates/{id}` (cu verificarea rolului si `no-store`), niciodata prin adresa directa a fisierului. Sesiunea de analiza (memorie) ramane pentru lucru; fisierul salvat este sursa persistenta.
- **Pe versiuni:** fiecare versiune a sablonului poate avea fisierul ei (de exemplu cea editata pe alta factura); implicit o versiune noua pastreaza fisierul versiunii precedente daca nu s-a incarcat altul. Consultarea arata fisierul versiunii alese.
- **Confidentialitate (de confirmat):** fisierele contin date reale (parteneri, conturi bancare, sume); raman accesibile numai administratorilor, se includ in backup/restaurare (directorul de active, ca celelalte fisiere) si se sterg impreuna cu sablonul (sau la stergerea ultimei legaturi). Sterge-le dintr-un sablon doar explicit, cu motiv, in jurnal.
- **Jurnal (actiuni exacte, cu test):** „Atasare fisier la sablon factura" (la salvare, cu numele, dimensiunea si `sha256`), „Inlocuire fisier sablon factura", „Stergere fisier sablon factura" (cu motiv), „Consultare fisier sablon factura" (optional, daca se doreste urma accesului); `AuditActions`, filtrul paginii Audit, `IsCreateOrEdit`.
- **Implementare propusa:** `IInvoiceTemplateFileStore` (disc + tabel), extinderea `InvoiceTemplateService.CreateAsync/SaveNewVersionAsync` cu fisierul din sesiune (scrierea fisierului si a randului in aceeasi operatie; daca una esueaza se anuleaza cealalta), `InvoiceAnalysisService.OpenSavedAsync(templateId, version)` care citeste fisierul salvat, il analizeaza si aplica sablonul, panou „Consulta fisierul" reutilizand suprapunerea din `InvoiceTemplateWorkbench`, teste (scriere/citire/dedublare dupa `sha256`, acces numai pentru administrator, stergere impreuna cu sablonul, backup/restaurare, jurnal) si actualizarea `docs/TESTE_RAMASE.md`.
- **Lamuriri necesare:** (1) un singur fisier model per versiune de sablon sau mai multe (de exemplu un PDF si o scanare)? (2) la stergerea sablonului se sterge si fisierul (recomandat, daca nu il mai foloseste alt sablon)? (3) sabloanele salvate pana acum (fara fisier) raman fara fisier pana la prima editare cu un fisier? (4) fisierele salvate se includ in backup-ul bazei sau raman doar pe disc, in directorul de active?

## Task 3 - Aliniere estetica a elementelor alaturate in formulare

Cerut de utilizator la 30.09.2026 (exemplul: in „Integrare ANAF", comutatorul „Integrare activata" sta alaturi de campul „Denumire integrare", dar nu este pe aceeasi linie cu caseta campului si pare pierdut; la fel casuta „Sablon activ" langa „Eveniment de expirare"). Situatii de genul acesta nu trebuie sa existe: fie elementul se muta intr-un loc firesc, fie se aliniaza astfel incat sa fie estetic.

- **Regula:** in orice grila de formular (`.editor-grid`, `.anaf-grid` si echivalentele) un element care sta pe acelasi rand cu un camp cu eticheta deasupra (comutator, buton, nota) se aliniaza cu **caseta campului** (nu cu eticheta) sau se muta pe propriul rand, pe toata latimea, sub campurile pe care le comanda; campurile de pe acelasi rand au aceeasi linie de baza pentru casete, chiar daca etichetele sau notele lor au inaltimi diferite (etichete pe doua randuri, note de ajutor).
- **Audit:** parcurgerea tuturor formularelor si dialogurilor (utilizator, beneficiar, punct de lucru, contract, interventie, vehicul, produs, proiect, sabloane de notificari, setari notificari, ANAF, harta, filtre ale listelor si jurnalelor) la latime mare, medie si pe telefon, cu captura inainte/dupa; corectia se face in CSS comun (de exemplu `align-items: start` pe grila, clasa `.field-inline` pentru comutatoare care stau pe linia casetei, `min-height` rezervat pentru note) si nu pagina cu pagina, ca sa nu apara alte cazuri.
- **Verificare:** lista paginilor cu capturi inainte/dupa la 3 latimi in `docs/TESTE_RAMASE.md`; un test automat de aspect nu este practic, asa ca regula se adauga in `CLAUDE.md` („elementele alaturate se aliniaza estetic; un comutator/buton nu sta „plutind" langa un camp") pentru dezvoltarile viitoare.

## Task 4 - Fara contur gros in jurul campurilor de editare active; evidentiere mai discreta

Cerut de utilizator la 30.09.2026 (exemplul: campul de cautare din jurnal are, cand este activ, un contur verde gros, cu distanta fata de caseta, care se suprapune peste caseta de cautare si „strica estetica"). Marginea aceea nu trebuie sa existe cand campul este activ.

- **Cauza:** regula globala din `wwwroot/app.css`: `input:focus-visible, select:focus-visible, textarea:focus-visible { outline: 3px solid #76bfb0; outline-offset: 3px }` (la fel `.clipboard-zone:focus`). La campurile din interiorul unui container cu chenar propriu (`.search-box`, `.pick-date`, campuri cu pictograma) conturul se deseneaza in afara containerului si dubleaza chenarul.
- **Audit:** toate campurile de editare: text, cautare (liste, jurnale, harta, produse), numar, parola, data (`PickOnlyDate`), selectii (`select`, `SearchableSelect`), zona de text, campurile din dialoguri si din filtre, zonele de incarcare de fisiere. Se verifica fiecare la focus cu tastatura si cu mausul.
- **Alternativa propusa (evidentiere fara contur exterior):** campul activ isi schimba doar **chenarul propriu** (1 px, culoarea de accent `--green`) si primeste o **umbra interioara foarte fina** (`box-shadow: 0 0 0 1px var(--green)`, fara distanta fata de caseta); pentru containerele cu pictograma (`.search-box`) evidentierea trece pe container (`:focus-within`), iar campul interior isi pierde orice contur. Evidentierea ramane vizibila pentru accesibilitate (contrast de cel putin 3:1 fata de fundal, cerinta WCAG pentru indicatorul de focus), dar nu mai apare un inel gros separat. Butoanele si linkurile pastreaza un inel discret (2 px, distanta 1 px) doar la navigarea cu tastatura (`:focus-visible`), nu la clic.
- **Implementare:** reguli comune in `app.css` (nu pagina cu pagina): `:where(input, select, textarea):focus { outline: none }`, stilul de accent pe chenar, `:focus-within` pe containere; `prefers-reduced-motion`/`forced-colors` respectate (in modul cu contrast ridicat se pastreaza conturul sistemului). Verificare vizuala inainte/dupa pe formularele si listele principale, la latime mare si pe telefon; nota in `docs/TESTE_RAMASE.md` cu lista paginilor verificate si regula in `CLAUDE.md` pentru dezvoltarile viitoare.

## Task 5 - Pagina principala: intrari pentru Mentenanta si Notificari, cu semnalizarea notificarilor

Cerut de utilizator la 30.09.2026. Pagina principala (`Components/Pages/Dashboard.razor`, sectiunea „Sectiunile aplicatiei") are intrari pentru Produse, Categorii, Inventar, Beneficiari, Vehicule, Utilizatori si Jurnal, dar **nu** pentru Mentenanta (`/mentenanta`) si Notificari (`/notificari`), care exista in meniul lateral.

- **Intrari noi:** „Mentenanta" (scadente, registru de interventii, contracte si harta; descriere scurta in stilul celorlalte) si „Notificari" (notificari de expirare si de mentenanta), cu pictograma din meniul lateral (⚒, ✉), vizibile tuturor utilizatorilor autentificati, in aceeasi ordine ca in meniu.
- **Semnalizarea se extinde:** logica prin care intrarea „Notificari" din meniul lateral devine rosie cu semnul de exclamare/triunghi (`nav-alert`, `nav-triangle` in `MainLayout.razor` si `wwwroot/app.css`; numarul notificarilor care avertizeaza vine de la `IExpiryNotificationService.AlertCountAsync`; `wwwroot/notification-watch.js` o tine la zi la 60 s prin `/api/notifications/alerts`) se aplica si intrarii „Notificari" din pagina principala: titlul si pictograma rosii plus semnul ⚠ (cu `aria-label="Notificari noi"`). Acelasi mecanism si aceeasi sursa de date, nu o logica separata: `notification-watch.js` actualizeaza toate elementele marcate cu `data-notification-nav`/`data-notification-triangle`, deci noua intrare primeste aceleasi atribute.
- **Mentenanta:** intrarea poate semnala si ea, cu acelasi stil, cand exista scadente depasite sau contracte expirate (numar dat de lista de scadente), daca utilizatorul o vrea; de confirmat, implicit doar Notificarile (care includ deja notificarile de mentenanta) primesc semnul, ca sa nu existe doua semnale pentru aceeasi cauza.
- **Verificari:** pagina principala pentru administrator si pentru utilizator limitat, cu si fara notificari care avertizeaza (semnul apare si dispare fara reincarcare), stil identic cu meniul lateral, navigare de la tastatura, ecran ingust; test pentru marcajele `data-notification-*` pe ambele intrari; actualizarea `docs/TESTE_RAMASE.md`.

## Task 6 - Sabloane de facturi: scanari slabe si tabele dense (contrast si linii desenate)

Cerut de utilizator la 30.09.2026, ca urmare a comparatiei cu Preluarea situatiei de inventar (`InventoryPickupOcr`): motorul de sabloane de facturi (`Services/Invoices`) preia deja redresarea de inclinare, dar nu si intarirea contrastului, iar liniile desenate nu sunt folosite ca indiciu pentru coloane.

- **6.1 Intarirea contrastului pentru scanari palide sau facute cu creion:** in `InvoicePdfReader` (calea OCR), pagina cu contrast slab (nivelul hartiei minus cele mai inchise 1% dintre pixeli sub un prag) se intinde pe toata gama de gri inainte de recunoastere, ca in `InventoryPickupOcrService` (`InkContrast`, `StretchContrast`, pragurile `MinUsableContrast` si `FaintInkContrast`); o pagina fara nicio cerneala ramane neatinsa. Reutilizare prin metode comune (nu copie), cu citire de proba pe imaginea originala si pe cea intinsa si alegerea celei cu mai multe cuvinte sigure. Teste: pagina generata si rasterizata cu contrast scazut (gri deschis, creion simulat), pe care citirea fara intarire pierde text si cu intarire il recupereaza; fixtura de scanare palida in `InvoiceFixtures`.
- **6.2 Liniile desenate ca indiciu suplimentar pentru coloane:** linii verticale si orizontale detectate in pagina (PDF cu text: desenele din pagina, cu PdfPig; scanare: detectarea de linii din OpenCV, ca in inventar, `FindGridLines`/`FindDividers`) devin indiciu pentru limitele coloanelor si ale randurilor, alaturi de alinierea cuvintelor, fara sa fie obligatorii. Util la tabele dense, fara spatiu intre coloane, unde doua coloane se unesc intr-un singur segment. Limita dintre doua coloane se aseaza pe linia verticala cea mai apropiata dintre antetele lor; un rand cu linii orizontale isi ia inaltimea din linii. Teste: fixtura cu coloane lipite (text fara spatiu) si linii desenate, pe care citirea fara linii amesteca valorile; raportul de corpus arata daca liniile au fost folosite.
- **Verificare:** fixturile noi in `tests/BlazorStoc.Checks/InvoiceFixtures.cs`, rularea suitei (inclusiv cele trei facturi reale din `INVOICE_CORPUS_DIR`, care nu trebuie sa se schimbe), o scanare reala palida, daca utilizatorul o aduce (`docs/TESTE_RAMASE.md`, N28).

## Task 7 - Robustete la erori si la caderea aplicatiei pe server

Cerut de utilizator la 06.10.2026, dupa analiza comportamentului la exceptii (vezi raspunsul din sesiune): o exceptie obisnuita inchide doar circuitul utilizatorului (banner „Conexiunea cu aplicatia a fost intrerupta"), exceptiile din serviciile de fundal sunt prinse, dar o exceptie in afara oricarui `try` (sarcina lansata fara asteptare, temporizator, `async void`) opreste procesul, iar nimic nu il reporneste; jurnalul este numai pe consola.

- **Supervizor cu repornire automata:** serviciu Windows, `systemd` sau container cu `restart: always`, dupa locul unde se instaleaza aplicatia (de clarificat: Windows, QNAP sau Linux; Docker si QNAP sunt in afara fazei curente). Reteta de instalare in `docs/`.
- **Jurnal in fisier** cu rotire si retentie (de exemplu Serilog) si handler global `AppDomain.UnhandledException` care scrie eroarea inainte de oprire; stack trace-urile nu se arata utilizatorilor (ramane `DetailedErrors` oprit).
- **`ErrorBoundary`** in jurul continutului paginii din layout: o eroare dintr-o componenta arata „Eroare in aceasta pagina, reincarca" fara sa inchida tot circuitul.
- **Endpoint `/health`** (verifica baza de date) pentru monitorizare si pentru supervizor.
- **Ajutor comun pentru sarcinile lansate fara asteptare** (`_ = InvokeAsync(...)`, `Task.Run`, `async void`): prinde si logheaza exceptiile; auditul celor ~22 de locuri din `Services`, `Components` si `Program.cs`.
- **De verificat:** comportamentul la pornire cu baza de date indisponibila, eliberarea blocarilor de operatie si de produs cand circuitul cade (battement de inima), testul de cadere simulata cu repornire.

## Task 8 - Sablon de import factura de tip XML

Cerut de utilizator la 07.10.2026. Facturile de furnizor se pot prelua si dintr-un fisier **XML** (de exemplu factura electronica UBL / e-Factura), nu doar din PDF/OCR: fisierul se citeste direct, fara OCR, pe baza unui **sablon XML** care spune ce element/atribut contine numarul facturii, data, furnizorul (CUI), liniile (cod/denumire, cantitate, unitate) si totalurile.

- Sablonul XML se defineste in Setari -> Facturi (cai de elemente, ex. XPath simplificat), se salveaza ca restul sabloanelor si se potriveste furnizorului; pentru UBL standard exista un sablon propus implicit.
- Preluarea foloseste acelasi asistent ca PDF-ul (recunoastere furnizor, potrivire produse, intrari legate de factura, jurnal exact, avertismente de duplicat).
- Fisiere de test generate in teste (fara facturi reale); XML invalid sau cu alt format primeste mesaj clar.
- **Teste:** UBL valid citit complet, XML cu linii lipsa, XML invalid, furnizor necunoscut, factura deja preluata.

## Observații pentru etapa de implementare

- Schema bazei de date și scripturile aferente se stabilesc în etapa dedicată integrării MariaDB.
- Implementarea trebuie să rămână complet asincronă.
- Toate mesajele afișate utilizatorului sunt în limba română (regula finalizată „Mesaje exclusiv în limba română”).
- Toate datele calendaristice afișate utilizatorului au forma `dd.mm.yyyy` (de exemplu `25.09.2026`; cu oră: `25.09.2026 14:08`), în orice pagină, dialog, jurnal, mesaj sau document generat. Formatele interne (`yyyy-MM-dd` în SQLite, `dd-MM-yyyy` în coloana existentă `io_data` din MariaDB, adresele URL) nu se afișează; se folosesc `StockMovementRules.DisplayDate` și formatul `dd.MM.yyyy`.
- Funcționalitățile trebuie validate atât în modul demonstrativ, cât și prin teste de integrare cu două sesiuni concurente.

