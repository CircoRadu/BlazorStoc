# Help contextual (iconita "?" din fiecare fereastra)

Propunere si decizii: `docs/PROPUNERE_HELP.md`. Continutul sta in `Assets/Help/*.help` si se schimba la rebuild. Etapa 1 (infrastructura + topicuri pilot) este gata; restul ferestrelor se scriu in ordinea meniului principal. O fereastra al carei topic nu e scris inca arata "Ajutor indisponibil".

## Modificari de testat

### 19.10.2026 - Infrastructura help si primele topicuri

**Ce s-a adaugat:** iconita "?" in bara de sus a aplicatiei (langa "Deconectare"), in ferestrele separate (harta mentenantei, categorii), pe paginile Autentificare si Acces refuzat, si in antetul dialogului "Istoricul modificarilor". Fereastra de ajutor (popup) cu: fluxuri de lucru colapsabile marcate cu "+", elementele ferestrei descrise (cu iconita reala pentru actiunile de pe randuri), cautare in tot ajutorul numai din pagina principala. Topicuri scrise: Pagina principala, Produse si stocuri (Catalog), De regularizat, Istoricul modificarilor unei operatii, Autentificare, Acces refuzat.

**Ce face acum:** iconita deschide descrierea ferestrei in care esti; pe o pagina cu taburi (Produse: Catalog / De regularizat) se deschide descrierea tabului activ si ofera link catre celelalte sectiuni; din dialoguri, iconita dialogului deschide descrierea lui peste el, fara sa-l inchida. Doar pe pagina principala apare campul "Cauta in tot ajutorul". Textele sunt scrise in romana, cu diacritice.

