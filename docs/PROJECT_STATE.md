# Starea curentă a proiectului

Actualizat de: **Claude**
Data: **24 septembrie 2026**
Stare ciclu: **Task „Cod produs” finalizat; pregătit pentru predarea următorului ciclu către Codex după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari și utilizatori, autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse, componenta comună `CollapsibleSection` și identificarea produselor prin „Cod produs”.

Proiectul folosește un repository Git local și cicluri strict secvențiale Codex–Claude. Următorul ciclu îi este predat lui Codex.

## Ultimele modificări funcționale (ciclul Claude — Cod produs)

- Câmpul „Denumire” al produsului a devenit „Cod produs” în formularul de creare/editare, catalog (coloana „COD PRODUS”, căutarea „după cod produs sau descriere”), panoul de detalii, validări, mesajele de duplicat, audit și arhivă. Nu există un câmp separat și nici generare automată de cod; formularul explică faptul că se introduce codul producătorului.
- Obligativitatea, limita de 100 de caractere, curățarea spațiilor/diacriticelor la salvare, căutarea și ordonarea au rămas neschimbate.
- Unicitatea folosește `TextNormalization.UniquenessKey` (fără diferențe de spații, majuscule sau diacritice) în toate modurile. Mesajul la duplicat: „Codul produsului «…» există deja în catalog (categoria «…», subcategoria «…»). Introdu alt cod.”
- Concurență: SQLite verifică în tranzacție `Serializable`, iar indexul `UNIQUE` pe `products.normalized_name` este garda finală; o încălcare a lui este tradusă în mesajul `ProductCode.ConcurrentDuplicateMessage`. MariaDB verifică sub `Serializable` cu `FOR UPDATE` pe toate produsele. Modul demo folosește lock-ul existent.
- Identificatorul intern `#<id>` nu mai este afișat pentru produse: catalog, panou de detalii („COD PRODUS”), titlul editorului („Editează produsul <cod>”), ținta de audit și de arhivă. Evenimentele vechi din jurnal cu ținta `#<id> · <cod>` sunt afișate fără prefix prin `AuditNavigation.DisplayTarget`; datele salvate nu sunt modificate. Linkurile din jurnal folosesc în continuare `EntityId`.
- Codul comun pentru etichetă, mesaje, ținta de audit, identificare și modificări este centralizat în `ProductCode` (`Services/ProductInput.cs`), eliminând trei copii ale `ProductAuditChanges`.

## Decizii și limitări

- Proprietatea C# `Product.Name`/`ProductInput.Name` și coloanele `products.name`/`produs_denumire` au fost păstrate intenționat: sunt serializate în instantaneele JSON din `archive_operations` și în `log`-ul MariaDB; redenumirea ar fi fragmentat formatul arhivei. Semantica lor este „Cod produs” (comentariu în `ProductInput`).
- Detaliile text ale evenimentelor vechi din jurnal pot conține încă „Denumire: …”; jurnalul este append-only și nu a fost rescris.
- Beneficiarii și utilizatorii afișează în continuare `#<id>`; nu fac parte din acest task.
- Unicitatea MariaDB nu a fost testată pe un server MariaDB real în acest ciclu.

## Validare

- Build Release: 0 avertismente, 0 erori.
- `BlazorStoc.Checks`: 176 verificări trecute (166 anterioare + 10 noi: duplicate cu variante de spații/majuscule/diacritice, cod gol, limită 100, mesajul de duplicat, ținta de arhivă și de audit fără `#<id>`, afișarea evenimentelor vechi, două sesiuni SQLite concurente cu același cod — una singură reușește).
- Browser, `http://127.0.0.1:5082` (admin demo): catalog fără `#<id>`, coloana și căutarea „Cod produs”, panoul de detalii, titlul și eticheta editorului, fără buton de generare; cod duplicat introdus cu alte majuscule și spații respins cu mesaj clar și formular păstrat; toate cele 23 de evenimente de produs din jurnal fără `#<id>`, cu linkuri `/produse?edit=<id>` funcționale.
- Preview-ul rulează din `bin\Release\net9.0\BlazorStoc.exe --urls http://127.0.0.1:5082`. Notă: preview-ul anterior fusese pornit sub contul sandbox Codex și a trebuit oprit manual de utilizator; repository-ul aparține aceluiași cont, deci Git cere `safe.directory` pentru contul local (folosit doar per proces, fără modificarea configurației globale).

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse și beneficiari, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Codex**. Dacă utilizatorul nu stabilește altă prioritate, următorul element activ este Task 2 — „Proiecte asociate beneficiarilor” din `TODO.md` (se recomandă începerea cu Subtask 2.1 și 2.2).

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice, inclusiv secțiunea „Cod produs”.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
