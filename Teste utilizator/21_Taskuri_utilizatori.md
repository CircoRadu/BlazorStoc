# Taskuri utilizatori (Administrare -> Taskuri utilizator, Taskurile mele, fila Taskuri din Notificari)

## Modificari de testat

### 09.11.2026 - Taskuri date de administrator unui utilizator, pe un beneficiar

**Ce s-a adaugat:** modulul Taskuri. Administratorul (sau cine are cheile `taskuri.*`) da taskuri unui utilizator: titlu, descriere, utilizator, stare (In viitor / In lucru / Finalizat), termen limita optional, fisiere. Pagina Administrare -> "Taskuri utilizator" (cautare beneficiar + "+ Adauga beneficiar" prin fereastra existenta), tabele Active / Finalizate cu etichete de filtrare, sectiunea "Taskuri" pe fisa beneficiarului, pagina "Taskurile mele" (meniul numelui din bara de sus), fila "Taskuri" din Notificari, avertizare globala de 3 zile inainte de termen.

**Ce face acum:** un task nou apare la utilizator pe fond rosu ("Nou", insigna rosie pe numele din bara de sus) pana il preia; schimbarea starii sau un fisier adaugat de el il preiau automat. Utilizatorul schimba starea si adauga fisiere, sterge doar fisierele lui (nu pe ale administratorului). Un task Finalizat trece in fila "Finalizate". Tot ce se intampla are actiune proprie in Jurnal. Un beneficiar cu taskuri nu poate fi sters.

Pregatire: un cont de administrator si un cont "Utilizator" (Setari -> Utilizatori).

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Administrare -> "Taskuri utilizator". Scrie o parte din numele unui beneficiar si alege-l; sau apasa "+ Adauga beneficiar", completeaza fereastra si salveaza | Beneficiarul ales (sau cel nou) apare ca "Beneficiar: ..." si sub el formularul de task | |
| 2 | Completeaza titlul, alege utilizatorul, pune un termen peste 2 zile (comutatorul "Are termen"), scrie descrierea, "Adauga taskul" | "Taskul a fost adaugat"; fereastra ramane deschisa; taskul apare in tabelul Active, "Nepreluat" | |
| 3 | In acelasi formular atasezi un fisier (TXT/PDF/imagine) | Fisierul apare in lista, se descarca la apasarea numelui | |
| 4 | In tabel apasa eticheta de stare, apoi eticheta utilizatorului | Deasupra tabelului apar etichetele "stare: ..." si "utilizator: ..." cu "x"; "Sterge filtrele" le scoate | |
| 5 | Cu contul utilizatorului: pe numele din bara de sus vezi insigna rosie; meniul are "Taskurile mele" | Rand pe fond rosu cu insigna "Nou"; termenul apropiat e galben | |
| 6 | Notificari -> fila "Taskuri" | Taskul cu motivele "Nepreluat" si "Termen in 2 zile"; fila e rosie cu numarul | |
| 7 | Apasa iconita de bifa ("Preia taskul") | Fondul rosu si insigna dispar; administratorul vede data preluarii | |
| 8 | Deschide taskul (iconita ochi): adauga un fisier, incearca sa stergi fisierul administratorului | Fisierul tau se adauga si se sterge; al administratorului are stergerea dezactivata | |
| 9 | Schimba starea in "In lucru", apoi in "Finalizat" | Taskul trece in fila "Finalizate" | |
| 10 | Administrator: fila "Finalizate" -> iconita ochi -> schimba starea in "In lucru" | Taskul se redeschide si apare in Active | |
| 11 | Administrator: editeaza (creion) un task activ, muta-l la alt utilizator | Taskul redevine "Nepreluat" la noul utilizator | |
| 12 | Administrator: sterge (cos) un task activ, motiv + cuvantul "sterge" | Taskul si fisierele lui dispar; Jurnal: "Stergere task" cu toate datele | |
| 13 | Jurnal, filtrul "Taskuri utilizatori" | Actiuni distincte: Adaugare task, Adaugare fisier task, Preluare task, Schimbare status task, Mutare task..., Stergere task | |
| 14 | Fisa unui beneficiar cu task activ: sectiunea "Taskuri"; incearca sa-l stergi pe beneficiar | Sectiunea listeaza taskurile cu creion/cos; stergerea beneficiarului e refuzata cu mesaj | |
| 15 | Setari -> Notificari, sectiunea "Avertizarea taskurilor inainte de termen": schimba zilele (1-60) si salveaza | Mesajul de reusita; Jurnal: "Modificare zile avertizare taskuri"; termenele galbene si fila Taskuri folosesc noua valoare | |

## Prioritate, ordonare si editare de catre utilizator (0.5.58)

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Administrare -> Taskuri utilizator -> Taskuri nou: alege Prioritate Ridicat, salveaza | Taskul apare cu eticheta rosiatica; in Jurnal exista "Adaugare task" cu Prioritate | |
| 2 | Editeaza taskul si schimba prioritatea in Mediu | Eticheta galbena; in Jurnal "Modificare prioritate task" | |
| 3 | Taskurile existente dinainte de versiune | Toate au prioritate Scazut (alb) | |
| 4 | Apasa antetul Creat, apoi Termen, apoi Prioritate (de 3 ori) | Crescator, descrescator, apoi ordinea implicita; taskurile fara termen raman la sfarsit | |
| 5 | Conectat ca utilizator: Taskurile mele | Starea e doar afisata; coloana Creat arata data si autorul | |
| 6 | Deschide taskul (ochi) | Fereastra de vizualizare, doar informatii; butonul Editeaza | |
| 7 | Editeaza: schimba starea, adauga un fisier propriu, sterge-l | Se salveaza imediat; fisierele administratorului nu se pot sterge | |
