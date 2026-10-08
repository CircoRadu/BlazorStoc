# Propunere: selectarea liniilor ofertei si stergerea unei oferte din proiect

Data: 08.10.2026. Stare: propunere pentru studiu, nimic implementat. Raspunsurile la deciziile din sectiunea 5 stabilesc implementarea.

## 1. Situatia de azi (din cod)

- **Pasul 4 al preluarii ofertei** (`Components/Pages/OfferPickup.razor`) arata toate liniile ofertei. Liniile „in afara stocului" (metraje, ore, servicii) sunt marcate automat si nu se leaga de produse. La restul se alege un produs din catalog sau ramane „de achizitionat". O linie nu se poate scoate din oferta.
- **Oferta se salveaza intreaga** (`Services/Offers/MariaOfferRepository.cs`, tabelele `offers` si `offer_lines`, cu revizii dupa numarul ofertei). Din ea deriva:
  - situatia proiectului pe componente si lista de achizitie (`MariaProjectSituationReader` citeste ultima revizie a fiecarei oferte);
  - componenta proiectului (adaugata la preluare, daca se alege un tip de sistem);
  - legaturile retinute dintre denumiri si produse (`offer_line_matches`, globale, invatate din toate ofertele);
  - denumirea alternativa a beneficiarului (`beneficiary_aliases`).
