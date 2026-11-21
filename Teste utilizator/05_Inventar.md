# Inventar (/inventar, /inventar/preluare; restaurarea a trecut in Setari -> Backup)

Fluxul: Generare situatie inventar (PDF cu categoriile alese) -> tiparesti, completezi de mana -> Preluare inventar (scanare/PDF) -> verifici cifrele citite -> se genereaza miscari de corectie. "Restaureaza stoc" (administrator) readuce stocul la o stare anterioara.

## Modificari de testat

### 17.11.2026 - Preluare inventar: indicator de progres in procente (PROP-0033)
**Ce s-a adaugat:** cat timp se analizeaza scanarea, pagina "Preluare inventar" arata procentul din analiza si o bara de progres.
**Ce face acum:** langa "Se citeste fisierul..." apare procentul (de la 0% la 100%), care creste pe masura ce sunt citite paginile formularului; la 100% apar diferentele de stoc. Procentul vine din procesul de citire (OCR) si ajunge in pagina in timp ce acesta lucreaza.
Pasi: Inventar -> Preluare inventar -> alege un formular PDF cu mai multe pagini -> apare procentul si bara; procentul nu scade si se opreste la 100% cand apare tabelul de diferente.

### 15.10.2026 - Inventarul ca modul (oprire fara efect asupra restului aplicatiei)

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** Inventarul (generarea situatiei de inventar si preluarea foii numarate) este un modul al aplicatiei, ca Analizele de risc, Vehiculele, Mentenanta si Oferte. Daca se opreste (din configurare sau dupa erori repetate), restul aplicatiei merge.

**Ce face acum:** cu modulul oprit, /inventar si /inventar/preluare arata "Modul indisponibil"; sectiunea Inventar din meniu si cele doua carduri de pe pagina principala dispar. Un refuz obisnuit (nimic selectat, produs lipsa) da mesajul de pana acum si nu opreste modulul. Notele de consum si situatia proiectului se genereaza la fel ca inainte (folosesc acelasi font PDF).

Pasi:

1. Cu modulul pornit (normal): Inventar -> Generare situatie si Preluare inventar se deschid ca pana acum; genereaza un PDF si verifica diacriticele.
2. Porneste aplicatia cu `Modules__inventar__Enabled=false`: /inventar si /inventar/preluare arata "Modul indisponibil", meniul si pagina principala nu mai au Inventar, Setari -> Module arata Inventar "Oprit (oprit din configurare)".
3. Tot cu modulul oprit: genereaza o nota de consum (Export consum) si situatia unui proiect in PDF: se genereaza normal, cu diacritice.

### 15.10.2026 - Citirea inventarului scanat ruleaza intr-un proces separat

**Verificat de agent 19.10.2026** (pe instanta de test, baza de test; vezi `docs/TESTE_RAMASE.md`, „Verificare completa a modulelor”).

**Ce s-a adaugat:** recunoasterea (OCR) fisierului scanat de la Preluare inventar ruleaza acum intr-un proces worker separat, nu in aplicatia web.

**Ce face acum:** incarcarea unui inventar scanat da aceleasi randuri ca inainte. Daca procesul de recunoastere se opreste sau depaseste timpul (5 minute), pagina arata "Citirea fisierului a esuat: procesul de recunoastere s-a oprit..." iar aplicatia continua sa mearga; dupa 3 esecuri la rand pagina spune "Recunoasterea documentelor este indisponibila momentan" pentru 30 de secunde, apoi merge din nou fara repornire.

Pasi:

1. Pe Inventar -> Preluare inventar, incarca un inventar scanat: se citesc randurile ca inainte (cod citit, valoare reala).
2. Incarca un fisier care nu e PDF: apare mesajul obisnuit al paginii (nu mesajul de proces oprit).
3. In `/health/modules` apare modulul `ocr` cu starea `healthy`.


### 07.10.2026 - Produsele negative primele

