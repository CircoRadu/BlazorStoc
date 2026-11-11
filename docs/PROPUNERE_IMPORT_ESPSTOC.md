# Propunere: modulul de preluare a stocului din ESPStoc (aplicatia veche)

Scris la 11.11.2026. Fara diacritice. Stare: propunere, nimic implementat. Surse analizate: `D:\Radu\ESP_Stoc\ESP_Stoc_backup_11 11  2026.zip` (export HeidiSQL, MariaDB 5.5, schema `stocesp`, 129 MB) si copia de siguranta BlazorStoc din 11.11.2026 14:24 (`dump.sql` + `assets/`). Scop: trecerea gestiunii pe BlazorStoc (cantitati + produse + imagini); miscarile vechi si restul datelor nu se preiau.

## 1. Ce contine exportul vechi (11.11.2026)
- Doar 6 tabele: `categorie` (9), `subcategorie` (107), `produs` (1210), `imagine` (1210), `produs_bak` (295, copie veche), `log` (3661). Nu mai sunt `io` (miscari), `beneficiar`, `user` (erau in exportul din 12.10). Nu e nevoie de ele.
- `produs`: `produs_denumire` = codul producatorului (max 47 car.), `produs_descriere`, `produs_cantitate`. Nu exista cod separat, nici cod furnizor, nici pret.
- Cantitate pozitiva: **342 produse, 1527 bucati** (868 au 0, niciunul negativ/gol). Pe categorii (produse/bucati): Supraveghere Video 126/553, Efractie 44/154, Incendiu 48/272, Control Acces 46/225, Interfonie 28/67, Retelistica 40/221, Porti 5/29, Diverse 4/5, IT&C 1/1.
- Imagini: exact una pe produs, toate PNG valide (6-219 KB). Coduri unice (0 dubluri, 0 spatii de capat/duble). 2 produse au categoria diferita de cea a subcategoriei (se urmeaza subcategoria, ca la importul din 12.10).
- Tipuri de problema deja cunoscute: coduri cu parametri scrisi in nume ("DS-2CD1141G0-I-2.8mm", "ST4000VX016 (4TB)").

## 2. Situatia din BlazorStoc (copia din 11.11.2026)
- 372 produse, 1779 bucati, 82 subcategorii, 9 categorii. Aproape tot vine din importul unic din 12.10.2026 (`tests/BlazorStoc.EspImport`, miscare "Preluare din baza veche", 370 intrari) + 2 intrari de pe factura (03.11: DS-7608NXI-K1-VPro, DS-PWA64-Kit-WE). Nu exista alte miscari: BlazorStoc nu a fost folosit pentru iesiri.
- Vechea aplicatie a continuat sa fie folosita dupa 12.10: totalul a scazut de la 1777 la 1527 buc. Deci cantitatile din BlazorStoc sunt **invechite** si trebuie aduse la zi.
- Codul produsului in BlazorStoc este unic (`normalized_name`, comparatie fara diferenta de litere).
- Subcategoriile au nume unice global; un nume folosit in mai multe categorii a primit categoria in paranteza ("Surse (Interfonie/Telefonie)", "Diverse (Porti)"). Regula trebuie refolosita la potrivire.
- Subcategorii cu parametri obligatorii: **Camere IP** si **Camere analogice** (familia "Lentila": Fixa mm / Varifocala mm-mm / Fisheye) si **Hard disk** (Capacitate TB, lista 1-12). Produsul de acolo are `base_model` + valori, iar codul se compune "model - valoare" (ex. "DS-2CD1047G2H-LIU - 2.8 mm", "ST4000VX016 - 4 TB"). 24 de produse din cele 372 au deja parametri (restul de 11 produse din Camere IP/analogice/Hard disk au ramas cu cod vechi, fara parametri).
- Imaginile sunt fisiere `product-<id>.png` pe disc (`IProductImageStore`); tabelul `product_images` e gol in dump.