- **Rezervarile** (`project_reservations`) nu au legatura in baza de date cu oferta: sunt identificate dupa proiect, produs si componenta. Le face utilizatorul din situatia proiectului („Rezerva") sau din propunerile de dupa o intrare. La preluarea ofertei nu se creeaza nicio rezervare.
- **Iesirile si intrarile** se leaga de **componenta** proiectului (`stock_movements.project_component_id`, migrarea 26), nu de o linie anume a ofertei; o iesire „in afara ofertei" are marcaj propriu.
- **Stergerea unei oferte** nu exista nicaieri (nici repository, nici pagina).

## 2. Propunere A - selectarea liniilor la pasul 4

### Ce vede utilizatorul
- O coloana noua „Include" cu slider On/Off la fiecare linie (regula: orice comutator este `ToggleSwitch`).
  - Implicit sunt pornite toate liniile de stoc (ca acum). Liniile „in afara stocului" raman marcate si nu se pot porni.
  - O linie oprita se afiseaza estompata, iar selectorul ei de produs este inactiv.
- Deasupra tabelului: contor „Selectate 18 din 42", „Porneste toate / Opreste toate", „Doar liniile legate de produse" si un camp de filtrare (denumire, sectiune), cu aceeasi logica de filtrare ca la Jurnal (cautare, etichete „Filtre active", „Sterge filtrele").
- Pasul 5 (rezumat) si jurnalul spun „X linii incluse, Y excluse".

### Ce se salveaza (recomandare)
Liniile oprite **se pastreaza in oferta, marcate „nu e de interes"** (migrarea 36: `offer_lines.skipped TINYINT NOT NULL DEFAULT 0`). Motive:
- la o revizie noua alegerea ramane (liniile se potrivesc dupa `OfferLineRules.LineKey`), fara sa fie ofertate din nou ca „adaugate";
- diferenta dintre revizii (`OfferDiffRules`) nu le numara ca adaugate sau scoase;
- oferta ramane completa pentru consultare si verificare.

Alternativa „nu se importa deloc" este mai simpla, dar la revizie liniile ar reaparea selectate si ar trebui o memorie separata a excluderilor.

### Ce nu mai folosesc liniile oprite
Situatia proiectului si lista de achizitie, propunerile de rezervare dupa intrari, legaturile retinute (`offer_line_matches`: o linie oprita nu invata nimic) si contoarele din rezumat.

## 3. Propunere B - stergerea unei oferte din proiect

### Unde
In situatia proiectului, la fiecare oferta, o iconita de stergere (regula iconitelor pe randuri). Se sterge **oferta intreaga, cu toate reviziile ei**. Pentru o revizie gresita se preia una noua.

### Cand este permisa
Numai daca oferta **nu are legaturi**. Definitie propusa: nicio iesire sau intrare pe componenta ofertei pentru produse care apar in liniile ei (si nu sunt acoperite de o alta oferta ramasa pe aceeasi componenta).
- Cu legaturi: butonul este inactiv, cu explicatia „are 4 iesiri legate" si linkuri spre ele (pagina produsului, filtrata dupa proiect).
- Rezervarile **nu** blocheaza stergerea: se anuleaza odata cu oferta (vezi mai jos).

### Ce se anuleaza odata cu oferta (cu motiv obligatoriu, in jurnal)
1. **Rezervarile proiectului** pentru produsele ofertei. Fiecare rezervare anulata are propria inregistrare in jurnal („anulata la stergerea ofertei X"). Stocul liber creste la loc, pentru ca rezervarile nu schimba stocul. Un produs care apare si intr-o alta oferta ramasa a proiectului isi pastreaza rezervarea.
2. **Oferta, reviziile si liniile** (`offers`, `offer_lines`; `offer_lines` se sterg in cascada).
3. Tot ce se citeste din oferta dispare singur: linia din situatia proiectului, lista de achizitie, propunerile de rezervare.

### Ce ramane
- **Componenta proiectului** ramane activa (poate fi folosita si altfel). Daca a fost adaugata doar de aceasta oferta si nu mai are nimic legat, se propune „Arhiveaza componenta" (operatie existenta, reversibila).
- **Proiectul** ramane. Daca a fost creat la preluarea ofertei si este acum gol, se propune „Sterge si proiectul", dupa regulile lui de stergere.
- **Legaturile retinute** (`offer_line_matches`) si **denumirea alternativa a beneficiarului**: raman (sunt invatate din toate ofertele, nu apartin unui proiect).
- **Jurnalul** ramane complet; stergerea are actiune proprie (ex. „Stergere oferta din proiect") si cate o actiune pentru fiecare rezervare anulata („Anulare rezervare la stergerea ofertei").

### Fereastra de confirmare
Arata impactul inainte de stergere: „Se sterge oferta 123 (3 revizii, 42 linii). Se anuleaza 2 rezervari: Produs A 5 buc, Produs B 2 buc. Componenta si proiectul raman." Confirmarea cere motiv (ca la celelalte stergeri) si cuvantul de confirmare; Enter in camp confirma.

## 4. Plan de implementare (dupa decizii)

| Zona | Modificare |
|---|---|
| Baza de date | Migrarea 36: `offer_lines.skipped`. Fara alte tabele. |
| `Services/Offers` | `OfferImportLine.Skipped`; `OfferLineRecord.Skipped`; `GetLinesAsync` o intoarce; `MariaOfferRepository.ImportAsync` o salveaza si nu invata legaturi din liniile oprite; `OfferDiffRules` ignora liniile oprite; metoda noua `GetRemovalImpactAsync(projectId, number)` (legaturi, rezervari de anulat, ce ramane) si `RemoveOfferAsync(projectId, number, reason)` (tranzactie: verifica legaturile, anuleaza rezervarile, sterge oferta, jurnal). |
| `Services/MariaProjectSituationReader` | ignora liniile oprite la situatie si lista de achizitie. |
| `Services/MariaReservationRepository` | metoda de anulare a rezervarilor unui proiect pentru un set de produse (cu jurnal propriu). |
| `Services/AuditTrail` + `AuditFilters` | actiuni noi: `RemoveOffer`, `CancelReservationOnOfferRemoval`, eventual `ExcludeOfferLines` la import. |
| `Components/Pages/OfferPickup.razor` | coloana „Include", contor, comenzi de grup, filtrare, rezumat. |
| `Components/Pages/ProjectSituationPage.razor` | iconita de stergere pe oferta, fereastra cu impactul, motivul, propunerile pentru componenta/proiect. |
| Teste | repository: import cu linii oprite, revizie care pastreaza alegerea, situatie fara liniile oprite, stergere cu/fara legaturi, rezervari anulate si pastrate (alta oferta), jurnal; componente: coloana Include, contoare, fereastra de stergere; pasi in `Teste utilizator/14_Oferte_preluare.md`. |

Estimare: o sesiune pentru A, una pentru B (B depinde de A doar prin coloana `skipped`).

## 5. Decizii de luat

1. **Liniile oprite:** salvate cu marcaj (recomandat) sau neimportate deloc?
2. **Selectia implicita:** toate liniile de stoc pornite (recomandat) sau doar cele legate de produse?
3. **Stergerea ofertei:** definitiva, cu jurnal complet (recomandat), sau in arhiva, cu posibilitate de restaurare, ca la alte stergeri?
4. **Legaturile care blocheaza:** este corecta definitia „iesiri/intrari pe componenta, pentru produsele ofertei"? Rezervarile raman neblocante si se anuleaza (asa a fost cerut)?
5. **Reviziile:** se sterge mereu oferta intreaga (recomandat) sau si cate o singura revizie?
6. **Componenta si proiectul create de oferta:** doar propuneri la stergere (recomandat) sau automat?

## 6. Riscuri si limite
- Iesirile sunt legate de componenta, nu de linia ofertei; de aceea „legatura" se defineste pe componenta si produs, nu pe linie. Daca doua oferte diferite acopera aceeasi componenta si acelasi produs, stergerea uneia este permisa cat timp cealalta acopera produsul.
- Stergerea nu poate fi anulata (in varianta definitiva): de aceea fereastra de impact, motivul obligatoriu si blocarea cand exista legaturi.
- Liniile oprite raman in baza de date: daca o oferta are foarte multe linii nefolosite, tabelul `offer_lines` creste, dar efectul este neglijabil.
