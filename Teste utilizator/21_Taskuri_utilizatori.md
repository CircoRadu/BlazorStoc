# Taskuri utilizatori (Administrare -> Adauga task utilizator, Taskurile mele, fila Taskuri din Notificari)

## Modificari de testat

### 09.11.2026 - Taskuri date de administrator unui utilizator, pe un beneficiar

**Ce s-a adaugat:** modulul Taskuri. Administratorul (sau cine are cheile `taskuri.*`) da taskuri unui utilizator: titlu, descriere, utilizator, stare (In viitor / In lucru / Finalizat), termen limita optional, fisiere. Pagina Administrare -> "Adauga task utilizator" (cautare beneficiar + "+ Adauga beneficiar" prin fereastra existenta), tabele Active / Finalizate cu etichete de filtrare, sectiunea "Taskuri" pe fisa beneficiarului, pagina "Taskurile mele" (meniul numelui din bara de sus), fila "Taskuri" din Notificari, avertizare globala de 3 zile inainte de termen.

**Ce face acum:** un task nou apare la utilizator pe fond rosu ("Nou", insigna rosie pe numele din bara de sus) pana il preia; schimbarea starii sau un fisier adaugat de el il preiau automat. Utilizatorul schimba starea si adauga fisiere, sterge doar fisierele lui (nu pe ale administratorului). Un task Finalizat trece in fila "Finalizate". Tot ce se intampla are actiune proprie in Jurnal. Un beneficiar cu taskuri nu poate fi sters.

Pregatire: un cont de administrator si un cont "Utilizator" (Setari -> Utilizatori).

| Nr | Pas | Rezultat asteptat | Rezultat obtinut |
|---|---|---|---|
| 1 | Administrare -> "Adauga task utilizator". Scrie o parte din numele unui beneficiar si alege-l; sau apasa "+ Adauga beneficiar", completeaza fereastra si salveaza | Beneficiarul ales (sau cel nou) apare ca "Beneficiar: ..." si sub el formularul de task | |
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