**Verificat de agent 19.10.2026** (instanta de test pe baza de test, port 5089): iconita in bara de sus, deschiderea pe Pagina principala (cu cautare, rezultate, "Inapoi"), pe /produse cu tab selectat, pe Acces refuzat, in fereastra separata a hartei si peste un dialog (marcajul real al antetului de dialog, simulat deasupra paginii: ajutorul se deschide peste el, dialogul ramane); endpoint-urile /ajutor/* (public doar pentru topicurile Autentificare si Acces refuzat); testele grupului `help`.

Pasi:

1. Deschide Pagina principala si apasa iconita "?" din bara de sus. Verifica: se deschide un popup cu titlul "Ajutor · Pagina principala"; exista campul "Cauta in tot ajutorul"; sectiunile "Cum ajung la o sectiune" si "Cum caut in ajutor" sunt inchise (cu "+") si se deschid la clic (devin "-").
2. In campul de cautare scrie "regularizare": apare un rezultat; apasa-l, apoi "Inapoi" te intoarce la pagina principala.
3. Inchide cu "Inchide" si cu tasta Escape (ambele inchid popupul, pagina de dedesubt ramane neschimbata).
4. Deschide Produse si stocuri si apasa "?": se descrie catalogul; fiecare element vizibil pe pagina (butoane din antet, cele 4 cifre, filtre, coloane, iconitele de pe rand, paginarea) are o descriere. Verifica ca iconitele "Iesire rapida" si "Miscarile produsului" din ajutor arata ca cele din tabel.
5. Treci pe tabul "De regularizat" si apasa "?": se deschide "Produse si stocuri · De regularizat"; linkul "Produse si stocuri" din "Alte sectiuni ale ferestrei" duce la descrierea catalogului.
6. Deschide un produs, apoi istoricul unei operatii modificate (iconita "Istoric" de pe rand): in antetul dialogului apare "?"; apasa-l: ajutorul se deschide peste dialog, iar dialogul ramane deschis dupa ce inchizi ajutorul.
7. Verifica pe o pagina al carei ajutor nu e scris inca (de exemplu Furnizori): iconita deschide un popup "Ajutor indisponibil", fara eroare.
8. Iesi din cont: pe pagina de autentificare apare "?" in coltul cardului si descrierea se deschide fara sa fii autentificat; pe alta pagina (ex. /ajutor/fereastra?ruta=/produse) fara cont primesti eroare 401.
9. Ecran ingust (telefon): popup-ul incape pe latime, elementele se aseaza pe un rand, nimic nu iese din ecran (verificare doar a ta, agentul nu poate verifica ecranul real al telefonului).

### 19.10.2026 - Topicuri: Preluare factura, Iesire multipla, Export consum, Inventar

**Ce s-a adaugat:** topicuri de ajutor pentru Preluare factura (cu fereastra separata a facturii), Iesire multipla, Export consum, Inventar (cu previzualizarea formularului) si Preluare inventar (cu confirmarea modificarilor); iconita "?" si in antetul previzualizarii de inventar si al confirmarii din Preluare inventar.

**Ce face acum:** pe fiecare din aceste pagini iconita deschide descrierea fiecarui element vizibil si fluxurile de lucru ("Cum preiau o factura", "Cum inregistrez o iesire catre un beneficiar", "Cum generez situatia de inventar" etc.). Din Inventar, linkul din flux deschide descrierea previzualizarii.

**Verificat de agent 19.10.2026** (preview 5089 pe baza de dezvoltare): se deschide ajutorul corect pe /iesiri/multipla, /consum, /produse/preluare-factura, /inventar (inclusiv linkul catre previzualizare) si /inventar/preluare; testele grupului `help` si `components`.

Pasi:

1. Deschide fiecare pagina de mai sus si apasa "?": compara descrierea cu ce vezi pe ecran; noteaza elementele lipsa sau descrise gresit.
2. La Preluare factura parcurge o factura reala urmand fluxul "Cum preiau o factura": pasii din ajutor trebuie sa corespunda cu ce faci.
3. La Iesire multipla verifica fluxurile "Cum inregistrez o iesire catre un beneficiar" si "Cum mut produse intr-o masina".
4. La Inventar genereaza o situatie si verifica ca "?" din fereastra de previzualizare deschide descrierea ei peste previzualizare.
5. La Preluare inventar (dupa incarcarea unui formular) deschide "Trimite modificari in stoc" si verifica "?" din fereastra de confirmare.

### 19.10.2026 - Topicuri: Administrare (Categorii, Beneficiari, Furnizori, Facturi, Vehicule, Utilizatori, Nomenclator)

**Ce s-a adaugat:** topicuri pentru toate paginile din meniul Administrare si pentru formularele si ferestrele lor: Categorii (+ Parametri obligatori ai subcategoriei), Beneficiari (lista, fisa, formularul de beneficiar, formularul punctului de lucru, formularul de proiect, "Date diferite in ANAF"), Furnizori (lista, fisa, formularul), Facturi (cu cele doua sectiuni, editarea facturii, factura deschisa, adaugarea manuala a unui produs, legarea intrarilor), Vehicule (lista, fisa, echipamente, formularul, modificarea unei date de expirare, mutarea/restituirea), Utilizatori (lista, fisa, formularul), Nomenclator. Iconita "?" in antetul fiecarui formular/dialog dintre acestea.

**Ce face acum:** pe fiecare pagina si in fiecare formular/dialog de mai sus, "?" deschide descrierea elementelor si fluxurile ("Cum adaug un beneficiar", "Cum leg intrarile fara factura de factura primita", "Cum restitui un produs din masina" etc.). Pe /facturi, sectiunea "Intrari fara factura" are propriul ajutor, distinct de cel al listei de facturi. Fereastra separata a categoriilor are acelasi ajutor ca pagina Categorii.

**Verificat de agent 19.10.2026:** testele grupurilor `help` si `components` (inclusiv fixul unui test care cauta primul buton din antetul unui editor); suita completa in memoria (1597 PASS); browser pe preview 5089: ajutorul se deschide pe /facturi (ambele sectiuni), /beneficiari (pagina si formularul), /categorii, iar toate rutele noi se rezolva la topicul corect.

Pasi:

1. Deschide fiecare pagina din meniul Administrare si apasa "?": compara descrierea cu ecranul; noteaza elementele lipsa sau gresite.
2. Pe /facturi apasa "?" pe sectiunea "Facturi", apoi treci pe "Intrari fara factura" si apasa din nou "?": se schimba descrierea.
3. Deschide un formular (beneficiar, furnizor, vehicul, utilizator, proiect, punct de lucru): in antet, langa "Inchide", apare "?"; verifica ca descrierea corespunde campurilor.
4. La Categorii deschide "Parametri obligatori" pentru o subcategorie si apasa "?" din antetul panoului.
5. Pe fisa unui vehicul, la "Modifica data" de pe un rand de expirare, apasa "?" in fereastra deschisa; la Echipamente deschide "Restituie in depozit" si apasa "?".
6. Pe /categorii/fereastra (fereastra separata) apasa "?" din coltul din dreapta sus.

### 19.10.2026 - Topicuri: Oferte, Mentenanta (cu harta), Analize de risc, Produs, Proiecte, Notificari, Jurnal, Setari - help complet pe toate ferestrele

**Ce s-a adaugat:** topicuri pentru toate ferestrele ramase: Oferte (preluare, sabloane, editor de sablon), formularul de produs, pagina produsului si editarea unei miscari, Mentenanta (scadente, registru, contracte, interventii, formularele lor, activarea contractului, harta si detaliile unui punct), Analize de risc (pagina, sectiunea, formular, reinnoire), Proiecte (pagina, situatie, echipamente, observatii si formularul), Notificari (active, rezolvate, detalii), Jurnal (recent si complet), Setari (pagina, ANAF, Notificari: sabloane si setari, Harta: motor si overlay, Facturi: generare, sabloane salvate, editare, XML, camp, coloana, salvare ca nou, Backup: backup si restaurare, NAS, verificare ora, Module, Tipuri de utilizatori si formularul lor). Iconita "?" si in antetul formularelor/dialogurilor corespunzatoare, inclusiv fereastra de creare a sablonului din Preluare factura. Toate ferestrele aplicatiei au acum ajutor (in afara confirmarilor simple).

**Ce face acum:** pe orice pagina "?" deschide ajutorul ei; pe Setari ajutorul urmeaza sectiunea si subsectiunea alese (de exemplu Backup -> Backup NAS), pe Mentenanta/Notificari/Facturi urmeaza sectiunea (Scadente/Registru, Active/Rezolvate, Facturi/Intrari fara factura). Testul `help` verifica acum si ca fiecare pictograma numita intr-un topic exista in aplicatie si ca fiecare fisier cu fereastra peste pagina are buton de ajutor (sau e o confirmare/un invelis al unui formular care are).

**Corectat pe drum:** eticheta "Registru interventii" din pagina Mentenanta arata text brut ("@if (registerLoaded) { 0 }") fiindca textul era lipit de `@if`; acum arata numarul de interventii.

**Verificat de agent 19.10.2026** (preview 5089, baza de dezvoltare): ajutorul se deschide cu titlul corect pe fiecare sectiune si subsectiune de Setari, pe ambele sectiuni de Notificari, pe Analize de risc; suita completa (1600 PASS). Pagina Mentenanta dupa corectia etichetei se verifica la republicare.

Pasi:

1. Parcurge Setari: pentru fiecare sectiune si subsectiune apasa "?" si compara descrierea cu ecranul.
2. Pe Mentenanta verifica ca eticheta "Registru interventii" arata numarul (nu text brut) si ca "?" schimba descrierea intre Scadente si Registru.
3. Deschide fisa unui beneficiar cu contract si interventii: formularele "Adauga contract", "Inregistreaza interventie" si "Adauga analiza de risc" au "?" in antet.
4. Pe Harta mentenantei apasa un punct: panoul de detalii are "?"; apasa si "?" din fereastra separata a hartei.
5. Citeste cateva topicuri cap-coada (de exemplu Preluare oferta, Situatia proiectului, Backup si restaurare) si noteaza ce lipseste sau e descris gresit; actualizarea se face la rebuild.
