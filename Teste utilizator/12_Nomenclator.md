# Nomenclator - Tipuri de sisteme (/nomenclator) - doar administrator

Fluxul: Administrare -> Nomenclator (sau cardul de pe pagina principala) -> adaugi tipurile de sisteme din care sunt facute proiectele (antiefractie, TVCI, control acces, incendiu, retelistica), le ordonezi, le dezactivezi cand nu se mai ofera si le dai denumiri alternative ca sa fie recunoscute in oferte ("CCTV" = TVCI). Nu exista nomenclator de unitati de masura: in stoc se tin doar produse pe bucati. Tipurile vor fi folosite de taskurile urmatoare (componentele unui proiect, sabloanele de oferte).

## Modificari de testat

### 07.11.2026 - Tabul "Componente mentenanta"

**Ce s-a adaugat:** tab nou in Nomenclator, "Componente mentenanta": familii (ex. Securitate fizica) si componente (discrete sau in familie), cu denumiri alternative, ordine, activ/inactiv si mutarea unei componente intre familii. Tabul "Cuvinte cheie mentenanta" ramane separat.

**Ce face acum:** familiile si componentele pot fi definite si ordonate; ce nu mai trebuie se dezactiveaza (nu se sterge). Contractele si tichetele le vor folosi in etapele urmatoare (in aceasta etapa nu se schimba inca niciun contract).

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Nomenclator -> tabul "Componente mentenanta"; deschide sectiunea explicativa | Sectiunea este inchisa implicit si se deschide la clic | |
| 2 | Adauga o familie (ex. "TEST Securitate") | Apare ca sectiune noua, cu 0 componente | |
| 3 | Adauga o componenta in familie si una "componenta discreta" | Prima apare in familie, a doua la "Componente discrete" | |
| 4 | Adauga o denumire alternativa (ex. CCTV pentru TVCI); incearca aceeasi denumire la alta componenta | Prima se adauga; a doua este refuzata (denumire existenta) | |
| 5 | Muta o componenta intre familii si apoi la discreta din coloana "Familie" | Ajunge la finalul listei noii familii / la discrete | |
| 6 | Dezactiveaza o familie si incearca sa adaugi in ea o componenta | Familia inactiva nu mai apare la alegerea familiei noii componente | |
| 7 | Verifica Jurnal activitate | Adaugarile, redenumirile, mutarile si dezactivarile apar cu actiuni proprii | |

### 07.10.2026 - Tipuri de sisteme

**Ce s-a adaugat:** pagina `Nomenclator` (`SystemTypes.razor`), serviciul `ISystemTypeRepository` / `MariaSystemTypeRepository`, tabelele `system_types` si `system_type_aliases` (migrarea 22), intrare in meniul Administrare si card pe pagina principala (doar administrator). Un tip are denumire, ordine, stare Activ/Inactiv si denumiri alternative; nu se sterge, se dezactiveaza. Unicitatea ignora litera mare/mica, diacriticele si separatorii, atat la denumiri cat si la denumirile alternative. Jurnal cu actiune exacta pentru fiecare operatie.

