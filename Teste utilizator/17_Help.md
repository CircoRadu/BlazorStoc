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
