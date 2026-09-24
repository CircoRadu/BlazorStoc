# Starea curentă a proiectului

Actualizat de: **Codex**
Data: **24 septembrie 2026**
Stare ciclu: **pregătit pentru predare către Claude după commitul curent**

## Rezumat

BlazorStoc este o aplicație Blazor Web App .NET 9, cu mod local persistent SQLite și suport MariaDB. Rulează direct cu .NET, fără Docker. Integrarea și publicarea NAS/QNAP sunt în afara fazei curente.

Sunt implementate CRUD-urile pentru produse, beneficiari și utilizatori, autentificarea pe roluri, administrarea categoriilor/subcategoriilor, imaginile produselor pe server, auditul persistent, arhivarea obiectelor șterse și preview-ul local.

Proiectul folosește acum un repository Git local și un protocol strict secvențial Codex–Claude. Fiecare agent începe numai dintr-un handoff curat, modifică, verifică, actualizează starea și jurnalul, creează propriul commit, predă celuilalt agent și se oprește.

## Ultimele modificări funcționale

- Categoriile din pagina de administrare pornesc restrânse și afișează/ascund subcategoriile prin apăsarea antetului.
- Secțiunea „Administrare” din meniul lateral folosește collapse nativ accesibil și păstrează stilul meniului.
- Numărul subcategoriilor este afișat textual în același stil ca numărul produselor asociate.
- Denumirile și codurile obiectelor sunt curățate la creare și editare: spațiile exterioare sunt eliminate, iar secvențele de spații sunt reduse la unul singur.
- Categoriile și subcategoriile pot fi create numai în pagina lor de administrare; formularul produsului selectează doar valori existente.
- `TODO.md` are ca priorități active Task 0 pentru collapse unitar, Task 1 pentru „Cod produs” și Task 2 pentru proiecte asociate beneficiarilor.

## Validare cunoscută

- Ultima suită completă `BlazorStoc.Checks` a trecut integral după introducerea normalizării spațiilor.
- Ultimul build Release a reușit cu 0 avertismente și 0 erori.
- Preview-ul se rulează la `http://127.0.0.1:5082/` și trebuie repornit după schimbările de cod.

## Reguli active ale proiectului

- Operațiile rămân asincrone.
- Nu se introduce Docker în această etapă.
- Nu se implementează integrare NAS/QNAP în această etapă.
- Nu se generează fișiere SQL de upgrade separate.
- După fiecare modificare funcțională se actualizează preview-ul local.
- Administratorul are acces complet; utilizatorul limitat operează produse și beneficiari, fără meniul Utilizatori.
- Modificările persistente trebuie jurnalizate și entitățile șterse trebuie arhivate conform contractului existent.

## Următorul pas

Următorul agent este **Claude**. Va prelua numai după commitul Codex și după verificarea unui working tree curat. Taskul concret este stabilit de următorul prompt al utilizatorului; dacă nu este specificat, prioritatea curentă este Task 0 din `TODO.md`.

## Fișiere de orientare

- `TODO.md` — backlog și criterii de acceptare.
- `README.md` — configurare și comportament general.
- `VALIDARE.md` — verificări istorice.
- `ARCHIVE_RECOVERY.md` — contractul de arhivare și recuperare.
- `docs/SEQUENTIAL_COLLABORATION.md` — protocolul Codex–Claude.
- `docs/AGENT_CHANGELOG.md` — istoricul handoff-urilor.
- `.collaboration/state.json` — agentul activ și agentul care poate prelua următorul ciclu.
- `tools/agent-cycle.ps1` — verifică și execută tranzițiile `start`, `finish` și `status`.