**Ce s-a adaugat:** in situatia de inventar, in fiecare subcategorie, produsele cu stoc negativ apar primele (marcate), ca sa fie numarate si regularizate intai; legatura cu tabul „De regularizat” de pe pagina Produse (vezi 01 si 02).

**Ce face acum:** Inventarul listeaza intai produsele cu stoc negativ, ca sa fie numarate si regularizate primele.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Genereaza situatia pentru o subcategorie cu un produs cu stoc negativ | Produsul negativ este primul, marcat; celelalte alfabetic | |
| 2 | La preluarea numaratorii reale pentru un produs negativ (neverificat inca) | Stocul se aduce la cantitatea numarata prin corectia existenta; produsul iese din "De regularizat" | |

### 06.10.2026 - Selectie pe randuri

**Ce s-a adaugat:** selectia pe randuri (marcaj rotund, clic pe rand, „Selecteaza tot”) in locul casetelor de bifat, la Inventar si Preluare inventar.

**Ce face acum:** Randurile din inventar se selecteaza cu marcajul rotund sau cu clic pe rand, iar „Selecteaza tot" din antet bifeaza toate.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 2 | Preluare inventar: selectie pe randuri | La fel | |

### Verificari ramase din etapele anterioare (vezi docs/TESTE_RAMASE.md grupa H)

**Ce s-a adaugat:** Verificari ramase din etapele anterioare ale inventarului (scaner, rezolutie, scris de mana).

**Ce face acum:** Preluarea inventarului citeste cifrele corect sau le marcheaza de verificat.


| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Alt scaner/rezolutie, alt scris de mana | Cifrele citite corect sau marcate de verificat | |
| 2 | Tiparire fizica a situatiei si deschidere in alt cititor PDF | Aspect corect | |
| 3 | Miscarile generate: iesirile sunt "peste stoc" cand e cazul, nu blocheaza | Jurnal cu actiuni exacte | |

## Fluxul de baza

1. Genereaza situatia pentru o categorie -> PDF.
2. Completeaza -> scaneaza -> Preluare inventar.
3. Corecteaza cifrele -> confirma -> stocul se ajusteaza, jurnalul arata corectiile.

## Modificari de testat - previzualizarea PDF a situatiei de inventar (12.11.2026, BUG-0021)

**Ce s-a adaugat:** corectie de afisare: dialogul de previzualizare avea continutul gol.

**Ce face acum:** dupa generare, in dialogul "Situatia de inventar" se vede PDF-ul in vizualizator, sub numele fisierului, iar butoanele Imprima / Salveaza / Inchide sunt dedesubt.

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Inventar -> selecteaza categorii -> "Genereaza situatia de inventar" | Dialogul se deschide cu PDF-ul afisat pe toata inaltimea | |
| 2 | Apasa "Imprima" si "Salveaza" | Pornesc imprimarea, respectiv descarcarea fisierului | |

## Modificari de testat - ordinea la preluare si citirea scanului (16.11.2026, versiunea 0.5.88)

**Ce s-a adaugat:** lista de la Preluare inventar are aceeasi ordine ca situatia de inventar; citirea scanului nu mai pierde randuri cand o linie a tabelului este intrerupta de scris.

**Ce face acum:** categoriile sunt alfabetic, subcategoriile in ordinea din arbore, in fiecare produsele cu stoc negativ primele, apoi dupa cod. Randurile de langa o linie intrerupta (ex. scris peste ea) sunt citite.

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Genereaza situatia de inventar si noteaza ordinea subcategoriilor si a produselor | - | |
| 2 | Preluare inventar -> incarca scanul foii completate | Subcategoriile si produsele apar in aceeasi ordine ca pe foaie | |
| 3 | La un scan cu scris peste linia tabelului, verifica pozitiile din jurul ei | Pozitiile apar in lista (daca au diferenta de stoc) | |
| 4 | Un produs cu valoarea scrisa egala cu stocul curent | Nu apare in lista (nu are de corectat), este normal | |

