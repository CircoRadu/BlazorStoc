# Propunere: analize de risc (beneficiar + punct de lucru), reinnoiri si notificari de expirare

Document de analiza din 10.10.2026. Este o propunere, nu o implementare. Modelul urmat: contractele de mentenanta (`docs/PROPUNERE_CONTRACTE_MENTENANTA.md`): sectiune pe fisa beneficiarului, pagina proprie cu tabel, sursa de notificari in registrul `IExpirySource`, sablon implicit semanat de `DefaultNotificationTemplates`.

## 1. Cerinte (din cererea utilizatorului)

- Analiza de risc apartine unui beneficiar **si unui punct de lucru** al lui.
- Pentru un punct de lucru **o singura analiza activa** (cele vechi raman, dezactivate).
- Date: numar de inregistrare, data initiala (inregistrarii), **data ultimei reinnoiri** (la o analiza noua = data initiala), numele celui care a intocmit-o, valabilitate editabila in **luni** (implicit 36).
- **Istoric al reinnoirilor.**
- Notificare pentru apropierea expirarii, calculata din **data ultimei reinnoiri**, dupa modelul celorlalte notificari.
- Apare ca **tabel** in sectiunea dedicata si ca **sectiune** pe pagina beneficiarului.

## 2. Model de date (migrarea 39 + `database/mariadb/schema-mariadb.sql`)

`risk_analyses`:

| Coloana | Tip | Observatie |
|---|---|---|
| `id`, `version` | BIGINT | ca in celelalte tabele (concurenta optimista) |
| `beneficiary_id` | BIGINT | FK `RESTRICT` |
| `work_point_id` | BIGINT | FK `RESTRICT` spre `beneficiary_work_points` (punctul principal este rand real din migrarea 7) |
| `registration_number` | VARCHAR(30) | numarul de inregistrare al analizei |
| `author` | VARCHAR(120) | numele celui care a intocmit analiza (text liber) |
| `initial_date` | DATE | data la care a fost inregistrata analiza; nu se schimba la reinnoire |
| `last_renewal_date` | DATE | la creare = `initial_date`; se actualizeaza la fiecare reinnoire (copie denormalizata a ultimului rand din istoric, ca interogarea notificarilor sa ramana simpla) |
| `validity_months` | SMALLINT | 1-120, implicit 36 |
| `is_active` | TINYINT | On/Off |
| `notes` | VARCHAR(1000) | optional |
| `active_work_point_id` | BIGINT, coloana generata `IF(is_active=1, work_point_id, NULL)` | **UNIQUE**: o singura analiza activa pe punct de lucru, garantat de baza de date (mai multe NULL sunt permise, deci oricate analize Off) |

Expirarea **nu se stocheaza**: `last_renewal_date + validity_months luni` (`RiskAnalysisRules.ExpiryDate`, `AddMonths`; in SQL `DATE_ADD(... INTERVAL ... MONTH)`; ambele taie la sfarsit de luna, 31.01 + 1 luna = 28/29.02). Starea afisata (Valabila / Expira curand / Expirata / Off) este derivata, ca la contracte; "Expira curand" foloseste pragul sablonului activ.

`risk_analysis_renewals` (istoric):

| Coloana | Tip | Observatie |
|---|---|---|
| `id` | BIGINT | |
| `risk_analysis_id` | BIGINT | FK `RESTRICT` |
| `renewal_date` | DATE | data reinnoirii |
| `previous_renewal_date` | DATE | data ultimei reinnoiri dinainte (se vede din istoric ce s-a inlocuit) |
| `registration_number` | VARCHAR(30) NULL | numarul actului de reinnoire, daca are unul propriu |
| `recorded_by` | VARCHAR(120) | utilizatorul aplicatiei care a inregistrat reinnoirea |
| `recorded_utc` | DATETIME | |
| `notes` | VARCHAR(1000) | optional |

Prima linie a istoricului, "Inregistrare initiala", este derivata din `initial_date` (nu are rand propriu). Fara schimbari la `beneficiary_work_points`.