## 3. Potrivirea codurilor (masurata pe date reale)
Din cele 342 produse cu cantitate pozitiva:
| Rezultat | Produse | Observatie |
|---|---|---|
| cod identic in BlazorStoc | 300 | 250 cu aceeasi cantitate, 50 cu cantitate diferita (39 BlazorStoc mai mare, 11 mai mic) |
| cod = `base_model` al unui produs cu parametri | 5 | ex. "DS-2CD1047G2H-LIU" (14 buc.) vs "DS-2CD1047G2H-LIU - 2.8 mm" (7 buc.); alegerea parametrului e propusa din produsul existent |
| cod cu parametrul scris in nume, model gasit | 14 | ex. "DS-2CD1141G0-I-2.8mm" -> model "DS-2CD1141G0-I", parametru 2.8 mm; "DS-2XS2T41G1-ID-4G-C05S07 4mm" -> 4 mm |
| produs nou (nu exista in BlazorStoc) | 23 | din care cateva in subcategorii cu parametri (ex. "DS-2CD1023G2-I 2.8mm", "ST4000VX016 (4TB)" pentru alt model, "DS20HKVS-VX1", "DS-2CFS04-4G 2.8mm") |
- Subcategorii vechi care nu exista in BlazorStoc: "Kituri" la Efractie si "Placi de retea" la Retelistica (se creeaza, ca la importul din 12.10). Restul se potrivesc dupa nume, cu regula parantezei.
- In BlazorStoc exista 53 de produse (229 buc.) fara corespondent cu cantitate pozitiva in baza veche: au fost consumate dupa 12.10 (sau nu mai au stoc). Include cele 2 de pe factura din 03.11, care pot fi intrari reale facute doar in BlazorStoc.

> **Modificare 11.11.2026:** importul este *punctul zero* al gestiunii in BlazorStoc: miscarile, facturile preluate, ofertele si rezervarile se sterg, iar stocul de plecare = stocul din baza veche. Ajustarile (deciziile A si B de mai jos) se inlocuiesc cu acest pas; vezi sectiunea 8, care are prioritate fata de restul documentului acolo unde difera.

## 4. Modulul propus
`Modules/EspImport/`, id `import-espstoc`, "Preluare stoc ESPStoc" (`IModule`, circuit breaker ca ceilalti, oprire din Setari -> Module), pagina in Setari -> Preluare ESPStoc, doar Administrator (permisiuni `setari-import-espstoc.*`). Inlocuieste instrumentul unic `tests/BlazorStoc.EspImport`.

### Sursa
- Se alege fisierul exportului (`.sql` sau `.zip` cu un `.sql`), incarcat in pagina sau indicat pe server (130 MB: citire in flux direct din zip, fara extragere). Se citesc doar tabelele `categorie`, `subcategorie`, `produs`, `imagine`; blob-urile (hex) se decodeaza pe rand. `log`, `produs_bak` se sar. Nu e nevoie de serverul vechi (192.168.100.50).
- Pregatirea e doar citire; nimic nu se scrie pana la "Aplica".

### Pasii in pagina
1. **Analiza** (simulare): se construieste planul in memorie, pe produs. Se pot repeta cu alt export.
2. **Lista pe categorii, colapsabila**: fiecare categorie veche (apoi subcategorie) este un rand care se deschide; pe rand: nume, numar de produse preluate / total cu stoc, bucati, insigna rosie cu numarul de produse cu probleme, insigna galbena cu cele care cer alegerea unui parametru. Sub rand, tabelul de produse (cod, descriere scurta, imagine miniatura, stoc vechi, stoc BlazorStoc, actiune, problema). Filtre: doar problemele, doar de decis; paginare pe tabel (regula aplicatiei).
3. **Rezolvarea problemelor** direct in rand, apoi "Aplica" activ doar cand nu mai ramane nicio problema nerezolvata (sau produsele cu probleme se pot omite explicit).
4. **Aplica** cu confirmare (ce se creeaza/actualizeaza, cate bucati), progres si raport final cu jurnal pe produs.

