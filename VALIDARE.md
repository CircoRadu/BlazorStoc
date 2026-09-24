# Verificări — versiunea 0.2

## Imagini produse — 17 septembrie 2026

- Formularul produsului acceptă o imagine din fișier sau clipboard și refuză salvarea unui produs nou fără imagine.
- Sunt acceptate JPG, PNG, WebP și GIF de maximum 5 MB; formatul este identificat din conținut, iar SVG este refuzat.
- Catalogul afișează un thumbnail clicabil, panoul produsului include imaginea, iar vizualizarea mărită se deschide într-o fereastră dedicată.
- În modul MariaDB, imaginile noi sunt stocate în `data/product-images`, în afara bazei și a directorului public, și sunt livrate printr-o rută care cere autentificare.
- 71 de verificări automate au trecut. Build Release reușit fără avertismente și fără erori.
- Verificarea în browser a confirmat obligativitatea imaginii, încărcarea unui PNG real, salvarea produsului, thumbnail-ul, imaginea din detalii și vizualizarea mărită.

## Secțiunea Beneficiari — 17 septembrie 2026

- A fost adăugat CRUD-ul asincron pentru beneficiari, cu nume, CUI unic, căutare și sincronizare automată la 15 secunde.
- Ambele roluri pot vedea secțiunea și pot adăuga, edita sau șterge beneficiari; meniurile Utilizatori și Jurnal rămân ascunse contului limitat.
- Modificările beneficiarilor sunt jurnalizate cu operator, rol, operație, țintă și timestamp.
- 67 de verificări automate au trecut, inclusiv normalizarea și unicitatea CUI-ului, validarea, concurența la editare, ștergerea și auditul.
- Verificarea în browser a confirmat că `utilizator.demo` vede meniul Beneficiari și poate adăuga un beneficiar; totalul și indicatorii s-au actualizat imediat.
- Build Release reușit fără avertismente și fără erori. Nu a fost generat niciun fișier SQL de upgrade.

## Actualizare utilizatori 0.3 — 16 septembrie 2026

- Au fost adăugate CRUD-ul utilizatorilor, autentificarea din `web_user`, rolurile Administrator/Utilizator și autorizarea scrierilor de produse.
- 58 de verificări automate au trecut: catalogul și CRUD-ul, autentificarea ambelor roluri demonstrative, refuzul parolei greșite, autentificarea unui cont nou, starea distinctă pentru contul inactiv, protecțiile administratorului, dreptul utilizatorului standard de a opera produse și jurnalizarea fără parole.
- Verificarea vizuală în browser a trecut în modul demonstrativ: validarea formularului gol, adăugarea unui utilizator limitat, actualizarea numelui și promovarea la Administrator, actualizarea indicatorilor și afișarea dialogului de ștergere.
- Build Release final reușit fără avertismente și fără erori; toate cele 58 de verificări automate au fost rerulate cu succes.
- Autentificarea este obligatorie și în preview. Verificarea în browser a confirmat că rolul Utilizator vede și poate opera Produse fără meniul Utilizatori, iar Administratorul vede ambele secțiuni și poate deschide administrarea conturilor.
- Verificarea în browser a confirmat mesajul explicit pentru cont inactiv și dashboard-ul administrativ cu timestamp UTC, actor, rol, operație, țintă și detalii. Dezactivarea și reactivarea contului demonstrativ au produs două evenimente, fără parolă în jurnal.
- Butonul manual „Actualizează” a fost eliminat de pe pagina Utilizatori. Lista se sincronizează automat la 15 secunde; verificarea în browser a confirmat că formularul de adăugare rămâne deschis și intact peste un ciclu de sincronizare.
- Nu se livrează fișier SQL pentru 0.3 și nu s-a modificat sau accesat nicio bază de date. Integrarea schemei utilizatorilor este amânată pentru o fază separată.
- Proiectul rulează direct cu .NET; fișierele și pașii Docker au fost eliminați din faza curentă.

## Actualizare asincronă — 16 septembrie 2026