## 3. Reguli

1. **O singura analiza activa pe punct**: verificata in aplicatie (mesaj clar: „Punctul X are deja o analiza de risc activa (nr. N). Dezactiveaz-o mai intai sau foloseste Inlocuieste.") si pazita de indexul unic.
2. **Analiza noua**: `last_renewal_date = initial_date`. Data initiala nu poate fi in viitor.
3. **Reinnoire**: data >= ultima reinnoire (strict mai mare decat cea curenta, ca sa nu apara dubluri) si nu in viitor; initial propusa = azi; dialogul arata „Noua expirare: dd.mm.yyyy" calculata. O reinnoire atinge in aceeasi tranzactie randul din istoric si `last_renewal_date`.
4. **Anularea ultimei reinnoiri** (greseala de introducere): numai ultima, restabileste `last_renewal_date` la `previous_renewal_date`; jurnalizata. Reinnoirile intermediare nu se editeaza.
5. **Editarea**: numar, intocmit de, observatii, valabilitate oricand; `initial_date` numai cat timp nu exista reinnoiri (apoi trebuie sa ramana <= prima reinnoire; in lipsa reinnoirilor `last_renewal_date` o urmeaza). Schimbarea valabilitatii muta expirarea → sistemul inchide automat notificarea cu motiv explicit.
6. **On/Off**: Off pastreaza analiza si istoricul, scoate notificarea (motiv automat „Analiza de risc a fost dezactivata"). Activarea unei analize Off pe un punct care are deja una activa este refuzata cu mesaj.
7. **Inlocuire** (analiza noua pentru un punct care are deja una activa): dialogul de adaugare ofera, in loc de blocaj, comutatorul „Dezactiveaza analiza veche (nr. N) la salvare"; cele doua operatii se fac intr-o tranzactie (veche Off, noua On), jurnalizate separat.
8. **Stergerea**: ca la contracte, stergere arhivata (`ArchivePersistence`), cu confirmare; istoricul de reinnoiri se arhiveaza odata cu analiza. Un punct de lucru cu analiza legata nu se sterge (FK RESTRICT; mesaj „sterge/muta mai intai analiza de risc"). Aceeasi regula la stergerea beneficiarului ca pentru contracte.
9. **Drepturi**: citire pentru toti; adaugare/editare/reinnoire/On-Off/stergere numai cu `EnsureBeneficiaryOperatorAsync` (ca la contracte).

## 4. Notificari

- Cheie noua `ExpirySourceKeys.RiskAnalysisExpiry = "analiza-risc.expirare"`; sursa `RiskAnalysisExpirySource : IExpirySource` in fisier propriu, cu citire doar-citire fara verificarea operatorului (`IRiskAnalysisNotificationReader`/`MariaRiskAnalysisNotificationReader`, ca `IMaintenanceNotificationReader`), inregistrata in `Startup/ServiceRegistration.cs`.
- **O instanta pe analiza ACTIVA**, `ObjectId` = id analiza, `Expiry` = `last_renewal_date + validity_months`, `Url` = `/beneficiari/{id}`.
- Categorie „Analize de risc", eveniment „Expirare analiza de risc", `DateLabel` „expirarii", prag implicit **90 zile** (analizele se refac greu; editabil in sablon).
- Placeholdere proprii: `<beneficiar>`, `<punct de lucru>`, `<adresa punct de lucru>`, `<numar inregistrare>`, `<data inregistrare initiala>`, `<data ultima reinnoire>`, `<valabilitate luni>`, `<intocmit de>`; plus cele comune (`<data expirare>`, `<zile ramase>`, `<zile depasire>`).
- Subiect implicit „Expirare analiza de risc – <beneficiar>, <punct de lucru>"; text implicit pe modelul contractului. Sablonul implicit apare singur prin `DefaultNotificationTemplates` (o data, fara recreare daca administratorul il sterge).
- **Efectul reinnoirii**: data expirarii se muta → motorul inchide automat notificarea veche cu motivul „Data expirarii analizei de risc s-a modificat de la X la Y (ultima reinnoire: dd.mm.yyyy)" si, daca noua data tot intra in prag, apare una noua; altfel reapare la apropierea noii expirari. Dezactivarea / stergerea inchid cu `RemovedReason`. Dupa expirare notificarea ramane pana e rezolvata (regulile de depasire existente).
- Setarile de purjare a rezolvatelor si pagina `/notificari` nu se schimba.

## 5. Interfata

**Pagina beneficiarului** (`/beneficiari/{id}`), sectiune noua „Analize de risc N" dupa „Contracte de mentenanta", `ServiceContractsSection` ca model (`RiskAnalysesSection.razor` + `RiskAnalysisEditor.razor` + `RiskAnalysisRenewalDialog.razor`):

- Tabel: NR. INREGISTRARE | PUNCT DE LUCRU | INTOCMIT DE | DATA INITIALA | ULTIMA REINNOIRE | VALABILITATE | EXPIRA | STARE (chip) | actiuni. Analizele Off sunt ascunse implicit, cu comutatorul „Arata si analizele Off (N)".
- Actiuni pe rand: buton cu text **Reinnoieste** si **Istoric** (deschide randul cu lista: „Inregistrare initiala", apoi reinnoirile, cea mai noua prima, cu butonul de anulare pe ultima), comutator On/Off compact, iconite `row-icon edit` / `row-icon delete` cu `aria-label` si `title`.
- Sub tabel o nota „N puncte de lucru fara analiza de risc activa" cu butonul „+ Adauga" precompletat pe punctul respectiv; antet: „+ Adauga analiza de risc".
- Formular (adaugare/modificare): Punct de lucru (lista punctelor beneficiarului), Numar de inregistrare, Intocmita de, Data inregistrarii (`PickOnlyDate`), Valabilitate (luni, implicit 36), Observatii; la adaugare, „Ultima reinnoire" apare doar-citire = data inregistrarii. La modificare se cere motivul (`ChangeReasonField`) ca la celelalte editari.
- Optional pe tabelul punctelor de lucru: chip „Fara analiza de risc" / „Analiza expira in N zile" (propus la etapa finala, nu blocant).

**Pagina dedicata `/analize-risc`**, intrare noua in meniu langa Mentenanta (`MainLayout.razor`), `RiskAnalyses.razor`:

- Tabel cu toate analizele: aceleasi coloane + BENEFICIAR (link catre fisa), sortat implicit dupa expirare crescator; filtre: cautare (beneficiar/punct/numar/intocmit de), stare (Toate / Expira curand / Expirate / Valabile), comutator „Arata si analizele Off".
- Randurile au aceleasi actiuni (Reinnoieste, Istoric, editare, On/Off) prin **aceleasi componente** ca sectiunea beneficiarului; „+ Adauga analiza" are in plus selectorul de beneficiar (`SearchableSelect`), apoi punctul lui de lucru. Astfel logica exista o singura data.
- Titlu/eyebrow: „Analize de risc", text introductiv scurt, butonul „↻ Actualizeaza", paginare ca la celelalte liste.

## 6. Jurnal (actiuni numite exact, `AuditActions`, filtrul paginii Audit, `IsCreateOrEdit`)

Entitate noua `AnalizaRisc` (se foloseste numai la stergerea arhivata; restul se inregistreaza pe beneficiar, ca la contracte).

| Actiune | Detalii |
|---|---|
| Adaugare analiza de risc | punct, numar, intocmit de, data, valabilitate |
| Modificare analiza de risc | camp cu valoare veche → noua |
| Modificare valabilitate analiza de risc | luni veche → noua, expirarea veche → noua |
| Reinnoire analiza de risc | data veche → noua, numar act, noua expirare |
| Anulare reinnoire analiza de risc | data anulata → data restabilita |
| Activare analiza de risc / Dezactivare analiza de risc | punct, numar |
| Stergere (arhivata) | continut + istoric |

Inchiderea automata a notificarilor foloseste actiunea existenta „Rezolvare automata notificare".

## 7. Impact asupra codului existent

- Nou: `Services/RiskAnalyses.cs` (modele, `RiskAnalysisRules`, `IRiskAnalysisRepository`), `Services/MariaRiskAnalysisRepository.cs`, `Services/RiskAnalysisNotificationSources.cs`, `Assets/Migrations/039.sql`, trei componente + o pagina, intrare in meniu.
- Modificat: `ExpirySourceKeys`, `ServiceRegistration.cs`, `AuditTrail.cs` (actiuni, entitate, `IsCreateOrEdit`), filtrul Audit, `BeneficiaryDetail.razor` (sectiune + `ReloadAsync`), `MainLayout.razor`, `Archiving`, `docs/HARTA_COD.md` (rand nou „Analize de risc"), `Teste utilizator/` (fisier nou + index), `TESTE_RAMASE.md`.
- Nemodificat: motorul de notificari, pagina `/notificari`, setarile de purjare.

## 8. Teste (tintite, 4-5 cai critice)

1. `RiskAnalysisRules`: expirarea din ultima reinnoire (inclusiv 31.01 + luni), validari reinnoire (nu in viitor, > curenta), anulare ultima reinnoire.
2. Maria: indexul unic (a doua analiza activa pe punct refuzata; oricate Off), inlocuirea in tranzactie, reinnoire + istoric + `last_renewal_date`, FK punct de lucru.
3. Sursa de notificari: o instanta pe analiza activa, data din ultima reinnoire; dupa reinnoire notificarea veche se inchide automat cu motiv, Off/stergere o inchid.
4. Jurnal: fiecare actiune nou-numita scrie intrare cu valori vechi/noi.
5. Rute (`StructureChecks`): `/analize-risc`.

## 9. Etapizare

1. Migrarea + reguli + repository + jurnal (+ teste 1, 2, 4).
2. Sectiunea de pe fisa beneficiarului: tabel, formular, reinnoire, istoric, On/Off, stergere.
3. Sursa de notificari + sablon implicit + test 3.
4. Pagina `/analize-risc` + meniu + test 5.
5. Documentatie (`HARTA_COD`, `Teste utilizator/`, `TESTE_RAMASE`).

## 10. Decizii confirmate de utilizator (10.10.2026) si ce s-a implementat

| # | Decizie | Cum este implementat |
|---|---|---|
| D1 | Analiza are intotdeauna numar de inregistrare. Numarul se pastreaza la revizii; o analiza noua are numar nou | Reinnoirea nu are numar propriu (este revizia aceleiasi analize); numarul este unic pe beneficiar, iar mesajul de duplicat trimite la „Reinnoieste". Analiza noua cu numar nou inlocuieste pe cea veche prin comutatorul din D3 |
| D2 | „Intocmit de": text liber, numele persoanei abilitate sa intocmeasca analize de risc la securitate | Camp obligatoriu, fara precompletare |
| D3 | Comutator la analiza noua pe un punct care are deja una activa | „Dezactiveaza analiza veche la salvare" (pornit implicit), intr-o singura tranzactie |
| D4 | Gestionare si din pagina dedicata | `/analize-risc` foloseste aceleasi componente ca fisa beneficiarului; formularul alege intai beneficiarul |
| D5 | Prag de notificare 60 de zile | `DefaultThresholdDays` = 60 (si pragul chipului „Expira curand"); editabil in sablon |
| D6 | Valabilitate 36 de luni acum, modificabila ulterior | Implicit 36, camp editabil, 1-120 de luni |
| D7 | Chip pe tabelul punctelor de lucru | Coloana „Analiza de risc": „Fara analiza de risc inregistrata" sau „Nr. N · expira dd.mm.yyyy" |

Diferente fata de propunerea initiala: `risk_analysis_renewals` nu are `registration_number` (D1); cheia analizei active este o coloana obisnuita `active_work_point_id` setata de aplicatie (ca la `service_contract_points`), nu o coloana generata; sectiunea de pe fisa beneficiarului este dupa „Interventii".