### Actiunea pe produs (stabilita de planificator, pura si testabila)
| Caz | Actiune |
|---|---|
| cod identic, stoc egal | nimic (imagine doar daca lipseste) |
| cod identic, stoc diferit | dupa punctul zero (sectiunea 8) primeste intrarea initiala cu cantitatea veche; nu mai exista ajustari in plus/minus |
| cod nou, subcategorie fara parametri | produs nou: cod + descriere + imagine + intrare initiala |
| cod nou / model cunoscut, subcategorie cu parametri | **alegerea parametrului**: formular cu modelul si valorile subcategoriei (familie lentila, capacitate), cu propunere automata din sufixul codului ("-2.8mm", "(4TB)", " 4mm") sau din produsul BlazorStoc cu acelasi `base_model`; cu valorile alese, daca produsul compus exista deja = actualizare, altfel produs nou cu `base_model` si valori; se poate crea o valoare noua din lista (ex. 6 mm) daca regulile parametrului permit |
| subcategorie veche fara echivalent | se creeaza (la nevoie in categoria potrivita) |
| categoria produsului difera de cea a subcategoriei | se urmeaza subcategoria (avertisment) |
| produs BlazorStoc fara corespondent cu stoc | ramane in catalog cu stoc 0 (punctul zero); lista separata "doar in BlazorStoc" doar ca informare |

### Probleme semnalate (insigna)
cod ambiguu (doua produse BlazorStoc cu acelasi `base_model` si parametrul nealeg); parametru neales; valoare de parametru inexistenta; cod prea lung / caractere nepermise dupa normalizare; imagine lipsa sau > 5 MB; subcategorie ce ar fi creata (informare); cod identic dar in alta subcategorie in BlazorStoc (se pastreaza locul din BlazorStoc, avertisment); produs blocat (`product_locks`) sau modificat in timpul analizei (versiune) -> reanaliza.

### Scriere
- Prin repository-urile existente (`MariaProductRepository.CreateAsync`, `FileProductImageStore.SaveAsync`, `MariaStockMovementRepository.CreateAsync`), nu prin SQL, ca sa ramana auditul, versiunile si regulile (cod unic, parametri, bucati intregi). Cantitati doar in bucati.
- Crearea produsului: aceeasi cale ca in pagina de produs, cu `BaseModel` + `Parameters` cand subcategoria are parametri (importul din 12.10 a trebuit sa ocoleasca parametrii; acum ii completeaza).
- Stocul se scrie ca miscare, niciodata direct in cantitate: produs nou = intrare "Preluare din ESPStoc <data export>" (`FreeEntryType.Adjustment`); ajustare in plus = aceeasi intrare cu diferenta; ajustare in minus = iesire `StockCorrection` cu aceeasi descriere. Referinta pe miscare: `ESPStoc P<id vechi>`.
- Jurnal de audit cu operatiunea exacta ("Preluare stoc ESPStoc: produs nou", "...: aliniere stoc 12 -> 14", "...: imagine adaugata", "...: parametru ales"), un rand de rulare in tabel propriu (harta `espstoc_import_map`: id vechi -> id produs, data exportului) ca re-rularea sa fie idempotenta si sa nu dubleze nimic.
- Imaginea: se pune doar daca produsul nu are; la cerere, "inlocuieste imaginea" per produs. Se pastreaza PNG-ul original.
- Cerinte de aplicare: lacat comun de import, se refuza daca exista iesiri/intrari noi dupa analiza (versiunea produsului s-a schimbat) -> reanaliza.