- Toate operațiile MariaDB folosesc API-urile asincrone pentru deschidere, citire, scriere, commit, rollback și eliberarea resurselor.
- Adăugarea și editarea întorc acum task-uri asincrone inclusiv când validarea eșuează; apelantul primește uniform erorile prin `await`.
- Produsele și grupurile categorie/subcategorie se încarcă în paralel; evenimentele Blazor care pornesc încărcări sau scrieri returnează `Task` și propagă anularea componentei.
- Gazda SQL implicită este `127.0.0.1`. Integrarea și publicarea pe NAS/QNAP sunt în afara fazei curente și nu au fost executate.

## Verificări anterioare — 15 septembrie 2026

- Build și publish Release reușite cu SDK .NET 9.0.317. O încercare intermediară de rebuild a fost blocată de instanța demo activă; după oprirea ei, verificările și publicarea au reușit.
- 35 verificări automate trecute: 10 pentru catalog/căutare/anulare și 25 pentru CRUD, validare, izolare demo, versiuni concurente, ștergere, identitate obligatorie și blocarea scrierilor spre baza veche înainte de conectare.
- 8 verificări HTTP auth/CSRF/logout trecute pe instanța locală `127.0.0.1:5083`, cu destinația SQL fictivă `127.0.0.1:1`. Instanța de test a fost oprită după verificare.
- Browser desktop, `127.0.0.1:5082`, mod demo: formular gol respins cu mesaje în română; adăugare cu diacritice și categorie/subcategorie noi; produs afișat în catalog și detalii; editare; schimbarea cantității respinsă fără motiv și acceptată cu motiv; ștergere blocată cu stoc nenul; ștergere reușită după corecție la zero; numărul de produse și indicatorii actualizați. Aspectul catalogului a fost verificat vizual.
- Codul MariaDB folosește parametri SQL, tranzacții SERIALIZABLE, blocare de rânduri, versiuni de produs și jurnal în aceeași tranzacție. Acestea au fost revizuite în surse, **nu validate prin integrare cu un server SQL**.
- Scriptul `database/BlazorStoc_upgrade_0.2.sql`, conversia câmpurilor la utf8mb4, granturile contului de scriere, rollback-ul la eroare de jurnal, concurența dintre conexiuni și protecția relațiilor trebuie testate pe o bază MariaDB locală de test înainte de folosire reală. Nu s-a importat backupul.
- Scriptul inițial de creare și arhiva 0.1 rămân istorice; pentru 0.2 se aplică atât scriptul de creare (numai dacă baza nu există), cât și cel de actualizare. Nicio migrare automată la pornire.

## Verificări istorice — versiunea 0.1

- Build și publish Release: reușite pe Windows cu SDK .NET 9.0.317.
- 10 verificări automate pentru căutare, câmpuri, filtre combinate, stoc negativ/zero, catalog gol și anulare: reușite.
- 8 verificări HTTP pe o instanță locală izolată: redirecționare la login, CSS accesibil, antiforgery obligatoriu, parolă incorectă, login corect, pagină autentificată, logout și acces refuzat după logout.
- Browser desktop: catalog, căutare, panou de detalii, pagina a doua, filtrul de stoc negativ și revenirea la pagina întâi verificate.
- Mod MariaDB cu adresă locală indisponibilă: eroare distinctă, fără înlocuire cu date demonstrative. Nicio conexiune către NAS în teste.
- Compatibilitatea interogării cu schema furnizată a fost verificată prin citirea definițiilor. Nu s-a importat backupul și nu s-a executat un test de integrare pe MariaDB 5.5.68.
- Adaptările și testarea pentru mobil sunt în afara scopului, conform cerinței utilizatorului.

Pentru repetarea testelor HTTP, pornește separat o instanță locală de test, în modul MariaDB, cu DB host 127.0.0.1, port 1, Database__Password=local-test-only și Authentication__Password=local-test-password-123. Nu folosi aceste valori pentru instalare. Apoi rulează:

```sh
dotnet run --project tests/BlazorStoc.Checks -c Release -- --http http://127.0.0.1:5081
```

Portul trebuie să coincidă cu portul instanței de test. Verificările HTTP refuză destinații care nu sunt localhost.
