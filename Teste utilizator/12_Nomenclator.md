# Nomenclator - Tipuri de sisteme (/nomenclator) - doar administrator

Fluxul: Administrare -> Nomenclator (sau cardul de pe pagina principala) -> adaugi tipurile de sisteme din care sunt facute proiectele (antiefractie, TVCI, control acces, incendiu, retelistica), le ordonezi, le dezactivezi cand nu se mai ofera si le dai denumiri alternative ca sa fie recunoscute in oferte ("CCTV" = TVCI). Nu exista nomenclator de unitati de masura: in stoc se tin doar produse pe bucati. Tipurile vor fi folosite de taskurile urmatoare (componentele unui proiect, sabloanele de oferte).

## Modificari de testat

### 07.10.2026 - Tipuri de sisteme

**Ce s-a adaugat:** pagina `Nomenclator` (`SystemTypes.razor`), serviciul `ISystemTypeRepository` / `MariaSystemTypeRepository`, tabelele `system_types` si `system_type_aliases` (migrarea 22), intrare in meniul Administrare si card pe pagina principala (doar administrator). Un tip are denumire, ordine, stare Activ/Inactiv si denumiri alternative; nu se sterge, se dezactiveaza. Unicitatea ignora litera mare/mica, diacriticele si separatorii, atat la denumiri cat si la denumirile alternative. Jurnal cu actiune exacta pentru fiecare operatie.

**Ce face acum:** Nomenclatorul `/nomenclator` are tipurile de sisteme (adaugare, redenumire, activare, ordine, denumiri alternative) din care se aleg componentele proiectelor.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Meniul Administrare -> Nomenclator (si cardul „Nomenclator” de pe pagina principala) | Se deschide pagina „Tipuri de sisteme”; un cont „Utilizator” nu vede intrarea in meniu, iar adresa /nomenclator ii este refuzata | |
| 2 | Adauga „Antiefracție”, „TVCI”, „Control acces”, „Incendiu”, „Rețelistică” | Apar in ordinea adaugarii, mesaj „Tipul a fost adăugat.”, campul se goleste | |
| 3 | Adauga „antiefractie” sau „CONTROL-ACCES” | Refuzat: „Există deja un tip de sistem sau o denumire alternativă…” | |
| 4 | „Redenumește” un tip (de ex. „Incendiu” -> „Detecție incendiu”), apoi „Renunț” la alta redenumire | Denumirea se schimba; „Renunț” nu schimba nimic; redenumirea in aceeasi denumire: „Denumirea nu s-a modificat.” | |
| 5 | La TVCI scrie „CCTV” in campul „+ denumire alternativă” si Enter | Apare ca eticheta sub TVCI; aceeasi denumire pe alt tip este refuzata | |
| 6 | Sterge eticheta „CCTV” cu × | Dispare | |
| 7 | Muta un tip cu ▲ / ▼ | Ordinea se schimba; primul nu poate urca, ultimul nu poate cobori | |
| 8 | Dezactiveaza un tip (comutatorul Activ) | Randul se estompeaza, mesaj „…nu mai apare la alegeri noi”; reactiveaza-l, redevine normal | |
| 9 | Deschide pagina in doua taburi, redenumeste in primul, apoi incearca alta schimbare pe acelasi tip in al doilea | Al doilea afiseaza „Tipul de sistem a fost modificat între timp…” si lista se reincarca | |
| 10 | /jurnal | Evenimente exacte: „Adăugare tip de sistem”, „Modificare denumire tip de sistem”, „Activare/Dezactivare tip de sistem”, „Schimbare ordine tip de sistem”, „Adăugare/Ștergere denumire alternativă tip de sistem”; filtrul de entitati are „Tipuri de sisteme”; evenimentul duce la pagina Nomenclator | |
| 11 | Ecran ingust | Tabelul se deruleaza, butoanele ▲▼ si eticheta alternativa nu ies din pagina | |

Decizie: tipurile nu se sterg niciodata (se dezactiveaza), ca sa nu se piarda legaturile cu proiectele si ofertele care le vor folosi.

## Fluxul de baza

1. Adaugi tipurile -> le ordonezi -> adaugi denumirile alternative folosite in oferte.
2. Un tip scos din uz se dezactiveaza (nu mai apare la alegeri noi, dar ramane la proiectele care il au).