## 5. Decizii de confirmat
**A, B, C (inlocuite la 11.11.2026).** Alinierea stocului (A) si zeroizarea produselor consumate (B) nu mai sunt necesare: punctul zero sterge toate miscarile si pune stocul din baza veche (sectiunea 8). Cutover-ul (C) ramane: importul se face o singura data, in ziua trecerii, iar dupa el baza veche se inchide.
**D. Subcategorii cu parametri.** Se propune alegerea la preluare pentru Camere IP, Camere analogice si Hard disk; pentru restul nu se cere nimic. Daca in viitor se definesc parametri la alte subcategorii, modulul ii preia automat din definitii (nu sunt hard-codati).
**E. Imagini.** Se preiau imaginile produselor care urmeaza sa existe (342 posibile); la produsele existente doar daca lipseste imaginea.

## 6. Etape
1. Cititorul exportului (flux, zip/sql, 4 tabele) + planificatorul pur (potrivire cod/`base_model`/sufix, subcategorii cu regula parantezei, probleme) + teste pe un extras real mic.
2. Pagina: lista colapsabila cu insigne, filtre, paginare, rezolvarea parametrilor cu propunere automata.
3. Aplicarea (produse, parametri, imagini, miscari), harta de idempotenta, audit, raport final.
4. Verificari: simulare pe cele doua export-uri reale (cifre asteptate: 342 produse, 1527 buc; 300 identice, 19 cu parametri de potrivit, 23 noi), aplicare pe baza de test, a doua rulare = zero modificari, stoc total = 1527 (+ produsele pastrate din decizia B), imagini 342 prezente.
5. Fisiere de test utilizator, migrare pentru harta, docs (HARTA_COD, IMPLEMENTED), ajutor doar la cerere.
Estimare: 4 etape de lucru; cea mai mare parte e interfata de rezolvare a parametrilor.

## 7. Riscuri
- Exportul din 11.11 e partial (fara miscari): nu se poate verifica `produs_cantitate` = suma miscarilor (la 12.10 difereau 6 produse din 1198); se ia `produs_cantitate` ca adevar, ca la importul anterior.
- Modelele de la 12.10 redenumite manual (29) nu au corespondent exact in baza veche; potrivirea pe `base_model` le acopera doar daca modelul este scris identic (restul = produse noi de aliniat de mana in lista).
- Daca baza veche se mai foloseste dupa export, stocul din BlazorStoc iese gresit: ora exportului se tine in raport si se cere un export proaspat chiar inainte de aplicare.

## 8. Punctul zero al gestiunii (cerinta din 11.11.2026)
Importul devine inceputul utilizarii: din BlazorStoc dispar informatiile legate de cantitati, miscari, facturi preluate si oferte; produsele, categoriile, subcategoriile si restul obiectelor raman. Analizat pe copia de siguranta din 11.11.2026 (116 tabele, schema la migrarea 87).

### 8.1 Ce se sterge (si cat era in copia analizata)
| Obiect | Tabele | Randuri | Observatie |
|---|---|---|---|
| Miscari de stoc + corectiile lor | `stock_movements`, `stock_movement_history` | 372 + 0 | include iesirile pe proiect/beneficiar/vehicul si toate legaturile (`project_id`, `invoice_id`, `vehicle_id`, `project_component_id`) |
| Facturi preluate | `supplier_invoices`, `supplier_invoice_lines`, `supplier_recognitions` | 1 + 2 + 1 | nu exista PDF-uri atasate (singurele fisiere din `entity_files` sunt la puncte de lucru si taskuri) |
| Oferte | `offers`, `offer_lines` | 0 + 0 | modulul Oferte pastreaza doar liniile si potrivirile |
| Rezervari pe proiecte | `project_reservations` | 0 | sunt cantitati; fara stoc nu mai au sens (proiectul si componentele lui raman) |
| Cantitatea produselor | `products.quantity` | 372 produse | se pune 0 (cu versiune noua), apoi intrarea initiala o aduce la stocul vechi |
| Miscari arhivate | `archive_stock_movements` + operatiile de arhivare `MiscareStoc` din `archive_operations` | 5 + 5 | o miscare arhivata nu mai poate fi restaurata intr-o gestiune fara istoric |
| Cantitatea din produsul arhivat | `archive_products.quantity` | 1 | se pune 0, ca un produs restaurat sa nu readuca stocul vechi |
| Notificari de stoc | `expiry_notifications` cu sursa `stoc.*` (ex. iesiri peste stoc) si cele de factura asteptata | 1 | se inchid cu motiv (nu se sterg) |

