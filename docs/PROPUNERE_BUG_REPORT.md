# Propunere: modul Raportare eroare (bug report), cu intrare in meniul principal

Data: 7 noiembrie 2026 (revizuit dupa raspunsul utilizatorului: intrare in meniu deasupra de Ajutor, fara limita anti-abuz; rapoartele raman in aplicatie; intrare vizibila tuturor; captura automata + Ctrl+V). Stare: DOAR PROPUNERE, nimic implementat. Fisier scris fara diacritice (regula .md).

## 1. Cerinta

Modul de raportare a erorilor cu intrare in meniul lateral (`MainLayout.razor`, sidebar), plasata imediat deasupra intrarii "Ajutor". Rapoartele se pastreaza si se gestioneaza in aplicatie; singura iesire este exportul ZIP descarcat manual de administrator. Elementul de meniu este vizibil TUTURORILOR utilizatorilor autentificati.

## 2. Punct de intrare

- Element "Raporteaza o eroare" (iconita bug) in meniul lateral, imediat deasupra lui `nav-help` ("Ajutor"), cu aceeasi forma ca celelalte `nav-item` (iconita, text, sageata).
- Click -> popup (dialog nativ, ca `logout-dialog`) cu formularul, fara navigare: utilizatorul ramane pe pagina cu problema. Elementul nu are ruta proprie, deci nu apare in lista de pagini.
- Elementul nu este un modul cu `NavItems()` (acelea se aseaza inaintea Notificarilor/Jurnalului); se adauga explicit in `MainLayout.razor`, in fata `<a id="nav-help">`.
- Optional: buton "Raporteaza o problema" in pagina `/Error`.
- Fara permisiune de acces: elementul si formularul sunt disponibile oricarui utilizator autentificat (nu exista `AuthorizeView` pe policy). Doar tabul de administrare din Setari are permisiune (sectiunea 5).

## 3. Formularul (popup)

Campuri completate automat (afisate in sectiune colapsabila, fara editare, pentru transparenta):
- ruta curenta (`/produse/12`), titlul ferestrei, utilizator, tip utilizator, versiunea aplicatiei, data/ora, tema, rezolutia ferestrei, browser (user agent);
- ultimele erori din log legate de sesiunea curenta (circuit/exceptii), daca exista; cod de corelare.

Campuri completate de utilizator:
- Titlu scurt (obligatoriu);
- Ce ai facut / ce ai asteptat / ce s-a intamplat (text, obligatoriu) - cu `MarkedTextField` daca e cazul;
- Gravitate: Blocant / Important / Minor / Sugestie (selector);
- Captura automata a paginii de sub popup: la click pe elementul din meniu, INAINTE de deschiderea dialogului, un script (`wwwroot/bug-report.js`) fotografiaza pagina curenta (zona `main-shell`, fara dialog) si o ataseaza ca prima imagine. Se face din DOM cu o biblioteca locala (de tip html2canvas, inclusa in `wwwroot`, fara CDN), deci fara cerere de permisiune de la browser. Utilizatorul vede miniatura in popup si o poate scoate (iconita stergere) sau inlocui.
- Imagini adaugate de utilizator: lipire cu Ctrl+V oriunde in popup (handler `paste` pe dialog, imaginea din clipboard devine miniatura noua), plus alegere fisier ca alternativa. Cate imagini se doresc, cu limita de dimensiune pe fisier; fiecare are iconita de stergere.
- Toggle On/Off (nu checkbox): "Atasez detaliile tehnice" implicit On.

Confirmare dupa trimitere: numar raport (ex. BUG-0042), cu mesaj clar. Anulare cu confirmare daca s-a scris text (`LeaveConfirmation`).

## 4. Stocare

Tabela noua `bug_reports` (migrare prin mecanismul existent, cont de migrare):
- id, numar, titlu, descriere, gravitate, stare (Nou, In analiza, Rezolvat, Respins), ruta, versiune, utilizator_id, creat_la, rezolvat_la, nota_admin, date_tehnice (JSON);
- `bug_report_files` pentru captura automata si imaginile lipite (reutilizeaza `EntityFiles`); includ in backup, impreuna cu restul activelor (vezi regula backup cu assets).

Se scrie in jurnalul de audit cu actiuni specifice: "Raportare eroare creata", "Raportare eroare: stare schimbata din X in Y", "Raportare eroare stearsa".

## 5. Gestionare (administrator)

Rapoartele se administreaza in aplicatie, in `/setari`, ca tab nou "Rapoarte erori" (permisiune `setari-bug-raport.view`, vizibil doar administratorilor):
- tabel paginat cu selector de randuri/pagina (`AuditPager`), filtre dupa stare, gravitate, utilizator, perioada;
- deschidere raport: toate datele, imaginile, schimbare stare, nota interna;
- iconite SAU butoane pe randuri (nu amestecate), conform regulii existente.

Export ZIP descarcabil (in tabul de administrare, permisiune `setari-bug-raport.export`): butoane "Exporta raportul" (pe randul deschis) si "Exporta selectia / filtrul curent" (`act-export`). Descarcarea se face din browser, cu jurnalizare "Raportare eroare exportata" (cine, ce rapoarte). Un ZIP contine, pentru fiecare raport, un folder `BUG-0042/` cu `raport.json` (toate campurile si datele tehnice), `raport.txt` (varianta lizibila), captura automata si imaginile lipite; la export multiplu, un `index.csv` cu lista rapoartelor. Fara parole sau chei.

Orice utilizator isi poate vedea propriile rapoarte si starea lor din acelasi popup (tab "Rapoartele mele"), fara pagina separata.

## 6. Notificari

- La raport nou cu gravitate Blocant: notificare in modulul Notificari catre administratori (se poate dezactiva din Setari -> Notificari).
- La schimbarea starii: notificare catre autorul raportului.
- Rapoartele raman in aplicatie: fara email, webhook sau trimitere automata. Singura iesire este exportul ZIP descarcat manual (sectiunea 5).

## 7. Confidentialitate si siguranta

- Nu se colecteaza parole, chei, cookie-uri sau continutul formularelor; doar metadatele din sectiunea 3.
- Captura automata si imaginile pot contine date sensibile: avertisment in popup, acces doar autorului si administratorilor; utilizatorul poate scoate captura inainte de trimitere.
- Fara limita de numar de rapoarte; raman doar sanitizarea textului si dimensiunea maxima a imaginilor.
- Modulul se inregistreaza in `ModuleRegistry` ca modul optional (se poate opri din Setari -> Module); cand e oprit, elementul din meniu dispare.

## 8. Etape propuse

1. Model + migrare + repository + audit.
2. Element in meniu (deasupra de Ajutor) + popup (formular, date automate, captura automata, Ctrl+V).
3. Tab administrare in Setari + permisiuni noi + export ZIP.
4. Notificari + "Rapoartele mele".
5. Help (la cerere), teste de utilizator in "Teste utilizator/", teste tintite.

## 9. Decizii luate (7 noiembrie 2026)

- Rapoartele raman in aplicatie, cu export ZIP descarcabil manual de administrator (fara trimitere externa).
- Elementul din meniu este vizibil tuturor utilizatorilor.
- Captura automata a paginii de sub popup, facuta din DOM + imagini adaugate cu Ctrl+V.
- Export ZIP descarcabil.

## 10. Intrebari deschise

Niciuna. Captura se face din DOM (decizie confirmata); la canvas/iframe (ex. harta Google) poate iesi incompleta, caz in care utilizatorul o inlocuieste cu Ctrl+V.
