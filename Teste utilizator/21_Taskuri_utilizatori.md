# Taskuri utilizatori (Administrare -> Taskuri utilizator, Taskurile mele, fila Taskuri din Notificari)

## Modificari de testat

### 10.11.2026 - Taburi pe utilizator, ordine proprie, stadiu si jurnal propriu al taskului (Tasks 1.2.1, migrarea 85)

**Ce s-a adaugat:** pagina Taskuri utilizator are tab "Nepreluate" (global) si cate un tab pe fiecare utilizator care are taskuri; in tab, taskurile active sunt numerotate in ordinea stabilita de administrator (maner de tragere), cele finalizate urmeaza dedesubt, cu comutator care le ascunde; taskurile finalizate pot fi editate; camp nou "Stadiu / note de lucru"; sectiunea pliabila "Jurnalul taskului" in fereastra de detalii.
**Ce face acum:** administratorul aranjeaza ordinea in care fiecare utilizator isi trateaza taskurile; utilizatorul vede aceeasi ordine in Taskurile mele; stadiul unui task (de exemplu "trimis la politie la 12.11.2026") se scrie si dupa finalizare, de utilizator sau administrator; fiecare actiune pe task (creat, preluat, finalizat, stadiu, mutat, reordonat) se vede in jurnalul taskului.

| Nr | Pasi | Asteptat | Rezultat |
|---|---|---|---|
| 1 | Administrare -> Taskuri utilizator, cu taskuri date la 2 utilizatori | Taburi: Nepreluate si cate unul pe utilizator; fiecare tab are insigna cu numarul taskurilor active; un utilizator fara taskuri nu are tab | |
| 2 | Tab Nepreluate | Doar taskurile active pe care utilizatorul nu le-a preluat; numele utilizatorului deschide tab-ul lui | |
| 3 | Tab de utilizator: trage manerul unui task peste altul | Taskul se muta, numerele raman 1, 2, 3...; Jurnal: "Reordonare taskuri utilizator" cu pozitia veche si noua | |
| 4 | Focus pe maner, sageata sus / jos | Taskul urca / coboara o pozitie | |
| 5 | Scrie ceva in cautare sau apasa un antet de sortare | Manerele dispar si apare nota "Sterge filtrele..."; la stergerea filtrelor revin | |
| 6 | Marcheaza un task Finalizat (din detalii) | Iese din numerotare, apare sub separatorul Finalizate; ceilalti se renumeroteaza | |
| 7 | Comutatorul "Ascunde taskurile finalizate" | Finalizatele dispar / reapar | |
| 8 | Editeaza un task finalizat (creion) si schimba titlul | Se salveaza; ramane finalizat, cu aceeasi data a finalizarii | |
| 9 | Ca utilizator: deschide un task finalizat -> Editeaza -> scrie in Stadiu "Trimis la politie la 12.11.2026" -> Salveaza stadiul | Stadiul apare in tabelul administratorului sub titlu; Jurnal: "Modificare stadiu task" | |
| 10 | Deschide taskul -> Jurnalul taskului | Lista cu data si cine a creat, preluat, finalizat, scris stadiul, mutat, reordonat; ordine cronologica | |
| 11 | Mutarea unui task la alt utilizator / redeschiderea unuia finalizat | Intra ultimul in lista noului utilizator; lista veche se renumeroteaza | |
| 12 | Taskurile mele (utilizator) | Coloana NR. cu ordinea stabilita de administrator | |
| 13 | Dupa actualizare, taskurile existente | Fiecare utilizator are taskurile active numerotate dupa termen si data crearii | |

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