Totalul stocului pe vehicule dispare odata cu miscarile (se calculeaza din ele): dupa punctul zero toate vehiculele au 0, iar materialele din vehicule se transfera din depozit dupa import.

### 8.2 Ce ramane neatins
Produsele (id-urile, codurile, descrierile, imaginile, etichetele, valorile de parametri), categoriile, subcategoriile, parametrii si familiile; stocul minim (`product_min_stock`) si nivelurile tinta ale vehiculelor (`vehicle_target_levels`); utilizatorii, tipurile de utilizator, beneficiarii, punctele de lucru, furnizorii si codurile lor (`supplier_product_codes`, memorie de potrivire, nu cantitati), proiectele si componentele lor, interventiile, mentenanta, taskurile, sabloanele de facturi si de oferte, setarile, preluarile ActivityApp/Elcomtur (`elcomtur_invoices` sunt facturi emise catre clienti, nu facturi de preluare in stoc), copiile de siguranta si **jurnalul de audit**.

### 8.3 Decizii de confirmat pe cazurile limita
1. **Jurnalul de audit** (2724 de evenimente, din care 382 `MiscareStoc` si 1 `FacturaFurnizor`): recomandat sa ramana (e istoria faptelor si se verifica in pachetele de siguranta); obiectele sterse apar in el fara link. Se adauga evenimente cu operatiunea exacta: "Punct zero gestiune: stergere miscari (N)", "...: stergere facturi preluate (N)", "...: stergere oferte (N)", "...: stergere rezervari (N)", "...: resetare cantitati (N produse)", "Preluare ESPStoc: stoc initial" (cate unul pe produs, cu cantitatea). Alternativa: stergerea si a intrarilor de audit legate de miscari si facturi (nerecomandat).
2. **Potrivirile din oferte** (`offer_line_matches`, nume din oferta -> produs confirmat de utilizator) si **codurile furnizorilor** (`supplier_product_codes`): recomandat sa ramana, sunt cunostinte despre produse, nu oferte; id-urile produselor nu se schimba, deci raman valabile. Se pot sterge la cerere.
3. **Rezervarile pe proiecte**: recomandat stergere (cantitati). Alternativa: pastrate, cu semnalare cand depasesc stocul.
4. **Facturile vechi reintroduse**: stergerea facturilor ridica protectia la dublura (furnizor + numar). Propunere: se retine data punctului zero (in `app_metadata`) si preluarea unei facturi cu data emiterii inainte de aceasta data cere confirmare explicita ("factura anterioara punctului zero, stocul din ea e deja in stocul initial?"). Fara asta, o factura veche incarcata din greseala dubleaza stocul.
5. **Rapoartele pe proiect** (situatia proiectului, nota de consum) pornesc de la punctul zero: consumul de pana acum dispare din aplicatie. Propunere: inainte de aplicare, pagina ofera descarcarea situatiilor pe proiectele deschise; oricum, tot istoricul ramane in pachetul de siguranta facut inainte.