**Ce face acum:** Nomenclatorul `/nomenclator` are tipurile de sisteme (adaugare, redenumire, activare, ordine, denumiri alternative) din care se aleg componentele proiectelor.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Meniul Administrare -> Nomenclator (si cardul „Nomenclator” de pe pagina principala) | Se deschide pagina „Tipuri de sisteme”; un cont „Utilizator” nu vede intrarea in meniu, iar adresa /nomenclator ii este refuzata | |
| 2 | Adauga „Antiefracție”, „TVCI”, „Control acces”, „Incendiu”, „Rețelistică” | Apar in ordinea adaugarii, mesaj „Tipul a fost adăugat.”, campul se goleste | OK - adaugat TEST Antiefractie: mesaj Tipul a fost adaugat, camp golit, apare ultimul (24.10.2026) |
| 4 | „Redenumește” un tip (de ex. „Incendiu” -> „Detecție incendiu”), apoi „Renunț” la alta redenumire | Denumirea se schimba; „Renunț” nu schimba nimic; redenumirea in aceeasi denumire: „Denumirea nu s-a modificat.” | OK - redenumire OK (Denumirea a fost modificata); aceeasi denumire: Denumirea nu s-a modificat; Renunt nerulat (24.10.2026) |
| 7 | Muta un tip cu ▲ / ▼ | Ordinea se schimba; primul nu poate urca, ultimul nu poate cobori | OK - primul nu poate urca, ultimul nu poate cobori; ordinea s-a schimbat (24.10.2026) |
| 8 | Dezactiveaza un tip (comutatorul Activ) | Randul se estompeaza, mesaj „…nu mai apare la alegeri noi”; reactiveaza-l, redevine normal | OK - dezactivat: mesaj nu mai apare la alegeri noi, rand estompat; reactivat: Tipul a fost activat (24.10.2026) |
| 9 | Deschide pagina in doua taburi, redenumeste in primul, apoi incearca alta schimbare pe acelasi tip in al doilea | Al doilea afiseaza „Tipul de sistem a fost modificat între timp…” si lista se reincarca | |
| 10 | /jurnal | Evenimente exacte: „Adăugare tip de sistem”, „Modificare denumire tip de sistem”, „Activare/Dezactivare tip de sistem”, „Schimbare ordine tip de sistem”, „Adăugare/Ștergere denumire alternativă tip de sistem”; filtrul de entitati are „Tipuri de sisteme”; evenimentul duce la pagina Nomenclator | OK - Jurnal: Adaugare tip de sistem, Modificare denumire, Schimbare ordine, Dezactivare/Activare tip de sistem; link catre pagina si filtrul de entitati neverificate (24.10.2026) |
| 11 | Ecran ingust | Tabelul se deruleaza, butoanele ▲▼ si eticheta alternativa nu ies din pagina | |

Decizie: tipurile nu se sterg niciodata (se dezactiveaza), ca sa nu se piarda legaturile cu proiectele si ofertele care le vor folosi.

## Fluxul de baza

1. Adaugi tipurile -> le ordonezi -> adaugi denumirile alternative folosite in oferte.
2. Un tip scos din uz se dezactiveaza (nu mai apare la alegeri noi, dar ramane la proiectele care il au).

### 23.10.2026 - Tab nou: Cuvinte cheie mentenanta

**Ce s-a adaugat:** Nomenclatorul are doua taburi: „Tipuri de sisteme” (ca pana acum) si „Cuvinte cheie mentenanta” (lista cuvintelor folosite la preluarea tichetelor din ActivityApp; vezi `18_Preluare_tichete_ActivityApp.md`). Dreptul: nomenclator view/add/edit/delete.

**Ce face acum:** poti adauga, redenumi, activa/dezactiva si sterge cuvinte cheie; „Incearca un text” arata ce ar face importul cu un text.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Nomenclator | Doua taburi; primul arata tipurile de sisteme ca inainte | OK - Doua taburi, primul arata tipurile (24.10.2026) |
| 2 | Tabul „Cuvinte cheie mentenanta” | Lista, campul de adaugare si „Incearca un text” | OK - Lista, camp de adaugare si Incearca un text; cuvant adaugat, text exact gasit, m3ntenan@a asemanator (24.10.2026) |
| 3 | Un cont fara dreptul de modificare la nomenclator | Nu poate adauga, modifica sau sterge | |

## Modificari de testat - sectiunea Analizor de risc (30.10.2026, versiunea 0.5.34)

**Ce s-a adaugat:** al treilea tab din Nomenclator, „Analizor de risc" (adaugare, editare, stergere).
**Ce face acum:** analizorii din nomenclator se aleg la analiza de risc; actiunile apar in jurnal pe analizor.

| Nr | Pas | Rezultat asteptat | Rezultat |
|----|-----|-------------------|----------|
| 1 | Nomenclator -> Analizor de risc -> adauga un analizor | Apare in lista; jurnalul il noteaza | |
| 2 | Editeaza / sterge analizorul | Jurnalul noteaza operatia | |
