# Help contextual (iconita "?" din fiecare fereastra)

Propunere si decizii: `docs/PROPUNERE_HELP.md`. Continutul sta in `Assets/Help/*.help` si se schimba la rebuild. Etapa 1 (infrastructura + topicuri pilot) este gata; restul ferestrelor se scriu in ordinea meniului principal. O fereastra al carei topic nu e scris inca arata "Ajutor indisponibil".

## Modificari de testat

### 24.10.2026 - Ajutor unificat si cautare in orice fereastra

**Ce s-a adaugat:** intrarea „Ajutor" in meniul din stanga si placa „Ajutor" pe pagina principala deschid cuprinsul ajutorului (toate ferestrele pe sectiuni). Campul „Cauta in tot ajutorul" apare acum in orice fereastra de ajutor, nu doar pe pagina principala.

**Ce face acum:** din cuprins deschizi o sectiune si apoi o fereastra; „Inapoi" revine la cuprins; cautarea functioneaza din orice fereastra.

| Pas | Ce faci | Ce trebuie sa vezi | Rezultat |
|---|---|---|---|
| 1 | Apasa „Ajutor" din meniul din stanga | Fereastra de ajutor cu cuprinsul pe 12 sectiuni si campul de cautare | |
| 2 | Deschide „Furnizori si facturi" si alege o fereastra | Descrierea ferestrei; „Inapoi" te readuce la cuprins | |
| 3 | Pe pagina principala apasa placa „Ajutor" | Acelasi cuprins | |
| 4 | Intr-o pagina oarecare (ex. Produse) apasa iconita „?" din bara de sus si scrie „factura" in cautare | Rezultate din tot ajutorul, nu doar pagina curenta | |

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

### 19.10.2026 - Help: capturi de ecran cu chenar pulsatil in "Elementele ferestrei"

**Ce s-a adaugat:** in fiecare topic de help al unei ferestre apare, la "Elementele ferestrei", captura ferestrei (1920x1080, pagina intreaga) si, sub ea, tabelul elementelor (Element / Tip / Descriere). Capturile sunt facute automat de unealta `tools/help-capture.mjs` pe o baza cu date demonstrative fictive (`blazorstoc_demo`, `tests/BlazorStoc.HelpDemo`); 89 de capturi, cate una pe topic, pe toate cele 89 de topicuri.

**Ce face acum:** apasa un rand din tabel: pe captura apare un chenar portocaliu care pulseaza in jurul elementului (numai cat timp randul este ales; al doilea clic pe rand, alt rand sau Escape il scot); captura ramane vizibila sub antet cand derulezi tabelul. Butonul "Mareste imaginea" arata captura la marime reala, cu derulare. Dupa numele fiecarui element este o iconita "Arata pe pagina": inchide temporar ajutorul si incadreaza elementul pe pagina reala 2-3 secunde, apoi redeschide ajutorul; daca elementul nu e vizibil acum, spune asta in rand. Fara insigne si fara numere pe imagine.

**Verificat de agent 19.10.2026** (instanta 5089 pe baza demo, browser CDP): chenarul apare/dispare la clic, la al doilea clic, la alt rand si la Escape; marirea; "Arata pe pagina" (pe pagina si peste un dialog); capturile sunt generate pentru 89 de ferestre (fereastra ANAF cu CUI-ul real al firmei Electric Standard Prest Simeria, ales de utilizator), inclusiv sabloanele de factura (din PDF-ul si XML-ul demo), fereastra separata a facturii si punctul de pe harta; grupul de teste `help` (61 PASS).

Pasi:

1. Deschide Produse si apasa "?": sub "Elementele ferestrei" vezi captura si tabelul; apasa pe rand "Cauta produse" si apoi pe "Adauga produs": chenarul pulseaza in jurul fiecarui element, pe rand.
2. Apasa din nou pe randul ales (chenarul dispare), apoi alege altul si apasa Escape (chenarul dispare, ajutorul ramane deschis; al doilea Escape il inchide).
3. Apasa "Mareste imaginea", alege un rand de jos si verifica ca captura se deruleaza la element.
4. Apasa iconita "Arata pe pagina" de langa un element: ajutorul se ascunde, elementul de pe pagina este incadrat cateva secunde, apoi ajutorul revine.
5. Deschide ajutorul unui formular (de exemplu Beneficiari -> "Adauga beneficiar" -> "?"): captura arata formularul; fiecare topic are captura.
6. Verifica ca imaginile nu contin date reale (firme, persoane fictive).


### 21.10.2026 - Help: fiecare element are chenar pe imaginea din help (nu mai exista "Arata pe pagina")

**Ce s-a adaugat:** butonul "Arata pe pagina" a fost scos; iconita de pe fiecare rand ("Arata elementul pe imagine") incadreaza elementul numai pe captura din help. Toate topicurile au capturi pentru toate elementele (pasii fiecarui asistent, ferestre deschise, stari de eroare, liste cu pagini). Un test (grupul help) esueaza daca un element nu are chenar.

**Ce face acum:** apasarea iconitei de pe un rand deruleaza captura potrivita (unele topicuri au mai multe capturi) si pulseaza chenarul; ajutorul ramane deschis, pagina live nu este atinsa. Exceptie: doua elemente care cer hardware sau conturi pe care mediul demo nu le are ("Copiaza pe NAS", "Rezultatul ultimei operatii" de la conturile bazei de date) nu au iconita.

Pasi:

1. Deschide ajutorul pe Preluare oferta: apasa pe rand elementele fiecarui pas (fisier, beneficiar, proiect, linii, rezumat) si verifica ca captura se schimba si chenarul incadreaza elementul.
2. Deschide ajutorul pe Preluare factura: parcurge elementele pasilor 1-3, "Regularizare", "Se preia din nou", "Preluare noua".
3. Deschide Setari -> Actualizare -> "?": elementele (pachet verificat, nu poate fi instalat, anunt cu numaratoare, Updater-ul lucreaza, banda rosie) au capturi proprii.
4. Verifica pe cateva topicuri ca imaginile nu contin date reale si ca niciun rand cu iconita nu duce spre pagina live.