### 8.4 Fluxul de aplicare (cel mai periculos pas al modulului)
Pasii sunt o masina de stari salvata in `app_metadata`, deci se pot relua dupa o intrerupere:
1. **Conditii**: doar Administrator, reconfirmare cu parola (`AdministratorReconfirmation`), analiza pe un export proaspat fara probleme nerezolvate, nicio alta operatiune pe baza (lacatul comun ca la restaurare). Punctul zero se poate stabili o singura data ("punct zero deja stabilit la dd.mm.yyyy" blocheaza o a doua rulare); unul nou se face numai dupa restaurare din pachet.
2. **Ecranul "Ce se sterge"**: numere exacte, pe tabel (cele din 8.1, calculate pe baza reala), cate bucati de stoc dispar, cate produse primesc stoc, cate raman la 0; confirmare tastata ("PUNCT ZERO") ca la restaurarea bazei.
3. **Copie de siguranta obligatorie** (pachet complet cu suma de control, ca cele de dinaintea actualizarii) facuta si verificata inainte de orice stergere; fara ea, pasul nu continua. Optional copie pe NAS.
4. **Stergerea**, intr-o singura tranzactie MariaDB, in ordinea cheilor straine: `stock_movement_history` -> `stock_movements` -> `supplier_recognitions` -> `supplier_invoice_lines` -> `supplier_invoices` -> `offer_lines` -> `offers` -> `project_reservations` -> arhivele de miscari; apoi `products.quantity = 0` (versiune +1) si inchiderea notificarilor de stoc. Daca ceva esueaza, tranzactia se anuleaza si nu s-a schimbat nimic. Dupa succes, starea = "punct zero in curs": miscarile de stoc noi sunt blocate pana la terminare.
5. **Importul** (produse noi/actualizate, parametri, imagini, apoi intrarile initiale prin `MariaStockMovementRepository`, data = ziua punctului zero, descriere "Punct zero - preluare din ESPStoc (export dd.mm.yyyy)", referinta `ESPStoc P<id vechi>`). Idempotent prin harta `espstoc_import_map`: o intrerupere se reia de unde a ramas.
6. **Verificare finala**: suma stocurilor = suma cantitatilor pozitive din export (1527 la exportul analizat), numarul de produse cu stoc = cele analizate, nicio miscare in afara celor ale punctului zero, imagini prezente; raport cu diferente. Starea trece pe "terminat" si se ridica blocarea. La nepotrivire, raportul recomanda restaurarea pachetului din pasul 3.

### 8.5 Efecte de stiut inainte de ziua trecerii
- Stocul vechi din BlazorStoc (1779 buc.) se pierde; devine cel din export (1527). Cele 2 intrari de pe factura din 03.11 (DS-7608NXI-K1-VPro, DS-PWA64-Kit-WE) dispar si ele; ele trebuie sa fie in stocul vechi daca au fost operate si acolo, altfel se adauga dupa punctul zero.
- Istoricul de miscari al fiecarui produs, raportul de consum pe proiecte si evidenta pe vehicule incep de la punctul zero.
- Numerotarea ofertelor/facturilor (numar, revizie) nu mai are istoric: numerele se pot refolosi.
- Exportul din baza veche se face chiar inainte de punctul zero, dupa ultima operatie in aplicatia veche; din acel moment nu se mai opereaza in ea.

### 8.6 Etape (completeaza sectiunea 6)
- Etapa 3 devine "punct zero + aplicare": masina de stari, ecranul "Ce se sterge", pachetul de siguranta obligatoriu, tranzactia de stergere, intrarile initiale, verificarea finala.
- Teste (MariaDB): pe o copie cu miscari, facturi, oferte, rezervari, proiecte cu componente si vehicule cu stoc: se verifica ce dispare si ce ramane (inclusiv numararea randurilor pe fiecare tabel neatins), anularea tranzactiei la o eroare simulata, reluarea dupa intrerupere, refuzul unei a doua rulari, blocarea miscarilor in "punct zero in curs", avertizarea la factura anterioara punctului zero.
- Un test care esueaza daca apare un tabel nou cu cheie spre `products`/`stock_movements`/`supplier_invoices`/`offers` si nu e clasificat in lista "se sterge"/"ramane" a modulului, ca punctul zero sa nu lase date orfane dupa migrari viitoare.