## Modificari de testat - Invatare OCR la preluarea inventarului (16.11.2026, versiunea 0.5.89)

**Ce s-a adaugat:** la fiecare preluare confirmata se salveaza corectiile pe care le-ai facut valorilor citite de OCR; pagina noua Inventar -> Invatare OCR le arata cu captura din formularul scanat si lasa administratorul sa aleaga din care invata citirea.

**Ce face acum:** daca ai schimbat o valoare citita (de exemplu 45 -> 15), se salveaza cifra gresita (4 -> 1), pozitia ei si captura celulei; daca numarul de cifre difera, corectia este de tip "Segmentare". Se pastreaza cel mult 1000 de corectii (cele mai vechi Noi/Ignorate se sterg primele, cele Invatate raman). Decizia "Invatat" se salveaza global, dar folosirea ei la citirea formularelor noi urmeaza intr-o etapa ulterioara.

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Preluare inventar -> incarca un scan, schimba o valoare citita gresit (ex. 45 in 15), trimite modificarile in stoc | Preluarea reuseste ca pana acum | |
| 2 | Deschide Inventar -> Invatare OCR (meniu sau butonul din antetul preluarii) | Corectia apare cu captura celulei si cifra gresita incadrata cu rosu, "4 -> 1 (pozitia 1)" | |
| 3 | Filtreaza dupa Confuzie (4 -> 1), Tip si Stare | Lista se restrange corespunzator | |
| 4 | Ca administrator: selecteaza corectia (rand sau cerc) si apasa "Invata din selectie" | Starea devine Invatat, apare mesajul de confirmare | |
| 5 | Selecteaza o corectie de tip Segmentare si apasa "Invata din selectie" | Nu se invata (se poate doar ignora); mesajul o spune | |
| 6 | "Ignora selectia", apoi "Marcheaza nou" | Starea devine Ignorat, apoi Nou | |
| 7 | "Reseteaza invatarea" si confirma | Toate corectiile Invatate redevin Noi | |
| 8 | Intra cu un utilizator simplu in Invatare OCR | Vede lista, fara butoane de decizie | |
| 9 | Jurnal -> filtreaza dupa actiune | Apar "Salvare corectii OCR", "Invatare OCR", "Ignorare corectie OCR", "Resetare invatare OCR" cu cifrele implicate | |

## Modificari de testat - citirea tine cont de ce a invatat aplicatia (16.11.2026, versiunea 0.5.89)

**Ce s-a adaugat:** la citirea unui formular nou, cifrele nesigure sunt corectate cu ce ai marcat "Invatat" in Invatare OCR.

**Ce face acum:** o cifra citita cu incredere sub 90% se inlocuieste cu cifra pe care ai pus-o de obicei in locul ei, doar daca a fost invatata de cel putin 2 ori, reprezinta cel putin jumatate din ce s-a invatat pentru acea cifra si este printre primele trei variante ale clasificatorului. Randul ramane "de verificat" si arata "↻ din invatare 4 -> 1". Daca anulezi corectia, ea devine o corectie noua 1 -> 4.

| Pas | Ce faci | Ce trebuie sa vezi | OK |
|---|---|---|---|
| 1 | Marcheaza "Invatat" cel putin 2 corectii identice (ex. 4 -> 1) in Invatare OCR | Starea Invatat | |
| 2 | Preluare inventar -> incarca un scan unde un 1 este citit ca 4 cu incredere scazuta | Valoarea apare cu cifra corectata si insigna "↻ din invatare 4 -> 1" | |
| 3 | Un 4 clar (incredere mare) pe acelasi scan | Ramane 4, fara insigna | |
| 4 | "Reseteaza invatarea", incarca din nou acelasi scan | Citirea revine la cea initiala, fara insigna | |
